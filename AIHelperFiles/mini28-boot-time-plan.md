# Mini-Plan 28 — The boot time: attribute the minute, then remove what is repeated

**Series note:** twenty-eighth of the *mini-plan* series. It takes `PRIVATE_ROADMAP.md` ▶ NEXT **item 2b** and §8
**T4.7**, inserted by the developer on 2026-10-22 after mini-plan 27 measured the boot for the first time
(`mini27-user-settings-plan.md` §6.4).

**Status:** 🔨 **IN PROGRESS.** Specified 2026-10-22. Unit 1 (§4) built 2026-10-23 (`c5b0e6d`). **M1 run
2026-10-23 (§6.1): P2 refuted.** The minute is the derived-wallet rescan (82.8%), not the per-node validation
(15.8%). **§5A amends unit 2 (F-2 fixed-base derivation + F-1 validate once) and awaits the developer's approval.**
Next, once approved: §5A.5's offline harness, then unit 2, then M2.

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

## 5A. Unit 2, amended from M1's attribution (2026-10-23) — awaiting the developer's approval

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

### 5A.5 Order

1. **Offline harness** (scratchpad, never in the repo): a console project compiling the current `Secp256k1.cs`
   next to the F-2 version. It checks identity over Step 16's vector shapes (seed-derived keys, the `" #r{i}"` and
   `"sign:"` seeds, the empty seed, the edge scalars 1, 2, 3, 255, N−2, N−1) plus a few thousand more, and times
   both. P7 and P8 are read here first. **If either is refuted, F-2 is not built** and this section returns to the
   developer.
2. Unit 2 in the repo: F-2, F-1, the rescan counters, the E3 switch, and the two stale cost comments. Builds, the
   locale detector and the tripwire, stage, approval.
3. **M2**: the same two launches as M1. The second runs E3, so it is about a minute longer, once.

## 6. Measurement — protocols (the developer runs them; watches nothing; Claude reads the files)

**M1, after unit 1** (the code unchanged, only instrumented):
1. Launch from the editor, wait for the main menu, then **Main Menu → Exit Game → Exit**. *(This first launch rebuilds
   the C# project and is not used for the figures.)*
2. Launch again, wait for the main menu, and Exit Game the same way. Tell me "M1 done", and, if you timed it, the
   stopwatch figure.
3. **I** read `godot.log` from launch 2, plus launch 1 from its rotated copy, and copy both out of `user://logs/` as
   `M1_boot_*.log` in the scratchpad.

**M2, after unit 2** (now §5A's unit 2, F-2 + F-1): the same two launches. The second launch also runs E3's
side-by-side measurement, so it is about a minute longer, once.

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

**Added by the §5A amendment (2026-10-23), open until the developer answers:**

| # | Decision | Proposed |
|---|---|---|
| **D-28.4** | Unit 2 targets the rescan with **F-2** (fixed-base table for G), output-identical, no bump, no wipe | yes: it is 82.8% of the world load, and only the derivation's cost changes, never its result |
| **D-28.5** | **F-1** is built in the same unit, on M1's measured 8.6 s | yes: it is now the second-largest step and a timed cause, not an inferred one |
| **D-28.6** | Parallel rescans, a persisted wallet index, a lower gap limit and limb arithmetic are not built (§5A.2) | yes: parallelism and limb arithmetic stay as the next levers if E4 misses, with the data the instrument will print |
| **D-28.7** | The rescan counters (derivations, chain-pass ms, largest wallet) stay after close-out like D-28.3's; the old `ScalarMul` reference and the E3 switch are deleted | yes |
