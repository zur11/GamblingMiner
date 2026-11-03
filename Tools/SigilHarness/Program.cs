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

// Bets until this sigil completes, at a fixed chance and side policy.
SigilProgressState DrawOne(SigilProgressState st, int chance, Func<long, bool> high, ref long setPasses)
{
	long bet = 0;
	while (!st.IsComplete)
	{
		var res = engine.Play(1m, chance, high(bet++));
		var o = SigilRules.ApplyBet(st, res.Roll, res.IsWin, chance, 1m, DateTime.UnixEpoch);
		if ((o & SigilBetOutcome.SetPassCompleted) != 0) setPasses++;
	}
	return st;
}

Scripts.Finance.BetTransactionEvent Bet(int roll, bool isWin, int chance, DateTime when) =>
	new(1m, 0m, 0m, 0m, isWin, roll, chance, 1m, false, when);

string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);

(int K1, int K2) Targets(int n)
{
	int k1 = 0, k2 = 0;
	for (int t = 1; t <= n * n; t++) { if (t <= 9 || t % 10 == 0) k1++; else k2++; }
	return (k1, k2);
}

// Bets for one-PassesPerChance-th of violet: c passes per set at chance c. Violet is this × SigilRules.PassesPerChance.
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

Console.WriteLine("\n3. Exact completion: a set at chance c takes exactly PassesPerChance × c passes (D-GN.8, D-GN.23, rationals)");
foreach (int c in new[] { 1, 3, 7, 50, 95 })
{
	var st = SigilRules.NewProgress(); long passes = 0;
	var rec = DrawOne(st, c, _ => false, ref passes);
	long expected = 3L * SigilRules.PassesPerChance * c;
	Check(passes == expected && rec.Sets.All(s => s.PassesCompleted == SigilRules.PassesPerChance * c),
		string.Create(CultureInfo.InvariantCulture, $"Aquarius at {c}%: {passes:N0} set passes (expected {expected:N0}), {rec.BetsPlaced:N0} bets"));
}
{
	// The largest square at the rarest chance: D-GN.13's loss streak is what makes this finish at all.
	var st = SigilRules.NewProgress(13); long passes = 0;
	var sw13 = Stopwatch.StartNew();
	var rec = DrawOne(st, 1, _ => false, ref passes);
	Check(passes == 9L * SigilRules.PassesPerChance, string.Create(CultureInfo.InvariantCulture,
		$"{PlanetarySquares.PlanetOfSigil(13).SigilName(PlanetarySquares.KindOfSigil(13))} at 1%: {passes:N0} set passes, {rec.BetsPlaced:N0} bets ({sw13.Elapsed.TotalSeconds:F1} s)"));
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
		if (st.IsComplete) st = SigilRules.NewProgress();   // stay on Aquarius: all its targets are 1-digit
	}
	double rate = (double)hits / wins;
	Check(Math.Abs(rate - 1.0 / 9) < 0.003,
		string.Create(CultureInfo.InvariantCulture, $"hits / wins = {rate:F4} (1/9 = {1.0 / 9:F4}) over {wins:N0} wins"));
}

// Every sigil to violet in every setting would be 11.8 billion bets at ×100 (D-GN.23). The balance is a property of a pass,
// so the table measures c passes per set: one-PassesPerChance-th of violet, the same bets and the same noise as before it.
Console.WriteLine("\n5. Pr-3 / Pr-4: cost of c passes per set — one-hundredth of violet — per sigil (bets placed, D-GN.19 record)");
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
		for (int k = 0; k < PlanetarySquares.SigilCount; k++)
		{
			var st = SigilRules.NewProgress(k);
			long target = (long)st.Sets.Count * settings[i].Chance, passes = 0, bet = 0;
			while (passes < target)
			{
				var res = engine.Play(1m, settings[i].Chance, settings[i].High(bet++));
				if ((SigilRules.ApplyBet(st, res.Roll, res.IsWin, settings[i].Chance, 1m, DateTime.UnixEpoch) & SigilBetOutcome.SetPassCompleted) != 0) passes++;
			}
			means[i, k] += st.BetsPlaced / (double)Runs;
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
// A fixed percentage tolerance was flaky: at 1% this is one pass per set, and 6 runs leave ~10% noise on Aries.
Check(worst < 4.0, string.Create(CultureInfo.InvariantCulture,
	$"every cell within 4σ of the prediction, σ from the cell's own expected noise (worst {worst:F2}σ, {worstCell}; {Runs} runs per cell)"));
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  ({sw.Elapsed.TotalSeconds:F1} s)"));

Console.WriteLine("\n6. Per-set tracking (D-GN.20): the sets add up to the sigil, and the passes to c per set");
{
	// One bet every 100 game-seconds, as at one hardware credit; the chance changes once mid-run to force restarts.
	var st = SigilRules.NewProgress();
	var t0 = new DateTime(2009, 3, 21, 0, 0, 0, DateTimeKind.Utc);
	long bet = 0;
	while (!st.IsComplete)
	{
		int chance = bet < 2000 ? 40 : 50;
		var res = engine.Play(1m, chance, false);
		SigilRules.ApplyBet(st, res.Roll, res.IsWin, chance, 0.5m, t0.AddSeconds(100 * bet));
		bet++;
	}
	var rec = st;
	Check(rec.Sets.Sum(s => s.BetsPlaced) == rec.BetsPlaced && rec.BetsPlaced == bet, $"set bets add up to the sigil's ({rec.BetsPlaced} bets)");
	Check(rec.Sets.Sum(s => s.ScWagered) == rec.ScWagered && rec.ScWagered == 0.5m * bet, "set SC adds up to the sigil's");
	Check(Math.Abs(rec.Sets.Sum(s => s.GameSeconds) - 100.0 * (bet - 1)) < 1e-6 && rec.CompletedAtUtc - rec.StartedAtUtc == TimeSpan.FromSeconds(100 * (bet - 1)),
		"set game time adds up to first-bet → last-bet");
	Check(rec.Sets.All(s => s.Finished && Math.Abs(SigilRules.SetCompletion(s) - 1.0) < 1e-12), "every set finished at 100%");
	Console.WriteLine("  set  passes  restarts      bets   share     game-days");
	foreach (var (s, i) in rec.Sets.Select((s, i) => (s, i)))
		Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
			$"  {i + 1,3}  {s.PassesCompleted,6}  {s.PassRestarts,8}  {s.BetsPlaced,8:N0}  {(double)s.BetsPlaced / rec.BetsPlaced,6:P1}  {s.GameSeconds / 86400,12:F2}"));
	Check(SigilRules.SigilCompletion(st) == 1.0 && SigilRules.SigilLevel(st) == SigilRules.MaxLevel && st.SigilIndex == 0,
		"a complete sigil stays itself, at 100% and MaxLevel: nothing moves on by itself (D-GN.26)");
	var frozen = Json(st);
	var after = SigilRules.ApplyBet(st, 1, true, 50, 1m, t0.AddSeconds(100 * bet), out var stepAfter);
	Check(after == SigilBetOutcome.None && stepAfter == default && Json(st) == frozen, "a bet on a complete sigil returns None and changes nothing");
	// The godot.log lines the measurements read: they must carry the figures the analysis needs, in invariant format.
	string completed = SigilRules.DescribeCompleted(rec), done = SigilRules.Describe(st), fresh = SigilRules.Describe(SigilRules.NewProgress(1));
	Console.WriteLine("  log: SIGIL COMPLETE — " + completed);
	Console.WriteLine("  log: " + done);
	Console.WriteLine("  log: " + fresh);
	Check(completed.StartsWith("Aquarius Sigil: bets=" + rec.BetsPlaced) && completed.Contains("@40") && completed.Contains("@50")
		&& done.StartsWith("Aquarius Sigil: level=" + SigilRules.MaxLevel + " ") && done.Contains(" target=complete ")
		&& fresh.StartsWith("Capricorn Sigil: level=0 bets=0 ") && fresh.Contains(" target=1 ")
		&& completed.Contains("sc=" + rec.ScWagered.ToString("F8", CultureInfo.InvariantCulture) + " ") && fresh.Contains("sc=0.00000000 "),
		"Describe() carries the sigil, its level, its bets and each set's passes by chance, invariant (no decimal comma)");
	Check(SigilRules.Describe((SigilProgressState)null) == "slot empty"
		&& SigilRules.Describe(new SigilCollectionState()) == "slot=empty owned=0 planetary=0", "Describe() reads an empty slot and an empty collection");
}

Console.WriteLine("\n7. Pr-13: the checkpoint round trip (P-GN.4, D-GN.11, D-GN.25)");
{
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	var outcomes = new List<SigilBetOutcome>();
	PlayerSigilProgress.Changed += outcomes.Add;
	var t0 = new DateTime(2009, 3, 21, 0, 0, 0, DateTimeKind.Utc);
	PlayerSigilProgress.Acquire(0);
	PlayerSigilProgress.Acquire(1);
	PlayerSigilProgress.Equip(0);
	outcomes.Clear();
	for (int b = 0; b < 3000; b++)
	{
		var res = engine.Play(1m, 50, false);
		PlayerSigilProgress.RegisterPlayerBet(Bet(res.Roll, res.IsWin, 50, t0.AddSeconds(100 * b)));
	}
	var block = PlayerSigilProgress.CaptureForCheckpoint();
	string atBlock = Json(block);
	Check(PlayerSigilProgress.Equipped.BetsPlaced == 3000 && outcomes.Count > 0 && outcomes.All(o => o != SigilBetOutcome.None),
		$"3,000 bets registered; Changed fired {outcomes.Count} times, never for a bet that changed nothing");
	for (int b = 0; b < 500; b++)
		PlayerSigilProgress.RegisterPlayerBet(Bet(1, true, 50, t0.AddSeconds(100 * (3000 + b))));
	Check(Json(block) == atBlock, "the captured snapshot does not move with the live state");
	var recent = new SigilStep[8];
	int got = PlayerSigilProgress.CopyRecentSteps(recent, 8);
	Check(got == 8 && recent.All(s => s.IsWin && s.Root == 1) && recent.Take(7).Zip(recent.Skip(1)).All(p => p.First.VAfter == p.Second.VBefore),
		"the V chain holds the latest 8 operations, oldest first, each starting where the previous one ended");

	// After the block: another acquisition and a switch, then bets on the other sigil. The restart reverts all of it.
	PlayerSigilProgress.Acquire(2);
	PlayerSigilProgress.Equip(1);
	for (int b = 0; b < 500; b++)
	{
		var res = engine.Play(1m, 50, false);
		PlayerSigilProgress.RegisterPlayerBet(Bet(res.Roll, res.IsWin, 50, t0.AddSeconds(100 * (3500 + b))));
	}
	PlayerSigilProgress.RestoreFromCheckpoint(block);
	Check(Json(PlayerSigilProgress.Collection) == atBlock, "a restart restores exactly what is owned, the slot and every sigil's progress");
	Check(!PlayerSigilProgress.IsOwned(2) && PlayerSigilProgress.Equipped.SigilIndex == 0 && PlayerSigilProgress.Find(1).BetsPlaced == 0,
		"and so reverts the acquisition, the switch and the bets made since the block");
	Check(PlayerSigilProgress.CopyRecentSteps(recent, 8) == 0, "a restore empties the chain");
	Check(ReferenceEquals(PlayerSigilProgress.Equipped, PlayerSigilProgress.Find(0)) && !ReferenceEquals(PlayerSigilProgress.Equipped, block.Owned[0]),
		"the cached slot is the restored list's own object, not the snapshot's: a bet after a restore counts in what the next block captures");
	PlayerSigilProgress.RegisterPlayerBet(Bet(50, false, 50, t0.AddSeconds(100 * 3000)));
	Check(PlayerSigilProgress.CaptureForCheckpoint().Owned[0].BetsPlaced == 3001 && block.Owned[0].BetsPlaced == 3000, "so the next capture carries it, and the old snapshot does not");

	PlayerSigilProgress.RestoreFromCheckpoint(null);
	Check(PlayerSigilProgress.Collection.Owned.Count == 0 && PlayerSigilProgress.Equipped == null && PlayerSigilProgress.Collection.EquippedSigilIndex == null,
		"a checkpoint from before the collection existed restores an empty one");
	var fromEmptyJson = System.Text.Json.JsonSerializer.Deserialize<SigilCollectionState>("{}");
	Check(fromEmptyJson.Owned.Count == 0 && fromEmptyJson.EquippedSigilIndex == null, "a fresh collection is empty, with an empty slot (D-GN.25)");
	PlayerSigilProgress.Changed -= outcomes.Add;
}

