using Godot;
using Scripts.User;
using System.Globalization;

public partial class FinancialBettingStats : Control
{
	[Export] private Label _lastDepositProfitLabel;
	[Export] private Label _lastDepositGambledLabel;
	[Export] private Label _generalProfitLabel;
	[Export] private Label _totalGambledLabel;

	[Export] private Color _winColor = Colors.Green;
	[Export] private Color _lossColor = Colors.Red;

	public void UpdateFrom(UserBettingStats stats)
	{
		_lastDepositProfitLabel.Text =
			stats.ProfitSinceDeposit.ToString("F8", CultureInfo.InvariantCulture);

		_generalProfitLabel.Text =
			stats.TotalProfit.ToString("F8", CultureInfo.InvariantCulture);

		_lastDepositGambledLabel.Text =
			stats.AmountWageredSinceDeposit.ToString("F8", CultureInfo.InvariantCulture);

		_totalGambledLabel.Text =
			stats.TotalAmountWagered.ToString("F8", CultureInfo.InvariantCulture);

		UpdateColor(_lastDepositProfitLabel, stats.ProfitSinceDeposit);
		UpdateColor(_generalProfitLabel, stats.TotalProfit);
	}

	private void UpdateColor(Label label, decimal value)
	{
		label.Modulate = value >= 0 ? _winColor : _lossColor;
	}
}
