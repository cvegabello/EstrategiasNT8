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
// VERSIÓN: 3.0 - Poker de Ases (PAS) [Filtro Zonas 0/1 en Cascada, Desescalado Dinámico & Victoria 1:00 PM]
//
// LAS 4 CARTAS DEL POKER (LOS 4 ASES):
//   1. As 1: [Z1-100Bar] - Ruptura quirúrgica tras >= 100 barras comprimidas en EMA 200 con filtro anti-Zona 0 y SL tope.
//                          (Fiel al SniperSurfer original con TEMA 9 y MinCrossTicks 2.3).
//   2. As 2: [SOBB]      - SurferOutBigBanda: Cruce TEMA 9 + Disparo en Primera Vela Fuera (One-Shot Estricto)
//                          con SL Escalonado Estructural (C1 Pequeña, C2 Midline, C3 Pequeña Opuesta).
//   3. As 3: [Ratchet]   - Súper-tendencia sostenida con filtro de 20 barras, gatillo en Midline y Chicle Parabólico.
//   4. As 4: [Cascada]   - El Tren Bala Desbocado (25 barras): FILTRO ESTRUCTURAL ESTRICTO DE ZONAS 0/1 (Largos)
//                          y ZONAS 4/5 (Cortos). Prohibido comprar en Zona 2 (agotamiento de tendencia).
//
// GESTIÓN DE RIESGO INTELIGENTE (DESESCALADO DINÁMICO & CORTE HORARIO A LA 1:00 PM):
//   - Desescalado Dinámico: Si el PnL está cerca del piso protegido, reduce la posición (a 2 o 1 contrato) para garantizar
//     un Stop Loss saludable de al menos 8 ticks sin romper el piso garantizado ($500 USD).
//   - Victoria Temprana a la 1:00 PM: Si a las 01:00 PM EST estamos en Escalón 1 ($400 - $599 USD), apaga nuevas entradas y
//     asegura la ganancia del día para evitar amagues de bajo volumen en la sesión de la tarde.
//
// ESCUDO DE CUENTA (CALIBRACIÓN INSTITUCIONAL 3 VIDAS - TRAILING INFINITO - BLINDAJE v3.0):
//   - Cortacircuito Inicial: -$800 USD (ES) / -$80 USD (MES) -> Respaldo para 2.5 a 3 trades de 3 contratos.
//   - Escalón 1 (+$400): Piso protegido en +$250 USD (Corte automático a la 1:00 PM para asegurar victoria).
//   - Escalón 2 (+$600): Piso protegido CLAVADO en +$500 USD (inviolable, comprobación continua en cada barra).
//   - Modo Dulce (+$900+): Trailing Infinito (Piso = Récord Ganado - Colchón $150). ¡No te para de ganar!
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

        // === VARIABLES DEL AS 1: ZONA 1 (100 BARRAS ESTRUCTURAL EMA EXTERNA) ===
        private int lastTemaSide = 0;              // 1 = Arriba de EMA200, -1 = Debajo de EMA200
        private bool crossUpConsumed = false;
        private bool crossDownConsumed = false;
        private int barsBelowEmaWithoutCross = 0;  // Acumulación continua con EMA200 sobre Upper 1.75
        private int barsAboveEmaWithoutCross = 0;  // Acumulación continua con EMA200 bajo Lower 1.75
        private int priorBarsBelowEma = 0;         // Barras acumuladas antes del cruce alcista
        private int priorBarsAboveEma = 0;         // Barras acumuladas antes del cruce bajista
        private bool z1LongArmed = false;          // Acumulación de 100 barras completada para Largo
        private bool z1ShortArmed = false;         // Acumulación de 100 barras completada para Corto

        // === VARIABLES DEL AS 2: SOBB (SurferOutBigBanda - GATILLO 2 CAPAS) ===
        private bool sobbArmedLong = false;        // Fase 1: TEMA 9 cruzó Upper 3.5 (Espera activa a que la vela salga)
        private bool sobbArmedShort = false;       // Fase 1: TEMA 9 cruzó Lower 3.5 (Espera activa a que la vela salga)
        private bool sobbHasFloatedLong = false;   // Bandera de vela flotando afuera para Largos
        private bool sobbHasFloatedShort = false;  // Bandera de vela flotando afuera para Cortos
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

        // === VARIABLES DEL AS 4: CASCADA 25 BARRAS (ESCALONADA) ===
        private int barsCascadaShortCount = 0;     // Barras con EMA200 > Upper 3.5 + 4 ticks
        private int barsCascadaLongCount  = 0;     // Barras con EMA200 < Lower 3.5 - 4 ticks
        private bool cascadaC1Achieved    = false; // Contrato 1 cerrado a +8 ticks (BE armado)
        private bool cascadaC2Achieved    = false; // Contrato 2 cerrado a +16 ticks
        private bool cascadaChokeActive   = false; // Contrato 3 Runner en Modo Ahogo (8 ticks)

        // Tracking de MFE/MAE de la posición activa
        private double entryPriceGlobal = 0.0;
        private double highestPriceSinceEntry = 0.0;
        private double lowestPriceSinceEntry = double.MaxValue;

        // === ESCUDO DIARIO Y GESTIÓN DE RIESGO DE CUENTA (TRAILING INFINITO) ===
        private double dailyRealizedPnL = 0.0;
        private int consecutiveLosses = 0;
        private bool isDailyLocked = false;
        private string dailyLockReason = string.Empty;
        private int lastSessionDate = 0;
        private double currentDailyFloor = -800.0; // Piso de pérdida en vivo (Calibrado a -$800 ES / -$80 MES)
        private int currentDailyTier = 0;          // 0 = Base (-$800), 1 = Piso 1 (+$300), 2 = Piso 2 (+$550), 3 = Trailing Infinito
        private double peakDailyClosedPnL = 0.0;   // Máximo PnL diario cerrado para arrastre del Trailing Infinito
        private int activeTradeContracts = 3;      // Contratos asignados dinámicamente según el margen del piso

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
                Description                                 = @"Estrategia Institucional Poker de Ases Sniper (PAS) v3.0 en 610 Ticks para ES/MES. Combina Z1-100Bar (Filtro Zona 0), SOBB (Primera Vela One-Shot & SL Escalonado Canales), Ratchet, Cascada 25 (Filtro Estructural Zonas 0/1 y 4/5), Desescalado Dinámico de Contratos, Evaluación Continua de Pisos Diarios y Victoria Temprana a la 1:00 PM.";
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
                Version                                     = "3.5 - Poker de Ases (PAS) [SOBB Filtro 2 Capas EMA/Pendientes & Corte Universal 1:00 PM]";

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

                // Parámetros del As 4: Cascada 25 Barras (Escalonamiento 3 Fases + 6 Zonas Keltner)
                CascadaMinBars                              = 25;     // 25 barras consecutivas requeridas (calibración óptima)
                CascadaSeparationTicks                      = 4;      // Mínimo 4 ticks de separación de la Banda 3.5
                CascadaMinSlopeTicks                        = 2;      // Inclinación mínima requerida en la EMA 200
                CascadaTp1Ticks                             = 8;      // C1 TP (8 ticks = $100 ES / $10 MES) -> Pasa a BE +1tk
                CascadaTp2Ticks                             = 16;     // C2 TP (16 ticks = $200 ES / $20 MES) -> Meta clásica
                CascadaChokeTicks                           = 8;      // C3 Runner Ahogo a 8 ticks del High/Low

                // Escudo Diario de Cuenta (Trailing Diario Infinito - Calibración Ceñida v2.0 para 3 Contratos)
                UseDailyManagement                          = true;   // Activar Cortacircuito y Trailing Diario
                DailyLossLimit                              = 800.0;  // Cortacircuito estricto inicial: -$800 ES / -$80 MES (2.5 - 3 vidas)
                DailyGoal_Piso1                             = 400.0;  // Meta Escalón 1: +$400 ES / +$40 MES (Piso a +$250)
                DailyFloor_Piso1                            = 250.0;  // Piso protegido Escalón 1: +$250 ES / +$25 MES
                DailyGoal_Piso2                             = 600.0;  // Meta Escalón 2: +$600 ES / +$60 MES (Piso a +$500)
                DailyFloor_Piso2                            = 500.0;  // Piso protegido Escalón 2: +$500 ES / +$50 MES
                DailyGoal_Piso3                             = 900.0;  // Umbral Modo Dulce: +$900 ES / +$90 MES (Activa Trailing Infinito)
                DailyTrailingGiveback                       = 150.0;  // Colchón de retroceso permitido del pico ganado ($150 ES / $15 MES)
                MaxConsecutiveLosses                        = 3;      // Regla de 3 Strikes por Operación Global
                UseTier1LunchCutoff                         = true;   // Activar Victoria Temprana en Escalón 1 a la 1:00 PM EST
                Tier1CutoffTime                             = 130000; // 01:00 PM EST (Hora de corte para Escalón 1)
            }
            else if (State == State.DataLoaded)
            {
                // Auto-Detección de Instrumento (Mini ES vs Micro MES)
                isMicroInstrument = Instrument.MasterInstrument.Name.Contains("MES");
                instrumentMultiplier = isMicroInstrument ? 0.10 : 1.0;

                // Si es Micro MES, escalamos proporcionalmente los umbrales en dólares por defecto
                if (isMicroInstrument)
                {
                    DailyLossLimit        = 80.0;
                    DailyGoal_Piso1       = 40.0;
                    DailyFloor_Piso1      = 25.0;
                    DailyGoal_Piso2       = 60.0;
                    DailyFloor_Piso2      = 50.0;
                    DailyGoal_Piso3       = 90.0;
                    DailyTrailingGiveback = 15.0;
                    SobbLevel1Dollars     = 18.0;
                    SobbLevel2Dollars     = 22.0;
                    SobbLevel3Dollars     = 28.0;
                    currentDailyFloor     = -DailyLossLimit;
                    Print(string.Format("=== [PAS] INSTRUMENTO MICRO (MES) DETECTADO === Multiplicador 0.10x aplicado. Cortacircuito: -${0:F2} | Escalón 1: +${1:F2} (Piso +${2:F2}) | Escalón 2: +${3:F2} (Piso +${4:F2}) | Modo Dulce Trailing: +${5:F2} (Giveback ${6:F2})",
                        DailyLossLimit, DailyGoal_Piso1, DailyFloor_Piso1, DailyGoal_Piso2, DailyFloor_Piso2, DailyGoal_Piso3, DailyTrailingGiveback));
                }
                else
                {
                    currentDailyFloor = -DailyLossLimit;
                    Print(string.Format("=== [PAS] INSTRUMENTO ESTÁNDAR (ES) DETECTADO === Cortacircuito: -${0:F2} | Escalón 1: +${1:F2} (Piso +${2:F2}) | Escalón 2: +${3:F2} (Piso +${4:F2}) | Modo Dulce Trailing: +${5:F2} (Giveback ${6:F2})",
                        DailyLossLimit, DailyGoal_Piso1, DailyFloor_Piso1, DailyGoal_Piso2, DailyFloor_Piso2, DailyGoal_Piso3, DailyTrailingGiveback));
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

            // 1. ACTUALIZACIÓN DE CONTADORES DE MERCADO (HISTÓRICO Y TIEMPO REAL)
            // Es vital que los contadores de acumulación corran en CADA barra (incluso si está pausado
            // o fuera de horario) para que la memoria matemática de 100/150 barras nunca se pierda.
            UpdateMarketCounters();

            // 2. VERIFICACIÓN DE NUEVA SESIÓN DIARIA (RESET DE GESTIÓN DE RIESGO)
            int currentDate = Time[0].Date.DayOfYear;
            if (currentDate != lastSessionDate)
            {
                lastSessionDate = currentDate;
                dailyRealizedPnL = 0.0;
                consecutiveLosses = 0;
                isDailyLocked = false;
                dailyLockReason = string.Empty;
                currentDailyFloor = -DailyLossLimit;
                currentDailyTier = 0;
                peakDailyClosedPnL = 0.0;
                lastReportedTradeCount = (SystemPerformance != null && SystemPerformance.AllTrades != null) ? SystemPerformance.AllTrades.Count : 0;
                
                // Reseteo estricto de memoria acumulativa para evitar arrastres de días previos o pre-mercado
                barsBelowEmaWithoutCross = 0;
                barsAboveEmaWithoutCross = 0;
                priorBarsBelowEma = 0;
                priorBarsAboveEma = 0;
                z1LongArmed = false;
                z1ShortArmed = false;
                crossUpConsumed = false;
                crossDownConsumed = false;
                lastTemaSide = 0;
                barsCascadaLongCount = 0;
                barsCascadaShortCount = 0;

                Print(string.Format("==================== NUEVA SESIÓN RTH ({0:MM/dd/yyyy}) ==================== PnL Diario: $0.00 | Piso Inicial: -${1:F2}",
                    Time[0], DailyLossLimit));
            }

            // 2.1 SINCRONIZACIÓN CONTINUA DE TRADES, PNL Y PISOS PROTEGIDOS EN CADA BARRA
            SyncTradesAndStrikes();
            UpdateDailyPnLAndFloors();

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

            // 3.1 CORTACIRCUITO UNIVERSAL A LA 1:00 PM (ZONA BASE Y ESCALÓN 1)
            // Si son las 01:00 PM EST y el mercado no despegó a Escalón 2 (estamos en Base o Escalón 1),
            // se apaga la operativa para evitar operar en la tarde de bajo volumen y arriesgar pérdidas innecesarias.
            if (UseDailyManagement && UseTier1LunchCutoff && !isDailyLocked && timeNow >= Tier1CutoffTime)
            {
                if (currentDailyTier <= 1)
                {
                    if (Position.MarketPosition == MarketPosition.Flat)
                    {
                        isDailyLocked = true;
                        dailyLockReason = string.Format("CORTE DE OPERATIVA A LA 1:00 PM (PnL ${0:F2})", dailyRealizedPnL);
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [ESCUDO DIARIO | CORTE A LA 1:00 PM EST] Hora actual ({1}) alcanzó las 01:00 PM.\n" +
                                           "PnL Diario Realizado: ${2:F2} USD (Zona {3}). ¡APAGANDO OPERATIVA POR EL RESTO DEL DÍA!\n" +
                                           "No se regala dinero en la tarde sin volumen. ¡Día cerrado con disciplina!\n" +
                                           "****************************************************************************************",
                            Time[0], timeNow, dailyRealizedPnL, currentDailyTier == 1 ? "Escalón 1" : "Base"));
                        return;
                    }
                }
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

            // 5. CORTACIRCUITO DIARIO EN VIVO ("PUM, ADIÓS") Y PISO EXACTO POR TICKS
            if (UseDailyManagement)
            {
                double unrealizedPnL = 0.0;
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    unrealizedPnL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
                }
                double totalDailyFloatingPnL = dailyRealizedPnL + unrealizedPnL;

                // CORTACIRCUITO DE PISO EXACTO EN TIEMPO REAL:
                // Si el piso protegido está activo y el PnL cerrado lo supera, calculamos el precio
                // exacto en ticks donde el retroceso alcanzaría el piso, para liquidar sin esperar slippage de barra.
                if (!isDailyLocked && Position.MarketPosition != MarketPosition.Flat && dailyRealizedPnL > currentDailyFloor)
                {
                    double allowedLossDollars = dailyRealizedPnL - currentDailyFloor;
                    double tickValue = TickSize * Instrument.MasterInstrument.PointValue;
                    double dollarsPerTickPosition = Position.Quantity * tickValue;

                    if (dollarsPerTickPosition > 0)
                    {
                        int maxLossTicks = (int)Math.Floor(allowedLossDollars / dollarsPerTickPosition);

                        if (Position.MarketPosition == MarketPosition.Long)
                        {
                            double exactFloorPrice = Instrument.MasterInstrument.RoundToTickSize(Position.AveragePrice - (maxLossTicks * TickSize));
                            if (Low[0] <= exactFloorPrice || Close[0] <= exactFloorPrice)
                            {
                                isDailyLocked = true;
                                dailyLockReason = string.Format("PISO EXACTO ALCANZADO (${0:F2})", currentDailyFloor);
                                Print(string.Format("****************************************************************************************\n" +
                                                    "{0} - [ESCUDO DIARIO | PISO EXACTO ALCANZADO] Precio tocó {1:F2} (Límite {2} ticks desde entrada {3:F2}).\n" +
                                                    "¡PUM, ADIÓS! Liquidando a mercado en el tick exacto para asegurar Piso de ${4:F2} USD.\n" +
                                                    "****************************************************************************************",
                                    Time[0], exactFloorPrice, maxLossTicks, Position.AveragePrice, currentDailyFloor));
                                LiquidateAllMarket("Cortacircuito_Diario_Piso_Exacto");
                                return;
                            }
                        }
                        else if (Position.MarketPosition == MarketPosition.Short)
                        {
                            double exactFloorPrice = Instrument.MasterInstrument.RoundToTickSize(Position.AveragePrice + (maxLossTicks * TickSize));
                            if (High[0] >= exactFloorPrice || Close[0] >= exactFloorPrice)
                            {
                                isDailyLocked = true;
                                dailyLockReason = string.Format("PISO EXACTO ALCANZADO (${0:F2})", currentDailyFloor);
                                Print(string.Format("****************************************************************************************\n" +
                                                    "{0} - [ESCUDO DIARIO | PISO EXACTO ALCANZADO] Precio tocó {1:F2} (Límite {2} ticks desde entrada {3:F2}).\n" +
                                                    "¡PUM, ADIÓS! Liquidando a mercado en el tick exacto para asegurar Piso de ${4:F2} USD.\n" +
                                                    "****************************************************************************************",
                                    Time[0], exactFloorPrice, maxLossTicks, Position.AveragePrice, currentDailyFloor));
                                LiquidateAllMarket("Cortacircuito_Diario_Piso_Exacto");
                                return;
                            }
                        }
                    }
                }

                // CORTACIRCUITO EN VIVO: Si el PnL total (realizado + flotante) toca el piso actual
                if (!isDailyLocked && totalDailyFloatingPnL <= currentDailyFloor)
                {
                    isDailyLocked = true;
                    dailyLockReason = string.Format("CORTACIRCUITO DIARIO ({0}) ALCANZADO (${1:F2})",
                        currentDailyTier == 0 ? "PÉRDIDA MÁXIMA" : ("PISO ESCALÓN " + currentDailyTier), currentDailyFloor);
                    
                    Print(string.Format("****************************************************************************************\n" +
                                        "{0} - [CORTACIRCUITO EN VIVO | PISO ACTIVADO] PnL total cayó a ${1:F2} (Piso protegido: ${2:F2}).\n" +
                                        "¡PUM, ADIÓS! Liquidando a mercado inmediatamente para proteger el capital planeado. Operativa bloqueada por hoy.\n" +
                                        "****************************************************************************************",
                        Time[0], totalDailyFloatingPnL, currentDailyFloor));
                    
                    if (Position.MarketPosition != MarketPosition.Flat)
                        LiquidateAllMarket("Cortacircuito_Diario_Piso");
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

            // 7. EVALUACIÓN DE ENTRADAS DE LOS 4 ASES (SOLO SI BOTÓN ACTIVO, FLAT Y EN HORARIO VÁLIDO)
            if (isStrategyActive && Position.MarketPosition == MarketPosition.Flat && isTradingTime && !isDailyLocked)
            {
                // GESTIÓN DE RIESGO INTELIGENTE: Verificamos si existe margen para operar al menos 1 contrato con Stop saludable
                if (GetRiskAdjustedContracts() <= 0)
                {
                    // No hay margen suficiente ni para 1 contrato sin arriesgar el piso; protegemos el capital
                    return;
                }

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
            int timeNow = ToTime(Time[0]);

            // Inicialización de seguridad para lastTemaSide en la primera barra válida
            if (lastTemaSide == 0)
            {
                lastTemaSide = (tema9[1] <= ema200[1]) ? -1 : 1;
            }

            // === CONTADOR AS 1: ZONA 1 (Acumulación con EMA 200 FUERA del Canal Central) ===
            // Condición Estricta:
            // 1. Para LARGOS: La EMA 200 debe estar por FUERA del canal arriba (EMA200 >= Upper 1.75) y TEMA 9 abajo.
            // 2. Para CORTOS: La EMA 200 debe estar por FUERA del canal abajo (EMA200 <= Lower 1.75) y TEMA 9 arriba.
            // Si la EMA 200 está dentro del canal (entre Lower 1.75 y Upper 1.75), NO cuenta barras para Z1-100B.

            priorBarsBelowEma = barsBelowEmaWithoutCross;
            priorBarsAboveEma = barsAboveEmaWithoutCross;

            double currentUpTicks   = (tema9[0] - ema200[0]) / TickSize;
            double currentDownTicks = (ema200[0] - tema9[0]) / TickSize;

            double upperSmall = keltnerSmall.Upper[0];
            double lowerSmall = keltnerSmall.Lower[0];
            double midline    = keltnerBig.Midline[0];

            // --- Monitoreo de Acumulación para Largos: EMA 200 afuera por ARRIBA (>= Upper 1.75) y TEMA abajo ---
            bool emaFueraArriba = ema200[0] >= upperSmall;
            bool temaBajoEma    = currentUpTicks < MinCrossTicks;

            if (emaFueraArriba && temaBajoEma)
            {
                barsBelowEmaWithoutCross++;
                if (barsBelowEmaWithoutCross >= MinBarsZona1)
                {
                    z1LongArmed = true;
                }
                if (timeNow >= StartTime && timeNow <= ForceCloseTime)
                {
                    Print(string.Format("{0} - [PAS - Z1-100B | LARGO] Z1_100B_Barra {1}/{2} | EMA200 ({3:F2}) >= UpperSmall ({4:F2}) | TEMA9: {5:F2} | Armed: {6}",
                        Time[0], barsBelowEmaWithoutCross, MinBarsZona1, ema200[0], upperSmall, tema9[0], z1LongArmed));
                }
            }
            else if (Close[0] > ema200[0] || (z1LongArmed && ema200[0] < midline))
            {
                if (barsBelowEmaWithoutCross > 0 && timeNow >= StartTime && timeNow <= ForceCloseTime)
                {
                    Print(string.Format("{0} - [PAS - Z1-100B | LARGO RESET] Acumulación bajo EMA reseteada tras {1} barras. Close ({2:F2}) > EMA200 ({3:F2}) o EMA cruzó Línea Media.",
                        Time[0], barsBelowEmaWithoutCross, Close[0], ema200[0]));
                }
                barsBelowEmaWithoutCross = 0;
                z1LongArmed = false;
            }
            else if (!emaFueraArriba && !z1LongArmed)
            {
                // Si la EMA 200 está dentro del canal y aún no se alcanzaron las 100 barras, no cuenta
                if (barsBelowEmaWithoutCross > 0 && timeNow >= StartTime && timeNow <= ForceCloseTime)
                {
                    Print(string.Format("{0} - [PAS - Z1-100B | LARGO PAUSA] EMA200 ({1:F2}) dentro del canal (< UpperSmall {2:F2}). Conteo no acumula ({3}/{4}).",
                        Time[0], ema200[0], upperSmall, barsBelowEmaWithoutCross, MinBarsZona1));
                }
            }

            // --- Monitoreo de Acumulación para Cortos: EMA 200 afuera por ABAJO (<= Lower 1.75) y TEMA arriba ---
            bool emaFueraAbajo = ema200[0] <= lowerSmall;
            bool temaSobreEma  = currentDownTicks < MinCrossTicks;

            if (emaFueraAbajo && temaSobreEma)
            {
                barsAboveEmaWithoutCross++;
                if (barsAboveEmaWithoutCross >= MinBarsZona1)
                {
                    z1ShortArmed = true;
                }
                if (timeNow >= StartTime && timeNow <= ForceCloseTime)
                {
                    Print(string.Format("{0} - [PAS - Z1-100B | CORTO] Z1_100B_Barra {1}/{2} | EMA200 ({3:F2}) <= LowerSmall ({4:F2}) | TEMA9: {5:F2} | Armed: {6}",
                        Time[0], barsAboveEmaWithoutCross, MinBarsZona1, ema200[0], lowerSmall, tema9[0], z1ShortArmed));
                }
            }
            else if (Close[0] < ema200[0] || (z1ShortArmed && ema200[0] > midline))
            {
                if (barsAboveEmaWithoutCross > 0 && timeNow >= StartTime && timeNow <= ForceCloseTime)
                {
                    Print(string.Format("{0} - [PAS - Z1-100B | CORTO RESET] Acumulación sobre EMA reseteada tras {1} barras. Close ({2:F2}) < EMA200 ({3:F2}) o EMA cruzó Línea Media.",
                        Time[0], barsAboveEmaWithoutCross, Close[0], ema200[0]));
                }
                barsAboveEmaWithoutCross = 0;
                z1ShortArmed = false;
            }
            else if (!emaFueraAbajo && !z1ShortArmed)
            {
                // Si la EMA 200 está dentro del canal y aún no se alcanzaron las 100 barras, no cuenta
                if (barsAboveEmaWithoutCross > 0 && timeNow >= StartTime && timeNow <= ForceCloseTime)
                {
                    Print(string.Format("{0} - [PAS - Z1-100B | CORTO PAUSA] EMA200 ({1:F2}) dentro del canal (> LowerSmall {2:F2}). Conteo no acumula ({3}/{4}).",
                        Time[0], ema200[0], lowerSmall, barsAboveEmaWithoutCross, MinBarsZona1));
                }
            }

            // Rastrear estado de cruces de TEMA respecto a EMA200 (Fiel a SniperSurfer)
            if (timeNow < StartTime)
            {
                if (tema9[0] > ema200[0]) lastTemaSide = 1;
                else if (tema9[0] < ema200[0]) lastTemaSide = -1;
            }
            else
            {
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

            // Inicialización de seguridad para lastTemaSide
            if (lastTemaSide == 0)
            {
                lastTemaSide = (tema9[1] <= ema200[1]) ? -1 : 1;
            }

            // CASO LARGO: Cruce alcista tras >= 100 barras bajo la EMA 200
            if (lastTemaSide == -1 && !crossUpConsumed && tema9[0] > ema200[0])
            {
                double crossDistanceTicks = (tema9[0] - ema200[0]) / TickSize;
                double slopeTicks = (tema9[0] - tema9[1]) / TickSize;

                if (crossDistanceTicks >= MinCrossTicks && Close[0] > ema200[0] && slopeTicks > 0)
                {
                    crossUpConsumed = true;
                    lastTemaSide = 1;

                    // Filtro Anti-Zona 0: El cruce solo es válido si el precio permanece en Zona 1 (TEMA9 >= UpperSmall y Close <= UpperBig)
                    if (tema9[0] >= upperSmall && Close[0] <= upperBig)
                    {
                        if (z1LongArmed || priorBarsBelowEma >= MinBarsZona1)
                        {
                            ExecuteAceEntry(AceType.As1_Zona1_100Bar, MarketPosition.Long, "Z1-100Bar_Long");
                            Print(string.Format("{0} - [PAS - Z1-100Bar] ¡DISPARO LARGO! Ruptura en ZONA 1 ejecutada tras acumular {1} barras con EMA fuera del canal. Entrada @ ~{2:F2} | Contratos: {3}",
                                Time[0], Math.Max(priorBarsBelowEma, barsBelowEmaWithoutCross), Close[0], Contracts));
                            z1LongArmed = false;
                            barsBelowEmaWithoutCross = 0;
                            return;
                        }
                        else
                        {
                            Print(string.Format("{0} - [PAS - Z1-100Bar | IGNORADO] Cruce válido pero barras acumuladas insuficientes ({1}/{2}).",
                                Time[0], priorBarsBelowEma, MinBarsZona1));
                        }
                    }
                    else
                    {
                        Print(string.Format("{0} - [PAS - Z1-100Bar | DESCARTADO FUERA DE ZONA 1] Cruce EMA200 detectado pero TEMA9 ({1:F2}) < UpperSmall ({2:F2}) o precio en Zona 0 (Close {3:F2} > UpperBig {4:F2}). Trade descartado.",
                            Time[0], tema9[0], upperSmall, Close[0], upperBig));
                    }
                }
            }

            // CASO CORTO: Cruce bajista tras >= 100 barras con EMA 200 fuera del canal abajo
            if (lastTemaSide == 1 && !crossDownConsumed && tema9[0] < ema200[0])
            {
                double crossDistanceTicks = (ema200[0] - tema9[0]) / TickSize;
                double slopeTicks = (tema9[1] - tema9[0]) / TickSize;

                if (crossDistanceTicks >= MinCrossTicks && Close[0] < ema200[0] && slopeTicks > 0)
                {
                    crossDownConsumed = true;
                    lastTemaSide = -1;

                    // Filtro Anti-Zona 5: El cruce solo es válido si el precio permanece en Zona 4 (TEMA9 <= LowerSmall y Close >= LowerBig)
                    if (tema9[0] <= lowerSmall && Close[0] >= lowerBig)
                    {
                        if (z1ShortArmed || priorBarsAboveEma >= MinBarsZona1)
                        {
                            ExecuteAceEntry(AceType.As1_Zona1_100Bar, MarketPosition.Short, "Z1-100Bar_Short");
                            Print(string.Format("{0} - [PAS - Z1-100Bar] ¡DISPARO CORTO! Ruptura en ZONA 4 ejecutada tras acumular {1} barras con EMA fuera del canal. Entrada @ ~{2:F2} | Contratos: {3}",
                                Time[0], Math.Max(priorBarsAboveEma, barsAboveEmaWithoutCross), Close[0], Contracts));
                            z1ShortArmed = false;
                            barsAboveEmaWithoutCross = 0;
                            return;
                        }
                        else
                        {
                            Print(string.Format("{0} - [PAS - Z1-100Bar | IGNORADO] Cruce válido pero barras acumuladas insuficientes ({1}/{2}).",
                                Time[0], priorBarsAboveEma, MinBarsZona1));
                        }
                    }
                    else
                    {
                        Print(string.Format("{0} - [PAS - Z1-100Bar | DESCARTADO FUERA DE ZONA 4] Cruce EMA200 detectado pero TEMA9 ({1:F2}) > LowerSmall ({2:F2}) o precio en Zona 5 (Close {3:F2} < LowerBig {4:F2}). Trade descartado.",
                            Time[0], tema9[0], lowerSmall, Close[0], lowerBig));
                    }
                }
            }
        }

        // =========================================================================
        // AS 2: SOBB (SurferOutBigBanda - FILTRO DE 2 CAPAS: EMA ZONA 3 Y ESPERA DE PENDIENTES)
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

            // =========================================================================
            // SOBB LARGO: Cruce de TEMA 9 de adentro hacia afuera + Filtro 2 Capas
            // 1. Armado: TEMA 9 cruza por primera vez hacia afuera de Upper 3.5.
            // 2. Desarmado por repliegue: Si TEMA 9 regresa adentro (<= Upper 3.5), se desactiva.
            // 3. Evaluación de Capas cuando la vela esté 100% afuera (Low > Upper 3.5):
            //    - CAPA 1 (EMA 200 en Zona 3 - Inmediato y Estricto):
            //      En la primera vela afuera, si la EMA 200 NO está en Zona 3 (entre Midline y Lower 1.75),
            //      se descarta inmediatamente (sobbArmedLong = false). ¡PAILAS! Se cancela el setup.
            //    - CAPA 2 (Pendientes - Espera Paciente):
            //      Si la EMA 200 SÍ está en Zona 3, pero las pendientes aún no giran:
            //      NO se desarma. Se mantiene en espera en las siguientes barras mientras flote afuera.
            //      En cuanto las pendientes se alineen estando afuera: ¡PUM! Entrada Long ejecutada.
            //    - Desarmado si el precio regresa adentro (Low <= Upper 3.5) tras haber flotado afuera sin pendientes.
            // =========================================================================
            bool isTemaCruzoSalidaLargo = (tema9[1] <= keltnerBig.Upper[1] && tema9[0] > upperBig);
            if (isTemaCruzoSalidaLargo)
            {
                sobbArmedLong = true;
                sobbHasFloatedLong = false;
                Print(string.Format("{0} - [PAS - SOBB | LARGO ARMADO] TEMA 9 ({1:F2}) cruzó hacia afuera de Upper 3.5 ({2:F2}). Espera de vela completa afuera y confirmación de 2 capas.",
                    Time[0], tema9[0], upperBig));
            }
            else if (tema9[0] <= upperBig)
            {
                if (sobbArmedLong)
                {
                    sobbArmedLong = false;
                    sobbHasFloatedLong = false;
                    Print(string.Format("{0} - [PAS - SOBB | LARGO DESARMADO] TEMA 9 ({1:F2}) regresó dentro del canal sin haber disparado. Espera cancelada.",
                        Time[0], tema9[0]));
                }
            }

            if (sobbArmedLong)
            {
                bool isVelaFlotandoLargo = (Low[0] > upperBig);
                if (isVelaFlotandoLargo)
                {
                    sobbHasFloatedLong = true;

                    bool isEmaZonaLargo    = (ema200[0] <= midline && ema200[0] >= lowerSmall);
                    bool isPendientesLargo = (midlineSlope > 0 && upperSlope > 0 && emaSlope < midlineSlope);

                    // CAPA 1: EMA 200 en Zona 3 (Contexto Macro Estricto e Inmediato)
                    if (!isEmaZonaLargo)
                    {
                        sobbArmedLong = false;
                        sobbHasFloatedLong = false;
                        Print(string.Format("{0} - [PAS - SOBB | LARGO DESCARTADO (PAILAS)] Vela flotando en Zona 0 detectada pero EMA 200 ({1:F2}) fuera de Zona 3 ([{2:F2}, {3:F2}]). ¡Apague y vámonos! Bloqueado hasta nuevo cruce de TEMA 9.",
                            Time[0], ema200[0], lowerSmall, midline));
                    }
                    // CAPA 2: Si EMA 200 está en Zona 3, evaluamos las pendientes
                    else if (isPendientesLargo)
                    {
                        // ¡Ambas capas confirmadas con éxito!
                        sobbArmedLong = false;
                        sobbHasFloatedLong = false;
                        ExecuteAceEntry(AceType.As2_SOBB, MarketPosition.Long, "SOBB_Long");
                        Print(string.Format("{0} - [PAS - SOBB] ¡DISPARO LARGO CONFIRMADO! Vela flotando en Zona 0 con EMA en Zona 3 y pendientes alineadas | Entrada @ ~{1:F2} | SL Canales 3 Tiers.",
                            Time[0], Close[0]));
                        return;
                    }
                    else
                    {
                        // EMA 200 en Zona 3 OK, pero pendientes aún no alineadas: mantenemos armado a la espera de la siguiente barra afuera
                        Print(string.Format("{0} - [PAS - SOBB | LARGO EN ESPERA DE PENDIENTES] EMA 200 en Zona 3 OK. Pendientes no alineadas todavía (MidSlope={1:F4}, UpperSlope={2:F4}). Esperando mientras el precio siga afuera...",
                            Time[0], midlineSlope, upperSlope));
                    }
                }
                else if (sobbHasFloatedLong && Low[0] <= upperBig)
                {
                    // La vela ya había flotado afuera pero regresó adentro antes de que las pendientes confirmaran
                    sobbArmedLong = false;
                    sobbHasFloatedLong = false;
                    Print(string.Format("{0} - [PAS - SOBB | LARGO DESARMADO POR REPLIEGUE] El precio regresó adentro de Upper 3.5 ({1:F2}) sin haber confirmado pendientes. Setup cancelado.",
                        Time[0], upperBig));
                }
            }

            // =========================================================================
            // SOBB CORTO: Cruce de TEMA 9 de adentro hacia afuera + Filtro 2 Capas
            // 1. Armado: TEMA 9 cruza por primera vez hacia afuera de Lower 3.5.
            // 2. Desarmado por repliegue: Si TEMA 9 regresa adentro (>= Lower 3.5), se desactiva.
            // 3. Evaluación de Capas cuando la vela esté 100% afuera (High < Lower 3.5):
            //    - CAPA 1 (EMA 200 en Zona 3 - Inmediato y Estricto):
            //      En la primera vela afuera, si la EMA 200 NO está en Zona 3 (entre Midline y Upper 1.75),
            //      se descarta inmediatamente (sobbArmedShort = false). ¡PAILAS! Se cancela el setup.
            //    - CAPA 2 (Pendientes - Espera Paciente):
            //      Si la EMA 200 SÍ está en Zona 3, pero las pendientes aún no giran:
            //      NO se desarma. Se mantiene en espera en las siguientes barras mientras flote afuera.
            //      En cuanto las pendientes se alineen estando afuera: ¡PUM! Entrada Short ejecutada.
            //    - Desarmado si el precio regresa adentro (High >= Lower 3.5) tras haber flotado afuera sin pendientes.
            // =========================================================================
            bool isTemaCruzoSalidaCorto = (tema9[1] >= keltnerBig.Lower[1] && tema9[0] < lowerBig);
            if (isTemaCruzoSalidaCorto)
            {
                sobbArmedShort = true;
                sobbHasFloatedShort = false;
                Print(string.Format("{0} - [PAS - SOBB | CORTO ARMADO] TEMA 9 ({1:F2}) cruzó hacia afuera de Lower 3.5 ({2:F2}). Espera de vela completa afuera y confirmación de 2 capas.",
                    Time[0], tema9[0], lowerBig));
            }
            else if (tema9[0] >= lowerBig)
            {
                if (sobbArmedShort)
                {
                    sobbArmedShort = false;
                    sobbHasFloatedShort = false;
                    Print(string.Format("{0} - [PAS - SOBB | CORTO DESARMADO] TEMA 9 ({1:F2}) regresó dentro del canal sin haber disparado. Espera cancelada.",
                        Time[0], tema9[0]));
                }
            }

            if (sobbArmedShort)
            {
                bool isVelaFlotandoCorto = (High[0] < lowerBig);
                if (isVelaFlotandoCorto)
                {
                    sobbHasFloatedShort = true;

                    bool isEmaZonaCorto    = (ema200[0] >= midline && ema200[0] <= upperSmall);
                    bool isPendientesCorto = (midlineSlope < 0 && lowerSlope < 0 && emaSlope > midlineSlope);

                    // CAPA 1: EMA 200 en Zona 3 (Contexto Macro Estricto e Inmediato)
                    if (!isEmaZonaCorto)
                    {
                        sobbArmedShort = false;
                        sobbHasFloatedShort = false;
                        Print(string.Format("{0} - [PAS - SOBB | CORTO DESCARTADO (PAILAS)] Vela flotando en Zona 5 detectada pero EMA 200 ({1:F2}) fuera de Zona 3 ([{2:F2}, {3:F2}]). ¡Apague y vámonos! Bloqueado hasta nuevo cruce de TEMA 9.",
                            Time[0], ema200[0], midline, upperSmall));
                    }
                    // CAPA 2: Si EMA 200 está en Zona 3, evaluamos las pendientes
                    else if (isPendientesCorto)
                    {
                        // ¡Ambas capas confirmadas con éxito!
                        sobbArmedShort = false;
                        sobbHasFloatedShort = false;
                        ExecuteAceEntry(AceType.As2_SOBB, MarketPosition.Short, "SOBB_Short");
                        Print(string.Format("{0} - [PAS - SOBB] ¡DISPARO CORTO CONFIRMADO! Vela flotando en Zona 5 con EMA en Zona 3 y pendientes alineadas | Entrada @ ~{1:F2} | SL Canales 3 Tiers.",
                            Time[0], Close[0]));
                        return;
                    }
                    else
                    {
                        // EMA 200 en Zona 3 OK, pero pendientes aún no alineadas: mantenemos armado a la espera de la siguiente barra afuera
                        Print(string.Format("{0} - [PAS - SOBB | CORTO EN ESPERA DE PENDIENTES] EMA 200 en Zona 3 OK. Pendientes no alineadas todavía (MidSlope={1:F4}, LowerSlope={2:F4}). Esperando mientras el precio siga afuera...",
                            Time[0], midlineSlope, lowerSlope));
                    }
                }
                else if (sobbHasFloatedShort && High[0] >= lowerBig)
                {
                    // La vela ya había flotado afuera pero regresó adentro antes de que las pendientes confirmaran
                    sobbArmedShort = false;
                    sobbHasFloatedShort = false;
                    Print(string.Format("{0} - [PAS - SOBB | CORTO DESARMADO POR REPLIEGUE] El precio regresó adentro de Lower 3.5 ({1:F2}) sin haber confirmado pendientes. Setup cancelado.",
                        Time[0], lowerBig));
                }
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
        // AS 4: CASCADA 25 BARRAS (EL TREN BALA DESBOCADO - FILTRO ZONAS EXPANSIVAS 0/1 Y 4/5)
        // =========================================================================
        private void EvaluateAs4_Cascada50()
        {
            double midline    = keltnerBig.Midline[0];
            double upperSmall = keltnerSmall.Upper[0];
            double lowerSmall = keltnerSmall.Lower[0];
            double minSlope   = CascadaMinSlopeTicks * TickSize;

            // DISPARO CORTO: EMA 200 > Upper 3.5 + 4 ticks por >= 25 barras.
            // FILTRO ESTRUCTURAL: El precio DEBE estar en Zona 5 o Zona 4 (Close <= Lower 1.75).
            // Si el precio subió a Zona 3 o superior, el tren perdió velocidad y NO se opera.
            if (barsCascadaShortCount >= CascadaMinBars)
            {
                bool precioEnZonaExpansion = Close[0] <= lowerSmall;
                bool pendienteBajista      = (ema200[5] - ema200[0]) >= minSlope;

                if (precioEnZonaExpansion && pendienteBajista)
                {
                    ExecuteAceEntry(AceType.As4_Cascada50, MarketPosition.Short, "Cascada50_Short");
                    Print(string.Format("{0} - [PAS - Cascada50] ¡DISPARO CORTO CONFIRMADO! Tren bala en ZONA 5/4 (Close {1:F2} <= Lower 1.75 {2:F2}) con EMA > Upper 3.5 por {3} barras | Entrada @ ~{4:F2}",
                        Time[0], Close[0], lowerSmall, barsCascadaShortCount, Close[0]));
                    return;
                }
            }

            // DISPARO LARGO: EMA 200 < Lower 3.5 - 4 ticks por >= 25 barras.
            // FILTRO ESTRUCTURAL: El precio DEBE estar en Zona 0 o Zona 1 (Close >= Upper 1.75).
            // Si el precio cayó a Zona 2 o inferior, el tren perdió velocidad y NO se opera.
            if (barsCascadaLongCount >= CascadaMinBars)
            {
                bool precioEnZonaExpansion = Close[0] >= upperSmall;
                bool pendienteAlcista      = (ema200[0] - ema200[5]) >= minSlope;

                if (precioEnZonaExpansion && pendienteAlcista)
                {
                    ExecuteAceEntry(AceType.As4_Cascada50, MarketPosition.Long, "Cascada50_Long");
                    Print(string.Format("{0} - [PAS - Cascada50] ¡DISPARO LARGO CONFIRMADO! Tren bala en ZONA 0/1 (Close {1:F2} >= Upper 1.75 {2:F2}) con EMA < Lower 3.5 por {3} barras | Entrada @ ~{4:F2}",
                        Time[0], Close[0], upperSmall, barsCascadaLongCount, Close[0]));
                    return;
                }
            }
        }

        #endregion

        #region Ejecución y Gestión de Salidas Escalonadas

        private int GetRiskAdjustedContracts()
        {
            int targetContracts = Contracts; // Configurado por defecto (3 contratos)

            if (UseDailyManagement && !isDailyLocked && currentDailyFloor > 0)
            {
                double allowedLossDollars = dailyRealizedPnL - currentDailyFloor;
                double tickDollarVal = TickSize * Instrument.MasterInstrument.PointValue;

                // REGLA DE ORO DE ESCALÓN 1: Si ya estamos en ganancias protegidas (Escalón 1: $400 - $599),
                // se opera OBLIGATORIAMENTE con 1 solo contrato. Esto garantiza un Stop Loss amplísimo y holgado
                // (16 a 24 ticks de respiro) para absorber el ruido del mercado y dejar correr los runners hasta el Escalón 2/3.
                if (currentDailyTier == 1)
                {
                    int maxTicksFor1Contract = (int)Math.Floor(allowedLossDollars / tickDollarVal);

                    if (maxTicksFor1Contract >= MinSafetySlTicks)
                    {
                        Print(string.Format("{0} - [PAS | REGLA DE ORO ESCALÓN 1: 1 SOLO CONTRATO] PnL actual: ${1:F2} USD (Piso +${2:F2}). Operando con 1 solo contrato con Stop holgado de {3} ticks para dejar correr la ganancia.",
                            Time[0], dailyRealizedPnL, currentDailyFloor, maxTicksFor1Contract));
                        return 1;
                    }
                    else
                    {
                        Print(string.Format("{0} - [PAS | PROTECCIÓN TOTAL DE PISO] Margen disponible (${1:F2}) insuficiente para Stop mínimo de {2} ticks en 1 contrato. Protegiendo Piso de +${3:F2} USD.",
                            Time[0], allowedLossDollars, MinSafetySlTicks, currentDailyFloor));
                        return 0;
                    }
                }

                // Para Escalón 2 o superior: Exigimos al menos 14 ticks de respiro real por contrato para permitir 2 o más contratos
                int minHealthyTicksPerContract = 14;

                for (int qty = Contracts; qty >= 1; qty--)
                {
                    double dollarsPerTick = qty * tickDollarVal;
                    int maxTicksForQty = (int)Math.Floor(allowedLossDollars / dollarsPerTick);

                    if (qty == 1 && maxTicksForQty >= MinSafetySlTicks)
                    {
                        Print(string.Format("{0} - [PAS | DESESCALADO DINÁMICO] Margen disponible: ${1:F2} USD. Posición ajustada a 1 contrato con {2} ticks de Stop holgado sin romper Piso (+${3:F2}).",
                            Time[0], allowedLossDollars, maxTicksForQty, currentDailyFloor));
                        return 1;
                    }
                    else if (qty > 1 && maxTicksForQty >= minHealthyTicksPerContract)
                    {
                        if (qty < Contracts)
                        {
                            Print(string.Format("{0} - [PAS | DESESCALADO DINÁMICO] Margen disponible: ${1:F2} USD. Posición desescalada de {2} a {3} contrato(s) para dar Stop saludable de {4} ticks sin romper Piso (+${5:F2}).",
                                Time[0], allowedLossDollars, Contracts, qty, maxTicksForQty, currentDailyFloor));
                        }
                        return qty;
                    }
                }

                // Si ni siquiera con 1 contrato alcanzamos al menos MinSafetySlTicks de respiro, no arriesgamos el piso
                Print(string.Format("{0} - [PAS | PROTECCIÓN TOTAL DE PISO] Margen disponible (${1:F2}) insuficiente para Stop mínimo en 1 contrato. Protegiendo Piso de +${2:F2} USD.",
                    Time[0], allowedLossDollars, currentDailyFloor));
                return 0;
            }

            return targetContracts;
        }

        private void ExecuteAceEntry(AceType ace, MarketPosition pos, string signalName)
        {
            currentAce = ace;
            entryPriceGlobal = Close[0];
            highestPriceSinceEntry = High[0];
            lowestPriceSinceEntry  = Low[0];

            // Sincronizamos pisos y PnL inmediatamente antes de calcular el SL
            UpdateDailyPnLAndFloors();

            // DESESCALADO DINÁMICO DE CONTRATOS (Position Sizing Institucional)
            int entryContracts = GetRiskAdjustedContracts();
            if (entryContracts <= 0)
            {
                Print(string.Format("{0} - [PAS | ENTRADA DESCARTADA] Margen disponible insuficiente para arriesgar sin violar el piso garantizado (+${1:F2} USD).",
                    Time[0], currentDailyFloor));
                return;
            }
            activeTradeContracts = entryContracts;

            sobbLevel1Achieved = false;
            sobbLevel2Achieved = false;
            sobbLevel3ChokeActive = false;

            ratchetC1Achieved = false;
            ratchetC2Achieved = false;
            ratchetChokeActive = false;

            cascadaC1Achieved = false;
            cascadaC2Achieved = false;
            cascadaChokeActive = false;

            // Stop Loss inicial físico en el DOM
            double initialSlPrice = 0.0;
            if (pos == MarketPosition.Long)
            {
                if (ace == AceType.As1_Zona1_100Bar)
                {
                    // Z1-100Bar: SL en Línea Media, pero con TOPE MÁXIMO de seguridad de 14 ticks (R:R saludable 1:1 vs TP 12 ticks)
                    double naturalSl = keltnerBig.Midline[0] - (SlOffsetTicks * TickSize);
                    double maxSafeSl = Close[0] - ((Zone1FixedTpTicks + 2) * TickSize);
                    initialSlPrice = Math.Max(naturalSl, maxSafeSl);
                }
                else if (ace == AceType.As4_Cascada50)
                {
                    // LÓGICA DE 6 ZONAS KELTNER PARA SL INICIAL EN LARGOS:
                    // Zona 0 y Zona 1 (Close >= Upper 1.75): SL en Línea Media (Midline - 2 ticks)
                    // Zona 2 (Close < Upper 1.75): SL en Banda Inferior Pequeña (Lower 1.75 - 2 ticks) para evitar amagues
                    if (Close[0] >= keltnerSmall.Upper[0])
                    {
                        initialSlPrice = keltnerBig.Midline[0] - (2 * TickSize);
                        Print(string.Format("{0} - [PAS - Cascada | SL 6 ZONAS] Entrada en ZONA 0/1 (Expansión Alta). SL en Línea Media: {1:F2}", Time[0], initialSlPrice));
                    }
                    else
                    {
                        initialSlPrice = keltnerSmall.Lower[0] - (2 * TickSize);
                        Print(string.Format("{0} - [PAS - Cascada | SL 6 ZONAS] Entrada en ZONA 2 (Media Alta). SL en Lower 1.75 -2tk: {1:F2} (Anti-amague)", Time[0], initialSlPrice));
                    }

                    // REGLA DE ORO INSTITUCIONAL: Targets pasivos LÍMITE en el libro para eliminar slippage
                    double tp1Price = Instrument.MasterInstrument.RoundToTickSize(Close[0] + (CascadaTp1Ticks * TickSize));
                    double tp2Price = Instrument.MasterInstrument.RoundToTickSize(Close[0] + (CascadaTp2Ticks * TickSize));
                    if (activeTradeContracts >= 3)
                    {
                        ExitLongLimit(1, tp1Price, "Cascada_C1_Tp8", "Cascada50_Long");
                        ExitLongLimit(1, tp2Price, "Cascada_C2_Tp16", "Cascada50_Long");
                    }
                    else if (activeTradeContracts == 2)
                    {
                        ExitLongLimit(1, tp1Price, "Cascada_C1_Tp8", "Cascada50_Long");
                    }
                    Print(string.Format("{0} - [PAS - Cascada | TARGETS LÍMITE COLOCADOS] C1 (+{1}tk): {2:F2} | Posición activa: {3} contrato(s)",
                        Time[0], CascadaTp1Ticks, tp1Price, activeTradeContracts));
                }
                else if (ace == AceType.As3_Ratchet)
                    initialSlPrice = keltnerBig.Lower[0] - (SlOffsetTicks * TickSize); // Banda opuesta en Ratchet
                else
                    initialSlPrice = keltnerSmall.Lower[0] - (1 * TickSize);

                initialSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Min(initialSlPrice, Close[0] - (MinSafetySlTicks * TickSize)));

                // ESCUDO DIARIO BLINDADO EN DOM: Si hay piso activo de ganancias previas, el piso manda absolutamente sobre el SL
                if (UseDailyManagement && !isDailyLocked && dailyRealizedPnL > currentDailyFloor)
                {
                    double allowedLossDollars = dailyRealizedPnL - currentDailyFloor;
                    double tickDollarVal = TickSize * Instrument.MasterInstrument.PointValue;
                    double totalDollarPerTick = activeTradeContracts * tickDollarVal;
                    if (totalDollarPerTick > 0)
                    {
                        int maxTicksAllowedByFloor = (int)Math.Floor(allowedLossDollars / totalDollarPerTick);
                        double floorSlPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0] - (maxTicksAllowedByFloor * TickSize));
                        if (floorSlPrice > initialSlPrice)
                        {
                            initialSlPrice = floorSlPrice;
                            Print(string.Format("{0} - [ESCUDO PISO ACTIVO] Piso de +${1:F2} USD limita pérdida a {2} ticks ({3} contrato(s)). SL Ceñido en {4:F2} colocado en DOM.",
                                Time[0], currentDailyFloor, maxTicksAllowedByFloor, activeTradeContracts, initialSlPrice));
                        }
                    }
                }

                // Solo Zona 1 lleva TP y SL fijos en bloque de NinjaTrader (SetStopLoss / SetProfitTarget).
                // SOBB, Cascada y Ratchet son escuadrones con salidas escalonadas que gestionan sus paradas
                // de resguardo EXCLUSIVAMENTE en ManageDynamicExits, eliminando cualquier posibilidad
                // de sobrellenado (overshoot) que voltee la posición al revés en el broker.
                if (ace == AceType.As1_Zona1_100Bar)
                {
                    SetStopLoss(signalName, CalculationMode.Price, initialSlPrice, false);
                    double tpPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0] + (Zone1FixedTpTicks * TickSize));
                    SetProfitTarget(signalName, CalculationMode.Price, tpPrice);
                }

                EnterLong(activeTradeContracts, signalName);
            }
            else // Short
            {
                if (ace == AceType.As1_Zona1_100Bar)
                {
                    // Z1-100Bar: SL en Línea Media, pero con TOPE MÁXIMO de seguridad de 14 ticks (R:R saludable 1:1 vs TP 12 ticks)
                    double naturalSl = keltnerBig.Midline[0] + (SlOffsetTicks * TickSize);
                    double maxSafeSl = Close[0] + ((Zone1FixedTpTicks + 2) * TickSize);
                    initialSlPrice = Math.Min(naturalSl, maxSafeSl);
                }
                else if (ace == AceType.As4_Cascada50)
                {
                    // LÓGICA DE 6 ZONAS KELTNER PARA SL INICIAL EN CORTOS:
                    // Zona 5 y Zona 4 (Close <= Lower 1.75): SL en Línea Media (Midline + 2 ticks)
                    // Zona 3 (Close > Lower 1.75): SL en Banda Superior Pequeña (Upper 1.75 + 2 ticks) para evitar amagues
                    if (Close[0] <= keltnerSmall.Lower[0])
                    {
                        initialSlPrice = keltnerBig.Midline[0] + (2 * TickSize);
                        Print(string.Format("{0} - [PAS - Cascada | SL 6 ZONAS] Entrada en ZONA 5/4 (Expansión Baja). SL en Línea Media: {1:F2}", Time[0], initialSlPrice));
                    }
                    else
                    {
                        initialSlPrice = keltnerSmall.Upper[0] + (2 * TickSize);
                        Print(string.Format("{0} - [PAS - Cascada | SL 6 ZONAS] Entrada en ZONA 3 (Media Baja). SL en Upper 1.75 +2tk: {1:F2} (Anti-amague)", Time[0], initialSlPrice));
                    }

                    // REGLA DE ORO INSTITUCIONAL: Targets pasivos LÍMITE en el libro para eliminar slippage
                    double tp1Price = Instrument.MasterInstrument.RoundToTickSize(Close[0] - (CascadaTp1Ticks * TickSize));
                    double tp2Price = Instrument.MasterInstrument.RoundToTickSize(Close[0] - (CascadaTp2Ticks * TickSize));
                    if (activeTradeContracts >= 3)
                    {
                        ExitShortLimit(1, tp1Price, "Cascada_C1_Tp8", "Cascada50_Short");
                        ExitShortLimit(1, tp2Price, "Cascada_C2_Tp16", "Cascada50_Short");
                    }
                    else if (activeTradeContracts == 2)
                    {
                        ExitShortLimit(1, tp1Price, "Cascada_C1_Tp8", "Cascada50_Short");
                    }
                    Print(string.Format("{0} - [PAS - Cascada | TARGETS LÍMITE COLOCADOS] C1 (-{1}tk): {2:F2} | Posición activa: {3} contrato(s)",
                        Time[0], CascadaTp1Ticks, tp1Price, activeTradeContracts));
                }
                else if (ace == AceType.As3_Ratchet)
                    initialSlPrice = keltnerBig.Upper[0] + (SlOffsetTicks * TickSize); // Banda opuesta en Ratchet
                else
                    initialSlPrice = keltnerSmall.Upper[0] + (1 * TickSize);

                initialSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Max(initialSlPrice, Close[0] + (MinSafetySlTicks * TickSize)));

                // ESCUDO DIARIO BLINDADO EN DOM: Si hay piso activo de ganancias previas, el piso manda absolutamente sobre el SL
                if (UseDailyManagement && !isDailyLocked && dailyRealizedPnL > currentDailyFloor)
                {
                    double allowedLossDollars = dailyRealizedPnL - currentDailyFloor;
                    double tickDollarVal = TickSize * Instrument.MasterInstrument.PointValue;
                    double totalDollarPerTick = activeTradeContracts * tickDollarVal;
                    if (totalDollarPerTick > 0)
                    {
                        int maxTicksAllowedByFloor = (int)Math.Floor(allowedLossDollars / totalDollarPerTick);
                        double floorSlPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0] + (maxTicksAllowedByFloor * TickSize));
                        if (floorSlPrice < initialSlPrice)
                        {
                            initialSlPrice = floorSlPrice;
                            Print(string.Format("{0} - [ESCUDO PISO ACTIVO] Piso de +${1:F2} USD limita pérdida a {2} ticks ({3} contrato(s)). SL Ceñido en {4:F2} colocado en DOM.",
                                Time[0], currentDailyFloor, maxTicksAllowedByFloor, activeTradeContracts, initialSlPrice));
                        }
                    }
                }

                // Solo Zona 1 lleva TP y SL fijos en bloque de NinjaTrader (SetStopLoss / SetProfitTarget).
                // SOBB, Cascada y Ratchet son escuadrones con salidas escalonadas que gestionan sus paradas
                // de resguardo EXCLUSIVAMENTE en ManageDynamicExits, eliminando cualquier posibilidad
                // de sobrellenado (overshoot) que voltee la posición al revés en el broker.
                if (ace == AceType.As1_Zona1_100Bar)
                {
                    SetStopLoss(signalName, CalculationMode.Price, initialSlPrice, false);
                    double tpPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0] - (Zone1FixedTpTicks * TickSize));
                    SetProfitTarget(signalName, CalculationMode.Price, tpPrice);
                }

                EnterShort(activeTradeContracts, signalName);
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
                double currentGainDollars = currentGainTicks * (activeTradeContracts * Instrument.MasterInstrument.PointValue * TickSize);
                double peakGainTicks = (highestPriceSinceEntry - entryPrice) / TickSize;
                double peakGainDollars = peakGainTicks * (activeTradeContracts * Instrument.MasterInstrument.PointValue * TickSize);

                // --- AS 2: SOBB (SURFER OUT BIG BANDA - SL ESCALONADO 3 TIERS & PISOS $180, $220, $280) ---
                if (currentAce == AceType.As2_SOBB)
                {
                    // === GESTIÓN DE PROFIT (LOS 3 PISOS DE GANANCIA) ===
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
                        else
                        {
                            Print(string.Format("{0} - [PAS - SOBB | ESCALÓN 1 SUPERADO] Ganancia: {1:C2} (+{2:F1} tks). Posición desescalada ({3} cto) blindada en BREAKEVEN +1tk ({4:F2}). ¡Riesgo CERO!",
                                Time[0], Math.Max(currentGainDollars, peakGainDollars), Math.Max(currentGainTicks, peakGainTicks), currentQty, entryPrice + (1 * TickSize)));
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

                    // === EJECUCIÓN DE STOPS DINÁMICOS TRAS GANANCIA ASEGURADA ===
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
                    // === STOP LOSS DEFENSIVO ESCALONADO POR CANALES (ANTES DE ALCANZAR EL PISO 1) ===
                    else
                    {
                        // 1. TIER 1 (Contrato 1): Banda Pequeña Superior (Upper 1.75 - 1tk)
                        if (currentQty >= 3)
                        {
                            double slC1 = keltnerSmall.Upper[0] - (1 * TickSize);
                            if (Close[0] <= slC1 || Low[0] <= slC1)
                            {
                                ExitLong(1, "SOBB_SL_C1_UpperSmall", "SOBB_Long");
                                currentQty--;
                                Print(string.Format("{0} - [PAS - SOBB | SL CANALES C1] Contrato 1 liquidado en Banda Pequeña Upper 1.75 -1tk ({1:F2}). Contratos restantes: {2}",
                                    Time[0], slC1, currentQty));
                            }
                        }

                        // 2. TIER 2 (Contrato 2): Línea Media (Midline - 1tk)
                        if (currentQty == 2)
                        {
                            double slC2 = keltnerBig.Midline[0] - (1 * TickSize);
                            if (Close[0] <= slC2 || Low[0] <= slC2)
                            {
                                ExitLong(1, "SOBB_SL_C2_Midline", "SOBB_Long");
                                currentQty--;
                                Print(string.Format("{0} - [PAS - SOBB | SL CANALES C2] Contrato 2 liquidado en Línea Media ({1:F2}). Contratos restantes: {2}",
                                    Time[0], slC2, currentQty));
                            }
                        }

                        // 3. TIER 3 (Contrato 3): Extremo Opuesto del Canal Pequeño (Lower 1.75 - 1tk)
                        if (currentQty == 1)
                        {
                            double slC3 = keltnerSmall.Lower[0] - (1 * TickSize);
                            if (Close[0] <= slC3 || Low[0] <= slC3)
                            {
                                ExitLong("SOBB_SL_C3_LowerSmall", "SOBB_Long");
                                Print(string.Format("{0} - [PAS - SOBB | SL CANALES C3] Contrato 3 liquidado en Extremo Opuesto Lower 1.75 -1tk ({1:F2}). Posición cerrada por completo.",
                                    Time[0], slC3));
                                return;
                            }
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
                // --- GESTIÓN AS 4: CASCADA 25 (ESCALONAMIENTO 3 FASES + 6 ZONAS KELTNER + TARGETS LÍMITE) ---
                else if (currentAce == AceType.As4_Cascada50)
                {
                    double maxProfitTicks = (highestPriceSinceEntry - entryPrice) / TickSize;

                    // CONTRATO 1: Target Límite pasivo a +8 ticks (CascadaTp1Ticks) ejecutado o detectado
                    if (!cascadaC1Achieved && (currentQty <= 2 || maxProfitTicks >= CascadaTp1Ticks || currentGainTicks >= CascadaTp1Ticks))
                    {
                        cascadaC1Achieved = true;
                        if (currentQty >= 3)
                        {
                            ExitLong(1, "Cascada_C1_Tp8", "Cascada50_Long");
                        }
                        Print(string.Format("{0} - [PAS - Cascada | FASE 1 SUPERADA] C1 ejecutado a +{1} ticks (+${2:F2}). Stop blindado en BREAKEVEN +1tk ({3:F2}). ¡Riesgo CERO!",
                            Time[0], CascadaTp1Ticks, CascadaTp1Ticks * TickSize * Instrument.MasterInstrument.PointValue, entryPrice + (1 * TickSize)));
                    }

                    // CONTRATO 2: Target Límite pasivo a +16 ticks (CascadaTp2Ticks) ejecutado o detectado
                    if (cascadaC1Achieved && !cascadaC2Achieved && (currentQty <= 1 || maxProfitTicks >= CascadaTp2Ticks || currentGainTicks >= CascadaTp2Ticks))
                    {
                        cascadaC2Achieved = true;
                        cascadaChokeActive = true;
                        if (currentQty >= 2)
                        {
                            ExitLong(1, "Cascada_C2_Tp16", "Cascada50_Long");
                        }
                        Print(string.Format("{0} - [PAS - Cascada | FASE 2 SUPERADA] C2 cobrado a +{1} ticks (+${2:F2}). Contrato 3 Runner en MODO AHOGO a {3} ticks del High.",
                            Time[0], CascadaTp2Ticks, CascadaTp2Ticks * TickSize * Instrument.MasterInstrument.PointValue, CascadaChokeTicks));
                    }

                    // CONTRATO 3 (RUNNER): Modo Ahogo a 8 ticks del pico (CascadaChokeTicks)
                    if (cascadaChokeActive || (cascadaC1Achieved && cascadaC2Achieved))
                    {
                        double chokeSl = highestPriceSinceEntry - (CascadaChokeTicks * TickSize);
                        if (Close[0] <= chokeSl || Low[0] <= chokeSl)
                        {
                            ExitLong("Cascada_C3_RunnerAhogo", "Cascada50_Long");
                            Print(string.Format("{0} - [PAS - Cascada | RUNNER AHOGADO EN LA CIMA] Salida a Mercado @ ~{1:F2} (Pico: {2:F2}). ¡Ganancia máxima asegurada!",
                                Time[0], Close[0], highestPriceSinceEntry));
                        }
                    }
                    // Stop de Resguardo tras cobrar C1: BreakEven + 1 tick
                    else if (cascadaC1Achieved)
                    {
                        double bePrice = entryPrice + (1 * TickSize);
                        if (Close[0] <= bePrice || Low[0] <= bePrice)
                        {
                            ExitLong("Cascada_Stop_BE", "Cascada50_Long");
                            Print(string.Format("{0} - [PAS - Cascada | SALIDA EN BREAKEVEN] Posición restante protegida en {1:F2}. Salida sin pérdida.",
                                Time[0], bePrice));
                        }
                    }
                    // Stop Inicial antes de C1 (según las 6 Zonas de Keltner):
                    else
                    {
                        double initialSl = (entryPrice >= keltnerSmall.Upper[0]) 
                            ? (keltnerBig.Midline[0] - (2 * TickSize)) 
                            : (keltnerSmall.Lower[0] - (2 * TickSize));

                        if (Close[0] <= initialSl || Low[0] <= initialSl)
                        {
                            ExitLong("SL_Zona_Cascada", "Cascada50_Long");
                            Print(string.Format("{0} - [PAS - Cascada | STOP INICIAL ZONA] Precio perforó SL ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                Time[0], initialSl, Close[0]));
                        }
                    }
                }
            }
            // =========================================================================
            // SALIDAS EN POSICIÓN CORTA (ESPEJO INVERSO)
            // =========================================================================
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double currentGainTicks = (entryPrice - Low[0]) / TickSize;
                double currentGainDollars = currentGainTicks * (activeTradeContracts * Instrument.MasterInstrument.PointValue * TickSize);
                double peakGainTicks = (entryPrice - lowestPriceSinceEntry) / TickSize;
                double peakGainDollars = peakGainTicks * (activeTradeContracts * Instrument.MasterInstrument.PointValue * TickSize);

                // --- AS 2: SOBB (SURFER OUT BIG BANDA - SL ESCALONADO 3 TIERS & PISOS $180, $220, $280) ---
                if (currentAce == AceType.As2_SOBB)
                {
                    // === GESTIÓN DE PROFIT (LOS 3 PISOS DE GANANCIA) ===
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
                        else
                        {
                            Print(string.Format("{0} - [PAS - SOBB | ESCALÓN 1 SUPERADO] Ganancia: {1:C2} (+{2:F1} tks). Posición desescalada ({3} cto) blindada en BREAKEVEN -1tk ({4:F2}). ¡Riesgo CERO!",
                                Time[0], Math.Max(currentGainDollars, peakGainDollars), Math.Max(currentGainTicks, peakGainTicks), currentQty, entryPrice - (1 * TickSize)));
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

                    // === EJECUCIÓN DE STOPS DINÁMICOS TRAS GANANCIA ASEGURADA ===
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
                    // === STOP LOSS DEFENSIVO ESCALONADO POR CANALES (ANTES DE ALCANZAR EL PISO 1) ===
                    else
                    {
                        // 1. TIER 1 (Contrato 1): Banda Pequeña Inferior (Lower 1.75 + 1tk)
                        if (currentQty >= 3)
                        {
                            double slC1 = keltnerSmall.Lower[0] + (1 * TickSize);
                            if (Close[0] >= slC1 || High[0] >= slC1)
                            {
                                ExitShort(1, "SOBB_SL_C1_LowerSmall", "SOBB_Short");
                                currentQty--;
                                Print(string.Format("{0} - [PAS - SOBB | SL CANALES C1] Contrato 1 liquidado en Banda Pequeña Lower 1.75 +1tk ({1:F2}). Contratos restantes: {2}",
                                    Time[0], slC1, currentQty));
                            }
                        }

                        // 2. TIER 2 (Contrato 2): Línea Media (Midline + 1tk)
                        if (currentQty == 2)
                        {
                            double slC2 = keltnerBig.Midline[0] + (1 * TickSize);
                            if (Close[0] >= slC2 || High[0] >= slC2)
                            {
                                ExitShort(1, "SOBB_SL_C2_Midline", "SOBB_Short");
                                currentQty--;
                                Print(string.Format("{0} - [PAS - SOBB | SL CANALES C2] Contrato 2 liquidado en Línea Media ({1:F2}). Contratos restantes: {2}",
                                    Time[0], slC2, currentQty));
                            }
                        }

                        // 3. TIER 3 (Contrato 3): Extremo Opuesto del Canal Pequeño (Upper 1.75 + 1tk)
                        if (currentQty == 1)
                        {
                            double slC3 = keltnerSmall.Upper[0] + (1 * TickSize);
                            if (Close[0] >= slC3 || High[0] >= slC3)
                            {
                                ExitShort("SOBB_SL_C3_UpperSmall", "SOBB_Short");
                                Print(string.Format("{0} - [PAS - SOBB | SL CANALES C3] Contrato 3 liquidado en Extremo Opuesto Upper 1.75 +1tk ({1:F2}). Posición cerrada por completo.",
                                    Time[0], slC3));
                                return;
                            }
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
                // --- GESTIÓN AS 4: CASCADA 25 (ESCALONAMIENTO 3 FASES + 6 ZONAS KELTNER + TARGETS LÍMITE) ---
                else if (currentAce == AceType.As4_Cascada50)
                {
                    double maxProfitTicks = (entryPrice - lowestPriceSinceEntry) / TickSize;

                    // CONTRATO 1: Target Límite pasivo a +8 ticks (CascadaTp1Ticks) ejecutado o detectado
                    if (!cascadaC1Achieved && (currentQty <= 2 || maxProfitTicks >= CascadaTp1Ticks || currentGainTicks >= CascadaTp1Ticks))
                    {
                        cascadaC1Achieved = true;
                        if (currentQty >= 3)
                        {
                            ExitShort(1, "Cascada_C1_Tp8", "Cascada50_Short");
                        }
                        Print(string.Format("{0} - [PAS - Cascada | FASE 1 SUPERADA] C1 ejecutado a +{1} ticks (+${2:F2}). Stop blindado en BREAKEVEN -1tk ({3:F2}). ¡Riesgo CERO!",
                            Time[0], CascadaTp1Ticks, CascadaTp1Ticks * TickSize * Instrument.MasterInstrument.PointValue, entryPrice - (1 * TickSize)));
                    }

                    // CONTRATO 2: Target Límite pasivo a +16 ticks (CascadaTp2Ticks) ejecutado o detectado
                    if (cascadaC1Achieved && !cascadaC2Achieved && (currentQty <= 1 || maxProfitTicks >= CascadaTp2Ticks || currentGainTicks >= CascadaTp2Ticks))
                    {
                        cascadaC2Achieved = true;
                        cascadaChokeActive = true;
                        if (currentQty >= 2)
                        {
                            ExitShort(1, "Cascada_C2_Tp16", "Cascada50_Short");
                        }
                        Print(string.Format("{0} - [PAS - Cascada | FASE 2 SUPERADA] C2 cobrado a +{1} ticks (+${2:F2}). Contrato 3 Runner en MODO AHOGO a {3} ticks del Low.",
                            Time[0], CascadaTp2Ticks, CascadaTp2Ticks * TickSize * Instrument.MasterInstrument.PointValue, CascadaChokeTicks));
                    }

                    // CONTRATO 3 (RUNNER): Modo Ahogo a 8 ticks del pico bajista (CascadaChokeTicks)
                    if (cascadaChokeActive || (cascadaC1Achieved && cascadaC2Achieved))
                    {
                        double chokeSl = lowestPriceSinceEntry + (CascadaChokeTicks * TickSize);
                        if (Close[0] >= chokeSl || High[0] >= chokeSl)
                        {
                            ExitShort("Cascada_C3_RunnerAhogo", "Cascada50_Short");
                            Print(string.Format("{0} - [PAS - Cascada | RUNNER AHOGADO EN EL FONDO] Salida a Mercado @ ~{1:F2} (Pico: {2:F2}). ¡Ganancia máxima asegurada!",
                                Time[0], Close[0], lowestPriceSinceEntry));
                        }
                    }
                    // Stop de Resguardo tras cobrar C1: BreakEven - 1 tick
                    else if (cascadaC1Achieved)
                    {
                        double bePrice = entryPrice - (1 * TickSize);
                        if (Close[0] >= bePrice || High[0] >= bePrice)
                        {
                            ExitShort("Cascada_Stop_BE", "Cascada50_Short");
                            Print(string.Format("{0} - [PAS - Cascada | SALIDA EN BREAKEVEN] Posición restante protegida en {1:F2}. Salida sin pérdida.",
                                Time[0], bePrice));
                        }
                    }
                    // Stop Inicial antes de C1 (según las 6 Zonas de Keltner):
                    else
                    {
                        double initialSl = (entryPrice <= keltnerSmall.Lower[0]) 
                            ? (keltnerBig.Midline[0] + (2 * TickSize)) 
                            : (keltnerSmall.Upper[0] + (2 * TickSize));

                        if (Close[0] >= initialSl || High[0] >= initialSl)
                        {
                            ExitShort("SL_Zona_Cascada", "Cascada50_Short");
                            Print(string.Format("{0} - [PAS - Cascada | STOP INICIAL ZONA] Precio perforó SL ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                Time[0], initialSl, Close[0]));
                        }
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
            z1LongArmed = false;
            z1ShortArmed = false;
            sobbArmedLong = false;
            sobbArmedShort = false;
            sobbHasFloatedLong = false;
            sobbHasFloatedShort = false;
            sobbLevel1Achieved = false;
            sobbLevel2Achieved = false;
            sobbLevel3ChokeActive = false;

            ratchetSetup = RatchetSetup.None;
            ratchetSetupBarCounter = 0;
            ratchetC1Achieved = false;
            ratchetC2Achieved = false;
            ratchetChokeActive = false;

            cascadaC1Achieved = false;
            cascadaC2Achieved = false;
            cascadaChokeActive = false;

            highestPriceSinceEntry = 0.0;
            lowestPriceSinceEntry = double.MaxValue;
        }

        #endregion

        #region Control de Cierre de Posición y Estadísticas Diarias
        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            if (marketPosition == MarketPosition.Flat)
            {
                // Sincronización inmediata de contratos cerrados, strikes y cálculo de pisos
                SyncTradesAndStrikes();
                UpdateDailyPnLAndFloors();

                // Verificación de corte a la 1:00 PM si el trade que acaba de cerrar ocurrió después de la 1:00 PM
                if (UseDailyManagement && UseTier1LunchCutoff && !isDailyLocked && currentDailyTier <= 1)
                {
                    int timeNow = ToTime(Time[0]);
                    if (timeNow >= Tier1CutoffTime)
                    {
                        isDailyLocked = true;
                        dailyLockReason = string.Format("CORTE DE OPERATIVA A LA 1:00 PM TRAS CIERRE (PnL ${0:F2})", dailyRealizedPnL);
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [ESCUDO DIARIO | CORTE A LA 1:00 PM TRAS CIERRE DE TRADE] Trade cerrado después de las 01:00 PM EST ({1}).\n" +
                                           "PnL Diario Realizado: ${2:F2} USD (Zona {3}). ¡APAGANDO OPERATIVA POR EL RESTO DEL DÍA!\n" +
                                           "****************************************************************************************",
                            Time[0], timeNow, dailyRealizedPnL, currentDailyTier == 1 ? "Escalón 1" : "Base"));
                    }
                }

                // Si se cerró Cascada, se resetea su contador para permitir re-entradas si la tendencia persiste
                if (currentAce == AceType.As4_Cascada50)
                {
                    barsCascadaShortCount = 0;
                    barsCascadaLongCount = 0;
                    Print(string.Format("{0} - [PAS - Cascada] Posición cerrada. Contador reseteado a 0; re-entrará si la tendencia acumula {1} barras más.",
                        Time[0], CascadaMinBars));
                }

                ResetAllTradeCounters();
            }
        }

        private void SyncTradesAndStrikes()
        {
            if (Position.MarketPosition == MarketPosition.Flat && SystemPerformance != null && SystemPerformance.AllTrades != null && SystemPerformance.AllTrades.Count > lastReportedTradeCount)
            {
                int newTradesCount = SystemPerformance.AllTrades.Count - lastReportedTradeCount;
                double sessionTradesProfit = 0.0;

                for (int i = SystemPerformance.AllTrades.Count - newTradesCount; i < SystemPerformance.AllTrades.Count; i++)
                {
                    Trade trade = SystemPerformance.AllTrades[i];
                    double netProfit = trade.ProfitCurrency;
                    sessionTradesProfit += netProfit;

                    Print(string.Format("{0} - [PAS | CONTRATO CERRADO] Salida: {1} | PnL Contrato: {2:C2} | PnL Diario Acumulado: {3:C2}",
                        trade.Exit.Time, trade.Exit.Name, netProfit, dailyRealizedPnL));
                }

                lastReportedTradeCount = SystemPerformance.AllTrades.Count;

                // REGLA DE 3 STRIKES POR OPERACIÓN GLOBAL (CICLO FLAT -> FLAT):
                // Evaluamos el resultado neto combinado de todos los contratos de esta operación completa
                if (sessionTradesProfit < 0)
                {
                    consecutiveLosses++;
                    Print(string.Format("{0} - [PAS | STRIKE REGISTRADO] Operación global cerrada con pérdida neta ({1:C2}). Strikes consecutivos: {2}/{3}",
                        Time[0], sessionTradesProfit, consecutiveLosses, MaxConsecutiveLosses));
                }
                else if (sessionTradesProfit > 0)
                {
                    consecutiveLosses = 0;
                    Print(string.Format("{0} - [PAS | STRIKES REINICIADOS] Operación global cerrada con ganancia neta ({1:C2}). Contador de strikes en 0.",
                        Time[0], sessionTradesProfit));
                }

                if (consecutiveLosses >= MaxConsecutiveLosses)
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

        private void UpdateDailyPnLAndFloors()
        {
            if (!UseDailyManagement) return;

            // 1. Sincronización continua de PnL Realizado de la sesión actual directamente de la colección oficial
            double sessionClosedPnL = 0.0;
            if (SystemPerformance != null && SystemPerformance.AllTrades != null && SystemPerformance.AllTrades.Count > 0)
            {
                for (int i = 0; i < SystemPerformance.AllTrades.Count; i++)
                {
                    Trade t = SystemPerformance.AllTrades[i];
                    if (t.Exit != null && t.Exit.Time.Date == Time[0].Date)
                    {
                        sessionClosedPnL += t.ProfitCurrency;
                    }
                }
                dailyRealizedPnL = sessionClosedPnL;
            }

            // 2. Trailing y Escalones de Piso Protegido (Ratcheting Up: solo sube, NUNCA baja)
            if (!isDailyLocked)
            {
                // Escalón 3 / Modo Dulce: >= $900 USD ($90 MES) -> ¡Sigue operando y sube el piso con Trailing!
                if (dailyRealizedPnL >= DailyGoal_Piso3)
                {
                    currentDailyTier = 3;
                    peakDailyClosedPnL = Math.Max(peakDailyClosedPnL, dailyRealizedPnL);
                    double dynamicFloor = peakDailyClosedPnL - DailyTrailingGiveback;
                    if (dynamicFloor > currentDailyFloor)
                    {
                        currentDailyFloor = dynamicFloor;
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [¡MODO DULCE ACTIVO! TRAILING DIARIO INFINITO] PnL Diario Realizado: ${1:F2} (Pico: ${2:F2}).\n" +
                                           "¡SEGUIMOS OPERANDO SIN TECHO ARTIFICIAL! Nuevo Piso Protegido clavado en: +${3:F2} USD (Giveback: -${4:F2}).\n" +
                                           "****************************************************************************************",
                            Time[0], dailyRealizedPnL, peakDailyClosedPnL, currentDailyFloor, DailyTrailingGiveback));
                    }
                }
                // Escalón 2: >= $600 USD ($60 MES) -> Piso Protegido de +$500 USD (+ $50 MES)
                else if (dailyRealizedPnL >= DailyGoal_Piso2)
                {
                    if (currentDailyFloor < DailyFloor_Piso2)
                    {
                        currentDailyTier = 2;
                        currentDailyFloor = DailyFloor_Piso2;
                        Print(string.Format("========================================================================================\n" +
                                           "{0} - [ESCUDO DIARIO | ESCALÓN 2 ACTIVADO] PnL Diario: ${1:F2} (Superó Meta 2: ${2:F2}).\n" +
                                           "Nuevo Piso Protegido CLAVADO en +${3:F2} USD. ¡Pase lo que pase, los $500 están garantizados!\n" +
                                           "========================================================================================",
                            Time[0], dailyRealizedPnL, DailyGoal_Piso2, currentDailyFloor));
                    }
                }
                // Escalón 1: >= $400 USD ($40 MES) -> Piso Protegido de +$250 USD (+ $25 MES)
                else if (dailyRealizedPnL >= DailyGoal_Piso1)
                {
                    if (currentDailyFloor < DailyFloor_Piso1)
                    {
                        currentDailyTier = 1;
                        currentDailyFloor = DailyFloor_Piso1;
                        Print(string.Format("========================================================================================\n" +
                                           "{0} - [ESCUDO DIARIO | ESCALÓN 1 ACTIVADO] PnL Diario: ${1:F2} (Superó Meta Base: ${2:F2}).\n" +
                                           "Nuevo Piso Protegido CLAVADO en +${3:F2} USD. Jugamos con la plata de la casa buscando el Escalón 2.\n" +
                                           "========================================================================================",
                            Time[0], dailyRealizedPnL, DailyGoal_Piso1, currentDailyFloor));
                    }
                }
                // Si el PnL cerrado cayó por debajo del piso protegido ya establecido
                else if (currentDailyFloor > -DailyLossLimit && dailyRealizedPnL <= currentDailyFloor)
                {
                    isDailyLocked = true;
                    dailyLockReason = string.Format("PISO DE PROTECCIÓN (${0:F2}) ALCANZADO", currentDailyFloor);
                    Print(string.Format("{0} - [ESCUDO DIARIO | CIERRE POR PISO] PnL Diario Realizado cayó a ${1:F2} (Piso protegido: ${2:F2}). Operativa bloqueada por el resto del día.",
                        Time[0], dailyRealizedPnL, currentDailyFloor));
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
        [Display(Name = "Activar Gestión Diaria", Description = "Activa Cortacircuito y Meta Diaria", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 1)]
        public bool UseDailyManagement { get; set; }

        [NinjaScriptProperty]
        [Range(10.0, 5000.0)]
        [Display(Name = "Cortacircuito Diario Inicial ($)", Description = "Pérdida máxima inicial para apagar bot ($800 ES / $80 MES - 2.5 a 3 vidas)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 2)]
        public double DailyLossLimit { get; set; }

        [NinjaScriptProperty]
        [Range(50.0, 5000.0)]
        [Display(Name = "Meta Escalón 1 ($)", Description = "Meta para activar Piso 1 ($450 ES / $45 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 3)]
        public double DailyGoal_Piso1 { get; set; }

        [NinjaScriptProperty]
        [Range(10.0, 5000.0)]
        [Display(Name = "Piso Protegido Escalón 1 ($)", Description = "Piso mínimo garantizado tras tocar Meta 1 ($300 ES / $30 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 4)]
        public double DailyFloor_Piso1 { get; set; }

        [NinjaScriptProperty]
        [Range(100.0, 10000.0)]
        [Display(Name = "Meta Escalón 2 ($)", Description = "Meta para activar Piso 2 ($750 ES / $75 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 5)]
        public double DailyGoal_Piso2 { get; set; }

        [NinjaScriptProperty]
        [Range(50.0, 10000.0)]
        [Display(Name = "Piso Protegido Escalón 2 ($)", Description = "Piso mínimo garantizado tras tocar Meta 2 ($550 ES / $55 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 6)]
        public double DailyFloor_Piso2 { get; set; }

        [NinjaScriptProperty]
        [Range(200.0, 20000.0)]
        [Display(Name = "Umbral Modo Dulce ($)", Description = "Meta para activar Trailing Infinito ($1,000 ES / $100 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 7)]
        public double DailyGoal_Piso3 { get; set; }

        [NinjaScriptProperty]
        [Range(10.0, 2000.0)]
        [Display(Name = "Colchón Trailing Infinito ($)", Description = "Distancia de retroceso permitida del récord ganado ($150 ES / $15 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 8)]
        public double DailyTrailingGiveback { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Máximo Strikes Seguidos", Description = "Número de pérdidas seguidas para apagar por el día", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 9)]
        public int MaxConsecutiveLosses { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Victoria Temprana Escalón 1", Description = "Si a la 1:00 PM EST estamos en Escalón 1 ($400-$599), apaga nuevas entradas y asegura la ganancia", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 10)]
        public bool UseTier1LunchCutoff { get; set; }

        [NinjaScriptProperty]
        [Range(100000, 160000)]
        [Display(Name = "Hora Límite Escalón 1 (HHmmss)", Description = "Hora límite para apagar en Escalón 1 (130000 = 01:00 PM EST)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 11)]
        public int Tier1CutoffTime { get; set; }

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
        [Range(5, 200)]
        [Display(Name = "Cascada Barras Mínimas", Description = "Barras con EMA 200 separada de Banda 3.5 (25 por defecto)", GroupName = "7. As 4: Cascada 25", Order = 1)]
        public int CascadaMinBars { get; set; }

        [NinjaScriptProperty]
        [Range(4, 30)]
        [Display(Name = "Cascada C1 TP (Ticks)", Description = "Target de salida de C1 y activación de BE (8 ticks = $100 ES / $10 MES)", GroupName = "7. As 4: Cascada 25", Order = 2)]
        public int CascadaTp1Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(8, 50)]
        [Display(Name = "Cascada C2 TP (Ticks)", Description = "Target de salida de C2 (16 ticks = $200 ES / $20 MES)", GroupName = "7. As 4: Cascada 25", Order = 3)]
        public int CascadaTp2Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(2, 20)]
        [Display(Name = "Cascada C3 Ahogo (Ticks)", Description = "Distancia ceñida para el trailing del C3 Runner (8 ticks)", GroupName = "7. As 4: Cascada 25", Order = 4)]
        public int CascadaChokeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Cascada Separación Ticks", GroupName = "7. As 4: Cascada 25", Order = 5)]
        public int CascadaSeparationTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Cascada Pendiente Mínima Ticks", GroupName = "7. As 4: Cascada 25", Order = 6)]
        public int CascadaMinSlopeTicks { get; set; }

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
        #endregion
    }
}
