using System;
using System.Globalization;
using Godot;

namespace Scripts.Sessions
{
	/// <summary>
	/// Mini-plan 05 D2 — one row per bet-session construction, Start and Stop.
	///
	/// The question this exists to answer is "how many bet sessions are alive at once, and who owns them?".
	/// Mini-plan 04 §13 proved from the journal that TWO independent wallets were writing to the player's
	/// bet history, and could not say which code created either, because the journal records no author.
	///
	/// It is hooked inside <see cref="BaseBetSession"/> itself rather than at the two known creation sites,
	/// and that is the whole design: hypothesis H4 is "a session instance nobody knows about", and a trace
	/// wired only into the sites we already know about is structurally incapable of catching it. A session
	/// whose owner was never set shows up as <c>unknown</c>, which is itself a finding.
	///
	/// Trace-only, no persisted state, no format bump — so it runs against the world that ALREADY has the
	/// bug in it, which is the only known reproduction (plan §6.1).
	/// </summary>
	public static class SessionLifecycleTrace
	{
		public const string TracePath = "user://logs/session_lifecycle_trace.csv";

		private const string Header =
			"gameTimeLocal,realTimeUtc,event,owner,sessionId,type,nodeId,walletBalance,reason,note";

		private static int _nextSessionId = 1;
		private static bool _headerChecked;

		/// <summary>Process-monotonic id, handed out at construction so even an unstarted session is
		/// identifiable. Ids restart at 1 each launch — the trace is a within-session instrument.</summary>
		public static int NextSessionId() => _nextSessionId++;

		public static void Write(
			string eventName,
			string owner,
			int sessionId,
			string type,
			string nodeId,
			decimal walletBalance,
			string reason = "",
			string note = "")
		{
			try
			{
				EnsureHeader();

				// Game time, not wall-clock: this trace is read side by side with the bet journal, whose
				// timestamps are game time. A row nobody can line up against the evidence is decoration.
				string gameTime = ResolveGameTimeLocal();

				string row = string.Format(
					CultureInfo.InvariantCulture,
					"{0},{1},{2},{3},{4},{5},{6},{7:F8},{8},{9}\n",
					gameTime,
					DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
					eventName,
					owner ?? "unknown",
					sessionId,
					type ?? "",
					nodeId ?? "",
					walletBalance,
					reason ?? "",
					note ?? "");

				using FileAccess file = FileAccess.Open(TracePath, FileAccess.ModeFlags.ReadWrite);
				if (file == null)
				{
					return;
				}

				file.SeekEnd();
				file.StoreString(row);
			}
			catch (Exception)
			{
				// A diagnostic must never be able to take down the thing it is diagnosing.
			}
		}

		private static string ResolveGameTimeLocal()
		{
			// Resolved defensively through the scene tree: this class is static and is called from sessions
			// that are not Nodes, so it cannot hold an autoload reference of its own.
			SceneTree tree = Engine.GetMainLoop() as SceneTree;
			var calendar = tree?.Root?.GetNodeOrNull<CalendarTimeService>("CalendarTimeService");
			DateTime local = calendar?.CurrentLocalDateTime ?? DateTime.Now;
			return local.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
		}

		private static void EnsureHeader()
		{
			if (_headerChecked)
			{
				return;
			}

			_headerChecked = true;

			if (!DirAccess.DirExistsAbsolute("user://logs"))
			{
				DirAccess.MakeDirRecursiveAbsolute("user://logs");
			}

			if (FileAccess.FileExists(TracePath))
			{
				// ND.10j's stale-schema rule: rotate rather than append rows under a header that no longer
				// describes them. A misaligned trace is worse than no trace, because it is read as data.
				using FileAccess existing = FileAccess.Open(TracePath, FileAccess.ModeFlags.Read);
				string firstLine = existing?.GetLine() ?? string.Empty;
				existing?.Close();
				if (!string.Equals(firstLine, Header, StringComparison.Ordinal))
				{
					DirAccess.RenameAbsolute(TracePath, TracePath + ".old");
				}
				else
				{
					return;
				}
			}

			using FileAccess created = FileAccess.Open(TracePath, FileAccess.ModeFlags.Write);
			created?.StoreString(Header + "\n");
		}
	}
}
