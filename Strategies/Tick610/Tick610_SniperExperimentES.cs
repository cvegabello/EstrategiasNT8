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

namespace NinjaTrader.NinjaScript.Strategies
{
    public class Tick610_SniperExperimentES : Strategy
    {
        // === MÁQUINA DE ESTADOS (SETUP Y ESPERA) ===
        private enum SetupType { None, Long, Short }
        private SetupType currentSetup = SetupType.None;
        private int setupBarCounter = 0;

        private enum WaitState { None, WaitingCrossUp, WaitingCrossDown }
        private WaitState currentWaitState = WaitState.None;
        private int waitBarCounter = 0;
        private bool isTrapTrade = false; // Flag para saber si es un trade fijo de trampa

        private enum TrailingState { None, Phase1_OuterBand, Phase1_5_BreakEven, Phase2_Midline, Phase3_Choke }
        private TrailingState currentTrailingState = TrailingState.None;

        // Variables para tracking de MFE
        private double highestPriceSinceEntry = 0;
        private double lowestPriceSinceEntry = double.MaxValue;

        // === INDICADORES ===
        private TEMA temaTrigger;
        private KeltnerChannel keltner;
        private MACD macd;

        // === VARIABLES DE CONTROL (INTERFAZ WPF) ===
        private System.Windows.Controls.Button panicButton;
        private System.Windows.Controls.Grid chartGrid;
        private bool isStrategyActive = false; 

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Estrategia Sniper EXPERIMENTO: Espera 6 barras tras el cruce para evaluar Trampa (Fake-out) vs Continuación.";
                Name                                        = "Tick610_SniperExperimentES";
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

                // Propiedades
                Version                 = "2.0-Trampas";
                
                // Horarios
                StartTime               = 95000;   
                StopEntriesTime         = 154500;  
                ForceCloseTime          = 160000;  

                TemaPeriod              = 9;
                KeltnerPeriod           = 52;
                KeltnerMultiplier       = 3.5;
                CountdownBars           = 10;
                TemaToleranceTicks      = 2;
                
                // Experimento de Barras de Espera
                WaitBars                = 6;
                TrapProfitTicks         = 16;      // 16 ticks = $200 en ES
                
                UseMacdFilter           = false;
                MacdFast                = 8;
                MacdSlow                = 17;
                MacdSmooth              = 9;

                // Gestión de Riesgo Dinámica (Solo aplica a Continuación)
                UseBreakEven            = true;    
                SlOffsetTicks           = 1;
                BreakEvenTicks          = 22;      
                ChokeThresholdTicks     = 72;      
                ChokeTrailTicks         = 15;      
                
