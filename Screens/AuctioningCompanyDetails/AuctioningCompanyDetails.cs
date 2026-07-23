using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GodotBlockchainPort.Simulation;
using GodotBlockchainPort.Blockchain;
using UI.StatusBar;
#nullable enable

// Step 14 (ND.5c, D-ND5.9) — per-non-miner detail scene: the live top-10 tracked donation pool while the
// auction is still open, switching to a founding/stock-distribution summary once it resolves (ND.8b.2,
// D-ND8.14 — supersedes the original SC-cashback/BTC-sweep settlement view). Entered from BlockExplorer's
// Enroll Mode (D-ND5.2) via the static hand-off field below — no existing cross-scene parameter-passing
// convention was found elsewhere in the codebase to reuse (D-ND5.9), so this is a new, minimal, self-
// contained pattern, not an extension of SceneManager. The scene NEVER triggers founding itself —
// NetworkRoot.TrySettleResolvedAuctions/FoundCompany already ran it live, exactly once, from
// HandleMinedBlock; this scene only ever displays the result (GetNonMinerAuctionLedger / GetCompanyFounding
// are both pure, side-effect-free reads). A stand-in until ND.8b.4's CompanyDetails scene replaces it.
public partial class AuctioningCompanyDetails : Control
{
	// Set by BlockExplorer immediately before SceneManager.Go(SceneId.AuctioningCompanyDetails).
	public static string? PendingNonMinerAddress;

	private NetworkRoot _networkRoot = null!;
	private SceneManager? _sceneManager;
	private string _nonMinerAddress = string.Empty;

	private Label _identityLabel = null!;
	private Label _statusLabel = null!;
	private Label _sectionTitleLabel = null!;
	private VBoxContainer _contentVBox = null!;

	public override void _Ready()
	{
		_networkRoot = GetNode<NetworkRoot>("NetworkRoot");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());

