using Godot;
using Scripts.Betting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
#nullable enable

// Mini-plan 27 unit 1 — the player's PREFERENCES (layer 2, plan §3.4): one small versioned JSON object under
// user://player/preferences/, which no world wipe can reach (plan §2.2). A preference is PLAYER state, not world
// state: it is never checkpoint-reverted, never reset pre-genesis, and not under WorldWriteGuard — a broken world
// must not stop a player from changing a view (plan §3.1, §3.3; an exemption by CLASS, CLAUDE.md Pattern 2).
//
// Autoload #20, appended deliberately (D-27.3, Pattern 5): it depends on no other service, and every reader is a
// scene, which loads after all autoloads.
//
// Every getter returns null for a setting never set: the DEFAULT belongs to the scene that owns the setting, so
// there is one copy of it (Convention 15) and "Reset preferences" is simply the file's absence. A missing key takes
// the scene's default and an unknown key is ignored, so adding a setting later needs no version bump (Convention 5).
public partial class UserSettingsService : Node
{
	public const string SettingsPath = PlayerDataFiles.PreferencesDir + "/settings.json";
	public const int CurrentSettingsVersion = 1;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = true
	};

	/// <summary>The key a <see cref="SettingChanged"/> carries. A per-node strategy is "strategyPanel.&lt;nodeId&gt;".</summary>
	public static class Keys
	{
		public const string DiceBetView = "diceGame.betView";
		public const string ExplorerPace = "betsExplorer.pace";
		public const string ExplorerSpeed = "betsExplorer.speed";
		public const string ExplorerPerBetCredits = "betsExplorer.perBetCredits";
		public const string ExplorerLatestWinnerOnly = "betsExplorer.latestWinnerOnly";
		public const string StrategyPanel = "strategyPanel";
		public const string DevRequestedTimeScale = "dev.requestedTimeScale";
		public const string DevShowCasinoScBalances = "dev.showCasinoScBalances";
	}

	/// <summary>The strategy panel's values for one node (D-27.5). Distinct from a SAVED strategy, which has a name.</summary>
	public sealed class StrategyPanelValues
	{
		public BettingStrategyConfig? Config { get; set; }
		public int NumberOfBets { get; set; }
		public int WinningChance { get; set; }
		public bool BetHigh { get; set; }
	}

	/// <summary>Fired on a real change only: a set to the current value neither fires nor writes.</summary>
	public event Action<string>? SettingChanged;

	private SettingsFile _settings = new();
	// Set when a corrupt file could not be moved aside: writing would then destroy the only copy of the evidence,
	// so nothing is written this session (plan §3.3, INC-004). The atomic write is DurableFile.WriteAtomically.
	private bool _persistBlocked;

#if DEBUG
	private int _changeCount;
	private int _unchangedSetCount;
	private int _writeCount;
