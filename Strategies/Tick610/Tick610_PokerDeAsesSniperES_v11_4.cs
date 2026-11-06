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
// Desarrollado con Mentor├¡a Cuantitativa Institucional para Gr├íficas de 610 Ticks en Futuros ES / MES.
// VERSI├ôN: 11.4 - Poker de Ases (PAS) [Corte de Mediod├¡a 12:30 PM, Cascada Slope 0.15 / Cuarentena 30min, SOBB Slope 0.12]
//
// LAS 4 CARTAS DEL POKER (LOS 4 ASES):
//   1. As 1: [Z1-100Bar] - Ruptura quir├║rgica tras >= 100 barras comprimidas en EMA 200 con Canal Grande 3.5 y TTL 25 barras.
//   2. As 2: [SOBB]      - SurferOutBigBanda: MONOCONTRATO QUIR├ÜRGICO (1 contrato fijo) con Stop Estructural holgado
//                          anclado a la Banda Peque├▒a 1.75 (Tope m├íx 24 ticks = -$300 USD). Cero asfixia, salida en expansi├│n (+20 ticks)
//                          o por repliegue de TEMA 9 (Ciclo Terminado) tras alcanzar Break-Even (+10 ticks).
//   3. As 3: [Ratchet]   - S├║per-tendencia vertical institucional real: Requiere OBLIGATORIAMENTE que EMA 200 est├⌐
//                          FUERA del Canal Grande 3.5 y que la L├¡nea Media tenga pendiente a favor. Stop inicial con
//                          TOPE DE SEGURIDAD de 12 ticks (m├íx -$450 USD en 3 contratos) y Chicle Parab├│lico.
//   4. As 4: [Cascada]   - El Tren Bala Desbocado (25 barras): FILTRO ESTRUCTURAL ESTRICTO DE ZONAS 0/1 (Largos)
//                          y ZONAS 4/5 (Cortos). Prohibido comprar en Zona 2 (agotamiento de tendencia).
//
// GESTI├ôN DE RIESGO INTELIGENTE (DESESCALADO DIN├üMICO, DESESCALADO TRAS P├ëRDIDA & CORTE A LA 1:00 PM):
//   - Desescalado tras P├⌐rdida: Si la operaci├│n previa cerr├│ con p├⌐rdida (Strike activo), el siguiente trade entra
//     obligatoriamente con 1 solo contrato para proteger la cuenta.
//   - Desescalado Din├ímico: Si el PnL est├í cerca del piso protegido, reduce la posici├│n (a 2 o 1 contrato) para garantizar
//     un Stop Loss saludable sin romper el piso garantizado.
//   - Victoria Temprana a la 1:00 PM: Si a las 01:00 PM EST estamos en Escal├│n 1 ($400 - $599 USD), apaga nuevas entradas y
//     asegura la ganancia del d├¡a para evitar amagues de bajo volumen en la sesi├│n de la tarde.
//
// ESCUDO DE CUENTA (CALIBRACI├ôN INSTITUCIONAL 2.5 - 3 VIDAS - TRAILING INFINITO - BLINDAJE v6.0):
//   - Cortacircuito Inicial: -$650 USD (ES) / -$65 USD (MES) -> Respaldo para 2.5 a 3 oportunidades saludables.
//   - Escal├│n 1 (+$400): Piso protegido en +$250 USD (Corte autom├ítico a la 1:00 PM para asegurar victoria).
//   - Escal├│n 2 (+$600): Piso protegido CLAVADO en +$500 USD (inviolable, comprobaci├│n continua en cada barra).
//   - Modo Dulce (+$900+): Trailing Infinito (Piso = R├⌐cord Ganado - Colch├│n $150). ┬íNo te para de ganar!
//   - Bot├│n de P├ínico WPF en Gr├ífica y Atajo de Teclado (Ctrl + Espacio).
// =========================================================================================================

namespace NinjaTrader.NinjaScript.Strategies
{
    public class Tick610_PokerDeAsesSniperES_v11_4 : Strategy
    {
        #region Variables y Enumeraciones

        // === ENUMERACI├ôN DE LOS 4 ASES ===
        private enum AceType
        {
            None,
            As1_Zona1_100Bar,
            As2_SOBB,
            As3_Ratchet,
            As4_Cascada50
        }
        private AceType currentAce = AceType.None;

        // === VARIABLES DEL AS 1: ZONA 1 (100 BARRAS - GOLAZO A L├ìNEA MEDIA EN 40 BARRAS) ===
        private int lastTemaSide = 0;              // 1 = Arriba de EMA200, -1 = Debajo de EMA200
        private bool crossUpConsumed = false;
        private bool crossDownConsumed = false;
        private int barsBelowEmaWithoutCross = 0;  // Acumulaci├│n continua con TEMA/Precio bajo EMA200
        private int barsAboveEmaWithoutCross = 0;  // Acumulaci├│n continua con TEMA/Precio sobre EMA200
        private bool z1LongArmed = false;          // Acumulaci├│n de 100 barras completada para Largo
        private bool z1ShortArmed = false;         // Acumulaci├│n de 100 barras completada para Corto
        private bool z1WaitingForMidlineLong = false; // Cron├│metro activo tras cruce TEMA buscando toque de L├¡nea Media (m├íx 40 barras)
        private int z1MidlineCountdownLong = 0;      // Contador de barras transcurridas tras el cruce para Largo
        private bool z1WaitingForMidlineShort = false;// Cron├│metro activo tras cruce TEMA buscando toque de L├¡nea Media (m├íx 40 barras)
        private int z1MidlineCountdownShort = 0;     // Contador de barras transcurridas tras el cruce para Corto
        private bool z1C1Achieved = false;           // Contrato 1 cerrado a +8 ticks (BE armado)
        private bool z1C2Achieved = false;           // Contrato 2 cerrado a +16 ticks
        private bool z1ChokeActive = false;          // Contrato 3 Runner en Modo Ahogo (8 ticks)

        // === VARIABLES DEL AS 2: SOBB (SurferOutBigBanda - GATILLO 2 CAPAS + ESTRUCTURA CONSECUTIVA v9.0) ===
        private bool sobbArmedLong = false;        // Fase 1: TEMA 9 cruz├│ Upper 3.5 (Espera activa a que la vela salga)
        private bool sobbArmedShort = false;       // Fase 1: TEMA 9 cruz├│ Lower 3.5 (Espera activa a que la vela salga)
        private bool sobbHasFloatedLong = false;   // Bandera de vela flotando afuera para Largos
        private bool sobbHasFloatedShort = false;  // Bandera de vela flotando afuera para Cortos
        private int  sobbFloatingBarsCountLong = 0;  // Contador de barras consecutivas flotando afuera en Largos
        private int  sobbFloatingBarsCountShort = 0; // Contador de barras consecutivas flotando afuera en Cortos
        private bool sobbLevel1Achieved = false;   // Piso 1 alcanzado (Contrato 1 cerrado, BE armado)
        private bool sobbLevel2Achieved = false;   // Piso 2 alcanzado (Contrato 2 cerrado, Banda 3.5 anclada)
        private bool sobbLevel3ChokeActive = false;// Piso 3 activo (Modo Ahogo para Contrato 3 Runner)

        // === VARIABLES DEL AS 3: RATCHET (EL CHICLE PARAB├ôLICO ORIGINAL) ===
        private enum RatchetSetup { None, Long, Short }
        private RatchetSetup ratchetSetup = RatchetSetup.None;
        private int ratchetSetupBarCounter = 0;
        private bool ratchetC1Achieved = false;    // Contrato 1 cerrado en Banda 3.5 o +22 ticks (BE armado)
        private bool ratchetC2Achieved = false;    // Contrato 2 cerrado (Stop L├¡nea Media o Fase 3)
        private bool ratchetChokeActive = false;   // Contrato 3 Runner en Chicle Parab├│lico

        // === VARIABLES DEL AS 4: CASCADA (CAZADOR DE R├üPIDOS POR PENDIENTE NORMALIZADA + CANDADO $180) ===
        private int barsCascadaShortCount = 0;     // Barras con EMA200 > Upper 3.5 + 4 ticks (conteo de fondo)
        private int barsCascadaLongCount  = 0;     // Barras con EMA200 < Lower 3.5 - 4 ticks (conteo de fondo)
        private bool cascadaCycleLongFired  = false; // Bandera de 1 solo tiro por impulso en Largos (Anti-compras en techos)
        private bool cascadaCycleShortFired = false; // Bandera de 1 solo tiro por impulso en Cortos (Anti-ventas en suelos)
        private bool isCascadaLockActive  = false; // Candado de $180 activado (salida en Banda 1.75 favorable)
        private bool cascadaC1Achieved    = false; // Contrato 1 cerrado a +8 ticks (BE armado)
        private bool cascadaC2Achieved    = false; // Contrato 2 cerrado a +16 ticks
        private bool cascadaChokeActive   = false; // Contrato 3 Runner en Modo Ahogo (8 ticks)
        private int  cascadaTradesToday   = 0;     // Contador de disparos de Cascada en la sesi├│n diaria actual
        private bool cascadaHasWonToday   = false; // Bandera: Cascada ya cobr├│ un trade ganador hoy (Pisar Suave liberado)
        private DateTime cascadaQuarantineEndTime = DateTime.MinValue; // Fin de la cuarentena de 60 min tras victoria (>= $180 USD)

        // Tracking de MFE/MAE de la posici├│n activa
        private double entryPriceGlobal = 0.0;
        private double highestPriceSinceEntry = 0.0;
        private double lowestPriceSinceEntry = double.MaxValue;

        // === ESCUDO DIARIO Y GESTI├ôN DE RIESGO DE CUENTA (TRAILING INFINITO) ===
        private double dailyRealizedPnL = 0.0;
        private int consecutiveLosses = 0;
        private bool isDailyLocked = false;
        private string dailyLockReason = string.Empty;
        private int lastSessionDate = 0;
        private double currentDailyFloor = -800.0; // Piso de p├⌐rdida en vivo (Calibrado a -$800 ES / -$80 MES)
        private int currentDailyTier = 0;          // 0 = Base (-$800), 1 = Piso 1 (+$300), 2 = Piso 2 (+$550), 3 = Trailing Infinito
        private double peakDailyClosedPnL = 0.0;   // M├íximo PnL diario cerrado para arrastre del Trailing Infinito
        private int activeTradeContracts = 3;      // Contratos asignados din├ímicamente seg├║n el margen del piso

        // === TRANCA DE ADAMANTIUM (GARRAS DE WOLVERINE & FILTRO ANTI-ASFIXIA) ===
        private bool   trancaActive = false;         // Bandera: ┬┐Se activ├│ la tranca de adamantium en este trade?
        private double trancaPrice = 0.0;            // Precio del Stop de adamantium que asegura el nuevo escal├│n neto
        private double trancaBonusPrice = 0.0;       // Precio del TP Sprint Bonus (+ $100 extra)
        private double trancaMilestoneLevel = 0.0;   // Nivel del escal├│n conquistado ($400, $600 o $900)
        private int    trancaArmedBar = -1;          // Barra donde se clav├│ la Tranca para evitar falso autodisparo en misma vela

        // === FACTOR MULTIPLICADOR (AUTO-ADAPTACI├ôN MINI ES VS MICRO MES) ===
        private double instrumentMultiplier = 1.0; // 1.0 para ES, 0.1 para MES
        private bool isMicroInstrument = false;

        // === INDICADORES EN MEMORIA (SIN DIBUJAR POR REGLA AGENTS.md) ===
        private TEMA tema9;
        private EMA ema200;
        private KeltnerChannel keltnerBig;   // Canal Exterior (52, 3.5)
        private KeltnerChannel keltnerSmall; // Canal Interior (52, 1.75)
        private ATR atr14;                   // ATR 14 para Normalizaci├│n Institucional de Pendientes LinReg

        // === INTERFAZ WPF (BOT├ôN DE P├üNICO) ===
        private System.Windows.Controls.Button panicButton;
        private bool isStrategyActive = false; // Siempre inicia en PAUSADO en tiempo real
        private Chart chartWindow;
        private bool isSubscribedToKeys = false;
        private int lastReportedTradeCount = 0;

        #endregion

        #region Inicializaci├│n y Configuraci├│n
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Estrategia Institucional Poker de Ases Sniper (PAS) v6.0 en 610 Ticks para ES/MES. Combina Z1-100Bar (Canal Grande 3.5 con TTL 25 Barras & Filtro Zona 0), SOBB (Monocontrato Quir├║rgico con Ox├¡geno Estructural), Ratchet (Vertical Institucional con EMA 200 Externa 3.5, Pendiente Midline & SL Tope 12tk), Cascada 25, Cortacircuito -$650 USD, Desescalado tras P├⌐rdida y Victoria Temprana a la 1:00 PM.";
                Name = "Tick610_PokerDeAsesSniperES_v11_4";
                Calculate                                   = Calculate.OnBarClose;
                EntriesPerDirection                         = 3; // Permite hasta 3 contratos por direcci├│n
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
                Version                                     = "11.4 - Poker de Ases (PAS) [Corte Mediodia 12:30 PM, Cascada 0.15/30m, SOBB 0.12]";

                // Horario RTH estricto (Nueva York EST - Oficial 09:50 AM - Regla de Oro AGENTS.md)
                StartTime                                   = 95000;  // 09:50 AM EST
                StopEntriesTime                             = 154500; // 03:45 PM EST (Bloqueo de nuevas entradas)
                ForceCloseTime                              = 160000; // 04:00 PM EST (Liquidaci├│n obligatoria)

                // Gesti├│n de Contratos del Escuadr├│n
                Contracts                                   = 3;      // Por defecto: 3 contratos (Asegurador, Estructural, Runner)

                // Par├ímetros de Indicadores
                TemaPeriod                                  = 9;
                EmaPeriod                                   = 200;
                KeltnerPeriod                               = 52;
                KeltnerBigMult                              = 3.5;
                KeltnerSmallMult                            = 1.75;

                // Par├ímetros del As 1: Z1-100Bar (Golazo Sincronizado EMA200 a L├¡nea Media en 40 Barras + Escalonamiento 3 Fases)
                MinCrossTicks                               = 2.3;    // Penetraci├│n m├¡nima de cruce real
                MinBarsZona1                                = 100;    // 100 barras obligatorias de compresi├│n previa
                Z1MaxBarsToMidline                          = 40;     // Ventana m├íxima de 40 barras para que EMA 200 toque la L├¡nea Media tras el cruce
                Z1MaxZone0Ticks                             = 10;     // L├¡mite m├íximo de ticks en Zona 0 (no m├ís de 10 ticks sobre/bajo Banda 3.5)
                Zone1Tp1Ticks                               = 8;      // C1 TP (+8 ticks = $100 ES / $10 MES) -> Pasa a BE +1tk
                Zone1Tp2Ticks                               = 16;     // C2 TP (+16 ticks = $200 ES / $20 MES) -> Meta de Expansi├│n
                Zone1ChokeTicks                             = 8;      // C3 Runner Ahogo a 8 ticks del High/Low
                SlOffsetTicks                               = 2;      // Offset de respiro en ticks para el Stop Loss
                MinSafetySlTicks                            = 8;      // Colch├│n m├¡nimo de seguridad inicial

                // Par├ímetros del As 2: SOBB (SurferOutBigBanda) - Monocontrato Quir├║rgico con Ox├¡geno Estructural
                SobbRequiredFloatingBars                    = 2;      // Barras m├¡nimas consecutivas 100% flotando afuera para confirmar fuga (2 barras por defecto)
                SobbMaxFloatingBarsWindow                   = 3;      // Ventana m├íxima de barras consecutivas afuera para gatillar (m├íx 3 barras antes de cancelar)
                SobbTp1Ticks                                = 10;     // Escal├│n 1: +10 ticks ($125 ES / $12.50 MES) - Pasa Stop a Break-Even + 1tk
                SobbTp2Ticks                                = 20;     // Escal├│n 2: +20 ticks ($250 ES / $25.00 MES) - Meta de Expansi├│n
                SobbMaxSlTicks                              = 24;     // Tope de seguridad m├íximo para SL Estructural (24 ticks = 6 pts = $300 ES / $30 MES)
                SobbChokeThresholdTicks                     = 30;     // Escal├│n 3: +30 ticks ($375 ES / $37.50 MES) - Activa Modo Ahogo
                SobbChokeTicks                              = 8;      // Distancia ce├▒ida pegada al High/Low para el trailing del Runner

                // Par├ímetros del As 3: Ratchet (El Chicle Parab├│lico Original - Vertical Institucional)
                RatchetLookbackBars                         = 20;     // Barras sin tocar EMA 200
                RatchetCountdownBars                        = 10;     // Ventana de conteo buscando cruce de L├¡nea Media
                RatchetTemaTolTicks                         = 2;      // Tolerancia en ticks para gancho de TEMA 9
                RatchetBreakEvenTicks                       = 22;     // BreakEven din├ímico para Contrato 1
                RatchetChokeThresholdTicks                  = 72;     // Fase 3 Choke a los 72 ticks ($900 ES / $90 MES)
                RatchetChicleTicks                          = 7;      // Trailing ce├▒ido del Chicle Parab├│lico (7 ticks)
                RatchetMaxSlTicks                           = 12;     // Tope m├íximo de seguridad para SL de Ratchet (12 ticks = $450 ES / $45 MES en 3 contratos)

                // Par├ímetros del As 4: Cascada (Cazador de R├ípidos por Pendiente Normalizada + Candado $180)
                CascadaStartTime                            = 100000; // 10:00 AM EST - Filtro Anti-Acomodaci├│n de Pre-Mercado
                CascadaFirstEntryContracts                  = 1;      // Modo exploratorio (1 contrato)
                CascadaMinBars                              = 25;     // 25 barras consecutivas requeridas (compatibilidad)
                CascadaSeparationTicks                      = 4;      // M├¡nimo 4 ticks de separaci├│n de la Banda 3.5
                CascadaMinSlopeTicks                        = 2;      // Inclinaci├│n m├¡nima requerida en la EMA 200
                CascadaLockTriggerDollars                   = 180.0;  // Umbral de ganancia en USD para activar Candado ($180 ES / $18 MES)
                CascadaMaxSlTicks                           = 24;     // Tope m├íximo de seguridad estructural inicial (24 ticks = 6 pts = $300 ES / $30 MES)
                CascadaBigWinThresholdDollars               = 180.0;  // Umbral de ganancia para activar Cuarentena de Saciedad ($180 ES / $18 MES)
                CascadaQuarantineMinutes                    = 30;     // Minutos de cuarentena/congelamiento de Cascada tras victoria (30 min)
                CascadaTp1Ticks                             = 8;      // (Compatibilidad NT8)
                CascadaTp2Ticks                             = 16;     // (Compatibilidad NT8)
                CascadaChokeTicks                           = 8;      // (Compatibilidad NT8)
                CascadaSlopeThreshold                       = 0.15;   // Umbral de aceleraci├│n de Pendiente Normalizada LinReg/ATR para Cascada (0.15)
                SobbSlopeThreshold                          = 0.12;   // Umbral de Pendiente Normalizada LinReg/ATR para SOBB (0.12)
                Zona1SlopeThreshold                         = 0.15;   // Umbral de Pendiente Normalizada LinReg/ATR para Z1-100Bar (0.15)