Console.WriteLine("\n8. Pr-14: cost per bet, the rule and its one caller (every player bet runs both)");
{
	const int Bets = 5_000_000;
	var rng = new Random(1);
	var t0 = new DateTime(2009, 3, 21, 0, 0, 0, DateTimeKind.Utc);
	// Built first and reused: a real event per bet is the bet path's own allocation, not this one's. The clock runs backwards
	// once per 65,536 bets, which costs a comparison and credits nothing.
	var events = new Scripts.Finance.BetTransactionEvent[1 << 16];
	for (int i = 0; i < events.Length; i++) { int roll = rng.Next(0, 100); events[i] = Bet(roll, roll < 50, 50, t0.AddSeconds(i)); }

	// The rule alone, on the largest sigil: 5,000,000 bets at 50% leave it far from complete, whereas Aquarius would complete
	// after ~810,000 and spend the rest on the one-line early return, reading as a third of the cost (D-GN.26 did that).
	var st = SigilRules.NewProgress(PlanetarySquares.SigilCount - 1);
	for (int i = 0; i < 100_000; i++) { var e = events[i & 0xFFFF]; SigilRules.ApplyBet(st, e.Roll, e.IsWin, e.Chance, e.BetAmount, e.Timestamp, out _); }   // warm-up
	long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
	var timer = Stopwatch.StartNew();
	for (int i = 0; i < Bets; i++) { var e = events[i & 0xFFFF]; SigilRules.ApplyBet(st, e.Roll, e.IsWin, e.Chance, e.BetAmount, e.Timestamp, out _); }
	timer.Stop();
	double ns = timer.Elapsed.TotalMilliseconds * 1e6 / Bets;
	double bytes = (double)(GC.GetAllocatedBytesForCurrentThread() - bytesBefore) / Bets;
	Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  the rule alone: {ns:F1} ns and {bytes:F2} bytes per bet, mean of {Bets:N0} bets at 50%"));
	Check(bytes < 8, "an ordinary bet allocates nothing (what is allocated comes from completed passes)");

	// The same sigil through its one caller, in the worst case for a search: all fourteen owned, the last one in the slot.
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	for (int k = 0; k < PlanetarySquares.SigilCount; k++) PlayerSigilProgress.Acquire(k);
	PlayerSigilProgress.Equip(PlanetarySquares.SigilCount - 1);
	for (int i = 0; i < 100_000; i++) PlayerSigilProgress.RegisterPlayerBet(events[i & 0xFFFF]);
	bytesBefore = GC.GetAllocatedBytesForCurrentThread();
	timer.Restart();
	for (int i = 0; i < Bets; i++) PlayerSigilProgress.RegisterPlayerBet(events[i & 0xFFFF]);
	timer.Stop();
	double nsEquipped = timer.Elapsed.TotalMilliseconds * 1e6 / Bets;
	bytes = (double)(GC.GetAllocatedBytesForCurrentThread() - bytesBefore) / Bets;
	Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  through PlayerSigilProgress, 14 owned, the last in the slot: {nsEquipped:F1} ns and {bytes:F2} bytes per bet"));
	Check(bytes < 8, "the equipped path allocates nothing either");
	Check(nsEquipped < ns * 2, string.Create(CultureInfo.InvariantCulture, $"the slot adds little to the rule, compared within this run: {nsEquipped / ns:F2}×"));

	var snapshotTimer = Stopwatch.StartNew();
	var snapshot = PlayerSigilProgress.CaptureForCheckpoint();
	Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
		$"  one capture with 14 owned: {snapshotTimer.Elapsed.TotalMilliseconds:F2} ms, {Json(snapshot).Length:N0} characters (per block, not per bet; not asserted)"));

	PlayerSigilProgress.ResetToPreGenesisDefaults();
	bytesBefore = GC.GetAllocatedBytesForCurrentThread();
	timer.Restart();
	for (int i = 0; i < Bets; i++) PlayerSigilProgress.RegisterPlayerBet(events[i & 0xFFFF]);
	timer.Stop();
	double nsEmpty = timer.Elapsed.TotalMilliseconds * 1e6 / Bets;
	bytes = (double)(GC.GetAllocatedBytesForCurrentThread() - bytesBefore) / Bets;
	Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  an empty slot: {nsEmpty:F1} ns and {bytes:F2} bytes per bet"));
	Check(bytes == 0 && PlayerSigilProgress.LastOutcome == SigilBetOutcome.None, "an empty slot allocates nothing and reports nothing");
}

