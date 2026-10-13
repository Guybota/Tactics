using System;
using System.Collections.Generic;

namespace Tactics.Core
{
    // Server storage only. Never return this envelope to a client: the random stream is private.
    [Serializable]
    public sealed class MatchStateData
    {
        public const int CurrentVersion = 1;
        public int SchemaVersion;
        public MatchSnapshotData Snapshot;
        public uint RandomState;

        public static MatchStateData FromState(MatchState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return new MatchStateData { SchemaVersion = CurrentVersion,
                Snapshot = MatchSnapshotData.FromState(state), RandomState = state.RandomState };
        }
        public MatchState ToState()
        {
            // This converter assumes a trusted private record. The storage provider must
            // verify JSON field presence, including RandomState: zero is a valid saved seed.
            if (SchemaVersion != CurrentVersion || Snapshot == null) throw new ArgumentException("Invalid stored match format.");
            var state = Snapshot.ToState();
            state.RandomState = RandomState;
            return state;
        }
    }

    // Client wire format. Row-major cells at y * Width + x; no server random stream.
    [Serializable]
    public sealed class MatchSnapshotData
    {
        public const int CurrentVersion = 1;
        public int SchemaVersion;
        public int Width, Height, Turn;
        public bool BuildMode, BattleRoyaleMode, IsOver;
        public int Winner = -1;
        public int StormWarmupRounds = 1, StormRoundsPerShrink = 1;
        public List<MatchCellData> Cells = new List<MatchCellData>();
        public List<MatchPlayerData> Players = new List<MatchPlayerData>();

        public static MatchSnapshotData FromState(MatchState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var data = new MatchSnapshotData { SchemaVersion = CurrentVersion, Width = state.Width, Height = state.Height, Turn = state.Turn,
                BuildMode = state.BuildMode, BattleRoyaleMode = state.BattleRoyaleMode, IsOver = state.IsOver,
                Winner = state.Winner, StormWarmupRounds = state.StormWarmupRounds, StormRoundsPerShrink = state.StormRoundsPerShrink };
            for (int y = 0; y < state.Height; y++) for (int x = 0; x < state.Width; x++)
            {
                var cell = state.Cells[x, y];
                if (cell == null) throw new ArgumentException("Missing cell.");
                data.Cells.Add(new MatchCellData { Terrain = cell.Terrain, MovementCost = cell.MovementCost,
                    SupportsCastle = cell.SupportsCastle, HasCastle = cell.HasCastle, HasStorm = cell.HasStorm, CastleOwner = cell.CastleOwner });
            }
            foreach (var player in state.Players)
            {
                if (player == null) throw new ArgumentException("Missing player.");
                var p = new MatchPlayerData { RaceId = player.RaceId, Money = player.Money, NextUnitId = player.NextUnitId, HasLost = player.HasLost };
                foreach (var unit in player.Units)
                {
                    if (unit == null) throw new ArgumentException("Missing unit.");
                    p.Units.Add(new MatchUnitData { Id = unit.Id, TypeId = unit.TypeId, X = unit.Position.X, Y = unit.Position.Y,
                        Health = unit.Health, MaxHealth = unit.MaxHealth, Attack = unit.Attack, Movement = unit.Movement, Range = unit.Range,
                        IsLeader = unit.IsLeader, HasActed = unit.HasActed, IsDead = unit.IsDead });
                }
                data.Players.Add(p);
            }
            data.Validate();
            return data;
        }

        // For projection/prediction only. Server execution must restore MatchStateData instead.
        public MatchState ToState()
        {
            Validate();
            var state = new MatchState(Width, Height) { Turn = Turn, BuildMode = BuildMode, BattleRoyaleMode = BattleRoyaleMode,
                IsOver = IsOver, Winner = Winner, StormWarmupRounds = StormWarmupRounds, StormRoundsPerShrink = StormRoundsPerShrink };
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                var c = Cells[y * Width + x];
                state.Cells[x, y] = new CellState { Terrain = c.Terrain, MovementCost = c.MovementCost, SupportsCastle = c.SupportsCastle,
                    HasCastle = c.HasCastle, HasStorm = c.HasStorm, CastleOwner = c.CastleOwner };
            }
            foreach (var p in Players)
            {
                var player = new PlayerState { RaceId = p.RaceId, Money = p.Money, NextUnitId = p.NextUnitId, HasLost = p.HasLost };
                foreach (var u in p.Units) player.Units.Add(new UnitState { Id = u.Id, TypeId = u.TypeId, Position = new GridPosition(u.X, u.Y),
                    Health = u.Health, MaxHealth = u.MaxHealth, Attack = u.Attack, Movement = u.Movement, Range = u.Range,
                    IsLeader = u.IsLeader, HasActed = u.HasActed, IsDead = u.IsDead });
                state.Players.Add(player);
            }
            return state;
        }

