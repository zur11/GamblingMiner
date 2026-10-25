using Godot;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
using UI.CompanyRows;
using UI.StatusBar;
#nullable enable

// Mini-plan 29 unit 2 — "My Companies", the Player Holdings hub: one row per company the player holds stock in, so a
// holding no longer has to be hunted for among the 40 in BlockExplorer's Enroll Mode. An INDEX, not a second
// CompanyDetails: every signal on a row is read from the source the explorer and CompanyDetails already use (the row
// itself from CompanyRowSignals, the FBI line from GetFbiInvestigationWarning, the bank line from
// GetBankLendingSummary, the claim from HasPlayerClaimableDividends), and every action happens one click away, in
// CompanyDetails. Display-only: nothing here writes state (D-29.7).
//
// Refreshed by EVENTS (Pattern 6): everything on the page moves with a block (votes open and close, dividends drip,
// FBI files grow), so BlockAccepted queues ONE deferred rebuild, never inside the block commit and never a poll. A
// vote cast or a claim made in CompanyDetails is seen when the player comes back, because _Ready rebuilds.
public partial class PlayerHoldings : Control
{
	private NetworkRoot _networkRoot = null!;
	private SceneManager? _sceneManager;
	private Label _summaryLabel = null!;
	private ScrollContainer _holdingsScroll = null!;
	private VBoxContainer _holdingsVBox = null!;
	private bool _rebuildQueued;

	// Where Back goes. Static, because the hub is left and re-entered across a CompanyDetails round trip, and
	// SceneManager's memory is one deep: coming back from a company would otherwise make Back return to that company.
	// Only an entry from a THIRD scene (MainMenu, or wherever the StatusBar badge was pressed) moves it.
	private static SceneManager.SceneId _returnTo = SceneManager.SceneId.MainMenu;

	// H1's DEBUG line is printed when the rows change, not at every rebuild (a block each few seconds at 9000X).
	private static string _lastLoggedRows = string.Empty;

	public override void _Ready()
	{
		_networkRoot = GetNode<NetworkRoot>("NetworkRoot");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());
		_summaryLabel = GetNode<Label>("%SummaryLabel");
		_holdingsScroll = GetNode<ScrollContainer>("%HoldingsScroll");
		_holdingsVBox = GetNode<VBoxContainer>("%HoldingsVBox");

		SceneManager.SceneId? previous = _sceneManager?.PreviousScene;
		if (previous is null)
		{
			_returnTo = SceneManager.SceneId.MainMenu;
		}
		else if (previous != SceneManager.SceneId.CompanyDetails && previous != SceneManager.SceneId.PlayerHoldings)
		{
			_returnTo = previous.Value;
		}

		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(_returnTo);

