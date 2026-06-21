using Godot;
using System;
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

    private OptionButton _minerNodeOption = null!;
    private Label _chainInfoLabel = null!;
    private RichTextLabel _latestBlockLabel = null!;
    private RichTextLabel _networkStatusLabel = null!;
    private RichTextLabel _addressDirectoryLabel = null!;
    private Label _actionFeedbackLabel = null!;

    private LineEdit _txLookupInput = null!;
    private LineEdit _addressLookupInput = null!;
    private LineEdit _blockLookupInput = null!;
    private RichTextLabel _lookupResultLabel = null!;

    // Notepad
    private NotepadPopup _notepadPopup = null!;

    public override void _Ready()
    {
        _networkRoot = GetNode<NetworkRoot>("NetworkRoot");
        _minerNodeOption = GetNode<OptionButton>("%MinerNodeOption");

        _chainInfoLabel = GetNode<Label>("%ChainInfoLabel");
        _latestBlockLabel = GetNode<RichTextLabel>("%LatestBlockLabel");
        _networkStatusLabel = GetNode<RichTextLabel>("%NetworkStatusLabel");
        _addressDirectoryLabel = GetNode<RichTextLabel>("%AddressDirectoryLabel");
        _actionFeedbackLabel = GetNode<Label>("%ActionFeedbackLabel");

        _txLookupInput = GetNode<LineEdit>("%TxLookupInput");
        _addressLookupInput = GetNode<LineEdit>("%AddressLookupInput");
        _blockLookupInput = GetNode<LineEdit>("%BlockLookupInput");
        _lookupResultLabel = GetNode<RichTextLabel>("%LookupResultLabel");

        GetNode<Button>("%MineButton").Pressed      += OnMinePressed;
        GetNode<Button>("%ConsensusButton").Pressed += OnConsensusPressed;
        GetNode<Button>("%RefreshButton").Pressed   += OnRefreshPressed;
        GetNode<Button>("%LookupTxButton").Pressed      += OnLookupTransactionPressed;
        GetNode<Button>("%LookupAddressButton").Pressed += OnLookupAddressPressed;
        GetNode<Button>("%LookupBlockButton").Pressed   += OnLookupBlockPressed;
        GetNode<Button>("%BackToDiceButton").Pressed    += OnBackToDicePressed;
        _sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");

        var mainVBox = GetNode<VBoxContainer>("Margin/MainVBox");
        var statusBar = new StatusBar();
        mainVBox.AddChild(statusBar);
        mainVBox.MoveChild(statusBar, 0);

        _notepadPopup = new NotepadPopup();
        AddChild(_notepadPopup);
        GetNode<Button>("%NotepadBtn").Pressed += _notepadPopup.Open;

        PopulateNodeSelectors();
        PopulateAddressDirectory();
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

    private void PopulateAddressDirectory()
    {
        _addressDirectoryLabel.Text = "[b]Node -> Address[/b]\n" + string.Join("\n", _networkRoot.GetNodeAddressLines());
    }

    private void OnMinePressed()
    {
        string minerNodeId = _minerNodeOption.GetItemText(_minerNodeOption.Selected);
        bool ok = _networkRoot.MineAndBroadcastBlock(minerNodeId);
        _actionFeedbackLabel.Text = ok
            ? $"Block mined by {minerNodeId}. Reward moved to next pending block."
            : "Mining failed: invalid miner node.";
        RefreshUi();
    }

    private void OnConsensusPressed()
    {
        _networkRoot.RunConsensus();
        _actionFeedbackLabel.Text = "Consensus round executed.";
        RefreshUi();
    }

    private void OnRefreshPressed()
    {
        _actionFeedbackLabel.Text = "UI refreshed.";
        RefreshUi();
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

    private void RefreshUi()
    {
        Block last = _networkRoot.GetPlayerLatestBlock();
        _chainInfoLabel.Text = $"Player chain length: {_networkRoot.GetPlayerChainLength()} | Player pending tx: {_networkRoot.GetPlayerPendingTransactionCount()}";

        _latestBlockLabel.Text =
            "[b]Latest Block (player view)[/b]\n" +
            $"Index: {last.Index}\n" +
            $"Time: {FormatBlockTime(last.Timestamp)}\n" +
            $"Nonce: {last.Nonce}\n" +
            $"Hash: {last.Hash}\n" +
            $"PrevHash: {last.PreviousBlockHash}\n" +
            $"MerkleRoot: {last.MerkleRoot}\n" +
            $"Transactions: {last.Transactions.Count}\n" +
            BuildLatestTransactionPreview(last);

        _networkStatusLabel.Text = "[b]Network Status[/b]\n" + string.Join("\n", _networkRoot.GetNodeStatusLines());
    }

    private static string FormatBlockTime(long unixMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

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
