using Godot;
using System.Collections.Generic;
using System.Linq;
using Scripts.Finance;
using UI.Readouts;
using UI.StatusBar;
using UI.NotepadPopup;
#nullable enable

// Study screen: shows the most recent settled plays of each miner bot currently betting alongside the player.
// Data comes from SimulationService's in-memory per-bot ring buffers (see GetBotPlayHistory), so the list is
// empty after a launch until a bot has settled a bet in this session.
//
// Mini-plan 25 (developer, 2026-10-19): the plays are shown in DiceGame's own bet list (BetHistoryContainer), so a
// bot's plays flow exactly as the player's do: every settled bet of the SELECTED bot goes into the list's ring
// as it happens, and once per frame the list rewrites only its visible rows (mini-plan 10). It replaced a
// 260-row BBCode table rebuilt at most once a second (17.C.4), which the developer found too slow to follow.
// The columns are DiceGame's: time, multiplier, bet, profit (win/loss by colour). The old Roll and Result
// columns are gone with the table.
public partial class BotPlayHistory : Control
{
	private SceneManager? _sceneManager;
	private SimulationService? _simulation;
	private CalendarTimeService? _calendar;

	private VBoxContainer _botList = null!;
	private Label _botTitleLabel = null!;
	private BetHistoryContainer _history = null!;

	private NotepadPopup _notepadPopup = null!;

	private readonly Dictionary<string, Button> _botButtons = new();
	private string? _selectedNodeId;

	// ClientBetSettled fires on every settled bet of every client (hundreds a second at 9000X), so the handler
	// does O(1) work only (§38.7 rule 1): one ring write for the selected bot's bets, and a flag for the bot list.
	// The list changes only when a node is seen for the first time: a bot is listed once it has a recorded play,
	// the sim pushes that play just before it fires the event, and the rings are never removed in-process.
	private readonly HashSet<string> _seenNodeIds = new();
	private bool _listDirty;

	// §29.13 — the time column may not show a finer field than the clock can honestly carry at this speed; the
	// same measured unit DiceGame hands its own list (mini-plan 20 C).
	private readonly AdaptiveReadoutSampler _clockSampler = new();

	public override void _Ready()
	{
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		_simulation = GetNodeOrNull<SimulationService>("/root/SimulationService");
		_calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());
		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);

		_notepadPopup = new NotepadPopup();
		AddChild(_notepadPopup);
		GetNode<Button>("%NotepadBtn").Pressed += _notepadPopup.Open;

		_botList = GetNode<VBoxContainer>("%BotList");
		_botTitleLabel = GetNode<Label>("%BotTitleLabel");
		_history = GetNode<BetHistoryContainer>("%BotHistoryContainer");

		RebuildBotList();
		ShowNoSelection();

		if (_simulation != null) _simulation.ClientBetSettled += OnClientBetSettled;
	}

	public override void _ExitTree()
	{
		if (_simulation != null) _simulation.ClientBetSettled -= OnClientBetSettled;
	}

	private void OnClientBetSettled(string nodeId, string _, BetTransactionEvent bet)
	{
		if (_seenNodeIds.Add(nodeId)) _listDirty = true;
		if (nodeId == _selectedNodeId && bet != null) _history.AppendLiveEvent(bet);
	}

	public override void _Process(double delta)
	{
		if (_listDirty)
		{
			_listDirty = false;
			RebuildBotList();
		}

		if (_calendar != null && _clockSampler.ShouldRepaint(delta))
		{
			_clockSampler.NoteRepaint(_calendar.CurrentLocalDateTime);
			_history.SetMotionUnit(_clockSampler.Unit);
		}
	}

	// ── Bot list ─────────────────────────────────────────────────────────────

	private void RebuildBotList()
	{
		Scripts.Diagnostics.UiRefreshProbe.Count("BotPlayHistory.RebuildBotList");   // DEBUG-only, mini-plan 25
		IReadOnlyList<string> nodeIds = _simulation?.GetActiveBotNodeIds() ?? new List<string>();
		_seenNodeIds.UnionWith(nodeIds); // a listed bot never needs a first-sighting rebuild

		// Drop buttons for bots that are no longer present.
		foreach (string existing in new List<string>(_botButtons.Keys))
		{
			if (!nodeIds.Contains(existing))
			{
				_botButtons[existing].QueueFree();
				_botButtons.Remove(existing);
			}
		}

		// Add buttons for newly seen bots (preserve order from GetActiveBotNodeIds).
		foreach (string nodeId in nodeIds)
		{
			if (_botButtons.ContainsKey(nodeId)) continue;
			var btn = new Button { Text = nodeId, Alignment = HorizontalAlignment.Left, ToggleMode = true };
			btn.Pressed += () => SelectBot(nodeId);
			_botList.AddChild(btn);
			_botButtons[nodeId] = btn;
		}

		if (_selectedNodeId == null) PaintNoSelectionCaption();
	}

	private void SelectBot(string nodeId)
	{
		_selectedNodeId = nodeId;
		foreach (var (id, btn) in _botButtons)
		{
			btn.ButtonPressed = id == nodeId;
		}

		IReadOnlyList<BetTransactionEvent> plays = _simulation?.GetBotPlayHistory(nodeId) ?? new List<BetTransactionEvent>();
		// Dense columns declare their currency once, in the caption (CLAUDE.md, Money Handling).
		_botTitleLabel.Text = plays.Count == 0
			? $"Last plays — {nodeId}: none recorded yet in this session."
			: $"Last plays — {nodeId}   (time · multiplier · bet (SC) · profit/loss (SC), newest on top)";
		_history.LoadFromEvents(plays);
	}

	private void ShowNoSelection()
	{
		_selectedNodeId = null;
		_history.ClearEntries();
		PaintNoSelectionCaption();
	}

	// Repainted by RebuildBotList too, so the "nobody has played yet" wording turns into the prompt the moment a
	// first bot is listed.
	private void PaintNoSelectionCaption()
	{
		_botTitleLabel.Text = _botButtons.Count == 0
			? "No miner bot has played in this session yet: the list fills as soon as one settles a bet."
			: "Select a miner bot to study its recent plays.";
	}
}
