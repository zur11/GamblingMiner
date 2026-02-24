using Godot;
using System;
using Scripts.Finance;
using Scripts.User;
using Scripts.Game;

public partial class UserStatsService : Node
{
    public event Action<UserBettingStats> StatsChanged;

    public UserBettingStats Stats { get; private set; }

    public override void _Ready()
    {
        Stats = new UserBettingStats();
    }

    public void OnBetExecutedRegisterBet(string gameId, BetTransactionEvent bet)
    {
        Stats.RegisterBet(gameId, bet);
        StatsChanged?.Invoke(Stats);
    }

    public void RegisterDeposit()
    {
        Stats.RegisterDeposit();
        StatsChanged?.Invoke(Stats);
    }

    public void RegisterSource(IBetEventSource source)
    {
        source.BetExecuted += OnBetExecutedRegisterBet;
    }
}
