using System;
using System.Collections.Generic;

namespace Tactics.Core
{
    public enum TerrainKind { Grass, Forest, Mountain }

    [Serializable]
    public sealed class CellState
    {
        public TerrainKind Terrain;
        public int MovementCost = 1;
        public bool SupportsCastle = true;
        public bool HasCastle, HasStorm;
        public int CastleOwner = -1;
        public int DefenseStars => HasCastle ? 3 : Terrain == TerrainKind.Forest ? 2 : 1;
        public CellState Clone() => (CellState)MemberwiseClone();
    }

    public readonly struct UnitKey : IEquatable<UnitKey>
    {
        public readonly int Player, Id;
        public UnitKey(int player, int id) { Player = player; Id = id; }
        public bool Equals(UnitKey other) => Player == other.Player && Id == other.Id;
        public override bool Equals(object other) => other is UnitKey key && Equals(key);
        public override int GetHashCode() => unchecked(Player * 397 ^ Id);
    }

    public sealed class UnitState
    {
        public int Id;
        public string TypeId;
        public GridPosition Position;
        public float Health, MaxHealth;
        public int Attack, Movement, Range;
        public bool IsLeader, HasActed, IsDead;
        public bool IsAlive => !IsDead && Health > 0;
        public UnitState Clone() => (UnitState)MemberwiseClone();
    }

    public sealed class PlayerState
    {
        public string RaceId;
        public int Money, NextUnitId = 1;
        public bool HasLost;
        public readonly List<UnitState> Units = new List<UnitState>();
        public PlayerState Clone()
        {
            var clone = new PlayerState { RaceId = RaceId, Money = Money, NextUnitId = NextUnitId, HasLost = HasLost };
            foreach (var unit in Units) clone.Units.Add(unit.Clone());
            return clone;
        }
    }

    // Contains no Unity objects, presentation flags, or scene references.
    public sealed class MatchState
    {
        public readonly CellState[,] Cells;
        public readonly List<PlayerState> Players = new List<PlayerState>();
        public int Turn;
        public bool BuildMode, BattleRoyaleMode, IsOver;
        public int Winner = -1;
        public uint RandomState = 0x6D2B79F5;
        public int StormWarmupRounds = 1, StormRoundsPerShrink = 1;
        public int Width => Cells.GetLength(0);
        public int Height => Cells.GetLength(1);
        public int ActivePlayer => Turn % Players.Count;
        public int Round => TurnRules.Round(Turn, Players.Count);

        public MatchState(int width, int height)
        {
            if (width < 1 || width > 50 || height < 1 || height > 50) throw new ArgumentOutOfRangeException();
            Cells = new CellState[width, height];
            for (int x = 0; x < width; x++) for (int y = 0; y < height; y++) Cells[x, y] = new CellState();
        }
        public bool Contains(GridPosition p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
        public UnitState FindUnit(UnitKey key) => key.Player >= 0 && key.Player < Players.Count
            ? Players[key.Player].Units.Find(u => u.Id == key.Id) : null;
        public UnitState UnitAt(GridPosition p)
        {
            foreach (var player in Players) foreach (var unit in player.Units)
                if (unit.IsAlive && unit.Position.Equals(p)) return unit;
            return null;
        }
        public MatchState Clone()
        {
            var copy = new MatchState(Width, Height)
            {
                Turn = Turn, BuildMode = BuildMode, BattleRoyaleMode = BattleRoyaleMode,
                IsOver = IsOver, Winner = Winner, RandomState = RandomState,
                StormWarmupRounds = StormWarmupRounds, StormRoundsPerShrink = StormRoundsPerShrink
            };
            for (int x = 0; x < Width; x++) for (int y = 0; y < Height; y++) copy.Cells[x, y] = Cells[x, y].Clone();
            foreach (var player in Players) copy.Players.Add(player.Clone());
            return copy;
        }
    }
}
