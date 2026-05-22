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

    private OptionButton _recipientOption = null!;
    private LineEdit _amountInput = null!;
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

        _recipientOption = GetNode<OptionButton>("%RecipientOption");
        _amountInput = GetNode<LineEdit>("%AmountInput");
        _chainInfoLabel = GetNode<Label>("%ChainInfoLabel");
        _latestBlockLabel = GetNode<RichTextLabel>("%LatestBlockLabel");
        _networkStatusLabel = GetNode<RichTextLabel>("%NetworkStatusLabel");
        _addressDirectoryLabel = GetNode<RichTextLabel>("%AddressDirectoryLabel");
        _actionFeedbackLabel = GetNode<Label>("%ActionFeedbackLabel");

        _txLookupInput = GetNode<LineEdit>("%TxLookupInput");
        _addressLookupInput = GetNode<LineEdit>("%AddressLookupInput");
        _blockLookupInput = GetNode<LineEdit>("%BlockLookupInput");
        _lookupResultLabel = GetNode<RichTextLabel>("%LookupResultLabel");

        GetNode<Button>("%CreateTxButton").Pressed += OnCreateTransactionPressed;
        GetNode<Button>("%MineButton").Pressed += OnMinePressed;
        GetNode<Button>("%ConsensusButton").Pressed += OnConsensusPressed;
        GetNode<Button>("%RefreshButton").Pressed += OnRefreshPressed;

        GetNode<Button>("%LookupTxButton").Pressed += OnLookupTransactionPressed;
        GetNode<Button>("%LookupAddressButton").Pressed += OnLookupAddressPressed;
        GetNode<Button>("%LookupBlockButton").Pressed += OnLookupBlockPressed;

        PopulateRecipients();
        PopulateAddressDirectory();
        RefreshUi();
    }

    private void PopulateRecipients()
    {
        _recipientOption.Clear();
        foreach (string nodeId in _networkRoot.GetRecipientNodeIds())
        {
            _recipientOption.AddItem(nodeId);
        }
    }

    private void PopulateAddressDirectory()
    {
        _addressDirectoryLabel.Text = "[b]Node -> Address[/b]\n" + string.Join("\n", _networkRoot.GetNodeAddressLines());
    }

    private void OnCreateTransactionPressed()
    {
        if (!decimal.TryParse(_amountInput.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount) || amount <= 0m)
        {
            _actionFeedbackLabel.Text = "Invalid amount. Use positive number (e.g. 2.5).";
            return;
        }

        if (_recipientOption.ItemCount <= 0)
        {
            _actionFeedbackLabel.Text = "No recipient nodes found.";
            return;
        }

        string recipientNodeId = _recipientOption.GetItemText(_recipientOption.Selected);
        Transaction? tx = _networkRoot.PlayerCreateAndBroadcastTransaction(amount, recipientNodeId);
        if (tx is null)
        {
            _actionFeedbackLabel.Text = "Transaction creation failed.";
            return;
        }

        _actionFeedbackLabel.Text = $"Tx created: {tx.TransactionId}";
        _txLookupInput.Text = tx.TransactionId;
        _addressLookupInput.Text = tx.Recipient;
        RefreshUi();
    }

    private void OnMinePressed()
    {
        _networkRoot.PlayerMineAndBroadcastBlock();
        _actionFeedbackLabel.Text = "Block mined and broadcast. Reward added to next pending set.";
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

        _lookupResultLabel.Text = "[b]Transaction Lookup[/b]\n" + _networkRoot.BuildTransactionDetails(txId);
    }

    private void OnLookupAddressPressed()
    {
        string address = _addressLookupInput.Text.Trim();
        if (string.IsNullOrEmpty(address))
        {
            _lookupResultLabel.Text = "Enter an address first.";
            return;
        }

        _lookupResultLabel.Text = "[b]Address Lookup[/b]\n" + _networkRoot.BuildAddressDetails(address);
    }

    private void OnLookupBlockPressed()
    {
        if (!int.TryParse(_blockLookupInput.Text.Trim(), out int blockIndex) || blockIndex <= 0)
        {
            _lookupResultLabel.Text = "Enter a valid positive block number.";
            return;
        }

        Block? block = _networkRoot.GetPlayerBlockByIndex(blockIndex);
        if (block is null)
        {
            _lookupResultLabel.Text = $"Block {blockIndex} not found.";
            return;
        }

        StringBuilder sb = new();
        sb.AppendLine("[b]Block Lookup[/b]");
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

    private void RefreshUi()
    {
        Block last = _networkRoot.GetPlayerLatestBlock();
        _chainInfoLabel.Text = $"Chain length: {_networkRoot.GetPlayerChainLength()} | Pending tx: {_networkRoot.GetPlayerPendingTransactionCount()}";

        _latestBlockLabel.Text =
            "[b]Latest Block[/b]\n" +
            $"Index: {last.Index}\n" +
            $"Nonce: {last.Nonce}\n" +
            $"Hash: {last.Hash}\n" +
            $"PrevHash: {last.PreviousBlockHash}\n" +
            $"Transactions: {last.Transactions.Count}\n" +
            BuildLatestTransactionPreview(last);

        _networkStatusLabel.Text = "[b]Network Status[/b]\n" + string.Join("\n", _networkRoot.GetNodeStatusLines().ToArray());
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
