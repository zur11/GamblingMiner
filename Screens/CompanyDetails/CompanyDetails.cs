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

	// ND.9b — a holding-keyed page border: gold when the player holds NST, silver when PST, black when
	// neither. A transparent-centre StyleBoxFlat overlay drawn just inside the screen edge (Ch. 29-safe —
	// it sits behind the content, mouse-transparent, and never touches the scroll/footer layout).
	private Panel _borderPanel = null!;
	private StyleBoxFlat _borderStyle = null!;
	private static readonly Color HoldingGold = new(0.85f, 0.65f, 0.13f);   // NST
	private static readonly Color HoldingSilver = new(0.75f, 0.75f, 0.78f); // PST
	private static readonly Color HoldingBlack = new(0.05f, 0.05f, 0.05f);  // none

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

		BuildBorderOverlay();

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

		// ND.9b — the player's holding class drives the page-border colour + a legible caption.
		CompanyShareHolding? playerHolding = founding.Holdings.FirstOrDefault(h => h.HolderId == PlayerNodeId);
		bool hasNst = playerHolding is { Nst: > 0m };
		bool hasPst = playerHolding is { Pst: > 0m };
		_borderStyle.BorderColor = hasNst ? HoldingGold : hasPst ? HoldingSilver : HoldingBlack;
		string holdingCaption = hasNst ? "NST (voting shares)" : hasPst ? "PST (dividend shares)" : "no shares";

		string appearance = summary.CompanyAppearanceDateLocal is DateTime d
			? $"  —  appeared {d:yyyy-MM-dd}"
			: string.Empty;
		_identityLabel.Text = $"{NetworkRoot.DescribeCompany(summary)}{appearance}   [{summary.NonMinerAddress}]";
		_statusLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Founded {FormatDate(founding.FoundedAtUnixMs)}  |  treasury {_networkRoot.GetNodeSpendableBalance(founding.NonMinerNodeId):F8} BTC  |  You hold: {holdingCaption}");

		RebuildInfo(founding, gov);
		RebuildOrUpdateActions(founding, gov);
	}

	// ── The always-visible readout (rebuilt each refresh) ────────────────────────────────────────

	private void RebuildInfo(CompanyFounding founding, CompanyGovernanceState? gov)
	{
		foreach (Node child in _infoVBox.GetChildren()) child.QueueFree();

		// Stock distribution (the founding mint, D-ND8.15).
		_infoVBox.AddChild(SectionTitle("Founding Snapshot — Stock Distribution"));
		decimal totalNst = founding.Holdings.Sum(h => h.Nst);
		decimal totalPst = founding.Holdings.Sum(h => h.Pst);
		decimal totalTokens = totalNst + totalPst;

		// ND.9c — the frozen "how the company was founded" table (tier occupancy, participation %, slot
		// bonus, base→final tokens), read from the persisted breakdown. Legacy companies (founded before
		// ND.9c) have no breakdown ⇒ a one-line notice + the plain token list below.
		if (founding.FoundingBreakdown.Count > 0)
		{
			decimal poolBtc = founding.FoundingBreakdown.Sum(b => b.AmountBtcAtClose);
			_infoVBox.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"Auction pool at close: {poolBtc:F8} BTC over {founding.FoundingBreakdown.Count} bidder(s). Each bidder's share of the 10,000-token base pool × (1 + slot bonus):")
			});
			foreach (CompanyFoundingBreakdown b in founding.FoundingBreakdown.OrderByDescending(b => b.FinalTokens))
			{
				string tierList = "#" + string.Join(",#", b.Tiers);
				string cls = b.IsNst
					? string.Create(CultureInfo.InvariantCulture, $"NST (votes {(totalNst > 0m ? b.FinalTokens / totalNst : 0m):P2})")
					: "PST (no votes)";
				_infoVBox.AddChild(new Label
				{
					Text = string.Create(CultureInfo.InvariantCulture,
						$"{_networkRoot.DescribeAddress(b.HolderId)}  —  tier(s) {tierList}  —  bid {b.AmountBtcAtClose:F8} BTC  —  participation {b.ParticipationShare:P2}")
				});
				_infoVBox.AddChild(new Label
				{
					Text = string.Create(CultureInfo.InvariantCulture,
						$"      base {b.BaseTokens:F8} × (1 + bonus {b.BonusFraction:P2}) = {b.FinalTokens:F8} tokens  —  {cls}")
				});
			}
		}
		else
		{
			_infoVBox.AddChild(new Label { Text = "Founding breakdown unavailable (company founded before this feature)." });
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

		// ND.9d/e/f — the combined "Company Policy (initial → current)" panel (replaces the old scattered
		// Reserve-mix / SC-reserve / Market-category lines): Reserves, Market level (gradient slider),
		// Dividend rate — each showing initial → current.
		_infoVBox.AddChild(new HSeparator());
		BuildCompanyPolicySection(founding, gov);

		// Governance status (ND.8b.3).
		_infoVBox.AddChild(new HSeparator());
		_infoVBox.AddChild(SectionTitle("Governance status"));
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

		// ND.9g / ND.9h — the Last Vote Snapshot (every participant's ballot + before→after dials + the
		// quarterly dividend distribution). Reads gov.VoteHistory.Last().
		BuildLastVoteSnapshot(founding, gov);

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
			// ND.10h (D-ND10h.3) — the same predicate the BlockExplorer row button lights up on, so the two
			// surfaces cannot disagree. A balance that exists but sits below the day's network fee is NOT
			// claimable (the fee is deducted from the claim itself) — say so, otherwise a player looking at a
			// non-zero figure beside an un-green row button has no way to know why.
			string claimNote = (claim.Btc > 0m || claim.Sc > 0m)
				&& !_networkRoot.HasPlayerClaimableDividends(founding.NonMinerNodeId)
				? "   — below the network fee; still accruing"
				: string.Empty;
			_claimableLabel.Text = string.Create(CultureInfo.InvariantCulture,
				$"Claimable now: {claim.Btc:F8} BTC + {claim.Sc:F8} SC{claimNote}");
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

	// ── ND.9d/e/f — the "Company Policy (initial → current)" panel ────────────────────────────────

	private static readonly string[] MarketOrder = { "official", "light_grey", "dark_grey", "black" };

	private static int MarketIndex(string id)
	{
		int i = Array.IndexOf(MarketOrder, id);
		return i < 0 ? 0 : i;
	}

	// ND.9e — player-facing label + light/dark percentage (darkness% = index/3 × 100). Shared with ND.9g.
	private static (string label, int darkPercent) MarketDisplay(string id) => id switch
	{
		"official" => ("Official", 0),
		"light_grey" => ("Light-grey", 33),
		"dark_grey" => ("Dark-grey", 67),
		"black" => ("Black market", 100),
		_ => (id, 0)
	};

	private void BuildCompanyPolicySection(CompanyFounding founding, CompanyGovernanceState gov)
	{
		_infoVBox.AddChild(SectionTitle("Company Policy (initial → current)"));

		// ND.9d — Reserves: BTC & SC side by side, original → current mix.
		decimal current = gov.ReserveScPercent;
		decimal orig = gov.VoteHistory.FirstOrDefault(v => v.Kind == "founding")?.ResultReserveScPercent ?? current;
		decimal treasuryBtc = _networkRoot.GetNodeSpendableBalance(founding.NonMinerNodeId);
		decimal? price = _btcMarketDataService?.GetEffectivePriceUsd(
			DateTimeOffset.FromUnixTimeMilliseconds(_networkRoot.GetPlayerLatestBlock().Timestamp).LocalDateTime);
		string btcInSc = price is decimal p
			? string.Create(CultureInfo.InvariantCulture, $"  (~{treasuryBtc * p:N2} SC)")
			: string.Empty;

		_infoVBox.AddChild(new Label { Text = "Reserves:" });
		_infoVBox.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture,
				$"   BTC reserve: {treasuryBtc:F8} BTC  ({100m - current:F0}% target){btcInSc}")
		});
		_infoVBox.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture,
				$"   SC reserve:  {gov.ScReserve:N8} SC  ({current:F0}% target)")
		});
		_infoVBox.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture,
				$"   Mix: initial {orig:F0}% SC / {100m - orig:F0}% BTC   →   current {current:F0}% SC / {100m - current:F0}% BTC   ({gov.CurrencyBand}, band {FormatBounds(gov.CurrencyBand)})")
		});

		// ND.9e — Market (Light↔Dark) level: gradient slider (current marked) + default in numbers only.
		_infoVBox.AddChild(new Label { Text = "Market level:" });
		_infoVBox.AddChild(BuildMarketGradientBar(gov.MarketCategory));
		(string curLabel, int curDark) = MarketDisplay(gov.MarketCategory);
		(string defLabel, int defDark) = MarketDisplay(gov.DefaultMarketCategory);
		_infoVBox.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture,
				$"   Current: {curLabel} ({curDark}% dark)     Default: {defLabel} ({defDark}% dark)")
		});

		// ND.9f — Dividend rate: initial default → current voted.
		decimal defaultRate = NetworkRoot.DefaultQuarterlyPayoutRatePercent(gov.DefaultMarketCategory);
		_infoVBox.AddChild(new Label { Text = "Dividend rate:" });
		_infoVBox.AddChild(new Label
		{
			Text = gov.QuarterPayoutRatePercent > 0m
				? string.Create(CultureInfo.InvariantCulture,
					$"   {gov.QuarterPayoutRatePercent:F1}% of reserves paid out per quarter as dividends; the rest stays in reserve  (default {defaultRate:F0}%, max {defaultRate * 2m:F0}%)")
				: string.Create(CultureInfo.InvariantCulture,
					$"   not yet set — first quarter pending  (default {defaultRate:F0}%/quarter, max {defaultRate * 2m:F0}%)")
		});
	}

	// ND.9e — a fixed-width white→grey→black gradient bar with the four categories as labelled ticks
	// (name + % dark) and a caret marking ONLY the current category (default is shown in numbers, not here).
	private Control BuildMarketGradientBar(string currentId)
	{
		// Layout bands (no overlap): tick labels (name + % dark) sit ABOVE the gradient, the gradient bar
		// in the middle, the "▲ current" caret below it.
		const int W = 440;
		const int TickTop = 0;
		const int TickH = 32;      // two lines of font 11, fully above the gradient
		const int GradTop = 34;
		const int GradH = 16;
		const int CaretTop = 52;
		const int H = 72;
		const int TickW = 96;
		var bar = new Control { CustomMinimumSize = new Vector2(W, H) };

		var grad = new Gradient
		{
			Offsets = new[] { 0f, 0.5f, 1f },
			Colors = new[] { new Color(1f, 1f, 1f), new Color(0.5f, 0.5f, 0.5f), new Color(0.05f, 0.05f, 0.05f) }
		};
		var tex = new GradientTexture1D { Gradient = grad, Width = W };
		var rect = new TextureRect
		{
			Texture = tex,
			Position = new Vector2(0, GradTop),
			Size = new Vector2(W, GradH),
			StretchMode = TextureRect.StretchModeEnum.Scale
		};
		bar.AddChild(rect);

		for (int i = 0; i < MarketOrder.Length; i++)
		{
			float x = i / (float)(MarketOrder.Length - 1) * W;
			(string label, int darkPercent) = MarketDisplay(MarketOrder[i]);
			var tick = new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture, $"{label}\n{darkPercent}%"),
				Position = new Vector2(Mathf.Clamp(x - TickW / 2f, 0f, W - TickW), TickTop),
				Size = new Vector2(TickW, TickH),
				HorizontalAlignment = HorizontalAlignment.Center
			};
			tick.AddThemeFontSizeOverride("font_size", 11);
			bar.AddChild(tick);
		}

		float cx = MarketIndex(currentId) / (float)(MarketOrder.Length - 1) * W;
		var marker = new Label
		{
			Text = "▲ current",
			Position = new Vector2(Mathf.Clamp(cx - 26f, 0f, W - 52f), CaretTop)
		};
		marker.AddThemeFontSizeOverride("font_size", 12);
		marker.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0f));
		bar.AddChild(marker);

		return bar;
	}

	// ── ND.9g / ND.9h — the Last Vote Snapshot ────────────────────────────────────────────────────

	private static string MarketShiftLabel(int shift) => shift > 0 ? "darker" : shift < 0 ? "lighter" : "hold";

	private void BuildLastVoteSnapshot(CompanyFounding founding, CompanyGovernanceState gov)
	{
		CompanyVoteRecord? rec = gov.VoteHistory.Count > 0 ? gov.VoteHistory[^1] : null;
		if (rec is null) return;

		_infoVBox.AddChild(new HSeparator());
		_infoVBox.AddChild(SectionTitle("Last Vote Snapshot"));
		_infoVBox.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture,
				$"{rec.Kind} vote — opened {FormatDate(rec.OpenedAtMs)}, closed {FormatDate(rec.ClosedAtMs)}")
		});

		// ND.9g — before → after for the three policy dials. Legacy records (closed before ND.9g) have no
		// captured "before" (empty BeforeMarketCategory) → show results only.
		bool hasBefore = !string.IsNullOrEmpty(rec.BeforeMarketCategory);
		if (hasBefore)
		{
			_infoVBox.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"   Reserve (SC%): {rec.BeforeReserveScPercent:F0}%  →  {rec.ResultReserveScPercent:F0}%")
			});
			(string beforeMkt, int beforeDark) = MarketDisplay(rec.BeforeMarketCategory);
			(string afterMkt, int afterDark) = MarketDisplay(rec.ResultMarketCategory);
			_infoVBox.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"   Market level: {beforeMkt} ({beforeDark}% dark)  →  {afterMkt} ({afterDark}% dark)")
			});
			if (rec.Kind == "quarterly")
			{
				_infoVBox.AddChild(new Label
				{
					Text = string.Create(CultureInfo.InvariantCulture,
						$"   Dividend rate: {rec.BeforePayoutRatePercent:F1}%  →  {rec.ResultPayoutRatePercent:F1}% /quarter")
				});
			}
		}
		else
		{
			(string afterMkt, int afterDark) = MarketDisplay(rec.ResultMarketCategory);
			_infoVBox.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"   Result: reserve {rec.ResultReserveScPercent:F0}% SC, market {afterMkt} ({afterDark}% dark)  (before-values not captured — legacy vote)")
			});
		}

		// ND.9g — every participant's cast ballot.
		if (rec.Ballots.Count > 0)
		{
			_infoVBox.AddChild(new Label { Text = "Ballots cast:" });
			foreach (VoteBallotRecord b in rec.Ballots.OrderByDescending(b => b.Weight))
			{
				string market = rec.Kind == "quarterly"
					? string.Create(CultureInfo.InvariantCulture, $", market {MarketShiftLabel(b.MarketShift)}, payout {b.PayoutRatePercent:F1}%")
					: string.Empty;
				_infoVBox.AddChild(new Label
				{
					Text = string.Create(CultureInfo.InvariantCulture,
						$"   {_networkRoot.DescribeAddress(b.HolderId)}  —  weight {b.Weight:P2}  —  voted: reserve {b.ReserveScPercentTarget:F0}%{market}")
				});
			}
		}
		else
		{
			_infoVBox.AddChild(new Label { Text = "   No ballots were cast (result held the prior values)." });
		}

		// ND.9h — on a QUARTERLY snapshot, publish each participant's dividend this quarter (PST split to a
		// daily amount so it's clear why they receive a given amount per day).
		if (rec.Kind == "quarterly" && (rec.FinalizedDividendBtc > 0m || rec.FinalizedDividendSc > 0m))
		{
			decimal totalTokens = founding.Holdings.Sum(h => h.Nst + h.Pst);
			int days = Math.Max(1, rec.QuarterDaysInCycle);
			_infoVBox.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"This quarter's dividends — pool {rec.FinalizedDividendBtc:F8} BTC + {rec.FinalizedDividendSc:F8} SC over {days} day(s):")
			});
			if (totalTokens > 0m)
			{
				foreach (CompanyShareHolding h in founding.Holdings.OrderByDescending(h => h.Nst + h.Pst))
				{
					decimal share = (h.Nst + h.Pst) / totalTokens;
					decimal qBtc = share * rec.FinalizedDividendBtc;
					decimal qSc = share * rec.FinalizedDividendSc;
					bool isNst = h.Nst > 0m;
					_infoVBox.AddChild(new Label
					{
						Text = string.Create(CultureInfo.InvariantCulture,
							$"   {_networkRoot.DescribeAddress(h.HolderId)}  —  {(isNst ? "NST" : "PST")}  —  profit share {share:P2}  —  quarter total {qBtc:F8} BTC + {qSc:F8} SC")
					});
					if (!isNst && rec.QuarterDaysInCycle > 0)
					{
						_infoVBox.AddChild(new Label
						{
							Text = string.Create(CultureInfo.InvariantCulture,
								$"      → daily {qBtc / days:F8} BTC + {qSc / days:F8} SC  × {days} days  (PST drip)")
						});
					}
				}
			}
		}
	}

	// ND.9b — a mouse-transparent bordered Panel inset a few px from the screen edge, sitting behind the
	// content (index 0). Its centre is transparent, so only the coloured frame shows; the colour is set per
	// refresh from the player's holding class. Does NOT touch RootMargin/RootVBox — Ch. 29-safe by design.
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