		NetworkRoot.BlockAccepted += OnBlockAccepted;
		Rebuild();
	}

	public override void _ExitTree()
	{
		NetworkRoot.BlockAccepted -= OnBlockAccepted; // static event — must not outlive this scene
	}

	// BlockAccepted fires from inside HandleMinedBlock: only queue here, the rebuild runs at the end of the frame. The
	// flag coalesces several blocks in one frame into one rebuild.
	private void OnBlockAccepted(Block _)
	{
		if (_rebuildQueued) return;
		_rebuildQueued = true;
		Callable.From(Rebuild).CallDeferred();
	}

	private void Rebuild()
	{
		_rebuildQueued = false;
		if (!IsInsideTree()) return;

		// Ch. 29 trap 5's twin for Pattern A: rebuilding the rows must not throw the reader back to the top.
		int scroll = _holdingsScroll.ScrollVertical;
		foreach (Node child in _holdingsVBox.GetChildren())
		{
			_holdingsVBox.RemoveChild(child);
			child.QueueFree();
		}

		var awaitingVote = new HashSet<string>(NetworkRoot.GetCompaniesAwaitingPlayerVote().Select(c => c.nonMinerNodeId));
		List<CompanyFounding> held = NetworkRoot.GetCompanyFoundings()
			.Where(f => CompanyRowSignals.PlayerHolding(f) is { } h && (h.Nst > 0m || h.Pst > 0m))
			.OrderBy(f => f.FoundedAtUnixMs)
			.ToList();
		// A dissolved company the player held stock in keeps its terminal row (what was lost, and why), as in the
		// explorer. Newest closure first, the order GetClosedCompanies already gives.
		List<CompanyClosure> dissolved = NetworkRoot.GetClosedCompanies()
			.Where(c => c.PlayerNstAtClosure > 0m || c.PlayerPstAtClosure > 0m)
			.ToList();

		var logRows = new List<string>();
		foreach (CompanyFounding founding in held)
		{
			_holdingsVBox.AddChild(BuildHoldingRow(founding, awaitingVote.Contains(founding.NonMinerNodeId), logRows));
		}

		foreach (CompanyClosure closure in dissolved)
		{
			_holdingsVBox.AddChild(CompanyRowSignals.BuildClosedCompanyRow(
				NetworkRoot.DescribeCompany(closure.CompanyId, closure.NonMinerNodeId), closure));
			logRows.Add(string.Create(CultureInfo.InvariantCulture,
				$"{closure.CompanyId} DISSOLVED nstAtClosure={closure.PlayerNstAtClosure:F8} pstAtClosure={closure.PlayerPstAtClosure:F8}"));
		}

		CompanyRowSignals.HoldingsTally tally = CompanyRowSignals.TallyPlayerHoldings();
		_summaryLabel.Text = held.Count == 0 && dissolved.Count == 0
			? "You hold no company stock yet: bid in an auction from the Block Explorer's Enroll Mode."
			: $"{tally.Held} {(tally.Held == 1 ? "company" : "companies")} held   ·   {tally.Waiting} waiting for you " +
			  $"(votes {tally.VotesWaiting} · dividends to claim {tally.ClaimsWaiting})" +
			  (dissolved.Count > 0 ? $"   ·   {dissolved.Count} dissolved" : string.Empty);

		LogRowsIfChanged(logRows, tally);
		Callable.From(() => _holdingsScroll.ScrollVertical = scroll).CallDeferred();
	}

	// One holding: the explorer's own row (bordered title + the work-state button, from the same helper calls), then
	// the lines only an index needs: what the player holds, what is claimable, and the two risks worth a glance.
	private Control BuildHoldingRow(CompanyFounding founding, bool votePending, List<string> logRows)
	{
		CompanyShareHolding holding = CompanyRowSignals.PlayerHolding(founding)!;
		bool canClaim = _networkRoot.HasPlayerClaimableDividends(founding.NonMinerNodeId);
		string stake = holding.Nst > 0m
			? string.Create(CultureInfo.InvariantCulture, $"you hold {holding.Nst:N2} NST (votes and dividends)")
			: string.Create(CultureInfo.InvariantCulture, $"you hold {holding.Pst:N2} PST (dividends)");

		var rowVBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
		var titleRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
		var titleLabel = new Label
		{
			Text = CompanyRowSignals.FoundedRowTitle(
				$"{NetworkRoot.DescribeCompany(founding.CompanyId, founding.NonMinerNodeId)}  | {stake}", votePending),
			AutowrapMode = TextServer.AutowrapMode.Word,
			MouseFilter = MouseFilterEnum.Pass
		};
		var actionBtn = new Button();
		CompanyRowSignals.ApplyWorkState(titleLabel, actionBtn, votePending, canClaim);
		Color border = CompanyRowSignals.HoldingBorderColor(founding);
		titleRow.AddChild(CompanyRowSignals.BuildTitlePanel(titleLabel, border));
		string address = founding.NonMinerAddress;
		actionBtn.Pressed += () => OnOpenCompanyDetails(address);
		titleRow.AddChild(actionBtn);
		rowVBox.AddChild(titleRow);

		CompanyGovernanceState? gov = _networkRoot.GetCompanyGovernanceByNodeId(founding.NonMinerNodeId);
		if (gov != null && gov.ClaimableByHolder.TryGetValue("player", out CompanyClaimable? claim) && (claim.Btc > 0m || claim.Sc > 0m))
		{
			// CompanyDetails' own line and its own note (D-ND10h.3): a balance below the day's fee is not claimable.
			string note = canClaim ? string.Empty : "   — below the network fee; still accruing";
			rowVBox.AddChild(DetailLabel(string.Create(CultureInfo.InvariantCulture,
				$"Claimable now: {claim.Btc:F8} BTC + {claim.Sc:F8} SC{note}")));
		}

		if (NetworkRoot.GetFbiInvestigationWarning(founding.NonMinerNodeId) is string fbiWarning && gov != null)
		{
			Label fbi = DetailLabel(fbiWarning);
			fbi.AddThemeColorOverride("font_color", CompanyRowSignals.FbiWarningColor(gov.InvestigationScore));
			rowVBox.AddChild(fbi);
		}

		if (NetworkRoot.GetBankLendingSummary(founding.NonMinerNodeId) is NetworkRoot.BankLendingSummary bank)
		{
			rowVBox.AddChild(DetailLabel(string.Create(CultureInfo.InvariantCulture,
				$"Bank lending book: Central Bank debt {bank.FedDebtSc:N2} SC   ·   next installment {bank.NextInstallmentSc:N2} SC   ·   collateral {bank.CollateralBtc:N8} BTC   ·   {bank.ClientCount} client company(ies)")));
		}

		rowVBox.AddChild(new HSeparator { MouseFilter = MouseFilterEnum.Pass });

		logRows.Add(string.Create(CultureInfo.InvariantCulture,
			$"{founding.CompanyId} nst={holding.Nst:F8} pst={holding.Pst:F8} border={BorderName(border)} vote={votePending} claim={canClaim} button='{actionBtn.Text}'"));
		return rowVBox;
	}

	private static Label DetailLabel(string text)
	{
		var label = new Label
		{
			Text = "      " + text,
			AutowrapMode = TextServer.AutowrapMode.Word,
			MouseFilter = MouseFilterEnum.Pass
		};
		label.AddThemeFontSizeOverride("font_size", 18);
		return label;
	}

	private static string BorderName(Color border) =>
		border == CompanyRowSignals.HoldingGold ? "gold"
		: border == CompanyRowSignals.HoldingSilver ? "silver"
		: "black";

	private void OnOpenCompanyDetails(string nonMinerAddress)
	{
		CompanyDetails.PendingNonMinerAddress = nonMinerAddress;
		_sceneManager?.Go(SceneManager.SceneId.CompanyDetails);
	}

	// H1 (mini-plan 29 §3.4) — one line per row, read afterwards against state.json's CompanyFoundings. Printed with
	// GD.Print so it lands in godot.log and the Output panel (CLAUDE.md, "name the panel").
	[Conditional("DEBUG")]
	private static void LogRowsIfChanged(List<string> rows, CompanyRowSignals.HoldingsTally tally)
	{
		string joined = string.Join("\n", rows);
		if (joined == _lastLoggedRows) return;
		_lastLoggedRows = joined;

		GD.Print($"[Holdings] hub: {rows.Count} row(s) · held {tally.Held} · waiting {tally.Waiting} (votes {tally.VotesWaiting} · claims {tally.ClaimsWaiting})");
		foreach (string row in rows)
		{
			GD.Print($"[Holdings]   {row}");
		}
	}
}
