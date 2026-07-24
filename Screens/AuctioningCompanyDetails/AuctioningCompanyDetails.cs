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

	// ND.10f (2026-07-23) — the auction-time twin of CompanyDetails' ND.9b holding-keyed page border, using
	// the identical three colours but keyed on the PROJECTED stake: gold when the player currently occupies
	// a top-3 tier of this pool (it would mint NST if the auction closed at this block), silver when it holds
	// only lower tiers (PST), black when it holds no tracked slot at all. Same transparent-centre
	// StyleBoxFlat overlay behind the content, mouse-transparent — Ch. 29-safe, never touches the layout.
	private Panel _borderPanel = null!;
	private StyleBoxFlat _borderStyle = null!;
	private static readonly Color HoldingGold = new(0.85f, 0.65f, 0.13f);   // would mint NST
	private static readonly Color HoldingSilver = new(0.75f, 0.75f, 0.78f); // would mint PST
	private static readonly Color HoldingBlack = new(0.05f, 0.05f, 0.05f);  // no tracked slot

	public override void _Ready()
	{
		_networkRoot = GetNode<NetworkRoot>("NetworkRoot");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		BuildBorderOverlay();

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

		// ND.10f — the page border + caption: what the player WOULD mint if this pool closed right now.
		// A running forecast, not a promise — every later bid can re-order the tracked pool beneath it.
		PlayerAuctionStake stake = _networkRoot.GetPlayerProjectedStake(summary);
		_borderStyle.BorderColor = stake switch
		{
			PlayerAuctionStake.Nst => HoldingGold,
			PlayerAuctionStake.Pst => HoldingSilver,
			_ => HoldingBlack
		};
		string stakeCaption = stake switch
		{
			PlayerAuctionStake.Nst => "NST (voting shares)",
			PlayerAuctionStake.Pst => "PST (dividend shares)",
			_ => "nothing (no tracked slot)"
		};
		_statusLabel.Text = $"In Auction — {clock}  |  If it closed now you would mint: {stakeCaption}";

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
					"priced out" => "[priced out]  ", // ND.10d — the raise no longer fits its half-spendable cap
					"reserve" => "[reserve]  ",      // ND.10e — the bot is rebuilding its BTC reserve guard
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

	// ND.10c — the 4 casino bots and each one's REAL chance to place the leading bid in THIS pool, as a
	// TRUE PER-BLOCK probability: the single-source computation in NetworkRoot.RealLeadingBidRoll folds in
	// the parallel per-pool rolls + their uniform tie-break, the eligible-bot draw (roll 1) AND the 0/1/2
	// count draw. A 0% therefore means genuinely impossible (satisfied / self-eviction guard / unaffordable),
	// never merely "the selection walk never reached this pool" — the ND.10b artifact this replaced.
	// Two decimals on purpose: realistic values sit in 0.10%–25%, which integer percent rounds to 0%.
	// Derivation + worked example: Documentation/ProjectDesignManual.md §22.14. Each row's tooltip lists
	// that bot's full per-pool distribution. Bots only (the player bids manually, so no player row).
	private void BuildBotRollPanel(VBoxContainer col, NonMinerDonationSummary summary, IReadOnlyList<NonMinerDonationSummary> ledger)
	{
		var title = new Label { Text = "Real leading-bid roll — this pool", MouseFilter = MouseFilterEnum.Pass };
		title.AddThemeFontSizeOverride("font_size", 16);
		col.AddChild(title);
		col.AddChild(new Label { Text = "(chance this bot lands the leading bid here, this block)", MouseFilter = MouseFilterEnum.Pass });

		Dictionary<string, List<(string poolNodeId, string poolName, double percent)>> rollByBot =
			_networkRoot.RealLeadingBidRoll(ledger);

		foreach (KeyValuePair<string, List<(string poolNodeId, string poolName, double percent)>> kv
			in rollByBot.OrderBy(k => k.Key, StringComparer.Ordinal))
		{
			(string poolNodeId, string poolName, double percent) hit =
				kv.Value.FirstOrDefault(e => e.poolNodeId == summary.NonMinerNodeId);
			double pct = hit.poolNodeId != null ? hit.percent : 0d;

			// ND.10d — a zero here has four very different meanings; say which. "priced out" (the raise no
			// longer fits the half-spendable cap) is the one that used to read as a contradiction against
			// the left column's ladder %, which ignored affordability entirely.
			string shown = pct switch
			{
				<= 0d => "0.00%",
				< 0.01d => "<0.01%", // a real but tiny chance — never round it back into a bare 0%
				_ => string.Create(CultureInfo.InvariantCulture, $"{pct:F2}%"),
			};
			string note = pct > 0d ? string.Empty : _networkRoot.BotPoolExclusionNote(summary, kv.Key);
			if (note.Length > 0) shown += $"  ({note})";

			string tip = kv.Value.Count > 0
				? "Where this bot's bid lands this block:\n" + string.Join("\n",
					kv.Value.Select(e => string.Create(CultureInfo.InvariantCulture, $"  {e.poolName}: {e.percent:F2}%")))
				: "No pool this bot can bid this block.";
			col.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture, $"{kv.Key}     {shown}"),
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

	// ND.10f — identical construction to CompanyDetails.BuildBorderOverlay (ND.9b): a mouse-transparent
	// bordered Panel inset 8 px from the screen edge, behind the content (index 0), transparent centre so
	// only the coloured frame shows. Colour is set per refresh from the projected stake above.
	private void BuildBorderOverlay()
	{
		_borderStyle = new StyleBoxFlat
		{
			BgColor = new Color(0, 0, 0, 0),
			BorderColor = HoldingBlack,
			BorderWidthLeft = 4,
			BorderWidthTop = 4,
			BorderWidthRight = 4,
			BorderWidthBottom = 4,
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6
		};

		_borderPanel = new Panel { MouseFilter = MouseFilterEnum.Ignore };
		_borderPanel.AddThemeStyleboxOverride("panel", _borderStyle);
		AddChild(_borderPanel);
		MoveChild(_borderPanel, 0);
		_borderPanel.SetAnchorsPreset(LayoutPreset.FullRect);
		_borderPanel.OffsetLeft = 8;
		_borderPanel.OffsetTop = 8;
		_borderPanel.OffsetRight = -8;
		_borderPanel.OffsetBottom = -8;
	}

	private static string FormatDate(long unixMs) =>
		DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
}
