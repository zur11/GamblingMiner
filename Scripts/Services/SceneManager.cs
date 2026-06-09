using Godot;
using System.Collections.Generic;

public partial class SceneManager : Node
{
	public enum SceneId
	{
		MainMenu,
		DiceGame,
		BlockExplorer,
		BankrollProgrammer,
		BetsHistoryExplorer,
		CalendarsNavigator,
		MartingaleCalculator,
	}

	private static readonly Dictionary<SceneId, string> Paths = new()
	{
		[SceneId.MainMenu]             = "res://Screens/MainMenu/MainMenu.tscn",
		[SceneId.DiceGame]             = "res://Screens/DiceGame/DiceGame.tscn",
		[SceneId.BlockExplorer]        = "res://Screens/BlockExplorer/BlockExplorer.tscn",
		[SceneId.BankrollProgrammer]   = "res://Screens/BankrollProgrammer/BankrollProgrammer.tscn",
		[SceneId.BetsHistoryExplorer]  = "res://Screens/BetsHistoryExplorer/BetsHistoryExplorer.tscn",
		[SceneId.CalendarsNavigator]   = "res://Screens/CalendarsNavigator/CalendarsNavigator.tscn",
		[SceneId.MartingaleCalculator] = "res://Screens/MartingaleCalculatorStandalone/MartingaleCalculatorStandalone.tscn",
	};

	public void Go(SceneId scene)
	{
		GetTree().ChangeSceneToFile(Paths[scene]);
	}
}