                ParabolicTriggerTicks   = 10;      
                ParabolicChicleTicks    = 7;       
            }
            else if (State == State.DataLoaded)
            {
                temaTrigger = TEMA(TemaPeriod);
                keltner = KeltnerChannel(KeltnerMultiplier, KeltnerPeriod);
                macd = MACD(MacdFast, MacdSlow, MacdSmooth);

                if (ChartControl == null)
                {
                    isStrategyActive = true; 
                    Print("Entorno Headless (Analyzer) detectado. Estrategia auto-activada.");
                }
            }
            else if (State == State.Historical)
            {
                if (ChartControl != null && UserControlCollection != null)
                {
                    Dispatcher.InvokeAsync((() => { CreateWPFControls(); }));
                }
            }
            else if (State == State.Terminated)
            {
                if (ChartControl != null && UserControlCollection != null)
                {
                    Dispatcher.InvokeAsync((() => { DisposeWPFControls(); }));
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade) return;

            // 1. CONTROL WPF
            if (!isStrategyActive) return;

            // 2. CONTROL HORARIO
            int timeNow = ToTime(Time[0]);
            bool isForceCloseTime = timeNow >= ForceCloseTime;
            bool isTradingTime = timeNow >= StartTime && timeNow <= StopEntriesTime;

            if (isForceCloseTime)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    ExitLong("Cierre Fuera Horario", "");
                    ExitShort("Cierre Fuera Horario", "");
                    Print(Time[0] + " - Posición cerrada por seguridad (4:00 PM).");
                }
                currentSetup = SetupType.None;
                currentWaitState = WaitState.None;
                return;
            }

            // Actualizar MFE
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (High[0] > highestPriceSinceEntry) highestPriceSinceEntry = High[0];
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (Low[0] < lowestPriceSinceEntry) lowestPriceSinceEntry = Low[0];
            }

            // 3. GESTIÓN DE RIESGO
            ManageDynamicTrailingStop();

            // 4. LÓGICA DE ESPERA Y ENTRADA
            if (Position.MarketPosition == MarketPosition.Flat && isTradingTime)
            {
                // A. Si estamos en el periodo de espera de 6 barras
                if (currentWaitState != WaitState.None)
                {
                    waitBarCounter++;
                    if (waitBarCounter >= WaitBars)
                    {
                        if (currentWaitState == WaitState.WaitingCrossUp)
                        {
                            if (temaTrigger[0] < keltner.Midline[0]) 
                            {
                                // TRAMPA (TEMA debajo) -> Corto
                                isTrapTrade = true;
                                EnterShort("SniperTrapShort");
                                SetProfitTarget("SniperTrapShort", CalculationMode.Ticks, TrapProfitTicks);
                                SetStopLoss("SniperTrapShort", CalculationMode.Price, keltner.Upper[0] + (SlOffsetTicks * TickSize), false);
                                Print(Time[0] + " - [TRAMPA ALCISTA] 6 barras después, TEMA debajo Midline. CORTO (TP " + TrapProfitTicks + " tks).");
                            }
                            else
                            {
                                // CONTINUACIÓN -> Largo
                                isTrapTrade = false;
                                EnterLong("SniperExpLong");
                                currentTrailingState = TrailingState.Phase1_OuterBand;
                                highestPriceSinceEntry = High[0]; 
                                SetStopLoss("SniperExpLong", CalculationMode.Price, keltner.Lower[0] - (SlOffsetTicks * TickSize), true); 
                                Print(Time[0] + " - [CONTINUACIÓN ALCISTA] 6 barras después, TEMA sobre Midline. LARGO Dinámico.");
                            }
                        }
                        else if (currentWaitState == WaitState.WaitingCrossDown)
                        {
                            if (temaTrigger[0] > keltner.Midline[0]) 
                            {
                                // TRAMPA (TEMA arriba) -> Largo
                                isTrapTrade = true;
                                EnterLong("SniperTrapLong");
                                SetProfitTarget("SniperTrapLong", CalculationMode.Ticks, TrapProfitTicks);
                                SetStopLoss("SniperTrapLong", CalculationMode.Price, keltner.Lower[0] - (SlOffsetTicks * TickSize), false);
                                Print(Time[0] + " - [TRAMPA BAJISTA] 6 barras después, TEMA sobre Midline. LARGO (TP " + TrapProfitTicks + " tks).");
                            }
                            else
                            {
                                // CONTINUACIÓN -> Corto
                                isTrapTrade = false;
                                EnterShort("SniperExpShort");
                                currentTrailingState = TrailingState.Phase1_OuterBand;
                                lowestPriceSinceEntry = Low[0]; 
                                SetStopLoss("SniperExpShort", CalculationMode.Price, keltner.Upper[0] + (SlOffsetTicks * TickSize), true); 
                                Print(Time[0] + " - [CONTINUACIÓN BAJISTA] 6 barras después, TEMA debajo Midline. CORTO Dinámico.");
                            }
                        }
                        
                        // Resetear estado de espera
                        currentWaitState = WaitState.None;
                    }
                    return; // No evaluar setups nuevos mientras estamos esperando
                }

                // B. Lógica de armado original (Buscando tocar extremos)
                bool touchedUpperExtreme = false;
                bool touchedLowerExtreme = false;
                
                for (int i = 1; i <= 3; i++)
                {
                    double upperTemaTol = keltner.Upper[i] - (TemaToleranceTicks * TickSize);
                    double lowerTemaTol = keltner.Lower[i] + (TemaToleranceTicks * TickSize);
                    double upperPricePen = keltner.Upper[i] + (1.5 * TickSize);
                    double lowerPricePen = keltner.Lower[i] - (1.5 * TickSize);

                    if (High[i] >= upperPricePen || temaTrigger[i] >= upperTemaTol) touchedUpperExtreme = true;
                    if (Low[i] <= lowerPricePen || temaTrigger[i] <= lowerTemaTol) touchedLowerExtreme = true;
                }

                double upperBandTolCross = keltner.Upper[1] - (TemaToleranceTicks * TickSize);
                double lowerBandTolCross = keltner.Lower[1] + (TemaToleranceTicks * TickSize);

                bool isPeakShort = temaTrigger[2] < temaTrigger[1] && temaTrigger[0] < temaTrigger[1];
                bool isCrossBackShort = temaTrigger[1] >= upperBandTolCross && temaTrigger[0] < upperBandTolCross && temaTrigger[0] < temaTrigger[1];
                
                if ((isPeakShort && touchedUpperExtreme) || isCrossBackShort)
                {
                    if (currentSetup == SetupType.Short) Print(Time[0] + " - [RESETEO CORTO] Reloj a 0.");
                    else Print(Time[0] + " - [ALERTA CORTO] Iniciando reloj de " + CountdownBars + " barras.");
                    currentSetup = SetupType.Short;
                    setupBarCounter = 0;
                }

                bool isTroughLong = temaTrigger[2] > temaTrigger[1] && temaTrigger[0] > temaTrigger[1];
                bool isCrossBackLong = temaTrigger[1] <= lowerBandTolCross && temaTrigger[0] > lowerBandTolCross && temaTrigger[0] > temaTrigger[1];
                
                if ((isTroughLong && touchedLowerExtreme) || isCrossBackLong)
                {
                    if (currentSetup == SetupType.Long) Print(Time[0] + " - [RESETEO LARGO] Reloj a 0.");
                    else Print(Time[0] + " - [ALERTA LARGO] Iniciando reloj de " + CountdownBars + " barras.");
                    currentSetup = SetupType.Long;
                    setupBarCounter = 0;
                }

                // C. Disparador del cruce (Inicia la espera)
                if (currentSetup != SetupType.None)
                {
                    setupBarCounter++;
                    if (setupBarCounter > CountdownBars)
                    {
                        Print(Time[0] + " - [SETUP CANCELADO] Pasaron " + CountdownBars + " barras sin cruce.");
                        currentSetup = SetupType.None;
                    }
                    else
                    {
                        bool macdValidLong = !UseMacdFilter || (macd.Diff[0] > macd.Diff[1]);
                        bool macdValidShort = !UseMacdFilter || (macd.Diff[0] < macd.Diff[1]);

                        if (currentSetup == SetupType.Long && CrossAbove(temaTrigger, keltner.Midline, 1) && macdValidLong)
                        {
                            currentWaitState = WaitState.WaitingCrossUp;
                            waitBarCounter = 0;
                            currentSetup = SetupType.None;
                            Print(Time[0] + " - [CRUCE ARRIBA] Congelado. Esperando " + WaitBars + " barras para confirmación.");
                        }
                        else if (currentSetup == SetupType.Short && CrossBelow(temaTrigger, keltner.Midline, 1) && macdValidShort)
                        {
                            currentWaitState = WaitState.WaitingCrossDown;
                            waitBarCounter = 0;
                            currentSetup = SetupType.None;
                            Print(Time[0] + " - [CRUCE ABAJO] Congelado. Esperando " + WaitBars + " barras para confirmación.");
                        }
                    }
                }
            }
            else if (Position.MarketPosition != MarketPosition.Flat)
            {
                currentSetup = SetupType.None; 
                currentWaitState = WaitState.None;
            }
        }

        private void ManageDynamicTrailingStop()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                currentTrailingState = TrailingState.None;
                return;
            }

            // Si es un trade de trampa, el SL/TP es fijo manejado por NT8. Ignoramos esta rutina.
            if (isTrapTrade) return;

            double entryPrice = Position.AveragePrice;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                double maxProfitTicks = (highestPriceSinceEntry - entryPrice) / TickSize;

                if (maxProfitTicks >= ChokeThresholdTicks && currentTrailingState < TrailingState.Phase3_Choke)
                {
                    currentTrailingState = TrailingState.Phase3_Choke;
                    Print(Time[0] + " - [Fase 3 LARGO] Límite de estrangulamiento alcanzado.");
                }
                else if (High[0] >= keltner.Upper[0] && currentTrailingState < TrailingState.Phase2_Midline && currentTrailingState != TrailingState.Phase3_Choke)
                {
                    currentTrailingState = TrailingState.Phase2_Midline;
                    Print(Time[0] + " - [Fase 2 LARGO] Banda tocada. Persiguiendo Línea Media.");
                }
                else if (UseBreakEven && maxProfitTicks >= BreakEvenTicks && currentTrailingState < TrailingState.Phase1_5_BreakEven && currentTrailingState != TrailingState.Phase3_Choke)
                {
                    currentTrailingState = TrailingState.Phase1_5_BreakEven;
                    Print(Time[0] + " - [Fase 1.5 LARGO] Asegurando Break-Even.");
                }

                double slPrice = 0;
                if (currentTrailingState == TrailingState.Phase1_OuterBand)
                {
                    slPrice = keltner.Lower[0] - (SlOffsetTicks * TickSize);
                }
                else if (currentTrailingState == TrailingState.Phase1_5_BreakEven)
                {
                    slPrice = entryPrice + (1 * TickSize); 
                }
                else if (currentTrailingState == TrailingState.Phase2_Midline)
                {
                    slPrice = keltner.Midline[0] - (SlOffsetTicks * TickSize);
                }
                else if (currentTrailingState == TrailingState.Phase3_Choke)
                {
                    double keltnerSl = keltner.Upper[0] - (SlOffsetTicks * TickSize);
                    double mathSlInside = highestPriceSinceEntry - (ChokeTrailTicks * TickSize);
                    double parabolicSl = highestPriceSinceEntry - (ParabolicChicleTicks * TickSize);

                    if (highestPriceSinceEntry >= keltner.Upper[0] + (ParabolicTriggerTicks * TickSize))
                    {
                        slPrice = Math.Max(keltnerSl, parabolicSl);
                    }
                    else if (Close[0] >= keltner.Upper[0]) 
                    {
                        slPrice = keltnerSl; 
                    }
                    else 
                    {
                        slPrice = mathSlInside; 
                    }
                }

                if (UseBreakEven && maxProfitTicks >= BreakEvenTicks)
                {
                    double bePrice = entryPrice + (1 * TickSize);
                    if (slPrice < bePrice) slPrice = bePrice; 
                }

                if (slPrice > 0) SetStopLoss("SniperExpLong", CalculationMode.Price, slPrice, true);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double maxProfitTicks = (entryPrice - lowestPriceSinceEntry) / TickSize;

                if (maxProfitTicks >= ChokeThresholdTicks && currentTrailingState < TrailingState.Phase3_Choke)
                {
                    currentTrailingState = TrailingState.Phase3_Choke;
                    Print(Time[0] + " - [Fase 3 CORTO] Límite de estrangulamiento alcanzado.");
                }
                else if (Low[0] <= keltner.Lower[0] && currentTrailingState < TrailingState.Phase2_Midline && currentTrailingState != TrailingState.Phase3_Choke)
                {
                    currentTrailingState = TrailingState.Phase2_Midline;
                    Print(Time[0] + " - [Fase 2 CORTO] Banda tocada. Persiguiendo Línea Media.");
                }
                else if (UseBreakEven && maxProfitTicks >= BreakEvenTicks && currentTrailingState < TrailingState.Phase1_5_BreakEven && currentTrailingState != TrailingState.Phase3_Choke)
                {
                    currentTrailingState = TrailingState.Phase1_5_BreakEven;
                    Print(Time[0] + " - [Fase 1.5 CORTO] Asegurando Break-Even.");
                }

                double slPrice = double.MaxValue;
                if (currentTrailingState == TrailingState.Phase1_OuterBand)
                {
                    slPrice = keltner.Upper[0] + (SlOffsetTicks * TickSize);
                }
                else if (currentTrailingState == TrailingState.Phase1_5_BreakEven)
                {
                    slPrice = entryPrice - (1 * TickSize); 
                }
                else if (currentTrailingState == TrailingState.Phase2_Midline)
                {
                    slPrice = keltner.Midline[0] + (SlOffsetTicks * TickSize);
                }
                else if (currentTrailingState == TrailingState.Phase3_Choke)
                {
                    double keltnerSl = keltner.Lower[0] + (SlOffsetTicks * TickSize);
                    double mathSlInside = lowestPriceSinceEntry + (ChokeTrailTicks * TickSize);
                    double parabolicSl = lowestPriceSinceEntry + (ParabolicChicleTicks * TickSize);

                    if (lowestPriceSinceEntry <= keltner.Lower[0] - (ParabolicTriggerTicks * TickSize))
                    {
                        slPrice = Math.Min(keltnerSl, parabolicSl);
                    }
                    else if (Close[0] <= keltner.Lower[0]) 
                    {
                        slPrice = keltnerSl; 
                    }
                    else 
                    {
                        slPrice = mathSlInside; 
                    }
                }

                if (UseBreakEven && maxProfitTicks >= BreakEvenTicks)
                {
                    double bePrice = entryPrice - (1 * TickSize);
                    if (slPrice > bePrice) slPrice = bePrice; 
                }

                if (slPrice < double.MaxValue) SetStopLoss("SniperExpShort", CalculationMode.Price, slPrice, true);
            }
        }

        #region Interfaz UI y Botón de Pánico (WPF)
        private void CreateWPFControls()
        {
            chartGrid = new System.Windows.Controls.Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 10) };
            panicButton = new System.Windows.Controls.Button { Content = "SniperExp: PAUSADO", Foreground = Brushes.White, Background = Brushes.Red, FontWeight = FontWeights.Bold, Padding = new Thickness(10, 5, 10, 5), BorderThickness = new Thickness(2), BorderBrush = Brushes.DarkRed };
            panicButton.Click += OnPanicButtonClick;
            chartGrid.Children.Add(panicButton);
            UserControlCollection.Add(chartGrid);
            ChartPanel.PreviewKeyDown += ChartPanel_PreviewKeyDown;

            if (isStrategyActive)
            {
                panicButton.Content = "SniperExp: ACTIVO";
                panicButton.Background = Brushes.Green;
                panicButton.BorderBrush = Brushes.DarkGreen;
            }
        }

        private void DisposeWPFControls()
        {
            if (panicButton != null) panicButton.Click -= OnPanicButtonClick;
            if (ChartPanel != null) ChartPanel.PreviewKeyDown -= ChartPanel_PreviewKeyDown;
            if (chartGrid != null && UserControlCollection.Contains(chartGrid)) UserControlCollection.Remove(chartGrid);
        }

        private void OnPanicButtonClick(object sender, RoutedEventArgs e) { ToggleStrategyStatus(); e.Handled = true; }
        private void ChartPanel_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) { ToggleStrategyStatus(); e.Handled = true; } }

        private void ToggleStrategyStatus()
        {
            isStrategyActive = !isStrategyActive;
            if (panicButton != null)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    panicButton.Content = isStrategyActive ? "SniperExp: ACTIVO" : "SniperExp: PAUSADO";
                    panicButton.Background = isStrategyActive ? Brushes.Green : Brushes.Red;
                    panicButton.BorderBrush = isStrategyActive ? Brushes.DarkGreen : Brushes.DarkRed;
                    Print(Time[0] + (isStrategyActive ? " - Estrategia SniperExp ACTIVADA." : " - Estrategia SniperExp PAUSADA."));
                });
            }
        }
        #endregion

        #region Propiedades
        [NinjaScriptProperty]
        [Display(Name="Versión", Order=0, GroupName="0. Información")]
        [ReadOnly(true)]
        public string Version { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Inicio de Entradas", Order=1, GroupName="1. Horarios")]
        public int StartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Límite Entradas Nuevas", Order=2, GroupName="1. Horarios")]
        public int StopEntriesTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Cierre Forzoso Total", Order=3, GroupName="1. Horarios")]
        public int ForceCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo Keltner", Order=1, GroupName="2. Indicadores")]
        public int KeltnerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name="Multiplicador Keltner", Order=2, GroupName="2. Indicadores")]
        public double KeltnerMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo TEMA", Order=1, GroupName="3. Disparo (Continuación)")]
        public int TemaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Barras de Reloj Setup", Order=2, GroupName="3. Disparo (Continuación)")]
        public int CountdownBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name="Tolerancia Gancho", Order=3, GroupName="3. Disparo (Continuación)")]
        public int TemaToleranceTicks { get; set; }

        // --- EXPERIMENTO DE ESPERA ---
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Barras de Espera tras Cruce", Order=1, GroupName="4. Experimento (Espera y Trampas)")]
        public int WaitBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="TP de la Trampa (Ticks) (16 = $200)", Order=2, GroupName="4. Experimento (Espera y Trampas)")]
        public int TrapProfitTicks { get; set; }

        // --- GESTIÓN DE RIESGO ---
        [NinjaScriptProperty]
        [Display(Name="Usar Break-Even (Fase 1.5)", Order=1, GroupName="5. Gestión de Riesgo (Dinámica)")]
        public bool UseBreakEven { get; set; }

        [NinjaScriptProperty]
        [Range(-100, int.MaxValue)]
        [Display(Name="Offset SL", Order=2, GroupName="5. Gestión de Riesgo (Dinámica)")]
        public int SlOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Break-Even Ticks (Fase 1.5)", Order=3, GroupName="5. Gestión de Riesgo (Dinámica)")]
        public int BreakEvenTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Inicio Ahogo Ticks (Fase 3)", Order=4, GroupName="5. Gestión de Riesgo (Dinámica)")]
        public int ChokeThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Trail de Ahogo Ticks", Order=5, GroupName="5. Gestión de Riesgo (Dinámica)")]
        public int ChokeTrailTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Activación Chicle (Ticks Extremos)", Order=6, GroupName="5. Gestión de Riesgo (Dinámica)")]
        public int ParabolicTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Distancia Chicle Ticks", Order=7, GroupName="5. Gestión de Riesgo (Dinámica)")]
        public int ParabolicChicleTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Usar MACD Opcional", Order=1, GroupName="6. Opcionales")]
        public bool UseMacdFilter { get; set; }

        [Browsable(false)] public int MacdFast { get; set; }
        [Browsable(false)] public int MacdSlow { get; set; }
        [Browsable(false)] public int MacdSmooth { get; set; }
        #endregion
    }
}
