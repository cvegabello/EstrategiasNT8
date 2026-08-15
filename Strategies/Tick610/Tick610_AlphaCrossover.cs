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
using System.Windows.Media;
using System.Windows.Input;
using System.Windows;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class Tick610_AlphaCrossover : Strategy
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
		private bool isUIActive = false; // Estado del botón UI

		// Indicadores
		private SMA smaFast;
		private SMA smaSlow;
		private ADX adx;
		private KeltnerChannel kc;
		private HMA hma;
		
		private bool startTrading = false;

		// Variables para Delay de Entrada
		private int crossoverDirection = 0; // 1 = Largo, -1 = Corto, 0 = Ninguno
		private int crossoverBar = -1;

		// ==========================================
		// VARIABLES DE ESTADO: SCALPER (Cazador)
		// ==========================================
		private bool isScalperAlive = false;
		private int maxProfitTicks_Scalper = 0;
		private bool phase2Triggered_Scalper = false;
		private bool phase3Triggered_Scalper = false;
		private int slKeltnerMode_Scalper = 0; // 1 = Upper, 2 = Midline, -1 = Lower
		private double activeSlPrice_Scalper = 0.0;

		// ==========================================
		// VARIABLES DE ESTADO: RUNNER (Macro)
		// ==========================================
		private bool isRunnerAlive = false;
		private double activeSlPrice_Runner = 0.0;
		private bool runnerBreakEvenTriggered = false;
		private int maxProfitTicks_Runner = 0;
		private bool phase2Triggered_Runner = false;
		private bool phase3Triggered_Runner = false;

		// VARIABLES FINANCIERAS
		private double entryPrice_Scalper = 0.0;
		private double entryPrice_Runner = 0.0;

		// RENDERIZADO VISUAL
		private MAX radarMax;
		private MIN radarMin;

		// ==========================================
		// VARIABLES DEL RADAR BAILOUT (ESCAPE TÁCTICO)
		// ==========================================
		private bool radarBailoutActive = false;
		private double targetObstaclePrice = 0.0;
		private bool obstacleBroken = false;
		private bool enteredObstacleZone = false;
		
		// ==========================================
		// VARIABLES DE REVERSIÓN (CAZADOR DE REBOTES)
		// ==========================================
		private bool esperandoRebote = false;
		private int direccionReboteEsperado = 0;
		private double precioMurallaRebote = 0.0;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Estrategia V10.0.2: Cazador de Rebotes (Filtro de Mechas).";
				Name										= "Tick610_AlphaCrossover";
				Calculate									= Calculate.OnEachTick; // ARQUITECTURA HÍBRIDA (ALTA VELOCIDAD)
				EntriesPerDirection							= 2; // Permite lanzar 2 señales (Scalper y Runner)
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
				StopTargetHandling							= StopTargetHandling.PerEntryExecution; // Separar SL/TP
				BarsRequiredToTrade							= 89;

				Version										= "12.0.2";
				RealTimeActivated 							= true;

				// Parámetros Base
				FastPeriod									= 34;
				SlowPeriod									= 89;
				ADXPeriod									= 14;
				ADXThreshold								= 15;
				KCPeriod									= 52;
				KCMultiplier								= 3.5;
				DelayBars									= 5;
				
				// Nuevo Filtro de Techos y Pisos
				LookbackTechosPisos							= 80;
				DistanciaMinimaBorde						= 8;
				TicksRompimientoRadar						= 8;
				ToleranciaHmaBailout						= 2;
				BarrasFiltroMuralla							= 4;
				DesplazamientoMuralla						= 10;
				
				// Reversión
				EnableContrarianTrade						= true;
				ReversionStopLossTicks						= 8;
				ReversionTakeProfitTicks					= 10;
				TicksReboteContrario						= 2;
				
				// Rompimiento (Breakout)
				EnableBreakout								= true;
				BreakoutProfitTicks							= 8;
				ReversionProfitTicks						= 8;
				
				// Nuevo Filtro de Anomalías (Velas Gigantes)
				LookbackAnomalia							= 25;
				MaxTamanoBarra								= 20;
				
				// Filtro HMA
				HMAPeriod									= 21;
				
				// Visualización
				DrawRadarRectangle							= true;
				
				ContractQuantityScalper						= 1;
				ContractQuantityRunner						= 1;
				
				// Parámetros de Gestión Dinámica (Generales y Scalper)
				MinStopLossTicks							= 4;
				MaxStopLossTicks							= 25; // Freno de Emergencia
				
				TriggerPhase2Ticks							= 16;
				TrailPhase2Ticks							= 8;
				TriggerPhase3Ticks							= 24;
				TrailPhase3Ticks							= 6;
				FinalTargetPhase3Ticks						= 8; 
				
				// Parámetros de Gestión Dinámica (Runner)
				RunnerTakeProfitTicks						= 50; // TP muy grande
				RunnerTriggerPhase2Ticks					= 35;
				RunnerTrailPhase2Ticks						= 8;
				RunnerTriggerPhase3Ticks					= 60;
				RunnerTrailPhase3Ticks						= 4;
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
				smaFast = SMA(FastPeriod);
				smaSlow = SMA(SlowPeriod);
				adx = ADX(ADXPeriod);
				kc = KeltnerChannel(KCMultiplier, KCPeriod);
				hma = HMA(HMAPeriod);
				radarMax = MAX(High, LookbackTechosPisos);
				radarMin = MIN(Low, LookbackTechosPisos);
			}
		}

		protected override void OnBarUpdate()
		{
			// BLOQUEO MAESTRO DEL BOTÓN UI
			if (!isUIActive) return;
			
			if (!startTrading) return;
			if (CurrentBar < Math.Max(BarsRequiredToTrade, LookbackTechosPisos + 2)) return;

			// ==========================================
			// 1. ZONA DE ALTA VELOCIDAD: GESTIÓN DINÁMICA (TICK A TICK)
			// ==========================================
			if (IsFirstTickOfBar == false)
			{
				// ------------------------------------------
				// CAZADOR DE REBOTES (MEAN REVERSION)
				// ------------------------------------------
				if (esperandoRebote && Position.MarketPosition == MarketPosition.Flat)
				{
					double offset = (DistanciaMinimaBorde / 2.0) * TickSize;
					if (direccionReboteEsperado == -1) // Esperando rebote bajista desde techo
					{
						double topBorder = precioMurallaRebote + offset;
						double bottomBorder = precioMurallaRebote - offset;
						
						if (Close[0] <= bottomBorder - (TicksReboteContrario * TickSize))
						{
							esperandoRebote = false;
							Print(string.Format("{0} - [Cazador] Rebote confirmado en {1}. ¡Disparando Corto (Reversión)!", Time[0].ToString("HH:mm:ss"), Close[0]));
							SetStopLoss("Reversion", CalculationMode.Price, topBorder, false);
							SetProfitTarget("Reversion", CalculationMode.Ticks, ReversionProfitTicks);
							EnterShort(ContractQuantityScalper, "Reversion");
						}
					}
					else if (direccionReboteEsperado == 1) // Esperando rebote alcista desde piso
					{
						double topBorder = precioMurallaRebote + offset;
						double bottomBorder = precioMurallaRebote - offset;
						
						if (Close[0] >= topBorder + (TicksReboteContrario * TickSize))
						{
							esperandoRebote = false;
							Print(string.Format("{0} - [Cazador] Rebote confirmado en {1}. ¡Disparando Largo (Reversión)!", Time[0].ToString("HH:mm:ss"), Close[0]));
							SetStopLoss("Reversion", CalculationMode.Price, bottomBorder, false);
							SetProfitTarget("Reversion", CalculationMode.Ticks, ReversionProfitTicks);
							EnterLong(ContractQuantityScalper, "Reversion");
						}
					}
					
					// ------------------------------------------
					// CAZADOR DE ROMPIMIENTOS (BREAKOUT TIPO TICK-A-TICK)
					// ------------------------------------------
					double rupturaOffset = (TicksRompimientoRadar * TickSize);
					
					// Rompimiento Alcista (Atraviesa el Techo)
					if (direccionReboteEsperado == -1 && Close[0] >= (precioMurallaRebote + offset) + rupturaOffset)
					{
						esperandoRebote = false;
						if (EnableBreakout)
						{
							double slPrice = precioMurallaRebote - offset; // Borde inferior del techo
							SetStopLoss("Breakout", CalculationMode.Price, slPrice, false);
							SetProfitTarget("Breakout", CalculationMode.Ticks, BreakoutProfitTicks);
							EnterLong(ContractQuantityScalper, "Breakout");
							Print(string.Format("{0} - [Cazador] Techo roto por {1} ticks. Abortando rebote y entrando en Breakout LARGO. SL: {2}.", Time[0].ToString("HH:mm:ss"), TicksRompimientoRadar, slPrice));
						}
						else
						{
							Print(string.Format("{0} - [Cazador] Precio tocó {1} ticks por encima del techo. Caza abortada.", Time[0].ToString("HH:mm:ss"), TicksRompimientoRadar));
						}
					}
					// Rompimiento Bajista (Atraviesa el Piso)
					else if (direccionReboteEsperado == 1 && Close[0] <= (precioMurallaRebote - offset) - rupturaOffset)
					{
						esperandoRebote = false;
						if (EnableBreakout)
						{
							double slPrice = precioMurallaRebote + offset; // Borde superior del piso
							SetStopLoss("Breakout", CalculationMode.Price, slPrice, false);
							SetProfitTarget("Breakout", CalculationMode.Ticks, BreakoutProfitTicks);
							EnterShort(ContractQuantityScalper, "Breakout");
							Print(string.Format("{0} - [Cazador] Piso roto por {1} ticks. Abortando rebote y entrando en Breakout CORTO. SL: {2}.", Time[0].ToString("HH:mm:ss"), TicksRompimientoRadar, slPrice));
						}
						else
						{
							Print(string.Format("{0} - [Cazador] Precio tocó {1} ticks por debajo del piso. Caza abortada.", Time[0].ToString("HH:mm:ss"), TicksRompimientoRadar));
						}
					}
				}

				// ------------------------------------------
				// ESCAPE TÁCTICO (RADAR BAILOUT)
				// ------------------------------------------
				if (radarBailoutActive && !obstacleBroken)
				{
					double offset = (DistanciaMinimaBorde / 2.0) * TickSize;
					if (Position.MarketPosition == MarketPosition.Long)
					{
						if (High[0] >= targetObstaclePrice - offset)
						{
							enteredObstacleZone = true;
						}
						
						if (enteredObstacleZone && !obstacleBroken && hma[0] < hma[ToleranciaHmaBailout])
						{
							Print(string.Format("{0} - [ESCAPE TÁCTICO] Rebote detectado en Techo Histórico (HMA perdiendo fuerza vs hace {1} barras). Misión Abortada.", Time[0].ToString("HH:mm:ss"), ToleranciaHmaBailout));
							if (isScalperAlive) ExitLong("Scalper Bailout", "Scalper");
							if (isRunnerAlive) ExitLong("Runner Bailout", "Runner");
							radarBailoutActive = false; // Solo mandamos la orden una vez
						}
					}
					else if (Position.MarketPosition == MarketPosition.Short)
					{
						if (Low[0] <= targetObstaclePrice + offset)
						{
							enteredObstacleZone = true;
						}
						
						if (enteredObstacleZone && !obstacleBroken && hma[0] > hma[ToleranciaHmaBailout])
						{
							Print(string.Format("{0} - [ESCAPE TÁCTICO] Rebote detectado en Piso Histórico (HMA perdiendo fuerza vs hace {1} barras). Misión Abortada.", Time[0].ToString("HH:mm:ss"), ToleranciaHmaBailout));
							if (isScalperAlive) ExitShort("Scalper Bailout", "Scalper");
							if (isRunnerAlive) ExitShort("Runner Bailout", "Runner");
							radarBailoutActive = false; // Solo mandamos la orden una vez
						}
					}
				}

				double currentProfit = 0;
				if (Position.MarketPosition == MarketPosition.Long)
					currentProfit = Close[0] - Position.AveragePrice;
				else if (Position.MarketPosition == MarketPosition.Short)
					currentProfit = Position.AveragePrice - Close[0];
				
				int currentProfitTicks = (int)Math.Floor(currentProfit / TickSize);
				
				// ------------------------------------------
				// CEREBRO 1: EL SCALPER (CAZADOR)
				// ------------------------------------------
				if (isScalperAlive)
				{
					if (currentProfitTicks > maxProfitTicks_Scalper)
						maxProfitTicks_Scalper = currentProfitTicks;

					if (maxProfitTicks_Scalper >= TriggerPhase2Ticks && maxProfitTicks_Scalper < TriggerPhase3Ticks && !phase2Triggered_Scalper)
					{
						phase2Triggered_Scalper = true;
						Print(string.Format("{0} - [CAZADOR FASE 2] Alcanzada. SL asfixiante a {1} Ticks. TP: No tiene.", Time[0].ToString("HH:mm:ss"), TrailPhase2Ticks));
					}
					
					if (maxProfitTicks_Scalper >= TriggerPhase3Ticks && !phase3Triggered_Scalper)
					{
						phase3Triggered_Scalper = true;
						
						int finalTarget = TriggerPhase3Ticks + FinalTargetPhase3Ticks;
						SetProfitTarget("Scalper", CalculationMode.Ticks, finalTarget);
						Print(string.Format("{0} - [CAZADOR FASE 3] Alcanzada. SL asfixiante a {1} Ticks. TP Fijo a {2} Ticks.", Time[0].ToString("HH:mm:ss"), TrailPhase3Ticks, finalTarget));
					}
					
					double theoreticalSl = activeSlPrice_Scalper;
					
					if (!phase2Triggered_Scalper && !phase3Triggered_Scalper)
					{
						if (Position.MarketPosition == MarketPosition.Long)
						{
							if (slKeltnerMode_Scalper == 1) theoreticalSl = kc.Upper[0] - TickSize;
							else if (slKeltnerMode_Scalper == 2) theoreticalSl = kc.Midline[0] - TickSize;
						}
						else if (Position.MarketPosition == MarketPosition.Short)
						{
							if (slKeltnerMode_Scalper == -1) theoreticalSl = kc.Lower[0] + TickSize;
							else if (slKeltnerMode_Scalper == 2) theoreticalSl = kc.Midline[0] + TickSize;
						}
					}
					else if (phase2Triggered_Scalper && !phase3Triggered_Scalper)
					{
						if (Position.MarketPosition == MarketPosition.Long)
							theoreticalSl = (Position.AveragePrice + (maxProfitTicks_Scalper * TickSize)) - (TrailPhase2Ticks * TickSize);
						else if (Position.MarketPosition == MarketPosition.Short)
							theoreticalSl = (Position.AveragePrice - (maxProfitTicks_Scalper * TickSize)) + (TrailPhase2Ticks * TickSize);
					}
					else if (phase3Triggered_Scalper)
					{
						if (Position.MarketPosition == MarketPosition.Long)
							theoreticalSl = (Position.AveragePrice + (maxProfitTicks_Scalper * TickSize)) - (TrailPhase3Ticks * TickSize);
						else if (Position.MarketPosition == MarketPosition.Short)
							theoreticalSl = (Position.AveragePrice - (maxProfitTicks_Scalper * TickSize)) + (TrailPhase3Ticks * TickSize);
					}
					
					if (Position.MarketPosition == MarketPosition.Long && theoreticalSl > activeSlPrice_Scalper) 
						activeSlPrice_Scalper = theoreticalSl;
					else if (Position.MarketPosition == MarketPosition.Short && theoreticalSl < activeSlPrice_Scalper) 
						activeSlPrice_Scalper = theoreticalSl;
						
					SetStopLoss("Scalper", CalculationMode.Price, activeSlPrice_Scalper, false);
				}
				
				// ------------------------------------------
				// CEREBRO 2: EL RUNNER (MACRO TENDENCIA AGRESIVA)
				// ------------------------------------------
				if (isRunnerAlive)
				{
					if (currentProfitTicks > maxProfitTicks_Runner)
						maxProfitTicks_Runner = currentProfitTicks;
						
					if (maxProfitTicks_Runner >= RunnerTriggerPhase2Ticks && !phase2Triggered_Runner)
					{
						phase2Triggered_Runner = true;
						Print(string.Format("{0} - [RUNNER FASE 2] Alcanzada. Despegue de Línea Media. SL agresivo a {1} Ticks. TP: No tiene.", Time[0].ToString("HH:mm:ss"), RunnerTrailPhase2Ticks));
					}
					if (maxProfitTicks_Runner >= RunnerTriggerPhase3Ticks && !phase3Triggered_Runner)
					{
						phase3Triggered_Runner = true;
						Print(string.Format("{0} - [RUNNER FASE 3] Alcanzada. Estrangulamiento Final a {1} Ticks. TP: No tiene.", Time[0].ToString("HH:mm:ss"), RunnerTrailPhase3Ticks));
					}
					
					double theoreticalSl = activeSlPrice_Runner;
					
					if (!phase2Triggered_Runner)
					{
						// Fase 1 Runner: Siempre Midline
						if (Position.MarketPosition == MarketPosition.Long)
							theoreticalSl = kc.Midline[0] - TickSize;
						else if (Position.MarketPosition == MarketPosition.Short)
							theoreticalSl = kc.Midline[0] + TickSize;
							
						// Protección Break Even
						if (runnerBreakEvenTriggered)
						{
							double breakEvenPrice = 0;
							if (Position.MarketPosition == MarketPosition.Long)
							{
								breakEvenPrice = Position.AveragePrice + (2 * TickSize);
								if (theoreticalSl < breakEvenPrice) theoreticalSl = breakEvenPrice; 
							}
							else if (Position.MarketPosition == MarketPosition.Short)
							{
								breakEvenPrice = Position.AveragePrice - (2 * TickSize);
								if (theoreticalSl > breakEvenPrice) theoreticalSl = breakEvenPrice;
							}
						}
					}
					else if (phase2Triggered_Runner && !phase3Triggered_Runner)
					{
						// Fase 2 Runner: Trailing Despegado
						if (Position.MarketPosition == MarketPosition.Long)
							theoreticalSl = (Position.AveragePrice + (maxProfitTicks_Runner * TickSize)) - (RunnerTrailPhase2Ticks * TickSize);
						else if (Position.MarketPosition == MarketPosition.Short)
							theoreticalSl = (Position.AveragePrice - (maxProfitTicks_Runner * TickSize)) + (RunnerTrailPhase2Ticks * TickSize);
					}
					else if (phase3Triggered_Runner)
					{
						// Fase 3 Runner: Estrangulamiento
						if (Position.MarketPosition == MarketPosition.Long)
							theoreticalSl = (Position.AveragePrice + (maxProfitTicks_Runner * TickSize)) - (RunnerTrailPhase3Ticks * TickSize);
						else if (Position.MarketPosition == MarketPosition.Short)
							theoreticalSl = (Position.AveragePrice - (maxProfitTicks_Runner * TickSize)) + (RunnerTrailPhase3Ticks * TickSize);
					}
					
					if (Position.MarketPosition == MarketPosition.Long && theoreticalSl > activeSlPrice_Runner) 
						activeSlPrice_Runner = theoreticalSl;
					else if (Position.MarketPosition == MarketPosition.Short && theoreticalSl < activeSlPrice_Runner) 
						activeSlPrice_Runner = theoreticalSl;
						
					SetStopLoss("Runner", CalculationMode.Price, activeSlPrice_Runner, false);
				}
			}


			// ==========================================
			// 2. ZONA DE BAJA VELOCIDAD: FILTRO DE BARRAS (FILTRADO DE ENTRADAS)
			// ==========================================
			if (IsFirstTickOfBar)
			{
				if (DrawRadarRectangle && CurrentBar > LookbackTechosPisos + 5 + DesplazamientoMuralla)
				{
					double currentCeiling = radarMax[DesplazamientoMuralla];
					double currentFloor = radarMin[DesplazamientoMuralla];
					Draw.Rectangle(this, "RadarCeiling", false, LookbackTechosPisos + 5 + DesplazamientoMuralla, currentCeiling + ((DistanciaMinimaBorde / 2.0) * TickSize), DesplazamientoMuralla, currentCeiling - ((DistanciaMinimaBorde / 2.0) * TickSize), Brushes.Transparent, Brushes.Red, 20);
					Draw.Rectangle(this, "RadarFloor", false, LookbackTechosPisos + 5 + DesplazamientoMuralla, currentFloor + ((DistanciaMinimaBorde / 2.0) * TickSize), DesplazamientoMuralla, currentFloor - ((DistanciaMinimaBorde / 2.0) * TickSize), Brushes.Transparent, Brushes.Green, 20);
				}

				// Usamos Time[1] porque es la barra completada que evaluamos
				int currentTime = ToTime(Time[1]);
				
				// ------------------------------------------
				// ESCAPE TÁCTICO (ABORTAR SI CIERRA AFUERA)
				// ------------------------------------------
				if (radarBailoutActive)
				{
					double offset = (DistanciaMinimaBorde / 2.0) * TickSize;
					double rupturaOffset = (TicksRompimientoRadar * TickSize);
					if (Position.MarketPosition == MarketPosition.Long && Close[1] > (targetObstaclePrice + offset) + rupturaOffset)
					{
						obstacleBroken = true;
						radarBailoutActive = false; // Ya no evaluamos más
						Print(string.Format("{0} - [RADAR] El precio cerró rompiendo el techo por {1} Ticks ({2}). Escape Táctico Desactivado.", Time[1].ToString("HH:mm:ss"), TicksRompimientoRadar, targetObstaclePrice));
					}
					else if (Position.MarketPosition == MarketPosition.Short && Close[1] < (targetObstaclePrice - offset) - rupturaOffset)
					{
						obstacleBroken = true;
						radarBailoutActive = false;
						Print(string.Format("{0} - [RADAR] El precio cerró rompiendo el piso por {1} Ticks ({2}). Escape Táctico Desactivado.", Time[1].ToString("HH:mm:ss"), TicksRompimientoRadar, targetObstaclePrice));
					}
				}
				if (currentTime < 93000 || currentTime >= 160000) return;

				// DETECCIÓN DE CRUCE MANUAL EN BARRAS CONSOLIDADAS [1] Y [2]
				bool crossUp = (smaFast[2] <= smaSlow[2]) && (smaFast[1] > smaSlow[1]);
				bool crossDown = (smaFast[2] >= smaSlow[2]) && (smaFast[1] < smaSlow[1]);

				if (crossUp)
				{
					crossoverDirection = 1;
					crossoverBar = CurrentBar - 1; // La barra que cruzó fue la [1], que internamente es CurrentBar - 1
					Print(string.Format("{0} - [Aviso] Cruce Alcista detectado. Iniciando cuenta de {1} barras.", Time[1].ToString("HH:mm:ss"), DelayBars));
				}
				else if (crossDown)
				{
					crossoverDirection = -1;
					crossoverBar = CurrentBar - 1;
					Print(string.Format("{0} - [Aviso] Cruce Bajista detectado. Iniciando cuenta de {1} barras.", Time[1].ToString("HH:mm:ss"), DelayBars));
				}

				// CONFIRMACIÓN Y ENTRADA
				// La barra actual consolidada es CurrentBar - 1. Comprobamos el Delay.
				if (crossoverDirection != 0 && (CurrentBar - 1) == crossoverBar + DelayBars)
				{
					// Validación 1: Posición del Precio vs SMAs (usando barra [1])
					bool validPricePosition = false;
					if (crossoverDirection == 1 && Close[1] > Math.Max(smaFast[1], smaSlow[1])) 
						validPricePosition = true;
					else if (crossoverDirection == -1 && Close[1] < Math.Min(smaFast[1], smaSlow[1])) 
						validPricePosition = true;
						
					// Validación 2: RADAR DE TECHOS Y PISOS CENTRADO
					bool validRadar = true;
					double offsetValidation = (DistanciaMinimaBorde / 2.0) * TickSize;
					double rupturaValidation = (TicksRompimientoRadar * TickSize);
					if (crossoverDirection == 1) // Compras (Techos)
					{
						double distancia = radarMax[DesplazamientoMuralla] - Close[1];
						// Zona de peligro: cerca del techo, pero no lo ha roto por completo
						if (distancia < offsetValidation && distancia > -rupturaValidation)
						{
							validRadar = false;
							Print(string.Format("{0} - [Cancelado] Muy cerca del Techo Histórico. Distancia: {1} Ticks (Mínimo requerido desde centro: {2}).", Time[1].ToString("HH:mm:ss"), Math.Round(distancia / TickSize, 1), DistanciaMinimaBorde / 2.0));
						}
						else
						{
							// Revisar memoria de velas previas (Mechas)
							for (int i = 1; i <= BarrasFiltroMuralla; i++)
							{
								double mechaDistancia = radarMax[DesplazamientoMuralla] - High[i];
								if (mechaDistancia < offsetValidation && mechaDistancia > -rupturaValidation)
								{
									validRadar = false;
									Print(string.Format("{0} - [Cancelado] La mecha de la vela [{1}] tocó el Techo Histórico. Memoria activada.", Time[1].ToString("HH:mm:ss"), i));
									break;
								}
							}
						}
					}
					else if (crossoverDirection == -1) // Ventas (Pisos)
					{
						double distancia = Close[1] - radarMin[DesplazamientoMuralla];
						// Zona de peligro: cerca del piso, pero no lo ha roto por completo
						if (distancia < offsetValidation && distancia > -rupturaValidation)
						{
							validRadar = false;
							Print(string.Format("{0} - [Cancelado] Muy cerca del Piso Histórico. Distancia: {1} Ticks (Mínimo requerido desde centro: {2}).", Time[1].ToString("HH:mm:ss"), Math.Round(distancia / TickSize, 1), DistanciaMinimaBorde / 2.0));
						}
						else
						{
							// Revisar memoria de velas previas (Mechas)
							for (int i = 1; i <= BarrasFiltroMuralla; i++)
							{
								double mechaDistancia = Low[i] - radarMin[DesplazamientoMuralla];
								if (mechaDistancia < offsetValidation && mechaDistancia > -rupturaValidation)
								{
									validRadar = false;
									Print(string.Format("{0} - [Cancelado] La mecha de la vela [{1}] tocó el Piso Histórico. Memoria activada.", Time[1].ToString("HH:mm:ss"), i));
									break;
								}
							}
						}
					}
					
					// Validación 3: FILTRO DE ANOMALÍAS DE VOLATILIDAD (Velas Gigantes)
					bool validVolatility = true;
					double maxBarSizeFound = 0;
					for (int i = 1; i <= LookbackAnomalia; i++)
					{
						double barRangeTicks = (High[i] - Low[i]) / TickSize;
						if (barRangeTicks > maxBarSizeFound) maxBarSizeFound = barRangeTicks;
					}
					
					Print(string.Format("{0} - [Análisis Volatilidad] La vela más grande en las últimas {1} barras midió {2} Ticks.", Time[1].ToString("HH:mm:ss"), LookbackAnomalia, maxBarSizeFound));
					
					if (maxBarSizeFound > MaxTamanoBarra)
					{
						validVolatility = false;
						Print(string.Format("{0} - [Cancelado] Anomalía de Volatilidad (Límite: {1} Ticks).", Time[1].ToString("HH:mm:ss"), MaxTamanoBarra));
					}
						
					// Validación 4: FILTRO DE PENDIENTE HMA
					bool validHMASlope = false;
					if (crossoverDirection == 1 && hma[1] > hma[2]) validHMASlope = true;
					else if (crossoverDirection == -1 && hma[1] < hma[2]) validHMASlope = true;
					
					// Validación 5: PENDIENTES DE SMA (Trade Normal vs Reversión)
					bool isReversionTrade = false;
					bool validEntryDirection = false;
					int tradeDirectionToExecute = 0; // 1 = Long, -1 = Short

					if (crossoverDirection == 1) // Cruce Alcista
					{
						bool fastSlopeUp = smaFast[1] > smaFast[4];
						bool slowSlopeUp = smaSlow[1] > smaSlow[4];
						
						if (fastSlopeUp && slowSlopeUp) 
						{
							validEntryDirection = true;
							tradeDirectionToExecute = 1;
						}
						else if (EnableContrarianTrade && fastSlopeUp && !slowSlopeUp)
						{
							validEntryDirection = true;
							tradeDirectionToExecute = -1;
							isReversionTrade = true;
							Print(string.Format("{0} - [REVERSIÓN] Cruce Alcista pero SMA lenta plana/bajista. Ejecutando CORTO.", Time[1].ToString("HH:mm:ss")));
						}
						else
						{
							Print(string.Format("{0} - [Cancelado] Pendientes no alineadas para Normal ni Reversión.", Time[1].ToString("HH:mm:ss")));
						}
					}
					else if (crossoverDirection == -1) // Cruce Bajista
					{
						bool fastSlopeDown = smaFast[1] < smaFast[4];
						bool slowSlopeDown = smaSlow[1] < smaSlow[4];
						
						if (fastSlopeDown && slowSlopeDown)
						{
							validEntryDirection = true;
							tradeDirectionToExecute = -1;
						}
						else if (EnableContrarianTrade && fastSlopeDown && !slowSlopeDown)
						{
							validEntryDirection = true;
							tradeDirectionToExecute = 1;
							isReversionTrade = true;
							Print(string.Format("{0} - [REVERSIÓN] Cruce Bajista pero SMA lenta plana/alcista. Ejecutando LARGO.", Time[1].ToString("HH:mm:ss")));
						}
						else
						{
							Print(string.Format("{0} - [Cancelado] Pendientes no alineadas para Normal ni Reversión.", Time[1].ToString("HH:mm:ss")));
						}
					}

					// Si aprueba todos los filtros, entramos
					if (adx[1] >= ADXThreshold && validPricePosition && validRadar && validVolatility && validHMASlope && validEntryDirection)
					{
						if (isReversionTrade)
						{
							// Ejecutar Trade de Reversión (Fijo)
							if (tradeDirectionToExecute == 1) // LARGO REVERSIÓN
							{
								SetStopLoss("Reversion", CalculationMode.Ticks, ReversionStopLossTicks, false);
								SetProfitTarget("Reversion", CalculationMode.Ticks, ReversionTakeProfitTicks);
								EnterLong(ContractQuantityScalper + ContractQuantityRunner, "Reversion");
								Print(string.Format("{0} - [LARGO REVERSIÓN] Entrada en {1}. SL: {2} Ticks. TP: {3} Ticks.", Time[0].ToString("HH:mm:ss"), Close[0], ReversionStopLossTicks, ReversionTakeProfitTicks));
							}
							else if (tradeDirectionToExecute == -1) // CORTO REVERSIÓN
							{
								SetStopLoss("Reversion", CalculationMode.Ticks, ReversionStopLossTicks, false);
								SetProfitTarget("Reversion", CalculationMode.Ticks, ReversionTakeProfitTicks);
								EnterShort(ContractQuantityScalper + ContractQuantityRunner, "Reversion");
								Print(string.Format("{0} - [CORTO REVERSIÓN] Entrada en {1}. SL: {2} Ticks. TP: {3} Ticks.", Time[0].ToString("HH:mm:ss"), Close[0], ReversionStopLossTicks, ReversionTakeProfitTicks));
							}
						}
						else
						{
							// Lógica de Ejecución Normal (Scalper + Runner)
							if (tradeDirectionToExecute == 1) // COMPRA (LONG) NORMAL
							{
							// DISPARO DEL SCALPER
							if (ContractQuantityScalper > 0)
							{
								maxProfitTicks_Scalper = 0;
								phase2Triggered_Scalper = false;
								phase3Triggered_Scalper = false;
								
								if (Low[1] > kc.Upper[1]) { activeSlPrice_Scalper = kc.Upper[1] - TickSize; slKeltnerMode_Scalper = 1; }
								else { activeSlPrice_Scalper = kc.Midline[1] - TickSize; slKeltnerMode_Scalper = 2; }
								
								// Freno de Emergencia (Max SL) y Colchón Mínimo evaluado sobre Close[0] porque ahí entramos
								double distanceScalper = Math.Abs(Close[0] - activeSlPrice_Scalper) / TickSize;
								if (distanceScalper > MaxStopLossTicks)
								{
									activeSlPrice_Scalper = Close[0] - (MaxStopLossTicks * TickSize);
									Print(string.Format("{0} - [SCALPER Freno] Distancia original {1} > Max {2}. Topando SL.", Time[0].ToString("HH:mm:ss"), distanceScalper, MaxStopLossTicks));
								}
								double minSlPrice = Close[0] - (MinStopLossTicks * TickSize);
								if (activeSlPrice_Scalper > minSlPrice) activeSlPrice_Scalper = minSlPrice; 
								
								SetStopLoss("Scalper", CalculationMode.Price, activeSlPrice_Scalper, false);
								EnterLong(ContractQuantityScalper, "Scalper");
								isScalperAlive = true;
								Print(string.Format("{0} - [LARGO SCALPER] Entrada en {1}. SL Inicial: {2}", Time[0].ToString("HH:mm:ss"), Close[0], activeSlPrice_Scalper));
							}
							
							// DISPARO DEL RUNNER
							if (ContractQuantityRunner > 0)
							{
								maxProfitTicks_Runner = 0;
								phase2Triggered_Runner = false;
								phase3Triggered_Runner = false;
								runnerBreakEvenTriggered = false;
								
								activeSlPrice_Runner = kc.Midline[1] - TickSize; // Siempre Midline inicial
								
								// Freno de Emergencia y Mínimo
								double distanceRunner = Math.Abs(Close[0] - activeSlPrice_Runner) / TickSize;
								if (distanceRunner > MaxStopLossTicks)
								{
									activeSlPrice_Runner = Close[0] - (MaxStopLossTicks * TickSize);
									Print(string.Format("{0} - [RUNNER Freno] Distancia original {1} > Max {2}. Topando SL.", Time[0].ToString("HH:mm:ss"), distanceRunner, MaxStopLossTicks));
								}
								double minSlPrice = Close[0] - (MinStopLossTicks * TickSize);
								if (activeSlPrice_Runner > minSlPrice) activeSlPrice_Runner = minSlPrice; 
								
								SetStopLoss("Runner", CalculationMode.Price, activeSlPrice_Runner, false);
								SetProfitTarget("Runner", CalculationMode.Ticks, RunnerTakeProfitTicks); 
								EnterLong(ContractQuantityRunner, "Runner");
								isRunnerAlive = true;
								Print(string.Format("{0} - [LARGO RUNNER] Entrada en {1}. SL Inicial (Midline): {2}", Time[0].ToString("HH:mm:ss"), Close[0], activeSlPrice_Runner));
							}
							
							// Configurar Radar Bailout
							radarBailoutActive = true;
							targetObstaclePrice = radarMax[DesplazamientoMuralla];
							obstacleBroken = false;
							enteredObstacleZone = false;
						}
							else if (tradeDirectionToExecute == -1) // VENTA (SHORT) NORMAL
							{
							// DISPARO DEL SCALPER
							if (ContractQuantityScalper > 0)
							{
								maxProfitTicks_Scalper = 0;
								phase2Triggered_Scalper = false;
								phase3Triggered_Scalper = false;
								
								if (High[1] < kc.Lower[1]) { activeSlPrice_Scalper = kc.Lower[1] + TickSize; slKeltnerMode_Scalper = -1; }
								else { activeSlPrice_Scalper = kc.Midline[1] + TickSize; slKeltnerMode_Scalper = 2; }
								
								// Freno de Emergencia y Mínimo
								double distanceScalper = Math.Abs(Close[0] - activeSlPrice_Scalper) / TickSize;
								if (distanceScalper > MaxStopLossTicks)
								{
									activeSlPrice_Scalper = Close[0] + (MaxStopLossTicks * TickSize);
									Print(string.Format("{0} - [SCALPER Freno] Distancia original {1} > Max {2}. Topando SL.", Time[0].ToString("HH:mm:ss"), distanceScalper, MaxStopLossTicks));
								}
								double minSlPrice = Close[0] + (MinStopLossTicks * TickSize);
								if (activeSlPrice_Scalper < minSlPrice) activeSlPrice_Scalper = minSlPrice; 
								
								SetStopLoss("Scalper", CalculationMode.Price, activeSlPrice_Scalper, false);
								EnterShort(ContractQuantityScalper, "Scalper");
								isScalperAlive = true;
								Print(string.Format("{0} - [CORTO SCALPER] Entrada en {1}. SL Inicial: {2}", Time[0].ToString("HH:mm:ss"), Close[0], activeSlPrice_Scalper));
							}
							
							// DISPARO DEL RUNNER
							if (ContractQuantityRunner > 0)
							{
								maxProfitTicks_Runner = 0;
								phase2Triggered_Runner = false;
								phase3Triggered_Runner = false;
								runnerBreakEvenTriggered = false;
								
								activeSlPrice_Runner = kc.Midline[1] + TickSize; // Siempre Midline inicial
								
								// Freno de Emergencia y Mínimo
								double distanceRunner = Math.Abs(Close[0] - activeSlPrice_Runner) / TickSize;
								if (distanceRunner > MaxStopLossTicks)
								{
									activeSlPrice_Runner = Close[0] + (MaxStopLossTicks * TickSize);
									Print(string.Format("{0} - [RUNNER Freno] Distancia original {1} > Max {2}. Topando SL.", Time[0].ToString("HH:mm:ss"), distanceRunner, MaxStopLossTicks));
								}
								double minSlPrice = Close[0] + (MinStopLossTicks * TickSize);
								if (activeSlPrice_Runner < minSlPrice) activeSlPrice_Runner = minSlPrice; 
								
								SetStopLoss("Runner", CalculationMode.Price, activeSlPrice_Runner, false);
								SetProfitTarget("Runner", CalculationMode.Ticks, RunnerTakeProfitTicks); 
								EnterShort(ContractQuantityRunner, "Runner");
								isRunnerAlive = true;
								Print(string.Format("{0} - [CORTO RUNNER] Entrada en {1}. SL Inicial (Midline): {2}", Time[0].ToString("HH:mm:ss"), Close[0], activeSlPrice_Runner));
							}
							
							// Configurar Radar Bailout
							radarBailoutActive = true;
							targetObstaclePrice = radarMin[DesplazamientoMuralla];
							obstacleBroken = false;
							enteredObstacleZone = false;
						}
					}
					}
					else
					{
						if (!validPricePosition)
							Print(string.Format("{0} - [Cancelado] El Precio está del lado equivocado de las SMAs (Cierre: {1}).", Time[1].ToString("HH:mm:ss"), Close[1]));
						else if (!validRadar)
						{
							// Activar Cazador de Rebotes
							if (EnableReversion && Position.MarketPosition == MarketPosition.Flat && !esperandoRebote)
							{
								esperandoRebote = true;
								direccionReboteEsperado = (crossoverDirection == 1) ? -1 : 1;
								precioMurallaRebote = (crossoverDirection == 1) ? radarMax[DesplazamientoMuralla] : radarMin[DesplazamientoMuralla];
								Print(string.Format("{0} - [Cazador] Señal cancelada. Esperando rebote en Muralla Centrada ({1}).", Time[1].ToString("HH:mm:ss"), precioMurallaRebote));
							}
						}
						else if (!validVolatility)
						{
							// El print ya se hizo arriba
						}
						else if (!validHMASlope)
						{
							Print(string.Format("{0} - [Cancelado] La pendiente del HMA ({1}) está en contra de la dirección del trade.", Time[1].ToString("HH:mm:ss"), HMAPeriod));
						}
						else
							Print(string.Format("{0} - [Cancelado] ADX ({1}) no tiene fuerza suficiente.", Time[1].ToString("HH:mm:ss"), Math.Round(adx[1], 2)));
					}
					
					crossoverDirection = 0;
					crossoverBar = -1;
				}
			} // Fin IsFirstTickOfBar
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution.Order.OrderState == OrderState.Filled || execution.Order.OrderState == OrderState.PartFilled)
			{
				if (execution.Order.OrderAction == OrderAction.Buy || execution.Order.OrderAction == OrderAction.SellShort) 
				{
					if (execution.Order.Name == "Scalper" || execution.Order.FromEntrySignal == "Scalper")
					{
						entryPrice_Scalper = execution.Price;
						string dir = (execution.Order.OrderAction == OrderAction.Buy) ? "LARGO" : "CORTO";
						Print(string.Format("{0} - [SCALPER] Abrió posición en {1} ({2})", time.ToString("HH:mm:ss"), dir, execution.Price));
					}
					else if (execution.Order.Name == "Runner" || execution.Order.FromEntrySignal == "Runner")
					{
						entryPrice_Runner = execution.Price;
						string dir = (execution.Order.OrderAction == OrderAction.Buy) ? "LARGO" : "CORTO";
						Print(string.Format("{0} - [RUNNER] Abrió posición en {1} ({2})", time.ToString("HH:mm:ss"), dir, execution.Price));
					}
				}
				else if (execution.Order.OrderAction == OrderAction.Sell || execution.Order.OrderAction == OrderAction.BuyToCover) 
				{
					double multiplier = Instrument.MasterInstrument.PointValue;
					
					// Procesar salidas (Exits)
					if (execution.Order.FromEntrySignal == "Scalper")
					{
						double pnlDollars = 0;
						if (execution.Order.OrderAction == OrderAction.Sell) pnlDollars = (price - entryPrice_Scalper) * multiplier * execution.Quantity;
						else if (execution.Order.OrderAction == OrderAction.BuyToCover) pnlDollars = (entryPrice_Scalper - price) * multiplier * execution.Quantity;
						
						if (execution.Order.Name == "Profit target")
						{
							Print(string.Format("{0} - [SCALPER CERRADO] Alcanzó su meta. Salió en {1}. P/L: {2} USD", time.ToString("HH:mm:ss"), price, pnlDollars.ToString("C2")));
							runnerBreakEvenTriggered = true; 
						}
						else if (execution.Order.Name == "Stop loss")
						{
							Print(string.Format("{0} - [SCALPER CERRADO] Tocado por Stop Loss en {1}. P/L: {2} USD", time.ToString("HH:mm:ss"), price, pnlDollars.ToString("C2")));
						}
						else if (execution.Order.Name == "Scalper Bailout")
						{
							Print(string.Format("{0} - [SCALPER CERRADO] Abortado por Radar Bailout en {1}. P/L: {2} USD", time.ToString("HH:mm:ss"), price, pnlDollars.ToString("C2")));
						}
						isScalperAlive = false;
					}
					else if (execution.Order.FromEntrySignal == "Runner")
					{
						double pnlDollars = 0;
						if (execution.Order.OrderAction == OrderAction.Sell) pnlDollars = (price - entryPrice_Runner) * multiplier * execution.Quantity;
						else if (execution.Order.OrderAction == OrderAction.BuyToCover) pnlDollars = (entryPrice_Runner - price) * multiplier * execution.Quantity;
						
						if (execution.Order.Name == "Profit target")
						{
							Print(string.Format("{0} - [RUNNER CERRADO] Mega-target en {1}. P/L: {2} USD", time.ToString("HH:mm:ss"), price, pnlDollars.ToString("C2")));
						}
						else if (execution.Order.Name == "Stop loss")
						{
							Print(string.Format("{0} - [RUNNER CERRADO] Tocado por Stop Loss en {1}. P/L: {2} USD", time.ToString("HH:mm:ss"), price, pnlDollars.ToString("C2")));
						}
						else if (execution.Order.Name == "Runner Bailout")
						{
							Print(string.Format("{0} - [RUNNER CERRADO] Abortado por Radar Bailout en {1}. P/L: {2} USD", time.ToString("HH:mm:ss"), price, pnlDollars.ToString("C2")));
						}
						isRunnerAlive = false;
					}
				}
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name="Habilitar Reversión", Order=1, GroupName="2. AlphaCrossover: Reversión de Tendencia")]
		public bool EnableContrarianTrade { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Stop Loss Reversión (Ticks)", Order=2, GroupName="2. AlphaCrossover: Reversión de Tendencia")]
		public int ReversionStopLossTicks { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Take Profit Reversión (Ticks)", Order=3, GroupName="2. AlphaCrossover: Reversión de Tendencia")]
		public int ReversionTakeProfitTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Periodo SMA Rápida", Order=1, GroupName="1. Parámetros de Estrategia")]
		public int FastPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Periodo SMA Lenta", Order=2, GroupName="1. Parámetros de Estrategia")]
		public int SlowPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Periodo ADX", Order=3, GroupName="1. Parámetros de Estrategia")]
		public int ADXPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Fuerza Mínima ADX", Order=4, GroupName="1. Parámetros de Estrategia")]
		public int ADXThreshold { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Delay de Entrada (Barras)", Order=5, GroupName="1. Parámetros de Estrategia")]
		public int DelayBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Periodo Keltner Channel", Order=6, GroupName="1. Parámetros de Estrategia")]
		public int KCPeriod { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name="Multiplicador Keltner Channel", Order=7, GroupName="1. Parámetros de Estrategia")]
		public double KCMultiplier { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Periodo HMA (Filtro Inercia)", Order=8, GroupName="1. Parámetros de Estrategia")]
		public int HMAPeriod { get; set; }
		
		// RADAR DE TECHOS Y PISOS
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Lookback Techos/Pisos (Velas)", Description="Cuantas velas atrás mirar para encontrar el Techo/Piso Histórico", Order=9, GroupName="1. Parámetros de Estrategia")]
		public int LookbackTechosPisos { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Distancia Mínima al Borde (Ticks)", Description="Distancia de la caja de muralla (se divide entre 2: mitad arriba, mitad abajo)", Order=10, GroupName="1. Parámetros de Estrategia")]
		public int DistanciaMinimaBorde { get; set; }
		
		[Display(Name="Ticks Rompimiento (Breakout)", Description="Ticks para considerar que la muralla fue destruida", Order=7, GroupName="3. Filtros del Cazador (Radar)")]
		public int TicksRompimientoRadar { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Memoria Muralla (Velas)", Order=8, GroupName="3. AlphaCrossover: Techos/Pisos (Murallas)")]
		public int BarrasFiltroMuralla { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Desplazamiento (Velas atrás)", Order=9, GroupName="3. AlphaCrossover: Techos/Pisos (Murallas)")]
		public int DesplazamientoMuralla { get; set; }

		[Display(Name="Tolerancia HMA Escape Táctico", Description="Número de barras hacia atrás para comparar el HMA en el Escape Táctico (ej. 2 significa hma[0] vs hma[2])", Order=12, GroupName="1. Parámetros de Estrategia")]
		public int ToleranciaHmaBailout { get; set; }

		// CAZADOR DE REBOTES (MEAN REVERSION)
		[NinjaScriptProperty]
		[Display(Name="Activar Cazador de Rebotes", Description="Si true, buscará reversiones cuando una señal se cancele por la muralla.", Order=12, GroupName="1. Parámetros de Estrategia")]
		public bool EnableReversion { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Rebote Contrario (Ticks)", Description="Ticks de salida de la caja para confirmar el rebote.", Order=12, GroupName="1. Parámetros de Estrategia")]
		public int TicksReboteContrario { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Take Profit Rebote (Ticks)", Description="Ganancia para el trade de rebote", Order=14, GroupName="1. Parámetros de Estrategia")]
		public int ReversionProfitTicks { get; set; }
		
		// CAZADOR DE ROMPIMIENTOS (BREAKOUT)
		[NinjaScriptProperty]
		[Display(Name="Activar Cazador de Rompimiento", Description="Si true, buscará entrar a favor del rompimiento cuando se aborte el rebote.", Order=15, GroupName="1. Parámetros de Estrategia")]
		public bool EnableBreakout { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Take Profit Breakout (Ticks)", Description="Ganancia fija para el trade de rompimiento", Order=16, GroupName="1. Parámetros de Estrategia")]
		public int BreakoutProfitTicks { get; set; }

		// FILTRO DE ANOMALÍAS
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Lookback Anomalía (Velas)", Description="Revisar n velas atrás buscando anomalías", Order=17, GroupName="1. Parámetros de Estrategia")]
		public int LookbackAnomalia { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Rango Máximo Vela (Ticks)", Description="Si una vela supera este rango, se cancela", Order=15, GroupName="1. Parámetros de Estrategia")]
		public int MaxTamanoBarra { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Mostrar Radar en Gráfica", Description="Dibuja la caja roja/verde de Techos y Pisos", Order=16, GroupName="1. Parámetros de Estrategia")]
		public bool DrawRadarRectangle { get; set; }

		// MULTICONTRATO
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="Contratos Scalper (Cazador)", Description="0 para desactivar", Order=1, GroupName="2. Contratos & Macro Trend")]
		public int ContractQuantityScalper { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="Contratos Runner (Macro)", Description="0 para desactivar", Order=2, GroupName="2. Contratos & Macro Trend")]
		public int ContractQuantityRunner { get; set; }
		
		[NinjaScriptProperty]
		[Range(10, int.MaxValue)]
		[Display(Name="Take Profit del Runner (Ticks)", Description="Pon un valor altísimo para que no deforme la gráfica", Order=3, GroupName="2. Contratos & Macro Trend")]
		public int RunnerTakeProfitTicks { get; set; }


		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Stop Minimo de Seguridad (Ticks)", Order=1, GroupName="3. Gestión Riesgo General")]
		public int MinStopLossTicks { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Stop Maximo Permitido (Ticks)", Description="Freno de Emergencia. Si el Keltner está más lejos, se usa este valor", Order=2, GroupName="3. Gestión Riesgo General")]
		public int MaxStopLossTicks { get; set; }


		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Trigger Fase 2 (Ticks)", Order=1, GroupName="4. Gestión Dinámica (Scalper)")]
		public int TriggerPhase2Ticks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Trailing Stop Fase 2 (Ticks)", Order=2, GroupName="4. Gestión Dinámica (Scalper)")]
		public int TrailPhase2Ticks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Trigger Fase 3 (Ticks)", Order=3, GroupName="4. Gestión Dinámica (Scalper)")]
		public int TriggerPhase3Ticks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Trailing Stop Fase 3 (Ticks)", Order=4, GroupName="4. Gestión Dinámica (Scalper)")]
		public int TrailPhase3Ticks { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Target Fijo Fase 3 (Ticks extra)", Order=5, GroupName="4. Gestión Dinámica (Scalper)")]
		public int FinalTargetPhase3Ticks { get; set; }
		
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Runner Trigger Fase 2 (Ticks)", Order=1, GroupName="5. Gestión Dinámica (Runner)")]
		public int RunnerTriggerPhase2Ticks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Runner Trailing Fase 2 (Ticks)", Order=2, GroupName="5. Gestión Dinámica (Runner)")]
		public int RunnerTrailPhase2Ticks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Runner Trigger Fase 3 (Ticks)", Order=3, GroupName="5. Gestión Dinámica (Runner)")]
		public int RunnerTriggerPhase3Ticks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Runner Trailing Fase 3 (Ticks)", Order=4, GroupName="5. Gestión Dinámica (Runner)")]
		public int RunnerTrailPhase3Ticks { get; set; }
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

		#endregion
	}
}