Console.WriteLine("\n9. Levels and rounds (D-GN.22, D-GN.23, D-GN.27): a set's level is its progress, 1 red … MaxLevel violet");
{
	SigilSetProgress With(params (int Passes, int Chance)[] passes)
	{
		var set = new SigilSetProgress();
		foreach (var (p, c) in passes) set.PassesByChance[c] = set.PassesByChance.GetValueOrDefault(c) + p;
		return set;
	}
	int Level(params (int, int)[] passes) => SigilRules.SetLevel(With(passes));

	Check(SigilRules.PassesPerChance == 100 && SigilRules.MaxLevel == 95, "the figures below are written for PassesPerChance 100 and MaxLevel 95");
	Check(Level() == 0 && Level((99, 95)) == 0, "no pass, and 99 passes at 95%, are still level 0");
	Check(Level((100, 95)) == 1 && Level((200, 95)) == 2 && Level((9400, 95)) == 94, "at 95%: one level per 100 passes — 1, 2 … 94");
	Check(Level((1, 1)) == 0 && Level((2, 1)) == 1 && Level((99, 1)) == 94 && Level((100, 1)) == 95, "at 1%: 1 pass is 0.95 of a level → 0; 2 → 1; 99 → 94; 100 → 95");
	Check(Level((52, 50)) == 0 && Level((53, 50)) == 1, "at 50%: 52 passes are 0.988 of a level → 0; 53 → 1");
	Check(Level((9400, 94)) == 95 && Level((9399, 94)) == 94, "at 94%: exactly 9,400 passes finish, one fewer does not");
	Check(Level((1000, 95), (100, 10)) == 19, "passes add over what is there: 1,000 at 95% + 100 at 10% = 19.5 levels → 19");
	Check(Level((200, 3)) == 63, "200 passes at 3% = 2/3 → 63.33 levels → 63");
	Check(Level((300, 3)) == 95, "300 passes at 3% = exactly 1 → 95 (a float sum would read 0.999… and stop at 94)");
	Check(Level((9400, 95), (100, 94)) == 95, "9,400 at 95% + 100 at 94% = 8931/8930, past 1 → 95");

	// MaxLevel means finished, and only that: the level reads the verdict's own exact fraction.
	var notQuite = With((100, 2), (100, 3), (100, 7), (100, 43));   // 1/2 + 1/3 + 1/7 + 1/43 = 1805/1806
	Check(SigilRules.SetLevel(notQuite) == 94, "1/2 + 1/3 + 1/7 + 1/43 = 1805/1806, short of 1 → 94, never 95");
	Check(SigilRules.SetLevelAfterPass(With((99, 95)), 95) == 1 && SigilRules.SetLevelAfterPass(With((98, 95)), 95) == 0
		&& SigilRules.SetLevelAfterPass(With(), 1) == 0 && SigilRules.SetLevelAfterPass(With((1, 1)), 1) == 1,
		"the preview equals the level after one more pass");

	Check(SigilRules.PassesToNextLevel(With(), 95) == 100 && SigilRules.PassesToNextLevel(With((60, 95)), 95) == 40
		&& SigilRules.PassesToNextLevel(With(), 1) == 2 && SigilRules.PassesToNextLevel(With((1, 1)), 1) == 1
		&& SigilRules.PassesToNextLevel(With((100, 95)), 1) == 2 && SigilRules.PassesToNextLevel(new SigilSetProgress { Finished = true }, 50) == 0,
		"passes to the next level: 100 · 40 at 95%; 2 · 1 at 1%; 2 at 1% from level 1; 0 when finished");
	var rngLevels = new Random(11);
	bool nextExact = true;
	int[] chances = { 95, 94, 80, 50, 33, 20, 9, 7, 3, 2, 1 };
	for (int i = 0; i < 2000; i++)
	{
		var set = With((rngLevels.Next(200), chances[rngLevels.Next(chances.Length)]), (rngLevels.Next(200), chances[rngLevels.Next(chances.Length)]));
		int level = SigilRules.SetLevel(set), chance = chances[rngLevels.Next(chances.Length)];
		if (level == SigilRules.MaxLevel) continue;
		int k = SigilRules.PassesToNextLevel(set, chance);
		var before = With(); foreach (var (c, p) in set.PassesByChance) before.PassesByChance[c] = p;
		before.PassesByChance[chance] = before.PassesByChance.GetValueOrDefault(chance) + k - 1;
		var after = With(); foreach (var (c, p) in before.PassesByChance) after.PassesByChance[c] = p;
		after.PassesByChance[chance]++;
		nextExact &= k >= 1 && SigilRules.SetLevel(before) == level && SigilRules.SetLevel(after) > level;
	}
	Check(nextExact, "over 2,000 random sets: exactly that many passes reach a higher level, one fewer does not");

	// D-GN.27: a sigil's level is its lowest set's; a combined figure's is the lower sigil's. 100 passes at 95% are a level.
	SigilProgressState AtLevels(int sigil, params int[] levels)
	{
		var st = SigilRules.NewProgress(sigil);
		for (int i = 0; i < levels.Length; i++) st.Sets[i].PassesByChance[95] = levels[i] * 100;
		return st;
	}
	var a = AtLevels(0, 10, 12, 30); var b = AtLevels(1, 4, 40, 90);
	Check(SigilRules.SigilLevel(a) == 10 && SigilRules.SigilLevel(b) == 4, "a sigil's level is its lowest set's: 10-12-30 → 10, 4-40-90 → 4");
	Check(SigilRules.CombinedLevel(a, b) == 4 && SigilRules.CombinedLevel(b, a) == 4, "the developer's example: sigil A at 10 and sigil B at 4 combine at 4");
	Check(SigilRules.SigilLevel(SigilRules.NewProgress()) == 0, "a fresh sigil is level 0");

	// Over real play at mixed chances, to violet: levels never go down, MaxLevel ⇔ finished, every preview comes true, a
	// sigil's level is the lowest of its sets', and the round invariants hold after every pass.
	var rng = new Random(7);
	var st = SigilRules.NewProgress();
	var last = new int[3];
	bool monotone = true, top = true, previewTrue = true, roundOk = true, lowest = true;
	int passesSeen = 0;
	while (!st.IsComplete)
	{
		int chance = new[] { 95, 80, 50, 20, 9, 3 }[rng.Next(6)];
		for (int b2 = 0; b2 < 2000 && !st.IsComplete; b2++)
		{
			int setIndex = st.SetIndex;
			int predicted = SigilRules.SetLevelAfterPass(st.Sets[setIndex], chance);
			var res = engine.Play(1m, chance, rng.Next(2) == 0);
			var o = SigilRules.ApplyBet(st, res.Roll, res.IsWin, chance, 1m, DateTime.UnixEpoch);
			if ((o & SigilBetOutcome.SetPassCompleted) == 0 || (o & SigilBetOutcome.SigilCompleted) != 0)
				continue;
			passesSeen++;
			previewTrue &= SigilRules.SetLevel(st.Sets[setIndex]) == predicted;
			for (int i = 0; i < 3; i++)
			{
				int l = SigilRules.SetLevel(st.Sets[i]);
				monotone &= l >= last[i];
				top &= (l == SigilRules.MaxLevel) == st.Sets[i].Finished;
				last[i] = l;
			}
			lowest &= SigilRules.SigilLevel(st) == last.Min();
			var open = st.Sets.Where(x => !x.Finished).Select(x => x.PassesCompleted).ToList();
			roundOk &= open.Max() - open.Min() <= 1 && !SigilRules.PassedThisRound(st, st.SetIndex);
		}
	}
	Check(monotone && top && previewTrue && roundOk && lowest, string.Create(CultureInfo.InvariantCulture,
		$"over {passesSeen:N0} passes at mixed chances: levels never fall, {SigilRules.MaxLevel} ⇔ finished, every preview came true, the sigil's level is its lowest set's, rounds hold"));

	// What the DEBUG log prints since D-GN.23: a pass adds at most 0.95 of a level, so no pass jumps one and each set
	// reaches every level exactly once, whatever the chance.
	foreach (int chance in new[] { 95, 50, 1 })
	{
		var counted = SigilRules.NewProgress();
		long reached = 0;
		while (!counted.IsComplete)
		{
			var res = engine.Play(1m, chance, false);
			if ((SigilRules.ApplyBet(counted, res.Roll, res.IsWin, chance, 1m, DateTime.UnixEpoch) & SigilBetOutcome.LevelReached) != 0) reached++;
		}
		Check(reached == 3L * SigilRules.MaxLevel, string.Create(CultureInfo.InvariantCulture,
			$"LevelReached at {chance}%: {reached} times over {counted.Sets.Sum(s => s.PassesCompleted):N0} passes (3 sets × {SigilRules.MaxLevel})"));
	}
}

Console.WriteLine("\n10. Pr-12: owning, the slot and the switch (D-GN.24, D-GN.25, D-GN.26)");
{
	var t0 = new DateTime(2009, 3, 21, 0, 0, 0, DateTimeKind.Utc);
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	long tick = 0;
	DateTime Now() => t0.AddSeconds(100 * tick++);
	void PlayUntil(Func<bool> done)
	{
		while (!done())
		{
			var res = engine.Play(1m, 50, false);
			PlayerSigilProgress.RegisterPlayerBet(Bet(res.Roll, res.IsWin, 50, Now()));
		}
	}

	// An empty slot, and owning.
	PlayerSigilProgress.RegisterPlayerBet(Bet(10, true, 50, Now()));
	Check(PlayerSigilProgress.Equipped == null && PlayerSigilProgress.LastOutcome == SigilBetOutcome.None && PlayerSigilProgress.CopyRecentSteps(new SigilStep[1], 1) == 0,
		"a bet with an empty slot draws nothing");
	Check(PlayerSigilProgress.Acquire(0) && PlayerSigilProgress.Equipped == null, "acquiring does not fill the slot");
	string before = Json(PlayerSigilProgress.Find(0));
	Check(!PlayerSigilProgress.Acquire(0) && PlayerSigilProgress.Collection.Owned.Count == 1 && Json(PlayerSigilProgress.Find(0)) == before, "one of each: a second acquisition does nothing (D-GN.24)");
	Check(!PlayerSigilProgress.Acquire(-1) && !PlayerSigilProgress.Acquire(PlanetarySquares.SigilCount), "there are no sigils outside the fourteen");
	Check(!PlayerSigilProgress.Equip(1) && PlayerSigilProgress.Equipped == null, "a sigil not owned cannot enter the slot");
	PlayerSigilProgress.Acquire(1);
	var changes = new List<SigilBetOutcome>();
	PlayerSigilProgress.Changed += changes.Add;
	Check(PlayerSigilProgress.Equip(0) && PlayerSigilProgress.Equipped.SigilIndex == 0 && PlayerSigilProgress.Collection.EquippedSigilIndex == 0 && changes.Count == 1,
		"equipping fills the slot and fires Changed once");

	// The switch, with a pass in progress: exactly Step, PassChance, that set's PassRestarts and the forgotten last bet.
	PlayUntil(() => PlayerSigilProgress.Equipped.Step > 0 && PlayerSigilProgress.Equipped.Sets.Sum(s => s.PassesCompleted) >= 3);
	var aquarius = PlayerSigilProgress.Equipped;
	int restartsBefore = aquarius.Sets[aquarius.SetIndex].PassRestarts, setBefore = aquarius.SetIndex;
	var expected = System.Text.Json.JsonSerializer.Deserialize<SigilProgressState>(Json(aquarius));
	expected.Step = 0;
	expected.PassChance = 0;
	expected.Sets[setBefore].PassRestarts++;
	expected.LastBetAtUtc = default;
	int vBefore = aquarius.V, streakBefore = aquarius.LossStreak;
	Check(!PlayerSigilProgress.Equip(0) && aquarius.Step > 0, "equipping the sigil already in the slot changes nothing: it is not a switch");
	changes.Clear();
	Check(PlayerSigilProgress.Equip(1) && Json(aquarius) == Json(expected), "the switch changes exactly: Step and PassChance to 0, that set's PassRestarts +1, and the last bet forgotten");
	Check(aquarius.V == vBefore && aquarius.LossStreak == streakBefore && aquarius.Sets[setBefore].PassRestarts == restartsBefore + 1 && aquarius.SetIndex == setBefore,
		"V, the loss streak and the set being drawn wait with it");
	Check(PlayerSigilProgress.CopyRecentSteps(new SigilStep[1], 1) == 0 && changes.Count == 1 && PlayerSigilProgress.LastOutcome == SigilBetOutcome.None,
		"the switch empties the V chain, fires Changed once and leaves no outcome behind");

	// While it is out: nothing of it moves, and the other sigil starts from its own first roll.
	string whileOut = Json(aquarius);
	var capricorn = PlayerSigilProgress.Equipped;
	PlayerSigilProgress.RegisterPlayerBet(Bet(47, true, 50, Now()));
	Check(capricorn.V == 2 && PlayerSigilProgress.CopyRecentSteps(new SigilStep[1], 1) == 1, "the sigil now in the slot starts from its own first roll");
	PlayUntil(() => capricorn.BetsPlaced >= 1000 && capricorn.Step == 0);
	Check(Json(aquarius) == whileOut, "a sigil out of the slot does not move");

	// The time trap: what it credits on returning.
	double secondsBefore = aquarius.Sets.Sum(s => s.GameSeconds);
	var startedBefore = aquarius.StartedAtUtc;
	long betsBefore = aquarius.BetsPlaced;
	restartsBefore = capricorn.Sets.Sum(s => s.PassRestarts);
	string capricornBefore = Json(capricorn);
	Check(PlayerSigilProgress.Equip(0) && capricorn.Sets.Sum(s => s.PassRestarts) == restartsBefore, "leaving with no pass in progress restarts nothing");
	PlayerSigilProgress.RegisterPlayerBet(Bet(50, false, 50, Now()));
	Check(aquarius.BetsPlaced == betsBefore + 1 && aquarius.Sets.Sum(s => s.GameSeconds) == secondsBefore && aquarius.StartedAtUtc == startedBefore,
		string.Create(CultureInfo.InvariantCulture, $"its first bet back credits none of the ≈ {capricorn.BetsPlaced * 100:N0} game-seconds it was out, and its start stays"));
	PlayerSigilProgress.RegisterPlayerBet(Bet(50, false, 50, Now()));
	Check(aquarius.Sets.Sum(s => s.GameSeconds) == secondsBefore + 100, "its second bet back credits the 100 game-seconds since its first");

	// D-GN.26: complete, it stays in the slot, and the slot can still be changed.
	var fired = SigilBetOutcome.None;
	void Collect(SigilBetOutcome o) => fired |= o;
	PlayerSigilProgress.Changed += Collect;
	PlayUntil(() => aquarius.IsComplete);
	PlayerSigilProgress.Changed -= Collect;
	Check((fired & SigilBetOutcome.SigilCompleted) != 0 && PlayerSigilProgress.Equipped == aquarius && aquarius.SigilIndex == 0 && SigilRules.SigilLevel(aquarius) == SigilRules.MaxLevel,
		string.Create(CultureInfo.InvariantCulture, $"a sigil completed at {aquarius.BetsPlaced:N0} bets stays in the slot, violet (D-GN.26)"));
	Check(aquarius.CompletedAtUtc != default && aquarius.Step == 0 && aquarius.PassChance == 0, "it carries its own completion");
	string violet = Json(aquarius);
	changes.Clear();
	var ring = new SigilStep[4];
	int ringBefore = PlayerSigilProgress.CopyRecentSteps(ring, 4);
	var lastBefore = ring[ringBefore - 1];
	PlayerSigilProgress.RegisterPlayerBet(Bet(10, true, 50, Now()));
	PlayerSigilProgress.CopyRecentSteps(ring, 4);
	Check(Json(aquarius) == violet && PlayerSigilProgress.LastOutcome == SigilBetOutcome.None && changes.Count == 0 && ring[ringBefore - 1] == lastBefore,
		"a bet on it changes nothing: no progress, no outcome, no event, nothing in the V chain");
	Check(PlayerSigilProgress.Equip(1) && aquarius.CompletedAtUtc != default && !PlayerSigilProgress.Equip(0) && PlayerSigilProgress.Equipped.SigilIndex == 1,
		"a complete sigil leaves and, since D-GN.28, does not return");
	PlayerSigilProgress.Changed -= changes.Add;

	// The pair, and the moment it may be shown (D-GN.24, D-GN.27).
	bool pairs = true;
	for (int k = 0; k < PlanetarySquares.SigilCount; k++)
		pairs &= SigilRules.PartnerOf(SigilRules.PartnerOf(k)) == k && SigilRules.PartnerOf(k) != k
			&& PlanetarySquares.PlanetOfSigil(k) == PlanetarySquares.PlanetOfSigil(SigilRules.PartnerOf(k));
	Check(pairs, "every sigil's partner is the other sigil of its planet");
	var one = SigilRules.NewProgress(0); var two = SigilRules.NewProgress(1);
	Check(!SigilRules.HasCombinedFigure(one, null) && !SigilRules.HasCombinedFigure(null, two) && !SigilRules.HasCombinedFigure(one, two),
		"no combined figure while a sigil is not owned, or both are at level 0");
	foreach (var set in one.Sets) set.PassesByChance[95] = 100;
	Check(!SigilRules.HasCombinedFigure(one, two), "nor with one at level 1 and the other at 0");
	foreach (var set in two.Sets) set.PassesByChance[95] = 100;
	Check(SigilRules.HasCombinedFigure(one, two) && SigilRules.CombinedLevel(one, two) == 1, "it exists when the lower of the two reaches level 1");
	PlayerSigilProgress.ResetToPreGenesisDefaults();
}

