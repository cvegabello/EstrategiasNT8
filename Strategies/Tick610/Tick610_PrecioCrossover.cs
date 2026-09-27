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
using System.Windows.Controls;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class Tick610_PrecioCrossover : Strategy
	{
		[NinjaScriptProperty]
		[Display(Name="Versión", Description="Versión actual de la estrategia", Order=0, GroupName="0. Información")]
		[ReadOnly(true)]
		public string Version { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Activar RealTime", Description="Inicia operativa solo al conectar en tiempo real", Order=1, GroupName="0. Información")]
		public bool RealTimeActivated { get; set; }

		// UI WPF
		private Button btnToggleTrading;
		private Grid myGrid;
		
		// Estado del botón UI (Regla de Oro: Inicia en PAUSA por defecto)
		private bool isUIActive = false; 
		
		private bool startTrading = false;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Estrategia Base: Precio Crossover.";
				Name										= "Tick610_PrecioCrossover";
				Calculate									= Calculate.OnEachTick; // ARQUITECTURA HÍBRIDA (ALTA VELOCIDAD)
				EntriesPerDirection							= 1;
				EntryHandling								= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy				= true;
				ExitOnSessionCloseSeconds					= 30; 
				IsFillLimitOnTouch							= false;
				MaximumBarsLookBack							= MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution							= OrderFillResolution.Standard;
				Slippage									= 0;
				StartBehavior								= StartBehavior.WaitUntilFlat;
				TimeInForce									= TimeInForce.Gtc;
				TraceOrders									= false; 
				RealtimeErrorHandling						= RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling							= StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade							= 20;

				Version										= "1.0.0";
				RealTimeActivated 							= true;
			}
			else if (State == State.Historical)
			{
				if (ChartControl != null)
				{
					if (UserControlCollection.Contains(myGrid)) return;
					
					ChartControl.Dispatcher.InvokeAsync(() => {
						InitWPF();
					});
				}
				else
				{
					// MODO STRATEGY ANALYZER (No hay gráfico, así que forzamos la activación)
					isUIActive = true;
					startTrading = true;
				}
			}
			else if (State == State.Terminated)
			{
				if (ChartControl != null)
				{
					ChartControl.Dispatcher.InvokeAsync(() => {
						DisposeWPF();
					});
				}
			}
			else if (State == State.Realtime)
			{
				if (RealTimeActivated)
				{
					startTrading = true;
					Print($"{Time[0]} - ** RealTime activado**");
				}
			}
			else if (State == State.DataLoaded)
			{				
				// Inicialización de indicadores y objetos (vacío por ahora)
			}
		}

		protected override void OnBarUpdate()
		{
			// BLOQUEO MAESTRO DEL BOTÓN UI (Gestión manual)
			if (!isUIActive) return;
			
			// Esperar a que inicie el trading en RealTime si está configurado
			if (!startTrading) return;
			
			// Asegurar que tenemos suficientes barras para procesar
			if (CurrentBar < BarsRequiredToTrade) return;

			// FILTRO HORARIO (Regla de oro: RTH 09:30 AM - 04:00 PM EST)
			int currentTime = ToTime(Time[0]);
			if (currentTime < 93000 || currentTime >= 160000) return;

			// ==========================================
			// LÓGICA DE ESTRATEGIA (LIENZO EN BLANCO)
			// ==========================================

			if (IsFirstTickOfBar)
			{
				// Lógica de baja velocidad (se ejecuta una vez por barra completada)
			}
			else
			{
				// Lógica de alta velocidad (se ejecuta tick a tick)
			}
		}

		#region WPF UI Methods
		private void InitWPF()
		{
			if (myGrid != null) return;

			myGrid = new Grid
			{
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(0, 30, 10, 0) // Debajo de las barras de herramientas
			};

			btnToggleTrading = new Button
			{
				Content = "PÁNICO (PAUSA)",
				Background = Brushes.Red,
				Foreground = Brushes.White,
				FontWeight = FontWeights.Bold,
				FontSize = 14,
				Padding = new Thickness(10, 5, 10, 5),
				BorderBrush = Brushes.DarkRed,
				BorderThickness = new Thickness(2),
				Cursor = Cursors.Hand
			};

			btnToggleTrading.Click += OnButtonClick;
			myGrid.Children.Add(btnToggleTrading);

			if (ChartControl != null && ChartPanel != null)
			{
				ChartPanel.PreviewKeyDown += Chart_PreviewKeyDown;
			}

			UserControlCollection.Add(myGrid);
		}

		private void DisposeWPF()
		{
			if (btnToggleTrading != null)
			{
				btnToggleTrading.Click -= OnButtonClick;
			}
			if (ChartPanel != null)
			{
				ChartPanel.PreviewKeyDown -= Chart_PreviewKeyDown;
			}
			if (myGrid != null)
			{
				UserControlCollection.Remove(myGrid);
				myGrid = null;
			}
		}

		private void OnButtonClick(object sender, RoutedEventArgs e)
		{
			ToggleTradingState();
		}

		private void Chart_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
			{
				ToggleTradingState();
				e.Handled = true;
			}
		}

		private void ToggleTradingState()
		{
			isUIActive = !isUIActive;
			if (isUIActive)
			{
				btnToggleTrading.Content = "ACTIVO";
				btnToggleTrading.Background = Brushes.LimeGreen;
				btnToggleTrading.BorderBrush = Brushes.DarkGreen;
			}
			else
			{
				btnToggleTrading.Content = "PÁNICO (PAUSA)";
				btnToggleTrading.Background = Brushes.Red;
				btnToggleTrading.BorderBrush = Brushes.DarkRed;
				
				// Cierre de emergencia OBLIGATORIO de todas las posiciones
				if (Position.MarketPosition != MarketPosition.Flat)
				{
					ExitLong();
					ExitShort();
					Print(string.Format("{0} - [BOTÓN DE PÁNICO] Activado. Todas las posiciones cerradas a mercado.", Time[0].ToString("HH:mm:ss")));
				}
			}
			
			// Forzamos actualización visual de la gráfica
			ForceRefresh();
		}
		#endregion
	}
}
