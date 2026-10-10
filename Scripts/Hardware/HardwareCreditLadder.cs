namespace Scripts.Hardware;

/// <summary>
/// Mini-plan 20 — the credit counts offered wherever a control picks a hardware credit count from a list: the
/// DEV player-credits selector in DiceGame and the Per Bet pace in BetsHistoryExplorer. One list, so the two
/// can never offer different rungs. Fine at the low end, where each credit changes the bet spacing a lot
/// (100 / credits game-seconds), coarse above; the top is the rate cap.
/// </summary>
public static class HardwareCreditLadder
{
	public static readonly int[] Steps =
		{ 1, 2, 3, 4, 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, SimulationService.MaxAutoBetBaseAps };
}
