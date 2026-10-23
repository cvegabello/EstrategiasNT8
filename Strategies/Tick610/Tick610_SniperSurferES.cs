#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// =========================================================================================================
// ESTRATEGIA: Tick610_SniperSurferES (Versión 1.0)
// AUTOR: Mentor de Trading & Desarrollador Senior de C#
// INSTRUMENTO: Futuros S&P 500 (ES / MES) en Barras Transaccionales de 610 Ticks (TickSize = 0.25)
// HORARIO OPERATIVO: RTH estricto (09:50 AM - 04:00 PM EST / Nueva York)
//
// FILOSOFÍA DE DISEÑO:
// Esta estrategia elimina el 100% del ruido y serrucho de los canales internos.
// Opera ÚNICAMENTE las 2 configuraciones de máxima probabilidad y mayor ratio riesgo/beneficio:
//
// 1. FRANCOTIRADOR ZONA 1 (SniperZona1):
//    - Captura el primer quiebre violento cuando una tendencia madura (>= 100 barras sin cruzar la EMA 200)
//      perfora la banda opuesta del canal de Keltner.
//    - TP Fijo rápido de 12 ticks ($150.00) y Stop Loss de respaldo anclado a la Línea Media (Midline).
//
// 2. MATRIX SURFER CON CANDADO $180 (Surfer):
//    - Captura las super-expansiones institucionales cuando una vela completa despega fuera de la banda 3.5
//      y las pendientes del abanico confirman aceleración de volatilidad.
//    - Salida Magistral por Fin de Ciclo cuando la EMA 200 toca la banda exterior opuesta.
//    - CANDADO $180: Apenas la ganancia flotante alcanza $180, el Stop Loss da un salto hacia adelante
//      a la banda pequeña cercana, blindando el trade y eliminando cualquier posibilidad de susto.
// =========================================================================================================

namespace NinjaTrader.NinjaScript.Strategies
{
    public class Tick610_SniperSurferES : Strategy
    {
        #region Variables Internas
        // Modos de operación exclusivos
        private enum SniperSurferMode
        {
            None,
            Long_Zona1,     // Francotirador Zona 1 Alcista
            Short_Zona1,    // Francotirador Zona 1 Bajista
            Long_Surfer,    // MatrixSurfer Alcista (Expansión 3.5)
            Short_Surfer    // MatrixSurfer Bajista (Expansión 3.5)
        }

        private SniperSurferMode currentMode = SniperSurferMode.None;

        // Rastreo de cruces y acumulación de barras para Francotirador Zona 1
        private int lastTemaSide = 0; // 1 = Arriba de EMA200, -1 = Debajo de EMA200, 0 = Indefinido
        private bool crossUpConsumed = false;
        private bool crossDownConsumed = false;
        private int barsBelowEmaWithoutCross = 0; // Conteo de barras sin cruce alcista real (Zona 1 Largo)
        private int barsAboveEmaWithoutCross = 0; // Conteo de barras sin cruce bajista real (Zona 1 Corto)

        // Candado de Seguridad de $180 para el MatrixSurfer
        private bool isSurferLockActive = false;
        private const double SurferLockTriggerDollars = 180.0; // Umbral en dólares para activar el candado

        // Indicadores internos (en memoria, sin dibujar por regla AGENTS.md)
        private TEMA tema9;
        private EMA ema200;
        private KeltnerChannel keltnerBig;   // Canal Exterior (52, 3.5)
        private KeltnerChannel keltnerSmall; // Canal Interior (52, 1.75)

        // Interfaz de Control WPF (Verdadero Botón de Pánico + Atajo Ctrl + Espacio)
        private System.Windows.Controls.Button panicButton;
        private bool isStrategyActive = false; // Siempre inicia en PAUSADO en tiempo real
        private Chart chartWindow;
        private bool isSubscribedToKeys = false;
        private int lastReportedTradeCount = 0;
        #endregion

        #region Inicialización y Configuración
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Estrategia de Élite Sniper & Surfer para ES/MES en 610 Ticks. Solo opera Rupturas Maduras de Zona 1 (100 barras) y Super-Expansiones MatrixSurfer con Candado de $180.";
                Name                                        = "Tick610_SniperSurferES";
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
                RealtimeErrorHandling                       = RealtimeErrorHandling.IgnoreAllErrors; // Resiliencia total: jamás mata la estrategia por descalce temporal de orden
                StopTargetHandling                          = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade                         = 200;
                IsInstantiatedOnEachOptimizationIteration   = true;
                IncludeCommission                           = true; // Comisiones incluidas siempre ($4.36 RT)