Console.WriteLine("\n11. The collection's text: the order and the descriptions give no pair away (D-GN.24)");
{
	var ordered = PlanetarySquares.SigilsByName.Select(PlanetarySquares.NameOfSigil).ToList();
	Check(string.Join(", ", ordered) == "Aquarius Sigil, Aries Sigil, Cancer Sigil, Capricorn Sigil, Eclipsed Cancer Sigil, Eclipsed Leo Sigil, Gemini Sigil, Leo Sigil, Libra Sigil, Pisces Sigil, Sagittarius Sigil, Scorpio Sigil, Taurus Sigil, Virgo Sigil",
		"fourteen items, alphabetical by displayed name");
	// Not asserted: that no pair sits side by side. The alphabet itself puts Pisces beside Sagittarius, and the rule is
	// alphabetical, not "scattered" (plan §10.9).

	// The test reads each DESCRIPTION, never a name: "Eclipsed Leo Sigil" holds "Leo Sigil" by D-GN.3, and that is fixed.
	// A sign's bare word covers both forms of its name, so no description may hold any of the twelve.
	var signs = Enumerable.Range(0, PlanetarySquares.SigilCount)
		.Select(i => PlanetarySquares.NameOfSigil(i).Replace("Eclipsed ", "").Replace(" Sigil", ""))
		.Distinct().ToList();
	string[] pairWords = { "pair", "partner", "combin", "counterpart", "twin", "other", "both", "together" };
	bool Reveals(string description) =>
		signs.Concat(pairWords).Any(word => description.Contains(word, StringComparison.OrdinalIgnoreCase));

	// Convention 16: a check that cannot fail proves nothing, so it must catch these first.
	Check(signs.Count == 12 && signs.Contains("Leo") && signs.Contains("Cancer"), "twelve sign words, Leo and Cancer among them");
	Check(Reveals("The Sun's square. Pairs with Leo.") && Reveals("Drawn with the Eclipsed Cancer Sigil") && Reveals("Aries, by day") && Reveals("The other half of a figure"),
		"the check flags a description that names a sign or speaks of a pair");

	bool anyReveals = false, allNameTheirPlanetAndSky = true;
	var descriptions = new List<string>();
	for (int i = 0; i < PlanetarySquares.SigilCount; i++)
	{
		string description = SigilRules.DescriptionOf(i);
		descriptions.Add(description);
		var square = PlanetarySquares.PlanetOfSigil(i);
		var kind = PlanetarySquares.KindOfSigil(i);
		string sky = square.IsEclipsed(kind) ? "in eclipse" : kind == SigilKind.Diurnal ? "by day" : "by night";
		anyReveals |= Reveals(description);
		allNameTheirPlanetAndSky &= description.Contains(square.Planet) && description.Contains(sky);
		Console.WriteLine($"        {PlanetarySquares.NameOfSigil(i),-22} {description}");
	}
	Check(!anyReveals, "no description names a sign, its partner included, or speaks of a pair");
	Check(allNameTheirPlanetAndSky, "every description names its planet and its sky, which is how the deduction is made");
	Check(descriptions.Distinct().Count() == PlanetarySquares.SigilCount, "no two descriptions are the same");
	Check(descriptions.Max(d => d.Length) <= 66, $"the longest fits the card's two reserved lines ({descriptions.Max(d => d.Length)} characters)");
}

// ─── Unit 10 (plan §8.2): every section below first proves its check can fail (Standing Convention 16). ───

// A pair at the given levels, owned, with the Saturn squares' set progress written directly (section 10's way: a real gate
// is about 8,500 bets each). 100 passes at 95% are one level.
void OwnPair(int planet, int firstLevel, int secondLevel)
{
	foreach (var (sigil, level) in new[] { (planet * 2, firstLevel), (planet * 2 + 1, secondLevel) })
	{
		PlayerSigilProgress.Acquire(sigil);
		foreach (var set in PlayerSigilProgress.Find(sigil).Sets)
			set.PassesByChance[95] = level * 100;
	}
}

// Plays real bets at a chance until the condition holds, stamping 100 game-seconds apart.
long Tick = 0;
var T0 = new DateTime(2009, 3, 21, 0, 0, 0, DateTimeKind.Utc);
DateTime NextBetTime() => T0.AddSeconds(100 * Tick++);
void PlayChance(int chance, Func<bool> done)
{
	long guard = 0;
	while (!done())
	{
		if (++guard > 50_000_000)
			throw new InvalidOperationException("PlayChance: the condition never held; is the record in the slot taking no progress?");
		var res = engine.Play(1m, chance, false);
		PlayerSigilProgress.RegisterPlayerBet(Bet(res.Roll, res.IsWin, chance, NextBetTime()));
	}
}

Console.WriteLine("\n12. Pr-15: a complete sigil cannot be played (D-GN.28)");
{
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	PlayerSigilProgress.Acquire(0);
	PlayerSigilProgress.Acquire(1);
	PlayerSigilProgress.Equip(0);
	foreach (var set in PlayerSigilProgress.Find(0).Sets) { set.PassesByChance[95] = 9499; set.PassesCompleted = 9499; }
	var aquarius = PlayerSigilProgress.Find(0);
	PlayChance(95, () => aquarius.IsComplete);
	Check(PlayerSigilProgress.Equipped == aquarius, "a sigil completing in the slot stays there");
	Check(PlayerSigilProgress.Equip(1) && PlayerSigilProgress.Equipped.SigilIndex == 1, "it can leave");

	// The refusal changes nothing at all: the whole collection as text, and the slot's cached record.
	string before = Json(PlayerSigilProgress.Collection);
	var ringBuffer = new SigilStep[1];
	int changed = 0;
	void Count(SigilBetOutcome _) => changed++;
	PlayerSigilProgress.Changed += Count;
	bool refused = !PlayerSigilProgress.Equip(0);
	PlayerSigilProgress.Changed -= Count;
	Check(refused && Json(PlayerSigilProgress.Collection) == before && PlayerSigilProgress.Equipped.SigilIndex == 1 && changed == 0, "once out it does not return: Equip returns false, changes nothing and fires nothing");

	// Convention 16: this JSON comparison must notice a one-field change.
	var altered = PlayerSigilProgress.CaptureForCheckpoint();
	altered.EquippedSigilIndex = 0;
	Check(Json(altered) != before, "the comparison above notices the slot moving (it can fail)");

	// A restore is not the player's action: a sigil complete in the slot comes back in the slot.
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	PlayerSigilProgress.Acquire(0);
	PlayerSigilProgress.Equip(0);
	foreach (var set in PlayerSigilProgress.Find(0).Sets) { set.PassesByChance[95] = 9499; set.PassesCompleted = 9499; }
	aquarius = PlayerSigilProgress.Find(0);
	PlayChance(95, () => aquarius.IsComplete);
	var snapshot = PlayerSigilProgress.CaptureForCheckpoint();
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	PlayerSigilProgress.RestoreFromCheckpoint(snapshot);
	Check(PlayerSigilProgress.Equipped?.SigilIndex == 0 && PlayerSigilProgress.Equipped.IsComplete, "RestoreFromCheckpoint still restores a complete sigil that was in the slot");
}

