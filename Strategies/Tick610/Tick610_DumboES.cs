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
// ESTRATEGIA: Tick610_DumboES - "Dumbo Super-Expansión ES"
// Desarrollada con Pedagogía y Mentoría de Trading para Gráficas de 610 Ticks en Futuros ES / MES.
// VERSIÓN: 1.4 - "El Elefante Volador" (Tranca de Ganancia $400 a Banda 3.5 + TEMA 9 Cascada + Botón v1.4)
//
// CONCEPTO (ELI5):
//   Dumbo es el elefante que aprende a volar. La gran mayoría de estrategias esperan que el precio vuelva
//   a la media (reversión), pero cuando se presenta una súper-expansión tendencial institucional, el precio
//   rompe el techo de la estratósfera (Banda Keltner 3.5) y se niega a descender.
//   Al mismo tiempo, la EMA 200 (la gravedad) quedó atrapada en el sótano opuesto por más de 60 barras
//   POR FUERA DEL CANAL GRANDE 3.5. En vez de operar reversión, Dumbo se monta en el viento a favor de la inercia.
//
// NOVEDADES v1.4:
//   1. Tranca de Ganancia a $400 USD (Anti-Giveback MFE):
//      Si un trade inició con SL en la Banda Azul 1.75 (porque entró a < 10 ticks de Banda 3.5) y no ha
//      alcanzado la separación de 10 ticks (porque el canal Keltner corre junto con el precio), en el momento
//      en que la ganancia flotante toque los +$400 USD (+8 puntos ES / +$40 MES), el SL asciende de inmediato
//      y se planta a 1 tick ADELANTE de la Banda 3.5 Exterior del Canal Grande.
//   2. Respeto al SL de Banda 3.5: Si el trade ya estaba anclado en Banda 3.5 desde el inicio o por separación,
//      no se toca ni se molesta, manteniendo su acompañamiento noble.
//   3. Sincronización Total a v1.4 en todo el código y Botón de Pánico WPF.
// =========================================================================================================

namespace NinjaTrader.NinjaScript.Strategies
{
    public class Tick610_DumboES : Strategy
    {
        #region Constantes Centralizadas
        private const string STRATEGY_VERSION = "1.41";
        #endregion

        #region Enumeraciones
        public enum BandaOpuestaEnum
        {
            [Description("Banda Pequeña 1.75 (Azul)")]
            BandaAzul175,

            [Description("Banda Grande 3.5 (Exterior)")]
            BandaGrande35
        }
        #endregion

        #region Variables e Indicadores
        // Indicadores Técnicos Principales
        private KeltnerChannel keltnerBig;    // Canal Keltner Grande 3.5 ATR (Estratósfera)
        private KeltnerChannel keltnerSmall;  // Canal Keltner Pequeño 1.75 ATR (Banda Azul)
        private EMA            ema200;        // Media Móvil Exponencial de 200 periodos (Gravedad / Suelo Macro)
        private TEMA           tema9;         // Triple Media Móvil Exponencial de 9 periodos (Micro-Tendencia Rápida Cascada)

        // Contadores de Barras Acumuladas en el Abismo (EMA 200)
        private int barsEmaBelowOppositeCount = 0; // Conteo de barras donde EMA 200 está por debajo de la banda opuesta (para Longs)
        private int barsEmaAboveOppositeCount = 0; // Conteo de barras donde EMA 200 está por encima de la banda opuesta (para Shorts)

        // Contadores de Velas Consecutivas Flotando Fuera de Banda 3.5
        private int floatingBarsLongCount  = 0; // Barras consecutivas con Low > Upper 3.5
        private int floatingBarsShortCount = 0; // Barras consecutivas con High < Lower 3.5

        // Control de Estado del Trade Activo y Trailing Choke
        private double entryPriceGlobal      = 0.0;   // Precio de llenado de la entrada
        private double currentSlPriceGlobal  = 0.0;   // Nivel de Stop Loss actual dinámico
        private int    entryBarGlobal        = -1;    // Barra donde se disparó la entrada
        private int    lastExitBar           = -999;  // Barra de última salida para cooldown
        private bool   longFiredInWave       = false; // Bandera para evitar re-entradas múltiples en la misma ola
        private bool   shortFiredInWave      = false; // Bandera para evitar re-entradas múltiples en la misma ola
        private bool   slAnchoredToBigBand   = false; // Bandera: true = SL anclado a Banda 3.5, false = SL anclado a Banda 1.75 Azul
        private bool   isMilestonePromoted   = false; // Bandera: true si el SL fue ascendido por la meta de ganancia de $400 USD

        // Control de Interfaz WPF (Botón de Pánico)
        private Chart  chartWindow           = null;
        private System.Windows.Controls.Button panicButton = null;
        private bool   isStrategyActive      = false; // Inicia en pausa en tiempo real
        private bool   isSubscribedToKeys    = false; // Bandera de suscripción a eventos de teclado

        // Adaptabilidad Mini ES vs Micro MES
        private bool   isMicroInstrument     = false;
        private double instrumentMultiplier  = 1.0;
        #endregion

        #region Configuración de Estado (OnStateChange)
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = "Tick610_DumboES: Estrategia tendencial de super-expansión fuera de Banda 3.5 con EMA 200 en abismo opuesto.";
                Name                                        = "Tick610_DumboES";
                Calculate                                   = Calculate.OnBarClose; // Ejecución limpia al cierre de barra
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
                BarsRequiredToTrade                         = 200;

                // 01. Información de Versión
                Version                                     = string.Format("v{0} (SL Inicial Banda Azul 1.75 + Tranca $40)", STRATEGY_VERSION);

                // 02. Gestión de Posición
                Contracts                                   = 1;      // Número de contratos a operar (escalable)

                // 03. Horario Operativo RTH Estricto
                StartTime                                   = 93500;  // 09:35 AM EST (inicia 5 min tras apertura para atrapar expansiones tempranas)
                StopEntriesTime                             = 154500; // 03:45 PM EST (corta nuevas entradas)
                ForceCloseTime                              = 160000; // 04:00 PM EST (cierra todo antes de la campana)

