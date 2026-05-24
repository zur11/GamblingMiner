using System;
using System.Collections.Generic;

namespace GodotBlockchainPort.Blockchain;

public sealed class Transaction
{
    public decimal Amount { get; set; }
    public string Sender { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string TransactionId { get; set; } = Guid.NewGuid().ToString("N");
    public string SignatureBase64 { get; set; } = string.Empty;
    public string PublicKeyBase64 { get; set; } = string.Empty;
}

public sealed class Block
{
    public int Index { get; set; }
    public long Timestamp { get; set; }
    public List<Transaction> Transactions { get; set; } = new();
    public long Nonce { get; set; }
    public string Hash { get; set; } = string.Empty;
    public string PreviousBlockHash { get; set; } = string.Empty;
    public string MinedByNodeId { get; set; } = string.Empty;
    public string MinedByAddress { get; set; } = string.Empty;
}

public sealed class AddressData
{
    public List<Transaction> AddressTransactions { get; set; } = new();
    public decimal AddressBalance { get; set; }
}
