using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Scripts.Dice;
using Scripts.Numerology;

// Unit 1 harness: the game's own PlanetarySquares / SigilRules / DiceEngine, checked against the plan's Node figures.
var engine = new DiceEngine();
int failures = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what); if (!ok) failures++; }
string Cell(PlanetarySquare q, int number) { var (r, c) = q.PositionOf(number); return $"{(char)('A' + c)}{r + 1}"; }
string Path(PlanetarySquare q, SigilKind k, int set) => string.Join("-", Enumerable.Range(0, q.N).Select(s => Cell(q, q.NumberAt(k, set, s))));

// Bets until the sigil currently being drawn completes, at a fixed chance and side policy. Returns its record.
CompletedSigilRecord DrawOne(SigilProgressState st, int chance, Func<long, bool> high, ref long setPasses)
{
	int start = st.Completed.Count;
	long bet = 0;
	while (st.Completed.Count == start)
	{
		var res = engine.Play(1m, chance, high(bet++));
		var o = SigilRules.ApplyBet(st, res.Roll, res.IsWin, chance, 1m, DateTime.UnixEpoch);
		if ((o & SigilBetOutcome.SetPassCompleted) != 0) setPasses++;
	}
	return st.Completed[start];
}

(int K1, int K2) Targets(int n)
{
	int k1 = 0, k2 = 0;
	for (int t = 1; t <= n * n; t++) { if (t <= 9 || t % 10 == 0) k1++; else k2++; }
	return (k1, k2);
}

double Predicted(int n) { var (k1, k2) = Targets(n); return 900.0 * k1 + 8100.0 * k2; }

// One run's standard deviation, as a share of the prediction. Each target is hit c times; the bets to one hit are
// geometric with mean 900/c (1-digit, round tens) or 8,100/c (two-digit), and a geometric's sd is about its mean.
double RelativeSd(int n, int c)
{
	var (k1, k2) = Targets(n);
	double m1 = 900.0 / c, m2 = 8100.0 / c;
	return Math.Sqrt(c * (k1 * m1 * m1 + k2 * m2 * m2)) / Predicted(n);
}

Console.WriteLine("1. Squares and paths (plan §3.2; source guide §4.3)");
var saturn = PlanetarySquares.All[0];
Check(PlanetarySquares.All.Select(q => q.MagicConstant).SequenceEqual(new[] { 15, 34, 65, 111, 175, 260, 369 }), "magic constants 15 · 34 · 65 · 111 · 175 · 260 · 369");
Check(Path(saturn, SigilKind.Diurnal, 0) == "B3-C1-A2" && Path(saturn, SigilKind.Diurnal, 1) == "A1-B2-C3" && Path(saturn, SigilKind.Diurnal, 2) == "C2-A3-B1", "Aquarius sets B3-C1-A2 · A1-B2-C3 · C2-A3-B1");
Check(Path(saturn, SigilKind.Nocturnal, 0) == "B3-A1-C2" && Path(saturn, SigilKind.Nocturnal, 1) == "C1-B2-A3" && Path(saturn, SigilKind.Nocturnal, 2) == "A2-C3-B1", "Capricorn sets B3-A1-C2 · C1-B2-A3 · A2-C3-B1");
Check(Path(PlanetarySquares.All[6], SigilKind.Diurnal, 0) == "E6-F7-G8-H9-I1-A2-B3-C4-D5", "Eclipsed Cancer set 1 E6-…-D5");
Check(Path(PlanetarySquares.All[6], SigilKind.Nocturnal, 8) == "D5-C6-B7-A8-I9-H1-G2-F3-E4", "Cancer set 9 D5-…-E4");
Check(string.Join(",", Enumerable.Range(0, 3).SelectMany(g => Enumerable.Range(0, 3).Select(s => saturn.NumberAt(SigilKind.Nocturnal, g, s)))) == "1,4,7,2,5,8,3,6,9", "Capricorn target order 1,4,7,2,5,8,3,6,9 (D-GN.17)");
bool reflection = PlanetarySquares.All.All(q =>
{
	for (int x = 1; x <= q.N * q.N; x++)
	{
		int a = (x - 1) / q.N, b = (x - 1) % q.N, t = b * q.N + a + 1;
		var (r, c) = q.PositionOf(x); var (r2, c2) = q.PositionOf(t);
		bool ok = q.N % 2 == 1 ? (r2 == r && c2 == q.N - 1 - c) : (r2 == q.N - 1 - c && c2 == q.N - 1 - r);
		if (!ok) return false;
	}
	return true;
});
Check(reflection, "day/night pair: odd squares left-right, even squares anti-diagonal (§3.4)");

