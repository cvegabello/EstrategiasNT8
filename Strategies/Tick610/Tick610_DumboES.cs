#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// =========================================================================================================
// ESTRATEGIA: Tick610_DumboES - "Equipo Dumbo: Relevo en Equipo Monocontrato"
// Desarrollada con Pedagogía y Mentoría de Trading para Gráficas de 610 Ticks en Futuros ES / MES.
// VERSIÓN: 3.0 - "La Campaña de Relevos" (T1: $60 USD | T2 y T3: $20 USD | T4+: $10 USD | SL Escalonado Opción B)
//
// CONCEPTO (ELI5):
//   Dumbo es el elefante volador. En lugar de exponer un solo contrato a un retroceso salvaje de la tendencia,
//   Dumbo corre una carrera de relevos en equipo:
//   1. TICKET 1 (El Primer Envión): Dispara al despegue con SL en Banda Azul 1.75. Cobra $60 USD con orden Limit.
//      - Si falla el primer envión y toca SL: NO entra en cuarentena; sigue acechando mientras EMA200 siga en abismo.
//   2. TICKET 2 y 3 (Relevos Quirúrgicos): Si tras cobrar sigue a >= 30 ticks de la Banda 3.5 y filtros OK,
//      pasa el testigo al siguiente Dumbo con TP Limit de $20 USD y SL Escalonado (Opción B: a Banda 3.5, +12tk o +20tk).
//   3. TICKET 4+ (El Gotero de Tendencia): Mientras conserve >= 25 ticks fuera, sigue mordiendo $10 USD por relevo.
//   4. CUARENTENA DE SACIEDAD (10 min): Si cualquier ticket secundario toca su SL o el espacio se acaba,
//      la campaña termina, congelando entradas por 10 minutos con todas las ganancias blindadas en el bolsillo.
// =========================================================================================================

namespace NinjaTrader.NinjaScript.Strategies
{
    public class Tick610_DumboES : Strategy
    {
        #region Constantes Centralizadas
        private const string STRATEGY_VERSION = "3.01";
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

        // Control de la Campaña de Relevos (Equipo Dumbo v3.0)
        private int            currentTicketNumber      = 0;     // 0 = Esperando T1, 1 = T1 activo, 2 = T2 activo, etc.
        private int            lastCompletedTicket      = 0;     // Último ticket que cobró su TP con éxito
        private bool           waitingForNextRelay      = false; // Bandera: esperando siguiente barra tras TP para pasar testigo
        private int            relayCheckBar            = -1;    // Barra donde se cobró el TP
        private MarketPosition relayDirection           = MarketPosition.Flat; // Dirección de la campaña (Long o Short)
        private int            lastExitBar              = -999;  // Barra de última salida
        private int            lastReportedTradeCount   = 0;     // Para rastreo en SystemPerformance
        private DateTime       dumboQuarantineEndTime   = DateTime.MinValue; // Cuarentena de saciedad (10 min)

        // Niveles de Precio del Trade Activo
        private double entryPriceGlobal      = 0.0;
        private double currentSlPriceGlobal  = 0.0;
        private double currentTpPriceGlobal  = 0.0;
        private int    entryBarGlobal        = -1;
        private int    stagedBufferUsedTicks = 0;     // Opción B: 0 = Banda 3.5 (-1tk), 12 = +12tk, 20 = +20tk

        // Control de Interfaz WPF (Botón de Pánico)
        private Chart  chartWindow           = null;
        private System.Windows.Controls.Button panicButton = null;
        private bool   isStrategyActive      = false; // Inicia en pausa en tiempo real
        private bool   isSubscribedToKeys    = false; // Bandera de suscripción a eventos de teclado

        // Adaptabilidad Mini ES vs Micro MES
        private bool   isMicroInstrument     = false;
        #endregion

        #region Configuración de Estado (OnStateChange)
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = "Tick610_DumboES v3.0: Campaña de Relevos Monocontrato en Super-Expansión fuera de Banda 3.5 con EMA 200 en abismo opuesto.";
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
                Version                                     = string.Format("v{0} (Equipo Dumbo Relevos: T1 $60 | T2/T3 $20 | T4+ $10)", STRATEGY_VERSION);

                // 02. Gestión de Posición
                Contracts                                   = 1;      // Número de contratos a operar (estricto monocontrato)

                // 03. Horario Operativo RTH Estricto
                StartTime                                   = 93500;  // 09:35 AM EST (inicia 5 min tras apertura)
                StopEntriesTime                             = 154500; // 03:45 PM EST (corta nuevas entradas)
                ForceCloseTime                              = 160000; // 04:00 PM EST (cierra todo antes de la campana)

                // 04. Parámetros del Disparador Dumbo (Ticket 1)
                FloatingBarsRequired                        = 3;      // Mínimo de velas consecutivas despegadas fuera de Banda 3.5
                StrictFloatingLowHigh                       = true;   // true: Low > Upper35 (Long) / High < Lower35 (Short). false: Close fuera.
                MinSeparationFromBand35Ticks                = 12;     // Regla Estructural: Exige estar al menos 12 ticks por fuera para el Ticket 1
                CooldownBarsAfterExit                       = 3;      // Barras de enfriamiento tras salir de un trade
                RequireTemaSlope                            = true;   // true: Exige pendiente estricta de TEMA 9 a favor
                TemaPeriod                                  = 9;      // Periodo de la media rápida TEMA (idéntica a CascadaVega)
                TemaSlopeLookback                           = 2;      // Barras hacia atrás para medir la pendiente de TEMA 9

