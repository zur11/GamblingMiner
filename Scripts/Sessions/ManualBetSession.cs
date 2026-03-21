using System;
using Godot;
using Scripts.Betting;
using Scripts.Finance;
using Scripts.Controllers;
using Scripts.Dice;
using Scripts.Game;

namespace Scripts.Sessions
{
    public class ManualBetSession : BaseBetSession
    {
        public ManualBetSession(
            BetService betService,
            Wallet wallet,
            ProgressiveBettingStrategy strategy)
            : base(betService, wallet, strategy)
        {
        }

        protected override Guid? GetSessionId()
        {
            return null; // 🔥 manual no usa aislamiento
        }
    }
}