Console.WriteLine("\n2. Rule details");
{
	var st = SigilRules.NewProgress();
	// Find a won roll at 50% Low that hits target 1 on the first bet: V = root(roll) must be 1 → roll 1, 10, 19, 28, 37, 46.
	var o = SigilRules.ApplyBet(st, 10, true, 50, 1m, DateTime.UnixEpoch);
	Check((o & SigilBetOutcome.SilhouetteDrawn) != 0 && st.Step == 1 && st.PassChance == 50 && st.V == 1, "first bet, win, roll 10 → V 1, hits target 1, silhouette at 50%");
	o = SigilRules.ApplyBet(st, 99, false, 40, 1m, DateTime.UnixEpoch);
	Check((o & SigilBetOutcome.PassRestarted) != 0 && st.Step == 0 && st.V == 1, "chance 50 → 40 restarts the pass; V kept (99 is root 9 = zero)");
	o = SigilRules.ApplyBet(st, 99, false, 40, 1m, DateTime.UnixEpoch);
	Check((o & SigilBetOutcome.PassRestarted) == 0, "no pass in progress → nothing to restart");
	Check(st.BetsPlaced == 3 && st.ScWagered == 3m, "D-GN.19 tracks 3 bets and 3 SC");
	Check(SigilRules.RootOf(0) == 9 && SigilRules.RootOf(47) == 2 && SigilRules.RootOf(99) == 9, "roots: 00 → 9, 47 → 2, 99 → 9");

	// The plan's worked example (D-GN.12): target 21, V = 3, win roll 8 → 38 miss, V 2; win roll 73 → 21 hit.
	var s2 = SigilRules.NewProgress();
	s2.SigilIndex = 12; s2.Sets = Enumerable.Range(0, 9).Select(_ => new SigilSetProgress()).ToList();   // Eclipsed Cancer
	s2.SetIndex = 2; s2.Step = 2; s2.PassChance = 50; s2.V = 3;   // diurnal set 3 is 19-27: step 2 → target 21
	Check(SigilRules.CurrentTarget(s2) == 21, "target 21 set up");
	o = SigilRules.ApplyBet(s2, 8, true, 50, 1m, DateTime.UnixEpoch, out var st1);
	Check((o & SigilBetOutcome.Hit) == 0 && s2.V == 2, "V 3 + roll 8 spells 38: miss, V → 2");
	Check(st1 == new SigilStep(3, true, 8, 0, 2, 21, 38, false), $"its V-chain step reads 3 + 8 = 2, spells 38 ({st1})");
	o = SigilRules.ApplyBet(s2, 73, true, 50, 1m, DateTime.UnixEpoch, out var st2);
	Check((o & SigilBetOutcome.LineDrawn) != 0 && s2.V == 3, "V 2 + roll 73 (root 1) spells 21: hit, V → 3");
	Check(st2 == new SigilStep(2, true, 1, 0, 3, 21, 21, true), $"its V-chain step reads 2 + 1 = 3, spells 21, HIT ({st2})");
	SigilRules.ApplyBet(s2, 47, false, 50, 1m, DateTime.UnixEpoch, out var st3);
	Check(st3 == new SigilStep(3, false, 2, 0, 1, 22, 0, false), $"a loss reads 3 - 2 = 1 ({st3})");
	var s3 = SigilRules.NewProgress(); s3.V = 4; s3.LossStreak = 6;
	SigilRules.ApplyBet(s3, 3, true, 5, 1m, DateTime.UnixEpoch, out var st4);
	Check(st4.Streak == 6 && st4.VAfter == 4, $"at 5% a win adds the streak: 4 + (6 + 3) = 4 ({st4})");
}