Console.WriteLine("\n13. Pr-16: the notice fires once per planet, at level 1, and lasts 600 game-seconds (D-GN.29)");
{
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	var fired = new List<(int Planet, DateTime At)>();
	void OnFired(int planet) => fired.Add((planet, PlayerSigilProgress.LastDrawn.LastBetAtUtc));
	PlayerSigilProgress.NoticeFired += OnFired;

	// Owning both, with the partner at level 5, fires nothing: a new sigil is at level 0.
	OwnPair(0, 5, 0);
	Check(fired.Count == 0 && PlayerSigilProgress.NoticePlanetAt(T0) == -1, "owning both fires nothing, even with one far along");

	// Convention 16: the edge must be able to fire at all, and on the bet that lifts the lower, by watching one reach it.
	var capricorn = PlayerSigilProgress.Find(1);
	foreach (var set in capricorn.Sets) { set.PassesByChance[95] = 99; set.PassesCompleted = 99; }
	PlayerSigilProgress.Equip(1);
	int betsBeforeEdge = 0;
	bool lowerWasZero = true;
	long seen = 0;
	while (fired.Count == 0 && seen < 100_000)
	{
		lowerWasZero &= SigilRules.CombinedLevel(PlayerSigilProgress.Find(0), capricorn) == 0;
		var res = engine.Play(1m, 95, false);
		PlayerSigilProgress.RegisterPlayerBet(Bet(res.Roll, res.IsWin, 95, NextBetTime()));
		betsBeforeEdge++;
		seen++;
	}
	Check(fired.Count == 1 && fired[0].Planet == 0, $"it fired, for Saturn, on bet {betsBeforeEdge} of this sigil's last three passes");
	Check(lowerWasZero && SigilRules.CombinedLevel(PlayerSigilProgress.Find(0), capricorn) == 1, "on exactly the bet that took the lower from 0 to 1");

	// The end: live at +600, gone just past it.
	DateTime firedAt = fired[0].At;
	Check(PlayerSigilProgress.NoticePlanetAt(firedAt.AddSeconds(SigilRules.NoticeGameSeconds)) == 0, "still live at exactly the bet's timestamp + 600 s");
	Check(PlayerSigilProgress.NoticePlanetAt(firedAt.AddSeconds(SigilRules.NoticeGameSeconds + 1)) == -1, "gone once the clock passes it");

	// Never again for this planet, whichever sigil moves.
	foreach (var set in capricorn.Sets) { set.PassesByChance[95] = 199; set.PassesCompleted = 199; }
	PlayChance(95, () => SigilRules.SigilLevel(capricorn) == 2);
	PlayerSigilProgress.Equip(0);
	var aquarius = PlayerSigilProgress.Find(0);
	foreach (var set in aquarius.Sets) { set.PassesByChance[95] = 599; set.PassesCompleted = 599; }
	PlayChance(95, () => SigilRules.SigilLevel(aquarius) == 6);
	Check(fired.Count == 1, "no second firing as either sigil rises further");

	// One sigil alone reaching level 1 is not the pair's edge, and neither is its partner reaching it first.
	fired.Clear();
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	PlayerSigilProgress.Acquire(2);
	PlayerSigilProgress.Equip(2);
	var sagittarius = PlayerSigilProgress.Find(2);
	foreach (var set in sagittarius.Sets) { set.PassesByChance[95] = 99; set.PassesCompleted = 99; }
	PlayChance(95, () => SigilRules.SigilLevel(sagittarius) == 1);
	Check(fired.Count == 0, "a sigil reaching level 1 with its partner not owned fires nothing");
	PlayerSigilProgress.Acquire(3);
	Check(fired.Count == 0, "and acquiring the partner afterwards fires nothing");
	PlayerSigilProgress.Equip(3);
	var pisces = PlayerSigilProgress.Find(3);
	foreach (var set in pisces.Sets) { set.PassesByChance[95] = 99; set.PassesCompleted = 99; }
	PlayChance(95, () => SigilRules.SigilLevel(pisces) == 1);
	Check(fired.Count == 1 && fired[0].Planet == 1, "the partner then reaching level 1 fires it, for Jupiter");

	// A restart reverts it: no notice restored, and the bet that raises the level fires it again.
	var older = PlayerSigilProgress.CaptureForCheckpoint();
	PlayerSigilProgress.RestoreFromCheckpoint(older);
	Check(PlayerSigilProgress.NoticePlanetAt(fired[0].At) == -1, "a notice live at a restore is not restored");
	PlayerSigilProgress.NoticeFired -= OnFired;
	PlayerSigilProgress.ResetToPreGenesisDefaults();
}

Console.WriteLine("\n14. Pr-17, Pr-20: the gate, the price and the alternation (D-GN.30, D-GN.33)");
{
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	Check(PlayerSigilProgress.NextOpenLevel(0) == 0 && !PlayerSigilProgress.StartActivation(0), "nothing owned: the gate is shut");
	PlayerSigilProgress.Acquire(0);
	PlayerSigilProgress.Acquire(1);
	Check(!PlayerSigilProgress.StartActivation(0), "both owned at level 0: still shut");
	OwnPair(0, 10, 4);
	Check(PlayerSigilProgress.NextOpenLevel(0) == 1, "the developer's example: sigils at 10 and 4 open level 1");
	OwnPair(1, 3, 3);   // another planet's, to see it does not move
	Check(!PlayerSigilProgress.StartActivation(-1) && !PlayerSigilProgress.StartActivation(PlanetarySquares.All.Count), "no planet outside the seven");

	string sigilsBefore = Json(PlayerSigilProgress.Find(0)) + Json(PlayerSigilProgress.Find(1));
	Check(PlayerSigilProgress.StartActivation(0), "starting");
	var saturnRecord = PlayerSigilProgress.Equipped;
	Check(saturnRecord.IsPlanetary && saturnRecord.Sets.Count == 6 && saturnRecord.LevelLimit == 4 && saturnRecord.V == 0, "its own record: both sigils' 6 sets, the limit the combined level, V fresh (OQ-GN.3)");
	Check(!PlayerSigilProgress.StartActivation(0), "it cannot start again while it is in the slot");

	// The order of the targets, read off the rule: diurnal 1-2-3, 4-5-6, 7-8-9, then nocturnal 1-4-7, 2-5-8, 3-6-9, then again.
	var expectedTargets = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 1, 4, 7, 2, 5, 8, 3, 6, 9 };
	var seenTargets = new List<int>();
	var seenSets = new List<int>();
	var levelAtPass = new List<(int Pass, int Level)>();
	int passes = 0;
	bool creditOnlyAtEnd = true;
	while (saturnRecord.ActivatedLevel < 4)
	{
		int target = SigilRules.CurrentTarget(saturnRecord);
		int setBefore = saturnRecord.SetIndex;
		var res = engine.Play(1m, 95, false);
		int levelBefore = saturnRecord.ActivatedLevel;
		PlayerSigilProgress.RegisterPlayerBet(Bet(res.Roll, res.IsWin, 95, NextBetTime()));
		var o = PlayerSigilProgress.LastOutcome;
		if ((o & SigilBetOutcome.Hit) != 0)
			seenTargets.Add(target);
		if ((o & SigilBetOutcome.SetPassCompleted) != 0)
		{
			passes++;
			seenSets.Add(setBefore);
			if (saturnRecord.ActivatedLevel != levelBefore) levelAtPass.Add((passes, saturnRecord.ActivatedLevel));
		}
		creditOnlyAtEnd &= saturnRecord.ActivatedLevel == levelBefore || passes % 6 == 0;
	}

	// Convention 16: the expected orders are compared as sequences, and a swapped order must read as different.
	var swapped = new[] { 1, 4, 7, 2, 5, 8, 3, 6, 9, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
	bool OrderOk(IEnumerable<int> seen2, int[] expected) => seen2.Take(36).SequenceEqual(expected.Concat(expected));
	Check(OrderOk(seenTargets, expectedTargets) && !OrderOk(seenTargets, swapped),
		"the targets run diurnal 1-2-3 4-5-6 7-8-9, then nocturnal 1-4-7 2-5-8 3-6-9, again; the same check rejects the opposite order");
	Check(seenSets.Select((s, i) => s == i % 6).All(x => x), $"over {seenSets.Count} set passes every one follows set 0 … 5 in order, so neither sigil repeats");
	bool notAlternating = new[] { 0, 1, 2, 0, 1, 2 }.Select((s, i) => s == i % 6).All(x => x);
	Check(!notAlternating, "a run that repeats the diurnal sigil fails that check (it can fail)");

	// Pr-17: exactly k whole passes per level: 6, 12, 18, 24 set passes, 60 in all for the four levels.
	Check(levelAtPass.SequenceEqual(new[] { (6, 1), (18, 2), (36, 3), (60, 4) }),
		"at 95%: level 1 after 1 whole pass (6 set passes), 2 after 2 more, 3 after 3 more, 4 after 4 more: " + string.Join(" ", levelAtPass));
	Check(creditOnlyAtEnd, "a level moves only on a bet ending a whole pass (Pr-20)");
	Check(saturnRecord.IsComplete && (PlayerSigilProgress.LastOutcome & SigilBetOutcome.SigilCompleted) != 0, "reaching the limit is its completion: it continued through 1, 2, 3 and stopped at 4 (D-GN.33)");
	Check(Json(PlayerSigilProgress.Find(0)) + Json(PlayerSigilProgress.Find(1)) == sigilsBefore, "and neither sigil's own record moved (Pr-18's second half)");
	Check(PlayerSigilProgress.FindPlanetary(1) == null, "no other activated level moved: Jupiter's was never begun");
	Check(PlayerSigilProgress.NextOpenLevel(0) == 0 && !PlayerSigilProgress.StartActivation(0), "the gate is now shut at 4 of 4");

	// A complete planetary record takes no progress, whatever is bet.
	string parked = Json(saturnRecord);
	PlayerSigilProgress.RegisterPlayerBet(Bet(1, true, 95, NextBetTime()));
	Check(Json(saturnRecord) == parked && PlayerSigilProgress.LastOutcome == SigilBetOutcome.None, "at the limit a bet changes nothing");

	// Raising the sigils reopens the gate, and the record resumes at the next level with its passes kept.
	PlayerSigilProgress.Equip(1);
	foreach (var set in PlayerSigilProgress.Find(1).Sets) set.PassesByChance[95] = 500;
	Check(PlayerSigilProgress.NextOpenLevel(0) == 5 && PlayerSigilProgress.StartActivation(0) && !saturnRecord.IsComplete && saturnRecord.ActivatedLevel == 4 && saturnRecord.LevelLimit == 5,
		"the lower rising to 5 opens level 5: same record, level kept, no longer complete");

	// Leaving costs only the pass in progress.
	PlayChance(95, () => saturnRecord.Step > 0 && saturnRecord.SetIndex == 2);
	int setPassesBefore = saturnRecord.Sets.Sum(s => s.PassesCompleted);
	int restartsBefore = saturnRecord.Sets[2].PassRestarts;
	var expected = System.Text.Json.JsonSerializer.Deserialize<SigilProgressState>(Json(saturnRecord));
	expected.Step = 0;
	expected.PassChance = 0;
	expected.Sets[2].PassRestarts = restartsBefore + 1;
	expected.LastBetAtUtc = default;
	Check(PlayerSigilProgress.Equip(1) && Json(saturnRecord) == Json(expected) && saturnRecord.Sets.Sum(s => s.PassesCompleted) == setPassesBefore,
		"leaving loses the pass in progress and nothing else");
	Check(PlayerSigilProgress.StartActivation(0) && saturnRecord.SetIndex == 2 && saturnRecord.Step == 0, "and it resumes at the same set of the same whole pass");

	// Pr-20: at 1%, one whole pass on Saturn is worth 95, which pays level 13 (91) and not 14 (105), and no level before its end.
	void Fractional(int limit, out int levelAfterOnePass, out SigilProgressState record)
	{
		PlayerSigilProgress.ResetToPreGenesisDefaults();
		OwnPair(0, limit, limit);
		PlayerSigilProgress.StartActivation(0);
		record = PlayerSigilProgress.Equipped;
		var r = record;
		int passes1 = 0;
		bool midPass = false;
		while (passes1 < 6)
		{
			var res = engine.Play(1m, 1, false);
			PlayerSigilProgress.RegisterPlayerBet(Bet(res.Roll, res.IsWin, 1, NextBetTime()));
			if ((PlayerSigilProgress.LastOutcome & SigilBetOutcome.SetPassCompleted) != 0)
			{
				passes1++;
				midPass |= passes1 < 6 && r.ActivatedLevel != 0;
			}
		}
		Check(!midPass, $"at 1%, gate at {limit}: nothing is credited during the first whole pass, though one set pass is worth 95/6");
		levelAfterOnePass = r.ActivatedLevel;
	}
	Fractional(95, out int reachedOpen, out _);
	Check(reachedOpen == 13, $"at 1% with the gate open, one whole pass reaches level 13 (91 ≤ 95 < 105): {reachedOpen}");
	Fractional(4, out int reachedShut, out var heldBack);
	Check(reachedShut == 4 && heldBack.IsComplete && SigilRules.WholePassesPaid(heldBack) == 95d, $"with the gate at 4 it stops at 4, and all 95 stay paid for: {SigilRules.WholePassesPaid(heldBack)}");
	PlayerSigilProgress.Equip(1);
	foreach (var sigil in new[] { 0, 1 })
		foreach (var set in PlayerSigilProgress.Find(sigil).Sets) set.PassesByChance[95] = 9000;
	PlayerSigilProgress.StartActivation(0);
	PlayChance(95, () => heldBack.ActivatedLevel > 4);
	Check(heldBack.ActivatedLevel == 13 && heldBack.Sets.Sum(s => s.PassesCompleted) == 12, $"what the gate held back is credited at the end of the next whole pass, not lost: level {heldBack.ActivatedLevel}");

	// Exact fractions: 3% and 7% in one whole pass, compared in integers. Three sets at 3% and three at 7% pay exactly
	// 3 × 95/(6×3) + 3 × 95/(6×7) = 95/6 + 95/14 = 22.619…, so level 6 (21) and not 7 (28).
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	OwnPair(0, 95, 95);
	PlayerSigilProgress.StartActivation(0);
	var mixed = PlayerSigilProgress.Equipped;
	for (int i = 0; i < 6; i++)
	{
		int chance = i < 3 ? 3 : 7;
		int before = mixed.Sets.Sum(s => s.PassesCompleted);
		PlayChance(chance, () => mixed.Sets.Sum(s => s.PassesCompleted) > before);
	}
	Check(mixed.ActivatedLevel == 6, $"mixed chances in one whole pass, 3% then 7%, pay 22.62 → level 6: {mixed.ActivatedLevel}");

	// Convention 9's trap: 95 passes at 95% on each of a whole pass's sets sum to exactly T(13) = 91? A value that lands
	// exactly on a target must count. 91 whole passes at 95% pay exactly T(13).
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	OwnPair(0, 95, 95);
	PlayerSigilProgress.StartActivation(0);
	var exactly = PlayerSigilProgress.Equipped;
	foreach (var set in exactly.Sets) { set.PassesByChance[95] = 90; set.PassesCompleted = 90; }
	PlayChance(95, () => exactly.Sets[5].PassesCompleted == 91);
	Check(exactly.ActivatedLevel == 13, $"a value landing exactly on a target counts: 91 whole passes at 95% are level 13: {exactly.ActivatedLevel}");
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	OwnPair(0, 95, 95);
	PlayerSigilProgress.StartActivation(0);
	var shortOf = PlayerSigilProgress.Equipped;
	foreach (var set in shortOf.Sets) { set.PassesByChance[95] = 89; set.PassesCompleted = 89; }
	PlayChance(95, () => shortOf.Sets[5].PassesCompleted == 90);
	Check(shortOf.ActivatedLevel == 12, $"and one short does not: 90 whole passes are level 12: {shortOf.ActivatedLevel}");

	// The whole of level 95: a record reaching MaxLevel stays complete and the gate never opens past it.
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	OwnPair(0, 95, 95);
	PlayerSigilProgress.StartActivation(0);
	var top = PlayerSigilProgress.Equipped;
	foreach (var set in top.Sets) { set.PassesByChance[95] = 4559; set.PassesCompleted = 4559; }
	PlayChance(95, () => top.IsComplete);
	Check(top.ActivatedLevel == SigilRules.MaxLevel && top.Sets.All(s => s.PassesCompleted == 4560) && PlayerSigilProgress.NextOpenLevel(0) == 0,
		"4,560 whole passes at 95% reach level 95 (OQ-GN.1, reading A: 1 + 2 + … + 95), and nothing opens past it");
	PlayerSigilProgress.ResetToPreGenesisDefaults();
}

