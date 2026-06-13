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
		BTCWallet,
		BotsBtcWallets,
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
		[SceneId.BTCWallet]            = "res://Screens/BTCWallet/BTCWallet.tscn",
		[SceneId.BotsBtcWallets]       = "res://Screens/BotsBtcWallets/BotsBtcWallets.tscn",
	};

	// Overlay stack: scenes added on top without replacing the current scene.
	private readonly List<Node> _overlayStack = new();

	public void Go(SceneId scene)
	{
		GetTree().ChangeSceneToFile(Paths[scene]);
	}

	// Push a scene as an overlay on top of the current scene tree.
	// The caller is responsible for hiding the scene below if needed.
	// Returns the instantiated node so the caller can subscribe to TreeExited.
	public Node PushScene(SceneId scene)
	{
		var node = GD.Load<PackedScene>(Paths[scene]).Instantiate();
		GetTree().Root.AddChild(node);
		_overlayStack.Add(node);
		return node;
	}

	// Remove the topmost overlay.
	public void PopOverlay()
	{
		if (_overlayStack.Count == 0) return;
		Node top = _overlayStack[^1];
		_overlayStack.RemoveAt(_overlayStack.Count - 1);
		if (GodotObject.IsInstanceValid(top)) top.QueueFree();
	}

	// Remove all overlays (e.g. "Back to Dice" skips the whole stack).
	public void PopAllOverlays()
	{
		foreach (Node overlay in _overlayStack)
			if (GodotObject.IsInstanceValid(overlay)) overlay.QueueFree();
		_overlayStack.Clear();
	}
}
