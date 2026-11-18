//
// Copyright (C) 2026, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
// PullingStacking.cs
// SuperDOM Column - Displays pulling/stacking for both bid and ask depth in a single column.
//
//   Ask side (best ask and above):  current ask depth minus base ask depth
//   Bid side (best bid and below):  current bid depth minus base bid depth
//   Spread cells (between bid/ask): transparent background, no value
//
//   Positive value (stacking) → current depth is larger than the base
//   Negative value (pulling)  → current depth is smaller than the base
//
// Base value behaviour:
//   - Captured once on the first time a price enters the depth window
//   - Never overwritten while the price remains in the window
//   - Only evicted after the price has been absent for 1 second
//   - Re-captured fresh the next time the price re-enters the window
//
// Click the column header to reset all base values.
//
#region Using declarations
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui.SuperDom;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
#endregion

namespace NinjaTrader.NinjaScript.SuperDomColumns
{
	public class PullingStacking : SuperDomColumn
	{
		public enum HAlign { Left, Center, Right }
		private const int EvictionDelayMs = 1000;

		private bool		eventsSubscribed;
		private FontFamily	fontFamily;
		private Pen			gridPen;
		private double		halfPenWidth;
		private Typeface	typeFace;

		// Separate base-depth dictionaries for ask and bid sides
		private ConcurrentDictionary<double, long>	askBaseDepth;
		private ConcurrentDictionary<double, long>	bidBaseDepth;

		// Separate eviction timers for ask and bid sides
		private ConcurrentDictionary<double, Timer>	askEvictionTimers;
		private ConcurrentDictionary<double, Timer>	bidEvictionTimers;

		// Reusable per-render snapshots of the visible depth window; OnRender runs on the UI thread only
		private Dictionary<double, long>			askVolumeSnapshot;
		private Dictionary<double, long>			bidVolumeSnapshot;

		public override void OnColumnLabelClicked(object sender, MouseButtonEventArgs e)
		{
			foreach (Timer t in askEvictionTimers.Values) t.Dispose();
			askEvictionTimers.Clear();
			askBaseDepth.Clear();

			foreach (Timer t in bidEvictionTimers.Values) t.Dispose();
			bidEvictionTimers.Clear();
			bidBaseDepth.Clear();

			SeedFromCurrentDepth();		// re-capture the base immediately so the column doesn't blank until the next depth update
			OnPropertyChanged();
		}

		private void OnMarketDepthUpdate(object sender, Data.MarketDepthEventArgs e)
		{
			Data.MarketDepth<LadderRow>	marketDepth		= SuperDom?.MarketDepth;
			List<LadderRow>				marketDepthAsks	= marketDepth?.Asks;
			List<LadderRow>				marketDepthBids	= marketDepth?.Bids;

			if (marketDepth == null) return;

			int levels = SuperDom.DepthLevels;

			lock (marketDepth.Instrument.SyncMarketDepth)
			{
				if (marketDepthAsks != null)
				{
					HashSet<double> activePrices = new HashSet<double>();
					for (int i = 0; i < Math.Min(marketDepthAsks.Count, levels); i++)
					{
						LadderRow row = marketDepthAsks[i];
						if (row.Volume <= 0) continue;

						activePrices.Add(row.Price);

						if (askEvictionTimers.TryRemove(row.Price, out Timer existing))
							existing.Dispose();

						askBaseDepth.TryAdd(row.Price, row.Volume);
					}

					foreach (double price in askBaseDepth.Keys)
						if (!activePrices.Contains(price))
							askEvictionTimers.GetOrAdd(price, p =>
								new Timer(OnAskEvictionTimer, p, EvictionDelayMs, Timeout.Infinite));
				}

				if (marketDepthBids != null)
				{
					HashSet<double> activePrices = new HashSet<double>();
					for (int i = 0; i < Math.Min(marketDepthBids.Count, levels); i++)
					{
						LadderRow row = marketDepthBids[i];
						if (row.Volume <= 0) continue;

						activePrices.Add(row.Price);

						if (bidEvictionTimers.TryRemove(row.Price, out Timer existing))
							existing.Dispose();

						bidBaseDepth.TryAdd(row.Price, row.Volume);
					}

					foreach (double price in bidBaseDepth.Keys)
						if (!activePrices.Contains(price))
							bidEvictionTimers.GetOrAdd(price, p =>
								new Timer(OnBidEvictionTimer, p, EvictionDelayMs, Timeout.Infinite));
				}
			}

			// Always repaint so the difference values stay current
			OnPropertyChanged();
		}