Console.WriteLine("\n15. Pr-18: the mean cost, and what activating does not touch (OQ-GN.3)");
{
	foreach (int planet in new[] { 0, 1, 2 })
	{
		var square = PlanetarySquares.All[planet];
		const int WholePasses = 60;
		var costs = new List<double>();
		const int CostRuns = 40;
		bool neverMoved = true;
		for (int run = 0; run < CostRuns; run++)
		{
			PlayerSigilProgress.ResetToPreGenesisDefaults();
			OwnPair(planet, 95, 95);
			string before = Json(PlayerSigilProgress.Find(planet * 2)) + Json(PlayerSigilProgress.Find(planet * 2 + 1));
			PlayerSigilProgress.StartActivation(planet);
			var record = PlayerSigilProgress.Equipped;
			PlayChance(95, () => record.Sets[^1].PassesCompleted == WholePasses);
			costs.Add(record.BetsPlaced);
			neverMoved &= Json(PlayerSigilProgress.Find(planet * 2)) + Json(PlayerSigilProgress.Find(planet * 2 + 1)) == before;
		}

		// Against the exact chain, not Predicted: at this precision Predicted itself misses, by -2.2% on Jupiter (plan §10.10).
		// It treats targets as independent, but a hit leaves V known: 10 leaves V on 1, which is just what 11 asks for.
		var both = ExactChain.Targets(square, SigilKind.Diurnal).Concat(ExactChain.Targets(square, SigilKind.Nocturnal)).ToList();
		double predicted = WholePasses * ExactChain.CycleCost(both, 95);
		double twoApart = WholePasses * (ExactChain.CycleCost(ExactChain.Targets(square, SigilKind.Diurnal), 95) + ExactChain.CycleCost(ExactChain.Targets(square, SigilKind.Nocturnal), 95));
		double mean = costs.Average();
		// A pass through both sigils is 2 × Targets(n) hits; each is geometric, sd ≈ mean, so the same sum as RelativeSd.
		var (k1, k2) = Targets(square.N);
		double m1 = 900.0 / 95, m2 = 8100.0 / 95;
		double sd = Math.Sqrt(WholePasses * 2 * (k1 * m1 * m1 + k2 * m2 * m2) / CostRuns);
		double sigmas = Math.Abs(mean - predicted) / sd;
		Check(sigmas <= 4, string.Create(CultureInfo.InvariantCulture,
			$"{square.Planet}: {WholePasses} whole passes at 95% cost {mean:N0} bets on average of {CostRuns} runs, the exact chain says {predicted:N0} ({sigmas:F2}σ, bar 4σ); Predicted says {WholePasses * 2 * Predicted(square.N) / 95:N0}"));
		Check(Math.Abs(predicted - twoApart) / predicted < 0.001, string.Create(CultureInfo.InvariantCulture,
			$"  an activation costs what its two sigils cost apart: {predicted:N0} against {twoApart:N0} in the same chain"));
		// Convention 16: the same bar must reject a mean 25% off, and the sigils' JSON must have been able to differ.
		Check(Math.Abs(predicted * 1.25 - predicted) / sd > 4, string.Create(CultureInfo.InvariantCulture, $"  the bar rejects a cost 25% off ({Math.Abs(predicted * 0.25) / sd:F1}σ)"));
		Check(neverMoved, "  neither sigil's own passes, V, level or figures moved");
	}
	Check(Math.Round(2 * Predicted(3) / 95, 1) == 170.5, "one pass through both on Saturn is the 170.5 bets §8.2 states, rounded: 2 × Predicted(3) / 95");
	foreach (var q in PlanetarySquares.All)
	{
		double exactBoth = ExactChain.CycleCost(ExactChain.Targets(q, SigilKind.Diurnal).Concat(ExactChain.Targets(q, SigilKind.Nocturnal)).ToList(), 95);
		double modelBoth = 2 * Predicted(q.N) / 95;
		Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
			$"        {q.Planet,-8} one whole pass at 95%: exact {exactBoth:N1} bets, Predicted {modelBoth:N1}, ratio {exactBoth / modelBoth:F4}; all 95 levels: exact {exactBoth * 4560:N0}, Predicted {modelBoth * 4560:N0}"));
	}

	PlayerSigilProgress.ResetToPreGenesisDefaults();
	OwnPair(0, 95, 95);
	string withoutBets = Json(PlayerSigilProgress.Find(0));
	PlayerSigilProgress.Find(0).V = 3;
	Check(withoutBets != Json(PlayerSigilProgress.Find(0)), "a sigil's V moving would have shown in that comparison (it can fail)");
	PlayerSigilProgress.ResetToPreGenesisDefaults();
}

