using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GodotBlockchainPort.Simulation;
using Scripts.Finance;
using UI.StatusBar;

// The Central Bank (FED) DEV readout — Step 15 P15.1e (D-15.16: a Main Menu entry, DEV-only for now; the
// casino's CasinoGamblingFinances scene deliberately does not link to it yet). One section per FED client
// (today only the casino; the four CB1 bank companies join at P15.2), each showing outstanding debt, total
// drawn/repaid and its loan/repayment history, plus system-wide totals and the monetary invariant.
//
// Pure display — it never draws, repays or mutates anything (the FED's write API is called by the casino and,
// later, by the bank companies). Layout follows ProjectDesignManual Ch. 29: bounded chain
// MarginContainer → VBoxContainer → ScrollContainer(size_flags_vertical = 3), Back button in a FIXED FOOTER
// outside the scroll (§29.10), and a 50 px bottom margin clearing the off-screen band (§29.11). Rows are
// plain Labels (Pattern A, §29.2) — they report honest minimum heights and default to mouse_filter = IGNORE,
// so the wheel reaches the ScrollContainer.
//
// Refresh is event-driven (CentralBankChanged / LedgerChanged) with a dirty-flag coalescer, because a casino
// recharge streak can fire many draws per second; a slow fallback tick covers anything that changes without
// an event (Ch. 38 poll-migration candidate — not blocking).
public partial class CentralBank : Control
{
	private CentralBankService      _fed;
	private ScMonetaryLedgerService _ledger;
	private SceneManager            _sceneManager;
	private BtcMarketDataService    _marketData;   // P15.5 — live valuation for the recovery tracker
	private CalendarTimeService     _calendarTime;

	private Label _totalsLabel;
	private Label _invariantLabel;
	private VBoxContainer _contentVBox;

	// Per-client history rows shown, newest first. The FED itself keeps the newest 500 records per client;
	// rendering all of them for every client would build thousands of Labels on a refresh, so the panel shows
	// a window and reports the remainder.
	private const int MaxHistoryRowsPerClient = 60;

	private bool _dirty = true;
	private double _coalesceTimer;
	private const double CoalesceInterval = 0.5;  // batch bursts of draw events into one rebuild
	private double _fallbackTimer;
	private const double FallbackInterval = 5.0; // safety net only — every figure on this page has an event

	private static readonly Color ColorDraw     = new Color(1f, 0.65f, 0.2f);  // orange — SC minted into debt
	private static readonly Color ColorRepay    = new Color(0.4f, 1f, 0.4f);   // green  — SC burned out of existence
	private static readonly Color ColorHeading  = new Color(0.7f, 0.85f, 1f);
	private static readonly Color ColorSubtle   = new Color(0.6f, 0.6f, 0.6f);

	public override void _Ready()
	{
		_fed          = GetNodeOrNull<CentralBankService>("/root/CentralBankService");
		_ledger       = GetNodeOrNull<ScMonetaryLedgerService>("/root/ScMonetaryLedgerService");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		_marketData   = GetNodeOrNull<BtcMarketDataService>("/root/BtcMarketDataService");
		_calendarTime = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());

