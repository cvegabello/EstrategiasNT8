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
// VERSIÓN: 6.0 - Poker de Ases (PAS) [Ratchet Sniper Vertical con EMA200 Fuera de Canal 3.5 & Pendiente Midline, SL Tope 12tk, SOBB Monocontrato & Cortacircuito -$650]
//
// LAS 4 CARTAS DEL POKER (LOS 4 ASES):
//   1. As 1: [Z1-100Bar] - Ruptura quirúrgica tras >= 100 barras comprimidas en EMA 200 con Canal Grande 3.5 y TTL 25 barras.
//   2. As 2: [SOBB]      - SurferOutBigBanda: MONOCONTRATO QUIRÚRGICO (1 contrato fijo) con Stop Estructural holgado
//                          anclado a la Banda Pequeña 1.75 (Tope máx 24 ticks = -$300 USD). Cero asfixia, salida en expansión (+20 ticks)
//                          o por repliegue de TEMA 9 (Ciclo Terminado) tras alcanzar Break-Even (+10 ticks).
//   3. As 3: [Ratchet]   - Súper-tendencia vertical institucional real: Requiere OBLIGATORIAMENTE que EMA 200 esté
//                          FUERA del Canal Grande 3.5 y que la Línea Media tenga pendiente a favor. Stop inicial con
//                          TOPE DE SEGURIDAD de 12 ticks (máx -$450 USD en 3 contratos) y Chicle Parabólico.
//   4. As 4: [Cascada]   - El Tren Bala Desbocado (25 barras): FILTRO ESTRUCTURAL ESTRICTO DE ZONAS 0/1 (Largos)
//                          y ZONAS 4/5 (Cortos). Prohibido comprar en Zona 2 (agotamiento de tendencia).
//
// GESTIÓN DE RIESGO INTELIGENTE (DESESCALADO DINÁMICO, DESESCALADO TRAS PÉRDIDA & CORTE A LA 1:00 PM):
//   - Desescalado tras Pérdida: Si la operación previa cerró con pérdida (Strike activo), el siguiente trade entra
//     obligatoriamente con 1 solo contrato para proteger la cuenta.
//   - Desescalado Dinámico: Si el PnL está cerca del piso protegido, reduce la posición (a 2 o 1 contrato) para garantizar
//     un Stop Loss saludable sin romper el piso garantizado.
//   - Victoria Temprana a la 1:00 PM: Si a las 01:00 PM EST estamos en Escalón 1 ($400 - $599 USD), apaga nuevas entradas y
//     asegura la ganancia del día para evitar amagues de bajo volumen en la sesión de la tarde.
//
// ESCUDO DE CUENTA (CALIBRACIÓN INSTITUCIONAL 2.5 - 3 VIDAS - TRAILING INFINITO - BLINDAJE v6.0):
//   - Cortacircuito Inicial: -$650 USD (ES) / -$65 USD (MES) -> Respaldo para 2.5 a 3 oportunidades saludables.
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

        // === VARIABLES DEL AS 1: ZONA 1 (100 BARRAS - GOLAZO A LÍNEA MEDIA EN 40 BARRAS) ===
        private int lastTemaSide = 0;              // 1 = Arriba de EMA200, -1 = Debajo de EMA200
        private bool crossUpConsumed = false;
        private bool crossDownConsumed = false;
        private int barsBelowEmaWithoutCross = 0;  // Acumulación continua con TEMA/Precio bajo EMA200
        private int barsAboveEmaWithoutCross = 0;  // Acumulación continua con TEMA/Precio sobre EMA200
        private bool z1LongArmed = false;          // Acumulación de 100 barras completada para Largo
        private bool z1ShortArmed = false;         // Acumulación de 100 barras completada para Corto
        private bool z1WaitingForMidlineLong = false; // Cronómetro activo tras cruce TEMA buscando toque de Línea Media (máx 40 barras)
        private int z1MidlineCountdownLong = 0;      // Contador de barras transcurridas tras el cruce para Largo
        private bool z1WaitingForMidlineShort = false;// Cronómetro activo tras cruce TEMA buscando toque de Línea Media (máx 40 barras)
        private int z1MidlineCountdownShort = 0;     // Contador de barras transcurridas tras el cruce para Corto
        private bool z1C1Achieved = false;           // Contrato 1 cerrado a +8 ticks (BE armado)
        private bool z1C2Achieved = false;           // Contrato 2 cerrado a +16 ticks
        private bool z1ChokeActive = false;          // Contrato 3 Runner en Modo Ahogo (8 ticks)

        // === VARIABLES DEL AS 2: SOBB (SurferOutBigBanda - GATILLO 2 CAPAS + ESTRUCTURA CONSECUTIVA v9.0) ===
        private bool sobbArmedLong = false;        // Fase 1: TEMA 9 cruzó Upper 3.5 (Espera activa a que la vela salga)
        private bool sobbArmedShort = false;       // Fase 1: TEMA 9 cruzó Lower 3.5 (Espera activa a que la vela salga)
        private bool sobbHasFloatedLong = false;   // Bandera de vela flotando afuera para Largos
        private bool sobbHasFloatedShort = false;  // Bandera de vela flotando afuera para Cortos
        private int  sobbFloatingBarsCountLong = 0;  // Contador de barras consecutivas flotando afuera en Largos
        private int  sobbFloatingBarsCountShort = 0; // Contador de barras consecutivas flotando afuera en Cortos
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

        // === VARIABLES DEL AS 4: CASCADA (CAZADOR DE RÁPIDOS POR PENDIENTE NORMALIZADA + CANDADO $180) ===
        private int barsCascadaShortCount = 0;     // Barras con EMA200 > Upper 3.5 + 4 ticks (conteo de fondo)
        private int barsCascadaLongCount  = 0;     // Barras con EMA200 < Lower 3.5 - 4 ticks (conteo de fondo)
        private bool cascadaCycleLongFired  = false; // Bandera de 1 solo tiro por impulso en Largos (Anti-compras en techos)
        private bool cascadaCycleShortFired = false; // Bandera de 1 solo tiro por impulso en Cortos (Anti-ventas en suelos)
        private bool isCascadaLockActive  = false; // Candado de $180 activado (salida en Banda 1.75 favorable)
        private bool cascadaC1Achieved    = false; // Contrato 1 cerrado a +8 ticks (BE armado)
        private bool cascadaC2Achieved    = false; // Contrato 2 cerrado a +16 ticks
        private bool cascadaChokeActive   = false; // Contrato 3 Runner en Modo Ahogo (8 ticks)
        private int  cascadaTradesToday   = 0;     // Contador de disparos de Cascada en la sesión diaria actual
        private bool cascadaHasWonToday   = false; // Bandera: Cascada ya cobró un trade ganador hoy (Pisar Suave liberado)
        private DateTime cascadaQuarantineEndTime = DateTime.MinValue; // Fin de la cuarentena de 60 min tras victoria (>= $180 USD)

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
        private ATR atr14;                   // ATR 14 para Normalización Institucional de Pendientes LinReg

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
                Description                                 = @"Estrategia Institucional Poker de Ases Sniper (PAS) v6.0 en 610 Ticks para ES/MES. Combina Z1-100Bar (Canal Grande 3.5 con TTL 25 Barras & Filtro Zona 0), SOBB (Monocontrato Quirúrgico con Oxígeno Estructural), Ratchet (Vertical Institucional con EMA 200 Externa 3.5, Pendiente Midline & SL Tope 12tk), Cascada 25, Cortacircuito -$650 USD, Desescalado tras Pérdida y Victoria Temprana a la 1:00 PM.";
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
                Version                                     = "10.1 - Poker de Ases (PAS) [Cascada Salida de Rapido Real, Candado $180, Pendiente LinReg+ATR, Logs Humanos]";

                // Horario RTH estricto (Nueva York EST - Oficial 09:50 AM - Regla de Oro AGENTS.md)
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

                // Parámetros del As 1: Z1-100Bar (Golazo Sincronizado EMA200 a Línea Media en 40 Barras + Escalonamiento 3 Fases)
                MinCrossTicks                               = 2.3;    // Penetración mínima de cruce real
                MinBarsZona1                                = 100;    // 100 barras obligatorias de compresión previa
                Z1MaxBarsToMidline                          = 40;     // Ventana máxima de 40 barras para que EMA 200 toque la Línea Media tras el cruce
                Z1MaxZone0Ticks                             = 10;     // Límite máximo de ticks en Zona 0 (no más de 10 ticks sobre/bajo Banda 3.5)
                Zone1Tp1Ticks                               = 8;      // C1 TP (+8 ticks = $100 ES / $10 MES) -> Pasa a BE +1tk
                Zone1Tp2Ticks                               = 16;     // C2 TP (+16 ticks = $200 ES / $20 MES) -> Meta de Expansión
                Zone1ChokeTicks                             = 8;      // C3 Runner Ahogo a 8 ticks del High/Low
                SlOffsetTicks                               = 2;      // Offset de respiro en ticks para el Stop Loss
                MinSafetySlTicks                            = 8;      // Colchón mínimo de seguridad inicial

                // Parámetros del As 2: SOBB (SurferOutBigBanda) - Monocontrato Quirúrgico con Oxígeno Estructural
                SobbRequiredFloatingBars                    = 2;      // Barras mínimas consecutivas 100% flotando afuera para confirmar fuga (2 barras por defecto)
                SobbMaxFloatingBarsWindow                   = 3;      // Ventana máxima de barras consecutivas afuera para gatillar (máx 3 barras antes de cancelar)
                SobbTp1Ticks                                = 10;     // Escalón 1: +10 ticks ($125 ES / $12.50 MES) - Pasa Stop a Break-Even + 1tk
                SobbTp2Ticks                                = 20;     // Escalón 2: +20 ticks ($250 ES / $25.00 MES) - Meta de Expansión
                SobbMaxSlTicks                              = 24;     // Tope de seguridad máximo para SL Estructural (24 ticks = 6 pts = $300 ES / $30 MES)
                SobbChokeThresholdTicks                     = 30;     // Escalón 3: +30 ticks ($375 ES / $37.50 MES) - Activa Modo Ahogo
                SobbChokeTicks                              = 8;      // Distancia ceñida pegada al High/Low para el trailing del Runner

                // Parámetros del As 3: Ratchet (El Chicle Parabólico Original - Vertical Institucional)
                RatchetLookbackBars                         = 20;     // Barras sin tocar EMA 200
                RatchetCountdownBars                        = 10;     // Ventana de conteo buscando cruce de Línea Media
                RatchetTemaTolTicks                         = 2;      // Tolerancia en ticks para gancho de TEMA 9
                RatchetBreakEvenTicks                       = 22;     // BreakEven dinámico para Contrato 1
                RatchetChokeThresholdTicks                  = 72;     // Fase 3 Choke a los 72 ticks ($900 ES / $90 MES)
                RatchetChicleTicks                          = 7;      // Trailing ceñido del Chicle Parabólico (7 ticks)
                RatchetMaxSlTicks                           = 12;     // Tope máximo de seguridad para SL de Ratchet (12 ticks = $450 ES / $45 MES en 3 contratos)

                // Parámetros del As 4: Cascada (Cazador de Rápidos por Pendiente Normalizada + Candado $180)
                CascadaStartTime                            = 100000; // 10:00 AM EST - Filtro Anti-Acomodación de Pre-Mercado
                CascadaFirstEntryContracts                  = 1;      // Modo exploratorio (1 contrato)
                CascadaMinBars                              = 25;     // 25 barras consecutivas requeridas (compatibilidad)
                CascadaSeparationTicks                      = 4;      // Mínimo 4 ticks de separación de la Banda 3.5
                CascadaMinSlopeTicks                        = 2;      // Inclinación mínima requerida en la EMA 200
                CascadaLockTriggerDollars                   = 180.0;  // Umbral de ganancia en USD para activar Candado ($180 ES / $18 MES)
                CascadaMaxSlTicks                           = 24;     // Tope máximo de seguridad estructural inicial (24 ticks = 6 pts = $300 ES / $30 MES)
                CascadaBigWinThresholdDollars               = 180.0;  // Umbral de ganancia para activar Cuarentena de Saciedad ($180 ES / $18 MES)
                CascadaQuarantineMinutes                    = 60;     // Minutos de cuarentena/congelamiento de Cascada tras victoria (60 min)
                CascadaTp1Ticks                             = 8;      // (Compatibilidad NT8)
                CascadaTp2Ticks                             = 16;     // (Compatibilidad NT8)
                CascadaChokeTicks                           = 8;      // (Compatibilidad NT8)
                CascadaSlopeThreshold                       = 0.20;   // Umbral de aceleración de Pendiente Normalizada LinReg/ATR para Cascada (0.20)
                SlopeThresholdTrend                         = 0.15;   // Umbral de Pendiente Normalizada LinReg/ATR para Tendencia Real en SOBB y Z1 (0.15)

                // Escudo Diario de Cuenta (Trailing Diario Infinito - Calibración Ceñida -$650 USD para 2.5 - 3 Vidas)
                UseDailyManagement                          = true;   // Activar Cortacircuito y Trailing Diario
                DailyLossLimit                              = 650.0;  // Cortacircuito calibrado a -$650 ES / -$65 MES (2.5 a 3 vidas holgadas)
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
                    DailyLossLimit        = 65.0;
                    DailyGoal_Piso1       = 40.0;
                    DailyFloor_Piso1      = 25.0;
                    DailyGoal_Piso2       = 60.0;
                    DailyFloor_Piso2      = 50.0;
                    DailyGoal_Piso3       = 90.0;
                    DailyTrailingGiveback = 15.0;
                    CascadaLockTriggerDollars = 18.0;
                    CascadaBigWinThresholdDollars = 18.0;
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
                atr14        = ATR(14);

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
                z1LongArmed = false;
                z1ShortArmed = false;
                z1WaitingForMidlineLong = false;
                z1MidlineCountdownLong = 0;
                z1WaitingForMidlineShort = false;
                z1MidlineCountdownShort = 0;
                crossUpConsumed = false;
                crossDownConsumed = false;
                lastTemaSide = 0;
                barsCascadaLongCount = 0;
                barsCascadaShortCount = 0;
                cascadaTradesToday = 0;
                cascadaHasWonToday = false;
                cascadaQuarantineEndTime = DateTime.MinValue;
                cascadaCycleLongFired = false;
                cascadaCycleShortFired = false;
                sobbArmedLong = false;
                sobbArmedShort = false;
                sobbHasFloatedLong = false;
                sobbHasFloatedShort = false;
                sobbFloatingBarsCountLong = 0;
                sobbFloatingBarsCountShort = 0;

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

            // 3.1 CORTACIRCUITO UNIVERSAL A LA 1:00 PM (SOLO ESCALÓN 3 / MODO DULCE PUEDE CONTINUAR HASTA LAS 4:00 PM)
            // Si son las 01:00 PM EST y no estamos en el último escalón (Modo Dulce Trailing Infinito >= $900),
            // se apaga la operativa para evitar operar en la tarde picada o eventos de alta volatilidad (FED/FOMC) y blindar las ganancias.
            if (UseDailyManagement && UseTier1LunchCutoff && !isDailyLocked && timeNow >= Tier1CutoffTime)
            {
                if (currentDailyTier < 3)
                {
                    if (Position.MarketPosition == MarketPosition.Flat)
                    {
                        isDailyLocked = true;
                        dailyLockReason = string.Format("CORTE DE OPERATIVA A LA 1:00 PM (PnL ${0:F2})", dailyRealizedPnL);
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [ESCUDO DIARIO | CORTE A LA 1:00 PM EST] Hora actual ({1}) alcanzó las 01:00 PM.\n" +
                                           "PnL Diario Realizado: ${2:F2} USD (Zona {3}). ¡APAGANDO OPERATIVA POR EL RESTO DEL DÍA!\n" +
                                           "No se regala dinero en la tarde sin volumen ni en trampas de la FED. ¡Día cerrado con disciplina!\n" +
                                           "****************************************************************************************",
                            Time[0], timeNow, dailyRealizedPnL, currentDailyTier == 2 ? "Escalón 2" : (currentDailyTier == 1 ? "Escalón 1" : "Base")));
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
                // Si el piso protegido está activo (Escalón 1 o 2: currentDailyFloor > 0) y el PnL cerrado lo supera, calculamos el precio
                // exacto en ticks donde el retroceso alcanzaría el piso, para liquidar sin esperar slippage de barra.
                if (!isDailyLocked && Position.MarketPosition != MarketPosition.Flat && currentDailyFloor > 0 && dailyRealizedPnL > currentDailyFloor)
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
                    
                    if (currentDailyTier == 0)
                    {
                        Print(string.Format("****************************************************************************************\n" +
                                            "{0} - [CORTACIRCUITO DE SEGURIDAD | PÉRDIDA MÁXIMA ALCANZADA]\n" +
                                            "¡APAGUE ESA VAINA! Ya perdiste el límite del día (-${1:F2} USD). Agradezca que no te hice perder más.\n" +
                                            "Olvídate de esa pérdida que fueron centavos, mañana será otro día.\n" +
                                            "¡Adiós pues, que no te veo apagando ese PC! 🛑🔒\n" +
                                            "****************************************************************************************",
                            Time[0], Math.Abs(totalDailyFloatingPnL)));
                    }
                    else
                    {
                        Print(string.Format("****************************************************************************************\n" +
                                            "{0} - [ESCUDO DIARIO | PISO DE GANANCIAS ALCANZADO]\n" +
                                            "¡APAGUE Y VÁMONOS! Te fuiste con ${1:F2} USD en el bolsillo, ¡vete a montar cicla! 🚴‍♂️💨\n" +
                                            "¿Qué haces pegado a ese PC todo el día si ya ganaste? ¡Para qué más!\n" +
                                            "Liquidando a mercado inmediatamente. Operativa bloqueada por hoy con la plata a salvo.\n" +
                                            "****************************************************************************************",
                            Time[0], totalDailyFloatingPnL));
                    }
                    
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
                // Reseteo inteligente del ciclo de Cascada (cuando el precio descansa en Línea Media o pendiente se neutraliza)
                double currentNormMidSlope = GetNormalizedSlope(keltnerBig.Midline);
                if (cascadaCycleLongFired && (Low[0] <= keltnerBig.Midline[0] || currentNormMidSlope < 0.05))
                {
                    cascadaCycleLongFired = false;
                }
                if (cascadaCycleShortFired && (High[0] >= keltnerBig.Midline[0] || currentNormMidSlope > -0.05))
                {
                    cascadaCycleShortFired = false;
                }

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

            // === CONTADOR AS 1: ZONA 1 (GOLAZO SINCRONIZADO: ACUMULACIÓN 100 BARRAS + CRONÓMETRO 40 BARRAS) ===
            double currentUpTicks   = (tema9[0] - ema200[0]) / TickSize;
            double currentDownTicks = (ema200[0] - tema9[0]) / TickSize;

            double upperBig   = keltnerBig.Upper[0];
            double lowerBig   = keltnerBig.Lower[0];
            double upperSmall = keltnerSmall.Upper[0];
            double lowerSmall = keltnerSmall.Lower[0];
            double midline    = keltnerBig.Midline[0];

            // =========================================================================
            // A) MONITOREO DE ACUMULACIÓN Y CRONÓMETRO 40 BARRAS PARA LARGOS
            // =========================================================================
            // 1. Acumulación de 100 barras: La línea azul (TEMA 9) por debajo de la línea roja (EMA 200) sin cruzar
            // La referencia pura es la TEMA 9 contra la EMA 200 (sin asfixia por mechas o ruido del precio).
            bool temaBajoEma = tema9[0] < ema200[0];

            if (temaBajoEma && !z1WaitingForMidlineLong)
            {
                barsBelowEmaWithoutCross++;
                if (barsBelowEmaWithoutCross >= MinBarsZona1)
                {
                    z1LongArmed = true;
                }
                if (timeNow >= StartTime && timeNow <= ForceCloseTime && barsBelowEmaWithoutCross % 25 == 0)
                {
                    Print(string.Format("{0} - [PAS - Z1-100B | LARGO ACUMULANDO] TEMA9 < EMA200 por {1}/{2} barras | Armed: {3}",
                        Time[0], barsBelowEmaWithoutCross, MinBarsZona1, z1LongArmed));
                }
            }
            else if (!z1WaitingForMidlineLong)
            {
                if (!z1LongArmed)
                {
                    barsBelowEmaWithoutCross = 0;
                }
            }

            // 2. Activación del Cronómetro de 40 Barras:
            // Al darse el cruce alcista (TEMA 9 cruza hacia arriba de EMA 200) tras haber acumulado >= 100 barras
            if (z1LongArmed && !z1WaitingForMidlineLong && tema9[0] > ema200[0])
            {
                z1WaitingForMidlineLong = true;
                z1MidlineCountdownLong = 0;
                z1LongArmed = false;
                barsBelowEmaWithoutCross = 0;
                Print(string.Format("{0} - [PAS - Z1-100B | CRUCE ALCISTA DETECTADO] TEMA 9 ({1:F2}) cruzó hacia arriba de EMA 200 ({2:F2}) tras acumular 100 barras. ¡CRONÓMETRO DE 40 BARRAS ACTIVADO buscando toque de Línea Media!",
                    Time[0], tema9[0], ema200[0]));
            }

            // 3. Monitoreo durante las 40 Barras de Largo:
            if (z1WaitingForMidlineLong)
            {
                z1MidlineCountdownLong++;

                // A) Si supera la ventana máxima de 40 barras: expira
                if (z1MidlineCountdownLong > Z1MaxBarsToMidline)
                {
                    z1WaitingForMidlineLong = false;
                    z1MidlineCountdownLong = 0;
                    Print(string.Format("{0} - [PAS - Z1-100B | LARGO EXPIRADO] Se cumplieron {1} barras tras el cruce sin que EMA 200 tocara la Línea Media. Setup cancelado.",
                        Time[0], Z1MaxBarsToMidline));
                }
                // B) Si la TEMA 9 hace un gancho falso real y penetra por debajo de la EMA 200 por >= 1 tick (0.25 pts):
                else if ((ema200[0] - tema9[0]) >= (1.0 * TickSize))
                {
                    z1WaitingForMidlineLong = false;
                    z1MidlineCountdownLong = 0;
                    Print(string.Format("{0} - [PAS - Z1-100B | LARGO ABORTADO] Gancho falso real: TEMA 9 ({1:F2}) penetró {2:F2} ticks por debajo de EMA 200 ({3:F2}) durante la espera. Cronómetro cancelado.",
                        Time[0], tema9[0], (ema200[0] - tema9[0]) / TickSize, ema200[0]));
                }
            }

            // =========================================================================
            // B) MONITOREO DE ACUMULACIÓN Y CRONÓMETRO 40 BARRAS PARA CORTOS
            // =========================================================================
            // 1. Acumulación de 100 barras: La línea azul (TEMA 9) por encima de la línea roja (EMA 200) sin cruzar
            bool temaSobreEma = tema9[0] > ema200[0];

            if (temaSobreEma && !z1WaitingForMidlineShort)
            {
                barsAboveEmaWithoutCross++;
                if (barsAboveEmaWithoutCross >= MinBarsZona1)
                {
                    z1ShortArmed = true;
                }
                if (timeNow >= StartTime && timeNow <= ForceCloseTime && barsAboveEmaWithoutCross % 25 == 0)
                {
                    Print(string.Format("{0} - [PAS - Z1-100B | CORTO ACUMULANDO] TEMA9 > EMA200 por {1}/{2} barras | Armed: {3}",
                        Time[0], barsAboveEmaWithoutCross, MinBarsZona1, z1ShortArmed));
                }
            }
            else if (!z1WaitingForMidlineShort)
            {
                if (!z1ShortArmed)
                {
                    barsAboveEmaWithoutCross = 0;
                }
            }

            // 2. Activación del Cronómetro de 40 Barras para Cortos:
            if (z1ShortArmed && !z1WaitingForMidlineShort && tema9[0] < ema200[0])
            {
                z1WaitingForMidlineShort = true;
                z1MidlineCountdownShort = 0;
                z1ShortArmed = false;
                barsAboveEmaWithoutCross = 0;
                Print(string.Format("{0} - [PAS - Z1-100B | CRUCE BAJISTA DETECTADO] TEMA 9 ({1:F2}) cruzó hacia abajo de EMA 200 ({2:F2}) tras acumular 100 barras. ¡CRONÓMETRO DE 40 BARRAS ACTIVADO buscando toque de Línea Media!",
                    Time[0], tema9[0], ema200[0]));
            }

            // 3. Monitoreo durante las 40 Barras de Corto:
            if (z1WaitingForMidlineShort)
            {
                z1MidlineCountdownShort++;

                if (z1MidlineCountdownShort > Z1MaxBarsToMidline)
                {
                    z1WaitingForMidlineShort = false;
                    z1MidlineCountdownShort = 0;
                    Print(string.Format("{0} - [PAS - Z1-100B | CORTO EXPIRADO] Se cumplieron {1} barras tras el cruce sin que EMA 200 tocara la Línea Media. Setup cancelado.",
                        Time[0], Z1MaxBarsToMidline));
                }
                // B) Si la TEMA 9 hace un gancho falso real y penetra por encima de la EMA 200 por >= 1 tick (0.25 pts):
                else if ((tema9[0] - ema200[0]) >= (1.0 * TickSize))
                {
                    z1WaitingForMidlineShort = false;
                    z1MidlineCountdownShort = 0;
                    Print(string.Format("{0} - [PAS - Z1-100B | CORTO ABORTADO] Gancho falso real: TEMA 9 ({1:F2}) penetró {2:F2} ticks por encima de EMA 200 ({3:F2}) durante la espera. Cronómetro cancelado.",
                        Time[0], tema9[0], (tema9[0] - ema200[0]) / TickSize, ema200[0]));
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

            // === CONTADOR AS 4: CASCADA (Separación de EMA 200 > 4 ticks de Banda 3.5 - Silencioso) ===
            double separationDist = CascadaSeparationTicks * TickSize;

            // Bajista (EMA 200 arriba de Upper 3.5 por > 4 ticks)
            if (ema200[0] > (keltnerBig.Upper[0] + separationDist))
            {
                barsCascadaShortCount++;
            }
            else
            {
                barsCascadaShortCount = 0;
            }

            // Alcista (EMA 200 abajo de Lower 3.5 por > 4 ticks)
            if (ema200[0] < (keltnerBig.Lower[0] - separationDist))
            {
                barsCascadaLongCount++;
            }
            else
            {
                barsCascadaLongCount = 0;
            }
        }
        #endregion

        #region Cálculo de Pendiente Normalizada Institucional (LinReg 5 Barras / ATR 14)
        /// <summary>
        /// Calcula la pendiente de regresión lineal por mínimos cuadrados (OLS) sobre 5 barras
        /// y la normaliza dividiéndola por el ATR(14), obteniendo un ratio adimensional de aceleración/inclinación.
        /// Fórmula cerrada OLS 5 barras: m = (2*Y[0] + 1*Y[1] - 1*Y[3] - 2*Y[4]) / 10
        /// Pendiente Normalizada = m / ATR(14)
        /// </summary>
        private double GetNormalizedSlope(ISeries<double> series)
        {
            if (CurrentBar < 15 || atr14 == null) return 0.0;
            
            double atr = atr14[0];
            if (atr <= 0.0001) return 0.0;

            // Regresión lineal exacta de 5 barras (pesos centrados: x in {-2, -1, 0, 1, 2})
            double linRegSlope = ((2.0 * series[0]) + (1.0 * series[1]) - (1.0 * series[3]) - (2.0 * series[4])) / 10.0;

            // Normalización por volatilidad
            return linRegSlope / atr;
        }
        #endregion

        #region Módulos de Entrada de los 4 Ases

        // =========================================================================
        // AS 1: FRANCOTIRADOR ZONA 1 (GOLAZO SINCRONIZADO: EMA200 A LÍNEA MEDIA EN 40 BARRAS)
        // =========================================================================
        private void EvaluateAs1_Zona1()
        {
            double upperSmall   = keltnerSmall.Upper[0];
            double lowerSmall   = keltnerSmall.Lower[0];
            double upperBig     = keltnerBig.Upper[0];
            double lowerBig     = keltnerBig.Lower[0];
            double midline      = keltnerBig.Midline[0];
            double normSlope    = GetNormalizedSlope(keltnerBig.Midline);

            // =========================================================================
            // AS 1 - GOLAZO LARGO: Toque de Línea Media durante ventana de 40 barras
            // =========================================================================
            if (z1WaitingForMidlineLong)
            {
                // 1. Toque o cruce de EMA 200 a la Línea Media:
                // La EMA 200 venía descendiendo y ahora toca o cruza la Línea Media
                bool emaTocoMidline = (ema200[0] <= midline) || (Math.Abs(ema200[0] - midline) <= (1.0 * TickSize));

                if (emaTocoMidline)
                {
                    // 2. Pendiente Normalizada positiva de la Línea Media (m/ATR >= SlopeThresholdTrend)
                    bool pendienteMidlineAlcista = normSlope >= SlopeThresholdTrend;

                    // 3. Ubicación del precio: Zona 1 o Zona 0 moderada (no más de 10 ticks sobre Upper 3.5)
                    bool precioEnZona = (Close[0] >= upperSmall) && (Close[0] <= upperBig + (Z1MaxZone0Ticks * TickSize));

                    // 4. Confirmación de TEMA 9 (azul) sin gancho falso bajo EMA 200 (tolerancia 1 tick)
                    bool confirmacionAlcista = (ema200[0] - tema9[0]) < (1.0 * TickSize);

                    if (pendienteMidlineAlcista && precioEnZona && confirmacionAlcista)
                    {
                        ExecuteAceEntry(AceType.As1_Zona1_100Bar, MarketPosition.Long, "Z1-100Bar_Long");
                        Print(string.Format("{0} - [PAS - Z1-100Bar | ¡GOLAZO LARGO EJECUTADO!] Entrada @ ~{1:F2} en barra {2}/40 | Pendiente Normalizada: +{3:F2} (Mín: +{4:F2}) | ATR: {5:F2} pts | TEMA9 ({6:F2}) > EMA200 ({7:F2})",
                            Time[0], Close[0], z1MidlineCountdownLong, normSlope, SlopeThresholdTrend, atr14 != null ? atr14[0] : 0.0, tema9[0], ema200[0]));
                    }
                    else
                    {
                        Print(string.Format("{0} - [PAS - Z1-100B | ¡PAILAS MI PEZ!] Cruce Midline en barra {1}/40 descartado: Pendiente en +{2:F2} (Requerido: >={3:F2}, ZonaOK: {4}, ConfOK: {5}) | ATR: {6:F2} pts. ¡Mercado plano sin fuerza, no regalamos ni un dólar!",
                            Time[0], z1MidlineCountdownLong, normSlope, SlopeThresholdTrend, precioEnZona, confirmacionAlcista, atr14 != null ? atr14[0] : 0.0));
                    }

                    // REGLA DE ORO DE PRECISIÓN: El cruce ya ocurrió. Se resetea inmediatamente el cronómetro
                    // tanto si disparó como si se descartó, eliminando cualquier posibilidad de entradas tardías.
                    z1WaitingForMidlineLong = false;
                    z1MidlineCountdownLong = 0;
                    return;
                }
            }

            // =========================================================================
            // AS 1 - GOLAZO CORTO: Toque de Línea Media durante ventana de 40 barras
            // =========================================================================
            if (z1WaitingForMidlineShort)
            {
                // 1. Toque o cruce de EMA 200 a la Línea Media:
                // La EMA 200 venía ascendiendo y ahora toca o cruza la Línea Media
                bool emaTocoMidline = (ema200[0] >= midline) || (Math.Abs(ema200[0] - midline) <= (1.0 * TickSize));

                if (emaTocoMidline)
                {
                    // 2. Pendiente Normalizada negativa de la Línea Media (m/ATR <= -SlopeThresholdTrend)
                    bool pendienteMidlineBajista = normSlope <= -SlopeThresholdTrend;

                    // 3. Ubicación del precio: Zona 4 o Zona 5 moderada (no más de 10 ticks bajo Lower 3.5)
                    bool precioEnZona = (Close[0] <= lowerSmall) && (Close[0] >= lowerBig - (Z1MaxZone0Ticks * TickSize));

                    // 4. Confirmación de TEMA 9 (azul) sin gancho falso sobre EMA 200 (tolerancia 1 tick)
                    bool confirmacionBajista = (tema9[0] - ema200[0]) < (1.0 * TickSize);

                    if (pendienteMidlineBajista && precioEnZona && confirmacionBajista)
                    {
                        ExecuteAceEntry(AceType.As1_Zona1_100Bar, MarketPosition.Short, "Z1-100Bar_Short");
                        Print(string.Format("{0} - [PAS - Z1-100Bar | ¡GOLAZO CORTO EJECUTADO!] Entrada @ ~{1:F2} en barra {2}/40 | Pendiente Normalizada: {3:F2} (Mín: -{4:F2}) | ATR: {5:F2} pts | TEMA9 ({6:F2}) < EMA200 ({7:F2})",
                            Time[0], Close[0], z1MidlineCountdownShort, normSlope, SlopeThresholdTrend, atr14 != null ? atr14[0] : 0.0, tema9[0], ema200[0]));
                    }
                    else
                    {
                        Print(string.Format("{0} - [PAS - Z1-100B | ¡PAILAS MI PEZ!] Cruce Midline en barra {1}/40 descartado: Pendiente en {2:F2} (Requerido: <={3:F2}, ZonaOK: {4}, ConfOK: {5}) | ATR: {6:F2} pts. ¡Mercado plano sin fuerza, no regalamos ni un dólar!",
                            Time[0], z1MidlineCountdownShort, normSlope, -SlopeThresholdTrend, precioEnZona, confirmacionBajista, atr14 != null ? atr14[0] : 0.0));
                    }

                    // REGLA DE ORO DE PRECISIÓN: El cruce ya ocurrió. Se resetea inmediatamente el cronómetro
                    // tanto si disparó como si se descartó, eliminando cualquier posibilidad de entradas tardías.
                    z1WaitingForMidlineShort = false;
                    z1MidlineCountdownShort = 0;
                    return;
                }
            }
        }

        // =========================================================================
        // AS 2: SOBB (SurferOutBigBanda - FILTRO DE 2 CAPAS: EMA ZONA 3 Y PENDIENTE NORMALIZADA)
        // =========================================================================
        private void EvaluateAs2_SOBB()
        {
            double upperBig   = keltnerBig.Upper[0];
            double lowerBig   = keltnerBig.Lower[0];
            double midline    = keltnerBig.Midline[0];
            double upperSmall = keltnerSmall.Upper[0];
            double lowerSmall = keltnerSmall.Lower[0];

            double normMidSlope   = GetNormalizedSlope(keltnerBig.Midline);
            double normUpperSlope = GetNormalizedSlope(keltnerBig.Upper);
            double normLowerSlope = GetNormalizedSlope(keltnerBig.Lower);

            // =========================================================================
            // SOBB LARGO: Cruce de TEMA 9 hacia afuera + Confirmación de Barras Consecutivas (v9.0)
            // 1. Armado: TEMA 9 cruza por primera vez hacia afuera de Upper 3.5.
            // 2. Desarmado por repliegue: Si TEMA 9 regresa adentro (<= Upper 3.5), se desactiva.
            // 3. Conteo de Barras Flotando Afuera (Low > Upper 3.5):
            //    - Barra 1 Afuera: Inicializa contador = 1.
            //    - Barra 2 Afuera: Si High[0] > High[1] (rompe techo anterior y continúa el empuje alcista)
            //      y se cumplen las 2 capas (EMA en Zona 3 + Pendientes Normalizadas alineadas >= SlopeThresholdTrend) -> ¡DISPARO LARGO!
            //    - Si High[0] <= High[1] (vela frenada/pequeña): Espera a la Barra 3 afuera.
            //    - Barra 3 Afuera: Si High[0] > High[1] y capas OK -> ¡DISPARO LARGO!
            //    - Si se pasa de SobbMaxFloatingBarsWindow (3 barras) sin romper techo o si regresa adentro -> ¡PAILAS!
            // =========================================================================
            bool isTemaCruzoSalidaLargo = (tema9[1] <= keltnerBig.Upper[1] && tema9[0] > upperBig);
            if (isTemaCruzoSalidaLargo)
            {
                sobbArmedLong = true;
                sobbHasFloatedLong = false;
                sobbFloatingBarsCountLong = 0;
                Print(string.Format("{0} - [PAS - SOBB v10.0 | LARGO ARMADO] TEMA 9 ({1:F2}) cruzó hacia afuera de Upper 3.5 ({2:F2}). Espera de {3} barras consecutivas afuera con rompimiento alcista.",
                    Time[0], tema9[0], upperBig, SobbRequiredFloatingBars));
            }
            else if (tema9[0] <= upperBig)
            {
                if (sobbArmedLong)
                {
                    sobbArmedLong = false;
                    sobbHasFloatedLong = false;
                    sobbFloatingBarsCountLong = 0;
                    Print(string.Format("{0} - [PAS - SOBB v10.0 | LARGO DESARMADO] TEMA 9 ({1:F2}) regresó dentro del canal sin haber disparado. Espera cancelada.",
                        Time[0], tema9[0]));
                }
            }

            if (sobbArmedLong)
            {
                bool isVelaFlotandoLargo = (Low[0] > upperBig);
                if (isVelaFlotandoLargo)
                {
                    sobbHasFloatedLong = true;
                    sobbFloatingBarsCountLong++;

                    bool isEmaZonaLargo    = (ema200[0] <= midline && ema200[0] >= lowerSmall);
                    bool isPendientesLargo = (normMidSlope >= SlopeThresholdTrend && normUpperSlope >= SlopeThresholdTrend);

                    // CAPA 1: EMA 200 en Zona 3 (Contexto Macro Estricto e Inmediato)
                    if (!isEmaZonaLargo)
                    {
                        sobbArmedLong = false;
                        sobbHasFloatedLong = false;
                        sobbFloatingBarsCountLong = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.0 | ¡PAILAS MI PEZ!] Largo descartado: Vela flotando #{1} afuera pero EMA 200 ({2:F2}) fuera de Zona 3 ([{3:F2}, {4:F2}]). ¡Apague y vámonos!",
                            Time[0], sobbFloatingBarsCountLong, ema200[0], lowerSmall, midline));
                    }
                    // REGLA DE ORO v9.0: Evaluación de Barras Consecutivas y Rompimiento de Techo (Higher High)
                    else if (sobbFloatingBarsCountLong >= SobbRequiredFloatingBars && sobbFloatingBarsCountLong <= SobbMaxFloatingBarsWindow)
                    {
                        // Si la barra actual supera el techo de la barra anterior (empuje alcista activo)
                        bool rompioTechoAnterior = (High[0] > High[1]);

                        if (rompioTechoAnterior && isPendientesLargo)
                        {
                            // ¡Confirmación estructural completa de 2 o 3 barras consecutivas con pendiente institucional!
                            sobbArmedLong = false;
                            sobbHasFloatedLong = false;
                            sobbFloatingBarsCountLong = 0;
                            ExecuteAceEntry(AceType.As2_SOBB, MarketPosition.Long, "SOBB_Long");
                            Print(string.Format("{0} - [PAS - SOBB v10.0 | ¡PUM, ADIÓS! QUÉ GROSERÍA DE TRADE] ¡Largo disparado @ ~{1:F2}! {2} barras consecutivas afuera con High ({3:F2}) > High[1] ({4:F2}) | Pendiente Normalizada: +{5:F2} (Mín: +{6:F2}) | ATR: {7:F2} pts.",
                                Time[0], Close[0], sobbFloatingBarsCountLong, High[0], High[1], normMidSlope, SlopeThresholdTrend, atr14 != null ? atr14[0] : 0.0));
                            return;
                        }
                        else if (!rompioTechoAnterior)
                        {
                            Print(string.Format("{0} - [PAS - SOBB v10.0 | LARGO FRENADO] Barra #{1} afuera pero techo High ({2:F2}) no superó High[1] ({3:F2}). Esperando impulso en siguiente barra...",
                                Time[0], sobbFloatingBarsCountLong, High[0], High[1]));
                        }
                        else
                        {
                            Print(string.Format("{0} - [PAS - SOBB v10.0 | ¡PAILAS MI PEZ!] Largo en espera de pendiente: Midline en +{1:F2} vs Umbral requerido: +{2:F2} | ATR: {3:F2} pts. Mercado plano/ruido, ¡no arriesgamos!",
                                Time[0], normMidSlope, SlopeThresholdTrend, atr14 != null ? atr14[0] : 0.0));
                        }
                    }
                    else if (sobbFloatingBarsCountLong > SobbMaxFloatingBarsWindow)
                    {
                        // Superó la ventana de 3 barras consecutivas sin confirmar rompimiento
                        sobbArmedLong = false;
                        sobbHasFloatedLong = false;
                        sobbFloatingBarsCountLong = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.0 | ¡PAILAS!] Pasaron {1} barras afuera sin rompimiento alcista con pendiente. Setup cancelado.",
                            Time[0], SobbMaxFloatingBarsWindow));
                    }
                    else
                    {
                        // Barra 1 afuera: guardando posición y esperando barra 2
                        Print(string.Format("{0} - [PAS - SOBB v10.0 | BARRA 1 AFUERA] Primera vela 100% afuera (Low {1:F2} > Upper {2:F2}). Esperando Barra 2 consecutiva...",
                            Time[0], Low[0], upperBig));
                    }
                }
                else
                {
                    // La vela ya no está 100% afuera (se metió adentro o tocó la banda) -> ¡PAILAS!
                    if (sobbHasFloatedLong || sobbFloatingBarsCountLong > 0)
                    {
                        sobbArmedLong = false;
                        sobbHasFloatedLong = false;
                        sobbFloatingBarsCountLong = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.0 | LARGO DESARMADO POR REPLIEGUE] El precio regresó adentro de Upper 3.5 ({1:F2}) tras {2} barra(s) afuera. ¡Pailas! Setup cancelado.",
                            Time[0], upperBig, sobbFloatingBarsCountLong));
                    }
                }
            }

            // =========================================================================
            // SOBB CORTO: Cruce de TEMA 9 hacia afuera + Confirmación de Barras Consecutivas (v9.0)
            // 1. Armado: TEMA 9 cruza por primera vez hacia afuera de Lower 3.5.
            // 2. Desarmado por repliegue: Si TEMA 9 regresa adentro (>= Lower 3.5), se desactiva.
            // 3. Conteo de Barras Flotando Afuera (High < Lower 3.5):
            //    - Barra 1 Afuera: Inicializa contador = 1.
            //    - Barra 2 Afuera: Si Low[0] < Low[1] (rompe piso anterior y continúa el empuje bajista)
            //      y se cumplen las 2 capas (EMA en Zona 3 + Pendientes Normalizadas alineadas <= -SlopeThresholdTrend) -> ¡DISPARO CORTO!
            //    - Si Low[0] >= Low[1] (vela frenada/pequeña): Espera a la Barra 3 afuera.
            //    - Barra 3 Afuera: Si Low[0] < Low[1] y capas OK -> ¡DISPARO CORTO!
            //    - Si se pasa de SobbMaxFloatingBarsWindow (3 barras) sin romper piso o si regresa adentro -> ¡PAILAS!
            // =========================================================================
            bool isTemaCruzoSalidaCorto = (tema9[1] >= keltnerBig.Lower[1] && tema9[0] < lowerBig);
            if (isTemaCruzoSalidaCorto)
            {
                sobbArmedShort = true;
                sobbHasFloatedShort = false;
                sobbFloatingBarsCountShort = 0;
                Print(string.Format("{0} - [PAS - SOBB v10.0 | CORTO ARMADO] TEMA 9 ({1:F2}) cruzó hacia afuera de Lower 3.5 ({2:F2}). Espera de {3} barras consecutivas afuera con rompimiento bajista.",
                    Time[0], tema9[0], lowerBig, SobbRequiredFloatingBars));
            }
            else if (tema9[0] >= lowerBig)
            {
                if (sobbArmedShort)
                {
                    sobbArmedShort = false;
                    sobbHasFloatedShort = false;
                    sobbFloatingBarsCountShort = 0;
                    Print(string.Format("{0} - [PAS - SOBB v10.0 | CORTO DESARMADO] TEMA 9 ({1:F2}) regresó dentro del canal sin haber disparado. Espera cancelada.",
                        Time[0], tema9[0]));
                }
            }

            if (sobbArmedShort)
            {
                bool isVelaFlotandoCorto = (High[0] < lowerBig);
                if (isVelaFlotandoCorto)
                {
                    sobbHasFloatedShort = true;
                    sobbFloatingBarsCountShort++;

                    bool isEmaZonaCorto    = (ema200[0] >= midline && ema200[0] <= upperSmall);
                    bool isPendientesCorto = (normMidSlope <= -SlopeThresholdTrend && normLowerSlope <= -SlopeThresholdTrend);

                    // CAPA 1: EMA 200 en Zona 3 (Contexto Macro Estricto e Inmediato)
                    if (!isEmaZonaCorto)
                    {
                        sobbArmedShort = false;
                        sobbHasFloatedShort = false;
                        sobbFloatingBarsCountShort = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.0 | ¡PAILAS MI PEZ!] Corto descartado: Vela flotando #{1} afuera pero EMA 200 ({2:F2}) fuera de Zona 3 ([{3:F2}, {4:F2}]). ¡Apague y vámonos!",
                            Time[0], sobbFloatingBarsCountShort, ema200[0], midline, upperSmall));
                    }
                    // REGLA DE ORO v9.0: Evaluación de Barras Consecutivas y Rompimiento de Suelo (Lower Low)
                    else if (sobbFloatingBarsCountShort >= SobbRequiredFloatingBars && sobbFloatingBarsCountShort <= SobbMaxFloatingBarsWindow)
                    {
                        // Si la barra actual perfora el suelo de la barra anterior (empuje bajista activo)
                        bool rompioSueloAnterior = (Low[0] < Low[1]);

                        if (rompioSueloAnterior && isPendientesCorto)
                        {
                            // ¡Confirmación estructural completa de 2 o 3 barras consecutivas con pendiente institucional!
                            sobbArmedShort = false;
                            sobbHasFloatedShort = false;
                            sobbFloatingBarsCountShort = 0;
                            ExecuteAceEntry(AceType.As2_SOBB, MarketPosition.Short, "SOBB_Short");
                            Print(string.Format("{0} - [PAS - SOBB v10.0 | ¡PUM, ADIÓS! QUÉ GROSERÍA DE TRADE] ¡Corto disparado @ ~{1:F2}! {2} barras consecutivas afuera con Low ({3:F2}) < Low[1] ({4:F2}) | Pendiente Normalizada: {5:F2} (Mín: -{6:F2}) | ATR: {7:F2} pts.",
                                Time[0], Close[0], sobbFloatingBarsCountShort, Low[0], Low[1], normMidSlope, SlopeThresholdTrend, atr14 != null ? atr14[0] : 0.0));
                            return;
                        }
                        else if (!rompioSueloAnterior)
                        {
                            Print(string.Format("{0} - [PAS - SOBB v10.0 | CORTO FRENADO] Barra #{1} afuera pero suelo Low ({2:F2}) no perforó Low[1] ({3:F2}). Esperando impulso en siguiente barra...",
                                Time[0], sobbFloatingBarsCountShort, Low[0], Low[1]));
                        }
                        else
                        {
                            Print(string.Format("{0} - [PAS - SOBB v10.0 | ¡PAILAS MI PEZ!] Corto en espera de pendiente: Midline en {1:F2} vs Umbral requerido: -{2:F2} | ATR: {3:F2} pts. Mercado plano/ruido, ¡no arriesgamos!",
                                Time[0], normMidSlope, SlopeThresholdTrend, atr14 != null ? atr14[0] : 0.0));
                        }
                    }
                    else if (sobbFloatingBarsCountShort > SobbMaxFloatingBarsWindow)
                    {
                        // Superó la ventana de 3 barras consecutivas sin confirmar rompimiento
                        sobbArmedShort = false;
                        sobbHasFloatedShort = false;
                        sobbFloatingBarsCountShort = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.0 | ¡PAILAS!] Pasaron {1} barras afuera sin rompimiento bajista con pendiente. Setup cancelado.",
                            Time[0], SobbMaxFloatingBarsWindow));
                    }
                    else
                    {
                        // Barra 1 afuera: guardando posición y esperando barra 2
                        Print(string.Format("{0} - [PAS - SOBB v10.0 | BARRA 1 AFUERA] Primera vela 100% afuera (High {1:F2} < Lower {2:F2}). Esperando Barra 2 consecutiva...",
                            Time[0], High[0], lowerBig));
                    }
                }
                else
                {
                    // La vela ya no está 100% afuera (se metió adentro o tocó la banda) -> ¡PAILAS!
                    if (sobbHasFloatedShort || sobbFloatingBarsCountShort > 0)
                    {
                        sobbArmedShort = false;
                        sobbHasFloatedShort = false;
                        sobbFloatingBarsCountShort = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.0 | CORTO DESARMADO POR REPLIEGUE] El precio regresó adentro de Lower 3.5 ({1:F2}) tras {2} barra(s) afuera. ¡Pailas! Setup cancelado.",
                            Time[0], lowerBig, sobbFloatingBarsCountShort));
                    }
                }
            }
        }

        // =========================================================================
        // AS 3: RATCHET (EL CHICLE PARABÓLICO ORIGINAL - TENDENCIA VERTICAL INSTITUCIONAL)
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

            // B) REGLA DE ORO INSTITUCIONAL v6.0: EMA 200 ESTRICTAMENTE FUERA DEL CANAL GRANDE 3.5 & PENDIENTE MIDLINE
            // Ratchet está diseñado EXCLUSIVAMENTE para tendencias verticales desbocadas.
            // Para Cortos: EMA 200 debe estar rezagada por ENCIMA de la Banda Superior 3.5 (keltnerBig.Upper) y Midline cayendo.
            // Para Largos: EMA 200 debe estar rezagada por DEBAJO de la Banda Inferior 3.5 (keltnerBig.Lower) y Midline subiendo.
            double midlineSlope = keltnerBig.Midline[0] - keltnerBig.Midline[2];
            bool emaOutsideBigBandShort = ema200[0] >= keltnerBig.Upper[0];
            bool emaOutsideBigBandLong  = ema200[0] <= keltnerBig.Lower[0];
            bool midlineFallingShort    = midlineSlope < -(0.5 * TickSize);
            bool midlineRisingLong      = midlineSlope > (0.5 * TickSize);

            // C) Detección de Toque o Penetración en Banda Keltner 3.5 en barras 1 a 3
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
                if (!emaShortValid || !emaOutsideBigBandShort || !midlineFallingShort)
                {
                    // Descartado si no hay tendencia macro vertical o si EMA 200 está dentro del canal
                }
                else
                {
                    if (ratchetSetup == RatchetSetup.Short)
                    {
                        Print(Time[0] + " - [PAS - Ratchet] Nuevo gancho bajista en Banda Superior. Reloj reiniciado a 0.");
                    }
                    else
                    {
                        Print(string.Format("{0} - [PAS - Ratchet] ALERTA CORTO VERTICAL: Gancho TEMA en Banda Superior con EMA200 ({1:F2} >= {2:F2}) y Midline cayendo. Reloj de {3} barras armado buscando cruce de Línea Media.",
                            Time[0], ema200[0], keltnerBig.Upper[0], RatchetCountdownBars));
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
                if (!emaLongValid || !emaOutsideBigBandLong || !midlineRisingLong)
                {
                    // Descartado si no hay tendencia macro vertical o si EMA 200 está dentro del canal
                }
                else
                {
                    if (ratchetSetup == RatchetSetup.Long)
                    {
                        Print(Time[0] + " - [PAS - Ratchet] Nuevo gancho alcista en Banda Inferior. Reloj reiniciado a 0.");
                    }
                    else
                    {
                        Print(string.Format("{0} - [PAS - Ratchet] ALERTA LARGO VERTICAL: Gancho TEMA en Banda Inferior con EMA200 ({1:F2} <= {2:F2}) y Midline subiendo. Reloj de {3} barras armado buscando cruce de Línea Media.",
                            Time[0], ema200[0], keltnerBig.Lower[0], RatchetCountdownBars));
                    }
                    ratchetSetup = RatchetSetup.Long;
                    ratchetSetupBarCounter = 0;
                }
            }

            // D) Conteo del Reloj y GATILLO CIRUJANO: Cruce de la Línea Media (Midline)
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
                    // DISPARO LARGO: Cruce alcista de Línea Media (Revalidando EMA 200 fuera y pendiente a favor)
                    if (ratchetSetup == RatchetSetup.Long && CrossAbove(tema9, keltnerBig.Midline, 1))
                    {
                        if (ema200[0] <= keltnerBig.Lower[0] && (keltnerBig.Midline[0] - keltnerBig.Midline[2]) > 0)
                        {
                            ExecuteAceEntry(AceType.As3_Ratchet, MarketPosition.Long, "Ratchet_Long");
                            ratchetSetup = RatchetSetup.None;
                            Print(string.Format("{0} - [PAS - Ratchet] ¡DISPARO LARGO EN LÍNEA MEDIA! TEMA 9 cruzó Midline ({1:F2}) con EMA200 ({2:F2} <= Lower 3.5 {3:F2}). Entrada @ ~{4:F2} | SL Tope 12tk armado.",
                                Time[0], keltnerBig.Midline[0], ema200[0], keltnerBig.Lower[0], Close[0]));
                            return;
                        }
                        else
                        {
                            Print(string.Format("{0} - [PAS - Ratchet | DISPARO LARGO ANULADO] Cruce en Midline detectado pero EMA 200 ({1:F2}) ingresó al canal o Midline frenó subida. Setup cancelado para evitar trampas.",
                                Time[0], ema200[0]));
                            ratchetSetup = RatchetSetup.None;
                        }
                    }
                    // DISPARO CORTO: Cruce bajista de Línea Media (Revalidando EMA 200 fuera y pendiente a favor)
                    else if (ratchetSetup == RatchetSetup.Short && CrossBelow(tema9, keltnerBig.Midline, 1))
                    {
                        if (ema200[0] >= keltnerBig.Upper[0] && (keltnerBig.Midline[0] - keltnerBig.Midline[2]) < 0)
                        {
                            ExecuteAceEntry(AceType.As3_Ratchet, MarketPosition.Short, "Ratchet_Short");
                            ratchetSetup = RatchetSetup.None;
                            Print(string.Format("{0} - [PAS - Ratchet] ¡DISPARO CORTO EN LÍNEA MEDIA! TEMA 9 cruzó Midline ({1:F2}) con EMA200 ({2:F2} >= Upper 3.5 {3:F2}). Entrada @ ~{4:F2} | SL Tope 12tk armado.",
                                Time[0], keltnerBig.Midline[0], ema200[0], keltnerBig.Upper[0], Close[0]));
                            return;
                        }
                        else
                        {
                            Print(string.Format("{0} - [PAS - Ratchet | DISPARO CORTO ANULADO] Cruce en Midline detectado pero EMA 200 ({1:F2}) ingresó al canal o Midline frenó bajada. Setup cancelado para evitar trampas.",
                                Time[0], ema200[0]));
                            ratchetSetup = RatchetSetup.None;
                        }
                    }
                }
            }
        }

        // =========================================================================
        // AS 4: CASCADA (EL CAZADOR DE RÁPIDOS DE RÍO - PENDIENTE NORMALIZADA + CANDADO $180)
        // =========================================================================
        private void EvaluateAs4_Cascada50()
        {
            // 1. FILTRO HORARIO EXCLUSIVO DE CASCADA: No opera antes de las 10:00 AM EST (Evita trampas de acomodación de pre-mercado)
            int timeNow = ToTime(Time[0]);
            if (timeNow < CascadaStartTime)
            {
                return;
            }

            // 2. FILTRO DE SACIEDAD: Si Cascada logró una victoria previa (>= $180 USD), permanece en cuarentena de 60 min
            if (Time[0] < cascadaQuarantineEndTime)
            {
                return;
            }

            double midline    = keltnerBig.Midline[0];
            double upperSmall = keltnerSmall.Upper[0];
            double lowerSmall = keltnerSmall.Lower[0];
            double normSlope  = GetNormalizedSlope(keltnerBig.Midline);

            // =========================================================================
            // DISPARO CORTO: Rápido de Río Bajista
            // 1. Regla de 1 Solo Tiro: no haber disparado ya en este mismo impulso bajista
            // 2. Contexto Macro: EMA 200 por encima de la Línea Media (keltnerBig.Midline)
            // 3. Ubicación: Precio en Zona 4 o 5 (Close <= lowerSmall)
            // 4. Aceleración: Pendiente Normalizada de Línea Media <= -CascadaSlopeThreshold (-0.20)
            // 5. Vela con intención bajista: Close[0] < Open[0]
            // =========================================================================
            if (!cascadaCycleShortFired)
            {
                bool macroBajista          = ema200[0] > midline;
                bool precioEnZonaExpansion = Close[0] <= lowerSmall;
                bool pendienteRapido       = normSlope <= -CascadaSlopeThreshold;
                bool velaBajista           = Close[0] < Open[0];

                if (macroBajista && precioEnZonaExpansion)
                {
                    if (pendienteRapido && velaBajista)
                    {
                        cascadaCycleShortFired = true;
                        ExecuteAceEntry(AceType.As4_Cascada50, MarketPosition.Short, "Cascada50_Short");
                        Print(string.Format("{0} - [PAS - Cascada v10.0 | ¡PUM, ADIÓS! RÁPIDO CORTO DISPARADO] Entrada @ ~{1:F2} en Zona 4/5 | Pendiente Normalizada: {2:F2} (Mínimo: -{3:F2}) | ATR(14): {4:F2} pts | SL inicial en Línea Media ({5:F2}). ¡Qué grosería de trade!",
                            Time[0], Close[0], normSlope, CascadaSlopeThreshold, atr14 != null ? atr14[0] : 0.0, midline));
                        return;
                    }
                    else if (!pendienteRapido)
                    {
                        if (normSlope < -0.05)
                        {
                            Print(string.Format("{0} - [PAS - Cascada v10.0 | ¡PAILAS MI PEZ!] Corto frenado: Pendiente en {1:F2} vs Umbral requerido: -{2:F2} | ATR: {3:F2} pts. Esperando aceleración real del río...",
                                Time[0], normSlope, CascadaSlopeThreshold, atr14 != null ? atr14[0] : 0.0));
                        }
                    }
                }
            }

            // =========================================================================
            // DISPARO LARGO: Rápido de Río Alcista
            // 1. Regla de 1 Solo Tiro: no haber disparado ya en este mismo impulso alcista
            // 2. Contexto Macro: EMA 200 por debajo de la Línea Media (keltnerBig.Midline)
            // 3. Ubicación: Precio en Zona 0 o 1 (Close >= upperSmall)
            // 4. Aceleración: Pendiente Normalizada de Línea Media >= CascadaSlopeThreshold (+0.20)
            // 5. Vela con intención alcista: Close[0] > Open[0]
            // =========================================================================
            if (!cascadaCycleLongFired)
            {
                bool macroAlcista          = ema200[0] < midline;
                bool precioEnZonaExpansion = Close[0] >= upperSmall;
                bool pendienteRapido       = normSlope >= CascadaSlopeThreshold;
                bool velaAlcista           = Close[0] > Open[0];

                if (macroAlcista && precioEnZonaExpansion)
                {
                    if (pendienteRapido && velaAlcista)
                    {
                        cascadaCycleLongFired = true;
                        ExecuteAceEntry(AceType.As4_Cascada50, MarketPosition.Long, "Cascada50_Long");
                        Print(string.Format("{0} - [PAS - Cascada v10.0 | ¡PUM, ADIÓS! RÁPIDO LARGO DISPARADO] Entrada @ ~{1:F2} en Zona 0/1 | Pendiente Normalizada: +{2:F2} (Mínimo: +{3:F2}) | ATR(14): {4:F2} pts | SL inicial en Línea Media ({5:F2}). ¡Qué grosería de trade!",
                            Time[0], Close[0], normSlope, CascadaSlopeThreshold, atr14 != null ? atr14[0] : 0.0, midline));
                        return;
                    }
                    else if (!pendienteRapido)
                    {
                        if (normSlope > 0.05)
                        {
                            Print(string.Format("{0} - [PAS - Cascada v10.0 | ¡PAILAS MI PEZ!] Largo frenado: Pendiente en +{1:F2} vs Umbral requerido: +{2:F2} | ATR: {3:F2} pts. Esperando aceleración real del río...",
                                Time[0], normSlope, CascadaSlopeThreshold, atr14 != null ? atr14[0] : 0.0));
                        }
                    }
                }
            }
        }

        #endregion

        #region Ejecución y Gestión de Salidas Escalonadas

        private int GetRiskAdjustedContracts()
        {
            int targetContracts = Contracts; // Configurado por defecto (3 contratos)

            // REGLA DE PROTECCIÓN TRAS PÉRDIDA: Si la última operación cerrada fue perdedora (Strike activo),
            // se desescala obligatoriamente a 1 contrato para evitar que una mala racha dañe la cuenta.
            if (consecutiveLosses > 0)
            {
                Print(string.Format("{0} - [PAS | DESESCALADO TRAS PÉRDIDA] Strike activo ({1} pérdida(s) consecutiva(s)). Entrada protegida con 1 solo contrato.",
                    Time[0], consecutiveLosses));
                targetContracts = 1;
            }

            if (UseDailyManagement && !isDailyLocked && currentDailyFloor > 0)
            {
                double allowedLossDollars = dailyRealizedPnL - currentDailyFloor;
                double tickDollarVal = TickSize * Instrument.MasterInstrument.PointValue;

                // Si el PnL ya cayó en o por debajo del piso protegido:
                if (allowedLossDollars <= 0)
                {
                    Print(string.Format("{0} - [PAS | PISO ALCANZADO] PnL actual (${1:F2}) en o por debajo del Piso (+${2:F2} USD). No se abren nuevas posiciones.",
                        Time[0], dailyRealizedPnL, currentDailyFloor));
                    return 0;
                }

                // REGLA DE ORO DE ESCALÓN 1: Si ya estamos en ganancias protegidas (Escalón 1: $400 - $599),
                // se opera con 1 solo contrato. Mientras estemos sobre el piso, se permite operar.
                if (currentDailyTier == 1)
                {
                    Print(string.Format("{0} - [PAS | ESCALÓN 1 ACTIVO: 1 CONTRATO] PnL actual: ${1:F2} USD (Piso +${2:F2}). Operando con 1 contrato protegido.",
                        Time[0], dailyRealizedPnL, currentDailyFloor));
                    return 1;
                }

                // Para Escalón 2 o superior (Modo Dulce): Permitimos 2 o 3 contratos solo si hay margen holgado (mínimo 14 ticks por contrato)
                int minHealthyTicksPerContract = 14;

                for (int qty = targetContracts; qty >= 1; qty--)
                {
                    double dollarsPerTick = qty * tickDollarVal;
                    int maxTicksForQty = (int)Math.Floor(allowedLossDollars / dollarsPerTick);

                    if (qty > 1 && maxTicksForQty >= minHealthyTicksPerContract)
                    {
                        if (qty < targetContracts)
                        {
                            Print(string.Format("{0} - [PAS | DESESCALADO DINÁMICO] Margen: ${1:F2} USD. Posición ajustada a {2} contratos ({3} ticks de respiro).",
                                Time[0], allowedLossDollars, qty, maxTicksForQty));
                        }
                        return qty;
                    }
                }

                // Si el margen no alcanza para multicontrato holgado pero seguimos sobre el piso, operamos con 1 contrato:
                Print(string.Format("{0} - [PAS | DESESCALADO A 1 CONTRATO] Margen disponible: ${1:F2} USD. Posición ajustada a 1 contrato para seguir operando sin romper piso (+${2:F2}).",
                    Time[0], allowedLossDollars, currentDailyFloor));
                return 1;
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

            // SOBB y CASCADA siempre operan con 1 solo contrato (Monocontrato Quirúrgico y Surfer con Oxígeno Estructural)
            if (ace == AceType.As2_SOBB || ace == AceType.As4_Cascada50)
            {
                entryContracts = 1;
            }

            if (ace == AceType.As4_Cascada50)
            {
                cascadaTradesToday++;
                Print(string.Format("{0} - [PAS - Cascada | SURFER MONOCONTRATO] Disparo #{1} de Cascada asignado a 1 contrato para surfear rally con respiro.",
                    Time[0], cascadaTradesToday));
            }

            activeTradeContracts = entryContracts;

            sobbLevel1Achieved = false;
            sobbLevel2Achieved = false;
            sobbLevel3ChokeActive = false;

            z1C1Achieved = false;
            z1C2Achieved = false;
            z1ChokeActive = false;

            ratchetC1Achieved = false;
            ratchetC2Achieved = false;
            ratchetChokeActive = false;

            cascadaC1Achieved = false;
            cascadaC2Achieved = false;
            cascadaChokeActive = false;
            isCascadaLockActive = false;

            // Stop Loss inicial físico en el DOM
            double initialSlPrice = 0.0;
            if (pos == MarketPosition.Long)
            {
                if (ace == AceType.As1_Zona1_100Bar)
                {
                    // Z1-100Bar: SL en Línea Media, pero con TOPE MÁXIMO de seguridad de 14 ticks (R:R saludable 1:1 vs TP 16 ticks)
                    double naturalSl = keltnerBig.Midline[0] - (SlOffsetTicks * TickSize);
                    double maxSafeSl = Close[0] - (14 * TickSize);
                    initialSlPrice = Math.Max(naturalSl, maxSafeSl);

                    // REGLA DE ORO INSTITUCIONAL: Targets pasivos LÍMITE en el libro para eliminar slippage
                    double tp1Price = Instrument.MasterInstrument.RoundToTickSize(Close[0] + (Zone1Tp1Ticks * TickSize));
                    double tp2Price = Instrument.MasterInstrument.RoundToTickSize(Close[0] + (Zone1Tp2Ticks * TickSize));
                    if (activeTradeContracts >= 3)
                    {
                        ExitLongLimit(1, tp1Price, "Z1_C1_Tp8", "Z1-100Bar_Long");
                        ExitLongLimit(1, tp2Price, "Z1_C2_Tp16", "Z1-100Bar_Long");
                    }
                    else if (activeTradeContracts == 2)
                    {
                        ExitLongLimit(1, tp1Price, "Z1_C1_Tp8", "Z1-100Bar_Long");
                    }
                    Print(string.Format("{0} - [PAS - Z1-100B | TARGETS LÍMITE COLOCADOS] C1 (+{1}tk): {2:F2} | C2 (+{3}tk): {4:F2} | Posición activa: {5} contrato(s)",
                        Time[0], Zone1Tp1Ticks, tp1Price, Zone1Tp2Ticks, tp2Price, activeTradeContracts));
                }
                else if (ace == AceType.As2_SOBB)
                {
                    // SOBB MONOCONTRATO: SL Estructural con Oxígeno en Banda Pequeña Lower 1.75 con tope de seguridad (24 ticks = -$300 máx)
                    double naturalSl = keltnerSmall.Lower[0] - (2 * TickSize);
                    double maxSafeSl = Close[0] - (SobbMaxSlTicks * TickSize);
                    initialSlPrice = Math.Max(naturalSl, maxSafeSl);
                    Print(string.Format("{0} - [PAS - SOBB | SL ESTRUCTURAL CON OXÍGENO] Entrada Long @ {1:F2} | SL en Lower 1.75: {2:F2} ({3:F1} ticks de respiro). Cero asfixia.",
                        Time[0], Close[0], initialSlPrice, (Close[0] - initialSlPrice) / TickSize));
                }
                else if (ace == AceType.As4_Cascada50)
                {
                    // CASCADA SURFER MONOCONTRATO: SL Estructural con Oxígeno en Banda Pequeña Lower 1.75 con tope de seguridad (24 ticks = -$300 máx)
                    double naturalSl = keltnerSmall.Lower[0] - (1 * TickSize);
                    double maxSafeSl = Close[0] - (CascadaMaxSlTicks * TickSize);
                    initialSlPrice = Math.Max(naturalSl, maxSafeSl);
                    isCascadaLockActive = false;
                    Print(string.Format("{0} - [PAS - Cascada | SURFER MONOCONTRATO] Entrada Long @ {1:F2} | SL en Lower 1.75 -1tk: {2:F2} ({3:F1} ticks de respiro | Tope {4}tk = -${5:F2}). Candado ${6:F0} armado.",
                        Time[0], Close[0], initialSlPrice, (Close[0] - initialSlPrice) / TickSize, CascadaMaxSlTicks, (CascadaMaxSlTicks * TickSize * Instrument.MasterInstrument.PointValue), CascadaLockTriggerDollars));
                }
                else if (ace == AceType.As3_Ratchet)
                {
                    // Ratchet Long: Banda opuesta, pero con TOPE MÁXIMO de seguridad (12 ticks = $450 ES máx en 3 ctos)
                    double naturalSl = keltnerBig.Lower[0] - (SlOffsetTicks * TickSize);
                    double maxSafeSl = Close[0] - (RatchetMaxSlTicks * TickSize);
                    initialSlPrice = Math.Max(naturalSl, maxSafeSl);
                    Print(string.Format("{0} - [PAS - Ratchet | SL INICIAL CON TOPE] Entrada Long @ {1:F2} | SL en {2:F2} (Tope máx {3} ticks = -${4:F2} USD en {5} ctos).",
                        Time[0], Close[0], initialSlPrice, RatchetMaxSlTicks, (RatchetMaxSlTicks * TickSize * Instrument.MasterInstrument.PointValue * activeTradeContracts), activeTradeContracts));
                }
                else
                    initialSlPrice = keltnerSmall.Lower[0] - (1 * TickSize);

                initialSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Min(initialSlPrice, Close[0] - (MinSafetySlTicks * TickSize)));

                // ESCUDO DIARIO BLINDADO EN DOM: Si hay piso activo de ganancias previas (currentDailyFloor > 0), el piso manda absolutamente sobre el SL
                if (UseDailyManagement && !isDailyLocked && currentDailyFloor > 0 && dailyRealizedPnL > currentDailyFloor)
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

                EnterLong(activeTradeContracts, signalName);
            }
            else // Short
            {
                if (ace == AceType.As1_Zona1_100Bar)
                {
                    // Z1-100Bar: SL en Línea Media, pero con TOPE MÁXIMO de seguridad de 14 ticks (R:R saludable 1:1 vs TP 16 ticks)
                    double naturalSl = keltnerBig.Midline[0] + (SlOffsetTicks * TickSize);
                    double maxSafeSl = Close[0] + (14 * TickSize);
                    initialSlPrice = Math.Min(naturalSl, maxSafeSl);

                    // REGLA DE ORO INSTITUCIONAL: Targets pasivos LÍMITE en el libro para eliminar slippage
                    double tp1Price = Instrument.MasterInstrument.RoundToTickSize(Close[0] - (Zone1Tp1Ticks * TickSize));
                    double tp2Price = Instrument.MasterInstrument.RoundToTickSize(Close[0] - (Zone1Tp2Ticks * TickSize));
                    if (activeTradeContracts >= 3)
                    {
                        ExitShortLimit(1, tp1Price, "Z1_C1_Tp8", "Z1-100Bar_Short");
                        ExitShortLimit(1, tp2Price, "Z1_C2_Tp16", "Z1-100Bar_Short");
                    }
                    else if (activeTradeContracts == 2)
                    {
                        ExitShortLimit(1, tp1Price, "Z1_C1_Tp8", "Z1-100Bar_Short");
                    }
                    Print(string.Format("{0} - [PAS - Z1-100B | TARGETS LÍMITE COLOCADOS] C1 (-{1}tk): {2:F2} | C2 (-{3}tk): {4:F2} | Posición activa: {5} contrato(s)",
                        Time[0], Zone1Tp1Ticks, tp1Price, Zone1Tp2Ticks, tp2Price, activeTradeContracts));
                }
                else if (ace == AceType.As2_SOBB)
                {
                    // SOBB MONOCONTRATO: SL Estructural con Oxígeno en Banda Pequeña Upper 1.75 con tope de seguridad (24 ticks = -$300 máx)
                    double naturalSl = keltnerSmall.Upper[0] + (2 * TickSize);
                    double maxSafeSl = Close[0] + (SobbMaxSlTicks * TickSize);
                    initialSlPrice = Math.Min(naturalSl, maxSafeSl);
                    Print(string.Format("{0} - [PAS - SOBB | SL ESTRUCTURAL CON OXÍGENO] Entrada Short @ {1:F2} | SL en Upper 1.75: {2:F2} ({3:F1} ticks de respiro). Cero asfixia.",
                        Time[0], Close[0], initialSlPrice, (initialSlPrice - Close[0]) / TickSize));
                }
                else if (ace == AceType.As4_Cascada50)
                {
                    // CASCADA SURFER MONOCONTRATO: SL Estructural con Oxígeno en Banda Pequeña Upper 1.75 con tope de seguridad (24 ticks = -$300 máx)
                    double naturalSl = keltnerSmall.Upper[0] + (1 * TickSize);
                    double maxSafeSl = Close[0] + (CascadaMaxSlTicks * TickSize);
                    initialSlPrice = Math.Min(naturalSl, maxSafeSl);
                    isCascadaLockActive = false;
                    Print(string.Format("{0} - [PAS - Cascada | SURFER MONOCONTRATO] Entrada Short @ {1:F2} | SL en Upper 1.75 +1tk: {2:F2} ({3:F1} ticks de respiro | Tope {4}tk = -${5:F2}). Candado ${6:F0} armado.",
                        Time[0], Close[0], initialSlPrice, (initialSlPrice - Close[0]) / TickSize, CascadaMaxSlTicks, (CascadaMaxSlTicks * TickSize * Instrument.MasterInstrument.PointValue), CascadaLockTriggerDollars));
                }
                else if (ace == AceType.As3_Ratchet)
                {
                    // Ratchet Short: Banda opuesta, pero con TOPE MÁXIMO de seguridad (12 ticks = $450 ES máx en 3 ctos)
                    double naturalSl = keltnerBig.Upper[0] + (SlOffsetTicks * TickSize);
                    double maxSafeSl = Close[0] + (RatchetMaxSlTicks * TickSize);
                    initialSlPrice = Math.Min(naturalSl, maxSafeSl);
                    Print(string.Format("{0} - [PAS - Ratchet | SL INICIAL CON TOPE] Entrada Short @ {1:F2} | SL en {2:F2} (Tope máx {3} ticks = -${4:F2} USD en {5} ctos).",
                        Time[0], Close[0], initialSlPrice, RatchetMaxSlTicks, (RatchetMaxSlTicks * TickSize * Instrument.MasterInstrument.PointValue * activeTradeContracts), activeTradeContracts));
                }
                else
                    initialSlPrice = keltnerSmall.Upper[0] + (1 * TickSize);

                initialSlPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Max(initialSlPrice, Close[0] + (MinSafetySlTicks * TickSize)));

                // ESCUDO DIARIO BLINDADO EN DOM: Si hay piso activo de ganancias previas (currentDailyFloor > 0), el piso manda absolutamente sobre el SL
                if (UseDailyManagement && !isDailyLocked && currentDailyFloor > 0 && dailyRealizedPnL > currentDailyFloor)
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

                // --- AS 2: SOBB (SURFER OUT BIG BANDA - MONOCONTRATO QUIRÚRGICO CON OXÍGENO) ---
                if (currentAce == AceType.As2_SOBB)
                {
                    // 1. TOMA DE GANANCIAS / ACTIVACIÓN DE BREAK-EVEN (+10 TICKS)
                    if (!sobbLevel1Achieved && (currentGainTicks >= SobbTp1Ticks || peakGainTicks >= SobbTp1Ticks))
                    {
                        sobbLevel1Achieved = true;
                        Print(string.Format("{0} - [PAS - SOBB | ESCALÓN 1 SUPERADO] Ganancia: +{1:F1} tks. Stop protegido en BREAKEVEN +1tk ({2:F2}). ¡Riesgo CERO!",
                            Time[0], Math.Max(currentGainTicks, peakGainTicks), entryPrice + (1 * TickSize)));
                    }

                    // 2. GESTIÓN TRAS ALCANZAR BREAK-EVEN (NIVEL 1 SUPERADO):
                    if (sobbLevel1Achieved)
                    {
                        double bePrice = entryPrice + (1 * TickSize);

                        // A) Target de Expansión (+20 ticks = $250 ES / $25 MES)
                        if (currentGainTicks >= SobbTp2Ticks || peakGainTicks >= SobbTp2Ticks)
                        {
                            ExitLong("SOBB_Tp2_Expansion", "SOBB_Long");
                            Print(string.Format("{0} - [PAS - SOBB | TARGET EXPANSIÓN ALCANZADO] +{1} ticks (+${2:F2} USD). Victoria asegurada.",
                                Time[0], SobbTp2Ticks, (SobbTp2Ticks * TickSize * Instrument.MasterInstrument.PointValue)));
                            return;
                        }

                        // B) Salida por Ciclo Terminado (Repliegue real adentro de Banda Upper 3.5 tras ganar >= 14 ticks - SIN SUSTO DE TEMA 9)
                        if (peakGainTicks >= 14 && Close[0] < keltnerBig.Upper[0])
                        {
                            ExitLong("Exit_SOBB_CicloTerminado", "SOBB_Long");
                            Print(string.Format("{0} - [PAS - SOBB v9.1 | CICLO TERMINADO REAL] Repliegue adentro de Upper 3.5 ({1:F2}). Salida a Mercado @ ~{2:F2} (+{3:F1} tks pico).",
                                Time[0], keltnerBig.Upper[0], Close[0], peakGainTicks));
                            return;
                        }

                        // C) Stop de Protección en Break-Even + 1 tick
                        if (Close[0] <= bePrice || Low[0] <= bePrice)
                        {
                            ExitLong("SOBB_Stop_BE", "SOBB_Long");
                            Print(string.Format("{0} - [PAS - SOBB | SALIDA EN BREAKEVEN] Posición protegida cerrada en {1:F2} tras amague.",
                                Time[0], bePrice));
                            return;
                        }
                    }
                    // 3. STOP LOSS INICIAL ESTRUCTURAL CON OXÍGENO (ANTES DE TOCAR BREAK-EVEN):
                    else
                    {
                        // SL Estructural en Banda Pequeña Lower 1.75 con tope máximo de seguridad (SobbMaxSlTicks = 24 ticks = -$300 USD máx)
                        double structuralSl = keltnerSmall.Lower[0] - (2 * TickSize);
                        double maxSafeSl = entryPrice - (SobbMaxSlTicks * TickSize);
                        double slPrice = Math.Max(structuralSl, maxSafeSl);

                        if (Close[0] <= slPrice || Low[0] <= slPrice)
                        {
                            ExitLong("SOBB_SL_Estructural", "SOBB_Long");
                            Print(string.Format("{0} - [PAS - SOBB | SL ESTRUCTURAL EJECUTADO] Precio perforó SL ({1:F2}) | Salida a Mercado @ ~{2:F2} (Riesgo acotado a 1 contrato).",
                                Time[0], slPrice, Close[0]));
                            return;
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
                // --- GESTIÓN AS 1: ZONA 1 (ESCALONAMIENTO 3 FASES CON RUNNER AHOGO + MIDLINE SL) ---
                else if (currentAce == AceType.As1_Zona1_100Bar)
                {
                    double maxProfitTicks = (highestPriceSinceEntry - entryPrice) / TickSize;

                    // Si estamos operando con 1 solo contrato (por desescalado tras pérdida o regla de piso):
                    if (activeTradeContracts == 1)
                    {
                        // Target pasivo de +8 ticks
                        if (maxProfitTicks >= Zone1Tp1Ticks || currentGainTicks >= Zone1Tp1Ticks)
                        {
                            ExitLong("Z1_Mono_Tp", "Z1-100Bar_Long");
                            Print(string.Format("{0} - [PAS - Z1-100B | MONOCONTRATO TP COBRADO] +{1} ticks (+${2:F2}). ¡Ganancia asegurada!",
                                Time[0], Zone1Tp1Ticks, Zone1Tp1Ticks * TickSize * Instrument.MasterInstrument.PointValue));
                            return;
                        }

                        // Stop Loss en Línea Media con TOPE MÁXIMO de seguridad de 14 ticks:
                        double naturalSl = keltnerBig.Midline[0] - (SlOffsetTicks * TickSize);
                        double slPrice = Math.Max(naturalSl, entryPrice - (14 * TickSize));

                        if (Close[0] <= slPrice || Low[0] <= slPrice)
                        {
                            ExitLong("Z1_Mono_SL", "Z1-100Bar_Long");
                            Print(string.Format("{0} - [PAS - Z1-100B | MONOCONTRATO SL EJECUTADO] Precio perforó SL ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                Time[0], slPrice, Close[0]));
                            return;
                        }
                    }
                    else
                    {
                        // MULTICONTRATO (2 o 3 contratos)
                        // CONTRATO 1: Target Límite pasivo a +8 ticks (Zone1Tp1Ticks) ejecutado o detectado
                        if (!z1C1Achieved && (currentQty < activeTradeContracts || maxProfitTicks >= Zone1Tp1Ticks || currentGainTicks >= Zone1Tp1Ticks))
                        {
                            z1C1Achieved = true;
                            if (currentQty >= 3)
                            {
                                ExitLong(1, "Z1_C1_Tp8", "Z1-100Bar_Long");
                            }
                            Print(string.Format("{0} - [PAS - Z1-100B | FASE 1 SUPERADA] C1 ejecutado a +{1} ticks (+${2:F2}). Stop blindado en BREAKEVEN +1tk ({3:F2}). ¡Riesgo CERO!",
                                Time[0], Zone1Tp1Ticks, Zone1Tp1Ticks * TickSize * Instrument.MasterInstrument.PointValue, entryPrice + (1 * TickSize)));
                        }

                        // CONTRATO 2: Target Límite pasivo a +16 ticks (Zone1Tp2Ticks) ejecutado o detectado
                        if (z1C1Achieved && !z1C2Achieved && (currentQty <= 1 || maxProfitTicks >= Zone1Tp2Ticks || currentGainTicks >= Zone1Tp2Ticks))
                        {
                            z1C2Achieved = true;
                            z1ChokeActive = true;
                            if (currentQty >= 2)
                            {
                                ExitLong(1, "Z1_C2_Tp16", "Z1-100Bar_Long");
                            }
                            Print(string.Format("{0} - [PAS - Z1-100B | FASE 2 SUPERADA] C2 cobrado a +{1} ticks (+${2:F2}). Contrato 3 Runner en MODO AHOGO a {3} ticks del High.",
                                Time[0], Zone1Tp2Ticks, Zone1Tp2Ticks * TickSize * Instrument.MasterInstrument.PointValue, Zone1ChokeTicks));
                        }

                        // CONTRATO 3 (RUNNER): Modo Ahogo a 8 ticks del pico (Zone1ChokeTicks)
                        if (z1ChokeActive || (z1C1Achieved && z1C2Achieved))
                        {
                            double chokeSl = highestPriceSinceEntry - (Zone1ChokeTicks * TickSize);
                            if (Close[0] <= chokeSl || Low[0] <= chokeSl)
                            {
                                ExitLong("Z1_C3_RunnerAhogo", "Z1-100Bar_Long");
                                Print(string.Format("{0} - [PAS - Z1-100B | RUNNER AHOGADO EN LA CIMA] Salida a Mercado @ ~{1:F2} (Pico: {2:F2}). ¡Ganancia máxima asegurada!",
                                    Time[0], Close[0], highestPriceSinceEntry));
                            }
                        }
                        // Stop de Resguardo tras cobrar C1: BreakEven + 1 tick
                        else if (z1C1Achieved)
                        {
                            double bePrice = entryPrice + (1 * TickSize);
                            if (Close[0] <= bePrice || Low[0] <= bePrice)
                            {
                                ExitLong("Z1_Stop_BE", "Z1-100Bar_Long");
                                Print(string.Format("{0} - [PAS - Z1-100B | SALIDA EN BREAKEVEN] Posición restante protegida en {1:F2}. Salida sin pérdida.",
                                    Time[0], bePrice));
                            }
                        }
                        // Stop Inicial antes de C1 (Línea Media con TOPE DE 14 TICKS):
                        else
                        {
                            double naturalSl = keltnerBig.Midline[0] - (SlOffsetTicks * TickSize);
                            double slPrice = Math.Max(naturalSl, entryPrice - (14 * TickSize));

                            if (Close[0] <= slPrice || Low[0] <= slPrice)
                            {
                                ExitLong("SL_Midline_Zona1", "Z1-100Bar_Long");
                                Print(string.Format("{0} - [PAS - Z1-100B | SL MIDLINE] Precio tocó Midline ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                    Time[0], slPrice, Close[0]));
                            }
                        }
                    }
                }
                // --- GESTIÓN AS 4: CASCADA SURFER MONOCONTRATO (CANDADO $180 Y FIN DE CICLO) ---
                else if (currentAce == AceType.As4_Cascada50)
                {
                    double currentFavorableTicks = (High[0] - entryPrice) / TickSize;
                    double currentProfitDollars  = currentFavorableTicks * (Instrument.MasterInstrument.PointValue * TickSize);
                    double effectiveLockTrigger  = CascadaLockTriggerDollars * instrumentMultiplier;

                    // CANDADO DE $180: Al superar $180 a favor ($18 en MES), el candado se arma
                    if (!isCascadaLockActive && currentProfitDollars >= effectiveLockTrigger)
                    {
                        isCascadaLockActive = true;
                        Print(string.Format("{0} - [PAS - Cascada | CANDADO ACTIVADO - LARGO] Ganancia flotante alcanzó {1:C2} (+{2:F1} tks). Candado armado en Upper 1.75 -1tk.",
                            Time[0], currentProfitDollars, currentFavorableTicks));
                    }

                    // 1. Salida por Candado Activo (Upper 1.75 - 1 tick tras superar $180 USD)
                    if (isCascadaLockActive)
                    {
                        double lockSl = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Upper[0] - (1 * TickSize));
                        if (Close[0] <= lockSl || Low[0] <= lockSl)
                        {
                            ExitLong("SL_Candado_Cascada", "Cascada50_Long");
                            Print(string.Format("{0} - [PAS - Cascada v10.1 | LARGO COBRADO CON CANDADO] Precio perforó Upper 1.75 -1tk ({1:F2}) | Salida a Mercado @ ~{2:F2} (Ganancia asegurada).",
                                Time[0], lockSl, Close[0]));
                            return;
                        }
                    }

                    // 2. Salida por Fin de Ciclo del Rápido (Repliegue adentro de Banda 1.75 o freno de pendiente tras ganar >= 14 ticks)
                    double normMidSlope = GetNormalizedSlope(keltnerBig.Midline);
                    if (peakGainTicks >= 14 && (Close[0] < keltnerSmall.Upper[0] || normMidSlope <= 0.0))
                    {
                        ExitLong("Exit_Cascada_CicloTerminado", "Cascada50_Long");
                        Print(string.Format("{0} - [PAS - Cascada v10.1 | FIN DE RÁPIDO] Repliegue adentro de Upper 1.75 ({1:F2}) o freno de pendiente ({2:F2}) tras ganar {3:F1} tks pico. Salida a Mercado @ ~{4:F2}",
                            Time[0], keltnerSmall.Upper[0], normMidSlope, peakGainTicks, Close[0]));
                        return;
                    }

                    // 3. Stop Loss Inicial Estructural (antes de alcanzar el Candado) en Línea Media con tope de seguridad CascadaMaxSlTicks (24 ticks = -$300 máx)
                    double structuralSl = keltnerBig.Midline[0] - (2 * TickSize);
                    double maxSafeSl    = entryPrice - (CascadaMaxSlTicks * TickSize);
                    double dynamicSl    = Instrument.MasterInstrument.RoundToTickSize(Math.Max(structuralSl, maxSafeSl));
                    if (Close[0] <= dynamicSl || Low[0] <= dynamicSl)
                    {
                        ExitLong("SL_BandaOpuesta_Cascada", "Cascada50_Long");
                        Print(string.Format("{0} - [PAS - Cascada v10.1 | LARGO CERRADO POR SL ESTRUCTURAL] Precio perforó Línea Media ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                            Time[0], dynamicSl, Close[0]));
                        return;
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

                // --- AS 2: SOBB (SURFER OUT BIG BANDA - MONOCONTRATO QUIRÚRGICO CON OXÍGENO) ---
                if (currentAce == AceType.As2_SOBB)
                {
                    // 1. TOMA DE GANANCIAS / ACTIVACIÓN DE BREAK-EVEN (+10 TICKS)
                    if (!sobbLevel1Achieved && (currentGainTicks >= SobbTp1Ticks || peakGainTicks >= SobbTp1Ticks))
                    {
                        sobbLevel1Achieved = true;
                        Print(string.Format("{0} - [PAS - SOBB | ESCALÓN 1 SUPERADO] Ganancia: +{1:F1} tks. Stop protegido en BREAKEVEN -1tk ({2:F2}). ¡Riesgo CERO!",
                            Time[0], Math.Max(currentGainTicks, peakGainTicks), entryPrice - (1 * TickSize)));
                    }

                    // 2. GESTIÓN TRAS ALCANZAR BREAK-EVEN (NIVEL 1 SUPERADO):
                    if (sobbLevel1Achieved)
                    {
                        double bePrice = entryPrice - (1 * TickSize);

                        // A) Target de Expansión (+20 ticks = $250 ES / $25 MES)
                        if (currentGainTicks >= SobbTp2Ticks || peakGainTicks >= SobbTp2Ticks)
                        {
                            ExitShort("SOBB_Tp2_Expansion", "SOBB_Short");
                            Print(string.Format("{0} - [PAS - SOBB | TARGET EXPANSIÓN ALCANZADO] +{1} ticks (+${2:F2} USD). Victoria asegurada.",
                                Time[0], SobbTp2Ticks, (SobbTp2Ticks * TickSize * Instrument.MasterInstrument.PointValue)));
                            return;
                        }

                        // B) Salida por Ciclo Terminado (Repliegue real adentro de Banda Lower 3.5 tras ganar >= 14 ticks - SIN SUSTO DE TEMA 9)
                        if (peakGainTicks >= 14 && Close[0] > keltnerBig.Lower[0])
                        {
                            ExitShort("Exit_SOBB_CicloTerminado", "SOBB_Short");
                            Print(string.Format("{0} - [PAS - SOBB v9.1 | CICLO TERMINADO REAL] Repliegue adentro de Lower 3.5 ({1:F2}). Salida a Mercado @ ~{2:F2} (+{3:F1} tks pico).",
                                Time[0], keltnerBig.Lower[0], Close[0], peakGainTicks));
                            return;
                        }

                        // C) Stop de Protección en Break-Even - 1 tick
                        if (Close[0] >= bePrice || High[0] >= bePrice)
                        {
                            ExitShort("SOBB_Stop_BE", "SOBB_Short");
                            Print(string.Format("{0} - [PAS - SOBB | SALIDA EN BREAKEVEN] Posición protegida cerrada en {1:F2} tras amague.",
                                Time[0], bePrice));
                            return;
                        }
                    }
                    // 3. STOP LOSS INICIAL ESTRUCTURAL CON OXÍGENO (ANTES DE TOCAR BREAK-EVEN):
                    else
                    {
                        // SL Estructural en Banda Pequeña Upper 1.75 con tope máximo de seguridad (SobbMaxSlTicks = 24 ticks = -$300 USD máx)
                        double structuralSl = keltnerSmall.Upper[0] + (2 * TickSize);
                        double maxSafeSl = entryPrice + (SobbMaxSlTicks * TickSize);
                        double slPrice = Math.Min(structuralSl, maxSafeSl);

                        if (Close[0] >= slPrice || High[0] >= slPrice)
                        {
                            ExitShort("SOBB_SL_Estructural", "SOBB_Short");
                            Print(string.Format("{0} - [PAS - SOBB | SL ESTRUCTURAL EJECUTADO] Precio perforó SL ({1:F2}) | Salida a Mercado @ ~{2:F2} (Riesgo acotado a 1 contrato).",
                                Time[0], slPrice, Close[0]));
                            return;
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
                // --- GESTIÓN AS 1: ZONA 1 (ESCALONAMIENTO 3 FASES CON RUNNER AHOGO + MIDLINE SL) ---
                else if (currentAce == AceType.As1_Zona1_100Bar)
                {
                    double maxProfitTicks = (entryPrice - lowestPriceSinceEntry) / TickSize;

                    // Si estamos operando con 1 solo contrato (por desescalado tras pérdida o regla de piso):
                    if (activeTradeContracts == 1)
                    {
                        // Target pasivo de +8 ticks
                        if (maxProfitTicks >= Zone1Tp1Ticks || currentGainTicks >= Zone1Tp1Ticks)
                        {
                            ExitShort("Z1_Mono_Tp", "Z1-100Bar_Short");
                            Print(string.Format("{0} - [PAS - Z1-100B | MONOCONTRATO TP COBRADO] +{1} ticks (+${2:F2}). ¡Ganancia asegurada!",
                                Time[0], Zone1Tp1Ticks, Zone1Tp1Ticks * TickSize * Instrument.MasterInstrument.PointValue));
                            return;
                        }

                        // Stop Loss en Línea Media con TOPE MÁXIMO de seguridad de 14 ticks:
                        double naturalSl = keltnerBig.Midline[0] + (SlOffsetTicks * TickSize);
                        double slPrice = Math.Min(naturalSl, entryPrice + (14 * TickSize));

                        if (Close[0] >= slPrice || High[0] >= slPrice)
                        {
                            ExitShort("Z1_Mono_SL", "Z1-100Bar_Short");
                            Print(string.Format("{0} - [PAS - Z1-100B | MONOCONTRATO SL EJECUTADO] Precio perforó SL ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                Time[0], slPrice, Close[0]));
                            return;
                        }
                    }
                    else
                    {
                        // MULTICONTRATO (2 o 3 contratos)
                        // CONTRATO 1: Target Límite pasivo a +8 ticks (Zone1Tp1Ticks) ejecutado o detectado
                        if (!z1C1Achieved && (currentQty < activeTradeContracts || maxProfitTicks >= Zone1Tp1Ticks || currentGainTicks >= Zone1Tp1Ticks))
                        {
                            z1C1Achieved = true;
                            if (currentQty >= 3)
                            {
                                ExitShort(1, "Z1_C1_Tp8", "Z1-100Bar_Short");
                            }
                            Print(string.Format("{0} - [PAS - Z1-100B | FASE 1 SUPERADA] C1 ejecutado a +{1} ticks (+${2:F2}). Stop blindado en BREAKEVEN -1tk ({3:F2}). ¡Riesgo CERO!",
                                Time[0], Zone1Tp1Ticks, Zone1Tp1Ticks * TickSize * Instrument.MasterInstrument.PointValue, entryPrice - (1 * TickSize)));
                        }

                        // CONTRATO 2: Target Límite pasivo a +16 ticks (Zone1Tp2Ticks) ejecutado o detectado
                        if (z1C1Achieved && !z1C2Achieved && (currentQty <= 1 || maxProfitTicks >= Zone1Tp2Ticks || currentGainTicks >= Zone1Tp2Ticks))
                        {
                            z1C2Achieved = true;
                            z1ChokeActive = true;
                            if (currentQty >= 2)
                            {
                                ExitShort(1, "Z1_C2_Tp16", "Z1-100Bar_Short");
                            }
                            Print(string.Format("{0} - [PAS - Z1-100B | FASE 2 SUPERADA] C2 cobrado a +{1} ticks (+${2:F2}). Contrato 3 Runner en MODO AHOGO a {3} ticks del Low.",
                                Time[0], Zone1Tp2Ticks, Zone1Tp2Ticks * TickSize * Instrument.MasterInstrument.PointValue, Zone1ChokeTicks));
                        }

                        // CONTRATO 3 (RUNNER): Modo Ahogo a 8 ticks del pico (Zone1ChokeTicks)
                        if (z1ChokeActive || (z1C1Achieved && z1C2Achieved))
                        {
                            double chokeSl = lowestPriceSinceEntry + (Zone1ChokeTicks * TickSize);
                            if (Close[0] >= chokeSl || High[0] >= chokeSl)
                            {
                                ExitShort("Z1_C3_RunnerAhogo", "Z1-100Bar_Short");
                                Print(string.Format("{0} - [PAS - Z1-100B | RUNNER AHOGADO EN EL PISO] Salida a Mercado @ ~{1:F2} (Piso: {2:F2}). ¡Ganancia máxima asegurada!",
                                    Time[0], Close[0], lowestPriceSinceEntry));
                            }
                        }
                        // Stop de Resguardo tras cobrar C1: BreakEven - 1 tick
                        else if (z1C1Achieved)
                        {
                            double bePrice = entryPrice - (1 * TickSize);
                            if (Close[0] >= bePrice || High[0] >= bePrice)
                            {
                                ExitShort("Z1_Stop_BE", "Z1-100Bar_Short");
                                Print(string.Format("{0} - [PAS - Z1-100B | SALIDA EN BREAKEVEN] Posición restante protegida en {1:F2}. Salida sin pérdida.",
                                    Time[0], bePrice));
                            }
                        }
                        // Stop Inicial antes de C1 (Línea Media con TOPE DE 14 TICKS):
                        else
                        {
                            double naturalSl = keltnerBig.Midline[0] + (SlOffsetTicks * TickSize);
                            double slPrice = Math.Min(naturalSl, entryPrice + (14 * TickSize));

                            if (Close[0] >= slPrice || High[0] >= slPrice)
                            {
                                ExitShort("SL_Midline_Zona1", "Z1-100Bar_Short");
                                Print(string.Format("{0} - [PAS - Z1-100B | SL MIDLINE] Precio tocó Midline ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                    Time[0], slPrice, Close[0]));
                            }
                        }
                    }
                }
                // --- GESTIÓN AS 4: CASCADA SURFER MONOCONTRATO (CANDADO $180 Y FIN DE CICLO) ---
                else if (currentAce == AceType.As4_Cascada50)
                {
                    double currentFavorableTicks = (entryPrice - Low[0]) / TickSize;
                    double currentProfitDollars  = currentFavorableTicks * (Instrument.MasterInstrument.PointValue * TickSize);
                    double effectiveLockTrigger  = CascadaLockTriggerDollars * instrumentMultiplier;

                    // CANDADO DE $180: Al superar $180 a favor ($18 en MES), el candado se arma
                    if (!isCascadaLockActive && currentProfitDollars >= effectiveLockTrigger)
                    {
                        isCascadaLockActive = true;
                        Print(string.Format("{0} - [PAS - Cascada | CANDADO ACTIVADO - CORTO] Ganancia flotante alcanzó {1:C2} (+{2:F1} tks). Candado armado en Lower 1.75 +1tk.",
                            Time[0], currentProfitDollars, currentFavorableTicks));
                    }

                    // 1. Salida por Candado Activo (Lower 1.75 + 1 tick tras superar $180 USD)
                    if (isCascadaLockActive)
                    {
                        double lockSl = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Lower[0] + (1 * TickSize));
                        if (Close[0] >= lockSl || High[0] >= lockSl)
                        {
                            ExitShort("SL_Candado_Cascada", "Cascada50_Short");
                            Print(string.Format("{0} - [PAS - Cascada v10.1 | CORTO COBRADO CON CANDADO] Precio superó Lower 1.75 +1tk ({1:F2}) | Salida a Mercado @ ~{2:F2} (Ganancia asegurada).",
                                Time[0], lockSl, Close[0]));
                            return;
                        }
                    }

                    // 2. Salida por Fin de Ciclo del Rápido (Repliegue adentro de Banda 1.75 o freno de pendiente tras ganar >= 14 ticks)
                    double normMidSlopeShort = GetNormalizedSlope(keltnerBig.Midline);
                    if (peakGainTicks >= 14 && (Close[0] > keltnerSmall.Lower[0] || normMidSlopeShort >= 0.0))
                    {
                        ExitShort("Exit_Cascada_CicloTerminado", "Cascada50_Short");
                        Print(string.Format("{0} - [PAS - Cascada v10.1 | FIN DE RÁPIDO] Repliegue adentro de Lower 1.75 ({1:F2}) o freno de pendiente ({2:F2}) tras ganar {3:F1} tks pico. Salida a Mercado @ ~{4:F2}",
                            Time[0], keltnerSmall.Lower[0], normMidSlopeShort, peakGainTicks, Close[0]));
                        return;
                    }

                    // 3. Stop Loss Inicial Estructural (antes de alcanzar el Candado) en Línea Media con tope de seguridad CascadaMaxSlTicks (24 ticks = -$300 máx)
                    double structuralSl = keltnerBig.Midline[0] + (2 * TickSize);
                    double maxSafeSl    = entryPrice + (CascadaMaxSlTicks * TickSize);
                    double dynamicSl    = Instrument.MasterInstrument.RoundToTickSize(Math.Min(structuralSl, maxSafeSl));
                    if (Close[0] >= dynamicSl || High[0] >= dynamicSl)
                    {
                        ExitShort("SL_BandaOpuesta_Cascada", "Cascada50_Short");
                        Print(string.Format("{0} - [PAS - Cascada v10.1 | CORTO CERRADO POR SL ESTRUCTURAL] Precio superó Línea Media ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                            Time[0], dynamicSl, Close[0]));
                        return;
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
            z1WaitingForMidlineLong = false;
            z1MidlineCountdownLong = 0;
            z1WaitingForMidlineShort = false;
            z1MidlineCountdownShort = 0;
            z1C1Achieved = false;
            z1C2Achieved = false;
            z1ChokeActive = false;
            sobbArmedLong = false;
            sobbArmedShort = false;
            sobbHasFloatedLong = false;
            sobbHasFloatedShort = false;
            sobbFloatingBarsCountLong = 0;
            sobbFloatingBarsCountShort = 0;
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
            isCascadaLockActive = false;

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

                // Verificación de corte a la 1:00 PM si el trade que acaba de cerrar ocurrió después de la 1:00 PM (Solo Escalón 3 sigue activo)
                if (UseDailyManagement && UseTier1LunchCutoff && !isDailyLocked && currentDailyTier < 3)
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
                            Time[0], timeNow, dailyRealizedPnL, currentDailyTier == 2 ? "Escalón 2" : (currentDailyTier == 1 ? "Escalón 1" : "Base")));
                    }
                }

                // Si se cerró Cascada, reseteo de contadores de fondo
                if (currentAce == AceType.As4_Cascada50)
                {
                    barsCascadaShortCount = 0;
                    barsCascadaLongCount = 0;
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

                    if (trade.Entry != null && trade.Entry.Name.IndexOf("Cascada", StringComparison.OrdinalIgnoreCase) >= 0 && netProfit > 0)
                    {
                        cascadaHasWonToday = true;

                        // CUARENTENA DE SACIEDAD: Si ganó >= $180 USD ($18 MES) o salió por Candado, congela Cascada por 60 minutos
                        double effectiveBigWinThreshold = CascadaBigWinThresholdDollars * instrumentMultiplier;
                        if (netProfit >= effectiveBigWinThreshold || (trade.Exit != null && trade.Exit.Name.Contains("Candado")))
                        {
                            cascadaQuarantineEndTime = trade.Exit.Time.AddMinutes(CascadaQuarantineMinutes);
                            Print(string.Format("****************************************************************************************\n" +
                                                "{0} - [PAS - Cascada v10.0 | ¡QUIETO EN PRIMERA! CUARENTENA DE SACIEDAD ACTIVADA]\n" +
                                                "¡Qué grosería de trade! Ganamos +{1:C2} en Cascada. Candado asegurado.\n" +
                                                "Gatillo de Cascada congelado por {2} minutos (hasta las {3:HH:mm:ss} EST). ¡A cuidar el botín!\n" +
                                                "****************************************************************************************",
                                trade.Exit.Time, netProfit, CascadaQuarantineMinutes, cascadaQuarantineEndTime));
                        }
                    }

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
                                           "{0} - [ESCUDO DIARIO | ¡MODO DULCE ACTIVADO!]\n" +
                                           "¡Qué grosería de día! PnL Diario: ${1:F2} USD. Ya superamos las ${2:F2} lukas.\n" +
                                           "Soltamos el trailing infinito y el nuevo piso protegido sube a +${3:F2} USD.\n" +
                                           "¡A romper la banca sin techo!\n" +
                                           "****************************************************************************************",
                            Time[0], dailyRealizedPnL, DailyGoal_Piso3, currentDailyFloor));
                    }
                }
                // Escalón 2: >= $600 USD ($60 MES) -> Piso Protegido de +$500 USD (+ $50 MES)
                else if (dailyRealizedPnL >= DailyGoal_Piso2)
                {
                    if (currentDailyFloor < DailyFloor_Piso2)
                    {
                        currentDailyTier = 2;
                        currentDailyFloor = DailyFloor_Piso2;
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [ESCUDO DIARIO | ¡SEGUNDO ESCALÓN CONQUISTADO!]\n" +
                                           "¡Qué nivel mi pez! Subiste al segundo escalón. PnL Diario: ${1:F2} USD.\n" +
                                           "Ya te llevas mínimo en tu bolsillo ${2:F2} lukas blindadas.\n" +
                                           "Ahora apúntele a las ${3:F2} lukas para activar el Modo Dulce y soltar el trailing infinito. ¡Eso es trading de verdad!\n" +
                                           "****************************************************************************************",
                            Time[0], dailyRealizedPnL, currentDailyFloor, DailyGoal_Piso3));
                    }
                }
                // Escalón 1: >= $400 USD ($40 MES) -> Piso Protegido de +$250 USD (+ $25 MES)
                else if (dailyRealizedPnL >= DailyGoal_Piso1)
                {
                    if (currentDailyFloor < DailyFloor_Piso1)
                    {
                        currentDailyTier = 1;
                        currentDailyFloor = DailyFloor_Piso1;
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [ESCUDO DIARIO | ¡PRIMER ESCALÓN CONQUISTADO!]\n" +
                                           "¡Ufff parsero, ya subiste el primer escalón! PnL Diario: ${1:F2} USD.\n" +
                                           "Ya te llevas mínimo en tu bolsillo ${2:F2} lukas aseguradas.\n" +
                                           "Sigue para el siguiente nivel que tú puedes, solo tienes que llegar a ${3:F2} lukas y estamos en el otro nivel. ¡Vamos con toda!\n" +
                                           "****************************************************************************************",
                            Time[0], dailyRealizedPnL, currentDailyFloor, DailyGoal_Piso2));
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
        [Range(2, 5)]
        [Display(Name = "SOBB Barras Consecutivas Requeridas", Description = "Número de barras consecutivas 100% afuera requeridas para evaluar confirmación (2 por defecto)", GroupName = "5. As 2: SOBB (Escalones)", Order = 0)]
        public int SobbRequiredFloatingBars { get; set; }

        [NinjaScriptProperty]
        [Range(2, 6)]
        [Display(Name = "SOBB Ventana Máxima Barras Afuera", Description = "Ventana máxima de barras afuera para confirmar rompimiento antes de cancelar (3 por defecto)", GroupName = "5. As 2: SOBB (Escalones)", Order = 1)]
        public int SobbMaxFloatingBarsWindow { get; set; }

        [NinjaScriptProperty]
        [Range(4, 50)]
        [Display(Name = "SOBB TP1 Asegurador (Ticks)", Description = "Ticks para liquidar C1 Asegurador o pasar a BE (+10 ticks = $125 ES / $12.50 MES)", GroupName = "5. As 2: SOBB (Escalones)", Order = 2)]
        public int SobbTp1Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(8, 100)]
        [Display(Name = "SOBB TP2 Expansión (Ticks)", Description = "Ticks para liquidar en zona de expansión (+20 ticks = $250 ES / $25 MES)", GroupName = "5. As 2: SOBB (Escalones)", Order = 3)]
        public int SobbTp2Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 50)]
        [Display(Name = "SOBB SL Máximo de Seguridad (Ticks)", Description = "Tope máximo de pérdida para el SL Estructural (24 ticks = 6 pts = $300 ES / $30 MES)", GroupName = "5. As 2: SOBB (Escalones)", Order = 4)]
        public int SobbMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 200)]
        [Display(Name = "SOBB C3 Umbral Ahogo (Ticks)", Description = "Ticks para activar Modo Ahogo para C3 Runner (+30 ticks = $375 ES / $37.50 MES)", GroupName = "5. As 2: SOBB (Escalones)", Order = 5)]
        public int SobbChokeThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(2, 20)]
        [Display(Name = "SOBB C3 Distancia Ahogo (Ticks)", Description = "Ticks de separación del High/Low para el trailing del Runner", GroupName = "5. As 2: SOBB (Escalones)", Order = 6)]
        public int SobbChokeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 300)]
        [Display(Name = "Z1 Barras Mínimas", Description = "Mínimo de barras comprimidas sin cruzar EMA 200", GroupName = "6. As 1: Zona 1", Order = 1)]
        public int MinBarsZona1 { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Z1 Ventana Máx a Línea Media (Barras)", Description = "Ventana máxima de barras tras cruce TEMA para que EMA 200 toque la Línea Media (40 barras por defecto)", GroupName = "6. As 1: Zona 1", Order = 2)]
        public int Z1MaxBarsToMidline { get; set; }

        [NinjaScriptProperty]
        [Range(2, 30)]
        [Display(Name = "Z1 Máx Ticks en Zona 0", Description = "Tolerancia máxima de ticks por encima/debajo de Banda 3.5 en Zona 0 (10 ticks por defecto)", GroupName = "6. As 1: Zona 1", Order = 3)]
        public int Z1MaxZone0Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(4, 30)]
        [Display(Name = "Z1 C1 TP (Ticks)", Description = "Target de salida de C1 y activación de BE (8 ticks = $100 ES / $10 MES)", GroupName = "6. As 1: Zona 1", Order = 4)]
        public int Zone1Tp1Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(8, 50)]
        [Display(Name = "Z1 C2 TP (Ticks)", Description = "Target de salida de C2 (16 ticks = $200 ES / $20 MES)", GroupName = "6. As 1: Zona 1", Order = 5)]
        public int Zone1Tp2Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(2, 20)]
        [Display(Name = "Z1 C3 Ahogo (Ticks)", Description = "Distancia ceñida para el trailing del C3 Runner (8 ticks)", GroupName = "6. As 1: Zona 1", Order = 6)]
        public int Zone1ChokeTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cascada Hora Inicio (HHMMSS)", Description = "Hora mínima para disparar Cascada (10:00 AM = 100000 para evitar trampas de pre-mercado)", GroupName = "7. As 4: Cascada Surfer", Order = 0)]
        public int CascadaStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, 3)]
        [Display(Name = "Cascada Contratos Modo Exploratorio", Description = "Contratos de Cascada en modo exploratorio (1 contrato)", GroupName = "7. As 4: Cascada Surfer", Order = 1)]
        public int CascadaFirstEntryContracts { get; set; }

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "Cascada Barras Mínimas", Description = "Barras con EMA 200 separada de Banda 3.5 (25 por defecto)", GroupName = "7. As 4: Cascada Surfer", Order = 1)]
        public int CascadaMinBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Cascada Separación Ticks", GroupName = "7. As 4: Cascada Surfer", Order = 2)]
        public int CascadaSeparationTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Cascada Pendiente Mínima Ticks", GroupName = "7. As 4: Cascada Surfer", Order = 3)]
        public int CascadaMinSlopeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(20.0, 500.0)]
        [Display(Name = "Cascada Candado Trigger (USD)", Description = "Umbral de ganancia flotante en USD para activar el Candado dinámico ($180 ES / $18 MES)", GroupName = "7. As 4: Cascada Surfer", Order = 4)]
        public double CascadaLockTriggerDollars { get; set; }

        [NinjaScriptProperty]
        [Range(6, 40)]
        [Display(Name = "Cascada SL Máximo de Seguridad (Ticks)", Description = "Tope máximo de pérdida estructural inicial (24 ticks = 6 pts = $300 ES / $30 MES)", GroupName = "7. As 4: Cascada Surfer", Order = 5)]
        public int CascadaMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(20.0, 3000.0)]
        [Display(Name = "Cascada Umbral Súper Win (USD)", Description = "Ganancia mínima en USD para activar la Cuarentena de Saciedad ($180 ES / $18 MES)", GroupName = "7. As 4: Cascada Surfer", Order = 6)]
        public double CascadaBigWinThresholdDollars { get; set; }

        [NinjaScriptProperty]
        [Range(10, 240)]
        [Display(Name = "Cascada Cuarentena (Minutos)", Description = "Minutos de congelamiento de Cascada tras una victoria (60 minutos por defecto)", GroupName = "7. As 4: Cascada Surfer", Order = 7)]
        public int CascadaQuarantineMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 1.0)]
        [Display(Name = "Cascada Umbral Pendiente Rápido", Description = "Umbral de aceleración de Pendiente Normalizada (LinReg 5 / ATR 14) para disparar rápido de Cascada (0.20 por defecto)", GroupName = "7. As 4: Cascada Surfer", Order = 8)]
        public double CascadaSlopeThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(4, 30)]
        [Display(Name = "Cascada C1 TP (Ticks)", Description = "(Compatibilidad NT8)", GroupName = "7. As 4: Cascada Surfer", Order = 9)]
        public int CascadaTp1Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(8, 50)]
        [Display(Name = "Cascada C2 TP (Ticks)", Description = "(Compatibilidad NT8)", GroupName = "7. As 4: Cascada Surfer", Order = 10)]
        public int CascadaTp2Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(2, 20)]
        [Display(Name = "Cascada C3 Ahogo (Ticks)", Description = "(Compatibilidad NT8)", GroupName = "7. As 4: Cascada Surfer", Order = 11)]
        public int CascadaChokeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 1.0)]
        [Display(Name = "Umbral Pendiente Tendencia SOBB y Z1", Description = "Umbral de Pendiente Normalizada (LinReg 5 / ATR 14) para filtrar mercados planos en SOBB y Z1 (0.15 por defecto)", GroupName = "8. Indicadores", Order = 0)]
        public double SlopeThresholdTrend { get; set; }

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
        [Range(6, 30)]
        [Display(Name = "Ratchet SL Máximo de Seguridad (Ticks)", Description = "Tope máximo de pérdida inicial para Ratchet (12 ticks = $450 ES / $45 MES en 3 contratos)", GroupName = "9. As 3: Ratchet", Order = 7)]
        public int RatchetMaxSlTicks { get; set; }
        #endregion
    }
}
