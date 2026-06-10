using Godot;
using System.Globalization;

public partial class BetRollRow : HBoxContainer
{
	private Label _betRollNumberLabel;
	private Label _betRollValueLabel;
	private Label _betRollBalanceLabel;
	private Label _failProbLabel;
	private Label _flagsLabel;

	public override void _Ready()
	{
		EnsureNodes();
	}

	public void SetData(int betRollNumber, double betRollValue, double betRollBalance)
	{
		EnsureNodes();
		_betRollNumberLabel.Text = betRollNumber.ToString(CultureInfo.InvariantCulture);
		_betRollValueLabel.Text = betRollValue.ToString("F8", CultureInfo.InvariantCulture);
		_betRollBalanceLabel.Text = betRollBalance.ToString("F8", CultureInfo.InvariantCulture);
	}

	public void SetFailProbability(double percent)
	{
		EnsureNodes();
		_failProbLabel.Text = $"{percent:F8}%";
	}

	private void EnsureNodes()
	{
		_betRollNumberLabel ??= GetNode<Label>("%BetRollNumberLabel");
		_betRollValueLabel ??= GetNode<Label>("%BetRollValueLabel");
		_betRollBalanceLabel ??= GetNode<Label>("%BetRollBalanceLabel");
		_failProbLabel ??= GetNode<Label>("%FailProbLabel");
		_flagsLabel ??= GetNode<Label>("%FlagsLabel");
	}

	public void SetFlags(bool isDoneAttempt, bool isCurrentAttempt, bool hitsStopOnLoss, bool hitsStopOnProfit)
	{
		EnsureNodes();

		string flags = "";

		if (isDoneAttempt)
			flags += "[DONE] ";
		if (isCurrentAttempt)
			flags += "[CURRENT] ";
		if (hitsStopOnLoss)
			flags += "[STOP LOSS] ";
		if (hitsStopOnProfit)
			flags += "[STOP WIN] ";

		_flagsLabel.Text = flags.TrimEnd();
	}
}
