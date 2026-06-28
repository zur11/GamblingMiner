using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GodotBlockchainPort.Simulation;
using GodotBlockchainPort.Blockchain;
using UI.StatusBar;
using UI.NotepadPopup;
#nullable enable

public partial class BlockExplorer : Control
{
    private NetworkRoot _networkRoot = null!;
    private SceneManager? _sceneManager;
    private SimulationService? _simulationService;

    private OptionButton _minerNodeOption = null!;
    private Label _chainInfoLabel = null!;
    // One scrollable right column (Latest Block + Network Status + Address Directory) — a single
    // internally-scrolling RichTextLabel so the whole column is reachable (incl. Satoshi, last in the directory).
    private RichTextLabel _rightColumnLabel = null!;

    private LineEdit _txLookupInput = null!;
    private LineEdit _addressLookupInput = null!;
    private LineEdit _blockLookupInput = null!;
    private RichTextLabel _lookupResultLabel = null!;

    // Notepad
    private NotepadPopup _notepadPopup = null!;

    // Enroll Mode (referral-auction foundation) — built programmatically
    private CheckBox _enrollModeToggle = null!;
    private RichTextLabel _enrollModeLabel = null!;

    // Live auto-refresh so background simulation (mining/balances) shows in real time.
    private double _autoRefreshTimer;
    private const double AutoRefreshInterval = 1.0;

    public override void _Ready()
    {
        _networkRoot = GetNode<NetworkRoot>("NetworkRoot");
        _minerNodeOption = GetNode<OptionButton>("%MinerNodeOption");

        _chainInfoLabel = GetNode<Label>("%ChainInfoLabel");
        _rightColumnLabel = GetNode<RichTextLabel>("%RightColumnLabel");

        _txLookupInput = GetNode<LineEdit>("%TxLookupInput");
        _addressLookupInput = GetNode<LineEdit>("%AddressLookupInput");
        _blockLookupInput = GetNode<LineEdit>("%BlockLookupInput");
        _lookupResultLabel = GetNode<RichTextLabel>("%LookupResultLabel");

        GetNode<Button>("%LookupTxButton").Pressed      += OnLookupTransactionPressed;
        GetNode<Button>("%LookupAddressButton").Pressed += OnLookupAddressPressed;
        GetNode<Button>("%LookupBlockButton").Pressed   += OnLookupBlockPressed;
        GetNode<Button>("%BackToDiceButton").Pressed    += OnBackToDicePressed;
        _sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
        _simulationService = GetNodeOrNull<SimulationService>("/root/SimulationService");

        // A dedicated "Go to DiceGame" button (the existing one goes to the Main Menu).
        var goToDiceBtn = new Button { Text = "Go to DiceGame" };
        goToDiceBtn.Pressed += OnGoToDicePressed;
        Button backBtn = GetNode<Button>("%BackToDiceButton");
        backBtn.GetParent().AddChild(goToDiceBtn);
        backBtn.GetParent().MoveChild(goToDiceBtn, backBtn.GetIndex() + 1);

        var mainVBox = GetNode<VBoxContainer>("Margin/MainVBox");
        var statusBar = new StatusBar();
        mainVBox.AddChild(statusBar);
        mainVBox.MoveChild(statusBar, 0);

        var devTimeScale = new UI.DevTimeScaleSelector.DevTimeScaleSelector();
        mainVBox.AddChild(devTimeScale);
        mainVBox.MoveChild(devTimeScale, 1);

        _notepadPopup = new NotepadPopup();
        AddChild(_notepadPopup);
        GetNode<Button>("%NotepadBtn").Pressed += _notepadPopup.Open;

        BuildEnrollModePanel();

        PopulateNodeSelectors();
        RefreshUi();
    }

