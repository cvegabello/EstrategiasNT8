//
// Copyright (C) 2026, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;

#endregion

// This namespace holds indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
	/// <summary>
	/// FI (Force Index) combines price change and volume by calculating (Close − prior Close) × Volume,
	/// then smoothing it with an EMA. Positive values reflect stronger upward pressure with volume
	/// participation, while negative values reflect stronger downward pressure with volume participation.
	/// </summary>
	public class ForceIndex : Indicator
	{
		private Series<double>	rawForce;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= Custom.Resource.NinjaScriptIndicatorDescriptionForceIndex;
				Name						= Custom.Resource.NinjaScriptIndicatorNameForceIndex;
				IsOverlay					= false;
				IsSuspendedWhileInactive	= true;
				Period						= 14;

				AddPlot(new Stroke(Brushes.DarkCyan),		PlotStyle.Bar,	Custom.Resource.ForceIndexPositive);
				AddPlot(new Stroke(Brushes.Crimson),		PlotStyle.Bar,	Custom.Resource.ForceIndexNegative);

				AddLine(Brushes.DarkGray, 0,	Custom.Resource.NinjaScriptIndicatorZeroLine);
			}
			else if (State == State.DataLoaded)
				rawForce = new Series<double>(this);
		}

		protected override void OnBarUpdate()
		{
			rawForce[0]	= CurrentBar < 1 ? 0 : (Close[0] - Close[1]) * Volume[0];

			double val	= EMA(rawForce, Period)[0];

			if (val >= 0)
			{
				Positive[0]	= val;
				Negative[0]	= 0;
			}
			else
			{
				Positive[0]	= 0;
				Negative[0]	= val;
			}
		}

		#region Properties
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> Positive => Values[0];

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> Negative => Values[1];

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
		private ForceIndex[] cacheForceIndex;
		public ForceIndex ForceIndex(int period)
		{
			return ForceIndex(Input, period);
		}

		public ForceIndex ForceIndex(ISeries<double> input, int period)
		{
			if (cacheForceIndex != null)
				for (int idx = 0; idx < cacheForceIndex.Length; idx++)
					if (cacheForceIndex[idx] != null && cacheForceIndex[idx].Period == period && cacheForceIndex[idx].EqualsInput(input))
						return cacheForceIndex[idx];
			return CacheIndicator<ForceIndex>(new ForceIndex(){ Period = period }, input, ref cacheForceIndex);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ForceIndex ForceIndex(int period)
		{
			return indicator.ForceIndex(Input, period);
		}

		public Indicators.ForceIndex ForceIndex(ISeries<double> input , int period)
		{
			return indicator.ForceIndex(input, period);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ForceIndex ForceIndex(int period)
		{
			return indicator.ForceIndex(Input, period);
		}

		public Indicators.ForceIndex ForceIndex(ISeries<double> input , int period)
		{
			return indicator.ForceIndex(input, period);
		}
	}
}

#endregion