# Implementation Roadmap — Unified Order of Work

**Purpose**: single source of truth for *what to build next and why*, across all the plan files in `AIHelperFiles/`. The individual plans hold the detail; this file holds the **order and the dependencies** so overlapping themes (block-candidate model, gradual network growth, UTXO realism) don't collide.

**Last updated**: 2026-06-19.

---

## 1. Plan inventory & current status

| Plan file | Scope | Status |
|---|---|---|
| `btc-wallet-system-plan.md` | Addresses, seeds, passphrase wallets, dev scenes, notepad | ✅ Done (Phases 0–9) |
| `100x-time-scale-migration-plan.md` | 100X time scale, 210k supply, 2,100-block halving | ✅ Done (Phase 5 validation is a checklist, not code) |
| `scheduled-bot-transactions-plan.md` | Miner-bot → holder-bot BTC recirculation | ⏸ Core done (Phase 1); rest deferred / needs re-alignment |
| `historical-founders-and-bootstrap-plan.md` | Satoshi/Hal/Hearn nodes, genesis fix, bootstrap to 21 Mar, Satoshi 11k target | 🆕 Designed, not coded |
| `historical-blockchain-events-research.md` | Character/event data + UTXO-realism direction | 🔬 Questionnaire (Q-X1–X4 resolved; address research open) |
| `btc-pools-hardware-plan.md` | Hardware credits, casino community pool, fees | ◻ Not started |

---

## 2. The two cross-cutting themes that were colliding

1. **Block Candidate + Hashrate model** — appears in founders (weighted lottery), hardware (per-credit nonce routing), and fees (template builder). **It is the keystone.** Resolved split (founders OQ-1): minimal `HashrateWeight` + weighted lottery **now**; full per-node candidate *template* (tx selection, Merkle, fees) deferred into the **block-template-builder**.
2. **Network initialization model** — older plans assume *all participants exist at block 1*. The historical migration replaces this with a **network that grows over time** (Satoshi → Hal → player @ 21 Mar → bots gradually). This is the new ground truth, so it should land **before** we expand recirculation / hardware / fees on the old assumption.

---

## 3. Recommended order

> Rule of thumb: do the things that **redefine the foundation** before the things that **build on it**. Identity + init model first; economy systems after.

### Step 1 — Founders identity foundation  *(founders P1–P3)*
- Satoshi & Hal as nodes with seed phrases; `FoundersWallets` dev scene; genesis & early coinbase recipients → derived `gm1q…`.
- **No dependencies.** Fixes the base58/`gm1q…` inconsistency that every other system references. Low risk, high foundation value. Founders exist as registered nodes but don't mine yet (mining arrives in Step 2/3); game still starts at genesis until Step 3.
- *(The 12 Jan 10 BTC Satoshi→Hal tx is NOT here — it needs a Jan-12 block and spendable Satoshi coins, so it lands in Step 3 with the bootstrap.)*

### Step 2 — Block Candidate + Hashrate model (minimal)  *(founders P0)*
- `HashrateWeight` per node + `RunWeightedBlockLottery`. Refactor of existing single-nonce mining; player bet-driven path unchanged.
- **The keystone.** Unblocks Steps 3, 4 (hardware), and 6 (template builder).

### Step 3 — Historical bootstrap + Satoshi targeting  *(founders P4–P6)*
- First-launch pre-mine genesis→21 Mar; Satoshi 11,000-BTC dynamic ramp (retire ≥ 2011-04-26); Hal's 3 blocks; the **12 Jan 10 BTC Satoshi→Hal tx** (founders Phase 6, inserted at the Jan-12 block).
- Use **single-address Satoshi** as the testing shortcut (Patoshi multi-address is Step 5).
- **Depends on Steps 1–2.** Establishes the gradual-growth init model as ground truth.

### Step 4 — Re-align recirculation + hardware bootstrap to gradual growth
- Revisit `scheduled-bot-transactions` triggers (circulation keyed to *bot introduction*, not block ≥ 5; scheduler no-ops with no miner bots).
- Define *when/how miner bots are introduced* after player start, then build **hardware pools** (`btc-pools-hardware-plan.md`) on the corrected init.
- **Depends on Step 3** (so bots/hardware are built once, correctly).

### Step 5 — UTXO realism / Patoshi per-receive addresses
- Fresh derived address per coinbase/deposit; real change outputs; surfaced via passphrase wallets. Founders first, then player wallet.
- **Depends on Step 1** (founders exist). Pending §6 address-reuse research in the research doc. Enhancement, not a blocker for Steps 3–4.

### Step 6 — Block template builder + fees  *(P4 roadmap; scheduled-bot-tx Phase 4 reactivates here)*
- Full per-node candidate template: ancestor-feerate tx selection, Merkle root, coinbase fee collection, `Transaction.Fee`.
- **Depends on Step 2** (extends the minimal candidate into a full template).

### Step 7 — Economy & meta  *(PRIVATE_ROADMAP P6–P8)*
- P6 casino finances → P7 BTC/SC trading → P8 achievements.

---

## 4. Dependency graph (compact)

```
Step1 (founders identity) ─┬─> Step3 (bootstrap) ─> Step4 (re-align + hardware) ─> Step6 (template+fees) ─> Step7
Step2 (candidate+hashrate)─┘                                   ▲                         ▲
                            └──────────────────────────────────┘                         │
Step1 ─> Step5 (UTXO/Patoshi) ──────────────────────────────────────────────────────────┘ (informs)
```

---

## 5. Mapping to `Documentation/PRIVATE_ROADMAP.md` (P0–P8)

| Roadmap step here | PRIVATE_ROADMAP priority |
|---|---|
| Step 1–3 (historical foundation) | New — predates/extends P3 (bots, transactions, mempool) |
| Step 4 (recirculation + hardware) | P3 + P5 (hardware progression) |
| Step 5 (UTXO/Patoshi) | New — refines P3 address model |
| Step 6 (template + fees) | P4 (block template builder) |
| Step 7 | P6 / P7 / P8 |

> Action item: fold this ordering into `PRIVATE_ROADMAP.md` once Step 1 starts, so the canonical roadmap reflects the historical foundation as a first-class priority.

---

## 6. What NOT to do next
- ❌ Don't expand `scheduled-bot-transactions` (functionally complete; its assumptions change in Step 4).
- ❌ Don't start `btc-pools-hardware` before Step 3 (its bootstrap assumes all-participants-at-block-1).
- ❌ Don't build the full block-template/fees before the minimal candidate model (Step 2).
