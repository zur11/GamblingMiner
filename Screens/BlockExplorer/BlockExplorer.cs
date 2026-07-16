using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
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
    private VBoxContainer _enrollModePanel = null!;
    private Label _enrollModeSummaryLabel = null!;
    private VBoxContainer _enrollModeRowsVBox = null!;

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
        // Step 14 (ND.5d) — a scrollable VBox of rows (was a single RichTextLabel blob) so each recruitable
        // non-miner can carry its own "Details →" button (D-ND5.2) alongside its leading-bid info.
        var mainVBox = GetNode<VBoxContainer>("Margin/MainVBox");
        _enrollModePanel = new VBoxContainer { Visible = false };

        _enrollModeSummaryLabel = new Label();
        _enrollModePanel.AddChild(_enrollModeSummaryLabel);

        var enrollScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 220),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _enrollModeRowsVBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
        enrollScroll.AddChild(_enrollModeRowsVBox);
        _enrollModePanel.AddChild(enrollScroll);

        mainVBox.AddChild(_enrollModePanel);
        mainVBox.MoveChild(_enrollModePanel, topActions.GetIndex() + 1);
    }

    private void RefreshEnrollMode()
    {
        bool on = _enrollModeToggle.ButtonPressed;
        _enrollModePanel.Visible = on;
        if (!on) return;

        var ledger = _networkRoot.GetNonMinerAuctionLedger();
        long nowMs = _networkRoot.GetPlayerLatestBlock().Timestamp;

        int inAuction = ledger.Count(s => s.Status == NonMinerAuctionStatus.InAuction);
        int resolved = ledger.Count(s => s.Status == NonMinerAuctionStatus.Resolved);
        int notYet = ledger.Count(s => s.Status == NonMinerAuctionStatus.NotIntroduced);
        _enrollModeSummaryLabel.Text =
            $"Enroll Mode — referral auction   |   In auction (recruitable): {inAuction}  |  Resolved: {resolved}  |  Not yet introduced: {notYet}";

        foreach (Node child in _enrollModeRowsVBox.GetChildren())
            child.QueueFree();

        // EB.2 (D-EB.6/7): TotalReceived counts ALL funding (bot economy + player bids); the leader and
        // the countdown reflect only QUALIFYING bids. A non-miner with no qualifying bid yet shows
        // "awaiting first bid" — its window countdown has not started and it stays recruitable.
        // ND.4b (D-ND4b.10, corrected 2026-07-11): the leading bid's BTC principal now also shows its
        // LIVE, CURRENT SC value (priced as of now, never frozen at the bid's own day).
        foreach (NonMinerDonationSummary s in ledger.Where(s => s.Status == NonMinerAuctionStatus.InAuction))
        {
            string scValue = s.LeadingDonorScValue is decimal sc
                ? string.Create(CultureInfo.InvariantCulture, $" ≈ {sc:F8} SC")
                : string.Empty;
            string leader = string.IsNullOrEmpty(s.LeadingDonorAddress)
                ? "no bids yet"
                : string.Create(CultureInfo.InvariantCulture, $"leading bid {_networkRoot.DescribeAddress(s.LeadingDonorAddress)} ({s.LeadingDonorTotal:F8} BTC{scValue})");
            string clock = s.LeadingBidUnixMs == 0
                ? "awaiting first bid — no countdown"
                : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0d, (s.WindowCloseUnixMs - nowMs) / 86_400_000d):0.0}d left");
            string line = string.Create(CultureInfo.InvariantCulture,
                $"{s.NonMinerNodeId}  {s.NonMinerAddress[..10]}…  | recv {s.TotalReceived:F8} ({s.DonorCount} donor)  | {leader}  | {clock}");

            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
            row.AddChild(new Label { Text = line, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass });

            // D-ND5.2 — the details button appears ONLY once this non-miner has received at least one
            // qualifying bid (a leader exists); a never-bid-on entry has nothing to show yet.
            if (s.LeadingBidUnixMs != 0)
            {
                var detailsBtn = new Button { Text = "Details →" };
                string nonMinerAddress = s.NonMinerAddress;
                detailsBtn.Pressed += () => OnOpenRecruitableBiddingDetails(nonMinerAddress);
                row.AddChild(detailsBtn);
            }

            _enrollModeRowsVBox.AddChild(row);
        }

        if (resolved > 0)
        {
            _enrollModeRowsVBox.AddChild(new Label { Text = "Resolved (out of auction):" });
            foreach (NonMinerDonationSummary s in ledger.Where(s => s.Status == NonMinerAuctionStatus.Resolved))
            {
                string winner = string.IsNullOrEmpty(s.WinnerAddress)
                    ? "no winner (legacy pre-EB.2 world)"
                    : $"referral of {_networkRoot.DescribeAddress(s.WinnerAddress)}";
                // D-ND5.9 — Resolved entries never offer the Details button again; the scene is only ever
                // reachable while InAuction (the "gets deleted" behavior is this gate's natural consequence).
                _enrollModeRowsVBox.AddChild(new Label { Text = $"{s.NonMinerNodeId}  | {winner}" });
            }
        }
    }

    private void OnOpenRecruitableBiddingDetails(string nonMinerAddress)
    {
        RecruitableBiddingDetails.PendingNonMinerAddress = nonMinerAddress;
        _sceneManager?.Go(SceneManager.SceneId.RecruitableBiddingDetails);
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
        SetLookupResult("[b]Transaction Lookup[/b]\n" + _networkRoot.BuildTransactionDetails(nodeId, txId));
    }

    private void OnLookupAddressPressed()
    {
        string address = _addressLookupInput.Text.Trim();
        if (string.IsNullOrEmpty(address)) { _lookupResultLabel.Text = "Enter an address first."; return; }
        string nodeId = _minerNodeOption.GetItemText(_minerNodeOption.Selected);
        SetLookupResult("[b]Address Lookup[/b]\n" + _networkRoot.BuildAddressDetailsForNode(nodeId, address));
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
        // OQ-8.2 cosmetic filter: bots are single-address (no ReceiveWallet yet), so their spends
        // produce change back to the same input address. Hide those transactions from the display
        // until simplified seeds + address rotation land for bots. Remove this filter when OQ-8.2
        // is resolved (before referral / rank systems ship).
        List<Transaction> visible = block.Transactions.Where(t => !IsSelfChangeTransaction(t)).ToList();
        decimal blockFees = visible.Where(t => !t.IsCoinbase).Sum(t => t.Fee);
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Transactions: {visible.Count}  |  Fees collected: {blockFees:F8} BTC"));
        foreach (Transaction tx in visible)
        {
            bool isCoinbase = tx.IsCoinbase;
            sb.AppendLine("-");
            sb.AppendLine($"TxId: {tx.TransactionId}{(isCoinbase ? "  [COINBASE]" : "")}");
            if (!isCoinbase)
            {
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Fee: {tx.Fee:F8} BTC"));
                sb.AppendLine($"Inputs ({tx.Inputs.Count}):");
                foreach (TxInput inp in tx.Inputs)
                    sb.AppendLine($"  {inp.Address}");
            }
            IReadOnlyList<TxOutput> outs = ExternalOutputs(tx);
            sb.AppendLine($"Outputs ({outs.Count}):");
            foreach (TxOutput txOut in outs)
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {txOut.Address}  {txOut.Amount:F8} BTC"));
        }
        SetLookupResult(sb.ToString());
    }

    // Sets the left-column lookup result. The trailing blank lines clear the scroll's bottom edge so the last
    // real line (e.g. the last transaction's Recipient) isn't half-clipped — same fix as the right column
    // (see ProjectDesignManual Ch. 29). The label is set on demand here (not on the auto-refresh), so it needs
    // no scroll-position preservation.
    private void SetLookupResult(string text) => _lookupResultLabel.Text = text + "\n\n\n";

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

    // OQ-8.2 cosmetic filter: hides the entire transaction only when every output goes back to an
    // input address (no external recipient at all — a pure self-loop). The complementary per-output
    // filter is ExternalOutputs(), which strips only the change output from transactions that DO have
    // an external recipient. Remove both methods and all callers once bots have simplified seeds +
    // DerivedAddressWallet (before referral / rank systems ship).
    private static bool IsSelfChangeTransaction(Transaction tx)
    {
        if (tx.IsCoinbase || tx.Inputs.Count == 0) return false;
        var inputAddrs = new HashSet<string>(tx.Inputs.Select(i => i.Address));
        return tx.Outputs.All(o => inputAddrs.Contains(o.Address));
    }

    // Returns only the outputs that go to an address NOT in the input set, hiding change-to-self
    // outputs. Coinbase transactions have no inputs so all outputs are returned unchanged.
    private static IReadOnlyList<TxOutput> ExternalOutputs(Transaction tx)
    {
        if (tx.IsCoinbase || tx.Inputs.Count == 0) return tx.Outputs;
        var inputAddrs = new HashSet<string>(tx.Inputs.Select(i => i.Address));
        return tx.Outputs.Where(o => !inputAddrs.Contains(o.Address)).ToList();
    }

    private static string BuildLatestTransactionPreview(Block block)
    {
        List<Transaction> visible = block.Transactions.Where(t => !IsSelfChangeTransaction(t)).ToList();
        if (visible.Count == 0) return "Last block tx details: none";
        var sb = new StringBuilder($"Last block txs ({visible.Count}):\n");
        foreach (Transaction tx in visible)
        {
            bool isCoinbase = tx.IsCoinbase;
            sb.Append("-\n");
            sb.Append($"TxId: {tx.TransactionId}{(isCoinbase ? "  [COINBASE]" : "")}\n");
            if (!isCoinbase)
            {
                sb.Append(string.Create(CultureInfo.InvariantCulture, $"Fee: {tx.Fee:F8} BTC\n"));
                foreach (TxInput inp in tx.Inputs)
                    sb.Append($"From: {inp.Address}\n");
            }
            else
            {
                sb.Append($"From: {BlockchainService.CoinbaseSender}\n");
            }
            IReadOnlyList<TxOutput> outs = ExternalOutputs(tx);
            sb.Append($"Outputs ({outs.Count}):\n");
            foreach (TxOutput txOut in outs)
                sb.Append(string.Create(CultureInfo.InvariantCulture, $"  {txOut.Address}  {txOut.Amount:F8} BTC\n"));
        }
        return sb.ToString().TrimEnd();
    }
}
