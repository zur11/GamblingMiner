using Godot;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GodotBlockchainPort.Simulation;
using Scripts.Betting;
using Scripts.Diagnostics;
using UI.StatusBar;

// Mini-plan 27 unit 5 (plan §3.4) — the player's two layers of data, and the three resets.
//
// It SHOWS the creations (layer 1) and the preferences (layer 2). It OFFERS exactly three actions (developer,
// 2026-10-21): Reset preferences, Reset world and Wipe everything. Each sits behind a confirmation that names what
// it will delete, read from the same source the reset deletes from, never from a second list (Convention 6). None runs
// here: each writes PlayerResetRequest and closes the game, and WorldGuardService performs it at the next launch,
// before anything loads (D-27.7), which is the only form a static cache cannot defeat.
//
// Layout: Ch. 29 Pattern A (MarginContainer → VBox → ScrollContainer that expands → content VBox, the wheel chain on
// PASS), and the Back button in a fixed footer outside the scroll, above the bottom band (§29.10, §29.11).
public partial class UserSettings : Control
{
	private SceneManager _sceneManager;
	private UserSettingsService _userSettings;
	private ConfirmationDialog _dialog;
	private PlayerResetRequest.ResetKind _dialogKind;
	private bool _wipeEverythingAskedOnce;

