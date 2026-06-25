using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Scripts.Hardware;
using GodotBlockchainPort.Simulation;
using UI.StatusBar;
#nullable enable

// Unified Pools & Hardware screen (Phase 4): a left node list + a right detail panel. For a mining node
// it shows/edits the individual↔casino credit split (and a DEV "Buy Hardware" button); for the casino it
// shows the community-pool stats (totals, fee, contributors, recent reward events). All state is read
// straight from the static HardwareAllocationRepository / CasinoPoolRepository — no NetworkRoot instance
// needed. Credit edits raise HardwareAllocationRepository.HardwareChanged so DiceGame re-locks its speed.
public partial class BTCPoolsAndHardwareShop : Control
{
	private const string CasinoNodeId = "casino";
	private static readonly string[] HardwareNodeIds = { "player", "bot_1", "bot_2", "bot_3", "bot_4" };

	private SceneManager? _sceneManager;

	private VBoxContainer _nodeList = null!;
	private VBoxContainer _detailVBox = null!;
	private Button _buyHardwareBtn = null!;

	private readonly Dictionary<string, Button> _nodeButtons = new();
	private string? _selectedNodeId;

	private double _refreshTimer;
	private const double RefreshInterval = 1.0;

	public override void _Ready()
	{
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		HardwareAllocationRepository.EnsureLoaded();
		CasinoPoolRepository.EnsureLoaded();

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());
		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);

		_nodeList = GetNode<VBoxContainer>("%NodeList");
		_detailVBox = GetNode<VBoxContainer>("%DetailVBox");
		_buyHardwareBtn = GetNode<Button>("%BuyHardwareBtn");
		_buyHardwareBtn.Pressed += OnBuyHardwarePressed;

		BuildNodeList();
		ShowNoSelection();
	}

	public override void _Process(double delta)
	{
		// Casino stats change as blocks are mined in the background — keep them live while shown.
		if (_selectedNodeId != CasinoNodeId) return;
		_refreshTimer += delta;
		if (_refreshTimer < RefreshInterval) return;
		_refreshTimer = 0d;
		BuildCasinoDetail();
	}

	// ── Node list ────────────────────────────────────────────────────────────

	private void BuildNodeList()
	{
		foreach (string nodeId in HardwareNodeIds.Append(CasinoNodeId))
		{
			var btn = new Button { Text = nodeId, Alignment = HorizontalAlignment.Left, ToggleMode = true };
			btn.Pressed += () => SelectNode(nodeId);
			_nodeList.AddChild(btn);
			_nodeButtons[nodeId] = btn;
		}
	}

	private void SelectNode(string nodeId)
	{
		_selectedNodeId = nodeId;
		foreach (var (id, btn) in _nodeButtons)
		{
			btn.ButtonPressed = id == nodeId;
		}

		if (nodeId == CasinoNodeId)
		{
			_buyHardwareBtn.Visible = false;
			BuildCasinoDetail();
		}
		else
		{
			_buyHardwareBtn.Visible = true; // DEV: free hardware for non-casino nodes
			BuildNodeDetail(nodeId);
		}
	}

	private void ShowNoSelection()
	{
		_selectedNodeId = null;
		_buyHardwareBtn.Visible = false;
		ClearDetail();
		AddDetailLabel("Select a mining node to manage its hardware pools.", 18);
	}

	// ── Detail: a mining node's pool split ───────────────────────────────────

	private void BuildNodeDetail(string nodeId)
	{
		ClearDetail();
		NodeHardwareState hw = HardwareAllocationRepository.GetNode(nodeId);

		AddDetailLabel(nodeId, 20);
		AddDetailLabel($"Total credits: {hw.TotalCredits}   (each credit = 1 nonce attempt/sec)");

		AddPoolRow($"Individual Pool: {hw.IndividualPoolCredits}", "→ Casino Pool",
			hw.IndividualPoolCredits <= 0, () => HardwareAllocationRepository.MoveToCasinoPool(nodeId, 1), nodeId);
		AddPoolRow($"Casino Pool: {hw.CasinoPoolCredits}", "← Individual",
			hw.CasinoPoolCredits <= 0, () => HardwareAllocationRepository.MoveToIndividual(nodeId, 1), nodeId);

		AddDetailLabel(
			"Moving a credit to the casino pool reallocates this node's mining power to the casino's shared " +
			"pool (rewards paid out per pool rules, minus the dynamic fee). Total betting speed is unchanged.", 12);
	}

	private void AddPoolRow(string text, string buttonText, bool disabled, System.Action move, string nodeId)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);

		var label = new Label { Text = text, CustomMinimumSize = new Vector2(220, 0) };
		var btn = new Button { Text = buttonText, Disabled = disabled };
		btn.Pressed += () =>
		{
			move();
			BuildNodeDetail(nodeId); // refresh with the new split
		};

		row.AddChild(label);
		row.AddChild(btn);
		_detailVBox.AddChild(row);
	}

	// ── Detail: casino community pool stats ──────────────────────────────────

	private void BuildCasinoDetail()
	{
		ClearDetail();
		int casinoTotal = HardwareAllocationRepository.TotalCasinoPoolCredits();
		int individualTotal = HardwareAllocationRepository.TotalIndividualCredits();
		decimal feePercent = NetworkRoot.CalculateCasinoFeePercent(casinoTotal, individualTotal);

		AddDetailLabel("Casino Community Pool", 20);
		AddDetailLabel($"Total casino pool credits: {casinoTotal}");
		AddDetailLabel($"Individual total credits: {individualTotal}");
		AddDetailLabel($"Current fee: {feePercent * 100m:0.0}%");

		AddDetailLabel("Contributors", 16);
		List<NodeHardwareState> contributors = HardwareAllocationRepository.AllNodes()
			.Where(n => n.CasinoPoolCredits > 0).ToList();
		if (contributors.Count == 0)
		{
			AddDetailLabel("No contributors yet.", 12);
		}
		else
		{
			foreach (NodeHardwareState n in contributors)
			{
				AddDetailLabel($"    {n.NodeId}: {n.CasinoPoolCredits} credit(s)", 12);
			}
		}

		AddDetailLabel("Recent reward events", 16);
		IReadOnlyList<CasinoPoolRewardEvent> history = CasinoPoolRepository.Current.RewardHistory;
		if (history.Count == 0)
		{
			AddDetailLabel("No casino blocks mined yet.", 12);
			return;
		}

		var table = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			ScrollActive = false,
			CustomMinimumSize = new Vector2(0, 200),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};

		var sb = new StringBuilder();
		sb.Append("[table=6]");
		foreach (string h in new[] { "Block", "Reward", "Fee%", "Fee", "Net dist.", "Status" })
		{
			sb.Append("[cell][b]").Append(h).Append("[/b][/cell]");
		}

		foreach (CasinoPoolRewardEvent evt in history.AsEnumerable().Reverse().Take(10))
		{
			decimal netDist = evt.Payouts.Sum(p => p.NetAmount);
			string status = evt.Distributed ? "[color=green]distributed[/color]" : "[color=yellow]pending[/color]";
			sb.Append("[cell]").Append(evt.BlockIndex).Append("[/cell]");
			sb.Append("[cell]").Append(evt.TotalReward.ToString("0.0000")).Append("[/cell]");
			sb.Append("[cell]").Append((evt.CasinoFeePercent * 100m).ToString("0.0")).Append("[/cell]");
			sb.Append("[cell]").Append(evt.CasinoFeeAmount.ToString("0.0000")).Append("[/cell]");
			sb.Append("[cell]").Append(netDist.ToString("0.0000")).Append("[/cell]");
			sb.Append("[cell]").Append(status).Append("[/cell]");
		}
		sb.Append("[/table]");
		table.Text = sb.ToString();
		_detailVBox.AddChild(table);
	}

	// ── Buy Hardware (DEV) ───────────────────────────────────────────────────

	private void OnBuyHardwarePressed()
	{
		if (_selectedNodeId == null || _selectedNodeId == CasinoNodeId) return;
		HardwareAllocationRepository.AddCredits(_selectedNodeId, 1); // lands in the individual pool
		BuildNodeDetail(_selectedNodeId);
	}

	// ── Detail panel helpers ─────────────────────────────────────────────────

	private void ClearDetail()
	{
		foreach (Node child in _detailVBox.GetChildren())
		{
			_detailVBox.RemoveChild(child); // detach now so rebuilt content doesn't collide
			child.QueueFree();
		}
	}

	private void AddDetailLabel(string text, int fontSize = 14)
	{
		var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		label.AddThemeFontSizeOverride("font_size", fontSize);
		_detailVBox.AddChild(label);
	}
}
