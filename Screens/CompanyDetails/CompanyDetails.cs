using Godot;
using System;
using System.Collections.Generic;
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
	private Label _pauseBannerLabel = null!; // P16.8e — names the company whose vote is freezing the game

	// P16.8e — same red as BlockExplorer's "board vote pending" row (§22.16), so the locator and the row it
	// points at are visibly the same signal.
	private static readonly Color WorkRed = new(1f, 0.3f, 0.3f);
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
	private SpinBox? _dividendsCutSpin; // Step 15 P15.4e — shortfall votes only
	private Label? _reservePreviewLabel; // Step 15 P15.9f — live "if the vote closed now" line
	private Label? _voteFeedbackLabel;
	private Label? _policyFeedbackLabel; // Step 16 P16.5c — the Vote Policy panel's own confirmation line
	private CheckBox? _abstainToggle;    // Step 16 P16.8b — intention, resolved by Submit Ballot
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

		// P16.8e — the pause locator, added in code rather than in the .tscn so it sits immediately under
		// the status line on every state this scene can render (including the not-founded / dissolved early
		// returns, which never reach the panel builders).
		_pauseBannerLabel = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.Word,
			Visible = false
		};
		_pauseBannerLabel.AddThemeColorOverride("font_color", WorkRed);
		Node statusParent = _statusLabel.GetParent();
		statusParent.AddChild(_pauseBannerLabel);
		statusParent.MoveChild(_pauseBannerLabel, _statusLabel.GetIndex() + 1);

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

	// Step 15 P15.7c (D-15.9) — a founded bank's lending book, for its shareholders. Every figure comes from
	// NetworkRoot.GetBankLendingSummary, which computes them from the same constants and helpers the
	// repayment itself uses — so the installment shown here is the installment that will actually be
	// charged (§39.16 rule 6). Collateral is valued at the world's current day, never frozen.
	private void BuildBankLendingPanel(CompanyGovernanceState gov)
	{
		NetworkRoot.BankLendingSummary? maybe = NetworkRoot.GetBankLendingSummary(gov.NonMinerNodeId);
		if (maybe is not NetworkRoot.BankLendingSummary s) return;

		_infoVBox!.AddChild(new HSeparator());
		_infoVBox.AddChild(SectionTitle("Bank lending book"));
		_infoVBox.AddChild(new Label
		{
			Text = "This company is a bank: it borrows SC from the Central Bank to buy BTC from other companies, and repays a slice of that debt every quarter by selling the BTC it bought.",
			AutowrapMode = TextServer.AutowrapMode.Word
		});

		_infoVBox.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture,
				$"Central Bank debt: {s.FedDebtSc:N2} SC   (drawn {s.TotalDrawnSc:N2} · repaid {s.TotalRepaidSc:N2})")
		});

		string collateralValue = s.CollateralValueSc > 0m
			? string.Create(CultureInfo.InvariantCulture, $" ≈ {s.CollateralValueSc:N2} SC today")
			: " (no market price for today)";
		_infoVBox.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture,
				$"Collateral held: {s.CollateralBtc:N8} BTC{collateralValue}   ·   {s.ClientCount} client company(ies)")
		});

		// The health line: is the BTC it is sitting on still worth what it borrowed? This is the carry the
		// whole reform exists to create — profitable while BTC rises, dangerous when it falls.
		if (s.FedDebtSc > 0m && s.CollateralValueSc > 0m)
		{
			decimal net = s.CollateralValueSc - s.FedDebtSc;
			var healthLabel = new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"Collateral vs debt: {net:+#,##0.00;-#,##0.00;0.00} SC  ({(net >= 0m ? "covered" : "UNDER-COLLATERALIZED")})")
			};
			healthLabel.AddThemeColorOverride("font_color", net >= 0m ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f));
			_infoVBox.AddChild(healthLabel);
		}

		_infoVBox.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture,
				$"Next installment: {s.NextInstallmentSc:N2} SC due {FormatDate(s.NextPaymentDueMs)}")
		});

		if (s.PendingShortfallSc > 0m)
		{
			var pending = new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"⚠ Shortfall of {s.PendingShortfallSc:N2} SC — a board vote will decide whether it comes out of dividends or reserves."),
				AutowrapMode = TextServer.AutowrapMode.Word
			};
			pending.AddThemeColorOverride("font_color", new Color(1f, 0.75f, 0.3f));
			_infoVBox.AddChild(pending);
		}

		if (s.UnrecoverableShortfallSc > 0m)
		{
			var insolvent = new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"✗ INSOLVENT — {s.UnrecoverableShortfallSc:N2} SC could not be covered by any source. This bank will be closed."),
				AutowrapMode = TextServer.AutowrapMode.Word
			};
			insolvent.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
			_infoVBox.AddChild(insolvent);
		}
	}

	// Step 15 P15.5d — the liquidation notice. This IS the player's notification that a company they held
	// stock in is gone (D-15.15: with no player stake the bots resolve everything silently and the player is
	// told only at the terminal moment). Everything they had already CLAIMED is untouched in their wallet;
	// what died with the company is the stock itself plus anything still unclaimed.
	private void ShowClosureNotice(NonMinerDonationSummary summary, CompanyClosure closure)
	{
		_borderStyle.BorderColor = HoldingBlack; // no stake remains — §22.15's vocabulary
		_identityLabel.Text = NetworkRoot.DescribeCompany(summary);

		string when = DateTimeOffset.FromUnixTimeMilliseconds(closure.ClosedAtUnixMs)
			.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
		string why = closure.Reason == NetworkRoot.ClosureReasonFbiSeizure
			? "SEIZED BY THE FBI"
			: "CLOSED — it could not service its Central Bank debt";

		var lines = new List<string>
		{
			$"✗ This company is {why}.",
			$"Closed {when}."
		};

		if (closure.PlayerNstAtClosure > 0m || closure.PlayerPstAtClosure > 0m)
		{
			string kind = closure.PlayerNstAtClosure > 0m ? "NST (voting)" : "PST (dividend)";
			decimal amount = closure.PlayerNstAtClosure > 0m ? closure.PlayerNstAtClosure : closure.PlayerPstAtClosure;
			lines.Add(string.Create(CultureInfo.InvariantCulture,
				$"Your {amount:N0} {kind} shares were liquidated and are gone, along with all future payments."));
			if (closure.PlayerUnclaimedBtcAtClosure > 0m || closure.PlayerUnclaimedScAtClosure > 0m)
			{
				lines.Add(string.Create(CultureInfo.InvariantCulture,
					$"Unclaimed at closure and lost: {closure.PlayerUnclaimedBtcAtClosure:N8} BTC / {closure.PlayerUnclaimedScAtClosure:N2} SC."));
			}
			lines.Add("Dividends you had already claimed are yours and remain in your wallet.");
		}
		else
		{
			lines.Add("You held no shares in this company.");
		}

		if (closure.WasBank)
		{
			lines.Add(string.Create(CultureInfo.InvariantCulture,
				$"Central Bank loss written off: {closure.DebtAtClosureSc:N2} SC. Its wallet ({closure.BtcAtClosure:N8} BTC at closure) passed into federal custody."));
			lines.Add(string.IsNullOrEmpty(closure.InheritingBankNodeId)
				? "No solvent bank of its market category has inherited that wallet yet — the Central Bank is holding it as BTC."
				: $"Now held by {NetworkRoot.DescribeNodeForDev(closure.InheritingBankNodeId)}.");
		}

		_statusLabel.Text = string.Join("\n", lines);
	}

	private void RefreshAll()
	{
		NonMinerDonationSummary? summary = _networkRoot.GetNonMinerAuctionLedger()
			.FirstOrDefault(s => s.NonMinerAddress == _nonMinerAddress);
		CompanyFounding? founding = _networkRoot.GetCompanyFounding(_nonMinerAddress);

		// P16.8e — computed BEFORE the not-founded / dissolved early returns below, so a frozen game is
		// still explained on a page that renders nothing else useful. A banner updated only on the happy
		// path would go stale exactly where the player is most lost.
		UpdatePauseBanner(founding?.NonMinerNodeId);
		if (summary is null || founding is null)
		{
			// Step 15 P15.5d (D-15.15) — "no founding" now has TWO meanings: not founded yet, or founded and
			// since DISSOLVED (closure removes the founding, which is what destroys the holdings). Tell the
			// two apart rather than showing "not founded yet?" over a company the player watched die.
			CompanyClosure? closure = summary is null ? null : NetworkRoot.GetCompanyClosure(summary.NonMinerNodeId);
			if (closure != null)
			{
				ShowClosureNotice(summary!, closure);
				return;
			}

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

	// Step 16 P16.8e — WHERE the pause is, said on the screen the player is actually looking at. The game
	// freezes globally but only ONE company can lift it, and nothing here used to say which: a player
	// standing in a company they had already voted at saw a normal page, blocked bets, and no route onward.
	// DiceGame's notice names the company, but by the time someone is navigating governance screens they
	// have left it behind. Found the hard way — a paused run where the blocking vote sat at BTC Guild while
	// the player was looking at ArtForz Cluster, which had correctly gone green because its ballot was in.
	//
	// Shown on EVERY company page, including ones the player holds nothing in, and including the blocking
	// company itself — where it reads as confirmation rather than redirection.
	private void UpdatePauseBanner(string? thisCompanyNodeId)
	{
		var blocking = NetworkRoot.GetCompaniesAwaitingPlayerVote();
		if (blocking.Count == 0)
		{
			_pauseBannerLabel.Visible = false;
			return;
		}

		bool isThisOne = thisCompanyNodeId != null
			&& blocking.Any(b => b.nonMinerNodeId == thisCompanyNodeId);
		_pauseBannerLabel.Text = isThisOne
			? "⚠ The game is paused for THIS company's board vote — cast a ballot (or abstain) below to resume play."
			: $"⚠ The game is paused for a board vote at {string.Join(", ", blocking.Select(b => b.companyDisplayName))} — not here. "
				+ "Block Explorer → Enroll Mode → the red \"Vote →\" row.";
		_pauseBannerLabel.Visible = true;
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

		// Step 15 P15.7c (D-15.9) — the BANK LENDING PANEL. A bank is not an ordinary company: most of its
		// balance sheet is borrowed, and the thing that kills it is a payment date, so a shareholder needs
		// the debt/collateral/installment picture the other panels never show. Shown for any founded bank.
		BuildBankLendingPanel(gov);

		// Step 15 P15.6d — the federal-investigation risk line. Shown to ANY viewer (the risk is a fact about
		// the company, not about the holding), and only when there is something to say: the FBI is active,
		// the category is not exempt, and the company is over tolerance or still cooling off.
		string? fbiWarning = NetworkRoot.GetFbiInvestigationWarning(gov.NonMinerNodeId);
		if (fbiWarning != null)
		{
			_infoVBox.AddChild(new HSeparator());
			var fbiLabel = new Label { Text = fbiWarning, AutowrapMode = TextServer.AutowrapMode.Word };
			// Amber while the file grows, red once flagged — the same "colour is never the only signal"
			// rule as §22.15: the text says which state it is.
			fbiLabel.AddThemeColorOverride("font_color",
				gov.InvestigationScore >= NetworkRoot.InvestigationFlagThreshold
					? new Color(1f, 0.4f, 0.4f)
					: new Color(1f, 0.75f, 0.3f));
			_infoVBox.AddChild(fbiLabel);
		}

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
				// P16.8d — the player's own participation, per vote, so a standing abstention leaves a
				// visible record across quarters rather than only on the newest snapshot below. Same
				// derivation (no ballot + held NST = abstained); "—" where the player holds no NST at all.
				bool playerHeldNst = founding.Holdings.Any(h => h.HolderId == PlayerNodeId && h.Nst > 0m);
				string mine = !playerHeldNst
					? string.Empty
					: rec.Ballots.FirstOrDefault(b => b.HolderId == PlayerNodeId) is { } pb
						? string.Create(CultureInfo.InvariantCulture,
							$"  |  you voted {pb.ReserveScPercentTarget:F0}%{(pb.WasAutoCast ? " (policy)" : string.Empty)}")
						: "  |  you abstained";
				_infoVBox.AddChild(new Label
				{
					Text = string.Create(CultureInfo.InvariantCulture,
						$"{FormatDate(rec.ClosedAtMs)}  {rec.Kind}: reserve {rec.ResultReserveScPercent:F0}% SC, market {rec.ResultMarketCategory}{dividend}{mine}")
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

		// P16.5c — the pause toggle joins the signature: turning it on/off changes what the panel SAYS the
		// game will do at the next vote, and that explanation would otherwise sit stale until the next vote
		// opened. The policy's numeric fields deliberately do NOT join it — they are edited in-place, and
		// rebuilding on every keystroke would fight the player's own typing (the reason this gate exists).
		// P16.8 — the standing abstention joins it for exactly the same reason, and it changes MORE of the
		// explanation than the pause does (it also decides whether the pause row is live at all).
		string signature = string.Create(CultureInfo.InvariantCulture,
			$"{hasNst}|{hasPst}|{gov?.OpenVote?.OpenedAtMs ?? 0}|{gov?.OpenVote?.AwaitingPlayerVote ?? false}|{gov?.OpenVote?.Kind ?? ""}|{gov?.PlayerPauseOnVotes ?? false}|{gov?.PlayerAutoAbstain ?? false}");
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
		_dividendsCutSpin = null;
		_reservePreviewLabel = null;
		_voteFeedbackLabel = null;
		_policyFeedbackLabel = null;
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

	// Step 16 P16.5c (D-16.11…13) — the vote policy, shown ABOVE the ballot form because it governs whether
	// that form will ever be waited for. The step15 §10 audit (F2) measured the old always-pause behaviour
	// at 93 full-simulation freezes for ~2 outcome changes, at a rate that RISES with how successful the
	// player's holdings are. So the pause is opt-in per company, and with it off the standing policy votes.
	private void BuildVotePolicyPanel(CompanyGovernanceState gov)
	{
		string nodeId = gov.NonMinerNodeId;
		(bool pause, decimal reserve, bool reserveConfigured,
			decimal payout, bool payoutConfigured,
			decimal cut, bool cutConfigured, bool autoAbstain) = NetworkRoot.GetPlayerVotePolicy(nodeId);

		_actionVBox.AddChild(SectionTitle("Vote Policy"));

		var pauseCheck = new CheckBox
		{
			Text = "Pause the game for this company's votes",
			ButtonPressed = pause
		};
		_actionVBox.AddChild(pauseCheck);

		// Step 16 P16.8 (D-16.19) — the standing abstention. A different question from the pause: that one
		// asks "should the game stop to ask me?", this one asks "do I want a say here at all?". It outranks
		// the pause in OpenCompanyVote, so the pause row is disabled while it is on rather than left looking
		// live — a control that has no effect must not read as one that does (§39.16 rule 6's sibling).
		var abstainCheck = new CheckBox
		{
			Text = "Abstain from every vote at this company",
			ButtonPressed = autoAbstain
		};
		_actionVBox.AddChild(abstainCheck);

		var explain = new Label { AutowrapMode = TextServer.AutowrapMode.Word };
		explain.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
		_actionVBox.AddChild(explain);

		(decimal min, decimal max) = NetworkRoot.BandScPercentBounds(gov.CurrencyBand);
		decimal defaultRate = NetworkRoot.DefaultQuarterlyPayoutRatePercent(gov.MarketCategory);

		var reserveRow = new HBoxContainer();
		reserveRow.AddChild(new Label { Text = string.Create(CultureInfo.InvariantCulture, $"Auto reserve target (% SC, {min:F0}–{max:F0}):  ") });
		var policyReserve = new SpinBox { MinValue = (double)min, MaxValue = (double)max, Step = 1, Value = (double)reserve };
		reserveRow.AddChild(policyReserve);
		_actionVBox.AddChild(reserveRow);

		var payoutRow = new HBoxContainer();
		payoutRow.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture, $"Auto payout rate (%/quarter, max {defaultRate * 2m:F0}):  ")
		});
		var policyPayout = new SpinBox { MinValue = 0, MaxValue = (double)(defaultRate * 2m), Step = 0.5, Value = (double)payout };
		payoutRow.AddChild(policyPayout);
		_actionVBox.AddChild(payoutRow);

		var cutRow = new HBoxContainer();
		cutRow.AddChild(new Label { Text = "Auto shortfall split (% taken from dividends):  " });
		var policyCut = new SpinBox { MinValue = 0, MaxValue = 100, Step = 5, Value = (double)cut };
		cutRow.AddChild(policyCut);
		_actionVBox.AddChild(cutRow);

		// Step 16 P16.8f — the dials are editable EXACTLY WHEN THEY STEER SOMETHING, i.e. whenever neither
		// tick is on. One rule, one direction, no unlock ritual.
		//
		// P16.8c had a second lock — "configured and saved ⇒ locked, press Follow Status Quo to unlock" —
		// meant to make a standing order legible as committed. It backfired: `configured` is PERSISTED, so
		// the dials came back disabled on every later visit, and the only way to touch them again was to
		// destroy the policy first. A lock whose sole escape is discarding the thing it protects is not
		// protecting it. **A control that is read-only on arrival must be re-openable without losing work —
		// otherwise "locked" just means "broken until you delete something".**
		//
		// The blanking stays, and it is the honest half: with a tick on, a stale "24%" sitting in a greyed
		// box reads as "24% is what gets sent", which is precisely what will not happen.
		// D-16.12 — an unconfigured policy votes the STATUS QUO, and the panel says so plainly. Showing a
		// resolved number with no note would read as a choice the player had made, which is exactly the
		// trust this feature spends: it votes on their behalf.
		bool anyDefault = !reserveConfigured || !payoutConfigured || !cutConfigured;
		var statusLabel = new Label { AutowrapMode = TextServer.AutowrapMode.Word };
		statusLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
		_actionVBox.AddChild(statusLabel);

		var buttonRow = new HBoxContainer();
		var saveBtn = new Button { Text = "Save Policy" };
		var clearBtn = new Button { Text = "Follow Status Quo" };

		// P16.8h — "Follow Status Quo" resets the dials to the company's current numbers, but does NOT
		// write: only Save Policy commits. `pendingClear` carries the intent across to Save, which then
		// stores the -1 "not configured" sentinels rather than the displayed values — the distinction
		// matters, because a not-configured policy keeps FOLLOWING the company as its numbers move, while a
		// policy configured at today's number stays pinned to it forever. Any manual edit cancels the
		// intent, since the player has just said what they want instead.
		bool pendingClear = false;

		// One place that re-derives everything the tick state governs — the two checkboxes' mutual
		// exclusion, the dial locks, the two explanatory lines and the Clear button — so no path can update
		// half of them. Called on build and on every toggle.
		void SyncPolicyControls()
		{
			bool ticked = pauseCheck.ButtonPressed || abstainCheck.ButtonPressed;

			// P16.8i — DEFENSIVE only: a world saved under P16.8's first cut can hold both flags true, and
			// Abstain is the one that wins in OpenCompanyVote. The live exclusion is done by the Toggled
			// handlers below, NOT here — see why disabling was wrong at the handlers.
			if (abstainCheck.ButtonPressed && pauseCheck.ButtonPressed)
			{
				pauseCheck.SetPressedNoSignal(false);
			}

			if (ticked)
			{
				policyReserve.Value = (double)gov.ReserveScPercent;
				policyPayout.Value = (double)gov.QuarterPayoutRatePercent;
				policyCut.Value = (double)NetworkRoot.DefaultShortfallDividendsCutPercent;
			}
			policyReserve.Editable = !ticked;
			policyPayout.Editable = !ticked;
			policyCut.Editable = !ticked;

			// P16.8h — with a tick on, the dials steer nothing, so there is nothing to reset TO the status
			// quo: the button would write a policy change the player cannot see the effect of. Disabled
			// rather than hidden, for the §41.4 reason — the reason is the part worth teaching.
			clearBtn.Disabled = ticked;

			explain.Text = abstainCheck.ButtonPressed
				? "You cast no ballot here. Your NST still counts toward the company's total, but the other "
					+ "holders decide — and their relative weight rises because you sat out."
				: pauseCheck.ButtonPressed
					? "The simulation stops at every vote here until you cast a ballot."
					: "Votes here are cast automatically by the policy below — the game never stops for them.";

			statusLabel.Text = abstainCheck.ButtonPressed
				? "No ballot is cast here, so these dials steer nothing. Untick Abstain to use them again."
				: pauseCheck.ButtonPressed
					? "You vote by hand at the Board Vote form below, so these dials steer nothing. Untick "
						+ "Pause to auto-vote them instead."
					: anyDefault
						? "Fields not yet configured follow the company's current values, so an untouched policy "
							+ "changes nothing — it votes the status quo. Market direction is never cast "
							+ "automatically: a category shift is hard to undo, so it needs you."
						: "These values are cast automatically at every vote here. Edit and Save to change "
							+ "them, or press \"Follow Status Quo\" then Save to go back to following the "
							+ "company. Market direction is never cast automatically.";
		}

		// P16.8i — the exclusion is SYMMETRIC: whichever box you tick clears the other, and BOTH stay
		// clickable at all times. P16.8h disabled Pause while Abstain was on, to express "Abstain outranks
		// Pause" — but that made switching a ONE-WAY DOOR: Abstain could always be pressed, Pause could not,
		// so going back required knowing to untick Abstain first, which nothing on screen said. Reported
		// after exactly that: two successful switches (both of which had passed through unticking) and then
		// a third that could not be made at all.
		//
		// With true mutual exclusion the precedence question disappears — the two states cannot coexist, so
		// there is nothing left for one to outrank. `SetPressedNoSignal` avoids re-entering the other
		// handler; `SyncPolicyControls` then re-derives everything from the settled pair.
		pauseCheck.Toggled += on =>
		{
			if (on) abstainCheck.SetPressedNoSignal(false);
			SyncPolicyControls();
		};
		abstainCheck.Toggled += on =>
		{
			if (on) pauseCheck.SetPressedNoSignal(false);
			SyncPolicyControls();
		};
		policyReserve.ValueChanged += _ => pendingClear = false;
		policyPayout.ValueChanged += _ => pendingClear = false;
		policyCut.ValueChanged += _ => pendingClear = false;
		SyncPolicyControls();
		// P16.8h — the ONLY write in this panel. Ticking a box, turning a dial or pressing Follow Status Quo
		// all change the FORM; nothing reaches the world until here. That was already true of the ticks and
		// dials, and Follow Status Quo was the one control that broke the rule — it wrote immediately, which
		// is why it could report "Policy cleared" on a press the player had not confirmed.
		saveBtn.Pressed += () =>
		{
			bool clearing = pendingClear;
			NetworkRoot.SetPlayerVotePolicy(nodeId, pauseCheck.ButtonPressed,
				clearing ? -1m : (decimal)policyReserve.Value,
				clearing ? -1m : (decimal)policyPayout.Value,
				clearing ? -1m : (decimal)policyCut.Value,
				abstainCheck.ButtonPressed);
			pendingClear = false;
			SyncPolicyControls();

			SetPolicyFeedback(abstainCheck.ButtonPressed
				? "Policy saved. You abstain from every vote here — no ballot, no pause."
				: pauseCheck.ButtonPressed
					? "Policy saved. Votes here will pause the game until you cast a ballot."
					: clearing
						? "Policy saved — auto-votes now follow the company's current values."
						: string.Create(CultureInfo.InvariantCulture,
							$"Policy saved. Every vote here is cast automatically at {(decimal)policyReserve.Value:F0}% SC reserve."));
		};
		buttonRow.AddChild(saveBtn);

		// Resets the dials to the company's current numbers and ARMS the "not configured" write — the player
		// can go back to following the company without having to know its current values. Deliberately does
		// NOT touch the abstention: that is a participation choice, not one of the three dials, and silently
		// opting the player back into voting is the surprise this panel exists to avoid.
		clearBtn.Pressed += () =>
		{
			policyReserve.Value = (double)gov.ReserveScPercent;
			policyPayout.Value = (double)gov.QuarterPayoutRatePercent;
			policyCut.Value = (double)NetworkRoot.DefaultShortfallDividendsCutPercent;
			pendingClear = true; // set AFTER the assignments — each fires ValueChanged, which clears it
			SetPolicyFeedback("Dials reset to the company's current values — press \"Save Policy\" to apply.");
		};
		buttonRow.AddChild(clearBtn);
		_actionVBox.AddChild(buttonRow);

		// Its own feedback line, NOT the ballot form's: the policy panel is built before that label exists,
		// and pressing Save with the pause unchanged leaves the panel signature untouched (so nothing
		// rebuilds) — a button with no visible effect reads as a broken button.
		_policyFeedbackLabel = new Label { Text = " ", AutowrapMode = TextServer.AutowrapMode.Word };
		_actionVBox.AddChild(_policyFeedbackLabel);
		_actionVBox.AddChild(new HSeparator());
	}

	private void SetPolicyFeedback(string text)
	{
		if (_policyFeedbackLabel != null)
		{
			_policyFeedbackLabel.Text = text;
		}
	}

	private void BuildBoardVotePanel(CompanyFounding founding, CompanyGovernanceState gov)
	{
		// P16.8b — drop the previous build's toggle before anything can read it. OnSubmitBallot consults it
		// to decide between a ballot and an abstention, and that is the path that unfreezes a paused game;
		// a stale reference to a freed node there is the one place this must not be clever.
		_abstainToggle = null;

		// P16.8g — while THIS company's vote is holding the game, the Vote Policy panel is hidden entirely.
		// It answers "what should be cast when I'm not here" — a question that is not being asked right now,
		// since the game stopped precisely to ask the player in person. Showing both put two sets of reserve
		// / payout dials on screen at once, one of them inert and greyed, immediately above the live ballot;
		// the greyed pair is the one the eye lands on first, and it is the wrong one. It returns the moment
		// the ballot is submitted.
		bool awaitingHere = gov.OpenVote is { AwaitingPlayerVote: true };
		if (!awaitingHere)
		{
			BuildVotePolicyPanel(gov);
		}
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

		// Step 15 P15.9f — the ballots ALREADY CAST in this vote. Until now the only ballot list in the
		// scene was the Last Vote Snapshot, which shows a CLOSED vote — always one quarter too late to be
		// useful. The bots cast the instant the vote opens, so at the moment the game pauses and asks the
		// player to vote, every other ballot is already known and persisted; not showing them meant voting
		// blind against information the engine had in hand. Shown for every vote kind and whether or not
		// the player has voted yet.
		BuildOpenVoteBallotList(founding, gov, vote);

		// Step 16 P16.8b — the ballot FORM is shown only while this vote is genuinely waiting on the player.
		// It used to be the fall-through case of "player has no ballot", which was correct while the only two
		// outcomes were "cast" and "still pausing" — P16.8 added a third, ABSTAINED, whose shape is identical
		// to not-having-voted-yet (no entry in vote.Ballots) but whose meaning is the opposite. The panel
		// therefore re-rendered a live Submit/Abstain form after an abstention, reading as though nothing had
		// happened; pressing Submit then silently REPLACED the abstention with a real ballot. Gating on
		// AwaitingPlayerVote makes the four states exhaustive and mutually exclusive.
		//
		// General rule: when a new outcome shares its DATA shape with an existing one, every branch that
		// distinguished them by that shape is now ambiguous — find them before adding the outcome, not after.
		bool playerVoted = vote.Ballots.TryGetValue(PlayerNodeId, out CompanyBallot? playerBallot);
		if (!vote.AwaitingPlayerVote)
		{
			string how;
			if (playerVoted)
			{
				// P16.5c (D-16.13) — an auto-cast ballot must never read as one the player deliberated. Say
				// which it was, and what it cast, so a policy that is quietly voting badly is discoverable.
				how = playerBallot!.WasAutoCast
					? string.Create(CultureInfo.InvariantCulture,
						$"Your standing policy voted for you ({playerBallot.ReserveScPercentTarget:F0}% SC reserve)")
					: "Ballot registered";
			}
			else
			{
				how = gov.PlayerAutoAbstain
					? "You are abstaining here — your standing policy casts no ballot"
					: "You abstained from this vote";
			}

			_actionVBox.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"{how} — the {vote.Kind} vote closes {FormatDate(vote.ClosesAtMs)} and applies from the next day."),
				AutowrapMode = TextServer.AutowrapMode.Word
			});
			return;
		}

		bool quarterly = vote.Kind == "quarterly";
		// Step 15 P15.4e — a SHORTFALL vote asks exactly one question, and answering the usual reserve /
		// market / payout dials would be misleading (the resolver ignores them for this kind). So the panel
		// swaps its whole body out rather than adding a fourth row.
		bool shortfall = vote.Kind == NetworkRoot.CompanyVoteKindShortfall;
		if (shortfall)
		{
			BuildShortfallBallot(gov, vote);
			return;
		}

		(decimal min, decimal max) = NetworkRoot.BandScPercentBounds(gov.CurrencyBand);

		var reserveRow = new HBoxContainer();
		reserveRow.AddChild(new Label { Text = string.Create(CultureInfo.InvariantCulture, $"Reserve target (% held as SC, band {gov.CurrencyBand}: {min:F0}–{max:F0}):  ") });
		_reserveSpin = new SpinBox { MinValue = (double)min, MaxValue = (double)max, Step = 1, Value = (double)gov.ReserveScPercent };
		reserveRow.AddChild(_reserveSpin);
		_actionVBox.AddChild(reserveRow);

		// P15.9f — the live "where does my dial land the result" line, recomputed on every turn of the
		// SpinBox through the SAME helper CloseCompanyVote resolves with, so what is promised here is what
		// the vote will do (§39.16 rule 6).
		_reservePreviewLabel = new Label { AutowrapMode = TextServer.AutowrapMode.Word };
		_actionVBox.AddChild(_reservePreviewLabel);
		UpdateReservePreview(founding, gov, vote);
		_reserveSpin.ValueChanged += _ => UpdateReservePreview(founding, gov, vote);

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

			// Step 15 P15.10a (D-15.25) — a bank's category is LOCKED (D-15.12), so every option in this
			// dropdown is counted and then refused. Disable + explain rather than hide: the reason IS the
			// interesting part (this company's category is the distance other companies' financier selection
			// is measured on), and a silently missing control invites "why does this one have fewer dials?".
			// Left on index 1, so the submitted shift is 0 by construction — never null the field instead,
			// OnSubmitBallot reads it and it survives panel rebuilds.
			if (NetworkRoot.IsBankCompany(gov.NonMinerNodeId))
			{
				_marketOption.Disabled = true;
				var lockedNote = new Label
				{
					Text = "Category locked — a bank's category is fixed at its roster default, because it is "
						+ "the distance other companies' financier selection is measured on (D-15.12). Ballots "
						+ "for a shift are still recorded, and still refused.",
					AutowrapMode = TextServer.AutowrapMode.Word
				};
				lockedNote.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
				_actionVBox.AddChild(lockedNote);
			}

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

		// Step 16 P16.8b — Abstain is a TOGGLE, not a second button. Two buttons that both resolve the vote
		// meant two ways to resume a paused game, and the paused game is the most fragile state in the
		// project: whichever one the player did not press was still sitting there live afterwards. As a
		// toggle it states an INTENTION — the dials clear and lock so the form stops implying it will send
		// them, and the forecast switches to the without-me outcome — while `Submit Ballot` stays the single
		// axis that unfreezes the simulation, exactly as it was before this phase.
		_abstainToggle = new CheckBox
		{
			Text = "Abstain — cast no ballot at this vote",
			TooltipText = "Your NST still counts toward the company's total, but the holders who do vote "
				+ "carry proportionally more weight. Press Submit Ballot to confirm."
		};
		_actionVBox.AddChild(_abstainToggle);
		_abstainToggle.Toggled += on =>
		{
			// P16.8k — the ballot dials are LOCKED but NOT blanked. P16.8b blanked them on the same
			// reasoning the policy panel uses ("a greyed 24% reads as a promise to send 24%"), but that
			// reasoning does not survive the two-branch preview below: the "vote N%" line is one half of the
			// comparison the player is making, so wiping N the instant they consider abstaining destroys the
			// number they are trying to weigh it against. The "»" marker states which branch is selected, so
			// there is nothing left for a stale value to imply.
			//
			// The policy panel still blanks, correctly — it has no marker and no alternative on screen.
			// Same widget, opposite call, because the surrounding readout differs.
			if (_reserveSpin != null) _reserveSpin.Editable = !on;
			if (_marketOption != null && !NetworkRoot.IsBankCompany(gov.NonMinerNodeId))
			{
				_marketOption.Disabled = on;
			}
			if (_payoutSpin != null) _payoutSpin.Editable = !on;

			UpdateReservePreview(founding, gov, vote);
		};

		string nodeId = gov.NonMinerNodeId;
		var submitBtn = new Button { Text = "Submit Ballot" };
		submitBtn.Pressed += () => OnSubmitBallot(nodeId, quarterly, shortfall: false, gov.ReserveScPercent);
		_actionVBox.AddChild(submitBtn);

		_voteFeedbackLabel = new Label { Text = vote.AwaitingPlayerVote ? "The game is paused until you vote." : " " };
		_actionVBox.AddChild(_voteFeedbackLabel);
	}

	// Step 15 P15.9f — every ballot already cast in the OPEN vote, plus who has not voted yet. The weights
	// are the resolver's own (holder NST ÷ total NST, D-ND8.19b), so a holder can see exactly how much of
	// the outcome their own ballot commands before they cast it.
	private void BuildOpenVoteBallotList(CompanyFounding founding, CompanyGovernanceState gov, CompanyVote vote)
	{
		decimal totalNst = founding.Holdings.Where(h => h.Nst > 0m).Sum(h => h.Nst);
		if (totalNst <= 0m) return;

		bool shortfall = vote.Kind == NetworkRoot.CompanyVoteKindShortfall;
		(decimal min, decimal max) = NetworkRoot.BandScPercentBounds(gov.CurrencyBand);
		string header = shortfall
			? "Ballots cast so far (shortfall split):"
			: string.Create(CultureInfo.InvariantCulture,
				$"Ballots cast so far (band {gov.CurrencyBand}: {min:F0}–{max:F0}% SC):");
		_actionVBox.AddChild(new Label { Text = header });

		foreach (CompanyShareHolding h in founding.Holdings.Where(h => h.Nst > 0m).OrderByDescending(h => h.Nst))
		{
			decimal weight = h.Nst / totalNst;
			string who = h.HolderId == PlayerNodeId ? "You" : _networkRoot.DescribeAddress(h.HolderId);
			string cast;
			if (!vote.Ballots.TryGetValue(h.HolderId, out CompanyBallot? ballot))
			{
				// P16.8 — "not voted yet" is only true while a ballot may still ARRIVE, and after this phase
				// that is exactly one case: the player, paused, undecided. A BOT's ballots are all cast the
				// instant the vote opens (OpenCompanyVote), so a missing bot entry has always meant it
				// abstained — the old wording just predated the player having the same option and read as if
				// the bot were still thinking. And with the standing abstention on, the player is not being
				// waited on at all, so the parenthetical would be a plain untruth (§39.16 rule 6).
				cast = h.HolderId != PlayerNodeId
					? "— abstained"
					: vote.AwaitingPlayerVote
						? "— not voted yet (this vote is waiting on you)"
						: gov.PlayerAutoAbstain
							? "— abstaining (your standing policy)"
							: "— abstained";
			}
			else if (shortfall)
			{
				cast = string.Create(CultureInfo.InvariantCulture,
					$"— voted: {ballot.DividendsCutPercent:F0}% out of dividends / {100m - ballot.DividendsCutPercent:F0}% out of reserves");
			}
			else
			{
				string extra = vote.Kind == "quarterly"
					? string.Create(CultureInfo.InvariantCulture,
						$", market {MarketShiftLabel(ballot.MarketShift)}, payout {ballot.PayoutRatePercent:F1}%")
					: string.Empty;
				cast = string.Create(CultureInfo.InvariantCulture,
					$"— voted: reserve {ballot.ReserveScPercentTarget:F0}%{extra}");
			}

			_actionVBox.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture, $"   {who}  —  weight {weight:P2}  {cast}")
			});
		}
	}

	// Step 15 P15.9f — "if the vote closed now", live against the reserve dial. Two numbers, because they
	// answer different questions: where the already-cast ballots stand on their own, and where the player's
	// current dial position would land the result once their weight joins.
	private void UpdateReservePreview(CompanyFounding founding, CompanyGovernanceState gov, CompanyVote vote)
	{
		if (_reservePreviewLabel == null) return;

		NetworkRoot.ReserveVoteOutcome cast = NetworkRoot.ComputeReserveVoteOutcome(
			founding, gov.CurrencyBand, vote.Ballots, gov.ReserveScPercent);

		string others = cast.HasVotes
			? string.Create(CultureInfo.InvariantCulture,
				$"Other holders have cast {cast.VotedWeight:P0} of the votes, averaging {cast.RawAverage:F2}% SC.")
			: "No other ballot has been cast yet.";

		// P16.8j — the SPAN the player's weight controls, not just the sample their dial happens to sit on.
		// The line used to show "your 84% closes at 85.38, abstaining closes at 85.79" and nothing else,
		// which reads as "my choice changes nothing" whenever the dial is near the others' average — the
		// case that prompted this. The two extremes answer the real question ("is my vote worth anything
		// here?") in one glance: at 84 the delta is 0.4 points, but the same ballot dialled to the band
		// bounds moves the close across ~6. Both bounds go through the resolver rather than being computed
		// locally, so the band clamp is applied exactly as CloseCompanyVote will apply it (§39.16 rule 6).
		(decimal bandMin, decimal bandMax) = NetworkRoot.BandScPercentBounds(gov.CurrencyBand);
		decimal OutcomeWithPlayerAt(decimal target)
		{
			var hypothetical = new Dictionary<string, CompanyBallot>(vote.Ballots)
			{
				[PlayerNodeId] = new CompanyBallot { ReserveScPercentTarget = target }
			};
			return NetworkRoot.ComputeReserveVoteOutcome(
				founding, gov.CurrencyBand, hypothetical, gov.ReserveScPercent).Outcome;
		}

		decimal lowEnd = OutcomeWithPlayerAt(bandMin);
		decimal highEnd = OutcomeWithPlayerAt(bandMax);
		string reach = Math.Abs(highEnd - lowEnd) >= 0.01m
			? string.Create(CultureInfo.InvariantCulture,
				$"Your {PlayerVoteWeight(founding):P2} can move the close anywhere between {lowEnd:F2}% and {highEnd:F2}%.")
			: "Your holding is too small to move the close measurably.";

		// P16.8k — BOTH branches are shown, always, as parallel lines, with "»" marking the one the toggle
		// currently selects. The previous wording put the chosen outcome mid-sentence and closed with
		// "(Now 83.79%)" — and the current value, which by definition cannot move until the vote closes, got
		// read as the RESULT. So the panel looked like it was reporting the same unchanging number no matter
		// what was chosen, which is the exact opposite of what it was built to show.
		//
		// Two rules came out of it: the CURRENT value gets its own line, named as the starting point and
		// never sharing a line with an outcome; and the alternatives are laid out identically so the only
		// difference the eye has to find is the number. Showing both regardless of the toggle also means
		// toggling changes only the emphasis, never the information — the comparison IS the decision.
		bool abstaining = _abstainToggle?.ButtonPressed ?? false;
		decimal mine = (decimal)(_reserveSpin?.Value ?? 0d);

		string abstainOutcome = cast.HasVotes
			? string.Create(CultureInfo.InvariantCulture, $"closes at {cast.Outcome:F2}% SC — the other holders decide")
			: string.Create(CultureInfo.InvariantCulture,
				$"no quorum (nobody else voted) — holds at {gov.ReserveScPercent:F2}% SC, payout falls back to the category default");

		string header = string.Create(CultureInfo.InvariantCulture,
			$"Reserve target now: {gov.ReserveScPercent:F2}% SC.  {others}");
		string voteLine = string.Create(CultureInfo.InvariantCulture,
			$"   {(abstaining ? "  " : "» ")}vote {mine:F0}%  →  closes at {OutcomeWithPlayerAt(mine):F2}% SC");
		string abstainLine = string.Create(CultureInfo.InvariantCulture,
			$"   {(abstaining ? "» " : "  ")}abstain  →  {abstainOutcome}");

		_reservePreviewLabel.Text = string.Join("\n", header, voteLine, abstainLine, reach);
	}

	// The player's share of this company's voting stock — the resolver's own weight (holder NST ÷ total
	// NST, D-ND8.19b), which is what bounds how far their ballot can pull the weighted average.
	private static decimal PlayerVoteWeight(CompanyFounding founding)
	{
		decimal totalNst = founding.Holdings.Where(h => h.Nst > 0m).Sum(h => h.Nst);
		if (totalNst <= 0m) return 0m;
		return (founding.Holdings.FirstOrDefault(h => h.HolderId == PlayerNodeId)?.Nst ?? 0m) / totalNst;
	}

	// Step 15 P15.4e (D-15.7) — the bank shortfall ballot: one dial deciding WHO absorbs the SC this bank
	// could not raise from collateral to pay its quarterly FED installment. Higher = shareholders forgo
	// that slice of this quarter's dividend; lower = the company's own SC reserve pays instead. The
	// no/tied-vote default is 50/50. If neither source can close the gap the bank becomes insolvent.
	private void BuildShortfallBallot(CompanyGovernanceState gov, CompanyVote vote)
	{
		_actionVBox!.AddChild(new Label
		{
			Text = string.Create(CultureInfo.InvariantCulture,
				$"This bank could not raise {vote.ShortfallScTarget:N2} SC of its Central Bank installment by selling collateral."),
			AutowrapMode = TextServer.AutowrapMode.Word
		});
		_actionVBox.AddChild(new Label
		{
			Text = "Vote how much of that gap comes out of SHAREHOLDERS' dividends; the rest comes out of the company's own SC reserve.",
			AutowrapMode = TextServer.AutowrapMode.Word
		});

		var cutRow = new HBoxContainer();
		cutRow.AddChild(new Label { Text = "Cut from dividends (%, the rest from reserves):  " });
		_dividendsCutSpin = new SpinBox
		{
			MinValue = 0,
			MaxValue = 100,
			Step = 5,
			Value = (double)NetworkRoot.DefaultShortfallDividendsCutPercent
		};
		cutRow.AddChild(_dividendsCutSpin);
		_actionVBox.AddChild(cutRow);

		// P16.8b — same toggle-then-Submit shape as the quarterly form; a shortfall vote pauses the game
		// exactly the same way, so it must not grow a second resume path either.
		_abstainToggle = new CheckBox
		{
			Text = "Abstain — cast no ballot at this vote",
			TooltipText = "The remaining holders decide the split. Press Submit Ballot to confirm."
		};
		_actionVBox.AddChild(_abstainToggle);
		_abstainToggle.Toggled += on =>
		{
			if (_dividendsCutSpin != null)
			{
				_dividendsCutSpin.Editable = !on;
				if (on) _dividendsCutSpin.Value = (double)NetworkRoot.DefaultShortfallDividendsCutPercent;
			}
		};

		string nodeId = gov.NonMinerNodeId;
		var submitBtn = new Button { Text = "Submit Ballot" };
		submitBtn.Pressed += () => OnSubmitBallot(nodeId, quarterly: false, shortfall: true, gov.ReserveScPercent);
		_actionVBox.AddChild(submitBtn);

		_voteFeedbackLabel = new Label { Text = vote.AwaitingPlayerVote ? "The game is paused until you vote." : " " };
		_actionVBox.AddChild(_voteFeedbackLabel);
	}

	// currentReservePercent is the "no change" echo used when this ballot has no reserve control at all
	// (a shortfall vote): the resolver ignores the field for that kind, but the recorded ballot should read
	// "leave the mix where it is" rather than a spurious band-minimum.
	private void OnSubmitBallot(string nonMinerNodeId, bool quarterly, bool shortfall, decimal currentReservePercent)
	{
		// Step 16 P16.8b — the single resume axis. With the Abstain toggle set this registers an ABSTENTION
		// (no entry written at all, so the player's weight leaves the denominator and every other holder's
		// share rises); otherwise it registers the dialled ballot. Note it never submits a ballot of zeros —
		// that would drag the weighted average down and pin the reserve to the band floor, which is the
		// P15.9 failure arriving through a new door.
		bool abstaining = _abstainToggle?.ButtonPressed ?? false;
		bool ok = abstaining
			? NetworkRoot.TryRegisterPlayerAbstention(nonMinerNodeId)
			: NetworkRoot.TryRegisterPlayerVote(nonMinerNodeId,
				(decimal)(_reserveSpin?.Value ?? (double)currentReservePercent),
				quarterly ? (_marketOption?.Selected ?? 1) - 1 : 0,
				quarterly ? (decimal)(_payoutSpin?.Value ?? 0d) : 0m,
				shortfall
					? (decimal)(_dividendsCutSpin?.Value ?? (double)NetworkRoot.DefaultShortfallDividendsCutPercent)
					: NetworkRoot.DefaultShortfallDividendsCutPercent);

		if (_voteFeedbackLabel != null)
		{
			_voteFeedbackLabel.Text = ok
				? abstaining
					? "Abstained — play resumes and the remaining holders decide this one."
					: "Ballot registered — play resumes; the result applies when the vote closes."
				: "Could not register (the vote may have closed).";
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

			// Step 15 P15.10b (D-15.25) — an unchanged Market level is normally self-explanatory (nobody
			// reached the 60% supermajority), but at a BANK it can also mean the holders DID win a shift and
			// the lock refused it. That case is the one this line exists for; where no supermajority was
			// reached the line above already tells the true story and nothing is appended. Re-derived from
			// the record's own ballots through the resolver's predicate — no persisted flag, so it reads
			// correctly on votes closed before this shipped, and no WorldFormatVersion bump.
			if (NetworkRoot.WasMarketShiftRefused(rec, gov.NonMinerNodeId))
			{
				var refused = new Label
				{
					Text = "      ↳ market shift refused — category locked (bank)",
					AutowrapMode = TextServer.AutowrapMode.Word
				};
				refused.AddThemeColorOverride("font_color", new Color(1f, 0.75f, 0.3f));
				_infoVBox.AddChild(refused);
			}
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
			// P15.9 — name the band range in the header. This is the readout that surfaced the out-of-band
			// bot ballots (bots voted their raw global stance, e.g. 0% at a CB1 company whose charter allows
			// only 75–100), and reading a bare "voted: reserve 0%" required remembering which band this
			// company is. With the range stated, an illegal value is obvious on sight.
			(decimal ballotMin, decimal ballotMax) = NetworkRoot.BandScPercentBounds(gov.CurrencyBand);
			_infoVBox.AddChild(new Label
			{
				Text = string.Create(CultureInfo.InvariantCulture,
					$"Ballots cast (band {gov.CurrencyBand}: {ballotMin:F0}–{ballotMax:F0}% SC):")
			});
			foreach (VoteBallotRecord b in rec.Ballots.OrderByDescending(b => b.Weight))
			{
				string market = rec.Kind == "quarterly"
					? string.Create(CultureInfo.InvariantCulture, $", market {MarketShiftLabel(b.MarketShift)}, payout {b.PayoutRatePercent:F1}%")
					: string.Empty;
				string how = b.WasAutoCast ? " (standing policy)" : string.Empty;
				_infoVBox.AddChild(new Label
				{
					Text = string.Create(CultureInfo.InvariantCulture,
						$"   {_networkRoot.DescribeAddress(b.HolderId)}  —  weight {b.Weight:P2}  —  voted: reserve {b.ReserveScPercentTarget:F0}%{market}{how}")
				});
			}
		}
		else
		{
			_infoVBox.AddChild(new Label
			{
				Text = "   No quorum — every holder abstained, so the reserve held and the payout rate fell "
					+ "back to this company's category default.",
				AutowrapMode = TextServer.AutowrapMode.Word
			});
		}

		// Step 16 P16.8d — WHO SAT OUT. Only cast ballots were ever recorded, so an abstention left no trace
		// anywhere in the scene: after abstaining (by hand or by standing policy) the player could not
		// confirm it had happened, and a bot's abstention was equally invisible — yet it is the thing that
		// MOVED everyone else's weight, so it explains the result more than some of the ballots do.
		//
		// Derived, not persisted: holdings are fixed at founding (stock trading is deferred, D-ND8.21), so
		// "held NST and has no ballot in this record" is exactly the set that abstained — and it reads
		// correctly on votes closed before this shipped. No new field, no WorldFormatVersion bump. If stock
		// trading ever lands, this derivation is one of the things it breaks: the holdings would no longer
		// be the ones the vote saw, and the abstention list would need persisting at close.
		var abstainers = founding.Holdings
			.Where(h => h.Nst > 0m && !rec.Ballots.Any(b => b.HolderId == h.HolderId))
			.OrderByDescending(h => h.Nst)
			.ToList();
		if (abstainers.Count > 0 && rec.Ballots.Count > 0)
		{
			decimal totalNstAtVote = founding.Holdings.Where(h => h.Nst > 0m).Sum(h => h.Nst);
			foreach (CompanyShareHolding h in abstainers)
			{
				decimal forfeited = totalNstAtVote > 0m ? h.Nst / totalNstAtVote : 0m;
				string who = h.HolderId == PlayerNodeId ? "You" : _networkRoot.DescribeAddress(h.HolderId);
				var line = new Label
				{
					Text = string.Create(CultureInfo.InvariantCulture,
						$"   {who}  —  abstained (forfeited {forfeited:P2} of the vote)")
				};
				line.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
				_infoVBox.AddChild(line);
			}
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
