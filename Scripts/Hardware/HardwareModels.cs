using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Scripts.Hardware;

#nullable enable

// Pure C# data models for the hardware credit / casino pool system.
// No Godot dependencies — persistence lives in the repository classes.

public record NodeHardwareState
{
	public string NodeId { get; init; } = string.Empty;
	public int IndividualPoolCredits { get; init; } = 0;
	public int CasinoPoolCredits { get; init; } = 0;

	[JsonIgnore]
	public int TotalCredits => IndividualPoolCredits + CasinoPoolCredits;
}

public record HardwareAllocationSnapshot
{
	public List<NodeHardwareState> Nodes { get; init; } = new();
}

public record CasinoPoolPendingPayout
{
	public string RecipientNodeId { get; init; } = string.Empty;
	public string RecipientAddress { get; init; } = string.Empty;
	public decimal GrossAmount { get; init; }     // before tx fee
	public decimal NetAmount { get; init; }        // after 0.1 BTC tx fee
	public int FromBlockIndex { get; init; }
}

public record CasinoPoolRewardEvent
{
	public int BlockIndex { get; init; }
	public decimal TotalReward { get; init; }
	public decimal CasinoFeePercent { get; init; }
	public decimal CasinoFeeAmount { get; init; }
	public List<CasinoPoolPendingPayout> Payouts { get; init; } = new();
	public bool Distributed { get; init; } = false;
}

public record CasinoPoolState
{
	public List<CasinoPoolRewardEvent> RewardHistory { get; init; } = new();
}
