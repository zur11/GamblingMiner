# Step 16 — Living Governance & Full UTXO Participation

> **▶ PICKING THIS UP COLD? READ §9 FIRST** — it carries the live run's state, what is already verified,
> the remaining checks in the order they become reachable, and the diagnostics available on request.
>
> **Status: ALL BUILD PHASES IMPLEMENTED (2026-07-30) — P16.6 (the verification run) IS IN PROGRESS.**
> `P16.1` ✅ · `P16.2` ✅ · `P16.3` ✅ · `P16.4` ✅ · `P16.5` ✅ — each with its build log in §7. Rounds 1–2
> decisions are `D-16.1…18` (§6). Nothing has been run yet: **every phase is build-verified only**, by the
> developer's call to defer testing until the whole step landed, so P16.6 is the first time any of it
> executes. One design is recorded as **deliberately open** and constrains this step by a single
> discipline: the **ghost typology** (§6.1) — P16.2 keys every decision off *"does this record carry a
> seed?"*, never off *"is this a ghost?"*.
>
> **⚠ Two resets are required on the first launch, and they are independent.** `WorldFormatVersion 4 → 5`
> wipes the world; `RegistryFormatVersion 1 → 2` regenerates `bot_wallet_registry.json`, which the world
> wipe does **not** touch (it is an identity file, exempt by design — Ch. 35 §35.1). Both announce
> themselves in the log. If only one fires, see check 0 below.
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
  gone. **Amended by D-16.17/§6.1:** "excluded" means *no seed today*, **not** *ghosts can never have
  keys*. Every wiring decision in P16.2 keys off **"does this record carry a seed?"**, never off "is this
  a ghost?" — that is what leaves the ghost-typology door open, and it is why a future spending ghost
  would arrive already rotating change and could not reintroduce these cosmetics.
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

## 6. Round 2 — the developer's picks (2026-07-30), all five resolved

- **D-16.14 (Q1 — P16.5):** **the pause toggle and standing policy ride `CompanyGovernanceState` in
  `BlockchainStateSnapshot`** (the ND.8g inheritance argument — checkpoint coverage, delete-list membership
  and the pre-genesis path all come free). Accepted consequence: a *player preference* rolls back to the
  last block on restart. At most a few minutes of setting, and the alternative — a `user://` settings file
  — buys correctness on a preference at the price of a fourth answer to the three-question rule.
- **D-16.15 (Q2 — P16.1):** **one settlement per company per quarter**, not an N-payable-holders threshold.
  It matches the dividend cycle that already exists and introduces **no new tuning constant** — and this
  plan is a reaction to a step whose open placeholders could not be priced.
- **D-16.16 (Q3 — scope):** **cast miners stay OUT of the auction/casino economy in this step** — the base
  remains the four casino-miner bots (D-EB.7). **Recorded as a permanently open option, not a closed
  door:** promoting cast miners to casino-player status is now *mechanically cheap* (after P16.2 they hold
  seeds, derived wallets and change rotation like everybody else), so the remaining question is purely one
  of **auction balance**. The developer's framing: introduce them **gradually, as a lever against
  stagnation and for company variety** — e.g. admitting one or two cast miners as bidders when a pool has
  stalled, rather than promoting the whole cast at once. That makes it a **tuning dial the auction can
  reach for**, which is a better shape than a one-time migration. Revisit alongside the §22.10 price-out
  terminator and the ND.10 escalation ladder, since those are the systems a new bidder population would
  perturb.
- **D-16.17 (Q4 — P16.2):** **the ghost check stays as written** (an honest check that can fail beats a
  forced result), **and the ghost model gains a recorded, deliberately-open future design** — see §6.1. The
  resolution that matters for *this* step: **any future ghost that can spend would be seed-backed like
  every other participant**, because P16.2 makes that the default path — so the ghost typology can never
  reintroduce the change-to-self cosmetics D-16.6 removes. The two questions are decoupled by construction.
- **D-16.18 (Q5 — P16.4):** **DEBUG assertion for ballot identity, plus a release-safe trace column for the
  per-vote ballot spread.** `AssertEscalationSlopesAreOrdered`'s `[Conditional("DEBUG")]` shape catches the
  gross failure during development; the CSV column is what lets a long run be audited *afterwards* — which
  is exactly how F1 was found (the trace read column-wise, not the game watched).

### 6.1 — Ghost typology: recorded now, designed later (from Q4)

Today every ghost is one thing: a **session-transient** `NodeAgent` with a random one-off wallet
(`EnsureGhostNodeRegistered`), whose keys die with the process — which is *precisely* what makes its
coinbase frozen forever (D-14.11). The developer's proposed typology replaces that single kind with four:

| # | Kind | Share | Life |
|---|---|---|---|
| 1 | **Always a ghost** | ~80% | mines, never spends, frozen forever — **today's behavior, unchanged** |
| 2 | **Active → ghost** | | participates, then goes dark and stays dark |
| 3 | **Active → ghost → active** | | the **"max sudden whale"** — three states; a dormant pile that reawakens |
| 4 | **Ghost → active** | | silent for years, then joins the economy late |

**The one thing to understand before building it:** this is **not a display feature — it is a change to the
persistence model.** Kinds 2, 3 and 4 all require keys that *survive the process*, which is the exact
boundary D-14.11 drew and the reason ghost coins are frozen at all. So the shape is:

- Kind 1 stays **exactly as it is** — session-transient, no registry entry, no keys, frozen. It is 80% of
  the population and it must stay free.
- Kinds 2–4 become **real identities**: a fourth `BotWalletRegistry` list, seeded and derived-wallet-backed
  like everything P16.2 touches. After this step that is a handful of lines, which is why recording it now
  costs nothing and why the skeleton must not assume "ghost ⇒ keyless".
- **Their transitions should be schedule-driven, not random per session** — the established pattern is
  `ComputeNonMinerIntroSchedule` / `ComputeAndPushFeeSchedule`: derive the dates once from the historical
  curve, push them into a pure static holder. That keeps it **time-shiftable for free** (D-14.7) and makes
  an entry-year world reproduce the same ghost biographies.
- **Kind 3 has real historical resonance** and is the most valuable of the four: dormant 2009–2011 coins
  suddenly moving is a genuine, recurring event in Bitcoin's history, and it is exactly the kind of thing
  the player should be able to *notice* in the Block Explorer.

**Constraint on this step:** nothing in P16.2 may assume a ghost is keyless — the wiring keys off *"does
this record carry a seed?"*, never off *"is this a ghost?"*. That single discipline is what leaves the door
open. Full design deferred; tracked in `PRIVATE_ROADMAP.md`.

---

## 7. Detailed subphase breakdowns

> Each subphase is individually buildable and individually verifiable. The order inside a phase matters;
> the order *between* phases is §5's suggested order (P16.2 → P16.1 → P16.4 → P16.5 → P16.3 → P16.6).

### P16.2 — Bot seeds + `ReceiveWallet` — ✅ IMPLEMENTED (2026-07-30, subphases a–f)

> **Build log.** `dotnet build` clean, 0 warnings. Two things went differently from the spec, both worth
> reading before P16.3:
>
> **1. P16.2d needed no widening at all.** `RescanFounderReceiveWallets` has *always* walked every
> registered node and rescanned whichever carry a `ReceiveWallet` — only its NAME and comment still
> described a founders-only world. Renamed to `RescanDerivedReceiveWallets` and the comment corrected.
> *A stale label made a reader (me, writing §7) believe a widening was needed when the code already did
> the right thing* — the mirror of a stale figure that lies.
>
> **2. P16.2f found a participant the OQ-8.2 scope list never had: PASSPHRASE WALLETS.**
> `NetworkRoot.RegisterPassphraseWallet` builds a spending node with no `ReceiveWallet`, so it was the last
> single-address change-producer standing — and the cosmetics could not honestly be deleted while it
> existed. Found by asking the phase's actual question (*"who can still produce a change-to-self
> output?"*) against the code, rather than trusting the list in `PRIVATE_ROADMAP.md` §5. They are migrated
> too. **Two sub-findings:** its base address is already `DeriveGmAddress(seedPhrase)` at the call sites, so
> `base == DeriveAddress(0)` holds exactly; and because it is created **mid-session** it misses the
> init-time rescan entirely — without a single-node rescan at registration (new `RescanReceiveWallet`) its
> change-held funds would go unowned and every unlock would reuse `DeriveAddress(1)`, which is precisely
> the Step 8 defect documented at `BuildUsedAddressSet`.
>
> **General rule earned here: when deleting a workaround, re-derive the set of cases it covered from the
> CODE — never trust the scope list written when the workaround was added.** Recorded in §29.9 (now
> history) and CLAUDE.md.
>
> Ghosts were confirmed spend-incapable (they are registered and mined with, and appear in no spend path),
> so D-16.6's check passed and did not block the removal.

