using Godot;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using GodotBlockchainPort.Simulation;
using GodotBlockchainPort.Blockchain;
#nullable enable

public partial class BlockExplorer : Control
{
    private NetworkRoot _networkRoot = null!;

    private OptionButton _fromNodeOption = null!;
    private OptionButton _toNodeOption = null!;
    private OptionButton _minerNodeOption = null!;
    private LineEdit _amountInput = null!;
    private Button _createTxButton = null!;
    private Label _chainInfoLabel = null!;
    private RichTextLabel _latestBlockLabel = null!;
    private RichTextLabel _networkStatusLabel = null!;
    private RichTextLabel _addressDirectoryLabel = null!;
    private Label _actionFeedbackLabel = null!;

    private LineEdit _txLookupInput = null!;
    private LineEdit _addressLookupInput = null!;
    private LineEdit _blockLookupInput = null!;
    private RichTextLabel _lookupResultLabel = null!;

    public override void _Ready()
    {
        _networkRoot = GetNode<NetworkRoot>("NetworkRoot");

        _fromNodeOption = GetNode<OptionButton>("%FromNodeOption");
        _toNodeOption = GetNode<OptionButton>("%ToNodeOption");
        _minerNodeOption = GetNode<OptionButton>("%MinerNodeOption");
        _amountInput = GetNode<LineEdit>("%AmountInput");
        _createTxButton = GetNode<Button>("%CreateTxButton");

        _chainInfoLabel = GetNode<Label>("%ChainInfoLabel");
        _latestBlockLabel = GetNode<RichTextLabel>("%LatestBlockLabel");
        _networkStatusLabel = GetNode<RichTextLabel>("%NetworkStatusLabel");
        _addressDirectoryLabel = GetNode<RichTextLabel>("%AddressDirectoryLabel");
        _actionFeedbackLabel = GetNode<Label>("%ActionFeedbackLabel");

        _txLookupInput = GetNode<LineEdit>("%TxLookupInput");
        _addressLookupInput = GetNode<LineEdit>("%AddressLookupInput");
        _blockLookupInput = GetNode<LineEdit>("%BlockLookupInput");
        _lookupResultLabel = GetNode<RichTextLabel>("%LookupResultLabel");

        _createTxButton.Pressed += OnCreateTransactionPressed;
        GetNode<Button>("%MineButton").Pressed += OnMinePressed;
        GetNode<Button>("%ConsensusButton").Pressed += OnConsensusPressed;
        GetNode<Button>("%RefreshButton").Pressed += OnRefreshPressed;

        GetNode<Button>("%LookupTxButton").Pressed += OnLookupTransactionPressed;
        GetNode<Button>("%LookupAddressButton").Pressed += OnLookupAddressPressed;
        GetNode<Button>("%LookupBlockButton").Pressed += OnLookupBlockPressed;
        GetNode<Button>("%BackToDiceButton").Pressed += OnBackToDicePressed;

        _fromNodeOption.ItemSelected += _ => RefreshTransferState();
        _toNodeOption.ItemSelected += _ => RefreshTransferState();
        _amountInput.TextChanged += _ => RefreshTransferState();

        PopulateNodeSelectors();
        PopulateAddressDirectory();
        RefreshUi();
    }

    private void PopulateNodeSelectors()
    {
        string[] nodeIds = _networkRoot.GetNodeIds().ToArray();

        _fromNodeOption.Clear();
        _toNodeOption.Clear();
        _minerNodeOption.Clear();

        foreach (string nodeId in nodeIds)
        {
            _fromNodeOption.AddItem(nodeId);
            _toNodeOption.AddItem(nodeId);
            _minerNodeOption.AddItem(nodeId);
        }

        int playerIndex = Array.IndexOf(nodeIds, "player");
        if (playerIndex >= 0)
        {
            _fromNodeOption.Select(playerIndex);
            _minerNodeOption.Select(playerIndex);
        }

        int botIndex = Array.FindIndex(nodeIds, id => id != "player");
        if (botIndex >= 0)
        {
            _toNodeOption.Select(botIndex);
        }
    }

    private void PopulateAddressDirectory()
    {
        _addressDirectoryLabel.Text = "[b]Node -> Address[/b]\n" + string.Join("\n", _networkRoot.GetNodeAddressLines());
    }

    private void OnCreateTransactionPressed()
    {
        if (!TryGetTransferContext(out string fromNodeId, out string toNodeId, out decimal amount, out string error))
        {
            _actionFeedbackLabel.Text = error;
            return;
        }

        Transaction? tx = _networkRoot.CreateAndBroadcastTransaction(fromNodeId, toNodeId, amount);
        if (tx is null)
        {
            _actionFeedbackLabel.Text = "Transaction rejected (invalid route, signature, or insufficient balance).";
            RefreshUi();
            return;
        }

        _actionFeedbackLabel.Text = $"Tx created: {tx.TransactionId}";
        _txLookupInput.Text = tx.TransactionId;
        _addressLookupInput.Text = tx.Recipient;
        RefreshUi();
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
        if (string.IsNullOrEmpty(txId))
        {
            _lookupResultLabel.Text = "Enter a transaction hash first.";
            return;
        }

        string nodeId = _fromNodeOption.GetItemText(_fromNodeOption.Selected);
        _lookupResultLabel.Text = "[b]Transaction Lookup[/b]\n" + _networkRoot.BuildTransactionDetails(nodeId, txId);
    }

