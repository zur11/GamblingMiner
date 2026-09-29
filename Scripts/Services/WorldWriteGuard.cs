using Godot;
using System;
using System.Collections.Generic;
using GodotBlockchainPort.Simulation;

/// <summary>
/// Mini-plan 15 A (2026-09-29) — one question, asked by every writer of world state: <b>may I write at all?</b>
///
/// <para><b>Why this exists.</b> Mini-plan 14's durability gate staged a damaged chain file. The world load
/// aborted correctly and <see cref="NetworkRoot"/> refused to persist — and then the session rewrote
/// <c>calendar_state.json</c>, <c>bankroll_state.json</c>, <c>principal_balance_state.json</c>, the lifetime
/// rollup and two journal segments, because those writers had never heard of the failure. They happened to
/// carry the real checkpoint's values that time, so nothing was corrupted; **that was luck, not design.**</para>
///
/// <para><b>The rule:</b> a session that could not load its world writes <b>nothing world-shaped</b>. Not the
/// clock, not a balance, not a ledger, not the journal. The world on disk is the last coherent thing that
/// exists, and a session that cannot read it has no business editing it.</para>
///
/// <para><b>Not covered, deliberately:</b> identity files (wallet seeds, the bot registry, the wordlist) and
/// developer conveniences (saved strategies, notepad notes) are not world state — they survive a world wipe by
/// the same reasoning, and they are not written by these paths anyway.</para>
///
/// <para>The refusal is announced <b>once per writer</b>, to both the Output panel and the Errors tab: a broken
/// session should say what it is refusing to do, and say it in the place a human reads (CLAUDE.md, "NAME THE
/// PANEL"), without flooding either one at bet rate.</para>
/// </summary>
public static class WorldWriteGuard
{
	private static readonly HashSet<string> _announced = new(StringComparer.Ordinal);

	/// <summary>
	/// True when the caller must NOT write. Call it at the top of any method that persists world state:
	/// <c>if (WorldWriteGuard.RefuseWrite(nameof(MyService))) return;</c>
	/// </summary>
	public static bool RefuseWrite(string writerName)
	{
		if (!NetworkRoot.WorldLoadFailed)
		{
			return false;
		}

		if (_announced.Add(writerName ?? "unknown"))
		{
			string message = $"[WorldWriteGuard] {writerName} is NOT writing — the world failed to load this " +
							 "session, so nothing world-shaped may be persisted. Restore or delete the files " +
							 "named in the load error above, then restart.";
			GD.Print(message);
			GD.PrintErr(message);
		}

		return true;
	}
}
