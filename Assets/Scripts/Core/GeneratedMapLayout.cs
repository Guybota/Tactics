using System;

namespace Tactics.Core
{
    // Shared by the live generator and diagnostic batches. Preserve draw order.
    public static class GeneratedMapLayout
    {
        public static CellState[,] Create(int width, int height, bool buildMode, Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            var cells = new CellState[width, height];
            for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
            {
                int draw = random.Next(0, 100);
                var terrain = draw < 15 ? TerrainKind.Mountain : draw < 20 ? TerrainKind.Forest : TerrainKind.Grass;
                cells[x, y] = new CellState { Terrain = terrain,
                    HasCastle = draw >= 20 && !buildMode && random.Next(0, 100) < 5 };
            }
            return cells;
        }
    }
}
