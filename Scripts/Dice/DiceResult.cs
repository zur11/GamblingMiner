namespace Scripts.Dice
{
	public sealed record DiceResult
	(
		int Roll,
		bool IsWin,
		decimal Bet,
		decimal Chance,
		decimal Multiplier,
		bool IsHigh,
		decimal Profit,
		int WinMin,
		int WinMax
	);
}
