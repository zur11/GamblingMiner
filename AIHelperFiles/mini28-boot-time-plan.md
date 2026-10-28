# Mini-Plan 28 — The boot time: attribute the minute, then remove what is repeated

**Series note:** twenty-eighth of the *mini-plan* series. It takes `PRIVATE_ROADMAP.md` ▶ NEXT **item 2b** and §8
**T4.7**, inserted by the developer on 2026-10-22 after mini-plan 27 measured the boot for the first time
(`mini27-user-settings-plan.md` §6.4).

**Status:** ✅ **COMPLETE 2026-10-23** (§9). **The boot went from 57.9 s to 8.6 s.** The record of how it got there
follows. Specified 2026-10-22. Unit 1 (§4) built 2026-10-23 (`c5b0e6d`). **M1 run
2026-10-23 (§6.1): P2 refuted.** The minute is the derived-wallet rescan (82.8%), not the per-node validation
(15.8%). §5A amends unit 2 (approved, `0a39a5a`), with **F-3 added at the developer's choice**: cast miners'
change goes back to their base address. **Offline harness run (§5A.6): P7 and P8 hold.** **Unit 2 built
2026-10-23 (§5A.7, `8912ae7`): F-2 + F-1 + F-3 + the rescan counters + E3.** **M2 run 2026-10-23 (§6.2): P6–P8 and
E1–E3 hold; the boot is 57.9 s → 8.6 s (E4).** **M3 run 2026-10-23 (§6.3): P9 refuted as worded (8 pre-F-3
mempool transactions), holds for every spend created under F-3.** Closed out 2026-10-23 (§9).

**Branch:** `mini28-boot-time` (proposed, D-28.1).

**World treatment: no bump, no wipe.** R3's world (v12, ~2016-06, height 3,968, the player at 10 credits) is the
subject, because its size is the cost. *If a wipe is ever proposed, `user://logs/rank_calibration_trace.csv` is
renamed `rank_calibration_R3.csv` first.*

**Prior art this plan consumes rather than re-derives:**
- Mini-plan 26 §12.3, lesson 3: in a simulated network every per-node check runs once per node, so ask whether its
  answer can differ between nodes, and if not, do it once. Its F-a (verify a signature once per broadcast) is the
  shape of this plan's fix.
- CLAUDE.md Pattern 6, rules 3 and 4: a millisecond figure does not reproduce across sessions, so a cost criterion
  is stated as a **count** and compared **within a session**; a structural reading says what happens, only a
  measurement says what it costs.
- `ProjectDesignManual.md` §40.7: time it, or say plainly that you did not.

---

## 1. Why this plan exists

Mini-plan 27 measured the boot with `BootProbe` (DEBUG; ms since the engine started):

| | Without a rebuild (E) | After a rebuild (D) |
|---|---|---|
| Engine start → first autoload | 2.9 s | 5.5 s |
| **Autoloads #2–#19** | **54.7 s** | **61.8 s** |
| Everything else to MainMenu | 0.1 s | 0.2 s |
| MainMenu ready / the developer's stopwatch | 57.7 s / 59.4 s | 67.6 s / 77.3 s |

**95% of the boot lies inside autoloads #2–#19, and nothing in that stretch is timed.** It is paid at every launch,
and every test protocol is several launches: mini-plan 27's verification alone took six.

## 2. What the code says (read 2026-10-22, not timed)

**Who loads the world.** Autoload #3, `CalendarTimeService._Ready`, calls `HistoricalBootstrapService.RunIfFirstLaunch`,
which calls `NetworkRoot.EnsureReady` → `EnsureInitialized`. The log agrees: the world's lines
(`[CompanyRoster]`, `[Governance] … restored with the world`) appear between #3's wallet lines and #7's.

**What `EnsureInitialized` does, in order:**
1. `TryLoadSnapshot`: reads and deserializes `state.json` (2.0 MB).
2. Registers every node: the player, `bot_1`…`bot_4`, the non-miners and cast miners with a full wallet, the casino
   and three founders. Each costs key derivations (`CryptoUtils.DeriveSigningKeypair`, the secp256k1 public key).
   Mini-plan 26 measured key derivation as one of the dearer per-block costs.
3. `ApplyStateFromSnapshot`: reads `chain.jsonl` (22.5 MB) once, then **for every registered node** calls
   `node.Blockchain.TryReplaceChain(chain, pending)`, and **each call runs `ChainIsValid` over the whole chain**:
   - recompute every transaction id (`ComputeTransactionId`: a string built from inputs and outputs, then two
     SHA-256 passes);
   - recompute every Merkle level;
   - hash every header and parse it as a `BigInteger` against the difficulty target.
   R3's chain: **3,968 blocks, 19,506 transactions** (counted from the file, 2026-10-22).
4. `NormalizeGenesisAcrossNodes`, `EnsureSecondBlockBootstrapPendingTx`.
5. `RescanDerivedReceiveWallets`: every node's derived wallet rescans the chain for its used addresses.
6. `EnsureBotGovernancePreferences`, then `PersistWorldNow`, which writes the world once at boot.

**The structural reading:** `ChainIsValid` runs once per node on the **same list**. Its answer cannot differ between
nodes, so all but one of those validations are redundant. With about 71–73 nodes (mini-plan 26's traces show 71 UTXO
rebuilds for 71 nodes), one validation would have to cost about 0.5–0.75 s for the loop to explain the minute.
**That is a hypothesis.** The instrument (§4) decides it.

## 3. The predictions (registered before any data)