Console.WriteLine("\n3. Exact completion: a set at chance c takes exactly c passes (D-GN.8, rationals)");
foreach (int c in new[] { 3, 7, 50, 95 })
{
	var st = SigilRules.NewProgress(); long passes = 0;
	DrawOne(st, c, _ => false, ref passes);
	Check(passes == 3L * c, $"Aquarius at {c}%: {passes} set passes (expected {3 * c})");
}

Console.WriteLine("\n4. Pr-2: a 1-digit target is hit on ≈ 1 win in 9 (≥ 10%)");
{
	var st = SigilRules.NewProgress(); long wins = 0, hits = 0;
	while (wins < 300_000)
	{
		var res = engine.Play(1m, 50, false);
		var o = SigilRules.ApplyBet(st, res.Roll, res.IsWin, 50, 1m, DateTime.UnixEpoch);
		if (res.IsWin) wins++;
		if ((o & SigilBetOutcome.Hit) != 0) hits++;
		if (st.SigilIndex != 0) st = SigilRules.NewProgress();   // stay on Aquarius: all its targets are 1-digit
	}
	double rate = (double)hits / wins;
	Check(Math.Abs(rate - 1.0 / 9) < 0.003,
		string.Create(CultureInfo.InvariantCulture, $"hits / wins = {rate:F4} (1/9 = {1.0 / 9:F4}) over {wins:N0} wins"));
}

Console.WriteLine("\n5. Pr-3 / Pr-4: violet cost per sigil, every planet in unlock order (bets placed, D-GN.19 record)");
var sw = Stopwatch.StartNew();
var settings = new (int Chance, string Side, Func<long, bool> High)[]
{
	(1, "Low", _ => false), (1, "High", _ => true), (5, "Low", _ => false), (5, "alternate", b => b % 2 == 1),
	(50, "Low", _ => false), (95, "High", _ => true),
};
const int Runs = 6;
var means = new double[settings.Length, PlanetarySquares.SigilCount];
for (int i = 0; i < settings.Length; i++)
	for (int run = 0; run < Runs; run++)
	{
		var st = SigilRules.NewProgress(); long passes = 0;
		while (!st.AllComplete)
		{
			var rec = DrawOne(st, settings[i].Chance, settings[i].High, ref passes);
			means[i, rec.SigilIndex] += rec.BetsPlaced / (double)Runs;
		}
	}
Console.Write("  sigil                       predicted");
foreach (var s in settings) Console.Write($"{s.Chance + "% " + s.Side,13}");
Console.WriteLine();
double worst = 0;
string worstCell = "";
for (int k = 0; k < PlanetarySquares.SigilCount; k++)
{
	var q = PlanetarySquares.PlanetOfSigil(k);
	double p = Predicted(q.N);
	Console.Write(string.Create(CultureInfo.InvariantCulture, $"  {q.SigilName(PlanetarySquares.KindOfSigil(k)),-26}{p,10:N0}"));
	for (int i = 0; i < settings.Length; i++)
	{
		Console.Write(string.Create(CultureInfo.InvariantCulture, $"{means[i, k],13:N0}"));
		double sdOfMean = RelativeSd(q.N, settings[i].Chance) * p / Math.Sqrt(Runs);
		double z = (means[i, k] - p) / sdOfMean;
		if (Math.Abs(z) > worst) { worst = Math.Abs(z); worstCell = $"{q.SigilName(PlanetarySquares.KindOfSigil(k))} at {settings[i].Chance}% {settings[i].Side}"; }
	}
	Console.WriteLine();
}
// A fixed percentage tolerance was flaky: at 1% a violet sigil is one pass and 6 runs leave ~10% noise on Aries.
Check(worst < 4.0, string.Create(CultureInfo.InvariantCulture,
	$"every cell within 4σ of the prediction, σ from the cell's own expected noise (worst {worst:F2}σ, {worstCell}; {Runs} runs per cell)"));
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  ({sw.Elapsed.TotalSeconds:F1} s)"));

