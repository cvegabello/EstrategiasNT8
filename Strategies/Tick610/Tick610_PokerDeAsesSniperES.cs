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

// =========================================================================================================
// ESTRATEGIA: Tick610_PokerDeAsesSniperES (PAS) - "Poker de Ases Sniper"
// Desarrollado con Mentoría Cuantitativa Institucional para Gráficas de 610 Ticks en Futuros ES / MES.
//
// LAS 4 CARTAS DEL POKER (LOS 4 ASES):
//   1. As 1: [Z1-100Bar] - Ruptura quirúrgica de tendencia madura tras >= 100 barras comprimidas en EMA 200.
//   2. As 2: [SOBB]      - SurferOutBigBanda: Expansión violenta fuera de 3.5 ATR con Escalonamiento de 3 Pisos.
//   3. As 3: [Ratchet]   - Súper-tendencia sostenida con filtro de 20 barras y Chicle Parabólico.
//   4. As 4: [Cascada50] - El Tren Bala Desbocado: EMA 200 separada > 4 ticks de Banda 3.5 por >= 50 barras.
//
// GESTIÓN DE 3 CONTRATOS (EL ESCUADRÓN):
//   - Contrato 1: Asegura plata en el Piso 1 ($180 ES / $18 MES) y mueve los contratos restantes a Breakeven.
//   - Contrato 2: Cobra en el Piso 2 ($220 ES / $22 MES) anclando el piso a la Banda 3.5.
//   - Contrato 3: El Runner salvaje en Modo Ahogo (Trailing ceñido) para capturar mega-corridas.
//
// ESCUDO DE CUENTA (PAZ MENTAL):
//   - Cortacircuito Diario en Vivo: -$300 USD (ES) / -$30 USD (MES) con liquidación inmediata a mercado.
//   - Meta Diaria: +$600 USD (ES) / +$60 USD (MES) para bloquear nuevas entradas e irse a descansar.
//   - Regla de 3 Strikes Consecutivos.
//   - Botón de Pánico WPF en Gráfica y Atajo de Teclado (Ctrl + Espacio).
// =========================================================================================================

namespace NinjaTrader.NinjaScript.Strategies
{
    public class Tick610_PokerDeAsesSniperES : Strategy
    {
        #region Variables y Enumeraciones

        // === ENUMERACIÓN DE LOS 4 ASES ===
        private enum AceType
        {
            None,
            As1_Zona1_100Bar,
            As2_SOBB,
            As3_Ratchet,
            As4_Cascada50
        }
        private AceType currentAce = AceType.None;

        // === VARIABLES DEL AS 1: ZONA 1 (100 BARRAS) ===
        private int lastTemaSide = 0;              // 1 = Arriba de EMA200, -1 = Debajo de EMA200
        private bool crossUpConsumed = false;
        private bool crossDownConsumed = false;
        private int barsBelowEmaWithoutCross = 0;  // Acumulación continua para Long
        private int barsAboveEmaWithoutCross = 0;  // Acumulación continua para Short

        // === VARIABLES DEL AS 2: SOBB (SurferOutBigBanda) ===
        private bool sobbLevel1Achieved = false;   // Piso 1 alcanzado (Contrato 1 cerrado, BE armado)
        private bool sobbLevel2Achieved = false;   // Piso 2 alcanzado (Contrato 2 cerrado, Banda 3.5 anclada)
        private bool sobbLevel3ChokeActive = false;// Piso 3 activo (Modo Ahogo para Contrato 3 Runner)

        // === VARIABLES DEL AS 3: RATCHET (EL CHICLE PARABÓLICO ORIGINAL) ===
        private enum RatchetSetup { None, Long, Short }
        private RatchetSetup ratchetSetup = RatchetSetup.None;
        private int ratchetSetupBarCounter = 0;
        private bool ratchetC1Achieved = false;    // Contrato 1 cerrado en Banda 3.5 o +22 ticks (BE armado)
        private bool ratchetC2Achieved = false;    // Contrato 2 cerrado (Stop Línea Media o Fase 3)
        private bool ratchetChokeActive = false;   // Contrato 3 Runner en Chicle Parabólico

        // === VARIABLES DEL AS 4: CASCADA 50 BARRAS ===
        private int barsCascadaShortCount = 0;     // Barras con EMA200 > Upper 3.5 + 4 ticks
        private int barsCascadaLongCount  = 0;     // Barras con EMA200 < Lower 3.5 - 4 ticks

        // Tracking de MFE/MAE de la posición activa
        private double entryPriceGlobal = 0.0;
        private double highestPriceSinceEntry = 0.0;
        private double lowestPriceSinceEntry = double.MaxValue;

        // === ESCUDO DIARIO Y GESTIÓN DE RIESGO DE CUENTA ===
        private double dailyRealizedPnL = 0.0;
        private int consecutiveLosses = 0;
        private bool isDailyLocked = false;
        private string dailyLockReason = string.Empty;
        private int lastSessionDate = 0;

        // === FACTOR MULTIPLICADOR (AUTO-ADAPTACIÓN MINI ES VS MICRO MES) ===
        private double instrumentMultiplier = 1.0; // 1.0 para ES, 0.1 para MES
        private bool isMicroInstrument = false;

        // === INDICADORES EN MEMORIA (SIN DIBUJAR POR REGLA AGENTS.md) ===
        private TEMA tema9;
        private EMA ema200;
        private KeltnerChannel keltnerBig;   // Canal Exterior (52, 3.5)
        private KeltnerChannel keltnerSmall; // Canal Interior (52, 1.75)

        // === INTERFAZ WPF (BOTÓN DE PÁNICO) ===
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
                Description                                 = @"Estrategia Institucional Poker de Ases Sniper (PAS) en 610 Ticks para ES/MES. Combina Z1-100Bar, SOBB (Surfer), Ratchet y Cascada 50 barras con Escalonamiento de 3 Contratos y Cortacircuito Diario.";
                Name                                        = "Tick610_PokerDeAsesSniperES";
                Calculate                                   = Calculate.OnBarClose;
                EntriesPerDirection                         = 3; // Permite hasta 3 contratos por dirección
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
                RealtimeErrorHandling                       = RealtimeErrorHandling.IgnoreAllErrors; // Resiliencia total
                StopTargetHandling                          = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade                         = 200;
                IsInstantiatedOnEachOptimizationIteration   = true;
                IncludeCommission                           = true; // Comisiones Lifetime descontadas ($4.36 ES / $1.04 MES)

                // Propiedades Generales
                Version                                     = "1.2 - Poker de Ases (PAS) [4 Ases Independientes]";

                // Horario RTH estricto (Nueva York EST)
                StartTime                                   = 95000;  // 09:50 AM EST
                StopEntriesTime                             = 154500; // 03:45 PM EST (Bloqueo de nuevas entradas)
                ForceCloseTime                              = 160000; // 04:00 PM EST (Liquidación obligatoria)

                // Gestión de Contratos del Escuadrón
                Contracts                                   = 3;      // Por defecto: 3 contratos (Asegurador, Estructural, Runner)

                // Parámetros de Indicadores
                TemaPeriod                                  = 9;
                EmaPeriod                                   = 200;
                KeltnerPeriod                               = 52;
                KeltnerBigMult                              = 3.5;
                KeltnerSmallMult                            = 1.75;

                // Parámetros del As 1: Z1-100Bar
                MinCrossTicks                               = 2.3;    // Penetración mínima de cruce real
                MinBarsZona1                                = 100;    // 100 barras obligatorias de compresión previa
                Zone1FixedTpTicks                           = 12;     // Target Fijo de Francotirador (12 ticks = $150 ES / $15 MES)
                SlOffsetTicks                               = 2;      // Offset de respiro en ticks para el Stop Loss
                MinSafetySlTicks                            = 8;      // Colchón mínimo de seguridad inicial

                // Parámetros del As 2: SOBB (SurferOutBigBanda) - Escalones de Ganancia Flotante
                SobbLevel1Dollars                           = 180.0;  // Escalón 1: Asegura Contrato 1 y pasa a Breakeven
                SobbLevel2Dollars                           = 220.0;  // Escalón 2: Cobra Contrato 2 y ancla SL a Banda 3.5
                SobbLevel3Dollars                           = 280.0;  // Escalón 3: Activa Modo Ahogo para Contrato 3
                SobbChokeTicks                              = 6;      // Distancia de ahogo pegado al High/Low para el Runner

                // Parámetros del As 3: Ratchet (El Chicle Parabólico Original)
                RatchetLookbackBars                         = 20;     // Barras sin tocar EMA 200
                RatchetCountdownBars                        = 10;     // Ventana de conteo buscando cruce de Línea Media
                RatchetTemaTolTicks                         = 2;      // Tolerancia en ticks para gancho de TEMA 9
                RatchetBreakEvenTicks                       = 22;     // BreakEven dinámico para Contrato 1
                RatchetChokeThresholdTicks                  = 72;     // Fase 3 Choke a los 72 ticks ($900 ES / $90 MES)
                RatchetChicleTicks                          = 7;      // Trailing ceñido del Chicle Parabólico (7 ticks)

                // Parámetros del As 4: Cascada 50 Barras
                CascadaMinBars                              = 50;     // 50 barras consecutivas requeridas
                CascadaSeparationTicks                      = 4;      // Mínimo 4 ticks de separación de la Banda 3.5
                CascadaMinSlopeTicks                        = 2;      // Inclinación mínima requerida en la EMA 200
                CascadaFixedTpTicks                         = 16;     // Target objetivo ($200 ES / $20 MES)

