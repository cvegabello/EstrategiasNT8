//
// Copyright (C) 2026, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
#endregion

// This namespace holds indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
	/// <summary>
	/// MI (Mass Index) measures range expansion by applying a double EMA to the bar range (High − Low)
	/// and summing the ratio of the single EMA to the double EMA over the specified lookback.
	/// Rising values indicate persistent range expansion, while falling values indicate range contraction.
	/// </summary>
	public class MassIndex : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= Custom.Resource.NinjaScriptIndicatorDescriptionMassIndex;
				Name						= Custom.Resource.NinjaScriptIndicatorNameMassIndex;
				IsOverlay					= false;
				IsSuspendedWhileInactive	= true;
				EmaPeriod					= 9;
				Period						= 25;

				AddPlot(Brushes.DodgerBlue, Custom.Resource.NinjaScriptIndicatorNameMassIndex);
			}
		}

		protected override void OnBarUpdate()
		{
			double sum = 0;

			for (int i = 0; i < Math.Min(CurrentBar + 1, Period); i++)
			{
				double denom = EMA(EMA(Range(), EmaPeriod), EmaPeriod)[i];

				if (denom != 0)
					sum += EMA(Range(), EmaPeriod)[i] / denom;
			}

			Value[0] = sum;
		}

		#region Properties
		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "EmaPeriod", GroupName = "NinjaScriptParameters", Order = 0)]
		public int EmaPeriod { get; set; }

		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "SumPeriod", GroupName = "NinjaScriptParameters", Order = 1)]
		public int Period { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MassIndex[] cacheMassIndex;
		public MassIndex MassIndex(int emaPeriod, int period)
		{
			return MassIndex(Input, emaPeriod, period);
		}

		public MassIndex MassIndex(ISeries<double> input, int emaPeriod, int period)
		{
			if (cacheMassIndex != null)
				for (int idx = 0; idx < cacheMassIndex.Length; idx++)
					if (cacheMassIndex[idx] != null && cacheMassIndex[idx].EmaPeriod == emaPeriod && cacheMassIndex[idx].Period == period && cacheMassIndex[idx].EqualsInput(input))
						return cacheMassIndex[idx];
			return CacheIndicator<MassIndex>(new MassIndex(){ EmaPeriod = emaPeriod, Period = period }, input, ref cacheMassIndex);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MassIndex MassIndex(int emaPeriod, int period)
		{
			return indicator.MassIndex(Input, emaPeriod, period);
		}

		public Indicators.MassIndex MassIndex(ISeries<double> input , int emaPeriod, int period)
		{
			return indicator.MassIndex(input, emaPeriod, period);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MassIndex MassIndex(int emaPeriod, int period)
		{
			return indicator.MassIndex(Input, emaPeriod, period);
		}

		public Indicators.MassIndex MassIndex(ISeries<double> input , int emaPeriod, int period)
		{
			return indicator.MassIndex(input, emaPeriod, period);
		}
	}
}

#endregion