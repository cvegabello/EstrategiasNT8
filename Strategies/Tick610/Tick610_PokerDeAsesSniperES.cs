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
// VERSIÓN: 15.0 - Poker de Ases (PAS) [Cascada: Trailing Banda 1.75 por Cierre Estricto Close[0], Filtro TEMA9 5b & Reintento 5b]
//
// LOS ASES ACTIVOS:
//   1. As 4: [Cascada]   - El Tren Bala Institucional con Entrada Rápida Pura (|m| >= 0.15 por 2 barras)
//                          - Filtro Micro: Pendiente TEMA 9 en 5 barras a favor del trade (tema9[0] > tema9[5] en Long / < tema9[5] en Short).
//                          - Filtro Macro: EMA 200 por debajo de Línea Media (Long) o por arriba de Línea Media (Short).
//                          - Gestión Estructural Pura v15.1:
//                            * Stop Inicial en Línea Media si entra dentro del canal.
//                            * Trailing Stop en Banda Pequeña 1.75 por Cierre Estricto de Vela (Close[0]) en cuanto la vela flota 100% afuera de Banda 3.5.
//                            * Ignora mechas de absorción/rechazo intra-vela permitiendo capturar Home Runs sin asfixiar la posición.
//                            * Cero parámetros artificiales de MFE ni candados forzados.
//                          - Reintento Quirúrgico (Segundo Boleto): 1 único reintento tras salir vivo por mecha sin ganar $180,
//                            requiriendo un enfriamiento obligatorio de >= 5 barras antes de volver a evaluar condiciones.
//                          - Cuarentena de Saciedad 100% Real: Si gana >= $180 USD netos en bolsillo ($18 MES), cero reintentos y 30 min de congelamiento.
//   2. As 3: [Ratchet]   - Súper-tendencia vertical institucional real (Sniper latente): Requiere OBLIGATORIAMENTE que EMA 200 esté
//                          FUERA del Canal Grande 3.5 y que la Línea Media tenga pendiente a favor. Stop inicial de 12 ticks y Chicle Parabólico.
// (As 1: Z1-100Bar y As 2: SOBB desactivados permanentemente tras auditoría histórica de 30 sesiones y 79 trades).
//
// GESTIÓN DE RIESGO INTELIGENTE Y HORARIO INSTITUCIONAL:
//   - Corte de Operativa a las 12:00 PM EST (Tier1CutoffTime = 120000): Protege el capital eliminando el mediodía picado (-$755.52 ahorrados).
//   - Cortacircuito Inicial: -$650 USD (ES) / -$65 USD (MES).
//   - Escalón 1 (+$400): Piso protegido en +$250 USD.
//   - Escalón 2 (+$600): Piso protegido en +$500 USD.
//   - Modo Dulce (+$900+): Trailing Infinito (Piso = Récord Ganado - Colchón $150).
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

        // === VARIABLES DEL AS 4: CASCADA (CAZADOR DE RÁPIDOS POR PENDIENTE NORMALIZADA + CANDADO SUELO $50) ===
        private int barsCascadaShortCount = 0;     // Barras con EMA200 > Upper 3.5 + 4 ticks (conteo de fondo)
        private int barsCascadaLongCount  = 0;     // Barras con EMA200 < Lower 3.5 - 4 ticks (conteo de fondo)
        private bool cascadaCycleLongFired  = false; // Bandera de 1 solo tiro por impulso en Largos (Anti-compras en techos)
        private bool cascadaCycleShortFired = false; // Bandera de 1 solo tiro por impulso en Cortos (Anti-ventas en suelos)
        private bool isCascadaLockActive  = false; // Candado activado (salida en Banda 1.75 favorable con suelo mínimo de +$50 USD)
        private bool cascadaC1Achieved    = false; // Contrato 1 cerrado a +8 ticks (BE armado)
        private bool cascadaC2Achieved    = false; // Contrato 2 cerrado a +16 ticks
        private bool cascadaChokeActive   = false; // Contrato 3 Runner en Modo Ahogo (8 ticks)
        private int  cascadaTradesToday   = 0;     // Contador de disparos de Cascada en la sesión diaria actual
        private bool cascadaHasWonToday   = false; // Bandera: Cascada ya cobró un trade ganador hoy (Pisar Suave liberado)
        private DateTime cascadaQuarantineEndTime = DateTime.MinValue; // Fin de la cuarentena tras victoria (>= $180 USD netos)
        private int cascadaConsecutiveSlopeBarsLong  = 0; // Barras consecutivas con Pendiente Normalizada >= +0.15 (v12.0)
        private int cascadaConsecutiveSlopeBarsShort = 0; // Barras consecutivas con Pendiente Normalizada <= -0.15 (v12.0)
        private double cascadaEntryOutsideTicks          = 0.0;   // Distancia en ticks fuera de Banda 3.5 al abrir la posicion (v15.1)
        private double cascadaClimaxTargetTicks          = 0.0;   // Meta en ticks de distancia a Banda 3.5 para salida por Climax Dinamico (v15.1)
        private bool   cascadaHasClimaxTarget            = false; // Bandera de salida activa por Climax Dinamico de Banda 3.5 (v15.1)
        private int    cascadaEnteredZone                = 0;     // Zona de entrada (0: <2tk, 1: 2-10tk, 2: 10-15tk, 3: 15-25tk, 4: 25-30tk) (v15.1)
        private double cascadaEntryMidlineDistTicks      = 0.0;   // Distancia en ticks a Linea Media al abrir la posicion para Zona 0 (v15.1)
        private double cascadaClimaxMidlineTargetTicks   = 0.0;   // Meta en ticks de distancia a Linea Media (1.5x) para Zona 0 (v15.1)
        private bool   cascadaHasMidlineClimaxTarget     = false; // Bandera de salida activa por Climax de Linea Media (v15.1)
        private double cascadaOutsideInitialSl       = 0.0;   // Precio de SL inicial anclado en la Banda 3.5 para salida rápida en ruptura fallida
        private bool cascadaSecondChanceLongAvailable  = false; // Bandera v15.1: Habilita 1 único Segundo Boleto en Largo tras salida por mecha sin $180
        private bool cascadaSecondChanceShortAvailable = false; // Bandera v15.1: Habilita 1 único Segundo Boleto en Corto tras salida por mecha sin $180
        private bool cascadaSecondChanceLongUsed       = false; // Bandera v15.1: Garantiza máximo 1 Segundo Boleto por ciclo alcista
        private bool cascadaSecondChanceShortUsed      = false; // Bandera v15.1: Garantiza máximo 1 Segundo Boleto por ciclo bajista
        private int cascadaLastExitBar                 = -999;  // Barra donde cerró el último trade de Cascada para enfriamiento obligatorio de >= 5 barras
        private bool cascadaReachedOutsideBigBand      = false; // Indica si el trade alcanzó o rompió hacia afuera de la Banda Grande 3.5
        private int lastCascadaDirection               = 0;     // Dirección de Cascada: 1 = Long, -1 = Short
        private int entryBarGlobal                     = -1;    // Barra donde se ejecutó la entrada activa

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

        // === TRANCA DE ADAMANTIUM (GARRAS DE WOLVERINE & FILTRO ANTI-ASFIXIA) ===
        private bool   trancaActive = false;         // Bandera: ¿Se activó la tranca de adamantium en este trade?
        private double trancaPrice = 0.0;            // Precio del Stop de adamantium que asegura el nuevo escalón neto
        private double trancaBonusPrice = 0.0;       // Precio del TP Sprint Bonus (+ $100 extra)
        private double trancaMilestoneLevel = 0.0;   // Nivel del escalón conquistado ($400, $600 o $900)
        private int    trancaArmedBar = -1;          // Barra donde se clavó la Tranca para evitar falso autodisparo en misma vela

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
                Description                                 = @"Estrategia Institucional Poker de Ases Sniper (PAS) v15.1 en 610 Ticks para ES/MES. Cascada Pura 0.15 y Ratchet Sniper con SL Ceñido en Banda 3.5, Trailing Banda 1.75 por Cierre Estricto de Vela (Close[0]).";
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
                Version                                     = "15.0 - Poker de Ases (PAS) [Cascada: Trailing Banda 1.75 por Cierre Estricto Close[0], Filtro TEMA9 5b & Reintento 5b]";

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

                // Parámetros del As 4: Cascada (Cazador de Rápidos por Pendiente Normalizada + Candado Suelo $50)
                CascadaStartTime                            = 100000; // 10:00 AM EST - Filtro Anti-Acomodación de Pre-Mercado
                CascadaFirstEntryContracts                  = 1;      // Modo exploratorio (1 contrato)
                CascadaMaxOutsideEntryTicks                 = 30;     // Tope maximo de distancia afuera de Banda 3.5 para permitir entrada (filtro anti-FOMO: 30 ticks por defecto)
                CascadaTier4ExtremaTicks                    = 25;     // Umbral de inicio de Zona 4 Extrema (25 a 30 ticks afuera)
                CascadaTier3AltaTicks                       = 15;     // Umbral de inicio de Zona 3 Alta (15 a 25 ticks afuera)
                CascadaTier2MediaTicks                      = 10;     // Umbral de inicio de Zona 2 Media (10 a 15 ticks afuera)
                CascadaTier1BajaTicks                       = 2;      // Umbral de inicio de Zona 1 Baja (2 a 10 ticks afuera)
                CascadaClimaxMultExt                        = 1.6;    // Multiplicador de climax para Zona 4 Extrema (25 a 30 ticks afuera -> 1.6x)
                CascadaClimaxMultHigh                       = 2.0;    // Multiplicador de climax para Zona 3 Alta (15 a 25 ticks afuera -> 2.0x)
                CascadaClimaxMultMid                        = 3.0;    // Multiplicador de climax para Zona 2 Media (10 a 15 ticks afuera -> 3.0x)
                CascadaClimaxMultLow                        = 5.0;    // Multiplicador de climax para Zona 1 Baja (2 a 10 ticks afuera -> 5.0x)
                CascadaClimaxMidlineMult                    = 1.5;    // Multiplicador de climax respecto a Linea Media para Zona 0 (< 2 ticks afuera -> 1.5x)
                CascadaSlopeConfirmBars                     = 2;      // Barras consecutivas requeridas con Pendiente Normalizada >= Umbral (2 por defecto)
                CascadaLockTriggerDollars                   = 250.0;  // Umbral de MFE en USD para activar Candado ($250 ES = +20 ticks / $25 MES en v13.4)
                CascadaMaxSlTicks                           = 24;     // Tope máximo de seguridad estructural inicial (24 ticks = 6 pts = $300 ES / $30 MES)
                CascadaBigWinThresholdDollars               = 180.0;  // Umbral de ganancia neta en bolsillo para activar Cuarentena de Saciedad ($180 ES / $18 MES)
                CascadaQuarantineMinutes                    = 30;     // Minutos de cuarentena/congelamiento de Cascada tras victoria (30 min)
                CascadaTp1Ticks                             = 8;      // (Compatibilidad NT8)
                CascadaTp2Ticks                             = 16;     // (Compatibilidad NT8)
                CascadaChokeTicks                           = 8;      // (Compatibilidad NT8)
                CascadaSlopeThreshold                       = 0.15;   // Umbral de aceleración de Pendiente Normalizada LinReg/ATR para Cascada (0.15 puro para todo el mundo)
                CascadaSecondTicketSlopeThreshold           = 0.18;   // Umbral de Pendiente Normalizada para 2do Boleto (> 0.18 estricto: no inclusive)   // Umbral de Pendiente Normalizada mÃ¡s exigente para 2do Boleto (+0.02 vs base: > 0.17 por defecto)
                CascadaOutsideThresholdTicks                = 8;      // Margen de ticks fuera de Banda 3.5 (>= 8 ticks activa SL Oxígeno en Sobreextensión; < 8 ticks tratada como adentro con SL Línea Media)
                CascadaOutsideSlTicks                       = 12;     // Oxígeno de Stop Loss para tiros en sobreextensión exterior (12 ticks = 3.0 pts = -$150 ES / -$15 MES)
                CascadaExitOnCloseOnly                      = true;   // Salida estricta por cierre de vela Close[0] en Banda 1.75 (ignora mechas de absorción/rechazo intra-vela)
                CascadaBandOffsetTicks                      = 0;      // Respiro en ticks más allá de la Banda 1.75 (0 ticks por defecto con cierre de vela Close[0])
                SobbSlopeThreshold                          = 0.12;   // Umbral de Pendiente Normalizada LinReg/ATR para SOBB (0.12)
                Zona1SlopeThreshold                         = 0.15;   // Umbral de Pendiente Normalizada LinReg/ATR para Z1-100Bar (0.15)

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
                UseTier1LunchCutoff                         = true;   // Activar Victoria Temprana en Escalón 1 a las 12:00 PM EST
                Tier1CutoffTime                             = 120000; // 12:00 PM EST (Hora de corte de mediodía institucional v12.0)
                UseTrancaUniversal                          = true;   // Activar Tranca de Adamantium Universal en cada escalón
                TrancaOnlyFromTier2                         = true;   // Pista libre en piso inicial: solo clava tranca desde Escalón 2 ($600+) y Modo Dulce ($900)
                TrancaBonusDollars                          = 100.0;  // Dólares de Sprint Bonus tras clavar la tranca ($100 ES / $10 MES)
                TrancaSafetyDollars                         = 10.0;   // Colchón de seguridad para comisiones ($10 ES / $1 MES)
                MinTrancaDistanceDollars                    = 125.0;  // Filtro Anti-Asfixia: Mínimo $125 USD (10 ticks ES / $12.5 MES) de pista para armar tranca
                TrancaConquestBufferDollars                 = 50.0;   // Colchón Conquista Adamantium: Penetración de $50 USD (4 ticks en ES / $5 MES) para validar fuerza real
                TrancaAnchorDollars                         = 20.0;   // Anclaje de Titanio sobre el Escalón: +$20 USD ($420, $620, $920 en ES / $2 MES)
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
                    CascadaLockTriggerDollars = 25.0;
                    CascadaBigWinThresholdDollars = 18.0;
                    TrancaBonusDollars    = 10.0;
                    TrancaSafetyDollars   = 1.0;
                    MinTrancaDistanceDollars = 12.5;
                    TrancaConquestBufferDollars = 5.0;
                    TrancaAnchorDollars = 2.0;
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
                cascadaSecondChanceLongAvailable = false;
                cascadaSecondChanceShortAvailable = false;
                cascadaSecondChanceLongUsed = false;
                cascadaSecondChanceShortUsed = false;
                cascadaLastExitBar = -999;
                cascadaReachedOutsideBigBand = false;
                cascadaConsecutiveSlopeBarsLong = 0;
                cascadaConsecutiveSlopeBarsShort = 0;
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

            // 3.1 CORTACIRCUITO UNIVERSAL A LAS 12:00 PM (SOLO ESCALÓN 3 / MODO DULCE PUEDE CONTINUAR HASTA LAS 4:00 PM)
            // Si son las 12:00 PM EST y no estamos en el último escalón (Modo Dulce Trailing Infinito >= $900),
            // se apaga la operativa para evitar operar en la tarde picada o eventos de alta volatilidad (FED/FOMC/Almuerzo) y blindar las ganancias.
            if (UseDailyManagement && UseTier1LunchCutoff && !isDailyLocked && timeNow >= Tier1CutoffTime)
            {
                if (currentDailyTier < 3)
                {
                    if (Position.MarketPosition == MarketPosition.Flat)
                    {
                        isDailyLocked = true;
                        dailyLockReason = string.Format("CORTE DE OPERATIVA A LAS 12:00 PM (PnL ${0:F2})", dailyRealizedPnL);
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [ESCUDO DIARIO | CORTE A LAS 12:00 PM EST] Hora actual ({1}) alcanzó las 12:00 PM.\n" +
                                           "PnL Diario Realizado: ${2:F2} USD (Zona {3}). ¡APAGANDO OPERATIVA POR EL RESTO DEL DÍA!\n" +
                                           "No se regala dinero en la tarde sin volumen ni en trampas de la FED o almuerzo. ¡Día cerrado con disciplina!\n" +
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
                // Barra completamente afuera de Banda Grande 3.5 (suelo de la vela por encima del techo 3.5)
                if (Low[0] > keltnerBig.Upper[0]) cascadaReachedOutsideBigBand = true;
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (Low[0] < lowestPriceSinceEntry) lowestPriceSinceEntry = Low[0];
                // Barra completamente afuera de Banda Grande 3.5 (techo de la vela por debajo del suelo 3.5)
                if (High[0] < keltnerBig.Lower[0]) cascadaReachedOutsideBigBand = true;
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

                // CORTACIRCUITO DE PISO EXACTO EN TIEMPO REAL (v13.2 ANTI-ASFIXIA):
                // En Modo Dulce (currentDailyTier >= 3), el piso de $900 es sagrado y liquida en el tick exacto.
                // En Escalón 1 y 2 (Tier 1 y 2), no se estrangula al tick para dar respiro natural de oxígeno (12tk);
                // la protección de piso en Tier 1 y 2 se evalúa al cierre de la posición en SyncTradesAndStrikes/UpdateDailyPnLAndFloors.
                if (!isDailyLocked && Position.MarketPosition != MarketPosition.Flat && currentDailyTier >= 3 && currentDailyFloor > 0 && dailyRealizedPnL > currentDailyFloor)
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

                // CORTACIRCUITO EN VIVO (v13.2): Si el PnL total (realizado + flotante) toca el piso actual
                // En Tier 0 (-$650 límite inicial) y Tier 3+ (Modo Dulce), el Cortacircuito en vivo actúa inmediatamente.
                // En Tier 1 y 2, se le concede pista al trade con su SL natural (12 ticks); si cierra por debajo del piso, se bloquea tras el cierre.
                bool evaluateLiveCortacircuito = (currentDailyTier == 0 || currentDailyTier >= 3);
                if (!isDailyLocked && evaluateLiveCortacircuito && totalDailyFloatingPnL <= currentDailyFloor)
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
                // Reseteo estructural estricto del ciclo de Cascada (SOLO cuando el precio descansa físicamente en la Línea Media)
                if ((cascadaCycleLongFired || cascadaSecondChanceLongAvailable || cascadaSecondChanceLongUsed) && Low[0] <= keltnerBig.Midline[0])
                {
                    cascadaCycleLongFired = false;
                    cascadaSecondChanceLongAvailable = false;
                    cascadaSecondChanceLongUsed = false;
                }
                if ((cascadaCycleShortFired || cascadaSecondChanceShortAvailable || cascadaSecondChanceShortUsed) && High[0] >= keltnerBig.Midline[0])
                {
                    cascadaCycleShortFired = false;
                    cascadaSecondChanceShortAvailable = false;
                    cascadaSecondChanceShortUsed = false;
                }

                // GESTIÓN DE RIESGO INTELIGENTE: Verificamos si existe margen para operar al menos 1 contrato con Stop saludable
                if (GetRiskAdjustedContracts() <= 0)
                {
                    // No hay margen suficiente ni para 1 contrato sin arriesgar el piso; protegemos el capital
                    return;
                }

                // AS 1 y AS 2: Desactivados permanentemente en v12.0 tras auditoría histórica cuantitativa
                // EvaluateAs1_Zona1();
                // EvaluateAs2_SOBB();

                // AS 3: Ratchet (Chicle Parabólico Vertical Institucional - Sniper Latente)
                if (Position.MarketPosition == MarketPosition.Flat)
                    EvaluateAs3_Ratchet();

                // AS 4: Cascada (Cazador de Rápidos de Río con Filtro de Persistencia de Pendiente 2b)
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
                // Acumulación silenciosa (Z1 inactivo en v12.0)
                // if (timeNow >= StartTime && timeNow <= ForceCloseTime && barsBelowEmaWithoutCross % 25 == 0)
                //     Print(string.Format("{0} - [PAS - Z1-100B | LARGO ACUMULANDO] TEMA9 < EMA200 por {1}/{2} barras | Armed: {3}", Time[0], barsBelowEmaWithoutCross, MinBarsZona1, z1LongArmed));
            }
            else if (!z1WaitingForMidlineLong)
            {
                if (!z1LongArmed)
                {
                    barsBelowEmaWithoutCross = 0;
                }
            }

            // 2. Activación del Cronómetro de 40 Barras:
            // Al darse el cruce alcista (TEMA 9 cruza hacia arriba de EMA 200) tras haber acumular >= 100 barras
            if (z1LongArmed && !z1WaitingForMidlineLong && tema9[0] > ema200[0])
            {
                z1WaitingForMidlineLong = true;
                z1MidlineCountdownLong = 0;
                z1LongArmed = false;
                barsBelowEmaWithoutCross = 0;
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
                }
                // B) Si la TEMA 9 hace un gancho falso real y penetra por debajo de la EMA 200 por >= 1 tick (0.25 pts):
                else if ((ema200[0] - tema9[0]) >= (1.0 * TickSize))
                {
                    z1WaitingForMidlineLong = false;
                    z1MidlineCountdownLong = 0;
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
            }

            // 3. Monitoreo durante las 40 Barras de Corto:
            if (z1WaitingForMidlineShort)
            {
                z1MidlineCountdownShort++;

                if (z1MidlineCountdownShort > Z1MaxBarsToMidline)
                {
                    z1WaitingForMidlineShort = false;
                    z1MidlineCountdownShort = 0;
                }
                // B) Si la TEMA 9 hace un gancho falso real y penetra por encima de la EMA 200 por >= 1 tick (0.25 pts):
                else if ((tema9[0] - ema200[0]) >= (1.0 * TickSize))
                {
                    z1WaitingForMidlineShort = false;
                    z1MidlineCountdownShort = 0;
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
            double separationDist = 4 * TickSize;

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

            // === CONTADOR DE PERSISTENCIA DE PENDIENTE NORMALIZADA PARA CASCADA (v12.0) ===
            // Requiere que la aceleración institucional se sostenga al menos 2 barras consecutivas
            // para evitar falsas rupturas en agotamientos o micro-amagues efímeros.
            double currentMidSlope = GetNormalizedSlope(keltnerBig.Midline);

            if (currentMidSlope > CascadaSlopeThreshold)
                cascadaConsecutiveSlopeBarsLong++;
            else
                cascadaConsecutiveSlopeBarsLong = 0;

            if (currentMidSlope < -CascadaSlopeThreshold)
                cascadaConsecutiveSlopeBarsShort++;
            else
                cascadaConsecutiveSlopeBarsShort = 0;
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
                    // 2. Pendiente Normalizada positiva de la Línea Media (m/ATR >= Zona1SlopeThreshold)
                    bool pendienteMidlineAlcista = normSlope >= Zona1SlopeThreshold;

                    // 3. Ubicación del precio: Zona 1 o Zona 0 moderada (no más de 10 ticks sobre Upper 3.5)
                    bool precioEnZona = (Close[0] >= upperSmall) && (Close[0] <= upperBig + (Z1MaxZone0Ticks * TickSize));

                    // 4. Confirmación de TEMA 9 (azul) sin gancho falso bajo EMA 200 (tolerancia 1 tick)
                    bool confirmacionAlcista = (ema200[0] - tema9[0]) < (1.0 * TickSize);

                    if (pendienteMidlineAlcista && precioEnZona && confirmacionAlcista)
                    {
                        ExecuteAceEntry(AceType.As1_Zona1_100Bar, MarketPosition.Long, "Z1-100Bar_Long");
                        Print(string.Format("{0} - [PAS - Z1-100Bar v10.2 | ¡GOLAZO LARGO EJECUTADO!] Entrada @ ~{1:F2} en barra {2}/40 | Pendiente Normalizada: +{3:F2} (Mínimo Z1: +{4:F2}) | ATR: {5:F2} pts | TEMA9 ({6:F2}) > EMA200 ({7:F2})",
                            Time[0], Close[0], z1MidlineCountdownLong, normSlope, Zona1SlopeThreshold, atr14 != null ? atr14[0] : 0.0, tema9[0], ema200[0]));
                    }
                    else
                    {
                        Print(string.Format("{0} - [PAS - Z1-100B v10.2 | ¡PAILAS MI PEZ!] Cruce Midline en barra {1}/40 descartado: Pendiente en +{2:F2} (Requerido Z1: >={3:F2}, ZonaOK: {4}, ConfOK: {5}) | ATR: {6:F2} pts. ¡Mercado plano sin fuerza, no regalamos ni un dólar!",
                            Time[0], z1MidlineCountdownLong, normSlope, Zona1SlopeThreshold, precioEnZona, confirmacionAlcista, atr14 != null ? atr14[0] : 0.0));
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
                    // 2. Pendiente Normalizada negativa de la Línea Media (m/ATR <= -Zona1SlopeThreshold)
                    bool pendienteMidlineBajista = normSlope <= -Zona1SlopeThreshold;

                    // 3. Ubicación del precio: Zona 4 o Zona 5 moderada (no más de 10 ticks bajo Lower 3.5)
                    bool precioEnZona = (Close[0] <= lowerSmall) && (Close[0] >= lowerBig - (Z1MaxZone0Ticks * TickSize));

                    // 4. Confirmación de TEMA 9 (azul) sin gancho falso sobre EMA 200 (tolerancia 1 tick)
                    bool confirmacionBajista = (tema9[0] - ema200[0]) < (1.0 * TickSize);

                    if (pendienteMidlineBajista && precioEnZona && confirmacionBajista)
                    {
                        ExecuteAceEntry(AceType.As1_Zona1_100Bar, MarketPosition.Short, "Z1-100Bar_Short");
                        Print(string.Format("{0} - [PAS - Z1-100Bar v10.2 | ¡GOLAZO CORTO EJECUTADO!] Entrada @ ~{1:F2} en barra {2}/40 | Pendiente Normalizada: {3:F2} (Mínimo Z1: -{4:F2}) | ATR: {5:F2} pts | TEMA9 ({6:F2}) < EMA200 ({7:F2})",
                            Time[0], Close[0], z1MidlineCountdownShort, normSlope, Zona1SlopeThreshold, atr14 != null ? atr14[0] : 0.0, tema9[0], ema200[0]));
                    }
                    else
                    {
                        Print(string.Format("{0} - [PAS - Z1-100B v10.2 | ¡PAILAS MI PEZ!] Cruce Midline en barra {1}/40 descartado: Pendiente en {2:F2} (Requerido Z1: <={3:F2}, ZonaOK: {4}, ConfOK: {5}) | ATR: {6:F2} pts. ¡Mercado plano sin fuerza, no regalamos ni un dólar!",
                            Time[0], z1MidlineCountdownShort, normSlope, -Zona1SlopeThreshold, precioEnZona, confirmacionBajista, atr14 != null ? atr14[0] : 0.0));
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
            //      y se cumplen las 2 capas (EMA en Zona 3 + Pendientes Normalizadas alineadas >= SobbSlopeThreshold) -> ¡DISPARO LARGO!
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
                    bool isPendientesLargo = (normMidSlope >= SobbSlopeThreshold && normUpperSlope >= SobbSlopeThreshold);

                    // CAPA 1: EMA 200 en Zona 3 (Contexto Macro Estricto e Inmediato)
                    if (!isEmaZonaLargo)
                    {
                        sobbArmedLong = false;
                        sobbHasFloatedLong = false;
                        sobbFloatingBarsCountLong = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.2 | ¡PAILAS MI PEZ!] Largo descartado: Vela flotando #{1} afuera pero EMA 200 ({2:F2}) fuera de Zona 3 ([{3:F2}, {4:F2}]). ¡Apague y vámonos!",
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
                            Print(string.Format("{0} - [PAS - SOBB v10.2 | ¡PUM, ADIÓS! QUÉ GROSERÍA DE TRADE] ¡Largo disparado @ ~{1:F2}! {2} barras consecutivas afuera con High ({3:F2}) > High[1] ({4:F2}) | Pendiente Mid: +{5:F2}, Upper: +{6:F2} (Mínimo SOBB: +{7:F2}) | ATR: {8:F2} pts.",
                                Time[0], Close[0], sobbFloatingBarsCountLong, High[0], High[1], normMidSlope, normUpperSlope, SobbSlopeThreshold, atr14 != null ? atr14[0] : 0.0));
                            return;
                        }
                        else if (!rompioTechoAnterior)
                        {
                            Print(string.Format("{0} - [PAS - SOBB v10.2 | LARGO FRENADO] Barra #{1} afuera pero techo High ({2:F2}) no superó High[1] ({3:F2}). Esperando impulso en siguiente barra...",
                                Time[0], sobbFloatingBarsCountLong, High[0], High[1]));
                        }
                        else
                        {
                            Print(string.Format("{0} - [PAS - SOBB v10.2 | ¡PAILAS MI PEZ!] Largo en espera de pendiente: Midline en +{1:F2}, Upper en +{2:F2} vs Umbral SOBB requerido: +{3:F2} | ATR: {4:F2} pts. Mercado plano/ruido, ¡no arriesgamos!",
                                Time[0], normMidSlope, normUpperSlope, SobbSlopeThreshold, atr14 != null ? atr14[0] : 0.0));
                        }
                    }
                    else if (sobbFloatingBarsCountLong > SobbMaxFloatingBarsWindow)
                    {
                        // Superó la ventana de 3 barras consecutivas sin confirmar rompimiento
                        sobbArmedLong = false;
                        sobbHasFloatedLong = false;
                        sobbFloatingBarsCountLong = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.2 | ¡PAILAS!] Pasaron {1} barras afuera sin rompimiento alcista con pendiente. Setup cancelado.",
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
            //      y se cumplen las 2 capas (EMA en Zona 3 + Pendientes Normalizadas alineadas <= -SobbSlopeThreshold) -> ¡DISPARO CORTO!
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
                    bool isPendientesCorto = (normMidSlope <= -SobbSlopeThreshold && normLowerSlope <= -SobbSlopeThreshold);

                    // CAPA 1: EMA 200 en Zona 3 (Contexto Macro Estricto e Inmediato)
                    if (!isEmaZonaCorto)
                    {
                        sobbArmedShort = false;
                        sobbHasFloatedShort = false;
                        sobbFloatingBarsCountShort = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.2 | ¡PAILAS MI PEZ!] Corto descartado: Vela flotando #{1} afuera pero EMA 200 ({2:F2}) fuera de Zona 3 ([{3:F2}, {4:F2}]). ¡Apague y vámonos!",
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
                            Print(string.Format("{0} - [PAS - SOBB v10.2 | ¡PUM, ADIÓS! QUÉ GROSERÍA DE TRADE] ¡Corto disparado @ ~{1:F2}! {2} barras consecutivas afuera con Low ({3:F2}) < Low[1] ({4:F2}) | Pendiente Mid: {5:F2}, Lower: {6:F2} (Mínimo SOBB: -{7:F2}) | ATR: {8:F2} pts.",
                                Time[0], Close[0], sobbFloatingBarsCountShort, Low[0], Low[1], normMidSlope, normLowerSlope, SobbSlopeThreshold, atr14 != null ? atr14[0] : 0.0));
                            return;
                        }
                        else if (!rompioSueloAnterior)
                        {
                            Print(string.Format("{0} - [PAS - SOBB v10.2 | CORTO FRENADO] Barra #{1} afuera pero suelo Low ({2:F2}) no perforó Low[1] ({3:F2}). Esperando impulso en siguiente barra...",
                                Time[0], sobbFloatingBarsCountShort, Low[0], Low[1]));
                        }
                        else
                        {
                            Print(string.Format("{0} - [PAS - SOBB v10.2 | ¡PAILAS MI PEZ!] Corto en espera de pendiente: Midline en {1:F2}, Lower en {2:F2} vs Umbral SOBB requerido: -{3:F2} | ATR: {4:F2} pts. Mercado plano/ruido, ¡no arriesgamos!",
                                Time[0], normMidSlope, normLowerSlope, SobbSlopeThreshold, atr14 != null ? atr14[0] : 0.0));
                        }
                    }
                    else if (sobbFloatingBarsCountShort > SobbMaxFloatingBarsWindow)
                    {
                        // Superó la ventana de 3 barras consecutivas sin confirmar rompimiento
                        sobbArmedShort = false;
                        sobbHasFloatedShort = false;
                        sobbFloatingBarsCountShort = 0;
                        Print(string.Format("{0} - [PAS - SOBB v10.2 | ¡PAILAS!] Pasaron {1} barras afuera sin rompimiento bajista con pendiente. Setup cancelado.",
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
            // 1. FILTRO HORARIO EXCLUSIVO DE CASCADA: No opera antes de las 10:00 AM EST (Evita trampas de acomodacion de pre-mercado)
            int timeNow = ToTime(Time[0]);
            if (timeNow < CascadaStartTime || timeNow >= StopEntriesTime) return;

            // 2. FILTRO DE SACIEDAD: Si Cascada logro una victoria previa (>= $180 USD), permanece en cuarentena
            if (Time[0] < cascadaQuarantineEndTime) return;

            double midline    = keltnerBig.Midline[0];
            double upperSmall = keltnerSmall.Upper[0];
            double lowerSmall = keltnerSmall.Lower[0];
            double upperBig   = keltnerBig.Upper[0];
            double lowerBig   = keltnerBig.Lower[0];
            double normSlope  = GetNormalizedSlope(keltnerBig.Midline);

            // =========================================================================
            // DISPARO CORTO: AS 4 CASCADA SURFER v15.1 (BAJISTA INSTITUCIONAL)
            // 1. Regla de 1 Solo Tiro Base + 1 Segundo Boleto Quirurgico con Enfriamiento >= 5 Barras
            // 2. Filtro Macro: Linea Roja EMA 200 por encima de la Linea Media (ema200[0] > midline)
            // 3. Pendiente Linea Media:
            //    - Disparo #1: normSlope < -0.15 confirmada por >= 2 barras consecutivas
            //    - 2do Boleto: Mayor exigencia (normSlope < -CascadaSecondTicketSlopeThreshold, ej. < -0.18)
            // 4. Filtro Micro TEMA 9: Pendiente negativa en 5 barras (CurrentBar >= 5 && tema9[0] < tema9[5])
            // 5. Ubicacion: Precio a favor de la tendencia (Close[0] <= midline)
            // 6. Vela con intencion bajista: Close[0] < Open[0]
            // 7. Filtro Anti-FOMO: Distancia afuera de Lower 3.5 <= CascadaMaxOutsideEntryTicks (30tk)
            // 8. Matriz de 4 Zonas de Climax Exterior + Climax de Linea Media en Zona 0
            // =========================================================================
            bool inQuarantineShort = Time[0] < cascadaQuarantineEndTime;
            bool isShortFirstTicket  = !cascadaCycleShortFired && !inQuarantineShort;
            bool isShortSecondTicket = cascadaCycleShortFired && cascadaSecondChanceShortAvailable && !cascadaSecondChanceShortUsed 
                                      && !inQuarantineShort && (CurrentBar - cascadaLastExitBar >= 5);

            if (isShortFirstTicket || isShortSecondTicket)
            {
                bool macroBajista          = ema200[0] > midline;
                bool precioEnZona          = Close[0] <= midline;
                bool pendienteRapido       = cascadaConsecutiveSlopeBarsShort >= CascadaSlopeConfirmBars;
                double requiredSlopeShort  = isShortSecondTicket ? CascadaSecondTicketSlopeThreshold : CascadaSlopeThreshold;
                bool slopeThresholdMet     = normSlope < -requiredSlopeShort;
                bool tema9SlopeBajista     = CurrentBar >= 5 && (tema9[0] < tema9[5]);
                bool velaBajista           = Close[0] < Open[0];

                if (macroBajista && precioEnZona)
                {
                    if (pendienteRapido && slopeThresholdMet && tema9SlopeBajista && velaBajista)
                    {
                        // 1. FILTRO DE TOPE MAXIMO DE SOBREEXTENSION ANTI-FOMO (30tk v15.1)
                        double ticksAfueraLower = (lowerBig - Close[0]) / TickSize;
                        if (ticksAfueraLower > CascadaMaxOutsideEntryTicks)
                        {
                            Print(string.Format("{0} - [PAS v15.1 | RECHAZO CASCADA CORTO] Entrada bloqueada: precio a {1:F1} ticks afuera de Lower 3.5 (Tope: {2} ticks). Sobreextension extrema evitada.",
                                Time[0], ticksAfueraLower, CascadaMaxOutsideEntryTicks));
                            return;
                        }

                        // 2. CONFIGURACION DE CLIMAX Y SL SEGUN MATRIZ DE ZONAS (v15.1)
                        if (ticksAfueraLower >= CascadaTier4ExtremaTicks) // Zona 4 Extrema: 25 a 30 ticks afuera
                        {
                            cascadaEnteredZone              = 4;
                            cascadaEntryOutsideTicks        = ticksAfueraLower;
                            cascadaClimaxTargetTicks        = ticksAfueraLower * CascadaClimaxMultExt; // 1.6x
                            cascadaHasClimaxTarget          = true;
                            cascadaHasMidlineClimaxTarget   = false;
                        }
                        else if (ticksAfueraLower >= CascadaTier3AltaTicks) // Zona 3 Alta: 15 a 25 ticks afuera
                        {
                            cascadaEnteredZone              = 3;
                            cascadaEntryOutsideTicks        = ticksAfueraLower;
                            cascadaClimaxTargetTicks        = ticksAfueraLower * CascadaClimaxMultHigh; // 2.0x
                            cascadaHasClimaxTarget          = true;
                            cascadaHasMidlineClimaxTarget   = false;
                        }
                        else if (ticksAfueraLower >= CascadaTier2MediaTicks) // Zona 2 Media: 10 a 15 ticks afuera
                        {
                            cascadaEnteredZone              = 2;
                            cascadaEntryOutsideTicks        = ticksAfueraLower;
                            cascadaClimaxTargetTicks        = ticksAfueraLower * CascadaClimaxMultMid; // 3.0x
                            cascadaHasClimaxTarget          = true;
                            cascadaHasMidlineClimaxTarget   = false;
                        }
                        else if (ticksAfueraLower >= CascadaTier1BajaTicks) // Zona 1 Baja: 2 a 10 ticks afuera
                        {
                            cascadaEnteredZone              = 1;
                            cascadaEntryOutsideTicks        = ticksAfueraLower;
                            cascadaClimaxTargetTicks        = ticksAfueraLower * CascadaClimaxMultLow; // 5.0x
                            cascadaHasClimaxTarget          = true;
                            cascadaHasMidlineClimaxTarget   = false;
                        }
                        else // Zona 0: Adentro del canal (< 2 ticks afuera) -> Climax de Linea Media 1.5x
                        {
                            cascadaEnteredZone              = 0;
                            cascadaEntryOutsideTicks = 0.0;
            cascadaClimaxTargetTicks = 0.0;
            cascadaHasClimaxTarget = false;
            cascadaEnteredZone = 0;
            cascadaEntryMidlineDistTicks = 0.0;
            cascadaClimaxMidlineTargetTicks = 0.0;
            cascadaHasMidlineClimaxTarget = false;
                            cascadaEntryMidlineDistTicks    = Math.Abs(Close[0] - midline) / TickSize;
                            cascadaClimaxMidlineTargetTicks = cascadaEntryMidlineDistTicks * CascadaClimaxMidlineMult; // 1.5x
                            cascadaHasMidlineClimaxTarget   = true;
                        }

                        cascadaCycleShortFired = true;
                        if (isShortSecondTicket)
                        {
                            cascadaSecondChanceShortAvailable = false;
                            cascadaSecondChanceShortUsed = true;
                        }

                        ExecuteAceEntry(AceType.As4_Cascada50, MarketPosition.Short, "Cascada50_Short");
                        string targetInfo = cascadaEnteredZone > 0 
                            ? string.Format("Afuera 3.5: {0:F1}tk | Climax 3.5 Meta: {1:F1}tk (Zona {2})", ticksAfueraLower, cascadaClimaxTargetTicks, cascadaEnteredZone)
                            : string.Format("Zona 0 (Adentro) | Dist Midline: {0:F1}tk | Climax Midline Meta: {1:F1}tk ({2:F1}x)", cascadaEntryMidlineDistTicks, cascadaClimaxMidlineTargetTicks, CascadaClimaxMidlineMult);

                        Print(string.Format("{0} - [PAS v15.1 | DISPARO CORTO CASCADA!] Entrada @ ~{1:F2} | Lower 1.75: {2:F2} | Midline: {3:F2} | Pendiente: {4:F2} (Req: < -{5:F2}) | TEMA9 OK | {6} | {7}",
                            Time[0], Close[0], lowerSmall, midline, normSlope, requiredSlopeShort, targetInfo,
                            (isShortSecondTicket ? "2DO BOLETO QUIRURGICO (5b Cooldown)" : "DISPARO #1")));
                        return;
                    }
                    else if (isShortSecondTicket && pendienteRapido && !slopeThresholdMet)
                    {
                        Print(string.Format("{0} - [PAS v15.1 | RECHAZO 2DO BOLETO CORTO] Pendiente en {1:F2} no alcanza la mayor exigencia requerida (< -{2:F2} vs base -{3:F2}). Esperando impulso...",
                            Time[0], normSlope, CascadaSecondTicketSlopeThreshold, CascadaSlopeThreshold));
                    }
                    else if (pendienteRapido && !tema9SlopeBajista && CurrentBar >= 5)
                    {
                        Print(string.Format("{0} - [PAS v15.1 | RECHAZO TEMA9 CORTO] Pendiente Mid OK ({1:F2}) pero TEMA 9 ({2:F2}) >= TEMA 9[5] ({3:F2}). Esperando momentum a favor...",
                            Time[0], normSlope, tema9[0], tema9[5]));
                    }
                }
            }

            // =========================================================================
            // DISPARO LARGO: AS 4 CASCADA SURFER v15.1 (ALCISTA INSTITUCIONAL)
            // 1. Regla de 1 Solo Tiro Base + 1 Segundo Boleto Quirurgico con Enfriamiento >= 5 Barras
            // 2. Filtro Macro: Linea Roja EMA 200 por debajo de la Linea Media (ema200[0] < midline)
            // 3. Pendiente Linea Media:
            //    - Disparo #1: normSlope > +0.15 confirmada por >= 2 barras consecutivas
            //    - 2do Boleto: Mayor exigencia (normSlope > +CascadaSecondTicketSlopeThreshold, ej. > +0.18)
            // 4. Filtro Micro TEMA 9: Pendiente positiva en 5 barras (CurrentBar >= 5 && tema9[0] > tema9[5])
            // 5. Ubicacion: Precio a favor de la tendencia (Close[0] >= midline)
            // 6. Vela con intencion alcista: Close[0] > Open[0]
            // 7. Filtro Anti-FOMO: Distancia afuera de Upper 3.5 <= CascadaMaxOutsideEntryTicks (30tk)
            // 8. Matriz de 4 Zonas de Climax Exterior + Climax de Linea Media en Zona 0
            // =========================================================================
            bool inQuarantineLong = Time[0] < cascadaQuarantineEndTime;
            bool isLongFirstTicket  = !cascadaCycleLongFired && !inQuarantineLong;
            bool isLongSecondTicket = cascadaCycleLongFired && cascadaSecondChanceLongAvailable && !cascadaSecondChanceLongUsed 
                                     && !inQuarantineLong && (CurrentBar - cascadaLastExitBar >= 5);

            if (isLongFirstTicket || isLongSecondTicket)
            {
                bool macroAlcista          = ema200[0] < midline;
                bool precioEnZona          = Close[0] >= midline;
                bool pendienteRapido       = cascadaConsecutiveSlopeBarsLong >= CascadaSlopeConfirmBars;
                double requiredSlopeLong   = isLongSecondTicket ? CascadaSecondTicketSlopeThreshold : CascadaSlopeThreshold;
                bool slopeThresholdMet     = normSlope > requiredSlopeLong;
                bool tema9SlopeAlcista     = CurrentBar >= 5 && (tema9[0] > tema9[5]);
                bool velaAlcista           = Close[0] > Open[0];

                if (macroAlcista && precioEnZona)
                {
                    if (pendienteRapido && slopeThresholdMet && tema9SlopeAlcista && velaAlcista)
                    {
                        // 1. FILTRO DE TOPE MAXIMO DE SOBREEXTENSION ANTI-FOMO (30tk v15.1)
                        double ticksAfueraUpper = (Close[0] - upperBig) / TickSize;
                        if (ticksAfueraUpper > CascadaMaxOutsideEntryTicks)
                        {
                            Print(string.Format("{0} - [PAS v15.1 | RECHAZO CASCADA LARGO] Entrada bloqueada: precio a {1:F1} ticks afuera de Upper 3.5 (Tope: {2} ticks). Sobreextension extrema evitada.",
                                Time[0], ticksAfueraUpper, CascadaMaxOutsideEntryTicks));
                            return;
                        }

                        // 2. CONFIGURACION DE CLIMAX Y SL SEGUN MATRIZ DE ZONAS (v15.1)
                        if (ticksAfueraUpper >= CascadaTier4ExtremaTicks) // Zona 4 Extrema: 25 a 30 ticks afuera
                        {
                            cascadaEnteredZone              = 4;
                            cascadaEntryOutsideTicks        = ticksAfueraUpper;
                            cascadaClimaxTargetTicks        = ticksAfueraUpper * CascadaClimaxMultExt; // 1.6x
                            cascadaHasClimaxTarget          = true;
                            cascadaHasMidlineClimaxTarget   = false;
                        }
                        else if (ticksAfueraUpper >= CascadaTier3AltaTicks) // Zona 3 Alta: 15 a 25 ticks afuera
                        {
                            cascadaEnteredZone              = 3;
                            cascadaEntryOutsideTicks        = ticksAfueraUpper;
                            cascadaClimaxTargetTicks        = ticksAfueraUpper * CascadaClimaxMultHigh; // 2.0x
                            cascadaHasClimaxTarget          = true;
                            cascadaHasMidlineClimaxTarget   = false;
                        }
                        else if (ticksAfueraUpper >= CascadaTier2MediaTicks) // Zona 2 Media: 10 a 15 ticks afuera
                        {
                            cascadaEnteredZone              = 2;
                            cascadaEntryOutsideTicks        = ticksAfueraUpper;
                            cascadaClimaxTargetTicks        = ticksAfueraUpper * CascadaClimaxMultMid; // 3.0x
                            cascadaHasClimaxTarget          = true;
                            cascadaHasMidlineClimaxTarget   = false;
                        }
                        else if (ticksAfueraUpper >= CascadaTier1BajaTicks) // Zona 1 Baja: 2 a 10 ticks afuera
                        {
                            cascadaEnteredZone              = 1;
                            cascadaEntryOutsideTicks        = ticksAfueraUpper;
                            cascadaClimaxTargetTicks        = ticksAfueraUpper * CascadaClimaxMultLow; // 5.0x
                            cascadaHasClimaxTarget          = true;
                            cascadaHasMidlineClimaxTarget   = false;
                        }
                        else // Zona 0: Adentro del canal (< 2 ticks afuera) -> Climax de Linea Media 1.5x
                        {
                            cascadaEnteredZone              = 0;
                            cascadaEntryOutsideTicks = 0.0;
            cascadaClimaxTargetTicks = 0.0;
            cascadaHasClimaxTarget = false;
            cascadaEnteredZone = 0;
            cascadaEntryMidlineDistTicks = 0.0;
            cascadaClimaxMidlineTargetTicks = 0.0;
            cascadaHasMidlineClimaxTarget = false;
                            cascadaEntryMidlineDistTicks    = Math.Abs(Close[0] - midline) / TickSize;
                            cascadaClimaxMidlineTargetTicks = cascadaEntryMidlineDistTicks * CascadaClimaxMidlineMult; // 1.5x
                            cascadaHasMidlineClimaxTarget   = true;
                        }

                        cascadaCycleLongFired = true;
                        if (isLongSecondTicket)
                        {
                            cascadaSecondChanceLongAvailable = false;
                            cascadaSecondChanceLongUsed = true;
                        }

                        ExecuteAceEntry(AceType.As4_Cascada50, MarketPosition.Long, "Cascada50_Long");
                        string targetInfo = cascadaEnteredZone > 0 
                            ? string.Format("Afuera 3.5: {0:F1}tk | Climax 3.5 Meta: {1:F1}tk (Zona {2})", ticksAfueraUpper, cascadaClimaxTargetTicks, cascadaEnteredZone)
                            : string.Format("Zona 0 (Adentro) | Dist Midline: {0:F1}tk | Climax Midline Meta: {1:F1}tk ({2:F1}x)", cascadaEntryMidlineDistTicks, cascadaClimaxMidlineTargetTicks, CascadaClimaxMidlineMult);

                        Print(string.Format("{0} - [PAS v15.1 | DISPARO LARGO CASCADA!] Entrada @ ~{1:F2} | Upper 1.75: {2:F2} | Midline: {3:F2} | Pendiente: +{4:F2} (Req: > +{5:F2}) | TEMA9 OK | {6} | {7}",
                            Time[0], Close[0], upperSmall, midline, normSlope, requiredSlopeLong, targetInfo,
                            (isLongSecondTicket ? "2DO BOLETO QUIRURGICO (5b Cooldown)" : "DISPARO #1")));
                        return;
                    }
                    else if (isLongSecondTicket && pendienteRapido && !slopeThresholdMet)
                    {
                        Print(string.Format("{0} - [PAS v15.1 | RECHAZO 2DO BOLETO LARGO] Pendiente en +{1:F2} no alcanza la mayor exigencia requerida (> +{2:F2} vs base +{3:F2}). Esperando impulso...",
                            Time[0], normSlope, CascadaSecondTicketSlopeThreshold, CascadaSlopeThreshold));
                    }
                    else if (pendienteRapido && !tema9SlopeAlcista && CurrentBar >= 5)
                    {
                        Print(string.Format("{0} - [PAS v15.1 | RECHAZO TEMA9 LARGO] Pendiente Mid OK (+{1:F2}) pero TEMA 9 ({2:F2}) <= TEMA 9[5] ({3:F2}). Esperando momentum a favor...",
                            Time[0], normSlope, tema9[0], tema9[5]));
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

                // REGLA SUPREMA DEL MODO DULCE (v11.3): En Escalón 3 (Modo Dulce $900+),
                // el piso de $900 es SAGRADO. Para abrir un trade nuevo dentro del Modo Dulce,
                // el margen por encima del piso DEBE permitir al menos 12 ticks de SL saludable ($150 ES / $15 MES).
                // Si sobra menos de $150 (ej. $45 USD en $945), NO se abre la posición para blindar el botín.
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
                // En Escalón 1 ($400-$599, Piso $250) y Escalón 2 ($600-$899, Piso $500),
                // se permite SIEMPRE operar con 1 contrato protegido mientras estemos por encima del piso,
                // para que la estrategia pueda dar el salto al siguiente escalón o al Modo Dulce sin trabas.
                if (currentDailyTier == 1 || currentDailyTier == 2)
                {
                    return 1;
                }

                // Para Modo Dulce holgado (>= 3 con margen amplio): Permitimos multicontrato si hay margen holgado (mínimo 14 ticks por contrato)
                int minMulticontractTicks = 14;

                for (int qty = targetContracts; qty >= 2; qty--)
                {
                    double dollarsPerTick = qty * tickDollarVal;
                    int maxTicksForQty = (int)Math.Floor(allowedLossDollars / dollarsPerTick);

                    if (maxTicksForQty >= minMulticontractTicks)
                    {
                        if (qty < targetContracts)
                        {
                            Print(string.Format("{0} - [PAS | DESESCALADO DINÁMICO] Margen: ${1:F2} USD. Posición ajustada a {2} contratos ({3} ticks de respiro).",
                                Time[0], allowedLossDollars, qty, maxTicksForQty));
                        }
                        return qty;
                    }
                }

                // Si estamos en Modo Dulce con margen >= 12 ticks pero < multicontrato, operamos con 1 contrato:
                Print(string.Format("{0} - [PAS | DESESCALADO A 1 CONTRATO] Margen disponible: ${1:F2} USD. Posición ajustada a 1 contrato para seguir operando sin romper piso (+${2:F2}).",
                    Time[0], allowedLossDollars, currentDailyFloor));
                return 1;
            }

            return targetContracts;
        }

        private void ExecuteAceEntry(AceType ace, MarketPosition pos, string signalName)
        {
            currentAce = ace;
            entryBarGlobal = CurrentBar;
            lastCascadaDirection = (pos == MarketPosition.Long) ? 1 : -1;
            entryPriceGlobal = Close[0];
            highestPriceSinceEntry = Close[0];
            lowestPriceSinceEntry  = Close[0];

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
            if (ace != AceType.As4_Cascada50)
            {
                cascadaEnteredZone = 0;
                cascadaOutsideInitialSl = 0.0;
            }

            trancaActive = false;
            trancaPrice = 0.0;
            trancaBonusPrice = 0.0;
            trancaMilestoneLevel = 0.0;
            trancaArmedBar = -1;

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
                    // CASCADA SURFER v15.1: SL Inicial segun zona de entrada
                    isCascadaLockActive = false;
                    if (cascadaEnteredZone >= 3)
                    {
                        // Zonas 3 y 4 (15 a 30 ticks afuera): SL ceñido a 2 ticks atras de Banda Grande 3.5
                        cascadaReachedOutsideBigBand = false;
                        initialSlPrice = Instrument.MasterInstrument.RoundToTickSize(keltnerBig.Upper[0] - (2 * TickSize));
                        Print(string.Format("{0} - [PAS v15.1 - Cascada | SURFER MONOCONTRATO] Entrada Long @ {1:F2} | SL Inicial Ceñido: Banda 3.5 (-2tk @ {2:F2} | {3:F1} ticks de riesgo). Meta Climax 3.5: {4:F1}tk (Zona {5}).",
                            Time[0], Close[0], initialSlPrice, (Close[0] - initialSlPrice) / TickSize, cascadaClimaxTargetTicks, cascadaEnteredZone));
                    }
                    else if (cascadaEnteredZone >= 1)
                    {
                        // Zonas 1 y 2 (2 a 15 ticks afuera): SL en el borde del Canal Pequeño (Banda 1.75 -2tk)
                        cascadaReachedOutsideBigBand = false;
                        initialSlPrice = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Upper[0] - (2 * TickSize));
                        Print(string.Format("{0} - [PAS v15.1 - Cascada | SURFER MONOCONTRATO] Entrada Long @ {1:F2} | SL Inicial: Canal Pequeño Banda 1.75 (-2tk @ {2:F2} | {3:F1} ticks de riesgo). Meta Climax 3.5: {4:F1}tk (Zona {5}).",
                            Time[0], Close[0], initialSlPrice, (Close[0] - initialSlPrice) / TickSize, cascadaClimaxTargetTicks, cascadaEnteredZone));
                    }
                    else
                    {
                        // Zona 0: Adentro del canal (< 2 ticks afuera): SL Inicial en Linea Media con Climax Linea Media 1.5x
                        cascadaReachedOutsideBigBand = (Low[0] > keltnerBig.Upper[0]);
                        double naturalSl = keltnerBig.Midline[0] - (2 * TickSize);
                        double maxSafeSl = Close[0] - (CascadaMaxSlTicks * TickSize);
                        initialSlPrice = Math.Max(naturalSl, maxSafeSl);
                        Print(string.Format("{0} - [PAS v15.1 - Cascada | SURFER MONOCONTRATO] Entrada Long @ {1:F2} | SL Inicial: Linea Media ({2:F2} | {3:F1} ticks de respiro). Meta Climax Linea Media: {4:F1}tk ({5:F1}x).",
                            Time[0], Close[0], initialSlPrice, (Close[0] - initialSlPrice) / TickSize, cascadaClimaxMidlineTargetTicks, CascadaClimaxMidlineMult));
                    }
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

                // ESCUDO DIARIO BLINDADO EN DOM (v13.2): En Modo Dulce (Tier >= 3), el piso manda sobre el SL. En Tier 1 y 2 se permite SL natural.
                if (UseDailyManagement && !isDailyLocked && currentDailyTier >= 3 && currentDailyFloor > 0 && dailyRealizedPnL > currentDailyFloor)
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
                    // CASCADA SURFER v15.1: SL Inicial segun zona de entrada
                    isCascadaLockActive = false;
                    if (cascadaEnteredZone >= 3)
                    {
                        // Zonas 3 y 4 (15 a 30 ticks afuera): SL ceñido a 2 ticks atras de Banda Grande 3.5
                        cascadaReachedOutsideBigBand = false;
                        initialSlPrice = Instrument.MasterInstrument.RoundToTickSize(keltnerBig.Lower[0] + (2 * TickSize));
                        Print(string.Format("{0} - [PAS v15.1 - Cascada | SURFER MONOCONTRATO] Entrada Short @ {1:F2} | SL Inicial Ceñido: Banda 3.5 (+2tk @ {2:F2} | {3:F1} ticks de riesgo). Meta Climax 3.5: {4:F1}tk (Zona {5}).",
                            Time[0], Close[0], initialSlPrice, (initialSlPrice - Close[0]) / TickSize, cascadaClimaxTargetTicks, cascadaEnteredZone));
                    }
                    else if (cascadaEnteredZone >= 1)
                    {
                        // Zonas 1 y 2 (2 a 15 ticks afuera): SL en el borde del Canal Pequeño (Banda 1.75 +2tk)
                        cascadaReachedOutsideBigBand = false;
                        initialSlPrice = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Lower[0] + (2 * TickSize));
                        Print(string.Format("{0} - [PAS v15.1 - Cascada | SURFER MONOCONTRATO] Entrada Short @ {1:F2} | SL Inicial: Canal Pequeño Banda 1.75 (+2tk @ {2:F2} | {3:F1} ticks de riesgo). Meta Climax 3.5: {4:F1}tk (Zona {5}).",
                            Time[0], Close[0], initialSlPrice, (initialSlPrice - Close[0]) / TickSize, cascadaClimaxTargetTicks, cascadaEnteredZone));
                    }
                    else
                    {
                        // Zona 0: Adentro del canal (< 2 ticks afuera): SL Inicial en Linea Media con Climax Linea Media 1.5x
                        cascadaReachedOutsideBigBand = (High[0] < keltnerBig.Lower[0]);
                        double naturalSl = keltnerBig.Midline[0] + (2 * TickSize);
                        double maxSafeSl = Close[0] + (CascadaMaxSlTicks * TickSize);
                        initialSlPrice = Math.Min(naturalSl, maxSafeSl);
                        Print(string.Format("{0} - [PAS v15.1 - Cascada | SURFER MONOCONTRATO] Entrada Short @ {1:F2} | SL Inicial: Linea Media ({2:F2} | {3:F1} ticks de respiro). Meta Climax Linea Media: {4:F1}tk ({5:F1}x).",
                            Time[0], Close[0], initialSlPrice, (initialSlPrice - Close[0]) / TickSize, cascadaClimaxMidlineTargetTicks, CascadaClimaxMidlineMult));
                    }
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

                // ESCUDO DIARIO BLINDADO EN DOM (v13.2): En Modo Dulce (Tier >= 3), el piso manda sobre el SL. En Tier 1 y 2 se permite SL natural.
                if (UseDailyManagement && !isDailyLocked && currentDailyTier >= 3 && currentDailyFloor > 0 && dailyRealizedPnL > currentDailyFloor)
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

                // =========================================================================
                // TRANCA DE ADAMANTIUM (GARRAS DE WOLVERINE & FILTRO ANTI-ASFIXIA) v11.0
                // 1. FILTRO ANTI-ASFIXIA: Si faltan menos de MinTrancaDistanceDollars ($125 ES / $12.5 MES)
                //    para la meta, la Tranca se APAGA para no asfixiar el trade por centavos.
                //    El trade corre libre con su gestión de As (Candado Cascada, TP2 SOBB, Zona 1, etc.).
                // 2. COLCHÓN DE CONQUISTA ADAMANTIUM: Para armarse, el mercado debe penetrar con fuerza
                //    superando el escalón por TrancaConquestBufferDollars ($35 ES / $3.5 MES).
                // 3. PISO BLINDADO GARANTIZADO: La Tranca se clava con TrancaCommissionBufferDollars ($15 ES / $1.5 MES),
                //    garantizando que al liquidar, la cuenta quede matemáticamente ADENTRO del nuevo escalón (>= $600 o $900).
                // 4. CERO AUTODISPARO: No evalúa el Low[0] de la vela donde se armó; solo evalúa retrocesos
                //    en velas posteriores o si el precio de cierre Close[0] perfora la tranca.
                // =========================================================================
                if (UseDailyManagement && UseTrancaUniversal)
                {
                    double nextMilestone = 0.0;
                    int nextTierTarget = 0;

                    if (TrancaOnlyFromTier2)
                    {
                        // Pista libre en piso inicial: solo trancamos al tocar Escalón 2 ($600) o Modo Dulce ($900)
                        if (dailyRealizedPnL < DailyGoal_Piso2)
                        {
                            nextMilestone = DailyGoal_Piso2; // $600 ES (Escalón 2)
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
                        // El trade corre libre con la gestión natural del As.
                        if (rawDistanceToMilestone < minHealthyDistance && !trancaActive)
                        {
                            // Tranca inhibida por Anti-Asfixia: el trade respira libremente
                        }
                        else
                        {
                            // ANCLAJE DE TITANIO SOBRE EL ESCALÓN (+$20 USD: $420, $620, $920):
                            double effectiveAnchorMilestone = nextMilestone + (TrancaAnchorDollars * instrumentMultiplier);
                            double distanceToAnchor = effectiveAnchorMilestone - dailyRealizedPnL;

                            double conquestRequiredDollars = distanceToAnchor + (TrancaConquestBufferDollars * instrumentMultiplier);
                            double lockRequiredDollars = distanceToAnchor + (TrancaSafetyDollars * instrumentMultiplier);
                            double floatingGainDollars = (Close[0] - entryPrice) * dollarsPerPoint;
                            double favorableGainDollars = (High[0] - entryPrice) * dollarsPerPoint;

                            // 1. ACTIVACIÓN DE LA TRANCA DE ADAMANTIUM: Requiere penetración con fuerza (Conquest Buffer de 4 ticks)
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
                                                    "{0} - [ESCUDO DIARIO v11.1 | ¡TRANCA DE ADAMANTIUM CLAVADA! ESCALÓN {1} CONQUISTADO]\n" +
                                                    "¡QUÉ GROSERÍA MI PEZ! El mercado perforó con fuerza la meta del Escalón {1} (${2:F2} USD).\n" +
                                                    "Tranca de Adamantium anclada en {3:F2} (Garantiza cerrar en ${4:F2} USD netos tras comisiones y slippage).\n" +
                                                    "Sprint Bonus armado en {5:F2} buscando +${6:F2} USD adicionales. ¡A romperla sin miedo!\n" +
                                                    "****************************************************************************************",
                                    Time[0], nextTierTarget, nextMilestone, trancaPrice, effectiveAnchorMilestone, trancaBonusPrice, TrancaBonusDollars * instrumentMultiplier));
                            }

                            // 2. GESTIÓN MIENTRAS LA TRANCA ESTÁ ACTIVA
                            if (trancaActive)
                            {
                                string exitSignal = currentAce == AceType.As4_Cascada50 ? "Cascada50_Long" :
                                                    (currentAce == AceType.As2_SOBB ? "SOBB_Long" :
                                                    (currentAce == AceType.As1_Zona1_100Bar ? "Z1-100Bar_Long" : "Ratchet_Long"));

                                // A) SPRINT BONUS ALCANZADO (+ $100 USD adicionales conquistados)
                                if (Close[0] >= trancaBonusPrice || High[0] >= trancaBonusPrice)
                                {
                                    ExitLong("Exit_Tranca_TP_Bonus", exitSignal);
                                    double actualGain = (Close[0] - entryPrice) * dollarsPerPoint;
                                    Print(string.Format("****************************************************************************************\n" +
                                                        "{0} - [ESCUDO DIARIO v11.1 | ¡PUM, ADIÓS! SPRINT BONUS CORONADO]\n" +
                                                        "¡GROSERÍA TOTAL! Alcanzó el TP Bonus en {1:F2}. Ganancia de +${2:F2} USD asegurada.\n" +
                                                        "Escalón {3} superado con creces. ¡Dinero blindado en la caja fuerte!\n" +
                                                        "****************************************************************************************",
                                        Time[0], Close[0], actualGain, nextTierTarget));
                                    return;
                                }

                                // B) RETROCESO DETENIDO EN SECO POR LA TRANCA DE ADAMANTIUM
                                // CERO AUTODISPARO: En la misma barra donde se armó solo salimos si Close[0] cae bajo la tranca;
                                // en barras posteriores evaluamos si Low[0] toca o perfora la tranca.
                                bool retrocesoConfirmadoLong = (Close[0] <= trancaPrice) || (CurrentBar > trancaArmedBar && Low[0] <= trancaPrice);

                                if (retrocesoConfirmadoLong)
                                {
                                    ExitLong("Exit_Tranca_Acero", exitSignal);
                                    double actualGain = (Close[0] - entryPrice) * dollarsPerPoint;
                                    Print(string.Format("****************************************************************************************\n" +
                                                        "{0} - [ESCUDO DIARIO v11.1 | TRANCA DE ADAMANTIUM INDESTRUCTIBLE EJECUTADA]\n" +
                                                        "El mercado intentó retroceder pero chocó contra las Garras de Wolverine en {1:F2}.\n" +
                                                        "Trade liquidado con +${2:F2} USD netos garantizados. ¡Escalón {3} (${4:F2} USD) sellado para siempre!\n" +
                                                        "****************************************************************************************",
                                        Time[0], Close[0], actualGain, nextTierTarget, trancaMilestoneLevel > 0 ? trancaMilestoneLevel : effectiveAnchorMilestone));
                                    return;
                                }
                            }
                        }
                    }
                }

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
                // --- GESTIÓN AS 4: CASCADA SURFER MONOCONTRATO v15.1 (SL LÍNEA MEDIA HASTA SALIR DE BANDA 3.5 -> TRAILING BANDA 1.75 POR CIERRE CLOSE[0]) ---
                else if (currentAce == AceType.As4_Cascada50)
                {
                    // Evitamos salida prematura en la vela exacta de ejecución
                    if (CurrentBar <= entryBarGlobal) return;

                    double upperBigBand   = keltnerBig.Upper[0];
                    double upperSmallBand = keltnerSmall.Upper[0];
                    double midline        = keltnerBig.Midline[0];

                    // Solo se activa si la vela esta COMPLETAMENTE AFUERA de la Banda Grande 3.5 (suelo de vela por encima del techo 3.5)
                    if (Low[0] > upperBigBand)
                    {
                        cascadaReachedOutsideBigBand = true;
                    }

                    // FASE 0A: Salida por Climax de Expansion de Banda 3.5 (Zonas 1 a 4: v15.1)
                    if (cascadaHasClimaxTarget)
                    {
                        double currentDistUpper = (Close[0] - upperBigBand) / TickSize;
                        if (currentDistUpper >= cascadaClimaxTargetTicks && Close[0] > entryPrice)
                        {
                            ExitLong("Exit_Cascada_ClimaxExpansion", "Cascada50_Long");
                            double closedProfit = (Close[0] - entryPrice) * Instrument.MasterInstrument.PointValue;
                            Print(string.Format("{0} - [PAS v15.1 | CLIMAX DE EXPANSION COBRADO - LARGO!] Distancia actual ({1:F1} tk) alcanzo meta ({2:F1} tk | Zona {3} sobre {4:F1} tk iniciales). Salida @ ~{5:F2} (PnL estimado: {6:C2}). Ganancia tomada en la cresta!",
                                Time[0], currentDistUpper, cascadaClimaxTargetTicks, cascadaEnteredZone, cascadaEntryOutsideTicks, Close[0], closedProfit));
                            cascadaHasClimaxTarget = false;
                            return;
                        }
                    }

                    // FASE 0B: Salida por Climax de Expansion de Linea Media (Zona 0 - Adentro: v15.1)
                    if (cascadaHasMidlineClimaxTarget)
                    {
                        double currentMidDist = (Close[0] - midline) / TickSize;
                        if (currentMidDist >= cascadaClimaxMidlineTargetTicks && Close[0] > entryPrice)
                        {
                            ExitLong("Exit_Cascada_ClimaxMidline", "Cascada50_Long");
                            double closedProfit = (Close[0] - entryPrice) * Instrument.MasterInstrument.PointValue;
                            Print(string.Format("{0} - [PAS v15.1 | CLIMAX LINEA MEDIA COBRADO - LARGO!] Distancia actual a Midline ({1:F1} tk) alcanzo meta ({2:F1} tk | {3:F1}x sobre {4:F1} tk iniciales). Salida @ ~{5:F2} (PnL estimado: {6:C2}). Ganancia tomada en el estiramiento!",
                                Time[0], currentMidDist, cascadaClimaxMidlineTargetTicks, CascadaClimaxMidlineMult, cascadaEntryMidlineDistTicks, Close[0], closedProfit));
                            cascadaHasMidlineClimaxTarget = false;
                            return;
                        }
                    }

                    // FASE 1: STOP LOSS ESTRUCTURAL POR BARRA COMPLETA TRASPASANDO BANDA (v15.1)
                    if (cascadaEnteredZone >= 3)
                    {
                        // Zonas 3 y 4 (15 a 30 ticks afuera): Barra completa por debajo de Banda Grande 3.5 (High[0] < upperBigBand)
                        if (High[0] < upperBigBand)
                        {
                            ExitLong("SL_Banda35_Cascada", "Cascada50_Long");
                            double closedLoss = (Close[0] - entryPrice) * Instrument.MasterInstrument.PointValue;
                            Print(string.Format("{0} - [PAS v15.1 | LARGO CERRADO POR SL BANDA 3.5] Barra completa traspaso hacia abajo Banda 3.5 (High {1:F2} < Banda {2:F2}) | Salida a Mercado @ ~{3:F2} | PnL: {4:C2}",
                                Time[0], High[0], upperBigBand, Close[0], closedLoss));
                            return;
                        }
                    }
                    else if (cascadaEnteredZone >= 1)
                    {
                        // Zonas 1 y 2 (2 a 15 ticks afuera): Barra completa por debajo de Banda Pequeña 1.75 (High[0] < upperSmallBand)
                        if (High[0] < upperSmallBand)
                        {
                            ExitLong("SL_Banda175_Cascada", "Cascada50_Long");
                            double closedLoss = (Close[0] - entryPrice) * Instrument.MasterInstrument.PointValue;
                            Print(string.Format("{0} - [PAS v15.1 | LARGO CERRADO POR SL CANAL PEQUEÑO] Barra completa traspaso hacia abajo Banda 1.75 (High {1:F2} < Banda {2:F2}) | Salida a Mercado @ ~{3:F2} | PnL: {4:C2}",
                                Time[0], High[0], upperSmallBand, Close[0], closedLoss));
                            return;
                        }
                    }
                    else if (!cascadaReachedOutsideBigBand)
                    {
                        // Zona 0 (Adentro): SL en Linea Media con tope de CascadaMaxSlTicks
                        double midlineSl = Instrument.MasterInstrument.RoundToTickSize(midline - (2 * TickSize));
                        double maxSafeSl = entryPrice - (CascadaMaxSlTicks * TickSize);
                        double dynamicSl = Math.Max(midlineSl, maxSafeSl);

                        if (Close[0] <= dynamicSl || Low[0] <= dynamicSl)
                        {
                            ExitLong("SL_Midline_Cascada", "Cascada50_Long");
                            double closedLoss = (Close[0] - entryPrice) * Instrument.MasterInstrument.PointValue;
                            Print(string.Format("{0} - [PAS v15.1 | LARGO CERRADO POR SL LINEA MEDIA] Precio perforo Midline ({1:F2}) | Salida a Mercado @ ~{2:F2} | PnL: {3:C2}",
                                Time[0], dynamicSl, Close[0], closedLoss));
                            return;
                        }
                    }

                    

                    // FASE 2: TRAILING EN BANDA PEQUEÑA 1.75 (LINEA AZUL)
                    // En Zonas 1 a 4: solo permite salida en Banda 1.75 si esta EN GANANCIA (bandSl > entryPrice)
                    // En Zona 0: surfeo tradicional tras romper Banda 3.5
                    if (cascadaReachedOutsideBigBand)
                    {
                        double bandSl = Instrument.MasterInstrument.RoundToTickSize(upperSmallBand - (CascadaBandOffsetTicks * TickSize));
                        bool allowTrailing = (cascadaEnteredZone == 0) || (bandSl > entryPrice);

                        if (allowTrailing)
                        {
                            bool shouldExit = CascadaExitOnCloseOnly ? (Close[0] <= bandSl) : (Close[0] <= bandSl || Low[0] <= bandSl);
                            if (shouldExit)
                            {
                                ExitLong("Exit_Cascada_Banda175", "Cascada50_Long");
                                double closedProfit = (Close[0] - entryPrice) * Instrument.MasterInstrument.PointValue;
                                Print(string.Format("{0} - [PAS v15.1 | LARGO CERRADO EN BANDA 1.75 (LINEA AZUL)] Vela {1} Banda 1.75 ({2:F2} con respiro {3}tk, SL @ {4:F2}) tras romper Banda Grande 3.5 | Salida @ ~{5:F2} (PnL estimado: {6:C2}).",
                                    Time[0], (CascadaExitOnCloseOnly ? string.Format("CERRO @ {0:F2} <=", Close[0]) : "toco/perforo"), upperSmallBand, CascadaBandOffsetTicks, bandSl, Close[0], closedProfit));
                                return;
                            }
                        }
                    }
                }
            }
            // SALIDAS EN POSICIÓN CORTA (ESPEJO INVERSO)
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
                //    El trade corre libre con su gestión de As (Candado Cascada, TP2 SOBB, Zona 1, etc.).
                // 2. COLCHÓN DE CONQUISTA ADAMANTIUM: Para armarse, el mercado debe penetrar con fuerza
                //    superando el escalón por TrancaConquestBufferDollars ($35 ES / $3.5 MES).
                // 3. PISO BLINDADO GARANTIZADO: La Tranca se clava con TrancaCommissionBufferDollars ($15 ES / $1.5 MES),
                //    garantizando que al liquidar, la cuenta quede matemáticamente ADENTRO del nuevo escalón (>= $600 o $900).
                // 4. CERO AUTODISPARO: No evalúa el High[0] de la vela donde se armó; solo evalúa retrocesos
                //    en velas posteriores o si el precio de cierre Close[0] perfora la tranca hacia arriba.
                // =========================================================================
                if (UseDailyManagement && UseTrancaUniversal)
                {
                    double nextMilestone = 0.0;
                    int nextTierTarget = 0;

                    if (TrancaOnlyFromTier2)
                    {
                        // Pista libre en piso inicial: solo trancamos al tocar Escalón 2 ($600) o Modo Dulce ($900)
                        if (dailyRealizedPnL < DailyGoal_Piso2)
                        {
                            nextMilestone = DailyGoal_Piso2; // $600 ES (Escalón 2)
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
                        // El trade corre libre con la gestión natural del As.
                        if (rawDistanceToMilestone < minHealthyDistance && !trancaActive)
                        {
                            // Tranca inhibida por Anti-Asfixia: el trade respira libremente
                        }
                        else
                        {
                            // ANCLAJE DE TITANIO SOBRE EL ESCALÓN (+$20 USD: $420, $620, $920):
                            double effectiveAnchorMilestone = nextMilestone + (TrancaAnchorDollars * instrumentMultiplier);
                            double distanceToAnchor = effectiveAnchorMilestone - dailyRealizedPnL;

                            double conquestRequiredDollars = distanceToAnchor + (TrancaConquestBufferDollars * instrumentMultiplier);
                            double lockRequiredDollars = distanceToAnchor + (TrancaSafetyDollars * instrumentMultiplier);
                            double floatingGainDollars = (entryPrice - Close[0]) * dollarsPerPoint;
                            double favorableGainDollars = (entryPrice - Low[0]) * dollarsPerPoint;

                            // 1. ACTIVACIÓN DE LA TRANCA DE ADAMANTIUM: Requiere penetración con fuerza (Conquest Buffer de 4 ticks)
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
                                                    "{0} - [ESCUDO DIARIO v11.1 | ¡TRANCA DE ADAMANTIUM CLAVADA! ESCALÓN {1} CONQUISTADO]\n" +
                                                    "¡QUÉ GROSERÍA MI PEZ! El mercado perforó con fuerza la meta del Escalón {1} (${2:F2} USD).\n" +
                                                    "Tranca de Adamantium anclada en {3:F2} (Garantiza cerrar en ${4:F2} USD netos tras comisiones y slippage).\n" +
                                                    "Sprint Bonus armado en {5:F2} buscando +${6:F2} USD adicionales. ¡A romperla sin miedo!\n" +
                                                    "****************************************************************************************",
                                    Time[0], nextTierTarget, nextMilestone, trancaPrice, effectiveAnchorMilestone, trancaBonusPrice, TrancaBonusDollars * instrumentMultiplier));
                            }

                            // 2. GESTIÓN MIENTRAS LA TRANCA ESTÁ ACTIVA
                            if (trancaActive)
                            {
                                string exitSignal = currentAce == AceType.As4_Cascada50 ? "Cascada50_Short" :
                                                    (currentAce == AceType.As2_SOBB ? "SOBB_Short" :
                                                    (currentAce == AceType.As1_Zona1_100Bar ? "Z1-100Bar_Short" : "Ratchet_Short"));

                                // A) SPRINT BONUS ALCANZADO (+ $100 USD adicionales conquistados)
                                if (Close[0] <= trancaBonusPrice || Low[0] <= trancaBonusPrice)
                                {
                                    ExitShort("Exit_Tranca_TP_Bonus", exitSignal);
                                    double actualGain = (entryPrice - Close[0]) * dollarsPerPoint;
                                    Print(string.Format("****************************************************************************************\n" +
                                                        "{0} - [ESCUDO DIARIO v11.1 | ¡PUM, ADIÓS! SPRINT BONUS CORONADO]\n" +
                                                        "¡GROSERÍA TOTAL! Alcanzó el TP Bonus en {1:F2}. Ganancia de +${2:F2} USD asegurada.\n" +
                                                        "Escalón {3} superado con creces. ¡Dinero blindado en la caja fuerte!\n" +
                                                        "****************************************************************************************",
                                        Time[0], Close[0], actualGain, nextTierTarget));
                                    return;
                                }

                                // B) RETROCESO DETENIDO EN SECO POR LA TRANCA DE ADAMANTIUM
                                // CERO AUTODISPARO: En la misma barra donde se armó solo salimos si Close[0] sube sobre la tranca;
                                // en barras posteriores evaluamos si High[0] toca o perfora la tranca.
                                bool retrocesoConfirmadoShort = (Close[0] >= trancaPrice) || (CurrentBar > trancaArmedBar && High[0] >= trancaPrice);

                                if (retrocesoConfirmadoShort)
                                {
                                    ExitShort("Exit_Tranca_Acero", exitSignal);
                                    double actualGain = (entryPrice - Close[0]) * dollarsPerPoint;
                                    Print(string.Format("****************************************************************************************\n" +
                                                        "{0} - [ESCUDO DIARIO v11.1 | TRANCA DE ADAMANTIUM INDESTRUCTIBLE EJECUTADA]\n" +
                                                        "El mercado intentó retroceder pero chocó contra las Garras de Wolverine en {1:F2}.\n" +
                                                        "Trade liquidado con +${2:F2} USD netos garantizados. ¡Escalón {3} (${4:F2} USD) sellado para siempre!\n" +
                                                        "****************************************************************************************",
                                        Time[0], Close[0], actualGain, nextTierTarget, trancaMilestoneLevel > 0 ? trancaMilestoneLevel : effectiveAnchorMilestone));
                                    return;
                                }
                            }
                        }
                    }
                }

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
                // --- GESTIÓN AS 4: CASCADA SURFER MONOCONTRATO v15.1 (SL LÍNEA MEDIA HASTA SALIR DE BANDA 3.5 -> TRAILING BANDA 1.75 POR CIERRE CLOSE[0]) ---
                else if (currentAce == AceType.As4_Cascada50)
                {
                    // Evitamos salida prematura en la vela exacta de ejecución
                    if (CurrentBar <= entryBarGlobal) return;

                    double lowerBigBand   = keltnerBig.Lower[0];
                    double lowerSmallBand = keltnerSmall.Lower[0];
                    double midline        = keltnerBig.Midline[0];

                    // Solo se activa si la vela esta COMPLETAMENTE AFUERA de la Banda Grande 3.5 (techo de vela por debajo del suelo 3.5)
                    if (High[0] < lowerBigBand)
                    {
                        cascadaReachedOutsideBigBand = true;
                    }

                    // FASE 0A: Salida por Climax de Expansion de Banda 3.5 (Zonas 1 a 4: v15.1)
                    if (cascadaHasClimaxTarget)
                    {
                        double currentDistLower = (lowerBigBand - Close[0]) / TickSize;
                        if (currentDistLower >= cascadaClimaxTargetTicks && Close[0] < entryPrice)
                        {
                            ExitShort("Exit_Cascada_ClimaxExpansion", "Cascada50_Short");
                            double closedProfit = (entryPrice - Close[0]) * Instrument.MasterInstrument.PointValue;
                            Print(string.Format("{0} - [PAS v15.1 | CLIMAX DE EXPANSION COBRADO - CORTO!] Distancia actual ({1:F1} tk) alcanzo meta ({2:F1} tk | Zona {3} sobre {4:F1} tk iniciales). Salida @ ~{5:F2} (PnL estimado: {6:C2}). Ganancia tomada en el fondo del pozo!",
                                Time[0], currentDistLower, cascadaClimaxTargetTicks, cascadaEnteredZone, cascadaEntryOutsideTicks, Close[0], closedProfit));
                            cascadaHasClimaxTarget = false;
                            return;
                        }
                    }

                    // FASE 0B: Salida por Climax de Expansion de Linea Media (Zona 0 - Adentro: v15.1)
                    if (cascadaHasMidlineClimaxTarget)
                    {
                        double currentMidDist = (midline - Close[0]) / TickSize;
                        if (currentMidDist >= cascadaClimaxMidlineTargetTicks && Close[0] < entryPrice)
                        {
                            ExitShort("Exit_Cascada_ClimaxMidline", "Cascada50_Short");
                            double closedProfit = (entryPrice - Close[0]) * Instrument.MasterInstrument.PointValue;
                            Print(string.Format("{0} - [PAS v15.1 | CLIMAX LINEA MEDIA COBRADO - CORTO!] Distancia actual a Midline ({1:F1} tk) alcanzo meta ({2:F1} tk | {3:F1}x sobre {4:F1} tk iniciales). Salida @ ~{5:F2} (PnL estimado: {6:C2}). Ganancia tomada en el estiramiento!",
                                Time[0], currentMidDist, cascadaClimaxMidlineTargetTicks, CascadaClimaxMidlineMult, cascadaEntryMidlineDistTicks, Close[0], closedProfit));
                            cascadaHasMidlineClimaxTarget = false;
                            return;
                        }
                    }

                    // FASE 1: STOP LOSS ESTRUCTURAL POR BARRA COMPLETA TRASPASANDO BANDA (v15.1)
                    if (cascadaEnteredZone >= 3)
                    {
                        // Zonas 3 y 4 (15 a 30 ticks afuera): Barra completa por encima de Banda Grande 3.5 (Low[0] > lowerBigBand)
                        if (Low[0] > lowerBigBand)
                        {
                            ExitShort("SL_Banda35_Cascada", "Cascada50_Short");
                            double closedLoss = (entryPrice - Close[0]) * Instrument.MasterInstrument.PointValue;
                            Print(string.Format("{0} - [PAS v15.1 | CORTO CERRADO POR SL BANDA 3.5] Barra completa traspaso hacia arriba Banda 3.5 (Low {1:F2} > Banda {2:F2}) | Salida a Mercado @ ~{3:F2} | PnL: {4:C2}",
                                Time[0], Low[0], lowerBigBand, Close[0], closedLoss));
                            return;
                        }
                    }
                    else if (cascadaEnteredZone >= 1)
                    {
                        // Zonas 1 y 2 (2 a 15 ticks afuera): Barra completa por encima de Banda Pequeña 1.75 (Low[0] > lowerSmallBand)
                        if (Low[0] > lowerSmallBand)
                        {
                            ExitShort("SL_Banda175_Cascada", "Cascada50_Short");
                            double closedLoss = (entryPrice - Close[0]) * Instrument.MasterInstrument.PointValue;
                            Print(string.Format("{0} - [PAS v15.1 | CORTO CERRADO POR SL CANAL PEQUEÑO] Barra completa traspaso hacia arriba Banda 1.75 (Low {1:F2} > Banda {2:F2}) | Salida a Mercado @ ~{3:F2} | PnL: {4:C2}",
                                Time[0], Low[0], lowerSmallBand, Close[0], closedLoss));
                            return;
                        }
                    }
                    else if (!cascadaReachedOutsideBigBand)
                    {
                        // Zona 0 (Adentro): SL en Linea Media con tope de CascadaMaxSlTicks
                        double midlineSl = Instrument.MasterInstrument.RoundToTickSize(midline + (2 * TickSize));
                        double maxSafeSl = entryPrice + (CascadaMaxSlTicks * TickSize);
                        double dynamicSl = Math.Min(midlineSl, maxSafeSl);

                        if (Close[0] >= dynamicSl || High[0] >= dynamicSl)
                        {
                            ExitShort("SL_Midline_Cascada", "Cascada50_Short");
                            double closedLoss = (entryPrice - Close[0]) * Instrument.MasterInstrument.PointValue;
                            Print(string.Format("{0} - [PAS v15.1 | CORTO CERRADO POR SL LINEA MEDIA] Precio perforo Midline ({1:F2}) | Salida a Mercado @ ~{2:F2} | PnL: {3:C2}",
                                Time[0], dynamicSl, Close[0], closedLoss));
                            return;
                        }
                    }

                    // FASE 2: TRAILING EN BANDA PEQUEÑA 1.75 (LINEA AZUL)
                    // En Zonas 1 a 4: solo permite salida en Banda 1.75 si esta EN GANANCIA (bandSl < entryPrice)
                    // En Zona 0: surfeo tradicional tras romper Banda 3.5
                    if (cascadaReachedOutsideBigBand)
                    {
                        double bandSl = Instrument.MasterInstrument.RoundToTickSize(lowerSmallBand + (CascadaBandOffsetTicks * TickSize));
                        bool allowTrailing = (cascadaEnteredZone == 0) || (bandSl < entryPrice);

                        if (allowTrailing)
                        {
                            bool shouldExit = CascadaExitOnCloseOnly ? (Close[0] >= bandSl) : (Close[0] >= bandSl || High[0] >= bandSl);
                            if (shouldExit)
                            {
                                ExitShort("Exit_Cascada_Banda175", "Cascada50_Short");
                                double closedProfit = (entryPrice - Close[0]) * Instrument.MasterInstrument.PointValue;
                                Print(string.Format("{0} - [PAS v15.1 | CORTO CERRADO EN BANDA 1.75 (LINEA AZUL)] Vela {1} Banda 1.75 ({2:F2} con respiro {3}tk, SL @ {4:F2}) tras romper Banda Grande 3.5 | Salida @ ~{5:F2} (PnL estimado: {6:C2}).",
                                    Time[0], (CascadaExitOnCloseOnly ? string.Format("CERRO @ {0:F2} >=", Close[0]) : "toco/perforo"), lowerSmallBand, CascadaBandOffsetTicks, bandSl, Close[0], closedProfit));
                                return;
                            }
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
            cascadaEnteredZone = 0;
            cascadaOutsideInitialSl = 0.0;
            cascadaReachedOutsideBigBand = false;
            cascadaEntryOutsideTicks = 0.0;
            cascadaClimaxTargetTicks = 0.0;
            cascadaHasClimaxTarget = false;
            cascadaEnteredZone = 0;
            cascadaEntryMidlineDistTicks = 0.0;
            cascadaClimaxMidlineTargetTicks = 0.0;
            cascadaHasMidlineClimaxTarget = false;
            entryBarGlobal = -1;

            trancaActive = false;
            trancaPrice = 0.0;
            trancaBonusPrice = 0.0;
            trancaMilestoneLevel = 0.0;
            trancaArmedBar = -1;

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

                // Verificación de corte a las 12:00 PM si el trade que acaba de cerrar ocurrió después de las 12:00 PM (Solo Escalón 3 sigue activo)
                if (UseDailyManagement && UseTier1LunchCutoff && !isDailyLocked && currentDailyTier < 3)
                {
                    int timeNow = ToTime(Time[0]);
                    if (timeNow >= Tier1CutoffTime)
                    {
                        isDailyLocked = true;
                        dailyLockReason = string.Format("CORTE DE OPERATIVA A LAS 12:00 PM TRAS CIERRE (PnL ${0:F2})", dailyRealizedPnL);
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [ESCUDO DIARIO | CORTE A LAS 12:00 PM TRAS CIERRE DE TRADE] Trade cerrado después de las 12:00 PM EST ({1}).\n" +
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

                    if (trade.Entry != null && trade.Entry.Name.IndexOf("Cascada", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        cascadaLastExitBar = CurrentBar;
                        double effectiveBigWinThreshold = CascadaBigWinThresholdDollars * instrumentMultiplier;

                        // 1. CUARENTENA DE SACIEDAD: Si la ganancia neta en bolsillo >= $180 USD ($18 MES)
                        if (netProfit >= effectiveBigWinThreshold)
                        {
                            cascadaHasWonToday = true;
                            cascadaQuarantineEndTime = trade.Exit.Time.AddMinutes(CascadaQuarantineMinutes);
                            cascadaSecondChanceLongAvailable = false;
                            cascadaSecondChanceShortAvailable = false;
                            Print(string.Format("****************************************************************************************\n" +
                                                "{0} - [PAS v15.1 - Cascada | ¡QUIETO EN PRIMERA! CUARENTENA DE SACIEDAD ACTIVADA]\n" +
                                                "¡Qué grosería de trade! Ganamos +{1:C2} netos en el bolsillo (>= {2:C2} meta de saciedad).\n" +
                                                "Gatillo de Cascada congelado por {3} minutos (hasta las {4:HH:mm:ss} EST). CERO reintentos.\n" +
                                                "****************************************************************************************",
                                trade.Exit.Time, netProfit, effectiveBigWinThreshold, CascadaQuarantineMinutes, cascadaQuarantineEndTime));
                        }
                        // 2. REINTENTO QUIRÚRGICO (SEGUNDO BOLETO CON ENFRIAMIENTO >= 5 BARRAS):
                        // Si el trade cerró sin alcanzar los $180 USD (por ejemplo mecha en Banda 1.75 o SL Línea Media),
                        // se habilita 1 reintento para este ciclo, requiriendo esperar al menos 5 barras de enfriamiento.
                        else
                        {
                            if (trade.Entry.MarketPosition == MarketPosition.Long && !cascadaSecondChanceLongUsed)
                            {
                                cascadaSecondChanceLongAvailable = true;
                                Print(string.Format("{0} - [PAS v15.1 - Cascada | 2DO BOLETO DISPONIBLE - LARGO] Trade cerró con PnL {1:C2} (< {2:C2}). Enfriamiento activo de 5 barras. Si la tendencia sigue viva, se autoriza 1 reintento.",
                                    trade.Exit.Time, netProfit, effectiveBigWinThreshold));
                            }
                            else if (trade.Entry.MarketPosition == MarketPosition.Short && !cascadaSecondChanceShortUsed)
                            {
                                cascadaSecondChanceShortAvailable = true;
                                Print(string.Format("{0} - [PAS v15.1 - Cascada | 2DO BOLETO DISPONIBLE - CORTO] Trade cerró con PnL {1:C2} (< {2:C2}). Enfriamiento activo de 5 barras. Si la tendencia sigue viva, se autoriza 1 reintento.",
                                    trade.Exit.Time, netProfit, effectiveBigWinThreshold));
                            }
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
                    // REGLA DE ORO DE MODO DULCE (v11.2): El piso NUNCA baja de DailyGoal_Piso3 ($900 ES / $90 MES)
                    double dynamicFloor = Math.Max(DailyGoal_Piso3, peakDailyClosedPnL - DailyTrailingGiveback);
                    if (dynamicFloor > currentDailyFloor)
                    {
                        currentDailyFloor = dynamicFloor;
                        Print(string.Format("****************************************************************************************\n" +
                                           "{0} - [ESCUDO DIARIO v11.2 | ¡MODO DULCE BLINDADO!]\n" +
                                           "¡Qué grosería de día! PnL Diario: ${1:F2} USD. Piso dulce blindado en +${2:F2} USD (Inviolable).\n" +
                                           "Trailing Infinito activo buscando el infinito sin permitir caer jamás de los $900.\n" +
                                           "****************************************************************************************",
                            Time[0], dailyRealizedPnL, currentDailyFloor));
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
                Content = isStrategyActive ? "ESTRATEGIA ACTIVA (v15.1)" : "ESTRATEGIA PAUSADA (v15.1)",
                Height = 30,
                Width = 240,
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
                panicButton.Content = isStrategyActive ? "ESTRATEGIA ACTIVA (v15.1)" : "ESTRATEGIA PAUSADA (v15.1)";
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
        [Display(Name = "Activar Corte de Mediodía", Description = "Si a las 12:00 PM EST estamos fuera de Modo Dulce (< $900), apaga nuevas entradas y asegura la ganancia", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 10)]
        public bool UseTier1LunchCutoff { get; set; }

        [NinjaScriptProperty]
        [Range(100000, 160000)]
        [Display(Name = "Hora Límite Mediodía (HHmmss)", Description = "Hora límite de corte para apagar antes del mediodía (120000 = 12:00 PM EST)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 11)]
        public int Tier1CutoffTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Tranca Universal de Escalón", Description = "Al alcanzar la meta de cualquier piso ($400, $600 o $900), asegura ese nivel con una tranca indestructible y busca un bonus", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 12)]
        public bool UseTrancaUniversal { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Tranca Solo Desde Escalón 2 ($600+)", Description = "Deja correr libre el trade en piso inicial y solo tranca al tocar Escalón 2 ($600) o Modo Dulce ($900)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 13)]
        public bool TrancaOnlyFromTier2 { get; set; }

        [NinjaScriptProperty]
        [Range(10.0, 500.0)]
        [Display(Name = "Tranca Dólares de Sprint Bonus ($)", Description = "Ganancia adicional en USD a buscar tras clavar la tranca del escalón ($100 ES / $10 MES por defecto)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 14)]
        public double TrancaBonusDollars { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 50.0)]
        [Display(Name = "Tranca Colchón de Comisiones ($)", Description = "Margen de seguridad sobre el piso para garantizar que quede limpio tras comisiones ($10 ES / $1 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 15)]
        public double TrancaSafetyDollars { get; set; }

        [NinjaScriptProperty]
        [Range(20.0, 500.0)]
        [Display(Name = "Tranca Distancia Mínima Filtro Anti-Asfixia ($)", Description = "Distancia mínima en USD para armar la Tranca. Si falta menos de esto para el escalón, se apaga la tranca para no asfixiar el trade ($125 ES / $12.5 MES)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 16)]
        public double MinTrancaDistanceDollars { get; set; }

        [NinjaScriptProperty]
        [Range(5.0, 200.0)]
        [Display(Name = "Tranca Colchón Conquista Adamantium ($)", Description = "Penetración requerida por encima del escalón para armar la Tranca de Adamantium ($50 ES / $5 MES = 4 ticks)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 17)]
        public double TrancaConquestBufferDollars { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Tranca Anclaje de Titanio sobre Escalón ($)", Description = "Margen de anclaje por encima de cada escalón para sellar la entrada ($20 ES / $2 MES = $420, $620, $920)", GroupName = "4. Escudo de Cuenta (3 Pisos)", Order = 18)]
        public double TrancaAnchorDollars { get; set; }

        // === PARÁMETROS RETIRADOS EN v12.1 (SOBB Y Z1 OCULTOS DE LA UI) ===
        [Browsable(false)]
        [XmlIgnore]
        public int SobbRequiredFloatingBars { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public int SobbMaxFloatingBarsWindow { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public int SobbTp1Ticks { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public int SobbTp2Ticks { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public int SobbMaxSlTicks { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public int SobbChokeThresholdTicks { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public int SobbChokeTicks { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public double SobbSlopeThreshold { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public int MinBarsZona1 { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public int Z1MaxBarsToMidline { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public int Z1MaxZone0Ticks { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public int Zone1Tp1Ticks { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public int Zone1Tp2Ticks { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public int Zone1ChokeTicks { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public double Zona1SlopeThreshold { get; set; }

        // === ASES ACTIVOS EN v12.2 ===
        [NinjaScriptProperty]
        [Display(Name = "Cascada Hora Inicio (HHMMSS)", Description = "Hora mínima para disparar Cascada (10:00 AM = 100000 para evitar trampas de pre-mercado)", GroupName = "5. As 4: Cascada Surfer", Order = 0)]
        public int CascadaStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, 3)]
        [Display(Name = "Cascada Contratos Modo Exploratorio", Description = "Contratos de Cascada en modo exploratorio (1 contrato)", GroupName = "5. As 4: Cascada Surfer", Order = 1)]
        public int CascadaFirstEntryContracts { get; set; }

        [NinjaScriptProperty]
        [Range(5, 60)]
        [Display(Name = "Cascada Distancia Maxima Entrada (Ticks)", Description = "Tope maximo de ticks fuera de Banda 3.5 para permitir entrada (filtro anti-FOMO: 30 ticks por defecto)", GroupName = "5. As 4: Cascada Surfer", Order = 2)]
        public int CascadaMaxOutsideEntryTicks { get; set; }

        [NinjaScriptProperty]
        [Range(15, 40)]
        [Display(Name = "Cascada Umbral Zona 4 Extrema (Ticks)", Description = "Umbral de ticks fuera de Banda 3.5 para Zona 4 Extrema (25 a 30 ticks afuera: 25 por defecto)", GroupName = "5. As 4: Cascada Surfer", Order = 3)]
        public int CascadaTier4ExtremaTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 30)]
        [Display(Name = "Cascada Umbral Zona 3 Alta (Ticks)", Description = "Umbral de ticks fuera de Banda 3.5 para Zona 3 Alta (15 a 25 ticks afuera: 15 por defecto)", GroupName = "5. As 4: Cascada Surfer", Order = 4)]
        public int CascadaTier3AltaTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 20)]
        [Display(Name = "Cascada Umbral Zona 2 Media (Ticks)", Description = "Umbral de ticks fuera de Banda 3.5 para Zona 2 Media (10 a 15 ticks afuera: 10 por defecto)", GroupName = "5. As 4: Cascada Surfer", Order = 5)]
        public int CascadaTier2MediaTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5)]
        [Display(Name = "Cascada Umbral Zona 1 Baja (Ticks)", Description = "Umbral de ticks fuera de Banda 3.5 para Zona 1 Baja (2 a 10 ticks afuera: 2 por defecto)", GroupName = "5. As 4: Cascada Surfer", Order = 6)]
        public int CascadaTier1BajaTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 10.0)]
        [Display(Name = "Cascada Multiplicador Climax Zona 4 Extrema", Description = "Multiplicador de climax para Zona 4 Extrema (25-30 ticks afuera): por defecto 1.6x", GroupName = "5. As 4: Cascada Surfer", Order = 7)]
        public double CascadaClimaxMultExt { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 10.0)]
        [Display(Name = "Cascada Multiplicador Climax Zona 3 Alta", Description = "Multiplicador de climax para Zona 3 Alta (15-25 ticks afuera): por defecto 2.0x", GroupName = "5. As 4: Cascada Surfer", Order = 8)]
        public double CascadaClimaxMultHigh { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 10.0)]
        [Display(Name = "Cascada Multiplicador Climax Zona 2 Media", Description = "Multiplicador de climax para Zona 2 Media (10-15 ticks afuera): por defecto 3.0x", GroupName = "5. As 4: Cascada Surfer", Order = 9)]
        public double CascadaClimaxMultMid { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 15.0)]
        [Display(Name = "Cascada Multiplicador Climax Zona 1 Baja", Description = "Multiplicador de climax para Zona 1 Baja (2-10 ticks afuera): por defecto 5.0x", GroupName = "5. As 4: Cascada Surfer", Order = 10)]
        public double CascadaClimaxMultLow { get; set; }

        [NinjaScriptProperty]
        [Range(1.1, 5.0)]
        [Display(Name = "Cascada Multiplicador Climax Linea Media Zona 0", Description = "Multiplicador de climax respecto a Linea Media para Zona 0 (< 2 ticks afuera): por defecto 1.5x", GroupName = "5. As 4: Cascada Surfer", Order = 11)]
        public double CascadaClimaxMidlineMult { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Barras Confirmacion Pendiente", Description = "Numero de barras consecutivas requeridas con Pendiente Normalizada >= Umbral antes de disparar (2 por defecto)", GroupName = "5. As 4: Cascada Surfer", Order = 3)]
        public int CascadaSlopeConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Range(20.0, 500.0)]
        [Display(Name = "Cascada Candado Trigger (USD)", Description = "Umbral de ganancia flotante en USD para activar el Candado dinámico ($250 ES = 20 ticks / $25 MES)", GroupName = "5. As 4: Cascada Surfer", Order = 5)]
        public double CascadaLockTriggerDollars { get; set; }

        [NinjaScriptProperty]
        [Range(6, 40)]
        [Display(Name = "Cascada SL Máximo de Seguridad (Ticks)", Description = "Tope máximo de pérdida estructural inicial (24 ticks = 6 pts = $300 ES / $30 MES)", GroupName = "5. As 4: Cascada Surfer", Order = 6)]
        public int CascadaMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(20.0, 3000.0)]
        [Display(Name = "Cascada Umbral Súper Win (USD)", Description = "Ganancia mínima neta en USD para activar la Cuarentena de Saciedad ($180 ES / $18 MES)", GroupName = "5. As 4: Cascada Surfer", Order = 7)]
        public double CascadaBigWinThresholdDollars { get; set; }

        [NinjaScriptProperty]
        [Range(10, 240)]
        [Display(Name = "Cascada Cuarentena (Minutos)", Description = "Minutos de congelamiento de Cascada tras una victoria (30 minutos por defecto)", GroupName = "5. As 4: Cascada Surfer", Order = 8)]
        public int CascadaQuarantineMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 1.0)]
        [Display(Name = "Cascada Umbral Pendiente Base", Description = "Umbral base de aceleración de Pendiente Normalizada (LinReg 5 / ATR 14) para disparar rápido de Cascada dentro del canal o en ruptura fresca (0.15 por defecto)", GroupName = "5. As 4: Cascada Surfer", Order = 9)]
        public double CascadaSlopeThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 1.0)]
        [Display(Name = "Cascada Umbral Pendiente 2do Boleto", Description = "Umbral de Pendiente Normalizada ms exigente para el 2do boleto quirrgico (por defecto 0.17: +0.02 vs base)", GroupName = "5. As 4: Cascada Surfer", Order = 10)]
        public double CascadaSecondTicketSlopeThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(2, 30)]
        [Display(Name = "Cascada Umbral Sobreextensión Exterior (Ticks)", Description = "Margen de ticks fuera de Banda 3.5 donde se activa el SL de Oxígeno en Sobreextensión (por defecto 8 ticks = 2.0 pts). Si entra a menos ticks, se trata como adentro con SL en Línea Media.", GroupName = "5. As 4: Cascada Surfer", Order = 10)]
        public int CascadaOutsideThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(4, 30)]
        [Display(Name = "Cascada SL Sobreextensión (Ticks)", Description = "Distancia de Stop Loss en ticks cuando Cascada entra en sobreextensión exterior (>= CascadaOutsideThresholdTicks). Proporciona oxígeno contra sacudidas de mecha en Banda 3.5 (12 ticks = 3.0 pts / -$150 ES por defecto).", GroupName = "5. As 4: Cascada Surfer", Order = 11)]
        public int CascadaOutsideSlTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cascada Salida Solo por Cierre (Close[0])", Description = "Si está activo (True por defecto), la salida en Banda 1.75 solo se ejecuta si la vela CIERRA al otro lado de la banda, ignorando mechas de absorción/rechazo intra-vela.", GroupName = "5. As 4: Cascada Surfer", Order = 12)]
        public bool CascadaExitOnCloseOnly { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Cascada Respiro Banda 1.75 (Ticks)", Description = "Respiro de penetración en ticks más allá de la Banda 1.75 antes de ejecutar el trailing stop (0 ticks por defecto con cierre de vela).", GroupName = "5. As 4: Cascada Surfer", Order = 13)]
        public int CascadaBandOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(4, 30)]
        [Display(Name = "Cascada C1 TP (Ticks)", Description = "(Compatibilidad NT8)", GroupName = "5. As 4: Cascada Surfer", Order = 14)]
        public int CascadaTp1Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(8, 50)]
        [Display(Name = "Cascada C2 TP (Ticks)", Description = "(Compatibilidad NT8)", GroupName = "5. As 4: Cascada Surfer", Order = 15)]
        public int CascadaTp2Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(2, 20)]
        [Display(Name = "Cascada C3 Ahogo (Ticks)", Description = "(Compatibilidad NT8)", GroupName = "5. As 4: Cascada Surfer", Order = 16)]
        public int CascadaChokeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "Ratchet Lookback Barras", Description = "Barras sin tocar EMA 200", GroupName = "6. As 3: Ratchet", Order = 1)]
        public int RatchetLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(5, 30)]
        [Display(Name = "Ratchet Countdown Barras", Description = "Ventana máxima para cruzar Línea Media", GroupName = "6. As 3: Ratchet", Order = 2)]
        public int RatchetCountdownBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Ratchet Tolerancia Gancho TEMA (Ticks)", Description = "Tolerancia para tocar banda en el gancho", GroupName = "6. As 3: Ratchet", Order = 3)]
        public int RatchetTemaTolTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 50)]
        [Display(Name = "Ratchet BreakEven Ticks", Description = "Ticks para liquidar C1 y armar BE (+22 ticks)", GroupName = "6. As 3: Ratchet", Order = 4)]
        public int RatchetBreakEvenTicks { get; set; }

        [NinjaScriptProperty]
        [Range(20, 150)]
        [Display(Name = "Ratchet Choke Threshold Ticks", Description = "Ticks para activar Fase 3 Choke (72 ticks = $900 ES / $90 MES)", GroupName = "6. As 3: Ratchet", Order = 5)]
        public int RatchetChokeThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(2, 20)]
        [Display(Name = "Ratchet Chicle Trailing (Ticks)", Description = "Distancia ceñida del Chicle Parabólico para C3 Runner", GroupName = "6. As 3: Ratchet", Order = 6)]
        public int RatchetChicleTicks { get; set; }

        [NinjaScriptProperty]
        [Range(6, 30)]
        [Display(Name = "Ratchet SL Máximo de Seguridad (Ticks)", Description = "Tope máximo de pérdida inicial para Ratchet (12 ticks = $450 ES / $45 MES en 3 contratos)", GroupName = "6. As 3: Ratchet", Order = 7)]
        public int RatchetMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "Período TEMA", GroupName = "7. Indicadores Macro", Order = 1)]
        public int TemaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(20, 500)]
        [Display(Name = "Período EMA 200", GroupName = "7. Indicadores Macro", Order = 2)]
        public int EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(10, 200)]
        [Display(Name = "Período Keltner", GroupName = "7. Indicadores Macro", Order = 3)]
        public int KeltnerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 6.0)]
        [Display(Name = "Multiplicador Keltner Grande", GroupName = "7. Indicadores Macro", Order = 4)]
        public double KeltnerBigMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 3.0)]
        [Display(Name = "Multiplicador Keltner Pequeño", GroupName = "7. Indicadores Macro", Order = 5)]
        public double KeltnerSmallMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 10.0)]
        [Display(Name = "Mínimo Cruce TEMA (Ticks)", GroupName = "7. Indicadores Macro", Order = 6)]
        public double MinCrossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Offset Stop Loss (Ticks)", GroupName = "7. Indicadores Macro", Order = 7)]
        public int SlOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(2, 20)]
        [Display(Name = "Colchón Mínimo Seguridad SL (Ticks)", GroupName = "7. Indicadores Macro", Order = 8)]
        public int MinSafetySlTicks { get; set; }
        #endregion
    }
}
