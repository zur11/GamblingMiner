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
		GetNode<Button>("%ScFinancesBtn").Pressed       += () => _sceneManager?.Go(SceneManager.SceneId.ScFinances);
		GetNode<Button>("%CasinoCoinSwapsBtn").Pressed  += () => _sceneManager?.Go(SceneManager.SceneId.CasinoCoinSwaps);
		GetNode<Button>("%BlockExplorerBtn").Pressed    += () => _sceneManager?.Go(SceneManager.SceneId.BlockExplorer);
		GetNode<Button>("%BankrollProgrammerBtn").Pressed     += () => _sceneManager?.Go(SceneManager.SceneId.BankrollProgrammer);
		GetNode<Button>("%CalendarsNavigatorBtn").Pressed  += () => _sceneManager?.Go(SceneManager.SceneId.CalendarsNavigator);
		GetNode<Button>("%MartingaleCalcBtn").Pressed       += () => _sceneManager?.Go(SceneManager.SceneId.MartingaleCalculator);
		GetNode<Button>("%BTCWalletBtn").Pressed            += () => _sceneManager?.Go(SceneManager.SceneId.BTCWallet);
		GetNode<Button>("%BotsBtcWalletsBtn").Pressed       += () => _sceneManager?.Go(SceneManager.SceneId.BotsBtcWallets);
		// Step 16 P16.3 (D-16.7) — the two populations split out of the old combined Bot Wallets screen.
		GetNode<Button>("%CompaniesWalletsBtn").Pressed     += () => _sceneManager?.Go(SceneManager.SceneId.CompaniesWallets);
		GetNode<Button>("%CastMinerWalletsBtn").Pressed     += () => _sceneManager?.Go(SceneManager.SceneId.CastMinerWallets);
		GetNode<Button>("%CasinoFinancesBtn").Pressed       += () => _sceneManager?.Go(SceneManager.SceneId.CasinoFinances);
		GetNode<Button>("%FoundersWalletsBtn").Pressed      += () => _sceneManager?.Go(SceneManager.SceneId.FoundersWallets);
		GetNode<Button>("%BotPlayHistoryBtn").Pressed       += () => _sceneManager?.Go(SceneManager.SceneId.BotPlayHistory);
		GetNode<Button>("%BTCPoolsAndHardwareShopBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.BTCPoolsAndHardwareShop);
		GetNode<Button>("%CasinoGamblingFinancesBtn").Pressed  += () => _sceneManager?.Go(SceneManager.SceneId.CasinoGamblingFinances);
		GetNode<Button>("%WorldEconomyBtn").Pressed             += () => _sceneManager?.Go(SceneManager.SceneId.WorldEconomy);
		GetNode<Button>("%CentralBankBtn").Pressed              += () => _sceneManager?.Go(SceneManager.SceneId.CentralBank);
	}
}