Console.WriteLine("\n6. Per-set tracking (D-GN.20): the sets add up to the sigil, and the passes to c per set");
{
	// One bet every 100 game-seconds, as at one hardware credit; the chance changes once mid-run to force restarts.
	var st = SigilRules.NewProgress();
	var t0 = new DateTime(2009, 3, 21, 0, 0, 0, DateTimeKind.Utc);
	long bet = 0;
	while (st.Completed.Count == 0)
	{
		int chance = bet < 2000 ? 40 : 50;
		var res = engine.Play(1m, chance, false);
		SigilRules.ApplyBet(st, res.Roll, res.IsWin, chance, 0.5m, t0.AddSeconds(100 * bet));
		bet++;
	}
	var rec = st.Completed[0];
	Check(rec.Sets.Sum(s => s.BetsPlaced) == rec.BetsPlaced && rec.BetsPlaced == bet, $"set bets add up to the sigil's ({rec.BetsPlaced} bets)");
	Check(rec.Sets.Sum(s => s.ScWagered) == rec.ScWagered && rec.ScWagered == 0.5m * bet, "set SC adds up to the sigil's");
	Check(Math.Abs(rec.Sets.Sum(s => s.GameSeconds) - 100.0 * (bet - 1)) < 1e-6 && rec.CompletedAtUtc - rec.StartedAtUtc == TimeSpan.FromSeconds(100 * (bet - 1)),
		"set game time adds up to first-bet → last-bet");
	Check(rec.Sets.All(s => s.Finished && Math.Abs(SigilRules.SetCompletion(s) - 1.0) < 1e-12), "every set finished at 100%");
	Console.WriteLine("  set  passes  restarts      bets   share     game-days");
	foreach (var (s, i) in rec.Sets.Select((s, i) => (s, i)))
		Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
			$"  {i + 1,3}  {s.PassesCompleted,6}  {s.PassRestarts,8}  {s.BetsPlaced,8:N0}  {(double)s.BetsPlaced / rec.BetsPlaced,6:P1}  {s.GameSeconds / 86400,12:F2}"));
	Check(SigilRules.SigilCompletion(st) == 0.0 && st.SigilIndex == 1, "the next sigil (Capricorn) starts at 0%");
	// The godot.log lines unit 5 reads: they must carry the figures the analysis needs, in invariant format.
	string completed = SigilRules.Describe(rec), live = SigilRules.Describe(st);
	Console.WriteLine("  log: SIGIL COMPLETE — " + completed);
	Console.WriteLine("  log: " + live);
	Check(completed.StartsWith("Aquarius Sigil: bets=" + rec.BetsPlaced) && completed.Contains("@40") && completed.Contains("@50")
		&& live.StartsWith("Capricorn Sigil: bets=0 ")
		&& completed.Contains("sc=" + rec.ScWagered.ToString("F8", CultureInfo.InvariantCulture) + " ") && live.Contains("sc=0.00000000 "),
		"Describe() carries the sigil, its bets and each set's passes by chance, invariant (no decimal comma)");
}

