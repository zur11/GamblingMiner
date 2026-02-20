using Godot;
using Scripts.Finance;
using System.Globalization;

public partial class BetHistoryItem : PanelContainer
{
	[Export] private Label _timestampLabel;
	[Export] private Label _multiplierLabel;
	[Export] private Label _betLabel;
	[Export] private Label _profitLabel;

	[Export] private Color _winColor = Colors.Green;
	[Export] private Color _lossColor = Colors.Red;

	public void Setup(BetTransactionEvent data)
	{
		_timestampLabel.Text = data.Timestamp
			.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

		_multiplierLabel.Text = "X " + data.Multiplier
			.ToString();

		_betLabel.Text = data.BetAmount
			.ToString("F8", CultureInfo.InvariantCulture);

		_profitLabel.Text = data.IsWin ? 
			"+" + data.Profit
			.ToString("F8", CultureInfo.InvariantCulture) :
			data.Profit
			.ToString("F8", CultureInfo.InvariantCulture);

		_profitLabel.Modulate =
			data.IsWin ? _winColor : _lossColor;
	}
}
