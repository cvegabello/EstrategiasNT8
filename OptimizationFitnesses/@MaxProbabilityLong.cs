// 
// Copyright (C) 2026, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
namespace NinjaTrader.NinjaScript.OptimizationFitnesses
{
	public class MaxProbablityLong : OptimizationFitness
	{
		protected override void OnCalculatePerformanceValue(StrategyBase strategy) => Value = strategy.SystemPerformance.LongTrades.TradesPerformance.Probability;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
				Name = Custom.Resource.NinjaScriptOptimizationFitnessNameMaxProbablityLong;
		}
	}
}
