using Godot;
using Scripts.Finance;

public partial class BetHistoryContainer : VBoxContainer
{
	[Export]
	private PackedScene _betHistoryItemScene;

	public void SubscribeTo(DiceGame game)
	{
		game.BetExecuted += OnBetExecuted;
	}

	private void OnBetExecuted(string _, BetTransactionEvent betEvent) // string _ es para ignorar el parámetro gameId, que podria implementar si quisiera mostrar el historial de varios juegos en un mismo contenedor
	{
		AddEntry(betEvent);
	}

	private void AddEntry(BetTransactionEvent betEvent)
	{
		var item = _betHistoryItemScene
			.Instantiate<BetHistoryItem>();

		AddChild(item);
		item.Setup(betEvent);

		MoveChild(item, 0);
	}
}
