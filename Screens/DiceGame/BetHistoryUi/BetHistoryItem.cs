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

	// timePattern is chosen by BetHistoryContainer for the whole column (mini-plan 20 C), so every row in view
	// shows the same precision. The full instant is always in the tooltip: a coarser column hides nothing.
	public void Setup(BetTransactionEvent data, string timePattern)
	{
		Visible = true;
		DateTime local = data.Timestamp.Kind == DateTimeKind.Utc
			? data.Timestamp.ToLocalTime()
			: data.Timestamp;

		_timestampLabel.Text = local.ToString(timePattern, CultureInfo.InvariantCulture);
		TooltipText = local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

		// InvariantCulture is load-bearing: `"X " + decimal` calls the culture-sensitive ToString(), which is
		// the fourth shape of the locale bug (CLAUDE.md, Money Handling) — neither detector pass can see it, and it
		// rendered "X 1,9804" on the developer's Spanish machine in every screenshot until mini-plan 10.
		_multiplierLabel.Text = "X " + data.Multiplier.ToString(CultureInfo.InvariantCulture);

		_betLabel.Text = data.BetAmount
			.ToString("F8", CultureInfo.InvariantCulture);

		_profitLabel.Text = Money.FormatSignedAdaptive(data.Profit);

		_profitLabel.Modulate =
			data.IsWin ? _winColor : _lossColor;
	}
}