		private void OnAskEvictionTimer(object state)
		{
			double price = (double)state;

			if (SuperDom?.MarketDepth?.Asks != null)
			{
				int levels = SuperDom.DepthLevels;
				lock (SuperDom.MarketDepth.Instrument.SyncMarketDepth)
					for (int i = 0; i < Math.Min(SuperDom.MarketDepth.Asks.Count, levels); i++)
						if (SuperDom.MarketDepth.Asks[i].Price.ApproxCompare(price) == 0)
						{
							if (askEvictionTimers.TryRemove(price, out Timer t)) t.Dispose();
							return;
						}
			}

			askEvictionTimers.TryRemove(price, out Timer timer);
			timer?.Dispose();

			if (askBaseDepth.TryRemove(price, out _))
				OnPropertyChanged();
		}

		private void OnBidEvictionTimer(object state)
		{
			double price = (double)state;

			if (SuperDom?.MarketDepth?.Bids != null)
			{
				int levels = SuperDom.DepthLevels;
				lock (SuperDom.MarketDepth.Instrument.SyncMarketDepth)
					for (int i = 0; i < Math.Min(SuperDom.MarketDepth.Bids.Count, levels); i++)
						if (SuperDom.MarketDepth.Bids[i].Price.ApproxCompare(price) == 0)
						{
							if (bidEvictionTimers.TryRemove(price, out Timer t)) t.Dispose();
							return;
						}
			}

			bidEvictionTimers.TryRemove(price, out Timer timer);
			timer?.Dispose();

			if (bidBaseDepth.TryRemove(price, out _))
				OnPropertyChanged();
		}

		private void SeedFromCurrentDepth()
		{
			if (SuperDom?.MarketDepth == null) return;

			bool changed = false;
			int  levels  = SuperDom.DepthLevels;

			lock (SuperDom.MarketDepth.Instrument.SyncMarketDepth)
			{
				if (SuperDom.MarketDepth.Asks != null)
					for (int i = 0; i < Math.Min(SuperDom.MarketDepth.Asks.Count, levels); i++)
					{
						LadderRow row = SuperDom.MarketDepth.Asks[i];
						if (row.Volume <= 0) continue;
						if (askBaseDepth.TryAdd(row.Price, row.Volume)) changed = true;
					}

				if (SuperDom.MarketDepth.Bids != null)
					for (int i = 0; i < Math.Min(SuperDom.MarketDepth.Bids.Count, levels); i++)
					{
						LadderRow row = SuperDom.MarketDepth.Bids[i];
						if (row.Volume <= 0) continue;
						if (bidBaseDepth.TryAdd(row.Price, row.Volume)) changed = true;
					}
			}

			if (changed)
				OnPropertyChanged();
		}

