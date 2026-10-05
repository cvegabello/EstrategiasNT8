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
    public class Tick610_RangeFisherES : Strategy
    {
        // === INDICADORES ===
        private EMA ema200;
        private TEMA temaTrigger;
        private KeltnerChannel keltner;

        // === VARIABLES DE CONTROL ===
        private double currentSlPrice = 0;
        private int entryBar = -1;
        private int countdownBars = 0;
        private bool armedLong = false;
        private bool armedShort = false;
        private double setupOuterBandPrice = 0;
        
        // INTERFAZ WPF
        private System.Windows.Controls.Button panicButton;
        private System.Windows.Controls.Grid chartGrid;
        private bool isStrategyActive = false; 

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Estrategia RangeFisher V2.0: Pescador de rebotes en mercados laterales (Midline Entry).";
                Name                                        = "Tick610_RangeFisherES";
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

                // Propiedades por defecto V2.0
                Version                 = "2.0";
                
                // Horarios
                StartTime               = 95000;   // 9:50 AM
                StopEntriesTime         = 154500;  // 3:45 PM
                ForceCloseTime          = 160000;  // 4:00 PM

                // Filtro Lateralidad
                RangeLookbackBars       = 40;      // Cuántas barras atrás mira para saber si la EMA está atrapada
                UseEmaBias              = false;   // Checkbox: Filtrar dirección según EMA vs Midline

                // Indicadores
                EmaPeriod               = 200;
                TemaPeriod              = 9;
                KeltnerPeriod           = 52;
                KeltnerMultiplier       = 3.5;
                TemaToleranceTicks      = 2;

                // Gestión de Riesgo (Reversión a la media)
                SlOffsetTicks           = 12;      // Holgura fija detrás de la banda para el Stop Loss
                TimeStopBars            = 14;      // Barras de paciencia antes de ajustar salidas a la línea media
                CountdownMaxBars        = 10;      // Maximo de barras esperando cruce de Midline tras la alerta
                SlopeLookbackBars       = 5;       // Cuántas barras atrás medir la pendiente de la Midline
                MaxSlopeTicks           = 4.0;     // Tolerancia máxima en ticks para considerar la Midline 'plana'
                MinCrossTicks           = 1;       // Penetración requerida (en ticks) para confirmar el cruce
            }
            else if (State == State.DataLoaded)
            {
                ema200 = EMA(EmaPeriod);
                temaTrigger = TEMA(TemaPeriod);
                keltner = KeltnerChannel(KeltnerMultiplier, KeltnerPeriod);

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
            if (CurrentBar < Math.Max(BarsRequiredToTrade, RangeLookbackBars)) return;

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
                    ExitLong("Cierre Fuera Horario", "FisherLong");
                    ExitShort("Cierre Fuera Horario", "FisherShort");
                    Print(Time[0] + " - Posición cerrada por seguridad (4:00 PM).");
                }
                return;
            }

            // 3. GESTIÓN DE SALIDAS (TP Dinámico y SL Fijo/Trailing)
            if (Position.MarketPosition == MarketPosition.Long)
            {
                int barsInTrade = CurrentBar - entryBar;
                if (barsInTrade >= TimeStopBars)
                {
                    ExitLong(Position.Quantity, "TimeStop", "FisherLong");
                    return;
                }

                ExitLongLimit(0, true, Position.Quantity, keltner.Upper[0], "TP_Dinamico", "FisherLong");
                ExitLongStopMarket(0, true, Position.Quantity, currentSlPrice, "SL_Fijo", "FisherLong");
                return; 
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                int barsInTrade = CurrentBar - entryBar;
                if (barsInTrade >= TimeStopBars)
                {
                    ExitShort(Position.Quantity, "TimeStop", "FisherShort");
                    return;
                }

                ExitShortLimit(0, true, Position.Quantity, keltner.Lower[0], "TP_Dinamico", "FisherShort");
                ExitShortStopMarket(0, true, Position.Quantity, currentSlPrice, "SL_Fijo", "FisherShort");
                return; 
            }

            // 4. LÓGICA DE ENTRADA (Mercado Plano - V2.0 Midline Entry)
            if (Position.MarketPosition == MarketPosition.Flat && isTradingTime)
            {
                entryBar = -1; // Reset del contador de trade
                
                // A) Filtro de Rango Estricto (Semáforo de Cruces)
                int crossUpCount = 0;
                int crossDownCount = 0;
                
                for (int i = 0; i < RangeLookbackBars; i++)
                {
                    if (Close[i + 1] <= ema200[i + 1] && Close[i] > ema200[i]) crossUpCount++;
                    if (Close[i + 1] >= ema200[i + 1] && Close[i] < ema200[i]) crossDownCount++;
                }
                bool isRanging = (crossUpCount >= 1) && (crossDownCount >= 1);

                // B) Sesgo Direccional (Gravedad)
                bool emaBelowMidline = ema200[0] < keltner.Midline[0];
                bool emaAboveMidline = ema200[0] > keltner.Midline[0];
                bool longBiasValid = !UseEmaBias || emaBelowMidline;
                bool shortBiasValid = !UseEmaBias || emaAboveMidline;

                // C) Evaluación de Alerta (Penetración + Gancho)
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

                bool isPeakShort = temaTrigger[2] < temaTrigger[1] && temaTrigger[0] < temaTrigger[1];
                bool isTroughLong = temaTrigger[2] > temaTrigger[1] && temaTrigger[0] > temaTrigger[1];

                // D) Estado Armado: Alertas
                if (isRanging && isPeakShort && touchedUpperExtreme && shortBiasValid && !armedShort)
                {
                    armedShort = true;
                    armedLong = false;
                    countdownBars = 0;
                    setupOuterBandPrice = keltner.Upper[0];
                    Print(Time[0] + " - [ALERTA CORTO] TEMA en gancho. Esperando cruce Midline (Max " + CountdownMaxBars + ").");
                }
                else if (isRanging && isTroughLong && touchedLowerExtreme && longBiasValid && !armedLong)
                {
                    armedLong = true;
                    armedShort = false;
                    countdownBars = 0;
                    setupOuterBandPrice = keltner.Lower[0];
                    Print(Time[0] + " - [ALERTA LARGO] TEMA en gancho. Esperando cruce Midline (Max " + CountdownMaxBars + ").");
                }

                // E) Gestión del Gatillo (Cuenta regresiva y Disparo)
                if (armedLong || armedShort)
                {
                    countdownBars++;
                    
                    if (countdownBars > CountdownMaxBars)
                    {
                        armedLong = false;
                        armedShort = false;
                        Print(Time[0] + " - [CANCELADO] Expiró el tiempo (" + countdownBars + " barras) sin cruzar Midline.");
                    }
                    else if (armedLong)
                    {
                        // Gatillo: TEMA azul cruza línea blanca hacia arriba (con penetración mínima)
                        double requiredCrossPrice = keltner.Midline[0] + (MinCrossTicks * TickSize);
                        if (temaTrigger[0] >= requiredCrossPrice && temaTrigger[1] <= keltner.Midline[1])
                        {
                            double slope = Math.Abs(keltner.Midline[0] - keltner.Midline[Math.Min(SlopeLookbackBars, CurrentBar)]) / TickSize;
                            
                            if (slope <= MaxSlopeTicks)
                            {
                                entryBar = CurrentBar;
                                EnterLong("FisherLong");
                                // STOP LOSS RIGIDO: Calculado desde el precio de entrada, no desde la banda
                                currentSlPrice = Close[0] - (SlOffsetTicks * TickSize);
                                armedLong = false;
                                Print(Time[0] + " - [DISPARO LARGO] Midline cruzada (" + MinCrossTicks + " ticks). Pendiente plana OK (" + slope.ToString("F1") + " ticks).");
                            }
                            else
                            {
                                armedLong = false;
                                Print(Time[0] + " - [CANCELADO LARGO] Midline cruzada pero pendiente peligrosa (" + slope.ToString("F1") + " ticks).");
                            }
                        }
                    }
                    else if (armedShort)
                    {
                        // Gatillo: TEMA azul cruza línea blanca hacia abajo (con penetración mínima)
                        double requiredCrossPrice = keltner.Midline[0] - (MinCrossTicks * TickSize);
                        if (temaTrigger[0] <= requiredCrossPrice && temaTrigger[1] >= keltner.Midline[1])
                        {
                            double slope = Math.Abs(keltner.Midline[0] - keltner.Midline[Math.Min(SlopeLookbackBars, CurrentBar)]) / TickSize;
                            
                            if (slope <= MaxSlopeTicks)
                            {
                                entryBar = CurrentBar;
                                EnterShort("FisherShort");
                                // STOP LOSS RIGIDO: Calculado desde el precio de entrada, no desde la banda
                                currentSlPrice = Close[0] + (SlOffsetTicks * TickSize);
                                armedShort = false;
                                Print(Time[0] + " - [DISPARO CORTO] Midline cruzada (" + MinCrossTicks + " ticks). Pendiente plana OK (" + slope.ToString("F1") + " ticks).");
                            }
                            else
                            {
                                armedShort = false;
                                Print(Time[0] + " - [CANCELADO CORTO] Midline cruzada pero pendiente peligrosa (" + slope.ToString("F1") + " ticks).");
                            }
                        }
                    }
                }
            }
        }

        #region Interfaz UI y Botón de Pánico (WPF)
        private void CreateWPFControls()
        {
            chartGrid = new System.Windows.Controls.Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 10) };
            panicButton = new System.Windows.Controls.Button { Content = "Fisher: PAUSADO", Foreground = Brushes.White, Background = Brushes.Red, FontWeight = FontWeights.Bold, Padding = new Thickness(10, 5, 10, 5), BorderThickness = new Thickness(2), BorderBrush = Brushes.DarkRed };
            panicButton.Click += OnPanicButtonClick;
            chartGrid.Children.Add(panicButton);
            UserControlCollection.Add(chartGrid);
            ChartPanel.PreviewKeyDown += ChartPanel_PreviewKeyDown;

            if (isStrategyActive)
            {
                panicButton.Content = "Fisher: ACTIVO";
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
                    panicButton.Content = isStrategyActive ? "Fisher: ACTIVO" : "Fisher: PAUSADO";
                    panicButton.Background = isStrategyActive ? Brushes.Green : Brushes.Red;
                    panicButton.BorderBrush = isStrategyActive ? Brushes.DarkGreen : Brushes.DarkRed;
                    Print(Time[0] + (isStrategyActive ? " - Estrategia Fisher ACTIVADA." : " - Estrategia Fisher PAUSADA."));
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
        [Display(Name="Cierre Forzoso", Order=3, GroupName="1. Horarios")]
        public int ForceCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Barras Lookback Rango", Description="Barras para evaluar cruces EMA 200", Order=1, GroupName="2. Filtro Lateralidad")]
        public int RangeLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Usar Sesgo EMA (Checkbox)", Description="Solo largos si EMA<Midline, cortos si EMA>Midline", Order=2, GroupName="2. Filtro Lateralidad")]
        public bool UseEmaBias { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo EMA", Order=1, GroupName="3. Indicadores")]
        public int EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo Keltner", Order=2, GroupName="3. Indicadores")]
        public int KeltnerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name="Multiplicador Keltner", Order=3, GroupName="3. Indicadores")]
        public double KeltnerMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo TEMA", Order=4, GroupName="3. Indicadores")]
        public int TemaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name="Tolerancia Gancho", Order=5, GroupName="3. Indicadores")]
        public int TemaToleranceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Holgura Stop Loss Ticks", Order=1, GroupName="4. Gestión de Riesgo Fija")]
        public int SlOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Barras Time-Stop (Paciencia)", Order=2, GroupName="4. Gestión de Riesgo Fija")]
        public int TimeStopBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Barras Máximas Espera (Gatillo V2)", Order=3, GroupName="4. Gestión de Riesgo Fija")]
        public int CountdownMaxBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Lookback Pendiente (Barras)", Order=4, GroupName="4. Gestión de Riesgo Fija")]
        public int SlopeLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="Max Pendiente Ticks (Plana)", Order=5, GroupName="4. Gestión de Riesgo Fija")]
        public double MaxSlopeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name="Penetración Cruce Midline (Ticks)", Order=6, GroupName="4. Gestión de Riesgo Fija")]
        public int MinCrossTicks { get; set; }
        #endregion
    }
}
