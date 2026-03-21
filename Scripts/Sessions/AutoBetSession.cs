using System;
using Godot;
using Scripts.Betting;
using Scripts.Finance;
using Scripts.Controllers;
using Scripts.Dice;
using Scripts.Game;

namespace Scripts.Sessions
{
    public class AutoBetSession : BaseBetSession
    {
        private Guid _sessionId;

        public AutoBetSession(
            BetService betService,
            Wallet wallet,
            ProgressiveBettingStrategy strategy)
            : base(betService, wallet, strategy)
        {
        }

        public override void Start(int betCount, BettingStrategyConfig config)
        {
            _sessionId = Guid.NewGuid(); // 🔥 única diferencia real
            base.Start(betCount, config);
        }

        protected override Guid? GetSessionId()
        {
            return _sessionId;
        }
    }
}