| # | Prediction | How it is read |
|---|---|---|
| **P1** | Autoload **#3, `CalendarTimeService`**, holds **≥ 90%** of the time between the first and the last autoload | the per-autoload marks |
| **P2** | Inside `EnsureInitialized`, the **per-node `TryReplaceChain` loop holds ≥ 70%** of its time | the marks inside `EnsureInitialized` |
| **P3** | **`ChainIsValid` runs N times at boot, where N = the registered node count**, and N is 71 ± 3 | two counters printed at boot |
| **P4** | **One `ChainIsValid` costs about the same on every node**: the slowest and fastest are within 2× of the median | the per-call min/median/max |
| **P5** | **No other step of `EnsureInitialized` exceeds 10%** of it (load, registration, rescan, governance, persist) | the marks |
| **V1** *(self-check)* | N × median `ChainIsValid` reproduces the loop's measured total within 10%. If it does not, the loop holds work the counter does not see, and P2's reading is not trusted | arithmetic on M1's own lines |

## 4. Unit 1 — the instrument (Part A)

- **`BootProbe` gains a mark at the start of every autoload's `_Ready`** (20 one-line calls, `[Conditional("DEBUG")]`,
  compiled away in an export). Sequential autoloads make "the start of the next" the end of the previous, so 20
  marks time 19 spans. The span of #19 ends at #20's start mark, which exists since mini-plan 27.
- **Marks inside `EnsureInitialized`** at each of §2's six steps, with the counts beside them: nodes registered,
  blocks, transactions.
- **A `ChainIsValid` counter and timer** (DEBUG): calls, total, min / median / max per call, printed once when
  `EnsureInitialized` ends. It counts every call, not only the boot loop's, so a call from elsewhere shows up.
- Everything prints with `GD.Print`, so `godot.log` receives it. The developer watches nothing.

