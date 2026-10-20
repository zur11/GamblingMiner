using Godot;
using System;
using System.Globalization;
using Scripts.Finance;
using UI.StatusBar;

public partial class ClientsBetsHistory : Control
{
	private CasinoScBalanceService _casinoSc;
	private UserStatsService _userStats;
	private CasinoClientLedgerService _ledger;
	private SimulationService _simService;
	private SceneManager _sceneManager;

	private Label _overallTotalLabel;
	private Label _overallPlLabel;
	private Label _totalWageredAllLabel;
	private OptionButton _gameFilter;
	private OptionButton _clientFilter;
	private VBoxContainer _clientRows;
	private VBoxContainer _liveFeedVBox;

	private const int MaxFeedEntries = 50;
	// Only used until the first row has been laid out. Deliberately small: underestimating the pitch paints a few
	// rows too many, overestimating would leave visible rows stale.
	private const float FallbackFeedRowPitch = 16f;

	// Mini-plan 25 17.C.3 — no 2 s timer, and no work per bet beyond a ring write. ClientBetSettled fires on every
	// settled bet of all five clients: M0 counted it at 450 a second at 9000X, and each one created a Label, freed
	// another and recomputed the summary (§38.7 rule 1). Now:
	// - the FEED is DiceGame's bet-list design (BetHistoryContainer, mini-plan 10), at the developer's request
	//   (2026-10-19: "as fluid as DiceGame"). A settled bet only goes into a ring. The rows are built once and never
	//   move: row i always shows the i-th newest bet. Once per frame, if anything changed, only the rows inside the
	//   scroll's visible window are rewritten. The cost per frame is bounded by the VISIBLE rows, whatever the bet
	//   rate. (A first version flushed every 0.1 s by moving rows to the top, and read as jumpy.) DiceGame's own
	//   component cannot be reused as is: this feed needs the client's name and the casino's side of each bet.
	// - the SUMMARY repaints at most every SummaryIntervalSeconds, StatusBar's cadence.
	// - the client ROWS, which the old timer covered: their per-client bet stats move on every bet and no other event
	//   announces them. They rebuild through a leading-edge throttle, at most every RowsMinRefreshInterval, the old
	//   timer's rate. LedgerChanged (a deposit or a recharge moves the "since" baselines and dates) marks them too.
	// At rest: 0 refreshes a second (the timer was 0.5).
	private readonly record struct FeedItem(string DisplayName, BetTransactionEvent Bet, string GameId);
	private readonly FeedItem[] _feedRing = new FeedItem[MaxFeedEntries];
	private int _feedHead;
	private int _feedCount;
	// Every push shifts every row's content by one, so one counter says which rows are current.
	private int _feedVersion;
	private readonly int[] _feedRowVersion = new int[MaxFeedEntries];
	private readonly Label[] _feedRows = new Label[MaxFeedEntries];
	private int _visibleFeedRows;
	private bool _feedDirty;
	private ScrollContainer _feedScroll;

	// §29.13 — the time column may not show a finer field than the clock can honestly carry at this speed: the same
	// measured unit DiceGame hands its own list (mini-plan 20 C).
	private CalendarTimeService _calendar;
	private readonly UI.Readouts.AdaptiveReadoutSampler _clockSampler = new();
	private UI.Readouts.AdaptiveReadoutSampler.ClockUnit _paintedUnit = UI.Readouts.AdaptiveReadoutSampler.ClockUnit.Seconds;

	private bool _summaryDirty;
	private double _sinceSummaryPaint = double.MaxValue;
	private const double SummaryIntervalSeconds = 0.1;
	private bool _rowsDirty;
	private double _sinceRowsRebuild = double.MaxValue;
	private const double RowsMinRefreshInterval = 2.0;

	public override void _Ready()
	{
		_casinoSc     = GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService");
		_userStats    = GetNodeOrNull<UserStatsService>("/root/UserStatsService");
		_ledger       = GetNodeOrNull<CasinoClientLedgerService>("/root/CasinoClientLedgerService");
		_simService   = GetNodeOrNull<SimulationService>("/root/SimulationService");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());

		_overallTotalLabel     = GetNode<Label>("%OverallTotalLabel");
		_overallPlLabel        = GetNode<Label>("%OverallPlLabel");
		_totalWageredAllLabel  = GetNode<Label>("%TotalWageredAllLabel");
		_gameFilter            = GetNode<OptionButton>("%GameFilter");
		_clientFilter          = GetNode<OptionButton>("%ClientFilter");
		_clientRows            = GetNode<VBoxContainer>("%ClientRows");
		_liveFeedVBox          = GetNode<VBoxContainer>("%LiveFeedVBox");
		_calendar              = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");

