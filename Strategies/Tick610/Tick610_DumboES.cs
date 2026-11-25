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
// VERSIÓN: 1.2 - "El Elefante Volador" (Promoción Dinámica de SL a Banda 3.5 + EMA 9 Micro + 60 Barras Abismo)
//
// CONCEPTO (ELI5):
//   Dumbo es el elefante que aprende a volar. La gran mayoría de estrategias esperan que el precio vuelva
//   a la media (reversión), pero cuando se presenta una súper-expansión tendencial institucional, el precio
//   rompe el techo de la estratósfera (Banda Keltner 3.5) y se niega a descender.
//   Al mismo tiempo, la EMA 200 (la gravedad) quedó atrapada en el sótano opuesto por más de 60 barras
//   POR FUERA DEL CANAL GRANDE 3.5. En vez de operar reversión, Dumbo se monta en el viento a favor de la inercia.
//
// NOVEDADES v1.2:
//   1. Promoción Dinámica de SL: Si al abrir el trade el precio está a < 10 ticks (SL en Banda 1.75 Azul),
//      en el momento en que el precio sube y supera los 10 ticks afuera, el SL asciende automáticamente
//      y se reposiciona a la Banda 3.5 Exterior del Canal Grande.
//   2. Filtro Macro Calibrado: Se ajusta el umbral por defecto de la EMA 200 en el abismo a 60 barras.
//   3. Filtro Micro EMA 9: La EMA 9 debe tener pendiente a favor del trade midiendo 2 barras atrás partiendo
//      de la tercera barra que está afuera de la banda (EMA 9[0] vs EMA 9[2]).
// =========================================================================================================

namespace NinjaTrader.NinjaScript.Strategies
{
    public class Tick610_DumboES : Strategy
    {
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
        private EMA            ema9;          // Media Móvil Exponencial de 9 periodos (Micro-Tendencia de Despegue)

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
                Version                                     = "v1.2 (Promoción SL + EMA 9 Micro + 60 Barras Abismo)";

                // 02. Gestión de Posición
                Contracts                                   = 1;      // Número de contratos a operar (escalable)

                // 03. Horario Operativo RTH Estricto
                StartTime                                   = 95000;  // 09:50 AM EST (espera formación inicial de apertura)
                StopEntriesTime                             = 154500; // 03:45 PM EST (corta nuevas entradas)
                ForceCloseTime                              = 160000; // 04:00 PM EST (cierra todo antes de la campana)

                // 04. Parámetros del Disparador Dumbo
                FloatingBarsRequired                        = 3;      // Mínimo de velas consecutivas completamente afuera de Banda 3.5
                StrictFloatingLowHigh                       = true;   // true: Low > Upper35 (Long) / High < Lower35 (Short). false: Close fuera.
                CooldownBarsAfterExit                       = 3;      // Barras de enfriamiento tras salir antes de poder evaluar otra entrada
                RequireEmaShortSlope                        = true;   // true: Exige pendiente de EMA 9 a favor mirando 2 barras atrás
                EmaShortPeriod                              = 9;      // Periodo de la EMA 9 rápida de despegue
                EmaShortSlopeLookback                       = 2;      // Barras hacia atrás para medir la pendiente de EMA 9 (2 barras = inicio del despegue)

                // 05. Filtro Macro EMA 200 en el Abismo
                MinBarsEmaOpposite                          = 60;     // Mínimo de barras continuas con la EMA 200 fuera del canal opuesto (calibrado a 60)
                EmaOppositeBandTarget                       = BandaOpuestaEnum.BandaGrande35; // Umbral agresivo: fuera del Canal Grande 3.5 opuesto
                EmaSlopeLookback                            = 3;      // Barras para medir la pendiente de la EMA 200
                RequireEmaSlope                             = true;   // Exigir que la pendiente de EMA 200 esté a favor del trade

                // 06. Gestión de Riesgo (Banda Escalonada Ratchet con Promoción Dinámica)
                SeparationThresholdTicks                    = 10;     // Si el precio entra a >= 10 ticks de Banda 3.5, SL en 3.5; si es menor, SL en 1.75
                SlBufferTicks                               = 2;      // Colchón de respiro detrás de la banda elegida para el SL
                MaxCatastrophicStopTicks                    = 40;     // Stop de emergencia máximo (10 puntos en ES = $500)
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
                instrumentMultiplier = isMicroInstrument ? 0.10 : 1.0;