- **P16.2a — `BotWalletRecord` carries a seed.** Add `SeedWords` (string array, the existing
  `wordlist_256.json` generator the player/casino/founders already use) to the record and to `BotDto`.
  Generate it in `CreateRegistry` (miners + non-miners) and in `AddCastMiner`. **The base address becomes
  `CryptoUtils.DeriveGmAddress(seed)`** — i.e. `DerivedAddressWallet.DeriveAddress(0)` — and the signing
  keys `DeriveSigningKeypair(seed)` / `DeriveSecp256k1CompressedPublicKeyBase64(seed)`, replacing
  `GenerateWallet()` (D-16.3). **A record with no seed keeps its existing generated address and behaves
  exactly as today** — the sentinel-default path (§39.16 rule 5), and the reason a stale registry
  degrades rather than crashes.
- **P16.2b — version-gate the registry regeneration (D-16.4).** `bot_wallet_registry.json` is an identity
  file **exempt from the world-reset delete list**, so a `WorldFormatVersion` bump alone will *not* renew
  it — and a stale registry means seedless records, meaning the whole phase silently appears not to work.
  Add a registry-format marker: on mismatch, regenerate from scratch and log it loudly. **Do not** put the
  registry in the world delete list (that would destroy identity on every timeline switch, which is the
  distinction Ch. 35 §35.1 exists to preserve).
- **P16.2c — wire the node.** In `CreateAndRegisterNode`, when the record carries a seed:
  `node.ReceiveWallet = new DerivedAddressWallet(seed); node.RotateCoinbaseAddress = false;` (D-16.5 —
  coinbase non-reuse stays Satoshi-only). Same two lines in **`RegisterCastMinerNode`**, the mid-session
  spawn path. **Key off the seed's presence, never off the node's kind** (D-16.6 as amended).
- **P16.2d — widen the rescan.** `RescanFounderReceiveWallets` → `RescanDerivedReceiveWallets`: every
  registered node holding a `ReceiveWallet`, not just the founders. It already builds its used-address set
  in a single pass, so the added cost is address derivation (~20 SHA256 per node past the frontier), not
  chain scans. **Verify the cost at 74 nodes** — if it is material at launch, that is a finding for T4, not
  a reason to skip the rescan.
- **P16.2e — `WorldFormatVersion` 4 → 5** and confirm the delete list is complete for the new world.
- **P16.2f — the ghost check, then the cosmetics.** Confirm no ghost ever *spends* (they receive a coinbase
  and are frozen — D-14.11). If confirmed: delete `BlockExplorer.IsSelfChangeTransaction` and
  `ExternalOutputs`, and remove §29.9's cosmetic note from the docs. **If not confirmed: keep both, record
  why, and say so in CLAUDE.md** — a documented exception beats a quiet one.

### P16.1 — Dividend settlement batching — ✅ IMPLEMENTED (2026-07-30, subphases a–c)

