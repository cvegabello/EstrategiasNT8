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

		// ==========================================
		// PARÁMETROS DE LA ESTRATEGIA (OPCIÓN B)
		// ==========================================
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="1. Periodo HMA (Gatillo)", Description="Media móvil rápida que sigue al precio", Order=1, GroupName="1. Lógica del Gatillo")]
		public int HMAPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="2. Periodo EMA (Ancla)", Description="Media móvil lenta que sirve como zona de rebote elástico", Order=2, GroupName="1. Lógica del Gatillo")]
		public int EMAPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="3. Inclinación Mínima EMA (Ticks)", Description="Exige que la EMA se haya movido X ticks en 5 barras para validar tendencia", Order=3, GroupName="1. Lógica del Gatillo")]
		public int SlopeMinTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="4. Take Profit (Ticks)", Description="Ganancia esperada en ticks", Order=1, GroupName="2. Gestión de Riesgo")]
		public int TakeProfitTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="5. Stop Loss (Ticks)", Description="Pérdida máxima en ticks", Order=2, GroupName="2. Gestión de Riesgo")]
		public int StopLossTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="6. Cantidad de Contratos", Order=3, GroupName="2. Gestión de Riesgo")]
		public int ContractQty { get; set; }


		// UI WPF
		private Button btnToggleTrading;
		private Grid myGrid;
		private bool isUIActive = false; // Estado del botón UI (Inicia en PAUSA por defecto)
		private bool startTrading = false;

		// Variables de Indicadores
		private HMA hma;
		private EMA ema;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Estrategia Scalper: Rebote de HMA contra EMA.";
				Name										= "Tick610_PrecioCrossover";
				
				// REGLA DE ORO: Cambiado a OnBarClose como solicitaste para mayor seguridad
				Calculate									= Calculate.OnBarClose; 
				
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
				
				// Valores por Defecto
				Version										= "1.0.2";
				RealTimeActivated 							= true;
				BarsRequiredToTrade							= 40; // Mayor al periodo de la EMA
				
				HMAPeriod 									= 9;
				EMAPeriod									= 34;
				SlopeMinTicks								= 2; // Por defecto pedimos 2 ticks de inclinación
				TakeProfitTicks								= 10;
				StopLossTicks								= 15;
				ContractQty									= 1;
			}
			else if (State == State.Historical)
			{
				if (ChartControl != null)
				{
					if (UserControlCollection.Contains(myGrid)) return;
					ChartControl.Dispatcher.InvokeAsync(() => { InitWPF(); });
				}
				else
				{
					isUIActive = true;
					startTrading = true;
				}
			}
			else if (State == State.Terminated)
			{
				if (ChartControl != null)
				{
					ChartControl.Dispatcher.InvokeAsync(() => { DisposeWPF(); });
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
				// Instanciamos los indicadores para poder leer sus valores
				hma = HMA(HMAPeriod);
				ema = EMA(EMAPeriod);
			}
		}

		protected override void OnBarUpdate()
		{
			// BLOQUEOS DE SEGURIDAD (WPF, RealTime, y Barras Mínimas)
			if (!isUIActive || !startTrading || CurrentBar < BarsRequiredToTrade) return;

			// FILTRO HORARIO (Regla de oro: RTH 09:30 AM - 04:00 PM EST)
			int currentTime = ToTime(Time[0]);
			if (currentTime < 93000 || currentTime >= 160000) return;

			// ==========================================
			// LÓGICA DE ESTRATEGIA: SCALPER DE RETROCESO (OPCIÓN B)
			// ==========================================

			// Solo buscamos entradas si no estamos ya en una posición activa
			if (Position.MarketPosition == MarketPosition.Flat)
			{
				// ---------------------------------------------------------
				// 1. CONDICIÓN PARA LARGOS (COMPRAS)
				// ---------------------------------------------------------
				// Verificamos que el "Río" (EMA) fluya hacia arriba con FUERZA. 
				// La EMA actual debe ser mayor a la de hace 5 barras, por un mínimo de 'SlopeMinTicks'.
				bool isUptrend = ema[0] >= ema[5] + (SlopeMinTicks * TickSize);
				
				// Verificamos si nuestro "Delfín" (HMA) acaba de romper la superficie del río hacia arriba.
				bool crossUp = CrossAbove(hma, ema, 1);

				if (isUptrend && crossUp)
				{
					SetStopLoss("Rebote Largo", CalculationMode.Ticks, StopLossTicks, false);
					SetProfitTarget("Rebote Largo", CalculationMode.Ticks, TakeProfitTicks);
					
					EnterLong(ContractQty, "Rebote Largo");
					Print($"{Time[0]} - [ENTRADA LARGO] Rebote confirmado (Pendiente OK). HMA cruzó EMA hacia arriba.");
				}

				// ---------------------------------------------------------
				// 2. CONDICIÓN PARA CORTOS (VENTAS)
				// ---------------------------------------------------------
				// Verificamos que el "Río" fluya hacia abajo con FUERZA.
				bool isDowntrend = ema[0] <= ema[5] - (SlopeMinTicks * TickSize);
				
				// Verificamos si el HMA acaba de cruzar la superficie hacia abajo.
				bool crossDown = CrossBelow(hma, ema, 1);

				if (isDowntrend && crossDown)
				{
					SetStopLoss("Rebote Corto", CalculationMode.Ticks, StopLossTicks, false);
					SetProfitTarget("Rebote Corto", CalculationMode.Ticks, TakeProfitTicks);
					
					EnterShort(ContractQty, "Rebote Corto");
					Print($"{Time[0]} - [ENTRADA CORTO] Rebote bajista confirmado (Pendiente OK). HMA cruzó EMA hacia abajo.");
				}
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
				Margin = new Thickness(0, 30, 10, 0)
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
			if (btnToggleTrading != null) btnToggleTrading.Click -= OnButtonClick;
			if (ChartPanel != null) ChartPanel.PreviewKeyDown -= Chart_PreviewKeyDown;
			if (myGrid != null)
			{
				UserControlCollection.Remove(myGrid);
				myGrid = null;
			}
		}

		private void OnButtonClick(object sender, RoutedEventArgs e) { ToggleTradingState(); }

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
				if (Position.MarketPosition != MarketPosition.Flat)
				{
					ExitLong();
					ExitShort();
					Print(string.Format("{0} - [BOTÓN DE PÁNICO] Activado. Posiciones cerradas.", Time[0].ToString("HH:mm:ss")));
				}
			}
			ForceRefresh();
		}
		#endregion
	}
}
