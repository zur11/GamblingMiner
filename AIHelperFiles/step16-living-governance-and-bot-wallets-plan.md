# Step 16 — Living Governance & Full UTXO Participation

> **Status: DESIGN DRAFT — Round 1 written 2026-07-30, awaiting the developer's Round-2 picks (§6).**
> Scope fixed by the developer: **D + E + A + B** from the step15 §10 handoff — the dividend-traffic fix,
> OQ-8.2 (bot seed phrases / full UTXO integration, promoted in `PRIVATE_ROADMAP.md` §5 to "right after
> Step 15"), living probabilistic governance, and the end of the pause tax. **Company evolution levels and
> historical company fates (blocks C and F of the audit's idea set) are deliberately NOT in this step** —
> they are the larger design and get their own cycle once this one lands.
>
> Branch (suggested): `living-governance-and-bot-wallets` off `main`.
>
> **World treatment: bump-and-wipe, authorized.** The developer confirmed the world is already reset and
> may be wiped freely, so this plan takes the §39.16 rule 4 default — `WorldFormatVersion 4 → 5` — rather
> than contorting P16.2 to preserve on-chain bot addresses (D-16.4).

---

## 1. Why this step exists

Step 15 built a complete banking and credit layer and then measured it running for 4½ in-game years
(`step15-bank-companies-sc-provisioning-plan.md` §10). The machinery was correct and almost nothing pushed
on it. Four of that audit's six findings are actionable now, and they are what this step is:

| Audit finding | This step's answer |
|---|---|
| **F1** — governance is a constant function; 517 votes → ~2 outcome changes; the player is the only source of variance | **A** — ballots become draws shaped by persona × situation (§4.3) |
| **F2** — the pause tax: 93 full-simulation freezes for those ~2 changes | **B** — per-company pause toggle, default OFF, plus a standing policy that auto-casts (§4.4) |
| **F3** — bot dividend claims run at 8.66 tx/block against a ~5 tx historical budget and 23 usable slots | **D** — one multi-output settlement per company per quarter (§4.1) |
| **F5** — chronic frame saturation, per-block spikes | partially: **D** removes ~8.5 transaction constructions per block. The rest is `PRIVATE_ROADMAP.md` §8 **T4**, starting at T4.6, and is **not** in this step |

Plus the one carried debt: **E**, OQ-8.2, the last single-address participants.

**The through-line.** F1, F2 and F3 look like three problems and are one: *the company layer generates a
great deal of activity that neither the player nor the world can feel.* Votes that never change anything,
freezes that decide nothing, and thousands of transactions that only crowd the mempool. This step's job is
to convert that motion into signal — and, with E, to finish making every participant a real UTXO citizen so
the two Block Explorer cosmetics that have been hiding the gap since Step 8 can finally be deleted.

---

## 2. What already exists (the substrate)

Read before building. Every block below is mostly *wiring what is already there*, which is why the step is
small despite covering four areas.

**For D (dividends):**
- `NetworkRoot.TryAutoClaimBotDividends` — today: per holder, per block, on-chain, gated at 10× the median
  fee (ND.10e's batching, which bounded the *fee waste* and never the *count*).
- `CompanyGovernanceState.ClaimableByHolder` — **accrual is already separate from payment.** The PST daily
  drip and the NST quarter-end lump both accrue here; nothing about the accrual model has to change.
- **`NetworkRoot.DistributePoolEventAsSingleTx`** — the casino pool's *one multi-output transaction per
  event* path, built to fix exactly this class of bug (sequential single sends depleting the only available
  UTXO before change confirmed). **This is the shape D adopts**; it is already audited and in production.

**For E (bot wallets):**
- `DerivedAddressWallet` — HD-lite, pure C#, **no persisted state** (re-derived from the chain by `Rescan`,
  D3). Already used by player, casino, Satoshi, Hal, Hearn.
- `NodeAgent.ReceiveWallet` + `RotateCoinbaseAddress` — `false` everywhere except Satoshi (canonical:
  **coinbase address non-reuse is Satoshi-only**; everyone else rotates on **change only**).
- `NetworkRoot.TryResolveInputKeys` — resolves the base address and derived addresses through **independent
  branches**, so a node whose base is not seed-derived still signs correctly. (This is what the CLAUDE.md
  OQ-8.2 note flagged as "likely no format bump needed" — but see D-16.4, we are not taking that path.)
- **`NetworkRoot.CreateAndRegisterNode(nodeId, savedState)` is the single funnel** for `bot_1..4`, all 40
  non-miners/companies and every cast miner (NetworkRoot.cs ~L1007–1029). One edit there covers all three
  tiers.
- `RescanFounderReceiveWallets` — the launch-time frontier rebuild; needs widening, not rewriting.
- `BotWalletRegistry` — three lists (`MinerBots` / `NonMinerBots` / `CastMiners`), `BotWalletRecord` stores
  keys but **no seed words**. `bot_wallet_registry.json` is an **identity file, deliberately exempt** from
  the world-reset delete list (Ch. 35 §35.1).

**For A/B (governance):**
- `BuildBotBallot` (NetworkRoot.cs ~L3682) — the pure function to be replaced.
- `BotGovernancePreference` — `CurrencyBandPreference` / `MarketCategoryPreference` / `GreedPreference`,
  drawn per world as shuffled permutations by `EnsureBotGovernancePreferences`; `BackfillGreedPreferences`
  is the established shape for adding an axis to an existing record.
- `ProjectStanceIntoBand` (P15.9, D-15.24) — **stays exactly as is.** A's drift terms move the *stance*;
  the projection into the company's legal band remains the last step before the ballot. Every new term must
  pass through it, or the P15.9 tripwire will (correctly) fire.
- `ComputeReserveVoteOutcome` (P15.9f) — the shared resolver/preview. B's "what your standing policy will
  cast" preview must go through it too (§39.16 rule 6).
- `CompanyVote.AwaitingPlayerVote` / `NetworkRoot.IsAwaitingPlayerVote` — the pause hook B gates.

---

## 3. Decisions log — Round 1 (`D-16.x`)

- **D-16.1 (P16.1):** **dividends settle as ONE multi-output transaction per company per quarter**,
  reusing `DistributePoolEventAsSingleTx`'s shape — not per holder, not per block. Accrual is untouched
  (the PST daily drip stays a daily bookkeeping entry); only *payment* is batched. Expected traffic:
  ~30 companies × 1 tx per quarter ≈ **0.2 tx/block against today's 8.66**, and one network fee split
  across the holders instead of one per holder. **The player's manual claim is unchanged** — it is
  player-initiated, rare, and it is the one claim that should stay immediate.
- **D-16.2 (P16.1):** **the fee comes out of the settlement, split pro-rata across the outputs**, exactly
  as `TryClaimPlayerCompanyDividends` already deducts it from the claim. A holder whose share cannot cover
  its slice of the fee is **left accruing** rather than paid a negative amount — and, per §39.16 rule 6,
  `HasPlayerClaimableDividends`'s "payable, not non-zero" test is the model for how that is displayed.
- **D-16.3 (P16.2):** **a bot's base address is DERIVED FROM ITS SEED** (`DeriveAddress(0)`), making
  `bot_1..4`, the cast miners and the 40 companies structurally identical to the player/casino/founders.
  The alternative — keep each bot's existing generated address as a non-derived base — is *supported* by
  `TryResolveInputKeys` but is a combination **nothing in the project has ever run**, and it would leave
  `DerivedAddressWallet.BaseAddress` disagreeing with `NodeAgent.WalletAddress` for 74 nodes. Taking the
  free wipe buys structural uniformity; refusing it buys an untested special case forever.
- **D-16.4 (P16.2):** **`WorldFormatVersion` 4 → 5, clean wipe** (§39.16 rule 4). D-16.3 changes every
  bot/company/cast on-chain address, so the existing chain would reference addresses whose keys no longer
  exist. Authorized by the developer. `bot_wallet_registry.json` — an identity file, reset-exempt — **must
  be deleted by hand or regenerated** for the new seeds to take; **P16.2 adds it to the version-gated
  regeneration path** so this cannot be forgotten (a stale registry would silently keep the old
  seedless records and the whole phase would appear not to work).
- **D-16.5 (P16.2):** **`RotateCoinbaseAddress` stays `false` for every migrated node.** Coinbase address
  non-reuse remains **Satoshi-only** (the canonical Step 8 rule); cast miners mine to their base address
  and rotate on **change only**, like the player. This is a canon preservation, not an oversight.
- **D-16.6 (P16.2):** **ghosts are excluded, permanently** (D-14.11) — their keys die with the process by
  design. Verify (P16.2f) that a ghost never *spends*: if it only ever receives a coinbase it can produce
  no change output, so ghosts do **not** block the removal of the two Block Explorer cosmetics
  (`IsSelfChangeTransaction` / `ExternalOutputs`). **If that check fails, the cosmetics stay and the
  finding is recorded** — they may only be deleted when the last *spending* single-address participant is
  gone.
- **D-16.7 (P16.2):** **the wallet scenes split three ways** — `BotsBtcWallets` keeps **only** the four
  casino-miner bots, the 40 companies move to a new **`CompaniesWallets`**, and the cast miners get a new
  **`CastMinerWallets`**. Today one scene mixes two populations that share nothing but a registry file.
  All three follow Ch. 29 (bounded scroll + **fixed footer outside it**) and use `DescribeNodeForDev`'s
  two-tier naming (DEV form, `Mt. Gox (non_miner_7)`, since these are diagnostic screens read beside the
  traces — ND.10g).
- **D-16.8 (P16.3):** **a ballot is a draw, not a value.** `BuildBotBallot` becomes
  `stance + Σ drift → ProjectStanceIntoBand → + jitter`, where the drift terms read only facts the bot
  could plausibly observe (§4.3). **`ProjectStanceIntoBand` is not bypassed** — every term moves the
  *stance*, the projection stays the last legality step, and the P15.9 tripwire remains the guard.
- **D-16.9 (P16.3):** **the jitter is deterministically seeded** on `(companyId, quarterIndex, botNodeId)`.
  Ballots are cast when a vote OPENS and are persisted (P15.9f's finding), so a restart before close must
  reproduce the same ballot; a live `Random` would make an open vote's ballots change under the player.
- **D-16.10 (P16.3):** **bots may abstain.** `ComputeReserveVoteOutcome` already resolves over ballots
  *actually cast*, so abstention needs no resolver work — and it is what makes **the player's relative
  weight vary quarter to quarter**, which is the cheapest source of "this vote matters" in the whole
  design. An abstaining holder is shown as *not voted* in the open-vote list P15.9f added.
- **D-16.11 (P16.4):** **the pause is opt-in per company, default OFF.** A toggle on `CompanyDetails`
  (`Pause the game for this company's votes`). Default OFF means a new holding never freezes the
  simulation — the failure mode F2 measured — and the player opts in for the companies they actually steer.
- **D-16.12 (P16.4):** **with the pause off, a standing policy auto-casts, and it defaults to the status
  quo** (the company's currently-applied reserve % and payout rate). A default that changes nothing is the
  only safe default for an automation the player did not configure; the preview of what it will cast goes
  through `ComputeReserveVoteOutcome`, never a second implementation (§39.16 rule 6).
- **D-16.13 (P16.4):** **an auto-cast ballot is recorded as the player's, marked `auto`** — the
  `CasinoClientLedgerService.Method` precedent (manual/auto as a field, never a separate kind). The vote
  history must never imply the player deliberated a ballot their policy cast.

---

## 4. The four blocks

### 4.1 — P16.1: dividend settlement (D)

**Today.** `TryAutoClaimBotDividends` walks every founded company × every bot holder, every block, and
sends when the accrued claim clears 10× the median fee. Measured: **8.66 transactions per block**, against
an ND.4a historical budget of ~5 and 23 usable block slots. Consequence chain: `pendingTxs` 26–28 ⇒
`owed = max(0, target − pending)` is structurally 0 ⇒ the cast sell-flow that *funds these very companies*
stops ⇒ and every other participant's transactions (bids, swaps, player sends) queue behind them.

**After.** One settlement per company at its quarter close:

1. `TickCompanyGovernance` reaches the quarter-end for company *C*.
2. Collect every holder with a payable BTC claimable (D-16.2's *payable*, not non-zero).
3. Build **one transaction** from *C*'s treasury with one output per holder, via the
   `DistributePoolEventAsSingleTx` path — atomic, single coin selection, single change output.
4. Zero those claimables; append **one** `dividend_settlement` trace row carrying the holder count and the
   total, replacing up to N `bot_claim` rows.
5. SC dividends are **not on-chain** — they stay instant credits to `NodeFinancialState.PrincipalBalance` /
   the player's Main Balance, exactly as now. Only the BTC leg is batched.

**What must not regress:** the ND.10e telemetry fixes (`bot_claim_failed` on a silent broadcast failure,
and the SC leg being visible) — both must survive into the batched path, or this trades one blind spot for
another. **Watch the block cap:** a company with many holders still fits one transaction, but a settlement
tx with 10 outputs is one tx *for the cap* — that is the whole point.

### 4.2 — P16.2: full UTXO participation (E, OQ-8.2)

**The change is four lines and a wipe**, plus the scene work:

1. `BotWalletRecord` gains `SeedWords` (the existing `wordlist_256.json` generator the player/casino/
   founders already use). `BotWalletRegistry.CreateRegistry` and `AddCastMiner` generate it; the base
   address becomes `DeriveAddress(0)` from that seed (D-16.3).
2. `CreateAndRegisterNode` — when the record carries a seed:
   `node.ReceiveWallet = new DerivedAddressWallet(seed); node.RotateCoinbaseAddress = false;`
   That one edit covers `bot_1..4`, all 40 companies **and** every cast miner, because they all come
   through this funnel. `RegisterCastMinerNode` (the mid-session spawn path) needs the same two lines.
3. `RescanFounderReceiveWallets` widens to every registered node holding a `ReceiveWallet` — rename to
   `RescanDerivedReceiveWallets`. Its `appearsOnChain` set is already built in a single pass, so the cost
   is derivation, not chain scans.
4. A record **without** a seed keeps working exactly as today (single-address). That is §39.16 rule 5's
   sentinel default arriving for free, and it means a developer who forgets to delete the registry gets
   *old behavior*, not a crash — which is precisely why P16.2 also **version-gates the registry
   regeneration** (D-16.4) so the silent-old-behavior path cannot be hit by accident.

**Then the payoff:** delete `BlockExplorer.IsSelfChangeTransaction` and `ExternalOutputs` (§29.9) — the two
cosmetics that have hidden bots' change-to-self outputs since Step 8 — **after** the D-16.6 ghost check.

**Scenes (D-16.7).** `BotsBtcWallets` → casino-miner bots only; new `CompaniesWallets` (40 companies, the
natural place to also show each one's treasury/ScReserve/collateral at a glance); new `CastMinerWallets`.
Ch. 29 first, then build — the failure modes are documented and recurrent.

### 4.3 — P16.3: living ballots (A)

```
stance   = pref.BandStance                       (today's CurrencyBandPreference, unchanged)
drift    = Σ  w_i · signal_i                     (§4.3 table, scaled by the bot's Conviction)
ballot   = ProjectStanceIntoBand(stance + drift, company.CurrencyBand) + jitter
```

| Signal | Read from | Direction |
|---|---|---|
| Price momentum | `BtcMarketDataService`, ~90-day return | bull → less SC; drawdown → flight to SC |
| Own dividend experience | its claim history vs holding size | starved → payout up (scaled by `GreedPreference`) |
| Company health | `PendingShortfallSc` / `UnrecoverableShortfallSc` | shortfall → conservative |
| **FBI heat** | `CompanyGovernanceState.InvestigationScore` | a live file → **vote the SC pile down** |
| Peer anchoring | the previous vote's *result* | produces slow trends instead of white noise |

New persona axes, drawn exactly like the existing three (shuffled permutation per world, via
`EnsureBotGovernancePreferences` + a `Backfill…` twin): **`Conviction`** (reaction strength; low = stubborn),
**`RiskAversion`** (weights FBI/shortfall/darkness), **`Horizon`** (dividends now vs reserves later).

**The FBI term is the one to fight for**: it closes a loop between three shipped systems — investigation
meter → board vote → SC holdings — and makes "keep your companies lean" a strategy the bots visibly share
with the player. It is also the first thing that will make P15.8-G's stress states *approachable by
playing*, since a company whose board reacts to heat is a company whose board can also react too late.

**Acceptance is measurable, and must be** (D-15.34): after a run, `vote_close` reserve% for a given company
must show **variation across quarters**, and the four bots' ballots at one vote must not be identical.
Assert it in DEBUG the way `AssertEscalationSlopesAreOrdered` asserts the slope ordering.

### 4.4 — P16.4: the end of the pause tax (B)

- **Toggle** on `CompanyDetails`: *Pause the game for this company's votes* — **default OFF** (D-16.11).
- **Standing policy** per company: reserve % + payout %, defaulting to the currently-applied values
  (D-16.12), with a live "if the vote closed now" preview through `ComputeReserveVoteOutcome`.
- On `OpenCompanyVote`, for a player NST holding: pause **only** if that company's toggle is ON; otherwise
  cast the standing policy immediately, marked `auto` (D-16.13), and let the vote resolve normally.
- Shortfall votes: **same rule, no exception.** The developer's call is a single toggle per company; a
  hidden "except shortfall" clause would make the toggle lie. The standing policy's shortfall split
  defaults to the 50/50 the phase already uses.

---

## 5. Phase map

| Phase | Content | Depends on |
|---|---|---|
| **P16.1** | Dividend settlement batching (D) — a–c: the multi-output settlement, the telemetry parity, the trace | — |
| **P16.2** | Bot seeds + `ReceiveWallet` (E) — a–f: registry seeds, node wiring, rescan widening, format bump, cosmetics removal, ghost check | — |
| **P16.3** | Wallet scene split (E) — a–c: `BotsBtcWallets` trim, `CompaniesWallets`, `CastMinerWallets` | P16.2 |
| **P16.4** | Living ballots (A) — a–d: persona axes, drift terms, jitter, abstention + the variation assertion | — |
| **P16.5** | Pause toggle + standing policy (B) — a–c: the toggle, the policy + preview, `auto` marking | P16.4 |
| **P16.6** | Verification run | all |

**Suggested order: P16.2 → P16.1 → P16.4 → P16.5 → P16.3 → P16.6.** P16.2 first because it forces the wipe
(D-16.4) and everything after is then built on the world it produces; P16.3 (scenes) last of the build work
because it is the only purely-cosmetic block and the easiest to defer if the step runs long.

---

## 6. Open questions for the developer (Round 2)

1. **Where does the pause toggle + standing policy persist?** Riding `CompanyGovernanceState` in
   `BlockchainStateSnapshot` is free (the ND.8g inheritance argument) but means a *player preference* gets
   rolled back to the last block on restart — at most a few minutes' loss, but it is conceptually odd. The
   alternative is a small `user://` settings file, which then needs its own checkpoint/delete-list
   reasoning (the three-question rule). **Recommendation: ride the snapshot**, and accept the rollback.
2. **Should the settlement transaction be per company per quarter, or per company whenever N holders are
   payable?** The quarter is simpler and matches the dividend cycle; an N-threshold would smooth the
   traffic but reintroduces a tuning constant. **Recommendation: per quarter.**
3. **Cast miners in the auction/casino economy?** They now get real wallets and change rotation, which
   makes "promote cast miners to casino-player status" (deferred since ND.4b) mechanically closer.
   **Recommendation: still out of scope** — it changes who can bid, which is an auction-balance question,
   not a wallet one.
4. **Do you want the D-16.6 ghost check to be able to KEEP the cosmetics?** As written, a failed check
   leaves them in place and records why. The alternative is to make ghosts spend-incapable by assertion.
   **Recommendation: as written** — an honest check that can fail is worth more than a forced result.
5. **Does P16.4's variation assertion belong in DEBUG only, or should a flat quarter print a warning in
   release too?** `AssertEscalationSlopesAreOrdered` is `[Conditional("DEBUG")]`; this one is arguably a
   live-world health signal. **Recommendation: DEBUG for the ballot-identity check, and a release-safe
   trace column for the per-vote spread**, so a long run can be audited afterwards from the CSV — the way
   this whole class of defect was found in the first place.

---

## 7. Verification checklist (P16.6)

| # | Check | Correct | Failure signature |
|---|---|---|---|
| 1 | `network_population_trace.csv` → `pendingTxs` | Falls to at/below the historical `txTargetPerBlock` band | Still pinned at 26–28 ⇒ settlement not batched |
| 2 | `company_governance_trace.csv` | `dividend_settlement` rows replace the flood of `bot_claim` | `bot_claim` still ~8–9/block |
| 3 | Bot dividends still arrive | Bot BTC balances grow at quarter ends; SC leg still credits | A quarter with no settlement ⇒ the payable test is too strict |
| 4 | BlockExplorer on a bot spend | Multi-input spend shows change to a **fresh derived** address | Change back to the spending address ⇒ `ReceiveWallet` not wired |
| 5 | The two cosmetics are gone | Self-change txs render honestly, no hidden outputs | Any node still producing change-to-self |
| 6 | `CompaniesWallets` / `CastMinerWallets` | Both scroll, both have a footer **outside** the scroll (Ch. 29) | Back button clipped at the bottom band |
| 7 | `vote_close` reserve% for one company | **Varies** across quarters | Frozen at the founding value ⇒ A did not take |
| 8 | Ballots at one vote | Four different values; occasionally a *not voted yet* | Four identical ⇒ drift/jitter not applied |
| 9 | P15.9 tripwire | **Never fires** | Any occurrence ⇒ a drift term bypassed `ProjectStanceIntoBand` |
| 10 | A vote at a company with the toggle OFF | Game does **not** pause; history shows the ballot marked `auto` | A freeze ⇒ B did not gate |
| 11 | Toggle ON | Game pauses as before; submitting resumes | — |
| 12 | Monetary invariant + FED sync | Unchanged from Step 15 | Any drift ⇒ the settlement touched an SC path it should not |

**Exit:** the mempool breathes, every participant is a real UTXO citizen, no two bots vote alike, and the
game only stops for the companies the player asked it to stop for.
