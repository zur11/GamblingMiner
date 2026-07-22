using Godot;
using System;
using System.Globalization;
using System.Linq;
using GodotBlockchainPort.Simulation;
using UI.StatusBar;
#nullable enable

// Step 14 (ND.8b.4, D-ND8.16) — the per-FOUNDED-company scene: the founding/stock-distribution summary
// plus the live governance readout (reserve mix, market category, dividend cycle, vote history), with
// interaction panels GATED BY THE PLAYER'S HOLDING:
//   - no shares  → summary only, no action panels;
//   - holds NST  → Board Vote panel (submit a ballot into the open vote — this is also what lifts the
//                  D-ND8.18 game pause) + Quarterly Dividend panel (claim button);
//   - holds PST  → Daily Dividend panel only (the drip of the NST-agreed quarter total), no vote panel.
// Entered from BlockExplorer's Enroll Mode "Details →" on founded companies (the AuctioningCompanyDetails
// static hand-off pattern, D-ND5.9); AuctioningCompanyDetails also forwards here the moment its company
// resolves. PURE display + action surface (the ND.5 discipline): the engine lives in NetworkRoot's
// block-driven TickCompanyGovernance — this scene only reads state and calls the two action APIs
// (TryRegisterPlayerVote / TryClaimPlayerCompanyDividends), never founds/settles/closes anything itself.
public partial class CompanyDetails : Control
{
	// Set by the caller immediately before SceneManager.Go(SceneId.CompanyDetails).
	public static string? PendingNonMinerAddress;

	private const string PlayerNodeId = "player";

	private NetworkRoot _networkRoot = null!;
	private SceneManager? _sceneManager;
	private BtcMarketDataService? _btcMarketDataService;
	private string _nonMinerAddress = string.Empty;

	private Label _identityLabel = null!;
	private Label _statusLabel = null!;
	private VBoxContainer _infoVBox = null!;
	private VBoxContainer _actionVBox = null!;

	// The action panels are rebuilt ONLY when this signature changes (open-vote identity + holding
	// class), so the player's in-progress SpinBox/OptionButton edits survive the 1 s info refresh.
	private string _actionSignature = string.Empty;
	private SpinBox? _reserveSpin;
	private OptionButton? _marketOption;
	private SpinBox? _payoutSpin;
	private Label? _voteFeedbackLabel;
	private Label? _claimableLabel;
	private Label? _claimFeedbackLabel;

	private double _refreshTimer;
	private const double RefreshInterval = 1.0;

	public override void _Ready()
	{
		_networkRoot = GetNode<NetworkRoot>("NetworkRoot");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		_btcMarketDataService = GetNodeOrNull<BtcMarketDataService>("/root/BtcMarketDataService");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());

