using System;

namespace Tactics.Core
{
    public static class LeaderStats
    {
        public static float Health(float value) => (float)Math.Ceiling(value * 1.5f);
        public static int Attack(int value) => (int)Math.Ceiling(value * 1.5f);
    }
}
