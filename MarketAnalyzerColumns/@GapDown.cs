// 
// Copyright (C) 2026, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using NinjaTrader.Data;
#endregion

//This namespace holds Market Analyzer columns in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public class GapDown : MarketAnalyzerColumn
	{
		private	double priorLow		= double.NaN;
		private	double sessionHigh	= double.NaN;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name					= NinjaTrader.Custom.Resource.NinjaScriptMarketAnalyzerColumnNameGapDown;
				Description				= NinjaTrader.Custom.Resource.NinjaScriptMarketAnalyzerColumnDescriptionGapDown;

				Calculate				= Calculate.OnPriceChange;
				DaysBack				= 5;
				FormatDecimals			= 2;
				IsDataSeriesRequired	= true;
				IsStableSession			= true;
				IsTickReplay			= false;
				MaximumBarsLookBack		= MaximumBarsLookBack.Infinite;
				RangeType				= Cbi.RangeType.Days;
				StartDate				= Core.Globals.Now.AddDays(-DaysBack);
			}
			else if (State == State.Realtime)
			{
				if (Instrument?.MarketData?.DailyHigh is { Price: double price })
					sessionHigh = price;
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 1)
				return;

			if (double.IsNaN(priorLow) || Bars.IsFirstBarOfSession)
				priorLow = PriorDayOHLC().PriorLow[0];

			sessionHigh = CurrentDayOHL().CurrentHigh[0];
			SetCurrentValue();
		}

		protected override void OnMarketData(Data.MarketDataEventArgs marketDataUpdate)
		{
			if (marketDataUpdate.IsReset) 
				CurrentValue = double.MinValue;

			if (marketDataUpdate.MarketDataType != Data.MarketDataType.DailyHigh)
				return;

			sessionHigh	= marketDataUpdate.Price;
			SetCurrentValue();
		}

		protected void SetCurrentValue() =>
		CurrentValue = (priorLow, sessionHigh) switch
		{
			( > 0, > 0)		=> sessionHigh - priorLow,
			_				=> double.MinValue
		};

		#region Miscellaneous
		public override string Format(double value)
		{
			return value == double.MinValue ? string.Empty : Instrument.MasterInstrument.FormatPrice(value);
		}
		#endregion
	}
}