                // Escudo Diario de Cuenta
                UseDailyManagement                          = true;   // Activar Cortacircuito y Meta Diaria
                DailyLossLimit                              = 300.0;  // Cortacircuito estricto: -$300 ES / -$30 MES
                DailyProfitGoal                             = 600.0;  // Meta Diaria para irse a montar bicicleta: +$600 ES / +$60 MES
                MaxConsecutiveLosses                        = 3;      // Regla de 3 Strikes consecutivos
            }
            else if (State == State.DataLoaded)
            {
                // Auto-Detección de Instrumento (Mini ES vs Micro MES)
                isMicroInstrument = Instrument.MasterInstrument.Name.Contains("MES");
                instrumentMultiplier = isMicroInstrument ? 0.10 : 1.0;

                // Si es Micro MES, escalamos proporcionalmente los umbrales en dólares por defecto
                if (isMicroInstrument)
                {
                    DailyLossLimit     = 30.0;
                    DailyProfitGoal    = 60.0;
                    SobbLevel1Dollars  = 18.0;
                    SobbLevel2Dollars  = 22.0;
                    SobbLevel3Dollars  = 28.0;
                    Print(string.Format("=== [PAS] INSTRUMENTO MICRO (MES) DETECTADO === Multiplicador 0.10x aplicado. Cortacircuito: -${0:F2} | Meta: +${1:F2}",
                        DailyLossLimit, DailyProfitGoal));
                }
                else
                {
                    Print(string.Format("=== [PAS] INSTRUMENTO ESTÁNDAR (ES) DETECTADO === Cortacircuito: -${0:F2} | Meta: +${1:F2}",
                        DailyLossLimit, DailyProfitGoal));
                }

                // Instanciamos indicadores en memoria
                tema9        = TEMA(Close, TemaPeriod);
                ema200       = EMA(Close, EmaPeriod);
                keltnerBig   = KeltnerChannel(Close, KeltnerBigMult, KeltnerPeriod);
                keltnerSmall = KeltnerChannel(Close, KeltnerSmallMult, KeltnerPeriod);

                // En modo Backtest o Strategy Analyzer, se activa automáticamente
                if (ChartControl == null)
                {
                    isStrategyActive = true;
                    Print("Modo Headless (Strategy Analyzer / Backtest) detectado. Estrategia auto-activada.");
                }
            }
            else if (State == State.Historical)
            {
                if (ChartControl != null && UserControlCollection != null)
                {
                    Dispatcher.InvokeAsync(() => { CreateWPFControls(); });
                }
            }
            else if (State == State.Terminated)
            {
                if (ChartControl != null && UserControlCollection != null)
                {
                    Dispatcher.InvokeAsync(() => { DisposeWPFControls(); });
                }
            }
        }
        #endregion

        #region Bucle Principal de Trading (OnBarUpdate)
        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(BarsRequiredToTrade, EmaPeriod)) return;

            // 1. VERIFICACIÓN DEL BOTÓN DE PÁNICO
            if (!isStrategyActive) return;

            // 2. VERIFICACIÓN DE NUEVA SESIÓN DIARIA (RESET DE GESTIÓN DE RIESGO)
            int currentDate = Time[0].Date.DayOfYear;
            if (currentDate != lastSessionDate)
            {
                lastSessionDate = currentDate;
                dailyRealizedPnL = 0.0;
                consecutiveLosses = 0;
                isDailyLocked = false;
                dailyLockReason = string.Empty;
                Print(string.Format("==================== NUEVA SESIÓN RTH ({0:MM/dd/yyyy}) ==================== PnL Diario Reiniciado a $0.00", Time[0]));
            }

            // 3. HORARIO RTH ESTRICTO (09:50 AM - 04:00 PM EST)
            int timeNow = ToTime(Time[0]);
            bool isForceCloseTime = timeNow >= ForceCloseTime;
            bool isTradingTime    = timeNow >= StartTime && timeNow <= StopEntriesTime;

            if (isForceCloseTime)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    LiquidateAllMarket("Cierre Forzoso 4:00 PM EST");
                    Print(Time[0] + " - [CIERRE FORZOSO] Posiciones liquidadas por fin de sesión RTH.");
                }
                ResetAllTradeCounters();
                return;
            }

            // 4. ACTUALIZACIÓN DE MFE / MAE CONTINUO
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (High[0] > highestPriceSinceEntry) highestPriceSinceEntry = High[0];
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (Low[0] < lowestPriceSinceEntry) lowestPriceSinceEntry = Low[0];
            }

            // 5. CORTACIRCUITO DIARIO EN VIVO ("PUM, ADIÓS") Y META DIARIA
            if (UseDailyManagement)
            {
                double unrealizedPnL = 0.0;
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    unrealizedPnL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
                }
                double totalDailyFloatingPnL = dailyRealizedPnL + unrealizedPnL;

                // CORTACIRCUITO EN VIVO: Si la cuenta toca el límite de pérdida diaria
                if (!isDailyLocked && totalDailyFloatingPnL <= -DailyLossLimit)
                {
                    isDailyLocked = true;
                    dailyLockReason = string.Format("CORTACIRCUITO DIARIO (-${0:F2}) ALCANZADO", DailyLossLimit);
                    Print(string.Format("{0} - [CORTACIRCUITO EN VIVO] Pérdida diaria tocó ${1:F2} (Límite: -${2:F2}). ¡PUM, ADIÓS! Liquidando a mercado y bloqueando operativa.",
                        Time[0], totalDailyFloatingPnL, DailyLossLimit));
                    
                    if (Position.MarketPosition != MarketPosition.Flat)
                        LiquidateAllMarket("Cortacircuito_Diario");
                    return;
                }

                // Si la estrategia ya está bloqueada por el día, no evaluar más entradas
                if (isDailyLocked) return;
            }

            // 6. GESTIÓN DE SALIDAS DINÁMICAS Y ESCALONAMIENTO
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                ManageDynamicExits();
            }

            // 7. ACTUALIZACIÓN DE CONTADORES DE MERCADO
            UpdateMarketCounters();

            // 8. EVALUACIÓN DE ENTRADAS DE LOS 4 ASES (SOLO SI FLAT Y EN HORARIO VÁLIDO)
            if (Position.MarketPosition == MarketPosition.Flat && isTradingTime && !isDailyLocked)
            {
                // AS 1: Francotirador Zona 1 (100 Barras)
                EvaluateAs1_Zona1();

                // Si As 1 no disparó, evaluar AS 2: SOBB (SurferOutBigBanda)
                if (Position.MarketPosition == MarketPosition.Flat)
                    EvaluateAs2_SOBB();

                // Si As 2 no disparó, evaluar AS 3: Ratchet (Chicle Parabólico)
                if (Position.MarketPosition == MarketPosition.Flat)
                    EvaluateAs3_Ratchet();

                // Si As 3 no disparó, evaluar AS 4: Cascada 50 Barras
                if (Position.MarketPosition == MarketPosition.Flat)
                    EvaluateAs4_Cascada50();
            }
        }
        #endregion

        #region Actualización de Contadores de Mercado
        private void UpdateMarketCounters()
        {
            // === CONTADOR AS 1: ZONA 1 (Acumulación continua sin tocar EMA 200) ===
            if (High[0] < ema200[0])
            {
                barsBelowEmaWithoutCross++;
                barsAboveEmaWithoutCross = 0;
            }
            else if (Low[0] > ema200[0])
            {
                barsAboveEmaWithoutCross++;
                barsBelowEmaWithoutCross = 0;
            }
            else
            {
                // La barra cruzó o tocó la EMA 200
                if (barsBelowEmaWithoutCross > 0 && barsBelowEmaWithoutCross < MinBarsZona1)
                {
                    Print(string.Format("{0} - [PAS - Z1-100Bar] Toque/Cruce de EMA200 IGNORADO: solo acumulaba {1} barras bajo EMA (Mínimo: {2}). Contador a 0.",
                        Time[0], barsBelowEmaWithoutCross, MinBarsZona1));
                }
                if (barsAboveEmaWithoutCross > 0 && barsAboveEmaWithoutCross < MinBarsZona1)
                {
                    Print(string.Format("{0} - [PAS - Z1-100Bar] Toque/Cruce de EMA200 IGNORADO: solo acumulaba {1} barras sobre EMA (Mínimo: {2}). Contador a 0.",
                        Time[0], barsAboveEmaWithoutCross, MinBarsZona1));
                }
                barsBelowEmaWithoutCross = 0;
                barsAboveEmaWithoutCross = 0;
            }

            // === CONTADOR AS 4: CASCADA 50 BARRAS (Separación de EMA 200 > 4 ticks de Banda 3.5) ===
            double separationDist = CascadaSeparationTicks * TickSize;

            // Bajista (EMA 200 arriba de Upper 3.5 por > 4 ticks)
            if (ema200[0] > (keltnerBig.Upper[0] + separationDist))
            {
                barsCascadaShortCount++;
                Print(string.Format("{0} - [PAS - Cascada50 | CORTO] Cascada50_Barra {1}/{2} | EMA200 ({3:F2}) > Upper 3.5 + {4}tk ({5:F2})",
                    Time[0], barsCascadaShortCount, CascadaMinBars, ema200[0], CascadaSeparationTicks, keltnerBig.Upper[0] + separationDist));
            }
            else
            {
                if (barsCascadaShortCount > 0)
                {
                    Print(string.Format("{0} - [PAS - Cascada50 | CORTO] Separación perdida tras {1} barras. Cascada50_Barra 0.",
                        Time[0], barsCascadaShortCount));
                }
                barsCascadaShortCount = 0;
            }

            // Alcista (EMA 200 abajo de Lower 3.5 por > 4 ticks)
            if (ema200[0] < (keltnerBig.Lower[0] - separationDist))
            {
                barsCascadaLongCount++;
                Print(string.Format("{0} - [PAS - Cascada50 | LARGO] Cascada50_Barra {1}/{2} | EMA200 ({3:F2}) < Lower 3.5 - {4}tk ({5:F2})",
                    Time[0], barsCascadaLongCount, CascadaMinBars, ema200[0], CascadaSeparationTicks, keltnerBig.Lower[0] - separationDist));
            }
            else
            {
                if (barsCascadaLongCount > 0)
                {
                    Print(string.Format("{0} - [PAS - Cascada50 | LARGO] Separación perdida tras {1} barras. Cascada50_Barra 0.",
                        Time[0], barsCascadaLongCount));
                }
                barsCascadaLongCount = 0;
            }
        }
        #endregion

        #region Módulos de Entrada de los 4 Ases

        // =========================================================================
        // AS 1: FRANCOTIRADOR ZONA 1 (RUPTURA TRAS >= 100 BARRAS COMPRIMIDAS)
        // =========================================================================
        private void EvaluateAs1_Zona1()
        {
            double upperSmall = keltnerSmall.Upper[0];
            double lowerSmall = keltnerSmall.Lower[0];
            double upperBig   = keltnerBig.Upper[0];
            double lowerBig   = keltnerBig.Lower[0];
            double midline    = keltnerBig.Midline[0];
            double slOffset   = SlOffsetTicks * TickSize;

            // CASO LARGO: Cruce alcista tras >= 100 barras bajo la EMA 200
            if (lastTemaSide == -1 && !crossUpConsumed && tema9[0] > ema200[0])
            {
                double crossDistanceTicks = (tema9[0] - ema200[0]) / TickSize;
                double slopeTicks = (tema9[0] - tema9[1]) / TickSize;

                if (crossDistanceTicks >= MinCrossTicks && Close[0] > ema200[0] && slopeTicks > 0)
                {
                    crossUpConsumed = true;
                    lastTemaSide = 1;

                    if (tema9[0] >= upperSmall && Close[0] <= upperBig + (12 * TickSize))
                    {
                        if (barsBelowEmaWithoutCross >= MinBarsZona1)
                        {
                            ExecuteAceEntry(AceType.As1_Zona1_100Bar, MarketPosition.Long, "Z1-100Bar_Long");
                            Print(string.Format("{0} - [PAS - Z1-100Bar] ¡DISPARO LARGO! Ruptura ejecutada tras acumular {1} barras bajo EMA 200. Entrada @ ~{2:F2} | Contratos: {3}",
                                Time[0], barsBelowEmaWithoutCross, Close[0], Contracts));
                            return;
                        }
                    }
                }
            }

            // CASO CORTO: Cruce bajista tras >= 100 barras sobre la EMA 200
            if (lastTemaSide == 1 && !crossDownConsumed && tema9[0] < ema200[0])
            {
                double crossDistanceTicks = (ema200[0] - tema9[0]) / TickSize;
                double slopeTicks = (tema9[1] - tema9[0]) / TickSize;

                if (crossDistanceTicks >= MinCrossTicks && Close[0] < ema200[0] && slopeTicks > 0)
                {
                    crossDownConsumed = true;
                    lastTemaSide = -1;

                    if (tema9[0] <= lowerSmall && Close[0] >= lowerBig - (12 * TickSize))
                    {
                        if (barsAboveEmaWithoutCross >= MinBarsZona1)
                        {
                            ExecuteAceEntry(AceType.As1_Zona1_100Bar, MarketPosition.Short, "Z1-100Bar_Short");
                            Print(string.Format("{0} - [PAS - Z1-100Bar] ¡DISPARO CORTO! Ruptura ejecutada tras acumular {1} barras sobre EMA 200. Entrada @ ~{2:F2} | Contratos: {3}",
                                Time[0], barsAboveEmaWithoutCross, Close[0], Contracts));
                            return;
                        }
                    }
                }
            }

            // Detección de lado TEMA para preparar el cruce
            if (tema9[0] > ema200[0] && lastTemaSide != 1)
            {
                lastTemaSide = 1;
                crossUpConsumed = false;
            }
            else if (tema9[0] < ema200[0] && lastTemaSide != -1)
            {
                lastTemaSide = -1;
                crossDownConsumed = false;
            }
        }

        // =========================================================================
        // AS 2: SOBB (SurferOutBigBanda - EXPANSIÓN FUERA DE 3.5 ATR)
        // =========================================================================
        private void EvaluateAs2_SOBB()
        {
            double upperBig   = keltnerBig.Upper[0];
            double lowerBig   = keltnerBig.Lower[0];
            double midline    = keltnerBig.Midline[0];
            double upperSmall = keltnerSmall.Upper[0];
            double lowerSmall = keltnerSmall.Lower[0];

            double emaSlope     = ema200[0] - ema200[2];
            double midlineSlope = midline - keltnerBig.Midline[2];
            double upperSlope   = upperBig - keltnerBig.Upper[2];
            double lowerSlope   = lowerBig - keltnerBig.Lower[2];

            // SOBB LARGO: Vela completa flotando sobre Banda 3.5 con pendientes sincronizadas
            bool isVelaFlotandoLargo = (Low[0] > upperBig);
            bool isEmaZonaLargo      = (ema200[0] <= midline && ema200[0] >= lowerSmall);
            bool isPendientesLargo   = (midlineSlope > 0 && upperSlope > 0 && emaSlope < midlineSlope);

            if (isVelaFlotandoLargo && isEmaZonaLargo && isPendientesLargo)
            {
                ExecuteAceEntry(AceType.As2_SOBB, MarketPosition.Long, "SOBB_Long");
                Print(string.Format("{0} - [PAS - SOBB] ¡DISPARO LARGO! Expansión fuera de Banda 3.5 | Entrada @ ~{1:F2} | Escalonamiento de 3 Pisos armado.",
                    Time[0], Close[0]));
                return;
            }

            // SOBB CORTO: Vela completa flotando bajo Banda 3.5 con pendientes sincronizadas
            bool isVelaFlotandoCorto = (High[0] < lowerBig);
            bool isEmaZonaCorto      = (ema200[0] >= midline && ema200[0] <= upperSmall);
            bool isPendientesCorto   = (midlineSlope < 0 && lowerSlope < 0 && emaSlope > midlineSlope);

            if (isVelaFlotandoCorto && isEmaZonaCorto && isPendientesCorto)
            {
                ExecuteAceEntry(AceType.As2_SOBB, MarketPosition.Short, "SOBB_Short");
                Print(string.Format("{0} - [PAS - SOBB] ¡DISPARO CORTO! Expansión fuera de Banda 3.5 | Entrada @ ~{1:F2} | Escalonamiento de 3 Pisos armado.",
                    Time[0], Close[0]));
                return;
            }
        }

        // =========================================================================
        // AS 3: RATCHET (EL CHICLE PARABÓLICO ORIGINAL - CRUCE DE LÍNEA MEDIA)
        // =========================================================================
        private void EvaluateAs3_Ratchet()
        {
            // A) Filtro Macro de Tendencia Limpia (Últimas 20 barras sin tocar EMA 200)
            bool emaShortValid = true;
            bool emaLongValid  = true;
            for (int i = 0; i <= RatchetLookbackBars; i++)
            {
                if (High[i] >= ema200[i]) emaShortValid = false; // Todo debajo para cortos
                if (Low[i]  <= ema200[i]) emaLongValid  = false; // Todo encima para largos
            }

            // B) Detección de Toque o Penetración en Banda Keltner 3.5 en barras 1 a 3
            bool touchedUpperExtreme = false;
            bool touchedLowerExtreme = false;
            for (int i = 1; i <= 3; i++)
            {
                double upperTemaTol  = keltnerBig.Upper[i] - (RatchetTemaTolTicks * TickSize);
                double lowerTemaTol  = keltnerBig.Lower[i] + (RatchetTemaTolTicks * TickSize);
                double upperPricePen = keltnerBig.Upper[i] + (1.5 * TickSize);
                double lowerPricePen = keltnerBig.Lower[i] - (1.5 * TickSize);

                if (High[i] >= upperPricePen || tema9[i] >= upperTemaTol) touchedUpperExtreme = true;
                if (Low[i]  <= lowerPricePen || tema9[i] <= lowerTemaTol) touchedLowerExtreme = true;
            }

            // Referencia fija para calcular si el TEMA enganchó o cruzó de regreso
            double upperBandTolCross = keltnerBig.Upper[1] - (RatchetTemaTolTicks * TickSize);
            double lowerBandTolCross = keltnerBig.Lower[1] + (RatchetTemaTolTicks * TickSize);

            // Alerta Bajista (preparando corto): Gancho en Banda Superior
            bool isPeakShort = tema9[2] < tema9[1] && tema9[0] < tema9[1];
            bool isCrossBackShort = tema9[1] >= upperBandTolCross && tema9[0] < upperBandTolCross && tema9[0] < tema9[1];

            if ((isPeakShort && touchedUpperExtreme) || isCrossBackShort)
            {
                if (!emaShortValid)
                {
                    // Filtro macro: precio tocó la EMA 200 recientemente
                }
                else
                {
                    if (ratchetSetup == RatchetSetup.Short)
                    {
                        Print(Time[0] + " - [PAS - Ratchet] Nuevo gancho bajista en Banda Superior. Reloj reiniciado a 0.");
                    }
                    else
                    {
                        Print(string.Format("{0} - [PAS - Ratchet] ALERTA CORTO: Gancho TEMA en Banda Superior. Iniciando reloj de {1} barras buscando cruce de Línea Media.",
                            Time[0], RatchetCountdownBars));
                    }
                    ratchetSetup = RatchetSetup.Short;
                    ratchetSetupBarCounter = 0;
                }
            }

            // Alerta Alcista (preparando largo): Gancho en Banda Inferior
            bool isTroughLong = tema9[2] > tema9[1] && tema9[0] > tema9[1];
            bool isCrossBackLong = tema9[1] <= lowerBandTolCross && tema9[0] > lowerBandTolCross && tema9[0] > tema9[1];

            if ((isTroughLong && touchedLowerExtreme) || isCrossBackLong)
            {
                if (!emaLongValid)
                {
                    // Filtro macro: precio tocó la EMA 200 recientemente
                }
                else
                {
                    if (ratchetSetup == RatchetSetup.Long)
                    {
                        Print(Time[0] + " - [PAS - Ratchet] Nuevo gancho alcista en Banda Inferior. Reloj reiniciado a 0.");
                    }
                    else
                    {
                        Print(string.Format("{0} - [PAS - Ratchet] ALERTA LARGO: Gancho TEMA en Banda Inferior. Iniciando reloj de {1} barras buscando cruce de Línea Media.",
                            Time[0], RatchetCountdownBars));
                    }
                    ratchetSetup = RatchetSetup.Long;
                    ratchetSetupBarCounter = 0;
                }
            }

            // C) Conteo del Reloj y GATILLO CIRUJANO: Cruce de la Línea Media (Midline)
            if (ratchetSetup != RatchetSetup.None)
            {
                ratchetSetupBarCounter++;
                if (ratchetSetupBarCounter > RatchetCountdownBars)
                {
                    Print(string.Format("{0} - [PAS - Ratchet] Setup cancelado: Pasaron {1} barras sin cruzar la Línea Media.",
                        Time[0], RatchetCountdownBars));
                    ratchetSetup = RatchetSetup.None;
                }
                else
                {
                    // DISPARO LARGO: Cruce alcista de Línea Media
                    if (ratchetSetup == RatchetSetup.Long && CrossAbove(tema9, keltnerBig.Midline, 1))
                    {
                        ExecuteAceEntry(AceType.As3_Ratchet, MarketPosition.Long, "Ratchet_Long");
                        ratchetSetup = RatchetSetup.None;
                        Print(string.Format("{0} - [PAS - Ratchet] ¡DISPARO LARGO EN LÍNEA MEDIA! TEMA 9 cruzó Midline ({1:F2}) en barra {2}/{3}. Entrada @ ~{4:F2} | Escalonamiento de 3 Fases armado.",
                            Time[0], keltnerBig.Midline[0], ratchetSetupBarCounter, RatchetCountdownBars, Close[0]));
                        return;
                    }
                    // DISPARO CORTO: Cruce bajista de Línea Media
                    else if (ratchetSetup == RatchetSetup.Short && CrossBelow(tema9, keltnerBig.Midline, 1))
                    {
                        ExecuteAceEntry(AceType.As3_Ratchet, MarketPosition.Short, "Ratchet_Short");
                        ratchetSetup = RatchetSetup.None;
                        Print(string.Format("{0} - [PAS - Ratchet] ¡DISPARO CORTO EN LÍNEA MEDIA! TEMA 9 cruzó Midline ({1:F2}) en barra {2}/{3}. Entrada @ ~{4:F2} | Escalonamiento de 3 Fases armado.",
                            Time[0], keltnerBig.Midline[0], ratchetSetupBarCounter, RatchetCountdownBars, Close[0]));
                        return;
                    }
                }
            }
        }

        // =========================================================================
        // AS 4: CASCADA 50 BARRAS (EL TREN BALA DESBOCADO)
        // =========================================================================
        private void EvaluateAs4_Cascada50()
        {
            double midline = keltnerBig.Midline[0];
            double minSlope = CascadaMinSlopeTicks * TickSize;

            // DISPARO CORTO: EMA 200 > Upper 3.5 + 4 ticks por >= 50 barras, precio bajo Midline y pendiente bajista
            if (barsCascadaShortCount >= CascadaMinBars)
            {
                bool precioBajoMedia = Close[0] < midline;
                bool pendienteBajista = (ema200[5] - ema200[0]) >= minSlope;

                if (precioBajoMedia && pendienteBajista)
                {
                    ExecuteAceEntry(AceType.As4_Cascada50, MarketPosition.Short, "Cascada50_Short");
                    Print(string.Format("{0} - [PAS - Cascada50] ¡DISPARO CORTO! Tren bala desbocado ({1} barras con EMA > Upper 3.5). Entrada @ ~{2:F2} | SL ceñido en Midline +2tk: {3:F2}",
                        Time[0], barsCascadaShortCount, Close[0], midline + (2 * TickSize)));
                    return;
                }
            }

            // DISPARO LARGO: EMA 200 < Lower 3.5 - 4 ticks por >= 50 barras, precio sobre Midline y pendiente alcista
            if (barsCascadaLongCount >= CascadaMinBars)
            {
                bool precioSobreMedia = Close[0] > midline;
                bool pendienteAlcista = (ema200[0] - ema200[5]) >= minSlope;

                if (precioSobreMedia && pendienteAlcista)
                {
                    ExecuteAceEntry(AceType.As4_Cascada50, MarketPosition.Long, "Cascada50_Long");
                    Print(string.Format("{0} - [PAS - Cascada50] ¡DISPARO LARGO! Tren bala desbocado ({1} barras con EMA < Lower 3.5). Entrada @ ~{2:F2} | SL ceñido en Midline -2tk: {3:F2}",
                        Time[0], barsCascadaLongCount, Close[0], midline - (2 * TickSize)));
                    return;
                }
            }
        }

        #endregion

        #region Ejecución y Gestión de Salidas Escalonadas

        private void ExecuteAceEntry(AceType ace, MarketPosition pos, string signalName)
        {
            currentAce = ace;
            entryPriceGlobal = Close[0];
            highestPriceSinceEntry = High[0];
            lowestPriceSinceEntry  = Low[0];

            sobbLevel1Achieved = false;
            sobbLevel2Achieved = false;
            sobbLevel3ChokeActive = false;

            ratchetC1Achieved = false;
            ratchetC2Achieved = false;
            ratchetChokeActive = false;

            // Stop Loss inicial de desastre lejano (se envía una sola vez)
            double initialSlPrice = 0.0;
            if (pos == MarketPosition.Long)
            {
                if (ace == AceType.As1_Zona1_100Bar)
                    initialSlPrice = keltnerBig.Midline[0] - (SlOffsetTicks * TickSize);
                else if (ace == AceType.As4_Cascada50)
                    initialSlPrice = keltnerBig.Midline[0] - (2 * TickSize);
                else if (ace == AceType.As3_Ratchet)
                    initialSlPrice = keltnerBig.Lower[0] - (SlOffsetTicks * TickSize); // Banda opuesta en Ratchet
                else
                    initialSlPrice = keltnerSmall.Lower[0] - (1 * TickSize);

                initialSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Min(initialSlPrice, Close[0] - (MinSafetySlTicks * TickSize)));
                SetStopLoss(signalName, CalculationMode.Price, initialSlPrice, false);

                if (ace == AceType.As1_Zona1_100Bar)
                {
                    double tpPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0] + (Zone1FixedTpTicks * TickSize));
                    SetProfitTarget(signalName, CalculationMode.Price, tpPrice);
                }
                else if (ace == AceType.As4_Cascada50)
                {
                    double tpPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0] + (CascadaFixedTpTicks * TickSize));
                    SetProfitTarget(signalName, CalculationMode.Price, tpPrice);
                }

                EnterLong(Contracts, signalName);
            }
            else // Short
            {
                if (ace == AceType.As1_Zona1_100Bar)
                    initialSlPrice = keltnerBig.Midline[0] + (SlOffsetTicks * TickSize);
                else if (ace == AceType.As4_Cascada50)
                    initialSlPrice = keltnerBig.Midline[0] + (2 * TickSize);
                else if (ace == AceType.As3_Ratchet)
                    initialSlPrice = keltnerBig.Upper[0] + (SlOffsetTicks * TickSize); // Banda opuesta en Ratchet
                else
                    initialSlPrice = keltnerSmall.Upper[0] + (1 * TickSize);

                initialSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Max(initialSlPrice, Close[0] + (MinSafetySlTicks * TickSize)));
                SetStopLoss(signalName, CalculationMode.Price, initialSlPrice, false);

                if (ace == AceType.As1_Zona1_100Bar)
                {
                    double tpPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0] - (Zone1FixedTpTicks * TickSize));
                    SetProfitTarget(signalName, CalculationMode.Price, tpPrice);
                }
                else if (ace == AceType.As4_Cascada50)
                {
                    double tpPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0] - (CascadaFixedTpTicks * TickSize));
                    SetProfitTarget(signalName, CalculationMode.Price, tpPrice);
                }

                EnterShort(Contracts, signalName);
            }
        }

        private void ManageDynamicExits()
        {
            double entryPrice = Position.AveragePrice;
            int currentQty    = Position.Quantity;

            // =========================================================================
            // SALIDAS EN POSICIÓN LARGA
            // =========================================================================
            if (Position.MarketPosition == MarketPosition.Long)
            {
                double currentGainTicks = (High[0] - entryPrice) / TickSize;
                double currentGainDollars = currentGainTicks * (Instrument.MasterInstrument.PointValue * TickSize);
                double peakGainTicks = (highestPriceSinceEntry - entryPrice) / TickSize;
                double peakGainDollars = peakGainTicks * (Instrument.MasterInstrument.PointValue * TickSize);

                // --- AS 2: SOBB (SURFER OUT BIG BANDA - ESCALONES $180, $220, $280) ---
                if (currentAce == AceType.As2_SOBB)
                {
                    // PISO 1: Al alcanzar $180 ES / $18 MES
                    if (!sobbLevel1Achieved && (currentGainDollars >= SobbLevel1Dollars || peakGainDollars >= SobbLevel1Dollars))
                    {
                        sobbLevel1Achieved = true;
                        if (currentQty >= 3)
                        {
                            ExitLong(1, "SOBB_Piso1_Asegurador", "SOBB_Long");
                            Print(string.Format("{0} - [PAS - SOBB | ESCALÓN 1 SUPERADO] Ganancia: {1:C2} (+{2:F1} tks). Contrato 1 liquidado. Stops restantes blindados en BREAKEVEN +1tk ({3:F2}). ¡Riesgo CERO!",
                                Time[0], Math.Max(currentGainDollars, peakGainDollars), Math.Max(currentGainTicks, peakGainTicks), entryPrice + (1 * TickSize)));
                        }
                    }

                    // PISO 2: Al alcanzar $220 ES / $22 MES
                    if (sobbLevel1Achieved && !sobbLevel2Achieved && (currentGainDollars >= SobbLevel2Dollars || peakGainDollars >= SobbLevel2Dollars))
                    {
                        sobbLevel2Achieved = true;
                        if (currentQty >= 2)
                        {
                            ExitLong(1, "SOBB_Piso2_Estructural", "SOBB_Long");
                            Print(string.Format("{0} - [PAS - SOBB | ESCALÓN 2 SUPERADO] Ganancia: {1:C2} (+{2:F1} tks). Contrato 2 liquidado en zona de expansión.",
                                Time[0], Math.Max(currentGainDollars, peakGainDollars), Math.Max(currentGainTicks, peakGainTicks)));
                            Print(string.Format("{0} - [PAS - SOBB | ANCLAJE BANDA 3.5] Stop de Contrato 3 anclado al piso de Banda Upper 3.5 -1tk ({1:F2}).",
                                Time[0], keltnerBig.Upper[0] - (1 * TickSize)));
                        }
                    }

                    // PISO 3: Al superar $280 ES / $28 MES (Modo Ahogo / Trailing Ceñido para Contrato 3)
                    if (sobbLevel2Achieved && !sobbLevel3ChokeActive && (currentGainDollars >= SobbLevel3Dollars || peakGainDollars >= SobbLevel3Dollars))
                    {
                        sobbLevel3ChokeActive = true;
                        Print(string.Format("{0} - [PAS - SOBB | ESCALÓN 3 - MODO AHOGO ACTIVADO] Ganancia: {1:C2} (+{2:F1} tks). Contrato 3 Runner en Modo Ahogo a {3} ticks del High.",
                            Time[0], Math.Max(currentGainDollars, peakGainDollars), Math.Max(currentGainTicks, peakGainTicks), SobbChokeTicks));
                    }

                    // EJECUCIÓN DE STOPS DINÁMICOS DEL SOBB
                    if (sobbLevel3ChokeActive)
                    {
                        double chokeSlPrice = highestPriceSinceEntry - (SobbChokeTicks * TickSize);
                        if (Close[0] <= chokeSlPrice || Low[0] <= chokeSlPrice)
                        {
                            ExitLong("SOBB_Runner_Ahogo", "SOBB_Long");
                            Print(string.Format("{0} - [PAS - SOBB | RUNNER COBRADO POR AHOGO] Salida a Mercado @ ~{1:F2} | Ganancia asegurada en la cima.",
                                Time[0], Close[0]));
                        }
                    }
                    else if (sobbLevel2Achieved)
                    {
                        double band35Sl = keltnerBig.Upper[0] - (1 * TickSize);
                        if (Close[0] <= band35Sl || Low[0] <= band35Sl)
                        {
                            ExitLong("SOBB_Stop_Banda35", "SOBB_Long");
                            Print(string.Format("{0} - [PAS - SOBB | SALIDA EN PISO BANDA 3.5] Precio perforó Banda 3.5 ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                Time[0], band35Sl, Close[0]));
                        }
                    }
                    else if (sobbLevel1Achieved)
                    {
                        double bePrice = entryPrice + (1 * TickSize);
                        if (Close[0] <= bePrice || Low[0] <= bePrice)
                        {
                            ExitLong("SOBB_Stop_BreakEven", "SOBB_Long");
                            Print(string.Format("{0} - [PAS - SOBB | SALIDA EN BREAKEVEN] Posición restante protegida @ {1:F2}. Salida sin pérdida.",
                                Time[0], bePrice));
                        }
                    }
                    else
                    {
                        // Candado Inicial SOBB: Upper 1.75 - 1 tick
                        double lockSl = keltnerSmall.Upper[0] - (1 * TickSize);
                        if (Close[0] <= lockSl || Low[0] <= lockSl)
                        {
                            ExitLong("SOBB_Stop_Candado", "SOBB_Long");
                            Print(string.Format("{0} - [PAS - SOBB | SALIDA CANDADO 1.75] Precio perforó Upper 1.75 ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                Time[0], lockSl, Close[0]));
                        }
                    }

                    // Salida por Fin de Ciclo (EMA 200 corta banda inferior 3.5 para SOBB)
                    if (ema200[0] <= keltnerBig.Lower[0])
                    {
                        ExitLong("Exit_SOBB_FinCiclo", "SOBB_Long");
                        Print(string.Format("{0} - [PAS - SOBB | FIN DE CICLO] EMA200 ({1:F2}) cortó Lower 3.5 ({2:F2}) | Cierre total a Mercado @ ~{3:F2}",
                            Time[0], ema200[0], keltnerBig.Lower[0], Close[0]));
                    }
                }
                // --- AS 3: RATCHET (EL CHICLE PARABÓLICO - ESCALONAMIENTO POR FASES DE CANAL) ---
                else if (currentAce == AceType.As3_Ratchet)
                {
                    double maxProfitTicks = (highestPriceSinceEntry - entryPrice) / TickSize;

                    // CONTRATO 1: Se baja del bus al tocar Banda Superior 3.5 O al alcanzar BreakEven (+22 ticks)
                    if (!ratchetC1Achieved && (High[0] >= keltnerBig.Upper[0] || maxProfitTicks >= RatchetBreakEvenTicks))
                    {
                        ratchetC1Achieved = true;
                        if (currentQty >= 3)
                        {
                            ExitLong(1, "Ratchet_C1_Banda35", "Ratchet_Long");
                            Print(string.Format("{0} - [PAS - Ratchet | FASE 1 COMPLETADA] Precio tocó Banda Superior 3.5 ({1:F2}) / +{2:F1} tks. Contrato 1 liquidado. Stops restantes blindados en BREAKEVEN +1tk ({3:F2}). ¡Riesgo CERO!",
                                Time[0], keltnerBig.Upper[0], maxProfitTicks, entryPrice + (1 * TickSize)));
                        }
                    }

                    // CONTRATO 2: Se baja del bus si alcanza Fase 3 Choke (72 ticks) O si retrocede a Línea Media
                    if (ratchetC1Achieved && !ratchetC2Achieved)
                    {
                        // Si vuela a la Fase 3 Choke (72 ticks = $900 ES / $90 MES)
                        if (maxProfitTicks >= RatchetChokeThresholdTicks)
                        {
                            ratchetC2Achieved = true;
                            ratchetChokeActive = true;
                            if (currentQty >= 2)
                            {
                                ExitLong(1, "Ratchet_C2_ChokeTrigger", "Ratchet_Long");
                                Print(string.Format("{0} - [PAS - Ratchet | FASE 3 ESTRANGULAMIENTO] Superó {1} ticks. Contrato 2 cobrado. Contrato 3 Runner en Chicle Parabólico a {2} ticks del High.",
                                    Time[0], RatchetChokeThresholdTicks, RatchetChicleTicks));
                            }
                        }
                        // Si retrocede a la Línea Media tras haber tocado la banda superior
                        else if (Close[0] <= keltnerBig.Midline[0] - (1 * TickSize) || Low[0] <= keltnerBig.Midline[0] - (1 * TickSize))
                        {
                            ratchetC2Achieved = true;
                            if (currentQty >= 2)
                            {
                                ExitLong(1, "Ratchet_C2_Midline", "Ratchet_Long");
                                Print(string.Format("{0} - [PAS - Ratchet | FASE 2 CUMPLIDA] Precio perforó Línea Media ({1:F2}). Contrato 2 liquidado en resguardo estructural.",
                                    Time[0], keltnerBig.Midline[0]));
                            }
                        }
                    }

                    // CONTRATO 3 (RUNNER): El Chicle Parabólico
                    if (ratchetChokeActive || maxProfitTicks >= RatchetChokeThresholdTicks)
                    {
                        double chokeSl = highestPriceSinceEntry - (RatchetChicleTicks * TickSize);
                        if (Close[0] <= chokeSl || Low[0] <= chokeSl)
                        {
                            ExitLong("Ratchet_C3_RunnerChicle", "Ratchet_Long");
                            Print(string.Format("{0} - [PAS - Ratchet | RUNNER LIQUIDADO EN LA CIMA] Chicle Parabólico ejecutado a ~{1:F2} (Pico: {2:F2}). ¡Ganancia máxima asegurada!",
                                Time[0], Close[0], highestPriceSinceEntry));
                        }
                    }
                    // Stop de resguardo antes de entrar en Choke:
                    else if (ratchetC1Achieved)
                    {
                        double bePrice = entryPrice + (1 * TickSize);
                        if (Close[0] <= bePrice || Low[0] <= bePrice)
                        {
                            ExitLong("Ratchet_Stop_BE", "Ratchet_Long");
                            Print(string.Format("{0} - [PAS - Ratchet | SALIDA EN BREAKEVEN] Posición restante protegida en {1:F2}. Salida sin pérdida.",
                                Time[0], bePrice));
                        }
                    }
                    else
                    {
                        // Stop inicial estructural: Banda Keltner Inferior - 1 tick
                        double initialSl = keltnerBig.Lower[0] - (SlOffsetTicks * TickSize);
                        if (Close[0] <= initialSl || Low[0] <= initialSl)
                        {
                            ExitLong("Ratchet_Stop_Inicial", "Ratchet_Long");
                            Print(string.Format("{0} - [PAS - Ratchet | STOP INICIAL] Precio perforó Banda Inferior ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                Time[0], initialSl, Close[0]));
                        }
                    }
                }
                // --- GESTIÓN AS 1: ZONA 1 (MIDLINE STOP) ---
                else if (currentAce == AceType.As1_Zona1_100Bar)
                {
                    double midlineSl = keltnerBig.Midline[0] - (SlOffsetTicks * TickSize);
                    if (Close[0] <= midlineSl || Low[0] <= midlineSl)
                    {
                        ExitLong("SL_Midline_Zona1", "Z1-100Bar_Long");
                        Print(string.Format("{0} - [PAS - Z1-100Bar | SL MIDLINE] Precio tocó Midline ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                            Time[0], midlineSl, Close[0]));
                    }
                }
                // --- GESTIÓN AS 4: CASCADA 50 (MIDLINE STOP CEÑIDO) ---
                else if (currentAce == AceType.As4_Cascada50)
                {
                    double midlineSl = keltnerBig.Midline[0] - (2 * TickSize);
                    if (Close[0] <= midlineSl || Low[0] <= midlineSl)
                    {
                        ExitLong("SL_Midline_Cascada", "Cascada50_Long");
                        Print(string.Format("{0} - [PAS - Cascada50 | SL MIDLINE] Precio perforó Midline -2tk ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                            Time[0], midlineSl, Close[0]));
                    }
                }
            }
            // =========================================================================
            // SALIDAS EN POSICIÓN CORTA (ESPEJO INVERSO)
            // =========================================================================
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double currentGainTicks = (entryPrice - Low[0]) / TickSize;
                double currentGainDollars = currentGainTicks * (Instrument.MasterInstrument.PointValue * TickSize);
                double peakGainTicks = (entryPrice - lowestPriceSinceEntry) / TickSize;
                double peakGainDollars = peakGainTicks * (Instrument.MasterInstrument.PointValue * TickSize);

                // --- AS 2: SOBB (SURFER OUT BIG BANDA - ESCALONES $180, $220, $280) ---
                if (currentAce == AceType.As2_SOBB)
                {
                    // PISO 1: Al alcanzar $180 ES / $18 MES
                    if (!sobbLevel1Achieved && (currentGainDollars >= SobbLevel1Dollars || peakGainDollars >= SobbLevel1Dollars))
                    {
                        sobbLevel1Achieved = true;
                        if (currentQty >= 3)
                        {
                            ExitShort(1, "SOBB_Piso1_Asegurador", "SOBB_Short");
                            Print(string.Format("{0} - [PAS - SOBB | ESCALÓN 1 SUPERADO] Ganancia: {1:C2} (+{2:F1} tks). Contrato 1 liquidado. Stops restantes blindados en BREAKEVEN -1tk ({3:F2}). ¡Riesgo CERO!",
                                Time[0], Math.Max(currentGainDollars, peakGainDollars), Math.Max(currentGainTicks, peakGainTicks), entryPrice - (1 * TickSize)));
                        }
                    }

                    // PISO 2: Al alcanzar $220 ES / $22 MES
                    if (sobbLevel1Achieved && !sobbLevel2Achieved && (currentGainDollars >= SobbLevel2Dollars || peakGainDollars >= SobbLevel2Dollars))
                    {
                        sobbLevel2Achieved = true;
                        if (currentQty >= 2)
                        {
                            ExitShort(1, "SOBB_Piso2_Estructural", "SOBB_Short");
                            Print(string.Format("{0} - [PAS - SOBB | ESCALÓN 2 SUPERADO] Ganancia: {1:C2} (+{2:F1} tks). Contrato 2 liquidado en zona de expansión.",
                                Time[0], Math.Max(currentGainDollars, peakGainDollars), Math.Max(currentGainTicks, peakGainTicks)));
                            Print(string.Format("{0} - [PAS - SOBB | ANCLAJE BANDA 3.5] Stop de Contrato 3 anclado al piso de Banda Lower 3.5 +1tk ({1:F2}).",
                                Time[0], keltnerBig.Lower[0] + (1 * TickSize)));
                        }
                    }

                    // PISO 3: Al superar $280 ES / $28 MES (Modo Ahogo / Trailing Ceñido para Contrato 3)
                    if (sobbLevel2Achieved && !sobbLevel3ChokeActive && (currentGainDollars >= SobbLevel3Dollars || peakGainDollars >= SobbLevel3Dollars))
                    {
                        sobbLevel3ChokeActive = true;
                        Print(string.Format("{0} - [PAS - SOBB | ESCALÓN 3 - MODO AHOGO ACTIVADO] Ganancia: {1:C2} (+{2:F1} tks). Contrato 3 Runner en Modo Ahogo a {3} ticks del Low.",
                            Time[0], Math.Max(currentGainDollars, peakGainDollars), Math.Max(currentGainTicks, peakGainTicks), SobbChokeTicks));
                    }

                    // EJECUCIÓN DE STOPS DINÁMICOS DEL SOBB
                    if (sobbLevel3ChokeActive)
                    {
                        double chokeSlPrice = lowestPriceSinceEntry + (SobbChokeTicks * TickSize);
                        if (Close[0] >= chokeSlPrice || High[0] >= chokeSlPrice)
                        {
                            ExitShort("SOBB_Runner_Ahogo", "SOBB_Short");
                            Print(string.Format("{0} - [PAS - SOBB | RUNNER COBRADO POR AHOGO] Salida a Mercado @ ~{1:F2} | Ganancia asegurada en el piso.",
                                Time[0], Close[0]));
                        }
                    }
                    else if (sobbLevel2Achieved)
                    {
                        double band35Sl = keltnerBig.Lower[0] + (1 * TickSize);
                        if (Close[0] >= band35Sl || High[0] >= band35Sl)
                        {
                            ExitShort("SOBB_Stop_Banda35", "SOBB_Short");
                            Print(string.Format("{0} - [PAS - SOBB | SALIDA EN PISO BANDA 3.5] Precio superó Banda 3.5 ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                Time[0], band35Sl, Close[0]));
                        }
                    }
                    else if (sobbLevel1Achieved)
                    {
                        double bePrice = entryPrice - (1 * TickSize);
                        if (Close[0] >= bePrice || High[0] >= bePrice)
                        {
                            ExitShort("SOBB_Stop_BreakEven", "SOBB_Short");
                            Print(string.Format("{0} - [PAS - SOBB | SALIDA EN BREAKEVEN] Posición restante protegida @ {1:F2}. Salida sin pérdida.",
                                Time[0], bePrice));
                        }
                    }
                    else
                    {
                        double lockSl = keltnerSmall.Lower[0] + (1 * TickSize);
                        if (Close[0] >= lockSl || High[0] >= lockSl)
                        {
                            ExitShort("SOBB_Stop_Candado", "SOBB_Short");
                            Print(string.Format("{0} - [PAS - SOBB | SALIDA CANDADO 1.75] Precio superó Lower 1.75 ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                Time[0], lockSl, Close[0]));
                        }
                    }

                    // Salida por Fin de Ciclo (EMA 200 corta banda superior 3.5 para SOBB)
                    if (ema200[0] >= keltnerBig.Upper[0])
                    {
                        ExitShort("Exit_SOBB_FinCiclo", "SOBB_Short");
                        Print(string.Format("{0} - [PAS - SOBB | FIN DE CICLO] EMA200 ({1:F2}) cortó Upper 3.5 ({2:F2}) | Cierre total a Mercado @ ~{3:F2}",
                            Time[0], ema200[0], keltnerBig.Upper[0], Close[0]));
                    }
                }
                // --- AS 3: RATCHET (EL CHICLE PARABÓLICO - ESCALONAMIENTO POR FASES DE CANAL) ---
                else if (currentAce == AceType.As3_Ratchet)
                {
                    double maxProfitTicks = (entryPrice - lowestPriceSinceEntry) / TickSize;

                    // CONTRATO 1: Se baja del bus al tocar Banda Inferior 3.5 O al alcanzar BreakEven (+22 ticks)
                    if (!ratchetC1Achieved && (Low[0] <= keltnerBig.Lower[0] || maxProfitTicks >= RatchetBreakEvenTicks))
                    {
                        ratchetC1Achieved = true;
                        if (currentQty >= 3)
                        {
                            ExitShort(1, "Ratchet_C1_Banda35", "Ratchet_Short");
                            Print(string.Format("{0} - [PAS - Ratchet | FASE 1 COMPLETADA] Precio tocó Banda Inferior 3.5 ({1:F2}) / +{2:F1} tks. Contrato 1 liquidado. Stops restantes blindados en BREAKEVEN -1tk ({3:F2}). ¡Riesgo CERO!",
                                Time[0], keltnerBig.Lower[0], maxProfitTicks, entryPrice - (1 * TickSize)));
                        }
                    }

                    // CONTRATO 2: Se baja del bus si alcanza Fase 3 Choke (72 ticks) O si supera Línea Media
                    if (ratchetC1Achieved && !ratchetC2Achieved)
                    {
                        // Si vuela a la Fase 3 Choke (72 ticks = $900 ES / $90 MES)
                        if (maxProfitTicks >= RatchetChokeThresholdTicks)
                        {
                            ratchetC2Achieved = true;
                            ratchetChokeActive = true;
                            if (currentQty >= 2)
                            {
                                ExitShort(1, "Ratchet_C2_ChokeTrigger", "Ratchet_Short");
                                Print(string.Format("{0} - [PAS - Ratchet | FASE 3 ESTRANGULAMIENTO] Superó {1} ticks. Contrato 2 cobrado. Contrato 3 Runner en Chicle Parabólico a {2} ticks del Low.",
                                    Time[0], RatchetChokeThresholdTicks, RatchetChicleTicks));
                            }
                        }
                        // Si supera la Línea Media tras haber tocado la banda inferior
                        else if (Close[0] >= keltnerBig.Midline[0] + (1 * TickSize) || High[0] >= keltnerBig.Midline[0] + (1 * TickSize))
                        {
                            ratchetC2Achieved = true;
                            if (currentQty >= 2)
                            {
                                ExitShort(1, "Ratchet_C2_Midline", "Ratchet_Short");
                                Print(string.Format("{0} - [PAS - Ratchet | FASE 2 CUMPLIDA] Precio perforó Línea Media ({1:F2}). Contrato 2 liquidado en resguardo estructural.",
                                    Time[0], keltnerBig.Midline[0]));
                            }
                        }
                    }

                    // CONTRATO 3 (RUNNER): El Chicle Parabólico
                    if (ratchetChokeActive || maxProfitTicks >= RatchetChokeThresholdTicks)
                    {
                        double chokeSl = lowestPriceSinceEntry + (RatchetChicleTicks * TickSize);
                        if (Close[0] >= chokeSl || High[0] >= chokeSl)
                        {
                            ExitShort("Ratchet_C3_RunnerChicle", "Ratchet_Short");
                            Print(string.Format("{0} - [PAS - Ratchet | RUNNER LIQUIDADO EN EL PISO] Chicle Parabólico ejecutado a ~{1:F2} (Piso: {2:F2}). ¡Ganancia máxima asegurada!",
                                Time[0], Close[0], lowestPriceSinceEntry));
                        }
                    }
                    // Stop de resguardo antes de entrar en Choke:
                    else if (ratchetC1Achieved)
                    {
                        double bePrice = entryPrice - (1 * TickSize);
                        if (Close[0] >= bePrice || High[0] >= bePrice)
                        {
                            ExitShort("Ratchet_Stop_BE", "Ratchet_Short");
                            Print(string.Format("{0} - [PAS - Ratchet | SALIDA EN BREAKEVEN] Posición restante protegida en {1:F2}. Salida sin pérdida.",
                                Time[0], bePrice));
                        }
                    }
                    else
                    {
                        // Stop inicial estructural: Banda Keltner Superior + 1 tick
                        double initialSl = keltnerBig.Upper[0] + (SlOffsetTicks * TickSize);
                        if (Close[0] >= initialSl || High[0] >= initialSl)
                        {
                            ExitShort("Ratchet_Stop_Inicial", "Ratchet_Short");
                            Print(string.Format("{0} - [PAS - Ratchet | STOP INICIAL] Precio superó Banda Superior ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                Time[0], initialSl, Close[0]));
                        }
                    }
                }
                // --- GESTIÓN AS 1: ZONA 1 (MIDLINE STOP) ---
                else if (currentAce == AceType.As1_Zona1_100Bar)
                {
                    double midlineSl = keltnerBig.Midline[0] + (SlOffsetTicks * TickSize);
                    if (Close[0] >= midlineSl || High[0] >= midlineSl)
                    {
                        ExitShort("SL_Midline_Zona1", "Z1-100Bar_Short");
                        Print(string.Format("{0} - [PAS - Z1-100Bar | SL MIDLINE] Precio tocó Midline ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                            Time[0], midlineSl, Close[0]));
                    }
                }
                // --- GESTIÓN AS 4: CASCADA 50 (MIDLINE STOP CEÑIDO) ---
                else if (currentAce == AceType.As4_Cascada50)
                {
                    double midlineSl = keltnerBig.Midline[0] + (2 * TickSize);
                    if (Close[0] >= midlineSl || High[0] >= midlineSl)
                    {
                        ExitShort("SL_Midline_Cascada", "Cascada50_Short");
                        Print(string.Format("{0} - [PAS - Cascada50 | SL MIDLINE] Precio superó Midline +2tk ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                            Time[0], midlineSl, Close[0]));
                    }
                }
            }
        }

        private void LiquidateAllMarket(string reason)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(reason, "");
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(reason, "");
        }

        private void ResetAllTradeCounters()
        {
            currentAce = AceType.None;
            sobbLevel1Achieved = false;
            sobbLevel2Achieved = false;
            sobbLevel3ChokeActive = false;

            ratchetSetup = RatchetSetup.None;
            ratchetSetupBarCounter = 0;
            ratchetC1Achieved = false;
            ratchetC2Achieved = false;
            ratchetChokeActive = false;

            highestPriceSinceEntry = 0.0;
            lowestPriceSinceEntry = double.MaxValue;
        }

        #endregion

        #region Control de Cierre de Posición y Estadísticas Diarias
        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            if (marketPosition == MarketPosition.Flat && SystemPerformance.AllTrades.Count > lastReportedTradeCount)
            {
                int newTradesCount = SystemPerformance.AllTrades.Count - lastReportedTradeCount;
                double sessionTradesProfit = 0.0;

                for (int i = SystemPerformance.AllTrades.Count - newTradesCount; i < SystemPerformance.AllTrades.Count; i++)
                {
                    Trade trade = SystemPerformance.AllTrades[i];
                    double netProfit = trade.ProfitCurrency;
                    dailyRealizedPnL += netProfit;
                    sessionTradesProfit += netProfit;

                    if (netProfit < 0)
                        consecutiveLosses++;
                    else if (netProfit > 0)
                        consecutiveLosses = 0;

                    Print(string.Format("{0} - [PAS | TRADE COMPLETADO] Salida: {1} | PnL: {2:C2} | PnL Diario Realizado: {3:C2} | Strikes: {4}",
                        trade.Exit.Time, trade.Exit.Name, netProfit, dailyRealizedPnL, consecutiveLosses));
                }

                lastReportedTradeCount = SystemPerformance.AllTrades.Count;

                // Si se cerró Cascada 50, se resetea su contador para permitir re-entradas si la tendencia persiste
                if (currentAce == AceType.As4_Cascada50)
                {
                    barsCascadaShortCount = 0;
                    barsCascadaLongCount = 0;
                    Print(Time[0] + " - [PAS - Cascada50] Posición cerrada. Contador reseteado a 0; re-entrará si la tendencia acumula 50 barras más.");
                }

                ResetAllTradeCounters();

                // EVALUACIÓN DE META DIARIA Y 3 STRIKES AL CERRAR OPERACIONES
                if (UseDailyManagement && !isDailyLocked)
                {
                    if (dailyRealizedPnL >= DailyProfitGoal)
                    {
                        isDailyLocked = true;
                        dailyLockReason = string.Format("META DIARIA (+${0:F2}) LOGRADA", DailyProfitGoal);
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [META DIARIA ALCANZADA] PnL Diario: ${1:F2} (Meta: +${2:F2}). ¡A tomar tinto y montar bicicleta!\n" +
                                           "Operativa bloqueada por el resto de la sesión.\n" +
                                           "****************************************************************************************",
                            Time[0], dailyRealizedPnL, DailyProfitGoal));
                    }
                    else if (consecutiveLosses >= MaxConsecutiveLosses)
                    {
                        isDailyLocked = true;
                        dailyLockReason = string.Format("{0} STRIKES CONSECUTIVOS", MaxConsecutiveLosses);
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [REGLA DE 3 STRIKES] {1} pérdidas consecutivas registradas. PnL Diario: ${2:F2}.\n" +
                                           "Operativa bloqueada por el resto de la sesión para proteger capital.\n" +
                                           "****************************************************************************************",
                            Time[0], MaxConsecutiveLosses, dailyRealizedPnL));
                    }
                }
            }
        }
        #endregion

        #region Interfaz de Control WPF (Botón de Pánico y Atajo Ctrl + Espacio)
        private void CreateWPFControls()
        {
            chartWindow = Window.GetWindow(ChartControl.Parent) as Chart;
            if (chartWindow != null && !isSubscribedToKeys)
            {
                chartWindow.KeyDown += OnChartKeyDown;
                isSubscribedToKeys = true;
            }

            panicButton = new System.Windows.Controls.Button
            {
                Content = isStrategyActive ? "ESTRATEGIA ACTIVA (PAS)" : "ESTRATEGIA PAUSADA (PAS)",
                Height = 30,
                Width = 230,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 10, 0),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Foreground = Brushes.White,
                Background = isStrategyActive ? Brushes.DarkGreen : Brushes.Crimson,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                Cursor = Cursors.Hand,
                ToolTip = "Clic o presiona [Ctrl + Espacio] para Alternar / PÁNICO (Liquida posiciones inmediatamente)."
            };

            panicButton.Click += (s, e) => { ToggleStrategyState(); };
            UserControlCollection.Add(panicButton);
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
                panicButton.Content = isStrategyActive ? "ESTRATEGIA ACTIVA (PAS)" : "ESTRATEGIA PAUSADA (PAS)";
                panicButton.Background = isStrategyActive ? Brushes.DarkGreen : Brushes.Crimson;
            }

            if (!isStrategyActive)
            {
                TriggerCustomEvent(o =>
                {
                    if (Position.MarketPosition != MarketPosition.Flat)
                    {
                        LiquidateAllMarket("Boton_Panico_WPF");
                        Print(Time[0] + " - [BOTÓN DE PÁNICO] ¡ORDEN DE PÁNICO EJECUTADA! Posiciones liquidadas a mercado.");
                    }
                    ResetAllTradeCounters();
                }, null);
            }
            else
            {
                Print(Time[0] + " - [CONTROL MANUAL] Estrategia reanudada por el usuario.");
            }
        }

        private void DisposeWPFControls()
        {
            if (chartWindow != null && isSubscribedToKeys)
            {
                chartWindow.KeyDown -= OnChartKeyDown;
                isSubscribedToKeys = false;
            }

            if (panicButton != null)
            {
                UserControlCollection.Remove(panicButton);
                panicButton = null;
            }
        }
        #endregion

        #region Propiedades Expuestas en NinjaTrader UI
        [NinjaScriptProperty]
        [Display(Name = "Versión", GroupName = "1. Información", Order = 1)]
        [ReadOnly(true)]
        public string Version { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Número de Contratos", Description = "Contratos a operar (1, 2 o 3 para escuadrón completo)", GroupName = "2. Gestión de Posición", Order = 1)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hora Inicio RTH (HHMMSS)", Description = "09:50 AM = 95000", GroupName = "3. Horario Operativo", Order = 1)]
        public int StartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hora Fin Entradas (HHMMSS)", Description = "03:45 PM = 154500", GroupName = "3. Horario Operativo", Order = 2)]
        public int StopEntriesTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hora Cierre Forzoso (HHMMSS)", Description = "04:00 PM = 160000", GroupName = "3. Horario Operativo", Order = 3)]
        public int ForceCloseTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Gestión Diaria", Description = "Activa Cortacircuito y Meta Diaria", GroupName = "4. Escudo de Cuenta", Order = 1)]
        public bool UseDailyManagement { get; set; }

        [NinjaScriptProperty]
        [Range(10.0, 5000.0)]
        [Display(Name = "Cortacircuito Diario ($)", Description = "Pérdida máxima diaria para apagar bot ($300 ES / $30 MES)", GroupName = "4. Escudo de Cuenta", Order = 2)]
        public double DailyLossLimit { get; set; }

        [NinjaScriptProperty]
        [Range(10.0, 10000.0)]
        [Display(Name = "Meta Diaria ($)", Description = "Ganancia objetivo para cerrar sesión ($600 ES / $60 MES)", GroupName = "4. Escudo de Cuenta", Order = 3)]
        public double DailyProfitGoal { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Máximo Strikes Seguidos", Description = "Número de pérdidas seguidas para apagar por el día", GroupName = "4. Escudo de Cuenta", Order = 4)]
        public int MaxConsecutiveLosses { get; set; }

        [NinjaScriptProperty]
        [Range(10.0, 1000.0)]
        [Display(Name = "Escuadrón Escalón 1 ($)", Description = "Flotante para cerrar Contrato 1 y armar BE ($180 ES / $18 MES)", GroupName = "5. Escuadrón 3 Contratos (Escalones)", Order = 1)]
        public double SobbLevel1Dollars { get; set; }

        [NinjaScriptProperty]
        [Range(10.0, 1000.0)]
        [Display(Name = "Escuadrón Escalón 2 ($)", Description = "Flotante para cerrar Contrato 2 en Banda 3.5 ($220 ES / $22 MES)", GroupName = "5. Escuadrón 3 Contratos (Escalones)", Order = 2)]
        public double SobbLevel2Dollars { get; set; }

        [NinjaScriptProperty]
        [Range(10.0, 2000.0)]
        [Display(Name = "Escuadrón Escalón 3 ($)", Description = "Flotante para activar Modo Ahogo ($280 ES / $28 MES)", GroupName = "5. Escuadrón 3 Contratos (Escalones)", Order = 3)]
        public double SobbLevel3Dollars { get; set; }

        [NinjaScriptProperty]
        [Range(2, 20)]
        [Display(Name = "Escuadrón Distancia Ahogo (Ticks)", Description = "Ticks de separación del High/Low para el trailing del Runner", GroupName = "5. Escuadrón 3 Contratos (Escalones)", Order = 4)]
        public int SobbChokeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 300)]
        [Display(Name = "Z1 Barras Mínimas", Description = "Mínimo de barras comprimidas sin cruzar EMA 200", GroupName = "6. As 1: Zona 1", Order = 1)]
        public int MinBarsZona1 { get; set; }

        [NinjaScriptProperty]
        [Range(4, 50)]
        [Display(Name = "Z1 TP Fijo (Ticks)", Description = "Target de salida de Zona 1 (12 ticks = $150 ES / $15 MES)", GroupName = "6. As 1: Zona 1", Order = 2)]
        public int Zone1FixedTpTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 200)]
        [Display(Name = "Cascada Barras Mínimas", Description = "Barras con EMA 200 separada de Banda 3.5", GroupName = "7. As 4: Cascada 50", Order = 1)]
        public int CascadaMinBars { get; set; }

        [NinjaScriptProperty]
        [Range(4, 50)]
        [Display(Name = "Cascada TP Fijo (Ticks)", Description = "Target de salida de Cascada 50 (16 ticks = $200 ES / $20 MES)", GroupName = "7. As 4: Cascada 50", Order = 2)]
        public int CascadaFixedTpTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "Período TEMA", GroupName = "8. Indicadores", Order = 1)]
        public int TemaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(20, 500)]
        [Display(Name = "Período EMA 200", GroupName = "8. Indicadores", Order = 2)]
        public int EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(10, 200)]
        [Display(Name = "Período Keltner", GroupName = "8. Indicadores", Order = 3)]
        public int KeltnerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 6.0)]
        [Display(Name = "Multiplicador Keltner Grande", GroupName = "8. Indicadores", Order = 4)]
        public double KeltnerBigMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 3.0)]
        [Display(Name = "Multiplicador Keltner Pequeño", GroupName = "8. Indicadores", Order = 5)]
        public double KeltnerSmallMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 10.0)]
        [Display(Name = "Mínimo Cruce TEMA (Ticks)", GroupName = "8. Indicadores", Order = 6)]
        public double MinCrossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Offset Stop Loss (Ticks)", GroupName = "8. Indicadores", Order = 7)]
        public int SlOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(2, 20)]
        [Display(Name = "Colchón Mínimo Seguridad SL (Ticks)", GroupName = "8. Indicadores", Order = 8)]
        public int MinSafetySlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "Ratchet Lookback Barras", Description = "Barras sin tocar EMA 200", GroupName = "9. As 3: Ratchet", Order = 1)]
        public int RatchetLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(5, 30)]
        [Display(Name = "Ratchet Countdown Barras", Description = "Ventana máxima para cruzar Línea Media", GroupName = "9. As 3: Ratchet", Order = 2)]
        public int RatchetCountdownBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Ratchet Tolerancia Gancho TEMA (Ticks)", Description = "Tolerancia para tocar banda en el gancho", GroupName = "9. As 3: Ratchet", Order = 3)]
        public int RatchetTemaTolTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 50)]
        [Display(Name = "Ratchet BreakEven Ticks", Description = "Ticks para liquidar C1 y armar BE (+22 ticks)", GroupName = "9. As 3: Ratchet", Order = 4)]
        public int RatchetBreakEvenTicks { get; set; }

        [NinjaScriptProperty]
        [Range(20, 150)]
        [Display(Name = "Ratchet Choke Threshold Ticks", Description = "Ticks para activar Fase 3 Choke (72 ticks = $900 ES / $90 MES)", GroupName = "9. As 3: Ratchet", Order = 5)]
        public int RatchetChokeThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(2, 20)]
        [Display(Name = "Ratchet Chicle Trailing (Ticks)", Description = "Distancia ceñida del Chicle Parabólico para C3 Runner", GroupName = "9. As 3: Ratchet", Order = 6)]
        public int RatchetChicleTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Cascada Separación Ticks", GroupName = "7. As 4: Cascada 50", Order = 3)]
        public int CascadaSeparationTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Cascada Pendiente Mínima Ticks", GroupName = "7. As 4: Cascada 50", Order = 4)]
        public int CascadaMinSlopeTicks { get; set; }
        #endregion
    }
}
