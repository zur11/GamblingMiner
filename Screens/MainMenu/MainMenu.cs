using Godot;
using UI.StatusBar;

public partial class MainMenu : Control
{
	private SceneManager _sceneManager;

	public override void _Ready()
	{
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");

		var statusBarSlot = GetNode<HBoxContainer>("%StatusBarPlaceholder");
		statusBarSlot.AddChild(new StatusBar());

		GetNode<Button>("%DiceGameBtn").Pressed         += () => _sceneManager?.Go(SceneManager.SceneId.DiceGame);
		GetNode<Button>("%BlockExplorerBtn").Pressed    += () => _sceneManager?.Go(SceneManager.SceneId.BlockExplorer);
		GetNode<Button>("%BankrollProgrammerBtn").Pressed     += () => _sceneManager?.Go(SceneManager.SceneId.BankrollProgrammer);
		GetNode<Button>("%CalendarsNavigatorBtn").Pressed  += () => _sceneManager?.Go(SceneManager.SceneId.CalendarsNavigator);
		GetNode<Button>("%MartingaleCalcBtn").Pressed       += () => _sceneManager?.Go(SceneManager.SceneId.MartingaleCalculator);
		GetNode<Button>("%BTCWalletBtn").Pressed            += () => _sceneManager?.Go(SceneManager.SceneId.BTCWallet);
		GetNode<Button>("%BotsBtcWalletsBtn").Pressed       += () => _sceneManager?.Go(SceneManager.SceneId.BotsBtcWallets);
	}
}
