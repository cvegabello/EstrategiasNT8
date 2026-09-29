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
        // === VARIABLES DEL TRINQUETE (MÁQUINA DE ESTADOS) ===
        private enum RatchetState { Flat, Initial, Phase1, Phase2 }
        private RatchetState currentState = RatchetState.Flat;
        private double currentStopPrice = 0;

        // === INDICADORES ===
        private LinReg linReg;
        private KeltnerChannel keltner;
        private SMA volSma;
        private MACD macd;
        private TEMA temaTrigger;

        // === VARIABLES DE CONTROL (INTERFAZ WPF) ===
        private System.Windows.Controls.Button panicButton;
        private System.Windows.Controls.Grid chartGrid;
        private bool isStrategyActive = false; // Inicia pausada por defecto

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Estrategia Sniper de alta probabilidad para 610 Ticks. Gestión tipo Trinquete.";
                Name                                        = "Tick610_SniperRatchetES";
                Calculate                                   = Calculate.OnBarClose; // Ideal para gráficas de Ticks para optimización
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
                BarsRequiredToTrade                         = 100; // Necesitamos al menos 89 para el LinReg
                IsInstantiatedOnEachOptimizationIteration   = true;

                // Parámetros de Indicadores
                SlopePeriod             = 89;
                SlopeLookbackBars       = 20; // Barras atrás para medir la pendiente
                SlopeThresholdTicks     = 20; // Diferencia mínima en Ticks para validar la fuerza de la pendiente
                KeltnerPeriod           = 52;
                TemaPeriod              = 13; // TEMA como gatillo alisado
                VolSmaPeriod            = 20;
                VolMultiplier           = 1.5;
                
                // Parámetros de Gestión de Riesgo (Trinquete)
                SlTicks                 = 12; // Stop Loss Inicial detrás de la estructura
                Tp1Ticks                = 8;  // Hito 1: Precio alcanza +8 Ticks
                Tp1LockTicks            = 2;  // Aseguramos +2 Ticks
                Tp2Ticks                = 14; // Hito 2: Precio alcanza +14 Ticks
                Tp2LockTicks            = 8;  // Aseguramos +8 Ticks
                Version                 = "2.0";
            }
            else if (State == State.DataLoaded)
            {
                // Instanciar Indicadores solo una vez para eficiencia en Ticks
                linReg = LinReg(SlopePeriod);
                keltner = KeltnerChannel(1.5, KeltnerPeriod); // Multiplicador de banda estándar, usaremos solo la línea media
                volSma = SMA(VOL(), VolSmaPeriod);
                macd = MACD(12, 26, 9);
                temaTrigger = TEMA(TemaPeriod);

                // Agregar indicadores a la gráfica para visualización
                AddChartIndicator(linReg);
                AddChartIndicator(keltner);
                AddChartIndicator(temaTrigger);
            }
            else if (State == State.Historical)
            {
                // Iniciar la UI en el hilo correcto al cargar la gráfica
                if (UserControlCollection != null)
                {
                    Dispatcher.InvokeAsync((() =>
                    {
                        CreateWPFControls();
                    }));
                }
            }
            else if (State == State.Terminated)
            {
                // Limpiar la UI al remover la estrategia
                if (UserControlCollection != null)
                {
                    Dispatcher.InvokeAsync((() =>
                    {
                        DisposeWPFControls();
                    }));
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade)
                return;

            // 1. CONTROL DE LA INTERFAZ WPF Y ESTADO ACTIVO
            if (!isStrategyActive)
            {
                // Si la estrategia está pausada y tenemos posición, dejamos que el Stop/Target la gestione
                // pero evitamos nuevas evaluaciones de entrada.
                return; 
            }

            // 2. FILTRO HORARIO RTH ESTRICTO (10:00 AM - 1:00 PM EST)
            int timeNow = ToTime(Time[0]);
            bool isRTH = timeNow >= 100000 && timeNow < 130000;

            if (!isRTH)
            {
                // Si estamos fuera de horario y hay posición abierta, forzar el cierre
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    ExitLong("Cierre Fuera Horario", "SniperLong");
                    ExitShort("Cierre Fuera Horario", "SniperShort");
                    Print(Time[0] + " - Posición cerrada por seguridad horaria (Fuera de 10AM-1PM).");
                }
                return; // Ignorar la evaluación de nuevas entradas
            }

            // 3. GESTIÓN DE RIESGO: MÁQUINA DE ESTADOS (TRINQUETE / RATCHET)
            ManageRatchetStops();

            // 4. LÓGICA DE ENTRADA (SOLO SI ESTAMOS FLAT - SIN POSICIÓN)
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                // --- Variables de Condiciones ---
                
                // A) Pendiente de la Regresión Lineal calculada matemáticamente en Ticks
                double currentLinReg = linReg[0];
                double oldLinReg = linReg[SlopeLookbackBars];
                double linRegDiffTicks = (currentLinReg - oldLinReg) / TickSize; // Diferencia neta en Ticks
                
                bool strongUptrend = linRegDiffTicks > SlopeThresholdTicks;
                bool strongDowntrend = linRegDiffTicks < -SlopeThresholdTicks;

                // B) Retroceso y Cruce usando TEMA como "Precio Alisado"
                bool touchKeltnerMidLong = temaTrigger[1] <= keltner.Midline[1] && temaTrigger[0] > keltner.Midline[0];
                bool touchKeltnerMidShort = temaTrigger[1] >= keltner.Midline[1] && temaTrigger[0] < keltner.Midline[0];

                // C) Explosión de Volumen Institucional
                bool extremeVolume = Volume[0] > (volSma[0] * VolMultiplier);

                // D) Momento MACD a favor (Aceleración)
                bool macdAccelUp = macd.Diff[0] > macd.Diff[1];
                bool macdAccelDown = macd.Diff[0] < macd.Diff[1];

                // --- Log de Diagnóstico (Solo cuando hay toque Keltner) ---
                if (touchKeltnerMidLong || touchKeltnerMidShort)
                {
                    string dir = touchKeltnerMidLong ? "LONG" : "SHORT";
                    string msg = string.Format("{0} - [EVALUANDO {1}] Toque Keltner Detectado.\n", Time[0], dir);
                    
                    bool trendPass = dir == "LONG" ? strongUptrend : strongDowntrend;
                    string reqTrend = dir == "LONG" ? "> " + SlopeThresholdTicks : "< -" + SlopeThresholdTicks;
                    msg += string.Format("   - Tendencia (LinReg Diff): {0:F2} Ticks. Requisito ({1}): {2}\n", 
                        linRegDiffTicks, reqTrend, trendPass ? "PASÓ" : "FALLÓ");
                    
                    double volThreshold = volSma[0] * VolMultiplier;
                    msg += string.Format("   - Volumen: {0}. Requisito (> {1:F2}): {2}\n", 
                        Volume[0], volThreshold, extremeVolume ? "PASÓ" : "FALLÓ");

                    bool macdPass = dir == "LONG" ? macdAccelUp : macdAccelDown;
                    msg += string.Format("   - MACD Aceleración: Diff[0]={0:F4}, Diff[1]={1:F4}. Requisito a favor: {2}", 
                        macd.Diff[0], macd.Diff[1], macdPass ? "PASÓ" : "FALLÓ");

                    Print(msg);
                }

                // --- Ejecución de Entradas ---

                // COMPRAS (Long)
                if (strongUptrend && touchKeltnerMidLong && extremeVolume && macdAccelUp)
                {
                    EnterLong("SniperLong");
                    currentStopPrice = Close[0] - (SlTicks * TickSize); // Calcular precio exacto del Stop Loss inicial
                    currentState = RatchetState.Initial;
                    SetStopLoss("SniperLong", CalculationMode.Price, currentStopPrice, false);
                    Print(Time[0] + " - ENTRADA LONG. Pendiente: " + linRegDiffTicks.ToString("F2") + " Ticks.");
                }
                // VENTAS (Short)
                else if (strongDowntrend && touchKeltnerMidShort && extremeVolume && macdAccelDown)
                {
                    EnterShort("SniperShort");
                    currentStopPrice = Close[0] + (SlTicks * TickSize); // Calcular precio exacto del Stop Loss inicial
                    currentState = RatchetState.Initial;
                    SetStopLoss("SniperShort", CalculationMode.Price, currentStopPrice, false);
                    Print(Time[0] + " - ENTRADA SHORT. Pendiente: " + linRegDiffTicks.ToString("F2") + " Ticks.");
                }
            }
        }

        // --- FUNCIÓN DE GESTIÓN DEL TRINQUETE ---
        // Se encarga de mover el Stop Loss en bloques irrecuperables (como un trinquete físico)
        private void ManageRatchetStops()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                currentState = RatchetState.Flat;
                return;
            }

            double avgPrice = Position.AveragePrice;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                // Calcular cuántos Ticks a favor hemos alcanzado como máximo
                double maxFavTicks = (High[0] - avgPrice) / TickSize;

                // Fase 1: Llegamos al primer hito (Ej. 8 Ticks)
                if (currentState == RatchetState.Initial && maxFavTicks >= Tp1Ticks)
                {
                    currentState = RatchetState.Phase1;
                    currentStopPrice = avgPrice + (Tp1LockTicks * TickSize);
                    SetStopLoss("SniperLong", CalculationMode.Price, currentStopPrice, false);
                    Print(Time[0] + " - Trinquete Fase 1 LONG: Stop asegurado en +" + Tp1LockTicks + " Ticks.");
                }
                // Fase 2: Llegamos al segundo hito (Ej. 14 Ticks)
                else if (currentState == RatchetState.Phase1 && maxFavTicks >= Tp2Ticks)
                {
                    currentState = RatchetState.Phase2;
                    currentStopPrice = avgPrice + (Tp2LockTicks * TickSize);
                    SetStopLoss("SniperLong", CalculationMode.Price, currentStopPrice, false);
                    Print(Time[0] + " - Trinquete Fase 2 LONG: Stop asegurado en +" + Tp2LockTicks + " Ticks.");
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                // Calcular cuántos Ticks a favor hemos alcanzado como máximo
                double maxFavTicks = (avgPrice - Low[0]) / TickSize;

                // Fase 1: Llegamos al primer hito
                if (currentState == RatchetState.Initial && maxFavTicks >= Tp1Ticks)
                {
                    currentState = RatchetState.Phase1;
                    currentStopPrice = avgPrice - (Tp1LockTicks * TickSize);
                    SetStopLoss("SniperShort", CalculationMode.Price, currentStopPrice, false);
                    Print(Time[0] + " - Trinquete Fase 1 SHORT: Stop asegurado en +" + Tp1LockTicks + " Ticks.");
                }
                // Fase 2: Llegamos al segundo hito
                else if (currentState == RatchetState.Phase1 && maxFavTicks >= Tp2Ticks)
                {
                    currentState = RatchetState.Phase2;
                    currentStopPrice = avgPrice - (Tp2LockTicks * TickSize);
                    SetStopLoss("SniperShort", CalculationMode.Price, currentStopPrice, false);
                    Print(Time[0] + " - Trinquete Fase 2 SHORT: Stop asegurado en +" + Tp2LockTicks + " Ticks.");
                }
            }
        }

        #region Interfaz UI y Botón de Pánico (WPF)
        private void CreateWPFControls()
        {
            chartGrid = new System.Windows.Controls.Grid
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 10, 10)
            };

            panicButton = new System.Windows.Controls.Button
            {
                Content = "Sniper: PAUSADO",
                Foreground = Brushes.White,
                Background = Brushes.Red,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(10, 5, 10, 5),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.DarkRed
            };

            panicButton.Click += OnPanicButtonClick;
            chartGrid.Children.Add(panicButton);
            UserControlCollection.Add(chartGrid);

            // Escuchar el evento de teclado global en la gráfica
            ChartPanel.PreviewKeyDown += ChartPanel_PreviewKeyDown;
        }

        private void DisposeWPFControls()
        {
            if (panicButton != null)
                panicButton.Click -= OnPanicButtonClick;

            if (ChartPanel != null)
                ChartPanel.PreviewKeyDown -= ChartPanel_PreviewKeyDown;

            if (chartGrid != null && UserControlCollection.Contains(chartGrid))
                UserControlCollection.Remove(chartGrid);
        }

        private void OnPanicButtonClick(object sender, RoutedEventArgs e)
        {
            ToggleStrategyStatus();
            e.Handled = true; // Evitar que el click haga focus en otras cosas de la gráfica
        }

        private void ChartPanel_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Atajo de teclado: Ctrl + Espacio
            if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ToggleStrategyStatus();
                e.Handled = true;
            }
        }

        private void ToggleStrategyStatus()
        {
            isStrategyActive = !isStrategyActive;

            if (panicButton != null)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (isStrategyActive)
                    {
                        panicButton.Content = "Sniper: ACTIVO";
                        panicButton.Background = Brushes.Green;
                        panicButton.BorderBrush = Brushes.DarkGreen;
                        Print(Time[0] + " - Estrategia Sniper ACTIVADA manualmente.");
                    }
                    else
                    {
                        panicButton.Content = "Sniper: PAUSADO";
                        panicButton.Background = Brushes.Red;
                        panicButton.BorderBrush = Brushes.DarkRed;
                        Print(Time[0] + " - Estrategia Sniper PAUSADA manualmente.");
                    }
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
        [Display(Name="Periodo LinReg", Order=1, GroupName="1. Filtro Tendencia")]
        public int SlopePeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Barras Atrás (Slope)", Description="Para medir la pendiente", Order=2, GroupName="1. Filtro Tendencia")]
        public int SlopeLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Ticks de Pendiente Mínima", Description="Diferencia en ticks para validar la fuerza", Order=3, GroupName="1. Filtro Tendencia")]
        public int SlopeThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo Keltner", Order=1, GroupName="2. Estructura y Pullback")]
        public int KeltnerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo TEMA (Gatillo)", Description="TEMA actuando como precio alisado", Order=2, GroupName="2. Estructura y Pullback")]
        public int TemaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Periodo SMA Volumen", Order=1, GroupName="3. Volumen")]
        public int VolSmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name="Multiplicador Volumen", Description="Ej: 1.5 es 50% más que el promedio", Order=2, GroupName="3. Volumen")]
        public double VolMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Stop Loss Inicial (Ticks)", Order=1, GroupName="4. Riesgo (Trinquete)")]
        public int SlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Hito 1: Llegar a (Ticks)", Order=2, GroupName="4. Riesgo (Trinquete)")]
        public int Tp1Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Hito 1: Asegurar (Ticks)", Order=3, GroupName="4. Riesgo (Trinquete)")]
        public int Tp1LockTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Hito 2: Llegar a (Ticks)", Order=4, GroupName="4. Riesgo (Trinquete)")]
        public int Tp2Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Hito 2: Asegurar (Ticks)", Order=5, GroupName="4. Riesgo (Trinquete)")]
        public int Tp2LockTicks { get; set; }
        #endregion
    }
}
