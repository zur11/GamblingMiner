using System;

namespace GodotBlockchainPort.Blockchain;

// P10 — whole-network fee-free before 2009-04-26; all participants pay after.
// Chosen strictly after the Hearn round-trip (2009-04-18) so scripted historical
// events remain fee-free as historically accurate.
// See AIHelperFiles/step10-network-fee-activation-plan.md §2.
public static class NetworkFeePolicy
{
    public static readonly DateTime ActivationDateLocal = new DateTime(2009, 4, 26);

    // Basic Mode v1 fee limits (player-facing and bot-automated)
    public const decimal DefaultFee = 0.1m;
    public const decimal MinFee     = 0.1m;
    public const decimal MaxFee     = 1.0m;

    // UI layer: compare against the game clock (CalendarTimeService.CurrentLocalDateTime).
    public static bool IsActive(DateTime gameLocalDateTime)
        => gameLocalDateTime.Date >= ActivationDateLocal;

    // Backend layer: compare against a block's Unix-ms timestamp.
    // ActivationDateLocal is interpreted as midnight UTC for the gate.
    public static readonly long ActivationDateMs =
        new DateTimeOffset(ActivationDateLocal, TimeSpan.Zero).ToUnixTimeMilliseconds();

    public static bool IsActiveByTimestamp(long blockTimestampMs)
        => blockTimestampMs >= ActivationDateMs;

    // Any value outside [MinFee, MaxFee] → DefaultFee. Never throws.
    public static decimal ClampOrDefault(decimal fee)
        => (fee >= MinFee && fee <= MaxFee) ? fee : DefaultFee;
}
