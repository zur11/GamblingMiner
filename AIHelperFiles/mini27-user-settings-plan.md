# Mini-Plan 27 — User settings persistence: one home, one rule for what survives a wipe

**Series note:** twenty-seventh of the *mini-plan* series. It takes `PRIVATE_ROADMAP.md` ▶ NEXT **item 2** and §6's
first open item ("User settings persistence — a real design, which does not exist yet. Required for Basic Mode").
Mini-plan 26 (the per-block freeze, `mini26-per-block-freeze-plan.md`) closed item 1 on 2026-10-21.

**Status:** 📝 **SPECIFIED 2026-10-21 (`668ff18`), amended the same day with the developer's answers (§3.4, §8).**
D-27.1 is answered with **two player layers** plus a **User Settings scene**, and D-27.2 with the recommendation.
**§3.4 governs where it differs from §3.1–§3.3 and §5.** One question is still open and blocks unit 1: **D-27.9**,
what "wipe everything" includes. Then **unit 1**, the service (§4).

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
| **2 — Preferences** | `user://player/preferences/` | `settings.json` (every preference in §2.1) **and the hardware credits** (`hardware_allocation.json`) | **yes** | **"Reset preferences"**, easily, or "Wipe everything" |

- **Each layer is a folder, so each reset deletes a folder's files.** That is exemption and deletion **by class**,
  never by a list (Pattern 2). Nobody has to keep a list of a layer's files.
- **The hardware credits move out of the world** at the developer's request: `hardware_allocation.json` leaves the
  wipe's delete list and goes into layer 2. **Checked first (2026-10-21):** credits are acquired only through the
  DEV "Buy Hardware" button and the DEV credits ladder, at no cost, and the checkpoint does not restore them. So no
  world money falls out of step with them, and they are already outside the restart revert. **⚠ A constraint for
  P5 (hardware progression):** once hardware is priced in world SC, credits that outlive the world that paid for
  them become an inconsistency. That plan must revisit this layer. The note goes into P5's roadmap entry.
- **A User Settings scene** (new `SceneId.UserSettings`, reached from MainMenu) shows both layers and offers three
  actions, each behind a confirmation:
  1. **Reset preferences**: layer 2 back to defaults, hardware credits included.
  2. **Wipe the world**: the same wipe a format bump performs. **Both layers are kept.**
  3. **Wipe everything**: the world and both layers. D-27.9 decides whether identity files go too.
- **Every action runs at the NEXT LAUNCH, never in place.** The scene writes a request file
  (`user://player/reset_request.json`) and quits. `WorldGuardService`, autoload #1, performs the request **before any
  other autoload loads anything**, prints every file it deletes, and deletes the request. This is the TL.3 lesson
  applied by construction: a static cache loaded before a wipe survives it in memory and writes itself back. A reset
  performed while the game runs would be exposed to every cache (`HardwareAllocationRepository`, the settings
  service, `NetworkRoot`); a reset performed before they load cannot be.
- **One wipe, not two.** "Wipe the world" calls the **same** code a format bump does. Unit 4 extracts the body of
  `ResetWorldIfIncompatible` into `WipeWorld(reason)` and calls it from both places. A second delete list would rot
  exactly as the first one did.
- **The rank trace rule still applies.** "Wipe the world" deletes the `user://logs` traces on the delete list, so the
  scene's confirmation names `rank_calibration_trace.csv` and the other traces it will take, which lets the
  developer rename them first. The archived `*_F1`/`*_M1`/`*_M2` files are not on the list and are untouched.

**The developer's answer to D-27.2: (a).** The existing files move **once**, at startup, by a rename of the same
bytes: the two creations into layer 1, and `hardware_allocation.json` into layer 2. **R3's 10 credits are therefore
kept**, and so are the developer's strategies and notes. The rename code is deleted at the close-out of the plan
after next.

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

**Unit 3 (amended, §3.4):** `saved_betting_strategies.json` and `notepad_notes.json` move into
`user://player/creations/`, and `hardware_allocation.json` into `user://player/preferences/`, by a one-time rename
at startup (D-27.2 = a). `settings.json` lives in `preferences/` too. The two creations' names leave Pattern 2's
exempt list, `hardware_allocation.json` leaves the wipe's delete list, and the path constants are updated in
`DiceGame`, `NotepadService`, `HardwareAllocationRepository` and `WalletInitializationService`.

**Unit 4 (new, §3.4) — the wipe as one function, and the request it runs at the next launch:**
- `WipeWorld(reason)` is extracted from `ResetWorldIfIncompatible`, unchanged in what it deletes.
- `WorldGuardService` reads `user://player/reset_request.json` and performs it **before** anything else loads:
  preferences, world, or everything. It prints every file it deletes, then deletes the request.

**Unit 5 (new, §3.4) — the User Settings scene:**
- `SceneId.UserSettings`, added the three-step way (CLAUDE.md Scene Management), reached from MainMenu.
- It shows each layer's contents (strategies and notes by count, the preferences, the hardware credits per node)
  and the three actions, each behind a confirmation that names what will be deleted.
- **Read `ProjectDesignManual.md` Ch. 29 before building it** if it holds a `ScrollContainer`. The Back button goes in
  a fixed footer outside any scroll (§29.10).

**Unit 6 — the docs, in the same branch** (was unit 4):
- **CLAUDE.md:** the autoload table gains row #20, "19 autoloads" becomes 20, Pattern 2 names the
  `user://player/` class, and **one sentence is added to Pattern 2's third question**: a player-owned file goes under
  `user://player/` and is therefore never on the delete list.
