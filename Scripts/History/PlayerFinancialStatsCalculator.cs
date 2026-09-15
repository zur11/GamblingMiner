using System;
using Scripts.User;
using Scripts.Finance;

namespace Scripts.History
{
	// SF.4B.1 — single source of truth for the player's three-scope betting stats (General / Since last bank
	// deposit / Since last bankroll recharge), used by both FinancialBettingStats (DiceGame) and ScFinances so
	// the two surfaces show byte-identical numbers. Pure computation — no Godot/UI/state.
	//
	// The since-X baselines come from the CLIENT LEDGER snapshots, NOT UserBettingStats.ProfitSinceDeposit
	// (whose baseline is reset on every bankroll recharge, conflating deposit with recharge — see the SF.4B
	// task note). The ledger separates them cleanly: GetLastDeposit (kind initial|deposit) and GetLastAutoRecharge
	// (kind auto_recharge), each carrying the lifetime wagered/profit snapshot taken at that event. This mirrors
	// ClientsBetsHistory's math but keeps the PLAYER sign convention (P/L = +TotalProfit, the player's own gain).
	public readonly struct PlayerFinancialSummary
	{
		public decimal TotalProfit          { get; }
		public decimal TotalWagered         { get; }
		public decimal ProfitSinceDeposit   { get; }
		public decimal WageredSinceDeposit  { get; }
		public decimal ProfitSinceRecharge  { get; }
		public decimal WageredSinceRecharge { get; }
		public DateTime? LastDepositUtc      { get; }
		public DateTime? LastRechargeUtc     { get; }

		public PlayerFinancialSummary(
			decimal totalProfit, decimal totalWagered,
			decimal profitSinceDeposit, decimal wageredSinceDeposit,
			decimal profitSinceRecharge, decimal wageredSinceRecharge,
			DateTime? lastDepositUtc, DateTime? lastRechargeUtc)
		{
			TotalProfit          = totalProfit;
			TotalWagered         = totalWagered;
			ProfitSinceDeposit   = profitSinceDeposit;
			WageredSinceDeposit  = wageredSinceDeposit;
			ProfitSinceRecharge  = profitSinceRecharge;
			WageredSinceRecharge = wageredSinceRecharge;
			LastDepositUtc       = lastDepositUtc;
			LastRechargeUtc      = lastRechargeUtc;
		}
	}

	public static class PlayerFinancialStatsCalculator
	{
		public static PlayerFinancialSummary Compute(UserBettingStats stats, CasinoClientLedgerService ledger, string clientId = "player")
		{
			decimal totalProfit  = stats?.TotalProfit ?? 0m;
			decimal totalWagered = stats?.TotalAmountWagered ?? 0m;

			CasinoClientLedgerService.LedgerEntry lastDeposit  = ledger?.GetLastDeposit(clientId);
			CasinoClientLedgerService.LedgerEntry lastRecharge = ledger?.GetLastAutoRecharge(clientId);

			// Before a real bank deposit, GetLastDeposit returns the "initial" (snapshots 0/0) ⇒ since-deposit ==
			// lifetime. Before any recharge, GetLastAutoRecharge is null ⇒ since-recharge == lifetime.
			//
			// Mini-plan 08 D2 — WAGERED IS NOT CLAMPED ANY MORE. It was `Math.Max(0, …)` under the note "a snapshot
			// can momentarily lead the counter", and no mechanism for that exists: the counter only grows within a
			// session, and a restart restores the ledger and the rollup from the same checkpoint. What the clamp
			// actually absorbed was D2 — a snapshot stored in a different scale from the counter — and it turned a
			// measured 5,381.34 SC of wrong baseline into a plausible 0.00. A negative wagered figure is impossible
			// by construction now; if one ever appears it is a defect, and it must reach the screen to be found.
			decimal profitSinceDeposit = lastDeposit != null
				? Money.Normalize(totalProfit - lastDeposit.NetProfitSnapshot)
				: Money.Normalize(totalProfit);
			decimal wageredSinceDeposit = lastDeposit != null
				? Money.Normalize(totalWagered - lastDeposit.TotalWageredSnapshot)
				: Money.Normalize(totalWagered);

			decimal profitSinceRecharge = lastRecharge != null
				? Money.Normalize(totalProfit - lastRecharge.NetProfitSnapshot)
				: Money.Normalize(totalProfit);
			decimal wageredSinceRecharge = lastRecharge != null
				? Money.Normalize(totalWagered - lastRecharge.TotalWageredSnapshot)
				: Money.Normalize(totalWagered);

			return new PlayerFinancialSummary(
				Money.Normalize(totalProfit),
				Money.Normalize(totalWagered),
				profitSinceDeposit, wageredSinceDeposit,
				profitSinceRecharge, wageredSinceRecharge,
				lastDeposit?.UtcTimestamp, lastRecharge?.UtcTimestamp);
		}
	}
}
