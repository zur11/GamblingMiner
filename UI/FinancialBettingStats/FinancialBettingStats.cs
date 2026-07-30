using Godot;
using Scripts.History;
using Scripts.Finance;
using System.Globalization;

// SF.4B.2 — compact 3-scope betting-stats panel (General / Since last bank deposit / Since last bankroll
// recharge), each with P/L + Gambled. Content-sized (a VBox + GridContainer), so the same scene drops into both
// DiceGame (absolute placement) and ScFinances (scroll VBox) unchanged. Numbers come from the shared
// PlayerFinancialStatsCalculator (single source of truth), driven by UserStatsService + CasinoClientLedgerService.
public partial class FinancialBettingStats : VBoxContainer
{
	[Export] private Label _generalProfitLabel;
	[Export] private Label _generalGambledLabel;
	[Export] private Label _sinceDepositProfitLabel;
	[Export] private Label _sinceDepositGambledLabel;
	[Export] private Label _sinceRechargeProfitLabel;
	[Export] private Label _sinceRechargeGambledLabel;

	[Export] private Color _winColor = Colors.Green;
	[Export] private Color _lossColor = Colors.Red;

	private UserStatsService _userStats;
	private CasinoClientLedgerService _ledger;
	private SimulationService _simService;

	// SF.4B follow-up (live-sync fix): the panel converges to the live truth on a timer, NOT only on events.
	// UserStatsService throttles StatsChanged to 250 ms in high-frequency (autobet) mode and DEFERS the final
	// batch until the next bet or a SetHighFrequencyMode(false) flush — so a passive subscriber (e.g. ScFinances,
	// which never toggles that flag) could be left showing a stale value when betting pauses. A cheap periodic
	// Refresh makes the panel correct-by-construction in ANY host scene regardless of which events it caught.
	// It runs ONLY while an autobet is active (SimulationService.IsRunning): that is the ONLY time StatsChanged
	// is throttled AND the only time stats change without a discrete player action. When idle (autobet off),
	// game time doesn't advance and every manual bet / deposit / recharge fires an IMMEDIATE StatsChanged /
	// LedgerChanged, so the events alone keep the panel current and the timer would be pure wasted work.
	private double _refreshTimer;
	private const double RefreshInterval = 0.75;

	// Wire both data sources for immediate updates: StatsChanged (every bet, throttled) refreshes lifetime;
	// LedgerChanged moves the since-deposit / since-recharge baselines. The periodic Refresh in _Process is the
	// safety net that catches anything these events defer or drop while an autobet runs.
	public void ConnectTo(UserStatsService userStats, CasinoClientLedgerService ledger)
	{
		_userStats  = userStats;
		_ledger     = ledger;
		_simService = GetNodeOrNull<SimulationService>("/root/SimulationService");

		if (_userStats != null) _userStats.StatsChanged += OnStatsChanged;
		if (_ledger != null)    _ledger.LedgerChanged += Refresh;

		Refresh();
	}

	// INC-001 / D-15.29 (§39.16 rule 1 — a displayed figure must not claim more than it is). The bet journal
	// is now retention-capped (BetHistoryRepository.MaxRetainedJournalChunks), so the "General" scope is no
	// longer a lifetime total: it covers whatever history is still retained. Said in a tooltip rather than in
	// the label, because the caption sits in a compact GridContainer that a longer string would reflow
	// (Ch. 29). MouseFilter must be PASS, not STOP — a STOP label swallows the mouse wheel inside a
	// ScrollContainer, which is the §29 anti-pattern this panel would otherwise walk straight into.
	public override void _Ready()
	{
		Label generalScope = GetNodeOrNull<Label>("Grid/GeneralScope");
		if (generalScope != null)
		{
			generalScope.MouseFilter = MouseFilterEnum.Pass;
			generalScope.TooltipText =
				"Covers the retained bet history, not the whole run — older bets are trimmed once the " +
				"journal passes its retention cap. The two 'Since…' scopes are exact: they are measured " +
				"from ledger events, not from the journal.";
		}
	}

	private void OnStatsChanged(Scripts.User.UserBettingStats _) => Refresh();

	public override void _Process(double delta)
	{
		// Only reconcile while an autobet is actively advancing time; otherwise events keep us current (above).
		if (_simService == null || !_simService.IsRunning) return;
		_refreshTimer += delta;
		if (_refreshTimer < RefreshInterval) return;
		_refreshTimer = 0;
		Refresh();
	}

	public void Refresh()
	{
		if (!GodotObject.IsInstanceValid(this) || _userStats == null) return;
		UpdateFrom(PlayerFinancialStatsCalculator.Compute(_userStats.Stats, _ledger));
	}

	public void UpdateFrom(PlayerFinancialSummary s)
	{
		SetProfit(_generalProfitLabel, s.TotalProfit);
		SetProfit(_sinceDepositProfitLabel, s.ProfitSinceDeposit);
		SetProfit(_sinceRechargeProfitLabel, s.ProfitSinceRecharge);

		SetGambled(_generalGambledLabel, s.TotalWagered);
		SetGambled(_sinceDepositGambledLabel, s.WageredSinceDeposit);
		SetGambled(_sinceRechargeGambledLabel, s.WageredSinceRecharge);
	}

	private void SetProfit(Label label, decimal value)
	{
		if (label == null) return;
		label.Text = Money.FormatSignedAdaptive(value);
		label.Modulate = value >= 0m ? _winColor : _lossColor;
	}

	private static void SetGambled(Label label, decimal value)
	{
		if (label == null) return;
		label.Text = value.ToString("N8", CultureInfo.InvariantCulture);
	}

	public override void _ExitTree()
	{
		if (_userStats != null) _userStats.StatsChanged -= OnStatsChanged;
		if (_ledger != null)    _ledger.LedgerChanged -= Refresh;
	}
}
