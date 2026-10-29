using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using Scripts.Numerology;
using UI.Readouts;

// Graphic Numerology — the sigil overlay over DiceGame (AIHelperFiles/graphic_numerology-plan.md P-GN.6, D-GN.19 to
// D-GN.21). Built like MartingaleCalculator: instanced hidden in DiceGame.tscn, opened with Open(), closed on its
// CloseRequested signal. The progress lives in PlayerSigilProgress, so closing it loses nothing.
//
// Two refresh paths, each paying only for what moves:
//   • the drawing and the sets' figures change only on a hit, a restart or a completed pass —
//     PlayerSigilProgress.Changed marks them dirty;
//   • the counters (bets, SC, game time) and the V chain move on EVERY bet, so they are SAMPLED at a fixed cadence
//     while the overlay is open, and the counters are floored to what that cadence can carry (§29.13,
//     CounterQuantizer). Hidden, it does no work at all: processing is off.
public partial class SigilOverlay : Control
{
	[Signal]
	public delegate void CloseRequestedEventHandler();

	private const double RepaintIntervalSeconds = 0.25;
	private const int ChainLines = 8;
	private const string LossColor = "#e05a5a";
	private const string WinColor = "#4cc46a";
	private const string MutedColor = "#9a9a9a";

	private Label _title;
	private SigilCanvas _canvas;
	private RichTextLabel _statusLabel;
	private RichTextLabel _setsLabel;
	private RichTextLabel _chainLabel;
	private RichTextLabel _completedLabel;

	private readonly CounterQuantizer _betsQuantizer = new();
	private readonly CounterQuantizer _satoshiQuantizer = new();
	private readonly CounterQuantizer _secondsQuantizer = new();
	private readonly SigilStep[] _chain = new SigilStep[ChainLines];

	private double _sinceRepaint;
	private long _paintedBets = -1;
	private bool _drawingDirty = true;

