# Mini-Plan 27 — User settings persistence: one home, one rule for what survives a wipe

**Series note:** twenty-seventh of the *mini-plan* series. It takes `PRIVATE_ROADMAP.md` ▶ NEXT **item 2** and §6's
first open item ("User settings persistence — a real design, which does not exist yet. Required for Basic Mode").
Mini-plan 26 (the per-block freeze, `mini26-per-block-freeze-plan.md`) closed item 1 on 2026-10-21.

**Status:** 📝 **SPECIFIED 2026-10-21 (`668ff18`), amended the same day with the developer's answers (§3.4, §8).**
D-27.1 is answered with **two player layers** plus a **User Settings scene**, and D-27.2 with the recommendation.
**§3.4 governs where it differs from §3.1–§3.3 and §5, and §3.5 (the hardware credits are world state) governs
over §3.4.** **D-27.9 is answered (2026-10-21): "Wipe everything" is the
equivalent of deleting the whole user data folder**, identity files and logs included. **Amended again the same day
with a new objective: a saved-strategies dropdown in DiceGame (unit 3b, §5; S9; D-27.10, answered: select, then
Load).** Unit 3 also makes both creations writers atomic (developer, 2026-10-21; §5).
**Order:** unit 1 → unit 2 → unit 3 → unit 3b → unit 4 → unit 5 → unit 6 → V1 (with V1b and V1c) → close-out.
**Unit 1 ✅ built (§4). Unit 2 ✅ built (§5). Unit 3 ✅ built (§5), and corrected by §3.5 (the hardware credits stay
in the world)**: the first launch after it moves the two creations (S6). **D-27.11 answered: "Reset preferences"
does not touch the hardware credits.** **Unit 3b ✅ built (§5). Unit 4 ✅ built (§5). Unit 5 ✅ built (§5). Unit 6 ✅ built
(§5).** **Verification: launches A–C done, every prediction so far holds (§6.3).** Next: **launch D**, with
`BootProbe` timing the boot the developer reported as slower (§6.3).

**Branch:** `mini27-user-settings` (proposed, D-27.6).

**World treatment: no bump, no wipe.** Nothing here changes a world-state file. The new files live **outside** the
world's reach by construction (§3.2). R3's world (v12, ~2016-06, the player at 10 credits) is kept. *If a wipe is
ever proposed, `user://logs/rank_calibration_trace.csv` is renamed `rank_calibration_R3.csv` first.*

**Prior art this plan consumes rather than re-derives:**
- CLAUDE.md **Pattern 2**: its three persistence questions, its exempt set "named in full", the durability rules
  (atomic, loud, never written back over the good copy), and "exempt by CLASS, never by list".
- **Pattern 5**: autoload order is a decision.
- **Pattern 6**: write on the event, never poll.
- **Convention 5**: a new field gets a sentinel default.
- `ProjectDesignManual.md` **Ch. 40**: durability; INC-001 and INC-004 are the cases.

---

## 1. Why this plan exists

Player-owned settings are scattered, and **no rule says what survives a world wipe.** The visible symptom
(2026-09-15, the v6 → v7 reset): `saved_betting_strategies.json` survived the wipe, and the developer expected it
to be deleted. It survives because the wipe deletes an **enumerated list** of files, and this one is not on it.
That is exemption by omission, the shape Pattern 2 warns against. Meanwhile every view preference added since
mini-plan 10 was given no home at all: each resets on scene entry or at launch, and §6's table has been recording
the debt ("a deferral nobody wrote down becomes an omission").

The roadmap's instruction is explicit: **"Do not fix that file alone.** Decide once which settings belong to the
player (survive a wipe) and which to the world (wiped with it), then give them one persistence home."

---

## 2. What the code says (verified 2026-10-21)

### 2.1 — There is no settings mechanism

No settings service, no `ConfigFile`, no settings JSON. Each value below lives where its feature put it, so its
**lifetime is an accident of its storage**:

| Setting | Where it lives today | Lifetime today |
|---|---|---|
| **Saved betting strategies** | `user://saved_betting_strategies.json` via `SavedBettingStrategyRepository` (path constant in `DiceGame`) | **forever**: not on the wipe's delete list |
| **Notepad notes** | `user://notepad_notes.json` via `NotepadService` | **forever**: on Pattern 2's exempt list |
| **DiceGame Bet View** (Detailed / Last 10 / Latest / OFF) | `DiceGame._betDisplayMode`, an instance field, default `Detailed` | **one scene entry** |
| **Explorer replay pace, speed, Per Bet credits** | `BetsHistoryExplorer._pace`, `_cursorSpeed`, `_perBetCredits`, instance fields | **one scene entry** |
| **Explorer winning-numbers view** (Last 10 / Latest) | `BetsHistoryExplorer._showLatestWinnerOnly`, a `static` | **one launch** |
| **Strategy panel's values, per node** | `DiceGame._nodeStrategies`, a `static` dictionary (deliberately static, CLAUDE.md Core Systems rule 1) | **one launch**. *§6 calls this "not persisted"; more precisely it survives scene changes and is lost at launch* |
| **DEV: requested time scale** | `CalendarTimeService.RequestedDevTimeScale`; not in `calendar_state.json` | **one launch** |
| **DEV: BlockExplorer's casino players' SC balances shown** | `BlockExplorer._showCasinoScBalances`, a `static`, default shown | **one launch** |
| DEV: the four profiler toggles | each profiler's static `Enabled` | one launch, **and must stay so**: their contract is OFF by default |
| DEV: player credits ladder | writes `hardware_allocation.json` (world state, on the delete list) | the world's: a shortcut to the shop, not a setting |

### 2.2 — How the wipe decides, and why a subdirectory is safe

`NetworkRoot.ResetWorldIfIncompatible` deletes:
- **named files**;
- two globs (`bet_history_*.jsonl` in `user://`, `blocks-*.json` in `user://blockchain`);
- a **suffix sweep** (`.tmp`, `.corrupt`, `.prerepair`) over `user://` and `user://blockchain/`. It uses
  `Directory.GetFiles(dir)`, whose default is `TopDirectoryOnly`.

**So no file in a new subdirectory such as `user://player/` can be deleted by the wipe**: no named path points
there, no glob matches there, and the sweep does not descend. The survival rule then holds by construction,
not by a list someone must remember to keep. U4's grep keeps it true (§6).

---

## 3. The rule (the design)

### 3.1 — Three lifetimes, named once