**As built (2026-10-23).** `BootProbe.AutoloadStarts(index, name)` is the first line of every autoload's `_Ready`,
indexed by its position in `project.godot`; #1's and #20's mini-plan 27 start marks became the same call, and their
"done" marks stay. `SceneManager` (#14) and `FoundersMiningService` (#16) had no `_Ready` and gained one holding only
the mark. Inside `EnsureInitialized`, ten `world: …` marks: start · self-check + guard + `state.json` · nodes
registered (count) · `chain.jsonl` read (blocks, transactions) · chain checked against the state stamp · **the
`TryReplaceChain` loop** (node count) · rest of the snapshot applied · genesis + second-block tx · rescan ·
governance · persisted. Each mark's `+ms` is its step. `ChainIsValid` is now a timed wrapper over the unchanged
`ValidateChain`; `PrintChainValidationSummary` runs once, right after the "persisted" mark. Build: Debug and
ExportRelease 0 warnings; locale detector 17/0/0/0, the 17 identical by content to `09cdb10`'s.

## 5. Unit 2 — the fix (Part B), conditional on M1

> **⚠ Superseded by §5A (2026-10-23).** M1 refuted P2: the `TryReplaceChain` loop holds 15.8% of the world load, and
> the derived-wallet rescan holds 82.8% (§6.1). The rule below therefore built nothing from its own text. It is
> kept as specified, because §5A reads against it. F-1 survives inside §5A as one of two fixes, now on measured data.

**Decision rule, applied to M1's data before anything is built:**
- **If P2 holds** (the loop ≥ 70%), or the loop is still the largest single step at ≥ 50%: build **F-1**.
  `ApplyStateFromSnapshot` validates the chain **once**, and every node is given the chain through an install path
  that takes that verdict instead of recomputing it. This is the F-a shape: the work whose answer cannot differ
  between nodes runs once. **Every other check stays per node**: the length comparison, and the pending-transaction
  copy. A chain that fails validation is still refused, everywhere, exactly as now.
- **If P2 is refuted** (the loop below 50%): **no fix is built from this plan's text.** §5 is re-specified as an
  amendment from M1's attribution, with its own predictions, and goes back to the developer first. A cause named from
  the code is a hypothesis until timed (mini-plan 26 §12.3, lesson 1).

**F-1's exit criterion, as counts first (Pattern 6, rule 3):**
- **E1:** `ChainIsValid` runs **once** at boot (N → 1).
- **E2:** the world loads to the **same state**. The tip height and hash and every node's chain length are equal to
  M1's, and the post-boot world write is byte-identical to M1's. *(R3's world does not change between two
  boots that mine nothing, so M1's file is the oracle.)*
- **E3, the time, within one session:** a boot cannot be A/B'd inside a session, so M2 adds a DEBUG-only
  measurement. After the new path has installed every chain, the boot also times **N extra `ChainIsValid` calls**,
  the old path's work, and prints both figures from the same process. The new path must cost **≤ 10%** of the old,
  measured side by side. The switch that runs the extra calls is a constant, deleted at close-out with its output
  (Convention 3).
- **E4, what the developer feels:** MainMenu ready since the engine started, reported but **not** a pass/fail figure,
  because it does not reproduce across launches. Predicted: below **15 s** without a rebuild.

## 5A. Unit 2, amended from M1's attribution (2026-10-23) — approved, with F-3 added at the developer's choice

### 5A.1 What M1 found, and what the code says about it

**The minute is the rescan, not the validation.** `RescanDerivedReceiveWallets` took **44,746 ms of 54,044**
(§6.1). It does two things (read 2026-10-23, the split is not timed):
1. `BuildUsedAddressSet`: one pass over the chain collecting every address. That is 36,094 outputs and 26,707
   inputs in R3's chain, about the same data the `chain.jsonl` read parses in 424 ms. Expected cost: tens of ms.
2. For each of the 73 wallets, `DerivedAddressWallet.Rescan` derives addresses by index, one after another, until
   it meets 20 unused in a row. It derives `lastUsed + 21` addresses in all. **Each derivation is one secp256k1
   scalar multiply** (`Secp256k1.GetCompressedPublicKey` → `ScalarMul`: about 256 doublings and 128 additions on
   `BigInteger`s). Step 16 measured it at **4.2 ms**.

**Why the Step 16 cost note ("~8 s") is now 44.7 s.** That note counted about 1,900 derivations: about 21 per
wallet, plus Satoshi's ~220 coinbases. But every coinbase and every change output takes a **fresh** address, so the
count is the number of addresses the world has ever used. It grows with the world's history, and nothing re-checked
it. R3's chain (~2016) holds **15,750 distinct addresses** (counted from `chain.jsonl`, 2026-10-23, read-only).
15,750 + 73 × 21 ≈ 17,000 derivations at the implied **≈ 2.6 ms** each is the 44.7 s. *That last step is arithmetic,
not a measurement; P6 tests it.* The lesson, in the register of `ProjectDesignManual.md` §40.7: **a cost that scales
with the world's history must be written per unit of history, never as a figure.** A figure was true on the day it
was typed, and it stays plausible after it has stopped being true.

**The `TryReplaceChain` loop is now measured and is the second-largest step:** 73 calls, 8,562 ms, median 113 ms.
Validating once would save about 8.4 s. F-1 is therefore built as well. Its cause is timed now, not inferred from
the code.

### 5A.2 The two fixes

- **F-2, fixed-base scalar multiplication.** Every derivation in the game multiplies the **same** point, the
  generator G. `ScalarMul` is called only from `GetCompressedPublicKey`, and always on G. So G's multiples can be
  computed once into a table: `d · 16^j · G` for the 64 four-bit windows of the scalar, d = 1…15, 960 points. Then
  `k · G` is the sum of one table entry per nonzero window: **at most 64 additions and no doublings**, against
  ~256 doublings and ~128 additions now. The table is built once per process on first use. The curve arithmetic is
  the same standard formulas, so **the output is bit-for-bit identical, and the derived addresses do not change. No
  format bump, no wipe.** This is the same claim Step 16 made for the Jacobian change, and it is verified the same
  way (below).
  *Edge cases, from the arithmetic:* every partial sum is a scalar below the next window's term and below N, so an
  addition never meets the doubling case or the point at infinity. The existing `JacobianAdd` handles both anyway.
- **F-1, validate once**, exactly as §5 specified it. `ApplyStateFromSnapshot` runs `ChainIsValid` once, and each
  node installs the chain through a path that takes that verdict. The length check and the pending copy stay per
  node, and an invalid chain is still refused everywhere.

- **F-3, cast miners send their change back to their base address** *(added 2026-10-23 at the developer's choice,
  option 2, after the attribution below)*. F-2 makes each derivation cheaper. F-3 stops the **count** from growing,
  without which F-2 only postpones the next minute.
  **Who makes the addresses** (R3's chain, ownership followed from the miners' coinbases, read-only): of 15,750
  distinct addresses, **15,398 are change**. By sender: **cast miners 10,924 (69%)**, all from `TryCastSellFlow`
  (the historical volume filler: real transactions per block ÷ 100, capped at 23 slots), companies 3,772 (24%),
  the four miner bots 700 (4%), founders 2. Satoshi's coinbase rotation is inside the 309 coinbase/system
  addresses. **14,206 addresses were received once and spent once.** New change addresses per year: 2010 → 91,
  2012 → 1,258, 2013 → 2,651, 2015 → 4,763. At full blocks the ceiling is 23 × ~540 blocks ≈ 12,400 per in-game
  year, every one of them paid again at every boot.
  **The change:** `NodeAgent` gains `RotateChangeAddress` (default `true`) beside `RotateCoinbaseAddress`, and the
  three change-producing send paths ask the node for its change address through one method. Cast miners, both at
  boot and when registered mid-session, are set to `false`. They **keep** their `DerivedAddressWallet`: it still owns
  and signs the 10,924 addresses already on the chain, so nothing already mined changes owner. Companies, the
  player, the casino, the founders and the miner bots rotate exactly as before. **No format bump, no wipe:** blocks
  already mined are untouched, and only change created after the change differs.
  **What the player will see:** a cast miner's sell shows two outputs in the Block Explorer, the payment and the
  change back to the sender's own base address. That is a real output of a real spend, and common in that era. It
  is shown, **never filtered**: CLAUDE.md's rule against the deleted change-to-self filter stands, and its premise
  ("every participant rotates") is reworded in this unit, as is §29.9.
  **Projection, not a measurement:** with F-3 the yearly growth falls to the companies' and bots' share, about a
  quarter of the above.

**Declined or deferred, with reasons:**
- **Running the 73 rescans in parallel:** deferred. The instrument (below) prints the largest wallet's share, so
  if E4 misses, that decision has its data.
- **Persisting each wallet's frontier/addresses in `state.json`:** declined. It would be a stored copy of a derivable
  value (Convention 1: it must never diverge), it would grow a file written at every block, and it changes the world
  format, which §7 excludes.
- **Lowering `DefaultGapLimit` (20):** declined. It is the safety margin against a gap in the address sequence. 73 ×
  20 = 1,460 derivations is ~9% of the work, and F-2 makes those cheaper rather than fewer. Its comment ("~20 SHA256
  derivations") is a stale cost note. It is corrected in this unit (Convention 15), as is the Step 16 note above
  `RescanDerivedReceiveWallets`.
- **Fixed-width limb arithmetic instead of `BigInteger`:** deferred. It is the next lever if F-2 is not enough, and
  it is far larger and riskier.

### 5A.3 Predictions (registered before the offline harness and before M2)

| # | Prediction | How it is read |
|---|---|---|
| **P6** | The rescan's work is the derivations. At boot it derives **D** addresses with **14,000 ≤ D ≤ 17,300**, `BuildUsedAddressSet` costs **< 2%** of the rescan, and the derivations **≥ 95%** | new DEBUG counters in the rescan (M2) |
| **P7** | One F-2 derivation costs **≤ 25%** of the current one, measured side by side in one process over the same seeds | the offline harness first, then M2's E3 |
| **P8** | F-2 is output-identical: **0 mismatches** | the offline harness (Step 16's vector shapes), then every boot derivation in M2 |
| **P9** | After F-3, blocks mined in play carry **0 new cast-miner change addresses**, and cast-miner sells with change pay it to the sender's base address. Companies and miner bots **still create new change addresses (> 0)**, so the rotation that should remain did remain (Convention 11) | M3: Claude reads the chain's new blocks with the ownership walk above |

### 5A.4 Exit criteria (counts first)

- **E1:** `ChainIsValid` runs **once** at boot (73 → 1).
- **E2:** the same world. Tip height and hash and every node's chain length equal M1's. **The post-boot `state.json`
  is byte-identical to M1's** (oracle copied out at M1, sha256 `3bde89f9…`; `chain.jsonl` `6bb5ed6f…`). In M2's
  side-by-side launch, every address the rescan derived is re-derived by the old path: **0 mismatches** out of D.
- **E3, the time, side by side in one launch:** behind a DEBUG constant, the boot also runs the old paths after the
  new ones: 73 extra `ChainIsValid` calls, and the D derivations through the old `ScalarMul`. It prints both figures
  from the same process. Pass: F-1 **≤ 10%** of the old loop; F-2 **≤ 25%** of the old derivations (P7). The constant,
  the old `ScalarMul` kept as a DEBUG reference, and their output are deleted at close-out (Convention 3).
- **E4, reported, not pass/fail:** MainMenu ready since the engine started, launch 2. Predicted **< 20 s**, against
  M1's 57.9 s: the rescan near 44.7 × 0.25 ≈ 11 s at most, the loop ≈ 0.1 s, everything else ≈ 4.5 s as measured.
- **E5 (F-3), read after a play run (M3):** P9. F-3 cannot be seen in a boot that mines nothing, so it does not
  change M2's E2: the post-boot `state.json` is still byte-identical to M1's.

### 5A.5 Order

1. **Offline harness** (scratchpad, never in the repo): a console project compiling the current `Secp256k1.cs`
   next to the F-2 version. It checks identity over Step 16's vector shapes (seed-derived keys, the `" #r{i}"` and
   `"sign:"` seeds, the empty seed, the edge scalars 1, 2, 3, 255, N−2, N−1) plus a few thousand more, and times
   both. P7 and P8 are read here first. **If either is refuted, F-2 is not built** and this section returns to the
   developer.
2. Unit 2 in the repo: F-2, F-1, F-3, the rescan counters, the E3 switch, the two stale cost comments, and the
   CLAUDE.md / §29.9 rewording. Builds, the locale detector and the tripwire, stage, approval.
3. **M2**: the same two launches as M1. The second runs E3, so it is about a minute longer, once.
4. **M3**, after M2 has been read: a play run long enough to mine **≥ 20 blocks** in R3's full-block era, then
   Exit. It **advances R3's world**, which is why it comes after M2's byte comparison. Claude reads P9 from the new
   blocks. The protocol, with every setting named, is given when M3 is due.

### 5A.6 The offline harness (2026-10-23) — P7 and P8 hold; the design measured into its final shape

A console project in the scratchpad compiled the current `Secp256k1.cs` (renamed `Secp256k1Old`) beside the F-2
copy, in one process. **Identity:** 7,422 vectors, made of 300 seed-derived keys, 2,103 `" #r{i}"` seeds (three
bases × indices 0…700), 3 `"sign:"` seeds, the empty seed, 13 edge scalars (1, 2, 3, 15, 16, 17, 255, 256, 2¹²⁸,
2²⁵⁵, 15·16⁶³, N−2, N−1), one scalar with every nibble nonzero, one with every nibble F below N, and 5,000 random
keys. Plus the k=1 known answer. **Timing:** the boot's shape (2,000 `" #r{i}"` keys), old and new interleaved in
blocks of 200, three rounds, 6,000 calls each.

| Variant | Mismatches | Table, once | Old ms/call | New ms/call | New / old |
|---|---|---|---|---|---|
| 4-bit windows (as §5A.2 specified), Release | 0 | 25 ms | 2.399 | 0.581 | **24.2%** |
| 4-bit windows, Debug | 0 | 27 ms | 2.498 | 0.584 | **23.4%** |
| 8-bit windows, Debug | 0 | 206 ms | 2.581 | 0.417 | 16.1% |
| **8-bit windows + Euclid inversion, Debug** (built) | **0** | 185 ms | 2.846 / 2.923 / 2.889 | 0.255 / 0.252 / 0.261 | **8.9% / 8.6% / 9.0%** (three runs) |

- **P8 holds:** 0 mismatches in every variant. The DEBUG reference kept for E3 also matches the old copy on all
  7,422 vectors.
- **P7 holds** for the design as specified (≤ 25%), **by a margin of under 2 points.** That is why two refinements
  were measured before building. The one built is ~2.6× better than the specified one.
  - **8-bit windows:** 32 additions instead of 64, from an 8,160-point table built once in ~0.2 s.
  - **Extended-Euclid inversion:** measured separately at **0.023 ms against `ModPow`'s 0.223** per inverse, 3,000
    inputs, 0 disagreements. Left in place, the single inversion was over half of the 8-bit path's cost.
  - The old `ScalarMul` keeps `ModPow`, so the E3 reference is the untouched old path.
- **The old per-derivation cost, 2.4–2.9 ms, agrees with the 2.6 ms M1 implied** from 44.7 s ÷ ~17,000. That
  supports P6's count before M2 measures it.
- **Expectation after the harness** (this is not a re-registration; E4's < 20 s stands as registered): ~17,000 ×
  0.25 ms ≈ 4.3 s of rescan + 0.19 s of table + ~0.1 s of validation + ~4.5 s of everything else ≈ **9–10 s**.

### 5A.7 Unit 2 as built (2026-10-23)

- **F-2, `Secp256k1`:** `GetCompressedPublicKey` computes `ScalarMulG` from a lazily built `GTable` (`d·256^j·G`,
  32 × 255 affine points, Jacobian build + one batch inversion). It uses mixed addition (`JacobianAddAffine`,
  madd-2007-bl) and `JacobianToAffineByEuclid`. `ScalarMul` survives only behind the DEBUG
  `GetCompressedPublicKeyByDoubleAndAdd`. The Step 16 performance note now carries mini-plan 28's correction.
- **F-1, `BlockchainService`:** `TryReplaceChainWithVerdict(chain, pending, chainIsValid)`. It shares
  `InstallChain` with `TryReplaceChain`, whose behaviour is unchanged. `ApplyStateFromSnapshot` validates once and
  installs on the verdict.
- **F-3, `NodeAgent` / `NetworkRoot`:** `RotateChangeAddress` (default `true`), `NextChangeAddress()`, and
  `OnChangeAddressUsed()`. The three change-producing send paths (generic, casino, company dividends) use them.
  `ApplyCastMinerChangePolicy` runs at both cast-miner registration points: the boot loop and
  `RegisterCastMinerNode`.
- **Instrument:** `RescanDerivedReceiveWallets` reports its chain pass and each wallet's derivations
  (`DerivedAddressWallet.DerivedAddressCount`) to `BootProbe`, which prints one `[Boot] Rescan:` line. It is kept
  after close-out (D-28.7).
- **E3:** the DEBUG const `BootProbe.CompareOldPathsAtBoot = true`. `NetworkRoot.RunBootSideBySide` runs after the
  world write and prints `[Boot] E3 F-1:` and `[Boot] E3 F-2:`. The second line is also E2's identity count
  (`DerivedAddressWallet.CountMismatchesAgainst` over `CryptoUtils.DeriveGmAddressByDoubleAndAdd`). All of it goes
  at close-out.
- **Stale cost notes corrected (Convention 15):** `DefaultGapLimit` ("~20 SHA256") and the Step 16 note above
  `RescanDerivedReceiveWallets` ("~8 s").
- **Docs:** CLAUDE.md's change-to-self rule now says cast miners send change to their base on purpose, shown as a
  real output (+70 characters, file at 86,948). §29.9 gained the F-3 paragraph.
- **Convention 13 sweep**, for the retired premise "change never lands on the sender's base". No reader classifies
  an output as a payment by comparing it to a base address. The wallet history counts the base as owned, so a
  change-to-base output is netted out of "sent". The one self-change skip covers founders' scripted events only.
- **Checks:** Debug and ExportRelease 0 warnings, 0 errors. Locale detector 17/0/0/0, the 17 identical by content to
  `09cdb10`'s. Wipe tripwire 0. Every added `.cs` line is tab-indented.

## 6. Measurement — protocols (the developer runs them; watches nothing; Claude reads the files)

**M1, after unit 1** (the code unchanged, only instrumented):
1. Launch from the editor, wait for the main menu, then **Main Menu → Exit Game → Exit**. *(This first launch rebuilds
   the C# project and is not used for the figures.)*
2. Launch again, wait for the main menu, and Exit Game the same way. Tell me "M1 done", and, if you timed it, the
   stopwatch figure.
3. **I** read `godot.log` from launch 2, plus launch 1 from its rotated copy, and copy both out of `user://logs/` as
   `M1_boot_*.log` in the scratchpad.

**M2, after unit 2** (now §5A's unit 2, F-2 + F-1 + F-3): the same two launches. `CompareOldPathsAtBoot` is a
constant, so **both** launches run E3's side-by-side after the world write, each about 50 s longer (≈ 17,000 old
derivations × ~2.6 ms + 73 validations × ~113 ms). Launch 1's figures are a second E3 reading. E4 is read from
launch 2's "world: persisted; EnsureInitialized ends" mark plus what follows E3, i.e. MainMenu minus the E3 span.

### 6.1 M1 results (2026-10-23, R3's world, read from `godot.log` by Claude)

Launch 1 rebuilt the C# project (log rotated to `godot2026-10-23T08.35.39.log`). Launch 2 is `godot.log`, 08:36.
Both are copied to the scratchpad as `M1_boot_launch1.log` / `M1_boot_launch2.log`. The developer timed launch 2 at
"about a minute", not precisely. Figures are from **launch 2**; launch 1 shows the same split (in brackets).

| Span (launch 2) | ms | Share |
|---|---|---|
| Engine start → autoload #1 | 3,427 | |
| Autoloads #2 → #20 start | 54,409 | 100% of the autoload stretch |
| … of which **#3 `CalendarTimeService`** | **54,076** | **99.4%** |
| … of which all 17 others together | 333 | 0.6% (largest: #13 checkpoint 120, #2 stats 61) |
| **`EnsureInitialized`**, inside #3 | **54,044** | 100% of the world load |
| … self-check + guard + `state.json` (2.0 MB) | 126 | 0.2% |
| … 73 nodes registered | 76 | 0.1% |
| … `chain.jsonl` read (3,968 blocks, 19,506 transactions) | 424 | 0.8% |
| … **the `TryReplaceChain` loop**, 73 nodes | **8,564** [8,729] | **15.8%** |
| … rest of the snapshot, genesis, governance | 28 | 0.1% |
| … **derived receive wallets rescanned** | **44,746** [49,295] | **82.8%** |
| … world persisted | 82 | 0.2% |
| MainMenu ready since engine start | **57,912** [62,392] | |

`ChainIsValid`: **73 calls, 8,561.9 ms total, per call min 97.6 / median 113.0 / max 203.3 ms** [73; 8,726.9;
103.0 / 113.8 / 208.0]. No call came from outside the loop: 73 calls against 73 nodes.

| # | Verdict | The reading |
|---|---|---|
| **P1** | ✅ **holds** | #3 holds 99.4% of the autoload stretch (≥ 90%) |
| **P2** | ❌ **refuted** | the loop holds 15.8% of `EnsureInitialized` (predicted ≥ 70%). It is not even the largest step; the rescan is, at 82.8% |
| **P3** | ✅ holds | N = 73 calls = 73 registered nodes, inside 71 ± 3 |
| **P4** | ✅ holds | max / median = 1.80, median / min = 1.16, both within 2× |
| **P5** | ❌ **refuted** | the rescan holds 82.8% (predicted no other step > 10%) |
| **V1** | ✅ holds | 73 × 113.0 = 8,249 ms against the loop's 8,564 ms: −3.7%, within 10%. The counter's own total is 99.98% of the loop |

**§5's rule applied:** the loop is below 50%, so F-1 is not built from §5's text. §5A re-specifies unit 2.
**The plan's structural reading was correct about the loop and wrong about where the minute is.** The loop runs N
redundant validations exactly as §2 said (P3, P4 and V1 all hold). It costs 8.6 s, not the minute. The step §7 had
explicitly put out of scope ("key derivation and rescans, unless M1 refutes P5") is the one M1 named.

**E2's oracle**, copied out after launch 2: `state.json` (2,028,773 bytes, sha256 `3bde89f98467ae50…`) into the
scratchpad's `M1_oracle/`, and `chain.jsonl` hashed (`6bb5ed6f83138a78…`, unchanged since 2026-10-20). `state.json`
holds no wall-clock field, so a byte comparison is meaningful.

### 6.2 M2 results (2026-10-23, R3's world unchanged, read from `godot.log` by Claude)

Launch 1 rebuilt (rotated to `godot2026-10-23T12.31.17.log`); launch 2 is `godot.log`, 12:32. Both are copied to the
scratchpad as `M2_boot_launch1.log` / `M2_boot_launch2.log`. The developer timed launch 2 at **exactly 1 minute**.
That is the boot **plus E3's 49.6 s** side-by-side, which only this plan's measurement runs. Figures are from
**launch 2**; launch 1 is in brackets.

| Span (launch 2) | M1 | **M2** |
|---|---|---|
| Engine start → autoload #1 | 3,427 | 2,613 |
| `state.json` + guard | 126 | 126 |
| 73 nodes registered | 76 | **241** *(includes building F-2's table on the first derivation; the harness measured 185 ms)* |
| `chain.jsonl` read | 424 | 370 |
| **The install loop** (F-1) | **8,564** | **224** [247] |
| **The rescan** (F-2) | **44,746** | **4,486** [4,617] |
| World persisted | 82 | 70 |
| **`EnsureInitialized`** | **54,044** | **5,544** |
| E3 side-by-side (M2 only) | — | 49,635 [48,556] |
| MainMenu ready since engine start | **57,912** | 58,230 − 49,635 = **8,595** [59,378 − 48,556 = 10,821] |

**The rescan line:** chain pass 11.6 ms (**0.3%** of the rescan, 15,750 used addresses); 73 wallets derived
**17,164** addresses in 4,472.8 ms (**0.261 ms** each); largest wallet `artforz`, 687 derivations (**4.0%**),
148.5 ms. **E3:** F-1 = new path 1 validation, 222.7 ms, against 73 old validations, 8,042.2 ms. F-2 = 17,164
derivations at 0.261 ms against **2.423 ms** each by the old path, **0 mismatches of 17,164**.

| # | Verdict | The reading |
|---|---|---|
| **P6** | ✅ holds | D = **17,164**, inside 14,000–17,300 (near the top: almost every chain address belongs to a wallet, and 73 × 21 = 1,533 are base/gap). Chain pass 0.3% (< 2%); derivations 99.7% (≥ 95%) |
| **P7** | ✅ holds | **10.8%** [11.4%] per derivation in-game (≤ 25%). The harness said 8.6–9.0%; in-game the old path is the cheaper side of its range, 2.42 ms against 2.85 |
| **P8** | ✅ holds | **0 mismatches of 17,164** in each launch, in-game, on R3's real wallets |
| **E1** | ✅ holds | `ChainIsValid`: **1 call** at boot (73 → 1) |
| **E2** | ✅ holds for what is persisted; per-node lengths now counted | the post-boot `state.json` is **byte-identical** to M1's oracle (`3bde89f9…`), `chain.jsonl` unchanged (`6bb5ed6f…`), no integrity line in either log. The per-node chain lengths were **not counted** by the instrument; it inferred them from the one verdict. The install mark now prints "installed on X of N" (added with these results), so M3's boots count it |
| **E3** | ✅ holds | F-1 **2.8%** [3.0%] of the old loop (≤ 10%); F-2 **10.8%** [11.4%] (≤ 25%) |
| **E4** | ✅ **8.6 s** [10.8 s on the rebuild launch] | predicted < 20 s; the harness expectation was 9–10 s. **M1 → M2: 57.9 s → 8.6 s, 6.7× faster.** Reported, not pass/fail |

**Two readings worth keeping:**
- **The one remaining validation costs 223 ms, twice M1's 113 ms median.** M1's own max was 203 ms, so this is the
  first call's price (JIT, cold caches), not a slower validation. E3's 73 warm calls average 110 ms.
- **What the boot is now:** the engine 2.6 s · the rescan 4.5 s (52%) · the chain read 0.4 s · everything else
  ≈ 1.1 s. The rescan is still the largest step and still scales with history. F-3 slows its growth (P9, M3).
  The next levers stay as §5A.2 named them: parallel rescans (the largest wallet is only 4.0% of the work, so it
  would parallelize well) and limb arithmetic. Neither is needed at 8.6 s.

### 6.3 M3 results (2026-10-23, R3's world advanced 3,968 → 4,047 blocks)

Protocol: one launch, Dice Game, DEV time **9000X**, the strategy panel at its defaults (the preferences file was
absent: S7's "Reset preferences" had removed it), AUTO for **11 min 47 s** by the developer's clock, STOP, Exit. Then
one more boot, also Exit. Logs: the play session `godot2026-10-23T13.29.44.log` → scratchpad `M3_play.log`; the final
boot `godot.log` → `M3_boot.log`. Pre-run copies of `chain.jsonl` and `state.json`: scratchpad `pre_M3/`. The new
chain is append-only: its first 22,539,707 bytes equal the pre-run file. **79 blocks, 1,394 transactions.**

The ownership walk (§5A.2's, from the miners' coinbases forward, 1 unresolved spend in the whole chain) over the 79
new blocks:

| Sender | Spends with change | Change to the sender's **base** | Change to a **new** address |
|---|---|---|---|
| Cast miners | 681 | **673** | **8** |
| Companies | 611 | 0 | 599 (12 more went to addresses already used) |
| Miner bots, player, founders, casino | 0 | — | — |

**The 8 were all in the mempool saved with the world on 2026-10-20, before F-3.** Each of the 8 transaction ids is
in the pre-run `state.json`'s `PlayerPendingTransactions` (20 entries). All 8 were mined in block 3969, the run's
first block. Every cast-miner spend **created** under F-3 sent its change to the base: **673 of 673**.

| # | Verdict | The reading |
|---|---|---|
| **P9** | ❌ **refuted as worded**; ✅ holds for every spend created under F-3 | "0 new cast-miner change addresses in blocks mined in play" read 8. The prediction forgot that a policy applies to what is **created** after it: transactions already pending were built under the old rule and carry it until mined. Under F-3: 673 / 673 to base, 0 new. **The "still rotates" half:** companies made 599 new addresses (> 0) ✅. The miner bots made no spend with change in these 79 blocks, so their half is **unobserved**, not confirmed |
| **E5** | ✅ holds, with P9's qualification | the same reading |
| **E2** (per node) | ✅ now counted | the final boot: **"chain installed on 73 of 73 nodes (verdict valid)"** |
| Projection (§5A.2) | ❌ **refuted** | "yearly growth falls to about a quarter". These 79 blocks made 607 new addresses where the old rule would have made 1,280: **−52.6%, not −75%.** In R3's current era the companies make 47% of change spends; across the whole history they made 24% |

**Reconciliation, which needs no assumption:** the final boot's rescan derived **17,771** addresses, against M2's
17,164: **+607**, exactly the 599 + 8 new addresses the chain walk found. The rescan took 4,583 ms (0.258 ms each), the
validation 1 call, MainMenu **10.0 s** (63,793 − E3's 53,764; the engine start alone was 3.5 s this time).
**`[UtxoAssert]`** (every 25 blocks, a node's cached UTXO set against a full chain replay) **stayed silent** through
the run, so the UTXO caches agree with the chain with change landing on base addresses. No integrity line, no
`UNDECLARED` journal line, no error but the audio driver's `WASAPI` line.

**Not exercised:** F-3's mid-session registration point (`RegisterCastMinerNode`). No cast miner spawned; the 3
nodes beyond the boot's 73 were ghost miners (`forgotten_rig`, `anon_cpu_rig` mined one block each). Ghosts have no
`ReceiveWallet`, so their change already lands on their only address. That path is covered by reading the code
alone.

## 7. Out of scope

- **T4.2, one canonical chain + one UTXO set.** F-1 removes the repeated validation, not the 73 copies, and not the
  per-node UTXO rebuild that mini-plan 26 measured after the first block. That is T4.2's decision.
- **The editor's rebuild time** before the engine starts (≈ 8 s after a code change). That is the toolchain, not the
  game.
- Any change to what the boot loads, or to the world's format: **no bump, no wipe.**
- Key derivation and rescans, unless M1 refutes P5. Then they are named in §5's amendment.

## 8. Decisions at specification (the developer's)

| # | Decision | Proposed |
|---|---|---|
| **D-28.1** | Branch name | `mini28-boot-time` |
| **D-28.2** | M2's side-by-side measurement (E3) makes one launch about a minute longer | yes: it is the only way to compare the two paths within one session |
| **D-28.3** | The per-autoload marks and the `ChainIsValid` counter stay after close-out (DEBUG, compiled away in an export) | yes: the boot was unmeasured for a year, and nothing noticed it growing. The E3 switch is deleted |

**Added by the §5A amendment (2026-10-23), approved by the developer the same day (D-28.8 by their own choice):**

| # | Decision | Proposed |
|---|---|---|
| **D-28.4** | Unit 2 targets the rescan with **F-2** (fixed-base table for G), output-identical, no bump, no wipe | yes: it is 82.8% of the world load, and only the derivation's cost changes, never its result |
| **D-28.5** | **F-1** is built in the same unit, on M1's measured 8.6 s | yes: it is now the second-largest step and a timed cause, not an inferred one |
| **D-28.6** | Parallel rescans, a persisted wallet index, a lower gap limit and limb arithmetic are not built (§5A.2) | yes: parallelism and limb arithmetic stay as the next levers if E4 misses, with the data the instrument will print |
| **D-28.7** | The rescan counters (derivations, chain-pass ms, largest wallet) stay after close-out like D-28.3's; the old `ScalarMul` reference and the E3 switch are deleted | yes |
| **D-28.8** | **F-3**: cast miners' change returns to their base address (option 2); companies keep rotating | **decided by the developer, 2026-10-23**, in this plan. The policy is set by KIND, at the cast miners' two registration points. That is deliberate, and separate from D-16.6's rule that the *wallet* is attached by `HasSeed`, which is unchanged: cast miners keep their wallet |

## 9. Close-out (2026-10-23)

**Mini-plan 28 is complete.** Branch `mini28-boot-time`, 6 commits. **The boot went from 57.9 s to 8.6 s** (MainMenu
since the engine started, R3's world, launches without a rebuild, read from `godot.log`). After M3's 79 extra blocks
it is 10.0 s. M2's 8.6 s is the like-for-like figure, against M1's 57.9 s on the same world.

### 9.1 — What shipped

- **The instrument (unit 1, kept: D-28.3, D-28.7).** `BootProbe` marks the start of every autoload's `_Ready`, each step
  of `NetworkRoot.EnsureInitialized` with its counts, every `ChainIsValid` call (calls, total, min / median / max),
  the chain install as "installed on X of N nodes", and the rescan's chain pass, derivations, and largest wallet. All
  of it is `[Conditional("DEBUG")]` and printed with `GD.Print`. **The boot was unmeasured for a year and nothing
  noticed it growing; every DEBUG boot now prints where its seconds went.**
- **F-2, fixed-base key derivation (`Secp256k1`).** Every scalar multiply in the game is k·G. G's multiples are
  tabulated once (`d·256^j·G`, 8,160 affine points, ~0.18 s per process). k·G is at most 32 mixed additions, and the
  inverse is by extended Euclid. One derivation went from **2.42 ms to 0.26 ms** in-game. The output is identical:
  0 mismatches over 17,164 real R3 addresses (M2, twice) and 7,422 harness vectors (re-run on the final file after
  the old path was deleted). The general double-and-add `ScalarMul` and the `ModPow` inverse are deleted.
- **F-1, validate once (`BlockchainService.TryReplaceChainWithVerdict`).** The world load validates the chain once
  and installs it on all 73 nodes on that verdict: **8,564 ms → 224 ms.** The length check stays per node, and an
  invalid chain is still refused everywhere.
- **F-3, cast miners' change returns to their base address (D-28.8, the developer's choice).**
  `NodeAgent.RotateChangeAddress` with `NextChangeAddress()` / `OnChangeAddressUsed()` is the one change policy for
  the three send paths. `ApplyCastMinerChangePolicy` runs at both cast-miner registration points. On the 79 blocks M3
  mined, it removed 673 of the 1,280 new addresses the old rule would have made. CLAUDE.md's change-to-self rule and
  §29.9 now say this output is real and shown.
- **Deleted at close-out (Convention 3):** E3's switch `CompareOldPathsAtBoot`, `RunBootSideBySide`, the DEBUG
  references `GetCompressedPublicKeyByDoubleAndAdd`, `DeriveGmAddressByDoubleAndAdd` and `CountMismatchesAgainst`,
  and the E3 accessors in `BootProbe`. `CryptoUtils.cs` is byte-identical to its pre-plan state.

### 9.2 — Predictions, as they fell

| # | Verdict |
|---|---|
| P1 | ✅ #3 held 99.4% of the autoload stretch |
| **P2** | ❌ **refuted**: the per-node validation loop was 15.8% of the world load, not ≥ 70% |
| P3, P4, V1 | ✅ 73 validations, one per node, within 2× of each other, and they added up to the loop |
| **P5** | ❌ **refuted**: the derived-wallet rescan was 82.8% |
| P6 | ✅ 17,164 derivations (14,000–17,300), 99.7% of the rescan |
| P7 | ✅ 10.8% in-game (≤ 25%); the harness had read 8.6–9.0% |
| P8 | ✅ 0 mismatches |
| **P9** | ❌ **refuted as worded** (8 new cast-miner addresses, all from the mempool saved before F-3); ✅ for every spend created under F-3 (673 / 673) |
| E1–E5 | ✅ (E4: 8.6 s against a predicted < 20 s) |
| §5A.2's growth projection | ❌ **refuted**: −52.6% of new addresses in R3's era, not −75% |

### 9.3 — Corrections made during the plan, each against the code

- The plan's §2 structural reading named the validation loop. It was **right that the loop was redundant** (P3, P4,
  V1) and **wrong that it was the minute**. The step §7 had put out of scope held the minute.
- Step 16's cost note above `RescanDerivedReceiveWallets` ("~8 s") and `DefaultGapLimit`'s ("~20 SHA256") were
  both stale. They are corrected in place, with the reason (Convention 15).
- E3's switch is a constant, so **both** M2 launches ran the side-by-side, not one as §6 first said. That was
  corrected before M2 ran.
- E2's per-node chain length was **inferred** at M2 from the single verdict. The install mark now counts it, and
  M3's boot read 73 of 73.

### 9.4 — Lessons

1. **A cost that scales with the world's history must be written per unit of history, never as a figure.** Step
   16 wrote "~8 s" for a loop whose count was "every address the world has ever used". It was true the day it was
   typed, and it stayed plausible for three months while it grew to 44.7 s. The per-unit figure (2.4 ms per
   derivation before F-2, 0.26 after) is the one that stays true. This is Convention 15's case for a cost: the
   frozen number was a *consequence* (count × unit cost), and it was written as if it were a constant.
2. **Fix the count as well as the cost, when the count is unbounded.** F-2 alone would have bought time. On R3's
   chain 97.8% of all addresses were one-time change, 69% of them made by background volume no player handles.
   F-3 is what keeps F-2 from expiring. The question that found it was the developer's, not the instrument's:
   *why are so many addresses being made?*
3. **A policy applies to what is created after it, and saved state was created before.** P9 forgot the restored
   mempool. Eight transactions built under the old rule were mined under the new one. Any rule about how
   transactions are built has this tail, one mempool deep.
4. **A thin margin is a reason to measure the next design, not to ship the specified one.** The specified 4-bit
   table passed P7 by under 2 points. Two more harness runs, each minutes long, found a design 2.6× better: one
   showed the inversion was half the remaining cost. Mini-plan 26 §12.3's lesson 1 held again: the code named a
   cause, and only timing said how much it cost.

### 9.5 — Handed on

- **The rescan is still the largest step** (4.5 s of 8.6, 52%) and **still grows with history**, now mainly at the
  companies' rate. The next levers, measured or named here: parallel rescans (the largest wallet is 4.0% of the work,
  so it would parallelize well), and limb arithmetic in place of `BigInteger`. **Neither is scheduled**; at 8.6 s
  the boot is no longer the cost of a test.
- **The miner bots' change rotation went unobserved in M3** (no change-bearing spend in 79 blocks), and F-3's
  mid-session registration point was not exercised. Both are covered by the code, not by a run.
- From mini-plan 27, unchanged: S8's real wipe runs (at a wipe the developer chooses); deleting
  `LegacyPlayerFileRelocation` at mini-plan 29's close-out; code-built UI controls into `.tscn` at Basic Mode's UI
  refinement. *(2026-10-28: the deletion is done, and S8's "Reset world" half ran; mini-plan 29 §10.)*
