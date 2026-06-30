using Godot;
using System;
using System.Globalization;
using System.Linq;
using Scripts.Finance;
using UI.StatusBar;

public partial class ClientsTransactions : Control
{
	private CasinoClientLedgerService _ledger;
	private SceneManager _sceneManager;

	private Label _globalDepositedLabel;
	private Label _globalWithdrawnLabel;
	private OptionButton _clientSelector;
	private Label _enrolledLabel;
	private Label _totalDepositedLabel;
	private Label _totalWithdrawnLabel;
	private Label _netCommitmentLabel;
	private VBoxContainer _txListVBox;

	private double _refreshTimer;
	private const double RefreshInterval = 2.0;

	private static readonly (string Id, string Display)[] KnownClients =
	{
		("player", "Player"),
	};

	public override void _Ready()
	{
		_ledger       = GetNodeOrNull<CasinoClientLedgerService>("/root/CasinoClientLedgerService");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());

		_globalDepositedLabel = GetNode<Label>("%GlobalDepositedLabel");
		_globalWithdrawnLabel = GetNode<Label>("%GlobalWithdrawnLabel");
		_clientSelector       = GetNode<OptionButton>("%ClientSelector");
		_enrolledLabel        = GetNode<Label>("%EnrolledLabel");
		_totalDepositedLabel  = GetNode<Label>("%TotalDepositedLabel");
		_totalWithdrawnLabel  = GetNode<Label>("%TotalWithdrawnLabel");
		_netCommitmentLabel   = GetNode<Label>("%NetCommitmentLabel");
		_txListVBox           = GetNode<VBoxContainer>("%TxListVBox");

		foreach ((string _, string display) in KnownClients)
			_clientSelector.AddItem(display);

		_clientSelector.ItemSelected += _ => RebuildClientPanel();

		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.CasinoGamblingFinances);

		if (_ledger != null)
			_ledger.LedgerChanged += RefreshAll;

		RefreshAll();
	}

	public override void _ExitTree()
	{
		if (_ledger != null)
			_ledger.LedgerChanged -= RefreshAll;
	}

	public override void _Process(double delta)
	{
		_refreshTimer += delta;
		if (_refreshTimer >= RefreshInterval)
		{
			_refreshTimer = 0;
			RefreshAll();
		}
	}

	private void RefreshAll()
	{
		RefreshGlobalTotals();
		RebuildClientPanel();
	}

	private void RefreshGlobalTotals()
	{
		if (_ledger == null) return;

		decimal deposited = _ledger.Entries
			.Where(e => e.Kind == "initial" || e.Kind == "deposit" || e.Kind == "auto_recharge")
			.Sum(e => e.Amount);
		decimal withdrawn = _ledger.Entries
			.Where(e => e.Kind == "withdrawal")
			.Sum(e => e.Amount);

		_globalDepositedLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Total SC deposited by all clients:   {deposited:N8} SC");
		_globalWithdrawnLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Total SC withdrawn by all clients:   {withdrawn:N8} SC");
	}

	private void RebuildClientPanel()
	{
		int idx = Math.Max(0, _clientSelector.Selected);
		if (idx >= KnownClients.Length) idx = 0;
		string clientId = KnownClients[idx].Id;

		var entries = _ledger?.GetEntriesForClient(clientId);

		if (entries == null || entries.Count == 0)
		{
			_enrolledLabel.Text       = "Enrolled:         —";
			_totalDepositedLabel.Text = "Total deposited:  0.00000000 SC";
			_totalWithdrawnLabel.Text = "Total withdrawn:  0.00000000 SC";
			_netCommitmentLabel.Text  = "Net commitment:   0.00000000 SC";
			ClearTxList();
			return;
		}

		DateTime enrolledLocal = entries[0].UtcTimestamp.ToLocalTime();

		decimal deposited = entries
			.Where(e => e.Kind == "initial" || e.Kind == "deposit" || e.Kind == "auto_recharge")
			.Sum(e => e.Amount);
		decimal withdrawn = entries
			.Where(e => e.Kind == "withdrawal")
			.Sum(e => e.Amount);
		decimal net = Money.Normalize(deposited - withdrawn);

		_enrolledLabel.Text = $"Enrolled:         {enrolledLocal:dd MMM yyyy HH:mm:ss}";
		_totalDepositedLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Total deposited:  {deposited:N8} SC");
		_totalWithdrawnLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Total withdrawn:  {withdrawn:N8} SC");
		_netCommitmentLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Net commitment:   {net:N8} SC");

		BuildTxList(entries);
	}

	private void ClearTxList()
	{
		foreach (Node child in _txListVBox.GetChildren())
			child.QueueFree();
	}

	private void BuildTxList(System.Collections.Generic.IReadOnlyList<CasinoClientLedgerService.LedgerEntry> entries)
	{
		ClearTxList();

		for (int i = entries.Count - 1; i >= 0; i--)
		{
			CasinoClientLedgerService.LedgerEntry e = entries[i];
			string ts = e.UtcTimestamp.ToLocalTime().ToString("dd MMM yyyy HH:mm:ss");

			string kindLabel;
			Color color;
			string wagersAnnotation = string.Empty;

			switch (e.Kind)
			{
				case "initial":
					kindLabel = "[INITIAL DEPOSIT]";
					color = new Color(0.4f, 0.9f, 1f);   // cyan
					wagersAnnotation = string.Create(CultureInfo.InvariantCulture,
						$"  │ wager base: {e.TotalWageredSnapshot:N8} SC");
					break;
				case "deposit":
					kindLabel = "[DEPOSIT        ]";
					color = new Color(0.4f, 1f, 0.4f);   // green
					wagersAnnotation = string.Create(CultureInfo.InvariantCulture,
						$"  │ wager base: {e.TotalWageredSnapshot:N8} SC");
					break;
				case "auto_recharge":
					kindLabel = "[AUTO-RECHARGE  ]";
					color = new Color(0.7f, 0.7f, 0.7f); // gray
					break;
				case "withdrawal":
					kindLabel = "[WITHDRAWAL     ]";
					color = new Color(1f, 0.65f, 0.2f);  // orange
					break;
				default:
					kindLabel = $"[{e.Kind.ToUpperInvariant()}]";
					color = new Color(1f, 1f, 1f);
					break;
			}

			string line = string.Create(CultureInfo.InvariantCulture,
				$"{ts}  {kindLabel}  {e.Amount:N8} SC{wagersAnnotation}");

			var label = new Label();
			label.Text = line;
			label.AddThemeFontSizeOverride("font_size", 16);
			label.AddThemeColorOverride("font_color", color);
			_txListVBox.AddChild(label);
		}
	}
}
