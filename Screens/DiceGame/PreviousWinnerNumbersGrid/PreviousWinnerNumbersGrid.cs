using Godot;
using System;
using Scripts.Finance;

public partial class PreviousWinnerNumbersGrid : GridContainer
{
	[Export]
	private PackedScene _winnerPresenterScene;

	public void SubscribeTo(DiceGame game)
	{
		game.BetExecuted += OnBetExecuted;
	}

	private void OnBetExecuted(BetTransactionEvent betEvent)
	{
		AddWinnerNumber(betEvent.Roll, betEvent.IsWin);
	}

	public void AddWinnerNumber(int number, bool won)
	{
		var item = _winnerPresenterScene.Instantiate<WinnerNumberPresenter>();

		AddChild(item);
		item.Setup(number, won);

		MoveChild(item, 0);
	}
}
