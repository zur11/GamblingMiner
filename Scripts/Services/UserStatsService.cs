using Godot;
using Scripts.Finance;
using Scripts.User;
using Scripts.Game;

public partial class UserStatsService : Node
{
    public UserBettingStats Stats { get; private set; }

    public override void _Ready()
    {
        Stats = new UserBettingStats();
    }

    public void RegisterBet(string gameId, BetTransactionEvent bet)
    {
        Stats.RegisterBet(gameId, bet);
    }

    public void RegisterDeposit()
    {
        Stats.RegisterDeposit();
    }

    public void RegisterSource(IBetEventSource source)
    {
        source.BetExecuted += OnBetExecuted;
    }

    private void OnBetExecuted(string gameId, BetTransactionEvent bet)
    {
        Stats.RegisterBet(gameId, bet);
    }
}