Console.WriteLine("\n16. Pr-19: the checkpoint carries activations, and an ordinary bet still costs as Pr-14 says");
{
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	OwnPair(0, 8, 8);
	PlayerSigilProgress.StartActivation(0);
	var record = PlayerSigilProgress.Equipped;
	PlayChance(95, () => record.ActivatedLevel >= 2 && record.Step > 0);
	var snapshot = PlayerSigilProgress.CaptureForCheckpoint();
	string atBlock = SigilRules.Describe(snapshot);
	string jsonAtBlock = Json(snapshot);

	// After the block: an activated level, and a switch.
	var bets = record.BetsPlaced;
	PlayChance(95, () => record.ActivatedLevel >= 3);
	PlayerSigilProgress.Acquire(4);
	PlayerSigilProgress.Equip(4);
	Check(SigilRules.Describe(PlayerSigilProgress.Collection) != atBlock, "the state after the block differs, as text (the comparison can fail)");

	PlayerSigilProgress.RestoreFromCheckpoint(snapshot);
	Check(SigilRules.Describe(PlayerSigilProgress.Collection) == atBlock, "a restore reproduces the checkpoint as text, activated levels and the activation in progress included");
	Check(Json(PlayerSigilProgress.Collection) == jsonAtBlock, "and exactly as JSON");
	Check(atBlock.Contains("slot=Saturn Sigil") && atBlock.Contains("activated=2") && atBlock.Contains("limit=8") && atBlock.Contains("planetary=1"), "the text names the activation and its level: " + atBlock[..atBlock.IndexOf(" | ", StringComparison.Ordinal)]);
	var restored = PlayerSigilProgress.Equipped;
	Check(restored.IsPlanetary && restored == PlayerSigilProgress.FindPlanetary(0) && restored != snapshot.Planetary[0], "the cached slot is the restored list's own record, not the snapshot's");
	PlayerSigilProgress.RegisterPlayerBet(Bet(10, true, 95, NextBetTime()));
	Check(restored.BetsPlaced == bets + 1, "and the activation carries on drawing from there");
	Check(Json(snapshot) == jsonAtBlock, "drawing on after a restore does not touch the snapshot");

	// An older checkpoint carries no activation fields: nothing activated (Convention 5).
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	PlayerSigilProgress.Acquire(0);
	PlayerSigilProgress.Equip(0);
	string olderJson = Json(PlayerSigilProgress.Collection)
		.Replace(",\"Planetary\":[],\"EquippedPlanetIndex\":null", "")
		.Replace("\"IsPlanetary\":false,\"ActivatedLevel\":0,\"LevelLimit\":0,", "");
	Check(!olderJson.Contains("Planetary") && !olderJson.Contains("ActivatedLevel"), "a checkpoint written before the activation carries no such fields");
	PlayerSigilProgress.RestoreFromCheckpoint(System.Text.Json.JsonSerializer.Deserialize<SigilCollectionState>(olderJson));
	Check(PlayerSigilProgress.Collection.Planetary.Count == 0 && PlayerSigilProgress.Collection.EquippedPlanetIndex == null && PlayerSigilProgress.Equipped?.SigilIndex == 0, "and restores nothing activated, its slot intact");

	// The cost: section 8's measurement, on the largest sigil, within this run. Reaches for the fields an ordinary bet reads.
	const int Bets = 3_000_000;
	var rng = new Random(5);
	var t0 = new DateTime(2009, 3, 21, 0, 0, 0, DateTimeKind.Utc);
	var events = new Scripts.Finance.BetTransactionEvent[65536];
	for (int i = 0; i < events.Length; i++) { int roll = rng.Next(0, 100); events[i] = Bet(roll, roll < 50, 50, t0.AddSeconds(i)); }

	PlayerSigilProgress.ResetToPreGenesisDefaults();
	for (int k = 0; k < PlanetarySquares.SigilCount; k++) PlayerSigilProgress.Acquire(k);
	PlayerSigilProgress.Equip(PlanetarySquares.SigilCount - 1);
	for (int i = 0; i < 100_000; i++) PlayerSigilProgress.RegisterPlayerBet(events[i & 0xFFFF]);
	long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
	var timer = Stopwatch.StartNew();
	for (int i = 0; i < Bets; i++) PlayerSigilProgress.RegisterPlayerBet(events[i & 0xFFFF]);
	timer.Stop();
	double ns = timer.Elapsed.TotalMilliseconds * 1e6 / Bets;
	double bytes = (double)(GC.GetAllocatedBytesForCurrentThread() - bytesBefore) / Bets;
	Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  a sigil's ordinary bet, no activation running: {ns:F1} ns and {bytes:F2} bytes"));
	Check(bytes < 8, "it allocates nothing beyond what completed passes do");

	// The same on an activation, for comparison within this run.
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	OwnPair(6, 95, 95);
	PlayerSigilProgress.StartActivation(6);
	var moon = PlayerSigilProgress.Equipped;
	for (int i = 0; i < 100_000; i++) PlayerSigilProgress.RegisterPlayerBet(events[i & 0xFFFF]);
	bytesBefore = GC.GetAllocatedBytesForCurrentThread();
	timer.Restart();
	for (int i = 0; i < Bets; i++) PlayerSigilProgress.RegisterPlayerBet(events[i & 0xFFFF]);
	timer.Stop();
	double nsActivation = timer.Elapsed.TotalMilliseconds * 1e6 / Bets;
	bytes = (double)(GC.GetAllocatedBytesForCurrentThread() - bytesBefore) / Bets;
	Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  an activation's ordinary bet, the Moon at 50%: {nsActivation:F1} ns and {bytes:F2} bytes"));
	Check(bytes < 8 && !moon.IsComplete, "and the activation's path allocates nothing either, and was still drawing");

	// A slot holding a finished sigil, on every later bet: an early return, with nothing searched.
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	PlayerSigilProgress.Acquire(0);
	PlayerSigilProgress.Equip(0);
	foreach (var set in PlayerSigilProgress.Find(0).Sets) { set.PassesByChance[95] = 9499; set.PassesCompleted = 9499; }
	var finished = PlayerSigilProgress.Find(0);
	PlayChance(95, () => finished.IsComplete);
	bytesBefore = GC.GetAllocatedBytesForCurrentThread();
	for (int i = 0; i < Bets; i++) { PlayerSigilProgress.RegisterPlayerBet(events[i & 0xFFFF]); _ = PlayerSigilProgress.MustChooseSigil; }
	Check(GC.GetAllocatedBytesForCurrentThread() == bytesBefore, "with a finished sigil in the slot and nothing else to play, a bet allocates nothing: the clock can run on at full rate");
	PlayerSigilProgress.ResetToPreGenesisDefaults();
}

