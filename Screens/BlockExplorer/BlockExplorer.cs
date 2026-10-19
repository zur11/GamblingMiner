using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GodotBlockchainPort.Simulation;
using GodotBlockchainPort.Blockchain;
using Scripts.Hardware;
using UI.StatusBar;
using UI.NotepadPopup;
#nullable enable

public partial class BlockExplorer : Control
{
	private NetworkRoot _networkRoot = null!;
	private SceneManager? _sceneManager;
	private SimulationService? _simulationService;

	private OptionButton _minerNodeOption = null!;
	// ND.10g — the raw node ids behind _minerNodeOption's items, parallel by index. The item TEXT is the
	// DEV display form, so the id must be carried as data (see SelectedLookupNodeId).
	private string[] _selectorNodeIds = [];
	private Label _chainInfoLabel = null!;
	// One scrollable right column (Latest Block + Network Status + Address Directory) — a single
	// internally-scrolling RichTextLabel so the whole column is reachable (incl. Satoshi, last in the directory).
	// Mini-plan 25 (developer, 2026-10-19) — the right side is FOUR fixed panels in a 2×2 grid, each its own
	// RichTextLabel with its own internal scroll (Ch. 29 §29.2 Pattern B, four times), instead of ONE label holding all
	// of it. With one label, every block changed the length of the whole text (the latest block's transaction list
	// above everything else), so the scroll position jumped and navigating at 9000X was hard. Now each panel's size is
	// fixed by the grid, each keeps its own scroll position across refreshes, and nothing scrolls inside a scroll.
	// The casino players (player + bot_1..4) have their own panel, out of Network Status.
	private RichTextLabel _latestBlockLabel = null!;
	private RichTextLabel _casinoPlayersLabel = null!;
	private RichTextLabel _networkStatusLabel = null!;
	private RichTextLabel _addressDirectoryLabel = null!;

	private LineEdit _txLookupInput = null!;
	private LineEdit _addressLookupInput = null!;
	private LineEdit _blockLookupInput = null!;
	private RichTextLabel _lookupResultLabel = null!;

	// Notepad
	private NotepadPopup _notepadPopup = null!;

	// Enroll Mode (referral-auction foundation) — built programmatically
	private CheckBox _enrollModeToggle = null!;
	private VBoxContainer _enrollModePanel = null!;
	private Label _enrollModeSummaryLabel = null!;
	private VBoxContainer _enrollModeRowsVBox = null!;

	// ND.9b (extended 2026-07-22) — the same holding-keyed border colours CompanyDetails uses, applied
	// here around each FOUNDED company's title: gold when the player holds NST, silver PST, black neither.
	// ND.10f (2026-07-23) — the identical three colours now also frame each STILL-OPEN auction pool's
	// title, keyed on the PROJECTED stake (what would be minted if the pool closed at the current block).
	private static readonly Color HoldingGold = new(0.85f, 0.65f, 0.13f);
	private static readonly Color HoldingSilver = new(0.75f, 0.75f, 0.78f);
	private static readonly Color HoldingBlack = new(0.05f, 0.05f, 0.05f);

	// ND.10h (2026-07-23, D-ND10h.1) — the founded row's PENDING-WORK colours. A different axis from the
	// stake borders above: those say "what do I own here?", these say "what must I do here?" — a gold
	// border beside a green Claim → reads "you hold NST and have a dividend waiting". They share only the
	// convention that BLACK MEANS NOTHING, which is why the idle button is black-bordered rather than bare.
	private static readonly Color WorkRed = new(1f, 0.3f, 0.3f);      // #FF4D4D — board vote pending (unchanged)
	private static readonly Color WorkGreen = new(0.3f, 0.85f, 0.48f); // #4DD97A — dividends waiting
	// MOCHA (both). NOT an RGB average of the two above — that gives #A6C364, a muddy yellow-green, the
	// opposite of the requested "mixed like pigments". Pigment mixing is SUBTRACTIVE: in CMY,
	// #FF4D4D → (0, .70, .70) and #4DD97A → (.70, .15, .52); summed and clamped → (.70, .85, 1.0) → #4D2600,
	// a deep brown. That is the correct HUE but unreadable as a font colour on a dark theme, so the shipped
	// constant keeps the hue and lifts the value for legibility. Do not "correct" this toward yellow.
	private static readonly Color WorkMocha = new(0.75f, 0.52f, 0.32f); // #C08552

	// Mini-plan 25 17.C.2 (BlockExplorer) — what this page shows, and what moves it. It was rebuilt EVERY real second by a
	// timer: 63 nodes' balances (a UTXO pass each, in Network Status), the address directory, and with Auction Mode on,
	// the whole auction ledger (a walk of the entire chain). Everything on it changes only when:
	//   • a block is mined (latest block, difficulty, balances, blocks mined, auctions);
	//   • a transaction enters the mempool (pending counts, balances);
	//   • hardware changes, or the sim starts or stops (the mining rates in Network Status);
	//   • and the auction COUNTDOWN, which moves with the game clock.
	// So the page now rebuilds on those EVENTS through a leading-edge throttle at the old 1 s (0/s at rest, never more
	// than before under load), and the countdown alone is rewritten once a real second while the clock moves, from the
	// close times cached at the last rebuild, without recomputing anything. Its tenths-of-a-day precision at a 1 s
	// cadence is the documented exception in ProjectDesignManual §29.13.1 (arithmetic beside RefreshEnrollMode).
	private bool _uiDirty = true;
	private double _sinceLastRefresh = double.MaxValue;
	private const double MinRefreshInterval = 1.0;
	private bool _lastSimRunning;
	private CalendarTimeService? _calendar;
	private readonly List<(Label Label, string Prefix, long CloseMs)> _countdownRows = new();
	private double _sinceCountdownPaint;
	private const double CountdownIntervalSeconds = 1.0;
	private DateTime _lastCountdownClock;

