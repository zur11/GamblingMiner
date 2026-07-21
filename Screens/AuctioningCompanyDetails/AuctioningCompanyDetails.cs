using Godot;
using System;
using System.Globalization;
using System.Linq;
using GodotBlockchainPort.Simulation;
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

	private double _refreshTimer;
	private const double RefreshInterval = 1.0;

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

		RefreshAll();
	}

	public override void _Process(double delta)
	{
		if (string.IsNullOrEmpty(_nonMinerAddress)) return;
		_refreshTimer += delta;
		if (_refreshTimer < RefreshInterval) return;
		_refreshTimer = 0d;
		RefreshAll();
	}

	private void RefreshAll()
	{
		NonMinerDonationSummary? summary = _networkRoot.GetNonMinerAuctionLedger()
			.FirstOrDefault(s => s.NonMinerAddress == _nonMinerAddress);
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
			ShowLiveTrackedDonations(summary);
		}
	}

	// D-ND5.9 — while InAuction: the live top-10 tracked donation pool (D-ND5.3), auto-refreshing.
	private void ShowLiveTrackedDonations(NonMinerDonationSummary summary)
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
		// per-slot "re-bid NN%" is the chance a casino-bot holding THAT slot as its best position
		// re-bids when picked (see NetworkRoot).
		int occupied = summary.TrackedDonations.Count;
		bool urgent = NetworkRoot.IsAuctionInUrgencyWindow(summary.WindowCloseUnixMs, nowMs);
		string mode = occupied < 7
			? "early-rush bidding (pool <7 slots)"
			: urgent
				? "normal bidding — FINAL-WEEK URGENCY (≤7d left)"
				: "normal bidding (pool ≥7 slots)";
		_sectionTitleLabel.Text = $"Tracked Donation Pool ({occupied}/10 by value) — {mode}";

		ClearContent();
		if (occupied == 0)
		{
			_contentVBox.AddChild(new Label { Text = "No qualifying donations tracked yet." });
			return;
		}

		int tier = 1;
		foreach (TrackedDonation d in summary.TrackedDonations.OrderByDescending(d => d.AmountBtc))
		{
			string scValue = d.CurrentValueSc is decimal sc
				? string.Create(CultureInfo.InvariantCulture, $"  (≈ {sc:F8} SC today)")
				: string.Empty;
			string prob = NetworkRoot.ReBidProbabilityLabel(tier, occupied, urgent);
			string probCol = prob switch
			{
				"" => string.Empty,
				"satisfied" => "[satisfied]  ",
				_ => $"[re-bid {prob}]  ",
			};
			string line = string.Create(CultureInfo.InvariantCulture,
				$"#{tier}  {probCol}{_networkRoot.DescribeAddress(d.DonorAddress)}  {d.AmountBtc:F8} BTC{scValue}  — {FormatDate(d.TimestampMs)}");
			_contentVBox.AddChild(new Label { Text = line });
			tier++;
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