                // 05. Filtro Macro EMA 200 en el Abismo
                MinBarsEmaOpposite                          = 40;     // Mínimo de barras continuas con la EMA 200 fuera del canal opuesto (40 barras v3.0)
                EmaOppositeBandTarget                       = BandaOpuestaEnum.BandaGrande35; // Umbral: fuera del Canal Grande 3.5 opuesto
                EmaSlopeLookback                            = 3;      // Barras para medir la pendiente de la EMA 200
                RequireEmaSlope                             = true;   // Exigir que la pendiente de EMA 200 esté a favor del trade

                // 06. Campaña de Relevos: Metas de Take Profit ($ USD)
                Ticket1TpDollars                            = 60.0;   // TP Limit Ticket 1: $60 MES / $600 ES (48 ticks = 12 pts)
                Ticket23TpDollars                           = 20.0;   // TP Limit Tickets 2 y 3: $20 MES / $200 ES (16 ticks = 4 pts)
                Ticket4PlusTpDollars                        = 10.0;   // TP Limit Tickets 4+: $10 MES / $100 ES (8 ticks = 2 pts)

                // 07. Campaña de Relevos: Distancias Mínimas y Relevos
                RelayMinDistanceTicks                       = 30;     // Ticks mínimos fuera de Banda 3.5 para habilitar Tickets 2 y 3
                RelayLateMinDistanceTicks                   = 25;     // Ticks mínimos fuera de Banda 3.5 para habilitar Tickets 4 en adelante

                // 08. Opción B: Salto Escalonado de Stop Loss en Relevos (Sin Asfixia)
                StagedStage1DistanceTicks                   = 35;     // Umbral de distancia para Stage 1 (35 a 39 ticks)
                StagedStage1SlAdvanceTicks                  = 12;     // Avance del SL por delante de Banda 3.5 en Stage 1 (12 ticks)
                StagedStage2DistanceTicks                   = 40;     // Umbral de distancia para Stage 2 (≥ 40 ticks)
                StagedStage2SlAdvanceTicks                  = 20;     // Avance del SL por delante de Banda 3.5 en Stage 2 (20 ticks)
                SlBufferTicks                               = 2;      // Colchón de respiro detrás de la Banda Azul 1.75 en Ticket 1 (2 ticks)

                // 09. Gestión de Seguridad y Cuarentena
                QuarantineMinutes                           = 10;     // Minutos de congelamiento tras finalizar campaña o SL secundario (10 min)
                MaxCatastrophicStopTicks                    = 40;     // Stop de emergencia máximo absoluto (40 ticks = 10 pts = $50 MES / $500 ES)

                // 10. Canales e Indicadores Técnicos
                KeltnerBigMult                              = 3.5;
                KeltnerSmallMult                            = 1.75;
                KeltnerPeriod                               = 52;
                EmaPeriod                                   = 200;
            }
            else if (State == State.DataLoaded)
            {
                // Detección automática de instrumento Mini ES vs Micro MES
                isMicroInstrument = Instrument.MasterInstrument.Name.Contains("MES");

                // Reseteo de estados de campaña y cuarentena
                currentTicketNumber     = 0;
                lastCompletedTicket     = 0;
                waitingForNextRelay     = false;
                relayCheckBar           = -1;
                dumboQuarantineEndTime  = DateTime.MinValue;
                lastReportedTradeCount  = (SystemPerformance != null && SystemPerformance.AllTrades != null) ? SystemPerformance.AllTrades.Count : 0;

                // Instanciación de Indicadores Técnicos en memoria
                keltnerBig   = KeltnerChannel(Close, KeltnerBigMult, KeltnerPeriod);
                keltnerSmall = KeltnerChannel(Close, KeltnerSmallMult, KeltnerPeriod);
                ema200       = EMA(Close, EmaPeriod);
                tema9        = TEMA(Close, TemaPeriod);

                // En modo Backtest o Strategy Analyzer, se activa automáticamente
                if (ChartControl == null)
                {
                    isStrategyActive = true;
                    Print("[EQUIPO DUMBO v3.0] Modo Headless (Strategy Analyzer / Backtest) detectado. Estrategia auto-activada.");
                }
            }
            else if (State == State.Historical)
            {
                lastReportedTradeCount = (SystemPerformance != null && SystemPerformance.AllTrades != null) ? SystemPerformance.AllTrades.Count : 0;
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
            // Protección de datos suficientes
            if (CurrentBar < Math.Max(BarsRequiredToTrade, Math.Max(EmaPeriod, MinBarsEmaOpposite)))
                return;

            // 1. ACTUALIZAR CONTADORES DE MERCADO
            UpdateMarketCounters();

            // 2. FILTRO DE HORARIO RTH ESTRICTO
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

            // 3. RASTREO Y EVALUACIÓN DE TRADES CERRADOS
            VerificarCierreDeTrades();

            // 4. GESTIÓN DE POSICIÓN ABIERTA (TRAILING DINÁMICO DEL SL)
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                GestionarPosicionAbierta();
                return; // Mientras esté en posición, no evalúa nuevas entradas
            }

            // 5. VERIFICACIÓN DE BOTÓN DE PÁNICO / PAUSA
            if (!isStrategyActive)
                return;

            // 6. FILTRO DE VENTANA OPERATIVA
            if (currentTime < StartTime || currentTime >= StopEntriesTime)
                return;

            // 7. FILTRO DE CUARENTENA DE SACIEDAD TRAS CAMPAÑA (10 MINUTOS)
            if (Time[0] < dumboQuarantineEndTime)
            {
                return;
            }

            // 8. EVALUACIÓN DE RELEVO EN EQUIPO (SI TICKET PREVIO COBRÓ TP)
            if (waitingForNextRelay)
            {
                if (CurrentBar > relayCheckBar)
                {
                    EvaluarSiguienteRelevo();
                }
                return;
            }

