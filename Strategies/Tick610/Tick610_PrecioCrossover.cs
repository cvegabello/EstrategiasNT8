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
		// PARÁMETROS DE LA ESTRATEGIA
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
		[Range(1, int.MaxValue)]
		[Display(Name="3. Periodo EMA Macro (Filtro)", Description="EMA gigante para definir tendencia principal", Order=3, GroupName="1. Lógica del Gatillo")]
		public int MacroEMAPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="4. Barra Inicial (Slope Start)", Description="0 = Evalúa desde la barra actual. 1 = Desde la barra anterior (evita ruido)", Order=4, GroupName="1. Lógica del Gatillo")]
		public int SlopeStartBar { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="5. Barras a Evaluar (Lookback)", Description="Cuántas barras atrás mirar para calcular la pendiente", Order=5, GroupName="1. Lógica del Gatillo")]
		public int TrendLookbackBars { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name="6. Inclinación Mínima (Ticks, ej: 0.5)", Description="Pendiente mínima exigida usando decimales", Order=6, GroupName="1. Lógica del Gatillo")]
		public double SlopeMinTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="7. Take Profit (Ticks)", Description="Ganancia esperada en ticks", Order=1, GroupName="2. Gestión de Riesgo")]
		public int TakeProfitTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="8. Stop Loss (Ticks)", Description="Pérdida máxima en ticks", Order=2, GroupName="2. Gestión de Riesgo")]
		public int StopLossTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="9. Cantidad de Contratos", Order=3, GroupName="2. Gestión de Riesgo")]
		public int ContractQty { get; set; }

		// UI WPF
		private Button btnToggleTrading;
		private Grid myGrid;
		private bool isUIActive = false; // Inicia en PAUSA por defecto
		private bool startTrading = false;

		// Variables de Indicadores
		private HMA hma;
		private EMA ema;
		private EMA macroEma;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Estrategia Scalper: HMA vs EMA + Filtro Macro 200.";
				Name										= "Tick610_PrecioCrossover";
				
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
				
				Version										= "1.0.6";
				RealTimeActivated 							= true;
				BarsRequiredToTrade							= 200; 
				
				HMAPeriod 									= 9;
				EMAPeriod									= 34;
				MacroEMAPeriod								= 200; 
				SlopeStartBar								= 1; // Evaluamos desde 1 barra atrás por defecto
				TrendLookbackBars							= 15;
				SlopeMinTicks								= 0.5;
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
				hma = HMA(HMAPeriod);
				ema = EMA(EMAPeriod);
				macroEma = EMA(MacroEMAPeriod);
			}
		}

		protected override void OnBarUpdate()
		{
			// BLOQUEOS DE SEGURIDAD
			if (!isUIActive || !startTrading || CurrentBar < Math.Max(BarsRequiredToTrade, SlopeStartBar + TrendLookbackBars + 1)) return;

			// FILTRO HORARIO
			int currentTime = ToTime(Time[0]);
			if (currentTime < 93000 || currentTime >= 160000) return;

			// ==========================================
			// LÓGICA DE ESTRATEGIA: SCALPER DE RETROCESO
			// ==========================================

			if (Position.MarketPosition == MarketPosition.Flat)
			{
				// ---------------------------------------------------------
				// 1. CONDICIÓN PARA LARGOS (COMPRAS)
				// ---------------------------------------------------------
				bool crossUp = CrossAbove(hma, ema, 1);

				if (crossUp)
				{
					// Calculamos desde 'SlopeStartBar' hacia atrás
					double pendienteTicks = (ema[SlopeStartBar] - ema[SlopeStartBar + TrendLookbackBars]) / TickSize;
					bool isSlopeOK = pendienteTicks >= SlopeMinTicks;
					bool isMacroUptrend = Close[0] > macroEma[0]; 

					if (isSlopeOK && isMacroUptrend)
					{
						SetStopLoss("Rebote Largo", CalculationMode.Ticks, StopLossTicks, false);
						SetProfitTarget("Rebote Largo", CalculationMode.Ticks, TakeProfitTicks);
						EnterLong(ContractQty, "Rebote Largo");
						Print($"{Time[0]} - [ENTRADA LARGO] Rebote confirmado. Pendiente (desde barra [{SlopeStartBar}]): {Math.Round(pendienteTicks, 2)} Ticks. Macro: OK.");
					}
					else if (!isMacroUptrend)
					{
						Print($"{Time[0]} - [CRUCE LARGO IGNORADO] Filtro Macro: Precio por debajo de la EMA {MacroEMAPeriod}.");
					}
					else if (!isSlopeOK)
					{
						Print($"{Time[0]} - [CRUCE LARGO IGNORADO] Pendiente débil. Actual (desde barra [{SlopeStartBar}]): {Math.Round(pendienteTicks, 2)} Ticks. Requerido: {SlopeMinTicks}.");
					}
				}

				// ---------------------------------------------------------
				// 2. CONDICIÓN PARA CORTOS (VENTAS)
				// ---------------------------------------------------------
				bool crossDown = CrossBelow(hma, ema, 1);

				if (crossDown)
				{
					// Calculamos desde 'SlopeStartBar' hacia atrás
					double pendienteCaidaTicks = (ema[SlopeStartBar + TrendLookbackBars] - ema[SlopeStartBar]) / TickSize;
					bool isSlopeOK = pendienteCaidaTicks >= SlopeMinTicks;
					bool isMacroDowntrend = Close[0] < macroEma[0]; 

					if (isSlopeOK && isMacroDowntrend)
					{
						SetStopLoss("Rebote Corto", CalculationMode.Ticks, StopLossTicks, false);
						SetProfitTarget("Rebote Corto", CalculationMode.Ticks, TakeProfitTicks);
						EnterShort(ContractQty, "Rebote Corto");
						Print($"{Time[0]} - [ENTRADA CORTO] Rebote confirmado. Pendiente (desde barra [{SlopeStartBar}]): {Math.Round(pendienteCaidaTicks, 2)} Ticks. Macro: OK.");
					}
					else if (!isMacroDowntrend)
					{
						Print($"{Time[0]} - [CRUCE CORTO IGNORADO] Filtro Macro: Precio por encima de la EMA {MacroEMAPeriod}.");
					}
					else if (!isSlopeOK)
					{
						Print($"{Time[0]} - [CRUCE CORTO IGNORADO] Pendiente débil. Caída (desde barra [{SlopeStartBar}]): {Math.Round(pendienteCaidaTicks, 2)} Ticks. Requerido: {SlopeMinTicks}.");
					}
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
