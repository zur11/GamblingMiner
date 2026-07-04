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

	// Wire both data sources: StatsChanged (every bet, throttled) refreshes lifetime; LedgerChanged moves the
	// since-deposit / since-recharge baselines. Both recompute the same summary.
	public void ConnectTo(UserStatsService userStats, CasinoClientLedgerService ledger)
	{
		_userStats = userStats;
		_ledger    = ledger;

		if (_userStats != null) _userStats.StatsChanged += OnStatsChanged;
		if (_ledger != null)    _ledger.LedgerChanged += Refresh;

		Refresh();
	}

	private void OnStatsChanged(Scripts.User.UserBettingStats _) => Refresh();

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
