using System;
using System.Collections.Generic;

namespace Tactics.Core
{
    public readonly struct GridPosition : IEquatable<GridPosition>
    {
        public readonly int X;
        public readonly int Y;
        public GridPosition(int x, int y) { X = x; Y = y; }
        public int DistanceTo(GridPosition other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
        public bool Equals(GridPosition other) => X == other.X && Y == other.Y;
        public override bool Equals(object other) => other is GridPosition position && Equals(position);
        public override int GetHashCode() => unchecked(X * 397 ^ Y);
        public override string ToString() => $"({X}, {Y})";
    }

    public enum CellOccupant { Empty, Ally, Enemy }

    public readonly struct MovementCell
    {
        public readonly int Cost;
        public readonly bool IsWall;
        public readonly CellOccupant Occupant;
        public MovementCell(int cost, bool isWall = false, CellOccupant occupant = CellOccupant.Empty)
        {
            if (cost < 1) throw new ArgumentOutOfRangeException(nameof(cost));
            Cost = cost;
            IsWall = isWall;
            Occupant = occupant;
        }
    }

    // A snapshot: presentation state cannot change movement costs or attack origins.
    public sealed class MovementResult
    {
        private readonly MovementCell[,] cells;
        private readonly int[,] costs;
        private readonly GridPosition[,] previous;
        private readonly List<GridPosition> reachable;
        public GridPosition Origin { get; }
        public int Width => cells.GetLength(0);
        public int Height => cells.GetLength(1);
        public IEnumerable<GridPosition> Reachable => reachable.AsReadOnly();

        internal MovementResult(MovementCell[,] cells, int[,] costs, GridPosition[,] previous, GridPosition origin)
        {
            this.cells = cells;
            this.costs = costs;
            this.previous = previous;
            Origin = origin;
            reachable = new List<GridPosition>();
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (costs[x, y] >= 0) reachable.Add(new GridPosition(x, y));
        }

        private bool Contains(GridPosition p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
        public bool IsReachable(GridPosition p) => Contains(p) && costs[p.X, p.Y] >= 0;
        public int CostTo(GridPosition p) => IsReachable(p) ? costs[p.X, p.Y] : int.MaxValue;
        public bool CanStop(GridPosition p) => IsReachable(p) &&
            (p.Equals(Origin) || cells[p.X, p.Y].Occupant == CellOccupant.Empty);

        // Includes the origin and destination; an invalid destination has no path.
        public List<GridPosition> PathTo(GridPosition destination)
        {
            var path = new List<GridPosition>();
            if (!CanStop(destination)) return path;
            GridPosition cursor = destination;
            while (!cursor.Equals(Origin))
            {
                path.Add(cursor);
                cursor = previous[cursor.X, cursor.Y];
            }
            path.Add(Origin);
            path.Reverse();
            return path;
        }

        public bool TryAttackOrigin(GridPosition target, int range, out GridPosition position)
        {
            if (range < 1) throw new ArgumentOutOfRangeException(nameof(range));
            position = Origin;
            if (!Contains(target)) return false;
            bool found = false;
            int bestCost = int.MaxValue;
            int bestDistance = int.MaxValue;
            foreach (GridPosition candidate in reachable)
            {
                int distance = candidate.DistanceTo(target);
                if (!CanStop(candidate) || distance < 1 || distance > range) continue;
                int cost = CostTo(candidate);
                // Equal candidates retain coordinate order for deterministic plans.
                if (cost > bestCost || (cost == bestCost && distance >= bestDistance)) continue;
                found = true;
                position = candidate;
                bestCost = cost;
                bestDistance = distance;
            }
            return found;
        }
    }

    public static class MovementRules
    {
        private readonly struct Frontier : IComparable<Frontier>
        {
            public readonly int Cost;
            public readonly GridPosition Position;
            public Frontier(int cost, GridPosition position) { Cost = cost; Position = position; }
            public int CompareTo(Frontier other)
            {
                int comparison = Cost.CompareTo(other.Cost);
                if (comparison == 0) comparison = Position.X.CompareTo(other.Position.X);
                if (comparison == 0) comparison = Position.Y.CompareTo(other.Position.Y);
                return comparison;
            }
        }

        public static MovementResult Calculate(MovementCell[,] board, GridPosition origin, int budget)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget));
            int width = board.GetLength(0), height = board.GetLength(1);
            if (origin.X < 0 || origin.Y < 0 || origin.X >= width || origin.Y >= height)
                throw new ArgumentOutOfRangeException(nameof(origin));
            var cells = (MovementCell[,])board.Clone();
            var costs = new int[width, height];
            var previous = new GridPosition[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    if (cells[x, y].Cost < 1) throw new ArgumentException("Every cell needs a positive movement cost.", nameof(board));
                    costs[x, y] = -1;
                }
            costs[origin.X, origin.Y] = 0;
            var frontier = new SortedSet<Frontier> { new Frontier(0, origin) };
            int[] dx = { -1, 1, 0, 0 }, dy = { 0, 0, -1, 1 };
            while (frontier.Count > 0)
            {
                Frontier current = frontier.Min;
                frontier.Remove(current);
                if (current.Cost != costs[current.Position.X, current.Position.Y]) continue;
                for (int direction = 0; direction < 4; direction++)
                {
                    int x = current.Position.X + dx[direction], y = current.Position.Y + dy[direction];
                    if (x < 0 || y < 0 || x >= width || y >= height) continue;
                    MovementCell cell = cells[x, y];
                    if (cell.IsWall || cell.Occupant == CellOccupant.Enemy) continue;
                    // Subtract first to avoid overflowing when a cell cost is very large.
                    if (cell.Cost > budget - current.Cost) continue;
                    int cost = current.Cost + cell.Cost;
                    if (costs[x, y] >= 0 && cost >= costs[x, y]) continue;
                    costs[x, y] = cost;
                    previous[x, y] = current.Position;
                    frontier.Add(new Frontier(cost, new GridPosition(x, y)));
                }
            }
            return new MovementResult(cells, costs, previous, origin);
        }
    }
}
