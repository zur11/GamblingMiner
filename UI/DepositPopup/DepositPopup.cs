using Godot;
using System;
using System.Globalization;

public partial class DepositPopup : Control
{
	[Signal]
	public delegate void DepositConfirmedEventHandler(double amount);

	[Signal]
	public delegate void DepositCanceledEventHandler();

	private LineEdit _amountInput;
	private Button _confirmBtn;
	private Button _cancelBtn;

	public override void _Ready()
	{
		_amountInput = GetNode<LineEdit>("%AmountToDepositInput");
		_confirmBtn = GetNode<Button>("%ConfirmDepositBtn");
		_cancelBtn = GetNode<Button>("%CancelDepositBtn");

		_confirmBtn.Pressed += OnConfirmPressed;
		_cancelBtn.Pressed += OnCancelPressed;

		Visible = false;
	}

	public void Open()
	{
		_amountInput.Text = "";
		Visible = true;
		_amountInput.GrabFocus();
	}

	public void Close()
	{
		Visible = false;
	}

	private void OnConfirmPressed()
	{
		string text = _amountInput.Text.Trim().Replace(',', '.');

		if (!double.TryParse(
			text,
			NumberStyles.AllowDecimalPoint,
			CultureInfo.InvariantCulture,
			out double amount))
		{
			GD.Print("Invalid deposit amount.");
			return;
		}

		if (amount <= 0.0)
		{
			GD.Print("Deposit must be greater than zero.");
			return;
		}

		EmitSignal(SignalName.DepositConfirmed, amount);
		Close();
	}

	private void OnCancelPressed()
	{
		EmitSignal(SignalName.DepositCanceled);
		Close();
	}
}