                // Instanciación de Indicadores Técnicos en memoria (Sin AddChartIndicator para no alterar plantilla visual)
                keltnerBig   = KeltnerChannel(Close, KeltnerBigMult, KeltnerPeriod);
                keltnerSmall = KeltnerChannel(Close, KeltnerSmallMult, KeltnerPeriod);
                ema200       = EMA(Close, EmaPeriod);
                ema9         = EMA(Close, EmaShortPeriod);

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
            if (!longFiredInWave && floatingBarsLongCount >= FloatingBarsRequired)
            {
                // 1. Validación de EMA 200 en el abismo inferior (>= MinBarsEmaOpposite)
                if (barsEmaBelowOppositeCount >= MinBarsEmaOpposite)
                {
                    // 2. Validación de Pendiente EMA 200 Macro a favor (alcista o plana)
                    bool ema200SlopeOk = !RequireEmaSlope || (ema200[0] >= ema200[EmaSlopeLookback]);

                    // 3. Validación de Pendiente EMA 9 Micro a favor (mirando 2 barras atrás partiendo de la 3ra barra afuera)
                    bool ema9SlopeOk = !RequireEmaShortSlope || (ema9[0] > ema9[EmaShortSlopeLookback]);

                    if (ema200SlopeOk && ema9SlopeOk)
                    {
                        EjecutarEntradaLong();
                        return;
                    }
                }
            }

            // === 2. EVALUACIÓN DUMBO SHORT (ELEFANTE BAJISTA) ===
            if (!shortFiredInWave && floatingBarsShortCount >= FloatingBarsRequired)
            {
                // 1. Validación de EMA 200 en el abismo superior (>= MinBarsEmaOpposite)
                if (barsEmaAboveOppositeCount >= MinBarsEmaOpposite)
                {
                    // 2. Validación de Pendiente EMA 200 Macro a favor (bajista o plana)
                    bool ema200SlopeOk = !RequireEmaSlope || (ema200[0] <= ema200[EmaSlopeLookback]);

                    // 3. Validación de Pendiente EMA 9 Micro a favor (mirando 2 barras atrás partiendo de la 3ra barra afuera)
                    bool ema9SlopeOk = !RequireEmaShortSlope || (ema9[0] < ema9[EmaShortSlopeLookback]);

                    if (ema200SlopeOk && ema9SlopeOk)
                    {
                        EjecutarEntradaShort();
                        return;
                    }
                }
            }
        }

        // =================================================================================================
        // EJECUCIÓN LONG Y ANCLAJE DE STOP LOSS INICIAL (BANDA ESCALONADA)
        // =================================================================================================
        private void EjecutarEntradaLong()
        {
            entryPriceGlobal = Close[0];
            entryBarGlobal = CurrentBar;
            longFiredInWave = true; // Bloquea disparos duplicados en esta misma ola

            // Medir distancia de penetración por fuera de la Banda 3.5
            double separationTicks = (Close[0] - keltnerBig.Upper[0]) / TickSize;

            // Determinar Stop Loss Inicial según umbral de penetración (Regla de 10 ticks)
            if (separationTicks >= SeparationThresholdTicks)
            {
                // Despegue fuerte (>= 10 ticks): SL anclado a Banda 3.5 exterior del canal grande
                slAnchoredToBigBand = true;
                currentSlPriceGlobal = keltnerBig.Upper[0] - (SlBufferTicks * TickSize);
            }
            else
            {
                // Despegue corto (< 10 ticks): SL anclado a Banda 1.75 azul del canal pequeño (espacio de respiro)
                slAnchoredToBigBand = false;
                currentSlPriceGlobal = keltnerSmall.Upper[0] - (SlBufferTicks * TickSize);
            }

            // Limitar por Stop Catastrófico de Emergencia
            double catastrophicSl = entryPriceGlobal - (MaxCatastrophicStopTicks * TickSize);
            currentSlPriceGlobal = Math.Max(currentSlPriceGlobal, catastrophicSl);

            // Redondeo al tick exacto
            currentSlPriceGlobal = Instrument.MasterInstrument.RoundToTickSize(currentSlPriceGlobal);

            // Disparo de orden
            EnterLong(Contracts, "Dumbo_Long");
            Print(string.Format("{0} [DUMBO LONG] Disparado a {1:F2} | Sep: {2:F1} ticks | SL Anclado en {3} ({4:F2}) | EMA 9: {5:F2} vs {6:F2} | EMA200 Abismo: {7} barras",
                Time[0], entryPriceGlobal, separationTicks, (slAnchoredToBigBand ? "Banda 3.5 Grande" : "Banda 1.75 Azul"), currentSlPriceGlobal, ema9[0], ema9[EmaShortSlopeLookback], barsEmaBelowOppositeCount));
        }

