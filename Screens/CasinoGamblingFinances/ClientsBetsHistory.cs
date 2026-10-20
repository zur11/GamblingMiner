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

	// Mini-plan 25 17.C.3 — no 2 s timer, and no work per bet. ClientBetSettled fires on every settled bet of all five
	// clients: M0 counted it at 450 a second at 9000X, and each one created a Label, freed another and recomputed the
	// summary (§38.7 rule 1). Now the handler only queues the bet and marks two things dirty:
	// - the FEED and the SUMMARY repaint at most every FeedFlushIntervalSeconds. A flush writes the queued bets into
	//   recycled rows (the oldest row moves to the top), so a full feed allocates nothing, and the queue never holds
	//   more than the feed can show.
	// - the client ROWS, which the old timer covered: their per-client bet stats move on every bet and no other event
	//   announces them. They rebuild through a leading-edge throttle, at most every RowsMinRefreshInterval, the old
	//   timer's rate. LedgerChanged (a deposit or a recharge moves the "since" baselines and dates) marks them too.
	// At rest: 0 refreshes a second (the timer was 0.5).
	private readonly record struct FeedItem(string DisplayName, BetTransactionEvent Bet, string GameId);
	private readonly System.Collections.Generic.Queue<FeedItem> _pendingFeed = new();
	private bool _summaryDirty;
	private double _sinceFeedFlush = double.MaxValue;
	private const double FeedFlushIntervalSeconds = 0.1;
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
		_sinceFeedFlush += delta;
		if ((_pendingFeed.Count > 0 || _summaryDirty) && _sinceFeedFlush >= FeedFlushIntervalSeconds)
		{
			_sinceFeedFlush = 0d;
			FlushLiveFeed();
			if (_summaryDirty)
			{
				_summaryDirty = false;
				RefreshGlobalSummary();
			}
		}

		_sinceRowsRebuild += delta;
		if (_rowsDirty && _sinceRowsRebuild >= RowsMinRefreshInterval)
		{
			_rowsDirty = false;
			_sinceRowsRebuild = 0d;
			RefreshClientRows();
		}
	}

	// Called hundreds of times a second under the sim: it queues and marks, nothing else (see the fields).
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
			_pendingFeed.Enqueue(new FeedItem(DisplayNameFor(nodeId), bet, gameId));
			while (_pendingFeed.Count > MaxFeedEntries)
				_pendingFeed.Dequeue(); // older than anything the feed can still show
		}
	}

	private void FlushLiveFeed()
	{
		if (_pendingFeed.Count == 0) return;
		Scripts.Diagnostics.UiRefreshProbe.Count("ClientsBetsHistory.FlushLiveFeed");   // DEBUG-only, mini-plan 25
		while (_pendingFeed.Count > 0)
		{
			FeedItem item = _pendingFeed.Dequeue();
			AddLiveFeedEntry(item.DisplayName, item.Bet, item.GameId);
		}
	}

	private static string DisplayNameFor(string nodeId)
	{
		foreach ((string id, string display) in CasinoClientLedgerService.CanonicalClients)
			if (id == nodeId) return display;
		return nodeId;
	}

	private void AddLiveFeedEntry(string displayName, BetTransactionEvent bet, string gameId)
	{
		string ts      = bet.Timestamp.ToLocalTime().ToString("dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture);
		string outcome = bet.IsWin ? "WIN " : "LOSS";
		decimal delta  = -bet.CreditedProfit;

		// A full feed recycles its oldest row instead of freeing one and allocating another.
		Label label;
		int count = _liveFeedVBox.GetChildCount();
		if (count >= MaxFeedEntries)
		{
			label = (Label)_liveFeedVBox.GetChild(count - 1);
		}
		else
		{
			label = new Label();
			label.AddThemeFontSizeOverride("font_size", 16);
			_liveFeedVBox.AddChild(label);
		}

		label.Text = string.Create(CultureInfo.InvariantCulture, $"{ts}  {displayName,-6}  {gameId}  Bet {bet.BetAmount:N8} SC  {outcome}  {bet.CreditedProfit:+0.00000000;-0.00000000} SC  → casino: {delta:+0.00000000;-0.00000000} SC");
		label.AddThemeColorOverride("font_color", bet.IsWin
			? new Color(1f, 0.5f, 0.4f)   // player win = casino loss → red-ish
			: new Color(0.4f, 1f, 0.5f));  // player loss = casino gain → green-ish
		_liveFeedVBox.MoveChild(label, 0);
	}

	// RemoveChild before QueueFree: a node queued for deletion still counts as a child until the frame ends, and the
	// recycling above must never pick one up.
	private void ClearLiveFeed()
	{
		_pendingFeed.Clear();
		foreach (Node child in _liveFeedVBox.GetChildren())
		{
			_liveFeedVBox.RemoveChild(child);
			child.QueueFree();
		}
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
