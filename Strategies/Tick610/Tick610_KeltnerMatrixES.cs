#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// El namespace debe permanecer intacto según las reglas de NinjaTrader 8
namespace NinjaTrader.NinjaScript.Strategies
{
    public class Tick610_KeltnerMatrixES : Strategy
    {
        // === MÁQUINA DE ESTADOS DE LA POSICIÓN ===
        // Nos permite recordar qué tipo de trade abrimos para saber qué bandas perseguir en cada barra
        private enum MatrixTradeMode
        {
            None,
            Long_InnerChannel,      // Cruce entre Lower_1.75 y Upper_1.75 -> TP: Upper_3.5, SL: Lower_1.75
            Long_ExtremeBelow,      // Cruce debajo de Lower_1.75          -> TP: Midline,   SL: Lower_3.5
            Long_Zone1TrendBreak,   // v2.0: Ruptura en Zona 1 / Ruptura Externa tras N barras sin cruzar EMA200 -> TP Fijo, SL: Midline
            Short_InnerChannel,     // Cruce entre Lower_1.75 y Upper_1.75 -> TP: Lower_3.5, SL: Upper_1.75
            Short_ExtremeAbove,     // Cruce arriba de Upper_1.75          -> TP: Midline,   SL: Upper_3.5
            Short_Zone1TrendBreak   // v2.0: Ruptura en Zona 1 / Ruptura Externa tras N barras sin cruzar EMA200 -> TP Fijo, SL: Midline
        }

        private MatrixTradeMode currentTradeMode = MatrixTradeMode.None;

        // Variables para rastrear el lado previo del TEMA respecto a la EMA 200 y evitar disparos repetidos
        private int lastTemaSide = 0; // 1 = Arriba de EMA200, -1 = Debajo de EMA200, 0 = Indefinido
        private bool crossUpConsumed = false;
        private bool crossDownConsumed = false;

        // === VARIABLES v1.6 y v1.7: ESCUDO MFE BREAKEVEN, ANTI-SANCOCHO Y CONTADOR DE TENDENCIA VIRGEN ZONA 1 ===
        private bool isInnerBreakEvenActive = false;       // Indica si el trade de canal interno ya aseguró BreakEven tras superar la banda 1.75
        private int consecutiveLosses = 0;                 // Contador de pérdidas seguidas para apagar el Stop & Reverse en sancochos
        private int barsBelowEmaWithoutRealCross = 0;      // v1.7: Barras acumuladas con TEMA debajo de EMA200 sin cruce alcista real (>= MinCrossTicks)
        private int barsAboveEmaWithoutRealCross = 0;      // v1.7: Barras acumuladas con TEMA arriba de EMA200 sin cruce bajista real (>= MinCrossTicks)

        // === INDICADORES ===
        private TEMA tema9;
        private EMA ema200;
        private KeltnerChannel keltnerBig;   // Canal Grande (52, 3.5)
        private KeltnerChannel keltnerSmall; // Canal Pequeño (52, 1.75)

        // === INTERFAZ WPF (BOTÓN DE PÁNICO) ===
        private System.Windows.Controls.Button panicButton;
        private bool isStrategyActive = false; // Siempre inicia en PAUSADO por regla de oro
        private Chart chartWindow;
        private bool isSubscribedToKeys = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Estrategia Keltner Matrix en 610 Ticks para ES/MES. Cruces de TEMA(9) y EMA(200) clasificados por zonas de Doble Canal de Keltner (3.5 y 1.75) con TP/SL dinámicos, MFE BreakEven, S&R Adaptativo y Francotirador Zona 1.";
                Name                                        = "Tick610_KeltnerMatrixES";
                Calculate                                   = Calculate.OnBarClose;
                EntriesPerDirection                         = 1;
                EntryHandling                               = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy                = true;
                ExitOnSessionCloseSeconds                   = 30;
                IsFillLimitOnTouch                          = false;
                MaximumBarsLookBack                         = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution                         = OrderFillResolution.Standard;
                Slippage                                    = 0;
                StartBehavior                               = StartBehavior.WaitUntilFlat;
                TimeInForce                                 = TimeInForce.Gtc;
                TraceOrders                                 = false;
                RealtimeErrorHandling                       = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling                          = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade                         = 200;
                IsInstantiatedOnEachOptimizationIteration   = true;
                IncludeCommission                           = true; // v1.6: Incluir comisiones por defecto siempre en Strategy Performance

                // Propiedades generales
                Version                         = "2.0";

                // Horarios RTH (Nueva York EST) - Inicia 09:50 AM por regla transversal
                StartTime                       = 95000;  // 09:50 AM EST
                StopEntriesTime                 = 154500; // 03:45 PM EST (Dejar de abrir nuevas)
                ForceCloseTime                  = 160000; // 04:00 PM EST (Cerrar todo sí o sí)

                // Parámetros de Indicadores
                TemaPeriod                      = 9;
                EmaPeriod                       = 200;
                KeltnerPeriod                   = 52;
                KeltnerBigMult                  = 3.5;
                KeltnerSmallMult                = 1.75;

