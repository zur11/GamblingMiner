using Godot;
using System;
using System.Globalization;
using System.Linq;
using GodotBlockchainPort.Simulation;
using UI.StatusBar;
#nullable enable

// Step 14 (ND.5c, D-ND5.9) — per-non-miner detail scene: the live top-10 tracked donation pool while the
// auction is still open, switching to a settlement summary once it resolves. Entered from BlockExplorer's
// Enroll Mode (D-ND5.2) via the static hand-off field below — no existing cross-scene parameter-passing
// convention was found elsewhere in the codebase to reuse (D-ND5.9), so this is a new, minimal, self-
// contained pattern, not an extension of SceneManager. The scene NEVER triggers settlement itself —
// NetworkRoot.TrySettleResolvedAuctions already ran it live, exactly once, from HandleMinedBlock; this
// scene only ever displays the result (GetNonMinerAuctionLedger / GetAuctionSettlementSummary are both
// pure, side-effect-free reads).
public partial class RecruitableBiddingDetails : Control
{
	// Set by BlockExplorer immediately before SceneManager.Go(SceneId.RecruitableBiddingDetails).
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

		_identityLabel.Text = $"{summary.NonMinerNodeId}   {summary.NonMinerAddress}";

		if (summary.Status == NonMinerAuctionStatus.Resolved)
		{
			ShowSettlementSummary(summary);
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
		_sectionTitleLabel.Text = $"Tracked Donation Pool ({summary.TrackedDonations.Count}/10 by value)";

		ClearContent();
		if (summary.TrackedDonations.Count == 0)
		{
			_contentVBox.AddChild(new Label { Text = "No qualifying donations tracked yet." });
			return;
		}

		int rank = 1;
		foreach (TrackedDonation d in summary.TrackedDonations.OrderByDescending(d => d.AmountBtc))
		{
			string scValue = d.CurrentValueSc is decimal sc
				? string.Create(CultureInfo.InvariantCulture, $"  (≈ {sc:F8} SC today)")
				: string.Empty;
			string line = string.Create(CultureInfo.InvariantCulture,
				$"#{rank}  {_networkRoot.DescribeAddress(d.DonorAddress)}  {d.AmountBtc:F8} BTC{scValue}  — {FormatDate(d.TimestampMs)}");
			_contentVBox.AddChild(new Label { Text = line });
			rank++;
		}
	}

	// D-ND5.9 — once resolved: each paid donor + their SC amount, and the BTC total swept to casino.
	private void ShowSettlementSummary(NonMinerDonationSummary summary)
	{
		string winner = string.IsNullOrEmpty(summary.WinnerAddress)
			? "no winner (legacy pre-EB.2 world)"
			: _networkRoot.DescribeAddress(summary.WinnerAddress);
		_statusLabel.Text = $"Resolved — now a permanent referral of {winner}";
		_sectionTitleLabel.Text = "Settlement Summary";

		ClearContent();

		AuctionSettlementSummary? settlement = _networkRoot.GetAuctionSettlementSummary(_nonMinerAddress);
		if (settlement is null)
		{
			_contentVBox.AddChild(new Label { Text = "Settlement figures unavailable (no closing-date price data)." });
			return;
		}

		_contentVBox.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture,
				$"Closing date: {FormatDate(settlement.ClosingBlockTimestampMs)}   |   Closing price: {settlement.ClosingPriceUsd:F8} SC/BTC")
		});
		_contentVBox.AddChild(new HSeparator());

		foreach (AuctionSettlementPayout payout in settlement.Payouts.OrderByDescending(p => p.PayoutSc))
		{
			string line = string.Create(CultureInfo.InvariantCulture,
				$"{_networkRoot.DescribeAddress(payout.DonorAddress)}  paid  {payout.PayoutSc:F8} SC");
			_contentVBox.AddChild(new Label { Text = line });
		}

		_contentVBox.AddChild(new HSeparator());
		_contentVBox.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture, $"Total paid out: {settlement.TotalPayoutSc:F8} SC")
		});
		_contentVBox.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture,
				$"BTC swept to casino: {settlement.SweepAmountBtc:F8} BTC  (of {settlement.WindowTotalBtc:F8} BTC tracked, network fee absorbed from the total)")
		});
	}

	private void ClearContent()
	{
		foreach (Node child in _contentVBox.GetChildren())
			child.QueueFree();
	}

	private static string FormatDate(long unixMs) =>
		DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
}
