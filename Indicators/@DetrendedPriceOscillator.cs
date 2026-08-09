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
	/// DPO (Detrended Price Oscillator) removes longer-term trend influence by comparing a past price value
	/// to a shifted simple moving average. The result oscillates around zero and is used to highlight
	/// shorter-term cycles and swings without the longer-term trend component.
	/// </summary>
	public class DetrendedPriceOscillator : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= Custom.Resource.NinjaScriptIndicatorDescriptionDetrendedPriceOscillator;
				Name						= Custom.Resource.NinjaScriptIndicatorNameDetrendedPriceOscillator;
				IsOverlay					= false;
				IsSuspendedWhileInactive	= true;
				Period						= 14;

				AddPlot(Brushes.DodgerBlue,	Custom.Resource.NinjaScriptIndicatorNameDetrendedPriceOscillator);
				AddLine(Brushes.DarkGray, 0,	Custom.Resource.NinjaScriptIndicatorZeroLine);
			}
		}

		protected override void OnBarUpdate()
		{
			int lookback	= Period / 2 + 1;

			Value[0]		= CurrentBar < lookback ? 0 : Input[lookback] - SMA(Period)[0];
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
		private DetrendedPriceOscillator[] cacheDetrendedPriceOscillator;
		public DetrendedPriceOscillator DetrendedPriceOscillator(int period)
		{
			return DetrendedPriceOscillator(Input, period);
		}

		public DetrendedPriceOscillator DetrendedPriceOscillator(ISeries<double> input, int period)
		{
			if (cacheDetrendedPriceOscillator != null)
				for (int idx = 0; idx < cacheDetrendedPriceOscillator.Length; idx++)
					if (cacheDetrendedPriceOscillator[idx] != null && cacheDetrendedPriceOscillator[idx].Period == period && cacheDetrendedPriceOscillator[idx].EqualsInput(input))
						return cacheDetrendedPriceOscillator[idx];
			return CacheIndicator<DetrendedPriceOscillator>(new DetrendedPriceOscillator(){ Period = period }, input, ref cacheDetrendedPriceOscillator);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.DetrendedPriceOscillator DetrendedPriceOscillator(int period)
		{
			return indicator.DetrendedPriceOscillator(Input, period);
		}

		public Indicators.DetrendedPriceOscillator DetrendedPriceOscillator(ISeries<double> input , int period)
		{
			return indicator.DetrendedPriceOscillator(input, period);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.DetrendedPriceOscillator DetrendedPriceOscillator(int period)
		{
			return indicator.DetrendedPriceOscillator(Input, period);
		}

		public Indicators.DetrendedPriceOscillator DetrendedPriceOscillator(ISeries<double> input , int period)
		{
			return indicator.DetrendedPriceOscillator(input, period);
		}
	}
}

#endregion