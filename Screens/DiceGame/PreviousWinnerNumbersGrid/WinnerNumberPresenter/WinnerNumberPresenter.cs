using Godot;

public partial class WinnerNumberPresenter : PanelContainer
{
	[Export] private Label _numberLabel;

	[Export] private Color _winColor = Colors.Green;
	[Export] private Color _lossColor = Colors.Red;

	private StyleBoxFlat _styleBox;

	public override void _Ready()
	{
		var original = _numberLabel.GetThemeStylebox("normal") as StyleBoxFlat;

		if (original != null)
		{
			_styleBox = original.Duplicate() as StyleBoxFlat;
			_numberLabel.AddThemeStyleboxOverride("normal", _styleBox);
		}
	}

	public void Setup(int number, bool won)
	{
		_numberLabel.Text = number.ToString("D2");

		if (_styleBox != null)
		{
			_styleBox.BgColor = won ? _winColor : _lossColor;
		}
	}
}