		_identityLabel = GetNode<Label>("%IdentityLabel");
		_statusLabel = GetNode<Label>("%StatusLabel");
		_infoVBox = GetNode<VBoxContainer>("%InfoVBox");
		_actionVBox = GetNode<VBoxContainer>("%ActionVBox");

		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.BlockExplorer);

		_nonMinerAddress = PendingNonMinerAddress ?? string.Empty;
		if (string.IsNullOrEmpty(_nonMinerAddress))
		{
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
		CompanyFounding? founding = _networkRoot.GetCompanyFounding(_nonMinerAddress);
		if (summary is null || founding is null)
		{
			_identityLabel.Text = _nonMinerAddress;
			_statusLabel.Text = "Company not found (not founded yet?).";
			return;
		}

		CompanyGovernanceState? gov = _networkRoot.GetCompanyGovernanceByNodeId(founding.NonMinerNodeId);

		string appearance = summary.CompanyAppearanceDateLocal is DateTime d
			? $"  —  appeared {d:yyyy-MM-dd}"
			: string.Empty;
		_identityLabel.Text = $"{NetworkRoot.DescribeCompany(summary)}{appearance}   [{summary.NonMinerAddress}]";
		_statusLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Founded {FormatDate(founding.FoundedAtUnixMs)}  |  treasury {_networkRoot.GetNodeSpendableBalance(founding.NonMinerNodeId):F8} BTC");

		RebuildInfo(founding, gov);
		RebuildOrUpdateActions(founding, gov);
	}

	// ── The always-visible readout (rebuilt each refresh) ────────────────────────────────────────

	private void RebuildInfo(CompanyFounding founding, CompanyGovernanceState? gov)
	{
		foreach (Node child in _infoVBox.GetChildren()) child.QueueFree();

		// Stock distribution (the founding mint, D-ND8.15).
		_infoVBox.AddChild(SectionTitle("Stock Distribution (founding mint)"));
		decimal totalNst = founding.Holdings.Sum(h => h.Nst);
		decimal totalPst = founding.Holdings.Sum(h => h.Pst);
		decimal totalTokens = totalNst + totalPst;
		foreach (CompanyShareHolding h in founding.Holdings.OrderByDescending(h => h.Nst + h.Pst))
		{
			decimal tokens = h.Nst + h.Pst;
			string shareClass = h.Nst > 0m ? "NST" : "PST";
			decimal profitShare = totalTokens > 0m ? tokens / totalTokens : 0m;
			string votes = h.Nst > 0m && totalNst > 0m
				? string.Create(CultureInfo.InvariantCulture, $"  |  votes {h.Nst / totalNst:P2}")
				: string.Empty;
			_infoVBox.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"{_networkRoot.DescribeAddress(h.HolderId)}  —  {tokens:F8} {shareClass}  |  profit share {profitShare:P2}{votes}")
			});
		}
		_infoVBox.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture, $"Total minted: {totalNst:F8} NST  +  {totalPst:F8} PST")
		});

		if (gov == null)
		{
			_infoVBox.AddChild(new HSeparator());
			_infoVBox.AddChild(new Label { Text = "Governance not started (company founded before ND.8b.3 — a legacy world)." });
			return;
		}

		// Governance status (ND.8b.3).
		_infoVBox.AddChild(new HSeparator());
		_infoVBox.AddChild(SectionTitle("Governance"));
		_infoVBox.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture,
				$"Reserve mix target: {gov.ReserveScPercent:F0}% SC / {100m - gov.ReserveScPercent:F0}% BTC  ({gov.CurrencyBand}, vote range {FormatBounds(gov.CurrencyBand)})")
		});
		_infoVBox.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture,
				$"SC reserve: {gov.ScReserve:N8} SC (auto-converted from treasury BTC via the provisional casino path)")
		});
		string drift = gov.MarketCategory == gov.DefaultMarketCategory
			? string.Empty
			: $"  (default: {gov.DefaultMarketCategory})";
		_infoVBox.AddChild(new Label { Text = $"Market category: {gov.MarketCategory}{drift}" });
		_infoVBox.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture,
				$"Next quarterly vote: {FormatDate(gov.NextQuarterlyDueMs)}  (quarter #{gov.QuarterIndex + 1})")
		});

		if (gov.OpenVote is { } vote)
		{
			string awaiting = vote.AwaitingPlayerVote ? "  —  AWAITING YOUR BALLOT (game paused)" : string.Empty;
			_infoVBox.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"OPEN VOTE: {vote.Kind}  |  opened {FormatDate(vote.OpenedAtMs)}, closes {FormatDate(vote.ClosesAtMs)}{awaiting}")
			});
		}

		if (gov.QuarterCycleStartMs > 0 && (gov.QuarterDividendBtc > 0m || gov.QuarterDividendSc > 0m))
		{
			string state = gov.QuarterLumpCredited ? "settled" : "distributing";
			_infoVBox.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"Quarter dividend ({state}): {gov.QuarterDividendBtc:F8} BTC + {gov.QuarterDividendSc:F8} SC at {gov.QuarterPayoutRatePercent:F2}%/quarter  |  {FormatDate(gov.QuarterCycleStartMs)} → {FormatDate(gov.QuarterCycleEndMs)}")
			});
		}

		// Vote history (newest first, short).
		if (gov.VoteHistory.Count > 0)
		{
			_infoVBox.AddChild(new HSeparator());
			_infoVBox.AddChild(SectionTitle("Vote History"));
			foreach (CompanyVoteRecord rec in Enumerable.Reverse(gov.VoteHistory).Take(10))
			{
				string dividend = rec.Kind == "quarterly"
					? string.Create(CultureInfo.InvariantCulture,
						$"  |  payout {rec.ResultPayoutRatePercent:F2}% → {rec.FinalizedDividendBtc:F8} BTC + {rec.FinalizedDividendSc:F8} SC")
					: string.Empty;
				_infoVBox.AddChild(new Label
				{
					Text = string.Create(CultureInfo.InvariantCulture,
						$"{FormatDate(rec.ClosedAtMs)}  {rec.Kind}: reserve {rec.ResultReserveScPercent:F0}% SC, market {rec.ResultMarketCategory}{dividend}")
				});
			}
		}

		// ND.8g (§12.5.6) — the PLAYER's own dividend claim history for this company (bot auto-claims never
		// write here) + lifetime totals. Rebuilt every refresh (not signature-gated like the action panels
		// below) so a fresh claim shows up immediately without needing a holding/vote-state change.
		if (gov.PlayerClaimHistory.Count > 0)
		{
			_infoVBox.AddChild(new HSeparator());
			_infoVBox.AddChild(SectionTitle("Dividend Claim History"));

			decimal totalSc = gov.PlayerClaimHistory.Sum(r => r.ScAmount);
			decimal totalBtc = gov.PlayerClaimHistory.Sum(r => r.BtcAmount);
			// 2a. Historical value — each payment valued at ITS OWN day's price (never recomputed).
			decimal historicalScValue = gov.PlayerClaimHistory
				.Where(r => r.BtcPriceUsdAtClaim is decimal)
				.Sum(r => r.BtcAmount * r.BtcPriceUsdAtClaim!.Value);
			// 2b. Current value — the SAME BTC total revalued at TODAY's live price (recomputed every
			// refresh, the established "always live" convention — TrackedDonation.CurrentValueSc / the
			// auction's LeadingDonorScValue).
			decimal? currentPrice = _btcMarketDataService?.GetEffectivePriceUsd(
				DateTimeOffset.FromUnixTimeMilliseconds(_networkRoot.GetPlayerLatestBlock().Timestamp).LocalDateTime);
			string currentValueText = currentPrice is decimal price
				? string.Create(CultureInfo.InvariantCulture, $"{totalBtc * price:F8} SC")
				: "n/a (no live price yet)";

			_infoVBox.AddChild(new Label { Text = string.Create(CultureInfo.InvariantCulture, $"Total SC received (all-time): {totalSc:F8} SC") });
			_infoVBox.AddChild(new Label { Text = string.Create(CultureInfo.InvariantCulture, $"Total BTC received (all-time): {totalBtc:F8} BTC") });
			_infoVBox.AddChild(new Label { Text = string.Create(CultureInfo.InvariantCulture, $"  → Historical BTC/SC payment value: {historicalScValue:F8} SC  (each payment valued at its own day's price)") });
			_infoVBox.AddChild(new Label { Text = $"  → Current BTC/SC payment value: {currentValueText}  (the same BTC revalued at today's live price)" });

			int shown = Math.Min(30, gov.PlayerClaimHistory.Count);
			_infoVBox.AddChild(new Label
			{
				Text = gov.PlayerClaimHistory.Count > shown
					? $"Most recent {shown} of {gov.PlayerClaimHistory.Count} claims:"
					: "All claims:"
			});
			foreach (CompanyDividendClaimRecord rec in Enumerable.Reverse(gov.PlayerClaimHistory).Take(shown))
			{
				string priceText = rec.BtcPriceUsdAtClaim is decimal claimPrice
					? string.Create(CultureInfo.InvariantCulture, $"{claimPrice:F8} SC/BTC that day")
					: "price unavailable";
				_infoVBox.AddChild(new Label
				{
					Text = string.Create(CultureInfo.InvariantCulture,
						$"{FormatDate(rec.ClaimedAtUnixMs)}   {rec.BtcAmount:F8} BTC + {rec.ScAmount:F8} SC   ({priceText})")
				});
			}
		}

		// Ch. 29 — trailing spacer so the last line clears the scroll's bottom edge.
		_infoVBox.AddChild(new Label { Text = " " });
	}

	// ── The holding-gated action panels (D-ND8.16) ───────────────────────────────────────────────

	private void RebuildOrUpdateActions(CompanyFounding founding, CompanyGovernanceState? gov)
	{
		CompanyShareHolding? playerHolding = founding.Holdings.FirstOrDefault(h => h.HolderId == PlayerNodeId);
		bool hasNst = playerHolding is { Nst: > 0m };
		bool hasPst = playerHolding is { Pst: > 0m };

		string signature = string.Create(CultureInfo.InvariantCulture,
			$"{hasNst}|{hasPst}|{gov?.OpenVote?.OpenedAtMs ?? 0}|{gov?.OpenVote?.AwaitingPlayerVote ?? false}|{gov?.OpenVote?.Kind ?? ""}");
		if (signature != _actionSignature)
		{
			_actionSignature = signature;
			BuildActionPanels(founding, gov, hasNst, hasPst);
		}

		// Live-updated pieces (claimables drip daily) without rebuilding the panels.
		if (_claimableLabel != null && gov != null
			&& gov.ClaimableByHolder.TryGetValue(PlayerNodeId, out CompanyClaimable? claim))
		{
			_claimableLabel.Text = string.Create(CultureInfo.InvariantCulture,
				$"Claimable now: {claim.Btc:F8} BTC + {claim.Sc:F8} SC");
		}
	}

	private void BuildActionPanels(CompanyFounding founding, CompanyGovernanceState? gov, bool hasNst, bool hasPst)
	{
		foreach (Node child in _actionVBox.GetChildren()) child.QueueFree();
		_reserveSpin = null;
		_marketOption = null;
		_payoutSpin = null;
		_voteFeedbackLabel = null;
		_claimableLabel = null;
		_claimFeedbackLabel = null;

		if (gov == null || (!hasNst && !hasPst))
		{
			return; // no shares → summary only (D-ND8.16)
		}

		_actionVBox.AddChild(new HSeparator());

		if (hasNst)
		{
			BuildBoardVotePanel(founding, gov);
			BuildDividendPanel(gov, "Quarterly Dividend (NST lump — credited at each quarter end)");
		}
		else
		{
			// PST only: the daily drip panel, no vote panel (PST carries zero votes, D-ND8.6).
			BuildDividendPanel(gov, "Daily Dividend (PST drip — accrues each in-game day)");
		}
	}

	private void BuildBoardVotePanel(CompanyFounding founding, CompanyGovernanceState gov)
	{
		_actionVBox.AddChild(SectionTitle("Board Vote"));

		if (gov.OpenVote is not { } vote)
		{
			_actionVBox.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"No vote open right now — the next quarterly vote lands {FormatDate(gov.NextQuarterlyDueMs)}.")
			});
			return;
		}

		bool playerVoted = vote.Ballots.ContainsKey(PlayerNodeId);
		if (playerVoted && !vote.AwaitingPlayerVote)
		{
			_actionVBox.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"Ballot registered — the {vote.Kind} vote closes {FormatDate(vote.ClosesAtMs)} and applies from the next day.")
			});
			return;
		}

		bool quarterly = vote.Kind == "quarterly";
		(decimal min, decimal max) = NetworkRoot.BandScPercentBounds(gov.CurrencyBand);

		var reserveRow = new HBoxContainer();
		reserveRow.AddChild(new Label { Text = $"Reserve target (% held as SC, band {gov.CurrencyBand}: {min:F0}–{max:F0}):  " });
		_reserveSpin = new SpinBox { MinValue = (double)min, MaxValue = (double)max, Step = 1, Value = (double)gov.ReserveScPercent };
		reserveRow.AddChild(_reserveSpin);
		_actionVBox.AddChild(reserveRow);

		if (quarterly)
		{
			var marketRow = new HBoxContainer();
			marketRow.AddChild(new Label { Text = "Market direction:  " });
			_marketOption = new OptionButton();
			_marketOption.AddItem("Vote lighter (toward legal)");   // -1
			_marketOption.AddItem("Hold the current category");     //  0
			_marketOption.AddItem("Vote darker (toward black)");    // +1
			_marketOption.Select(1);
			marketRow.AddChild(_marketOption);
			_actionVBox.AddChild(marketRow);

			decimal defaultRate = NetworkRoot.DefaultQuarterlyPayoutRatePercent(gov.MarketCategory);
			var payoutRow = new HBoxContainer();
			payoutRow.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"Quarterly payout rate (%/quarter, default {defaultRate:F0}, max {defaultRate * 2m:F0}):  ")
			});
			_payoutSpin = new SpinBox { MinValue = 0, MaxValue = (double)(defaultRate * 2m), Step = 0.5, Value = (double)defaultRate };
			payoutRow.AddChild(_payoutSpin);
			_actionVBox.AddChild(payoutRow);
		}

		var submitBtn = new Button { Text = "Submit Ballot" };
		string nodeId = gov.NonMinerNodeId;
		submitBtn.Pressed += () => OnSubmitBallot(nodeId, quarterly);
		_actionVBox.AddChild(submitBtn);

		_voteFeedbackLabel = new Label { Text = vote.AwaitingPlayerVote ? "The game is paused until you vote." : " " };
		_actionVBox.AddChild(_voteFeedbackLabel);
	}

	private void OnSubmitBallot(string nonMinerNodeId, bool quarterly)
	{
		decimal reserveTarget = (decimal)(_reserveSpin?.Value ?? 0d);
		int marketShift = quarterly ? (_marketOption?.Selected ?? 1) - 1 : 0;
		decimal payoutRate = quarterly ? (decimal)(_payoutSpin?.Value ?? 0d) : 0m;

		bool ok = NetworkRoot.TryRegisterPlayerVote(nonMinerNodeId, reserveTarget, marketShift, payoutRate);
		if (_voteFeedbackLabel != null)
		{
			_voteFeedbackLabel.Text = ok
				? "Ballot registered — play resumes; the result applies when the vote closes."
				: "Could not register the ballot (vote may have closed).";
		}
		_actionSignature = string.Empty; // force a panel rebuild on the next refresh
	}

	private void BuildDividendPanel(CompanyGovernanceState gov, string title)
	{
		_actionVBox.AddChild(new HSeparator());
		_actionVBox.AddChild(SectionTitle(title));

		_claimableLabel = new Label { Text = "Claimable now: 0.00000000 BTC + 0.00000000 SC" };
		_actionVBox.AddChild(_claimableLabel);

		var claimBtn = new Button { Text = "Claim Dividends" };
		string nodeId = gov.NonMinerNodeId;
		claimBtn.Pressed += () => OnClaimDividends(nodeId);
		_actionVBox.AddChild(claimBtn);

		_claimFeedbackLabel = new Label { Text = "BTC claims broadcast on-chain to your base address (network fee deducted from the claim)." };
		_actionVBox.AddChild(_claimFeedbackLabel);

		// Ch. 29 — trailing spacer so the last control clears the scroll's bottom edge.
		_actionVBox.AddChild(new Label { Text = " " });
	}

	private void OnClaimDividends(string nonMinerNodeId)
	{
		(bool ok, string message) = _networkRoot.TryClaimPlayerCompanyDividends(nonMinerNodeId);
		if (_claimFeedbackLabel != null)
		{
			_claimFeedbackLabel.Text = message;
			_claimFeedbackLabel.Modulate = ok ? Colors.LightGreen : Colors.Orange;
		}
	}

	private static Label SectionTitle(string text)
	{
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", 20);
		return label;
	}

	private static string FormatBounds(string band)
	{
		(decimal min, decimal max) = NetworkRoot.BandScPercentBounds(band);
		return string.Create(CultureInfo.InvariantCulture, $"{min:F0}–{max:F0}% SC");
	}

	private static string FormatDate(long unixMs) =>
		DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
}
