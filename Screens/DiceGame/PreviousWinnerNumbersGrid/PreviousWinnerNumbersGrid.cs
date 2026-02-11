using Godot;
using System;

public partial class PreviousWinnerNumbersGrid : GridContainer
{
	public void AddWinnerNumber(int number, bool won)
	{
		var panel = new PanelContainer
		{
			CustomMinimumSize = new Vector2(40, 40)
		};

		var label = new Label
		{
			Text = number.ToString("D2"),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};

		var sb = new StyleBoxFlat
		{
			BgColor = won ? Colors.Green : Colors.Red,
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6,
			DrawCenter = true
		};

		panel.AddThemeStyleboxOverride("panel", sb);

		panel.AddChild(label);
		AddChild(panel);

		// mover el último resultado a la primera posición del grid
		MoveChild(panel, 0);
	}
}
