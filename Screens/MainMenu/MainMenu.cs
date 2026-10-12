using Godot;
using System;
using System.Globalization;
using UI.StatusBar;

public partial class MainMenu : Control
{
	private SceneManager _sceneManager;
	private ConfirmationDialog _exitDialog;

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
		GetNode<Button>("%ExitBtn").Pressed                     += OnExitPressed;
	}

	// ── Mini-plan 23 A — Exit (D-20.1: a confirmation naming what a restart will restore) ─────────────────────────
	//
	// A restart reverts the whole world to the last mined block — clock, balances, mempool and, since mini-plan 22,
	// the bet journal too, at boot. The window's X has always done this silently; a deliberate Exit is the place to
	// say it once. The date comes from the checkpoint's OWN CalendarLocalTicks, the value the next boot restores —
	// never recomputed (Standing Convention 6: a message shares its source with what it describes).
	private void OnExitPressed()
	{
		if (_exitDialog == null)
		{
			_exitDialog = new ConfirmationDialog
			{
				Title = "Exit the game",
				OkButtonText = "Exit",
				CancelButtonText = "Cancel"
			};
			_exitDialog.Confirmed += QuitLikeTheWindowClose;
			AddChild(_exitDialog);
		}

		_exitDialog.DialogText = BuildExitMessage();
		_exitDialog.PopupCentered();
	}

	private string BuildExitMessage()
	{
		BlockSessionCheckpointService checkpoint = GetNodeOrNull<BlockSessionCheckpointService>("/root/BlockSessionCheckpointService");
		if (checkpoint == null || !checkpoint.HasCheckpoint())
		{
			return "No block has been mined yet.\nThe world will restart at the player-start date.";
		}

		long? ticks = checkpoint.CurrentSnapshot?.CalendarLocalTicks;
		string when = ticks.HasValue
			? new DateTime(ticks.Value, DateTimeKind.Local).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
			: "the last mined block";
		return "The world will resume from the last mined block (" + when + ").\nBets placed since then will be undone.";
	}

	// No weaker than the window's X: the close request is propagated to every node first, so each existing
	// NotificationWMCloseRequest handler (BankrollStateService, CasinoScBalanceService) — and any future one — flushes
	// exactly as it does on X, and only then does the tree quit. A bare Quit() would not deliver that notification.
	private void QuitLikeTheWindowClose()
	{
		GD.Print("[Exit] close request propagated"); // the Godot editor's OUTPUT panel
		GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest);
		GetTree().Quit();
	}
}
