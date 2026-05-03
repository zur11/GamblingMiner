using Godot;
using System.Globalization;

public partial class BetRollRow : HBoxContainer
{
	private Label _betRollNumberLabel;
	private Label _betRollValueLabel;
	private Label _betRollBalanceLabel;

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

	private void EnsureNodes()
	{
		_betRollNumberLabel ??= GetNode<Label>("%BetRollNumberLabel");
		_betRollValueLabel ??= GetNode<Label>("%BetRollValueLabel");
		_betRollBalanceLabel ??= GetNode<Label>("%BetRollBalanceLabel");
	}
}