#endif

	public override void _Ready()
	{
		Scripts.Diagnostics.BootProbe.Mark("last autoload (UserSettingsService) starts");
		Load();
		Scripts.Diagnostics.BootProbe.Mark("UserSettingsService done");
	}

	public override void _ExitTree() => ReportWriteCount();

	// --- DiceGame -------------------------------------------------------------------------------------------------

	public string? GetDiceBetView() => _settings.DiceGame?.BetView;

	public void SetDiceBetView(string value)
	{
		DiceGameSection section = _settings.DiceGame ??= new DiceGameSection();
		Change(Keys.DiceBetView, section.BetView, value, () => section.BetView = value);
	}

	// --- BetsHistoryExplorer --------------------------------------------------------------------------------------

	public string? GetExplorerPace() => _settings.BetsExplorer?.Pace;
	public double? GetExplorerSpeed() => _settings.BetsExplorer?.Speed;
	public int? GetExplorerPerBetCredits() => _settings.BetsExplorer?.PerBetCredits;
	public bool? GetExplorerLatestWinnerOnly() => _settings.BetsExplorer?.LatestWinnerOnly;

	public void SetExplorerPace(string value)
	{
		BetsExplorerSection section = _settings.BetsExplorer ??= new BetsExplorerSection();
		Change(Keys.ExplorerPace, section.Pace, value, () => section.Pace = value);
	}

	public void SetExplorerSpeed(double value)
	{
		BetsExplorerSection section = _settings.BetsExplorer ??= new BetsExplorerSection();
		Change(Keys.ExplorerSpeed, section.Speed, value, () => section.Speed = value);
	}

	public void SetExplorerPerBetCredits(int value)
	{
		BetsExplorerSection section = _settings.BetsExplorer ??= new BetsExplorerSection();
		Change(Keys.ExplorerPerBetCredits, section.PerBetCredits, value, () => section.PerBetCredits = value);
	}

	public void SetExplorerLatestWinnerOnly(bool value)
	{
		BetsExplorerSection section = _settings.BetsExplorer ??= new BetsExplorerSection();
		Change(Keys.ExplorerLatestWinnerOnly, section.LatestWinnerOnly, value, () => section.LatestWinnerOnly = value);
	}

	/// <summary>
	/// Reads an enum setting stored by name. False for null, for an unknown name, and for a number that names no member,
	/// so a renamed or removed member falls back to the caller's default instead of producing an undefined value.
	/// </summary>
	public static bool TryParseEnum<T>(string? value, out T result) where T : struct, Enum =>
		Enum.TryParse(value, ignoreCase: false, out result) && Enum.IsDefined(result);

	// --- Strategy panel, per node id (D-27.5) ---------------------------------------------------------------------

	public IReadOnlyList<string> GetStrategyPanelNodeIds() =>
		_settings.StrategyPanel == null ? Array.Empty<string>() : new List<string>(_settings.StrategyPanel.Keys);

	/// <summary>A copy: editing it changes nothing until it is passed back to <see cref="SetStrategyPanelValues"/>.</summary>
	public StrategyPanelValues? GetStrategyPanelValues(string nodeId)
	{
		if (string.IsNullOrWhiteSpace(nodeId) || _settings.StrategyPanel == null ||
			!_settings.StrategyPanel.TryGetValue(nodeId, out StrategyPanelValues? stored))
		{
			return null;
		}

		return JsonSerializer.Deserialize<StrategyPanelValues>(JsonSerializer.Serialize(stored, JsonOptions), JsonOptions);
	}

	public void SetStrategyPanelValues(string nodeId, StrategyPanelValues values)
	{
		if (string.IsNullOrWhiteSpace(nodeId) || values == null)
		{
			return;
		}

		Dictionary<string, StrategyPanelValues> map = _settings.StrategyPanel ??= new Dictionary<string, StrategyPanelValues>();
		// Compared by their serialized form: the values are a small tree (Config is a class), and the file is the
		// thing a change must alter.
		string incoming = JsonSerializer.Serialize(values, JsonOptions);
		string? current = map.TryGetValue(nodeId, out StrategyPanelValues? stored)
			? JsonSerializer.Serialize(stored, JsonOptions)
			: null;
		Change(Keys.StrategyPanel + "." + nodeId, current, incoming,
			() => map[nodeId] = JsonSerializer.Deserialize<StrategyPanelValues>(incoming, JsonOptions)!);
	}

	// --- DEV (D-27.4): stored in the same file, read only in a DEBUG build ----------------------------------------
	// An exported build reads null and writes nothing, so a developer's 9000X can never reach a player's game. The
	// section is still carried through a release build's writes untouched, because it is part of the loaded object.

	public int? GetDevRequestedTimeScale()
	{
#if DEBUG
		return _settings.Dev?.RequestedTimeScale;
#else
		return null;
#endif
	}

	public bool? GetDevShowCasinoScBalances()
	{
#if DEBUG
		return _settings.Dev?.ShowCasinoScBalances;
#else
		return null;
#endif
	}

	public void SetDevRequestedTimeScale(int value)
	{
#if DEBUG
		DevSection section = _settings.Dev ??= new DevSection();
		Change(Keys.DevRequestedTimeScale, section.RequestedTimeScale, value, () => section.RequestedTimeScale = value);
#endif
	}

	public void SetDevShowCasinoScBalances(bool value)
	{
#if DEBUG
		DevSection section = _settings.Dev ??= new DevSection();
		Change(Keys.DevShowCasinoScBalances, section.ShowCasinoScBalances, value, () => section.ShowCasinoScBalances = value);
#endif
	}

	// --- Change, load, persist ------------------------------------------------------------------------------------

	// Pattern 6: a setting is written when it changes, never on a timer. Boxed comparison is fine here — a setting
	// changes at the rate of a click.
	private void Change(string key, object? current, object value, Action assign)
	{
		if (Equals(current, value))
		{
#if DEBUG
			_unchangedSetCount++;
#endif
			return;
		}

		assign();
#if DEBUG
		_changeCount++;
#endif
		Persist();
		SettingChanged?.Invoke(key);
	}

	private void Load()
	{
		if (!Godot.FileAccess.FileExists(SettingsPath))
		{
			GD.Print($"[UserSettings] no file at {SettingsPath}: every preference at its default");
			return;
		}

		SettingsFile? loaded;
		try
		{
			using Godot.FileAccess file = Godot.FileAccess.Open(SettingsPath, Godot.FileAccess.ModeFlags.Read);
			if (file == null)
			{
				throw new IOException($"could not open the file ({Godot.FileAccess.GetOpenError()})");
			}

			loaded = JsonSerializer.Deserialize<SettingsFile>(file.GetAsText(), JsonOptions);
			if (loaded == null)
			{
				throw new JsonException("the file holds JSON null");
			}
		}
		catch (Exception ex)
		{
			PreserveCorruptFile(ex.Message);
			return;
		}

		_settings = loaded;
		(int keys, int devKeys) = CountKeys();
#if DEBUG
		GD.Print($"[UserSettings] loaded {keys} keys from {SettingsPath} (version {_settings.SettingsVersion})");
#else
		GD.Print($"[UserSettings] loaded {keys - devKeys} keys from {SettingsPath} (version {_settings.SettingsVersion}); {devKeys} DEV keys ignored");
#endif
	}

	// Plan §3.3 — a bad file is moved aside and never overwritten, so the evidence survives; the game starts from
	// defaults and the next change writes a fresh file (DurableFile.MoveAsideAsCorrupt).
	private void PreserveCorruptFile(string detail)
	{
		string? target = DurableFile.MoveAsideAsCorrupt(SettingsPath);
		if (target != null)
		{
			GD.Print($"[UserSettings] could not read {SettingsPath} ({detail}). It is preserved as {target}; " +
				"every preference starts at its default, and the next change writes a fresh file.");
		}
		else
		{
			_persistBlocked = true;
			GD.Print($"[UserSettings] could not read {SettingsPath} ({detail}), and could not move it aside. " +
				"Nothing will be written this session, so the file is left exactly as found.");
		}

		GD.PushWarning($"[UserSettings] {SettingsPath} was unreadable: {detail}");
	}

	private void Persist()
	{
		if (_persistBlocked)
		{
			return;
		}

		try
		{
			_settings.SettingsVersion = CurrentSettingsVersion;
			DurableFile.WriteAtomically(SettingsPath, JsonSerializer.Serialize(_settings, JsonOptions));
#if DEBUG
			_writeCount++;
#endif
		}
		catch (Exception ex)
		{
			GD.Print($"[UserSettings] could not write {SettingsPath}: {ex.Message}");
			GD.PushError($"[UserSettings] could not write {SettingsPath}: {ex.Message}");
		}
	}

	/// <summary>
	/// The keys that hold a value, i.e. every preference not at its default (mini-plan 27 unit 5: the User Settings
	/// scene lists them). DEV keys only in a DEBUG build, matching what the getters return.
	/// </summary>
	public IReadOnlyList<string> GetSetKeys()
	{
		var keys = new List<string>();
		if (_settings.DiceGame?.BetView != null) keys.Add(Keys.DiceBetView);
		BetsExplorerSection? explorer = _settings.BetsExplorer;
		if (explorer?.Pace != null) keys.Add(Keys.ExplorerPace);
		if (explorer?.Speed != null) keys.Add(Keys.ExplorerSpeed);
		if (explorer?.PerBetCredits != null) keys.Add(Keys.ExplorerPerBetCredits);
		if (explorer?.LatestWinnerOnly != null) keys.Add(Keys.ExplorerLatestWinnerOnly);
		if (_settings.StrategyPanel != null)
		{
			foreach (string nodeId in _settings.StrategyPanel.Keys)
			{
				keys.Add(Keys.StrategyPanel + "." + nodeId);
			}
		}
#if DEBUG
		if (_settings.Dev?.RequestedTimeScale != null) keys.Add(Keys.DevRequestedTimeScale);
		if (_settings.Dev?.ShowCasinoScBalances != null) keys.Add(Keys.DevShowCasinoScBalances);
#endif
		return keys;
	}

	private (int Keys, int DevKeys) CountKeys()
	{
		int keys = 0;
		if (_settings.DiceGame?.BetView != null) keys++;

		BetsExplorerSection? explorer = _settings.BetsExplorer;
		if (explorer?.Pace != null) keys++;
		if (explorer?.Speed != null) keys++;
		if (explorer?.PerBetCredits != null) keys++;
		if (explorer?.LatestWinnerOnly != null) keys++;

		keys += _settings.StrategyPanel?.Count ?? 0;

		int devKeys = 0;
		if (_settings.Dev?.RequestedTimeScale != null) devKeys++;
		if (_settings.Dev?.ShowCasinoScBalances != null) devKeys++;

		return (keys + devKeys, devKeys);
	}

	// S2: writes must equal changes. Printed with GD.Print so godot.log receives it and nobody has to watch for it.
	[System.Diagnostics.Conditional("DEBUG")]
	private void ReportWriteCount()
	{
#if DEBUG
		GD.Print($"[UserSettings] session: {_changeCount} changes, {_writeCount} writes, {_unchangedSetCount} sets to the current value (no write)");
#endif
	}

	// --- The file's shape (camelCase on disk). Every leaf is nullable: null means "never set" ----------------------

	internal sealed class SettingsFile
	{
		public int SettingsVersion { get; set; } = CurrentSettingsVersion;
		public DiceGameSection? DiceGame { get; set; }
		public BetsExplorerSection? BetsExplorer { get; set; }
		public Dictionary<string, StrategyPanelValues>? StrategyPanel { get; set; }
		public DevSection? Dev { get; set; }
	}

	internal sealed class DiceGameSection
	{
		public string? BetView { get; set; }
	}

	internal sealed class BetsExplorerSection
	{
		public string? Pace { get; set; }
		public double? Speed { get; set; }
		public int? PerBetCredits { get; set; }
		public bool? LatestWinnerOnly { get; set; }
	}

	internal sealed class DevSection
	{
		public int? RequestedTimeScale { get; set; }
		public bool? ShowCasinoScBalances { get; set; }
	}
}
