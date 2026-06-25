using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Scripts.Finance;
using UI.StatusBar;
using UI.NotepadPopup;
#nullable enable

// Study screen: shows the last 260 settled plays of each miner bot currently betting alongside the
// player. Data comes from SimulationService's in-memory per-bot ring buffers (see GetBotPlayHistory).
// Read-only and live-updating on a light timer so it tracks a running background autobet.
public partial class BotPlayHistory : Control
{
	private SceneManager? _sceneManager;
	private SimulationService? _simulation;

	private VBoxContainer _botList = null!;
	private Label _botTitleLabel = null!;
	private RichTextLabel _historyTable = null!;

	private NotepadPopup _notepadPopup = null!;

	private readonly Dictionary<string, Button> _botButtons = new();
	private string? _selectedNodeId;

	private double _refreshTimer;
	private const double RefreshInterval = 1.0;

	public override void _Ready()
	{
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		_simulation = GetNodeOrNull<SimulationService>("/root/SimulationService");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());
		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);

		_notepadPopup = new NotepadPopup();
		AddChild(_notepadPopup);
		GetNode<Button>("%NotepadBtn").Pressed += _notepadPopup.Open;

		_botList = GetNode<VBoxContainer>("%BotList");
		_botTitleLabel = GetNode<Label>("%BotTitleLabel");
		_historyTable = GetNode<RichTextLabel>("%HistoryTable");

		RebuildBotList();
		ShowNoSelection();
	}

	public override void _Process(double delta)
	{
		_refreshTimer += delta;
		if (_refreshTimer < RefreshInterval) return;
		_refreshTimer = 0d;

		RebuildBotList();
		if (_selectedNodeId != null) RefreshHistoryTable(_selectedNodeId);
	}

	// ── Bot list ─────────────────────────────────────────────────────────────

	private void RebuildBotList()
	{
		IReadOnlyList<string> nodeIds = _simulation?.GetActiveBotNodeIds() ?? new List<string>();

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
	}

	private void SelectBot(string nodeId)
	{
		_selectedNodeId = nodeId;
		foreach (var (id, btn) in _botButtons)
		{
			btn.ButtonPressed = id == nodeId;
		}
		_botTitleLabel.Text = $"Last plays — {nodeId}";
		RefreshHistoryTable(nodeId);
	}

	private void ShowNoSelection()
	{
		_selectedNodeId = null;
		_botTitleLabel.Text = "Select a miner bot to study its recent plays.";
		_historyTable.Text = string.Empty;
	}

	// ── History table ──────────────────────────────────────────────────────────

	private void RefreshHistoryTable(string nodeId)
	{
		IReadOnlyList<SimulationService.BotPlayEntry> plays =
			_simulation?.GetBotPlayHistory(nodeId) ?? new List<SimulationService.BotPlayEntry>();

		if (plays.Count == 0)
		{
			_historyTable.Text = "[i]No plays recorded yet for this bot.[/i]";
			return;
		}

		var sb = new StringBuilder();
		sb.Append("[table=6]");
		AppendHeader(sb, "#", "Bet", "Roll", "Mult.", "Result", "Profit");

		int index = plays.Count; // newest first → highest number at top
		foreach (SimulationService.BotPlayEntry play in plays)
		{
			string result = play.IsWin ? "[color=green]WIN[/color]" : "[color=red]LOSS[/color]";
			string profitColor = play.Profit >= 0m ? "green" : "red";
			AppendRow(sb,
				index.ToString(),
				play.BetAmount.ToString("0.00000000"),
				play.Roll.ToString(),
				$"{play.Multiplier.ToString("0.####")}x",
				result,
				$"[color={profitColor}]{Money.FormatSignedAdaptive(play.Profit)}[/color]");
			index--;
		}

		sb.Append("[/table]");
		_historyTable.Text = sb.ToString();
	}

	private static void AppendHeader(StringBuilder sb, params string[] cells)
	{
		foreach (string cell in cells)
		{
			sb.Append("[cell][b]").Append(cell).Append("[/b][/cell]");
		}
	}

	private static void AppendRow(StringBuilder sb, params string[] cells)
	{
		foreach (string cell in cells)
		{
			sb.Append("[cell]").Append(cell).Append("[/cell]");
		}
	}
}
