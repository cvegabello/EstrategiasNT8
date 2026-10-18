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
            Long_InnerChannel,  // Cruce entre Lower_1.75 y Upper_1.75 -> TP: Upper_3.5, SL: Lower_1.75
            Long_ExtremeBelow,  // Cruce debajo de Lower_1.75          -> TP: Midline,   SL: Lower_3.5
            Short_InnerChannel, // Cruce entre Lower_1.75 y Upper_1.75 -> TP: Lower_3.5, SL: Upper_1.75
            Short_ExtremeAbove  // Cruce arriba de Upper_1.75          -> TP: Midline,   SL: Upper_3.5
        }

        private MatrixTradeMode currentTradeMode = MatrixTradeMode.None;

        // Variables para rastrear el lado previo del TEMA respecto a la EMA 200 y evitar disparos repetidos
        private int lastTemaSide = 0; // 1 = Arriba de EMA200, -1 = Debajo de EMA200, 0 = Indefinido
        private bool crossUpConsumed = false;
        private bool crossDownConsumed = false;

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
                Description                                 = @"Estrategia Keltner Matrix en 610 Ticks para ES/MES. Cruces de TEMA(9) y EMA(200) clasificados por zonas de Doble Canal de Keltner (3.5 y 1.75) con TP y SL dinámicos pegados a las bandas.";
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

                // Propiedades generales
                Version                 = "1.4";

                // Horarios RTH (Nueva York EST) - Inicia 09:50 AM por regla transversal
                StartTime               = 95000;  // 09:50 AM EST
                StopEntriesTime         = 154500; // 03:45 PM EST (Dejar de abrir nuevas)
                ForceCloseTime          = 160000; // 04:00 PM EST (Cerrar todo sí o sí)

                // Parámetros de Indicadores
                TemaPeriod              = 9;
                EmaPeriod               = 200;
                KeltnerPeriod           = 52;
                KeltnerBigMult          = 3.5;
                KeltnerSmallMult        = 1.75;

                // Filtros de Seguridad, Offset de Bandas y Stop & Reverse
                MinCrossTicks           = 1;      // Penetración mínima en ticks para confirmar cruce real
                UseSlopeFilter          = true;   // Exigir que el TEMA lleve pendiente a favor
                SlOffsetTicks           = 2;      // Ticks de respiro por fuera de la banda para el Stop Loss
                MinSafetySlTicks        = 8;      // Colchón mínimo de seguridad para el SL (Peligro 1 - Opción A)
                UseStopAndReverse       = true;   // Opción B: Voltear o cerrar posición si ocurre cruce real contrario
                ShowVisualMarkers       = false;  // No dibujar figuras extra en la gráfica por defecto
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
                // Mantenemos rastreado de qué lado está el TEMA fuera de horario para que a las 09:50 AM sepa de dónde viene
                if (tema9[0] <= ema200[0]) { lastTemaSide = -1; crossUpConsumed = false; }
                else if (tema9[0] > ema200[0]) { lastTemaSide = 1; crossDownConsumed = false; }
                return;
            }

            // 3. Si estamos dentro de una posición abierta, actualizamos FIELMENTE el TP y el SL siguiendo sus bandas (+ Offset de SL)
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                UpdateDynamicBandsTargets();
            }
            else
            {
                currentTradeMode = MatrixTradeMode.None;
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

            // Evaluamos cruces cuando estamos Flat, O cuando estamos en la posición contraria y UseStopAndReverse está activo (Opción B)
            bool canEvaluateLongCross  = (Position.MarketPosition == MarketPosition.Flat) || (UseStopAndReverse && Position.MarketPosition == MarketPosition.Short);
            bool canEvaluateShortCross = (Position.MarketPosition == MarketPosition.Flat) || (UseStopAndReverse && Position.MarketPosition == MarketPosition.Long);

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

                // Si ya superó la penetración mínima, marcamos este cruce como consumido para no repetirlo en la misma onda
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
                // ZONA 1: Arriba de la banda exterior superior del canal pequeño (Upper_1.75) -> NO ABRIR LARGO
                if (tema9[0] >= upperSmall)
                {
                    if (isReversalFromShort)
                    {
                        ExitShort("StopReverse_CierreCorto", "");
                        currentTradeMode = MatrixTradeMode.None;
                        Print(Time[0] + string.Format(" - [STOP CRUCE CONTRARIO] Corto cerrado por cruce alcista real, pero NO se abre Largo por estar en Zona Sobrecomprada (TEMA: {0:F2} >= Upper1.75: {1:F2}).", tema9[0], upperSmall));
                    }
                    else
                    {
                        Print(Time[0] + string.Format(" - [LARGO CANCELADO - ZONA SOBRECOMPRADA] Cruce ocurrió arriba de Banda Superior Pequeña (TEMA: {0:F2} >= Upper1.75: {1:F2}).", tema9[0], upperSmall));
                    }
                    if (ShowVisualMarkers)
                        Draw.Text(this, "CancelLong_" + CurrentBar, "✖ Zona Alta", 0, High[0] + (4 * TickSize), Brushes.OrangeRed);
                    return;
                }
                // ZONA 2 y 3: Entre Lower_1.75 y Upper_1.75 (Ya sea arriba o debajo de Midline)
                // -> TP en Upper_3.5 y SL en Lower_1.75 - Offset
                else if (tema9[0] >= lowerSmall && tema9[0] < upperSmall)
                {
                    string subZona = (tema9[0] >= midline) ? "Entre Media y Superior Pequeña" : "Entre Inferior Pequeña y Media";
                    double rawSlPrice = lowerSmall - slOffset;
                    double rawTpPrice = upperBig;

                    // Peligro 1 (Opción A): Colchón de seguridad mínimo para el SL
                    double minSafeSlPrice = Close[0] - (MinSafetySlTicks * TickSize);
                    double finalSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Min(rawSlPrice, minSafeSlPrice));
                    double finalTpPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Max(rawTpPrice, Close[0] + (4 * TickSize)));

                    currentTradeMode = MatrixTradeMode.Long_InnerChannel;
                    SetStopLoss("MatrixLong_Inner", CalculationMode.Price, finalSlPrice, false);
                    SetProfitTarget("MatrixLong_Inner", CalculationMode.Price, finalTpPrice);
                    EnterLong("MatrixLong_Inner");

                    if (ShowVisualMarkers)
                        Draw.TriangleUp(this, "LongInner_" + CurrentBar, true, 0, Low[0] - (4 * TickSize), Brushes.LimeGreen);

                    string tagAccion = isReversalFromShort ? "STOP & REVERSE -> LARGO ABIERTO" : "LARGO ABIERTO";
                    Print(Time[0] + string.Format(" - [{0} - CANAL INTERNO ({1})] Entrada @ ~{2:F2} | TP Dinámico (Upper 3.5): {3:F2} | SL Dinámico (Lower 1.75 - {4}tks): {5:F2}",
                        tagAccion, subZona, Close[0], finalTpPrice, SlOffsetTicks, finalSlPrice));
                }
                // ZONA 4: Debajo de la banda inferior del canal pequeño (Lower_1.75)
                // -> TP en Banda Media (Midline) y SL en Banda Inferior Grande (Lower_3.5) - Offset
                else if (tema9[0] < lowerSmall)
                {
                    double rawSlPrice = lowerBig - slOffset;
                    double rawTpPrice = midline;

                    // Validamos que todavía exista espacio hacia la Midline
                    if (Close[0] >= midline - (2 * TickSize))
                    {
                        if (isReversalFromShort)
                        {
                            ExitShort("StopReverse_CierreCorto", "");
                            currentTradeMode = MatrixTradeMode.None;
                        }
                        Print(Time[0] + " - [LARGO CANCELADO - SIN ESPACIO A MIDLINE] El TEMA cruzó debajo de Lower 1.75 pero la vela cerró ya pegada a la Línea Media.");
                        return;
                    }

                    // Peligro 1 (Opción A): Colchón de seguridad mínimo para el SL
                    double minSafeSlPrice = Close[0] - (MinSafetySlTicks * TickSize);
                    double finalSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Min(rawSlPrice, minSafeSlPrice));
                    double finalTpPrice = Instrument.MasterInstrument.RoundToTickSize(rawTpPrice);

                    currentTradeMode = MatrixTradeMode.Long_ExtremeBelow;
                    SetStopLoss("MatrixLong_Extreme", CalculationMode.Price, finalSlPrice, false);
                    SetProfitTarget("MatrixLong_Extreme", CalculationMode.Price, finalTpPrice);
                    EnterLong("MatrixLong_Extreme");

                    if (ShowVisualMarkers)
                        Draw.TriangleUp(this, "LongExt_" + CurrentBar, true, 0, Low[0] - (4 * TickSize), Brushes.Cyan);

                    string tagAccion = isReversalFromShort ? "STOP & REVERSE -> LARGO ABIERTO" : "LARGO ABIERTO";
                    Print(Time[0] + string.Format(" - [{0} - ZONA EXTREMA INFERIOR] Entrada @ ~{1:F2} | TP Dinámico (Midline): {2:F2} | SL Dinámico (Lower 3.5 - {3}tks): {4:F2}",
                        tagAccion, Close[0], finalTpPrice, SlOffsetTicks, finalSlPrice));
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
                // ZONA 1 ESPEJO: Debajo de la banda exterior inferior del canal pequeño (Lower_1.75) -> NO ABRIR CORTO
                if (tema9[0] <= lowerSmall)
                {
                    if (isReversalFromLong)
                    {
                        ExitLong("StopReverse_CierreLargo", "");
                        currentTradeMode = MatrixTradeMode.None;
                        Print(Time[0] + string.Format(" - [STOP CRUCE CONTRARIO] Largo cerrado por cruce bajista real, pero NO se abre Corto por estar en Zona Sobrevendida (TEMA: {0:F2} <= Lower1.75: {1:F2}).", tema9[0], lowerSmall));
                    }
                    else
                    {
                        Print(Time[0] + string.Format(" - [CORTO CANCELADO - ZONA SOBREVENDIDA] Cruce ocurrió debajo de Banda Inferior Pequeña (TEMA: {0:F2} <= Lower1.75: {1:F2}).", tema9[0], lowerSmall));
                    }
                    if (ShowVisualMarkers)
                        Draw.Text(this, "CancelShort_" + CurrentBar, "✖ Zona Baja", 0, Low[0] - (4 * TickSize), Brushes.OrangeRed);
                    return;
                }
                // ZONA 2 y 3 ESPEJO: Entre Lower_1.75 y Upper_1.75
                // -> TP en Lower_3.5 y SL en Upper_1.75 + Offset
                else if (tema9[0] > lowerSmall && tema9[0] <= upperSmall)
                {
                    string subZona = (tema9[0] <= midline) ? "Entre Inferior Pequeña y Media" : "Entre Media y Superior Pequeña";
                    double rawSlPrice = upperSmall + slOffset;
                    double rawTpPrice = lowerBig;

                    // Peligro 1 (Opción A): Colchón de seguridad mínimo para el SL
                    double minSafeSlPrice = Close[0] + (MinSafetySlTicks * TickSize);
                    double finalSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Max(rawSlPrice, minSafeSlPrice));
                    double finalTpPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Min(rawTpPrice, Close[0] - (4 * TickSize)));

                    currentTradeMode = MatrixTradeMode.Short_InnerChannel;
                    SetStopLoss("MatrixShort_Inner", CalculationMode.Price, finalSlPrice, false);
                    SetProfitTarget("MatrixShort_Inner", CalculationMode.Price, finalTpPrice);
                    EnterShort("MatrixShort_Inner");

                    if (ShowVisualMarkers)
                        Draw.TriangleDown(this, "ShortInner_" + CurrentBar, true, 0, High[0] + (4 * TickSize), Brushes.Magenta);

                    string tagAccion = isReversalFromLong ? "STOP & REVERSE -> CORTO ABIERTO" : "CORTO ABIERTO";
                    Print(Time[0] + string.Format(" - [{0} - CANAL INTERNO ({1})] Entrada @ ~{2:F2} | TP Dinámico (Lower 3.5): {3:F2} | SL Dinámico (Upper 1.75 + {4}tks): {5:F2}",
                        tagAccion, subZona, Close[0], finalTpPrice, SlOffsetTicks, finalSlPrice));
                }
                // ZONA 4 ESPEJO: Arriba de la banda superior del canal pequeño (Upper_1.75)
                // -> TP en Banda Media (Midline) y SL en Banda Superior Grande (Upper_3.5) + Offset
                else if (tema9[0] > upperSmall)
                {
                    double rawSlPrice = upperBig + slOffset;
                    double rawTpPrice = midline;

                    // Validamos que todavía exista espacio hacia la Midline
                    if (Close[0] <= midline + (2 * TickSize))
                    {
                        if (isReversalFromLong)
                        {
                            ExitLong("StopReverse_CierreLargo", "");
                            currentTradeMode = MatrixTradeMode.None;
                        }
                        Print(Time[0] + " - [CORTO CANCELADO - SIN ESPACIO A MIDLINE] El TEMA cruzó arriba de Upper 1.75 pero la vela cerró ya pegada a la Línea Media.");
                        return;
                    }

                    // Peligro 1 (Opción A): Colchón de seguridad mínimo para el SL
                    double minSafeSlPrice = Close[0] + (MinSafetySlTicks * TickSize);
                    double finalSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Max(rawSlPrice, minSafeSlPrice));
                    double finalTpPrice = Instrument.MasterInstrument.RoundToTickSize(rawTpPrice);

                    currentTradeMode = MatrixTradeMode.Short_ExtremeAbove;
                    SetStopLoss("MatrixShort_Extreme", CalculationMode.Price, finalSlPrice, false);
                    SetProfitTarget("MatrixShort_Extreme", CalculationMode.Price, finalTpPrice);
                    EnterShort("MatrixShort_Extreme");

                    if (ShowVisualMarkers)
                        Draw.TriangleDown(this, "ShortExt_" + CurrentBar, true, 0, High[0] + (4 * TickSize), Brushes.Orange);

                    string tagAccion = isReversalFromLong ? "STOP & REVERSE -> CORTO ABIERTO" : "CORTO ABIERTO";
                    Print(Time[0] + string.Format(" - [{0} - ZONA EXTREMA SUPERIOR] Entrada @ ~{1:F2} | TP Dinámico (Midline): {2:F2} | SL Dinámico (Upper 3.5 + {3}tks): {4:F2}",
                        tagAccion, Close[0], finalTpPrice, SlOffsetTicks, finalSlPrice));
                }
            }
        }

        // === ACTUALIZACIÓN FIEL DE BANDAS DINÁMICAS (PELIGRO 3 + OFFSET DE RESPIRO EN SL) ===
        // En cada barra cerrada, mueve el TP a su banda respectiva y el SL a su banda respectiva +/- SlOffsetTicks
        private void UpdateDynamicBandsTargets()
        {
            double slOffset     = SlOffsetTicks * TickSize;
            double upperBigTp   = Instrument.MasterInstrument.RoundToTickSize(keltnerBig.Upper[0]);
            double lowerBigTp   = Instrument.MasterInstrument.RoundToTickSize(keltnerBig.Lower[0]);
            double midlineTp    = Instrument.MasterInstrument.RoundToTickSize(keltnerBig.Midline[0]);

            double upperBigSl   = Instrument.MasterInstrument.RoundToTickSize(keltnerBig.Upper[0] + slOffset);
            double lowerBigSl   = Instrument.MasterInstrument.RoundToTickSize(keltnerBig.Lower[0] - slOffset);
            double upperSmallSl = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Upper[0] + slOffset);
            double lowerSmallSl = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Lower[0] - slOffset);

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (currentTradeMode == MatrixTradeMode.Long_InnerChannel)
                {
                    // Sigue fielmente a Upper_3.5 (TP) y Lower_1.75 - Offset (SL)
                    if (Close[0] >= upperBigTp)
                        ExitLong("TP_Dinamico_Upper3.5", "MatrixLong_Inner");
                    else if (Close[0] <= lowerSmallSl)
                        ExitLong("SL_Dinamico_Lower1.75", "MatrixLong_Inner");
                    else
                    {
                        SetProfitTarget("MatrixLong_Inner", CalculationMode.Price, upperBigTp);
                        SetStopLoss("MatrixLong_Inner", CalculationMode.Price, lowerSmallSl, false);
                    }
                }
                else if (currentTradeMode == MatrixTradeMode.Long_ExtremeBelow)
                {
                    // Sigue fielmente a Midline (TP) y Lower_3.5 - Offset (SL)
                    if (Close[0] >= midlineTp)
                        ExitLong("TP_Dinamico_Midline", "MatrixLong_Extreme");
                    else if (Close[0] <= lowerBigSl)
                        ExitLong("SL_Dinamico_Lower3.5", "MatrixLong_Extreme");
                    else
                    {
                        SetProfitTarget("MatrixLong_Extreme", CalculationMode.Price, midlineTp);
                        SetStopLoss("MatrixLong_Extreme", CalculationMode.Price, lowerBigSl, false);
                    }
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (currentTradeMode == MatrixTradeMode.Short_InnerChannel)
                {
                    // Sigue fielmente a Lower_3.5 (TP) y Upper_1.75 + Offset (SL)
                    if (Close[0] <= lowerBigTp)
                        ExitShort("TP_Dinamico_Lower3.5", "MatrixShort_Inner");
                    else if (Close[0] >= upperSmallSl)
                        ExitShort("SL_Dinamico_Upper1.75", "MatrixShort_Inner");
                    else
                    {
                        SetProfitTarget("MatrixShort_Inner", CalculationMode.Price, lowerBigTp);
                        SetStopLoss("MatrixShort_Inner", CalculationMode.Price, upperSmallSl, false);
                    }
                }
                else if (currentTradeMode == MatrixTradeMode.Short_ExtremeAbove)
                {
                    // Sigue fielmente a Midline (TP) y Upper_3.5 + Offset (SL)
                    if (Close[0] <= midlineTp)
                        ExitShort("TP_Dinamico_Midline", "MatrixShort_Extreme");
                    else if (Close[0] >= upperBigSl)
                        ExitShort("SL_Dinamico_Upper3.5", "MatrixShort_Extreme");
                    else
                    {
                        SetProfitTarget("MatrixShort_Extreme", CalculationMode.Price, midlineTp);
                        SetStopLoss("MatrixShort_Extreme", CalculationMode.Price, upperBigSl, false);
                    }
                }
            }
        }

        // === REPORTE DETALLADO EN EL LOG AL CERRAR OPERACIONES ===
        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            // Cuando la posición pasa a Flat (o cambia en un Stop & Reverse), reportamos el último trade cerrado si aumentó el conteo
            if (SystemPerformance.AllTrades.Count > lastReportedTradeCount)
            {
                lastReportedTradeCount = SystemPerformance.AllTrades.Count;
                Trade lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                double pnlCurrency = lastTrade.ProfitCurrency;
                double pnlTicks = lastTrade.ProfitTicks;
                string direction = (lastTrade.Entry.MarketPosition == MarketPosition.Long) ? "LARGO" : "CORTO";
                string resultado = (pnlCurrency >= 0) ? "CON GANANCIA" : "CON PÉRDIDA";

                Print(Time[0] + string.Format(" - [{0} CERRADO {1}] Señal: {2} -> {3} | Resultado: {4:C2} ({5:F0} ticks) | Entrada: {6:F2} -> Salida: {7:F2}",
                    direction, resultado, lastTrade.Entry.Name, lastTrade.Exit.Name, pnlCurrency, pnlTicks, lastTrade.Entry.Price, lastTrade.Exit.Price));
            }

            if (marketPosition == MarketPosition.Flat)
            {
                currentTradeMode = MatrixTradeMode.None;
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
        [Range(0, int.MaxValue)]
        [Display(Name = "Ticks Mínimos Cruce Real", Order = 1, GroupName = "3. Filtros y Seguridad")]
        public int MinCrossTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exigir Pendiente a Favor", Order = 2, GroupName = "3. Filtros y Seguridad")]
        public bool UseSlopeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Offset Respiro SL en Banda (Ticks)", Order = 3, GroupName = "3. Filtros y Seguridad")]
        public int SlOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Colchón Mínimo SL Inicial (Ticks)", Order = 4, GroupName = "3. Filtros y Seguridad")]
        public int MinSafetySlTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Stop & Reverse en Cruce", Order = 5, GroupName = "3. Filtros y Seguridad")]
        public bool UseStopAndReverse { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Marcadores Visuales", Order = 6, GroupName = "3. Filtros y Seguridad")]
        public bool ShowVisualMarkers { get; set; }
        #endregion
    }
}