                // 04. Parámetros del Disparador Dumbo
                FloatingBarsRequired                        = 3;      // Mínimo de velas consecutivas completamente afuera de Banda 3.5
                StrictFloatingLowHigh                       = true;   // true: Low > Upper35 (Long) / High < Lower35 (Short). false: Close fuera.
                MinSeparationFromBand35Ticks                = 12;     // Regla Estructural: Exige estar al menos 12 ticks por fuera de Banda 3.5 para disparar
                CooldownBarsAfterExit                       = 3;      // Barras de enfriamiento tras salir antes de poder evaluar otra entrada
                RequireTemaSlope                            = true;   // true: Exige pendiente estricta de TEMA 9 a favor (barra a barra y vs 2 barras atrás)
                TemaPeriod                                  = 9;      // Periodo de la media rápida TEMA (idéntica a CascadaVega)
                TemaSlopeLookback                           = 2;      // Barras hacia atrás para medir la pendiente de TEMA 9 (2 barras = inicio del despegue)

                // 05. Filtro Macro EMA 200 en el Abismo
                MinBarsEmaOpposite                          = 60;     // Mínimo de barras continuas con la EMA 200 fuera del canal opuesto (calibrado a 60)
                EmaOppositeBandTarget                       = BandaOpuestaEnum.BandaGrande35; // Umbral agresivo: fuera del Canal Grande 3.5 opuesto
                EmaSlopeLookback                            = 3;      // Barras para medir la pendiente de la EMA 200
                RequireEmaSlope                             = true;   // Exigir que la pendiente de EMA 200 esté a favor del trade

                // 06. Gestión de Riesgo (Banda Escalonada Ratchet con Promoción Dinámica)
                SeparationThresholdTicks                    = 10;     // (Compatibilidad) Si es mayor a 10 ticks, se usa para ascensos dinámicos
                SlBufferTicks                               = 2;      // Colchón de respiro detrás de la Banda Azul 1.75 para el SL inicial (2 ticks v1.41)
                UseProfitMilestoneSlUpgrade                 = true;   // Activar promoción de SL a Banda 3.5 al alcanzar meta de ganancia en dólares
                ProfitMilestoneDollars                      = 40.0;   // Meta de ganancia en USD para mover SL a Banda 3.5 ($40 MES nativo / auto-escala a $400 en ES)
                MilestoneAheadTicks                         = 1;      // Ticks adelante de la Banda 3.5 al alcanzar los $40 (1 tick en favor)
                MaxCatastrophicStopTicks                    = 40;     // Stop de emergencia máximo (10 puntos en ES = $500 / $50 MES)
                UseTrailingChoke                            = false;  // Por ahora desactivado para probar la banda pura (activable a demanda)
                ChokeActivationProfitTicks                  = 20;     // Ticks mínimos de ganancia antes de iniciar ahogo si se activa Choke
                ChokeLookbackBars                           = 2;      // Mínimo de últimas N barras para anclar el Choke
                ChokeOffsetTicks                            = 2;      // Ticks de colchón detrás del Low/High de las N barras del Choke
                ExitOnBand35Reentry                         = false;  // false: respeta el SL en Banda 1.75 o 3.5 sin cortar prematuro
                UseFixedTakeProfit                          = false;  // false: Deja correr la tendencia con la banda. true: TP fijo
                FixedTakeProfitTicks                        = 60;     // 15 puntos en ES (+$750) si se activa TP fijo

