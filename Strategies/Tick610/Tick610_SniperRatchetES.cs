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

        private enum TrailingState { None, Phase1_OuterBand, Phase2_Midline }
        private TrailingState currentTrailingState = TrailingState.None;

        // === INDICADORES ===
        private EMA ema200;
        private TEMA temaTrigger;
        private KeltnerChannel keltner;
        private MACD macd;

        // === VARIABLES DE CONTROL (INTERFAZ WPF) ===
        private System.Windows.Controls.Button panicButton;
        private System.Windows.Controls.Grid chartGrid;
        private bool isStrategyActive = false; // Inicia pausada por defecto

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Estrategia Sniper V2.1: Setup de rebote TEMA en extremos Keltner y SL Dinámico en fases.";
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
                BarsRequiredToTrade                         = 200; // Necesitamos al menos 200 para la EMA 200
                IsInstantiatedOnEachOptimizationIteration   = true;

                // Propiedades por defecto
                Version                 = "2.2";
                
                EmaPeriod               = 200;
                TemaPeriod              = 9;
                KeltnerPeriod           = 52;
                KeltnerMultiplier       = 3.5;
                CountdownBars           = 10;
                
                UseMacdFilter           = false;
                MacdFast                = 8;
                MacdSlow                = 17;
                MacdSmooth              = 9;

                SlOffsetTicks           = 1; // 1 tick "afuerita" del canal
                TemaToleranceTicks      = 2; // Distancia máxima para considerar que el TEMA "tocó" la banda
            }
            else if (State == State.DataLoaded)
            {
                // Instanciar Indicadores (Sin AddChartIndicator por solicitud del usuario)
                ema200 = EMA(EmaPeriod);
                temaTrigger = TEMA(TemaPeriod);
                keltner = KeltnerChannel(KeltnerMultiplier, KeltnerPeriod);
                macd = MACD(MacdFast, MacdSlow, MacdSmooth);
            }
            else if (State == State.Historical)
            {
                if (UserControlCollection != null)
                {
                    Dispatcher.InvokeAsync((() => { CreateWPFControls(); }));
                }
            }
            else if (State == State.Terminated)
            {
                if (UserControlCollection != null)
                {
                    Dispatcher.InvokeAsync((() => { DisposeWPFControls(); }));
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade) return;

            // 1. CONTROL DE LA INTERFAZ WPF
            if (!isStrategyActive) return; // Si está pausada, no evalúa ni gestiona.

            // 2. FILTRO HORARIO RTH EXTENDIDO (10:00 AM - 4:00 PM EST)
            int timeNow = ToTime(Time[0]);
            bool isRTH = timeNow >= 100000 && timeNow < 160000;

            if (!isRTH)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    ExitLong("Cierre Fuera Horario", "SniperLong");
                    ExitShort("Cierre Fuera Horario", "SniperShort");
                    Print(Time[0] + " - Posición cerrada por seguridad horaria (Fuera de 10AM-4PM).");
                }
                currentSetup = SetupType.None; // Resetear setups pendientes
                return;
            }

            // 3. GESTIÓN DE RIESGO: SL DINÁMICO EN FASES
            ManageDynamicTrailingStop();

            // 4. LÓGICA DE ALERTA (SETUP) Y DISPARO (TRIGGER)
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                // A) Verificar Filtro Macro (EMA 200) de las últimas N barras
                bool emaShortValid = true;
                bool emaLongValid = true;
                for (int i = 0; i <= CountdownBars; i++)
                {
                    if (High[i] >= ema200[i]) emaShortValid = false; // Todo debe estar debajo para cortos
                    if (Low[i] <= ema200[i]) emaLongValid = false;   // Todo debe estar arriba para largos
                }

                // B) Modelo Híbrido: Precio o TEMA en Extremo (últimas 3 barras) + Gancho/Cruce de Retorno
                bool touchedUpperExtreme = false;
                bool touchedLowerExtreme = false;
                double upperBandTolerance = keltner.Upper[1] - (TemaToleranceTicks * TickSize);
                double lowerBandTolerance = keltner.Lower[1] + (TemaToleranceTicks * TickSize);

                for (int i = 1; i <= 3; i++)
                {
                    if (High[i] >= upperBandTolerance || temaTrigger[i] >= upperBandTolerance) touchedUpperExtreme = true;
                    if (Low[i] <= lowerBandTolerance || temaTrigger[i] <= lowerBandTolerance) touchedLowerExtreme = true;
                }

                // Setup Corto: Gancho (Pico) o Cruce de Retorno bajista
                bool isPeakShort = temaTrigger[2] < temaTrigger[1] && temaTrigger[0] < temaTrigger[1];
                bool isCrossBackShort = temaTrigger[1] >= upperBandTolerance && temaTrigger[0] < upperBandTolerance && temaTrigger[0] < temaTrigger[1];
                
                if ((isPeakShort && touchedUpperExtreme) || isCrossBackShort)
                {
                    if (!emaShortValid) 
                        Print(Time[0] + " - [FILTRO MACRO] Gancho bajista ignorado. El precio cruzó la EMA 200 en las últimas " + CountdownBars + " barras.");
                    else
                    {
                        if (currentSetup == SetupType.Short) Print(Time[0] + " - [RESETEO CORTO] Nuevo gancho bajista. Reloj reiniciado a 0.");
                        else Print(Time[0] + " - [ALERTA CORTO] Zona Extrema + Gancho TEMA. Iniciando reloj de " + CountdownBars + " barras.");
                        
                        currentSetup = SetupType.Short;
                        setupBarCounter = 0;
                    }
                }

                // Setup Largo: Gancho (Valle) o Cruce de Retorno alcista
                bool isTroughLong = temaTrigger[2] > temaTrigger[1] && temaTrigger[0] > temaTrigger[1];
                bool isCrossBackLong = temaTrigger[1] <= lowerBandTolerance && temaTrigger[0] > lowerBandTolerance && temaTrigger[0] > temaTrigger[1];
                
                if ((isTroughLong && touchedLowerExtreme) || isCrossBackLong)
                {
                    if (!emaLongValid) 
                        Print(Time[0] + " - [FILTRO MACRO] Gancho alcista ignorado. El precio cruzó la EMA 200 en las últimas " + CountdownBars + " barras.");
                    else
                    {
                        if (currentSetup == SetupType.Long) Print(Time[0] + " - [RESETEO LARGO] Nuevo gancho alcista. Reloj reiniciado a 0.");
                        else Print(Time[0] + " - [ALERTA LARGO] Zona Extrema + Gancho TEMA. Iniciando reloj de " + CountdownBars + " barras.");
                        
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
                        Print(Time[0] + " - [SETUP CANCELADO] Pasaron " + CountdownBars + " barras sin cruzar la Línea Media.");
                        currentSetup = SetupType.None;
                    }
                    else
                    {
                        // MACD Opcional
                        bool macdValidLong = !UseMacdFilter || (macd.Diff[0] > macd.Diff[1]);
                        bool macdValidShort = !UseMacdFilter || (macd.Diff[0] < macd.Diff[1]);

                        // Disparo Largo: TEMA cruza Línea Media hacia arriba
                        if (currentSetup == SetupType.Long && CrossAbove(temaTrigger, keltner.Midline, 1) && macdValidLong)
                        {
                            EnterLong("SniperLong");
                            currentTrailingState = TrailingState.Phase1_OuterBand;
                            currentSetup = SetupType.None;
                            
                            // Colocar Stop Loss Inicial inmediatamente
                            double slPrice = keltner.Lower[0] - (SlOffsetTicks * TickSize);
                            SetStopLoss("SniperLong", CalculationMode.Price, slPrice, false);
                            Print(Time[0] + " - [DISPARO LARGO] TEMA cruzó Midline. SL Inicial en Banda Inferior: " + slPrice);
                        }
                        // Disparo Corto: TEMA cruza Línea Media hacia abajo
                        else if (currentSetup == SetupType.Short && CrossBelow(temaTrigger, keltner.Midline, 1) && macdValidShort)
                        {
                            EnterShort("SniperShort");
                            currentTrailingState = TrailingState.Phase1_OuterBand;
                            currentSetup = SetupType.None;

                            // Colocar Stop Loss Inicial inmediatamente
                            double slPrice = keltner.Upper[0] + (SlOffsetTicks * TickSize);
                            SetStopLoss("SniperShort", CalculationMode.Price, slPrice, false);
                            Print(Time[0] + " - [DISPARO CORTO] TEMA cruzó Midline. SL Inicial en Banda Superior: " + slPrice);
                        }
                    }
                }
            }
            else
            {
                // Si tenemos posición, apagamos cualquier setup pendiente por si acaso.
                currentSetup = SetupType.None;
            }
        }

        // --- GESTIÓN DEL STOP LOSS DINÁMICO ---
        private void ManageDynamicTrailingStop()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                currentTrailingState = TrailingState.None;
                return;
            }

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (currentTrailingState == TrailingState.Phase1_OuterBand)
                {
                    // SL anclado a la Banda Inferior
                    double slPrice = keltner.Lower[0] - (SlOffsetTicks * TickSize);
                    SetStopLoss("SniperLong", CalculationMode.Price, slPrice, false);

                    // Verificar transición a Fase 2 (Precio toca Banda Superior)
                    if (High[0] >= keltner.Upper[0])
                    {
                        currentTrailingState = TrailingState.Phase2_Midline;
                        Print(Time[0] + " - [Fase 2 LARGO] El precio tocó la Banda Superior. El SL salta a perseguir la Línea Media.");
                    }
                }
                else if (currentTrailingState == TrailingState.Phase2_Midline)
                {
                    // SL anclado a la Línea Media
                    double slPrice = keltner.Midline[0] - (SlOffsetTicks * TickSize);
                    SetStopLoss("SniperLong", CalculationMode.Price, slPrice, false);
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (currentTrailingState == TrailingState.Phase1_OuterBand)
                {
                    // SL anclado a la Banda Superior
                    double slPrice = keltner.Upper[0] + (SlOffsetTicks * TickSize);
                    SetStopLoss("SniperShort", CalculationMode.Price, slPrice, false);

                    // Verificar transición a Fase 2 (Precio toca Banda Inferior)
                    if (Low[0] <= keltner.Lower[0])
                    {
                        currentTrailingState = TrailingState.Phase2_Midline;
                        Print(Time[0] + " - [Fase 2 CORTO] El precio tocó la Banda Inferior. El SL salta a perseguir la Línea Media.");
                    }
                }
                else if (currentTrailingState == TrailingState.Phase2_Midline)
                {
                    // SL anclado a la Línea Media
                    double slPrice = keltner.Midline[0] + (SlOffsetTicks * TickSize);
                    SetStopLoss("SniperShort", CalculationMode.Price, slPrice, false);
                }
            }
        }

        #region Interfaz UI y Botón de Pánico (WPF)
        private void CreateWPFControls()
        {
            chartGrid = new System.Windows.Controls.Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 10) };
            panicButton = new System.Windows.Controls.Button { Content = "Sniper: PAUSADO", Foreground = Brushes.White, Background = Brushes.Red, FontWeight = FontWeights.Bold, Padding = new Thickness(10, 5, 10, 5), BorderThickness = new Thickness(2), BorderBrush = Brushes.DarkRed };
            panicButton.Click += OnPanicButtonClick;
            chartGrid.Children.Add(panicButton);
            UserControlCollection.Add(chartGrid);
            ChartPanel.PreviewKeyDown += ChartPanel_PreviewKeyDown;
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

        #region Propiedades Expuestas en NT8
        [NinjaScriptProperty]
        [Display(Name="Versión", Description="Versión actual de la estrategia", Order=0, GroupName="0. Información")]
        [ReadOnly(true)]
        public string Version { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo EMA (Maestro)", Order=1, GroupName="1. Filtro Macro")]
        public int EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Usar MACD Opcional", Description="¿Validar que el MACD esté a favor en el cruce?", Order=2, GroupName="1. Filtro Macro")]
        public bool UseMacdFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo Keltner", Order=1, GroupName="2. Estructura Keltner")]
        public int KeltnerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name="Multiplicador Keltner", Order=2, GroupName="2. Estructura Keltner")]
        public double KeltnerMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo TEMA (Gatillo)", Order=1, GroupName="3. Lógica de Disparo")]
        public int TemaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Barras Máximas (Conteo)", Description="Máximo de barras esperando cruce tras alerta", Order=2, GroupName="3. Lógica de Disparo")]
        public int CountdownBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name="Tolerancia Gancho (Ticks)", Description="Distancia máx a la banda para considerar rebote", Order=3, GroupName="3. Lógica de Disparo")]
        public int TemaToleranceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(-100, int.MaxValue)]
        [Display(Name="Offset SL (Ticks)", Description="Ticks para poner SL 'afuerita' (positivos) o 'adentrico' (negativos)", Order=1, GroupName="4. Gestión de Riesgo Dinámica")]
        public int SlOffsetTicks { get; set; }

        // --- Parámetros MACD ocultos si no se usan, pero requeridos para instanciar ---
        [Browsable(false)] public int MacdFast { get; set; }
        [Browsable(false)] public int MacdSlow { get; set; }
        [Browsable(false)] public int MacdSmooth { get; set; }
        #endregion
    }
}
