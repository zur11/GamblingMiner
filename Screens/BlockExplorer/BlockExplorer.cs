using Godot;
using System;
using System.Globalization;
using GodotBlockchainPort.Simulation;
using GodotBlockchainPort.Blockchain;

public partial class BlockExplorer : Control
{
    private NetworkRoot _networkRoot = null!;
    private OptionButton _recipientOption = null!;
    private LineEdit _amountInput = null!;
    private Label _chainInfoLabel = null!;
    private RichTextLabel _latestBlockLabel = null!;

    public override void _Ready()
    {
        _networkRoot = GetNode<NetworkRoot>("NetworkRoot");
        _recipientOption = GetNode<OptionButton>("%RecipientOption");
        _amountInput = GetNode<LineEdit>("%AmountInput");
        _chainInfoLabel = GetNode<Label>("%ChainInfoLabel");
        _latestBlockLabel = GetNode<RichTextLabel>("%LatestBlockLabel");

        Button createTxButton = GetNode<Button>("%CreateTxButton");
        Button mineButton = GetNode<Button>("%MineButton");
        Button consensusButton = GetNode<Button>("%ConsensusButton");
        Button refreshButton = GetNode<Button>("%RefreshButton");

        createTxButton.Pressed += OnCreateTransactionPressed;
        mineButton.Pressed += OnMinePressed;
        consensusButton.Pressed += OnConsensusPressed;
        refreshButton.Pressed += RefreshUi;

        PopulateRecipients();
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

    private void OnCreateTransactionPressed()
    {
        if (!decimal.TryParse(_amountInput.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount) || amount <= 0m)
        {
            _chainInfoLabel.Text = "Invalid amount. Use positive number (e.g. 2.5).";
            return;
        }

        if (_recipientOption.ItemCount <= 0)
        {
            _chainInfoLabel.Text = "No recipient nodes found.";
            return;
        }

        string recipientNodeId = _recipientOption.GetItemText(_recipientOption.Selected);
        _networkRoot.PlayerCreateAndBroadcastTransaction(amount, recipientNodeId);
        _chainInfoLabel.Text = $"Transaction queued: {amount:F4} to {recipientNodeId}";
        RefreshUi();
    }

    private void OnMinePressed()
    {
        _networkRoot.PlayerMineAndBroadcastBlock();
        RefreshUi();
    }

    private void OnConsensusPressed()
    {
        _networkRoot.RunConsensus();
        RefreshUi();
    }

    private void RefreshUi()
    {
        Block last = _networkRoot.GetPlayerLatestBlock();
        _chainInfoLabel.Text = $"Chain length: {_networkRoot.GetPlayerChainLength()} | Pending tx: {_networkRoot.GetPlayerPendingTransactionCount()}";
        _latestBlockLabel.Text =
            "[b]Latest block[/b]\n" +
            $"Index: {last.Index}\n" +
            $"Nonce: {last.Nonce}\n" +
            $"Hash: {last.Hash}\n" +
            $"PrevHash: {last.PreviousBlockHash}\n" +
            $"Transactions: {last.Transactions.Count}";
    }
}