Console.WriteLine("\n7. The checkpoint round trip (P-GN.4, D-GN.11)");
{
	string Json(SigilProgressState s) => System.Text.Json.JsonSerializer.Serialize(s);
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	var outcomes = new List<SigilBetOutcome>();
	PlayerSigilProgress.Changed += outcomes.Add;
	var t0 = new DateTime(2009, 3, 21, 0, 0, 0, DateTimeKind.Utc);
	for (int b = 0; b < 3000; b++)
	{
		var res = engine.Play(1m, 50, false);
		PlayerSigilProgress.RegisterPlayerBet(new Scripts.Finance.BetTransactionEvent(
			1m, 0m, 0m, 0m, res.IsWin, res.Roll, 50, res.Multiplier, false, t0.AddSeconds(100 * b)));
	}
	var block = PlayerSigilProgress.CaptureForCheckpoint();
	string atBlock = Json(block);
	Check(PlayerSigilProgress.State.BetsPlaced == 3000 && outcomes.Count > 0 && outcomes.All(o => o != SigilBetOutcome.None),
		$"3,000 bets registered; Changed fired {outcomes.Count} times, never for a bet that changed nothing");
	for (int b = 0; b < 500; b++)
		PlayerSigilProgress.RegisterPlayerBet(new Scripts.Finance.BetTransactionEvent(
			1m, 0m, 0m, 0m, true, 1, 50, 1.98m, false, t0.AddSeconds(100 * (3000 + b))));
	Check(Json(block) == atBlock, "the captured snapshot does not move with the live state");
	var recent = new SigilStep[8];
	int got = PlayerSigilProgress.CopyRecentSteps(recent, 8);
	Check(got == 8 && recent.All(s => s.IsWin && s.Root == 1) && recent.Take(7).Zip(recent.Skip(1)).All(p => p.First.VAfter == p.Second.VBefore),
		"the V chain holds the latest 8 operations, oldest first, each starting where the previous one ended");
	PlayerSigilProgress.RestoreFromCheckpoint(block);
	Check(Json(PlayerSigilProgress.State) == atBlock, "a restart restores exactly the block's state");
	Check(PlayerSigilProgress.CopyRecentSteps(recent, 8) == 0, "a restore empties the chain");
	PlayerSigilProgress.RestoreFromCheckpoint(null);
	Check(PlayerSigilProgress.State.BetsPlaced == 0 && PlayerSigilProgress.State.SigilIndex == 0 && PlayerSigilProgress.State.Sets.Count == 3,
		"a checkpoint from before Graphic Numerology restores fresh progress");
	PlayerSigilProgress.Changed -= outcomes.Add;
}

Console.WriteLine("\n8. Cost per bet (the rule runs on every player bet)");
{
	const int Bets = 5_000_000;
	var rng = new Random(1);
	var rolls = new int[Bets];
	for (int i = 0; i < Bets; i++) rolls[i] = rng.Next(0, 100);
	var st = SigilRules.NewProgress();
	var t0 = new DateTime(2009, 3, 21, 0, 0, 0, DateTimeKind.Utc);
	for (int i = 0; i < 100_000; i++) SigilRules.ApplyBet(st, rolls[i], rolls[i] < 50, 50, 1m, t0.AddSeconds(i));   // warm-up
	long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
	var timer = Stopwatch.StartNew();
	for (int i = 0; i < Bets; i++) SigilRules.ApplyBet(st, rolls[i], rolls[i] < 50, 50, 1m, t0.AddSeconds(100_000 + i));
	timer.Stop();
	double ns = timer.Elapsed.TotalMilliseconds * 1e6 / Bets;
	double bytes = (double)(GC.GetAllocatedBytesForCurrentThread() - bytesBefore) / Bets;
	Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {ns:F1} ns and {bytes:F2} bytes allocated per bet, mean of {Bets:N0} bets at 50%"));
	Check(bytes < 8, "an ordinary bet allocates nothing (what is allocated comes from completed passes and sigils)");
}

