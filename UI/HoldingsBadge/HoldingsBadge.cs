using Godot;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
using UI.CompanyRows;
#nullable enable

namespace UI.HoldingsBadge
{
	// Mini-plan 29 — the holdings badge: "Holdings: N waiting", N = companies with a vote awaiting the player or a
	// dividend a claim would pay, each counted once (CompanyRowSignals.TallyPlayerHoldings, the hub header's own
	// count). A LABELLED control, never a bare number (the StatusBar rule). Hidden while the player holds no stock;
	// pressing it opens the Player Holdings hub.
	//
	// One control for every place it appears (unit 3's StatusBar, unit 4's DiceGame, which builds no StatusBar), so
	// the two can never show different counts or colours. It moves only with a block, so BlockAccepted queues ONE
	// deferred refresh: never inside the block commit, several blocks in a frame coalesce, and no _Process work.
	public partial class HoldingsBadge : Button
	{
		// Names this instance in the DEBUG line, so a run can tell which badge refreshed (V-H3).
		public string Host { get; set; } = "StatusBar";

		private SceneManager? _sceneManager;
		private bool _refreshQueued;

		// H3's DEBUG line fires when the badge's state changes, not once per instance: every scene builds its own.
		private static string _lastLoggedState = string.Empty;

		public override void _Ready()
		{
			Visible = false;
			TooltipText = "Open My Companies";
			_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
			Pressed += () => _sceneManager?.Go(SceneManager.SceneId.PlayerHoldings);

			NetworkRoot.BlockAccepted += OnBlockAccepted;
			Refresh();
		}

		public override void _ExitTree()
		{
			NetworkRoot.BlockAccepted -= OnBlockAccepted; // static event — must not outlive this node
		}

		private void OnBlockAccepted(Block _)
		{
			if (_refreshQueued) return;
			_refreshQueued = true;
			Callable.From(Refresh).CallDeferred();
		}

		private void Refresh()
		{
			_refreshQueued = false;
			if (!IsInsideTree()) return;

			CompanyRowSignals.HoldingsTally tally = CompanyRowSignals.TallyPlayerHoldings();
			Visible = tally.Held > 0;
			if (tally.Held > 0)
			{
				Text = $"Holdings: {tally.Waiting} waiting";
				// The same four-state colour as the rows it points at: red a vote, green a claim, mocha both.
				Color work = CompanyRowSignals.PendingWorkColor(tally.VotesWaiting > 0, tally.ClaimsWaiting > 0);
				if (tally.Waiting > 0)
				{
					AddThemeColorOverride("font_color", work);
				}
				else
				{
					RemoveThemeColorOverride("font_color");
				}

				CompanyRowSignals.ApplyButtonBorder(this, work);
			}

			LogIfChanged(tally, Host);
		}

		// H3 (mini-plan 29 §3.4) — a line at each change of the badge, read afterwards from godot.log (GD.Print: the
		// Output panel and the log file, CLAUDE.md "name the panel").
		[System.Diagnostics.Conditional("DEBUG")]
		private static void LogIfChanged(CompanyRowSignals.HoldingsTally tally, string host)
		{
			string state = tally.Held > 0
				? $"\"Holdings: {tally.Waiting} waiting\" (held {tally.Held} · votes {tally.VotesWaiting} · claims {tally.ClaimsWaiting})"
				: "hidden (held 0)";
			if (state == _lastLoggedState) return;
			_lastLoggedState = state;
			GD.Print($"[Holdings] badge ({host}): {state}");
		}
	}
}