		_totalsLabel    = GetNode<Label>("%TotalsLabel");
		_invariantLabel = GetNode<Label>("%InvariantLabel");
		_contentVBox    = GetNode<VBoxContainer>("%ContentVBox");

		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);

		if (_fed != null)    _fed.CentralBankChanged += MarkDirty;
		if (_ledger != null) _ledger.LedgerChanged   += MarkDirty;

		RefreshAll();
	}

	public override void _ExitTree()
	{
		if (_fed != null)    _fed.CentralBankChanged -= MarkDirty;
		if (_ledger != null) _ledger.LedgerChanged   -= MarkDirty;
	}

	private void MarkDirty() => _dirty = true;

	public override void _Process(double delta)
	{
		if (_dirty)
		{
			_coalesceTimer += delta;
			if (_coalesceTimer >= CoalesceInterval)
			{
				_coalesceTimer = 0;
				_dirty = false;
				_fallbackTimer = 0;
				RefreshAll();
				return;
			}
		}

		_fallbackTimer += delta;
		if (_fallbackTimer >= FallbackInterval)
		{
			_fallbackTimer = 0;
			RefreshAll();
		}
	}

	private void RefreshAll()
	{
		if (!GodotObject.IsInstanceValid(this) || _fed == null) return;

		_totalsLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Clients: {_fed.Accounts.Count}   |   Lent all-time: {_fed.TotalLentAllTime:N8} SC   |   Outstanding: {_fed.TotalOutstandingDebt:N8} SC   |   Repaid: {_fed.TotalRepaidAllTime:N8} SC");

		// The FED's outstanding total IS the debt half of the monetary invariant, so any drift between the two
		// layers is a real bug (they are kept in lockstep by DrawLoan/Repay — D-15.23 Fork A). Show both.
		if (_ledger != null)
		{
			decimal ledgerDebt = _ledger.TotalDebtOutstanding;
			bool inSync = ledgerDebt == _fed.TotalOutstandingDebt;
			_invariantLabel.Text = string.Create(CultureInfo.InvariantCulture,
				$"Circulation {_ledger.TotalCirculation:N8} = grants {_ledger.TotalGenesisGrants:N8} + debt {ledgerDebt:N8} SC   |   FED/ledger debt {(inSync ? "in sync ✓" : "OUT OF SYNC ✗")}");
			_invariantLabel.AddThemeColorOverride("font_color", inSync ? ColorSubtle : new Color(1f, 0.4f, 0.4f));
		}

		BuildClientSections();
	}

	private void BuildClientSections()
	{
		if (!GodotObject.IsInstanceValid(_contentVBox)) return;

		foreach (Node child in _contentVBox.GetChildren())
			child.QueueFree();

		// The casino first (the FED's founding client), then everything else alphabetically — a stable order
		// so a client appearing/disappearing never shuffles the rest of the page.
		List<string> clientIds = _fed.ClientIds
			.OrderBy(id => id == CentralBankService.ClientCasino ? 0 : 1)
			.ThenBy(id => id, StringComparer.Ordinal)
			.ToList();

		if (clientIds.Count == 0)
		{
			AddLabel("No client has borrowed from the FED yet.", 18, ColorSubtle);
			AddLabel("The casino draws its first loan on demand — when a client win empties its Bankroll and its Main Balance can't cover a dose.", 15, ColorSubtle, wrap: true);
		}

		foreach (string clientId in clientIds)
		{
			var account = _fed.Accounts[clientId];

			AddLabel(DisplayName(clientId), 22, ColorHeading);
			AddLabel(string.Create(CultureInfo.InvariantCulture,
				$"    Outstanding debt: {account.OutstandingDebt:N8} SC     Total drawn: {account.TotalDrawn:N8} SC ({account.DrawCount} draws)     Total repaid: {account.TotalRepaid:N8} SC ({account.RepayCount} repayments)"), 17);

			var history = account.History;
			int shown = Math.Min(history.Count, MaxHistoryRowsPerClient);
			int untracked = account.DrawCount + account.RepayCount - history.Count; // trimmed past the FED's cap
			string olderNote = untracked > 0 ? $"  (+{untracked} older, trimmed)" : "";

			if (history.Count == 0)
			{
				AddLabel("    (no movements)", 15, ColorSubtle);
			}
			else
			{
				AddLabel(string.Create(CultureInfo.InvariantCulture,
					$"    Movements — newest {shown} of {history.Count}{olderNote}"), 15, ColorSubtle);

				for (int i = history.Count - 1; i >= history.Count - shown; i--)
				{
					var r = history[i];
					bool isRepay = r.Kind == CentralBankService.KindRepay;
					string sign = isRepay ? "−" : "+";
					AddLabel(string.Create(CultureInfo.InvariantCulture,
						$"        {r.GameDateLocal:yyyy-MM-dd HH:mm:ss}  {(isRepay ? "REPAY" : "DRAW "),-5}  {sign}{r.Amount:N8} SC  ·  {r.Reason}"),
						15, isRepay ? ColorRepay : ColorDraw);
				}
			}

			// A bare Control defaults to mouse_filter = STOP, which would swallow the wheel over its band
			// (§29.3 trap #3) — Labels default to IGNORE, so only this spacer needs saying explicitly.
			AddSpacer();
		}

		BuildBankingLayerSection();
	}

	// Step 15 P15.2 — the layer-1 view beneath the FED's own accounts: which of the four CB1 banks have
	// founded, their LOCKED market category (D-15.12), their quarantined CollateralBtc and client count,
	// plus a read-only preview of which financier each founded company would route to. An early slice of
	// P15.7a, added so P15.2a–d are verifiable in-game before P15.3 wires the real reroute.
	private void BuildBankingLayerSection()
	{
		AddSpacer(18);
		AddLabel("Banking layer (Step 15 P15.2)", 22, ColorHeading);

		var banks = NetworkRoot.GetFoundedBankRows();
		if (banks.Count == 0)
		{
			AddLabel("    No bank company has founded yet — the first is First Satoshi Savings (2012-09-03).", 15, ColorSubtle);
			AddLabel("    Until then every company conversion falls back to the casino path (D-15.20 (c)).", 15, ColorSubtle);
		}
		else
		{
			foreach (var bank in banks)
			{
				string founded = DateTimeOffset.FromUnixTimeMilliseconds(bank.FoundedAtUnixMs)
					.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
				AddLabel(string.Create(CultureInfo.InvariantCulture,
					$"    {bank.DisplayName}  ·  category {bank.MarketCategory} (locked)  ·  founded {founded}"), 17);
				AddLabel(string.Create(CultureInfo.InvariantCulture,
					$"        FED debt: {NetworkRootFedDebt(bank.BankNodeId):N8} SC     CollateralBtc: {bank.CollateralBtc:N8} BTC     client companies: {bank.ClientCount}"), 15, ColorSubtle);

				// P15.7a — the LAYER-1 sub-ledger: which companies this bank financed, and on what terms.
				// The FED's own client accounts above are layer 0 (bank↔FED); this is bank↔company.
				var sheet = NetworkRoot.GetBankBalanceSheet(bank.BankNodeId);
				if (sheet != null && sheet.Clients.Count > 0)
				{
					foreach (var kv in sheet.Clients.OrderByDescending(c => c.Value.ScPaid))
					{
						var acct = kv.Value;
						AddLabel(string.Create(CultureInfo.InvariantCulture,
							$"          → {NetworkRoot.DescribeNodeForDev(kv.Key)}: bought {acct.BtcBought:N8} BTC for {acct.ScPaid:N2} SC over {acct.ProvisionCount} provision(s)"), 14, ColorSubtle);
					}
				}

				// P15.4d/e — the two states that say this bank is failing to service its debt.
				if (bank.PendingShortfallSc > 0m)
				{
					AddLabel(string.Create(CultureInfo.InvariantCulture,
						$"        ⚠ shortfall awaiting a board vote: {bank.PendingShortfallSc:N8} SC"), 15, ColorDraw);
				}
				if (bank.UnrecoverableShortfallSc > 0m)
				{
					AddLabel(string.Create(CultureInfo.InvariantCulture,
						$"        ✗ INSOLVENT — unrecoverable: {bank.UnrecoverableShortfallSc:N8} SC (dissolution lands at P15.5a)"), 15,
						new Color(1f, 0.4f, 0.4f));
				}
			}
		}

		BuildFbiSection();
		BuildClosedCompaniesSection();

		AddSpacer(12);
		AddLabel("Financier selection preview (§5.1 / D-15.20)", 20, ColorHeading);
		var preview = NetworkRoot.PreviewCompanyFinanciers();
		if (preview.Count == 0)
		{
			AddLabel("    No company has founded yet.", 15, ColorSubtle);
		}
		else
		{
			foreach (var row in preview)
			{
				AddLabel(string.Create(CultureInfo.InvariantCulture,
					$"    {row.CompanyDisplay} [{row.CompanyCategory}]  →  {row.FinancierDisplay}  ({row.Tier})"), 15,
					row.Tier == NetworkRoot.FinancierTierCasino ? ColorSubtle : ColorRepay);
			}
		}
	}

	// Step 15 P15.6 — the FBI board: activation state, its self-funding budget, and every company currently
	// carrying an investigation file, ordered the way the raid roll orders them (non-banks first by overage,
	// banks last — D-15.19). Shares GetFbiInvestigationWarning's source with the roll, so a displayed
	// number cannot drift from the mechanism (§39.16 rule 6).
	private void BuildFbiSection()
	{
		AddSpacer(18);
		AddLabel("Federal investigations (Step 15 P15.6)", 22, ColorHeading);

		if (!NetworkRoot.FbiActivated)
		{
			AddLabel(string.Create(CultureInfo.InvariantCulture,
				$"    Not active yet — the FBI thread starts {NetworkRoot.FbiActivationDateLocal:yyyy-MM-dd} (Gavin Andresen's In-Q-Tel presentation, D-15.14)."),
				15, ColorSubtle, wrap: true);
			return;
		}

		AddLabel(string.Create(CultureInfo.InvariantCulture,
			$"    Active since {NetworkRoot.FbiActivationDateLocal:yyyy-MM-dd}   ·   budget {NetworkRoot.FbiScFunds:N2} SC (initial federal grant + seizures)"), 15, ColorSubtle);

		var files = NetworkRoot.GetFbiInvestigationFiles();
		if (files.Count == 0)
		{
			AddLabel("    No company is currently over its category's SC tolerance.", 15, ColorSubtle);
			return;
		}

		// R3 — a row now says which of THREE states it is in, because "score 340/100, SC under tolerance"
		// read as a live threat when it was in fact a file already closing. Raid eligibility is
		// `score ≥ threshold AND overage > 0` (TickFbiInvestigations), so the colour must test both:
		//   red    ⚑ — raid-eligible RIGHT NOW
		//   orange ·  — over tolerance, file still growing toward the threshold
		//   grey   ·  — under tolerance, cooling; shows the blocks left before the row disappears
		foreach (var f in files)
		{
			bool overTolerance = f.Overage > 0m;
			bool raidEligible = f.Score >= NetworkRoot.InvestigationFlagThreshold && overTolerance;
			string tail = overTolerance
				? "  ·  OVER TOLERANCE"
				: string.Create(CultureInfo.InvariantCulture,
					$"  ·  under tolerance, cooling — clears in ~{NetworkRoot.FbiBlocksToClear(f.Score)} blocks (no raid meanwhile)");

			AddLabel(string.Create(CultureInfo.InvariantCulture,
				$"    {(raidEligible ? "⚑" : "·")} {f.DisplayName} [{f.MarketCategory}{(f.IsBank ? ", bank" : "")}]  score {f.Score:N1}/{NetworkRoot.InvestigationFlagThreshold:N0}  ·  SC {f.ScReserve:N2} vs tolerated {f.ToleranceSc:N2}{tail}"),
				15, raidEligible ? new Color(1f, 0.4f, 0.4f) : overTolerance ? ColorDraw : ColorSubtle);
		}
	}

	// Step 15 P15.5 — the Closed-Companies list and the post-dissolution RECOVERY TRACKER (D-15.8/15.15).
	// A dead company's wallet stays on-chain in federal custody and keeps receiving its scheduled inflows;
	// once a solvent bank of its market category inherits it, those inflows are forwarded there. The
	// tracker asks the only question that matters: has the redirected income out-earned the debt the FED
	// wrote off? Recovery is held in BTC and valued at the LIVE price (never frozen at a historical day —
	// the AuctioningCompanyDetails/BTCWallet "always live" precedent).
	private void BuildClosedCompaniesSection()
	{
		var closures = NetworkRoot.GetClosedCompanies();
		if (closures.Count == 0) return;

		AddSpacer(18);
		AddLabel("Closed companies & recovery (Step 15 P15.5)", 22, ColorHeading);

		decimal? price = _marketData?.GetEffectivePriceUsd(
			_calendarTime?.CurrentLocalDateTime ?? DateTime.Now);

		foreach (var c in closures)
		{
			string when = DateTimeOffset.FromUnixTimeMilliseconds(c.ClosedAtUnixMs)
				.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			AddLabel(string.Create(CultureInfo.InvariantCulture,
				$"    ✗ {NetworkRoot.DescribeNodeForDev(c.NonMinerNodeId)}  ·  {c.Reason}  ·  {when}  ·  category {c.MarketCategory}"), 17, ColorSubtle);

			string custody = string.IsNullOrEmpty(c.InheritingBankNodeId)
				? "FED custody (100% BTC, no matching solvent bank yet)"
				: $"held by {NetworkRoot.DescribeNodeForDev(c.InheritingBankNodeId)}";
			AddLabel(string.Create(CultureInfo.InvariantCulture,
				$"        FED loss: {c.DebtAtClosureSc:N8} SC     at closure: {c.BtcAtClosure:N8} BTC / {c.ScAtClosure:N8} SC     wallet: {custody}"), 15, ColorSubtle);

			// The tracker line: recovered-vs-owed, and the verdict. Only meaningful for a bank (a non-bank
			// FBI seizure carries no debt to recover against — D-15.19).
			if (!c.WasBank) continue;

			decimal recoveredSc = price is decimal p && p > 0m
				? Money.Normalize(c.RecoveredBtc * p)
				: 0m;
			decimal net = Money.Normalize(recoveredSc - c.DebtAtClosureSc);
			string verdict = c.DebtAtClosureSc <= 0m ? "no loss to recover"
				: net >= 0m ? "RECOVERED (+profit)"
				: "underwater";
			AddLabel(string.Create(CultureInfo.InvariantCulture,
				$"        recovered {c.RecoveredBtc:N8} BTC ≈ {recoveredSc:N2} SC at today's price  →  net {net:+#,##0.00;-#,##0.00;0.00} SC  ({verdict})"),
				15, net >= 0m ? ColorRepay : ColorDraw);
		}
	}

	// A founded bank's FED account is keyed "bank:<nodeId>" (CentralBankService.BankClientId) — it stays 0
	// until P15.3a draws the first provisioning loan.
	private decimal NetworkRootFedDebt(string bankNodeId) =>
		_fed?.OutstandingDebt(CentralBankService.BankClientId(bankNodeId)) ?? 0m;

	private void AddSpacer(int height = 12)
	{
		// A bare Control defaults to mouse_filter = STOP, which would swallow the wheel over its band
		// (§29.3 trap #3) — Labels default to IGNORE, so only this spacer needs saying explicitly.
		var spacer = new Control();
		spacer.CustomMinimumSize = new Vector2(0, height);
		spacer.MouseFilter = MouseFilterEnum.Ignore;
		_contentVBox.AddChild(spacer);
	}

	// "casino" → the house; "bank:first_satoshi_savings" → the bank company (P15.2 onward). The raw client id
	// is kept alongside the friendly name — this is a DEV scene, and the id is the join key of the FED account
	// dictionary, the monetary ledger's borrower key and (P15.7d) the bank_credit_trace rows (the ND.10g
	// two-tier naming rule: player-facing shows the name alone, DEV shows both).
	private static string DisplayName(string clientId)
	{
		if (clientId == CentralBankService.ClientCasino) return "The Casino  (casino)";
		const string bankPrefix = "bank:";
		if (clientId.StartsWith(bankPrefix, StringComparison.Ordinal))
		{
			string nodeId = clientId[bankPrefix.Length..];
			return $"{NetworkRoot.DescribeNodeForDev(nodeId)}  [bank]";
		}
		return clientId;
	}

	private void AddLabel(string text, int fontSize, Color? color = null, bool wrap = false)
	{
		var label = new Label();
		label.Text = text;
		label.AddThemeFontSizeOverride("font_size", fontSize);
		if (color.HasValue) label.AddThemeColorOverride("font_color", color.Value);
		// §29.6: a long non-wrapping Label reports its full single-line width as its minimum and drags the whole
		// column sideways past the (horizontally non-scrollable) viewport. Only prose labels need this — the
		// fixed-width numeric rows above are short enough to stay inside.
		if (wrap) label.AutowrapMode = TextServer.AutowrapMode.Word;
		_contentVBox.AddChild(label);
	}
}
