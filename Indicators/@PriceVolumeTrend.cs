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
	/// PVT (Price Volume Trend) is a cumulative volume-based indicator that adds or subtracts volume
	/// based on the percent change in close from the prior bar. The line rises when up moves occur on
	/// higher relative volume and falls when down moves occur on higher relative volume, providing a
	/// running view of price change weighted by volume.
	/// </summary>
	public class PriceVolumeTrend : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= Custom.Resource.NinjaScriptIndicatorDescriptionPriceVolumeTrend;
				Name						= Custom.Resource.NinjaScriptIndicatorNamePriceVolumeTrend;
				IsOverlay					= false;
				IsSuspendedWhileInactive	= true;

				AddPlot(Brushes.DodgerBlue, Custom.Resource.NinjaScriptIndicatorNamePriceVolumeTrend);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 1)
			{
				Value[0] = 0;
				return;
			}

			if (Close[1] == 0)
			{
				Value[0] = Value[1];
				return;
			}

			double percentChange = (Close[0] - Close[1]) / Close[1];

			Value[0] = percentChange * Volume[0] + Value[1];
		}
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private PriceVolumeTrend[] cachePriceVolumeTrend;
		public PriceVolumeTrend PriceVolumeTrend()
		{
			return PriceVolumeTrend(Input);
		}

		public PriceVolumeTrend PriceVolumeTrend(ISeries<double> input)
		{
			if (cachePriceVolumeTrend != null)
				for (int idx = 0; idx < cachePriceVolumeTrend.Length; idx++)
					if (cachePriceVolumeTrend[idx] != null &&  cachePriceVolumeTrend[idx].EqualsInput(input))
						return cachePriceVolumeTrend[idx];
			return CacheIndicator<PriceVolumeTrend>(new PriceVolumeTrend(), input, ref cachePriceVolumeTrend);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.PriceVolumeTrend PriceVolumeTrend()
		{
			return indicator.PriceVolumeTrend(Input);
		}

		public Indicators.PriceVolumeTrend PriceVolumeTrend(ISeries<double> input)
		{
			return indicator.PriceVolumeTrend(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.PriceVolumeTrend PriceVolumeTrend()
		{
			return indicator.PriceVolumeTrend(Input);
		}

		public Indicators.PriceVolumeTrend PriceVolumeTrend(ISeries<double> input)
		{
			return indicator.PriceVolumeTrend(input);
		}
	}
}

#endregion