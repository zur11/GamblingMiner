using Godot;
using System;

public partial class PreviousWinnerNumbersGrid : GridContainer
{
	[Export]
	private PackedScene _winnerPresenterScene;

	public void AddWinnerNumber(int number, bool won)
	{
		var item = _winnerPresenterScene.Instantiate<WinnerNumberPresenter>();

		AddChild(item);
		item.Setup(number, won);

		MoveChild(item, 0);
	}
}
