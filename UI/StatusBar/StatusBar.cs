using Godot;
using Scripts.Finance;

namespace UI.StatusBar
{
	public partial class StatusBar : HBoxContainer
	{
		private Label _mainBalanceLabel;
		private Label _bankrollLabel;
		private Label _clockLabel;

		private PrincipalBalanceService _principal;
		private BankrollStateService _bankroll;
		private CalendarTimeService _calendar;

		public override void _Ready()
		{
			AddThemeConstantOverride("separation", 40);

			_mainBalanceLabel = BuildLabel();
			_bankrollLabel = BuildLabel();
			_clockLabel = BuildLabel();

			_principal = GetNodeOrNull<PrincipalBalanceService>("/root/PrincipalBalanceService");
			_bankroll = GetNodeOrNull<BankrollStateService>("/root/BankrollStateService");
			_calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");

			Refresh();
		}

		public override void _Process(double delta)
		{
			Refresh();
		}

		private Label BuildLabel()
		{
			var label = new Label();
			label.AddThemeFontSizeOverride("font_size", 22);
			AddChild(label);
			return label;
		}

		private void Refresh()
		{
			if (_mainBalanceLabel == null) return;

			decimal mainBalance = _principal?.CurrentBalance ?? 0m;
			decimal bankroll = _bankroll?.CurrentBalance ?? 0m;

			_mainBalanceLabel.Text = $"Main Balance: {mainBalance:F2} SC";
			_bankrollLabel.Text = $"Bankroll: {bankroll:F2} SC";
			_clockLabel.Text = _calendar?.CurrentLocalDateTime.ToString("MMM d, yyyy  HH:mm:ss") ?? "--";
		}
	}
}