	public override void _Ready()
	{
		_networkRoot = GetNode<NetworkRoot>("NetworkRoot");
		_minerNodeOption = GetNode<OptionButton>("%MinerNodeOption");

		_chainInfoLabel = GetNode<Label>("%ChainInfoLabel");
		_latestBlockLabel = GetNode<RichTextLabel>("%LatestBlockLabel");
		_casinoPlayersLabel = GetNode<RichTextLabel>("%CasinoPlayersLabel");
		_networkStatusLabel = GetNode<RichTextLabel>("%NetworkStatusLabel");
		_addressDirectoryLabel = GetNode<RichTextLabel>("%AddressDirectoryLabel");

		_txLookupInput = GetNode<LineEdit>("%TxLookupInput");
		_addressLookupInput = GetNode<LineEdit>("%AddressLookupInput");
		_blockLookupInput = GetNode<LineEdit>("%BlockLookupInput");
		_lookupResultLabel = GetNode<RichTextLabel>("%LookupResultLabel");

		GetNode<Button>("%LookupTxButton").Pressed      += OnLookupTransactionPressed;
		GetNode<Button>("%LookupAddressButton").Pressed += OnLookupAddressPressed;
		GetNode<Button>("%LookupBlockButton").Pressed   += OnLookupBlockPressed;
		GetNode<Button>("%BackToDiceButton").Pressed    += OnBackToDicePressed;
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		_simulationService = GetNodeOrNull<SimulationService>("/root/SimulationService");

		// A dedicated "Go to DiceGame" button (the existing one goes to the Main Menu).
		var goToDiceBtn = new Button { Text = "Go to DiceGame" };
		goToDiceBtn.Pressed += OnGoToDicePressed;
		Button backBtn = GetNode<Button>("%BackToDiceButton");
		backBtn.GetParent().AddChild(goToDiceBtn);
		backBtn.GetParent().MoveChild(goToDiceBtn, backBtn.GetIndex() + 1);

		var mainVBox = GetNode<VBoxContainer>("Margin/MainVBox");
		var statusBar = new StatusBar();
		mainVBox.AddChild(statusBar);
		mainVBox.MoveChild(statusBar, 0);

		var devTimeScale = new UI.DevTimeScaleSelector.DevTimeScaleSelector();
		mainVBox.AddChild(devTimeScale);
		mainVBox.MoveChild(devTimeScale, 1);

		_notepadPopup = new NotepadPopup();
		AddChild(_notepadPopup);
		GetNode<Button>("%NotepadBtn").Pressed += _notepadPopup.Open;

		BuildEnrollModePanel();

		PopulateNodeSelectors();
		_calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		_lastSimRunning = _calendar?.IsAutobetActive == true;
		RefreshUi();
		_uiDirty = false;
		_sinceLastRefresh = 0d;

		NetworkRoot.BlockAccepted += OnBlockAccepted;
		NetworkRoot.MempoolChanged += MarkUiDirty;
		HardwareAllocationRepository.HardwareChanged += OnHardwareChanged;
	}

	public override void _ExitTree()
	{
		// All three events are static: unsubscribe, or this scene stays reachable after it leaves.
		NetworkRoot.BlockAccepted -= OnBlockAccepted;
		NetworkRoot.MempoolChanged -= MarkUiDirty;
		HardwareAllocationRepository.HardwareChanged -= OnHardwareChanged;
	}

	private void OnBlockAccepted(Block _) => _uiDirty = true;
	private void OnHardwareChanged(string _) => _uiDirty = true;
	private void MarkUiDirty() => _uiDirty = true;

	// "Enroll Mode" (referral-auction foundation): a toggle that reveals the donation race for
	// still-recruitable non-miner holder bots. Observe-only for now — enrolled/permanent filtering
	// activates once auction resolution (window timing) and the economy land.
	private void BuildEnrollModePanel()
	{
		// Toggle lives in the top action bar (always visible).
		var topActions = GetNode<HBoxContainer>("Margin/MainVBox/TopActions");
		// ND.9a — the toggle now surfaces both still-recruitable auctions AND founded companies, so it is
		// labelled "Auction / Company Mode" (was "Enroll Mode"). Deferred Basic-Mode objective (non-priority,
		// see PRIVATE_ROADMAP / ProjectDesignManual §22.3): in Basic Mode this reverts to Auction Mode only,
		// and founded companies move to their own dedicated scene/list. Internal ids keep the "enrollMode"
		// name (code-facing, not player-facing).
		_enrollModeToggle = new CheckBox { Text = "Auction / Company Mode" };
		_enrollModeToggle.Toggled += _ => RefreshEnrollMode();
		topActions.AddChild(_enrollModeToggle);

		// Panel sits just below the top bar (above the main split), so it's visible when toggled on.
		// Step 14 (ND.5d) — a scrollable VBox of rows (was a single RichTextLabel blob) so each recruitable
		// non-miner can carry its own "Details →" button (D-ND5.2) alongside its leading-bid info.
		var mainVBox = GetNode<VBoxContainer>("Margin/MainVBox");
		_enrollModePanel = new VBoxContainer { Visible = false };

		_enrollModeSummaryLabel = new Label();
		_enrollModePanel.AddChild(_enrollModeSummaryLabel);

		var enrollScroll = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(0, 220),
			SizeFlagsVertical = SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
		};
		_enrollModeRowsVBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
		enrollScroll.AddChild(_enrollModeRowsVBox);
		_enrollModePanel.AddChild(enrollScroll);

