using Godot;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
using Scripts.Finance;
using UI.StatusBar;

// Step 14 ND.8c (D-ND8.25 / D-ND8.35) — the World Economy DEV hub. Hosts the SC Monetary Ledger readout
// (circulation / grants / debt totals, per-party breakdowns, the mint/burn event log). ND.8b later adds
// its company inflow/expansion knobs to this same scene. DEV-only — never linked from player-facing UI.
//
// Layout follows ProjectDesignManual Ch. 29: bounded chain (RootMargin → RootVBox → the expanding log
// label), the event log is a Pattern B RichTextLabel (scroll_active, fit_content = false — it scrolls
// its own wheel), the Back button is a fixed footer OUTSIDE the scroll (§29.10), and RootMargin keeps
// margin_bottom = 50 for the bottom safe area (§29.11).
public partial class WorldEconomy : Control
{
	private SceneManager _sceneManager;
	private ScMonetaryLedgerService _ledger;

	private Label _circulationLabel;
	private Label _grantsLabel;
	private Label _debtLabel;
	private RichTextLabel _ledgerLabel;

	// ND.8b.6 (D-ND8.25) — the company inflow/expansion DEV knobs (per-company inflow multiplier over
	// the D-ND8.36 weighted draw). The multiplier lives in NetworkRoot's static state and rides the
	// block-commit snapshot like the rest of the governance state.
	private OptionButton _companySelector;
	private SpinBox _inflowMultiplierSpin;
	private Label _companyInflowInfoLabel;

	public override void _Ready()
	{
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		_ledger       = GetNodeOrNull<ScMonetaryLedgerService>("/root/ScMonetaryLedgerService");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());

		_circulationLabel = GetNode<Label>("%CirculationLabel");
		_grantsLabel      = GetNode<Label>("%GrantsLabel");
		_debtLabel        = GetNode<Label>("%DebtLabel");
		_ledgerLabel      = GetNode<RichTextLabel>("%LedgerLabel");

		_companySelector        = GetNode<OptionButton>("%CompanySelector");
		_inflowMultiplierSpin   = GetNode<SpinBox>("%InflowMultiplierSpin");
		_companyInflowInfoLabel = GetNode<Label>("%CompanyInflowInfoLabel");