	public override void _Ready()
	{
		_title = GetNode<Label>("%SigilTitle");
		_canvas = GetNode<SigilCanvas>("%SigilCanvas");
		_statusLabel = GetNode<RichTextLabel>("%SigilStatusLabel");
		_setsLabel = GetNode<RichTextLabel>("%SigilSetsLabel");
		_chainLabel = GetNode<RichTextLabel>("%SigilChainLabel");
		_completedLabel = GetNode<RichTextLabel>("%SigilCompletedLabel");
		GetNode<Button>("%CloseSigilsButton").Pressed += OnClosePressed;

		// Opaque, like the calculator's panel (mini-plan 21 E): DiceGame's bet list must not show through.
		GetNode<PanelContainer>("Panel").AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.16f, 0.16f, 0.17f, 1f),
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6
		});

		PlayerSigilProgress.Changed += OnSigilChanged;
		Visible = false;
		SetProcess(false);
	}

	public override void _ExitTree()
	{
		// A static event outlives this scene; an unremoved handler would keep a freed overlay alive.
		PlayerSigilProgress.Changed -= OnSigilChanged;
	}

	public void Open()
	{
		// Above every control DiceGame builds in code after this node, for drawing and for input (as the calculator).
		MoveToFront();
		Visible = true;
		_drawingDirty = true;
		_paintedBets = -1;
		Repaint();
		SetProcess(true);
	}

	public void Close()
	{
		Visible = false;
		SetProcess(false);
	}

	private void OnClosePressed()
	{
		EmitSignal(SignalName.CloseRequested);
		Close();
	}

	private void OnSigilChanged(SigilBetOutcome _)
	{
		_drawingDirty = true;   // picked up by the next sampled repaint; nothing happens while hidden
	}

	public override void _Process(double delta)
	{
		_sinceRepaint += delta;
		if (_sinceRepaint < RepaintIntervalSeconds)
			return;

		_sinceRepaint = 0;
		if (_drawingDirty || PlayerSigilProgress.State.BetsPlaced != _paintedBets)
			Repaint();
	}

	private void Repaint()
	{
		var state = PlayerSigilProgress.State;
		_paintedBets = state.BetsPlaced;

		if (_drawingDirty)
		{
			_drawingDirty = false;
			_canvas.State = state;
			_canvas.QueueRedraw();
		}

		if (state.AllComplete)
		{
			_title.Text = "All fourteen sigils are drawn";
			_statusLabel.Text = "Every planet is complete.";
			_setsLabel.Text = string.Empty;
			_chainLabel.Text = string.Empty;
			SetTextKeepingScroll(_completedLabel, BuildCompleted(state));
			return;
		}

		var square = PlanetarySquares.PlanetOfSigil(state.SigilIndex);
		var kind = PlanetarySquares.KindOfSigil(state.SigilIndex);
		string sky = square.IsEclipsed(kind) ? "eclipse" : kind == SigilKind.Diurnal ? "day" : "night";
		_title.Text = string.Create(CultureInfo.InvariantCulture,
			$"{square.SigilName(kind)}  —  {square.Planet}, {sky}  ·  sigil {state.SigilIndex + 1} of {PlanetarySquares.SigilCount}");

		long betsQuantum = _betsQuantizer.NoteSample(state.BetsPlaced);
		long satoshiQuantum = _satoshiQuantizer.NoteSample(ToSatoshis(state.ScWagered));
		double sigilSeconds = state.Sets.Sum(s => s.GameSeconds);
		long secondsQuantum = _secondsQuantizer.NoteSample((long)sigilSeconds);

		_statusLabel.Text = BuildStatus(state, square, kind, betsQuantum, satoshiQuantum, sigilSeconds, secondsQuantum);
		SetTextKeepingScroll(_setsLabel, BuildSets(state, square, kind, betsQuantum, satoshiQuantum, sigilSeconds, secondsQuantum));
		_chainLabel.Text = BuildChain();
		SetTextKeepingScroll(_completedLabel, BuildCompleted(state));
	}

	private static string BuildStatus(SigilProgressState state, PlanetarySquare square, SigilKind kind,
		long betsQuantum, long satoshiQuantum, double sigilSeconds, long secondsQuantum)
	{
		var sb = new StringBuilder();
		int target = SigilRules.CurrentTarget(state);
		string how = target <= 9
			? "a win that leaves V on it"
			: target % 10 == 0
				? string.Create(CultureInfo.InvariantCulture, $"behaves as {target / 10}: a win that leaves V on {target / 10}")
				: string.Create(CultureInfo.InvariantCulture,
					$"V = {target / 10} before a win whose root is {target % 10} (at 1–9%, the loss streak adds to the roll)");
		sb.Append(string.Create(CultureInfo.InvariantCulture, $"[b]Next target: {target}[/b]   [color={MutedColor}]{how}[/color]\n"));
		sb.Append(string.Create(CultureInfo.InvariantCulture,
			$"V: [b]{(state.V == 0 ? "—" : state.V.ToString(CultureInfo.InvariantCulture))}[/b]     Loss streak: {state.LossStreak}\n"));

		if (state.Step > 0)
		{
			string color = SigilCanvas.ColorForChance(state.PassChance).ToHtml(false);
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"Pass in progress: set {state.SetIndex + 1}, {state.Step} of {square.N} numbers, at [color=#{color}]{state.PassChance}%[/color]"));
			sb.Append($"   [color={MutedColor}](lost if the chance changes)[/color]\n");
		}
		else
		{
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"Set {state.SetIndex + 1} waits for its first number, which draws its silhouette\n"));
		}

		double done = SigilRules.SigilCompletion(state);
		sb.Append(string.Create(CultureInfo.InvariantCulture,
			$"[b]{done * 100:F1}%[/b] complete  ·  {(1 - done) * 100:F1}% to go   [color={MutedColor}](completed passes only)[/color]\n"));
		sb.Append(string.Create(CultureInfo.InvariantCulture,
			$"Bets: {CounterQuantizer.Format(state.BetsPlaced, betsQuantum)}   ·   SC wagered: {FormatSc(state.ScWagered, satoshiQuantum)} SC   ·   Game time: {FormatDays(sigilSeconds, secondsQuantum)} days"));

		// D-GN.19: a planet's two sigils add up; while the nocturnal one is drawn, show the diurnal's figures beside it.
		if (kind == SigilKind.Nocturnal)
		{
			var diurnal = state.Completed.LastOrDefault(c => c.SigilIndex == state.SigilIndex - 1);
			if (diurnal != null)
			{
				sb.Append(string.Create(CultureInfo.InvariantCulture,
					$"\n[color={MutedColor}]{square.Planet} so far: {diurnal.BetsPlaced + state.BetsPlaced:N0} bets · {diurnal.ScWagered + state.ScWagered:N2} SC (with the {square.DiurnalSigilName})[/color]"));
			}
		}

		return sb.ToString();
	}

	private static string BuildSets(SigilProgressState state, PlanetarySquare square, SigilKind kind,
		long betsQuantum, long satoshiQuantum, double sigilSeconds, long secondsQuantum)
	{
		var sb = new StringBuilder("[b]Sets[/b]\n[table=8]");
		foreach (string header in new[] { "Set", "Numbers", "Passes", "Done", "Bets", "Bets %", "Game days", "Restarts" })
			sb.Append($"[cell][color={MutedColor}]{header}[/color]   [/cell]");

		for (int set = 0; set < state.Sets.Count; set++)
		{
			var progress = state.Sets[set];
			bool current = set == state.SetIndex;
			string numbers = string.Join("-", Enumerable.Range(0, square.N).Select(k => square.NumberAt(kind, set, k)));
			string color = progress.Finished
				? SigilCanvas.ColorForChance(1).ToHtml(false)
				: progress.LastPassChance > 0 ? SigilCanvas.ColorForChance(progress.LastPassChance).ToHtml(false) : "bbbbbb";
			// The set being drawn is the only row whose figures move with every bet; it takes the sampled quanta.
			string bets = current ? CounterQuantizer.Format(progress.BetsPlaced, betsQuantum)
				: progress.BetsPlaced.ToString("N0", CultureInfo.InvariantCulture);
			double share = state.BetsPlaced > 0 ? (double)progress.BetsPlaced / state.BetsPlaced * 100 : 0;
			string days = current ? FormatDays(progress.GameSeconds, secondsQuantum)
				: (progress.GameSeconds / 86400).ToString("F2", CultureInfo.InvariantCulture);
			string mark = current ? "> " : "";

			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"[cell]{mark}{set + 1}   [/cell][cell]{numbers}   [/cell][cell]{progress.PassesCompleted}   [/cell]" +
				$"[cell][color=#{color}]{SigilRules.SetCompletion(progress) * 100:F1}%[/color]   [/cell][cell]{bets}   [/cell]" +
				$"[cell]{share:F1}%   [/cell][cell]{days}   [/cell][cell]{progress.PassRestarts}[/cell]"));
		}

		sb.Append("[/table]");
		return sb.ToString();
	}

	// D-GN.21 — the latest bets as operations on V: V, + or − the root (green on a win, red on a loss), the new V.
	// Sampled: at autobet rate these are the latest bets at the moment of the repaint, never a log of every bet.
	private string BuildChain()
	{
		int count = PlayerSigilProgress.CopyRecentSteps(_chain, ChainLines);
		var sb = new StringBuilder("[b]V, bet by bet[/b]   [color=" + MutedColor + "](latest at the bottom)[/color]\n");
		for (int i = 0; i < count; i++)
		{
			var step = _chain[i];
			string root = string.Create(CultureInfo.InvariantCulture,
				$"[color={(step.IsWin ? WinColor : LossColor)}]{step.Root}[/color]");
			if (step.VBefore == 0)
			{
				sb.Append(string.Create(CultureInfo.InvariantCulture, $"new sigil: first roll {root}  =  [b]{step.VAfter}[/b]"));
			}
			else if (!step.IsWin)
			{
				sb.Append(string.Create(CultureInfo.InvariantCulture, $"{step.VBefore}  -  {root}  =  [b]{step.VAfter}[/b]"));
			}
			else
			{
				string added = step.Streak > 0
					? string.Create(CultureInfo.InvariantCulture, $"([color={MutedColor}]{step.Streak}[/color] + {root})")
					: root;
				sb.Append(string.Create(CultureInfo.InvariantCulture, $"{step.VBefore}  +  {added}  =  [b]{step.VAfter}[/b]"));
				if (step.Spelled > 0)
					sb.Append(string.Create(CultureInfo.InvariantCulture, $"     spells {step.Spelled}, target {step.Target}"));
				if (step.Hit)
					sb.Append(string.Create(CultureInfo.InvariantCulture, $"     [b][color=#ffd84a]HIT {step.Target}[/color][/b]"));
			}

			sb.Append('\n');
		}

		return sb.ToString();
	}

	private static string BuildCompleted(SigilProgressState state)
	{
		if (state.Completed.Count == 0)
			return "[b]Completed sigils[/b]\n[color=" + MutedColor + "]None yet. The Aquarius Sigil comes first.[/color]";

		var sb = new StringBuilder("[b]Completed sigils[/b]\n");
		foreach (var record in state.Completed)
		{
			var square = PlanetarySquares.PlanetOfSigil(record.SigilIndex);
			var kind = PlanetarySquares.KindOfSigil(record.SigilIndex);
			double days = record.Sets.Sum(s => s.GameSeconds) / 86400;
			int passes = record.Sets.Sum(s => s.PassesCompleted);
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"{square.SigilName(kind)}: {record.BetsPlaced:N0} bets · {record.ScWagered:N2} SC · {passes} passes · {days:F2} game-days · " +
				$"{record.StartedAtUtc.ToLocalTime():yyyy-MM-dd} → {record.CompletedAtUtc.ToLocalTime():yyyy-MM-dd}\n"));

			// Both sigils of a planet drawn: the planet is complete, and its figures are the sum of the two (D-GN.19).
			if (kind == SigilKind.Nocturnal)
			{
				var diurnal = state.Completed.FirstOrDefault(c => c.SigilIndex == record.SigilIndex - 1);
				if (diurnal != null)
				{
					sb.Append(string.Create(CultureInfo.InvariantCulture,
						$"[b]{square.Planet} complete: {diurnal.BetsPlaced + record.BetsPlaced:N0} bets · {diurnal.ScWagered + record.ScWagered:N2} SC[/b]\n"));
				}
			}
		}

		return sb.ToString();
	}

	private static long ToSatoshis(decimal sc) => (long)(sc * 100_000_000m);

	// SC floored to the sampled quantum, showing only the decimals the quantum can carry (§29.13).
	private static string FormatSc(decimal sc, long satoshiQuantum)
	{
		if (satoshiQuantum <= 1)
			return sc.ToString("N8", CultureInfo.InvariantCulture);

		int decimals = Math.Max(0, 8 - (int)Math.Round(Math.Log10(satoshiQuantum)));
		long satoshis = ToSatoshis(sc);
		decimal floored = (satoshis - satoshis % satoshiQuantum) / 100_000_000m;
		return "~" + floored.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
	}

	// Game-days floored to the sampled quantum of seconds: two decimals (~864 s) only while the step is below that.
	private static string FormatDays(double seconds, long secondsQuantum)
	{
		int decimals = secondsQuantum <= 864 ? 2 : secondsQuantum <= 8640 ? 1 : 0;
		long whole = (long)seconds;
		double floored = secondsQuantum > 1 ? whole - whole % secondsQuantum : seconds;
		string text = (floored / 86400).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
		return secondsQuantum > 1 ? "~" + text : text;
	}

	// Setting Text resets a RichTextLabel's scroll; keep it, and pad the last line clear of the edge (§29.3, traps 4–5).
	private static void SetTextKeepingScroll(RichTextLabel label, string text)
	{
		VScrollBar scroll = label.GetVScrollBar();
		double position = scroll.Value;
		label.Text = text + "\n\n\n";
		scroll.Value = position;
	}
}