| Class | Survives a scene change | Survives a restart | Reverted to the last block's checkpoint | Survives a world wipe |
|---|---|---|---|---|
| **World** (balances, chain, clock, hardware…) | yes | yes | **yes** (Pattern 2) | **no** |
| **Player** (preferences, the player's own creations) | yes | **yes** | **never** | **yes** |
| **Session** (profiler toggles; transient UI) | yes or no | **no** | — | — |

**A player setting is never checkpoint-reverted.** A restart reverts the world to its last block. It must not
revert the player's choice of view: that choice is not game-world state, and reverting it would make the game feel
broken. This is Pattern 2's first question ("does this need a checkpoint restore path?") answered **no**, with the
reason, so the next reader does not "fix" the omission.

### 3.2 — One home: the `user://player/` directory

- **Everything of class Player lives under `user://player/`, and nothing else does.** The rule CLAUDE.md gets is
  that one sentence. It replaces "remember to keep these names off the delete list".
- **`user://player/settings.json`** holds the preferences: one small versioned JSON object, camelCase like every
  `user://` file.
- **The player's creations** (saved strategies, notepad notes) are collections, not preferences. They keep their own
  files, but move **into** the directory (D-27.2): `user://player/saved_betting_strategies.json` and
  `user://player/notepad_notes.json`.
- **Identity files** (the five wallet seeds, `bot_wallet_registry.json`, `wordlist_256.json`) are a **different
  class** and **stay where they are**. They belong to the world's identities, not the player's preferences, and
  moving them is not this plan's business.

### 3.3 — The persistence rules (Pattern 2's three questions, answered for this class)

- **Atomic:** every write goes `.tmp` → flush → rename, never truncate-and-stream (INC-001).
- **Loud on a bad read:** an unreadable `settings.json` is preserved **once** as `settings.json.corrupt` and
  printed with `GD.Print` (so it lands in `godot.log`). The game then starts from defaults. **The `.corrupt`
  copy is never overwritten**, so the evidence survives.
- **Never written back over the good copy:** a failed load leaves the file renamed, not overwritten, and the next
  write creates a fresh file (INC-004).
- **Defaults are sentinel-safe** (Convention 5): an unknown key is ignored and a missing key takes its default, so
  adding a setting later needs no version bump and no migration. A `settingsVersion` field exists for the day a
  meaning changes, and is **1**.
- **Not under `WorldWriteGuard`.** The guard refuses writes while a world is unreadable. A broken world must not
  stop a player from changing a view preference, and nothing here is world state. **This is an exemption by class**,
  and it goes into Pattern 2's exempt set by name ("the `user://player/` class").
- **Writes are event-driven** (Pattern 6): a setting is written when it changes, never on a timer or per frame.
  Counted (S2).

### 3.4 — Amendment (developer, 2026-10-21): two player layers, each a folder, and a scene to manage them

**The developer's answer to D-27.1.** The 2026-09-15 surprise was that **nobody had said** strategies survive a
wipe, not that they should not. What the developer wants is **two layers of player data**:

| Layer | Folder | Holds | Survives a world wipe | Reset by |
|---|---|---|---|---|
| **1 — Creations** | `user://player/creations/` | saved betting strategies, notepad notes | **yes** | only **"Wipe everything"** |
| **2 — Preferences** | `user://player/preferences/` | `settings.json` (every preference in §2.1) | **yes** | **"Reset preferences"**, easily, or "Wipe everything" |

- **Each layer is a folder, so each reset deletes a folder's files.** That is exemption and deletion **by class**,
  never by a list (Pattern 2). Nobody has to keep a list of a layer's files.
- ~~**The hardware credits move out of the world** into layer 2.~~ **Reversed the same day, before any file moved
  (§3.5): the hardware credits are WORLD state** and stay on the wipe's delete list.
- **A User Settings scene** (new `SceneId.UserSettings`, reached from MainMenu) shows both layers and offers three
  actions, each behind a confirmation:
  1. **Reset preferences**: layer 2 back to defaults, and nothing else. *(It said "hardware credits included" until
     §3.5; D-27.11 answered no.)*
  2. **Reset world**: the same wipe a format bump performs. **Both layers are kept.** Every node's hardware credits
     return to the initial allocation (§3.5).
  3. **Wipe everything**: **the equivalent of deleting the whole user data folder** (D-27.9, answered 2026-10-21).
     Every file under `user://`, recursively: the world, both layers, the **identity files** (the five wallet seeds,
     `bot_wallet_registry.json`, `wordlist_256.json`) and **`user://logs/`**, archived evidence included. The next
     launch is a first launch, with new wallets and new bot identities.
     - **What it cannot delete, and must say so.** Windows holds the current session's `godot.log` open from the
       moment the engine starts, before any autoload runs. The wipe therefore skips any file it cannot delete and
       reports each one by name. It never fails silently, and never fails the launch.
     - **Its confirmation is the strongest of the three:** it names the identity files and `user://logs/` explicitly,
       and asks twice.

  **Exactly three actions** (developer, 2026-10-21): Reset preferences, Reset world, Wipe everything.
- **Every action runs at the NEXT LAUNCH, never in place.** The scene writes a request file
  (`user://player/reset_request.json`) and quits. `WorldGuardService`, autoload #1, performs the request **before any
  other autoload loads anything**, prints every file it deletes, and deletes the request. This is the TL.3 lesson
  applied by construction: a static cache loaded before a wipe survives it in memory and writes itself back. A reset
  performed while the game runs would be exposed to every cache (`HardwareAllocationRepository`, the settings
  service, `NetworkRoot`); a reset performed before they load cannot be.
- **One wipe, not two.** "Reset world" calls the **same** code a format bump does. Unit 4 extracts the body of
  `ResetWorldIfIncompatible` into `WipeWorld(reason)` and calls it from both places. A second delete list would rot
  exactly as the first one did.
- **The rank trace rule still applies, but not for the reason this bullet first gave.** *Corrected at unit 5,
  against the code:* `rank_calibration_trace.csv` is **not** on the delete list. `user://logs/*` is evidence,
  exempt by class, and a kept trace **keeps appending in the new world**. So the reason to rename it first is to
  keep R3's rows apart from the next world's, not to save it from deletion. The confirmation says both things. It
  names the traces the wipe **deletes** (from `NetworkRoot.WorldTracePaths`, the array `WipeWorld` iterates), and it
  says that every other file in `user://logs/` is kept and continues, citing the rank trace by its symbol. The
  archived `*_F1`/`*_M1`/`*_M2` files are not on the list and are untouched.

**The developer's answer to D-27.2: (a).** The existing files move **once**, at startup, by a rename of the same
bytes: the two creations into layer 1. The developer's strategies and notes are kept. The rename code is deleted at
the close-out of the plan after next. *(`hardware_allocation.json` was a third file to move into layer 2 until
§3.5. It now stays where it is, so R3's 10 credits are kept by not moving it.)*

### 3.5 — Amendment (developer, 2026-10-21): the hardware credits are world state

**The developer's decision, given after unit 3 was committed (`660603b`) and before any launch had moved a file:**
"Reset world" must include the hardware credits. Outside DEV, hardware will be bought with the players' money, so a
world reset must be able to return **every player's credits to 1, in the individual (private) pool**.

- **`hardware_allocation.json` stays at `user://`**, on the wipe's delete list. **One delete list, so a format
  bump resets the credits too** (D-27.8: "Reset world" and a bump share `WipeWorld`).
- **The reset target needs no new code.** When the file is absent, `WalletInitializationService.EnsureHardwareAllocation`
  bootstraps every node (`player`, `bot_1`…`bot_4`) at 1 individual-pool credit and 0 casino-pool. The wipe also
  deletes `casino_pool_state.json`, so no pool ledger survives the credits it accounts for.
- **It is under `WorldWriteGuard` again**, because it is world state again. Unit 3's durability fixes stay: the write is
  atomic, and a bad read is preserved instead of throwing at boot. The two primitives move from `PlayerDataFiles` to
  a neutral **`DurableFile`**, because a world file now uses them. `PlayerDataFiles` keeps only the player paths.
- **Checked against the filesystem first (2026-10-21):** there was no `user://player/` folder yet, and both old files
  were at their old paths. So the reversal is a code change only, with no file to move back.
- **⚠ The constraint this hands P5.** It is no longer "credits that outlive their world"; that is solved. It is the
  restart revert. **The checkpoint does not restore hardware credits** (verified at §3.4). Once P5 prices hardware in
  SC, a purchase changes two things between blocks: the SC, which a restart reverts, and the credits, which it would
  not. **P5 must give hardware a checkpoint restore path** (Pattern 2's first question). The note in P5's roadmap
  entry is rewritten to say so.

---

## 4. Unit 1 — `UserSettingsService`

- **An autoload, appended as #20** (D-27.3). It depends on no other service, and every reader is a scene, which
  loads after all autoloads. So the end of the block is the correct place, not a default (Pattern 5). It loads
  `settings.json` in `_Ready`.
- **Typed accessors, one per setting** (`GetBetDisplayMode()` / `SetBetDisplayMode(…)` and so on), so a misspelt key
  is a compile error, not a silent default.
- **A `SettingChanged` event** (`event Action<string>`), fired on a real change only. A set to the current value
  writes nothing.
- **DEV keys** live in a `dev` section of the same file and are **read only in DEBUG builds** (D-27.4). An exported
  build ignores them, so a developer's 9000X can never leak into a player's game.
- **A one-line load report**, `[UserSettings] loaded N keys from user://player/settings.json (version 1)`, or the
  corrupt-file line. Under DEBUG it also prints a **write counter** on quit. Both use `GD.Print`, which `godot.log`
  receives, so the developer never has to watch for them.

**✅ Built 2026-10-21** (`Scripts/Services/UserSettingsService.cs`, registered #20 in `project.godot`). Three choices
made at build, recorded so unit 2 does not re-derive them:
- **Every getter returns `null` for a setting never set.** The default stays in the scene that owns the setting, so
  there is one copy of it (Convention 15), and "Reset preferences" is simply the file's absence. Enum settings are
  stored by **name** (a string), so a renamed enum member falls back to the scene's default instead of failing.
- **The per-node strategy values are `Config`, `NumberOfBets`, `WinningChance` and `BetHigh`.** Two of
  `NodeStrategyState`'s fields are left out deliberately: `AutoRechargeEnabled` (the player's is
  `BankrollProgramService`'s flag, which is world state the checkpoint restores; bots' is always ON) and
  `BetsPerSecond` (hardware-locked since Phase 3; `ApplyAutoBetSpeedSettings` ignores it).
- **A corrupt file that cannot be moved aside blocks every write for the session**, because writing would destroy
  the only copy of the evidence. A second corrupt file finds `settings.json.corrupt` taken and gets a timestamped
  `.corrupt` name of its own, so neither overwrites the other. The DEBUG quit line counts changes, writes and
  sets to the current value (S2).

## 5. Unit 2 — wiring the settings (one commit), and unit 3 — the two files (one commit)

**Unit 2:** each scene reads its value in `_Ready` and writes through the service on change. Nothing polls.
- DiceGame's Bet View.
- The explorer's pace, speed, Per Bet credits and winning-numbers view.
- The strategy panel's values, per node id: `player`, `bot_1`…`bot_4`, stable across worlds (D-27.5). The panel
  pre-fills from the saved values, and a running session still reads its own `SessionConfig`: **CLAUDE.md Core
  Systems rule 1 is untouched**.
- DEV: the requested time scale, and BlockExplorer's balances toggle.
- `static` fields that existed only to remember a preference are **deleted** (Convention 3), with one exception:
  `_nodeStrategies` stays `static`, because it also carries live state.

**✅ Unit 2 built 2026-10-21.** What the code does now, and four things found while wiring it:
- **The strategy panel's values re-arm the bots after a restart.** `_nodeStrategies` is not just what pre-fills the
  panel: `BuildBotConfigs` reads it to decide which bots run when the player starts an autobet, and the node
  selector's dots read it too. Restoring it once per launch (`RestoreNodeStrategiesFromSettingsOnce`, before
  anything reads the store) therefore puts **every node back as it was before the restart, bots' readiness
  included**. Before this unit, a restart left every bot unconfigured. *This is what "survives a restart" means
  for these values. The alternative, pre-filling the panel without arming the bots, would make a bot's state depend
  on whether the player had looked at it.*
- **The store's comment said not to persist it, and why.** "A config outliving a restart would describe a run that
  no longer exists." **Re-checked against the code, the reason no longer holds.** Since D-M2.8 a snapshot is the
  strategy's *configuration*, taken from the session during a run and never a mid-run rung. A configuration
  describes the next run as well as the last one. The comment is rewritten to say so.
- **Every snapshot calls the service, and only a real change writes.** Snapshots are frequent and mostly restate
  the same strategy (a manual bet, a bot burst, leaving the scene). Each costs one comparison of two small
  serialized objects. **Not timed.** Dragging the chance slider is the densest real-change source: one write per
  value step. That happens only with the autobet stopped, because the slider is run-locked.
- **The explorer's speed is restored, and its automatic drop to 1x on reaching the present (§2.1) is not
  written.** That drop is the replay's own rule, not the player's choice. The scene still arrives paused (§4.1).
  A saved value that the control no longer offers (a speed step, a ladder rung, a pace) is ignored in favour of
  the default, so a later change to a ladder cannot reopen the scene in a state it cannot show.
- **Deleted statics:** `BetsHistoryExplorer._showLatestWinnerOnly` and `BlockExplorer._showCasinoScBalances`.
  **Two new statics, both once-per-launch flags rather than preferences:** `DiceGame._nodeStrategiesRestored`, and
  `DevTimeScaleSelector._requestRestored`. The clock keeps the DEV request for the whole process, so the first
  selector to load applies the saved request once.

**Unit 3 (amended, §3.4, §3.5):** `saved_betting_strategies.json` and `notepad_notes.json` move into
`user://player/creations/` by a one-time rename at startup (D-27.2 = a). `settings.json` lives in `preferences/`.
The two creations' names leave Pattern 2's exempt list, and the path constants are updated in `DiceGame` and
`NotepadService`. `hardware_allocation.json` stays a world file at `user://` (§3.5).
**Unit 3 also makes both creations writers atomic** (developer, 2026-10-21). `SavedBettingStrategyRepository.Persist`
and `NotepadService.PersistNotes` both open the file with `FileAccess` in Write mode, which truncates it first: the
INC-001 shape, which §3.3 rules out for the player class. Both become `.tmp` → flush → rename. Both also **swallow a
bad read** today: the repository warns with `GD.PushWarning` and continues with an empty list, which the next Save then
writes over the file. `NotepadService` catches silently and does the same. Both get §3.3's treatment: a bad file is
preserved as `.corrupt`, the failure is printed with `GD.Print`, and nothing is written back over it.

**✅ Unit 3 built 2026-10-21.**
- **`PlayerDataFiles`** (`Scripts/Services/`) names the folder and its two layers. **`DurableFile`** holds the two
  durability primitives: `WriteAtomically` (`.tmp`, flushed to the device, renamed; creates the folder) and
  `MoveAsideAsCorrupt` (never overwrites an earlier `.corrupt`; returns null if the file cannot be moved, and the
  caller then writes nothing that session). *(Both lived in `PlayerDataFiles` until §3.5, when a world file began
  using them too.)* All four writers use them, so Pattern 2's durability answers are written **once**.
- **`LegacyPlayerFileRelocation`** moves the two creations by `File.Move`, from `WorldGuardService` **before** the
  world guard. An absent source prints nothing. If source and destination both exist, it moves neither and says
  so. Its file header says to delete it at the close-out of the plan after next.
- **Each path has one copy**, in its owner: `SavedBettingStrategyRepository.DefaultPath`, `NotepadService.SavePath`,
  `HardwareAllocationRepository.SavePath`. DiceGame's constant is deleted. `WalletInitializationService`'s copy
  now reads the repository's. The old paths survive only in the relocation's table.
- ~~**The hardware credits became player class**: off the wipe's delete list, off `WorldWriteGuard`.~~ **Corrected by
  §3.5 in the next commit:** they are world state again. They are back on the delete list and back under the guard,
  and the relocation moves only the two creations. **What unit 3 gave them stays:** an atomic write, and a corrupt
  read that is preserved instead of throwing. *Found while building:* a corrupt hardware file used to throw an
  uncaught `JsonException` at boot. It is now moved aside, and `WalletInitializationService` bootstraps the
  initial allocation as on a first launch. The primitives moved from `PlayerDataFiles` to `DurableFile`.
- **S4's tripwire, defined:** `grep -cE 'user://player/|PlayerDataFiles\.' Scripts/BlockchainPort/Simulation/NetworkRoot.cs`,
  **baseline 0**. *Two traps found writing it:*
  - **The slash is part of the pattern.** Without it, the grep counts `user://player_bank_account_state.json`, a
    world file.
  - **A literal-only grep can be bypassed** by code that names the folder through `PlayerDataFiles.Root`, so the
    class name counts too.
- **S6's baseline was re-hashed just before this commit and is unchanged:** `saved_betting_strategies.json`
  `10fab9ed593e7122`, 6,408 bytes; `hardware_allocation.json` `7a21a42a44c9d375`, 554 bytes; `notepad_notes.json`
  absent; no `player/` folder yet.

**Unit 3b (amendment, developer, 2026-10-21) — a saved-strategies dropdown in DiceGame.** It belongs to layer 1
(creations), so it is built **after** unit 3 has moved the strategies file.

*What the code does today (verified 2026-10-21).* Loading a saved strategy means typing its exact name into
`%StrategyNameInput` and pressing Load: `DiceGame.OnLoadStrategyPressed` → `SavedBettingStrategyRepository.TryGet(GameId,
name)`, matched case-insensitively. **A blank name loads the most recently saved strategy.** Load applies to the
**active node** (the player or the bot chosen in `%ActiveNodeSelector`), stops a running `_session` first, carries
the current auto-recharge value through unchanged, re-syncs the player's toggle from `BankrollProgramService`, and
saves the per-node snapshot. Load is **disabled under the run lock** and when the game has no saved strategy
(`UpdateStrategySaveLoadButtons`). The repository has `HasAnyForGame` and `TryGet`, **no list accessor**. The
developer's file holds **13** strategies, all `GameId = "Dice"`.

*The change:*
- **The repository gains `GetNamesForGame(gameId)`**: the names for that game, sorted by name, ordinal and
  case-insensitive (the same comparison `TryGet` matches with). Nothing else in the repository changes.
- **`%SavedStrategySelect`, an `OptionButton`**, on a second row directly under `StrategySaveControls`. It lists
  every saved strategy for `GameId`. **Disabled when there are none.** *Placement corrected 2026-10-21 (unit 2): this
  said "the band between it and the bet-history headers is free". **That was wrong.** The check scanned only the
  `.tscn`, and DiceGame builds its **Bet View button in code** at (706, 528), 260 × 42, in that band. The free part
  is **x 966–1260, y 528–570**, beside the Bet View button, about 290 px wide.*
- **Choosing an item SELECTS, it does not apply** (D-27.10, proposed): it writes the name into `%StrategyNameInput`,
  and the existing **Load** button applies it. `OnLoadStrategyPressed` is **not edited**, so today's semantics hold by
  construction rather than by re-implementation: active node, session stopped first, auto-recharge carried through,
  snapshot saved. CLAUDE.md Core Systems rule 1 is untouched.
- **The dropdown never shows a name other than the one Load will use** (Convention 6). Editing the name field
  re-selects the matching item, or none.
- **It refreshes after every Save**, so a new name appears at once and an overwritten one is not listed twice. No
  event is needed: DiceGame's own repository instance is the only writer while the scene is open (Pattern 6).
- **The name field stays**, for naming a Save. A blank-name Load still loads the most recent strategy.
- **Two DEBUG instruments**, `[Conditional("DEBUG")]`, printed with `GD.Print` so `godot.log` receives them:
  `[StrategyDropdown] listed N for Dice` on every refresh, and `[StrategyLoad] '<name>' -> <nodeId>` on every Load.

**✅ Unit 3b built 2026-10-21.** Two departures from the text above, plus two of the developer's decisions taken
before the commit:
- **Built in code, not in the `.tscn`**, as `_savedStrategySelect`. It sits at (976, 528), 280 × 42, beside the
  Bet View button, which is built in code the same way. The `%SavedStrategySelect` name above is therefore not a
  scene node.
- **`OnLoadStrategyPressed` has two lines appended**, and nothing above them is changed. One re-syncs the
  dropdown, because a name set from code emits no `TextChanged`. The other is the `[StrategyLoad]` trace.
  "Not edited" was the promise about its semantics, and the semantics are untouched.
- ~~**The dropdown is not run-locked.**~~ **It IS run-locked** (developer, 2026-10-21, before this unit's commit):
  disabled during an active autobet, like Load, the chance slider and HIGH/LOW. `ApplySavedStrategySelectAvailability`
  is one rule (no strategies, or the run lock), called from both the refresh and `ApplyRunLock`, so it holds
  whichever runs first on scene entry.
- **Code-built is for now** (developer, 2026-10-21). Every control built in code moves into a scene at Basic Mode's
  UI refinement, once everything works. The roadmap entry "UI built in scenes" holds the rule and the inventory
  command. That work is out of this plan's scope (§7).

Also: with nothing selected, the closed button reads **"Saved strategies (N)"**, or **"No saved strategies"** while
disabled, instead of a blank. The two traces are `[Conditional("DEBUG")]` and print with `GD.Print`.

**Unit 4 (new, §3.4) — the wipe as one function, and the request it runs at the next launch:**
- `WipeWorld(reason)` is extracted from `ResetWorldIfIncompatible`, unchanged in what it deletes.
- `WorldGuardService` reads `user://player/reset_request.json` and performs it **before** anything else loads:
  preferences, world, or everything. It prints every file it deletes, then deletes the request.

**✅ Unit 4 built 2026-10-21.**
- **`NetworkRoot.WipeWorld(reason)`** is the old body, moved. `ResetWorldIfIncompatible` keeps only the detection
  and calls it, with the same "World reset triggered (…)" line it always printed. **Two additions, neither a change
  to what is deleted:**
  - **Every named file it deletes, or fails to delete, is printed.** `DeleteIfExists` was silent, and so was a
    failure. The two globs print too. A format bump gains the same record.
  - **The version stamp is deleted first, then rewritten last as before.** *Found while writing the request's
    comment, which claimed the opposite:* a wipe interrupted half-way used to leave a half-deleted world behind
    a compatible stamp. Now it leaves no stamp, and the guard finishes the wipe at the next launch.
- **`PlayerResetRequest`** (`Scripts/Services/`) has `Write`, `Pending` (so the scene can say a reset is waiting),
  `Cancel`, and `PerformPending`. The request is `user://player/reset_request.json`, with the kind by name and a
  wall-clock bookkeeping stamp.
  - **At most once:** the request is deleted **before** the reset runs, and if it cannot be deleted nothing runs.
    Deleting it afterwards would make a stuck request wipe the world at every launch.
  - **An unreadable request resets nothing.** It is preserved as `.corrupt`, and the failure is printed.
  - **ResetPreferences** deletes every file under `preferences/` and nothing else (D-27.11).
  - **ResetWorld** calls `WipeWorld`.
  - **WipeEverything** deletes every file under `user://` at any depth, prints each, skips and names any it cannot
    delete, then removes emptied folders. The rest of that launch is a first launch: the wordlist, wallets and
    bot identities are regenerated by the paths a first launch already uses.
- **`WorldGuardService`** order: the pending request → the relocation → the world guard.
- **S8's code half, measured now:** every `DeleteIfExists` / `DeleteAbsoluteLoudly` call in `NetworkRoot.cs` is
  inside `WipeWorld` (**0 outside**). `WipeWorld` has **exactly 2 callers**: `ResetWorldIfIncompatible` and
  `PlayerResetRequest`. The commands: the `awk` line range of `WipeWorld` against `grep -n` of the two helpers,
  and `grep -rn "WipeWorld(" --include=*.cs .` minus the declaration.
- **Not testable before unit 5**, which writes the request, and only on a world the developer chooses (S8). R3's
  world is never the subject.

**Unit 5 (new, §3.4) — the User Settings scene:**
- `SceneId.UserSettings`, added the three-step way (CLAUDE.md Scene Management), reached from MainMenu.
- It shows each layer's contents (strategies and notes by count, the preferences)
  and the three actions, each behind a confirmation that names what will be deleted.
- **Read `ProjectDesignManual.md` Ch. 29 before building it** if it holds a `ScrollContainer`. The Back button goes in
  a fixed footer outside any scroll (§29.10).

**✅ Unit 5 built 2026-10-21** (`Screens/UserSettings/`, after reading Ch. 29 §29.1–§29.5, §29.10 and §29.11).
- **Layout:** Pattern A. `RootMargin` (`margin_bottom = 50`, §29.11) → `RootVBox` → `ContentScroll` (expands,
  horizontal scroll off) → `ContentVBox`. Every control in the content chain is on `mouse_filter = PASS`, and the
  long labels wrap (§29.6). The Back button sits in a **fixed footer outside the scroll**, the `ScTransactions`
  shape.
- **It shows** the creations (strategies and notes by count, and the folder) and the preferences (each key that
  holds a value, and the folder). A pending request appears with a **Cancel** button.
- **The three actions**, each behind a `ConfirmationDialog`. Each confirmation names what it deletes **from the
  source the reset deletes from** (Convention 6):
  - "Reset world" lists `NetworkRoot.WorldTracePaths`. That array is new: `WipeWorld` now iterates it instead of
    holding nine literal `DeleteIfExists` lines, so the dialog and the wipe cannot disagree.
  - "Wipe everything" lists `WalletInitializationService.IdentityFilePaths`, also new: the five seeds,
    `BotWalletRegistry.RegistryPath` and `WordlistBootstrapper.StorePath`, both made public. It shows the user
    folder's absolute path, says to copy anything worth keeping out first, and **asks twice**.
- **Confirming** writes the request. If the write succeeds, the game closes through
  `SceneManager.QuitLikeTheWindowClose`, which moved there from MainMenu so that both scenes use one copy. If it
  fails, nothing is reset and the game stays open to say why.
- **Reached from MainMenu's fixed footer**, beside Exit. `SceneId.UserSettings` was added the three-step way: 26 ids
  and 26 paths.
- **Built in `.tscn`**, not in code, apart from the dialogs, which follow MainMenu's Exit dialog.

**Unit 6 — the docs, in the same branch** (was unit 4):
- **CLAUDE.md:** the autoload table gains row #20, "19 autoloads" becomes 20, Pattern 2 names the
  `user://player/` class, and **one sentence is added to Pattern 2's third question**: a player-owned file goes under
  `user://player/` and is therefore never on the delete list.
- **`SERVICES.md`:** the service's section.
- **`PRIVATE_ROADMAP.md`:** §6's item is ticked, and its table becomes the plan's classification.
- **A tripwire grep:** `user://player/` must not appear in `ResetWorldIfIncompatible`. The baseline is 0.

**✅ Unit 6 built 2026-10-21.**
- **CLAUDE.md:**
  - The autoload count is 20, and row #20 is added.
  - NotepadService's path is updated.
  - Pattern 2's third question now names `WipeWorld` and adds the player-class sentence.
  - The exempt set is rewritten **by class**: the identity class (cited as
    `WalletInitializationService.IdentityFilePaths`) and the `user://player/` class. The S4 tripwire sits beside
    it, with its load-bearing slash.
  - The `WorldWriteGuard` "exempt by class" bullet adds `user://player/`.
  - The scene count is 26.
  - **Size: 86,055 → 86,826 characters (+771).** Above the 85,000 target, below the 95,000 warning. All of it is
    rules or index, which is the growth the budget section allows.
- **`SERVICES.md`:** 20 entries; `WorldGuardService`'s three steps; the `UserSettingsService` section;
  `CalendarTimeService`'s "neither is persisted" line corrected.
- **`SCENES.md`:** 26 ids, the `UserSettings` row, and its place in the navigation map.
- **`PRIVATE_ROADMAP.md`:** §6's item is marked **`[~]` built, not ticked**. It is ticked at close-out, once V1
  passes: ticking a functional item before its verification would be claiming the result. Its table is now the
  classification as built. The ▶ NEXT row reads IN PROGRESS.
- **`ProjectDesignManual.md` §20.1:** the notepad's new path and durability.
- **Not edited: `ROADMAP_ARCHIVE.md`**, which is history and quotes the old paths as they were.
- **The run order** is registered in §6.2, before any data.

---

## 6. Verification — predictions registered before any run

This is a functional plan, not a performance one, so the criteria are **counts of outcomes**:

| # | Prediction | How it is read |
|---|---|---|
| **S1** | **Every setting changed before a restart reads back after it**: N of N, where N is the number the protocol changes (8 in a DEBUG build, **plus one per bot whose strategy values the session touched**: since unit 2, each node's values are a key of their own) | `godot.log`'s load line (N keys), and `user://player/preferences/settings.json`, read by Claude |
| **S2** | **Writes = changes.** The DEBUG write counter equals the number of real changes made in the session, give or take a set to the same value, which writes nothing | the quit line in `godot.log` |
| **S3** | **A corrupt file fails loudly and harmlessly:** `settings.json.corrupt` exists, the load line says so, the game starts with defaults, and the next change writes a fresh `settings.json` | Claude corrupts the file before the developer's second launch (protocol step 4), then reads the files |
| **S4** | **The world never sees `user://player/`:** 0 references from `ResetWorldIfIncompatible`; the sweep stays `TopDirectoryOnly` | a grep, baseline 0, rerun at close-out (the exact command is in unit 3's build note: it counts the literal path **with its slash** and the `PlayerDataFiles.` class, over the whole of `NetworkRoot.cs`). A real wipe will confirm it at the next `WorldFormatVersion` bump, and the plan does not wait for one |
| **S5** | **The checkpoint never reverts a setting:** a setting changed between two blocks is still set after a restart, which reverts the world to the earlier block | the protocol changes a setting with the autobet stopped, then closes before the next block |
| **S6** | **The one-time move keeps everything:** after the first launch, `creations/` holds the strategies (and the notes, if any), **byte-identical** to the originals, and none remain at the old paths. `hardware_allocation.json` is **unmoved and byte-identical** at `user://` (§3.5). **R3 still runs at 10 credits** | Claude hashes the files before and after. **Baseline, 2026-10-21 (SHA-256, first 16 hex):** `saved_betting_strategies.json` `10fab9ed593e7122`, 6,408 bytes · `hardware_allocation.json` `7a21a42a44c9d375`, 554 bytes (player 10 credits, `bot_1`…`bot_4` 1 each, all individual) · `notepad_notes.json` **does not exist**, so the move must treat an absent file as nothing to move, never as an error |
| **S7** | **"Reset preferences" resets layer 2 only:** after the next launch the preferences are at their defaults; the creations and the world are untouched (same tip height, same files, **the hardware credits included**: D-27.11) | the request file, the deletion lines in `godot.log`, and the files themselves |
| **S8** | **"Reset world" runs the one `WipeWorld`** (a grep finds a single delete list), and **"Wipe everything" leaves `user://` holding nothing but the files it reports it could not delete** (expected: the session's open `godot.log` at most). **Both are tested only at a wipe the developer chooses, never on R3's world** | a grep now; at that wipe, the deletion lines in `godot.log` and a listing of `user://` |
| **S9** | **The dropdown lists exactly the saved strategies, and loads exactly what typing the name loads** (unit 3b). With N strategies for `Dice` it lists **N** (13 today) and is **disabled at 0**. A Save under a new name lists **N+1 at once**, without leaving the scene; a Save over an existing name keeps **N**. A strategy chosen and loaded onto a bot or the player lands in that node's strategy values with the saved `Config` | the `[StrategyDropdown]` lines against a `node -e` count of the file; the `[StrategyLoad]` lines against the per-node strategy values unit 2 writes to `settings.json` |

### 6.1 — Protocol V1 (the developer runs it; watches nothing; Claude reads the files afterwards)

1. Launch from the editor as usual, open DiceGame, and set Bet View to **Latest**.
2. Open the Bets Explorer and set Per Bet, credits **5**, speed **4x**, and winning numbers **Latest**. In
   BlockExplorer, **hide** the casino players' SC balances. In DiceGame's DEV selector choose **3000X**. Edit the
   strategy panel's base bet for the player to a value you will recognise.
3. With the autobet **stopped**, close the game normally. Tell me "V1 part 1 done".
4. **I** then copy `settings.json` aside and overwrite the original with invalid JSON (S3). The world is not touched.
5. Launch again, open DiceGame, change Bet View once, and close. Tell me "V1 done".
6. I restore the copy from step 4, and you launch once more to confirm S1 by eye. **Every value from steps 1–2 should
   be back.**

**Protocol V1b — the dropdown (S9).** It needs a world with **no** saved strategy, and the developer's file must
come out of it untouched. So the file is moved **out of `user://`** for one launch, and restored afterwards:
1. Launch, open DiceGame. Select **bot_2** in the node selector, choose **R2 L50** in the dropdown, press **Load**.
   Select **player**, choose **R1 0.5**, press **Load**. Close the game with the autobet stopped.
2. **I** copy `user://player/creations/saved_betting_strategies.json` to the scratchpad, record its SHA-256, and move
   it out of `user://` (only with your go-ahead at this step).
3. Launch, open DiceGame. The dropdown should be **disabled**. Type **V1 probe** in the name field, press **Save**,
   then press **Save** again. Close the game.
4. **I** restore the file and check its hash against step 2. The probe strategy disappears with the restore, so
   nothing of the test is left in your strategies.

**Protocol V1c — the User Settings scene (S7), on R3's world.** "Reset preferences" touches neither the world nor
the creations, so it is safe on R3. The other two resets are only **opened and cancelled** here:
1. From MainMenu, open **User Settings**. Then open **Reset world** → **Cancel**, and open **Wipe everything** →
   **Continue** → **Cancel** at the last confirmation. Nothing is written by either.
2. Press **Reset preferences** and confirm. The game closes.
3. Launch again, open DiceGame and close. **I** then read `godot.log`'s `[Reset]` lines and the files: the
   preferences are gone and are at their defaults, and the strategies, the tip height and the hardware credits are
   unchanged (S7).

### 6.2 — The run order (registered before any data, 2026-10-21)

V1, V1b and V1c, merged into four launches. **The developer watches nothing.** Every check is read afterwards from
`godot.log`, `settings.json` and the files, and V1's step 6 ("confirm by eye") is replaced by Claude reading the
load line and the file.

- **Launch A** (V1 steps 1–3 and V1b step 1):
  - Load **R2 L50** onto **bot_2** and **R1 0.5** onto the **player**, both through the dropdown and Load.
  - Then set every V1 preference, the player's base bet last, so that the edited value is the one persisted.
  - Close with the autobet stopped.
  - **Claude reads:**
    - S6: the move, by hash.
    - S1: **9 keys** in `settings.json`, V1's 8 plus `strategyPanel.bot_2`.
    - S2: the quit line.
    - S5: no block mined.
    - S9: 13 listed, and `[StrategyLoad]` against `settings.json`'s `strategyPanel.bot_2`.
  - **Then, between launches:** Claude copies `settings.json` aside and corrupts the original (S3). With the
    developer's go-ahead, Claude also moves the strategies file out of `user://` (V1b step 2).
- **Launch B:**
  - In DiceGame the dropdown should be disabled.
  - Save **V1 probe** twice, change Bet View once, and close.
  - **Claude reads:** S3 (`.corrupt` kept, the defaults, a fresh file) and S9 (0, then 1, then still 1).
  - **Then:** Claude restores the strategies file (hash-checked) and the `settings.json` copy.
- **Launch C** (V1c steps 1–2):
  - In User Settings, open Reset world and Cancel; open Wipe everything, Continue, and Cancel.
  - Then **Reset preferences** and confirm. The game closes.
  - **Claude reads:** the restored preferences' load line (S1 again, 9 keys), and that no request was written by
    the two cancelled dialogs.
- **Launch D:**
  - Open DiceGame and close.
  - **Claude reads:** the `[Reset]` lines, an empty `preferences/`, and the unchanged strategies, tip height and
    hardware credits (S7).
- **Not in this run:** "Reset world" and "Wipe everything" for real (S8), at a wipe the developer chooses.

### 6.3 — Results (launches A–C, 2026-10-22; D pending)

| # | Result |
|---|---|
| **S6** | **Holds.** Launch A's first line from the plan: `[PlayerFiles] moved user://saved_betting_strategies.json -> user://player/creations/… (6408 bytes, renamed, not rewritten)`. The moved file hashes `10fab9ed593e7122`, as at the baseline. Nothing remains at the old path, and `hardware_allocation.json` is unmoved and identical (`7a21a42a44c9d375`). |
| **S1** | **Holds.** After launch A, `settings.json` held **9 keys**, each with the value set: `LatestOnly`, `PerBet`, speed `400` (4x), `5` credits, latest-only `true`, the player's base bet `0.12345678`, `strategyPanel.bot_2`, DEV scale `30` (3000X), balances hidden. The read-back at launch C: `[UserSettings] loaded 9 keys … (version 1)`. |
| **S2** | **Holds.** Launch A: `19 changes, 19 writes, 5 sets to the current value`. Launch B: `2 changes, 2 writes`. Launch C: `0, 0, 0`. |
| **S3** | **Holds.** The corrupt file was preserved byte-for-byte as `settings.json.corrupt`, and the failure was printed with the parser's message. The game started from defaults (Bet View back to Detailed; one press made it `LastTen`), and the next change wrote a fresh file. |
| **S5** | **Holds** by construction of the run: no block was mined in any launch, and every setting survived the restarts that reverted the world. |
| **S9** | **Holds.** 13 listed; 0 listed with the file moved out; **1** after the first "V1 probe" Save, still **1** after the second (an overwrite). Both loads logged (`'R2 L50' -> bot_2`, `'R1 0.5' -> player`). **A first comparison looked like a refutation and was not one:** `bot_2`'s persisted Insist switches read ON where R2 L50 stores OFF, and the player's read OFF where R1 0.5 stores ON. Both come from the code's own rules: `BuildBotStrategyConfig` forces a bot's Insist ON, and the panel reads an Insist without its stop amount as OFF (R1 0.5 has no stops). The prediction's comparison must be against what the RESOLVER applies, not against the stored record. That is Standing Convention 8, and it is the comparison that holds. |
| **V1c (S7's half)** | The two cancelled dialogs wrote nothing (one `[Reset] requested` line, `ResetPreferences`); the request waits for launch D. |

**Two process notes from the run, both Claude's:**
- **A deletion ran before its guard had passed.** Between A and B, the script that moved the strategies file out
  compared hashes with `sha256sum FILE`. On a path with backslashes, that prints a leading `\`, so the comparison
  failed. The `rm` was not made conditional on it and ran anyway. **Nothing was lost.** The copy hashes to the
  baseline in full (`10fab9ed…09e97bd3`) and parses with all 13 strategies, and the restore after B was checked
  against that full hash before anything else. **Rule applied from then on: hash from stdin (`sha256sum < FILE`),
  and a deletion runs only inside the branch where its check passed.**
- The test's own `settings.json.corrupt` was moved to the scratchpad, not deleted.

**The developer's report after launch C (2026-10-22): starting the game now takes "almost half a minute", more than
double what it used to, not measured.** Checked first:
- **The ANGLE fallback is not new.** It is present in the 2026-10-20 logs.
- **The world has grown about 13%**, from height 3,496 (the last log before this plan) to 3,968, with a 22.5 MB
  `chain.jsonl`. That is not double.
- **This plan adds to the boot** three `FileExists` checks in `WorldGuardService`, the reset request when one is
  pending, and a 1 KB JSON read. **None of it is timed.**
- **A third candidate:** launching from the editor after a code change rebuilds the C# project before the engine
  starts, and launch A was the first after this plan's commits.
- **Nothing measured the boot**, because the scene-entry probe starts at a button press. So **`BootProbe`**
  (DEBUG, `GD.Print`) marks six points in time since the engine's start:
  - `WorldGuardService` start and end: the first autoload, and all this plan's added boot work.
  - `UserSettingsService` start and end: the last autoload, new in this plan.
  - MainMenu ready.
  Whatever is felt beyond the last mark happened before the engine started.
- **Prediction, registered before launch D:** WorldGuardService's span stays **under 50 ms** with a pending
  "Reset preferences", and UserSettingsService's under 50 ms. If either is in seconds, this plan caused the
  slowdown. If MainMenu's total is far below what the developer feels, the time is spent before the engine
  starts, in the editor's rebuild and launch.

---

## 7. Out of scope

- An **options menu**. The service is the foundation it will stand on. The menu is UI with its own design round.
- **Per-world** settings (e.g. "this world's preferred view"). The player class is per installation.
- The **identity files** (§3.2).
- Moving any **world** file. The delete list stays as it is, minus the two names unit 3 retires.
- Persisting the **profiler toggles**. Their OFF-by-default contract is the point.
- **Moving code-built controls into scenes**, this plan's dropdown included. It belongs to Basic Mode's UI refinement
  (developer, 2026-10-21: everything works first). See `PRIVATE_ROADMAP.md` "UI built in scenes".

---

## 8. Decisions at specification (the developer's)

| # | Decision | Proposed |
|---|---|---|
| **✅ D-27.1 — ANSWERED 2026-10-21** | Two player layers: **creations** (strategies, notes) persist; **preferences** (every other setting) are easily reset. A **User Settings scene** manages both and offers a general wipe. *(The hardware credits were in the preferences until §3.5 made them world state, the same day)* | §3.4 is the design, §3.5 corrects it |
| **✅ D-27.2 — ANSWERED 2026-10-21** | (a), as recommended: move the existing files once. The two creations (`hardware_allocation.json` was a third until §3.5) | §3.4, unit 3 |
| **✅ D-27.11 — ANSWERED 2026-10-21: no** | **Does "Reset preferences" still reset the hardware credits?** §3.4 said yes, back when the credits were a preference | **Proposed: no.** They are world state now (§3.5), and outside DEV they are bought with money, so resetting the view preferences would destroy a paid-for asset while the world keeps the SC that bought it. That is the same mismatch §3.5 removes. "Reset world" and a format bump already reset them |
| **D-27.7** | Every reset or wipe runs at the next launch, by a request file `WorldGuardService` performs first | yes: it is the only form a stale static cache cannot defeat (TL.3) |
| **D-27.8** | "Reset world" and a format bump share one `WipeWorld` | yes: one delete list |
| **✅ D-27.9 — ANSWERED 2026-10-21: YES** | **"Wipe everything" is the equivalent of deleting the whole user data folder**: identity files and `user://logs/` included, so the next launch is a first launch. The developer keeps the other two actions, **three in total**: Reset preferences, Reset world, Wipe everything | §3.4 item 3. *(Proposed was "no", keeping the identities; the developer chose the full wipe.)* |
| *D-27.1 as first proposed (superseded)* | **The classification.** Player: Bet View, the explorer's pace/speed/credits/winners view, the strategy panel's values, saved strategies, notepad notes. DEV, player-class (kept across wipes, DEBUG only): the requested time scale, the BlockExplorer toggle. Session: the profiler toggles. World: the credits ladder | as listed. **The one to look at twice is saved strategies.** Your 2026-09-15 surprise could mean "they should be wiped" or "nobody told me they survive". The plan assumes the second: they are your work, like the notes |
| *D-27.2 as first proposed (superseded)* | **The two existing files** move into `user://player/` | **(a) move them once at startup** (a rename of the same bytes, not a format migration, so your strategies and notes are kept). The rename's code is deleted at the close-out of the plan after next. **(b)** let them be lost and re-save: what the "no migrations" rule usually prefers. I recommend (a), because this is your data, not world state |
| **✅ D-27.10 — ANSWERED 2026-10-21: select, then Load** | **The strategies dropdown (unit 3b): does choosing an item apply it, or select it for Load?** | **Select, then Load.** *(Re-checked against the code: the run lock already disables Load and the node selector during an autobet, so "an accidental stop of a running session" is covered either way.)* The reasons that remain: applying **overwrites the active node's panel, unsaved edits included, with no undo**, so a mis-click in the popup would be destructive; and select-only leaves `OnLoadStrategyPressed` unedited, so Load's semantics hold by construction |
| **D-27.3** | `UserSettingsService` is autoload **#20** (appended) | yes: it depends on nothing, and nothing reads it before the scenes load |
| **D-27.4** | DEV keys stored in the same file and read only in DEBUG | yes |
| **D-27.5** | The strategy panel's values are kept per node id (`player`, `bot_1`…`bot_4`) | yes: the ids are stable across worlds |
| **D-27.6** | Branch name | `mini27-user-settings` |