		_identityLabel = GetNode<Label>("%IdentityLabel");
		_statusLabel = GetNode<Label>("%StatusLabel");
		_sectionTitleLabel = GetNode<Label>("%SectionTitleLabel");
		_contentVBox = GetNode<VBoxContainer>("%ContentVBox");

		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.BlockExplorer);

		_nonMinerAddress = PendingNonMinerAddress ?? string.Empty;
		if (string.IsNullOrEmpty(_nonMinerAddress))
		{
			// Opened without a hand-off (e.g. a stray direct load) — nothing to show, bounce back.
			_sceneManager?.Go(SceneManager.SceneId.BlockExplorer);
			return;
		}

		// ND.10b (Ch. 38) — event-driven: everything shown (tracked pool, tiers, escalation %, and the
		// days-left, anchored to the latest block's timestamp) changes ONLY on a new block, so rebuild on
		// BlockAccepted instead of a per-frame timer. Unsubscribed in _ExitTree — a static event holding an
		// instance handler must be released or it leaks / crashes on the freed scene.
		NetworkRoot.BlockAccepted += OnBlockAccepted;
		RefreshAll();
	}

	public override void _ExitTree()
	{
		NetworkRoot.BlockAccepted -= OnBlockAccepted;
	}

	private void OnBlockAccepted(Block block)
	{
		if (string.IsNullOrEmpty(_nonMinerAddress)) return;
		// BlockAccepted fires mid-HandleMinedBlock; defer the rebuild (and the possible resolve→forward scene
		// change) to idle rather than running re-entrantly inside the block processing.
		Callable.From(RefreshAll).CallDeferred();
	}

	private void RefreshAll()
	{
		IReadOnlyList<NonMinerDonationSummary> ledger = _networkRoot.GetNonMinerAuctionLedger();
		NonMinerDonationSummary? summary = ledger.FirstOrDefault(s => s.NonMinerAddress == _nonMinerAddress);
		if (summary is null)
		{
			_identityLabel.Text = _nonMinerAddress;
			_statusLabel.Text = "Not found.";
			return;
		}

		string appearance = summary.CompanyAppearanceDateLocal is DateTime d
			? $"  —  appeared {d:yyyy-MM-dd}"
			: string.Empty;
		_identityLabel.Text = $"{NetworkRoot.DescribeCompany(summary)}{appearance}   [{summary.NonMinerAddress}]";

		if (summary.Status == NonMinerAuctionStatus.Resolved)
		{
			// ND.8b.4 (D-ND8.16) — a founded company's home is now the CompanyDetails scene (founding
			// summary + holding-gated Board Vote / dividend panels); forward there instead of the retired
			// ND.8b.2 stand-in summary. This scene stays the InAuction-only view (D-ND5.9).
			CompanyDetails.PendingNonMinerAddress = _nonMinerAddress;
			_nonMinerAddress = string.Empty; // stop this scene's refresh loop before the switch
			_sceneManager?.Go(SceneManager.SceneId.CompanyDetails);
		}
		else
		{
			ShowLiveTrackedDonations(summary, ledger);
		}
	}

	// D-ND5.9 — while InAuction: the live top-10 tracked donation pool (D-ND5.3). ND.10b — now two columns:
	// the bids list (left) + the per-bot real leading-bid roll panel (right). Rebuilt on BlockAccepted.
	private void ShowLiveTrackedDonations(NonMinerDonationSummary summary, IReadOnlyList<NonMinerDonationSummary> ledger)
	{
		long nowMs = _networkRoot.GetPlayerLatestBlock().Timestamp;
		string clock = summary.LeadingBidUnixMs == 0
			? "awaiting first bid — no countdown"
			: string.Create(CultureInfo.InvariantCulture,
				$"{Math.Max(0d, (summary.WindowCloseUnixMs - nowMs) / 86_400_000d):0.0}d left in the current window");
		_statusLabel.Text = $"In Auction — {clock}";

		// ND.6d — the pool's occupied-slot count selects the casino-bot re-bid mode shown per slot below:
		// EARLY RUSH while it holds <7 slots (steep tier 4/5/6 probabilities so bots contest young pools
		// hard), reverting to the NORMAL ladder once it reaches 7. ND.6e — a NORMAL pool inside the final
		// 7 days of its rolling window rolls the one-Fibonacci-level-up URGENCY table instead. The
		// per-slot "re-bid NN%" is the live chance a casino-bot occupying THAT slot re-bids when picked —
		// ND.8d now factors the occupant's OWN bid-count (tiers 2/3), and a player-held slot shows none.
		int occupied = summary.TrackedDonations.Count;
		bool urgent = NetworkRoot.IsAuctionInUrgencyWindow(summary.WindowCloseUnixMs, nowMs);
		string mode = occupied < 7
			? "early-rush bidding (pool <7 slots)"
			: urgent
				? "normal bidding — FINAL-WEEK URGENCY (≤7d left)"
				: "normal bidding (pool ≥7 slots)";
		_sectionTitleLabel.Text = $"Tracked Donation Pool ({occupied}/10 by value) — {mode}";

		ClearContent();

		// ND.10b — Ch. 29: an HBoxContainer (never HSplit) inside the existing bounded scroll. Left = bids,
		// right = the per-bot real leading-bid roll. mouse_filter Pass everywhere so the wheel reaches the
		// scroll (Ch. 29) AND tooltips still show (Pass, not Ignore).
		var columns = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
		columns.AddThemeConstantOverride("separation", 24);
		_contentVBox.AddChild(columns);

		var leftCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
		columns.AddChild(leftCol);
		columns.AddChild(new VSeparator());
		var rightCol = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0), MouseFilter = MouseFilterEnum.Pass };
		columns.AddChild(rightCol);

		// LEFT — the tracked-pool bids.
		if (occupied == 0)
		{
			leftCol.AddChild(new Label { Text = "No qualifying donations tracked yet.", MouseFilter = MouseFilterEnum.Pass });
		}
		else
		{
			// ND.8d.2 — each slot's live re-bid % factors the OCCUPANT's own bid-count (how many tracked
			// slots that donor holds here); a slot held by the PLAYER shows no probability at all (blank).
			Dictionary<string, int> slotsByDonor = summary.TrackedDonations
				.GroupBy(d => d.DonorAddress)
				.ToDictionary(g => g.Key, g => g.Count());

			int tier = 1;
			foreach (TrackedDonation d in summary.TrackedDonations.OrderByDescending(d => d.AmountBtc))
			{
				string scValue = d.CurrentValueSc is decimal sc
					? string.Create(CultureInfo.InvariantCulture, $"  (≈ {sc:F8} SC today)")
					: string.Empty;
				int ownBidCount = slotsByDonor.TryGetValue(d.DonorAddress, out int c) ? c : 1;
				// ND.8d round-3 label parity — reflects the roll's max(mode rate, stuck-single-bidder
				// escalation); ND.10b — "guard" (self-eviction) now shown instead of a bare "0%".
				string prob = _networkRoot.IsPlayerBidderAddress(d.DonorAddress)
					? string.Empty // the player bids manually; never a ladder re-bid probability
					: _networkRoot.ReBidProbabilityLabelForSlot(summary, d.DonorAddress, tier, occupied, urgent, ownBidCount);
				string probCol = prob switch
				{
					"" => string.Empty,
					"satisfied" => "[satisfied]  ",
					"guard" => "[guard]  ",
					_ => $"[re-bid {prob}]  ",
				};
				string line = string.Create(CultureInfo.InvariantCulture,
					$"#{tier}  {probCol}{_networkRoot.DescribeAddress(d.DonorAddress)}  {d.AmountBtc:F8} BTC{scValue}  — {FormatDate(d.TimestampMs)}");
				leftCol.AddChild(new Label { Text = line, MouseFilter = MouseFilterEnum.Pass });
				tier++;
			}
		}

		// RIGHT — per-bot real leading-bid roll for THIS pool (tooltip = each bot's full per-pool breakdown).
		BuildBotRollPanel(rightCol, summary, ledger);
	}

	// ND.10b — the 4 casino bots and each one's REAL chance to place the leading bid in THIS pool this block
	// (conditional on running its pipeline), diluted across all in-auction pools by priority order — the
	// single-source computation in NetworkRoot.RealLeadingBidRoll. Each row's tooltip lists that bot's full
	// per-pool distribution. Bots only (the player bids manually, so no player row).
	private void BuildBotRollPanel(VBoxContainer col, NonMinerDonationSummary summary, IReadOnlyList<NonMinerDonationSummary> ledger)
	{
		var title = new Label { Text = "Real leading-bid roll — this pool", MouseFilter = MouseFilterEnum.Pass };
		title.AddThemeFontSizeOverride("font_size", 16);
		col.AddChild(title);
		col.AddChild(new Label { Text = "(if the bot runs its pipeline this block)", MouseFilter = MouseFilterEnum.Pass });

		Dictionary<string, List<(string poolNodeId, string poolName, int percent)>> rollByBot =
			_networkRoot.RealLeadingBidRoll(ledger);

		foreach (KeyValuePair<string, List<(string poolNodeId, string poolName, int percent)>> kv
			in rollByBot.OrderBy(k => k.Key, StringComparer.Ordinal))
		{
			(string poolNodeId, string poolName, int percent) hit =
				kv.Value.FirstOrDefault(e => e.poolNodeId == summary.NonMinerNodeId);
			int pct = hit.poolNodeId != null ? hit.percent : 0;

			string tip = kv.Value.Count > 0
				? "Where this bot's bid lands this block:\n" + string.Join("\n",
					kv.Value.Select(e => string.Create(CultureInfo.InvariantCulture, $"  {e.poolName}: {e.percent}%")))
				: "No qualifying pool this block.";
			col.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture, $"{kv.Key}     {pct}%"),
				MouseFilter = MouseFilterEnum.Pass,
				TooltipText = tip
			});
		}
	}

	// (ND.8b.2's ShowSettlementSummary stand-in was removed at ND.8b.4 — the resolved view now lives in
	// the CompanyDetails scene, reached via the forward in RefreshAll above.)

	private void ClearContent()
	{
		foreach (Node child in _contentVBox.GetChildren())
			child.QueueFree();
	}

	private static string FormatDate(long unixMs) =>
		DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
}