            // 9. COOLDOWN TRAS ÚLTIMA SALIDA PARA TICKET 1
            if (CurrentBar - lastExitBar < CooldownBarsAfterExit)
                return;

            // 10. EVALUAR SEÑAL DE ENTRADA INICIAL: TICKET 1
            EvaluarEntradasTicket1();
        }
        #endregion

        #region Máquina de Estados: Relevos en Equipo (v3.0)

        // =================================================================================================
        // VERIFICAR CIERRE DE TRADES: Detecta si el trade cerró en TP Limit o en SL
        // =================================================================================================
        private void VerificarCierreDeTrades()
        {
            if (Position.MarketPosition == MarketPosition.Flat && SystemPerformance != null && SystemPerformance.AllTrades != null && SystemPerformance.AllTrades.Count > lastReportedTradeCount)
            {
                lastReportedTradeCount = SystemPerformance.AllTrades.Count;
                Trade lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                string exitName = (lastTrade.Exit != null) ? lastTrade.Exit.Name : "";
                double pnl = lastTrade.ProfitCurrency;

                // Identificar si la salida fue por Take Profit o Stop Loss
                bool isTp = exitName.IndexOf("TP", StringComparison.OrdinalIgnoreCase) >= 0 || pnl > 0;

                if (isTp)
                {
                    lastCompletedTicket = currentTicketNumber;
                    waitingForNextRelay = true;
                    relayCheckBar = CurrentBar;
                    relayDirection = (lastTrade.Entry != null) ? lastTrade.Entry.MarketPosition : MarketPosition.Flat;
                    lastExitBar = CurrentBar;

                    int nextReqDist = (currentTicketNumber >= 3) ? RelayLateMinDistanceTicks : RelayMinDistanceTicks;
                    Print(string.Format("*************************************************************************************************************\n" +
                                        "{0} - [EQUIPO DUMBO v{1} | ¡PUM, COBRADO TICKET {2}!] Ganancia: +{3:C2} USD.\n" +
                                        ">>> EL TESTIGO ESTÁ LISTO: Esperando apertura de siguiente barra para evaluar distancia (≥ {4} ticks) y pasar el relevo.\n" +
                                        "*************************************************************************************************************",
                        Time[0], STRATEGY_VERSION, currentTicketNumber, pnl, nextReqDist));
                }
                else
                {
                    // Salida por Stop Loss
                    lastExitBar = CurrentBar;
                    waitingForNextRelay = false;

                    if (currentTicketNumber <= 1)
                    {
                        // FALLO DE PRIMER ENVIÓN: NO entra en cuarentena, sigue acechando Ticket 1
                        Print(string.Format("*************************************************************************************************************\n" +
                                            "{0} - [EQUIPO DUMBO v{1} | FALLO PRIMER ENVIÓN (TICKET 1)] Salida por Stop Loss ({2:C2} USD).\n" +
                                            ">>> NO ENTRA EN CUARENTENA: Esperando {3} barras de enfriamiento para volver a acechar mientras EMA 200 siga en abismo.\n" +
                                            "*************************************************************************************************************",
                            Time[0], STRATEGY_VERSION, pnl, CooldownBarsAfterExit));
                        lastCompletedTicket = 0;
                        currentTicketNumber = 0;
                    }
                    else
                    {
                        // TICKET SECUNDARIO (T2, T3, T4+) TOCÓ SL: ¡Apague y vámonos! Cuarentena de 10 min
                        dumboQuarantineEndTime = Time[0].AddMinutes(QuarantineMinutes);
                        Print(string.Format("*************************************************************************************************************\n" +
                                            "{0} - [EQUIPO DUMBO v{1} | ¡APAGUE Y VÁMONOS! SL ALCANZADO EN RELEVO T{2}] Salida: {3:C2} USD.\n" +
                                            ">>> CUARENTENA DE SACIEDAD ACTIVADA: Congelado por {4} minutos (hasta las {5:HH:mm:ss} EST). Ganancias de la campaña protegidas en el bolsillo.\n" +
                                            "*************************************************************************************************************",
                            Time[0], STRATEGY_VERSION, currentTicketNumber, pnl, QuarantineMinutes, dumboQuarantineEndTime));
                        lastCompletedTicket = 0;
                        currentTicketNumber = 0;
                    }
                }
            }
        }

        // =================================================================================================
        // EVALUAR SIGUIENTE RELEVO: En la barra posterior al TP, evalúa Distancia Pura en la estratósfera
        // =================================================================================================
        private void EvaluarSiguienteRelevo()
        {
            int nextTicket = lastCompletedTicket + 1;
            int reqDistance = (lastCompletedTicket >= 3) ? RelayLateMinDistanceTicks : RelayMinDistanceTicks;

            double currentDist = 0.0;
            bool distanceOk = false;
            bool emaOk = false;

            if (relayDirection == MarketPosition.Long)
            {
                currentDist = (Close[0] - keltnerBig.Upper[0]) / TickSize;
                distanceOk  = currentDist >= reqDistance;
                emaOk       = !RequireEmaSlope  || (ema200[0] >= ema200[EmaSlopeLookback]);
            }
            else if (relayDirection == MarketPosition.Short)
            {
                currentDist = (keltnerBig.Lower[0] - Close[0]) / TickSize;
                distanceOk  = currentDist >= reqDistance;
                emaOk       = !RequireEmaSlope  || (ema200[0] <= ema200[EmaSlopeLookback]);
            }

            // RELEVO POR DISTANCIA PURA (v3.01): Ya estamos en la estratósfera (≥ 30tk o ≥ 25tk) y el SL Escalonado
            // nos cuida la espalda. No le exigimos pendientes micro a TEMA 9 para no ahogar los pases de relevo.
            if (distanceOk && emaOk)
            {
                // ¡El testigo pasa de mano en mano! Dispara el siguiente Dumbo
                EjecutarRelevo(nextTicket, relayDirection, currentDist);
                waitingForNextRelay = false;
            }
            else
            {
                // La ola se calmó o no hay suficiente espacio exterior: Fin de la campaña
                dumboQuarantineEndTime = Time[0].AddMinutes(QuarantineMinutes);
                waitingForNextRelay = false;
                Print(string.Format("*************************************************************************************************************\n" +
                                    "{0} - [EQUIPO DUMBO v{1} | FIN DE CAMPAÑA POR RELEVOS - T{2} NO DISPARA]\n" +
                                    ">>> MOTIVO: Espacio exterior insuficiente ({3:F1}tk < {4}tk requeridos). La ola perdió inercia estratosférica.\n" +
                                    ">>> CUARENTENA DE SACIEDAD ACTIVADA: Congelado por {5} minutos (hasta las {6:HH:mm:ss} EST). Cero sobreoperar.\n" +
                                    "*************************************************************************************************************",
                    Time[0], STRATEGY_VERSION, nextTicket, currentDist, reqDistance, QuarantineMinutes, dumboQuarantineEndTime));
                lastCompletedTicket = 0;
                currentTicketNumber = 0;
            }
        }

        // =================================================================================================
        // EJECUTAR RELEVO QUIRÚRGICO (TICKETS 2, 3, 4+) CON SL ESCALONADO OPCIÓN B
        // =================================================================================================
        private void EjecutarRelevo(int ticketNumber, MarketPosition direction, double currentDist)
        {
            currentTicketNumber = ticketNumber;
            entryPriceGlobal = Close[0];
            entryBarGlobal = CurrentBar;

            // Determinar meta de Take Profit según el ticket
            double targetDollars = (ticketNumber <= 3) ? Ticket23TpDollars : Ticket4PlusTpDollars;
            double effectiveTargetDollars = GetEffectiveDollars(targetDollars);
            double tpTicks = Math.Round(effectiveTargetDollars / (Contracts * Instrument.MasterInstrument.PointValue * TickSize));

            // DETERMINAR STOP LOSS ESCALONADO (OPCIÓN B - SIN ASFIXIA)
            string stagedDetail = "";
            if (currentDist >= StagedStage2DistanceTicks)
            {
                stagedBufferUsedTicks = StagedStage2SlAdvanceTicks; // +20 ticks adelante de Banda 3.5
                stagedDetail = string.Format("+{0}tk adelante de Banda 3.5 (Estratósfera ≥ {1}tk)", stagedBufferUsedTicks, StagedStage2DistanceTicks);
            }
            else if (currentDist >= StagedStage1DistanceTicks)
            {
                stagedBufferUsedTicks = StagedStage1SlAdvanceTicks; // +12 ticks adelante de Banda 3.5
                stagedDetail = string.Format("+{0}tk adelante de Banda 3.5 (Super-Estirado ≥ {1}tk)", stagedBufferUsedTicks, StagedStage1DistanceTicks);
            }
            else
            {
                stagedBufferUsedTicks = 0; // En Banda 3.5 con colchón de 1 tick
                stagedDetail = "En Banda 3.5 (-1tk colchón)";
            }

            if (direction == MarketPosition.Long)
            {
                currentTpPriceGlobal = Instrument.MasterInstrument.RoundToTickSize(entryPriceGlobal + (tpTicks * TickSize));
                currentSlPriceGlobal = (stagedBufferUsedTicks > 0)
                    ? (keltnerBig.Upper[0] + (stagedBufferUsedTicks * TickSize))
                    : (keltnerBig.Upper[0] - (1 * TickSize));
                currentSlPriceGlobal = Instrument.MasterInstrument.RoundToTickSize(currentSlPriceGlobal);

                // Stop Catastrófico de Seguridad
                currentSlPriceGlobal = Math.Max(currentSlPriceGlobal, entryPriceGlobal - (MaxCatastrophicStopTicks * TickSize));

                string entryName = string.Format("Dumbo_Long_T{0}", ticketNumber);
                string exitName  = string.Format("Exit_Dumbo_TP_T{0}", ticketNumber);

                EnterLong(Contracts, entryName);
                ExitLongLimit(Contracts, currentTpPriceGlobal, exitName, entryName);

                Print(string.Format("*************************************************************************************************************\n" +
                                    "{0} - [EQUIPO DUMBO v{1} | ¡PASÓ EL TESTIGO! RELEVO ALCISTA T{2} DISPARADO]\n" +
                                    ">>> DETALLE: Entrada: {3:F2} | TP Limit: {4:F2} (+${5:F0} USD / {6}tk) | SL Escalonado: {7:F2} [{8}] | Distancia: +{9:F1}tk\n" +
                                    "*************************************************************************************************************",
                    Time[0], STRATEGY_VERSION, ticketNumber, entryPriceGlobal, currentTpPriceGlobal, effectiveTargetDollars, tpTicks, currentSlPriceGlobal, stagedDetail, currentDist));
            }
            else if (direction == MarketPosition.Short)
            {
                currentTpPriceGlobal = Instrument.MasterInstrument.RoundToTickSize(entryPriceGlobal - (tpTicks * TickSize));
                currentSlPriceGlobal = (stagedBufferUsedTicks > 0)
                    ? (keltnerBig.Lower[0] - (stagedBufferUsedTicks * TickSize))
                    : (keltnerBig.Lower[0] + (1 * TickSize));
                currentSlPriceGlobal = Instrument.MasterInstrument.RoundToTickSize(currentSlPriceGlobal);

                // Stop Catastrófico de Seguridad
                currentSlPriceGlobal = Math.Min(currentSlPriceGlobal, entryPriceGlobal + (MaxCatastrophicStopTicks * TickSize));

                string entryName = string.Format("Dumbo_Short_T{0}", ticketNumber);
                string exitName  = string.Format("Exit_Dumbo_TP_T{0}", ticketNumber);

                EnterShort(Contracts, entryName);
                ExitShortLimit(Contracts, currentTpPriceGlobal, exitName, entryName);

                Print(string.Format("*************************************************************************************************************\n" +
                                    "{0} - [EQUIPO DUMBO v{1} | ¡PASÓ EL TESTIGO! RELEVO BAJISTA T{2} DISPARADO]\n" +
                                    ">>> DETALLE: Entrada: {3:F2} | TP Limit: {4:F2} (+${5:F0} USD / {6}tk) | SL Escalonado: {7:F2} [{8}] | Distancia: +{9:F1}tk\n" +
                                    "*************************************************************************************************************",
                    Time[0], STRATEGY_VERSION, ticketNumber, entryPriceGlobal, currentTpPriceGlobal, effectiveTargetDollars, tpTicks, currentSlPriceGlobal, stagedDetail, currentDist));
            }
        }

        // =================================================================================================
        // EVALUACIÓN DE ENTRADAS PARA TICKET 1: Dispara el despegue inicial
        // =================================================================================================
        private void EvaluarEntradasTicket1()
        {
            // === 1. TICKET 1 LONG (DESPEGUE ALCISTA) ===
            if (floatingBarsLongCount >= FloatingBarsRequired)
            {
                double separationTicks = (Close[0] - keltnerBig.Upper[0]) / TickSize;
                double temaSlope = (tema9[0] - tema9[TemaSlopeLookback]) / TemaSlopeLookback;
                double emaSlope = (ema200[0] - ema200[EmaSlopeLookback]);

                if (separationTicks >= MinSeparationFromBand35Ticks
                    && barsEmaBelowOppositeCount >= MinBarsEmaOpposite
                    && (!RequireEmaSlope || (ema200[0] >= ema200[EmaSlopeLookback]))
                    && (!RequireTemaSlope || (tema9[0] > tema9[1] && tema9[0] > tema9[TemaSlopeLookback])))
                {
                    EjecutarTicket1Long(separationTicks, temaSlope, emaSlope);
                    return;
                }
            }

            // === 2. TICKET 1 SHORT (DESPEGUE BAJISTA) ===
            if (floatingBarsShortCount >= FloatingBarsRequired)
            {
                double separationTicks = (keltnerBig.Lower[0] - Close[0]) / TickSize;
                double temaSlope = (tema9[TemaSlopeLookback] - tema9[0]) / TemaSlopeLookback;
                double emaSlope = (ema200[EmaSlopeLookback] - ema200[0]);

                if (separationTicks >= MinSeparationFromBand35Ticks
                    && barsEmaAboveOppositeCount >= MinBarsEmaOpposite
                    && (!RequireEmaSlope || (ema200[0] <= ema200[EmaSlopeLookback]))
                    && (!RequireTemaSlope || (tema9[0] < tema9[1] && tema9[0] < tema9[TemaSlopeLookback])))
                {
                    EjecutarTicket1Short(separationTicks, temaSlope, emaSlope);
                    return;
                }
            }
        }

        // =================================================================================================
        // EJECUCIÓN TICKET 1 LONG
        // =================================================================================================
        private void EjecutarTicket1Long(double separationTicks, double temaSlope, double emaSlope)
        {
            currentTicketNumber = 1;
            entryPriceGlobal = Close[0];
            entryBarGlobal = CurrentBar;
            stagedBufferUsedTicks = 0;

            double effectiveTpDollars = GetEffectiveDollars(Ticket1TpDollars);
            double tpTicks = Math.Round(effectiveTpDollars / (Contracts * Instrument.MasterInstrument.PointValue * TickSize));
            currentTpPriceGlobal = Instrument.MasterInstrument.RoundToTickSize(entryPriceGlobal + (tpTicks * TickSize));

            // SL Inicial en Banda Azul 1.75 (Holgura para despegue)
            currentSlPriceGlobal = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Upper[0] - (SlBufferTicks * TickSize));
            currentSlPriceGlobal = Math.Max(currentSlPriceGlobal, entryPriceGlobal - (MaxCatastrophicStopTicks * TickSize));

            EnterLong(Contracts, "Dumbo_Long_T1");
            ExitLongLimit(Contracts, currentTpPriceGlobal, "Exit_Dumbo_TP_T1", "Dumbo_Long_T1");

            Print(string.Format("*************************************************************************************************************\n" +
                                "{0} - [EQUIPO DUMBO v{1} | ¡DESPEGUE TICKET 1 ALCISTA DISPARADO!] Separación: +{2:F1}tk (mín {3}tk).\n" +
                                ">>> DETALLE: Entrada: {4:F2} | TP Limit: {5:F2} (+${6:F0} USD / {7}tk) | SL Inicial: {8:F2} (Banda Azul 1.75 - Colchón {9}tk) | EMA200 Abismo: {10}b\n" +
                                "*************************************************************************************************************",
                Time[0], STRATEGY_VERSION, separationTicks, MinSeparationFromBand35Ticks, entryPriceGlobal, currentTpPriceGlobal, effectiveTpDollars, tpTicks, currentSlPriceGlobal, SlBufferTicks, barsEmaBelowOppositeCount));
        }

        // =================================================================================================
        // EJECUCIÓN TICKET 1 SHORT
        // =================================================================================================
        private void EjecutarTicket1Short(double separationTicks, double temaSlope, double emaSlope)
        {
            currentTicketNumber = 1;
            entryPriceGlobal = Close[0];
            entryBarGlobal = CurrentBar;
            stagedBufferUsedTicks = 0;

            double effectiveTpDollars = GetEffectiveDollars(Ticket1TpDollars);
            double tpTicks = Math.Round(effectiveTpDollars / (Contracts * Instrument.MasterInstrument.PointValue * TickSize));
            currentTpPriceGlobal = Instrument.MasterInstrument.RoundToTickSize(entryPriceGlobal - (tpTicks * TickSize));

            // SL Inicial en Banda Azul 1.75 (Holgura para despegue)
            currentSlPriceGlobal = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Lower[0] + (SlBufferTicks * TickSize));
            currentSlPriceGlobal = Math.Min(currentSlPriceGlobal, entryPriceGlobal + (MaxCatastrophicStopTicks * TickSize));

            EnterShort(Contracts, "Dumbo_Short_T1");
            ExitShortLimit(Contracts, currentTpPriceGlobal, "Exit_Dumbo_TP_T1", "Dumbo_Short_T1");

            Print(string.Format("*************************************************************************************************************\n" +
                                "{0} - [EQUIPO DUMBO v{1} | ¡DESPEGUE TICKET 1 BAJISTA DISPARADO!] Separación: +{2:F1}tk (mín {3}tk).\n" +
                                ">>> DETALLE: Entrada: {4:F2} | TP Limit: {5:F2} (+${6:F0} USD / {7}tk) | SL Inicial: {8:F2} (Banda Azul 1.75 - Colchón {9}tk) | EMA200 Abismo: {10}b\n" +
                                "*************************************************************************************************************",
                Time[0], STRATEGY_VERSION, separationTicks, MinSeparationFromBand35Ticks, entryPriceGlobal, currentTpPriceGlobal, effectiveTpDollars, tpTicks, currentSlPriceGlobal, SlBufferTicks, barsEmaAboveOppositeCount));
        }

        // =================================================================================================
        // GESTIÓN DE POSICIÓN ABIERTA (TRAILING DINÁMICO DE STOP LOSS)
        // =================================================================================================
        private void GestionarPosicionAbierta()
        {
            if (CurrentBar <= entryBarGlobal)
                return;

            string entryName = (Position.MarketPosition == MarketPosition.Long)
                ? string.Format("Dumbo_Long_T{0}", currentTicketNumber)
                : string.Format("Dumbo_Short_T{0}", currentTicketNumber);

            string slExitName = string.Format("Exit_Dumbo_SL_T{0}", currentTicketNumber);
            string tpExitName = string.Format("Exit_Dumbo_TP_T{0}", currentTicketNumber);

            if (Position.MarketPosition == MarketPosition.Long)
            {
                // Re-asegurar orden Limit en el libro
                ExitLongLimit(Contracts, currentTpPriceGlobal, tpExitName, entryName);

                // Trailing dinámico del SL
                double candidateSl = 0.0;
                if (currentTicketNumber == 1)
                {
                    // Ticket 1: acompaña con Banda Azul 1.75
                    candidateSl = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Upper[0] - (SlBufferTicks * TickSize));
                }
                else
                {
                    // Tickets 2+: acompaña con Banda 3.5 + buffer escalonado Opción B
                    candidateSl = (stagedBufferUsedTicks > 0)
                        ? (keltnerBig.Upper[0] + (stagedBufferUsedTicks * TickSize))
                        : (keltnerBig.Upper[0] - (1 * TickSize));
                    candidateSl = Instrument.MasterInstrument.RoundToTickSize(candidateSl);
                }

                if (candidateSl > currentSlPriceGlobal)
                {
                    currentSlPriceGlobal = candidateSl;
                }

                // Salida por SL
                if (Close[0] <= currentSlPriceGlobal || Low[0] <= currentSlPriceGlobal)
                {
                    ExitLong(slExitName, entryName);
                    Print(string.Format("{0} - [EQUIPO DUMBO v{1} | SL ALCANZADO] Long T{2} cerrado a {3:F2} (Nivel SL: {4:F2})",
                        Time[0], STRATEGY_VERSION, currentTicketNumber, Close[0], currentSlPriceGlobal));
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                // Re-asegurar orden Limit en el libro
                ExitShortLimit(Contracts, currentTpPriceGlobal, tpExitName, entryName);

                // Trailing dinámico del SL
                double candidateSl = 0.0;
                if (currentTicketNumber == 1)
                {
                    // Ticket 1: acompaña con Banda Azul 1.75
                    candidateSl = Instrument.MasterInstrument.RoundToTickSize(keltnerSmall.Lower[0] + (SlBufferTicks * TickSize));
                }
                else
                {
                    // Tickets 2+: acompaña con Banda 3.5 + buffer escalonado Opción B
                    candidateSl = (stagedBufferUsedTicks > 0)
                        ? (keltnerBig.Lower[0] - (stagedBufferUsedTicks * TickSize))
                        : (keltnerBig.Lower[0] + (1 * TickSize));
                    candidateSl = Instrument.MasterInstrument.RoundToTickSize(candidateSl);
                }

                if (candidateSl < currentSlPriceGlobal)
                {
                    currentSlPriceGlobal = candidateSl;
                }

                // Salida por SL
                if (Close[0] >= currentSlPriceGlobal || High[0] >= currentSlPriceGlobal)
                {
                    ExitShort(slExitName, entryName);
                    Print(string.Format("{0} - [EQUIPO DUMBO v{1} | SL ALCANZADO] Short T{2} cerrado a {3:F2} (Nivel SL: {4:F2})",
                        Time[0], STRATEGY_VERSION, currentTicketNumber, Close[0], currentSlPriceGlobal));
                }
            }
        }
        #endregion

        #region Métodos Auxiliares y Normalización

        // =================================================================================================
        // ACTUALIZACIÓN DE CONTADORES: Conteo de EMA 200 en el abismo y velas flotando
        // =================================================================================================
        private void UpdateMarketCounters()
        {
            double emaVal = ema200[0];
            double upperOpposite = (EmaOppositeBandTarget == BandaOpuestaEnum.BandaGrande35) ? keltnerBig.Upper[0] : keltnerSmall.Upper[0];
            double lowerOpposite = (EmaOppositeBandTarget == BandaOpuestaEnum.BandaGrande35) ? keltnerBig.Lower[0] : keltnerSmall.Lower[0];

            if (emaVal < lowerOpposite)
                barsEmaBelowOppositeCount++;
            else
                barsEmaBelowOppositeCount = 0;

            if (emaVal > upperOpposite)
                barsEmaAboveOppositeCount++;
            else
                barsEmaAboveOppositeCount = 0;

            bool isFloatingLong  = StrictFloatingLowHigh ? (Low[0] > keltnerBig.Upper[0]) : (Close[0] > keltnerBig.Upper[0]);
            bool isFloatingShort = StrictFloatingLowHigh ? (High[0] < keltnerBig.Lower[0]) : (Close[0] < keltnerBig.Lower[0]);

            if (isFloatingLong)
                floatingBarsLongCount++;
            else
                floatingBarsLongCount = 0;

            if (isFloatingShort)
                floatingBarsShortCount++;
            else
                floatingBarsShortCount = 0;
        }

        // =================================================================================================
        // NORMALIZACIÓN INTELIGENTE DE DÓLARES (MES vs ES)
        // =================================================================================================
        private double GetEffectiveDollars(double inputDollars)
        {
            if (isMicroInstrument)
                return inputDollars >= 100.0 ? (inputDollars / 10.0) : inputDollars;
            return inputDollars < 100.0 ? (inputDollars * 10.0) : inputDollars;
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
                Content = isStrategyActive ? string.Format("Dumbo ACTIVA (v{0})", STRATEGY_VERSION) : string.Format("Dumbo PAUSADA (v{0})", STRATEGY_VERSION),
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
                panicButton.Content = isStrategyActive ? string.Format("Dumbo ACTIVA (v{0})", STRATEGY_VERSION) : string.Format("Dumbo PAUSADA (v{0})", STRATEGY_VERSION);
                panicButton.Background = isStrategyActive ? Brushes.DarkGreen : Brushes.Crimson;
            }

            if (!isStrategyActive)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    ExitLong("Boton_Panico_WPF", "");
                    ExitShort("Boton_Panico_WPF", "");
                    Print(Time[0] + " - [BOTÓN DE PÁNICO DUMBO v3.0] ¡Posiciones liquidadas a mercado!");
                }
                waitingForNextRelay = false;
                currentTicketNumber = 0;
                lastCompletedTicket = 0;
            }

            Print(string.Format("{0} - [ESTADO DUMBO v{1}] Estrategia {2}.",
                Time[0], STRATEGY_VERSION, (isStrategyActive ? "ACTIVADA" : "PAUSADA (Pánico)")));
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
            get { return string.Format("{0} (MES / ES) - Equipo Dumbo Relevos", STRATEGY_VERSION); }
            set { }
        }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Número de Contratos", Description = "Cantidad de contratos a operar (estricto monocontrato = 1)", GroupName = "02. Gestión de Posición", Order = 1)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hora Inicio RTH (HHMMSS)", Description = "09:35 AM = 93500", GroupName = "03. Horario Operativo", Order = 1)]
        public int StartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hora Fin Entradas (HHMMSS)", Description = "03:45 PM = 154500", GroupName = "03. Horario Operativo", Order = 2)]
        public int StopEntriesTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hora Cierre Forzoso (HHMMSS)", Description = "04:00 PM = 160000", GroupName = "03. Horario Operativo", Order = 3)]
        public int ForceCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Velas Flotando Requeridas (T1)", Description = "Mínimo de velas consecutivas despegadas fuera de Banda 3.5 para Ticket 1 (por defecto: 3)", GroupName = "04. Parámetros Gatillo Dumbo (T1)", Order = 1)]
        public int FloatingBarsRequired { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Separación Estricta (Low/High)", Description = "true: Toda la vela flota afuera. false: Solo el Close fuera.", GroupName = "04. Parámetros Gatillo Dumbo (T1)", Order = 2)]
        public bool StrictFloatingLowHigh { get; set; }

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Separación Mínima Banda 3.5 (T1)", Description = "Ticks mínimos por fuera de Banda 3.5 para Ticket 1 (por defecto: 12 ticks)", GroupName = "04. Parámetros Gatillo Dumbo (T1)", Order = 3)]
        public int MinSeparationFromBand35Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Cooldown tras Salida (Barras)", Description = "Barras de espera tras SL de Ticket 1 antes de reintentar (por defecto: 3)", GroupName = "04. Parámetros Gatillo Dumbo (T1)", Order = 4)]
        public int CooldownBarsAfterExit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exigir Pendiente TEMA 9 Micro", Description = "true: Requiere que la TEMA 9 apunte a favor del trade de forma estricta", GroupName = "04. Parámetros Gatillo Dumbo (T1)", Order = 5)]
        public bool RequireTemaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name = "Periodo TEMA 9 Micro", Description = "Periodo de la media rápida TEMA (por defecto: 9)", GroupName = "04. Parámetros Gatillo Dumbo (T1)", Order = 6)]
        public int TemaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Lookback Pendiente TEMA 9", Description = "Barras hacia atrás para medir la pendiente de TEMA 9 (por defecto: 2 barras)", GroupName = "04. Parámetros Gatillo Dumbo (T1)", Order = 7)]
        public int TemaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Range(10, 500)]
        [Display(Name = "Barras Mínimas EMA en Abismo", Description = "Barras continuas que la EMA 200 debe llevar fuera de banda opuesta (por defecto: 40)", GroupName = "05. Filtro Macro EMA 200", Order = 1)]
        public int MinBarsEmaOpposite { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Banda Opuesta de Referencia", Description = "Banda contra la cual se mide si la EMA 200 está en el abismo", GroupName = "05. Filtro Macro EMA 200", Order = 2)]
        public BandaOpuestaEnum EmaOppositeBandTarget { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Lookback Pendiente EMA 200", Description = "Barras hacia atrás para calcular si la pendiente de EMA 200 está a favor", GroupName = "05. Filtro Macro EMA 200", Order = 3)]
        public int EmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exigir Pendiente EMA a Favor", Description = "true: Requiere que la EMA 200 apunte a favor del trade", GroupName = "05. Filtro Macro EMA 200", Order = 4)]
        public bool RequireEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(5.0, 5000.0)]
        [Display(Name = "TP Limit Ticket 1 ($)", Description = "Meta de ganancia fija para Ticket 1 ($60 MES nativo / auto-escala a $600 ES)", GroupName = "06. Campaña Relevos: Metas Take Profit ($)", Order = 1)]
        public double Ticket1TpDollars { get; set; }

        [NinjaScriptProperty]
        [Range(5.0, 5000.0)]
        [Display(Name = "TP Limit Tickets 2 y 3 ($)", Description = "Meta de ganancia fija para Tickets 2 y 3 ($20 MES nativo / auto-escala a $200 ES)", GroupName = "06. Campaña Relevos: Metas Take Profit ($)", Order = 2)]
        public double Ticket23TpDollars { get; set; }

        [NinjaScriptProperty]
        [Range(5.0, 5000.0)]
        [Display(Name = "TP Limit Tickets 4+ ($)", Description = "Meta de ganancia fija para Tickets 4 en adelante ($10 MES nativo / auto-escala a $100 ES)", GroupName = "06. Campaña Relevos: Metas Take Profit ($)", Order = 3)]
        public double Ticket4PlusTpDollars { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Distancia Mínima Relevos T2/T3 (Ticks)", Description = "Separación mínima fuera de Banda 3.5 requerida para disparar Tickets 2 y 3 (por defecto: 30 ticks)", GroupName = "07. Campaña Relevos: Distancias Exteriores", Order = 1)]
        public int RelayMinDistanceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Distancia Mínima Relevos T4+ (Ticks)", Description = "Separación mínima fuera de Banda 3.5 requerida para disparar Tickets 4+ (por defecto: 25 ticks)", GroupName = "07. Campaña Relevos: Distancias Exteriores", Order = 2)]
        public int RelayLateMinDistanceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(20, 60)]
        [Display(Name = "Umbral Distancia Stage 1 (Ticks)", Description = "Distancia exterior a partir de la cual se adelanta el SL a Stage 1 (por defecto: 35 ticks)", GroupName = "08. Opción B: Salto Escalonado de Stop Loss", Order = 1)]
        public int StagedStage1DistanceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Avance SL en Stage 1 (Ticks)", Description = "Ticks por delante de Banda 3.5 para anclar el SL en Stage 1 (por defecto: 12 ticks)", GroupName = "08. Opción B: Salto Escalonado de Stop Loss", Order = 2)]
        public int StagedStage1SlAdvanceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(25, 80)]
        [Display(Name = "Umbral Distancia Stage 2 (Ticks)", Description = "Distancia exterior a partir de la cual se adelanta el SL a Stage 2 (por defecto: 40 ticks)", GroupName = "08. Opción B: Salto Escalonado de Stop Loss", Order = 3)]
        public int StagedStage2DistanceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 40)]
        [Display(Name = "Avance SL en Stage 2 (Ticks)", Description = "Ticks por delante de Banda 3.5 para anclar el SL en Stage 2 (por defecto: 20 ticks)", GroupName = "08. Opción B: Salto Escalonado de Stop Loss", Order = 4)]
        public int StagedStage2SlAdvanceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Colchón Respiro SL Banda Azul T1 (Ticks)", Description = "Margen de holgura detrás de Banda Azul 1.75 en Ticket 1 (por defecto: 2 ticks)", GroupName = "08. Opción B: Salto Escalonado de Stop Loss", Order = 5)]
        public int SlBufferTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 60)]
        [Display(Name = "Minutos Cuarentena de Saciedad", Description = "Minutos de congelamiento tras finalizar campaña o SL secundario (por defecto: 10 min)", GroupName = "09. Gestión de Seguridad y Cuarentena", Order = 1)]
        public int QuarantineMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Stop Catastrófico Máximo (Ticks)", Description = "Riesgo máximo absoluto de seguridad por contrato (40 ticks = 10 pts = $50 MES)", GroupName = "09. Gestión de Seguridad y Cuarentena", Order = 2)]
        public int MaxCatastrophicStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 5.0)]
        [Display(Name = "Multiplicador Keltner Grande", GroupName = "10. Canales e Indicadores Técnicos", Order = 1)]
        public double KeltnerBigMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 3.0)]
        [Display(Name = "Multiplicador Keltner Pequeño", GroupName = "10. Canales e Indicadores Técnicos", Order = 2)]
        public double KeltnerSmallMult { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Periodo Keltner", GroupName = "10. Canales e Indicadores Técnicos", Order = 3)]
        public int KeltnerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(20, 300)]
        [Display(Name = "Periodo EMA Gravedad", GroupName = "10. Canales e Indicadores Técnicos", Order = 4)]
        public int EmaPeriod { get; set; }
        #endregion
    }
}