                // Propiedades generales
                Version                         = "1.1"; // v1.1: Blindaje de seguridad en modificación de SL (buffer 3 ticks e IgnoreAllErrors)

                // Horario RTH estricto (Nueva York EST)
                StartTime                       = 95000;  // 09:50 AM EST
                StopEntriesTime                 = 154500; // 03:45 PM EST
                ForceCloseTime                  = 160000; // 04:00 PM EST

                // Parámetros de Indicadores
                TemaPeriod                      = 9;
                EmaPeriod                       = 200;
                KeltnerPeriod                   = 52;
                KeltnerBigMult                  = 3.5;
                KeltnerSmallMult                = 1.75;

                // Parámetros Francotirador Zona 1
                MinCrossTicks                   = 2.3;    // Penetración mínima de cruce real
                MinBarsTrendBreakZona1          = 100;    // Barras mínimas de tendencia madura previa
                Zone1FixedTpTicks               = 12;     // TP Fijo de Francotirador (12 ticks = $150.00)
                MaxOuterBufferTicks             = 12;     // Tolerancia de ruptura externa fuera de banda 3.5 (12 ticks = 3.00 pts)
                SlOffsetTicks                   = 2;      // Offset de respiro en ticks para el Stop Loss
                MinSafetySlTicks                = 8;      // Colchón mínimo de seguridad para el SL inicial
                ShowVisualMarkers               = false;  // Marcadores en gráfica
            }
            else if (State == State.DataLoaded)
            {
                // Instanciamos los indicadores en memoria limpia (sin AddChartIndicator)
                tema9        = TEMA(Close, TemaPeriod);
                ema200       = EMA(Close, EmaPeriod);
                keltnerBig   = KeltnerChannel(Close, KeltnerBigMult, KeltnerPeriod);
                keltnerSmall = KeltnerChannel(Close, KeltnerSmallMult, KeltnerPeriod);
            }
            else if (State == State.Historical)
            {
                // En Strategy Analyzer inicia activo para simulación; en Gráfica Sim/Real inicia PAUSADO
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
                    ChartControl.Dispatcher.InvokeAsync(() => DisposeWPFControls());
                }
            }
        }
        #endregion

        #region Lógica Principal (OnBarUpdate)
        protected override void OnBarUpdate()
        {
            // Validamos barras suficientes y datos históricos cargados
            if (CurrentBar < BarsRequiredToTrade)
                return;

            // Inicializamos lastTemaSide la primera vez que tenemos datos válidos
            if (lastTemaSide == 0)
            {
                lastTemaSide = (tema9[1] <= ema200[1]) ? -1 : 1;
            }

            // === CONTADOR CONTINUO DE BARRAS SIN CRUCE REAL (ZONA 1) ===
            // Guardamos cuántas barras acumuladas traíamos ANTES de evaluar la barra actual
            int priorBarsBelowEma = barsBelowEmaWithoutCross;
            int priorBarsAboveEma = barsAboveEmaWithoutCross;

            double currentUpTicks   = (tema9[0] - ema200[0]) / TickSize;
            double currentDownTicks = (ema200[0] - tema9[0]) / TickSize;

            if (currentUpTicks < MinCrossTicks)
                barsBelowEmaWithoutCross++;
            else if (Close[0] > ema200[0])
                barsBelowEmaWithoutCross = 0;

            if (currentDownTicks < MinCrossTicks)
                barsAboveEmaWithoutCross++;
            else if (Close[0] < ema200[0])
                barsAboveEmaWithoutCross = 0;

            // 1. Control de Horario RTH Estricto
            int currentTime = ToTime(Time[0]);

            // Cierre forzoso de fin de sesión RTH (04:00 PM EST)
            if (currentTime >= ForceCloseTime)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong("ForceClose_RTH_FinDia", "");
                    else
                        ExitShort("ForceClose_RTH_FinDia", "");

                    currentMode = SniperSurferMode.None;
                    isSurferLockActive = false;
                    Print(Time[0] + " - [CIERRE FORZOSO RTH] Posición liquidada a las 04:00 PM EST.");
                }
                return;
            }

            // Filtro de inicio de jornada (09:50 AM EST)
            if (currentTime < StartTime)
            {
                if (tema9[0] > ema200[0]) lastTemaSide = 1;
                else if (tema9[0] < ema200[0]) lastTemaSide = -1;
                return;
            }

            // 2. Seguimiento Dinámico de Posiciones Abiertas
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                UpdateDynamicExits();
            }
            else
            {
                currentMode = SniperSurferMode.None;
                isSurferLockActive = false;
            }

            // 4. Si el Botón de Pánico está PAUSADO o pasó la hora de entrada (03:45 PM), no abrimos trades
            if (!isStrategyActive || currentTime > StopEntriesTime)
            {
                if (tema9[0] <= ema200[0]) { lastTemaSide = -1; crossUpConsumed = false; }
                else if (tema9[0] > ema200[0]) { lastTemaSide = 1; crossDownConsumed = false; }
                return;
            }

            // Rastrear estado de cruces de TEMA respecto a EMA200
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

            // Variables de niveles de bandas
            double upperBig   = keltnerBig.Upper[0];
            double lowerBig   = keltnerBig.Lower[0];
            double midline    = keltnerBig.Midline[0];
            double upperSmall = keltnerSmall.Upper[0];
            double lowerSmall = keltnerSmall.Lower[0];
            double slOffset   = SlOffsetTicks * TickSize;

            // =========================================================================
            // MÓDULO 1: MATRIX SURFER (CAZADOR DE SUPER-EXPANSIONES 3.5 CON CANDADO $180)
            // =========================================================================
            if (Position.MarketPosition == MarketPosition.Flat && CurrentBar >= 2)
            {
                // Pendientes del abanico (barra actual vs 2 barras atrás)
                double emaSlope     = ema200[0] - ema200[2];
                double midlineSlope = midline - keltnerBig.Midline[2];
                double upperSlope   = upperBig - keltnerBig.Upper[2];
                double lowerSlope   = lowerBig - keltnerBig.Lower[2];

                // CASO SURFER LARGO:
                // 1. Toda la vela flotando libre en el aire por encima de la banda 3.5
                // 2. EMA 200 en zona institucional (entre Midline y Lower 1.75)
                // 3. Pendientes alineadas: Midline y Upper subiendo con fuerza, EMA200 más suave
                bool isVelaFlotandoLargo = (Low[0] > upperBig);
                bool isEmaZonaLargo      = (ema200[0] <= midline && ema200[0] >= lowerSmall);
                bool isPendientesLargo   = (midlineSlope > 0 && upperSlope > 0 && emaSlope < midlineSlope);

                if (isVelaFlotandoLargo && isEmaZonaLargo && isPendientesLargo)
                {
                    // SL Inicial con máximo respiro: banda exterior inferior del canal pequeño (Lower 1.75 -1tk)
                    double rawSlPrice     = lowerSmall - (1 * TickSize);
                    double minSafeSlPrice = Close[0] - (MinSafetySlTicks * TickSize);
                    double finalSlPrice   = Instrument.MasterInstrument.RoundToTickSize(Math.Min(rawSlPrice, minSafeSlPrice));

                    currentMode = SniperSurferMode.Long_Surfer;
                    isSurferLockActive = false;
                    SetStopLoss("SniperSurfer_Long", CalculationMode.Price, finalSlPrice, false);
                    EnterLong("SniperSurfer_Long");

                    if (ShowVisualMarkers)
                        Draw.TriangleUp(this, "SurferL_" + CurrentBar, true, 0, Low[0] - (6 * TickSize), Brushes.DodgerBlue);

                    Print(Time[0] + string.Format(" - [SURFER -> LARGO ABIERTO] Expansión 3.5 | Entrada @ ~{0:F2} | SL Inicial Lower 1.75 -1tk: {1:F2} | Candado $180 armado",
                        Close[0], finalSlPrice));
                    return;
                }

                // CASO SURFER CORTO (ESPEJO INVERSO):
                // 1. Toda la vela flotando libre por debajo de la banda 3.5
                // 2. EMA 200 entre Midline y Upper 1.75
                // 3. Pendientes: Midline y Lower cayendo con fuerza, EMA200 más suave
                bool isVelaFlotandoCorto = (High[0] < lowerBig);
                bool isEmaZonaCorto      = (ema200[0] >= midline && ema200[0] <= upperSmall);
                bool isPendientesCorto   = (midlineSlope < 0 && lowerSlope < 0 && emaSlope > midlineSlope);

                if (isVelaFlotandoCorto && isEmaZonaCorto && isPendientesCorto)
                {
                    // SL Inicial con máximo respiro: banda exterior superior del canal pequeño (Upper 1.75 +1tk)
                    double rawSlPrice     = upperSmall + (1 * TickSize);
                    double minSafeSlPrice = Close[0] + (MinSafetySlTicks * TickSize);
                    double finalSlPrice   = Instrument.MasterInstrument.RoundToTickSize(Math.Max(rawSlPrice, minSafeSlPrice));

                    currentMode = SniperSurferMode.Short_Surfer;
                    isSurferLockActive = false;
                    SetStopLoss("SniperSurfer_Short", CalculationMode.Price, finalSlPrice, false);
                    EnterShort("SniperSurfer_Short");

                    if (ShowVisualMarkers)
                        Draw.TriangleDown(this, "SurferS_" + CurrentBar, true, 0, High[0] + (6 * TickSize), Brushes.DeepSkyBlue);

                    Print(Time[0] + string.Format(" - [SURFER -> CORTO ABIERTO] Expansión 3.5 | Entrada @ ~{0:F2} | SL Inicial Upper 1.75 +1tk: {1:F2} | Candado $180 armado",
                        Close[0], finalSlPrice));
                    return;
                }
            }

            // =========================================================================
            // MÓDULO 2: FRANCOTIRADOR ZONA 1 (RUPTURA DE TENDENCIA MADURA >= 100 BARRAS)
            // =========================================================================
            // CASO A: Cruce Alcista tras tendencia bajista madura
            if (Position.MarketPosition == MarketPosition.Flat && lastTemaSide == -1 && !crossUpConsumed && tema9[0] > ema200[0])
            {
                double crossDistanceTicks = (tema9[0] - ema200[0]) / TickSize;
                double slopeTicks = (tema9[0] - tema9[1]) / TickSize;

                if (crossDistanceTicks >= MinCrossTicks && Close[0] > ema200[0] && slopeTicks > 0)
                {
                    crossUpConsumed = true;
                    lastTemaSide = 1;

                    // Exclusivo de Zona 1: TEMA perfora por encima de Upper 1.75 tras >= MinBarsTrendBreakZona1 barras
                    if (tema9[0] >= upperSmall)
                    {
                        bool isPriceAllowed = (Close[0] <= upperBig + (MaxOuterBufferTicks * TickSize));
                        if (priorBarsBelowEma >= MinBarsTrendBreakZona1 && isPriceAllowed)
                        {
                            double rawSlPrice = midline - slOffset;
                            double minSafeSlPrice = Close[0] - (MinSafetySlTicks * TickSize);
                            double finalSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Min(rawSlPrice, minSafeSlPrice));
                            double finalTpPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0] + (Zone1FixedTpTicks * TickSize));

                            currentMode = SniperSurferMode.Long_Zona1;
                            isSurferLockActive = false;
                            SetStopLoss("SniperZona1_Long", CalculationMode.Price, finalSlPrice, false);
                            SetProfitTarget("SniperZona1_Long", CalculationMode.Price, finalTpPrice);
                            EnterLong("SniperZona1_Long");

                            if (ShowVisualMarkers)
                                Draw.TriangleUp(this, "SniperZ1L_" + CurrentBar, true, 0, Low[0] - (4 * TickSize), Brushes.Gold);

                            Print(Time[0] + string.Format(" - [FRANCOTIRADOR ZONA 1 -> LARGO ABIERTO] Ruptura tras {0} barras bajo EMA200 | Entrada @ ~{1:F2} | TP Fijo (+{2}tks): {3:F2} | SL Midline: {4:F2}",
                                priorBarsBelowEma, Close[0], Zone1FixedTpTicks, finalTpPrice, finalSlPrice));
                            return;
                        }
                    }
                }
            }
            // CASO B: Cruce Bajista tras tendencia alcista madura
            else if (Position.MarketPosition == MarketPosition.Flat && lastTemaSide == 1 && !crossDownConsumed && tema9[0] < ema200[0])
            {
                double crossDistanceTicks = (ema200[0] - tema9[0]) / TickSize;
                double slopeTicks = (tema9[1] - tema9[0]) / TickSize;

                if (crossDistanceTicks >= MinCrossTicks && Close[0] < ema200[0] && slopeTicks > 0)
                {
                    crossDownConsumed = true;
                    lastTemaSide = -1;

                    // Exclusivo de Zona 1: TEMA perfora por debajo de Lower 1.75 tras >= MinBarsTrendBreakZona1 barras
                    if (tema9[0] <= lowerSmall)
                    {
                        bool isPriceAllowed = (Close[0] >= lowerBig - (MaxOuterBufferTicks * TickSize));
                        if (priorBarsAboveEma >= MinBarsTrendBreakZona1 && isPriceAllowed)
                        {
                            double rawSlPrice = midline + slOffset;
                            double minSafeSlPrice = Close[0] + (MinSafetySlTicks * TickSize);
                            double finalSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Max(rawSlPrice, minSafeSlPrice));
                            double finalTpPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0] - (Zone1FixedTpTicks * TickSize));

                            currentMode = SniperSurferMode.Short_Zona1;
                            isSurferLockActive = false;
                            SetStopLoss("SniperZona1_Short", CalculationMode.Price, finalSlPrice, false);
                            SetProfitTarget("SniperZona1_Short", CalculationMode.Price, finalTpPrice);
                            EnterShort("SniperZona1_Short");

                            if (ShowVisualMarkers)
                                Draw.TriangleDown(this, "SniperZ1S_" + CurrentBar, true, 0, High[0] + (4 * TickSize), Brushes.Gold);

                            Print(Time[0] + string.Format(" - [FRANCOTIRADOR ZONA 1 -> CORTO ABIERTO] Ruptura tras {0} barras sobre EMA200 | Entrada @ ~{1:F2} | TP Fijo (-{2}tks): {3:F2} | SL Midline: {4:F2}",
                                priorBarsAboveEma, Close[0], Zone1FixedTpTicks, finalTpPrice, finalSlPrice));
                            return;
                        }
                    }
                }
            }
        }
        #endregion

        #region Salidas Dinámicas y Candado de Seguridad
        private void UpdateDynamicExits()
        {
            double slOffset   = SlOffsetTicks * TickSize;
            double entryPrice = Position.AveragePrice;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (currentMode == SniperSurferMode.Long_Zona1)
                {
                    // SL anclado a la Línea Media con colchón
                    double safeZone1Sl = Instrument.MasterInstrument.RoundToTickSize(Math.Min(keltnerBig.Midline[0] - slOffset, entryPrice - (MinSafetySlTicks * TickSize)));
                    if (Close[0] <= safeZone1Sl + (3 * TickSize))
                        ExitLong("SL_Midline_Zona1", "SniperZona1_Long");
                    else
                    {
                        double finalSl = Math.Min(safeZone1Sl, Close[0] - (3 * TickSize));
                        SetStopLoss("SniperZona1_Long", CalculationMode.Price, finalSl, false);
                    }
                }
                else if (currentMode == SniperSurferMode.Long_Surfer)
                {
                    double currentFavorableTicks = (High[0] - entryPrice) / TickSize;
                    double currentProfitDollars  = currentFavorableTicks * (Instrument.MasterInstrument.PointValue * TickSize);

                    // CANDADO DE $180: Al superar $180 a favor, el SL salta a la banda superior 1.75
                    if (!isSurferLockActive && currentProfitDollars >= SurferLockTriggerDollars)
                    {
                        isSurferLockActive = true;
                        Print(Time[0] + string.Format(" - [SURFER -> CANDADO $180 ACTIVADO - LARGO] Ganancia flotante alcanzó {0:C2} (+{1:F1} tks). Stop Loss blindado en Upper 1.75 -1tk.",
                            currentProfitDollars, currentFavorableTicks));
                    }

                    // Salida por Fin de Ciclo (EMA 200 corta banda inferior 3.5)
                    if (ema200[0] <= keltnerBig.Lower[0])
                    {
                        ExitLong("Exit_Surfer_CicloTerminado", "SniperSurfer_Long");
                        Print(Time[0] + string.Format(" - [SURFER -> LARGO CERRADO POR FIN DE CICLO] EMA200 ({0:F2}) cortó Lower 3.5 ({1:F2}) | Salida @ ~{2:F2}",
                            ema200[0], keltnerBig.Lower[0], Close[0]));
                    }
                    else if (isSurferLockActive)
                    {
                        // SL con Candado: Upper 1.75 - 1 tick
                        double lockSl = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Upper[0] - (1 * TickSize));
                        double safeLockSl = Math.Max(lockSl, entryPrice - (MinSafetySlTicks * TickSize));

                        // Blindaje de seguridad: si el precio está a 3 ticks o menos de la zona de stop, salimos de inmediato a mercado
                        if (Close[0] <= safeLockSl + (3 * TickSize))
                        {
                            ExitLong("SL_Candado_Surfer", "SniperSurfer_Long");
                            Print(Time[0] + string.Format(" - [SURFER -> LARGO PROTEGIDO POR CANDADO] Precio perforó Upper 1.75 -1tk ({0:F2}) | Salida a Mercado @ ~{1:F2}",
                                safeLockSl, Close[0]));
                        }
                        else
                        {
                            // Garantizar que la orden StopMarket esté SIEMPRE al menos 3 ticks por debajo del precio actual para evitar rechazos en caídas rápidas
                            double finalStopPrice = Math.Min(safeLockSl, Close[0] - (3 * TickSize));
                            SetStopLoss("SniperSurfer_Long", CalculationMode.Price, finalStopPrice, false);
                        }
                    }
                    else
                    {
                        // SL Inicial: Lower 1.75 - 1 tick (máximo respiro)
                        double dynamicSl = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Lower[0] - (1 * TickSize));
                        double safeSl = Math.Min(dynamicSl, entryPrice - (MinSafetySlTicks * TickSize));

                        if (Close[0] <= safeSl + (3 * TickSize))
                        {
                            ExitLong("SL_BandaOpuesta_Surfer", "SniperSurfer_Long");
                            Print(Time[0] + string.Format(" - [SURFER -> LARGO CERRADO POR STOP INICIAL] Precio perforó Lower 1.75 -1tk ({0:F2}) | Salida a Mercado @ ~{1:F2}",
                                safeSl, Close[0]));
                        }
                        else
                        {
                            double finalStopPrice = Math.Min(safeSl, Close[0] - (3 * TickSize));
                            SetStopLoss("SniperSurfer_Long", CalculationMode.Price, finalStopPrice, false);
                        }
                    }
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (currentMode == SniperSurferMode.Short_Zona1)
                {
                    // SL anclado a la Línea Media con colchón
                    double safeZone1Sl = Instrument.MasterInstrument.RoundToTickSize(Math.Max(keltnerBig.Midline[0] + slOffset, entryPrice + (MinSafetySlTicks * TickSize)));
                    if (Close[0] >= safeZone1Sl - (3 * TickSize))
                        ExitShort("SL_Midline_Zona1", "SniperZona1_Short");
                    else
                    {
                        double finalSl = Math.Max(safeZone1Sl, Close[0] + (3 * TickSize));
                        SetStopLoss("SniperZona1_Short", CalculationMode.Price, finalSl, false);
                    }
                }
                else if (currentMode == SniperSurferMode.Short_Surfer)
                {
                    double currentFavorableTicks = (entryPrice - Low[0]) / TickSize;
                    double currentProfitDollars  = currentFavorableTicks * (Instrument.MasterInstrument.PointValue * TickSize);

                    // CANDADO DE $180: Al superar $180 a favor, el SL salta a la banda inferior 1.75
                    if (!isSurferLockActive && currentProfitDollars >= SurferLockTriggerDollars)
                    {
                        isSurferLockActive = true;
                        Print(Time[0] + string.Format(" - [SURFER -> CANDADO $180 ACTIVADO - CORTO] Ganancia flotante alcanzó {0:C2} (+{1:F1} tks). Stop Loss blindado en Lower 1.75 +1tk.",
                            currentProfitDollars, currentFavorableTicks));
                    }

                    // Salida por Fin de Ciclo (EMA 200 corta banda superior 3.5)
                    if (ema200[0] >= keltnerBig.Upper[0])
                    {
                        ExitShort("Exit_Surfer_CicloTerminado", "SniperSurfer_Short");
                        Print(Time[0] + string.Format(" - [SURFER -> CORTO CERRADO POR FIN DE CICLO] EMA200 ({0:F2}) cortó Upper 3.5 ({1:F2}) | Salida @ ~{2:F2}",
                            ema200[0], keltnerBig.Upper[0], Close[0]));
                    }
                    else if (isSurferLockActive)
                    {
                        // SL con Candado: Lower 1.75 + 1 tick
                        double lockSl = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Lower[0] + (1 * TickSize));
                        double safeLockSl = Math.Min(lockSl, entryPrice + (MinSafetySlTicks * TickSize));

                        if (Close[0] >= safeLockSl - (3 * TickSize))
                        {
                            ExitShort("SL_Candado_Surfer", "SniperSurfer_Short");
                            Print(Time[0] + string.Format(" - [SURFER -> CORTO PROTEGIDO POR CANDADO] Precio superó Lower 1.75 +1tk ({0:F2}) | Salida a Mercado @ ~{1:F2}",
                                safeLockSl, Close[0]));
                        }
                        else
                        {
                            // Garantizar que la orden StopMarket esté SIEMPRE al menos 3 ticks por encima del precio actual para evitar rechazos en subidas rápidas
                            double finalStopPrice = Math.Max(safeLockSl, Close[0] + (3 * TickSize));
                            SetStopLoss("SniperSurfer_Short", CalculationMode.Price, finalStopPrice, false);
                        }
                    }
                    else
                    {
                        // SL Inicial: Upper 1.75 + 1 tick (máximo respiro)
                        double dynamicSl = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Upper[0] + (1 * TickSize));
                        double safeSl = Math.Max(dynamicSl, entryPrice + (MinSafetySlTicks * TickSize));

                        if (Close[0] >= safeSl - (3 * TickSize))
                        {
                            ExitShort("SL_BandaOpuesta_Surfer", "SniperSurfer_Short");
                            Print(Time[0] + string.Format(" - [SURFER -> CORTO CERRADO POR STOP INICIAL] Precio superó Upper 1.75 +1tk ({0:F2}) | Salida a Mercado @ ~{1:F2}",
                                safeSl, Close[0]));
                        }
                        else
                        {
                            double finalStopPrice = Math.Max(safeSl, Close[0] + (3 * TickSize));
                            SetStopLoss("SniperSurfer_Short", CalculationMode.Price, finalStopPrice, false);
                        }
                    }
                }
            }
        }
        #endregion

        #region Reporte de Posiciones
        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            if (SystemPerformance.AllTrades.Count > lastReportedTradeCount)
            {
                lastReportedTradeCount = SystemPerformance.AllTrades.Count;
                Trade lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                string direction = (lastTrade.Entry.MarketPosition == MarketPosition.Long) ? "LARGO" : "CORTO";
                string resultado = (lastTrade.ProfitTicks >= 0) ? "CON GANANCIA" : "CON PÉRDIDA";

                Print(Time[0] + string.Format(" - [{0} CERRADO {1}] Señal: {2} -> {3} | Resultado: {4:C2} ({5:F0} tks) | Entrada: {6:F2} -> Salida: {7:F2}",
                    direction, resultado, lastTrade.Entry.Name, lastTrade.Exit.Name, lastTrade.ProfitCurrency, lastTrade.ProfitTicks, lastTrade.Entry.Price, lastTrade.Exit.Price));
            }
        }
        #endregion

        #region Interfaz de Control WPF (Botón de Pánico + Atajo Ctrl + Espacio)
        private void CreateWPFControls()
        {
            chartWindow = Window.GetWindow(ChartControl.Parent) as Chart;
            if (chartWindow == null) return;

            panicButton = new System.Windows.Controls.Button
            {
                Content = "ESTRATEGIA PAUSADA",
                Background = Brushes.Crimson,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Height = 32,
                Margin = new Thickness(10, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Padding = new Thickness(10, 4, 10, 4),
                ToolTip = "Haz clic o presiona Ctrl + Espacio para Activar / Pausar (Pánico Inmediato)"
            };

            panicButton.Click += OnPanicButtonClick;
            UserControlCollection.Add(panicButton);

            if (!isSubscribedToKeys)
            {
                chartWindow.KeyDown += OnChartKeyDown;
                isSubscribedToKeys = true;
            }
        }

        private void DisposeWPFControls()
        {
            if (panicButton != null)
            {
                panicButton.Click -= OnPanicButtonClick;
                UserControlCollection.Remove(panicButton);
                panicButton = null;
            }

            if (chartWindow != null && isSubscribedToKeys)
            {
                chartWindow.KeyDown -= OnChartKeyDown;
                isSubscribedToKeys = false;
            }
        }

        private void OnPanicButtonClick(object sender, RoutedEventArgs e)
        {
            ToggleStrategyState();
        }

        private void OnChartKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Space)
            {
                ToggleStrategyState();
                e.Handled = true;
            }
        }

        private void ToggleStrategyState()
        {
            isStrategyActive = !isStrategyActive;

            if (panicButton != null)
            {
                if (isStrategyActive)
                {
                    panicButton.Content = "ESTRATEGIA ACTIVA";
                    panicButton.Background = Brushes.ForestGreen;
                    Print(Time[0] + " - [BOTÓN DE CONTROL] Estrategia ACTIVADA por el usuario.");
                }
                else
                {
                    panicButton.Content = "ESTRATEGIA PAUSADA";
                    panicButton.Background = Brushes.Crimson;
                    Print(Time[0] + " - [BOTÓN DE PÁNICO] Estrategia PAUSADA. Liquidando posiciones si existieran...");

                    TriggerCustomEvent(state =>
                    {
                        if (Position.MarketPosition == MarketPosition.Long)
                            ExitLong("Panico_CierreInmediato", "");
                        else if (Position.MarketPosition == MarketPosition.Short)
                            ExitShort("Panico_CierreInmediato", "");

                        currentMode = SniperSurferMode.None;
                        isSurferLockActive = false;
                    }, null);
                }
            }
        }
        #endregion

        #region Propiedades de Usuario
        [NinjaScriptProperty]
        [Display(Name = "Versión del Código", Order = 1, GroupName = "1. Información")]
        [ReadOnly(true)]
        public string Version { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Hora Inicio RTH (HHMMSS)", Order = 2, GroupName = "2. Horario RTH")]
        public int StartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Hora Límite Entradas (HHMMSS)", Order = 3, GroupName = "2. Horario RTH")]
        public int StopEntriesTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Hora Cierre Forzoso (HHMMSS)", Order = 4, GroupName = "2. Horario RTH")]
        public int ForceCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Período TEMA", Order = 5, GroupName = "3. Indicadores")]
        public int TemaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Período EMA", Order = 6, GroupName = "3. Indicadores")]
        public int EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Período Canales Keltner", Order = 7, GroupName = "3. Indicadores")]
        public int KeltnerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Multiplicador Canal Grande", Order = 8, GroupName = "3. Indicadores")]
        public double KeltnerBigMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Multiplicador Canal Pequeño", Order = 9, GroupName = "3. Indicadores")]
        public double KeltnerSmallMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20.0)]
        [Display(Name = "Penetración Mínima Cruce (Ticks)", Order = 10, GroupName = "4. Francotirador Zona 1")]
        public double MinCrossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, int.MaxValue)]
        [Display(Name = "Barras Mínimas Tendencia Madura (100)", Order = 11, GroupName = "4. Francotirador Zona 1")]
        public int MinBarsTrendBreakZona1 { get; set; }

        [NinjaScriptProperty]
        [Range(4, int.MaxValue)]
        [Display(Name = "TP Fijo Francotirador Zona 1 (Ticks)", Order = 12, GroupName = "4. Francotirador Zona 1")]
        public int Zone1FixedTpTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Tolerancia Ruptura Externa (Ticks)", Order = 13, GroupName = "4. Francotirador Zona 1")]
        public int MaxOuterBufferTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Offset Stop Loss (Ticks)", Order = 14, GroupName = "5. Gestión de Riesgo")]
        public int SlOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Colchón Mínimo SL (Ticks)", Order = 15, GroupName = "5. Gestión de Riesgo")]
        public int MinSafetySlTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Marcadores Visuales", Order = 16, GroupName = "6. Visual")]
        public bool ShowVisualMarkers { get; set; }
        #endregion
    }
}