- **`SERVICES.md`:** the service's section.
- **`PRIVATE_ROADMAP.md`:** §6's item is ticked, and its table becomes the plan's classification.
- **A tripwire grep:** `user://player/` must not appear in `ResetWorldIfIncompatible`. The baseline is 0.

---

## 6. Verification — predictions registered before any run

This is a functional plan, not a performance one, so the criteria are **counts of outcomes**:

| # | Prediction | How it is read |
|---|---|---|
| **S1** | **Every setting changed before a restart reads back after it**: N of N, where N is the number the protocol changes (8 in a DEBUG build) | `godot.log`'s load line (N keys), and `user://player/settings.json`, read by Claude |
| **S2** | **Writes = changes.** The DEBUG write counter equals the number of real changes made in the session, give or take a set to the same value, which writes nothing | the quit line in `godot.log` |
| **S3** | **A corrupt file fails loudly and harmlessly:** `settings.json.corrupt` exists, the load line says so, the game starts with defaults, and the next change writes a fresh `settings.json` | Claude corrupts the file before the developer's second launch (protocol step 4), then reads the files |
| **S4** | **The world never sees `user://player/`:** 0 references from `ResetWorldIfIncompatible`; the sweep stays `TopDirectoryOnly` | a grep, baseline 0, rerun at close-out. A real wipe will confirm it at the next `WorldFormatVersion` bump, and the plan does not wait for one |
| **S5** | **The checkpoint never reverts a setting:** a setting changed between two blocks is still set after a restart, which reverts the world to the earlier block | the protocol changes a setting with the autobet stopped, then closes before the next block |
| **S6** | **The one-time move keeps everything:** after the first launch, `creations/` holds the strategies and notes and `preferences/` holds `hardware_allocation.json`, **byte-identical** to the originals, and none remain at the old paths. **R3 still runs at 10 credits** | Claude hashes the files before and after. **Baseline, 2026-10-21 (SHA-256, first 16 hex):** `saved_betting_strategies.json` `10fab9ed593e7122`, 6,408 bytes · `hardware_allocation.json` `7a21a42a44c9d375`, 554 bytes (player 10 credits, `bot_1`…`bot_4` 1 each, all individual) · `notepad_notes.json` **does not exist**, so the move must treat an absent file as nothing to move, never as an error |
| **S7** | **"Reset preferences" resets layer 2 only:** after the next launch the preferences are at their defaults and the hardware credits at their initial allocation; the creations and the world are untouched (same tip height, same files) | the request file, the deletion lines in `godot.log`, and the files themselves |
| **S8** | **"Wipe the world" and "Wipe everything" run the one `WipeWorld`:** a grep finds a single delete list. **They are tested only at a wipe the developer chooses, never on R3's world** | a grep now; the deletion lines in `godot.log` at that wipe |

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

---

## 7. Out of scope

- An **options menu**. The service is the foundation it will stand on. The menu is UI with its own design round.
- **Per-world** settings (e.g. "this world's preferred view"). The player class is per installation.
- The **identity files** (§3.2).
- Moving any **world** file. The delete list stays as it is, minus the two names unit 3 retires.
- Persisting the **profiler toggles**. Their OFF-by-default contract is the point.

---

## 8. Decisions at specification (the developer's)

| # | Decision | Proposed |
|---|---|---|
| **✅ D-27.1 — ANSWERED 2026-10-21** | Two player layers: **creations** (strategies, notes) persist; **preferences** (every other setting, **plus the hardware credits**) are easily reset. A **User Settings scene** manages both and offers a general wipe | §3.4 is the design |
| **✅ D-27.2 — ANSWERED 2026-10-21** | (a), as recommended: move the existing files once. Now three files: the two creations and `hardware_allocation.json` | §3.4, unit 3 |
| **D-27.7** | Every reset or wipe runs at the next launch, by a request file `WorldGuardService` performs first | yes: it is the only form a stale static cache cannot defeat (TL.3) |
| **D-27.8** | "Wipe the world" and a format bump share one `WipeWorld` | yes: one delete list |
| **D-27.9 — OPEN, blocks unit 1** | **"Wipe everything": do the identity files go too?** That is the five wallet seeds, `bot_wallet_registry.json` and `wordlist_256.json`, which every wipe has kept so far | **No** (recommended): the same wallets and bot identities come back in the next world, and the closest thing to a fresh install stays "delete the user data folder by hand". **Yes:** a truly fresh start, with new addresses for everyone |
| *D-27.1 as first proposed (superseded)* | **The classification.** Player: Bet View, the explorer's pace/speed/credits/winners view, the strategy panel's values, saved strategies, notepad notes. DEV, player-class (kept across wipes, DEBUG only): the requested time scale, the BlockExplorer toggle. Session: the profiler toggles. World: the credits ladder | as listed. **The one to look at twice is saved strategies.** Your 2026-09-15 surprise could mean "they should be wiped" or "nobody told me they survive". The plan assumes the second: they are your work, like the notes |
| *D-27.2 as first proposed (superseded)* | **The two existing files** move into `user://player/` | **(a) move them once at startup** (a rename of the same bytes, not a format migration, so your strategies and notes are kept). The rename's code is deleted at the close-out of the plan after next. **(b)** let them be lost and re-save: what the "no migrations" rule usually prefers. I recommend (a), because this is your data, not world state |
| **D-27.3** | `UserSettingsService` is autoload **#20** (appended) | yes: it depends on nothing, and nothing reads it before the scenes load |
| **D-27.4** | DEV keys stored in the same file and read only in DEBUG | yes |
| **D-27.5** | The strategy panel's values are kept per node id (`player`, `bot_1`…`bot_4`) | yes: the ids are stable across worlds |
| **D-27.6** | Branch name | `mini27-user-settings` |