    // "Enroll Mode" (referral-auction foundation): a toggle that reveals the donation race for
    // still-recruitable non-miner holder bots. Observe-only for now — enrolled/permanent filtering
    // activates once auction resolution (window timing) and the economy land.
    private void BuildEnrollModePanel()
    {
        // Toggle lives in the top action bar (always visible).
        var topActions = GetNode<HBoxContainer>("Margin/MainVBox/TopActions");
        _enrollModeToggle = new CheckBox { Text = "Enroll Mode" };
        _enrollModeToggle.Toggled += _ => RefreshEnrollMode();
        topActions.AddChild(_enrollModeToggle);

        // Panel sits just below the top bar (above the main split), so it's visible when toggled on.
        var mainVBox = GetNode<VBoxContainer>("Margin/MainVBox");
        _enrollModeLabel = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            Visible = false,
            CustomMinimumSize = new Vector2(0, 160)
        };
        mainVBox.AddChild(_enrollModeLabel);
        mainVBox.MoveChild(_enrollModeLabel, topActions.GetIndex() + 1);
    }

    private void RefreshEnrollMode()
    {
        bool on = _enrollModeToggle.ButtonPressed;
        _enrollModeLabel.Visible = on;
        if (!on) return;

        var ledger = _networkRoot.GetNonMinerAuctionLedger();
        long nowMs = _networkRoot.GetPlayerLatestBlock().Timestamp;

        int inAuction = ledger.Count(s => s.Status == NonMinerAuctionStatus.InAuction);
        int resolved = ledger.Count(s => s.Status == NonMinerAuctionStatus.Resolved);
        int notYet = ledger.Count(s => s.Status == NonMinerAuctionStatus.NotIntroduced);

        var sb = new StringBuilder();
        sb.AppendLine("[b]Enroll Mode — referral auction[/b]");
        sb.AppendLine($"In auction (recruitable): {inAuction}  |  Resolved: {resolved}  |  Not yet introduced: {notYet}");

        foreach (NonMinerDonationSummary s in ledger.Where(s => s.Status == NonMinerAuctionStatus.InAuction))
        {
            string leader = string.IsNullOrEmpty(s.LeadingDonorAddress)
                ? "no donations yet"
                : $"leading {_networkRoot.DescribeAddress(s.LeadingDonorAddress)} ({s.LeadingDonorTotal:F8})";
            double daysLeft = Math.Max(0d, (s.WindowCloseUnixMs - nowMs) / 86_400_000d);
            sb.AppendLine($"{s.NonMinerNodeId}  {s.NonMinerAddress[..10]}…  | recv {s.TotalReceived:F8} ({s.DonorCount} donor)  | {leader}  | {daysLeft:0.0}d left");
        }

        if (resolved > 0)
        {
            sb.AppendLine("[b]Resolved (out of auction):[/b]");
            foreach (NonMinerDonationSummary s in ledger.Where(s => s.Status == NonMinerAuctionStatus.Resolved))
            {
                string winner = string.IsNullOrEmpty(s.WinnerAddress)
                    ? "no winner (no donations)"
                    : $"referral of {_networkRoot.DescribeAddress(s.WinnerAddress)}";
                sb.AppendLine($"{s.NonMinerNodeId}  | {winner}");
            }
        }

        _enrollModeLabel.Text = sb.ToString();
    }

    public override void _Process(double delta)
    {
        // Reflect the background simulation (blocks, balances, auction) in real time.
        _autoRefreshTimer += delta;
        if (_autoRefreshTimer < AutoRefreshInterval) return;
        _autoRefreshTimer = 0d;
        RefreshUi();
    }

    private void PopulateNodeSelectors()
    {
        string[] nodeIds = _networkRoot.GetNodeIds().ToArray();
        _minerNodeOption.Clear();
        foreach (string nodeId in nodeIds)
            _minerNodeOption.AddItem(nodeId);

        int playerIndex = Array.IndexOf(nodeIds, "player");
        if (playerIndex >= 0)
            _minerNodeOption.Select(playerIndex);
    }

    private string BuildAddressDirectory()
    {
        return "[b]Node -> Address[/b]\n" + string.Join("\n", _networkRoot.GetNodeAddressLines());
    }

    private void OnLookupTransactionPressed()
    {
        string txId = _txLookupInput.Text.Trim();
        if (string.IsNullOrEmpty(txId)) { _lookupResultLabel.Text = "Enter a transaction hash first."; return; }
        string nodeId = _minerNodeOption.GetItemText(_minerNodeOption.Selected);
        _lookupResultLabel.Text = "[b]Transaction Lookup[/b]\n" + _networkRoot.BuildTransactionDetails(nodeId, txId);
    }

    private void OnLookupAddressPressed()
    {
        string address = _addressLookupInput.Text.Trim();
        if (string.IsNullOrEmpty(address)) { _lookupResultLabel.Text = "Enter an address first."; return; }
        string nodeId = _minerNodeOption.GetItemText(_minerNodeOption.Selected);
        _lookupResultLabel.Text = "[b]Address Lookup[/b]\n" + _networkRoot.BuildAddressDetailsForNode(nodeId, address);
    }

    private void OnLookupBlockPressed()
    {
        if (!int.TryParse(_blockLookupInput.Text.Trim(), out int blockIndex) || blockIndex <= 0)
        {
            _lookupResultLabel.Text = "Enter a valid positive block number.";
            return;
        }

        string nodeId = _minerNodeOption.GetItemText(_minerNodeOption.Selected);
        Block? block = _networkRoot.GetBlockByIndexForNode(nodeId, blockIndex);
        if (block is null) { _lookupResultLabel.Text = $"Block {blockIndex} not found for node {nodeId}."; return; }

        StringBuilder sb = new();
        sb.AppendLine("[b]Block Lookup[/b]");
        sb.AppendLine($"Node: {nodeId}");
        sb.AppendLine($"Index: {block.Index}");
        sb.AppendLine($"Time: {FormatBlockTime(block.Timestamp)}");
        sb.AppendLine($"Hash: {block.Hash}");
        sb.AppendLine($"PrevHash: {block.PreviousBlockHash}");
        sb.AppendLine($"MerkleRoot: {block.MerkleRoot}");
        sb.AppendLine($"Nonce: {block.Nonce}");
        sb.AppendLine($"Difficulty: {block.Difficulty:F2}  (~{block.Difficulty:F0} attempts/block)");
        decimal blockFees = block.Transactions
            .Where(t => t.Sender != BlockchainService.CoinbaseSender)
            .Sum(t => t.Fee);
        sb.AppendLine($"Transactions: {block.Transactions.Count}  |  Fees collected: {blockFees:F8} BTC");
        foreach (Transaction tx in block.Transactions)
        {
            bool isCoinbase = tx.Sender == BlockchainService.CoinbaseSender;
            sb.AppendLine("-");
            sb.AppendLine($"TxId: {tx.TransactionId}{(isCoinbase ? "  [COINBASE]" : "")}");
            sb.AppendLine($"Amount: {tx.Amount:F8}");
            if (!isCoinbase) sb.AppendLine($"Fee: {tx.Fee:F8}");
            sb.AppendLine($"Sender: {tx.Sender}");
            sb.AppendLine($"Recipient: {tx.Recipient}");
        }
        _lookupResultLabel.Text = sb.ToString();
    }

    private void OnBackToDicePressed()
    {
        CalendarTimeService? calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
        calendar?.PersistCurrentTime();
        _sceneManager?.Go(SceneManager.SceneId.MainMenu);
    }

    private void OnGoToDicePressed()
    {
        CalendarTimeService? calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
        calendar?.PersistCurrentTime();
        _sceneManager?.Go(SceneManager.SceneId.DiceGame);
    }

    private void RefreshUi()
    {
        Block last = _networkRoot.GetPlayerLatestBlock();

        // Difficulty readout (D.3): the MAIN presenter shows the difficulty of the block being mined NOW
        // (next-block difficulty), not the last mined block — each mined block's own value is shown in its panel.
        int window = BlockchainService.LwmaWindow;
        double miningDifficulty = _networkRoot.GetPlayerNextBlockDifficulty();
        double avgBlockSec = _networkRoot.GetPlayerRecentAverageBlockSeconds(window);
        double targetSec = BlockchainService.TargetBlockSeconds;
        string trend = miningDifficulty > last.Difficulty * 1.001 ? "rising ↑"
            : miningDifficulty < last.Difficulty * 0.999 ? "falling ↓"
            : "steady →";
        string avgBlockText = avgBlockSec > 0 ? FormatDuration(avgBlockSec) : "n/a";

        _chainInfoLabel.Text =
            $"Player chain length: {_networkRoot.GetPlayerChainLength()} | Player pending tx: {_networkRoot.GetPlayerPendingTransactionCount()}"
            + $" | Mining difficulty (block #{last.Index + 1}): {miningDifficulty:F2} ({trend})"
            + $" | Avg block time (last {window}): {avgBlockText} (target {FormatDuration(targetSec)})";

        // Preserve the label's own internal scroll position across the 1 s refresh (setting Text resets it to top).
        VScrollBar rightVScroll = _rightColumnLabel.GetVScrollBar();
        double rightScroll = rightVScroll.Value;

        _rightColumnLabel.Text =
            "[b]Latest Block (player view)[/b]\n" +
            $"Index: {last.Index}\n" +
            $"Time: {FormatBlockTime(last.Timestamp)}\n" +
            $"Nonce: {last.Nonce}\n" +
            $"Difficulty: {last.Difficulty:F2}  (~{last.Difficulty:F0} attempts/block)\n" +
            $"Hash: {last.Hash}\n" +
            $"PrevHash: {last.PreviousBlockHash}\n" +
            $"MerkleRoot: {last.MerkleRoot}\n" +
            $"Transactions: {last.Transactions.Count}\n" +
            BuildLatestTransactionPreview(last) +
            "\n\n[b]Network Status[/b]\n" + string.Join("\n", BuildNodeStatusLinesWithMiningRates()) +
            "\n\n" + BuildAddressDirectory() +
            "\n\n\n"; // trailing padding so the last real line (Satoshi) clears the scroll's bottom edge

        rightVScroll.Value = rightScroll;

        RefreshEnrollMode();
    }

    // Appends a "⛏ <bets/sec>" marker to nodes that are actively mining in the background simulation.
    private IEnumerable<string> BuildNodeStatusLinesWithMiningRates()
    {
        IReadOnlyDictionary<string, double> rates =
            _simulationService?.GetActiveMiningRates() ?? new Dictionary<string, double>();

        foreach (string line in _networkRoot.GetNodeStatusLines())
        {
            int sep = line.IndexOf(" | ", StringComparison.Ordinal);
            string nodeId = sep > 0 ? line[..sep] : line;
            yield return rates.TryGetValue(nodeId, out double bps)
                ? $"{line} | ⛏ {bps:0.#}/s"
                : line;
        }
    }

    private static string FormatBlockTime(long unixMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

    // In-game block time as a human-readable duration (e.g. "16h 15m", "2d 03h 10m").
    private static string FormatDuration(double seconds)
    {
        if (seconds <= 0) return "n/a";
        long total = (long)Math.Round(seconds);
        long days = total / 86400; total %= 86400;
        long hours = total / 3600; total %= 3600;
        long mins = total / 60;
        return days > 0 ? $"{days}d {hours:00}h {mins:00}m" : $"{hours}h {mins:00}m";
    }

    private static string BuildLatestTransactionPreview(Block block)
    {
        if (block.Transactions.Count == 0) return "Last block tx details: none";
        Transaction tx = block.Transactions[0];
        bool isCoinbase = tx.Sender == BlockchainService.CoinbaseSender;
        return
            "Last block first tx:\n" +
            $"TxId: {tx.TransactionId}{(isCoinbase ? "  [COINBASE]" : "")}\n" +
            $"Amount: {tx.Amount:F8}\n" +
            (isCoinbase ? string.Empty : $"Fee: {tx.Fee:F8}\n") +
            $"Sender: {tx.Sender}\n" +
            $"Recipient: {tx.Recipient}";
    }
}