	public override void _Ready()
	{
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		_userSettings = GetNodeOrNull<UserSettingsService>("/root/UserSettingsService");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());
		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);
		GetNode<Button>("%CancelPendingBtn").Pressed += OnCancelPendingPressed;
		GetNode<Button>("%ResetPreferencesBtn").Pressed += () => Ask(PlayerResetRequest.ResetKind.ResetPreferences);
		GetNode<Button>("%ResetWorldBtn").Pressed += () => Ask(PlayerResetRequest.ResetKind.ResetWorld);
		GetNode<Button>("%WipeEverythingBtn").Pressed += () => Ask(PlayerResetRequest.ResetKind.WipeEverything);

		RefreshLayers();
		RefreshPending();
	}

	// ── What the two layers hold ────────────────────────────────────────────────────────────────────────────

	private void RefreshLayers()
	{
		int strategies = new SavedBettingStrategyRepository(SavedBettingStrategyRepository.DefaultPath).Count;
		int notes = GetNodeOrNull<NotepadService>("/root/NotepadService")?.GetAllNames().Count ?? 0;
		GetNode<Label>("%CreationsLabel").Text =
			$"Saved betting strategies: {strategies}   ·   Notepad notes: {notes}\n" +
			"Kept by \"Reset preferences\" and by \"Reset world\". Only \"Wipe everything\" deletes them.\n" +
			$"Folder: {ProjectSettings.GlobalizePath(PlayerDataFiles.CreationsDir)}";

		IReadOnlyList<string> set = _userSettings?.GetSetKeys() ?? new List<string>();
		string list = set.Count == 0
			? "None is set: every preference is at its default."
			: $"{set.Count} set, every other one at its default:\n" + string.Join("\n", set.Select(key => "   · " + key));
		GetNode<Label>("%PreferencesLabel").Text =
			list + "\n" +
			"Kept by \"Reset world\". \"Reset preferences\" and \"Wipe everything\" delete them.\n" +
			$"Folder: {ProjectSettings.GlobalizePath(PlayerDataFiles.PreferencesDir)}";
	}

	private void RefreshPending()
	{
		PlayerResetRequest.ResetKind? pending = PlayerResetRequest.Pending();
		GetNode<HBoxContainer>("%PendingRow").Visible = pending.HasValue;
		if (pending.HasValue)
		{
			GetNode<Label>("%PendingLabel").Text =
				$"A reset is pending: \"{DisplayName(pending.Value)}\". It runs at the next launch, before anything loads.";
		}
	}

	private void OnCancelPendingPressed()
	{
		PlayerResetRequest.Cancel();
		RefreshPending();
	}

	// ── The confirmations ───────────────────────────────────────────────────────────────────────────────────

	private void Ask(PlayerResetRequest.ResetKind kind)
	{
		_dialogKind = kind;
		_wipeEverythingAskedOnce = false;
		ShowDialog(DisplayName(kind), BuildMessage(kind), ConfirmText(kind));
	}

	private void ShowDialog(string title, string text, string okText)
	{
		if (_dialog == null)
		{
			_dialog = new ConfirmationDialog { DialogAutowrap = true, MinSize = new Vector2I(1000, 0), CancelButtonText = "Cancel" };
			_dialog.Confirmed += OnDialogConfirmed;
			AddChild(_dialog);
		}

		_dialog.Title = title;
		_dialog.DialogText = text;
		_dialog.OkButtonText = okText;
		_dialog.PopupCentered();
	}

	private void OnDialogConfirmed()
	{
		// "Wipe everything" asks twice (developer, 2026-10-21): the first confirmation only opens the last one.
		if (_dialogKind == PlayerResetRequest.ResetKind.WipeEverything && !_wipeEverythingAskedOnce)
		{
			_wipeEverythingAskedOnce = true;
			CallDeferred(MethodName.ShowLastWipeConfirmation);
			return;
		}

		if (PlayerResetRequest.Write(_dialogKind))
		{
			_sceneManager?.QuitLikeTheWindowClose();
			return;
		}

		// Not written: nothing will be reset, and the game stays open so the player can see why.
		var failed = new AcceptDialog
		{
			Title = "Nothing was reset",
			DialogText = $"The request could not be written to {PlayerResetRequest.RequestPath}. Nothing will be reset.",
		};
		AddChild(failed);
		failed.PopupCentered();
	}

	private void ShowLastWipeConfirmation() =>
		ShowDialog("Wipe everything: last confirmation",
			"This is the last confirmation. There is no undo.\n\n" +
			$"Every file in {ProjectSettings.GlobalizePath("user://")} will be deleted when the game next starts.",
			"Wipe everything and close the game");

	private static string BuildMessage(PlayerResetRequest.ResetKind kind)
	{
		const string closes = "\n\nThe game closes now, and the reset runs when you next launch it.";
		switch (kind)
		{
			case PlayerResetRequest.ResetKind.ResetPreferences:
				return "Every preference returns to its default (the views, the explorer's replay settings, the strategy " +
					"panel's values per node, and the DEV settings).\n\n" +
					"Not touched: your saved strategies and notepad notes, the world, and the hardware credits." + closes;

			case PlayerResetRequest.ResetKind.ResetWorld:
				return "The world starts again from genesis: the chain, the clock, every balance, the bet history and its " +
					"statistics, pending transactions, and every node's hardware credits (back to 1 credit each, " +
					"individual pool).\n\n" +
					"Kept: your saved strategies and notes, your preferences, and every wallet and identity.\n\n" +
					"These DEV traces in user://logs/ are deleted with it:\n" +
					string.Join("\n", NetworkRoot.WorldTracePaths.Select(path => "   · " + Path.GetFileName(path))) + "\n\n" +
					"Every other file in user://logs/ is kept and keeps appending in the new world. Rename any you want " +
					$"to keep apart first, such as {Path.GetFileName(RankCalibrationTrace.TracePath)}." + closes;

			default:
				return "Everything in the user data folder is deleted:\n" +
					$"   {ProjectSettings.GlobalizePath("user://")}\n\n" +
					"   · the world\n" +
					"   · your saved strategies and notepad notes\n" +
					"   · your preferences\n" +
					"   · the identity files, so the next launch creates new wallets and new bot identities:\n" +
					string.Join("\n", WalletInitializationService.IdentityFilePaths.Select(path => "        " + Path.GetFileName(path))) + "\n" +
					"   · user://logs/: every trace and every archived log in it\n\n" +
					"Copy anything you want to keep OUT of that folder before you continue. The next launch is a first launch." +
					closes;
		}
	}

	private static string ConfirmText(PlayerResetRequest.ResetKind kind) => kind switch
	{
		PlayerResetRequest.ResetKind.ResetPreferences => "Reset preferences and close the game",
		PlayerResetRequest.ResetKind.ResetWorld => "Reset the world and close the game",
		_ => "Continue",
	};

	private static string DisplayName(PlayerResetRequest.ResetKind kind) => kind switch
	{
		PlayerResetRequest.ResetKind.ResetPreferences => "Reset preferences",
		PlayerResetRequest.ResetKind.ResetWorld => "Reset world",
		_ => "Wipe everything",
	};
}
