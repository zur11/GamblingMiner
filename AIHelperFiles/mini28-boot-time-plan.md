# Mini-Plan 28 — The boot time: attribute the minute, then remove what is repeated

**Series note:** twenty-eighth of the *mini-plan* series. It takes `PRIVATE_ROADMAP.md` ▶ NEXT **item 2b** and §8
**T4.7**, inserted by the developer on 2026-10-22 after mini-plan 27 measured the boot for the first time
(`mini27-user-settings-plan.md` §6.4).

**Status:** 🔨 **IN PROGRESS.** Specified 2026-10-22. **Unit 1, the instrument (§4), built 2026-10-23.** Next:
measurement **M1** (§6), then §5's decision rule on its data.

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

## 6. Measurement — protocols (the developer runs them; watches nothing; Claude reads the files)

**M1, after unit 1** (the code unchanged, only instrumented):
1. Launch from the editor, wait for the main menu, then **Main Menu → Exit Game → Exit**. *(This first launch rebuilds
   the C# project and is not used for the figures.)*
2. Launch again, wait for the main menu, and Exit Game the same way. Tell me "M1 done", and, if you timed it, the
   stopwatch figure.
3. **I** read `godot.log` from launch 2, plus launch 1 from its rotated copy, and copy both out of `user://logs/` as
   `M1_boot_*.log` in the scratchpad.

**M2, after unit 2** (only if §5's rule builds F-1): the same two launches. The second launch also runs E3's
side-by-side measurement, so it is about a minute longer, once.

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
