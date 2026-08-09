//
// Copyright (C) 2026, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System.Windows.Media;
#endregion

// This namespace holds indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
	/// <summary>
	/// Performance (PERF) shows the percent change of the current value relative to the first value
	/// in the loaded series. It is calculated as ((current − first) / first) × 100 and updates as the
	/// chart's loaded history changes.
	/// </summary>
	public class Performance : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= Custom.Resource.NinjaScriptIndicatorDescriptionPerformance;
				Name						= Custom.Resource.NinjaScriptIndicatorNamePerformance;
				IsOverlay					= false;
				IsSuspendedWhileInactive	= true;

				AddPlot(Brushes.DodgerBlue, Custom.Resource.NinjaScriptIndicatorNamePerformance);
			}
		}

		protected override void OnBarUpdate() => Value[0] = Input[CurrentBar] == 0 ? 0 : (Input[0] - Input[CurrentBar]) / Input[CurrentBar] * 100;
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Performance[] cachePerformance;
		public Performance Performance()
		{
			return Performance(Input);
		}

		public Performance Performance(ISeries<double> input)
		{
			if (cachePerformance != null)
				for (int idx = 0; idx < cachePerformance.Length; idx++)
					if (cachePerformance[idx] != null &&  cachePerformance[idx].EqualsInput(input))
						return cachePerformance[idx];
			return CacheIndicator<Performance>(new Performance(), input, ref cachePerformance);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Performance Performance()
		{
			return indicator.Performance(Input);
		}

		public Indicators.Performance Performance(ISeries<double> input)
		{
			return indicator.Performance(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Performance Performance()
		{
			return indicator.Performance(Input);
		}

		public Indicators.Performance Performance(ISeries<double> input)
		{
			return indicator.Performance(input);
		}
	}
}

#endregion