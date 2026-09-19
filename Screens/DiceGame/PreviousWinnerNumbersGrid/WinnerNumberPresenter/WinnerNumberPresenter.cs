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

	// What the cell shows now, so a rewrite that would change nothing is skipped (mini-plan 10 D-10.2). The grid
	// rewrites all 100 cells once per frame in fixed order; setting the stylebox colour emits `changed` and
	// queues a redraw even when the colour is the same, so this check is what keeps an unchanged cell free.
	private int _shownNumber = -1;
	private bool _shownWon;

	public void Setup(int number, bool won)
	{
		Visible = true;
		if (number == _shownNumber && won == _shownWon)
		{
			return;
		}

		_shownNumber = number;
		_shownWon = won;
		_numberLabel.Text = number.ToString("D2");

		if (_styleBox != null)
		{
			_styleBox.BgColor = won ? _winColor : _lossColor;
		}
	}
}