		// The row pool: built once, shown as bets fill it, never reordered.
		for (int i = 0; i < MaxFeedEntries; i++)
		{
			// PASS, not a Label's default IGNORE: the row's tooltip carries the full instant (the time column may be
			// coarsened, §29.13), and PASS still hands the wheel on to the scroll (Ch. 29 §29.3 trap 3).
			var row = new Label { Visible = false, MouseFilter = MouseFilterEnum.Pass };
			row.AddThemeFontSizeOverride("font_size", 16);
			_liveFeedVBox.AddChild(row);
			_feedRows[i] = row;
		}

		// Rows scrolled into view are written the frame they appear.
		_feedScroll = _liveFeedVBox.GetParent() as ScrollContainer;
		if (_feedScroll != null)
		{
			_feedScroll.GetVScrollBar().ValueChanged += _ => _feedDirty = true;
			_feedScroll.Resized += () => _feedDirty = true;
		}

		_gameFilter.AddItem("All Games");
		_gameFilter.AddItem("Dice");
		_gameFilter.ItemSelected += _ => ClearLiveFeed();

		// ND.8f follow-up: per-client feed filter, mirroring the game filter (index 0 = all; index i > 0
		// maps to CanonicalClients[i − 1]).
		_clientFilter.AddItem("All Clients");
		foreach ((string _, string display) in CasinoClientLedgerService.CanonicalClients)
			_clientFilter.AddItem(display);
		_clientFilter.ItemSelected += _ => ClearLiveFeed();

		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.CasinoGamblingFinances);

		// ND.8f follow-up: the live feed shows EVERY casino client's settled bets (player autobet + bot_1..4)
		// via the typed ClientBetSettled event. Manual DiceGame bets can only happen while DiceGame itself is
		// the active scene, so the feed cannot miss them while this screen is open.
		if (_simService != null)
			_simService.ClientBetSettled += OnClientBetSettled;
		if (_ledger != null)
			_ledger.LedgerChanged += MarkRowsDirty;

		RefreshGlobalSummary();
		RefreshClientRows();
	}

	public override void _ExitTree()
	{
		if (_simService != null)
			_simService.ClientBetSettled -= OnClientBetSettled;
		if (_ledger != null)
			_ledger.LedgerChanged -= MarkRowsDirty;
	}

	private void MarkRowsDirty() => _rowsDirty = true;

	public override void _Process(double delta)
	{
		if (_calendar != null && _clockSampler.ShouldRepaint(delta))
		{
			_clockSampler.NoteRepaint(_calendar.CurrentLocalDateTime);
			if (_clockSampler.Unit != _paintedUnit)
			{
				// A new precision is a new rendering of every row, not just the ones whose bet changed.
				_paintedUnit = _clockSampler.Unit;
				_feedVersion++;
				_feedDirty = true;
			}
		}

		if (_feedDirty)
		{
			_feedDirty = false;
			FlushLiveFeed();
		}

		_sinceSummaryPaint += delta;
		if (_summaryDirty && _sinceSummaryPaint >= SummaryIntervalSeconds)
		{
			_summaryDirty = false;
			_sinceSummaryPaint = 0d;
			RefreshGlobalSummary();
		}

		_sinceRowsRebuild += delta;
		if (_rowsDirty && _sinceRowsRebuild >= RowsMinRefreshInterval)
		{
			_rowsDirty = false;
			_sinceRowsRebuild = 0d;
			RefreshClientRows();
		}
	}

	// Called hundreds of times a second under the sim: a ring write and some flags, nothing else (see the fields).
	private void OnClientBetSettled(string nodeId, string gameId, BetTransactionEvent bet)
	{
		_summaryDirty = true;
		_rowsDirty = true;
		if (bet == null) return;

		int filterIdx = _gameFilter.Selected;
		bool gameMatches = filterIdx == 0 || (filterIdx == 1 && gameId == "Dice");

		int clientIdx = _clientFilter.Selected;
		bool clientMatches = clientIdx <= 0
			|| (clientIdx - 1 < CasinoClientLedgerService.CanonicalClients.Length
				&& CasinoClientLedgerService.CanonicalClients[clientIdx - 1].Id == nodeId);

		if (gameMatches && clientMatches)
		{
			_feedRing[_feedHead] = new FeedItem(DisplayNameFor(nodeId), bet, gameId);
			_feedHead = (_feedHead + 1) % MaxFeedEntries;
			if (_feedCount < MaxFeedEntries) _feedCount++;
			_feedVersion++;
			_feedDirty = true;
		}
	}

	// Once per frame at most, and only the visible rows whose content is out of date.
	private void FlushLiveFeed()
	{
		Scripts.Diagnostics.UiRefreshProbe.Count("ClientsBetsHistory.FlushLiveFeed");   // DEBUG-only, mini-plan 25
		SyncFeedRowVisibility();
		if (_feedCount == 0) return;

		string timePattern = UI.Readouts.AdaptiveReadoutSampler.TimePatternFor(_paintedUnit);
		string pattern = timePattern.Length == 0 ? "dd MMM yyyy" : "dd MMM yyyy " + timePattern;
		(int first, int last) = VisibleFeedRows();
		for (int row = first; row <= last; row++)
		{
			if (_feedRowVersion[row] == _feedVersion) continue;

			// Row 0 is the newest bet: the one just behind the ring's write head.
			int slot = (_feedHead - 1 - row + MaxFeedEntries * 2) % MaxFeedEntries;
			PaintFeedRow(_feedRows[row], _feedRing[slot], pattern);
			_feedRowVersion[row] = _feedVersion;
		}
	}

	// Rows [first, last] that intersect the scroll's viewport, one row of margin each side. The rows are uniform (one
	// line, one font), so row 0's pitch is every row's.
	private (int First, int Last) VisibleFeedRows()
	{
		int lastFilled = _feedCount - 1;
		if (_feedScroll == null) return (0, lastFilled);

		float rowHeight = _feedRows[0].Size.Y > 0f ? _feedRows[0].Size.Y : FallbackFeedRowPitch;
		float pitch = rowHeight + _liveFeedVBox.GetThemeConstant("separation");
		float top = _feedScroll.ScrollVertical;
		float height = _feedScroll.Size.Y > 0f ? _feedScroll.Size.Y : pitch * MaxFeedEntries;

		int first = Mathf.Max(0, (int)(top / pitch) - 1);
		int last = Mathf.Min(lastFilled, (int)((top + height) / pitch) + 1);
		return (first, last);
	}

	// Only rows that hold a bet are visible, so the scroll's content height tracks the number of bets. A no-op once
	// the feed is full, until a filter change clears it.
	private void SyncFeedRowVisibility()
	{
		if (_visibleFeedRows == _feedCount) return;
		for (int i = 0; i < MaxFeedEntries; i++)
			_feedRows[i].Visible = i < _feedCount;
		_visibleFeedRows = _feedCount;
	}

	private static string DisplayNameFor(string nodeId)
	{
		foreach ((string id, string display) in CasinoClientLedgerService.CanonicalClients)
			if (id == nodeId) return display;
		return nodeId;
	}

	private static void PaintFeedRow(Label label, FeedItem item, string timePattern)
	{
		BetTransactionEvent bet = item.Bet;
		string ts      = bet.Timestamp.ToLocalTime().ToString(timePattern, CultureInfo.InvariantCulture);
		string outcome = bet.IsWin ? "WIN " : "LOSS";
		decimal delta  = -bet.CreditedProfit;

		label.Text = string.Create(CultureInfo.InvariantCulture, $"{ts}  {item.DisplayName,-6}  {item.GameId}  Bet {bet.BetAmount:N8} SC  {outcome}  {bet.CreditedProfit:+0.00000000;-0.00000000} SC  → casino: {delta:+0.00000000;-0.00000000} SC");
		label.AddThemeColorOverride("font_color", bet.IsWin
			? new Color(1f, 0.5f, 0.4f)   // player win = casino loss → red-ish
			: new Color(0.4f, 1f, 0.5f));  // player loss = casino gain → green-ish
		label.TooltipText = bet.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
	}

	// A filter change starts the feed over: the ring empties and every row hides on the next flush.
	private void ClearLiveFeed()
	{
		_feedHead = 0;
		_feedCount = 0;
		Array.Clear(_feedRing);
		_feedVersion++;
		_feedDirty = true;
	}

	private void RefreshGlobalSummary()
	{
		// ND.8f: "all clients" is now literal — player (UserStatsService) + bot_1..4 (the casino's book).
		decimal totalWageredAll = _userStats?.Stats?.TotalAmountWagered ?? 0m;
		foreach ((string id, string _) in CasinoClientLedgerService.CanonicalClients)
		{
			if (id == "player") continue;
			CasinoClientLedgerService.ClientBetStats book = _ledger?.GetBetStats(id);
			totalWageredAll = Money.Normalize(totalWageredAll + (book?.TotalWagered ?? 0m));
		}
		_totalWageredAllLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Total SC wagered (all clients):  {totalWageredAll:N8} SC");

		if (_casinoSc == null) return;
		_overallTotalLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Casino since 21 Mar 2009  |  Total SC: {_casinoSc.TotalSc:N8} SC");

		decimal pl = _casinoSc.CumulativeProfitSinceLoan;
		string arrow = pl >= 0m ? "▲" : "▼";
		_overallPlLabel.Text = string.Create(CultureInfo.InvariantCulture, $"P/L vs loans:  {pl:+0.00000000;-0.00000000} SC  {arrow}");
		_overallPlLabel.AddThemeColorOverride("font_color", pl >= 0m
			? new Color(0.4f, 1f, 0.4f)
			: new Color(1f, 0.4f, 0.4f));
	}

	private void RefreshClientRows()
	{
		Scripts.Diagnostics.UiRefreshProbe.Count("ClientsBetsHistory.RefreshClientRows");   // DEBUG-only, mini-plan 25
		foreach (Node child in _clientRows.GetChildren())
			child.QueueFree();

		// ND.8f: one row per canonical client — the player sourced from UserStatsService (richer, already
		// checkpoint-consistent), the bots from the casino's own per-client bet-stats book.
		foreach ((string id, string display) in CasinoClientLedgerService.CanonicalClients)
			BuildClientRow(id, display);
	}

	private void BuildClientRow(string clientId, string displayName)
	{
		CasinoClientLedgerService.LedgerEntry lastDeposit   = _ledger?.GetLastDeposit(clientId);
		CasinoClientLedgerService.LedgerEntry lastRecharge  = _ledger?.GetLastAutoRecharge(clientId);

		string enrolledDate = lastDeposit != null
			? lastDeposit.UtcTimestamp.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
			: "—";

		decimal wageredLifetime;
		decimal profitLifetime;
		int totalBets;
		int wins;
		int losses;
		if (clientId == "player")
		{
			var stats = _userStats?.Stats;
			wageredLifetime = stats?.TotalAmountWagered ?? 0m;
			profitLifetime  = stats?.TotalProfit ?? 0m;
			totalBets       = stats?.TotalBets ?? 0;
			wins            = stats?.TotalWins ?? 0;
			losses          = stats?.TotalLosses ?? 0;
		}
		else
		{
			CasinoClientLedgerService.ClientBetStats book = _ledger?.GetBetStats(clientId);
			wageredLifetime = book?.TotalWagered ?? 0m;
			profitLifetime  = book?.NetProfit ?? 0m;
			totalBets       = book?.TotalBets ?? 0;
			wins            = book?.TotalWins ?? 0;
			losses          = book?.TotalLosses ?? 0;
		}

		decimal wageredSinceDeposit = Math.Max(0m, wageredLifetime - (lastDeposit?.TotalWageredSnapshot ?? 0m));
		decimal plLifetime          = -profitLifetime;
		decimal plSinceDeposit      = -(profitLifetime - (lastDeposit?.NetProfitSnapshot ?? 0m));
		decimal plSinceRecharge     = lastRecharge != null
			? -(profitLifetime - lastRecharge.NetProfitSnapshot)
			: plLifetime; // no recharge yet → same as all-time

		decimal winRate = totalBets > 0 ? (decimal)wins / totalBets * 100m : 0m;

		string rechargeDate = lastRecharge != null
			? lastRecharge.UtcTimestamp.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)
			: "never";

		AddRow(new HSeparator());
		AddRowLabel($"{displayName}   (enrolled: {enrolledDate})", 20);
		AddRowLabel(string.Create(CultureInfo.InvariantCulture, $"Bets: {totalBets}   Won: {wins}   Lost: {losses}   Win rate: {winRate:F2}%"), 18);
		AddRowLabel(string.Create(CultureInfo.InvariantCulture, $"Cumulative SC wagered (all time):    {wageredLifetime:N8} SC"), 18);
		AddRowLabel(string.Create(CultureInfo.InvariantCulture, $"SC wagered since last deposit:       {wageredSinceDeposit:N8} SC"), 18);
		AddRowLabel(string.Create(CultureInfo.InvariantCulture, $"Casino P/L with this client (all time):        {plLifetime:+0.00000000;-0.00000000} SC"), 18,
			plLifetime >= 0m ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f));
		AddRowLabel(string.Create(CultureInfo.InvariantCulture, $"Casino P/L since last client deposit:          {plSinceDeposit:+0.00000000;-0.00000000} SC"), 18,
			plSinceDeposit >= 0m ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f));
		AddRowLabel(string.Create(CultureInfo.InvariantCulture, $"Casino P/L since last client bankroll recharge ({rechargeDate}):  {plSinceRecharge:+0.00000000;-0.00000000} SC"), 18,
			plSinceRecharge >= 0m ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f));
	}

	private void AddRow(Node node) => _clientRows.AddChild(node);

	private void AddRowLabel(string text, int fontSize, Color? color = null)
	{
		var label = new Label();
		label.Text = text;
		label.AddThemeFontSizeOverride("font_size", fontSize);
		if (color.HasValue)
			label.AddThemeColorOverride("font_color", color.Value);
		_clientRows.AddChild(label);
	}
}