                // Escudo Diario de Cuenta (Trailing Diario Infinito - Calibraci├│n Ce├▒ida -$650 USD para 2.5 - 3 Vidas)
                UseDailyManagement                          = true;   // Activar Cortacircuito y Trailing Diario
                DailyLossLimit                              = 650.0;  // Cortacircuito calibrado a -$650 ES / -$65 MES (2.5 a 3 vidas holgadas)
                DailyGoal_Piso1                             = 400.0;  // Meta Escal├│n 1: +$400 ES / +$40 MES (Piso a +$250)
                DailyFloor_Piso1                            = 250.0;  // Piso protegido Escal├│n 1: +$250 ES / +$25 MES
                DailyGoal_Piso2                             = 600.0;  // Meta Escal├│n 2: +$600 ES / +$60 MES (Piso a +$500)
                DailyFloor_Piso2                            = 500.0;  // Piso protegido Escal├│n 2: +$500 ES / +$50 MES
                DailyGoal_Piso3                             = 900.0;  // Umbral Modo Dulce: +$900 ES / +$90 MES (Activa Trailing Infinito)
                DailyTrailingGiveback                       = 150.0;  // Colch├│n de retroceso permitido del pico ganado ($150 ES / $15 MES)
                MaxConsecutiveLosses                        = 3;      // Regla de 3 Strikes por Operaci├│n Global
                UseTier1LunchCutoff                         = true;   // Activar Victoria Temprana en Escal├│n 1 a las 12:30 PM EST
                Tier1CutoffTime                             = 120000; // 12:00 PM EST (Hora de corte de mediodia solicitada)
                UseTrancaUniversal                          = true;   // Activar Tranca de Adamantium Universal en cada escal├│n
                TrancaOnlyFromTier2                         = true;   // Pista libre en piso inicial: solo clava tranca desde Escal├│n 2 ($600+) y Modo Dulce ($900)
                TrancaBonusDollars                          = 100.0;  // D├│lares de Sprint Bonus tras clavar la tranca ($100 ES / $10 MES)
                TrancaSafetyDollars                         = 10.0;   // Colch├│n de seguridad para comisiones ($10 ES / $1 MES)
                MinTrancaDistanceDollars                    = 125.0;  // Filtro Anti-Asfixia: M├¡nimo $125 USD (10 ticks ES / $12.5 MES) de pista para armar tranca
                TrancaConquestBufferDollars                 = 50.0;   // Colch├│n Conquista Adamantium: Penetraci├│n de $50 USD (4 ticks en ES / $5 MES) para validar fuerza real
                TrancaAnchorDollars                         = 20.0;   // Anclaje de Titanio sobre el Escal├│n: +$20 USD ($420, $620, $920 en ES / $2 MES)
            }
            else if (State == State.DataLoaded)
            {
                // Auto-Detecci├│n de Instrumento (Mini ES vs Micro MES)
                isMicroInstrument = Instrument.MasterInstrument.Name.Contains("MES");
                instrumentMultiplier = isMicroInstrument ? 0.10 : 1.0;

                // Si es Micro MES, escalamos proporcionalmente los umbrales en d├│lares por defecto
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
                    TrancaBonusDollars    = 10.0;
                    TrancaSafetyDollars   = 1.0;
                    MinTrancaDistanceDollars = 12.5;
                    TrancaConquestBufferDollars = 5.0;
                    TrancaAnchorDollars = 2.0;
                    currentDailyFloor     = -DailyLossLimit;
                    Print(string.Format("=== [PAS] INSTRUMENTO MICRO (MES) DETECTADO === Multiplicador 0.10x aplicado. Cortacircuito: -${0:F2} | Escal├│n 1: +${1:F2} (Piso +${2:F2}) | Escal├│n 2: +${3:F2} (Piso +${4:F2}) | Modo Dulce Trailing: +${5:F2} (Giveback ${6:F2})",
                        DailyLossLimit, DailyGoal_Piso1, DailyFloor_Piso1, DailyGoal_Piso2, DailyFloor_Piso2, DailyGoal_Piso3, DailyTrailingGiveback));
                }
                else
                {
                    currentDailyFloor = -DailyLossLimit;
                    Print(string.Format("=== [PAS] INSTRUMENTO EST├üNDAR (ES) DETECTADO === Cortacircuito: -${0:F2} | Escal├│n 1: +${1:F2} (Piso +${2:F2}) | Escal├│n 2: +${3:F2} (Piso +${4:F2}) | Modo Dulce Trailing: +${5:F2} (Giveback ${6:F2})",
                        DailyLossLimit, DailyGoal_Piso1, DailyFloor_Piso1, DailyGoal_Piso2, DailyFloor_Piso2, DailyGoal_Piso3, DailyTrailingGiveback));
                }

                // Instanciamos indicadores en memoria
                tema9        = TEMA(Close, TemaPeriod);
                ema200       = EMA(Close, EmaPeriod);
                keltnerBig   = KeltnerChannel(Close, KeltnerBigMult, KeltnerPeriod);
                keltnerSmall = KeltnerChannel(Close, KeltnerSmallMult, KeltnerPeriod);
                atr14        = ATR(14);

                // En modo Backtest o Strategy Analyzer, se activa autom├íticamente
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

            // 1. ACTUALIZACI├ôN DE CONTADORES DE MERCADO (HIST├ôRICO Y TIEMPO REAL)
            // Es vital que los contadores de acumulaci├│n corran en CADA barra (incluso si est├í pausado
            // o fuera de horario) para que la memoria matem├ítica de 100/150 barras nunca se pierda.
            UpdateMarketCounters();

            // 2. VERIFICACI├ôN DE NUEVA SESI├ôN DIARIA (RESET DE GESTI├ôN DE RIESGO)
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
                
                // Reseteo estricto de memoria acumulativa para evitar arrastres de d├¡as previos o pre-mercado
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

