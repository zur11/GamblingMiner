using System;

namespace GodotBlockchainPort.Blockchain;

// P10 — whole-network fee-free before 2009-04-26; all participants pay after.
// Chosen strictly after the Hearn round-trip (2009-04-18) so scripted historical
// events remain fee-free as historically accurate.
// See AIHelperFiles/step10-network-fee-activation-plan.md §2.
public static class NetworkFeePolicy
{
    // D-13.9 (step13 plan §3.6): routed through TimelineConfig's special case — canon keeps exactly
    // 2009-04-26; the alt timeline (branch-only) activates fees on the landing/market-open day itself.
    public static readonly DateTime ActivationDateLocal = TimelineConfig.FeeActivationLocal;

    // Basic Mode v1 fee limits (player-facing and bot-automated)
    public const decimal DefaultFee = 0.1m;
    public const decimal MinFee     = 0.1m;
    public const decimal MaxFee     = 1.0m;

    // UI layer: compare against the game clock (CalendarTimeService.CurrentLocalDateTime).
    public static bool IsActive(DateTime gameLocalDateTime)
        => gameLocalDateTime.Date >= ActivationDateLocal;

    // Backend layer: compare against a block's Unix-ms timestamp.
    // ActivationDateLocal is interpreted as midnight UTC for the gate. Kind is stripped to Unspecified
    // first — DateTimeOffset(DateTime, TimeSpan) throws if a Local-kind value's offset doesn't match the
    // machine's actual local UTC offset, and ActivationDateLocal now carries Local kind via TimelineConfig.
    public static readonly long ActivationDateMs =
        new DateTimeOffset(DateTime.SpecifyKind(ActivationDateLocal, DateTimeKind.Unspecified), TimeSpan.Zero).ToUnixTimeMilliseconds();

    public static bool IsActiveByTimestamp(long blockTimestampMs)
        => blockTimestampMs >= ActivationDateMs;

    // Any value outside [MinFee, MaxFee] → DefaultFee. Never throws.
    public static decimal ClampOrDefault(decimal fee)
        => (fee >= MinFee && fee <= MaxFee) ? fee : DefaultFee;
}
