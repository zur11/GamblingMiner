using Godot;
using System;
using System.Globalization;
using System.Text;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
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
