//
// Copyright (C) 2026, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
#endregion

// This namespace holds indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
	/// <summary>
	/// %B (Percent B) measures where the current price is positioned within the Bollinger Bands range.
	/// Values near 0 indicate price is near the lower band, values near 1 indicate price is near the upper band,
	/// and values outside 0–1 indicate price is below the lower band or above the upper band.
	/// </summary>
	public class PercentB : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= Custom.Resource.NinjaScriptIndicatorDescriptionPercentB;
				Name						= Custom.Resource.NinjaScriptIndicatorNamePercentB;
				IsOverlay					= false;
				IsSuspendedWhileInactive	= true;
				Period						= 20;

				AddPlot(Brushes.DodgerBlue, Custom.Resource.NinjaScriptIndicatorNamePercentB);
			}
		}

		protected override void OnBarUpdate()
		{
			double band = 4 * StdDev(Period)[0];

			if (band == 0)
			{
				Value[0] = 0;
				return;
			}

			double lowerBand = SMA(Period)[0] - 2 * StdDev(Period)[0];

			Value[0] = (Input[0] - lowerBand) / band;
		}

		#region Properties
		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Period", GroupName = "NinjaScriptParameters", Order = 0)]
		public int Period { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private PercentB[] cachePercentB;
		public PercentB PercentB(int period)
		{
			return PercentB(Input, period);
		}

		public PercentB PercentB(ISeries<double> input, int period)
		{
			if (cachePercentB != null)
				for (int idx = 0; idx < cachePercentB.Length; idx++)
					if (cachePercentB[idx] != null && cachePercentB[idx].Period == period && cachePercentB[idx].EqualsInput(input))
						return cachePercentB[idx];
			return CacheIndicator<PercentB>(new PercentB(){ Period = period }, input, ref cachePercentB);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.PercentB PercentB(int period)
		{
			return indicator.PercentB(Input, period);
		}

		public Indicators.PercentB PercentB(ISeries<double> input , int period)
		{
			return indicator.PercentB(input, period);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.PercentB PercentB(int period)
		{
			return indicator.PercentB(Input, period);
		}

		public Indicators.PercentB PercentB(ISeries<double> input , int period)
		{
			return indicator.PercentB(input, period);
		}
	}
}

#endregion