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
	private VBoxContainer _clientRows;
	private VBoxContainer _liveFeedVBox;

	private decimal _totalWageredAll = 0m;
	private const int MaxFeedEntries = 50;
	private double _refreshTimer;
	private const double RefreshInterval = 2.0;

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
		_clientRows            = GetNode<VBoxContainer>("%ClientRows");
		_liveFeedVBox          = GetNode<VBoxContainer>("%LiveFeedVBox");

		_gameFilter.AddItem("All Games");
		_gameFilter.AddItem("Dice");
		_gameFilter.ItemSelected += _ => ClearLiveFeed();

		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.CasinoGamblingFinances);

		if (_simService != null)
			_simService.BetSettled += OnBetSettled;

		// Seed session wagered counter from current stats (player bets placed before entering this scene).
		_totalWageredAll = _userStats?.Stats?.TotalAmountWagered ?? 0m;

		RefreshGlobalSummary();
		RefreshClientRows();
	}

	public override void _ExitTree()
	{
		if (_simService != null)
			_simService.BetSettled -= OnBetSettled;
	}

	public override void _Process(double delta)
	{
		_refreshTimer += delta;
		if (_refreshTimer >= RefreshInterval)
		{
			_refreshTimer = 0;
			RefreshGlobalSummary();
			RefreshClientRows();
		}
	}

	private void OnBetSettled()
	{
		BetTransactionEvent bet = _simService?.LastSettledBetEvent;
		if (bet == null) return;

		string gameId = _simService?.CurrentConfig?.GameId ?? "Dice";

		_totalWageredAll = Money.Normalize(_totalWageredAll + bet.BetAmount);
		_totalWageredAllLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Total SC wagered (all clients):  {_totalWageredAll:N8} SC");

		int filterIdx = _gameFilter.Selected;
		bool gameMatches = filterIdx == 0 || (filterIdx == 1 && gameId == "Dice");
		if (gameMatches)
			AddLiveFeedEntry(bet, gameId);

		RefreshGlobalSummary();
	}

	private void AddLiveFeedEntry(BetTransactionEvent bet, string gameId)
	{
		string ts      = bet.Timestamp.ToLocalTime().ToString("dd MMM yyyy HH:mm:ss");
		string outcome = bet.IsWin ? "WIN " : "LOSS";
		decimal delta  = -bet.CreditedProfit;

		var label = new Label();
		label.Text = string.Create(CultureInfo.InvariantCulture, $"{ts}  Player  {gameId}  Bet {bet.BetAmount:N8} SC  {outcome}  {bet.CreditedProfit:+0.00000000;-0.00000000} SC  → casino: {delta:+0.00000000;-0.00000000} SC");
		label.AddThemeFontSizeOverride("font_size", 16);
		label.AddThemeColorOverride("font_color", bet.IsWin
			? new Color(1f, 0.5f, 0.4f)   // player win = casino loss → red-ish
			: new Color(0.4f, 1f, 0.5f));  // player loss = casino gain → green-ish

		_liveFeedVBox.AddChild(label);
		_liveFeedVBox.MoveChild(label, 0);

		while (_liveFeedVBox.GetChildCount() > MaxFeedEntries)
		{
			Node last = _liveFeedVBox.GetChild(_liveFeedVBox.GetChildCount() - 1);
			_liveFeedVBox.RemoveChild(last);
			last.QueueFree();
		}
	}

	private void ClearLiveFeed()
	{
		foreach (Node child in _liveFeedVBox.GetChildren())
			child.QueueFree();
	}

	private void RefreshGlobalSummary()
	{
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
		foreach (Node child in _clientRows.GetChildren())
			child.QueueFree();

		BuildClientRow("player", "Player");
	}

	private void BuildClientRow(string clientId, string displayName)
	{
		var stats = _userStats?.Stats;
		CasinoClientLedgerService.LedgerEntry lastDeposit   = _ledger?.GetLastDeposit(clientId);
		CasinoClientLedgerService.LedgerEntry lastRecharge  = _ledger?.GetLastAutoRecharge(clientId);

		string enrolledDate = lastDeposit != null
			? lastDeposit.UtcTimestamp.ToLocalTime().ToString("dd MMM yyyy")
			: "—";

		decimal wageredLifetime     = stats?.TotalAmountWagered ?? 0m;
		decimal profitLifetime      = stats?.TotalProfit ?? 0m;
		decimal wageredSinceDeposit = Math.Max(0m, wageredLifetime - (lastDeposit?.TotalWageredSnapshot ?? 0m));
		decimal plLifetime          = -profitLifetime;
		decimal plSinceDeposit      = -(profitLifetime - (lastDeposit?.NetProfitSnapshot ?? 0m));
		decimal plSinceRecharge     = lastRecharge != null
			? -(profitLifetime - lastRecharge.NetProfitSnapshot)
			: plLifetime; // no recharge yet → same as all-time

		int totalBets = stats?.TotalBets ?? 0;
		int wins      = stats?.TotalWins ?? 0;
		int losses    = stats?.TotalLosses ?? 0;
		decimal winRate = totalBets > 0 ? (decimal)wins / totalBets * 100m : 0m;

		string rechargeDate = lastRecharge != null
			? lastRecharge.UtcTimestamp.ToLocalTime().ToString("dd MMM yyyy HH:mm")
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