                Print(string.Format("==================== NUEVA SESI├ôN RTH ({0:MM/dd/yyyy}) ==================== PnL Diario: $0.00 | Piso Inicial: -${1:F2}",
                    Time[0], DailyLossLimit));
            }

            // 2.1 SINCRONIZACI├ôN CONTINUA DE TRADES, PNL Y PISOS PROTEGIDOS EN CADA BARRA
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
                    Print(Time[0] + " - [CIERRE FORZOSO] Posiciones liquidadas por fin de sesi├│n RTH.");
                }
                ResetAllTradeCounters();
                return;
            }

            // 3.1 CORTACIRCUITO UNIVERSAL A LAS 12:30 PM (SOLO ESCAL├ôN 3 / MODO DULCE PUEDE CONTINUAR HASTA LAS 4:00 PM)
            // Si son las 12:30 PM EST y no estamos en el ├║ltimo escal├│n (Modo Dulce Trailing Infinito >= $900),
            // se apaga la operativa para evitar operar en la tarde picada o eventos de alta volatilidad (FED/FOMC/Almuerzo) y blindar las ganancias.
            if (UseDailyManagement && UseTier1LunchCutoff && !isDailyLocked && timeNow >= Tier1CutoffTime)
            {
                if (currentDailyTier < 3)
                {
                    if (Position.MarketPosition == MarketPosition.Flat)
                    {
                        isDailyLocked = true;
                        dailyLockReason = string.Format("CORTE DE OPERATIVA A LAS 12:30 PM (PnL ${0:F2})", dailyRealizedPnL);
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [ESCUDO DIARIO | CORTE A LAS 12:30 PM EST] Hora actual ({1}) alcanz├│ las 12:30 PM.\n" +
                                           "PnL Diario Realizado: ${2:F2} USD (Zona {3}). ┬íAPAGANDO OPERATIVA POR EL RESTO DEL D├ìA!\n" +
                                           "No se regala dinero en la tarde sin volumen ni en trampas de la FED o almuerzo. ┬íD├¡a cerrado con disciplina!\n" +
                                           "****************************************************************************************",
                            Time[0], timeNow, dailyRealizedPnL, currentDailyTier == 2 ? "Escal├│n 2" : (currentDailyTier == 1 ? "Escal├│n 1" : "Base")));
                        return;
                    }
                }
            }

            // 4. ACTUALIZACI├ôN DE MFE / MAE CONTINUO
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (High[0] > highestPriceSinceEntry) highestPriceSinceEntry = High[0];
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (Low[0] < lowestPriceSinceEntry) lowestPriceSinceEntry = Low[0];
            }

            // 5. CORTACIRCUITO DIARIO EN VIVO ("PUM, ADI├ôS") Y PISO EXACTO POR TICKS
            if (UseDailyManagement)
            {
                double unrealizedPnL = 0.0;
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    unrealizedPnL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
                }
                double totalDailyFloatingPnL = dailyRealizedPnL + unrealizedPnL;

                // CORTACIRCUITO DE PISO EXACTO EN TIEMPO REAL:
                // Si el piso protegido est├í activo (Escal├│n 1 o 2: currentDailyFloor > 0) y el PnL cerrado lo supera, calculamos el precio
                // exacto en ticks donde el retroceso alcanzar├¡a el piso, para liquidar sin esperar slippage de barra.
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
                                                    "{0} - [ESCUDO DIARIO | PISO EXACTO ALCANZADO] Precio toc├│ {1:F2} (L├¡mite {2} ticks desde entrada {3:F2}).\n" +
                                                    "┬íPUM, ADI├ôS! Liquidando a mercado en el tick exacto para asegurar Piso de ${4:F2} USD.\n" +
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
                                                    "{0} - [ESCUDO DIARIO | PISO EXACTO ALCANZADO] Precio toc├│ {1:F2} (L├¡mite {2} ticks desde entrada {3:F2}).\n" +
                                                    "┬íPUM, ADI├ôS! Liquidando a mercado en el tick exacto para asegurar Piso de ${4:F2} USD.\n" +
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
                        currentDailyTier == 0 ? "P├ëRDIDA M├üXIMA" : ("PISO ESCAL├ôN " + currentDailyTier), currentDailyFloor);
                    
                    if (currentDailyTier == 0)
                    {
                        Print(string.Format("****************************************************************************************\n" +
                                            "{0} - [CORTACIRCUITO DE SEGURIDAD | P├ëRDIDA M├üXIMA ALCANZADA]\n" +
                                            "┬íAPAGUE ESA VAINA! Ya perdiste el l├¡mite del d├¡a (-${1:F2} USD). Agradezca que no te hice perder m├ís.\n" +
                                            "Olv├¡date de esa p├⌐rdida que fueron centavos, ma├▒ana ser├í otro d├¡a.\n" +
                                            "┬íAdi├│s pues, que no te veo apagando ese PC! ≡ƒ¢æ≡ƒöÆ\n" +
                                            "****************************************************************************************",
                            Time[0], Math.Abs(totalDailyFloatingPnL)));
                    }
                    else
                    {
                        Print(string.Format("****************************************************************************************\n" +
                                            "{0} - [ESCUDO DIARIO | PISO DE GANANCIAS ALCANZADO]\n" +
                                            "┬íAPAGUE Y V├üMONOS! Te fuiste con ${1:F2} USD en el bolsillo, ┬ívete a montar cicla! ≡ƒÜ┤ΓÇìΓÖé∩╕Å≡ƒÆ¿\n" +
                                            "┬┐Qu├⌐ haces pegado a ese PC todo el d├¡a si ya ganaste? ┬íPara qu├⌐ m├ís!\n" +
                                            "Liquidando a mercado inmediatamente. Operativa bloqueada por hoy con la plata a salvo.\n" +
                                            "****************************************************************************************",
                            Time[0], totalDailyFloatingPnL));
                    }
                    
                    if (Position.MarketPosition != MarketPosition.Flat)
                        LiquidateAllMarket("Cortacircuito_Diario_Piso");
                    return;
                }

                // Si la estrategia ya est├í bloqueada por el d├¡a, no evaluar m├ís entradas
                if (isDailyLocked) return;
            }

            // 6. GESTI├ôN DE SALIDAS DIN├üMICAS Y ESCALONAMIENTO
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                ManageDynamicExits();
            }

            // 7. EVALUACI├ôN DE ENTRADAS DE LOS 4 ASES (SOLO SI BOT├ôN ACTIVO, FLAT Y EN HORARIO V├üLIDO)
            if (isStrategyActive && Position.MarketPosition == MarketPosition.Flat && isTradingTime && !isDailyLocked)
            {
                // Reseteo estructural estricto del ciclo de Cascada (SOLO cuando el precio descansa f├¡sicamente en la L├¡nea Media)
                if (cascadaCycleLongFired && Low[0] <= keltnerBig.Midline[0])
                {
                    cascadaCycleLongFired = false;
                }
                if (cascadaCycleShortFired && High[0] >= keltnerBig.Midline[0])
                {
                    cascadaCycleShortFired = false;
                }

                // GESTI├ôN DE RIESGO INTELIGENTE: Verificamos si existe margen para operar al menos 1 contrato con Stop saludable
                if (GetRiskAdjustedContracts() <= 0)
                {
                    // No hay margen suficiente ni para 1 contrato sin arriesgar el piso; protegemos el capital
                    return;
                }

                // AS 1: Francotirador Zona 1 (100 Barras)
                EvaluateAs1_Zona1();

                // Si As 1 no dispar├│, evaluar AS 2: SOBB (SurferOutBigBanda)
                if (Position.MarketPosition == MarketPosition.Flat)
                    EvaluateAs2_SOBB();

                // Si As 2 no dispar├│, evaluar AS 3: Ratchet (Chicle Parab├│lico)
                if (Position.MarketPosition == MarketPosition.Flat)
                    EvaluateAs3_Ratchet();

                // Si As 3 no dispar├│, evaluar AS 4: Cascada 50 Barras
                if (Position.MarketPosition == MarketPosition.Flat)
                    EvaluateAs4_Cascada50();
            }
        }
        #endregion

        #region Actualizaci├│n de Contadores de Mercado
        private void UpdateMarketCounters()
        {
            int timeNow = ToTime(Time[0]);

            // Inicializaci├│n de seguridad para lastTemaSide en la primera barra v├ílida
            if (lastTemaSide == 0)
            {
                lastTemaSide = (tema9[1] <= ema200[1]) ? -1 : 1;
            }

            // === CONTADOR AS 1: ZONA 1 (GOLAZO SINCRONIZADO: ACUMULACI├ôN 100 BARRAS + CRON├ôMETRO 40 BARRAS) ===
            double currentUpTicks   = (tema9[0] - ema200[0]) / TickSize;
            double currentDownTicks = (ema200[0] - tema9[0]) / TickSize;

            double upperBig   = keltnerBig.Upper[0];
            double lowerBig   = keltnerBig.Lower[0];
            double upperSmall = keltnerSmall.Upper[0];
            double lowerSmall = keltnerSmall.Lower[0];
            double midline    = keltnerBig.Midline[0];

            // =========================================================================
            // A) MONITOREO DE ACUMULACI├ôN Y CRON├ôMETRO 40 BARRAS PARA LARGOS
            // =========================================================================
            // 1. Acumulaci├│n de 100 barras: La l├¡nea azul (TEMA 9) por debajo de la l├¡nea roja (EMA 200) sin cruzar
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

            // 2. Activaci├│n del Cron├│metro de 40 Barras:
            // Al darse el cruce alcista (TEMA 9 cruza hacia arriba de EMA 200) tras haber acumulado >= 100 barras
            if (z1LongArmed && !z1WaitingForMidlineLong && tema9[0] > ema200[0])
            {
                z1WaitingForMidlineLong = true;
                z1MidlineCountdownLong = 0;
                z1LongArmed = false;
                barsBelowEmaWithoutCross = 0;
                Print(string.Format("{0} - [PAS - Z1-100B | CRUCE ALCISTA DETECTADO] TEMA 9 ({1:F2}) cruz├│ hacia arriba de EMA 200 ({2:F2}) tras acumular 100 barras. ┬íCRON├ôMETRO DE 40 BARRAS ACTIVADO buscando toque de L├¡nea Media!",
                    Time[0], tema9[0], ema200[0]));
            }

            // 3. Monitoreo durante las 40 Barras de Largo:
            if (z1WaitingForMidlineLong)
            {
                z1MidlineCountdownLong++;

                // A) Si supera la ventana m├íxima de 40 barras: expira
                if (z1MidlineCountdownLong > Z1MaxBarsToMidline)
                {
                    z1WaitingForMidlineLong = false;
                    z1MidlineCountdownLong = 0;
                    Print(string.Format("{0} - [PAS - Z1-100B | LARGO EXPIRADO] Se cumplieron {1} barras tras el cruce sin que EMA 200 tocara la L├¡nea Media. Setup cancelado.",
                        Time[0], Z1MaxBarsToMidline));
                }
                // B) Si la TEMA 9 hace un gancho falso real y penetra por debajo de la EMA 200 por >= 1 tick (0.25 pts):
                else if ((ema200[0] - tema9[0]) >= (1.0 * TickSize))
                {
                    z1WaitingForMidlineLong = false;
                    z1MidlineCountdownLong = 0;
                    Print(string.Format("{0} - [PAS - Z1-100B | LARGO ABORTADO] Gancho falso real: TEMA 9 ({1:F2}) penetr├│ {2:F2} ticks por debajo de EMA 200 ({3:F2}) durante la espera. Cron├│metro cancelado.",
                        Time[0], tema9[0], (ema200[0] - tema9[0]) / TickSize, ema200[0]));
                }
            }

            // =========================================================================
            // B) MONITOREO DE ACUMULACI├ôN Y CRON├ôMETRO 40 BARRAS PARA CORTOS
            // =========================================================================
            // 1. Acumulaci├│n de 100 barras: La l├¡nea azul (TEMA 9) por encima de la l├¡nea roja (EMA 200) sin cruzar
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

            // 2. Activaci├│n del Cron├│metro de 40 Barras para Cortos:
            if (z1ShortArmed && !z1WaitingForMidlineShort && tema9[0] < ema200[0])
            {
                z1WaitingForMidlineShort = true;
                z1MidlineCountdownShort = 0;
                z1ShortArmed = false;
                barsAboveEmaWithoutCross = 0;
                Print(string.Format("{0} - [PAS - Z1-100B | CRUCE BAJISTA DETECTADO] TEMA 9 ({1:F2}) cruz├│ hacia abajo de EMA 200 ({2:F2}) tras acumular 100 barras. ┬íCRON├ôMETRO DE 40 BARRAS ACTIVADO buscando toque de L├¡nea Media!",
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
                    Print(string.Format("{0} - [PAS - Z1-100B | CORTO EXPIRADO] Se cumplieron {1} barras tras el cruce sin que EMA 200 tocara la L├¡nea Media. Setup cancelado.",
                        Time[0], Z1MaxBarsToMidline));
                }
                // B) Si la TEMA 9 hace un gancho falso real y penetra por encima de la EMA 200 por >= 1 tick (0.25 pts):
                else if ((tema9[0] - ema200[0]) >= (1.0 * TickSize))
                {
                    z1WaitingForMidlineShort = false;
                    z1MidlineCountdownShort = 0;
                    Print(string.Format("{0} - [PAS - Z1-100B | CORTO ABORTADO] Gancho falso real: TEMA 9 ({1:F2}) penetr├│ {2:F2} ticks por encima de EMA 200 ({3:F2}) durante la espera. Cron├│metro cancelado.",
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

            // === CONTADOR AS 4: CASCADA (Separaci├│n de EMA 200 > 4 ticks de Banda 3.5 - Silencioso) ===
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

        #region C├ílculo de Pendiente Normalizada Institucional (LinReg 5 Barras / ATR 14)
        /// <summary>
        /// Calcula la pendiente de regresi├│n lineal por m├¡nimos cuadrados (OLS) sobre 5 barras
        /// y la normaliza dividi├⌐ndola por el ATR(14), obteniendo un ratio adimensional de aceleraci├│n/inclinaci├│n.
        /// F├│rmula cerrada OLS 5 barras: m = (2*Y[0] + 1*Y[1] - 1*Y[3] - 2*Y[4]) / 10
        /// Pendiente Normalizada = m / ATR(14)
        /// </summary>
        private double GetNormalizedSlope(ISeries<double> series)
        {
            if (CurrentBar < 15 || atr14 == null) return 0.0;
            
            double atr = atr14[0];
            if (atr <= 0.0001) return 0.0;

            // Regresi├│n lineal exacta de 5 barras (pesos centrados: x in {-2, -1, 0, 1, 2})
            double linRegSlope = ((2.0 * series[0]) + (1.0 * series[1]) - (1.0 * series[3]) - (2.0 * series[4])) / 10.0;

            // Normalizaci├│n por volatilidad
            return linRegSlope / atr;
        }
        #endregion

        #region M├│dulos de Entrada de los 4 Ases

        // =========================================================================
        // AS 1: FRANCOTIRADOR ZONA 1 (GOLAZO SINCRONIZADO: EMA200 A L├ìNEA MEDIA EN 40 BARRAS)
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
            // AS 1 - GOLAZO LARGO: Toque de L├¡nea Media durante ventana de 40 barras
            // =========================================================================
            if (z1WaitingForMidlineLong)
            {
                // 1. Toque o cruce de EMA 200 a la L├¡nea Media:
                // La EMA 200 ven├¡a descendiendo y ahora toca o cruza la L├¡nea Media
                bool emaTocoMidline = (ema200[0] <= midline) || (Math.Abs(ema200[0] - midline) <= (1.0 * TickSize));

                if (emaTocoMidline)
                {
                    // 2. Pendiente Normalizada positiva de la L├¡nea Media (m/ATR >= Zona1SlopeThreshold)
                    bool pendienteMidlineAlcista = normSlope >= Zona1SlopeThreshold;

                    // 3. Ubicaci├│n del precio: Zona 1 o Zona 0 moderada (no m├ís de 10 ticks sobre Upper 3.5)
                    bool precioEnZona = (Close[0] >= upperSmall) && (Close[0] <= upperBig + (Z1MaxZone0Ticks * TickSize));

                    // 4. Confirmaci├│n de TEMA 9 (azul) sin gancho falso bajo EMA 200 (tolerancia 1 tick)
                    bool confirmacionAlcista = (ema200[0] - tema9[0]) < (1.0 * TickSize);

                    if (pendienteMidlineAlcista && precioEnZona && confirmacionAlcista)
                    {
                        ExecuteAceEntry(AceType.As1_Zona1_100Bar, MarketPosition.Long, "Z1-100Bar_Long");
                        Print(string.Format("{0} - [PAS - Z1-100Bar v10.2 | ┬íGOLAZO LARGO EJECUTADO!] Entrada @ ~{1:F2} en barra {2}/40 | Pendiente Normalizada: +{3:F2} (M├¡nimo Z1: +{4:F2}) | ATR: {5:F2} pts | TEMA9 ({6:F2}) > EMA200 ({7:F2})",
                            Time[0], Close[0], z1MidlineCountdownLong, normSlope, Zona1SlopeThreshold, atr14 != null ? atr14[0] : 0.0, tema9[0], ema200[0]));
                    }
                    else
                    {
                        Print(string.Format("{0} - [PAS - Z1-100B v10.2 | ┬íPAILAS MI PEZ!] Cruce Midline en barra {1}/40 descartado: Pendiente en +{2:F2} (Requerido Z1: >={3:F2}, ZonaOK: {4}, ConfOK: {5}) | ATR: {6:F2} pts. ┬íMercado plano sin fuerza, no regalamos ni un d├│lar!",
                            Time[0], z1MidlineCountdownLong, normSlope, Zona1SlopeThreshold, precioEnZona, confirmacionAlcista, atr14 != null ? atr14[0] : 0.0));
                    }

                    // REGLA DE ORO DE PRECISI├ôN: El cruce ya ocurri├│. Se resetea inmediatamente el cron├│metro
                    // tanto si dispar├│ como si se descart├│, eliminando cualquier posibilidad de entradas tard├¡as.
                    z1WaitingForMidlineLong = false;
                    z1MidlineCountdownLong = 0;
                    return;
                }
            }

            // =========================================================================
            // AS 1 - GOLAZO CORTO: Toque de L├¡nea Media durante ventana de 40 barras
            // =========================================================================
            if (z1WaitingForMidlineShort)
            {
                // 1. Toque o cruce de EMA 200 a la L├¡nea Media:
                // La EMA 200 ven├¡a ascendiendo y ahora toca o cruza la L├¡nea Media
                bool emaTocoMidline = (ema200[0] >= midline) || (Math.Abs(ema200[0] - midline) <= (1.0 * TickSize));

                if (emaTocoMidline)
                {
                    // 2. Pendiente Normalizada negativa de la L├¡nea Media (m/ATR <= -Zona1SlopeThreshold)
                    bool pendienteMidlineBajista = normSlope <= -Zona1SlopeThreshold;

                    // 3. Ubicaci├│n del precio: Zona 4 o Zona 5 moderada (no m├ís de 10 ticks bajo Lower 3.5)
                    bool precioEnZona = (Close[0] <= lowerSmall) && (Close[0] >= lowerBig - (Z1MaxZone0Ticks * TickSize));

                    // 4. Confirmaci├│n de TEMA 9 (azul) sin gancho falso sobre EMA 200 (tolerancia 1 tick)
                    bool confirmacionBajista = (tema9[0] - ema200[0]) < (1.0 * TickSize);

                    if (pendienteMidlineBajista && precioEnZona && confirmacionBajista)
                    {
                        ExecuteAceEntry(AceType.As1_Zona1_100Bar, MarketPosition.Short, "Z1-100Bar_Short");
                        Print(string.Format("{0} - [PAS - Z1-100Bar v10.2 | ┬íGOLAZO CORTO EJECUTADO!] Entrada @ ~{1:F2} en barra {2}/40 | Pendiente Normalizada: {3:F2} (M├¡nimo Z1: -{4:F2}) | ATR: {5:F2} pts | TEMA9 ({6:F2}) < EMA200 ({7:F2})",
                            Time[0], Close[0], z1MidlineCountdownShort, normSlope, Zona1SlopeThreshold, atr14 != null ? atr14[0] : 0.0, tema9[0], ema200[0]));
                    }
                    else
                    {
                        Print(string.Format("{0} - [PAS - Z1-100B v10.2 | ┬íPAILAS MI PEZ!] Cruce Midline en barra {1}/40 descartado: Pendiente en {2:F2} (Requerido Z1: <={3:F2}, ZonaOK: {4}, ConfOK: {5}) | ATR: {6:F2} pts. ┬íMercado plano sin fuerza, no regalamos ni un d├│lar!",
                            Time[0], z1MidlineCountdownShort, normSlope, -Zona1SlopeThreshold, precioEnZona, confirmacionBajista, atr14 != null ? atr14[0] : 0.0));
                    }

                    // REGLA DE ORO DE PRECISI├ôN: El cruce ya ocurri├│. Se resetea inmediatamente el cron├│metro
                    // tanto si dispar├│ como si se descart├│, eliminando cualquier posibilidad de entradas tard├¡as.
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
            // SOBB LARGO: Cruce de TEMA 9 hacia afuera + Confirmaci├│n de Barras Consecutivas (v9.0)
            // 1. Armado: TEMA 9 cruza por primera vez hacia afuera de Upper 3.5.
            // 2. Desarmado por repliegue: Si TEMA 9 regresa adentro (<= Upper 3.5), se desactiva.
            // 3. Conteo de Barras Flotando Afuera (Low > Upper 3.5):
            //    - Barra 1 Afuera: Inicializa contador = 1.
            //    - Barra 2 Afuera: Si High[0] > High[1] (rompe techo anterior y contin├║a el empuje alcista)
            //      y se cumplen las 2 capas (EMA en Zona 3 + Pendientes Normalizadas alineadas >= SobbSlopeThreshold) -> ┬íDISPARO LARGO!
            //    - Si High[0] <= High[1] (vela frenada/peque├▒a): Espera a la Barra 3 afuera.
            //    - Barra 3 Afuera: Si High[0] > High[1] y capas OK -> ┬íDISPARO LARGO!
            //    - Si se pasa de SobbMaxFloatingBarsWindow (3 barras) sin romper techo o si regresa adentro -> ┬íPAILAS!
            // =========================================================================
            bool isTemaCruzoSalidaLargo = (tema9[1] <= keltnerBig.Upper[1] && tema9[0] > upperBig);
            if (isTemaCruzoSalidaLargo)
            {
                sobbArmedLong = true;
                sobbHasFloatedLong = false;
                sobbFloatingBarsCountLong = 0;
                Print(string.Format("{0} - [PAS - SOBB v10.0 | LARGO ARMADO] TEMA 9 ({1:F2}) cruz├│ hacia afuera de Upper 3.5 ({2:F2}). Espera de {3} barras consecutivas afuera con rompimiento alcista.",
                    Time[0], tema9[0], upperBig, SobbRequiredFloatingBars));
            }
            else if (tema9[0] <= upperBig)
            {
                if (sobbArmedLong)
                {
                    sobbArmedLong = false;
                    sobbHasFloatedLong = false;
                    sobbFloatingBarsCountLong = 0;
                    Print(string.Format("{0} - [PAS - SOBB v10.0 | LARGO DESARMADO] TEMA 9 ({1:F2}) regres├│ dentro del canal sin haber disparado. Espera cancelada.",
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
                    bool isPendientesLargo = (normMidSlope >= SobbSlopeThreshold && normUpperSlope >= SobbSlopeThreshold);

                    // CAPA 1: EMA 200 en Zona 3 (Contexto Macro Estricto e Inmediato)
                    if (!isEmaZonaLargo)
                    {
                        sobbArmedLong = false;
                        sobbHasFloatedLong = false;
                        sobbFloatingBarsCountLong = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.2 | ┬íPAILAS MI PEZ!] Largo descartado: Vela flotando #{1} afuera pero EMA 200 ({2:F2}) fuera de Zona 3 ([{3:F2}, {4:F2}]). ┬íApague y v├ímonos!",
                            Time[0], sobbFloatingBarsCountLong, ema200[0], lowerSmall, midline));
                    }
                    // REGLA DE ORO v9.0: Evaluaci├│n de Barras Consecutivas y Rompimiento de Techo (Higher High)
                    else if (sobbFloatingBarsCountLong >= SobbRequiredFloatingBars && sobbFloatingBarsCountLong <= SobbMaxFloatingBarsWindow)
                    {
                        // Si la barra actual supera el techo de la barra anterior (empuje alcista activo)
                        bool rompioTechoAnterior = (High[0] > High[1]);

                        if (rompioTechoAnterior && isPendientesLargo)
                        {
                            // ┬íConfirmaci├│n estructural completa de 2 o 3 barras consecutivas con pendiente institucional!
                            sobbArmedLong = false;
                            sobbHasFloatedLong = false;
                            sobbFloatingBarsCountLong = 0;
                            ExecuteAceEntry(AceType.As2_SOBB, MarketPosition.Long, "SOBB_Long");
                            Print(string.Format("{0} - [PAS - SOBB v10.2 | ┬íPUM, ADI├ôS! QU├ë GROSER├ìA DE TRADE] ┬íLargo disparado @ ~{1:F2}! {2} barras consecutivas afuera con High ({3:F2}) > High[1] ({4:F2}) | Pendiente Mid: +{5:F2}, Upper: +{6:F2} (M├¡nimo SOBB: +{7:F2}) | ATR: {8:F2} pts.",
                                Time[0], Close[0], sobbFloatingBarsCountLong, High[0], High[1], normMidSlope, normUpperSlope, SobbSlopeThreshold, atr14 != null ? atr14[0] : 0.0));
                            return;
                        }
                        else if (!rompioTechoAnterior)
                        {
                            Print(string.Format("{0} - [PAS - SOBB v10.2 | LARGO FRENADO] Barra #{1} afuera pero techo High ({2:F2}) no super├│ High[1] ({3:F2}). Esperando impulso en siguiente barra...",
                                Time[0], sobbFloatingBarsCountLong, High[0], High[1]));
                        }
                        else
                        {
                            Print(string.Format("{0} - [PAS - SOBB v10.2 | ┬íPAILAS MI PEZ!] Largo en espera de pendiente: Midline en +{1:F2}, Upper en +{2:F2} vs Umbral SOBB requerido: +{3:F2} | ATR: {4:F2} pts. Mercado plano/ruido, ┬íno arriesgamos!",
                                Time[0], normMidSlope, normUpperSlope, SobbSlopeThreshold, atr14 != null ? atr14[0] : 0.0));
                        }
                    }
                    else if (sobbFloatingBarsCountLong > SobbMaxFloatingBarsWindow)
                    {
                        // Super├│ la ventana de 3 barras consecutivas sin confirmar rompimiento
                        sobbArmedLong = false;
                        sobbHasFloatedLong = false;
                        sobbFloatingBarsCountLong = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.2 | ┬íPAILAS!] Pasaron {1} barras afuera sin rompimiento alcista con pendiente. Setup cancelado.",
                            Time[0], SobbMaxFloatingBarsWindow));
                    }
                    else
                    {
                        // Barra 1 afuera: guardando posici├│n y esperando barra 2
                        Print(string.Format("{0} - [PAS - SOBB v10.0 | BARRA 1 AFUERA] Primera vela 100% afuera (Low {1:F2} > Upper {2:F2}). Esperando Barra 2 consecutiva...",
                            Time[0], Low[0], upperBig));
                    }
                }
                else
                {
                    // La vela ya no est├í 100% afuera (se meti├│ adentro o toc├│ la banda) -> ┬íPAILAS!
                    if (sobbHasFloatedLong || sobbFloatingBarsCountLong > 0)
                    {
                        sobbArmedLong = false;
                        sobbHasFloatedLong = false;
                        sobbFloatingBarsCountLong = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.0 | LARGO DESARMADO POR REPLIEGUE] El precio regres├│ adentro de Upper 3.5 ({1:F2}) tras {2} barra(s) afuera. ┬íPailas! Setup cancelado.",
                            Time[0], upperBig, sobbFloatingBarsCountLong));
                    }
                }
            }

            // =========================================================================
            // SOBB CORTO: Cruce de TEMA 9 hacia afuera + Confirmaci├│n de Barras Consecutivas (v9.0)
            // 1. Armado: TEMA 9 cruza por primera vez hacia afuera de Lower 3.5.
            // 2. Desarmado por repliegue: Si TEMA 9 regresa adentro (>= Lower 3.5), se desactiva.
            // 3. Conteo de Barras Flotando Afuera (High < Lower 3.5):
            //    - Barra 1 Afuera: Inicializa contador = 1.
            //    - Barra 2 Afuera: Si Low[0] < Low[1] (rompe piso anterior y contin├║a el empuje bajista)
            //      y se cumplen las 2 capas (EMA en Zona 3 + Pendientes Normalizadas alineadas <= -SobbSlopeThreshold) -> ┬íDISPARO CORTO!
            //    - Si Low[0] >= Low[1] (vela frenada/peque├▒a): Espera a la Barra 3 afuera.
            //    - Barra 3 Afuera: Si Low[0] < Low[1] y capas OK -> ┬íDISPARO CORTO!
            //    - Si se pasa de SobbMaxFloatingBarsWindow (3 barras) sin romper piso o si regresa adentro -> ┬íPAILAS!
            // =========================================================================
            bool isTemaCruzoSalidaCorto = (tema9[1] >= keltnerBig.Lower[1] && tema9[0] < lowerBig);
            if (isTemaCruzoSalidaCorto)
            {
                sobbArmedShort = true;
                sobbHasFloatedShort = false;
                sobbFloatingBarsCountShort = 0;
                Print(string.Format("{0} - [PAS - SOBB v10.0 | CORTO ARMADO] TEMA 9 ({1:F2}) cruz├│ hacia afuera de Lower 3.5 ({2:F2}). Espera de {3} barras consecutivas afuera con rompimiento bajista.",
                    Time[0], tema9[0], lowerBig, SobbRequiredFloatingBars));
            }
            else if (tema9[0] >= lowerBig)
            {
                if (sobbArmedShort)
                {
                    sobbArmedShort = false;
                    sobbHasFloatedShort = false;
                    sobbFloatingBarsCountShort = 0;
                    Print(string.Format("{0} - [PAS - SOBB v10.0 | CORTO DESARMADO] TEMA 9 ({1:F2}) regres├│ dentro del canal sin haber disparado. Espera cancelada.",
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
                    bool isPendientesCorto = (normMidSlope <= -SobbSlopeThreshold && normLowerSlope <= -SobbSlopeThreshold);

                    // CAPA 1: EMA 200 en Zona 3 (Contexto Macro Estricto e Inmediato)
                    if (!isEmaZonaCorto)
                    {
                        sobbArmedShort = false;
                        sobbHasFloatedShort = false;
                        sobbFloatingBarsCountShort = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.2 | ┬íPAILAS MI PEZ!] Corto descartado: Vela flotando #{1} afuera pero EMA 200 ({2:F2}) fuera de Zona 3 ([{3:F2}, {4:F2}]). ┬íApague y v├ímonos!",
                            Time[0], sobbFloatingBarsCountShort, ema200[0], midline, upperSmall));
                    }
                    // REGLA DE ORO v9.0: Evaluaci├│n de Barras Consecutivas y Rompimiento de Suelo (Lower Low)
                    else if (sobbFloatingBarsCountShort >= SobbRequiredFloatingBars && sobbFloatingBarsCountShort <= SobbMaxFloatingBarsWindow)
                    {
                        // Si la barra actual perfora el suelo de la barra anterior (empuje bajista activo)
                        bool rompioSueloAnterior = (Low[0] < Low[1]);

                        if (rompioSueloAnterior && isPendientesCorto)
                        {
                            // ┬íConfirmaci├│n estructural completa de 2 o 3 barras consecutivas con pendiente institucional!
                            sobbArmedShort = false;
                            sobbHasFloatedShort = false;
                            sobbFloatingBarsCountShort = 0;
                            ExecuteAceEntry(AceType.As2_SOBB, MarketPosition.Short, "SOBB_Short");
                            Print(string.Format("{0} - [PAS - SOBB v10.2 | ┬íPUM, ADI├ôS! QU├ë GROSER├ìA DE TRADE] ┬íCorto disparado @ ~{1:F2}! {2} barras consecutivas afuera con Low ({3:F2}) < Low[1] ({4:F2}) | Pendiente Mid: {5:F2}, Lower: {6:F2} (M├¡nimo SOBB: -{7:F2}) | ATR: {8:F2} pts.",
                                Time[0], Close[0], sobbFloatingBarsCountShort, Low[0], Low[1], normMidSlope, normLowerSlope, SobbSlopeThreshold, atr14 != null ? atr14[0] : 0.0));
                            return;
                        }
                        else if (!rompioSueloAnterior)
                        {
                            Print(string.Format("{0} - [PAS - SOBB v10.2 | CORTO FRENADO] Barra #{1} afuera pero suelo Low ({2:F2}) no perfor├│ Low[1] ({3:F2}). Esperando impulso en siguiente barra...",
                                Time[0], sobbFloatingBarsCountShort, Low[0], Low[1]));
                        }
                        else
                        {
                            Print(string.Format("{0} - [PAS - SOBB v10.2 | ┬íPAILAS MI PEZ!] Corto en espera de pendiente: Midline en {1:F2}, Lower en {2:F2} vs Umbral SOBB requerido: -{3:F2} | ATR: {4:F2} pts. Mercado plano/ruido, ┬íno arriesgamos!",
                                Time[0], normMidSlope, normLowerSlope, SobbSlopeThreshold, atr14 != null ? atr14[0] : 0.0));
                        }
                    }
                    else if (sobbFloatingBarsCountShort > SobbMaxFloatingBarsWindow)
                    {
                        // Super├│ la ventana de 3 barras consecutivas sin confirmar rompimiento
                        sobbArmedShort = false;
                        sobbHasFloatedShort = false;
                        sobbFloatingBarsCountShort = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.2 | ┬íPAILAS!] Pasaron {1} barras afuera sin rompimiento bajista con pendiente. Setup cancelado.",
                            Time[0], SobbMaxFloatingBarsWindow));
                    }
                    else
                    {
                        // Barra 1 afuera: guardando posici├│n y esperando barra 2
                        Print(string.Format("{0} - [PAS - SOBB v10.0 | BARRA 1 AFUERA] Primera vela 100% afuera (High {1:F2} < Lower {2:F2}). Esperando Barra 2 consecutiva...",
                            Time[0], High[0], lowerBig));
                    }
                }
                else
                {
                    // La vela ya no est├í 100% afuera (se meti├│ adentro o toc├│ la banda) -> ┬íPAILAS!
                    if (sobbHasFloatedShort || sobbFloatingBarsCountShort > 0)
                    {
                        sobbArmedShort = false;
                        sobbHasFloatedShort = false;
                        sobbFloatingBarsCountShort = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.0 | CORTO DESARMADO POR REPLIEGUE] El precio regres├│ adentro de Lower 3.5 ({1:F2}) tras {2} barra(s) afuera. ┬íPailas! Setup cancelado.",
                            Time[0], lowerBig, sobbFloatingBarsCountShort));
                    }
                }
            }
        }

        // =========================================================================
        // AS 3: RATCHET (EL CHICLE PARAB├ôLICO ORIGINAL - TENDENCIA VERTICAL INSTITUCIONAL)
        // =========================================================================
        private void EvaluateAs3_Ratchet()
        {
            // A) Filtro Macro de Tendencia Limpia (├Ültimas 20 barras sin tocar EMA 200)
            bool emaShortValid = true;
            bool emaLongValid  = true;
            for (int i = 0; i <= RatchetLookbackBars; i++)
            {
                if (High[i] >= ema200[i]) emaShortValid = false; // Todo debajo para cortos
                if (Low[i]  <= ema200[i]) emaLongValid  = false; // Todo encima para largos
            }

            // B) REGLA DE ORO INSTITUCIONAL v6.0: EMA 200 ESTRICTAMENTE FUERA DEL CANAL GRANDE 3.5 & PENDIENTE MIDLINE
            // Ratchet est├í dise├▒ado EXCLUSIVAMENTE para tendencias verticales desbocadas.
            // Para Cortos: EMA 200 debe estar rezagada por ENCIMA de la Banda Superior 3.5 (keltnerBig.Upper) y Midline cayendo.
            // Para Largos: EMA 200 debe estar rezagada por DEBAJO de la Banda Inferior 3.5 (keltnerBig.Lower) y Midline subiendo.
            double midlineSlope = keltnerBig.Midline[0] - keltnerBig.Midline[2];
            bool emaOutsideBigBandShort = ema200[0] >= keltnerBig.Upper[0];
            bool emaOutsideBigBandLong  = ema200[0] <= keltnerBig.Lower[0];
            bool midlineFallingShort    = midlineSlope < -(0.5 * TickSize);
            bool midlineRisingLong      = midlineSlope > (0.5 * TickSize);

            // C) Detecci├│n de Toque o Penetraci├│n en Banda Keltner 3.5 en barras 1 a 3
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

            // Referencia fija para calcular si el TEMA enganch├│ o cruz├│ de regreso
            double upperBandTolCross = keltnerBig.Upper[1] - (RatchetTemaTolTicks * TickSize);
            double lowerBandTolCross = keltnerBig.Lower[1] + (RatchetTemaTolTicks * TickSize);

            // Alerta Bajista (preparando corto): Gancho en Banda Superior
            bool isPeakShort = tema9[2] < tema9[1] && tema9[0] < tema9[1];
            bool isCrossBackShort = tema9[1] >= upperBandTolCross && tema9[0] < upperBandTolCross && tema9[0] < tema9[1];

            if ((isPeakShort && touchedUpperExtreme) || isCrossBackShort)
            {
                if (!emaShortValid || !emaOutsideBigBandShort || !midlineFallingShort)
                {
                    // Descartado si no hay tendencia macro vertical o si EMA 200 est├í dentro del canal
                }
                else
                {
                    if (ratchetSetup == RatchetSetup.Short)
                    {
                        Print(Time[0] + " - [PAS - Ratchet] Nuevo gancho bajista en Banda Superior. Reloj reiniciado a 0.");
                    }
                    else
                    {
                        Print(string.Format("{0} - [PAS - Ratchet] ALERTA CORTO VERTICAL: Gancho TEMA en Banda Superior con EMA200 ({1:F2} >= {2:F2}) y Midline cayendo. Reloj de {3} barras armado buscando cruce de L├¡nea Media.",
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
                    // Descartado si no hay tendencia macro vertical o si EMA 200 est├í dentro del canal
                }
                else
                {
                    if (ratchetSetup == RatchetSetup.Long)
                    {
                        Print(Time[0] + " - [PAS - Ratchet] Nuevo gancho alcista en Banda Inferior. Reloj reiniciado a 0.");
                    }
                    else
                    {
                        Print(string.Format("{0} - [PAS - Ratchet] ALERTA LARGO VERTICAL: Gancho TEMA en Banda Inferior con EMA200 ({1:F2} <= {2:F2}) y Midline subiendo. Reloj de {3} barras armado buscando cruce de L├¡nea Media.",
                            Time[0], ema200[0], keltnerBig.Lower[0], RatchetCountdownBars));
                    }
                    ratchetSetup = RatchetSetup.Long;
                    ratchetSetupBarCounter = 0;
                }
            }

            // D) Conteo del Reloj y GATILLO CIRUJANO: Cruce de la L├¡nea Media (Midline)
            if (ratchetSetup != RatchetSetup.None)
            {
                ratchetSetupBarCounter++;
                if (ratchetSetupBarCounter > RatchetCountdownBars)
                {
                    Print(string.Format("{0} - [PAS - Ratchet] Setup cancelado: Pasaron {1} barras sin cruzar la L├¡nea Media.",
                        Time[0], RatchetCountdownBars));
                    ratchetSetup = RatchetSetup.None;
                }
                else
                {
                    // DISPARO LARGO: Cruce alcista de L├¡nea Media (Revalidando EMA 200 fuera y pendiente a favor)
                    if (ratchetSetup == RatchetSetup.Long && CrossAbove(tema9, keltnerBig.Midline, 1))
                    {
                        if (ema200[0] <= keltnerBig.Lower[0] && (keltnerBig.Midline[0] - keltnerBig.Midline[2]) > 0)
                        {
                            ExecuteAceEntry(AceType.As3_Ratchet, MarketPosition.Long, "Ratchet_Long");
                            ratchetSetup = RatchetSetup.None;
                            Print(string.Format("{0} - [PAS - Ratchet] ┬íDISPARO LARGO EN L├ìNEA MEDIA! TEMA 9 cruz├│ Midline ({1:F2}) con EMA200 ({2:F2} <= Lower 3.5 {3:F2}). Entrada @ ~{4:F2} | SL Tope 12tk armado.",
                                Time[0], keltnerBig.Midline[0], ema200[0], keltnerBig.Lower[0], Close[0]));
                            return;
                        }
                        else
                        {
                            Print(string.Format("{0} - [PAS - Ratchet | DISPARO LARGO ANULADO] Cruce en Midline detectado pero EMA 200 ({1:F2}) ingres├│ al canal o Midline fren├│ subida. Setup cancelado para evitar trampas.",
                                Time[0], ema200[0]));
                            ratchetSetup = RatchetSetup.None;
                        }
                    }
                    // DISPARO CORTO: Cruce bajista de L├¡nea Media (Revalidando EMA 200 fuera y pendiente a favor)
                    else if (ratchetSetup == RatchetSetup.Short && CrossBelow(tema9, keltnerBig.Midline, 1))
                    {
                        if (ema200[0] >= keltnerBig.Upper[0] && (keltnerBig.Midline[0] - keltnerBig.Midline[2]) < 0)
                        {
                            ExecuteAceEntry(AceType.As3_Ratchet, MarketPosition.Short, "Ratchet_Short");
                            ratchetSetup = RatchetSetup.None;
                            Print(string.Format("{0} - [PAS - Ratchet] ┬íDISPARO CORTO EN L├ìNEA MEDIA! TEMA 9 cruz├│ Midline ({1:F2}) con EMA200 ({2:F2} >= Upper 3.5 {3:F2}). Entrada @ ~{4:F2} | SL Tope 12tk armado.",
                                Time[0], keltnerBig.Midline[0], ema200[0], keltnerBig.Upper[0], Close[0]));
                            return;
                        }
                        else
                        {
                            Print(string.Format("{0} - [PAS - Ratchet | DISPARO CORTO ANULADO] Cruce en Midline detectado pero EMA 200 ({1:F2}) ingres├│ al canal o Midline fren├│ bajada. Setup cancelado para evitar trampas.",
                                Time[0], ema200[0]));
                            ratchetSetup = RatchetSetup.None;
                        }
                    }
                }
            }
        }

        // =========================================================================
        // AS 4: CASCADA (EL CAZADOR DE R├üPIDOS DE R├ìO - PENDIENTE NORMALIZADA + CANDADO $180)
        // =========================================================================
        private void EvaluateAs4_Cascada50()
        {
            // 1. FILTRO HORARIO EXCLUSIVO DE CASCADA: No opera antes de las 10:00 AM EST (Evita trampas de acomodaci├│n de pre-mercado)
            int timeNow = ToTime(Time[0]);
            if (timeNow < CascadaStartTime)
            {
                return;
            }

            // 2. FILTRO DE SACIEDAD: Si Cascada logr├│ una victoria previa (>= $180 USD), permanece en cuarentena de 60 min
            if (Time[0] < cascadaQuarantineEndTime)
            {
                return;
            }

            double midline    = keltnerBig.Midline[0];
            double upperSmall = keltnerSmall.Upper[0];
            double lowerSmall = keltnerSmall.Lower[0];
            double normSlope  = GetNormalizedSlope(keltnerBig.Midline);

            // =========================================================================
            // DISPARO CORTO: R├ípido de R├¡o Bajista
            // 1. Regla de 1 Solo Tiro: no haber disparado ya en este mismo impulso bajista
            // 2. Contexto Macro: EMA 200 por encima de la L├¡nea Media (keltnerBig.Midline)
            // 3. Ubicaci├│n: Precio en Zona 4 o 5 (Close <= lowerSmall)
            // 4. Aceleraci├│n: Pendiente Normalizada de L├¡nea Media <= -CascadaSlopeThreshold (-0.20)
            // 5. Vela con intenci├│n bajista: Close[0] < Open[0]
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
                        Print(string.Format("{0} - [PAS - Cascada v10.2 | ┬íPUM, ADI├ôS! R├üPIDO CORTO DISPARADO] Entrada @ ~{1:F2} en Zona 4/5 | Pendiente Normalizada: {2:F2} (M├¡nimo Cascada: -{3:F2}) | ATR(14): {4:F2} pts | SL inicial en L├¡nea Media ({5:F2}). ┬íQu├⌐ groser├¡a de trade!",
                            Time[0], Close[0], normSlope, CascadaSlopeThreshold, atr14 != null ? atr14[0] : 0.0, midline));
                        return;
                    }
                    else if (!pendienteRapido)
                    {
                        if (normSlope < -0.05)
                        {
                            Print(string.Format("{0} - [PAS - Cascada v10.2 | ┬íPAILAS MI PEZ!] Corto frenado: Pendiente en {1:F2} vs Umbral Cascada requerido: -{2:F2} | ATR: {3:F2} pts. Esperando aceleraci├│n real del r├¡o...",
                                Time[0], normSlope, CascadaSlopeThreshold, atr14 != null ? atr14[0] : 0.0));
                        }
                    }
                }
            }

            // =========================================================================
            // DISPARO LARGO: R├ípido de R├¡o Alcista
            // 1. Regla de 1 Solo Tiro: no haber disparado ya en este mismo impulso alcista
            // 2. Contexto Macro: EMA 200 por debajo de la L├¡nea Media (keltnerBig.Midline)
            // 3. Ubicaci├│n: Precio en Zona 0 o 1 (Close >= upperSmall)
            // 4. Aceleraci├│n: Pendiente Normalizada de L├¡nea Media >= CascadaSlopeThreshold (+0.20)
            // 5. Vela con intenci├│n alcista: Close[0] > Open[0]
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
                        Print(string.Format("{0} - [PAS - Cascada v10.2 | ┬íPUM, ADI├ôS! R├üPIDO LARGO DISPARADO] Entrada @ ~{1:F2} en Zona 0/1 | Pendiente Normalizada: +{2:F2} (M├¡nimo Cascada: +{3:F2}) | ATR(14): {4:F2} pts | SL inicial en L├¡nea Media ({5:F2}). ┬íQu├⌐ groser├¡a de trade!",
                            Time[0], Close[0], normSlope, CascadaSlopeThreshold, atr14 != null ? atr14[0] : 0.0, midline));
                        return;
                    }
                    else if (!pendienteRapido)
                    {
                        if (normSlope > 0.05)
                        {
                            Print(string.Format("{0} - [PAS - Cascada v10.2 | ┬íPAILAS MI PEZ!] Largo frenado: Pendiente en +{1:F2} vs Umbral Cascada requerido: +{2:F2} | ATR: {3:F2} pts. Esperando aceleraci├│n real del r├¡o...",
                                Time[0], normSlope, CascadaSlopeThreshold, atr14 != null ? atr14[0] : 0.0));
                        }
                    }
                }
            }
        }

        #endregion

        #region Ejecuci├│n y Gesti├│n de Salidas Escalonadas

        private int GetRiskAdjustedContracts()
        {
            int targetContracts = Contracts; // Configurado por defecto (3 contratos)

            // REGLA DE PROTECCI├ôN TRAS P├ëRDIDA: Si la ├║ltima operaci├│n cerrada fue perdedora (Strike activo),
            // se desescala obligatoriamente a 1 contrato para evitar que una mala racha da├▒e la cuenta.
            if (consecutiveLosses > 0)
            {
                Print(string.Format("{0} - [PAS | DESESCALADO TRAS P├ëRDIDA] Strike activo ({1} p├⌐rdida(s) consecutiva(s)). Entrada protegida con 1 solo contrato.",
                    Time[0], consecutiveLosses));
                targetContracts = 1;
            }

            if (UseDailyManagement && !isDailyLocked && currentDailyFloor > 0)
            {
                double allowedLossDollars = dailyRealizedPnL - currentDailyFloor;
                double tickDollarVal = TickSize * Instrument.MasterInstrument.PointValue;

                // Si el PnL ya cay├│ en o por debajo del piso protegido:
                if (allowedLossDollars <= 0)
                {
                    Print(string.Format("{0} - [PAS | PISO ALCANZADO] PnL actual (${1:F2}) en o por debajo del Piso (+${2:F2} USD). No se abren nuevas posiciones.",
                        Time[0], dailyRealizedPnL, currentDailyFloor));
                    return 0;
                }

                // REGLA SUPREMA DEL MODO DULCE (v11.3): En Escal├│n 3 (Modo Dulce $900+),
                // el piso de $900 es SAGRADO. Para abrir un trade nuevo dentro del Modo Dulce,
                // el margen por encima del piso DEBE permitir al menos 12 ticks de SL saludable ($150 ES / $15 MES).
                // Si sobra menos de $150 (ej. $45 USD en $945), NO se abre la posici├│n para blindar el bot├¡n.
                if (currentDailyTier >= 3)
                {
                    int minSweetTicks = 12; // 12 ticks = $150 ES / $15 MES
                    double minSweetDollars = minSweetTicks * tickDollarVal;

                    if (allowedLossDollars < minSweetDollars)
                    {
                        Print(string.Format("{0} - [ESCUDO MODO DULCE BLINDADO v11.3] Margen disponible (${1:F2} USD) insuficiente para 12 ticks de respiro (${2:F2} USD) sin violar el Piso Sagrado (+${3:F2} USD). Entrada bloqueada para blindar la gloria.",
                            Time[0], allowedLossDollars, minSweetDollars, currentDailyFloor));
                        return 0;
                    }
                }

                // REGLA DE ORO DE ESCALONES 1 Y 2 (v11.3):
                // En Escal├│n 1 ($400-$599, Piso $250) y Escal├│n 2 ($600-$899, Piso $500),
                // se permite SIEMPRE operar con 1 contrato protegido mientras estemos por encima del piso,
                // para que la estrategia pueda dar el salto al siguiente escal├│n o al Modo Dulce sin trabas.
                if (currentDailyTier == 1 || currentDailyTier == 2)
                {
                    int currentTicksMargin = (int)Math.Floor(allowedLossDollars / tickDollarVal);
                    Print(string.Format("{0} - [PAS | ESCAL├ôN {1} ACTIVO: 1 CONTRATO] PnL actual: ${2:F2} USD (Piso +${3:F2}). Operando con 1 contrato hacia el siguiente nivel ({4} ticks de respiro).",
                        Time[0], currentDailyTier, dailyRealizedPnL, currentDailyFloor, currentTicksMargin));
                    return 1;
                }

                // Para Modo Dulce holgado (>= 3 con margen amplio): Permitimos multicontrato si hay margen holgado (m├¡nimo 14 ticks por contrato)
                int minMulticontractTicks = 14;

                for (int qty = targetContracts; qty >= 2; qty--)
                {
                    double dollarsPerTick = qty * tickDollarVal;
                    int maxTicksForQty = (int)Math.Floor(allowedLossDollars / dollarsPerTick);

                    if (maxTicksForQty >= minMulticontractTicks)
                    {
                        if (qty < targetContracts)
                        {
                            Print(string.Format("{0} - [PAS | DESESCALADO DIN├üMICO] Margen: ${1:F2} USD. Posici├│n ajustada a {2} contratos ({3} ticks de respiro).",
                                Time[0], allowedLossDollars, qty, maxTicksForQty));
                        }
                        return qty;
                    }
                }

                // Si estamos en Modo Dulce con margen >= 12 ticks pero < multicontrato, operamos con 1 contrato:
                Print(string.Format("{0} - [PAS | DESESCALADO A 1 CONTRATO] Margen disponible: ${1:F2} USD. Posici├│n ajustada a 1 contrato para seguir operando sin romper piso (+${2:F2}).",
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

            // DESESCALADO DIN├üMICO DE CONTRATOS (Position Sizing Institucional)
            int entryContracts = GetRiskAdjustedContracts();
            if (entryContracts <= 0)
            {
                Print(string.Format("{0} - [PAS | ENTRADA DESCARTADA] Margen disponible insuficiente para arriesgar sin violar el piso garantizado (+${1:F2} USD).",
                    Time[0], currentDailyFloor));
                return;
            }

            // SOBB y CASCADA siempre operan con 1 solo contrato (Monocontrato Quir├║rgico y Surfer con Ox├¡geno Estructural)
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

            trancaActive = false;
            trancaPrice = 0.0;
            trancaBonusPrice = 0.0;
            trancaMilestoneLevel = 0.0;
            trancaArmedBar = -1;

            // Stop Loss inicial f├¡sico en el DOM
            double initialSlPrice = 0.0;
            if (pos == MarketPosition.Long)
            {
                if (ace == AceType.As1_Zona1_100Bar)
                {
                    // Z1-100Bar: SL en L├¡nea Media, pero con TOPE M├üXIMO de seguridad de 14 ticks (R:R saludable 1:1 vs TP 16 ticks)
                    double naturalSl = keltnerBig.Midline[0] - (SlOffsetTicks * TickSize);
                    double maxSafeSl = Close[0] - (14 * TickSize);
                    initialSlPrice = Math.Max(naturalSl, maxSafeSl);

                    // REGLA DE ORO INSTITUCIONAL: Targets pasivos L├ìMITE en el libro para eliminar slippage
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
                    Print(string.Format("{0} - [PAS - Z1-100B | TARGETS L├ìMITE COLOCADOS] C1 (+{1}tk): {2:F2} | C2 (+{3}tk): {4:F2} | Posici├│n activa: {5} contrato(s)",
                        Time[0], Zone1Tp1Ticks, tp1Price, Zone1Tp2Ticks, tp2Price, activeTradeContracts));
                }
                else if (ace == AceType.As2_SOBB)
                {
                    // SOBB MONOCONTRATO: SL Estructural con Ox├¡geno en Banda Peque├▒a Lower 1.75 con tope de seguridad (24 ticks = -$300 m├íx)
                    double naturalSl = keltnerSmall.Lower[0] - (2 * TickSize);
                    double maxSafeSl = Close[0] - (SobbMaxSlTicks * TickSize);
                    initialSlPrice = Math.Max(naturalSl, maxSafeSl);
                    Print(string.Format("{0} - [PAS - SOBB | SL ESTRUCTURAL CON OX├ìGENO] Entrada Long @ {1:F2} | SL en Lower 1.75: {2:F2} ({3:F1} ticks de respiro). Cero asfixia.",
                        Time[0], Close[0], initialSlPrice, (Close[0] - initialSlPrice) / TickSize));
                }
                else if (ace == AceType.As4_Cascada50)
                {
                    // CASCADA SURFER MONOCONTRATO: SL Estructural con Ox├¡geno en Banda Peque├▒a Lower 1.75 con tope de seguridad (24 ticks = -$300 m├íx)
                    double naturalSl = keltnerSmall.Lower[0] - (1 * TickSize);
                    double maxSafeSl = Close[0] - (CascadaMaxSlTicks * TickSize);
                    initialSlPrice = Math.Max(naturalSl, maxSafeSl);
                    isCascadaLockActive = false;
                    Print(string.Format("{0} - [PAS - Cascada | SURFER MONOCONTRATO] Entrada Long @ {1:F2} | SL en Lower 1.75 -1tk: {2:F2} ({3:F1} ticks de respiro | Tope {4}tk = -${5:F2}). Candado ${6:F0} armado.",
                        Time[0], Close[0], initialSlPrice, (Close[0] - initialSlPrice) / TickSize, CascadaMaxSlTicks, (CascadaMaxSlTicks * TickSize * Instrument.MasterInstrument.PointValue), CascadaLockTriggerDollars));
                }
                else if (ace == AceType.As3_Ratchet)
                {
                    // Ratchet Long: Banda opuesta, pero con TOPE M├üXIMO de seguridad (12 ticks = $450 ES m├íx en 3 ctos)
                    double naturalSl = keltnerBig.Lower[0] - (SlOffsetTicks * TickSize);
                    double maxSafeSl = Close[0] - (RatchetMaxSlTicks * TickSize);
                    initialSlPrice = Math.Max(naturalSl, maxSafeSl);
                    Print(string.Format("{0} - [PAS - Ratchet | SL INICIAL CON TOPE] Entrada Long @ {1:F2} | SL en {2:F2} (Tope m├íx {3} ticks = -${4:F2} USD en {5} ctos).",
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
                            Print(string.Format("{0} - [ESCUDO PISO ACTIVO] Piso de +${1:F2} USD limita p├⌐rdida a {2} ticks ({3} contrato(s)). SL Ce├▒ido en {4:F2} colocado en DOM.",
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
                    // Z1-100Bar: SL en L├¡nea Media, pero con TOPE M├üXIMO de seguridad de 14 ticks (R:R saludable 1:1 vs TP 16 ticks)
                    double naturalSl = keltnerBig.Midline[0] + (SlOffsetTicks * TickSize);
                    double maxSafeSl = Close[0] + (14 * TickSize);
                    initialSlPrice = Math.Min(naturalSl, maxSafeSl);

                    // REGLA DE ORO INSTITUCIONAL: Targets pasivos L├ìMITE en el libro para eliminar slippage
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
                    Print(string.Format("{0} - [PAS - Z1-100B | TARGETS L├ìMITE COLOCADOS] C1 (-{1}tk): {2:F2} | C2 (-{3}tk): {4:F2} | Posici├│n activa: {5} contrato(s)",
                        Time[0], Zone1Tp1Ticks, tp1Price, Zone1Tp2Ticks, tp2Price, activeTradeContracts));
                }
                else if (ace == AceType.As2_SOBB)
                {
                    // SOBB MONOCONTRATO: SL Estructural con Ox├¡geno en Banda Peque├▒a Upper 1.75 con tope de seguridad (24 ticks = -$300 m├íx)
                    double naturalSl = keltnerSmall.Upper[0] + (2 * TickSize);
                    double maxSafeSl = Close[0] + (SobbMaxSlTicks * TickSize);
                    initialSlPrice = Math.Min(naturalSl, maxSafeSl);
                    Print(string.Format("{0} - [PAS - SOBB | SL ESTRUCTURAL CON OX├ìGENO] Entrada Short @ {1:F2} | SL en Upper 1.75: {2:F2} ({3:F1} ticks de respiro). Cero asfixia.",
                        Time[0], Close[0], initialSlPrice, (initialSlPrice - Close[0]) / TickSize));
                }
                else if (ace == AceType.As4_Cascada50)
                {
                    // CASCADA SURFER MONOCONTRATO: SL Estructural con Ox├¡geno en Banda Peque├▒a Upper 1.75 con tope de seguridad (24 ticks = -$300 m├íx)
                    double naturalSl = keltnerSmall.Upper[0] + (1 * TickSize);
                    double maxSafeSl = Close[0] + (CascadaMaxSlTicks * TickSize);
                    initialSlPrice = Math.Min(naturalSl, maxSafeSl);
                    isCascadaLockActive = false;
                    Print(string.Format("{0} - [PAS - Cascada | SURFER MONOCONTRATO] Entrada Short @ {1:F2} | SL en Upper 1.75 +1tk: {2:F2} ({3:F1} ticks de respiro | Tope {4}tk = -${5:F2}). Candado ${6:F0} armado.",
                        Time[0], Close[0], initialSlPrice, (initialSlPrice - Close[0]) / TickSize, CascadaMaxSlTicks, (CascadaMaxSlTicks * TickSize * Instrument.MasterInstrument.PointValue), CascadaLockTriggerDollars));
                }
                else if (ace == AceType.As3_Ratchet)
                {
                    // Ratchet Short: Banda opuesta, pero con TOPE M├üXIMO de seguridad (12 ticks = $450 ES m├íx en 3 ctos)
                    double naturalSl = keltnerBig.Upper[0] + (SlOffsetTicks * TickSize);
                    double maxSafeSl = Close[0] + (RatchetMaxSlTicks * TickSize);
                    initialSlPrice = Math.Min(naturalSl, maxSafeSl);
                    Print(string.Format("{0} - [PAS - Ratchet | SL INICIAL CON TOPE] Entrada Short @ {1:F2} | SL en {2:F2} (Tope m├íx {3} ticks = -${4:F2} USD en {5} ctos).",
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
                            Print(string.Format("{0} - [ESCUDO PISO ACTIVO] Piso de +${1:F2} USD limita p├⌐rdida a {2} ticks ({3} contrato(s)). SL Ce├▒ido en {4:F2} colocado en DOM.",
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
            // SALIDAS EN POSICI├ôN LARGA
            // =========================================================================
            if (Position.MarketPosition == MarketPosition.Long)
            {
                double currentGainTicks = (High[0] - entryPrice) / TickSize;
                double currentGainDollars = currentGainTicks * (activeTradeContracts * Instrument.MasterInstrument.PointValue * TickSize);
                double peakGainTicks = (highestPriceSinceEntry - entryPrice) / TickSize;
                double peakGainDollars = peakGainTicks * (activeTradeContracts * Instrument.MasterInstrument.PointValue * TickSize);

                // =========================================================================
                // TRANCA DE ADAMANTIUM (GARRAS DE WOLVERINE & FILTRO ANTI-ASFIXIA) v11.0
                // 1. FILTRO ANTI-ASFIXIA: Si faltan menos de MinTrancaDistanceDollars ($125 ES / $12.5 MES)
                //    para la meta, la Tranca se APAGA para no asfixiar el trade por centavos.
                //    El trade corre libre con su gesti├│n de As (Candado Cascada, TP2 SOBB, Zona 1, etc.).
                // 2. COLCH├ôN DE CONQUISTA ADAMANTIUM: Para armarse, el mercado debe penetrar con fuerza
                //    superando el escal├│n por TrancaConquestBufferDollars ($35 ES / $3.5 MES).
                // 3. PISO BLINDADO GARANTIZADO: La Tranca se clava con TrancaCommissionBufferDollars ($15 ES / $1.5 MES),
                //    garantizando que al liquidar, la cuenta quede matem├íticamente ADENTRO del nuevo escal├│n (>= $600 o $900).
                // 4. CERO AUTODISPARO: No eval├║a el Low[0] de la vela donde se arm├│; solo eval├║a retrocesos
                //    en velas posteriores o si el precio de cierre Close[0] perfora la tranca.
                // =========================================================================
                if (UseDailyManagement && UseTrancaUniversal)
                {
                    double nextMilestone = 0.0;
                    int nextTierTarget = 0;

                    if (TrancaOnlyFromTier2)
                    {
                        // Pista libre en piso inicial: solo trancamos al tocar Escal├│n 2 ($600) o Modo Dulce ($900)
                        if (dailyRealizedPnL < DailyGoal_Piso2)
                        {
                            nextMilestone = DailyGoal_Piso2; // $600 ES (Escal├│n 2)
                            nextTierTarget = 2;
                        }
                        else if (dailyRealizedPnL < DailyGoal_Piso3)
                        {
                            nextMilestone = DailyGoal_Piso3; // $900 ES (Modo Dulce)
                            nextTierTarget = 3;
                        }
                    }
                    else
                    {
                        if (dailyRealizedPnL < DailyGoal_Piso1)
                        {
                            nextMilestone = DailyGoal_Piso1; // $400 ES
                            nextTierTarget = 1;
                        }
                        else if (dailyRealizedPnL < DailyGoal_Piso2)
                        {
                            nextMilestone = DailyGoal_Piso2; // $600 ES
                            nextTierTarget = 2;
                        }
                        else if (dailyRealizedPnL < DailyGoal_Piso3)
                        {
                            nextMilestone = DailyGoal_Piso3; // $900 ES (Modo Dulce)
                            nextTierTarget = 3;
                        }
                    }

                    double dollarsPerPoint = currentQty * Instrument.MasterInstrument.PointValue;
                    if (nextMilestone > 0.0 && dollarsPerPoint > 0)
                    {
                        double rawDistanceToMilestone = nextMilestone - dailyRealizedPnL;
                        double minHealthyDistance = MinTrancaDistanceDollars * instrumentMultiplier;

                        // FILTRO ANTI-ASFIXIA:
                        // Si falta una mincha (< $125 USD) para la meta, apagamos la tranca para no estrangular el trade.
                        // El trade corre libre con la gesti├│n natural del As.
                        if (rawDistanceToMilestone < minHealthyDistance && !trancaActive)
                        {
                            // Tranca inhibida por Anti-Asfixia: el trade respira libremente
                        }
                        else
                        {
                            // ANCLAJE DE TITANIO SOBRE EL ESCAL├ôN (+$20 USD: $420, $620, $920):
                            double effectiveAnchorMilestone = nextMilestone + (TrancaAnchorDollars * instrumentMultiplier);
                            double distanceToAnchor = effectiveAnchorMilestone - dailyRealizedPnL;

                            double conquestRequiredDollars = distanceToAnchor + (TrancaConquestBufferDollars * instrumentMultiplier);
                            double lockRequiredDollars = distanceToAnchor + (TrancaSafetyDollars * instrumentMultiplier);
                            double floatingGainDollars = (Close[0] - entryPrice) * dollarsPerPoint;
                            double favorableGainDollars = (High[0] - entryPrice) * dollarsPerPoint;

                            // 1. ACTIVACI├ôN DE LA TRANCA DE ADAMANTIUM: Requiere penetraci├│n con fuerza (Conquest Buffer de 4 ticks)
                            if (!trancaActive && (favorableGainDollars >= conquestRequiredDollars || floatingGainDollars >= conquestRequiredDollars))
                            {
                                trancaActive = true;
                                trancaArmedBar = CurrentBar;
                                trancaMilestoneLevel = effectiveAnchorMilestone;
                                double lockPoints = lockRequiredDollars / dollarsPerPoint;
                                trancaPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice + lockPoints);

                                double bonusPoints = (TrancaBonusDollars * instrumentMultiplier) / dollarsPerPoint;
                                trancaBonusPrice = Instrument.MasterInstrument.RoundToTickSize(trancaPrice + bonusPoints);

                                Print(string.Format("****************************************************************************************\n" +
                                                    "{0} - [ESCUDO DIARIO v11.1 | ┬íTRANCA DE ADAMANTIUM CLAVADA! ESCAL├ôN {1} CONQUISTADO]\n" +
                                                    "┬íQU├ë GROSER├ìA MI PEZ! El mercado perfor├│ con fuerza la meta del Escal├│n {1} (${2:F2} USD).\n" +
                                                    "Tranca de Adamantium anclada en {3:F2} (Garantiza cerrar en ${4:F2} USD netos tras comisiones y slippage).\n" +
                                                    "Sprint Bonus armado en {5:F2} buscando +${6:F2} USD adicionales. ┬íA romperla sin miedo!\n" +
                                                    "****************************************************************************************",
                                    Time[0], nextTierTarget, nextMilestone, trancaPrice, effectiveAnchorMilestone, trancaBonusPrice, TrancaBonusDollars * instrumentMultiplier));
                            }

                            // 2. GESTI├ôN MIENTRAS LA TRANCA EST├ü ACTIVA
                            if (trancaActive)
                            {
                                string exitSignal = currentAce == AceType.As4_Cascada50 ? "Cascada50_Long" :
                                                    (currentAce == AceType.As2_SOBB ? "SOBB_Long" :
                                                    (currentAce == AceType.As1_Zona1_100Bar ? "Z1-100Bar_Long" : "Ratchet_Long"));

                                // A) SPRINT BONUS ALCANZADO (+ $100 USD adicionales conquistados)
                                if (Close[0] >= trancaBonusPrice || High[0] >= trancaBonusPrice)
                                {
                                    if (currentAce == AceType.As4_Cascada50)
                                    {
                                        cascadaQuarantineEndTime = Time[0].AddMinutes(CascadaQuarantineMinutes);
                                    }

                                    ExitLong("Exit_Tranca_TP_Bonus", exitSignal);
                                    double actualGain = (Close[0] - entryPrice) * dollarsPerPoint;
                                    Print(string.Format("****************************************************************************************\n" +
                                                        "{0} - [ESCUDO DIARIO v11.1 | ┬íPUM, ADI├ôS! SPRINT BONUS CORONADO]\n" +
                                                        "┬íGROSER├ìA TOTAL! Alcanz├│ el TP Bonus en {1:F2}. Ganancia de +${2:F2} USD asegurada.\n" +
                                                        "Escal├│n {3} superado con creces. ┬íDinero blindado en la caja fuerte!\n" +
                                                        "****************************************************************************************",
                                        Time[0], Close[0], actualGain, nextTierTarget));
                                    return;
                                }

                                // B) RETROCESO DETENIDO EN SECO POR LA TRANCA DE ADAMANTIUM
                                // CERO AUTODISPARO: En la misma barra donde se arm├│ solo salimos si Close[0] cae bajo la tranca;
                                // en barras posteriores evaluamos si Low[0] toca o perfora la tranca.
                                bool retrocesoConfirmadoLong = (Close[0] <= trancaPrice) || (CurrentBar > trancaArmedBar && Low[0] <= trancaPrice);

                                if (retrocesoConfirmadoLong)
                                {
                                    if (currentAce == AceType.As4_Cascada50)
                                    {
                                        cascadaQuarantineEndTime = Time[0].AddMinutes(CascadaQuarantineMinutes);
                                    }

                                    ExitLong("Exit_Tranca_Acero", exitSignal);
                                    double actualGain = (Close[0] - entryPrice) * dollarsPerPoint;
                                    Print(string.Format("****************************************************************************************\n" +
                                                        "{0} - [ESCUDO DIARIO v11.1 | TRANCA DE ADAMANTIUM INDESTRUCTIBLE EJECUTADA]\n" +
                                                        "El mercado intent├│ retroceder pero choc├│ contra las Garras de Wolverine en {1:F2}.\n" +
                                                        "Trade liquidado con +${2:F2} USD netos garantizados. ┬íEscal├│n {3} (${4:F2} USD) sellado para siempre!\n" +
                                                        "****************************************************************************************",
                                        Time[0], Close[0], actualGain, nextTierTarget, trancaMilestoneLevel > 0 ? trancaMilestoneLevel : effectiveAnchorMilestone));
                                    return;
                                }
                            }
                        }
                    }
                }

                // --- AS 2: SOBB (SURFER OUT BIG BANDA - MONOCONTRATO QUIR├ÜRGICO CON OX├ìGENO) ---
                if (currentAce == AceType.As2_SOBB)
                {
                    // 1. TOMA DE GANANCIAS / ACTIVACI├ôN DE BREAK-EVEN (+10 TICKS)
                    if (!sobbLevel1Achieved && (currentGainTicks >= SobbTp1Ticks || peakGainTicks >= SobbTp1Ticks))
                    {
                        sobbLevel1Achieved = true;
                        Print(string.Format("{0} - [PAS - SOBB | ESCAL├ôN 1 SUPERADO] Ganancia: +{1:F1} tks. Stop protegido en BREAKEVEN +1tk ({2:F2}). ┬íRiesgo CERO!",
                            Time[0], Math.Max(currentGainTicks, peakGainTicks), entryPrice + (1 * TickSize)));
                    }

                    // 2. GESTI├ôN TRAS ALCANZAR BREAK-EVEN (NIVEL 1 SUPERADO):
                    if (sobbLevel1Achieved)
                    {
                        double bePrice = entryPrice + (1 * TickSize);

                        // A) Target de Expansi├│n (+20 ticks = $250 ES / $25 MES)
                        if (currentGainTicks >= SobbTp2Ticks || peakGainTicks >= SobbTp2Ticks)
                        {
                            ExitLong("SOBB_Tp2_Expansion", "SOBB_Long");
                            Print(string.Format("{0} - [PAS - SOBB | TARGET EXPANSI├ôN ALCANZADO] +{1} ticks (+${2:F2} USD). Victoria asegurada.",
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

                        // C) Stop de Protecci├│n en Break-Even + 1 tick
                        if (Close[0] <= bePrice || Low[0] <= bePrice)
                        {
                            ExitLong("SOBB_Stop_BE", "SOBB_Long");
                            Print(string.Format("{0} - [PAS - SOBB | SALIDA EN BREAKEVEN] Posici├│n protegida cerrada en {1:F2} tras amague.",
                                Time[0], bePrice));
                            return;
                        }
                    }
                    // 3. STOP LOSS INICIAL ESTRUCTURAL CON OX├ìGENO (ANTES DE TOCAR BREAK-EVEN):
                    else
                    {
                        // SL Estructural en Banda Peque├▒a Lower 1.75 con tope m├íximo de seguridad (SobbMaxSlTicks = 24 ticks = -$300 USD m├íx)
                        double structuralSl = keltnerSmall.Lower[0] - (2 * TickSize);
                        double maxSafeSl = entryPrice - (SobbMaxSlTicks * TickSize);
                        double slPrice = Math.Max(structuralSl, maxSafeSl);

                        if (Close[0] <= slPrice || Low[0] <= slPrice)
                        {
                            ExitLong("SOBB_SL_Estructural", "SOBB_Long");
                            Print(string.Format("{0} - [PAS - SOBB | SL ESTRUCTURAL EJECUTADO] Precio perfor├│ SL ({1:F2}) | Salida a Mercado @ ~{2:F2} (Riesgo acotado a 1 contrato).",
                                Time[0], slPrice, Close[0]));
                            return;
                        }
                    }

                    // Salida por Fin de Ciclo (EMA 200 corta banda inferior 3.5 para SOBB)
                    if (ema200[0] <= keltnerBig.Lower[0])
                    {
                        ExitLong("Exit_SOBB_FinCiclo", "SOBB_Long");
                        Print(string.Format("{0} - [PAS - SOBB | FIN DE CICLO] EMA200 ({1:F2}) cort├│ Lower 3.5 ({2:F2}) | Cierre total a Mercado @ ~{3:F2}",
                            Time[0], ema200[0], keltnerBig.Lower[0], Close[0]));
                    }
                }
                // --- AS 3: RATCHET (EL CHICLE PARAB├ôLICO - ESCALONAMIENTO POR FASES DE CANAL) ---
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
                            Print(string.Format("{0} - [PAS - Ratchet | FASE 1 COMPLETADA] Precio toc├│ Banda Superior 3.5 ({1:F2}) / +{2:F1} tks. Contrato 1 liquidado. Stops restantes blindados en BREAKEVEN +1tk ({3:F2}). ┬íRiesgo CERO!",
                                Time[0], keltnerBig.Upper[0], maxProfitTicks, entryPrice + (1 * TickSize)));
                        }
                    }

                    // CONTRATO 2: Se baja del bus si alcanza Fase 3 Choke (72 ticks) O si retrocede a L├¡nea Media
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
                                Print(string.Format("{0} - [PAS - Ratchet | FASE 3 ESTRANGULAMIENTO] Super├│ {1} ticks. Contrato 2 cobrado. Contrato 3 Runner en Chicle Parab├│lico a {2} ticks del High.",
                                    Time[0], RatchetChokeThresholdTicks, RatchetChicleTicks));
                            }
                        }
                        // Si retrocede a la L├¡nea Media tras haber tocado la banda superior
                        else if (Close[0] <= keltnerBig.Midline[0] - (1 * TickSize) || Low[0] <= keltnerBig.Midline[0] - (1 * TickSize))
                        {
                            ratchetC2Achieved = true;
                            if (currentQty >= 2)
                            {
                                ExitLong(1, "Ratchet_C2_Midline", "Ratchet_Long");
                                Print(string.Format("{0} - [PAS - Ratchet | FASE 2 CUMPLIDA] Precio perfor├│ L├¡nea Media ({1:F2}). Contrato 2 liquidado en resguardo estructural.",
                                    Time[0], keltnerBig.Midline[0]));
                            }
                        }
                    }

                    // CONTRATO 3 (RUNNER): El Chicle Parab├│lico
                    if (ratchetChokeActive || maxProfitTicks >= RatchetChokeThresholdTicks)
                    {
                        double chokeSl = highestPriceSinceEntry - (RatchetChicleTicks * TickSize);
                        if (Close[0] <= chokeSl || Low[0] <= chokeSl)
                        {
                            ExitLong("Ratchet_C3_RunnerChicle", "Ratchet_Long");
                            Print(string.Format("{0} - [PAS - Ratchet | RUNNER LIQUIDADO EN LA CIMA] Chicle Parab├│lico ejecutado a ~{1:F2} (Pico: {2:F2}). ┬íGanancia m├íxima asegurada!",
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
                            Print(string.Format("{0} - [PAS - Ratchet | SALIDA EN BREAKEVEN] Posici├│n restante protegida en {1:F2}. Salida sin p├⌐rdida.",
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
                            Print(string.Format("{0} - [PAS - Ratchet | STOP INICIAL] Precio perfor├│ Banda Inferior ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                Time[0], initialSl, Close[0]));
                        }
                    }
                }
                // --- GESTI├ôN AS 1: ZONA 1 (ESCALONAMIENTO 3 FASES CON RUNNER AHOGO + MIDLINE SL) ---
                else if (currentAce == AceType.As1_Zona1_100Bar)
                {
                    double maxProfitTicks = (highestPriceSinceEntry - entryPrice) / TickSize;

                    // Si estamos operando con 1 solo contrato (por desescalado tras p├⌐rdida o regla de piso):
                    if (activeTradeContracts == 1)
                    {
                        // Target pasivo de +8 ticks
                        if (maxProfitTicks >= Zone1Tp1Ticks || currentGainTicks >= Zone1Tp1Ticks)
                        {
                            ExitLong("Z1_Mono_Tp", "Z1-100Bar_Long");
                            Print(string.Format("{0} - [PAS - Z1-100B | MONOCONTRATO TP COBRADO] +{1} ticks (+${2:F2}). ┬íGanancia asegurada!",
                                Time[0], Zone1Tp1Ticks, Zone1Tp1Ticks * TickSize * Instrument.MasterInstrument.PointValue));
                            return;
                        }

                        // Stop Loss en L├¡nea Media con TOPE M├üXIMO de seguridad de 14 ticks:
                        double naturalSl = keltnerBig.Midline[0] - (SlOffsetTicks * TickSize);
                        double slPrice = Math.Max(naturalSl, entryPrice - (14 * TickSize));

                        if (Close[0] <= slPrice || Low[0] <= slPrice)
                        {
                            ExitLong("Z1_Mono_SL", "Z1-100Bar_Long");
                            Print(string.Format("{0} - [PAS - Z1-100B | MONOCONTRATO SL EJECUTADO] Precio perfor├│ SL ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                Time[0], slPrice, Close[0]));
                            return;
                        }
                    }
                    else
                    {
                        // MULTICONTRATO (2 o 3 contratos)
                        // CONTRATO 1: Target L├¡mite pasivo a +8 ticks (Zone1Tp1Ticks) ejecutado o detectado
                        if (!z1C1Achieved && (currentQty < activeTradeContracts || maxProfitTicks >= Zone1Tp1Ticks || currentGainTicks >= Zone1Tp1Ticks))
                        {
                            z1C1Achieved = true;
                            if (currentQty >= 3)
                            {
                                ExitLong(1, "Z1_C1_Tp8", "Z1-100Bar_Long");
                            }
                            Print(string.Format("{0} - [PAS - Z1-100B | FASE 1 SUPERADA] C1 ejecutado a +{1} ticks (+${2:F2}). Stop blindado en BREAKEVEN +1tk ({3:F2}). ┬íRiesgo CERO!",
                                Time[0], Zone1Tp1Ticks, Zone1Tp1Ticks * TickSize * Instrument.MasterInstrument.PointValue, entryPrice + (1 * TickSize)));
                        }

                        // CONTRATO 2: Target L├¡mite pasivo a +16 ticks (Zone1Tp2Ticks) ejecutado o detectado
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
                                Print(string.Format("{0} - [PAS - Z1-100B | RUNNER AHOGADO EN LA CIMA] Salida a Mercado @ ~{1:F2} (Pico: {2:F2}). ┬íGanancia m├íxima asegurada!",
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
                                Print(string.Format("{0} - [PAS - Z1-100B | SALIDA EN BREAKEVEN] Posici├│n restante protegida en {1:F2}. Salida sin p├⌐rdida.",
                                    Time[0], bePrice));
                            }
                        }
                        // Stop Inicial antes de C1 (L├¡nea Media con TOPE DE 14 TICKS):
                        else
                        {
                            double naturalSl = keltnerBig.Midline[0] - (SlOffsetTicks * TickSize);
                            double slPrice = Math.Max(naturalSl, entryPrice - (14 * TickSize));

                            if (Close[0] <= slPrice || Low[0] <= slPrice)
                            {
                                ExitLong("SL_Midline_Zona1", "Z1-100Bar_Long");
                                Print(string.Format("{0} - [PAS - Z1-100B | SL MIDLINE] Precio toc├│ Midline ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                    Time[0], slPrice, Close[0]));
                            }
                        }
                    }
                }
                // --- GESTI├ôN AS 4: CASCADA SURFER MONOCONTRATO (CANDADO $180 Y FIN DE CICLO) ---
                else if (currentAce == AceType.As4_Cascada50)
                {
                    double currentFavorableTicks = (High[0] - entryPrice) / TickSize;
                    double currentProfitDollars  = currentFavorableTicks * (Instrument.MasterInstrument.PointValue * TickSize);
                    double effectiveLockTrigger  = CascadaLockTriggerDollars * instrumentMultiplier;

                    // CANDADO DE $180: Al superar $180 a favor ($18 en MES), el candado se arma
                    if (!isCascadaLockActive && currentProfitDollars >= effectiveLockTrigger)
                    {
                        isCascadaLockActive = true;
                        Print(string.Format("{0} - [PAS - Cascada | CANDADO ACTIVADO - LARGO] Ganancia flotante alcanz├│ {1:C2} (+{2:F1} tks). Candado armado en Upper 1.75 -1tk.",
                            Time[0], currentProfitDollars, currentFavorableTicks));
                    }

                    // 1. Salida por Candado Activo (Upper 1.75 - 1 tick tras superar $180 USD)
                    if (isCascadaLockActive)
                    {
                        double lockSl = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Upper[0] - (1 * TickSize));
                        if (Close[0] <= lockSl || Low[0] <= lockSl)
                        {
                            cascadaQuarantineEndTime = Time[0].AddMinutes(CascadaQuarantineMinutes);
                            ExitLong("SL_Candado_Cascada", "Cascada50_Long");
                            Print(string.Format("{0} - [PAS - Cascada v10.1 | LARGO COBRADO CON CANDADO] Precio perfor├│ Upper 1.75 -1tk ({1:F2}) | Salida a Mercado @ ~{2:F2} (Ganancia asegurada). Cuarentena de {3} min armada.",
                                Time[0], lockSl, Close[0], CascadaQuarantineMinutes));
                            return;
                        }
                    }

                    // 2. Salida por Fin de Ciclo del R├ípido (Repliegue adentro de Banda 1.75 o freno de pendiente tras ganar >= 14 ticks)
                    double normMidSlope = GetNormalizedSlope(keltnerBig.Midline);
                    if (peakGainTicks >= 14 && (Close[0] < keltnerSmall.Upper[0] || normMidSlope <= 0.0))
                    {
                        ExitLong("Exit_Cascada_CicloTerminado", "Cascada50_Long");
                        Print(string.Format("{0} - [PAS - Cascada v10.1 | FIN DE R├üPIDO] Repliegue adentro de Upper 1.75 ({1:F2}) o freno de pendiente ({2:F2}) tras ganar {3:F1} tks pico. Salida a Mercado @ ~{4:F2}",
                            Time[0], keltnerSmall.Upper[0], normMidSlope, peakGainTicks, Close[0]));
                        return;
                    }

                    // 3. Stop Loss Inicial Estructural (antes de alcanzar el Candado) en L├¡nea Media con tope de seguridad CascadaMaxSlTicks (24 ticks = -$300 m├íx)
                    double structuralSl = keltnerBig.Midline[0] - (2 * TickSize);
                    double maxSafeSl    = entryPrice - (CascadaMaxSlTicks * TickSize);
                    double dynamicSl    = Instrument.MasterInstrument.RoundToTickSize(Math.Max(structuralSl, maxSafeSl));
                    if (Close[0] <= dynamicSl || Low[0] <= dynamicSl)
                    {
                        ExitLong("SL_BandaOpuesta_Cascada", "Cascada50_Long");
                        Print(string.Format("{0} - [PAS - Cascada v10.1 | LARGO CERRADO POR SL ESTRUCTURAL] Precio perfor├│ L├¡nea Media ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                            Time[0], dynamicSl, Close[0]));
                        return;
                    }
                }
            }
            // =========================================================================
            // SALIDAS EN POSICI├ôN CORTA (ESPEJO INVERSO)
            // =========================================================================
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double currentGainTicks = (entryPrice - Low[0]) / TickSize;
                double currentGainDollars = currentGainTicks * (activeTradeContracts * Instrument.MasterInstrument.PointValue * TickSize);
                double peakGainTicks = (entryPrice - lowestPriceSinceEntry) / TickSize;
                double peakGainDollars = peakGainTicks * (activeTradeContracts * Instrument.MasterInstrument.PointValue * TickSize);

                // =========================================================================
                // TRANCA DE ADAMANTIUM (GARRAS DE WOLVERINE & FILTRO ANTI-ASFIXIA) v11.0 (CORTO)
                // 1. FILTRO ANTI-ASFIXIA: Si faltan menos de MinTrancaDistanceDollars ($125 ES / $12.5 MES)
                //    para la meta, la Tranca se APAGA para no asfixiar el trade por centavos.
                //    El trade corre libre con su gesti├│n de As (Candado Cascada, TP2 SOBB, Zona 1, etc.).
                // 2. COLCH├ôN DE CONQUISTA ADAMANTIUM: Para armarse, el mercado debe penetrar con fuerza
                //    superando el escal├│n por TrancaConquestBufferDollars ($35 ES / $3.5 MES).
                // 3. PISO BLINDADO GARANTIZADO: La Tranca se clava con TrancaCommissionBufferDollars ($15 ES / $1.5 MES),
                //    garantizando que al liquidar, la cuenta quede matem├íticamente ADENTRO del nuevo escal├│n (>= $600 o $900).
                // 4. CERO AUTODISPARO: No eval├║a el High[0] de la vela donde se arm├│; solo eval├║a retrocesos
                //    en velas posteriores o si el precio de cierre Close[0] perfora la tranca hacia arriba.
                // =========================================================================
                if (UseDailyManagement && UseTrancaUniversal)
                {
                    double nextMilestone = 0.0;
                    int nextTierTarget = 0;

                    if (TrancaOnlyFromTier2)
                    {
                        // Pista libre en piso inicial: solo trancamos al tocar Escal├│n 2 ($600) o Modo Dulce ($900)
                        if (dailyRealizedPnL < DailyGoal_Piso2)
                        {
                            nextMilestone = DailyGoal_Piso2; // $600 ES (Escal├│n 2)
                            nextTierTarget = 2;
                        }
                        else if (dailyRealizedPnL < DailyGoal_Piso3)
                        {
                            nextMilestone = DailyGoal_Piso3; // $900 ES (Modo Dulce)
                            nextTierTarget = 3;
                        }
                    }
                    else
                    {
                        if (dailyRealizedPnL < DailyGoal_Piso1)
                        {
                            nextMilestone = DailyGoal_Piso1; // $400 ES
                            nextTierTarget = 1;
                        }
                        else if (dailyRealizedPnL < DailyGoal_Piso2)
                        {
                            nextMilestone = DailyGoal_Piso2; // $600 ES
                            nextTierTarget = 2;
                        }
                        else if (dailyRealizedPnL < DailyGoal_Piso3)
                        {
                            nextMilestone = DailyGoal_Piso3; // $900 ES (Modo Dulce)
                            nextTierTarget = 3;
                        }
                    }

                    double dollarsPerPoint = currentQty * Instrument.MasterInstrument.PointValue;
                    if (nextMilestone > 0.0 && dollarsPerPoint > 0)
                    {
                        double rawDistanceToMilestone = nextMilestone - dailyRealizedPnL;
                        double minHealthyDistance = MinTrancaDistanceDollars * instrumentMultiplier;

                        // FILTRO ANTI-ASFIXIA:
                        // Si falta una mincha (< $125 USD) para la meta, apagamos la tranca para no estrangular el trade.
                        // El trade corre libre con la gesti├│n natural del As.
                        if (rawDistanceToMilestone < minHealthyDistance && !trancaActive)
                        {
                            // Tranca inhibida por Anti-Asfixia: el trade respira libremente
                        }
                        else
                        {
                            // ANCLAJE DE TITANIO SOBRE EL ESCAL├ôN (+$20 USD: $420, $620, $920):
                            double effectiveAnchorMilestone = nextMilestone + (TrancaAnchorDollars * instrumentMultiplier);
                            double distanceToAnchor = effectiveAnchorMilestone - dailyRealizedPnL;

                            double conquestRequiredDollars = distanceToAnchor + (TrancaConquestBufferDollars * instrumentMultiplier);
                            double lockRequiredDollars = distanceToAnchor + (TrancaSafetyDollars * instrumentMultiplier);
                            double floatingGainDollars = (entryPrice - Close[0]) * dollarsPerPoint;
                            double favorableGainDollars = (entryPrice - Low[0]) * dollarsPerPoint;

                            // 1. ACTIVACI├ôN DE LA TRANCA DE ADAMANTIUM: Requiere penetraci├│n con fuerza (Conquest Buffer de 4 ticks)
                            if (!trancaActive && (favorableGainDollars >= conquestRequiredDollars || floatingGainDollars >= conquestRequiredDollars))
                            {
                                trancaActive = true;
                                trancaArmedBar = CurrentBar;
                                trancaMilestoneLevel = effectiveAnchorMilestone;
                                double lockPoints = lockRequiredDollars / dollarsPerPoint;
                                trancaPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice - lockPoints);

                                double bonusPoints = (TrancaBonusDollars * instrumentMultiplier) / dollarsPerPoint;
                                trancaBonusPrice = Instrument.MasterInstrument.RoundToTickSize(trancaPrice - bonusPoints);

                                Print(string.Format("****************************************************************************************\n" +
                                                    "{0} - [ESCUDO DIARIO v11.1 | ┬íTRANCA DE ADAMANTIUM CLAVADA! ESCAL├ôN {1} CONQUISTADO]\n" +
                                                    "┬íQU├ë GROSER├ìA MI PEZ! El mercado perfor├│ con fuerza la meta del Escal├│n {1} (${2:F2} USD).\n" +
                                                    "Tranca de Adamantium anclada en {3:F2} (Garantiza cerrar en ${4:F2} USD netos tras comisiones y slippage).\n" +
                                                    "Sprint Bonus armado en {5:F2} buscando +${6:F2} USD adicionales. ┬íA romperla sin miedo!\n" +
                                                    "****************************************************************************************",
                                    Time[0], nextTierTarget, nextMilestone, trancaPrice, effectiveAnchorMilestone, trancaBonusPrice, TrancaBonusDollars * instrumentMultiplier));
                            }

                            // 2. GESTI├ôN MIENTRAS LA TRANCA EST├ü ACTIVA
                            if (trancaActive)
                            {
                                string exitSignal = currentAce == AceType.As4_Cascada50 ? "Cascada50_Short" :
                                                    (currentAce == AceType.As2_SOBB ? "SOBB_Short" :
                                                    (currentAce == AceType.As1_Zona1_100Bar ? "Z1-100Bar_Short" : "Ratchet_Short"));

                                // A) SPRINT BONUS ALCANZADO (+ $100 USD adicionales conquistados)
                                if (Close[0] <= trancaBonusPrice || Low[0] <= trancaBonusPrice)
                                {
                                    if (currentAce == AceType.As4_Cascada50)
                                    {
                                        cascadaQuarantineEndTime = Time[0].AddMinutes(CascadaQuarantineMinutes);
                                    }

                                    ExitShort("Exit_Tranca_TP_Bonus", exitSignal);
                                    double actualGain = (entryPrice - Close[0]) * dollarsPerPoint;
                                    Print(string.Format("****************************************************************************************\n" +
                                                        "{0} - [ESCUDO DIARIO v11.1 | ┬íPUM, ADI├ôS! SPRINT BONUS CORONADO]\n" +
                                                        "┬íGROSER├ìA TOTAL! Alcanz├│ el TP Bonus en {1:F2}. Ganancia de +${2:F2} USD asegurada.\n" +
                                                        "Escal├│n {3} superado con creces. ┬íDinero blindado en la caja fuerte!\n" +
                                                        "****************************************************************************************",
                                        Time[0], Close[0], actualGain, nextTierTarget));
                                    return;
                                }

                                // B) RETROCESO DETENIDO EN SECO POR LA TRANCA DE ADAMANTIUM
                                // CERO AUTODISPARO: En la misma barra donde se arm├│ solo salimos si Close[0] sube sobre la tranca;
                                // en barras posteriores evaluamos si High[0] toca o perfora la tranca.
                                bool retrocesoConfirmadoShort = (Close[0] >= trancaPrice) || (CurrentBar > trancaArmedBar && High[0] >= trancaPrice);

                                if (retrocesoConfirmadoShort)
                                {
                                    if (currentAce == AceType.As4_Cascada50)
                                    {
                                        cascadaQuarantineEndTime = Time[0].AddMinutes(CascadaQuarantineMinutes);
                                    }

                                    ExitShort("Exit_Tranca_Acero", exitSignal);
                                    double actualGain = (entryPrice - Close[0]) * dollarsPerPoint;
                                    Print(string.Format("****************************************************************************************\n" +
                                                        "{0} - [ESCUDO DIARIO v11.1 | TRANCA DE ADAMANTIUM INDESTRUCTIBLE EJECUTADA]\n" +
                                                        "El mercado intent├│ retroceder pero choc├│ contra las Garras de Wolverine en {1:F2}.\n" +
                                                        "Trade liquidado con +${2:F2} USD netos garantizados. ┬íEscal├│n {3} (${4:F2} USD) sellado para siempre!\n" +
                                                        "****************************************************************************************",
                                        Time[0], Close[0], actualGain, nextTierTarget, trancaMilestoneLevel > 0 ? trancaMilestoneLevel : effectiveAnchorMilestone));
                                    return;
                                }
                            }
                        }
                    }
                }

                // --- AS 2: SOBB (SURFER OUT BIG BANDA - MONOCONTRATO QUIR├ÜRGICO CON OX├ìGENO) ---
                if (currentAce == AceType.As2_SOBB)
                {
                    // 1. TOMA DE GANANCIAS / ACTIVACI├ôN DE BREAK-EVEN (+10 TICKS)
                    if (!sobbLevel1Achieved && (currentGainTicks >= SobbTp1Ticks || peakGainTicks >= SobbTp1Ticks))
                    {
                        sobbLevel1Achieved = true;
                        Print(string.Format("{0} - [PAS - SOBB | ESCAL├ôN 1 SUPERADO] Ganancia: +{1:F1} tks. Stop protegido en BREAKEVEN -1tk ({2:F2}). ┬íRiesgo CERO!",
                            Time[0], Math.Max(currentGainTicks, peakGainTicks), entryPrice - (1 * TickSize)));
                    }

                    // 2. GESTI├ôN TRAS ALCANZAR BREAK-EVEN (NIVEL 1 SUPERADO):
                    if (sobbLevel1Achieved)
                    {
                        double bePrice = entryPrice - (1 * TickSize);

                        // A) Target de Expansi├│n (+20 ticks = $250 ES / $25 MES)
                        if (currentGainTicks >= SobbTp2Ticks || peakGainTicks >= SobbTp2Ticks)
                        {
                            ExitShort("SOBB_Tp2_Expansion", "SOBB_Short");
                            Print(string.Format("{0} - [PAS - SOBB | TARGET EXPANSI├ôN ALCANZADO] +{1} ticks (+${2:F2} USD). Victoria asegurada.",
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

                        // C) Stop de Protecci├│n en Break-Even - 1 tick
                        if (Close[0] >= bePrice || High[0] >= bePrice)
                        {
                            ExitShort("SOBB_Stop_BE", "SOBB_Short");
                            Print(string.Format("{0} - [PAS - SOBB | SALIDA EN BREAKEVEN] Posici├│n protegida cerrada en {1:F2} tras amague.",
                                Time[0], bePrice));
                            return;
                        }
                    }
                    // 3. STOP LOSS INICIAL ESTRUCTURAL CON OX├ìGENO (ANTES DE TOCAR BREAK-EVEN):
                    else
                    {
                        // SL Estructural en Banda Peque├▒a Upper 1.75 con tope m├íximo de seguridad (SobbMaxSlTicks = 24 ticks = -$300 USD m├íx)
                        double structuralSl = keltnerSmall.Upper[0] + (2 * TickSize);
                        double maxSafeSl = entryPrice + (SobbMaxSlTicks * TickSize);
                        double slPrice = Math.Min(structuralSl, maxSafeSl);

                        if (Close[0] >= slPrice || High[0] >= slPrice)
                        {
                            ExitShort("SOBB_SL_Estructural", "SOBB_Short");
                            Print(string.Format("{0} - [PAS - SOBB | SL ESTRUCTURAL EJECUTADO] Precio perfor├│ SL ({1:F2}) | Salida a Mercado @ ~{2:F2} (Riesgo acotado a 1 contrato).",
                                Time[0], slPrice, Close[0]));
                            return;
                        }
                    }

                    // Salida por Fin de Ciclo (EMA 200 corta banda superior 3.5 para SOBB)
                    if (ema200[0] >= keltnerBig.Upper[0])
                    {
                        ExitShort("Exit_SOBB_FinCiclo", "SOBB_Short");
                        Print(string.Format("{0} - [PAS - SOBB | FIN DE CICLO] EMA200 ({1:F2}) cort├│ Upper 3.5 ({2:F2}) | Cierre total a Mercado @ ~{3:F2}",
                            Time[0], ema200[0], keltnerBig.Upper[0], Close[0]));
                    }
                }
                // --- AS 3: RATCHET (EL CHICLE PARAB├ôLICO - ESCALONAMIENTO POR FASES DE CANAL) ---
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
                            Print(string.Format("{0} - [PAS - Ratchet | FASE 1 COMPLETADA] Precio toc├│ Banda Inferior 3.5 ({1:F2}) / +{2:F1} tks. Contrato 1 liquidado. Stops restantes blindados en BREAKEVEN -1tk ({3:F2}). ┬íRiesgo CERO!",
                                Time[0], keltnerBig.Lower[0], maxProfitTicks, entryPrice - (1 * TickSize)));
                        }
                    }

                    // CONTRATO 2: Se baja del bus si alcanza Fase 3 Choke (72 ticks) O si supera L├¡nea Media
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
                                Print(string.Format("{0} - [PAS - Ratchet | FASE 3 ESTRANGULAMIENTO] Super├│ {1} ticks. Contrato 2 cobrado. Contrato 3 Runner en Chicle Parab├│lico a {2} ticks del Low.",
                                    Time[0], RatchetChokeThresholdTicks, RatchetChicleTicks));
                            }
                        }
                        // Si supera la L├¡nea Media tras haber tocado la banda inferior
                        else if (Close[0] >= keltnerBig.Midline[0] + (1 * TickSize) || High[0] >= keltnerBig.Midline[0] + (1 * TickSize))
                        {
                            ratchetC2Achieved = true;
                            if (currentQty >= 2)
                            {
                                ExitShort(1, "Ratchet_C2_Midline", "Ratchet_Short");
                                Print(string.Format("{0} - [PAS - Ratchet | FASE 2 CUMPLIDA] Precio perfor├│ L├¡nea Media ({1:F2}). Contrato 2 liquidado en resguardo estructural.",
                                    Time[0], keltnerBig.Midline[0]));
                            }
                        }
                    }

                    // CONTRATO 3 (RUNNER): El Chicle Parab├│lico
                    if (ratchetChokeActive || maxProfitTicks >= RatchetChokeThresholdTicks)
                    {
                        double chokeSl = lowestPriceSinceEntry + (RatchetChicleTicks * TickSize);
                        if (Close[0] >= chokeSl || High[0] >= chokeSl)
                        {
                            ExitShort("Ratchet_C3_RunnerChicle", "Ratchet_Short");
                            Print(string.Format("{0} - [PAS - Ratchet | RUNNER LIQUIDADO EN EL PISO] Chicle Parab├│lico ejecutado a ~{1:F2} (Piso: {2:F2}). ┬íGanancia m├íxima asegurada!",
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
                            Print(string.Format("{0} - [PAS - Ratchet | SALIDA EN BREAKEVEN] Posici├│n restante protegida en {1:F2}. Salida sin p├⌐rdida.",
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
                            Print(string.Format("{0} - [PAS - Ratchet | STOP INICIAL] Precio super├│ Banda Superior ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                Time[0], initialSl, Close[0]));
                        }
                    }
                }
                // --- GESTI├ôN AS 1: ZONA 1 (ESCALONAMIENTO 3 FASES CON RUNNER AHOGO + MIDLINE SL) ---
                else if (currentAce == AceType.As1_Zona1_100Bar)
                {
                    double maxProfitTicks = (entryPrice - lowestPriceSinceEntry) / TickSize;

                    // Si estamos operando con 1 solo contrato (por desescalado tras p├⌐rdida o regla de piso):
                    if (activeTradeContracts == 1)
                    {
                        // Target pasivo de +8 ticks
                        if (maxProfitTicks >= Zone1Tp1Ticks || currentGainTicks >= Zone1Tp1Ticks)
                        {
                            ExitShort("Z1_Mono_Tp", "Z1-100Bar_Short");
                            Print(string.Format("{0} - [PAS - Z1-100B | MONOCONTRATO TP COBRADO] +{1} ticks (+${2:F2}). ┬íGanancia asegurada!",
                                Time[0], Zone1Tp1Ticks, Zone1Tp1Ticks * TickSize * Instrument.MasterInstrument.PointValue));
                            return;
                        }

                        // Stop Loss en L├¡nea Media con TOPE M├üXIMO de seguridad de 14 ticks:
                        double naturalSl = keltnerBig.Midline[0] + (SlOffsetTicks * TickSize);
                        double slPrice = Math.Min(naturalSl, entryPrice + (14 * TickSize));

                        if (Close[0] >= slPrice || High[0] >= slPrice)
                        {
                            ExitShort("Z1_Mono_SL", "Z1-100Bar_Short");
                            Print(string.Format("{0} - [PAS - Z1-100B | MONOCONTRATO SL EJECUTADO] Precio perfor├│ SL ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                Time[0], slPrice, Close[0]));
                            return;
                        }
                    }
                    else
                    {
                        // MULTICONTRATO (2 o 3 contratos)
                        // CONTRATO 1: Target L├¡mite pasivo a +8 ticks (Zone1Tp1Ticks) ejecutado o detectado
                        if (!z1C1Achieved && (currentQty < activeTradeContracts || maxProfitTicks >= Zone1Tp1Ticks || currentGainTicks >= Zone1Tp1Ticks))
                        {
                            z1C1Achieved = true;
                            if (currentQty >= 3)
                            {
                                ExitShort(1, "Z1_C1_Tp8", "Z1-100Bar_Short");
                            }
                            Print(string.Format("{0} - [PAS - Z1-100B | FASE 1 SUPERADA] C1 ejecutado a +{1} ticks (+${2:F2}). Stop blindado en BREAKEVEN -1tk ({3:F2}). ┬íRiesgo CERO!",
                                Time[0], Zone1Tp1Ticks, Zone1Tp1Ticks * TickSize * Instrument.MasterInstrument.PointValue, entryPrice - (1 * TickSize)));
                        }

                        // CONTRATO 2: Target L├¡mite pasivo a +16 ticks (Zone1Tp2Ticks) ejecutado o detectado
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
                                Print(string.Format("{0} - [PAS - Z1-100B | RUNNER AHOGADO EN EL PISO] Salida a Mercado @ ~{1:F2} (Piso: {2:F2}). ┬íGanancia m├íxima asegurada!",
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
                                Print(string.Format("{0} - [PAS - Z1-100B | SALIDA EN BREAKEVEN] Posici├│n restante protegida en {1:F2}. Salida sin p├⌐rdida.",
                                    Time[0], bePrice));
                            }
                        }
                        // Stop Inicial antes de C1 (L├¡nea Media con TOPE DE 14 TICKS):
                        else
                        {
                            double naturalSl = keltnerBig.Midline[0] + (SlOffsetTicks * TickSize);
                            double slPrice = Math.Min(naturalSl, entryPrice + (14 * TickSize));

                            if (Close[0] >= slPrice || High[0] >= slPrice)
                            {
                                ExitShort("SL_Midline_Zona1", "Z1-100Bar_Short");
                                Print(string.Format("{0} - [PAS - Z1-100B | SL MIDLINE] Precio toc├│ Midline ({1:F2}) | Salida a Mercado @ ~{2:F2}",
                                    Time[0], slPrice, Close[0]));
                            }
                        }
                    }
                }
                // --- GESTI├ôN AS 4: CASCADA SURFER MONOCONTRATO (CANDADO $180 Y FIN DE CICLO) ---
                else if (currentAce == AceType.As4_Cascada50)
                {
                    double currentFavorableTicks = (entryPrice - Low[0]) / TickSize;
                    double currentProfitDollars  = currentFavorableTicks * (Instrument.MasterInstrument.PointValue * TickSize);
                    double effectiveLockTrigger  = CascadaLockTriggerDollars * instrumentMultiplier;

                    // CANDADO DE $180: Al superar $180 a favor ($18 en MES), el candado se arma
                    if (!isCascadaLockActive && currentProfitDollars >= effectiveLockTrigger)
                    {
                        isCascadaLockActive = true;
                        Print(string.Format("{0} - [PAS - Cascada | CANDADO ACTIVADO - CORTO] Ganancia flotante alcanz├│ {1:C2} (+{2:F1} tks). Candado armado en Lower 1.75 +1tk.",
                            Time[0], currentProfitDollars, currentFavorableTicks));
                    }

                    // 1. Salida por Candado Activo (Lower 1.75 + 1 tick tras superar $180 USD)
                    if (isCascadaLockActive)
                    {
                        double lockSl = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Lower[0] + (1 * TickSize));
                        if (Close[0] >= lockSl || High[0] >= lockSl)
                        {
                            cascadaQuarantineEndTime = Time[0].AddMinutes(CascadaQuarantineMinutes);
                            ExitShort("SL_Candado_Cascada", "Cascada50_Short");
                            Print(string.Format("{0} - [PAS - Cascada v10.1 | CORTO COBRADO CON CANDADO] Precio super├│ Lower 1.75 +1tk ({1:F2}) | Salida a Mercado @ ~{2:F2} (Ganancia asegurada). Cuarentena de {3} min armada.",
                                Time[0], lockSl, Close[0], CascadaQuarantineMinutes));
                            return;
                        }
                    }

                    // 2. Salida por Fin de Ciclo del R├ípido (Repliegue adentro de Banda 1.75 o freno de pendiente tras ganar >= 14 ticks)
                    double normMidSlopeShort = GetNormalizedSlope(keltnerBig.Midline);
                    if (peakGainTicks >= 14 && (Close[0] > keltnerSmall.Lower[0] || normMidSlopeShort >= 0.0))
                    {
                        ExitShort("Exit_Cascada_CicloTerminado", "Cascada50_Short");
                        Print(string.Format("{0} - [PAS - Cascada v10.1 | FIN DE R├üPIDO] Repliegue adentro de Lower 1.75 ({1:F2}) o freno de pendiente ({2:F2}) tras ganar {3:F1} tks pico. Salida a Mercado @ ~{4:F2}",
                            Time[0], keltnerSmall.Lower[0], normMidSlopeShort, peakGainTicks, Close[0]));
                        return;
                    }

                    // 3. Stop Loss Inicial Estructural (antes de alcanzar el Candado) en L├¡nea Media con tope de seguridad CascadaMaxSlTicks (24 ticks = -$300 m├íx)
                    double structuralSl = keltnerBig.Midline[0] + (2 * TickSize);
                    double maxSafeSl    = entryPrice + (CascadaMaxSlTicks * TickSize);
                    double dynamicSl    = Instrument.MasterInstrument.RoundToTickSize(Math.Min(structuralSl, maxSafeSl));
                    if (Close[0] >= dynamicSl || High[0] >= dynamicSl)
                    {
                        ExitShort("SL_BandaOpuesta_Cascada", "Cascada50_Short");
                        Print(string.Format("{0} - [PAS - Cascada v10.1 | CORTO CERRADO POR SL ESTRUCTURAL] Precio super├│ L├¡nea Media ({1:F2}) | Salida a Mercado @ ~{2:F2}",
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

            trancaActive = false;
            trancaPrice = 0.0;
            trancaBonusPrice = 0.0;
            trancaMilestoneLevel = 0.0;
            trancaArmedBar = -1;

            highestPriceSinceEntry = 0.0;
            lowestPriceSinceEntry = double.MaxValue;
        }

        #endregion

        #region Control de Cierre de Posici├│n y Estad├¡sticas Diarias
        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            if (marketPosition == MarketPosition.Flat)
            {
                // Sincronizaci├│n inmediata de contratos cerrados, strikes y c├ílculo de pisos
                SyncTradesAndStrikes();
                UpdateDailyPnLAndFloors();

                // Verificaci├│n de corte a las 12:30 PM si el trade que acaba de cerrar ocurri├│ despu├⌐s de las 12:30 PM (Solo Escal├│n 3 sigue activo)
                if (UseDailyManagement && UseTier1LunchCutoff && !isDailyLocked && currentDailyTier < 3)
                {
                    int timeNow = ToTime(Time[0]);
                    if (timeNow >= Tier1CutoffTime)
                    {
                        isDailyLocked = true;
                        dailyLockReason = string.Format("CORTE DE OPERATIVA A LAS 12:30 PM TRAS CIERRE (PnL ${0:F2})", dailyRealizedPnL);
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [ESCUDO DIARIO | CORTE A LAS 12:30 PM TRAS CIERRE DE TRADE] Trade cerrado despu├⌐s de las 12:30 PM EST ({1}).\n" +
                                           "PnL Diario Realizado: ${2:F2} USD (Zona {3}). ┬íAPAGANDO OPERATIVA POR EL RESTO DEL D├ìA!\n" +
                                           "****************************************************************************************",
                            Time[0], timeNow, dailyRealizedPnL, currentDailyTier == 2 ? "Escal├│n 2" : (currentDailyTier == 1 ? "Escal├│n 1" : "Base")));
                    }
                }

                // Si se cerr├│ Cascada, reseteo de contadores de fondo
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

                        // CUARENTENA DE SACIEDAD: Si gan├│ >= $180 USD ($18 MES) o sali├│ por Candado, congela Cascada por 60 minutos
                        double effectiveBigWinThreshold = CascadaBigWinThresholdDollars * instrumentMultiplier;
                        if (netProfit >= effectiveBigWinThreshold || (trade.Exit != null && trade.Exit.Name.Contains("Candado")))
                        {
                            cascadaQuarantineEndTime = trade.Exit.Time.AddMinutes(CascadaQuarantineMinutes);
                            Print(string.Format("****************************************************************************************\n" +
                                                "{0} - [PAS - Cascada v10.0 | ┬íQUIETO EN PRIMERA! CUARENTENA DE SACIEDAD ACTIVADA]\n" +
                                                "┬íQu├⌐ groser├¡a de trade! Ganamos +{1:C2} en Cascada. Candado asegurado.\n" +
                                                "Gatillo de Cascada congelado por {2} minutos (hasta las {3:HH:mm:ss} EST). ┬íA cuidar el bot├¡n!\n" +
                                                "****************************************************************************************",
                                trade.Exit.Time, netProfit, CascadaQuarantineMinutes, cascadaQuarantineEndTime));
                        }
                    }

                    Print(string.Format("{0} - [PAS | CONTRATO CERRADO] Salida: {1} | PnL Contrato: {2:C2} | PnL Diario Acumulado: {3:C2}",
                        trade.Exit.Time, trade.Exit.Name, netProfit, dailyRealizedPnL));
                }

                lastReportedTradeCount = SystemPerformance.AllTrades.Count;

                // REGLA DE 3 STRIKES POR OPERACI├ôN GLOBAL (CICLO FLAT -> FLAT):
                // Evaluamos el resultado neto combinado de todos los contratos de esta operaci├│n completa
                if (sessionTradesProfit < 0)
                {
                    consecutiveLosses++;
                    Print(string.Format("{0} - [PAS | STRIKE REGISTRADO] Operaci├│n global cerrada con p├⌐rdida neta ({1:C2}). Strikes consecutivos: {2}/{3}",
                        Time[0], sessionTradesProfit, consecutiveLosses, MaxConsecutiveLosses));
                }
                else if (sessionTradesProfit > 0)
                {
                    consecutiveLosses = 0;
                    Print(string.Format("{0} - [PAS | STRIKES REINICIADOS] Operaci├│n global cerrada con ganancia neta ({1:C2}). Contador de strikes en 0.",
                        Time[0], sessionTradesProfit));
                }

                if (consecutiveLosses >= MaxConsecutiveLosses)
                {
                    isDailyLocked = true;
                    dailyLockReason = string.Format("{0} STRIKES CONSECUTIVOS", MaxConsecutiveLosses);
                    Print(string.Format("****************************************************************************************\n" +
                                       "{0} - [REGLA DE 3 STRIKES] {1} p├⌐rdidas consecutivas registradas. PnL Diario: ${2:F2}.\n" +
                                       "Operativa bloqueada por el resto de la sesi├│n para proteger capital.\n" +
                                       "****************************************************************************************",
                        Time[0], MaxConsecutiveLosses, dailyRealizedPnL));
                }
            }
        }

        private void UpdateDailyPnLAndFloors()
        {
            if (!UseDailyManagement) return;

            // 1. Sincronizaci├│n continua de PnL Realizado de la sesi├│n actual directamente de la colecci├│n oficial
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
                // Escal├│n 3 / Modo Dulce: >= $900 USD ($90 MES) -> ┬íSigue operando y sube el piso con Trailing!
                if (dailyRealizedPnL >= DailyGoal_Piso3)
                {
                    currentDailyTier = 3;
                    peakDailyClosedPnL = Math.Max(peakDailyClosedPnL, dailyRealizedPnL);
                    // REGLA DE ORO DE MODO DULCE (v11.2): El piso NUNCA baja de DailyGoal_Piso3 ($900 ES / $90 MES)
                    double dynamicFloor = Math.Max(DailyGoal_Piso3, peakDailyClosedPnL - DailyTrailingGiveback);
                    if (dynamicFloor > currentDailyFloor)
                    {
                        currentDailyFloor = dynamicFloor;
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [ESCUDO DIARIO v11.2 | ┬íMODO DULCE BLINDADO!]\n" +
                                           "┬íQu├⌐ groser├¡a de d├¡a! PnL Diario: ${1:F2} USD. Piso dulce blindado en +${2:F2} USD (Inviolable).\n" +
                                           "Trailing Infinito activo buscando el infinito sin permitir caer jam├ís de los $900.\n" +
                                           "****************************************************************************************",
                            Time[0], dailyRealizedPnL, currentDailyFloor));
                    }
                }
                // Escal├│n 2: >= $600 USD ($60 MES) -> Piso Protegido de +$500 USD (+ $50 MES)
                else if (dailyRealizedPnL >= DailyGoal_Piso2)
                {
                    if (currentDailyFloor < DailyFloor_Piso2)
                    {
                        currentDailyTier = 2;
                        currentDailyFloor = DailyFloor_Piso2;
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [ESCUDO DIARIO | ┬íSEGUNDO ESCAL├ôN CONQUISTADO!]\n" +
                                           "┬íQu├⌐ nivel mi pez! Subiste al segundo escal├│n. PnL Diario: ${1:F2} USD.\n" +
                                           "Ya te llevas m├¡nimo en tu bolsillo ${2:F2} lukas blindadas.\n" +
                                           "Ahora ap├║ntele a las ${3:F2} lukas para activar el Modo Dulce y soltar el trailing infinito. ┬íEso es trading de verdad!\n" +
                                           "****************************************************************************************",
                            Time[0], dailyRealizedPnL, currentDailyFloor, DailyGoal_Piso3));
                    }
                }
                // Escal├│n 1: >= $400 USD ($40 MES) -> Piso Protegido de +$250 USD (+ $25 MES)
                else if (dailyRealizedPnL >= DailyGoal_Piso1)
                {
                    if (currentDailyFloor < DailyFloor_Piso1)
                    {
                        currentDailyTier = 1;
                        currentDailyFloor = DailyFloor_Piso1;
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [ESCUDO DIARIO | ┬íPRIMER ESCAL├ôN CONQUISTADO!]\n" +
                                           "┬íUfff parsero, ya subiste el primer escal├│n! PnL Diario: ${1:F2} USD.\n" +
                                           "Ya te llevas m├¡nimo en tu bolsillo ${2:F2} lukas aseguradas.\n" +
                                           "Sigue para el siguiente nivel que t├║ puedes, solo tienes que llegar a ${3:F2} lukas y estamos en el otro nivel. ┬íVamos con toda!\n" +
                                           "****************************************************************************************",
                            Time[0], dailyRealizedPnL, currentDailyFloor, DailyGoal_Piso2));
                    }
                }
                // Si el PnL cerrado cay├│ por debajo del piso protegido ya establecido
                else if (currentDailyFloor > -DailyLossLimit && dailyRealizedPnL <= currentDailyFloor)
                {
                    isDailyLocked = true;
                    dailyLockReason = string.Format("PISO DE PROTECCI├ôN (${0:F2}) ALCANZADO", currentDailyFloor);
                    Print(string.Format("{0} - [ESCUDO DIARIO | CIERRE POR PISO] PnL Diario Realizado cay├│ a ${1:F2} (Piso protegido: ${2:F2}). Operativa bloqueada por el resto del d├¡a.",
                        Time[0], dailyRealizedPnL, currentDailyFloor));
                }
            }
        }
        #endregion

        #region Interfaz de Control WPF (Bot├│n de P├ínico y Atajo Ctrl + Espacio)
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
                ToolTip = "Clic o presiona [Ctrl + Espacio] para Alternar / P├üNICO (Liquida posiciones inmediatamente)."
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
                        Print(Time[0] + " - [BOT├ôN DE P├üNICO] ┬íORDEN DE P├üNICO EJECUTADA! Posiciones liquidadas a mercado.");
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
        [Display(Name = "Versi├│n", GroupName = "1. Informaci├│n", Order = 1)]
        [ReadOnly(true)]
        public string Version { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "N├║mero de Contratos", Description = "Contratos a operar (1, 2 o 3 para escuadr├│n completo)", GroupName = "2. Gesti├│n de Posici├│n", Order = 1)]
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
        [Display(Name = "Activar Gesti├│n Diaria", Description = "Activa Cortacircuito y Meta Diaria", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 1)]
        public bool UseDailyManagement { get; set; }

        [NinjaScriptProperty]
        [Range(10.0, 5000.0)]
        [Display(Name = "Cortacircuito Diario Inicial ($)", Description = "P├⌐rdida m├íxima inicial para apagar bot ($800 ES / $80 MES - 2.5 a 3 vidas)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 2)]
        public double DailyLossLimit { get; set; }

        [NinjaScriptProperty]
        [Range(50.0, 5000.0)]
        [Display(Name = "Meta Escal├│n 1 ($)", Description = "Meta para activar Piso 1 ($450 ES / $45 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 3)]
        public double DailyGoal_Piso1 { get; set; }

        [NinjaScriptProperty]
        [Range(10.0, 5000.0)]
        [Display(Name = "Piso Protegido Escal├│n 1 ($)", Description = "Piso m├¡nimo garantizado tras tocar Meta 1 ($300 ES / $30 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 4)]
        public double DailyFloor_Piso1 { get; set; }

        [NinjaScriptProperty]
        [Range(100.0, 10000.0)]
        [Display(Name = "Meta Escal├│n 2 ($)", Description = "Meta para activar Piso 2 ($750 ES / $75 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 5)]
        public double DailyGoal_Piso2 { get; set; }

        [NinjaScriptProperty]
        [Range(50.0, 10000.0)]
        [Display(Name = "Piso Protegido Escal├│n 2 ($)", Description = "Piso m├¡nimo garantizado tras tocar Meta 2 ($550 ES / $55 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 6)]
        public double DailyFloor_Piso2 { get; set; }

        [NinjaScriptProperty]
        [Range(200.0, 20000.0)]
        [Display(Name = "Umbral Modo Dulce ($)", Description = "Meta para activar Trailing Infinito ($1,000 ES / $100 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 7)]
        public double DailyGoal_Piso3 { get; set; }

        [NinjaScriptProperty]
        [Range(10.0, 2000.0)]
        [Display(Name = "Colch├│n Trailing Infinito ($)", Description = "Distancia de retroceso permitida del r├⌐cord ganado ($150 ES / $15 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 8)]
        public double DailyTrailingGiveback { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "M├íximo Strikes Seguidos", Description = "N├║mero de p├⌐rdidas seguidas para apagar por el d├¡a", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 9)]
        public int MaxConsecutiveLosses { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Corte de Mediod├¡a", Description = "Si a las 12:30 PM EST estamos fuera de Modo Dulce (< $900), apaga nuevas entradas y asegura la ganancia", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 10)]
        public bool UseTier1LunchCutoff { get; set; }

        [NinjaScriptProperty]
        [Range(100000, 160000)]
        [Display(Name = "Hora L├¡mite Mediod├¡a (HHmmss)", Description = "Hora l├¡mite de corte para apagar antes del almuerzo (120000 = 12:00 PM EST)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 11)]
        public int Tier1CutoffTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Tranca Universal de Escal├│n", Description = "Al alcanzar la meta de cualquier piso ($400, $600 o $900), asegura ese nivel con una tranca indestructible y busca un bonus", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 12)]
        public bool UseTrancaUniversal { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Tranca Solo Desde Escal├│n 2 ($600+)", Description = "Deja correr libre el trade en piso inicial y solo tranca al tocar Escal├│n 2 ($600) o Modo Dulce ($900)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 13)]
        public bool TrancaOnlyFromTier2 { get; set; }

        [NinjaScriptProperty]
        [Range(10.0, 500.0)]
        [Display(Name = "Tranca D├│lares de Sprint Bonus ($)", Description = "Ganancia adicional en USD a buscar tras clavar la tranca del escal├│n ($100 ES / $10 MES por defecto)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 14)]
        public double TrancaBonusDollars { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 50.0)]
        [Display(Name = "Tranca Colch├│n de Comisiones ($)", Description = "Margen de seguridad sobre el piso para garantizar que quede limpio tras comisiones ($10 ES / $1 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 15)]
        public double TrancaSafetyDollars { get; set; }

        [NinjaScriptProperty]
        [Range(20.0, 500.0)]
        [Display(Name = "Tranca Distancia M├¡nima Filtro Anti-Asfixia ($)", Description = "Distancia m├¡nima en USD para armar la Tranca. Si falta menos de esto para el escal├│n, se apaga la tranca para no asfixiar el trade ($125 ES / $12.5 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 16)]
        public double MinTrancaDistanceDollars { get; set; }

        [NinjaScriptProperty]
        [Range(5.0, 200.0)]
        [Display(Name = "Tranca Colch├│n Conquista Adamantium ($)", Description = "Penetraci├│n requerida por encima del escal├│n para armar la Tranca de Adamantium ($50 ES / $5 MES = 4 ticks)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 17)]
        public double TrancaConquestBufferDollars { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Tranca Anclaje de Titanio sobre Escal├│n ($)", Description = "Margen de anclaje por encima de cada escal├│n para sellar la entrada ($20 ES / $2 MES = $420, $620, $920)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 18)]
        public double TrancaAnchorDollars { get; set; }

        [NinjaScriptProperty]
        [Range(2, 5)]
        [Display(Name = "SOBB Barras Consecutivas Requeridas", Description = "N├║mero de barras consecutivas 100% afuera requeridas para evaluar confirmaci├│n (2 por defecto)", GroupName = "5. As 2: SOBB (Escalones)", Order = 0)]
        public int SobbRequiredFloatingBars { get; set; }

        [NinjaScriptProperty]
        [Range(2, 6)]
        [Display(Name = "SOBB Ventana M├íxima Barras Afuera", Description = "Ventana m├íxima de barras afuera para confirmar rompimiento antes de cancelar (3 por defecto)", GroupName = "5. As 2: SOBB (Escalones)", Order = 1)]
        public int SobbMaxFloatingBarsWindow { get; set; }

        [NinjaScriptProperty]
        [Range(4, 50)]
        [Display(Name = "SOBB TP1 Asegurador (Ticks)", Description = "Ticks para liquidar C1 Asegurador o pasar a BE (+10 ticks = $125 ES / $12.50 MES)", GroupName = "5. As 2: SOBB (Escalones)", Order = 2)]
        public int SobbTp1Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(8, 100)]
        [Display(Name = "SOBB TP2 Expansi├│n (Ticks)", Description = "Ticks para liquidar en zona de expansi├│n (+20 ticks = $250 ES / $25 MES)", GroupName = "5. As 2: SOBB (Escalones)", Order = 3)]
        public int SobbTp2Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 50)]
        [Display(Name = "SOBB SL M├íximo de Seguridad (Ticks)", Description = "Tope m├íximo de p├⌐rdida para el SL Estructural (24 ticks = 6 pts = $300 ES / $30 MES)", GroupName = "5. As 2: SOBB (Escalones)", Order = 4)]
        public int SobbMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 200)]
        [Display(Name = "SOBB C3 Umbral Ahogo (Ticks)", Description = "Ticks para activar Modo Ahogo para C3 Runner (+30 ticks = $375 ES / $37.50 MES)", GroupName = "5. As 2: SOBB (Escalones)", Order = 5)]
        public int SobbChokeThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(2, 20)]
        [Display(Name = "SOBB C3 Distancia Ahogo (Ticks)", Description = "Ticks de separaci├│n del High/Low para el trailing del Runner", GroupName = "5. As 2: SOBB (Escalones)", Order = 6)]
        public int SobbChokeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 1.0)]
        [Display(Name = "SOBB Umbral Pendiente Tendencia", Description = "Umbral de Pendiente Normalizada (LinReg 5 / ATR 14) para validar rompimiento con tendencia institucional en SOBB (0.12 por defecto)", GroupName = "5. As 2: SOBB (Escalones)", Order = 7)]
        public double SobbSlopeThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(10, 300)]
        [Display(Name = "Z1 Barras M├¡nimas", Description = "M├¡nimo de barras comprimidas sin cruzar EMA 200", GroupName = "6. As 1: Zona 1", Order = 1)]
        public int MinBarsZona1 { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Z1 Ventana M├íx a L├¡nea Media (Barras)", Description = "Ventana m├íxima de barras tras cruce TEMA para que EMA 200 toque la L├¡nea Media (40 barras por defecto)", GroupName = "6. As 1: Zona 1", Order = 2)]
        public int Z1MaxBarsToMidline { get; set; }

        [NinjaScriptProperty]
        [Range(2, 30)]
        [Display(Name = "Z1 M├íx Ticks en Zona 0", Description = "Tolerancia m├íxima de ticks por encima/debajo de Banda 3.5 en Zona 0 (10 ticks por defecto)", GroupName = "6. As 1: Zona 1", Order = 3)]
        public int Z1MaxZone0Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(4, 30)]
        [Display(Name = "Z1 C1 TP (Ticks)", Description = "Target de salida de C1 y activaci├│n de BE (8 ticks = $100 ES / $10 MES)", GroupName = "6. As 1: Zona 1", Order = 4)]
        public int Zone1Tp1Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(8, 50)]
        [Display(Name = "Z1 C2 TP (Ticks)", Description = "Target de salida de C2 (16 ticks = $200 ES / $20 MES)", GroupName = "6. As 1: Zona 1", Order = 5)]
        public int Zone1Tp2Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(2, 20)]
        [Display(Name = "Z1 C3 Ahogo (Ticks)", Description = "Distancia ce├▒ida para el trailing del C3 Runner (8 ticks)", GroupName = "6. As 1: Zona 1", Order = 6)]
        public int Zone1ChokeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 1.0)]
        [Display(Name = "Z1 Umbral Pendiente Tendencia", Description = "Umbral de Pendiente Normalizada (LinReg 5 / ATR 14) para validar toque a L├¡nea Media con tendencia institucional en Z1-100Bar (0.15 por defecto)", GroupName = "6. As 1: Zona 1", Order = 7)]
        public double Zona1SlopeThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cascada Hora Inicio (HHMMSS)", Description = "Hora m├¡nima para disparar Cascada (10:00 AM = 100000 para evitar trampas de pre-mercado)", GroupName = "7. As 4: Cascada Surfer", Order = 0)]
        public int CascadaStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, 3)]
        [Display(Name = "Cascada Contratos Modo Exploratorio", Description = "Contratos de Cascada en modo exploratorio (1 contrato)", GroupName = "7. As 4: Cascada Surfer", Order = 1)]
        public int CascadaFirstEntryContracts { get; set; }

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "Cascada Barras M├¡nimas", Description = "Barras con EMA 200 separada de Banda 3.5 (25 por defecto)", GroupName = "7. As 4: Cascada Surfer", Order = 1)]
        public int CascadaMinBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Cascada Separaci├│n Ticks", GroupName = "7. As 4: Cascada Surfer", Order = 2)]
        public int CascadaSeparationTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Cascada Pendiente M├¡nima Ticks", GroupName = "7. As 4: Cascada Surfer", Order = 3)]
        public int CascadaMinSlopeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(20.0, 500.0)]
        [Display(Name = "Cascada Candado Trigger (USD)", Description = "Umbral de ganancia flotante en USD para activar el Candado din├ímico ($180 ES / $18 MES)", GroupName = "7. As 4: Cascada Surfer", Order = 4)]
        public double CascadaLockTriggerDollars { get; set; }

        [NinjaScriptProperty]
        [Range(6, 40)]
        [Display(Name = "Cascada SL M├íximo de Seguridad (Ticks)", Description = "Tope m├íximo de p├⌐rdida estructural inicial (24 ticks = 6 pts = $300 ES / $30 MES)", GroupName = "7. As 4: Cascada Surfer", Order = 5)]
        public int CascadaMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(20.0, 3000.0)]
        [Display(Name = "Cascada Umbral S├║per Win (USD)", Description = "Ganancia m├¡nima en USD para activar la Cuarentena de Saciedad ($180 ES / $18 MES)", GroupName = "7. As 4: Cascada Surfer", Order = 6)]
        public double CascadaBigWinThresholdDollars { get; set; }

        [NinjaScriptProperty]
        [Range(10, 240)]
        [Display(Name = "Cascada Cuarentena (Minutos)", Description = "Minutos de congelamiento de Cascada tras una victoria (30 minutos por defecto)", GroupName = "7. As 4: Cascada Surfer", Order = 7)]
        public int CascadaQuarantineMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 1.0)]
        [Display(Name = "Cascada Umbral Pendiente R├ípido", Description = "Umbral de aceleraci├│n de Pendiente Normalizada (LinReg 5 / ATR 14) para disparar r├ípido de Cascada (0.15 por defecto)", GroupName = "7. As 4: Cascada Surfer", Order = 8)]
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
        [Range(5, 50)]
        [Display(Name = "Per├¡odo TEMA", GroupName = "8. Indicadores", Order = 1)]
        public int TemaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(20, 500)]
        [Display(Name = "Per├¡odo EMA 200", GroupName = "8. Indicadores", Order = 2)]
        public int EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(10, 200)]
        [Display(Name = "Per├¡odo Keltner", GroupName = "8. Indicadores", Order = 3)]
        public int KeltnerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 6.0)]
        [Display(Name = "Multiplicador Keltner Grande", GroupName = "8. Indicadores", Order = 4)]
        public double KeltnerBigMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 3.0)]
        [Display(Name = "Multiplicador Keltner Peque├▒o", GroupName = "8. Indicadores", Order = 5)]
        public double KeltnerSmallMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 10.0)]
        [Display(Name = "M├¡nimo Cruce TEMA (Ticks)", GroupName = "8. Indicadores", Order = 6)]
        public double MinCrossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Offset Stop Loss (Ticks)", GroupName = "8. Indicadores", Order = 7)]
        public int SlOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(2, 20)]
        [Display(Name = "Colch├│n M├¡nimo Seguridad SL (Ticks)", GroupName = "8. Indicadores", Order = 8)]
        public int MinSafetySlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "Ratchet Lookback Barras", Description = "Barras sin tocar EMA 200", GroupName = "9. As 3: Ratchet", Order = 1)]
        public int RatchetLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(5, 30)]
        [Display(Name = "Ratchet Countdown Barras", Description = "Ventana m├íxima para cruzar L├¡nea Media", GroupName = "9. As 3: Ratchet", Order = 2)]
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
        [Display(Name = "Ratchet Chicle Trailing (Ticks)", Description = "Distancia ce├▒ida del Chicle Parab├│lico para C3 Runner", GroupName = "9. As 3: Ratchet", Order = 6)]
        public int RatchetChicleTicks { get; set; }

        [NinjaScriptProperty]
        [Range(6, 30)]
        [Display(Name = "Ratchet SL M├íximo de Seguridad (Ticks)", Description = "Tope m├íximo de p├⌐rdida inicial para Ratchet (12 ticks = $450 ES / $45 MES en 3 contratos)", GroupName = "9. As 3: Ratchet", Order = 7)]
        public int RatchetMaxSlTicks { get; set; }
        #endregion
    }
}