		mainVBox.AddChild(_enrollModePanel);
		mainVBox.MoveChild(_enrollModePanel, topActions.GetIndex() + 1);
	}

	private void RefreshEnrollMode()
	{
		// The rows are about to be rebuilt (or hidden), so the countdown cache is rebuilt with them: a cached label
		// from the previous build is queued for deletion below.
		_countdownRows.Clear();
		bool on = _enrollModeToggle.ButtonPressed;
		_enrollModePanel.Visible = on;
		if (!on) return;

		var ledger = _networkRoot.GetNonMinerAuctionLedger();
		// Mini-plan 25 (developer, 2026-10-19) — the countdown reads the LIVE game clock, not the chain tip's timestamp,
		// which moved only when a block was mined. It is a live-clock readout WITHOUT a sampler, deliberately, and this
		// is why that is honest (§29.13's rule: cadence and precision are one decision). It shows tenths of a day
		// (8,640 game-seconds) and repaints on this page's 1 s refresh. At the clock's ceiling (9000X) one refresh
		// advances the tenths digit by ~1.04 steps, so it visibly moves every refresh. A freeze needs a step near 10
		// (~86,400 game-seconds per real second), which the game cannot reach, and slower speeds only move it more
		// slowly. Recorded as the one exception in §29.13.1.
		long nowMs = new DateTimeOffset(
			GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService")?.CurrentUtcDateTime
				?? DateTimeOffset.FromUnixTimeMilliseconds(_networkRoot.GetPlayerLatestBlock().Timestamp).UtcDateTime)
			.ToUnixTimeMilliseconds();

		int inAuction = ledger.Count(s => s.Status == NonMinerAuctionStatus.InAuction);
		int resolved = ledger.Count(s => s.Status == NonMinerAuctionStatus.Resolved);
		int notYet = ledger.Count(s => s.Status == NonMinerAuctionStatus.NotIntroduced);
		_enrollModeSummaryLabel.Text =
			$"Auction / Company Mode — referral auction & founded companies   |   In auction (recruitable): {inAuction}  |  Resolved: {resolved}  |  Not yet introduced: {notYet}";

		foreach (Node child in _enrollModeRowsVBox.GetChildren())
			child.QueueFree();

		// EB.2 (D-EB.6/7): the leader and the countdown reflect only QUALIFYING bids. Mini-plan 25 §4.5: so do the
		// donors (BidderCount, the same figure AuctioningCompanyDetails shows, D-17.4), and the rest of the
		// funding is the company's BTC income. A non-miner with no qualifying bid yet shows
		// "awaiting first bid" — its window countdown has not started and it stays recruitable.
		// ND.4b (D-ND4b.10, corrected 2026-07-11): the leading bid's BTC principal now also shows its
		// LIVE, CURRENT SC value (priced as of now, never frozen at the bid's own day).
		foreach (NonMinerDonationSummary s in ledger.Where(s => s.Status == NonMinerAuctionStatus.InAuction))
		{
			string scValue = s.LeadingDonorScValue is decimal sc
				? string.Create(CultureInfo.InvariantCulture, $" ≈ {sc:F8} SC")
				: string.Empty;
			string leader = string.IsNullOrEmpty(s.LeadingDonorAddress)
				? "no bids yet"
				: string.Create(CultureInfo.InvariantCulture, $"leading bid {_networkRoot.DescribeAddress(s.LeadingDonorAddress)} ({s.LeadingDonorTotal:F8} BTC{scValue})");
			string clock = s.LeadingBidUnixMs == 0
				? "awaiting first bid — no countdown"
				: CountdownText(s.WindowCloseUnixMs, nowMs);
			// Everything before the countdown is fixed until the next rebuild, so it is kept apart: the once-a-second
			// countdown tick rewrites only the end of the line (UpdateCountdowns).
			string prefix = string.Create(CultureInfo.InvariantCulture,
				$"{NetworkRoot.DescribeCompany(s)}  | donated {s.DonatedBtc:F8} BTC by {s.BidderCount} {(s.BidderCount == 1 ? "donor" : "donors")}  | BTC income {s.CompanyBtcIncome:F8} BTC  | {leader}  | ");
			string line = prefix + clock;

			var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };

			// ND.10f (2026-07-23) — the SAME gold/silver/black scheme the founded rows below use, extended
			// to a still-OPEN auction as a live PROJECTION: if this pool closed at the current block, gold =
			// the player would mint NST (it holds a top-3 tier), silver = PST only (lower tiers), black = no
			// tracked slot at all, so nothing would be minted. Every later bid can re-order the pool, so this
			// is a running forecast, never a promise. Same threshold/ranking as FoundCompany by construction
			// (NetworkRoot.GetPlayerProjectedStake) — the colour cannot drift from the real mint.
			var rowLabel = new Label { Text = line, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
			if (s.LeadingBidUnixMs != 0)
			{
				_countdownRows.Add((rowLabel, prefix, s.WindowCloseUnixMs));
			}
			row.AddChild(BuildTitlePanel(rowLabel, StakeBorderColor(_networkRoot.GetPlayerProjectedStake(s))));

			// D-ND5.2 — the details button appears ONLY once this non-miner has received at least one
			// qualifying bid (a leader exists); a never-bid-on entry has nothing to show yet.
			if (s.LeadingBidUnixMs != 0)
			{
				var detailsBtn = new Button { Text = "Details →" };
				string nonMinerAddress = s.NonMinerAddress;
				detailsBtn.Pressed += () => OnOpenAuctioningCompanyDetails(nonMinerAddress);
				row.AddChild(detailsBtn);
			}

			_enrollModeRowsVBox.AddChild(row);
		}

		if (resolved > 0)
		{
			_enrollModeRowsVBox.AddChild(new Label { Text = "Founded (out of auction):" });
			// ND.8b.4 (D-ND8.16) — a founded company's row now carries its own "Details →" into the new
			// CompanyDetails scene (founding summary + the holding-gated Board Vote / dividend panels).
			// AuctioningCompanyDetails stays the InAuction-only view (D-ND5.9).
			// ND.8b.3 follow-up (developer-requested): a company whose open board vote is WAITING FOR THE
			// PLAYER'S BALLOT paints its whole row red — the locator for "which company paused my game".
			// ND.10h (2026-07-23) — that red is now one of FOUR states, so the row also answers "is there
			// money here I forgot to collect?" (see PendingWorkColor). Dividends never block play, which is
			// exactly why they were easy to miss — the ND.8g playtest found a genuinely unclaimed PST
			// balance sitting untouched in ArtForz Cluster.
			var awaitingVote = new HashSet<string>(
				NetworkRoot.GetCompaniesAwaitingPlayerVote().Select(c => c.nonMinerNodeId));
			foreach (NonMinerDonationSummary s in ledger.Where(s => s.Status == NonMinerAuctionStatus.Resolved))
			{
				// Step 15 P15.5d — a DISSOLVED company still shows here (the auction ledger is chain-derived
				// and stays Resolved forever, D-ND4b.12), but it has no founding, no governance, no holdings
				// and nothing to act on. Give it its own terminal row rather than an empty "Details →".
				if (NetworkRoot.IsCompanyClosed(s.NonMinerNodeId))
				{
					_enrollModeRowsVBox.AddChild(BuildClosedCompanyRow(s));
					continue;
				}

				// ND.8b.2 — "founded" replaces the old SC-cashback "referral" wording (D-ND8.14): the
				// leading bidder now controls the company's founding stock mint, not a one-shot payout.
				string founder = string.IsNullOrEmpty(s.WinnerAddress)
					? "no leading bidder (legacy pre-EB.2 world)"
					: $"founded by {_networkRoot.DescribeAddress(s.WinnerAddress)}";
				bool votePending = awaitingVote.Contains(s.NonMinerNodeId);
				// ND.10h — the second half of the state: a dividend the player could ACTUALLY collect
				// (dust below the day's fee does not count — D-ND10h.3, the predicate is shared with the
				// Claim panel that performs the action).
				bool canClaim = _networkRoot.HasPlayerClaimableDividends(s.NonMinerNodeId);

				var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
				var rowLabel = new Label
				{
					// The ⚠ prefix stays the dedicated, unambiguous channel for "your ballot is pausing the
					// game" — colour is never the only signal (§22.15).
					Text = votePending
						? $"⚠ BOARD VOTE PENDING — {NetworkRoot.DescribeCompany(s)}  | {founder}"
						: $"{NetworkRoot.DescribeCompany(s)}  | {founder}",
					MouseFilter = MouseFilterEnum.Pass
				};
				// D-ND10h.5 — the text carries the state too: a blocking vote outranks a non-blocking claim.
				var detailsBtn = new Button
				{
					Text = votePending ? "Vote →" : canClaim ? "Claim →" : "Details →"
				};
				// D-ND10h.4 — label and button always share the state colour, so the row is scannable
				// without the eye having to land on the button.
				Color workColor = PendingWorkColor(votePending, canClaim);
				if (votePending || canClaim)
				{
					rowLabel.AddThemeColorOverride("font_color", workColor);
					detailsBtn.AddThemeColorOverride("font_color", workColor);
				}

				ApplyButtonBorder(detailsBtn, workColor);

				// ND.9b (2026-07-22) — a holding-keyed coloured border around the company TITLE (same
				// gold/silver/black scheme as CompanyDetails). Independent of the vote-pending red text —
				// both signals coexist (red = "your ballot is blocking play", border = "your stake here").
				CompanyFounding? founding = _networkRoot.GetCompanyFounding(s.NonMinerAddress);
				CompanyShareHolding? playerHolding = founding?.Holdings.FirstOrDefault(h => h.HolderId == "player");
				Color borderColor = playerHolding is { Nst: > 0m } ? HoldingGold
					: playerHolding is { Pst: > 0m } ? HoldingSilver
					: HoldingBlack;
				row.AddChild(BuildTitlePanel(rowLabel, borderColor));

				string nonMinerAddress = s.NonMinerAddress;
				detailsBtn.Pressed += () => OnOpenCompanyDetails(nonMinerAddress);
				row.AddChild(detailsBtn);
				_enrollModeRowsVBox.AddChild(row);
			}
		}
	}

	// Step 15 P15.5d (D-15.15) — the terminal row for a dissolved company: WHEN it closed, WHY, and — the
	// part that matters to the player — what they lost with it. Deliberately has NO action button: the
	// tokens are destroyed and there is nothing left to vote on or claim (§39.12).
	private Control BuildClosedCompanyRow(NonMinerDonationSummary s)
	{
		CompanyClosure? closure = NetworkRoot.GetCompanyClosure(s.NonMinerNodeId);
		string when = closure == null
			? string.Empty
			: $" on {DateTimeOffset.FromUnixTimeMilliseconds(closure.ClosedAtUnixMs).LocalDateTime:yyyy-MM-dd}";
		string why = closure?.Reason == NetworkRoot.ClosureReasonFbiSeizure ? "seized by the FBI" : "closed — debt default";

		string lost = string.Empty;
		if (closure is { PlayerNstAtClosure: > 0m } or { PlayerPstAtClosure: > 0m })
		{
			string kind = closure.PlayerNstAtClosure > 0m ? "NST" : "PST";
			decimal amount = closure.PlayerNstAtClosure > 0m ? closure.PlayerNstAtClosure : closure.PlayerPstAtClosure;
			lost = string.Create(CultureInfo.InvariantCulture, $"   |   you lost {amount:N0} {kind}");
			if (closure.PlayerUnclaimedBtcAtClosure > 0m || closure.PlayerUnclaimedScAtClosure > 0m)
			{
				lost += string.Create(CultureInfo.InvariantCulture,
					$" + {closure.PlayerUnclaimedBtcAtClosure:N8} BTC / {closure.PlayerUnclaimedScAtClosure:N2} SC unclaimed");
			}
		}

		var label = new Label
		{
			Text = $"✗ {NetworkRoot.DescribeCompany(s)}  | {why}{when}{lost}",
			MouseFilter = MouseFilterEnum.Pass
		};
		label.AddThemeColorOverride("font_color", ClosedCompanyGrey);
		// Black border: a dissolved company is a stake you no longer hold (§22.15's vocabulary).
		return BuildTitlePanel(label, HoldingBlack);
	}

	private static readonly Color ClosedCompanyGrey = new Color(0.55f, 0.55f, 0.55f);

	// ND.10f — the projected-stake → border-colour mapping, shared by the open-auction rows above and (by
	// the same three constants) the founded rows: gold NST, silver PST, black nothing.
	private static Color StakeBorderColor(PlayerAuctionStake stake) => stake switch
	{
		PlayerAuctionStake.Nst => HoldingGold,
		PlayerAuctionStake.Pst => HoldingSilver,
		_ => HoldingBlack
	};

	// ND.10h — the four-state pending-work colour. A pure function of the CURRENT state: voting clears the
	// vote flag and claiming clears the claimable, so nothing about what the player did last visit needs to
	// be remembered (no history record, no persisted state, no event — it is re-evaluated each refresh).
	private static Color PendingWorkColor(bool votePending, bool canClaim) =>
		votePending && canClaim ? WorkMocha
		: votePending ? WorkRed
		: canClaim ? WorkGreen
		: HoldingBlack;

	// ND.10h (D-ND10h.2) — the state colour as a border on the button itself. A Button has FOUR stylebox
	// states; overriding only "normal" makes the border vanish on hover, which reads as a rendering bug —
	// so all four get their own duplicate. (ND.10f's BuildTitlePanel is the neighbouring precedent but is a
	// PanelContainer wrapper, which a Button cannot reuse without swallowing its own click styling.)
	private static void ApplyButtonBorder(Button button, Color borderColor)
	{
		foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
		{
			StyleBox? existing = button.GetThemeStylebox(state);
			var style = existing?.Duplicate() as StyleBoxFlat ?? new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
			style.BorderColor = borderColor;
			style.BorderWidthLeft = 2;
			style.BorderWidthTop = 2;
			style.BorderWidthRight = 2;
			style.BorderWidthBottom = 2;
			button.AddThemeStyleboxOverride(state, style);
		}
	}

	// ND.10f — hoisted from the founded-rows loop (ND.9b) so both row kinds build an identical bordered
	// title: a transparent-centre StyleBoxFlat around the label, mouse-transparent so the wheel still
	// reaches the enclosing scroll (Ch. 29).
	private static PanelContainer BuildTitlePanel(Label titleLabel, Color borderColor)
	{
		var titleStyle = new StyleBoxFlat
		{
			BgColor = new Color(0, 0, 0, 0),
			BorderColor = borderColor,
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			ContentMarginLeft = 8,
			ContentMarginRight = 8,
			ContentMarginTop = 3,
			ContentMarginBottom = 3
		};
		var titlePanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
		titlePanel.AddThemeStyleboxOverride("panel", titleStyle);
		titlePanel.AddChild(titleLabel);
		return titlePanel;
	}

	// The auction countdown, one source for the rebuild and the tick. At zero it says so: an auction resolves on the first
	// block at or after its close (see AuctioningCompanyDetails.PaintStatus).
	private static string CountdownText(long closeMs, long nowMs) =>
		closeMs > nowMs
			? string.Create(CultureInfo.InvariantCulture, $"{(closeMs - nowMs) / 86_400_000d:0.0}d left")
			: "closed — resolves with the next block";

	// Mini-plan 25 — rewrites only the countdown at the end of each live auction row, from the cache the last rebuild
	// filled. No ledger walk and no balance pass: those run on the events above.
	private void UpdateCountdowns()
	{
		Scripts.Diagnostics.UiRefreshProbe.Count("BlockExplorer.UpdateCountdowns");   // DEBUG-only, mini-plan 25
		long nowMs = new DateTimeOffset(_calendar!.CurrentUtcDateTime).ToUnixTimeMilliseconds();
		foreach ((Label label, string prefix, long closeMs) in _countdownRows)
		{
			if (GodotObject.IsInstanceValid(label))
			{
				label.Text = prefix + CountdownText(closeMs, nowMs);
			}
		}
	}

	private void OnOpenAuctioningCompanyDetails(string nonMinerAddress)
	{
		AuctioningCompanyDetails.PendingNonMinerAddress = nonMinerAddress;
		_sceneManager?.Go(SceneManager.SceneId.AuctioningCompanyDetails);
	}

	private void OnOpenCompanyDetails(string nonMinerAddress)
	{
		CompanyDetails.PendingNonMinerAddress = nonMinerAddress;
		_sceneManager?.Go(SceneManager.SceneId.CompanyDetails);
	}

	public override void _Process(double delta)
	{
		// The sim starting or stopping changes the mining rates and nothing announces it: a one-bool edge per frame.
		bool simRunning = _calendar?.IsAutobetActive == true;
		if (simRunning != _lastSimRunning)
		{
			_lastSimRunning = simRunning;
			_uiDirty = true;
		}

		_sinceLastRefresh += delta;
		if (_uiDirty && _sinceLastRefresh >= MinRefreshInterval)
		{
			_uiDirty = false;
			_sinceLastRefresh = 0d;
			_sinceCountdownPaint = 0d;
			RefreshUi();
			return;
		}

		// Between rebuilds, only the countdown moves, and only while the clock does.
		_sinceCountdownPaint += delta;
		if (_countdownRows.Count == 0 || _calendar == null || _sinceCountdownPaint < CountdownIntervalSeconds) return;
		_sinceCountdownPaint = 0d;
		DateTime clockNow = _calendar.CurrentLocalDateTime;
		if (clockNow == _lastCountdownClock) return;
		_lastCountdownClock = clockNow;
		UpdateCountdowns();
	}

	private void PopulateNodeSelectors()
	{
		// ND.10g — the item TEXT is the DEV form ("Mt. Gox (non_miner_7)"), but the raw node id is kept as
		// DATA in _selectorNodeIds: the lookup handlers feed the selection straight into
		// BuildTransactionDetails / BuildAddressDetailsForNode, which resolve real nodes by id. Reading the
		// id back out of GetItemText() would have broken every lookup the moment the text changed.
		string[] nodeIds = _networkRoot.GetNodeIds().ToArray();
		_selectorNodeIds = nodeIds;
		_minerNodeOption.Clear();
		foreach (string nodeId in nodeIds)
			_minerNodeOption.AddItem(NetworkRoot.DescribeNodeForDev(nodeId));

		int playerIndex = Array.IndexOf(nodeIds, "player");
		if (playerIndex >= 0)
			_minerNodeOption.Select(playerIndex);
	}

	// The node id behind the currently selected item (ND.10g — never GetItemText, that is display text).
	private string SelectedLookupNodeId()
	{
		int index = _minerNodeOption.Selected;
		return index >= 0 && index < _selectorNodeIds.Length ? _selectorNodeIds[index] : "player";
	}

	private string BuildAddressDirectory()
	{
		return "[b]Node -> Address[/b]\n" + string.Join("\n", _networkRoot.GetNodeAddressLines());
	}

	private void OnLookupTransactionPressed()
	{
		string txId = _txLookupInput.Text.Trim();
		if (string.IsNullOrEmpty(txId)) { _lookupResultLabel.Text = "Enter a transaction hash first."; return; }
		string nodeId = SelectedLookupNodeId();
		SetLookupResult("[b]Transaction Lookup[/b]\n" + _networkRoot.BuildTransactionDetails(nodeId, txId));
	}

	private void OnLookupAddressPressed()
	{
		string address = _addressLookupInput.Text.Trim();
		if (string.IsNullOrEmpty(address)) { _lookupResultLabel.Text = "Enter an address first."; return; }
		string nodeId = SelectedLookupNodeId();
		SetLookupResult("[b]Address Lookup[/b]\n" + _networkRoot.BuildAddressDetailsForNode(nodeId, address));
	}

	private void OnLookupBlockPressed()
	{
		if (!int.TryParse(_blockLookupInput.Text.Trim(), out int blockIndex) || blockIndex <= 0)
		{
			_lookupResultLabel.Text = "Enter a valid positive block number.";
			return;
		}

		string nodeId = SelectedLookupNodeId();
		Block? block = _networkRoot.GetBlockByIndexForNode(nodeId, blockIndex);
		if (block is null) { _lookupResultLabel.Text = $"Block {blockIndex} not found for node {nodeId}."; return; }

		StringBuilder sb = new();
		sb.AppendLine("[b]Block Lookup[/b]");
		sb.AppendLine($"Node: {nodeId}");
		sb.AppendLine($"Index: {block.Index}");
		sb.AppendLine($"Time: {FormatBlockTime(block.Timestamp)}");
		sb.AppendLine($"Hash: {block.Hash}");
		sb.AppendLine($"PrevHash: {block.PreviousBlockHash}");
		sb.AppendLine($"MerkleRoot: {block.MerkleRoot}");
		sb.AppendLine($"Nonce: {block.Nonce}");
		sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
			$"Difficulty: {block.Difficulty:F2}  (~{block.Difficulty:F0} attempts/block)"));
		// Step 16 P16.2f — the OQ-8.2 cosmetic filters are GONE. Every spending participant now carries a
		// DerivedAddressWallet, so change lands on a fresh address and there is no self-loop left to hide.
		// The block's transactions are shown exactly as they are on-chain, which is the only reading whose
		// arithmetic adds up (a hidden change output made a real spend look like it lost coins).
		List<Transaction> visible = block.Transactions;
		decimal blockFees = visible.Where(t => !t.IsCoinbase).Sum(t => t.Fee);
		sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
			$"Transactions: {visible.Count}  |  Fees collected: {blockFees:F8} BTC"));
		foreach (Transaction tx in visible)
		{
			bool isCoinbase = tx.IsCoinbase;
			sb.AppendLine("-");
			sb.AppendLine($"TxId: {tx.TransactionId}{(isCoinbase ? "  [COINBASE]" : "")}");
			if (!isCoinbase)
			{
				sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Fee: {tx.Fee:F8} BTC"));
				sb.AppendLine($"Inputs ({tx.Inputs.Count}):");
				foreach (TxInput inp in tx.Inputs)
					sb.AppendLine($"  {inp.Address}");
			}
			sb.AppendLine($"Outputs ({tx.Outputs.Count}):");
			foreach (TxOutput txOut in tx.Outputs)
				sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {txOut.Address}  {txOut.Amount:F8} BTC"));
		}
		SetLookupResult(sb.ToString());
	}

	// Sets the left-column lookup result. The trailing blank lines clear the scroll's bottom edge so the last
	// real line (e.g. the last transaction's Recipient) isn't half-clipped — same fix as the right column
	// (see ProjectDesignManual Ch. 29). The label is set on demand here (not on the auto-refresh), so it needs
	// no scroll-position preservation.
	private void SetLookupResult(string text) => _lookupResultLabel.Text = text + "\n\n\n";

	private void OnBackToDicePressed()
	{
		CalendarTimeService? calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		calendar?.PersistCurrentTime();
		_sceneManager?.Go(SceneManager.SceneId.MainMenu);
	}

	private void OnGoToDicePressed()
	{
		CalendarTimeService? calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		calendar?.PersistCurrentTime();
		_sceneManager?.Go(SceneManager.SceneId.DiceGame);
	}

	private void RefreshUi()
	{
		Scripts.Diagnostics.UiRefreshProbe.Count("BlockExplorer.RefreshUi");   // DEBUG-only, mini-plan 25
		Block last = _networkRoot.GetPlayerLatestBlock();

		// Difficulty readout (D.3): the MAIN presenter shows the difficulty of the block being mined NOW
		// (next-block difficulty), not the last mined block — each mined block's own value is shown in its panel.
		int window = BlockchainService.LwmaWindow;
		double miningDifficulty = _networkRoot.GetPlayerNextBlockDifficulty();
		double avgBlockSec = _networkRoot.GetPlayerRecentAverageBlockSeconds(window);
		double targetSec = BlockchainService.TargetBlockSeconds;
		string trend = miningDifficulty > last.Difficulty * 1.001 ? "rising ↑"
			: miningDifficulty < last.Difficulty * 0.999 ? "falling ↓"
			: "steady →";
		string avgBlockText = avgBlockSec > 0 ? FormatDuration(avgBlockSec) : "n/a";

		_chainInfoLabel.Text =
			$"Player chain length: {_networkRoot.GetPlayerChainLength()} | Player pending tx: {_networkRoot.GetPlayerPendingTransactionCount()}"
			+ string.Create(CultureInfo.InvariantCulture, $" | Mining difficulty (block #{last.Index + 1}): {miningDifficulty:F2} ({trend})")
			+ $" | Avg block time (last {window}): {avgBlockText} (target {FormatDuration(targetSec)})";

		SetTextKeepingScroll(_latestBlockLabel,
			"[b]Latest Block (player view)[/b]\n" +
			$"Index: {last.Index}\n" +
			$"Time: {FormatBlockTime(last.Timestamp)}\n" +
			$"Nonce: {last.Nonce}\n" +
			string.Create(CultureInfo.InvariantCulture, $"Difficulty: {last.Difficulty:F2}  (~{last.Difficulty:F0} attempts/block)\n") +
			$"Hash: {last.Hash}\n" +
			$"PrevHash: {last.PreviousBlockHash}\n" +
			$"MerkleRoot: {last.MerkleRoot}\n" +
			$"Transactions: {last.Transactions.Count}\n" +
			BuildLatestTransactionPreview(last));

		// One pass over the status lines, split by WHO: the casino's players (the player and its four casino-miner bots)
		// get their own panel, everyone else stays in Network Status.
		var casinoLines = new List<string>();
		var networkLines = new List<string>();
		foreach ((string nodeId, string line) in BuildNodeStatusLinesWithMiningRates())
		{
			(IsCasinoPlayer(nodeId) ? casinoLines : networkLines).Add(line);
		}

		SetTextKeepingScroll(_casinoPlayersLabel, "[b]Casino Players[/b]\n" + string.Join("\n", casinoLines));
		SetTextKeepingScroll(_networkStatusLabel, "[b]Network Status[/b]\n" + string.Join("\n", networkLines));
		SetTextKeepingScroll(_addressDirectoryLabel, BuildAddressDirectory());

		RefreshEnrollMode();
	}

	// The player and the four casino-miner bots: the casino's own players, the same five canonical clients the
	// casino's books keep (CasinoClientLedgerService), read from the registry rather than repeated as literals.
	private static bool IsCasinoPlayer(string nodeId) =>
		nodeId == "player" || BotWalletRegistry.MinerBots.Any(b => b.NodeId == nodeId);

	// Ch. 29 §29.3, both traps at once: setting Text resets a RichTextLabel's scroll to the top, so the position is saved
	// and restored; and the last line sits flush against the bottom edge, so three blank lines pad it clear.
	private static void SetTextKeepingScroll(RichTextLabel label, string text)
	{
		VScrollBar scroll = label.GetVScrollBar();
		double position = scroll.Value;
		label.Text = text + "\n\n\n";
		scroll.Value = position;
	}

	// Appends a "⛏ <bets/sec>" marker to nodes that are actively mining in the background simulation.
	// Mini-plan 25 — yields the node id beside the line, so the caller can split the casino's players out.
	private IEnumerable<(string NodeId, string Line)> BuildNodeStatusLinesWithMiningRates()
	{
		IReadOnlyDictionary<string, double> rates =
			_simulationService?.GetActiveMiningRates() ?? new Dictionary<string, double>();

		// ND.10g — the node id now arrives as DATA beside the line. It used to be re-parsed out of the
		// line's prefix, which the DEV company rename ("Mt. Gox (non_miner_7) | mined: …") would have
		// silently broken: the lookup would have missed and the ⛏ marker would have vanished.
		foreach ((string nodeId, string line) in _networkRoot.GetNodeStatusLines())
		{
			yield return (nodeId, rates.TryGetValue(nodeId, out double bps)
				? string.Create(CultureInfo.InvariantCulture, $"{line} | ⛏ {bps:0.#}/s")
				: line);
		}
	}

	// InvariantCulture since mini-plan 25: in a custom pattern ':' is the culture's TIME SEPARATOR, so a culture that
	// uses another one would have rendered the time differently, invisible on an English machine (§29.12.1's shape).
	private static string FormatBlockTime(long unixMs) =>
		DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

	// In-game block time as a human-readable duration (e.g. "16h 15m", "2d 03h 10m").
	private static string FormatDuration(double seconds)
	{
		if (seconds <= 0) return "n/a";
		long total = (long)Math.Round(seconds);
		long days = total / 86400; total %= 86400;
		long hours = total / 3600; total %= 3600;
		long mins = total / 60;
		return days > 0 ? $"{days}d {hours:00}h {mins:00}m" : $"{hours}h {mins:00}m";
	}

	// Step 16 P16.2f — IsSelfChangeTransaction / ExternalOutputs (the OQ-8.2 cosmetic pair, §29.9) were
	// DELETED here. They existed only because single-address participants sent change back to the address
	// they spent from; P16.2c gave every spender a DerivedAddressWallet, so the condition they tested can
	// no longer occur. Removing them was gated on one check — that no participant still produces a
	// change-to-self output — which is what surfaced passphrase wallets as the last one (see
	// NetworkRoot.RegisterPassphraseWallet). If a future participant is ever added WITHOUT a seed, the
	// honest fix is to give it one, not to reintroduce a filter that makes real spends fail to add up.

	private static string BuildLatestTransactionPreview(Block block)
	{
		List<Transaction> visible = block.Transactions;
		if (visible.Count == 0) return "Last block tx details: none";
		var sb = new StringBuilder($"Last block txs ({visible.Count}):\n");
		foreach (Transaction tx in visible)
		{
			bool isCoinbase = tx.IsCoinbase;
			sb.Append("-\n");
			sb.Append($"TxId: {tx.TransactionId}{(isCoinbase ? "  [COINBASE]" : "")}\n");
			if (!isCoinbase)
			{
				sb.Append(string.Create(CultureInfo.InvariantCulture, $"Fee: {tx.Fee:F8} BTC\n"));
				foreach (TxInput inp in tx.Inputs)
					sb.Append($"From: {inp.Address}\n");
			}
			else
			{
				sb.Append($"From: {BlockchainService.CoinbaseSender}\n");
			}
			sb.Append($"Outputs ({tx.Outputs.Count}):\n");
			foreach (TxOutput txOut in tx.Outputs)
				sb.Append(string.Create(CultureInfo.InvariantCulture, $"  {txOut.Address}  {txOut.Amount:F8} BTC\n"));
		}
		return sb.ToString().TrimEnd();
	}
}
