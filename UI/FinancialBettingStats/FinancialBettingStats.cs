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
	// Every bet the player has placed in this world. Read from Stats, whose only source is the unpruned lifetime
	// rollup (mini-plan 08 D1), so it keeps counting past the journal's retention cap. A row below the grid
	// rather than a column: the ledger snapshots carry no bet count, so the two "Since…" rows would have no value.
	[Export] private Label _totalBetsLabel;

	[Export] private Color _winColor = Colors.Green;
	[Export] private Color _lossColor = Colors.Red;

	private UserStatsService _userStats;
	private CasinoClientLedgerService _ledger;

	// Both data sources are events: StatsChanged (every bet, throttled to 250 ms during an autobet) refreshes
	// lifetime, and LedgerChanged moves the since-deposit / since-recharge baselines.
	// Mini-plan 25 17.C.3 — the 0.75 s timer that ran while an autobet was active is gone. It existed (SF.4B's
	// live-sync fix) because the throttle deferred its last batch until the next bet, so a paused run left this
	// panel stale. The throttle now has a trailing edge (UserStatsService._Process) and delivers the deferred
	// change itself, so the gap is closed at its owner, for every subscriber, and this panel needs no timer.
	public void ConnectTo(UserStatsService userStats, CasinoClientLedgerService ledger)
	{
		_userStats  = userStats;
		_ledger     = ledger;

		if (_userStats != null) _userStats.StatsChanged += OnStatsChanged;
		if (_ledger != null)    _ledger.LedgerChanged += Refresh;

		Refresh();
	}

	// INC-001 / D-15.29 (§39.16 rule 1 — a displayed figure must not claim more than it is). The bet journal
	// is retention-capped (BetHistoryRepository.MaxRetainedJournalChunks), but since mini-plan 08 D1 "General"
	// no longer reads it: it is the lifetime rollup, so it IS a lifetime total and the tooltip says so. (This
	// note used to say the opposite, which was true only while Stats was rebuilt from the journal.) Said in a
	// tooltip rather than in the label, because the caption sits in a compact GridContainer that a longer
	// string would reflow (Ch. 29). MouseFilter must be PASS, not STOP — a STOP label swallows the mouse wheel inside a
	// ScrollContainer, which is the §29 anti-pattern this panel would otherwise walk straight into.
	public override void _Ready()
	{
		Label generalScope = GetNodeOrNull<Label>("Grid/GeneralScope");
		if (generalScope != null)
		{
			generalScope.MouseFilter = MouseFilterEnum.Pass;
			generalScope.TooltipText =
				"Covers every bet this world has recorded, including bets the history has since trimmed — " +
				"it is read from the lifetime totals, not from the retained bet history. The two 'Since…' " +
				"scopes subtract the lifetime totals captured at that ledger event.";
		}
	}

	private void OnStatsChanged(Scripts.User.UserBettingStats _) => Refresh();

	public void Refresh()
	{
		Scripts.Diagnostics.UiRefreshProbe.Count("FinancialBettingStats.Refresh");   // DEBUG-only, mini-plan 25
		if (!GodotObject.IsInstanceValid(this) || _userStats == null) return;
		UpdateFrom(PlayerFinancialStatsCalculator.Compute(_userStats.Stats, _ledger));
		if (_totalBetsLabel != null)
		{
			_totalBetsLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Total bets: {_userStats.Stats?.TotalBets ?? 0:N0}");
		}
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
