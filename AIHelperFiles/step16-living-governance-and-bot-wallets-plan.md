# Step 16 — Living Governance & Full UTXO Participation

> **Status: DESIGN LOCKED (Rounds 1–2 complete, 2026-07-30) — READY TO IMPLEMENT.** All five Round-2
> questions are resolved (`D-16.14…18` in §6); `D-16.1…13` carry Round 1. Subphases are broken out in **§7
> (P16.1a … P16.6)**; suggested build order **P16.2 → P16.1 → P16.4 → P16.5 → P16.3 → P16.6**. One design
> is recorded as **deliberately open** and constrains this step only by a single discipline: the **ghost
> typology** (§6.1) — P16.2 must key every decision off *"does this record carry a seed?"*, never off *"is
> this a ghost?"*.
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

### P16.4 — Living ballots

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

### P16.5 — Pause toggle + standing policy

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

### P16.3 — Wallet scene split (last of the build work)

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
