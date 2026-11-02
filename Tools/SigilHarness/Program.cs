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
		&& SigilRules.Describe(new SigilCollectionState()) == "slot=empty owned=0", "Describe() reads an empty slot and an empty collection");
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
	Check(PlayerSigilProgress.Equip(1) && aquarius.CompletedAtUtc != default && PlayerSigilProgress.Equip(0) && PlayerSigilProgress.Equipped.IsComplete,
		"a complete sigil leaves and returns still complete");
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

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASS" : $"\n{failures} CHECK(S) FAILED");
return failures;
