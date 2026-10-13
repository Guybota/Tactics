using System;

namespace Tactics.Core
{
    // An owned planning snapshot. The match's luck stream never crosses this boundary.
    public sealed class BotObservation
    {
        internal MatchState State { get; }
        private BotObservation(MatchState state) { State = state; }
        public static BotObservation Capture(MatchState state)
        {
            if (state == null || state.Players.Count < 2 || state.Players.Count > 4 || state.Turn < 0)
                throw new ArgumentException("Invalid bot observation.");
            var copy = state.Clone();
            copy.RandomState = 0;
            return new BotObservation(copy);
        }
    }
}