        // =================================================================================================
        // EJECUCIÓN SHORT Y ANCLAJE DE STOP LOSS INICIAL (BANDA ESCALONADA)
        // =================================================================================================
        private void EjecutarEntradaShort()
        {
            entryPriceGlobal = Close[0];
            entryBarGlobal = CurrentBar;
            shortFiredInWave = true; // Bloquea disparos duplicados en esta misma ola

            // Medir distancia de penetración por fuera de la Banda 3.5
            double separationTicks = (keltnerBig.Lower[0] - Close[0]) / TickSize;

            // Determinar Stop Loss Inicial según umbral de penetración (Regla de 10 ticks)
            if (separationTicks >= SeparationThresholdTicks)
            {
                // Despegue fuerte (>= 10 ticks): SL anclado a Banda 3.5 exterior del canal grande
                slAnchoredToBigBand = true;
                currentSlPriceGlobal = keltnerBig.Lower[0] + (SlBufferTicks * TickSize);
            }
            else
            {
                // Despegue corto (< 10 ticks): SL anclado a Banda 1.75 azul del canal pequeño (espacio de respiro)
                slAnchoredToBigBand = false;
                currentSlPriceGlobal = keltnerSmall.Lower[0] + (SlBufferTicks * TickSize);
            }

            // Limitar por Stop Catastrófico de Emergencia
            double catastrophicSl = entryPriceGlobal + (MaxCatastrophicStopTicks * TickSize);
            currentSlPriceGlobal = Math.Min(currentSlPriceGlobal, catastrophicSl);

            // Redondeo al tick exacto
            currentSlPriceGlobal = Instrument.MasterInstrument.RoundToTickSize(currentSlPriceGlobal);

            // Disparo de orden
            EnterShort(Contracts, "Dumbo_Short");
            Print(string.Format("{0} [DUMBO SHORT] Disparado a {1:F2} | Sep: {2:F1} ticks | SL Anclado en {3} ({4:F2}) | EMA 9: {5:F2} vs {6:F2} | EMA200 Abismo: {7} barras",
                Time[0], entryPriceGlobal, separationTicks, (slAnchoredToBigBand ? "Banda 3.5 Grande" : "Banda 1.75 Azul"), currentSlPriceGlobal, ema9[0], ema9[EmaShortSlopeLookback], barsEmaAboveOppositeCount));
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

                // 2. Promoción Dinámica de SL: Si inició en Banda 1.75 Azul y el precio sube a >= 10 ticks afuera, ascender a Banda 3.5 Grande
                if (!slAnchoredToBigBand)
                {
                    double currentSeparationTicks = (Close[0] - keltnerBig.Upper[0]) / TickSize;
                    if (currentSeparationTicks >= SeparationThresholdTicks)
                    {
                        slAnchoredToBigBand = true; // ¡Ascendido a Banda Grande 3.5!
                        Print(string.Format("{0} [DUMBO UPGRADE SL] ¡Precio alcanzó {1:F1} ticks fuera! SL ascendido a Banda 3.5 Grande",
                            Time[0], currentSeparationTicks));
                    }
                }

                // 3. Trailing Dinámico de la Banda Seleccionada (Ratchet hacia arriba)
                // A medida que el canal sube con la tendencia, el SL sube con la banda correspondiente
                double targetBandPrice = slAnchoredToBigBand ? keltnerBig.Upper[0] : keltnerSmall.Upper[0];
                double candidateBandSl = Instrument.MasterInstrument.RoundToTickSize(targetBandPrice - (SlBufferTicks * TickSize));
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

                // 2. Promoción Dinámica de SL: Si inició en Banda 1.75 Azul y el precio cae a >= 10 ticks afuera, descender a Banda 3.5 Grande
                if (!slAnchoredToBigBand)
                {
                    double currentSeparationTicks = (keltnerBig.Lower[0] - Close[0]) / TickSize;
                    if (currentSeparationTicks >= SeparationThresholdTicks)
                    {
                        slAnchoredToBigBand = true; // ¡Ascendido a Banda Grande 3.5!
                        Print(string.Format("{0} [DUMBO UPGRADE SL] ¡Precio alcanzó {1:F1} ticks fuera! SL descendido a Banda 3.5 Grande",
                            Time[0], currentSeparationTicks));
                    }
                }

                // 3. Trailing Dinámico de la Banda Seleccionada (Ratchet hacia abajo)
                // A medida que el canal desciende con la tendencia, el SL baja con la banda correspondiente
                double targetBandPrice = slAnchoredToBigBand ? keltnerBig.Lower[0] : keltnerSmall.Lower[0];
                double candidateBandSl = Instrument.MasterInstrument.RoundToTickSize(targetBandPrice + (SlBufferTicks * TickSize));
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
                Content = isStrategyActive ? "Dumbo ACTIVA (v1.0)" : "Dumbo PAUSADA (v1.0)",
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
                panicButton.Content = isStrategyActive ? "Dumbo ACTIVA (v1.0)" : "Dumbo PAUSADA (v1.0)";
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
        public string Version { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Número de Contratos", Description = "Cantidad total de contratos a operar (todos se gestionan en bloque)", GroupName = "02. Gestión de Posición", Order = 1)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hora Inicio RTH (HHMMSS)", Description = "09:50 AM = 95000", GroupName = "03. Horario Operativo", Order = 1)]
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
        [Range(0, 20)]
        [Display(Name = "Cooldown tras Salida (Barras)", Description = "Barras de espera obligatoria tras cerrar un trade antes de volver a evaluar entradas", GroupName = "04. Parámetros Gatillo Dumbo", Order = 3)]
        public int CooldownBarsAfterExit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exigir Pendiente EMA 9 Micro", Description = "true: Requiere que la EMA 9 apunte a favor del trade midiendo 2 barras atrás partiendo de la 3ra vela afuera", GroupName = "04. Parámetros Gatillo Dumbo", Order = 4)]
        public bool RequireEmaShortSlope { get; set; }

        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name = "Periodo EMA 9 Micro", Description = "Periodo de la media rápida para filtrar el micro-impulso de despegue (por defecto: 9)", GroupName = "04. Parámetros Gatillo Dumbo", Order = 5)]
        public int EmaShortPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Lookback Pendiente EMA 9", Description = "Barras hacia atrás para medir la pendiente de EMA 9 (por defecto: 2 barras)", GroupName = "04. Parámetros Gatillo Dumbo", Order = 6)]
        public int EmaShortSlopeLookback { get; set; }

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
        [Display(Name = "Colchón Respiro SL (Ticks)", Description = "Ticks de margen detrás de la banda para colocar el Stop Loss inicial", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 2)]
        public int SlBufferTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Stop Catastrófico Máximo (Ticks)", Description = "Riesgo máximo absoluto de seguridad por contrato (40 ticks = 10 pts = $500 ES)", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 3)]
        public int MaxCatastrophicStopTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Trailing Choke", Description = "true: Va subiendo/bajando el Stop al extremo de las últimas N barras al alcanzar ganancia mínima", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 4)]
        public bool UseTrailingChoke { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Ganancia Mínima para Choke (Ticks)", Description = "Ticks de ganancia flotante necesarios para activar el Trailing Choke si está habilitado", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 5)]
        public int ChokeActivationProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Barras Lookback Choke", Description = "Cantidad de barras previas para anclar el Trailing Choke (por defecto: 2)", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 6)]
        public int ChokeLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Offset Choke (Ticks)", Description = "Ticks de distancia detrás del extremo de las N barras para el Choke", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 7)]
        public int ChokeOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Salir al Cerrar dentro de Banda 3.5", Description = "true: Si una vela cierra de regreso dentro de la Banda 3.5, liquida inmediatamente", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 8)]
        public bool ExitOnBand35Reentry { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Take Profit Fijo", Description = "true: Cierra con ganancia fija. false: Deja correr la tendencia con la banda", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 9)]
        public bool UseFixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(10, 500)]
        [Display(Name = "Take Profit Fijo (Ticks)", Description = "Ticks de ganancia fija si UseFixedTakeProfit está activo (60 ticks = 15 pts = $750 ES)", GroupName = "06. Gestión de Riesgo (Banda Escalonada)", Order = 10)]
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
