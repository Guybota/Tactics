using System;
using System.Collections.Generic;

namespace Tactics.Core
{
    public static class TurnRules
    {
        public static int Round(int turn, int participantCount)
        {
            if (turn < 0 || participantCount < 1) throw new ArgumentOutOfRangeException();
            return turn / participantCount + 1;
        }

        public static int NextActiveTurn(int currentTurn, IReadOnlyList<bool> eliminated)
        {
            if (eliminated == null || eliminated.Count == 0) return -1;
            for (int offset = 1; offset <= eliminated.Count; offset++)
            {
                int next = checked(currentTurn + offset);
                if (!eliminated[next % eliminated.Count]) return next;
            }
            return -1;
        }
    }
}