> **Build log.** `dotnet build` clean. Three notes for P16.6's verification:
>
> **1. The split is BTC-batched / SC-per-block, and the old function was split in two to say so.**
> `TryAutoClaimBotDividends` → `PayBotScDividends` (SC only, per block — it costs no transaction, no fee
> and no block space, and prompt SC funds the bots' bidding) + `SettleCompanyDividendsBtc` (one
> multi-output transaction per company per quarter). Renaming rather than keeping one function with half
> its body removed is the P16.2d lesson applied on purpose: a name that describes the old behaviour is a
> stale label, and stale labels are what make the next reader wrong.
>
> **2. Where it is called is load-bearing.** Immediately after `SettleDividendCycleAtQuarterEnd` (which
> credits the closing quarter's claimables) and **before** `TryBankQuarterlyRepayment`. That repayment's
> own comment has always claimed "the closing quarter's obligations are already met" — which was *aspirational*
> while dividends trickled out across the whole quarter, and is now actually true.
>
> **3. `BotDividendClaimFeeMultiple` is retired but kept commented, with why.** ND.10e bounded the per-claim
> COST (10× fee) and that was correct; it never bounded the claim RATE, which is what the audit measured
> at 8.66 tx/block. **Bounding a per-event cost is not the same as bounding the event rate, and only the
> second protects a shared budget.**
>
> Telemetry (P16.1b/c): `bot_claim` is gone with the per-holder transaction that produced it. Replacements
> — `dividend_settlement` (holders + net BTC + fee, once per company per quarter),
> `dividend_settlement_failed` (**carrying a reason**: `insufficient_utxos` / `unresolvable_key` /
> `rejected_by_mempool` — ND.10e's rule that a failed payout must never be indistinguishable from "nothing
> was due", which matters MORE once one row covers N holders), and `bot_claim_sc` aggregated per company
> per block instead of per holder (the rule was that the SC leg be *visible*, not that it be one row each,
> and per-block telemetry I/O is itself one of the F5 per-block costs).
>
> Claimables are cleared **only after a successful broadcast**, so a failed settlement leaves every holder's
> accrual intact for the next quarter.

- **P16.1a — the multi-output settlement.** At a company's quarter close in `TickCompanyGovernance`,
  collect every holder with a **payable** BTC claimable (D-16.2 — the `HasPlayerClaimableDividends`
  "payable, not non-zero" test is the model) and pay them in **one transaction** through
  `DistributePoolEventAsSingleTx`'s path. Zero those claimables only on a successful broadcast; on failure
  leave them accruing and log it (see P16.1b).
- **P16.1b — telemetry parity.** ND.10e closed two blind spots — `bot_claim_failed` on a silent broadcast
  failure, and the SC leg's visibility (`sc=` + the standalone `bot_claim_sc` row). **Both must survive
  into the batched path.** A batched settlement that fails silently is a worse blind spot than the one it
  replaces, because it now hides N holders instead of one.
- **P16.1c — the trace.** One `dividend_settlement` row per settlement carrying holder count, total BTC,
  fee, and the per-holder breakdown or a count — replacing up to N `bot_claim` rows. Keep the retired
  row-kind's name out of the new schema, and **rotate the trace to `.csv.old` on a schema change** (the
  ND.10j rule — the difficulty trace learned this the hard way).
- **Leave the player's manual claim untouched** (D-16.1). It is player-initiated and rare, and it is the
  one claim whose immediacy is a feature.

### P16.4 — Living ballots — ✅ IMPLEMENTED (2026-07-30, subphases a–d)

> **Build log.** `dotnet build` clean. Four notes:
>
> **1. The three axes were chosen so no two overlap.** Band/market/greed already answer *what do I want* and
> *how much do I want paid out*; these add **reaction**, not more opinion. `Conviction` scales the whole
> drift sum, `RiskAversion` weights only the defensive terms (FBI heat, an unpaid installment), and
> **`Horizon` carries a SIGNED weight on price momentum** — short-horizon bots follow a rally, long-horizon
> bots fade it into the stable asset. That sign is the load-bearing part: it is what makes two bots read one
> market *oppositely*, which no amount of tuning a single-signed term can produce. `Steadfast` conviction
> (×0.25) keeps the pre-P16.4 near-constant ballot alive as one of four temperaments, the shape P15.4c
> already gave greed.
>
> **2. `BackfillGreedPreferences` became `BackfillMissingStances`.** Greed was the first axis to need the
> empty-default + backfill contract; these were the second through fourth, and a fourth near-identical
> method was the moment to name the pattern. One table of `(axis, read, write, order)` now covers all of
> them — **the next axis is a row, not a method.**
>
> **3. Peer anchoring was designed in §4.3 and deliberately NOT built.** `gov.ReserveScPercent` is a
> **band-space** number; drifting a **global** stance toward it mixes the two spaces, and inverting the
> projection to fix that is machinery for no gain. The momentum term already supplies the slow trend peer
> anchoring existed to create, because BTC prices move continuously. Recorded in code beside the terms so
> the omission reads as a decision, not a gap.
>
> **4. Every term is summed BEFORE the projection, never after** (D-16.8). Adding a term to an
> already-projected value would be a category error *and* would need its own clamp — quietly re-creating
> the exact P15.9 bug where a ballot lands outside the charter. The jitter is the only post-projection
> term and it is re-clamped to the band.
>
> **Abstention (D-16.10)** is deterministic per `(company, quarter, bot)` at 15%, and is **disabled for
> shortfall votes** — that ballot decides whether a bank survives and who eats the gap, which is not a
> meeting anyone skips (it also keeps `CloseCompanyVote`'s shortfall path from ever resolving with zero
> ballots). Determinism throughout comes from a **stable FNV-1a hash**, not `string.GetHashCode()`, which is
> randomized per process in .NET and would have made an open vote's persisted ballots change across a
> restart — precisely what D-16.9 forbids.
>
> **Guards (P16.4d):** `vote_open` now carries `ballots=N spread=X.X` on **every** vote (release-safe), and
> a `[Conditional("DEBUG")]` tripwire fires when **three or more** bots cast the identical target — the
> signature of a constant function, while two bots agreeing in a narrow band is legitimate. The spread
> column is the cheap detector for this entire class of defect: **it would have read `0.0` for 517
> consecutive votes.**

- **P16.4a — the persona axes.** Add `Conviction`, `RiskAversion`, `Horizon` to `BotGovernancePreference`,
  drawn as shuffled permutations in `EnsureBotGovernancePreferences` with a `Backfill…` twin per the
  greed precedent (empty string = "drawn before this axis existed", filled only where absent). Extend
  `PrintBotGovernanceStances` so the launch log shows the full persona — that readout is how P15.9's own
  fix was verified.
- **P16.4b — the drift terms.** Implement §4.3's table as pure functions `(bot, company, date) → decimal`,
  summed and scaled by `Conviction`. **Every term moves the STANCE**; `ProjectStanceIntoBand` stays the
  last step before the ballot (D-16.8). Build them one at a time and log each term's contribution — a
  single fused number is unattributable when it misbehaves.
- **P16.4c — jitter + abstention.** Jitter seeded on `(companyId, quarterIndex, botNodeId)` (D-16.9 — an
  open vote's ballots are persisted and must not change under the player on restart). Abstention as a
  per-bot roll; an abstaining holder shows as *not voted yet* in P15.9f's open-vote list, and
  `ComputeReserveVoteOutcome` already handles it with no resolver change (D-16.10).
- **P16.4d — the variation guards (D-16.18).** A `[Conditional("DEBUG")]` assertion that the ballots at one
  vote are not all identical, **plus** a release-safe ballot-spread column in
  `company_governance_trace.csv`'s `vote_close` row. The CSV column is the one that matters: it is how a
  long run gets audited afterwards, which is how F1 was found in the first place.

### P16.5 — Pause toggle + standing policy — ✅ IMPLEMENTED (2026-07-30, subphases a–c)

> **Build log.** `dotnet build` clean. Four notes:
>
> **1. "Not configured" is a sentinel, not a value.** The three policy fields persist as `-1` meaning
> *follow the company's current value*, so an untouched policy votes the **status quo** (D-16.12) — it
> participates (which matters: abstaining would change every other holder's relative weight) without
> steering anything the player never asked to steer. Storing a resolved number instead would have frozen
> whatever the company happened to hold the day the feature shipped, and presented it as the player's
> choice.
>
> **2. `MarketShift` is never auto-cast.** A category shift is discrete, hard to undo, and at a bank it is
> refused outright (D-15.12) — it is the one control worth requiring the player's presence for. The
> standing ballot always submits `0`, and the panel says so.
>
> **3. The standing ballot is clamped through the SAME bounds `TryRegisterPlayerVote` uses.** A policy
> stored before a band or category change would otherwise cast an out-of-charter ballot — the P15.9 failure
> arriving through a new door, *a stored value outliving the range that made it legal*. Worth watching for
> wherever a preference is persisted against a mutable range.
>
> **4. Two UI details that were nearly bugs.** The action panels are signature-gated (so live refreshes
> don't fight the player's typing), and the pause flag was not in the signature — so toggling it left the
> panel explaining the *old* behaviour until the next vote opened. Added. But the numeric fields
> deliberately stay OUT of the signature, since rebuilding on each keystroke is exactly what the gate
> exists to prevent — which is also why the panel needs **its own** feedback line: pressing Save with the
> pause unchanged rebuilds nothing, and a button with no visible effect reads as a broken button.
>
> **Shortfall votes take the same rule, with no exception** (§4.4): one toggle per company that quietly
> excluded a vote kind would make the toggle lie. Note the interaction with P16.4: bots never abstain from
> a shortfall vote, so that ballot is always fully attended — the player's auto-cast simply joins it.

- **P16.5a — the state.** `CompanyGovernanceState` gains `PlayerPauseOnVotes` (default **false**) and a
  standing policy (reserve %, payout %, shortfall split), riding the snapshot (D-16.14). Defaults are the
  **currently-applied** company values, so an unconfigured policy is a status-quo no-op (D-16.12).
- **P16.5b — the gate.** In `OpenCompanyVote`, a player NST holding sets `AwaitingPlayerVote` **only** when
  that company's toggle is on; otherwise cast the standing policy immediately via the same path
  `TryRegisterPlayerVote` uses, marked `auto` (D-16.13). **Shortfall votes take the same rule** — one
  toggle per company, no hidden exception, or the toggle lies.
- **P16.5c — the UI.** The toggle and the policy fields on `CompanyDetails`, with an "if the vote closed
  now" preview through `ComputeReserveVoteOutcome` (never a second implementation — §39.16 rule 6). The
  vote history must render an auto-cast ballot **as auto**, so it can never imply the player deliberated.

### P16.3 — Wallet scene split — ✅ IMPLEMENTED (2026-07-30, subphases a–c)

> **Build log.** `dotnet build` clean. Three notes:
>
> **1. Inheritance, not three copies.** `BotsBtcWallets` becomes the BASE of three sibling screens, each
> overriding only *which records to list* and *what to call them* — the ~450 lines of wallet / transaction
> / send / dev-control UI stay in one place because the detail panel already branched on `IsMinerNode` and
> needed no change at all. Three copies would have been the alternative, and **the third copy is where such
> things start to drift apart silently.**
>
> **2. One `.tscn` shape, sections hidden when empty.** All three scenes keep the proven two-column layout
> (`HSplitContainer` holding two `ScrollContainer`s — note this is the arrangement Ch. 29 permits: the split
> *contains* the scrolls, it is not *inside* one). A section with no population is **hidden**, not left as
> an empty header, which would read as "loading" or "broken" rather than "not here". `MinersSectionLabel`
> and `HoldersSectionHeader` gained `unique_name_in_owner` so the base can address them.
>
> **3. `margin_bottom` raised 30 → 50 on the new scenes** per §29.11's bottom safe area. The Back button
> here lives in a fixed TOP bar, so it was never at risk, but the detail column's last line was.
>
> Main Menu gains **Company Wallets [DEV]** and **Cast Miner Wallets [DEV]**; the original button is
> relabelled **Casino Bot Wallets [DEV]** so the three read as a set. The cast screen carries an explicit
> empty state — cast miners spawn-drip as the historical curve grows, so an early world legitimately shows
> none, and saying why is the difference between an empty list and a broken one.

- **P16.3a — trim `BotsBtcWallets`** to the four casino-miner bots. **Read Ch. 29 before touching the
  `.tscn`** — bounded scroll chain, fixed footer *outside* the scroll, and do not mirror another scene's
  layout without checking it first.
- **P16.3b — `CompaniesWallets`** (new): the 40 companies. Natural place to show treasury / `ScReserve` /
  `CollateralBtc` beside each address, since this is where a developer will look when a company's money
  does not add up.
- **P16.3c — `CastMinerWallets`** (new): the cast. Register both in `SceneManager.SceneId` + `Paths`, and
  use `DescribeNodeForDev`'s two-tier naming — DEV form (`Mt. Gox (non_miner_7)`), because these screens
  are read beside the CSV traces whose join key is the raw id (ND.10g).

### P16.6 — Verification run

Work §8's checklist top to bottom on a fresh world. **This is not another multi-hour calibration run** —
every check below is reachable within the first in-game year except the quarterly ones, which need one
company founded plus a quarter (~2010-08 onward at `DevEntryYear = 2010`). Set `DevEntryYear` back to `0`
before merging, and back up `user://logs/*.csv` first if the run is worth keeping (the §10 lesson).

---

## 8. Verification checklist (P16.6)

> **Order matters.** Checks 0 and 4 are visible within seconds of the first launch; 1–3 need a founded
> company plus a quarter (~2010-08 onward at `DevEntryYear = 2010`); 7–11 need a company where the player
> holds NST, which means winning a top-3 tracked tier in an auction. **This is not another multi-hour
> calibration run** — it is a first-execution pass. Back up `user://logs/*.csv` before switching
> `DevEntryYear` back to `0`, and remember the traces are in the world-reset delete list (§10.6's lesson).

| # | Check | Correct | Failure signature |
|---|---|---|---|
| 0 | **First launch, log** | BOTH resets announce themselves: `[NetworkRoot] World reset triggered (format 4 → 5…)` and `[BotWalletRegistry] Registry format 1 != 2 … Regenerating ALL bot/company/cast identities` | Only the world reset ⇒ the registry kept its old seedless records and **every bot stays single-address while the rest of the step appears to work** — the exact silent failure P16.2b's version gate exists to prevent |
| 1 | `network_population_trace.csv` → `pendingTxs` | Falls to at/below the historical `txTargetPerBlock` band | Still pinned at 26–28 ⇒ settlement not batched |
| 2 | `company_governance_trace.csv` | `dividend_settlement` rows replace the flood of `bot_claim` | `bot_claim` still ~8–9/block |
| 3 | Bot dividends still arrive | Bot BTC balances grow at quarter ends; SC leg still credits | A quarter with no settlement ⇒ the payable test is too strict |
| 4 | BlockExplorer on a bot spend | Multi-input spend shows change to a **fresh derived** address | Change back to the spending address ⇒ `ReceiveWallet` not wired |
| 5 | The two cosmetics are gone | Self-change txs render honestly, no hidden outputs | Any node still producing change-to-self |
| 6 | Main Menu → the three wallet screens | **Casino Bot Wallets** lists 4; **Company Wallets** lists the introduced companies + the inactive filter; **Cast Miner Wallets** lists the spawned cast (or its explicit empty state). Each scrolls, each keeps its Back button readable | An empty section header with no rows ⇒ the hide-when-empty branch; a clipped bottom line ⇒ §29.11 margin |
| 7 | `vote_open` → `spread=` in `company_governance_trace.csv` | **Non-zero and varying** across votes | `spread=0.0` on every row ⇒ P16.4 did not take. This is the column F1 would have failed on |
| 8 | Ballots at one vote (`CompanyDetails` open-vote list) | Different values; occasionally a *not voted yet* (abstention) | All identical ⇒ drift/jitter not applied. In DEBUG the P16.4 tripwire also fires |
| 9 | `vote_close` reserve% for one company across quarters | **Varies** | Frozen at the founding value for 5+ quarters ⇒ the drift terms are all inert (check that the market has a price — the momentum term is null before Market Birth **by design**) |
| 10 | P15.9 tripwire | **Never fires** | Any occurrence ⇒ a drift term bypassed `ProjectStanceIntoBand`, or a stored policy outlived its band |
| 11 | A vote at a company with the toggle OFF | Game does **not** pause; the panel says *"Your standing policy voted for you (N% SC reserve)"* | A freeze ⇒ P16.5b did not gate |
| 12 | Toggle ON, then a vote | Game pauses exactly as before; submitting resumes | — |
| 13 | Launch log → `[Governance]` stances | Each bot prints a second `reacts:` line — conviction / risk / horizon with their multipliers, and **at least one bot `follows` while another `fades`** | No `reacts:` line ⇒ stale build; all four the same direction ⇒ the permutation draw broke |
| 14 | Monetary invariant + FED sync | Unchanged from Step 15 | Any drift ⇒ the settlement touched an SC path it should not |

**Exit:** the mempool breathes, every participant is a real UTXO citizen, no two bots vote alike, and the
game only stops for the companies the player asked it to stop for.

---

## 9. ⏸ SESSION HANDOFF — read this first if you are picking Step 16 up cold (2026-07-30)

> Written at the point the P16.6 verification run was interrupted. Everything below is state, not plan.

### 9.1 Where things stand

| | |
|---|---|
| Branch | `living-governance-and-bot-wallets`, pushed to `origin` |
| Commits | `5b81853` P16.2 · `d6faba8` P16.1 · `4d69c50` P16.4 · `8d62214` P16.5 · `6b20715` P16.3 + P16.6 prep · `994ace8` wordlist cache + entry year · `3e1dff3` secp256k1 + bot base-address reads (§9.5) · **this one** reserve-guard seed + wallet-screen totals + these docs |
| Build | clean, 0 warnings |
| **`TimelineConfig.DevEntryYear`** | **`2011`** — ⚠ **MUST go back to `0` before merging to `main`** |
| World | fresh (the developer deleted `user://` entirely), landed **2011-03-21 11:49:32**; run reached **block 2188 / ~2013-02** (2026-08-01), **21 companies founded** (1 of them a bank), FBI activated at block 1745, player holds NST in **10** — audits at §9.6 (block 1753) and §9.8 (block 2126) |
| Bootstrap | Satoshi 107 · Hal 26 · cast 410 (11 spawned) · ghost 540 · 7 non-miners seeded |
| Status | ✅ **STEP 16 COMPLETE (2026-08-05).** All build phases implemented; P16.6 run finished with **all 15 checks verified**; P16.7 (two dividend defects + the no-quorum default) and P16.8 (player abstention, incl. subphases b–k) built after the run and **verified live**. Ten defects found and fixed across the run — §9.5, §9.8, §9.9. **Only remaining action: `DevEntryYear = 0` → rebuild → merge.** One cosmetic left open by choice: the `bots_only` label, §9.3 item 6 |

**This world's bot stances** (re-drawn at the wipe — they ride the world snapshot, unlike the registry):

| bot | band | market | greed | conviction | risk | horizon |
|---|---|---|---|---|---|---|
| bot_1 | CB4 | dark_grey | not_so_greedy | measured ×0.60 | cautious ×1.00 | generational **×−1.0 fades** |
| bot_2 | CB1 | light_grey | extremely_greedy | steadfast ×0.25 | bold ×0.25 | short_term **×+0.5 follows** |
| bot_3 | CB5 | official | almost_greedy | responsive ×1.00 | fearful ×1.50 | trader **×+1.0 follows** |
| bot_4 | CB3 | black | greedy | reactive ×1.50 | steady ×0.60 | long_term **×−0.5 fades** |

### 9.2 Verified so far

- **Check 0 ✅** — world reset `format 0 → 5`, identities created fresh. Confirmed on disk:
  `bot_wallet_registry.json` has `"formatVersion": 2` and **44 records carrying `seedWords`** (4 bots + 40
  companies; 0 cast at that moment). *Note the version GATE itself is still unexercised* — deleting the
  folder took the create-from-scratch path. It only fires against a pre-existing v1 registry.
- **Registry survives a timeline wipe ✅** — the second launch (`ENTRY-2011` re-tag) printed
  `[BotWalletRegistry] Loaded — 4 miner bots, 40 non-miner bots`, i.e. identities are correctly NOT world
  state, while the governance stances correctly WERE re-drawn.
- **Check 6 ✅** — the three wallet screens (developer-confirmed). ⚠ *All three were nonetheless reporting a
  base-address-only balance until 2026-07-31 (§9.5, §30.10) — the split itself was right, the number was not.*
- **Checks 4 & 5 ✅ (2026-07-31, verified from the chain rather than by eye).** The first bot→company bid,
  block **#1703**, tx `c5e870ac88ed`: one input from bot_1's **base** address (a 50.00144440 coinbase),
  `OUT[0]` 0.03786870 to BitPaid, `OUT[1]` 49.96307687 to a **fresh derived** address, fee 0.00049883 —
  Σin = Σout + fee exactly. So change rotates for bots (check 4) and a multi-output spend's arithmetic adds
  up with nothing hidden, the two cosmetics being gone (check 5). Confirmed at scale afterwards: bot_4's
  sixth bid was itself funded from the change of its third, i.e. derived-address spends round-trip.
- **Check 13 ✅** — every bot printed its `reacts:` line, all four temperaments distinct, and the momentum
  axis split 2 following / 2 fading.
- **`[ENTRY-2011 DEV]` watermark + clock at 2011-03-21 ✅.**

### 9.3 What is left, in the order it becomes reachable

1. ~~**Checks 4 & 5**~~ — ✅ done 2026-07-31 from the chain, see §9.2. (Still worth one glance in Block
   Explorer at a bot spend, purely to confirm the *rendering* matches the data.)
2. ~~**Play until a company FOUNDS**~~ — ✅ done 2026-07-31. **Three** founded by block 1752: Blackmarket
   Reboot (`non_miner_13`, CB4/black), Papa's Pizzeria (`non_miner_2`, CB1/official), Seals with Clubs
   (`non_miner_16`, CB4/light_grey). ⚠ **All three closed UNCONTESTED** — `trackedDonationCount=1`,
   `holderCount=1`, and the same winner in all three: **bot_1**. See §9.6 for why, and read it before
   interpreting item 3.
3. ~~**Check 7 — the single most valuable one.**~~ — ✅ **RESOLVED 2026-08-01 at block 2188** (§9.8).
   `spread=` is non-zero and widely varying: **2.0 → 42.0** across the 61 votes that drew ≥2 ballots,
   including a **25.0** — the exact figure §9.3 predicted for a CB1 company. The `0.0` rows are all
   `ballots=1` (arithmetic, §9.6.2) or the three unanimous abstentions in §9.8.3. **P16.4 is confirmed:
   this is the column F1 would have failed on, and it now moves.**
4. ~~**Checks 1, 2, 3 — the dividend batching.**~~ — ✅ **done 2026-08-01** (§9.8). `pendingTxs` mean
   **3.39** against a `txTargetPerBlock` of 2.57 over the last 200 blocks — tracking the historical band
   instead of pinned at P15.8's 26–28 (check 1). **Zero `bot_claim` rows** in the whole trace against 17
   `dividend_settlement` batches (check 2) — the 599 `bot_claim_sc` rows are the SC leg, which P16.1a
   deliberately left per-block. Settlements carry real BTC (e.g. `holders=2 btc=176.99345928`), so bot
   balances still grow, at quarter ends (check 3).
5. ~~**Check 9**~~ — ✅ **done 2026-08-01**. `vote_close` reserve% moves across quarters at every company
   with quarterly history: Seals with Clubs 29 → 27 → 26, Laundromat 15 → 9.02 → 8.76, Bitcoin Market
   39.55 → 41.55, BTC-e 67.95 → 69.32. Not frozen at the founding value.
6. ~~**Checks 8, 11, 12**~~ — ✅ **done 2026-08-02, developer-confirmed at the DeepBit / BTC Guild vote
   cluster.** The player holds NST in **10 companies** and foundings reach **6 holders**, so §9.6.2's
   single-bidder blockage is gone. Toggle ON at DeepBit (3 NST holders) paused the sim and rendered the
   open-vote ballot list with differing values (8, 12); the three toggle-OFF companies voted through
   without stopping the game and reported the standing-policy line (11). **The auction system as a whole
   is confirmed working** — it is what produced the multi-holder foundings all three checks needed.
   ⚠ Cosmetic, still open: `vote_open` labels a vote `bots_only` even when the player's **standing ballot
   was cast into it** ([NetworkRoot.cs:4157](../Scripts/BlockchainPort/Simulation/NetworkRoot.cs#L4157)
   reads `AwaitingPlayerVote`, not "did the player participate"). Harmless, but it made the toggle-OFF path
   look like the player was absent — worth a third label (`player_policy`) if anyone touches that line.
7. ~~**Checks 10 & 14**~~ — ✅ **done 2026-08-01**. Zero `PrintErr`/tripwire lines in `godot.log` for the
   whole session (check 10). The invariant reconciles **exactly** (check 14): grants `200,000` (5 × 40k)
   + debt `377,668.33563304` = circulation `577,668.33563304`, and the FED's three accounts (casino
   `200,000`, fbi `100,000`, `bank:non_miner_22` `77,668.33563304`) sum to the ledger's `DebtByBorrower`
   to the satoshi. Note the first **bank** FED client now carries real drawn debt — P15.3's credit loop is
   live, not just wired.
8. ~~**Then: `DevEntryYear = 0`, rebuild, merge.**~~ — ✅ **`DevEntryYear` restored to `0` and compiled
   clean, 2026-08-05.** The next launch re-tags the timeline and therefore **wipes and rebuilds the world**,
   landing on the canonical **2009-03-21** start — intended, and cheap (§9.7: this run's telemetry is not
   worth preserving, unlike P15.8's). `bot_wallet_registry.json` survives the wipe by design (identity, not
   world state), so the P16.2 seeds carry over. **Remaining: one launch to confirm the canonical start, then
   commit + merge — both the developer's, per CLAUDE.md's git workflow.**

### 9.4 Diagnostics on offer — just ask

**I can read the live save directory directly** (`%APPDATA%\Godot\app_userdata\GamblingMiner\`); nothing
needs pasting. Say the word and I will:

- **"revisa el spread"** → parse `company_governance_trace.csv`'s `vote_open` rows, report the `spread=`
  distribution and whether any vote came back all-identical (check 7/8).
- **"revisa los dividendos"** → count `dividend_settlement` vs any surviving `bot_claim` rows, cross-read
  `network_population_trace.csv`'s `pendingTxs` against `txTargetPerBlock` (checks 1–3).
- **"revisa las votaciones"** → track one company's `vote_close` reserve% and payout% across quarters and
  say whether they move (check 9), plus any `dividend_settlement_failed` reasons.
- **"revisa el invariante"** → read `central_bank_state.json` + `sc_monetary_ledger.json` and reconcile
  `circulation = grants + debt` (check 14).
- **"revisa el rendimiento"** → `difficulty_trace.csv` solvetime vs the 58,500 s target and the
  `simSecConsumed/simSecOffered` retention, to see whether P16.1's traffic cut moved the frame budget
  (this is *evidence for T4*, not a step-16 exit condition).

Paste any log excerpt and I will read it too — the wordlist defect below was found that way.

### 9.5 Found DURING the run (not in review)

- **The wordlist re-parse** (fixed, `994ace8`): every cast-miner spawn re-read and re-parsed
  `wordlist_256.json` and printed a line, because P16.2a gave `EnsureWordlist()` a hot call site it never
  had. Eleven times in one bootstrap, and once per spawn thereafter. Now cached per process. **Rule: an
  `Ensure…` that redoes its work every call is harmless with two callers and a liability the moment it
  gains a hot one — when adding a call site, check what the helper does on the SECOND call.** It was
  invisible in code review and obvious in the log.

- **The six-minute launch** (fixed, `3e1dff3`; full write-up **ProjectDesignManual §40.7**): the app took
  ~6 minutes to reach the main menu on an unplayed world. `Secp256k1.ScalarMul` used affine coordinates,
  where every point-add and point-double needs a modular inverse computed as a full 256-bit `ModPow` — so
  **one address derivation ran ~384 modexps and measured 127 ms**. Fine at ~6 seeded wallets; P16.2 made it
  ~79, and the launch rescan's ~1,900 derivations became ~4 minutes. Moved to Jacobian coordinates (one
  inversion per scalar multiply instead of 384): **127 ms → 3.3 ms, 31×**, verified bit-for-bit identical
  over 490 vectors + the `k=1` known-answer test, so no version bump. **Rule: a cost note is a measurement
  or it is a guess wearing a measurement's clothes.** The note at `RescanDerivedReceiveWallets` said
  "~20 SHA256 per node" — never timed, wrong by five orders of magnitude, and *read* as quantified, which
  is exactly why nobody re-checked it in the phase that multiplied it by thirteen. Its other half held
  perfectly ("a T4 finding, never a reason to skip the rescan") — the rescan is untouched.

- **Bots could spend change they could not see** (fixed, `3e1dff3`; full write-up **§30.10**): §30.9 ends
  "single-address participants (`bot_1..4`, OQ-8.2) need no equivalent", and **P16.2 deleted that premise**
  while eleven call sites went on relying on it. The spend path unions the owned address set; the
  affordability reads used `GetAddressSpendableBalance(node.WalletAddress)` — base only. So every bid moved
  ~50 BTC out of view (the consumed coinbase left the base, the change landed on a derived address), three
  bids took bot_1 from an apparent 300 to 150, and the ND.10e reserve guard parked it holding 299.9. Found
  by replaying the chain against `casino_bot_bid_trace.csv`: reported `300.00268330 / 250.00123890 /
  200.00061702` vs a truth of `300.00268330 / 299.96431577 / 299.93358562`, the gap being exactly the three
  change outputs. Fixed at **three layers** — 8 engine reads → `AggregateSpendable`; identity matching →
  the owned set (including `BuildAuctionBidderIdentity`'s **bot** half, whose player half had been correct
  since 2026-07-14, plus `ReBidProbabilityLabelForSlot`'s self-eviction guard and
  `AccumulateCompanyInflows`, which feeds the game-pausing >30% vote); and display → `GetNodeWalletTotals`
  for `BotsBtcWallets` (+ its two P16.3 subclasses), which was showing `0.00000000 BTC` for bots holding
  ~300. **Rule: when a capability is extended to a new class of participant, the reads that were correct
  only because that class lacked it will not announce themselves — they compile, run, and return a
  plausible number.** Grep for the retired *premise*, not just the code implementing it; three comments
  still asserted "bots are single-address" and were corrected with the fix.

- **A bot bid for ten blocks against its own reserve guard** (fixed here; full write-up **§22.20**):
  `_botsRestingOnReserve` is in-memory with a per-block sweep as its only writer, and §22.18 already ruled
  that such a cache "is empty and lying at process start — if a reader can predict the sweep, it must".
  ND.10j applied that to the *reader* (hysteresis over the live balance) and not to the cache's own
  *memory* — **so this is the second violation of a rule already written down.** The guard is
  `≤ 200` to rest, `≥ 300` to resume, so between the thresholds the answer depends on **how the bot
  arrived**, and 249 BTC cannot say which. Chain replay: `bot_4` peaked at exactly `250.00000000` and never
  reached 300, so it should have been resting since block 1 — instead a rebuild wiped the set, `249 > 200`
  passed the un-rested branch, and it took the **leading bid in six auctions**, three contested by the
  player. Fixed with `EnsureReserveGuardSeeded` (one chain replay per process, derived not persisted, no
  version bump), a launch line naming each bot's state, and a `[Conditional("DEBUG")]` tripwire at the bid
  broadcast. **Rule: predicting a sweep from the current value only works for a *memoryless* predicate; a
  predicate with hysteresis has to be replayed.** Audited every other in-memory static in `NetworkRoot` —
  only `_stuckBidderSignatures` (seeded at ND.10j) and this one carry history.
  - *Side finding, recorded not changed:* bots are born at 0 BTC, which is `≤ 200`, so **every bot is born
    resting and the 200 never gates a fresh one — only the 300 does.** A new bot must mine six blocks
    before its first bid, which is why the auctions sat inert for ~617 blocks of this run. Emergent rather
    than designed; left pending the variable-reserve work in `PRIVATE_ROADMAP.md` → "Casino-Bot Treasury
    Policy", where all five thresholds are already flagged as placeholders.
  - *Also observed, no change made:* a pool's tie-break weight is `FreshPoolSeedingWeight = 34` for any
    pool the bot has **not personally bid in** — so a virgin pool needing `0.019` BTC and a three-way
    contested pool needing `0.119` carry identical weight and an identical 100% roll. bot_4 duly paid
    **2.6× more than bot_1 for the same six bids**. D-ND10l.2 justified that constant for *seeding new
    companies*; applying it unchanged to a live bidding war reuses a seeding weight for a different
    question. Developer decision (2026-07-31): **leave it** — the run is a DEV entry-year world and the
    pool choice is genuinely uniform, so it can be refined later on canonical data.

### 9.6 Mid-run audit — 2026-07-31, at the first three foundings (block 1753 / 2012-04-10)

> Read from the live save directory (traces, `blockchain/state.json`, `central_bank_state.json`,
> `sc_monetary_ledger.json`, `bot_wallet_registry.json`), not by eye. **Nothing is broken** — every check
> that *can* be exercised passes. The finding is about what the run can and cannot currently test.

**9.6.1 Passing.** Check 0 (registry `formatVersion: 2`, 44 seeded records) · Check 1 — `pendingTxs`
**26–28 → mean 1.03** against a 0.44 target band, i.e. P16.1's traffic cut took · Check 10 — zero
`PrintErr` in the whole session · Check 13 — four distinct `reacts:` lines, momentum split 2 following /
2 fading · Check 14 — exact: grants `200,000` + debt `140,000` (casino 40k, fbi 100k) = circulation
`340,000`, FED and ledger agreeing. **Performance (evidence for T4, not an exit condition):** mean
solvetime **60,057 s vs the 58,500 target (+2.7%**, was +6.6% at P15.8), retention **0.816** (was 0.713)
— the Jacobian fix and the traffic cut both show up in the frame budget.

P15.9a is also confirmed live: bot_1 is CB4 (stance 25) and cast **81** at Papa's Pizzeria (CB1), the exact
figure §9.3 predicted. `shift=0` on all three is *correct*, not inert — P15.10 established that
`CloseCompanyVote` evaluates a market shift only on a **quarterly** vote.

**9.6.2 The finding: the auctions are single-bidder, so `spread=0.0` is arithmetic.** All three foundings
read `trackedDonationCount=1, holderCount=1, totalPst=0`, same winner (bot_1). One holder ⇒ one ballot ⇒
zero spread by definition. `AssertBotBallotsVary` correctly does not fire (it guards on `Ballots.Count < 3`),
so the tripwire's silence here is not evidence either way. **Do not read these three rows as a P16.4
failure.**

The cause is §9.5's side finding, now measured. Blocks mined across the whole chain: **bot_1 = 6,
bot_4 = 6, bot_2 = 5, bot_3 = 1** (player 6). At 50 BTC/block the resume threshold is *exactly six blocks*,
and the launch line agrees — `bot_1=biddable  bot_2=RESTING  bot_3=RESTING  bot_4=RESTING`. Casino
participants hold **5.0 of ~140 total network power (3.6%)**, so a bot earns ~4 blocks per in-game year:
bot_2 is one block short, bot_3 five. bot_4 crossed 300, spent to ~249 on its six pre-fix bids, and is now
**stranded below its own resume line with no income but mining**. The guard is behaving exactly as
specified; the specification is what starves the auction. Calibration input for
`PRIVATE_ROADMAP.md` → "Casino-Bot Treasury Policy" — **not a defect to fix inside Step 16.**

**9.6.3 It unblocks itself — no intervention needed.** Six pools already carry **two distinct bot donors**
(`non_miner_1, 9, 14, 15, 17, 18` — bot_4 bid before it went to rest, bot_1 after), so the next founding
out of that set produces a real `spread=`. The three that closed first were precisely the bot_1-only pools,
because every re-bid resets the 20-day window and the uncontested ones had none. Separately, bot_1's
`ownTiersInTarget=2|4` on `non_miner_3` against a 10.50 BTC floor says **the player already leads that
pool** — the fastest route to checks 8/11/12 is to hold it (the player's floor is +1 satoshi, bot_1's is
the full 5–10% band), which founds with two holders and puts the player in NST in one move. Earliest
quarter for checks 2/3 is Blackmarket Reboot at `NextQuarterlyDueMs` ≈ **2012-07-05**.

**9.6.4 Observation, no change made — drift compresses in narrow bands, jitter does not.**
`ComputeBotReserveBallot` applies drift on the bot's own 0–100 scale, projects into the company band, and
*then* adds ±`BallotJitterMaxPoints` (3). At a CB1 company (`[75,100]`) the 18-point momentum term arrives
as ~4.5 points while the jitter stays at 3 — so at CB1/CB5 companies quarter-to-quarter movement will be
**jitter-dominated**, and jitter is deterministic in `(companyId, quarterIndex, botId)`: varying, but not
*responsive*. Check 9 should still pass (81 against a founding default of 100 is a 19-point move). Worth
re-reading once check 9 has real quarterly data; not worth changing mid-run.

### 9.7 Standing reminders

- **Never headless-launch the game to test** — it writes to the real `user://` and can destroy the
  developer's run. `dotnet build` + developer verification, always.
- The P16.6 run produces **no telemetry worth preserving** (unlike P15.8's) — a wipe costs nothing here.
- `bot_wallet_registry.json` is an **identity** file and survives world wipes by design; only its own
  `RegistryFormatVersion` regenerates it.

### 9.8 P16.7 — the two dividend defects, found by audit at block 2126 (2026-07-31)

> Raised by the developer as two plain questions about the run — *"why do some dividends pay no SC?"* and
> *"why does Slush Pool hold 0 SC against a 2% target?"* Both had a single-line cause; neither was
> reachable by reading the P16 diff, because both predate Step 16 and had simply never been *looked at*
> with a company sitting on a low target. **Both fixed and confirmed live at block 2188.**

**9.8.1 A 5% conversion floor that was secretly a floor on the VOTED TARGET.**
`TryConvertCompanyReserves` gated on `deficitSc < totalValueSc × ConversionDeficitTriggerFraction`. Both
sides shared the `totalValueSc` denominator, and the largest deficit that can ever exist is
`totalValueSc × ReserveScPercent/100` (reached at an empty reserve) — so **any company whose board voted a
target below 5% could never clear the gate on any block, forever.** Pinned at 0 SC, and therefore at a
permanently 0 SC dividend too, since `QuarterDividendSc` is a percentage of the reserve.

The run's own data drew the line with no exceptions: Slush Pool at **1.87%** had **zero** `conversion` rows
across its entire life; Laundromat had zero while it sat at 4.51→4.76% and converted in the very block a
vote pushed it to 15%; Coinwash, at **exactly 5.00%**, fired on its first block. Fixed by measuring the
fraction against `targetSc` — *"5% of what this company is trying to hold"*, which is what the chunkiness
intent wanted as its reference all along. **Confirmed:** Slush Pool's first-ever conversion landed at block
2129, seven more followed, and its reserve is now `1,520.51 SC`.

**Rule: when a threshold and the quantity it bounds share a denominator, one of them is probably a
constraint on something you never meant to constrain.** The comment said "5% of total reserve value" and
was accurate about the arithmetic — it just never stated the consequence, which is where the defect lived.

**9.8.2 The dividend was priced before the reserve it reads was filled.**
`CloseCompanyVote` finalizes `QuarterDividendSc = ScReserve × payoutRate%` — a snapshot — but
`TryConvertCompanyReserves` runs in **step 3** of the same `TickCompanyGovernance` pass, i.e. *after*. So
the very vote that first raised a company's target produced a dividend priced off the pre-raise (usually
empty) reserve: a first quarterly of exactly **0 SC**, with the SC arriving one trace line later in the
same block. ArtForz Cluster, block 1955: `vote_close 0.00 → 24.08%, divSc=0`, then `conversion sc=11,029`.
Laundromat, block 1899, identical. Coinwash escaped only by accident — it had already converted at founding.

Fixed by calling the conversion inside the quarterly branch of `CloseCompanyVote`, before the snapshot.
Step 3's call still runs and early-returns on its own gate. **Confirmed:** Slush Pool block 2177 now traces
`conversion` *then* `vote_close` in that order, with `divSc=14.25` — its first non-zero SC dividend ever.

⚠ **Known consequence, accepted:** `QuarterDividendBtc` reads `CompanyOwnBtc` after the top-up has sold
some BTC, so quarterly BTC dividends now come in slightly lower (Slush Pool 36.03 → 31.69). Previously both
sides were priced pre-conversion; now both are post-. Consistent either way, and post- is the more honest
reading of *"the reserve at finalize time"* — but it is a real movement in a figure the run has been
watching, so do not read it as drift.

Neither fix touches a persisted schema ⇒ **no `WorldFormatVersion` bump**, and the live world carried
forward. Existing companies self-heal on their next quarterly.

**9.8.3 ✅ RESOLVED 2026-08-02 (option B, developer's call) — unanimous abstention silently zeroed a whole
quarter's dividend.**
Found while confirming the fixes; **not** caused by them, and **not** fixed. P16.4c (D-16.10) lets a bot
abstain, and `ComputeReserveVoteOutcome` correctly resolves over whoever showed up. But when **nobody**
shows up, `votedWeight` is 0, the resolver's guard is skipped, and `payoutResult` keeps its **initial value
of 0** — so the company pays *no dividend at all*, BTC and SC, for the entire quarter. The reserve % is
preserved; the payout rate is not.

Three occurrences so far, all at single-NST-holder companies where that one bot abstained: Blackmarket
Reboot (blk 1878), Grass Hill Alpacas (blk 1900), Seals with Clubs (blk 2145). The last is the clean
example — `vote_open ballots=0` → `vote_close pay=0.00 divBtc=0 divSc=0`, against `pay=6.50` the quarter
before, with a healthy `5,162 SC` reserve sitting untouched.

**The fix (P16.7c).** `payoutResult` was initialized to a bare `0m` and only ever assigned inside the
`votedWeight > 0` branch. The developer chose **option B — fall back to the category default**
(`DefaultQuarterlyPayoutRatePercent(gov.MarketCategory)`), over holding the previous quarter's rate,
because it is the figure the company was chartered with and the one every bot ballot is already a multiple
of: an unattended company drifts toward its **category norm** rather than freezing whatever the last quorum
happened to pick. Shipped with a `;no_quorum` marker on the `vote_close` trace row (`ballotRecords.Count
== 0`) so the case stays countable instead of reading as a quorum that coincidentally voted the charter
figure. Build clean; no persisted schema change, **no `WorldFormatVersion` bump**.

What made it findable at all is worth keeping: the other two dials on that same screen already defaulted to
something sensible — `reserveResult` to the status quo, `dividendsCutResult` to the 50/50 (D-15.7, which
even documents the no-quorum case in its comment). The payout rate was the **only** one whose no-quorum
answer was zero, and the inconsistency is the tell. **General rule: a resolver's "nobody answered" value is
a DESIGN DECISION, not an initializer — zero is almost never it.** Here it paid a whole quarter's
shareholders nothing while the reserve sat full, which is exactly what sent the developer looking for a
broken dividend engine (§9.8.1/.2 were found in the same sweep).

### 9.9 P16.8 — the player can abstain (2026-08-02, D-16.19)

> An extra phase, built **before** the merge at the developer's request, out of a question asked about
> §9.8.3's abstention data: *should the player be able to sit a vote out too?*

**9.9.1 The measurement that started it.** Across all 159 `vote_open` rows in the P16.6 run:

| NST holders | votes | ≥1 abstention | no quorum |
|---|---|---|---|
| 1 | 80 | 5 (6.3%) | **5** |
| 2 | 51 | 8 (15.7%) | 0 |
| 3 | 28 | 10 (35.7%) | 0 |

Two things fall out. **(a) Every no-quorum event is at a single-holder company** — `BotAbstainsFromVote`
rolls independently per bot at `BotAbstentionPercent = 15`, so an empty ballot box costs `0.15ⁿ` and only
`n = 1` makes it likely. A *"at most one abstainer"* cap — the developer's first instinct — would therefore
have prevented **none** of the five, because those companies had exactly one bot to begin with. **(b) Every
observed rate runs BELOW its theoretical value** (6.3% where 15% is predicted, 15.7% where 27.8% is), and
the reason is the whole phase: the player's forced participation was diluting it.

**9.9.2 What was built.** `OpenCompanyVote`'s player branch had exactly two paths — pause, or cast the
standing policy — so the player was the only holder who could not decline. Now:

- **`CompanyGovernanceState.PlayerAutoAbstain`** (defaults FALSE, D-16.11's reasoning) — a standing
  abstention, checked **before** the pause: pausing the whole simulation to collect a ballot the player has
  already declared they will not cast would spend the pause tax P16.5 removed on an answer of "nothing".
  It is a second field rather than a third state of `PlayerPauseOnVotes` because it answers a different
  question — the pause asks *"should the game stop to ask me?"*, this asks *"do I want a say at all?"*.
- **`NetworkRoot.TryRegisterPlayerAbstention`** — the manual path, an `Abstain` button beside `Submit` at
  both ballot forms (quarterly/special and shortfall). **It writes NO ballot, deliberately** — a
  zero-filled ballot would drag the weighted average toward 0 and pin the reserve to the band floor, which
  is the P15.9 failure arriving through a new door. Removing the entry is what raises every other holder's
  relative weight, matching what a bot's abstention has always done.
- It is the **second writer of `AwaitingPlayerVote`**, and the two are exhaustive: the player either casts
  or declines, and both resume the simulation. An abstention that did not lift the pause would freeze the
  game permanently.
- **Vote Policy panel**: an *"Abstain from every vote at this company"* checkbox that **disables** the
  pause row while on (a control with no effect must not look live), with the explanation swapped to say
  what actually happens to the player's weight. `Follow Status Quo` deliberately does **not** clear the
  abstention — that is a participation choice, not one of the three policy dials.

**9.9.3 The bot rule is UNCHANGED, by decision.** The developer explicitly kept the independent
probability roll rather than the *"a bot may abstain only if another holder is casting"* rule that had been
proposed to make no-quorum structurally impossible. Consequence, accepted and worth stating: **no-quorum
stays reachable and in fact becomes slightly MORE likely**, since the player can now also sit out — which
is precisely why §9.8.3's category-default landed first. P16.7c is the guarantee this phase leans on.

**9.9.4 One readout had to follow** (§39.16 rule 6). The ballot list rendered *"— not voted yet"* for any
holder without an entry, which after this phase is wrong twice over: a **bot's** ballots are all cast the
instant the vote opens, so a missing bot entry has always meant it **abstained** (the old wording merely
predated the player having the same option), and with the standing abstention on the player is not being
waited on at all, making *"(this vote is waiting on you)"* a plain untruth. Now four distinct states:
`— abstained` (bot, or the player manually), `— not voted yet (this vote is waiting on you)` (paused,
undecided), `— abstaining (your standing policy)`.

`PlayerAutoAbstain` rides `BlockchainStateSnapshot` like every other governance field, so checkpoint
coverage, delete-list membership and the pre-genesis path come free (D-16.14). Build clean, **no
`WorldFormatVersion` bump** — a new bool defaulting to `false` reads correctly out of every existing
snapshot, and `false` is exactly the pre-P16.8 behaviour.

**9.9.5 First live test (2026-08-03) — five findings, all shipped fixed as P16.8b–d.** Run at Slush Pool
(standing abstention) and ArtForz Cluster (pause). Full write-up: **`Documentation/ProjectDesignManual.md`
Chapter 41**, which is also the "explain the difference in the manual" the developer asked for.

1. **The defect behind the reported freeze (P16.8b).** An abstention and a not-yet-cast ballot have the
   **same data shape** — no entry in `vote.Ballots` — and the form's guard (`playerVoted &&
   !AwaitingPlayerVote`) distinguished them by exactly that. So after abstaining, the panel re-rendered a
   live Submit/Abstain form as if nothing had happened, and pressing Submit **silently replaced the
   abstention with a real ballot**. Fixed structurally: the form renders only while `AwaitingPlayerVote`,
   making the four post-vote states exhaustive and mutually exclusive. **General rule: when a new outcome
   shares its DATA shape with an existing one, every branch that told them apart by that shape is now
   ambiguous — find them before adding the outcome, not after.**
2. **Abstain is now a TOGGLE, `Submit Ballot` the sole resume axis (P16.8b).** Two buttons meant two ways
   to leave the most fragile state in the project, and the one not pressed stayed live. The toggle states
   an intention (blanks + locks the dials, switches the forecast), Submit resolves it.
3. **The forecast answers the toggle's own question (P16.8b), and shows the SPAN not one sample (P16.8j).**
   `ComputeReserveVoteOutcome` over the ballots *without* the player IS the abstention outcome, so the
   preview shows it alongside the dialled figure (§39.16 rule 6). At the first real vote that pair read
   `85.38%` vs `85.79%` and the developer reported, correctly, that the choice looked irrelevant — the dial
   happened to sit near the other holders' average. Both numbers were true; **one evaluated point cannot
   answer a question about a range.** The line now also states the reach the holding controls (here
   `83.34%–89.01%` on 22.67%), or says plainly when it controls nothing. Both bounds go through the
   resolver, so the band clamp cannot drift from the real one.
4. **The policy dials lock when they cannot steer anything (P16.8c, corrected at P16.8f).** Tick on ⇒
   blanked *and* locked (a stale `24%` in a greyed box reads as a promise). The **second** lock shipped in
   this round — *configured-and-saved ⇒ locked until `Follow Status Quo`* — was **reverted the next round**:
   `configured` is persisted, so the dials came back disabled on every later visit and the only way to edit
   them again was to destroy the policy first. Final rule is one line: **editable ⟺ neither tick is on**,
   applied on build *and* live on toggle. **A control that is read-only on arrival must be re-openable
   without losing work — if the only escape from a lock is discarding what it protects, it is not
   protecting anything.**
5. **Abstentions are now visible (P16.8d).** Only cast ballots were ever recorded, so the developer could
   not confirm their own Slush Pool abstention had happened — and a *bot's* abstention was equally
   invisible despite being the thing that MOVED everyone else's weight. Snapshot now lists who sat out and
   the share they forfeited; Vote History carries the player's participation per vote; auto-cast ballots
   say so. **Derived from `founding.Holdings` vs the record's ballots — no new field, no bump** — which
   works only because stock trading is deferred (D-ND8.21); Ch. 41 §41.5 records that dependency so the
   phase that lands trading finds it.

**9.9.6 Second live pass (2026-08-03) — two reports, neither a defect, one real dead-end.** After a restart
the developer found ArtForz Cluster green (`Claim →`) instead of mocha, and no ballot form inside it, with
betting blocked. Read from `state.json`: **both were correct.** ArtForz already held the player's manual
`reserve=24, autoCast=False` ballot, so no vote was pending there — green is right, and the missing form is
P16.8b working (before it, that page would have re-offered a live form and let the player overwrite their
own ballot). The game was held by **BTC Guild**, whose vote had **zero** ballots: the player had not voted
and bot_2 — its only other NST holder — had abstained on its 15% roll. Its BlockExplorer row was correctly
red with `⚠ BOARD VOTE PENDING`.

The dead-end was real though: **the game freezes globally, only one company can lift it, and nothing on a
governance screen said which.** DiceGame's notice names the company, but by the time someone is navigating
CompanyDetails they have left it behind. **P16.8e** adds a pause locator to `CompanyDetails` in the same red
as the BlockExplorer row (one signal, two places), computed **before** the not-founded / dissolved early
returns so it still shows on pages that render nothing else useful, and worded differently on the blocking
company (confirmation) versus any other (redirection).

**Three polish rounds closed with it (2026-08-03):** **P16.8f**, the dial-lock revert in item 4 above;
**P16.8g**, hiding the Vote Policy panel entirely while this company's vote is holding the game — it
answers *"what should be cast when I'm not here"*, which is not the question being asked at the moment the
simulation stopped to ask in person, and its greyed dials sat immediately above the live ballot where the
eye lands on them first; and **P16.8h**, three rules the panel needed before it was coherent, each of them
a state the panel could DISPLAY but the engine could not HOLD (full write-up: Ch. 41 §41.4b):

- **The ticks are mutually exclusive, and (P16.8i) the exclusion is SYMMETRIC.** Two attempts: the first
  *disabled* the Pause box while leaving it visibly ticked, so Save wrote `pause=true` alongside
  `abstain=true` — **disabling a stale control hides a contradiction; clearing it removes one**. The second
  cleared Pause but kept disabling it, which made switching a **one-way door** (Abstain always pressable,
  Pause not), reported after two switches that worked and a third that could not be made. Both boxes are
  now always clickable and each clears the other. **Modelling precedence between two options by disabling
  the loser only works when the player is never meant to pick it; if it is a choice, make them exclusive.**
- **The prose is re-derived, not built once.** Both explanatory lines were composed from the persisted
  flags at build time and never updated, so toggling a box left the panel explaining the previous state.
  Everything the tick state governs now re-derives in one `SyncPolicyControls()`, so no path can update
  half the panel.
- **Only `Save Policy` writes.** `Follow Status Quo` was the one control that wrote on its own press,
  reporting *"Policy cleared"* for something the player had not confirmed. It now resets the dials and arms
  a `pendingClear` that Save turns into the `-1` sentinels — keeping the sentinel matters, because a
  not-configured policy keeps FOLLOWING the company as its numbers move while one configured at today's
  number is pinned to it. It is also disabled while a tick is on (nothing to reset to).

**9.9.7 ✅ P16.8 CLOSED — verified live 2026-08-03/05 (Casascius special vote, 2013-10-08).** Every item
confirmed by the developer in the running world:

- **Toggle + single resume axis** — Abstain ticks, `Submit Ballot` resolves it, the game resumes, and the
  panel replaces the form with *"You abstained from this vote"*. **The Vote Policy panel returns after
  submit** (P16.8g's other half), confirmed explicitly.
- **Pause locator (P16.8e)** — *"⚠ The game is paused for THIS company's board vote"* shown in the
  BlockExplorer red, on the blocking company's own page.
- **Ballot forecast (P16.8b/j/k)** — the three-pass label, final layout accepted.
- **Policy panel (P16.8f/h/i)** — dials editable on arrival, greyed only while a tick is on, symmetric
  mutual exclusion with both boxes always clickable, `Follow Status Quo` disabled under a tick, and no
  write except through `Save Policy`.
- **Abstention record (P16.8d)** — Casascius' Vote History reads `| you abstained` for the four quarters
  under the standing policy and `| you voted 75% (policy)` before it, with the switch visible exactly where
  it was made.

Also observed in passing, both healthy: bot_1 cast **82%** against bot_4's **92%** at the same company (a
10-point spread — P16.4 doing its job, where pre-P16.4 they would have been identical), and the special
`>30%-inflow` vote fired ahead of the scheduled quarterly, as D-ND8.18 specifies.

**Nothing in Step 16 is now unverified.** The only remaining action is the merge chore below.