    private void OnLookupAddressPressed()
    {
        string address = _addressLookupInput.Text.Trim();
        if (string.IsNullOrEmpty(address))
        {
            _lookupResultLabel.Text = "Enter an address first.";
            return;
        }

        string nodeId = _fromNodeOption.GetItemText(_fromNodeOption.Selected);
        _lookupResultLabel.Text = "[b]Address Lookup[/b]\n" + _networkRoot.BuildAddressDetailsForNode(nodeId, address);
    }

    private void OnLookupBlockPressed()
    {
        if (!int.TryParse(_blockLookupInput.Text.Trim(), out int blockIndex) || blockIndex <= 0)
        {
            _lookupResultLabel.Text = "Enter a valid positive block number.";
            return;
        }

        string nodeId = _fromNodeOption.GetItemText(_fromNodeOption.Selected);
        Block? block = _networkRoot.GetBlockByIndexForNode(nodeId, blockIndex);
        if (block is null)
        {
            _lookupResultLabel.Text = $"Block {blockIndex} not found for node {nodeId}.";
            return;
        }

        StringBuilder sb = new();
        sb.AppendLine("[b]Block Lookup[/b]");
        sb.AppendLine($"Node: {nodeId}");
        sb.AppendLine($"Index: {block.Index}");
        sb.AppendLine($"Hash: {block.Hash}");
        sb.AppendLine($"PrevHash: {block.PreviousBlockHash}");
        sb.AppendLine($"Nonce: {block.Nonce}");
        sb.AppendLine($"Transactions: {block.Transactions.Count}");

        foreach (Transaction tx in block.Transactions)
        {
            sb.AppendLine("-");
            sb.AppendLine($"TxId: {tx.TransactionId}");
            sb.AppendLine($"Amount: {tx.Amount:F8}");
            sb.AppendLine($"Sender: {tx.Sender}");
            sb.AppendLine($"Recipient: {tx.Recipient}");
        }

        _lookupResultLabel.Text = sb.ToString();
    }

    private void OnBackToDicePressed()
    {
        CalendarTimeService? calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
        calendar?.PersistCurrentTime();
        GetTree().ChangeSceneToFile("res://Screens/DiceGame/DiceGame.tscn");
    }

    private void RefreshUi()
    {
        Block last = _networkRoot.GetPlayerLatestBlock();
        _chainInfoLabel.Text = $"Player chain length: {_networkRoot.GetPlayerChainLength()} | Player pending tx: {_networkRoot.GetPlayerPendingTransactionCount()}";

        _latestBlockLabel.Text =
            "[b]Latest Block (player view)[/b]\n" +
            $"Index: {last.Index}\n" +
            $"Nonce: {last.Nonce}\n" +
            $"Hash: {last.Hash}\n" +
            $"PrevHash: {last.PreviousBlockHash}\n" +
            $"Transactions: {last.Transactions.Count}\n" +
            BuildLatestTransactionPreview(last);

        _networkStatusLabel.Text = "[b]Network Status[/b]\n" + string.Join("\n", _networkRoot.GetNodeStatusLines());
        RefreshTransferState();
    }

    private void RefreshTransferState()
    {
        string fromNodeId = _fromNodeOption.ItemCount > 0 ? _fromNodeOption.GetItemText(_fromNodeOption.Selected) : string.Empty;
        string toNodeId = _toNodeOption.ItemCount > 0 ? _toNodeOption.GetItemText(_toNodeOption.Selected) : string.Empty;

        decimal senderBalance = string.IsNullOrEmpty(fromNodeId) ? 0m : _networkRoot.GetNodeSpendableBalance(fromNodeId);
        bool validAmount = decimal.TryParse(_amountInput.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount) && amount > 0m;

        bool canTransfer = !string.IsNullOrEmpty(fromNodeId)
            && !string.IsNullOrEmpty(toNodeId)
            && fromNodeId != toNodeId
            && validAmount
            && senderBalance > 0m
            && senderBalance >= amount;

        _createTxButton.Disabled = !canTransfer;

        string reason;
        if (string.IsNullOrEmpty(fromNodeId) || string.IsNullOrEmpty(toNodeId)) reason = "Select origin and destination nodes.";
        else if (fromNodeId == toNodeId) reason = "Origin and destination must be different.";
        else if (!validAmount) reason = "Enter a valid positive amount.";
        else if (senderBalance <= 0m) reason = "Origin node has 0 balance. Mine first or receive transfer.";
        else if (senderBalance < amount) reason = $"Insufficient balance. Available: {senderBalance:F8}";
        else reason = $"Transfer enabled. Available: {senderBalance:F8}";

        _actionFeedbackLabel.Text = reason;
    }

    private bool TryGetTransferContext(out string fromNodeId, out string toNodeId, out decimal amount, out string error)
    {
        fromNodeId = _fromNodeOption.GetItemText(_fromNodeOption.Selected);
        toNodeId = _toNodeOption.GetItemText(_toNodeOption.Selected);
        amount = 0m;

        if (fromNodeId == toNodeId)
        {
            error = "Origin and destination must be different.";
            return false;
        }

        if (!decimal.TryParse(_amountInput.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) || amount <= 0m)
        {
            error = "Invalid amount. Use positive number (e.g. 2.5).";
            return false;
        }

        decimal balance = _networkRoot.GetNodeSpendableBalance(fromNodeId);
        if (balance < amount)
        {
            error = $"Insufficient balance for {fromNodeId}. Available: {balance:F8}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string BuildLatestTransactionPreview(Block block)
    {
        if (block.Transactions.Count == 0)
        {
            return "Last block tx details: none";
        }

        Transaction tx = block.Transactions[0];
        return
            "Last block first tx:\n" +
            $"TxId: {tx.TransactionId}\n" +
            $"Sender: {tx.Sender}\n" +
            $"Recipient: {tx.Recipient}";
    }
}