        public void Validate()
        {
            if (SchemaVersion != CurrentVersion || Width < 1 || Width > 50 || Height < 1 || Height > 50
                || Turn < 0 || Turn > int.MaxValue - 4 || StormWarmupRounds < 0 || StormRoundsPerShrink < 1
                || Cells == null || Cells.Count != Width * Height || Players == null || Players.Count < 2 || Players.Count > 4
                || Winner < -1 || Winner >= Players.Count || (!IsOver && Winner != -1))
                throw new ArgumentException("Invalid match snapshot header.");
            foreach (var cell in Cells)
                if (cell == null || !Enum.IsDefined(typeof(TerrainKind), cell.Terrain) || cell.MovementCost < 1
                    || cell.CastleOwner < -1 || cell.CastleOwner >= Players.Count || (!cell.HasCastle && cell.CastleOwner != -1)
                    || (cell.HasCastle && (!cell.SupportsCastle || cell.Terrain != TerrainKind.Grass || cell.HasStorm)))
                    throw new ArgumentException("Invalid snapshot cell.");
            var occupied = new HashSet<GridPosition>();
            int survivors = 0, survivingPlayer = -1;
            for (int slot = 0; slot < Players.Count; slot++)
            {
                var player = Players[slot];
                if (player == null || !ValidId(player.RaceId) || player.Money < 0 || player.NextUnitId < 1
                    || player.Units == null || player.Units.Count > 10000) throw new ArgumentException("Invalid snapshot player.");
                if (!player.HasLost) { survivors++; survivingPlayer = slot; }
                var ids = new HashSet<int>();
                int leaders = 0, aliveLeaders = 0;
                foreach (var u in player.Units)
                {
                    if (u == null || u.Id < 1 || u.Id >= player.NextUnitId || !ids.Add(u.Id) || !ValidId(u.TypeId)
                        || u.X < 0 || u.Y < 0 || u.X >= Width || u.Y >= Height || !Finite(u.Health) || !Finite(u.MaxHealth)
                        || u.MaxHealth <= 0 || u.Health < 0 || u.Health > u.MaxHealth || u.Attack < 0 || u.Movement < 0 || u.Range < 1
                        || (u.IsDead != (u.Health <= 0)) || (player.HasLost && !u.IsDead))
                        throw new ArgumentException("Invalid snapshot unit.");
                    if (u.IsLeader) { leaders++; if (!u.IsDead) aliveLeaders++; }
                    if (!u.IsDead && (Cells[u.Y * Width + u.X].Terrain == TerrainKind.Mountain
                        || !occupied.Add(new GridPosition(u.X, u.Y)))) throw new ArgumentException("Invalid occupied cell.");
                }
                if (leaders > 1 || (!player.HasLost && aliveLeaders != 1))
                    throw new ArgumentException("Invalid snapshot leader.");
            }
            if (IsOver && (survivors > 1 || Winner != (survivors == 1 ? survivingPlayer : -1)))
                throw new ArgumentException("Invalid match outcome.");
            if (!IsOver && (survivors < 2 || Players[Turn % Players.Count].HasLost))
                throw new ArgumentException("Invalid active match participants.");
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool ValidId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return false;
            foreach (char c in value) if (char.IsControl(c)) return false;
            return true;
        }
    }

    [Serializable]
    public sealed class MatchCellData
    {
        public TerrainKind Terrain;
        public int MovementCost = 1;
        public bool SupportsCastle = true, HasCastle, HasStorm;
        public int CastleOwner = -1;
    }
    [Serializable]
    public sealed class MatchPlayerData
    {
        public string RaceId;
        public int Money, NextUnitId = 1;
        public bool HasLost;
        public List<MatchUnitData> Units = new List<MatchUnitData>();
    }
    [Serializable]
    public sealed class MatchUnitData
    {
        public int Id, X, Y;
        public string TypeId;
        public float Health, MaxHealth;
        public int Attack, Movement, Range;
        public bool IsLeader, HasActed, IsDead;
    }
}
