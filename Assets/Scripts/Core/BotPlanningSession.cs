using System;
using System.Collections.Generic;

namespace Tactics.Core
{
    // Each advance is one bounded piece of analysis or one scored candidate.
    public sealed class BotPlanningSession : IDisposable
    {
        private readonly IEnumerator<BotDecision> work;
        public BotDecision Best { get; private set; }
        public bool IsComplete { get; private set; }
        public int CandidatesEvaluated { get; private set; }
        internal BotPlanningSession(IEnumerable<BotDecision> candidates) { work = candidates.GetEnumerator(); }
        public bool Advance()
        {
            if (IsComplete) return false;
            if (!work.MoveNext()) { Dispose(); return false; }
            var candidate = work.Current;
            if (candidate != null)
            {
                CandidatesEvaluated++;
                // Stable generation order resolves ties, independent of frame timing or luck.
                if (Best == null || candidate.Score > Best.Score) Best = candidate;
            }
            return true;
        }
        public void Dispose() { if (!IsComplete) work.Dispose(); IsComplete = true; }
    }
}
