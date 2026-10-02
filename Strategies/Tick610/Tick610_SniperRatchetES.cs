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
    public class Tick610_SniperRatchetES : Strategy
    {
        // === MÁQUINA DE ESTADOS (SETUP Y TRAILING) ===
        private enum SetupType { None, Long, Short }
        private SetupType currentSetup = SetupType.None;
        private int setupBarCounter = 0;

        private enum TrailingState { None, Phase1_OuterBand, Phase1_5_BreakEven, Phase2_Midline, Phase3_Choke }
        private TrailingState currentTrailingState = TrailingState.None;

        // Variables para tracking de MFE (Máxima excursión a favor)
        private double highestPriceSinceEntry = 0;
        private double lowestPriceSinceEntry = double.MaxValue;

        // === INDICADORES ===
        private EMA ema200;
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
                Description                                 = @"Estrategia Sniper V3.2: Stops Simulados para evitar rechazos de bróker en alta volatilidad.";
                Name                                        = "Tick610_SniperRatchetES";
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

                // Propiedades por defecto V3.2
                Version                 = "3.2";
                
                // Horarios
                StartTime               = 93500;   // 9:35 AM
                StopEntriesTime         = 154500;  // 3:45 PM
                ForceCloseTime          = 160000;  // 4:00 PM

                EmaPeriod               = 200;
                EmaFilterBars           = 20;      // N barras hacia atrás para validar tendencia pura
                TemaPeriod              = 9;
                KeltnerPeriod           = 52;
                KeltnerMultiplier       = 3.5;
                CountdownBars           = 10;
                
                UseMacdFilter           = false;
                MacdFast                = 8;
                MacdSlow                = 17;
                MacdSmooth              = 9;

                // Gestión de Riesgo Dinámica
                SlOffsetTicks           = 1;
                TemaToleranceTicks      = 2;
                BreakEvenTicks          = 16;      // Fase 1.5 a los $200
                ChokeThresholdTicks     = 72;      // Fase 3 a los $900
                ChokeTrailTicks         = 15;      // Trail matemático de la Fase 3
            }
            else if (State == State.DataLoaded)
            {
                ema200 = EMA(EmaPeriod);
                temaTrigger = TEMA(TemaPeriod);
                keltner = KeltnerChannel(KeltnerMultiplier, KeltnerPeriod);
                macd = MACD(MacdFast, MacdSlow, MacdSmooth);

                // AUTO-ARRANQUE PARA STRATEGY ANALYZER
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
            if (CurrentBar < Math.Max(BarsRequiredToTrade, EmaFilterBars)) return;

            // 1. CONTROL DE LA INTERFAZ WPF
            if (!isStrategyActive) return;

            // 2. CONTROL HORARIO
            int timeNow = ToTime(Time[0]);
            bool isForceCloseTime = timeNow >= ForceCloseTime;
            bool isTradingTime = timeNow >= StartTime && timeNow <= StopEntriesTime;

            if (isForceCloseTime)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    ExitLong("Cierre Fuera Horario", "SniperLong");
                    ExitShort("Cierre Fuera Horario", "SniperShort");
                    Print(Time[0] + " - Posición cerrada por seguridad (4:00 PM).");
                }
                currentSetup = SetupType.None;
                return;
            }

            // Actualizar MFE si estamos en posición
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (High[0] > highestPriceSinceEntry) highestPriceSinceEntry = High[0];
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (Low[0] < lowestPriceSinceEntry) lowestPriceSinceEntry = Low[0];
            }

            // 3. GESTIÓN DE RIESGO: SL DINÁMICO EN FASES
            ManageDynamicTrailingStop();

            // 4. LÓGICA DE ENTRADA (Solo si estamos planos y es hora de operar)
            if (Position.MarketPosition == MarketPosition.Flat && isTradingTime)
            {
                // A) Verificar Filtro Macro Desacoplado (EMA 200 en últimas EmaFilterBars)
                bool emaShortValid = true;
                bool emaLongValid = true;
                for (int i = 0; i <= EmaFilterBars; i++)
                {
                    if (High[i] >= ema200[i]) emaShortValid = false; // Todo debe estar debajo
                    if (Low[i] <= ema200[i]) emaLongValid = false;   // Todo debe estar arriba
                }

                // B) Modelo Híbrido: Precio o TEMA en Extremo (últimas 3 barras)
                bool touchedUpperExtreme = false;
                bool touchedLowerExtreme = false;
                double upperBandTolerance = keltner.Upper[1] - (TemaToleranceTicks * TickSize);
                double lowerBandTolerance = keltner.Lower[1] + (TemaToleranceTicks * TickSize);

                for (int i = 1; i <= 3; i++)
                {
                    if (High[i] >= upperBandTolerance || temaTrigger[i] >= upperBandTolerance) touchedUpperExtreme = true;
                    if (Low[i] <= lowerBandTolerance || temaTrigger[i] <= lowerBandTolerance) touchedLowerExtreme = true;
                }

                // Setup Corto
                bool isPeakShort = temaTrigger[2] < temaTrigger[1] && temaTrigger[0] < temaTrigger[1];
                bool isCrossBackShort = temaTrigger[1] >= upperBandTolerance && temaTrigger[0] < upperBandTolerance && temaTrigger[0] < temaTrigger[1];
                
                if ((isPeakShort && touchedUpperExtreme) || isCrossBackShort)
                {
                    if (!emaShortValid) 
                        Print(Time[0] + " - [FILTRO MACRO] Corto ignorado. Precio cruzó EMA 200 en las últimas " + EmaFilterBars + " barras.");
                    else
                    {
                        if (currentSetup == SetupType.Short) Print(Time[0] + " - [RESETEO CORTO] Reloj a 0.");
                        else Print(Time[0] + " - [ALERTA CORTO] Iniciando reloj de " + CountdownBars + " barras.");
                        currentSetup = SetupType.Short;
                        setupBarCounter = 0;
                    }
                }

                // Setup Largo
                bool isTroughLong = temaTrigger[2] > temaTrigger[1] && temaTrigger[0] > temaTrigger[1];
                bool isCrossBackLong = temaTrigger[1] <= lowerBandTolerance && temaTrigger[0] > lowerBandTolerance && temaTrigger[0] > temaTrigger[1];
                
                if ((isTroughLong && touchedLowerExtreme) || isCrossBackLong)
                {
                    if (!emaLongValid) 
                        Print(Time[0] + " - [FILTRO MACRO] Largo ignorado. Precio cruzó EMA 200 en las últimas " + EmaFilterBars + " barras.");
                    else
                    {
                        if (currentSetup == SetupType.Long) Print(Time[0] + " - [RESETEO LARGO] Reloj a 0.");
                        else Print(Time[0] + " - [ALERTA LARGO] Iniciando reloj de " + CountdownBars + " barras.");
                        currentSetup = SetupType.Long;
                        setupBarCounter = 0;
                    }
                }

                // C) Evaluar Conteo y Disparo
                if (currentSetup != SetupType.None)
                {
                    setupBarCounter++;
                    if (setupBarCounter > CountdownBars)
                    {
                        Print(Time[0] + " - [SETUP CANCELADO] Pasaron " + CountdownBars + " barras.");
                        currentSetup = SetupType.None;
                    }
                    else
                    {
                        bool macdValidLong = !UseMacdFilter || (macd.Diff[0] > macd.Diff[1]);
                        bool macdValidShort = !UseMacdFilter || (macd.Diff[0] < macd.Diff[1]);

                        if (currentSetup == SetupType.Long && CrossAbove(temaTrigger, keltner.Midline, 1) && macdValidLong)
                        {
                            EnterLong("SniperLong");
                            currentTrailingState = TrailingState.Phase1_OuterBand;
                            highestPriceSinceEntry = High[0]; // Reset tracking
                            currentSetup = SetupType.None;
                            SetStopLoss("SniperLong", CalculationMode.Price, keltner.Lower[0] - (SlOffsetTicks * TickSize), true); // V3.2 Simulated
                        }
                        else if (currentSetup == SetupType.Short && CrossBelow(temaTrigger, keltner.Midline, 1) && macdValidShort)
                        {
                            EnterShort("SniperShort");
                            currentTrailingState = TrailingState.Phase1_OuterBand;
                            lowestPriceSinceEntry = Low[0]; // Reset tracking
                            currentSetup = SetupType.None;
                            SetStopLoss("SniperShort", CalculationMode.Price, keltner.Upper[0] + (SlOffsetTicks * TickSize), true); // V3.2 Simulated
                        }
                    }
                }
            }
            else if (Position.MarketPosition != MarketPosition.Flat)
            {
                currentSetup = SetupType.None; // Reset si ya estamos dentro
            }
        }

        private void ManageDynamicTrailingStop()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                currentTrailingState = TrailingState.None;
                return;
            }

            double entryPrice = Position.AveragePrice;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                double maxProfitTicks = (highestPriceSinceEntry - entryPrice) / TickSize;

                // --- EVALUAR UPGRADES DE FASE ---
                if (maxProfitTicks >= ChokeThresholdTicks && currentTrailingState < TrailingState.Phase3_Choke)
                {
                    currentTrailingState = TrailingState.Phase3_Choke;
                    Print(Time[0] + " - [Fase 3 LARGO] Límite de estrangulamiento alcanzado (" + ChokeThresholdTicks + " tks).");
                }
                else if (High[0] >= keltner.Upper[0] && currentTrailingState < TrailingState.Phase2_Midline && currentTrailingState != TrailingState.Phase3_Choke)
                {
                    currentTrailingState = TrailingState.Phase2_Midline;
                    Print(Time[0] + " - [Fase 2 LARGO] Banda tocada. Persiguiendo Línea Media.");
                }
                else if (maxProfitTicks >= BreakEvenTicks && currentTrailingState < TrailingState.Phase1_5_BreakEven && currentTrailingState != TrailingState.Phase3_Choke)
                {
                    currentTrailingState = TrailingState.Phase1_5_BreakEven;
                    Print(Time[0] + " - [Fase 1.5 LARGO] Asegurando Break-Even.");
                }

                // --- APLICAR STOP LOSS SEGÚN FASE ---
                double slPrice = 0;
                if (currentTrailingState == TrailingState.Phase1_OuterBand)
                {
                    slPrice = keltner.Lower[0] - (SlOffsetTicks * TickSize);
                }
                else if (currentTrailingState == TrailingState.Phase1_5_BreakEven)
                {
                    slPrice = entryPrice + (1 * TickSize); // Break Even + 1 tick
                }
                else if (currentTrailingState == TrailingState.Phase2_Midline)
                {
                    slPrice = keltner.Midline[0] - (SlOffsetTicks * TickSize);
                }
                else if (currentTrailingState == TrailingState.Phase3_Choke)
                {
                    if (Close[0] >= keltner.Upper[0]) // Si está fuera
                        slPrice = keltner.Upper[0] - (SlOffsetTicks * TickSize);
                    else // Si está dentro (Trail Matemático)
                        slPrice = highestPriceSinceEntry - (ChokeTrailTicks * TickSize);
                }

                // BLINDAJE DE BREAK-EVEN
                if (maxProfitTicks >= BreakEvenTicks)
                {
                    double bePrice = entryPrice + (1 * TickSize);
                    if (slPrice < bePrice) slPrice = bePrice; 
                }

                // V3.2 Simulated Stop = true
                if (slPrice > 0) SetStopLoss("SniperLong", CalculationMode.Price, slPrice, true);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double maxProfitTicks = (entryPrice - lowestPriceSinceEntry) / TickSize;

                // --- EVALUAR UPGRADES DE FASE ---
                if (maxProfitTicks >= ChokeThresholdTicks && currentTrailingState < TrailingState.Phase3_Choke)
                {
                    currentTrailingState = TrailingState.Phase3_Choke;
                    Print(Time[0] + " - [Fase 3 CORTO] Límite de estrangulamiento alcanzado (" + ChokeThresholdTicks + " tks).");
                }
                else if (Low[0] <= keltner.Lower[0] && currentTrailingState < TrailingState.Phase2_Midline && currentTrailingState != TrailingState.Phase3_Choke)
                {
                    currentTrailingState = TrailingState.Phase2_Midline;
                    Print(Time[0] + " - [Fase 2 CORTO] Banda tocada. Persiguiendo Línea Media.");
                }
                else if (maxProfitTicks >= BreakEvenTicks && currentTrailingState < TrailingState.Phase1_5_BreakEven && currentTrailingState != TrailingState.Phase3_Choke)
                {
                    currentTrailingState = TrailingState.Phase1_5_BreakEven;
                    Print(Time[0] + " - [Fase 1.5 CORTO] Asegurando Break-Even.");
                }

                // --- APLICAR STOP LOSS SEGÚN FASE ---
                double slPrice = double.MaxValue;
                if (currentTrailingState == TrailingState.Phase1_OuterBand)
                {
                    slPrice = keltner.Upper[0] + (SlOffsetTicks * TickSize);
                }
                else if (currentTrailingState == TrailingState.Phase1_5_BreakEven)
                {
                    slPrice = entryPrice - (1 * TickSize); // Break Even - 1 tick
                }
                else if (currentTrailingState == TrailingState.Phase2_Midline)
                {
                    slPrice = keltner.Midline[0] + (SlOffsetTicks * TickSize);
                }
                else if (currentTrailingState == TrailingState.Phase3_Choke)
                {
                    if (Close[0] <= keltner.Lower[0]) // Si está fuera
                        slPrice = keltner.Lower[0] + (SlOffsetTicks * TickSize);
                    else // Si está dentro (Trail Matemático)
                        slPrice = lowestPriceSinceEntry + (ChokeTrailTicks * TickSize);
                }

                // BLINDAJE DE BREAK-EVEN
                if (maxProfitTicks >= BreakEvenTicks)
                {
                    double bePrice = entryPrice - (1 * TickSize);
                    if (slPrice > bePrice) slPrice = bePrice; 
                }

                // V3.2 Simulated Stop = true
                if (slPrice < double.MaxValue) SetStopLoss("SniperShort", CalculationMode.Price, slPrice, true);
            }
        }

        #region Interfaz UI y Botón de Pánico (WPF)
        // ... (WPF logic remains identical)
        private void CreateWPFControls()
        {
            chartGrid = new System.Windows.Controls.Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 10) };
            panicButton = new System.Windows.Controls.Button { Content = "Sniper: PAUSADO", Foreground = Brushes.White, Background = Brushes.Red, FontWeight = FontWeights.Bold, Padding = new Thickness(10, 5, 10, 5), BorderThickness = new Thickness(2), BorderBrush = Brushes.DarkRed };
            panicButton.Click += OnPanicButtonClick;
            chartGrid.Children.Add(panicButton);
            UserControlCollection.Add(chartGrid);
            ChartPanel.PreviewKeyDown += ChartPanel_PreviewKeyDown;

            if (isStrategyActive)
            {
                panicButton.Content = "Sniper: ACTIVO";
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
                    panicButton.Content = isStrategyActive ? "Sniper: ACTIVO" : "Sniper: PAUSADO";
                    panicButton.Background = isStrategyActive ? Brushes.Green : Brushes.Red;
                    panicButton.BorderBrush = isStrategyActive ? Brushes.DarkGreen : Brushes.DarkRed;
                    Print(Time[0] + (isStrategyActive ? " - Estrategia Sniper ACTIVADA." : " - Estrategia Sniper PAUSADA."));
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
        [Display(Name="Inicio de Entradas", Order=1, GroupName="1. Horarios (HHMMSS)")]
        public int StartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Límite Entradas Nuevas", Order=2, GroupName="1. Horarios (HHMMSS)")]
        public int StopEntriesTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Cierre Forzoso Total", Order=3, GroupName="1. Horarios (HHMMSS)")]
        public int ForceCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo EMA", Order=1, GroupName="2. Filtro Macro")]
        public int EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Barras Filtro EMA", Description="Lookback para asegurar tendencia pura", Order=2, GroupName="2. Filtro Macro")]
        public int EmaFilterBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo Keltner", Order=1, GroupName="3. Keltner")]
        public int KeltnerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name="Multiplicador Keltner", Order=2, GroupName="3. Keltner")]
        public double KeltnerMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo TEMA", Order=1, GroupName="4. Disparo")]
        public int TemaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Barras de Reloj", Order=2, GroupName="4. Disparo")]
        public int CountdownBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name="Tolerancia Gancho", Order=3, GroupName="4. Disparo")]
        public int TemaToleranceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(-100, int.MaxValue)]
        [Display(Name="Offset SL", Order=1, GroupName="5. Gestión de Riesgo Dinámica")]
        public int SlOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Break-Even Ticks (Fase 1.5)", Order=2, GroupName="5. Gestión de Riesgo Dinámica")]
        public int BreakEvenTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Inicio Ahogo Ticks (Fase 3)", Order=3, GroupName="5. Gestión de Riesgo Dinámica")]
        public int ChokeThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Trail de Ahogo Ticks", Order=4, GroupName="5. Gestión de Riesgo Dinámica")]
        public int ChokeTrailTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Usar MACD Opcional", Order=1, GroupName="6. Opcionales")]
        public bool UseMacdFilter { get; set; }

        [Browsable(false)] public int MacdFast { get; set; }
        [Browsable(false)] public int MacdSlow { get; set; }
        [Browsable(false)] public int MacdSmooth { get; set; }
        #endregion
    }
}