		protected override void OnRender(DrawingContext dc, double renderWidth)
		{
			if (!IsVisible) return;

			if (gridPen == null)
			{
				if (UiWrapper != null && PresentationSource.FromVisual(UiWrapper)?.CompositionTarget is { } target)
				{
					Matrix m		= target.TransformToDevice;
					double dpiFactor= 1 / m.M11;
					gridPen			= new Pen(Application.Current.TryFindResource("BorderThinBrush") as Brush, 1 * dpiFactor);
					halfPenWidth	= gridPen.Thickness * 0.5;
				}
			}

			if (gridPen == null) return;

			double verticalOffset	= -gridPen.Thickness;
			double pixelsPerDip		= VisualTreeHelper.GetDpi(UiWrapper).PixelsPerDip;

			// Snapshot best bid/ask and the visible depth window once per render pass so each row below is an O(1) dictionary lookup
			double	bestBid		= double.MinValue;
			double	bestAsk		= double.MaxValue;
			int		depthLevels	= SuperDom.DepthLevels;

			askVolumeSnapshot.Clear();
			bidVolumeSnapshot.Clear();

			// Snapshot MarketDepth so a concurrent shutdown/disconnect cannot null it between the check and the lock
			Data.MarketDepth<LadderRow> marketDepth = SuperDom.MarketDepth;
			if (marketDepth != null)
			{
				lock (marketDepth.Instrument.SyncMarketDepth)
				{
					List<LadderRow> asks = marketDepth.Asks;
					List<LadderRow> bids = marketDepth.Bids;

					if (bids?.Count > 0)
						bestBid = bids[0].Price;
					if (asks?.Count > 0)
						bestAsk = asks[0].Price;

if (asks != null)
{
	int askCount = Math.Min(asks.Count, depthLevels);
	for (int i = 0; i < askCount; i++)
		askVolumeSnapshot[asks[i].Price] = asks[i].Volume;
}

if (bids != null)
{
	int bidCount = Math.Min(bids.Count, depthLevels);
	for (int i = 0; i < bidCount; i++)
		bidVolumeSnapshot[bids[i].Price] = bids[i].Volume;
}
				}
			}

			lock (SuperDom.Rows)
				foreach (PriceRow row in SuperDom.Rows)
				{
					if (renderWidth - halfPenWidth >= 0)
					{
						Rect rect = new(-halfPenWidth, verticalOffset, renderWidth - halfPenWidth, SuperDom.ActualRowHeight);

						GuidelineSet guidelines = new();
						guidelines.GuidelinesX.Add(rect.Left   + halfPenWidth);
						guidelines.GuidelinesX.Add(rect.Right  + halfPenWidth);
						guidelines.GuidelinesY.Add(rect.Top    + halfPenWidth);
						guidelines.GuidelinesY.Add(rect.Bottom + halfPenWidth);

						dc.PushGuidelineSet(guidelines);

						// ── Background: bid zone, ask zone, or transparent spread ──
						bool isAskZone = bestAsk < double.MaxValue && row.Price.ApproxCompare(bestAsk) >= 0;
						bool isBidZone = bestBid > double.MinValue && row.Price.ApproxCompare(bestBid) <= 0;

						Brush cellBackground = isAskZone ? AskBackColor
							                 : isBidZone ? BidBackColor
							                 : Brushes.Transparent;

						dc.DrawRectangle(cellBackground, null, rect);
						dc.DrawLine(gridPen, new Point(-gridPen.Thickness, rect.Bottom), new Point(renderWidth - halfPenWidth, rect.Bottom));
						dc.DrawLine(gridPen, new Point(rect.Right, verticalOffset), new Point(rect.Right, rect.Bottom));

						// ── Value: look up live volume from the per-render snapshot, ask side first ──
						bool isAskDepth = askVolumeSnapshot.TryGetValue(row.Price, out long currentVol) && currentVol > 0;
						if (!isAskDepth)
							bidVolumeSnapshot.TryGetValue(row.Price, out currentVol);

						// Only render text if there is live depth AND a stored base for this price
						long baseVol = 0;
						bool hasBase = isAskDepth
							? askBaseDepth.TryGetValue(row.Price, out baseVol)
							: bidBaseDepth.TryGetValue(row.Price, out baseVol);

						if (currentVol > 0 && hasBase && baseVol > 0)
						{
							long  diff	= currentVol - baseVol;
							Brush fg	= diff >= 0 ? PositiveForeColor : NegativeForeColor;

							fontFamily	= SuperDom.Font.Family;
							typeFace	= new Typeface(fontFamily,
								SuperDom.Font.Italic ? FontStyles.Italic : FontStyles.Normal,
								SuperDom.Font.Bold   ? FontWeights.Bold  : FontWeights.Normal,
								FontStretches.Normal);

							if (renderWidth - 8 > 0)
							{
								HAlign align = isAskDepth ? AskTextAlignment : BidTextAlignment;
								FormattedText text = new(
									diff.ToString(Core.Globals.GeneralOptions.CurrentCulture),
									Core.Globals.GeneralOptions.CurrentCulture,
									FlowDirection.LeftToRight,
									typeFace,
									SuperDom.Font.Size,
									fg,
									pixelsPerDip)
								{
									MaxLineCount  = 1,
									MaxTextWidth  = renderWidth - 8,
									Trimming      = TextTrimming.CharacterEllipsis,
									TextAlignment = align == HAlign.Left  ? System.Windows.TextAlignment.Left
													: align == HAlign.Right ? System.Windows.TextAlignment.Right
													: System.Windows.TextAlignment.Center
								};

								dc.DrawText(text, new Point(4, verticalOffset + (SuperDom.ActualRowHeight - text.Height) / 2));
							}
						}

						dc.Pop();
						verticalOffset += SuperDom.ActualRowHeight;
					}
				}
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name					= Gui.Resource.NinjaScriptSuperDomColumnPullingStackingLabel;
				Description				= Gui.Resource.NinjaScriptSuperdomColumnPullingStackingDescription;
				DefaultWidth			= 80;
				PreviousWidth			= -1;
				IsDataSeriesRequired	= false;
				AskBackColor			= new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0x66, 0x66));
				BidBackColor			= new SolidColorBrush(Color.FromArgb(0x25, 0x64, 0x95, 0xED));
				PositiveForeColor		= Application.Current.TryFindResource("FontControlBrush") as Brush;
				NegativeForeColor		= new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x00, 0x00));
				AskTextAlignment		= HAlign.Left;
				BidTextAlignment		= HAlign.Left;
				IsVisible				= true;
				askBaseDepth			= new ConcurrentDictionary<double, long>();
				bidBaseDepth			= new ConcurrentDictionary<double, long>();
				askEvictionTimers		= new ConcurrentDictionary<double, Timer>();
				bidEvictionTimers		= new ConcurrentDictionary<double, Timer>();
				askVolumeSnapshot		= new Dictionary<double, long>();
				bidVolumeSnapshot		= new Dictionary<double, long>();
			}
			else if (State == State.Configure)
			{
				if (UiWrapper != null && PresentationSource.FromVisual(UiWrapper)?.CompositionTarget is { } target)
				{
					Matrix m		= target.TransformToDevice;
					double dpiFactor= 1 / m.M11;
					gridPen			= new Pen(Application.Current.TryFindResource("BorderThinBrush") as Brush, 1 * dpiFactor);
					halfPenWidth	= gridPen.Thickness * 0.5;
				}
			}
			else if (State == State.Active)
			{
				SeedFromCurrentDepth();

				if (!eventsSubscribed && SuperDom?.MarketDepth != null)
				{
					WeakEventManager<Data.MarketDepth<LadderRow>, Data.MarketDepthEventArgs>
						.AddHandler(SuperDom.MarketDepth, "Update", OnMarketDepthUpdate);
					eventsSubscribed = true;
				}
			}
			else if (State == State.Terminated)
			{
				if (eventsSubscribed && SuperDom?.MarketDepth != null)
				{
					WeakEventManager<Data.MarketDepth<LadderRow>, Data.MarketDepthEventArgs>
						.RemoveHandler(SuperDom.MarketDepth, "Update", OnMarketDepthUpdate);
					eventsSubscribed = false;
				}

				foreach (Timer t in askEvictionTimers.Values) t.Dispose();
				askEvictionTimers.Clear();
				askBaseDepth.Clear();

				foreach (Timer t in bidEvictionTimers.Values) t.Dispose();
				bidEvictionTimers.Clear();
				bidBaseDepth.Clear();
			}
		}

		#region Properties
		// Setup
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptAskTextAlignment", GroupName = "NinjaScriptSetup", Order = 100)]
		public HAlign AskTextAlignment { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptBidTextAlignment", GroupName = "NinjaScriptSetup", Order = 110)]
		public HAlign BidTextAlignment { get; set; }

		// Visual
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptRecentColumnAskBackground", GroupName = "PropertyCategoryVisual", Order = 105)]
		public Brush AskBackColor { get; set; }

		[Browsable(false)]
		public string AskBackColorSerialize
		{
			get => Gui.Serialize.BrushToString(AskBackColor, "brushAskPriceColumnBackground");
			set => AskBackColor = Gui.Serialize.StringToBrush(value, "brushAskPriceColumnBackground");
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptRecentColumnBidBackground", GroupName = "PropertyCategoryVisual", Order = 116)]
		public Brush BidBackColor { get; set; }

		[Browsable(false)]
		public string BidBackColorSerialize
		{
			get => Gui.Serialize.BrushToString(BidBackColor, "brushBidPriceColumnBackground");
			set => BidBackColor = Gui.Serialize.StringToBrush(value, "brushBidPriceColumnBackground");
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptPositiveForeground", GroupName = "PropertyCategoryVisual", Order = 135)]
		public Brush PositiveForeColor { get; set; }

		[Browsable(false)]
		public string PositiveForeColorSerialize
		{
			get => Gui.Serialize.BrushToString(PositiveForeColor, "FontControlBrush");
			set => PositiveForeColor = Gui.Serialize.StringToBrush(value, "FontControlBrush");
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptNegativeForeground", GroupName = "PropertyCategoryVisual", Order = 140)]
		public Brush NegativeForeColor { get; set; }

		[Browsable(false)]
		public string NegativeForeColorSerialize
		{
			get => Gui.Serialize.BrushToString(NegativeForeColor);
			set => NegativeForeColor = Gui.Serialize.StringToBrush(value);
		}
		#endregion
	}
}
