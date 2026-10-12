using System;

namespace Tactics.Core
{
    // Inclusive zero-based bounds of the area that remains outside the storm.
    public readonly struct StormBounds
    {
        public readonly int MinX;
        public readonly int MaxX;
        public readonly int MinY;
        public readonly int MaxY;

        internal StormBounds(int minX, int maxX, int minY, int maxY)
        {
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
        }

        public bool Contains(int x, int y)
        {
            return x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
        }
    }

    public static class StormRules
    {
        // Provisional cadence: round one is safe, then one layer closes per round.
        // A custom cadence waits roundsPerShrink rounds after the warmup for its first layer.
        public static StormBounds SafeZone(int width, int height, int round,
            int warmupRounds = 1, int roundsPerShrink = 1)
        {
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));
            if (round < 1) throw new ArgumentOutOfRangeException(nameof(round));
            if (warmupRounds < 0) throw new ArgumentOutOfRangeException(nameof(warmupRounds));
            if (roundsPerShrink < 1) throw new ArgumentOutOfRangeException(nameof(roundsPerShrink));

            int layers = Math.Max(0, round - warmupRounds) / roundsPerShrink;
            // Each axis converges independently. Even dimensions retain two central cells.
            int insetX = Math.Min(layers, (width - 1) / 2);
            int insetY = Math.Min(layers, (height - 1) / 2);
            return new StormBounds(insetX, width - insetX - 1, insetY, height - insetY - 1);
        }

        public static bool IsStormCell(int x, int y, int width, int height, int round,
            int warmupRounds = 1, int roundsPerShrink = 1)
        {
            StormBounds safeZone = SafeZone(width, height, round, warmupRounds, roundsPerShrink);
            if (x < 0 || x >= width) throw new ArgumentOutOfRangeException(nameof(x));
            if (y < 0 || y >= height) throw new ArgumentOutOfRangeException(nameof(y));
            return !safeZone.Contains(x, y);
        }
    }
}