                // Filtros de Seguridad, Offsets de Bandas y Mejoras v1.6 / v1.7 / v2.0
                MinCrossTicks                   = 2.3;    // v1.7: Decimal (double) para filtrar punticas de 2.0 ticks sin llegar a 3.0
                UseSlopeFilter                  = true;   // Exigir que el TEMA lleve pendiente a favor
                RequireCloseOnCrossSide         = true;   // Exigir que el precio de cierre Close[0] también esté del lado del cruce
                SlOffsetTicks                   = 2;      // Ticks de respiro por fuera de la banda para el Stop Loss
                TpOffsetTicks                   = 1;      // Ticks por dentro de la banda para asegurar el Take Profit antes del rebote
                MinSafetySlTicks                = 8;      // Colchón mínimo de seguridad para el SL
                MinRoomToOuterTpTicks           = 10;     // v1.6 (Fuga #2): Ticks mínimos libres hacia banda 3.5 cuando la vela de cruce asoma fuera de 1.75
                UseMfeBreakEven                 = true;   // v1.6 (Fuga #1): Subir SL a BreakEven cuando supere banda 1.75 a favor + MfeTriggerTicks
                MfeTriggerTicks                 = 18;     // v1.6 (Fuga #1): Ganancia flotante mínima en ticks para activar el BreakEven en banda 1.75
                BreakEvenPlusTicks              = 2;      // v1.6 (Fuga #1): Ticks de ganancia asegurada al activar BreakEven (+2 ticks = +$25)
                UseStopAndReverse               = true;   // Activar Stop & Reverse en cruces contrarios
                UseAdaptiveSancochoShield       = true;   // v1.6 (Fuga #3): Apagar Stop & Reverse tras pérdida para no serrucharse en sancochos
                UseZone1TrendBreak              = true;   // v1.7: Activar Francotirador en Zona 1 cuando la EMA200 lleva N barras sin ser cruzada
                MinBarsTrendBreakZona1          = 100;    // v1.7: Barras mínimas sin cruce real para habilitar tiro en Zona 1 (100 barras ~= 25-30 min)
                Zone1FixedTpTicks               = 12;     // v1.7: Take Profit fijo rápido en Zona 1 (12 ticks = $150.00)
                UseOuterBandBreakout            = true;   // v2.0: Permitir entrada de Francotirador aunque el precio cierre unos ticks por fuera de la banda 3.5
                MaxOuterBufferTicks             = 6;      // v2.0: Tolerancia máxima en ticks por fuera de la banda 3.5 (6 ticks = 1.50 pts)
                ShowVisualMarkers               = false;  // No dibujar figuras extra en la gráfica por defecto
            }
            else if (State == State.DataLoaded)
            {
                // Instanciamos los indicadores únicamente en la memoria interna del robot (sin dibujarlos en la gráfica)
                tema9        = TEMA(Close, TemaPeriod);
                ema200       = EMA(Close, EmaPeriod);
                keltnerBig   = KeltnerChannel(Close, KeltnerBigMult, KeltnerPeriod);
                keltnerSmall = KeltnerChannel(Close, KeltnerSmallMult, KeltnerPeriod);
            }
            else if (State == State.Historical)
            {
                // En Backtest (Analyzer) la estrategia se activa sola; en Gráfica Real/Sim inicia PAUSADA
                if (ChartControl != null)
                {
                    isStrategyActive = false;
                    ChartControl.Dispatcher.InvokeAsync(() => CreateWPFControls());
                }
                else
                {
                    isStrategyActive = true;
                }
            }
            else if (State == State.Terminated)
            {
                if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() => RemoveWPFControls());
                }
            }
        }

        protected override void OnBarUpdate()
        {
            // 1. Validación de barras mínimas necesarias para que la EMA 200 exista
            if (CurrentBar < BarsRequiredToTrade)
                return;

            // Inicializamos lastTemaSide la primera vez que tenemos datos válidos
            if (lastTemaSide == 0)
            {
                lastTemaSide = (tema9[1] <= ema200[1]) ? -1 : 1;
            }

            // === v1.7: CONTADOR CONTINUO DE BARRAS SIN CRUCE REAL (DESDE EL INFINITO Y MÁS ALLÁ) ===
            // Guardamos cuántas barras acumuladas traíamos ANTES de evaluar esta barra.
            // Si el TEMA apenas toca o pasa la EMA200 por menos de MinCrossTicks (ej. < 2.3 ticks), NO es un cruce real y sigue sumando.
            int priorBarsBelowEma = barsBelowEmaWithoutRealCross;
            int priorBarsAboveEma = barsAboveEmaWithoutRealCross;

            double currentUpTicks   = (tema9[0] - ema200[0]) / TickSize;
            double currentDownTicks = (ema200[0] - tema9[0]) / TickSize;

            if (currentUpTicks < MinCrossTicks)
                barsBelowEmaWithoutRealCross++;
            else if (!RequireCloseOnCrossSide || Close[0] > ema200[0])
                barsBelowEmaWithoutRealCross = 0;

            if (currentDownTicks < MinCrossTicks)
                barsAboveEmaWithoutRealCross++;
            else if (!RequireCloseOnCrossSide || Close[0] < ema200[0])
                barsAboveEmaWithoutRealCross = 0;

            int currentTime = ToTime(Time[0]);

            // 2. Cierre Forzoso al final de la sesión RTH (16:00 EST)
            if (currentTime >= ForceCloseTime || currentTime < StartTime)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong("CierreSesionRTH", "");
                    else if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort("CierreSesionRTH", "");

                    Print(Time[0] + " - [CIERRE FORZOSO RTH] Posición cerrada por fin de horario operativo.");
                }
                currentTradeMode = MatrixTradeMode.None;
                isInnerBreakEvenActive = false;
                consecutiveLosses = 0; // Reiniciamos contador de pérdidas al iniciar/cerrar cada día operativo
                // Mantenemos rastreado de qué lado está el TEMA fuera de horario para que a las 09:50 AM sepa de dónde viene
                if (tema9[0] <= ema200[0]) { lastTemaSide = -1; crossUpConsumed = false; }
                else if (tema9[0] > ema200[0]) { lastTemaSide = 1; crossDownConsumed = false; }
                return;
            }

            // 3. Si estamos dentro de una posición abierta, actualizamos FIELMENTE el TP y el SL siguiendo sus bandas (+ Offsets + MFE BreakEven)
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                UpdateDynamicBandsTargets();
            }
            else
            {
                currentTradeMode = MatrixTradeMode.None;
                isInnerBreakEvenActive = false;
            }

            // 4. Si el Botón de Pánico está en PAUSADO (Rojo) o pasó la hora límite de entradas, no abrimos trades
            // Pero mantenemos rastreado el lado del TEMA para cuando el usuario active el botón
            if (!isStrategyActive || currentTime > StopEntriesTime)
            {
                if (tema9[0] <= ema200[0]) { lastTemaSide = -1; crossUpConsumed = false; }
                else if (tema9[0] > ema200[0]) { lastTemaSide = 1; crossDownConsumed = false; }
                return;
            }

            // === DETECCIÓN DEL ESTADO DE CRUCE (PELIGRO 2 - OPCIÓN A: CRUCE REAL + PENDIENTE) ===
            // Si el TEMA regresa al lado opuesto (o si ya había consumido el cruce previo en esa dirección),
            // actualizamos lastTemaSide para que quede armado el cruce hacia el otro lado.
            // Si aún NO ha superado MinCrossTicks, conservamos el lado de origen para evaluarlo en la siguiente barra.
            if (tema9[0] <= ema200[0])
            {
                crossUpConsumed = false;
                if (crossDownConsumed) lastTemaSide = -1;
            }
            if (tema9[0] >= ema200[0])
            {
                crossDownConsumed = false;
                if (crossUpConsumed) lastTemaSide = 1;
            }

            // Extraemos los niveles actuales de las bandas para legibilidad
            double upperBig   = keltnerBig.Upper[0];
            double lowerBig   = keltnerBig.Lower[0];
            double midline    = keltnerBig.Midline[0];
            double upperSmall = keltnerSmall.Upper[0];
            double lowerSmall = keltnerSmall.Lower[0];
            double slOffset   = SlOffsetTicks * TickSize;
            double tpOffset   = TpOffsetTicks * TickSize;

            // === MEJORA v1.6 (FUGA #3): STOP & REVERSE ADAPTATIVO (ESCUDO ANTI-SANCOCHO) ===
            // Si venimos de 0 pérdidas (o si el escudo está desactivado), el Stop & Reverse opera normal para capturar giros limpios (10/12, 10/13, 10/14).
            // Si el trade anterior cerró en pérdida (consecutiveLosses >= 1), apagamos temporalmente el Stop & Reverse para no serrucharnos
            // dentro de un sancocho y dejamos que la posición abierta respire hasta su SL en la banda 1.75 o su TP.
            bool effectiveStopAndReverse = UseStopAndReverse && (!UseAdaptiveSancochoShield || consecutiveLosses < 1);

            // Si estamos dentro de una posición y el Stop & Reverse está desactivado (manual o por Escudo Anti-Sancocho),
            // consumimos silenciosamente cualquier cruce que ocurra mientras estamos adentro para evitar que se dispare con retraso al tocar el SL.
            if (Position.MarketPosition == MarketPosition.Short && !effectiveStopAndReverse && tema9[0] > ema200[0])
            {
                double distTicks = (tema9[0] - ema200[0]) / TickSize;
                if (distTicks >= MinCrossTicks && (!RequireCloseOnCrossSide || Close[0] > ema200[0]))
                {
                    crossUpConsumed = true;
                    lastTemaSide = 1;
                    Print(Time[0] + " - [ESCUDO ANTI-SANCOCHO] Cruce alcista ignorado con Corto abierto (Stop & Reverse en enfriamiento).");
                }
            }
            else if (Position.MarketPosition == MarketPosition.Long && !effectiveStopAndReverse && tema9[0] < ema200[0])
            {
                double distTicks = (ema200[0] - tema9[0]) / TickSize;
                if (distTicks >= MinCrossTicks && (!RequireCloseOnCrossSide || Close[0] < ema200[0]))
                {
                    crossDownConsumed = true;
                    lastTemaSide = -1;
                    Print(Time[0] + " - [ESCUDO ANTI-SANCOCHO] Cruce bajista ignorado con Largo abierto (Stop & Reverse en enfriamiento).");
                }
            }

            // Evaluamos cruces cuando estamos Flat, O cuando estamos en la posición contraria y effectiveStopAndReverse está activo
            bool canEvaluateLongCross  = (Position.MarketPosition == MarketPosition.Flat) || (effectiveStopAndReverse && Position.MarketPosition == MarketPosition.Short);
            bool canEvaluateShortCross = (Position.MarketPosition == MarketPosition.Flat) || (effectiveStopAndReverse && Position.MarketPosition == MarketPosition.Long);

            // -------------------------------------------------------------------------
            // CASO A: INTENTO DE CRUCE ALCISTA (TEMA 9 cruza de abajo hacia arriba EMA 200)
            // -------------------------------------------------------------------------
            if (canEvaluateLongCross && lastTemaSide == -1 && !crossUpConsumed && tema9[0] > ema200[0])
            {
                double crossDistanceTicks = (tema9[0] - ema200[0]) / TickSize;
                double slopeTicks = (tema9[0] - tema9[1]) / TickSize;

                // Peligro 2 (Opción A): ¿Superó los ticks mínimos de cruce real?
                if (crossDistanceTicks < MinCrossTicks)
                {
                    Print(Time[0] + string.Format(" - [CRUCE LARGO RECHAZADO] TEMA cruzó EMA200 por solo {0:F1} tks (Mínimo requerido: {1} tks). Esperando mayor penetración.", crossDistanceTicks, MinCrossTicks));
                    return;
                }

                // Mejora 3 (Anti-Falso Cruce 2:36 PM): ¿El precio de cierre real Close[0] también cerró por encima de la EMA 200?
                // Si el TEMA subió por inercia pero la vela fue rechazada y su cierre (puntito naranja) quedó debajo de la EMA 200, es un falso cruce.
                if (RequireCloseOnCrossSide && Close[0] <= ema200[0])
                {
                    Print(Time[0] + string.Format(" - [FALSO CRUCE LARGO RECHAZADO] TEMA está sobre EMA200 pero la vela cerró con rechazo por debajo de EMA200 (Close: {0:F2} <= EMA200: {1:F2}).", Close[0], ema200[0]));
                    return;
                }

                // Si ya superó la penetración mínima y el cierre acompaña, marcamos este cruce como consumido
                crossUpConsumed = true;
                lastTemaSide = 1;

                // Peligro 2 (Opción A): ¿Lleva pendiente positiva a favor del Largo?
                if (UseSlopeFilter && slopeTicks <= 0)
                {
                    Print(Time[0] + string.Format(" - [LARGO CANCELADO POR PENDIENTE] El cruce fue real pero el TEMA lleva pendiente plana/negativa ({0:F2} tks).", slopeTicks));
                    return;
                }

                bool isReversalFromShort = (Position.MarketPosition == MarketPosition.Short);

                // CLASIFICACIÓN POR ZONAS DEL CANAL DE KELTNER:
                // ZONA 1: El TEMA(9) cruza arriba de la banda exterior superior del canal pequeño (Upper_1.75)
                // En condiciones normales NO se abre Largo, SALVO que venga de una tendencia bajista madura (>= MinBarsTrendBreakZona1 barras sin cruzar la EMA200)
                // v2.0: Con UseOuterBandBreakout permitimos entrar aunque el precio perfore por fuera de Upper_3.5 hasta MaxOuterBufferTicks
                if (tema9[0] >= upperSmall)
                {
                    bool isPriceInsideOrAllowedOuter = UseOuterBandBreakout
                        ? (Close[0] <= upperBig + (MaxOuterBufferTicks * TickSize))
                        : (Close[0] < upperBig);

                    if (UseZone1TrendBreak && priorBarsBelowEma >= MinBarsTrendBreakZona1 && isPriceInsideOrAllowedOuter)
                    {
                        double rawSlPrice = midline - slOffset;
                        double minSafeSlPrice = Close[0] - (MinSafetySlTicks * TickSize);
                        double finalSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Min(rawSlPrice, minSafeSlPrice));
                        double finalTpPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0] + (Zone1FixedTpTicks * TickSize));

                        currentTradeMode = MatrixTradeMode.Long_Zone1TrendBreak;
                        isInnerBreakEvenActive = false;
                        SetStopLoss("MatrixLong_Zona1", CalculationMode.Price, finalSlPrice, false);
                        SetProfitTarget("MatrixLong_Zona1", CalculationMode.Price, finalTpPrice);
                        EnterLong("MatrixLong_Zona1");

                        if (ShowVisualMarkers)
                            Draw.TriangleUp(this, "LongZ1_" + CurrentBar, true, 0, Low[0] - (4 * TickSize), Brushes.Gold);

                        string tagTolerancia = (Close[0] >= upperBig) ? string.Format(" [RUPTURA EXTERNA +{0:F1}tks]", (Close[0] - upperBig) / TickSize) : "";
                        Print(Time[0] + string.Format(" - [FRANCOTIRADOR ZONA 1 -> LARGO ABIERTO{0}] Ruptura tras {1} barras bajo EMA200 | Entrada @ ~{2:F2} | TP Fijo (+{3}tks): {4:F2} | SL Respaldo Midline: {5:F2}",
                            tagTolerancia, priorBarsBelowEma, Close[0], Zone1FixedTpTicks, finalTpPrice, finalSlPrice));
                        return;
                    }

                    if (isReversalFromShort)
                    {
                        ExitShort("StopReverse_CierreCorto", "");
                        currentTradeMode = MatrixTradeMode.None;
                        isInnerBreakEvenActive = false;
                        Print(Time[0] + string.Format(" - [STOP CRUCE CONTRARIO] Corto cerrado por cruce alcista real, pero NO se abre Largo por estar TEMA en Zona Sobrecomprada (TEMA: {0:F2} >= Upper1.75: {1:F2}, Barras sin cruce: {2}).", tema9[0], upperSmall, priorBarsBelowEma));
                    }
                    else
                    {
                        if (!isPriceInsideOrAllowedOuter)
                        {
                            Print(Time[0] + string.Format(" - [LARGO CANCELADO - SOBREEXTENSIÓN EXTERNA] Close ({0:F2}) superó Banda 3.5 ({1:F2}) por más de {2} ticks de tolerancia permitidos.", Close[0], upperBig, MaxOuterBufferTicks));
                        }
                        else
                        {
                            Print(Time[0] + string.Format(" - [LARGO CANCELADO - ZONA SOBRECOMPRADA] TEMA ({0:F2}) arriba de Upper1.75 ({1:F2}) y solo llevaba {2}/{3} barras sin cruce.", tema9[0], upperSmall, priorBarsBelowEma, MinBarsTrendBreakZona1));
                        }
                    }
                    if (ShowVisualMarkers)
                        Draw.Text(this, "CancelLong_" + CurrentBar, "✖ Zona Alta", 0, High[0] + (4 * TickSize), Brushes.OrangeRed);
                    return;
                }
                // ZONA 2 y 3: El TEMA(9) cruza entre Lower_1.75 y Upper_1.75 (Ya sea arriba o debajo de Midline)
                // -> TP en Upper_3.5 - TpOffset y SL en Lower_1.75 - SlOffset
                else if (tema9[0] >= lowerSmall && tema9[0] < upperSmall)
                {
                    double rawSlPrice = lowerSmall - slOffset;
                    double rawTpPrice = upperBig - tpOffset;
                    double roomToTpTicks = (rawTpPrice - Close[0]) / TickSize;

                    // Mejora v1.6 (Fuga #2): Si el TEMA cruzó dentro del canal 1.75 pero la vela de ruptura cerró asomando por encima de Upper_1.75,
                    // permitimos la entrada en cascada SIEMPRE Y CUANDO queden al menos MinRoomToOuterTpTicks (ej. 10 ticks) libres hasta el TP de la banda 3.5.
                    // Si cerró tan lejos que ya no queda espacio hacia la banda 3.5 (ej. vela gigante de las 2:54 PM del 10/14), cancelamos el Largo.
                    if ((Close[0] >= upperSmall && roomToTpTicks < MinRoomToOuterTpTicks) || roomToTpTicks < 4)
                    {
                        if (isReversalFromShort)
                        {
                            ExitShort("StopReverse_CierreCorto", "");
                            currentTradeMode = MatrixTradeMode.None;
                            isInnerBreakEvenActive = false;
                            Print(Time[0] + string.Format(" - [STOP CRUCE CONTRARIO] Corto cerrado por cruce alcista, pero NO se abre Largo por falta de espacio a Banda 3.5 (Close: {0:F2}, Espacio TP: {1:F1} tks < {2} tks).", Close[0], roomToTpTicks, MinRoomToOuterTpTicks));
                        }
                        else
                        {
                            Print(Time[0] + string.Format(" - [LARGO CANCELADO - VELA SOBREEXTENDIDA] Close ({0:F2}) superó Upper1.75 dejando solo {1:F1} tks hacia Banda 3.5 (Mínimo: {2} tks).", Close[0], roomToTpTicks, MinRoomToOuterTpTicks));
                        }
                        if (ShowVisualMarkers)
                            Draw.Text(this, "CancelLong_" + CurrentBar, "✖ Sin Espacio 3.5", 0, High[0] + (4 * TickSize), Brushes.OrangeRed);
                        return;
                    }

                    string subZona = (tema9[0] >= midline) ? "Entre Media y Superior Pequeña" : "Entre Inferior Pequeña y Media";

                    // Peligro 1 (Opción A): Colchón de seguridad mínimo para el SL
                    double minSafeSlPrice = Close[0] - (MinSafetySlTicks * TickSize);
                    double finalSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Min(rawSlPrice, minSafeSlPrice));
                    double finalTpPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Max(rawTpPrice, Close[0] + (4 * TickSize)));

                    currentTradeMode = MatrixTradeMode.Long_InnerChannel;
                    isInnerBreakEvenActive = false;
                    SetStopLoss("MatrixLong_Inner", CalculationMode.Price, finalSlPrice, false);
                    SetProfitTarget("MatrixLong_Inner", CalculationMode.Price, finalTpPrice);
                    EnterLong("MatrixLong_Inner");

                    if (ShowVisualMarkers)
                        Draw.TriangleUp(this, "LongInner_" + CurrentBar, true, 0, Low[0] - (4 * TickSize), Brushes.LimeGreen);

                    string tagAccion = isReversalFromShort ? "STOP & REVERSE -> LARGO ABIERTO" : "LARGO ABIERTO";
                    Print(Time[0] + string.Format(" - [{0} - CANAL INTERNO ({1})] Entrada @ ~{2:F2} | TP Dinámico (Upper 3.5 -{3}tks): {4:F2} | SL Dinámico (Lower 1.75 -{5}tks): {6:F2}",
                        tagAccion, subZona, Close[0], TpOffsetTicks, finalTpPrice, SlOffsetTicks, finalSlPrice));
                }
                // ZONA 4: Debajo de la banda inferior del canal pequeño (Lower_1.75)
                // -> TP en Banda Media (Midline - TpOffset) y SL en Banda Inferior Grande (Lower_3.5 - SlOffset) con colchón permanente
                else if (tema9[0] < lowerSmall)
                {
                    double rawSlPrice = lowerBig - slOffset;
                    double rawTpPrice = midline - tpOffset;

                    // Validamos que todavía exista espacio real (al menos 4 ticks) hacia la Midline
                    if (Close[0] >= rawTpPrice - (3 * TickSize))
                    {
                        if (isReversalFromShort)
                        {
                            ExitShort("StopReverse_CierreCorto", "");
                            currentTradeMode = MatrixTradeMode.None;
                            isInnerBreakEvenActive = false;
                        }
                        Print(Time[0] + " - [LARGO CANCELADO - SIN ESPACIO A MIDLINE] El TEMA cruzó debajo de Lower 1.75 pero la vela cerró ya pegada a la Línea Media.");
                        return;
                    }

                    // Mejora 5 (Caso 3:40 PM): Colchón de seguridad mínimo desde el precio de entrada y debajo de la EMA200
                    double minSafeSlPrice = Math.Min(Close[0] - (MinSafetySlTicks * TickSize), ema200[0] - slOffset);
                    double finalSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Min(rawSlPrice, minSafeSlPrice));
                    double finalTpPrice = Instrument.MasterInstrument.RoundToTickSize(rawTpPrice);

                    currentTradeMode = MatrixTradeMode.Long_ExtremeBelow;
                    isInnerBreakEvenActive = false;
                    SetStopLoss("MatrixLong_Extreme", CalculationMode.Price, finalSlPrice, false);
                    SetProfitTarget("MatrixLong_Extreme", CalculationMode.Price, finalTpPrice);
                    EnterLong("MatrixLong_Extreme");

                    if (ShowVisualMarkers)
                        Draw.TriangleUp(this, "LongExt_" + CurrentBar, true, 0, Low[0] - (4 * TickSize), Brushes.Cyan);

                    string tagAccion = isReversalFromShort ? "STOP & REVERSE -> LARGO ABIERTO" : "LARGO ABIERTO";
                    Print(Time[0] + string.Format(" - [{0} - ZONA EXTREMA INFERIOR] Entrada @ ~{1:F2} | TP Dinámico (Midline -{2}tks): {3:F2} | SL Dinámico (Lower 3.5 / Colchón): {4:F2}",
                        tagAccion, Close[0], TpOffsetTicks, finalTpPrice, finalSlPrice));
                }
            }

            // -------------------------------------------------------------------------
            // CASO B: INTENTO DE CRUCE BAJISTA (TEMA 9 cruza de arriba hacia abajo EMA 200)
            // -------------------------------------------------------------------------
            else if (canEvaluateShortCross && lastTemaSide == 1 && !crossDownConsumed && tema9[0] < ema200[0])
            {
                double crossDistanceTicks = (ema200[0] - tema9[0]) / TickSize;
                double slopeTicks = (tema9[1] - tema9[0]) / TickSize; // Positivo si está cayendo

                // Peligro 2 (Opción A): ¿Superó los ticks mínimos de cruce real?
                if (crossDistanceTicks < MinCrossTicks)
                {
                    Print(Time[0] + string.Format(" - [CRUCE CORTO RECHAZADO] TEMA cruzó EMA200 hacia abajo por solo {0:F1} tks (Mínimo requerido: {1} tks). Esperando mayor penetración.", crossDistanceTicks, MinCrossTicks));
                    return;
                }

                // Mejora 3 (Anti-Falso Cruce): ¿El precio de cierre real Close[0] también cerró por debajo de la EMA 200?
                if (RequireCloseOnCrossSide && Close[0] >= ema200[0])
                {
                    Print(Time[0] + string.Format(" - [FALSO CRUCE CORTO RECHAZADO] TEMA está bajo EMA200 pero la vela cerró con rechazo por encima de EMA200 (Close: {0:F2} >= EMA200: {1:F2}).", Close[0], ema200[0]));
                    return;
                }

                crossDownConsumed = true;
                lastTemaSide = -1;

                // Peligro 2 (Opción A): ¿Lleva pendiente negativa a favor del Corto?
                if (UseSlopeFilter && slopeTicks <= 0)
                {
                    Print(Time[0] + string.Format(" - [CORTO CANCELADO POR PENDIENTE] El cruce fue real pero el TEMA lleva pendiente plana/alcista ({0:F2} tks).", slopeTicks));
                    return;
                }

                bool isReversalFromLong = (Position.MarketPosition == MarketPosition.Long);

                // CLASIFICACIÓN POR ZONAS DEL CANAL DE KELTNER (ESPEJO INVERSO):
                // ZONA 1 ESPEJO: El TEMA(9) cruza debajo de la banda exterior inferior del canal pequeño (Lower_1.75)
                // En condiciones normales NO se abre Corto, SALVO que venga de una tendencia alcista madura (>= MinBarsTrendBreakZona1 barras sin cruzar la EMA200)
                // v2.0: Con UseOuterBandBreakout permitimos entrar aunque el precio perfore por fuera de Lower_3.5 hasta MaxOuterBufferTicks
                if (tema9[0] <= lowerSmall)
                {
                    bool isPriceInsideOrAllowedOuter = UseOuterBandBreakout
                        ? (Close[0] >= lowerBig - (MaxOuterBufferTicks * TickSize))
                        : (Close[0] > lowerBig);

                    if (UseZone1TrendBreak && priorBarsAboveEma >= MinBarsTrendBreakZona1 && isPriceInsideOrAllowedOuter)
                    {
                        double rawSlPrice = midline + slOffset;
                        double minSafeSlPrice = Close[0] + (MinSafetySlTicks * TickSize);
                        double finalSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Max(rawSlPrice, minSafeSlPrice));
                        double finalTpPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0] - (Zone1FixedTpTicks * TickSize));

                        currentTradeMode = MatrixTradeMode.Short_Zone1TrendBreak;
                        isInnerBreakEvenActive = false;
                        SetStopLoss("MatrixShort_Zona1", CalculationMode.Price, finalSlPrice, false);
                        SetProfitTarget("MatrixShort_Zona1", CalculationMode.Price, finalTpPrice);
                        EnterShort("MatrixShort_Zona1");

                        if (ShowVisualMarkers)
                            Draw.TriangleDown(this, "ShortZ1_" + CurrentBar, true, 0, High[0] + (4 * TickSize), Brushes.Gold);

                        string tagTolerancia = (Close[0] <= lowerBig) ? string.Format(" [RUPTURA EXTERNA -{0:F1}tks]", (lowerBig - Close[0]) / TickSize) : "";
                        Print(Time[0] + string.Format(" - [FRANCOTIRADOR ZONA 1 -> CORTO ABIERTO{0}] Ruptura tras {1} barras sobre EMA200 | Entrada @ ~{2:F2} | TP Fijo (-{3}tks): {4:F2} | SL Respaldo Midline: {5:F2}",
                            tagTolerancia, priorBarsAboveEma, Close[0], Zone1FixedTpTicks, finalTpPrice, finalSlPrice));
                        return;
                    }

                    if (isReversalFromLong)
                    {
                        ExitLong("StopReverse_CierreLargo", "");
                        currentTradeMode = MatrixTradeMode.None;
                        isInnerBreakEvenActive = false;
                        Print(Time[0] + string.Format(" - [STOP CRUCE CONTRARIO] Largo cerrado por cruce bajista real, pero NO se abre Corto por estar TEMA en Zona Sobrevendida (TEMA: {0:F2} <= Lower1.75: {1:F2}, Barras sin cruce: {2}).", tema9[0], lowerSmall, priorBarsAboveEma));
                    }
                    else
                    {
                        if (!isPriceInsideOrAllowedOuter)
                        {
                            Print(Time[0] + string.Format(" - [CORTO CANCELADO - SOBREEXTENSIÓN EXTERNA] Close ({0:F2}) perforó Banda 3.5 ({1:F2}) por más de {2} ticks de tolerancia permitidos.", Close[0], lowerBig, MaxOuterBufferTicks));
                        }
                        else
                        {
                            Print(Time[0] + string.Format(" - [CORTO CANCELADO - ZONA SOBREVENDIDA] TEMA ({0:F2}) debajo de Lower1.75 ({1:F2}) y solo llevaba {2}/{3} barras sin cruce.", tema9[0], lowerSmall, priorBarsAboveEma, MinBarsTrendBreakZona1));
                        }
                    }
                    if (ShowVisualMarkers)
                        Draw.Text(this, "CancelShort_" + CurrentBar, "✖ Zona Baja", 0, Low[0] - (4 * TickSize), Brushes.OrangeRed);
                    return;
                }
                // ZONA 2 y 3 ESPEJO: El TEMA(9) cruza entre Lower_1.75 y Upper_1.75
                // -> TP en Lower_3.5 + TpOffset y SL en Upper_1.75 + SlOffset
                else if (tema9[0] > lowerSmall && tema9[0] <= upperSmall)
                {
                    double rawSlPrice = upperSmall + slOffset;
                    double rawTpPrice = lowerBig + tpOffset;
                    double roomToTpTicks = (Close[0] - rawTpPrice) / TickSize;

                    // Mejora v1.6 (Fuga #2 - Desbloqueo de Cascadas 10/08 12:43 PM y 10/16 10:54 AM):
                    // Si el TEMA(9) cruzó dentro del canal 1.75 pero la vela de ruptura cerró debajo de Lower_1.75,
                    // permitimos el Corto SIEMPRE Y CUANDO queden al menos MinRoomToOuterTpTicks (ej. 10 ticks) libres hasta Lower_3.5.
                    if ((Close[0] <= lowerSmall && roomToTpTicks < MinRoomToOuterTpTicks) || roomToTpTicks < 4)
                    {
                        if (isReversalFromLong)
                        {
                            ExitLong("StopReverse_CierreLargo", "");
                            currentTradeMode = MatrixTradeMode.None;
                            isInnerBreakEvenActive = false;
                            Print(Time[0] + string.Format(" - [STOP CRUCE CONTRARIO] Largo cerrado por cruce bajista, pero NO se abre Corto por falta de espacio a Banda 3.5 (Close: {0:F2}, Espacio TP: {1:F1} tks < {2} tks).", Close[0], roomToTpTicks, MinRoomToOuterTpTicks));
                        }
                        else
                        {
                            Print(Time[0] + string.Format(" - [CORTO CANCELADO - VELA SOBREEXTENDIDA] Close ({0:F2}) perforó Lower1.75 dejando solo {1:F1} tks hacia Banda 3.5 (Mínimo: {2} tks).", Close[0], roomToTpTicks, MinRoomToOuterTpTicks));
                        }
                        if (ShowVisualMarkers)
                            Draw.Text(this, "CancelShort_" + CurrentBar, "✖ Sin Espacio 3.5", 0, Low[0] - (4 * TickSize), Brushes.OrangeRed);
                        return;
                    }

                    string subZona = (tema9[0] <= midline) ? "Entre Inferior Pequeña y Media" : "Entre Media y Superior Pequeña";

                    // Peligro 1 (Opción A): Colchón de seguridad mínimo para el SL
                    double minSafeSlPrice = Close[0] + (MinSafetySlTicks * TickSize);
                    double finalSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Max(rawSlPrice, minSafeSlPrice));
                    double finalTpPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Min(rawTpPrice, Close[0] - (4 * TickSize)));

                    currentTradeMode = MatrixTradeMode.Short_InnerChannel;
                    isInnerBreakEvenActive = false;
                    SetStopLoss("MatrixShort_Inner", CalculationMode.Price, finalSlPrice, false);
                    SetProfitTarget("MatrixShort_Inner", CalculationMode.Price, finalTpPrice);
                    EnterShort("MatrixShort_Inner");

                    if (ShowVisualMarkers)
                        Draw.TriangleDown(this, "ShortInner_" + CurrentBar, true, 0, High[0] + (4 * TickSize), Brushes.Magenta);

                    string tagAccion = isReversalFromLong ? "STOP & REVERSE -> CORTO ABIERTO" : "CORTO ABIERTO";
                    Print(Time[0] + string.Format(" - [{0} - CANAL INTERNO ({1})] Entrada @ ~{2:F2} | TP Dinámico (Lower 3.5 +{3}tks): {4:F2} | SL Dinámico (Upper 1.75 +{5}tks): {6:F2}",
                        tagAccion, subZona, Close[0], TpOffsetTicks, finalTpPrice, SlOffsetTicks, finalSlPrice));
                }
                // ZONA 4 ESPEJO: Arriba de la banda superior del canal pequeño (Upper_1.75)
                // -> TP en Banda Media (Midline + TpOffset) y SL en Banda Superior Grande (Upper_3.5 + SlOffset) con colchón permanente
                else if (tema9[0] > upperSmall)
                {
                    double rawSlPrice = upperBig + slOffset;
                    double rawTpPrice = midline + tpOffset;

                    // Validamos que todavía exista espacio real (al menos 4 ticks) hacia la Midline (Evita el micro-trade de las 11:21 AM)
                    if (Close[0] <= rawTpPrice + (3 * TickSize))
                    {
                        if (isReversalFromLong)
                        {
                            ExitLong("StopReverse_CierreLargo", "");
                            currentTradeMode = MatrixTradeMode.None;
                            isInnerBreakEvenActive = false;
                        }
                        Print(Time[0] + " - [CORTO CANCELADO - SIN ESPACIO A MIDLINE] El TEMA cruzó arriba de Upper 1.75 pero la vela cerró ya pegada a la Línea Media.");
                        return;
                    }

                    // Mejora 5: Colchón de seguridad mínimo desde el precio de entrada y encima de la EMA200
                    double minSafeSlPrice = Math.Max(Close[0] + (MinSafetySlTicks * TickSize), ema200[0] + slOffset);
                    double finalSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Max(rawSlPrice, minSafeSlPrice));
                    double finalTpPrice = Instrument.MasterInstrument.RoundToTickSize(rawTpPrice);

                    currentTradeMode = MatrixTradeMode.Short_ExtremeAbove;
                    isInnerBreakEvenActive = false;
                    SetStopLoss("MatrixShort_Extreme", CalculationMode.Price, finalSlPrice, false);
                    SetProfitTarget("MatrixShort_Extreme", CalculationMode.Price, finalTpPrice);
                    EnterShort("MatrixShort_Extreme");

                    if (ShowVisualMarkers)
                        Draw.TriangleDown(this, "ShortExt_" + CurrentBar, true, 0, High[0] + (4 * TickSize), Brushes.Orange);

                    string tagAccion = isReversalFromLong ? "STOP & REVERSE -> CORTO ABIERTO" : "CORTO ABIERTO";
                    Print(Time[0] + string.Format(" - [{0} - ZONA EXTREMA SUPERIOR] Entrada @ ~{1:F2} | TP Dinámico (Midline +{2}tks): {3:F2} | SL Dinámico (Upper 3.5 / Colchón): {4:F2}",
                        tagAccion, Close[0], TpOffsetTicks, finalTpPrice, finalSlPrice));
                }
            }
        }

        // === ACTUALIZACIÓN FIEL DE BANDAS DINÁMICAS (PELIGRO 3 + OFFSETS EN TP Y SL + COLCHÓN EXTREMO + MFE BREAKEVEN) ===
        private void UpdateDynamicBandsTargets()
        {
            double slOffset     = SlOffsetTicks * TickSize;
            double tpOffset     = TpOffsetTicks * TickSize;
            double entryPrice   = Position.AveragePrice;

            // TP con Offset hacia adentro del canal para asegurar llenado por orden Limit antes del rebote (Mejora 1: Caso 12:54 PM)
            double upperBigTp      = Instrument.MasterInstrument.RoundToTickSize(keltnerBig.Upper[0] - tpOffset);
            double lowerBigTp      = Instrument.MasterInstrument.RoundToTickSize(keltnerBig.Lower[0] + tpOffset);
            double midlineLongTp   = Instrument.MasterInstrument.RoundToTickSize(keltnerBig.Midline[0] - tpOffset);
            double midlineShortTp  = Instrument.MasterInstrument.RoundToTickSize(keltnerBig.Midline[0] + tpOffset);

            // SL con Offset hacia afuera de la banda para evitar barridas de mechas (Caso 9:51 AM y 10:36 AM)
            double upperBigSl   = Instrument.MasterInstrument.RoundToTickSize(keltnerBig.Upper[0] + slOffset);
            double lowerBigSl   = Instrument.MasterInstrument.RoundToTickSize(keltnerBig.Lower[0] - slOffset);
            double upperSmallSl = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Upper[0] + slOffset);
            double lowerSmallSl = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Lower[0] - slOffset);

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (currentTradeMode == MatrixTradeMode.Long_InnerChannel)
                {
                    // Mejora v1.6 (Fuga #1 - Protección MFE): Si la vela ya superó la banda 1.75 superior a favor y alcanzó MfeTriggerTicks, aseguramos BreakEven
                    if (UseMfeBreakEven && !isInnerBreakEvenActive)
                    {
                        double maxFavorableTicks = (High[0] - entryPrice) / TickSize;
                        if (High[0] >= keltnerSmall.Upper[0] && maxFavorableTicks >= MfeTriggerTicks)
                        {
                            isInnerBreakEvenActive = true;
                            Print(Time[0] + string.Format(" - [ESCUDO MFE BREAKEVEN ACTIVADO - LARGO] El precio superó Upper 1.75 con +{0:F1} tks a favor. SL protegido en BreakEven +{1} tks ({2:F2}).",
                                maxFavorableTicks, BreakEvenPlusTicks, entryPrice + (BreakEvenPlusTicks * TickSize)));
                        }
                    }

                    double effectiveLongSl = lowerSmallSl;
                    if (isInnerBreakEvenActive)
                    {
                        double breakEvenPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice + (BreakEvenPlusTicks * TickSize));
                        effectiveLongSl = Math.Max(lowerSmallSl, breakEvenPrice);
                    }

                    if (Close[0] >= upperBigTp)
                        ExitLong("TP_Dinamico_Upper3.5", "MatrixLong_Inner");
                    else if (Close[0] <= effectiveLongSl)
                        ExitLong(isInnerBreakEvenActive ? "SL_BreakEven_MFE" : "SL_Dinamico_Lower1.75", "MatrixLong_Inner");
                    else
                    {
                        SetProfitTarget("MatrixLong_Inner", CalculationMode.Price, upperBigTp);
                        SetStopLoss("MatrixLong_Inner", CalculationMode.Price, effectiveLongSl, false);
                    }
                }
                else if (currentTradeMode == MatrixTradeMode.Long_ExtremeBelow)
                {
                    // Mejora 5 (Caso 3:40 PM): Si el cruce fue fuera de la banda 3.5, mantenemos el colchón mínimo respecto a la entrada
                    double safeExtremeLongSl = Instrument.MasterInstrument.RoundToTickSize(Math.Min(lowerBigSl, entryPrice - (MinSafetySlTicks * TickSize)));

                    if (Close[0] >= midlineLongTp)
                        ExitLong("TP_Dinamico_Midline", "MatrixLong_Extreme");
                    else if (Close[0] <= safeExtremeLongSl)
                        ExitLong("SL_Dinamico_Lower3.5", "MatrixLong_Extreme");
                    else
                    {
                        SetProfitTarget("MatrixLong_Extreme", CalculationMode.Price, midlineLongTp);
                        SetStopLoss("MatrixLong_Extreme", CalculationMode.Price, safeExtremeLongSl, false);
                    }
                }
                else if (currentTradeMode == MatrixTradeMode.Long_Zone1TrendBreak)
                {
                    // v2.0 (Respiro Total Zona 1): Stop Loss anclado permanentemente a la Línea Media (Midline) para darle respiro completo al trade sin sustos de la EMA200
                    double safeZone1LongSl = Instrument.MasterInstrument.RoundToTickSize(Math.Min(keltnerBig.Midline[0] - slOffset, entryPrice - (MinSafetySlTicks * TickSize)));
                    if (Close[0] <= safeZone1LongSl)
                        ExitLong("SL_Midline_Zona1", "MatrixLong_Zona1");
                    else if (safeZone1LongSl < Close[0])
                        SetStopLoss("MatrixLong_Zona1", CalculationMode.Price, safeZone1LongSl, false);
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (currentTradeMode == MatrixTradeMode.Short_InnerChannel)
                {
                    // Mejora v1.6 (Fuga #1 - Protección MFE): Si la vela ya perforó la banda 1.75 inferior a favor y alcanzó MfeTriggerTicks, aseguramos BreakEven
                    if (UseMfeBreakEven && !isInnerBreakEvenActive)
                    {
                        double maxFavorableTicks = (entryPrice - Low[0]) / TickSize;
                        if (Low[0] <= keltnerSmall.Lower[0] && maxFavorableTicks >= MfeTriggerTicks)
                        {
                            isInnerBreakEvenActive = true;
                            Print(Time[0] + string.Format(" - [ESCUDO MFE BREAKEVEN ACTIVADO - CORTO] El precio perforó Lower 1.75 con +{0:F1} tks a favor. SL protegido en BreakEven +{1} tks ({2:F2}).",
                                maxFavorableTicks, BreakEvenPlusTicks, entryPrice - (BreakEvenPlusTicks * TickSize)));
                        }
                    }

                    double effectiveShortSl = upperSmallSl;
                    if (isInnerBreakEvenActive)
                    {
                        double breakEvenPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice - (BreakEvenPlusTicks * TickSize));
                        effectiveShortSl = Math.Min(upperSmallSl, breakEvenPrice);
                    }

                    if (Close[0] <= lowerBigTp)
                        ExitShort("TP_Dinamico_Lower3.5", "MatrixShort_Inner");
                    else if (Close[0] >= effectiveShortSl)
                        ExitShort(isInnerBreakEvenActive ? "SL_BreakEven_MFE" : "SL_Dinamico_Upper1.75", "MatrixShort_Inner");
                    else
                    {
                        SetProfitTarget("MatrixShort_Inner", CalculationMode.Price, lowerBigTp);
                        SetStopLoss("MatrixShort_Inner", CalculationMode.Price, effectiveShortSl, false);
                    }
                }
                else if (currentTradeMode == MatrixTradeMode.Short_ExtremeAbove)
                {
                    // Mejora 5: Si el cruce fue fuera de la banda 3.5 superior, mantenemos el colchón mínimo respecto a la entrada
                    double safeExtremeShortSl = Instrument.MasterInstrument.RoundToTickSize(Math.Max(upperBigSl, entryPrice + (MinSafetySlTicks * TickSize)));

                    if (Close[0] <= midlineShortTp)
                        ExitShort("TP_Dinamico_Midline", "MatrixShort_Extreme");
                    else if (Close[0] >= safeExtremeShortSl)
                        ExitShort("SL_Dinamico_Upper3.5", "MatrixShort_Extreme");
                    else
                    {
                        SetProfitTarget("MatrixShort_Extreme", CalculationMode.Price, midlineShortTp);
                        SetStopLoss("MatrixShort_Extreme", CalculationMode.Price, safeExtremeShortSl, false);
                    }
                }
                else if (currentTradeMode == MatrixTradeMode.Short_Zone1TrendBreak)
                {
                    // v2.0 (Respiro Total Zona 1): Stop Loss anclado permanentemente a la Línea Media (Midline) para darle respiro completo al trade sin sustos de la EMA200
                    double safeZone1ShortSl = Instrument.MasterInstrument.RoundToTickSize(Math.Max(keltnerBig.Midline[0] + slOffset, entryPrice + (MinSafetySlTicks * TickSize)));
                    if (Close[0] >= safeZone1ShortSl)
                        ExitShort("SL_Midline_Zona1", "MatrixShort_Zona1");
                    else if (safeZone1ShortSl > Close[0])
                        SetStopLoss("MatrixShort_Zona1", CalculationMode.Price, safeZone1ShortSl, false);
                }
            }
        }

        // === REPORTE DETALLADO EN EL LOG AL CERRAR OPERACIONES + CONTEO DE PÉRDIDAS PARA ESCUDO ANTI-SANCOCHO ===
        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            // Cuando se cierra un trade (ya sea pasando a Flat o volteándose en un Stop & Reverse), lo reportamos en el Log.
            // Mejora 2 (Caso 2:32 PM): NO borramos currentTradeMode aquí adentro porque durante un Stop & Reverse
            // NinjaTrader pasa momentáneamente por Flat antes de abrir la posición inversa, lo que antes dejaba congelado el TP dinámico.
            if (SystemPerformance.AllTrades.Count > lastReportedTradeCount)
            {
                lastReportedTradeCount = SystemPerformance.AllTrades.Count;
                Trade lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                double pnlCurrency = lastTrade.ProfitCurrency;
                double pnlTicks = lastTrade.ProfitTicks;
                string direction = (lastTrade.Entry.MarketPosition == MarketPosition.Long) ? "LARGO" : "CORTO";
                string resultado = (pnlTicks >= 0) ? "CON GANANCIA" : "CON PÉRDIDA";

                // v1.6 (Fuga #3): Si el trade cerró en pérdida (pnlTicks < 0), activamos el enfriamiento de Stop & Reverse.
                // Apenas se logre un trade ganador (pnlTicks > 0), se reinicia a 0 y el Stop & Reverse vuelve a estar 100% activo.
                if (pnlTicks < 0)
                {
                    consecutiveLosses++;
                }
                else if (pnlTicks > 0)
                {
                    consecutiveLosses = 0;
                }

                Print(Time[0] + string.Format(" - [{0} CERRADO {1}] Señal: {2} -> {3} | Resultado: {4:C2} ({5:F0} ticks) | Racha Pérdidas: {6} | Entrada: {7:F2} -> Salida: {8:F2}",
                    direction, resultado, lastTrade.Entry.Name, lastTrade.Exit.Name, pnlCurrency, pnlTicks, consecutiveLosses, lastTrade.Entry.Price, lastTrade.Exit.Price));
            }
        }

        private int lastReportedTradeCount = 0;

        #region Interfaz de Usuario WPF (Botón de Pánico + Atajo Ctrl+Espacio)
        private void CreateWPFControls()
        {
            chartWindow = Window.GetWindow(ChartControl.Parent) as Chart;
            if (chartWindow == null) return;

            panicButton = new System.Windows.Controls.Button
            {
                Content = "Matrix: PAUSADO",
                Background = Brushes.Red,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(10, 10, 10, 10),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.Hand,
                BorderBrush = Brushes.DarkRed,
                BorderThickness = new Thickness(2)
            };

            panicButton.Click += OnPanicButtonClick;

            System.Windows.Controls.Grid chartGrid = chartWindow.MainTabControl.Parent as System.Windows.Controls.Grid;
            if (chartGrid != null)
            {
                chartGrid.Children.Add(panicButton);
            }

            if (!isSubscribedToKeys)
            {
                chartWindow.PreviewKeyDown += OnChartKeyDown;
                isSubscribedToKeys = true;
            }
        }

        private void RemoveWPFControls()
        {
            if (chartWindow != null)
            {
                if (isSubscribedToKeys)
                {
                    chartWindow.PreviewKeyDown -= OnChartKeyDown;
                    isSubscribedToKeys = false;
                }

                System.Windows.Controls.Grid chartGrid = chartWindow.MainTabControl.Parent as System.Windows.Controls.Grid;
                if (chartGrid != null && panicButton != null && chartGrid.Children.Contains(panicButton))
                {
                    panicButton.Click -= OnPanicButtonClick;
                    chartGrid.Children.Remove(panicButton);
                }
            }
            panicButton = null;
            chartWindow = null;
        }

        private void OnPanicButtonClick(object sender, RoutedEventArgs e)
        {
            ToggleStrategyStatus();
        }

        private void OnChartKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ToggleStrategyStatus();
                e.Handled = true;
            }
        }

        private void ToggleStrategyStatus()
        {
            isStrategyActive = !isStrategyActive;
            if (panicButton != null)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    panicButton.Content = isStrategyActive ? "Matrix: ACTIVO" : "Matrix: PAUSADO";
                    panicButton.Background = isStrategyActive ? Brushes.Green : Brushes.Red;
                    panicButton.BorderBrush = isStrategyActive ? Brushes.DarkGreen : Brushes.DarkRed;
                    Print(Time[0] + (isStrategyActive ? " - [SISTEMA] Estrategia KeltnerMatrix ACTIVADA." : " - [SISTEMA] Estrategia KeltnerMatrix PAUSADA."));
                });
            }

            // BOTÓN DE PÁNICO: Si se pasa a PAUSADO, liquida cualquier posición abierta de inmediato
            if (!isStrategyActive)
            {
                TriggerCustomEvent(o =>
                {
                    if (Position.MarketPosition != MarketPosition.Flat)
                    {
                        if (Position.MarketPosition == MarketPosition.Long) ExitLong("BotonPanico", "");
                        if (Position.MarketPosition == MarketPosition.Short) ExitShort("BotonPanico", "");
                        Print(Time[0] + " - [BOTÓN DE PÁNICO] Posición liquidada de emergencia a mercado.");
                    }
                    currentTradeMode = MatrixTradeMode.None;
                }, null);
            }
        }
        #endregion

        #region Propiedades (Parámetros de NinjaTrader)
        [NinjaScriptProperty]
        [Display(Name = "Versión", Order = 1, GroupName = "0. Información")]
        public string Version { get; set; }

        // --- HORARIOS ---
        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Hora Inicio RTH (HHMMSS)", Order = 1, GroupName = "1. Horarios Operativos")]
        public int StartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Hora Límite Nuevas Entradas", Order = 2, GroupName = "1. Horarios Operativos")]
        public int StopEntriesTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Hora Cierre Forzoso RTH", Order = 3, GroupName = "1. Horarios Operativos")]
        public int ForceCloseTime { get; set; }

        // --- INDICADORES ---
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Periodo TEMA Rápido", Order = 1, GroupName = "2. Indicadores (Keltner Matrix)")]
        public int TemaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Periodo EMA Lenta", Order = 2, GroupName = "2. Indicadores (Keltner Matrix)")]
        public int EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Periodo Canales Keltner", Order = 3, GroupName = "2. Indicadores (Keltner Matrix)")]
        public int KeltnerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "Multiplicador Keltner Grande", Order = 4, GroupName = "2. Indicadores (Keltner Matrix)")]
        public double KeltnerBigMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "Multiplicador Keltner Pequeño", Order = 5, GroupName = "2. Indicadores (Keltner Matrix)")]
        public double KeltnerSmallMult { get; set; }

        // --- FILTROS Y SEGURIDAD ---
        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "Ticks Mínimos Cruce Real (Permite Decimales)", Order = 1, GroupName = "3. Filtros y Seguridad")]
        public double MinCrossTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exigir Pendiente a Favor", Order = 2, GroupName = "3. Filtros y Seguridad")]
        public bool UseSlopeFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exigir Cierre Vela del Lado Cruce", Order = 3, GroupName = "3. Filtros y Seguridad")]
        public bool RequireCloseOnCrossSide { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Offset Respiro SL en Banda (Ticks)", Order = 4, GroupName = "3. Filtros y Seguridad")]
        public int SlOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Offset Asegurar TP en Banda (Ticks)", Order = 5, GroupName = "3. Filtros y Seguridad")]
        public int TpOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Colchón Mínimo SL Inicial/Extremo (Ticks)", Order = 6, GroupName = "3. Filtros y Seguridad")]
        public int MinSafetySlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(4, int.MaxValue)]
        [Display(Name = "Espacio Mínimo a Banda 3.5 en Cascada (Ticks)", Order = 7, GroupName = "3. Filtros y Seguridad")]
        public int MinRoomToOuterTpTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Escudo MFE BreakEven (Banda 1.75)", Order = 8, GroupName = "3. Filtros y Seguridad")]
        public bool UseMfeBreakEven { get; set; }

        [NinjaScriptProperty]
        [Range(4, int.MaxValue)]
        [Display(Name = "Ticks Mínimos Ganancia para MFE BreakEven", Order = 9, GroupName = "3. Filtros y Seguridad")]
        public int MfeTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Ticks Asegurados en BreakEven (+Ticks)", Order = 10, GroupName = "3. Filtros y Seguridad")]
        public int BreakEvenPlusTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Stop & Reverse en Cruce", Order = 11, GroupName = "3. Filtros y Seguridad")]
        public bool UseStopAndReverse { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Escudo Anti-Sancocho (S&R Adaptativo)", Order = 12, GroupName = "3. Filtros y Seguridad")]
        public bool UseAdaptiveSancochoShield { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Francotirador Zona 1 (Tendencia Larga)", Order = 13, GroupName = "3. Filtros y Seguridad")]
        public bool UseZone1TrendBreak { get; set; }

        [NinjaScriptProperty]
        [Range(10, int.MaxValue)]
        [Display(Name = "Barras Sin Cruce para Zona 1 (Ej. 100-200)", Order = 14, GroupName = "3. Filtros y Seguridad")]
        public int MinBarsTrendBreakZona1 { get; set; }

        [NinjaScriptProperty]
        [Range(4, int.MaxValue)]
        [Display(Name = "TP Fijo Francotirador Zona 1 (Ticks)", Order = 15, GroupName = "3. Filtros y Seguridad")]
        public int Zone1FixedTpTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Ruptura Externa Banda 3.5 (Idea Loca)", Order = 16, GroupName = "3. Filtros y Seguridad")]
        public bool UseOuterBandBreakout { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Tolerancia Ticks Fuera de Banda 3.5", Order = 17, GroupName = "3. Filtros y Seguridad")]
        public int MaxOuterBufferTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Marcadores Visuales", Order = 18, GroupName = "3. Filtros y Seguridad")]
        public bool ShowVisualMarkers { get; set; }
        #endregion
    }
}
