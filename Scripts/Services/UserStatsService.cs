using Godot;
using Scripts.Finance;
using Scripts.User;

public partial class UserStatsService : Node
{
    public UserBettingStats Stats { get; private set; }

    public override void _Ready()
    {
        Stats = new UserBettingStats();
    }

    public void RegisterBet(BetTransactionEvent bet)
    {
        Stats.RegisterBet(bet);
    }

    public void RegisterDeposit()
    {
        Stats.RegisterDeposit();
    }
}