Console.WriteLine("\n9. Colour steps and rounds (D-GN.22): the colour is the set's progress, 1 red … 95 violet");
{
	SigilSetProgress With(params (int Passes, int Chance)[] passes)
	{
		var set = new SigilSetProgress();
		foreach (var (p, c) in passes) set.PassesByChance[c] = set.PassesByChance.GetValueOrDefault(c) + p;
		return set;
	}
	int Step(params (int, int)[] passes) => SigilRules.ColorStep(With(passes));

	Check(Step() == 0, "no pass yet → step 0 (nothing drawn)");
	Check(Step((1, 95)) == 1 && Step((2, 95)) == 2 && Step((94, 95)) == 94, "at 95%: one step per pass — 1, 2 … 94");
	Check(Step((1, 94)) == 1 && Step((2, 94)) == 2 && Step((94, 94)) == 95, "at 94%: 1/94 = 1.0106/95 a pass → 1, 2 … and 94 passes finish (exactly 1)");
	Check(Step((1, 50)) == 1 && Step((2, 50)) == 3 && Step((1, 10)) == 9 && Step((1, 2)) == 47 && Step((1, 1)) == 95,
		"one pass: 50% → 1 (1.9/95), two → 3 (3.8) · 10% → 9 (9.5) · 2% → 47 (47.5) · 1% → 95");
	Check(Step((10, 95), (1, 10)) == 19, "passes add over what is there: 10 at 95% + 1 at 10% = 19.5/95 → step 19");
	Check(Step((2, 3)) == 63, "two passes at 3% = 2/3 → 63.33/95 → step 63");
	Check(Step((3, 3)) == 95, "three passes at 3% = exactly 1 → 95 (a float sum would read 0.999… and stop at 94)");

	// Step 95 means finished, and only that: the colour reads the verdict's own exact fraction.
	Check(Step((94, 95), (1, 94)) == 95, "94 at 95% + 1 at 94% = 8931/8930, past 1 → 95");
	var notQuite = With((1, 2), (1, 3), (1, 7), (1, 43));   // 1/2 + 1/3 + 1/7 + 1/43 = 1805/1806
	Check(SigilRules.ColorStep(notQuite) == 94, "1/2 + 1/3 + 1/7 + 1/43 = 1805/1806, short of 1 → 94, never 95");
	notQuite.Finished = false;
	Check(SigilRules.ColorStepAfterPass(notQuite, 95) == 95 && SigilRules.ColorStepAfterPass(With((1, 95)), 95) == 2
		&& SigilRules.ColorStepAfterPass(With(), 50) == 1 && SigilRules.ColorStepAfterPass(With((1, 50)), 50) == 3,
		"the preview equals the step after one more pass");

	// Over real play at mixed chances: steps never go down, 95 ⇔ finished, the preview of each completed pass came
	// true, and the round invariants hold after every pass.
	var rng = new Random(7);
	var st = SigilRules.NewProgress();
	var last = new int[3];
	bool monotone = true, ninetyFive = true, previewTrue = true, roundOk = true;
	int passesSeen = 0;
	while (st.Completed.Count == 0)
	{
		int chance = new[] { 95, 80, 50, 20, 9, 3 }[rng.Next(6)];
		for (int b = 0; b < 400 && st.Completed.Count == 0; b++)
		{
			int setIndex = st.SetIndex;
			int predicted = SigilRules.ColorStepAfterPass(st.Sets[setIndex], chance);
			var res = engine.Play(1m, chance, rng.Next(2) == 0);
			var o = SigilRules.ApplyBet(st, res.Roll, res.IsWin, chance, 1m, DateTime.UnixEpoch);
			if ((o & SigilBetOutcome.SetPassCompleted) == 0 || (o & SigilBetOutcome.SigilCompleted) != 0)
				continue;
			passesSeen++;
			int now = SigilRules.ColorStep(st.Sets[setIndex]);
			previewTrue &= now == predicted;
			for (int i = 0; i < 3; i++)
			{
				int s = SigilRules.ColorStep(st.Sets[i]);
				monotone &= s >= last[i];
				ninetyFive &= (s == 95) == st.Sets[i].Finished;
				last[i] = s;
			}
			var open = st.Sets.Where(x => !x.Finished).Select(x => x.PassesCompleted).ToList();
			roundOk &= open.Max() - open.Min() <= 1 && !SigilRules.PassedThisRound(st, st.SetIndex);
		}
	}
	Check(monotone && ninetyFive && previewTrue && roundOk, string.Create(CultureInfo.InvariantCulture,
		$"over {passesSeen} passes at mixed chances: steps never fall, 95 ⇔ finished, every preview came true, rounds hold"));
}

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASS" : $"\n{failures} CHECK(S) FAILED");
return failures;
