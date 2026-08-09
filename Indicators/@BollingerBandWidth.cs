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
	/// BBWidth (Bollinger Band Width) measures Bollinger Band width as a percentage of the moving average,
	/// reflecting how wide the bands are relative to price level. Higher values indicate expanding volatility
	/// and lower values indicate contracting volatility.
	/// </summary>
	public class BollingerBandWidth : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= Custom.Resource.NinjaScriptIndicatorDescriptionBollingerBandWidth;
				Name						= Custom.Resource.NinjaScriptIndicatorNameBollingerBandWidth;
				IsOverlay					= false;
				IsSuspendedWhileInactive	= true;
				Period						= 20;

				AddPlot(Brushes.DodgerBlue, Custom.Resource.NinjaScriptIndicatorNameBollingerBandWidth);
			}
		}

		protected override void OnBarUpdate()
		{
			double avg = SMA(Period)[0];

			Value[0] = avg == 0 ? 0 : 4 * StdDev(Period)[0] / avg * 100;
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
		private BollingerBandWidth[] cacheBollingerBandWidth;
		public BollingerBandWidth BollingerBandWidth(int period)
		{
			return BollingerBandWidth(Input, period);
		}

		public BollingerBandWidth BollingerBandWidth(ISeries<double> input, int period)
		{
			if (cacheBollingerBandWidth != null)
				for (int idx = 0; idx < cacheBollingerBandWidth.Length; idx++)
					if (cacheBollingerBandWidth[idx] != null && cacheBollingerBandWidth[idx].Period == period && cacheBollingerBandWidth[idx].EqualsInput(input))
						return cacheBollingerBandWidth[idx];
			return CacheIndicator<BollingerBandWidth>(new BollingerBandWidth(){ Period = period }, input, ref cacheBollingerBandWidth);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BollingerBandWidth BollingerBandWidth(int period)
		{
			return indicator.BollingerBandWidth(Input, period);
		}

		public Indicators.BollingerBandWidth BollingerBandWidth(ISeries<double> input , int period)
		{
			return indicator.BollingerBandWidth(input, period);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BollingerBandWidth BollingerBandWidth(int period)
		{
			return indicator.BollingerBandWidth(Input, period);
		}

		public Indicators.BollingerBandWidth BollingerBandWidth(ISeries<double> input , int period)
		{
			return indicator.BollingerBandWidth(input, period);
		}
	}
}

#endregion