Console.WriteLine("\n17. Pr-21: what a completion does, and the level stop (D-GN.31, D-GN.32)");
{
	void Finish(SigilProgressState sigil)
	{
		foreach (var set in sigil.Sets) { set.PassesByChance[95] = 9499; set.PassesCompleted = 9499; }
		PlayChance(95, () => sigil.IsComplete);
	}

	// Stop mode, something else to play.
	PlayerSigilProgress.SetAutoSwitchOnCompletion(false);
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	PlayerSigilProgress.Acquire(0);
	PlayerSigilProgress.Acquire(2);
	PlayerSigilProgress.Equip(0);
	var aquarius = PlayerSigilProgress.Find(0);
	Check(!PlayerSigilProgress.MustChooseSigil && !PlayerSigilProgress.DrawsNothing, "drawing: nothing to choose, no warning");
	Check(PlayerSigilProgress.StopFor(SigilBetOutcome.None, true, true) == SigilStop.None, "and no stop on a bet that did nothing, with both switches on");
	Finish(aquarius);
	Check(PlayerSigilProgress.MustChooseSigil && PlayerSigilProgress.DrawsNothing && PlayerSigilProgress.Equipped == aquarius, "Stop mode: the completing bet leaves it in the slot, and the player must choose");
	Check(PlayerSigilProgress.StopFor(PlayerSigilProgress.LastOutcome, false, false) == SigilStop.SigilCompleted, "the stop fires with both switches off: it is not one of them");
	Check(PlayerSigilProgress.Equip(2) && !PlayerSigilProgress.MustChooseSigil && !PlayerSigilProgress.DrawsNothing, "changing the slot lifts the refusal");

	// Stop mode, nothing else to play: no stop, no refusal, and the warning's condition.
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	PlayerSigilProgress.Acquire(0);
	PlayerSigilProgress.Equip(0);
	aquarius = PlayerSigilProgress.Find(0);
	Finish(aquarius);
	Check(!PlayerSigilProgress.MustChooseSigil && PlayerSigilProgress.DrawsNothing, "only that sigil owned: no refusal, so the clock cannot freeze; the warning's condition holds");
	Check(PlayerSigilProgress.StopFor(PlayerSigilProgress.LastOutcome, false, false) == SigilStop.None, "and no stop");
	PlayerSigilProgress.Acquire(2);
	Check(PlayerSigilProgress.MustChooseSigil && PlayerSigilProgress.Equipped == aquarius, "acquiring something to play then asks for the choice, in Stop mode, without changing the slot");

	// An open activation counts as something else to play in Stop mode.
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	OwnPair(0, 95, 95);
	foreach (int s in new[] { 0, 1 }) { var sigil = PlayerSigilProgress.Find(s); foreach (var set in sigil.Sets) { set.PassesCompleted = 9500; set.Finished = true; } sigil.CompletedAtUtc = T0; }
	PlayerSigilProgress.RestoreFromCheckpoint(PlayerSigilProgress.CaptureForCheckpoint());
	Check(!PlayerSigilProgress.Equip(0) && PlayerSigilProgress.Equipped == null, "both Saturn sigils finished: neither can be played");
	Check(PlayerSigilProgress.DrawsNothing && !PlayerSigilProgress.MustChooseSigil, "an empty slot is the warning's condition too, and never a refusal: nothing was completed");
	Check(PlayerSigilProgress.StartActivation(0), "yet the activation can start: both violet is what D-GN.30's last level needs");

	// Automatic: the least played, alphabetical on a tie, never an activation.
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	PlayerSigilProgress.SetAutoSwitchOnCompletion(true);
	foreach (int s in new[] { 0, 8, 2, 4 }) PlayerSigilProgress.Acquire(s);
	Check(PlayerSigilProgress.Equipped == null, "automatic mode leaves an empty slot empty: nothing was completed");
	PlayerSigilProgress.Equip(0);
	PlayerSigilProgress.Find(8).BetsPlaced = 500;
	PlayerSigilProgress.Find(2).BetsPlaced = 40;
	PlayerSigilProgress.Find(4).BetsPlaced = 7;
	aquarius = PlayerSigilProgress.Find(0);
	Finish(aquarius);
	var completingOutcome = PlayerSigilProgress.LastOutcome;
	Check(PlayerSigilProgress.Equipped.SigilIndex == 4 && !PlayerSigilProgress.MustChooseSigil && !PlayerSigilProgress.DrawsNothing, "automatic: the slot takes the owned incomplete sigil with the fewest bets (7, not 40 or 500)");
	Check(PlayerSigilProgress.LastDrawn == aquarius && (completingOutcome & SigilBetOutcome.SigilCompleted) != 0, "the bet still reports the sigil it completed, so the log line cannot describe the next one");
	Check(PlayerSigilProgress.StopFor(completingOutcome, false, false) == SigilStop.None, "and the run goes on");
	// A tie: alphabetical by displayed name. Aries (4) before Sagittarius (2), which before Libra (8) would be wrong either way.
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	foreach (int s in new[] { 2, 8, 4, 0 }) PlayerSigilProgress.Acquire(s);
	PlayerSigilProgress.Equip(0);
	aquarius = PlayerSigilProgress.Find(0);
	Finish(aquarius);
	Check(PlayerSigilProgress.Equipped.SigilIndex == 4, $"a tie goes to the first by name, whatever the order acquired: {PlanetarySquares.NameOfSigil(PlayerSigilProgress.Equipped.SigilIndex)}");

	// It never starts an activation.
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	OwnPair(1, 5, 5);
	PlayerSigilProgress.Acquire(0);
	foreach (int s in new[] { 2, 3 }) { var sigil = PlayerSigilProgress.Find(s); foreach (var set in sigil.Sets) { set.PassesCompleted = 9500; set.Finished = true; set.PassesByChance[95] = 9500; } sigil.CompletedAtUtc = T0; }
	PlayerSigilProgress.RestoreFromCheckpoint(PlayerSigilProgress.CaptureForCheckpoint());
	PlayerSigilProgress.Equip(0);
	aquarius = PlayerSigilProgress.Find(0);
	Finish(aquarius);
	Check(PlayerSigilProgress.NextOpenLevel(1) > 0 && PlayerSigilProgress.Equipped == aquarius && PlayerSigilProgress.FindPlanetary(1) == null, "automatic with only an open activation left: it stays, and starts no activation");
	Check(!PlayerSigilProgress.MustChooseSigil && PlayerSigilProgress.DrawsNothing, "no refusal, in automatic mode, and the warning's condition holds");
	PlayerSigilProgress.SetAutoSwitchOnCompletion(false);
	Check(PlayerSigilProgress.MustChooseSigil, "turning it back to Stop asks for the choice, since the activation could be chosen by hand");
	PlayerSigilProgress.SetAutoSwitchOnCompletion(true);

	// Acquiring, or turning the toggle on, with a finished sigil waiting ("cuando lo tenga").
	Check(PlayerSigilProgress.Acquire(8) && PlayerSigilProgress.Equipped.SigilIndex == 8, "automatic: acquiring while a finished sigil waits takes the new one");
	PlayerSigilProgress.SetAutoSwitchOnCompletion(false);
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	PlayerSigilProgress.Acquire(0);
	PlayerSigilProgress.Acquire(4);
	PlayerSigilProgress.Equip(0);
	aquarius = PlayerSigilProgress.Find(0);
	Finish(aquarius);
	Check(PlayerSigilProgress.Equipped == aquarius && PlayerSigilProgress.SetAutoSwitchOnCompletion(true) && PlayerSigilProgress.Equipped.SigilIndex == 4, "turning the toggle on moves on at once");
	Check(!PlayerSigilProgress.SetAutoSwitchOnCompletion(true), "setting it to what it is does nothing");
	PlayerSigilProgress.SetAutoSwitchOnCompletion(false);

	// A planetary sigil reaching its limit is a completion like a sigil finishing.
	PlayerSigilProgress.ResetToPreGenesisDefaults();
	OwnPair(0, 1, 1);
	PlayerSigilProgress.Acquire(4);
	PlayerSigilProgress.StartActivation(0);
	var planetary = PlayerSigilProgress.Equipped;
	PlayChance(95, () => planetary.IsComplete);
	var limitOutcome = PlayerSigilProgress.LastOutcome;
	Check((limitOutcome & SigilBetOutcome.SigilCompleted) != 0 && PlayerSigilProgress.MustChooseSigil, "a planetary sigil at its highest open level asks for the same choice, in Stop mode");
	Check((limitOutcome & SigilBetOutcome.SigilLevelAcquired) != 0, "and that bet was a level acquired too: the two coincide there (D-GN.32)");
	PlayerSigilProgress.ResetToPreGenesisDefaults();

	// The level stop: exactly MaxLevel times a sigil's life at any chance, and the set's level alone never fires it.
	foreach (int chance in new[] { 95, 50, 1 })
	{
		var counted = SigilRules.NewProgress();
		long acquired = 0, reached = 0;
		int lastLevel = 0;
		bool everyRiseFlagged = true, noOtherFlagged = true;
		while (!counted.IsComplete)
		{
			var res = engine.Play(1m, chance, false);
			var o = SigilRules.ApplyBet(counted, res.Roll, res.IsWin, chance, 1m, DateTime.UnixEpoch);
			bool flagged = (o & SigilBetOutcome.SigilLevelAcquired) != 0;
			if ((o & SigilBetOutcome.LevelReached) != 0) reached++;
			if (flagged) acquired++;
			if ((o & SigilBetOutcome.SetPassCompleted) != 0)
			{
				int level = SigilRules.SigilLevel(counted);
				everyRiseFlagged &= level == lastLevel || flagged;
				noOtherFlagged &= level != lastLevel || !flagged;
				lastLevel = level;
			}
		}
		Check(acquired == SigilRules.MaxLevel && reached == 3L * SigilRules.MaxLevel && everyRiseFlagged && noOtherFlagged,
			$"at {chance}%: {acquired} levels acquired against {reached} set levels reached, flagged on exactly the bets the sigil's level rose");
	}

	// Convention 16: the same counting flags a set's rise that does not lift the lowest, if the rule were read from LevelReached.
	Check(3L * SigilRules.MaxLevel != SigilRules.MaxLevel, "counting by LevelReached instead would read 285, not 95 (the check above can fail)");

	// The priority, on one bet: the block path comes first in the engines; here a level before a set, and a choice before both.
	Check(PlayerSigilProgress.StopFor(SigilBetOutcome.SetPassCompleted | SigilBetOutcome.LevelReached | SigilBetOutcome.SigilLevelAcquired, true, true) == SigilStop.LevelAcquired, "a level is reported before a set");
	Check(PlayerSigilProgress.StopFor(SigilBetOutcome.SetPassCompleted | SigilBetOutcome.LevelReached | SigilBetOutcome.SigilLevelAcquired, false, true) == SigilStop.SetCompleted, "with the level switch off, the set");
	Check(PlayerSigilProgress.StopFor(SigilBetOutcome.SetPassCompleted | SigilBetOutcome.LevelReached, true, false) == SigilStop.None, "a set's level alone does not stop the level switch: it is the sigil's (D-GN.32)");
}

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASS" : $"\n{failures} CHECK(S) FAILED");
return failures;

// The exact mean cost of a repeating list of targets at a chance above LossStreakMaxChance, Low, by the game's roll rule: a
// Markov chain over V (1–9). Exists because Predicted treats targets as independent, which on the larger squares is wrong in
// the second digit (plan §10.10). A check against this can pass only if the code draws what the rule says.
static class ExactChain
{
	private static int Root(int x) => x % 9 == 0 ? 9 : x % 9;
	private static int Wrap(int x) { int m = ((x % 9) + 9) % 9; return m == 0 ? 9 : m; }

	public static List<int> Targets(PlanetarySquare q, SigilKind k) =>
		Enumerable.Range(0, q.N).SelectMany(set => Enumerable.Range(0, q.N).Select(s => q.NumberAt(k, set, s))).ToList();

	private static double[] Solve(double[,] a, double[] b)
	{
		int n = b.Length;
		for (int i = 0; i < n; i++)
		{
			int p = i;
			for (int r = i + 1; r < n; r++) if (Math.Abs(a[r, i]) > Math.Abs(a[p, i])) p = r;
			for (int c = 0; c < n; c++) (a[i, c], a[p, c]) = (a[p, c], a[i, c]);
			(b[i], b[p]) = (b[p], b[i]);
			for (int r = 0; r < n; r++)
			{
				if (r == i) continue;
				double f = a[r, i] / a[i, i];
				if (f == 0) continue;
				for (int c = i; c < n; c++) a[r, c] -= f * a[i, c];
				b[r] -= f * b[i];
			}
		}
		return Enumerable.Range(0, n).Select(i => b[i] / a[i, i]).ToArray();
	}

	// One target: the mean bets to the hit from each V, and where V lands on the hit.
	private static (double[] Mean, double[,] Exit) Step(int target, int chance)
	{
		var stay = new double[9, 9];
		var hit = new double[9, 9];
		for (int v = 1; v <= 9; v++)
			for (int roll = 0; roll < 100; roll++)
			{
				int r = Root(roll);
				if (roll >= chance) { stay[v - 1, Wrap(v - r) - 1] += 0.01; continue; }
				int after = Wrap(v + r);
				bool spells = target > 9 && target % 10 != 0;
				bool isHit = !spells ? after == (target <= 9 ? target : target / 10) : v == target / 10 && r == target % 10;
				(isHit ? hit : stay)[v - 1, after - 1] += 0.01;
			}

		var a = new double[9, 9];
		for (int i = 0; i < 9; i++) for (int j = 0; j < 9; j++) a[i, j] = (i == j ? 1 : 0) - stay[i, j];
		var mean = Solve((double[,])a.Clone(), Enumerable.Repeat(1.0, 9).ToArray());
		var exit = new double[9, 9];
		for (int col = 0; col < 9; col++)
		{
			var x = Solve((double[,])a.Clone(), Enumerable.Range(0, 9).Select(i => hit[i, col]).ToArray());
			for (int i = 0; i < 9; i++) exit[i, col] = x[i];
		}
		return (mean, exit);
	}

	// The long-run mean bets per cycle of the list, from V's stationary distribution at the cycle's start.
	public static double CycleCost(IReadOnlyList<int> targets, int chance)
	{
		var steps = targets.Select(t => Step(t, chance)).ToList();
		var d = Enumerable.Repeat(1.0 / 9, 9).ToArray();
		double cost = 0;
		for (int cycle = 0; cycle < 400; cycle++)
		{
			cost = 0;
			foreach (var (mean, exit) in steps)
			{
				cost += Enumerable.Range(0, 9).Sum(i => d[i] * mean[i]);
				var next = new double[9];
				for (int i = 0; i < 9; i++) for (int j = 0; j < 9; j++) next[j] += d[i] * exit[i, j];
				d = next;
			}
		}
		return cost;
	}
}