                // 07. Parámetros de Canales e Indicadores
                KeltnerBigMult                              = 3.5;
                KeltnerSmallMult                            = 1.75;
                KeltnerPeriod                               = 52;
                EmaPeriod                                   = 200;
            }
            else if (State == State.DataLoaded)
            {
                // Detección automática de instrumento Mini ES vs Micro MES
                isMicroInstrument = Instrument.MasterInstrument.Name.Contains("MES");
                instrumentMultiplier = isMicroInstrument ? 1.0 : 10.0; // En MES es x1 ($40 = $40). En ES es x10 ($40 = $400).

                // Instanciación de Indicadores Técnicos en memoria (Sin AddChartIndicator para no alterar plantilla visual)
                keltnerBig   = KeltnerChannel(Close, KeltnerBigMult, KeltnerPeriod);
                keltnerSmall = KeltnerChannel(Close, KeltnerSmallMult, KeltnerPeriod);
                ema200       = EMA(Close, EmaPeriod);
                tema9        = TEMA(Close, TemaPeriod);

                // En modo Backtest o Strategy Analyzer, se activa automáticamente
                if (ChartControl == null)
                {
                    isStrategyActive = true;
                    Print("[DUMBO] Modo Headless (Strategy Analyzer / Backtest) detectado. Estrategia auto-activada.");
                }
            }
            else if (State == State.Historical)
            {
                // Crear botón WPF en el hilo de interfaz gráfica si está en un Chart activo
                if (ChartControl != null && UserControlCollection != null)
                {
                    Dispatcher.InvokeAsync(() => { CreateWPFControls(); });
                }
            }
            else if (State == State.Terminated)
            {
                // Limpieza de recursos WPF al descargar o cerrar la estrategia
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
            // Protección de datos suficientes
            if (CurrentBar < Math.Max(BarsRequiredToTrade, Math.Max(EmaPeriod, MinBarsEmaOpposite)))
                return;

            // 1. ACTUALIZAR CONTADORES DE MERCADO
            // Corre en TODAS las barras para mantener la memoria matemática de la EMA 200 y las velas flotando
            UpdateMarketCounters();

            // 2. FILTRO DE HORARIO RTH ESTRICTO (09:50 AM - 03:45 PM EST)
            int currentTime = ToTime(Time[0]);
            if (currentTime >= ForceCloseTime)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    ExitLong("Exit_Dumbo_ForceClose", "");
                    ExitShort("Exit_Dumbo_ForceClose", "");
                }
                return;
            }

            // 3. GESTIÓN DE POSICIÓN ABIERTA (TRAILING CHOKE Y RE-ENTRADA EN BANDA)
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                GestionarPosicionAbierta();
                return; // Mientras esté en posición, no evalúa nuevas entradas
            }

            // 4. VERIFICACIÓN DE BOTÓN DE PÁNICO / PAUSA
            if (!isStrategyActive)
                return;

            // 5. FILTRO DE VENTANA OPERATIVA PARA NUEVAS ENTRADAS
            if (currentTime < StartTime || currentTime >= StopEntriesTime)
                return;

            // 6. COOLDOWN TRAS ÚLTIMA SALIDA
            if (CurrentBar - lastExitBar < CooldownBarsAfterExit)
                return;

            // 7. EVALUAR SEÑALES DE ENTRADA DUMBO
            EvaluarEntradasDumbo();
        }
        #endregion

        #region Métodos de Lógica Cuantitativa y Disparo

        // =================================================================================================
        // ACTUALIZACIÓN DE CONTADORES: Mide cuántas barras lleva la EMA 200 en el abismo opuesto
        // y cuántas velas consecutivas flotan por fuera de la Banda 3.5.
        // =================================================================================================
        private void UpdateMarketCounters()
        {
            double emaVal = ema200[0];

            // Determinar la referencia de la banda opuesta según configuración
            double upperOpposite = (EmaOppositeBandTarget == BandaOpuestaEnum.BandaGrande35) ? keltnerBig.Upper[0] : keltnerSmall.Upper[0];
            double lowerOpposite = (EmaOppositeBandTarget == BandaOpuestaEnum.BandaGrande35) ? keltnerBig.Lower[0] : keltnerSmall.Lower[0];

            // Conteo de EMA 200 en el abismo inferior (para compras Long)
            if (emaVal < lowerOpposite)
            {
                barsEmaBelowOppositeCount++;
            }
            else
            {
                barsEmaBelowOppositeCount = 0; // Se resetea si la EMA toca o entra a la banda
            }

            // Conteo de EMA 200 en el abismo superior (para ventas Short)
            if (emaVal > upperOpposite)
            {
                barsEmaAboveOppositeCount++;
            }
            else
            {
                barsEmaAboveOppositeCount = 0; // Se resetea si la EMA toca o entra a la banda
            }

            // Conteo de velas consecutivas flotando por fuera de la Banda 3.5
            bool isFloatingLong = StrictFloatingLowHigh ? (Low[0] > keltnerBig.Upper[0]) : (Close[0] > keltnerBig.Upper[0]);
            bool isFloatingShort = StrictFloatingLowHigh ? (High[0] < keltnerBig.Lower[0]) : (Close[0] < keltnerBig.Lower[0]);

            if (isFloatingLong)
            {
                floatingBarsLongCount++;
            }
            else
            {
                floatingBarsLongCount = 0;
                longFiredInWave = false; // Al regresar adentro, se rearma la ola para futuros trades
            }

            if (isFloatingShort)
            {
                floatingBarsShortCount++;
            }
            else
            {
                floatingBarsShortCount = 0;
                shortFiredInWave = false; // Al regresar adentro, se rearma la ola para futuros trades
            }
        }

        // =================================================================================================
        // EVALUACIÓN DE ENTRADAS DUMBO: Dispara cuando se cumplen las 3 condiciones de vuelo
        // =================================================================================================
        private void EvaluarEntradasDumbo()
        {
            // === 1. EVALUACIÓN DUMBO LONG (ELEFANTE ALCISTA) ===
            // Condición inicial: velitas despegadas fuera de Banda 3.5
            if (!longFiredInWave && floatingBarsLongCount >= FloatingBarsRequired)
            {
                double separationTicks = (Close[0] - keltnerBig.Upper[0]) / TickSize;
                double temaSlope = (tema9[0] - tema9[TemaSlopeLookback]) / TemaSlopeLookback;
                double emaSlope = (ema200[0] - ema200[EmaSlopeLookback]);

                // 1. Check de Separación Mínima (12 ticks)
                if (separationTicks < MinSeparationFromBand35Ticks)
                {
                    Print(string.Format("{0} - [DUMBO v1.4 | FILTRO DESCARTADO (LARGO)]\n" +
                                        ">>> MOTIVO: Separación insuficiente fuera de Banda 3.5 ({1:F1}tk < {2}tk mín). Precio aún no despega.\n" +
                                        ">>> RADIOGRAFÍA: Sep Banda 3.5: {1:F1}tk (mín {2}tk) | Velas Flotando: {3} (mín {4}) | EMA200 Abismo: {5}b (mín {6}b) | Pend EMA200: {7:+0.0000;-0.0000} | TEMA9 Slope: {8:+0.0000;-0.0000}",
                        Time[0], separationTicks, MinSeparationFromBand35Ticks, floatingBarsLongCount, FloatingBarsRequired, barsEmaBelowOppositeCount, MinBarsEmaOpposite, emaSlope, temaSlope));
                    return;
                }

                // 2. Check de EMA 200 en el abismo opuesto (>= 60 barras)
                if (barsEmaBelowOppositeCount < MinBarsEmaOpposite)
                {
                    Print(string.Format("{0} - [DUMBO v1.4 | FILTRO DESCARTADO (LARGO)]\n" +
                                        ">>> MOTIVO: EMA 200 no lleva suficiente tiempo en el abismo ({1}b < {2}b mín). Falta maduración tendencial macro.\n" +
                                        ">>> RADIOGRAFÍA: Sep Banda 3.5: {3:F1}tk (mín {4}tk) | Velas Flotando: {5} (mín {6}) | EMA200 Abismo: {1}b (mín {2}b) | Pend EMA200: {7:+0.0000;-0.0000} | TEMA9 Slope: {8:+0.0000;-0.0000}",
                        Time[0], barsEmaBelowOppositeCount, MinBarsEmaOpposite, separationTicks, MinSeparationFromBand35Ticks, floatingBarsLongCount, FloatingBarsRequired, emaSlope, temaSlope));
                    return;
                }

                // 3. Check de Pendiente EMA 200 Macro
                bool ema200SlopeOk = !RequireEmaSlope || (ema200[0] >= ema200[EmaSlopeLookback]);
                if (!ema200SlopeOk)
                {
                    Print(string.Format("{0} - [DUMBO v1.4 | FILTRO DESCARTADO (LARGO)]\n" +
                                        ">>> MOTIVO: Pendiente EMA 200 en contra ({1:F4} < 0.00). Tendencia macro no acompaña.\n" +
                                        ">>> RADIOGRAFÍA: Sep Banda 3.5: {2:F1}tk (mín {3}tk) | Velas Flotando: {4} (mín {5}) | EMA200 Abismo: {6}b (mín {7}b) | Pend EMA200: {1:+0.0000;-0.0000} | TEMA9 Slope: {8:+0.0000;-0.0000}",
                        Time[0], emaSlope, separationTicks, MinSeparationFromBand35Ticks, floatingBarsLongCount, FloatingBarsRequired, barsEmaBelowOppositeCount, MinBarsEmaOpposite, temaSlope));
                    return;
                }

                // 4. Check de Pendiente TEMA 9 Micro
                bool tema9SlopeOk = !RequireTemaSlope || (tema9[0] > tema9[1] && tema9[0] > tema9[TemaSlopeLookback]);
                if (!tema9SlopeOk)
                {
                    Print(string.Format("{0} - [DUMBO v1.4 | FILTRO DESCARTADO (LARGO)]\n" +
                                        ">>> MOTIVO: TEMA 9 no tiene inercia o dobló en contra (TEMA9[0]={1:F2} vs [1]={2:F2}). Falta aceleración micro.\n" +
                                        ">>> RADIOGRAFÍA: Sep Banda 3.5: {3:F1}tk (mín {4}tk) | Velas Flotando: {5} (mín {6}) | EMA200 Abismo: {7}b (mín {8}b) | Pend EMA200: {9:+0.0000;-0.0000} | TEMA9 Slope: {10:+0.0000;-0.0000}",
                        Time[0], tema9[0], tema9[1], separationTicks, MinSeparationFromBand35Ticks, floatingBarsLongCount, FloatingBarsRequired, barsEmaBelowOppositeCount, MinBarsEmaOpposite, emaSlope, temaSlope));
                    return;
                }

                // Si superó todos los filtros: ¡PUM, ADIÓS!
                EjecutarEntradaLong();
                return;
            }

            // === 2. EVALUACIÓN DUMBO SHORT (ELEFANTE BAJISTA) ===
            if (!shortFiredInWave && floatingBarsShortCount >= FloatingBarsRequired)
            {
                double separationTicks = (keltnerBig.Lower[0] - Close[0]) / TickSize;
                double temaSlope = (tema9[TemaSlopeLookback] - tema9[0]) / TemaSlopeLookback;
                double emaSlope = (ema200[EmaSlopeLookback] - ema200[0]);

                // 1. Check de Separación Mínima (12 ticks)
                if (separationTicks < MinSeparationFromBand35Ticks)
                {
                    Print(string.Format("{0} - [DUMBO v1.4 | FILTRO DESCARTADO (CORTO)]\n" +
                                        ">>> MOTIVO: Separación insuficiente fuera de Banda 3.5 ({1:F1}tk < {2}tk mín). Precio aún no cae suficiente.\n" +
                                        ">>> RADIOGRAFÍA: Sep Banda 3.5: {1:F1}tk (mín {2}tk) | Velas Flotando: {3} (mín {4}) | EMA200 Abismo: {5}b (mín {6}b) | Pend EMA200: {7:+0.0000;-0.0000} | TEMA9 Slope: {8:+0.0000;-0.0000}",
                        Time[0], separationTicks, MinSeparationFromBand35Ticks, floatingBarsShortCount, FloatingBarsRequired, barsEmaAboveOppositeCount, MinBarsEmaOpposite, emaSlope, temaSlope));
                    return;
                }

                // 2. Check de EMA 200 en el abismo superior (>= 60 barras)
                if (barsEmaAboveOppositeCount < MinBarsEmaOpposite)
                {
                    Print(string.Format("{0} - [DUMBO v1.4 | FILTRO DESCARTADO (CORTO)]\n" +
                                        ">>> MOTIVO: EMA 200 no lleva suficiente tiempo en el abismo superior ({1}b < {2}b mín). Falta maduración tendencial macro.\n" +
                                        ">>> RADIOGRAFÍA: Sep Banda 3.5: {3:F1}tk (mín {4}tk) | Velas Flotando: {5} (mín {6}) | EMA200 Abismo: {1}b (mín {2}b) | Pend EMA200: {7:+0.0000;-0.0000} | TEMA9 Slope: {8:+0.0000;-0.0000}",
                        Time[0], barsEmaAboveOppositeCount, MinBarsEmaOpposite, separationTicks, MinSeparationFromBand35Ticks, floatingBarsShortCount, FloatingBarsRequired, emaSlope, temaSlope));
                    return;
                }

                // 3. Check de Pendiente EMA 200 Macro
                bool ema200SlopeOk = !RequireEmaSlope || (ema200[0] <= ema200[EmaSlopeLookback]);
                if (!ema200SlopeOk)
                {
                    Print(string.Format("{0} - [DUMBO v1.4 | FILTRO DESCARTADO (CORTO)]\n" +
                                        ">>> MOTIVO: Pendiente EMA 200 en contra ({1:F4} > 0.00). Tendencia macro no acompaña.\n" +
                                        ">>> RADIOGRAFÍA: Sep Banda 3.5: {2:F1}tk (mín {3}tk) | Velas Flotando: {4} (mín {5}) | EMA200 Abismo: {6}b (mín {7}b) | Pend EMA200: {1:+0.0000;-0.0000} | TEMA9 Slope: {8:+0.0000;-0.0000}",
                        Time[0], emaSlope, separationTicks, MinSeparationFromBand35Ticks, floatingBarsShortCount, FloatingBarsRequired, barsEmaAboveOppositeCount, MinBarsEmaOpposite, temaSlope));
                    return;
                }

                // 4. Check de Pendiente TEMA 9 Micro
                bool tema9SlopeOk = !RequireTemaSlope || (tema9[0] < tema9[1] && tema9[0] < tema9[TemaSlopeLookback]);
                if (!tema9SlopeOk)
                {
                    Print(string.Format("{0} - [DUMBO v1.4 | FILTRO DESCARTADO (CORTO)]\n" +
                                        ">>> MOTIVO: TEMA 9 no tiene inercia o dobló hacia arriba (TEMA9[0]={1:F2} vs [1]={2:F2}). Falta aceleración bajista.\n" +
                                        ">>> RADIOGRAFÍA: Sep Banda 3.5: {3:F1}tk (mín {4}tk) | Velas Flotando: {5} (mín {6}) | EMA200 Abismo: {7}b (mín {8}b) | Pend EMA200: {9:+0.0000;-0.0000} | TEMA9 Slope: {10:+0.0000;-0.0000}",
                        Time[0], tema9[0], tema9[1], separationTicks, MinSeparationFromBand35Ticks, floatingBarsShortCount, FloatingBarsRequired, barsEmaAboveOppositeCount, MinBarsEmaOpposite, emaSlope, temaSlope));
                    return;
                }

                // Si superó todos los filtros: ¡PUM, ADIÓS!
                EjecutarEntradaShort();
                return;
            }
        }

        // =================================================================================================
        // EJECUCIÓN LONG Y ANCLAJE DE STOP LOSS INICIAL (BANDA AZUL 1.75 - ESPACIO DUMBO v1.41)
        // =================================================================================================
        private void EjecutarEntradaLong()
        {
            entryPriceGlobal = Close[0];
            entryBarGlobal = CurrentBar;
            longFiredInWave = true; // Bloquea disparos duplicados en esta misma ola
            isMilestonePromoted = false; // Reset de promoción por meta de dólares

            // Medir distancia de penetración por fuera de la Banda 3.5
            double separationTicks = (Close[0] - keltnerBig.Upper[0]) / TickSize;

            // ANCLAJE INICIAL DUMBO v1.41: Dumbo es un elefante grande y necesita espacio para volar.
            // Siempre inicia anclado en la BANDA AZUL 1.75 con colchón de 2 ticks (SlBufferTicks)
            slAnchoredToBigBand = false;
            currentSlPriceGlobal = keltnerSmall.Upper[0] - (SlBufferTicks * TickSize);

            // Limitar por Stop Catastrófico de Emergencia
            double catastrophicSl = entryPriceGlobal - (MaxCatastrophicStopTicks * TickSize);
            currentSlPriceGlobal = Math.Max(currentSlPriceGlobal, catastrophicSl);

            // Redondeo al tick exacto
            currentSlPriceGlobal = Instrument.MasterInstrument.RoundToTickSize(currentSlPriceGlobal);

            // Disparo de orden
            EnterLong(Contracts, "Dumbo_Long");
            double temaSlope = (tema9[0] - tema9[TemaSlopeLookback]) / TemaSlopeLookback;
            double emaSlope = (ema200[0] - ema200[EmaSlopeLookback]);
            Print(string.Format("*************************************************************************************************************\n" +
                                "{0} - [DUMBO v{1} | ¡PUM, ADIÓS! ELEFANTE ALCISTA DISPARADO] Despegue fuera de Banda 3.5: +{2:F1} ticks (mín {3}tk).\n" +
                                ">>> DETALLE: Entrada: {4:F2} | SL Inicial: {5:F2} (Banda Azul 1.75 - Colchón {6}tk) | Velas Flotando: {7} | EMA200 Abismo: {8}b | Pend EMA200: {9:+0.0000;-0.0000} | TEMA9 Slope: {10:+0.0000;-0.0000} | ¡A volar con el viento!\n" +
                                "*************************************************************************************************************",
                Time[0], STRATEGY_VERSION, separationTicks, MinSeparationFromBand35Ticks, entryPriceGlobal, currentSlPriceGlobal, SlBufferTicks, floatingBarsLongCount, barsEmaBelowOppositeCount, emaSlope, temaSlope));
        }

        // =================================================================================================
        // EJECUCIÓN SHORT Y ANCLAJE DE STOP LOSS INICIAL (BANDA AZUL 1.75 - ESPACIO DUMBO v1.41)
        // =================================================================================================
        private void EjecutarEntradaShort()
        {
            entryPriceGlobal = Close[0];
            entryBarGlobal = CurrentBar;
            shortFiredInWave = true; // Bloquea disparos duplicados en esta misma ola
            isMilestonePromoted = false; // Reset de promoción por meta de dólares

            // Medir distancia de penetración por fuera de la Banda 3.5
            double separationTicks = (keltnerBig.Lower[0] - Close[0]) / TickSize;

            // ANCLAJE INICIAL DUMBO v1.41: Dumbo es un elefante grande y necesita espacio para volar.
            // Siempre inicia anclado en la BANDA AZUL 1.75 con colchón de 2 ticks (SlBufferTicks)
            slAnchoredToBigBand = false;
            currentSlPriceGlobal = keltnerSmall.Lower[0] + (SlBufferTicks * TickSize);

            // Limitar por Stop Catastrófico de Emergencia
            double catastrophicSl = entryPriceGlobal + (MaxCatastrophicStopTicks * TickSize);
            currentSlPriceGlobal = Math.Min(currentSlPriceGlobal, catastrophicSl);

            // Redondeo al tick exacto
            currentSlPriceGlobal = Instrument.MasterInstrument.RoundToTickSize(currentSlPriceGlobal);

            // Disparo de orden
            EnterShort(Contracts, "Dumbo_Short");
            double temaSlope = (tema9[TemaSlopeLookback] - tema9[0]) / TemaSlopeLookback;
            double emaSlope = (ema200[EmaSlopeLookback] - ema200[0]);
            Print(string.Format("*************************************************************************************************************\n" +
                                "{0} - [DUMBO v{1} | ¡PUM, ADIÓS! ELEFANTE BAJISTA DISPARADO] Caída fuera de Banda 3.5: +{1:F1} ticks (mín {3}tk).\n" +
                                ">>> DETALLE: Entrada: {4:F2} | SL Inicial: {5:F2} (Banda Azul 1.75 - Colchón {6}tk) | Velas Flotando: {7} | EMA200 Abismo: {8}b | Pend EMA200: {9:+0.0000;-0.0000} | TEMA9 Slope: {10:+0.0000;-0.0000} | ¡A surfear la cascada!\n" +
                                "*************************************************************************************************************",
                Time[0], STRATEGY_VERSION, separationTicks, MinSeparationFromBand35Ticks, entryPriceGlobal, currentSlPriceGlobal, SlBufferTicks, floatingBarsShortCount, barsEmaAboveOppositeCount, emaSlope, temaSlope));
        }

        // =================================================================================================
        // GESTIÓN DE POSICIÓN ABIERTA (TRAILING DINÁMICO DE BANDA + PROMOCIÓN A BANDA 3.5 + CHOKE CONDICIONAL)
        // =================================================================================================
        private void GestionarPosicionAbierta()
        {
            // Evitar gestionar en la misma barra exacta de entrada
            if (CurrentBar <= entryBarGlobal)
                return;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                // 1. Take Profit Fijo Opcional
                if (UseFixedTakeProfit && Close[0] >= entryPriceGlobal + (FixedTakeProfitTicks * TickSize))
                {
                    ExitLong("Exit_Dumbo_FixedTP", "Dumbo_Long");
                    lastExitBar = CurrentBar;
                    Print(string.Format("{0} [DUMBO TP] Long cerrado en Take Profit Fijo a {1:F2}", Time[0], Close[0]));
                    return;
                }

                // 2. Promoción Dinámica de SL:
                if (!slAnchoredToBigBand)
                {
                    // A) Vía separación: Si el precio sube a >= 10 ticks fuera de Banda 3.5
                    double currentSeparationTicks = (Close[0] - keltnerBig.Upper[0]) / TickSize;
                    if (currentSeparationTicks >= SeparationThresholdTicks)
                    {
                        slAnchoredToBigBand = true; // ¡Ascendido a Banda Grande 3.5 estándar!
                        Print(string.Format("{0} [DUMBO UPGRADE SL] ¡Precio alcanzó {1:F1} ticks fuera! SL ascendido a Banda 3.5 Grande",
                            Time[0], currentSeparationTicks));
                    }
                    // B) Vía Escalón de Ganancia $400: Si el trade alcanza $400 USD sin haber tocado los 10 ticks fuera
                    else if (UseProfitMilestoneSlUpgrade)
                    {
                        double currentProfitDollars = (High[0] - entryPriceGlobal) * Instrument.MasterInstrument.PointValue * Contracts;
                        if (currentProfitDollars >= (ProfitMilestoneDollars * instrumentMultiplier))
                        {
                            slAnchoredToBigBand = true;
                            isMilestonePromoted = true; // ¡Promovido a 1 tick adelante de Banda 3.5!
                            Print(string.Format("{0} [DUMBO UPGRADE SL $400] ¡Trade alcanzó +${1:F2} USD! SL reposicionado a {2} tick(s) adelante de Banda 3.5 Grande",
                                Time[0], currentProfitDollars, MilestoneAheadTicks));
                        }
                    }
                }

                // 3. Trailing Dinámico de la Banda Seleccionada (Ratchet hacia arriba)
                // A medida que el canal sube con la tendencia, el SL sube con la banda correspondiente
                double targetBandPrice = slAnchoredToBigBand ? keltnerBig.Upper[0] : keltnerSmall.Upper[0];
                double buffer = isMilestonePromoted ? (-MilestoneAheadTicks * TickSize) : (SlBufferTicks * TickSize);
                double candidateBandSl = Instrument.MasterInstrument.RoundToTickSize(targetBandPrice - buffer);
                if (candidateBandSl > currentSlPriceGlobal)
                {
                    currentSlPriceGlobal = candidateBandSl;
                }

                // 4. Trailing Choke Vela a Vela (Opcional a partir de determinada ganancia alcanzada)
                if (UseTrailingChoke)
                {
                    double currentProfitTicks = (Close[0] - entryPriceGlobal) / TickSize;
                    if (currentProfitTicks >= ChokeActivationProfitTicks)
                    {
                        double lowestLow = Low[0];
                        for (int i = 1; i < ChokeLookbackBars; i++)
                        {
                            if (Low[i] < lowestLow)
                                lowestLow = Low[i];
                        }

                        double candidateChokeSl = lowestLow - (ChokeOffsetTicks * TickSize);
                        candidateChokeSl = Instrument.MasterInstrument.RoundToTickSize(candidateChokeSl);

                        if (candidateChokeSl > currentSlPriceGlobal)
                        {
                            currentSlPriceGlobal = candidateChokeSl;
                        }
                    }
                }

                // 5. Salida por Re-entrada en Banda 3.5 (Opcional, desactivada por defecto para no cortar Banda 1.75)
                if (ExitOnBand35Reentry && Close[0] < keltnerBig.Upper[0])
                {
                    ExitLong("Exit_Dumbo_BandReentry", "Dumbo_Long");
                    lastExitBar = CurrentBar;
                    Print(string.Format("{0} [DUMBO SALIDA] Long cerrado por re-entrada en Banda 3.5 a {1:F2}", Time[0], Close[0]));
                    return;
                }

                // 6. Salida por Stop Loss de Banda / Choke alcanzado
                if (Close[0] <= currentSlPriceGlobal || Low[0] <= currentSlPriceGlobal)
                {
                    ExitLong("Exit_Dumbo_SL", "Dumbo_Long");
                    lastExitBar = CurrentBar;
                    Print(string.Format("{0} [DUMBO STOP] Long cerrado por SL ({1}) a {2:F2} (Nivel SL: {3:F2})",
                        Time[0], (slAnchoredToBigBand ? "Banda 3.5" : "Banda 1.75"), Close[0], currentSlPriceGlobal));
                    return;
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                // 1. Take Profit Fijo Opcional
                if (UseFixedTakeProfit && Close[0] <= entryPriceGlobal - (FixedTakeProfitTicks * TickSize))
                {
                    ExitShort("Exit_Dumbo_FixedTP", "Dumbo_Short");
                    lastExitBar = CurrentBar;
                    Print(string.Format("{0} [DUMBO TP] Short cerrado en Take Profit Fijo a {1:F2}", Time[0], Close[0]));
                    return;
                }

                // 2. Promoción Dinámica de SL:
                if (!slAnchoredToBigBand)
                {
                    // A) Vía separación: Si el precio cae a >= 10 ticks fuera de Banda 3.5
                    double currentSeparationTicks = (keltnerBig.Lower[0] - Close[0]) / TickSize;
                    if (currentSeparationTicks >= SeparationThresholdTicks)
                    {
                        slAnchoredToBigBand = true; // ¡Ascendido a Banda Grande 3.5 estándar!
                        Print(string.Format("{0} [DUMBO UPGRADE SL] ¡Precio alcanzó {1:F1} ticks fuera! SL descendido a Banda 3.5 Grande",
                            Time[0], currentSeparationTicks));
                    }
                    // B) Vía Escalón de Ganancia $400: Si el trade alcanza $400 USD sin haber tocado los 10 ticks fuera
                    else if (UseProfitMilestoneSlUpgrade)
                    {
                        double currentProfitDollars = (entryPriceGlobal - Low[0]) * Instrument.MasterInstrument.PointValue * Contracts;
                        if (currentProfitDollars >= (ProfitMilestoneDollars * instrumentMultiplier))
                        {
                            slAnchoredToBigBand = true;
                            isMilestonePromoted = true; // ¡Promovido a 1 tick adelante de Banda 3.5!
                            Print(string.Format("{0} [DUMBO UPGRADE SL $400] ¡Trade alcanzó +${1:F2} USD! SL reposicionado a {2} tick(s) adelante de Banda 3.5 Grande",
                                Time[0], currentProfitDollars, MilestoneAheadTicks));
                        }
                    }
                }

                // 3. Trailing Dinámico de la Banda Seleccionada (Ratchet hacia abajo)
                // A medida que el canal desciende con la tendencia, el SL baja con la banda correspondiente
                double targetBandPrice = slAnchoredToBigBand ? keltnerBig.Lower[0] : keltnerSmall.Lower[0];
                double buffer = isMilestonePromoted ? (-MilestoneAheadTicks * TickSize) : (SlBufferTicks * TickSize);
                double candidateBandSl = Instrument.MasterInstrument.RoundToTickSize(targetBandPrice + buffer);
                if (candidateBandSl < currentSlPriceGlobal)
                {
                    currentSlPriceGlobal = candidateBandSl;
                }

                // 4. Trailing Choke Vela a Vela (Opcional a partir de determinada ganancia alcanzada)
                if (UseTrailingChoke)
                {
                    double currentProfitTicks = (entryPriceGlobal - Close[0]) / TickSize;
                    if (currentProfitTicks >= ChokeActivationProfitTicks)
                    {
                        double highestHigh = High[0];
                        for (int i = 1; i < ChokeLookbackBars; i++)
                        {
                            if (High[i] > highestHigh)
                                highestHigh = High[i];
                        }

                        double candidateChokeSl = highestHigh + (ChokeOffsetTicks * TickSize);
                        candidateChokeSl = Instrument.MasterInstrument.RoundToTickSize(candidateChokeSl);

                        if (candidateChokeSl < currentSlPriceGlobal)
                        {
                            currentSlPriceGlobal = candidateChokeSl;
                        }
                    }
                }

                // 5. Salida por Re-entrada en Banda 3.5 (Opcional, desactivada por defecto para no cortar Banda 1.75)
                if (ExitOnBand35Reentry && Close[0] > keltnerBig.Lower[0])
                {
                    ExitShort("Exit_Dumbo_BandReentry", "Dumbo_Short");
                    lastExitBar = CurrentBar;
                    Print(string.Format("{0} [DUMBO SALIDA] Short cerrado por re-entrada en Banda 3.5 a {1:F2}", Time[0], Close[0]));
                    return;
                }

                // 6. Salida por Stop Loss de Banda / Choke alcanzado
                if (Close[0] >= currentSlPriceGlobal || High[0] >= currentSlPriceGlobal)
                {
                    ExitShort("Exit_Dumbo_SL", "Dumbo_Short");
                    lastExitBar = CurrentBar;
                    Print(string.Format("{0} [DUMBO STOP] Short cerrado por SL ({1}) a {2:F2} (Nivel SL: {3:F2})",
                        Time[0], (slAnchoredToBigBand ? "Banda 3.5" : "Banda 1.75"), Close[0], currentSlPriceGlobal));
                    return;
                }
            }
        }
        #endregion

        #region Interfaz WPF: Botón de Pánico y Atajos de Teclado
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
                Content = isStrategyActive ? "Dumbo ACTIVA (v1.4)" : "Dumbo PAUSADA (v1.4)",
                Height = 30,
                Width = 220,
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
                panicButton.Content = isStrategyActive ? string.Format("Dumbo ACTIVA (v{0})", STRATEGY_VERSION) : string.Format("Dumbo PAUSADA (v{0})", STRATEGY_VERSION);
                panicButton.Background = isStrategyActive ? Brushes.DarkGreen : Brushes.Crimson;
            }

            if (!isStrategyActive)
            {
                TriggerCustomEvent(o =>
                {
                    if (Position.MarketPosition != MarketPosition.Flat)
                    {
                        ExitLong("Boton_Panico_WPF", "");
                        ExitShort("Boton_Panico_WPF", "");
                        Print(Time[0] + " - [BOTÓN DE PÁNICO DUMBO] ¡Posiciones liquidadas a mercado!");
                    }
                }, null);
            }
            else
            {
                Print(Time[0] + " - [CONTROL MANUAL DUMBO] Estrategia reanudada por el usuario.");
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
        [Display(Name = "Versión", GroupName = "01. Información", Order = 1)]
        [ReadOnly(true)]
        public string Version
        {
            get { return string.Format("{0} (MES / ES) - Dumbo Super-Expansión", STRATEGY_VERSION); }
            set { }
        }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Número de Contratos", Description = "Cantidad total de contratos a operar (todos se gestionan en bloque)", GroupName = "02. Gestión de Posición", Order = 1)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hora Inicio RTH (HHMMSS)", Description = "09:35 AM = 93500 (inicia tras los primeros 5 min)", GroupName = "03. Horario Operativo", Order = 1)]
        public int StartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hora Fin Entradas (HHMMSS)", Description = "03:45 PM = 154500", GroupName = "03. Horario Operativo", Order = 2)]
        public int StopEntriesTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hora Cierre Forzoso (HHMMSS)", Description = "04:00 PM = 160000", GroupName = "03. Horario Operativo", Order = 3)]
        public int ForceCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Velas Flotando Fuera Requeridas", Description = "Mínimo de velas consecutivas despegadas fuera de Banda 3.5 (por defecto: 3)", GroupName = "04. Parámetros Gatillo Dumbo", Order = 1)]
        public int FloatingBarsRequired { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Separación Estricta (Low/High)", Description = "true: Toda la vela flota afuera (Low > Banda 3.5). false: Solo el Close fuera.", GroupName = "04. Parámetros Gatillo Dumbo", Order = 2)]
        public bool StrictFloatingLowHigh { get; set; }

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Separación Mínima de Banda 3.5 (Ticks)", Description = "Regla Estructural: Exige que el precio esté al menos N ticks fuera de Banda 3.5 para disparar (por defecto: 12 ticks)", GroupName = "04. Parámetros Gatillo Dumbo", Order = 3)]
        public int MinSeparationFromBand35Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Cooldown tras Salida (Barras)", Description = "Barras de espera obligatoria tras cerrar un trade antes de volver a evaluar entradas", GroupName = "04. Parámetros Gatillo Dumbo", Order = 4)]
        public int CooldownBarsAfterExit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exigir Pendiente TEMA 9 Micro", Description = "true: Requiere que la TEMA 9 apunte a favor del trade de forma estricta (barra a barra y vs 2 barras atrás)", GroupName = "04. Parámetros Gatillo Dumbo", Order = 4)]
        public bool RequireTemaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name = "Periodo TEMA 9 Micro", Description = "Periodo de la media rápida TEMA (idéntica a CascadaVega, por defecto: 9)", GroupName = "04. Parámetros Gatillo Dumbo", Order = 5)]
        public int TemaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Lookback Pendiente TEMA 9", Description = "Barras hacia atrás para medir la pendiente de TEMA 9 (por defecto: 2 barras)", GroupName = "04. Parámetros Gatillo Dumbo", Order = 6)]
        public int TemaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Range(10, 500)]
        [Display(Name = "Barras Mínimas EMA en Abismo", Description = "Cantidad de barras continuas que la EMA 200 debe llevar fuera de la banda opuesta (por defecto: 60)", GroupName = "05. Filtro Macro EMA 200", Order = 1)]
        public int MinBarsEmaOpposite { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Banda Opuesta de Referencia", Description = "Banda contra la cual se mide si la EMA 200 está en el abismo", GroupName = "05. Filtro Macro EMA 200", Order = 2)]
        public BandaOpuestaEnum EmaOppositeBandTarget { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Lookback Pendiente EMA 200", Description = "Barras hacia atrás para calcular si la pendiente de la EMA 200 está a favor", GroupName = "05. Filtro Macro EMA 200", Order = 3)]
        public int EmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exigir Pendiente EMA a Favor", Description = "true: Requiere que la EMA 200 apunte a favor del trade. false: Ignora la pendiente.", GroupName = "05. Filtro Macro EMA 200", Order = 4)]
        public bool RequireEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name = "Umbral Separación SL (Ticks)", Description = "Si el despegue es >= N ticks, SL en Banda 3.5. Si es menor, SL en Banda 1.75 (por defecto: 10).", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 1)]
        public int SeparationThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Colchón Respiro SL Banda Azul (Ticks)", Description = "Ticks de margen detrás de la Banda Azul 1.75 para colocar el Stop Loss inicial (por defecto: 2 ticks v1.41)", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 2)]
        public int SlBufferTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Upgrade SL por Ganancia ($)", Description = "true: Mueve el SL a Banda 3.5 (1 tick adelante) al alcanzar la meta en dólares si aún estaba en Banda 1.75", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 3)]
        public bool UseProfitMilestoneSlUpgrade { get; set; }

        [NinjaScriptProperty]
        [Range(5.0, 5000.0)]
        [Display(Name = "Meta de Ganancia para Upgrade ($)", Description = "Monto en dólares para ascender el SL a Banda 3.5 ($40 MES nativo / auto-escala a $400 en ES)", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 4)]
        public double ProfitMilestoneDollars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Ticks Adelante de Banda en Upgrade", Description = "Ticks por delante de la Banda 3.5 al ascender por la meta de ganancia (por defecto: 1 tick)", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 5)]
        public int MilestoneAheadTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Stop Catastrófico Máximo (Ticks)", Description = "Riesgo máximo absoluto de seguridad por contrato (40 ticks = 10 pts = $500 ES)", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 6)]
        public int MaxCatastrophicStopTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Trailing Choke", Description = "true: Va subiendo/bajando el Stop al extremo de las últimas N barras al alcanzar ganancia mínima", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 7)]
        public bool UseTrailingChoke { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Ganancia Mínima para Choke (Ticks)", Description = "Ticks de ganancia flotante necesarios para activar el Trailing Choke si está habilitado", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 8)]
        public int ChokeActivationProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Barras Lookback Choke", Description = "Cantidad de barras previas para anclar el Trailing Choke (por defecto: 2)", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 9)]
        public int ChokeLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Offset Choke (Ticks)", Description = "Ticks de distancia detrás del extremo de las N barras para el Choke", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 10)]
        public int ChokeOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Salir al Cerrar dentro de Banda 3.5", Description = "true: Si una vela cierra de regreso dentro de la Banda 3.5, liquida inmediatamente", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 11)]
        public bool ExitOnBand35Reentry { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Take Profit Fijo", Description = "true: Cierra con ganancia fija. false: Deja correr la tendencia con la banda", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 12)]
        public bool UseFixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(10, 500)]
        [Display(Name = "Take Profit Fijo (Ticks)", Description = "Ticks de ganancia fija si UseFixedTakeProfit está activo (60 ticks = 15 pts = $750 ES)", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 13)]
        public int FixedTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 5.0)]
        [Display(Name = "Multiplicador Keltner Grande", GroupName = "07. Canales e Indicadores", Order = 1)]
        public double KeltnerBigMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 3.0)]
        [Display(Name = "Multiplicador Keltner Pequeño", GroupName = "07. Canales e Indicadores", Order = 2)]
        public double KeltnerSmallMult { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Periodo Keltner", GroupName = "07. Canales e Indicadores", Order = 3)]
        public int KeltnerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(20, 300)]
        [Display(Name = "Periodo EMA Gravedad", GroupName = "07. Canales e Indicadores", Order = 4)]
        public int EmaPeriod { get; set; }
        #endregion
    }
}
