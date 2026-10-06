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
    public class Tick610_MicroTrendES : Strategy
    {
        private EMA ema200;
        private TEMA temaTrigger;
        private KeltnerChannel keltner;

        // === VARIABLES DE CONTROL ===
        private double currentSlPrice = 0;
        private double highestPriceSinceEntry = 0;
        private double lowestPriceSinceEntry = 0;
        private bool isTrailingActive = false;
        
        // INTERFAZ WPF
        private System.Windows.Controls.Button panicButton;
        private System.Windows.Controls.Grid chartGrid;
        private bool isStrategyActive = false; 

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Estrategia MicroTrend V1.0: Atrapa sub-tendencias explosivas (Pinch Breakout y Runaway Trend).";
                Name                                        = "Tick610_MicroTrendES";
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

                Version                 = "1.0";
                
                // Horarios
                StartTime               = 95000;   
                StopEntriesTime         = 154500;  
                ForceCloseTime          = 160000;  

                // Indicadores
                EmaPeriod               = 200;
                TemaPeriod              = 9;
                KeltnerPeriod           = 52;
                KeltnerMultiplier       = 3.5;

                // Filtros de Entrada
                MaxPinchTicks           = 8;       // Máxima distancia entre EMA200 y Midline para Tipo 1
                SlopeLookbackBars       = 5;       // Barras atrás para medir pendiente
                MinSlopeTicks           = 2.0;     // Pendiente mínima exigida

                // Gestión de Riesgo (Trailing Stop)
                SlOffsetTicks           = 2;       // Holgura para el SL inicial
                BreakEvenTicks          = 20;      // Ticks de ganancia para mover SL a BreakEven
                TrailingStepTicks       = 10;      // Ticks de distancia para el Trailing una vez activo
            }
            else if (State == State.DataLoaded)
            {
                ema200 = EMA(EmaPeriod);
                temaTrigger = TEMA(TemaPeriod);
                keltner = KeltnerChannel(KeltnerPeriod, KeltnerMultiplier);

                ema200.Plots[0].Brush = Brushes.Red;
                temaTrigger.Plots[0].Brush = Brushes.Blue;
                keltner.Plots[0].Brush = Brushes.LightGray; 
                keltner.Plots[1].Brush = Brushes.White;     
                keltner.Plots[2].Brush = Brushes.LightGray; 

                AddChartIndicator(ema200);
                AddChartIndicator(temaTrigger);
                AddChartIndicator(keltner);
            }
            else if (State == State.Historical)
            {
                if (UserControlCollection.Contains(chartGrid)) return;
                Dispatcher.InvokeAsync((Action)(() => CreateWPFControls()));
            }
            else if (State == State.Terminated)
            {
                if (chartGrid != null)
                {
                    Dispatcher.InvokeAsync((Action)(() => { UserControlCollection.Remove(chartGrid); }));
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade) return;

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
                    ExitLong("Cierre Fuera Horario", "MicroLong_T1");
                    ExitLong("Cierre Fuera Horario", "MicroLong_T2");
                    ExitShort("Cierre Fuera Horario", "MicroShort_T1");
                    ExitShort("Cierre Fuera Horario", "MicroShort_T2");
                    Print(Time[0] + " - Posición cerrada por seguridad (4:00 PM).");
                }
                return;
            }

            // Actualizar MFE para el Trailing
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (High[0] > highestPriceSinceEntry) highestPriceSinceEntry = High[0];
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (Low[0] < lowestPriceSinceEntry) lowestPriceSinceEntry = Low[0];
            }

            // 3. GESTIÓN DE RIESGO (TRAILING STOP)
            ManageDynamicTrailingStop();

            // 4. LÓGICA DE ENTRADA
            if (Position.MarketPosition == MarketPosition.Flat && isTradingTime)
            {
                isTrailingActive = false;

                // Pendiente de la línea media (blanca)
                double midlineSlopeVal = (keltner.Midline[0] - keltner.Midline[Math.Min(SlopeLookbackBars, CurrentBar)]) / TickSize;
                
                // Distancia entre línea blanca y roja
                double pinchDistanceTicks = Math.Abs(keltner.Midline[0] - ema200[0]) / TickSize;

                // === TIPO 1: COMPRESIÓN (PINCH BREAKOUT) ===
                bool isPinchValid = pinchDistanceTicks <= MaxPinchTicks;
                
                // Disparo T1 Largo: Azul cruza blanca hacia arriba
                if (CrossAbove(temaTrigger, keltner.Midline, 1))
                {
                    if (isPinchValid && midlineSlopeVal >= MinSlopeTicks)
                    {
                        EnterLong("MicroLong_T1");
                        currentSlPrice = keltner.Lower[0] - (SlOffsetTicks * TickSize);
                        highestPriceSinceEntry = Close[0];
                        Print(Time[0] + " - [ENTRADA T1 LARGO] Pinch Squeeze. Pendiente: " + midlineSlopeVal.ToString("F1"));
                    }
                }
                // Disparo T1 Corto: Azul cruza blanca hacia abajo
                else if (CrossBelow(temaTrigger, keltner.Midline, 1))
                {
                    if (isPinchValid && midlineSlopeVal <= -MinSlopeTicks)
                    {
                        EnterShort("MicroShort_T1");
                        currentSlPrice = keltner.Upper[0] + (SlOffsetTicks * TickSize);
                        lowestPriceSinceEntry = Close[0];
                        Print(Time[0] + " - [ENTRADA T1 CORTO] Pinch Squeeze. Pendiente: " + midlineSlopeVal.ToString("F1"));
                    }
                }

                // === TIPO 2: TENDENCIA DESBOCADA (RUNAWAY BREAKOUT) ===
                // Disparo T2 Largo: Blanca cruza roja hacia arriba (Golden Cross micro/macro)
                if (CrossAbove(keltner.Midline, ema200, 1))
                {
                    if (Close[0] > keltner.Upper[0] && midlineSlopeVal >= MinSlopeTicks)
                    {
                        EnterLong("MicroLong_T2");
                        currentSlPrice = keltner.Upper[0] - (SlOffsetTicks * TickSize); // SL ajustadísimo
                        highestPriceSinceEntry = Close[0];
                        Print(Time[0] + " - [ENTRADA T2 LARGO] Runaway Breakout. Pendiente: " + midlineSlopeVal.ToString("F1"));
                    }
                }
                // Disparo T2 Corto: Blanca cruza roja hacia abajo
                else if (CrossBelow(keltner.Midline, ema200, 1))
                {
                    if (Close[0] < keltner.Lower[0] && midlineSlopeVal <= -MinSlopeTicks)
                    {
                        EnterShort("MicroShort_T2");
                        currentSlPrice = keltner.Lower[0] + (SlOffsetTicks * TickSize); // SL ajustadísimo
                        lowestPriceSinceEntry = Close[0];
                        Print(Time[0] + " - [ENTRADA T2 CORTO] Runaway Breakout. Pendiente: " + midlineSlopeVal.ToString("F1"));
                    }
                }
            }
        }

        private void ManageDynamicTrailingStop()
        {
            if (Position.MarketPosition == MarketPosition.Long)
            {
                double entryPrice = Position.AveragePrice;
                double profitTicks = (highestPriceSinceEntry - entryPrice) / TickSize;

                if (profitTicks >= BreakEvenTicks)
                {
                    double breakEvenPrice = entryPrice + (2 * TickSize); // Cubrir comisiones
                    double trailingPrice = highestPriceSinceEntry - (TrailingStepTicks * TickSize);
                    
                    double newSlPrice = Math.Max(breakEvenPrice, trailingPrice);

                    if (newSlPrice > currentSlPrice)
                    {
                        currentSlPrice = newSlPrice;
                        if (!isTrailingActive) 
                        {
                            Print(Time[0] + " - [TRAILING ACTIVADO] Moviendo a BreakEven.");
                            isTrailingActive = true;
                        }
                    }
                }

                ExitLongStopMarket(0, true, Position.Quantity, currentSlPrice, "SL_Trailing", "MicroLong_T1");
                ExitLongStopMarket(0, true, Position.Quantity, currentSlPrice, "SL_Trailing", "MicroLong_T2");
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double entryPrice = Position.AveragePrice;
                double profitTicks = (entryPrice - lowestPriceSinceEntry) / TickSize;

                if (profitTicks >= BreakEvenTicks)
                {
                    double breakEvenPrice = entryPrice - (2 * TickSize); 
                    double trailingPrice = lowestPriceSinceEntry + (TrailingStepTicks * TickSize);
                    
                    double newSlPrice = Math.Min(breakEvenPrice, trailingPrice);

                    if (newSlPrice < currentSlPrice)
                    {
                        currentSlPrice = newSlPrice;
                        if (!isTrailingActive) 
                        {
                            Print(Time[0] + " - [TRAILING ACTIVADO] Moviendo a BreakEven.");
                            isTrailingActive = true;
                        }
                    }
                }

                ExitShortStopMarket(0, true, Position.Quantity, currentSlPrice, "SL_Trailing", "MicroShort_T1");
                ExitShortStopMarket(0, true, Position.Quantity, currentSlPrice, "SL_Trailing", "MicroShort_T2");
            }
        }

        #region Interfaz UI y Botón de Pánico (WPF)
        private void CreateWPFControls()
        {
            chartGrid = new System.Windows.Controls.Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 10) };
            panicButton = new System.Windows.Controls.Button { Content = "MicroTrend: PAUSADO", Foreground = Brushes.White, Background = Brushes.Red, FontWeight = FontWeights.Bold, Padding = new Thickness(10, 5, 10, 5), BorderThickness = new Thickness(2), BorderBrush = Brushes.DarkRed };
            panicButton.Click += OnPanicButtonClick;
            chartGrid.Children.Add(panicButton);
            UserControlCollection.Add(chartGrid);
            ChartPanel.PreviewKeyDown += ChartPanel_PreviewKeyDown;

            if (isStrategyActive)
            {
                panicButton.Content = "MicroTrend: ACTIVO";
                panicButton.Background = Brushes.Green;
                panicButton.BorderBrush = Brushes.DarkGreen;
            }
        }

        private void OnPanicButtonClick(object sender, RoutedEventArgs e) { ToggleStrategyStatus(); }
        private void ChartPanel_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) { ToggleStrategyStatus(); e.Handled = true; } }

        private void ToggleStrategyStatus()
        {
            isStrategyActive = !isStrategyActive;
            if (panicButton != null)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    panicButton.Content = isStrategyActive ? "MicroTrend: ACTIVO" : "MicroTrend: PAUSADO";
                    panicButton.Background = isStrategyActive ? Brushes.Green : Brushes.Red;
                    panicButton.BorderBrush = isStrategyActive ? Brushes.DarkGreen : Brushes.DarkRed;
                    Print(Time[0] + (isStrategyActive ? " - Estrategia MicroTrend ACTIVADA." : " - Estrategia MicroTrend PAUSADA."));
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
        [Display(Name="Periodo EMA (Macro)", Order=1, GroupName="2. Indicadores")]
        public int EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo Keltner", Order=2, GroupName="2. Indicadores")]
        public int KeltnerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name="Multiplicador Keltner", Order=3, GroupName="2. Indicadores")]
        public double KeltnerMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo TEMA (Gatillo)", Order=4, GroupName="2. Indicadores")]
        public int TemaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Máx Distancia Roja-Blanca (T1)", Order=1, GroupName="3. Filtros de Entrada")]
        public int MaxPinchTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Barras Lookback Pendiente", Order=2, GroupName="3. Filtros de Entrada")]
        public int SlopeLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="Mínima Pendiente (Ticks)", Order=3, GroupName="3. Filtros de Entrada")]
        public double MinSlopeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name="Holgura Inicial SL (Ticks)", Order=1, GroupName="4. Gestión de Riesgo")]
        public int SlOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Ticks para BreakEven", Order=2, GroupName="4. Gestión de Riesgo")]
        public int BreakEvenTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Distancia del Trailing (Ticks)", Order=3, GroupName="4. Gestión de Riesgo")]
        public int TrailingStepTicks { get; set; }
        #endregion
    }
}