		CompanyRoster.EnsureLoaded();
		foreach (CompanyRecord record in CompanyRoster.Auctionable)
			_companySelector.AddItem(record.DisplayName);
		_companySelector.ItemSelected += _ => RefreshCompanyKnobs();
		GetNode<Button>("%ApplyMultiplierBtn").Pressed += OnApplyInflowMultiplier;
		RefreshCompanyKnobs();

		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);

		if (_ledger != null)
			_ledger.LedgerChanged += Refresh;
		Refresh();
	}

	private CompanyRecord? SelectedCompany()
	{
		int index = _companySelector.Selected;
		return index >= 0 && index < CompanyRoster.Auctionable.Count ? CompanyRoster.Auctionable[index] : null;
	}

	private void OnApplyInflowMultiplier()
	{
		if (SelectedCompany() is not CompanyRecord record) return;
		NetworkRoot.SetCompanyInflowMultiplier(record.CompanyId, (decimal)_inflowMultiplierSpin.Value);
		RefreshCompanyKnobs();
	}

	private void RefreshCompanyKnobs()
	{
		if (SelectedCompany() is not CompanyRecord record)
		{
			_companyInflowInfoLabel.Text = "No auctionable companies loaded (roster missing?).";
			return;
		}

		decimal multiplier = NetworkRoot.GetCompanyInflowMultiplier(record.CompanyId);
		_inflowMultiplierSpin.Value = (double)multiplier;

		long nowMs = NetworkRoot.GetPlayerLatestBlockTimestampMsStatic();
		decimal effective = NetworkRoot.EffectiveInflowWeight(record.CompanyId, nowMs);
		string expansion = record.ExpansionDateLocal is DateTime e && record.ExpansionMultiplier is decimal m
			? string.Create(CultureInfo.InvariantCulture,
				$"expansion ×{m:0.##} from {e:yyyy-MM-dd} ({(nowMs >= new DateTimeOffset(e).ToUnixTimeMilliseconds() ? "ACTIVE" : "pending")})")
			: "no scheduled expansion";
		_companyInflowInfoLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"{record.DisplayName} ({record.CompanyId}) — {record.CurrencyBand}, {record.MarketCategory}, appears {record.AppearanceDateLocal:yyyy-MM-dd}  |  " +
			$"inflow weight {record.InflowWeight}, {expansion}, dev multiplier ×{multiplier:0.##}  ⇒  effective weight {effective:0.##}");
	}

	public override void _ExitTree()
	{
		if (_ledger != null)
			_ledger.LedgerChanged -= Refresh;
	}

	private static string Sc(decimal v) => v.ToString("N8", CultureInfo.InvariantCulture);

	// Step 15 P15.7b — the BANKING-LAYER AGGREGATE. This scene is the macro monetary view, so it carries the
	// system-wide question ("is the banking layer solvent?") and a per-bank strip; the PER-CLIENT detail,
	// the movement histories, the Closed-Companies list and the FBI board live in the Central Bank [DEV]
	// scene, which is the FED's own page — duplicating them here would mean two places to keep in step
	// (§39.15 rule 6's spirit: one source per signal). The closure line below is a pointer, not a copy.
	//
	// Leverage is the honest headline: banks borrow SC and sit on BTC, so their solvency is a live price
	// question, valued at TODAY's price — never frozen at a historical day.
	private void AppendBankingLayer(StringBuilder sb)
	{
		var banks = NetworkRoot.GetFoundedBankRows();
		sb.Append("\n[color=aqua]Banking layer[/color] (Step 15 — the four CB1 banks as FED clients):\n");
		if (banks.Count == 0)
		{
			sb.Append("  (no bank company has founded yet — company conversions still route to the casino)\n");
			return;
		}

		var fed = GetNodeOrNull<CentralBankService>("/root/CentralBankService");
		var market = GetNodeOrNull<BtcMarketDataService>("/root/BtcMarketDataService");
		var calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		decimal? price = market?.GetEffectivePriceUsd(calendar?.CurrentLocalDateTime ?? DateTime.Now);

		decimal totalDebt = 0m, totalCollateral = 0m;
		foreach (var b in banks)
		{
			decimal debt = fed?.OutstandingDebt(CentralBankService.BankClientId(b.BankNodeId)) ?? 0m;
			totalDebt += debt;
			totalCollateral += b.CollateralBtc;

			decimal collateralSc = price is decimal p && p > 0m ? Money.Normalize(b.CollateralBtc * p) : 0m;
			bool under = debt > 0m && collateralSc < debt;
			string flag = under ? "  [color=red]UNDER-COLLATERALIZED[/color]" : string.Empty;
			if (b.UnrecoverableShortfallSc > 0m) flag = "  [color=red]INSOLVENT[/color]";
			else if (b.PendingShortfallSc > 0m) flag = "  [color=orange]SHORTFALL PENDING[/color]";

			sb.Append($"  [color=lime]{b.DisplayName}[/color] [{b.MarketCategory}]  —  FED debt {Sc(debt)} SC  ·  collateral {b.CollateralBtc:N8} BTC (≈ {Sc(collateralSc)} SC)  ·  {b.ClientCount} client(s){flag}\n");
		}

		decimal totalCollateralSc = price is decimal pr && pr > 0m ? Money.Normalize(totalCollateral * pr) : 0m;
		decimal net = Money.Normalize(totalCollateralSc - totalDebt);
		string verdict = totalDebt <= 0m ? "no debt drawn yet"
			: net >= 0m ? "[color=lime]solvent[/color]"
			: "[color=red]under-collateralized[/color]";
		sb.Append($"  [color=aqua]System:[/color] collateral {Sc(totalCollateralSc)} SC vs FED debt {Sc(totalDebt)} SC  →  net {Sc(net)} SC  ({verdict})\n");

		// P15.5/P15.6 pointer — the detail lives in the Central Bank scene, this is just the headline.
		var closures = NetworkRoot.GetClosedCompanies();
		if (closures.Count > 0)
		{
			decimal writtenOff = closures.Sum(c => c.DebtAtClosureSc);
			int seized = closures.Count(c => c.Reason == NetworkRoot.ClosureReasonFbiSeizure);
			sb.Append($"  [color=orange]Closed companies:[/color] {closures.Count} ({seized} FBI-seized)  ·  FED written off {Sc(writtenOff)} SC  —  detail + recovery tracker in the Central Bank [DEV] scene\n");
		}
	}

	private void Refresh()
	{
		if (_ledger == null)
		{
			_circulationLabel.Text = "Total SC in circulation:   (ScMonetaryLedgerService not available)";
			return;
		}

		_circulationLabel.Text = $"Total SC in circulation:   {Sc(_ledger.TotalCirculation)} SC";
		_grantsLabel.Text      = $"Genesis grants (equity):   {Sc(_ledger.TotalGenesisGrants)} SC";
		_debtLabel.Text        = $"Debt outstanding:          {Sc(_ledger.TotalDebtOutstanding)} SC";

		var sb = new StringBuilder();

		sb.Append("[color=aqua]Invariant:[/color] circulation = genesis grants + outstanding debt\n\n");

		sb.Append("[color=aqua]Genesis grants by party[/color] (equity — granted once, never debt):\n");
		if (_ledger.GrantsByParty.Count == 0)
			sb.Append("  (none registered yet)\n");
		foreach (var kv in _ledger.GrantsByParty)
			sb.Append($"  [color=lime]{kv.Key}[/color]  —  {Sc(kv.Value)} SC\n");

		sb.Append("\n[color=aqua]Debt outstanding by borrower[/color] (each SC here was minted by a bank loan):\n");
		if (_ledger.DebtByBorrower.Count == 0)
			sb.Append("  (no debt — no loan has been drawn)\n");
		foreach (var kv in _ledger.DebtByBorrower)
			sb.Append($"  [color=orange]{kv.Key}[/color]  —  {Sc(kv.Value)} SC\n");

		AppendBankingLayer(sb);

		sb.Append($"\n[color=aqua]Mint/burn events[/color] (newest first, last {_ledger.Events.Count} kept):\n");
		if (_ledger.Events.Count == 0)
			sb.Append("  (no events)\n");
		for (int i = _ledger.Events.Count - 1; i >= 0; i--)
		{
			var e = _ledger.Events[i];
			string date  = e.GameDateLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
			string color = e.Kind switch
			{
				ScMonetaryLedgerService.KindGrant => "lime",
				ScMonetaryLedgerService.KindBurn  => "red",
				_                                 => "orange"
			};
			sb.Append($"  {date}  ·  [color={color}]{e.Kind}[/color]  ·  {e.PartyId}  ·  {Sc(e.Amount)} SC  ·  {e.Reason}\n");
		}

		// §29.3 traps #4/#5: trailing blank lines so the last row clears the bottom edge, and preserve the
		// internal scroll position across a refresh (setting Text snaps it back to the top).
		double scrollPos = _ledgerLabel.GetVScrollBar().Value;
		_ledgerLabel.Text = sb.Append("\n\n\n").ToString();
		_ledgerLabel.GetVScrollBar().Value = scrollPos;
	}
}
