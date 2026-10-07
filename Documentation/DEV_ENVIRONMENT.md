# Dev Environment — the machine this project is built on

Extracted from `CLAUDE.md` by mini-plan 19 D (2026-10-07), where it had grown to 5,590 characters of
environment facts loaded into context on every message. **CLAUDE.md keeps the three facts that cost a cycle
when not known up front** — no Python, PowerShell is 5.1, `node -e` is the only open-tail interpreter rule —
and points here for the rest. Nothing below was changed in the move.

---

### Scripting tools on this machine — **there is NO Python. Do not reach for it.**

`python` / `python3` **appear on PATH and are not Python**: they are the Microsoft Store *app execution alias* (`AppInstallerPythonRedirector.exe`). `which python` succeeds, so availability checks pass; then every invocation prints `Python was not found; run without arguments to install from the Microsoft Store` and exits non-zero. This has cost repeated retry-and-switch cycles and left dead `Bash(python -c ...)` entries in `.claude/settings.local.json` that can never succeed. **Never write a `.py` file and never call `python`/`pip`.**

This is a deliberate choice, not a gap — for THIS project's workload the installed tools are the better instruments:

| Task | Use | Why |
|---|---|---|
| Aggregating CSV telemetry (`casino_bot_bid_trace.csv`, `difficulty_trace.csv`, `network_population_trace.csv`, …) | **`awk`** (Git Bash, via the Bash tool) | Per-row filter/group/sum is what it's for; handles the large traces without loading them. Extensive precedent in the allowlist. |
| Inspecting `user://` state (`state.json`, registries, checkpoints) | **`node -e`** (v22, already allowlisted) | JSON is native — no parser, no quoting fight. PowerShell's `ConvertFrom-Json` returns PSCustomObjects and is clumsy on nested chain state. |
| Verifying arithmetic that must MATCH the game | **`dotnet run`** on a throwaway console project in the scratchpad — **expect one approval prompt; deliberately not allowlisted** | The only faithful option. C# `decimal` ≠ Python/JS float, and `Money.Normalize`, the secp256k1 math and the DP verifications are exactly the cases where a reimplementation in another numeric model proves nothing. Precedent: the P16.6 secp256k1 benchmark, the ND.10l knapsack-DP brute-force check. |
| Filesystem, `%APPDATA%`, HTTP/dataset building | **PowerShell** | Windows-native; all the historical dataset scripts (`Get-BtcNetworkDaily.ps1`, …) are PowerShell. |

⚠️ **A trailing open `*` on an interpreter is arbitrary execution — learn the SHAPE, it is what you must recognise at the next approval prompt.** Claude Code's Bash patterns let a single `*` span any text including spaces, so `Bash(node -e ' *)`, `Bash(awk 'BEGIN{ *)` and `Bash(perl -i -pe ' *)` do not approve *a command* — they approve *any program the interpreter will accept*. The prefix reads specific and is not: what follows the quote is unbounded. The tell is an interpreter's inline-program flag (`-e`, `-c`, an opening `{`) followed by `*)`. **When a prompt offers to save a rule of that shape, it is a decision about arbitrary code execution, not about the command you just ran.** Approve it only for a tool with a standing, documented need — otherwise take the per-use prompt.

**The 2026-08-23 allowlist audit (237 → 60 entries) settled this for every such rule in the project, and `node -e` is now the ONLY one left.** Removed: `perl -i -pe` (an in-place, no-backup rewrite of any path — the exact shape Standing Convention 14 was written about), `dotnet run` (arbitrary compilation; one prompt per throwaway console project is a fair price), and two open-tail `awk` rules (`awk`'s `system()` is the same hole in a smaller disguise). The closed, fully-specified `awk` aggregations over the network and roster CSVs stayed — a *bounded* interpreter invocation is not this problem.

**The consequence to keep straight: while `node -e` stands, path permissions are ADVISORY, not a boundary.** `Read(…)`/`Edit(…)` rules bind Claude's own file tools and the file commands it recognizes (`cat`, `head`, `tail`, `sed`); they do **not** bind a subprocess that opens files through its own runtime. So a Node one-liner reaches any path on the machine however narrow the read rules look. That is an accepted trade — `node -e` is the sanctioned way to inspect `user://` JSON and the friction of re-approving it several times a session buys nothing real — but it is a trade, and it has a failure mode: **reading the two surviving `Read(…)` rules and concluding the agent is confined to those two trees.** It is not. A real boundary has to be OS-level — Claude Code's sandbox, which merges `sandbox.filesystem` with the Read/Edit **deny** rules — never the allowlist on its own.

Note PowerShell here is **5.1**, not 7 — see the PowerShell tool's own constraints (no `&&`/`||`, no ternary, `Import-Csv` is slow on large traces; prefer `awk` for those). Two more 5.1 traps worth knowing up front: **`2>&1` on a native exe** (e.g. `dotnet build`) wraps stderr in `ErrorRecord`s and sets `$?` to `$false` even on exit 0 — don't gate logic on it; and **`Set-Content`/`Add-Content` default to the ANSI codepage**, so appending to a doc containing `—`/`§`/`✅` needs an explicit `-Encoding utf8` or `[System.IO.File]::AppendAllText`.

**Upgrading to PowerShell 7 is DEFERRED by decision (2026-08-06), not an oversight — do not propose it as a fix.** Installing `pwsh` would silently switch the agent's PowerShell tool to 7.x (Claude Code autodetects it), so it is a project decision, not a machine tweak. The full record — measured state, what 5.1 costs, why the risk isn't worth it today, the explicit reactivation triggers, and the install trap to avoid — is `Documentation/PRIVATE_ROADMAP.md` **§8 T5**. Read that before raising the topic again.

If Python is ever installed, delete this section — and disable the Store aliases first (Settings → Apps → Advanced app settings → App execution aliases), or the stub keeps shadowing the real interpreter.


