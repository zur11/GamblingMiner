using Godot;
using Scripts.Finance;
using System.Globalization;
using System;

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
		Visible = true;
		DateTime local = data.Timestamp.Kind == DateTimeKind.Utc
			? data.Timestamp.ToLocalTime()
			: data.Timestamp;

		_timestampLabel.Text = local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

		_multiplierLabel.Text = "X " + data.Multiplier
			.ToString();

		_betLabel.Text = data.BetAmount
			.ToString("F8", CultureInfo.InvariantCulture);

		_profitLabel.Text = data.IsWin ? 
			Money.FormatSignedAdaptive(data.Profit) :
			Money.FormatSignedAdaptive(data.Profit);

		_profitLabel.Modulate =
			data.IsWin ? _winColor : _lossColor;
	}
}
