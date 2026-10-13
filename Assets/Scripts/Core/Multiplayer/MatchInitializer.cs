using System;
using System.Collections.Generic;

namespace Tactics.Core
{
    [Serializable]
    public sealed class MatchPlayerSetup
    {
        public string RaceId, LeaderTypeId;
    }

    [Serializable]
    public sealed class MatchOptions
    {
        public bool BuildMode, BattleRoyaleMode;
        // -1 uses local game's defaults: zero, or 4000 in build mode, before opening income.
        public int InitialMoney = -1;
        public int StormWarmupRounds = 1, StormRoundsPerShrink = 1;
    }

    public static class MatchInitializer
    {
        public static MatchState Create(CustomMapData map, MatchContentData catalogue, IReadOnlyList<MatchPlayerSetup> players,
            uint randomSeed, MatchOptions options = null)
        {
            CustomMapValidator.ValidatePlayable(map);
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));
            var content = catalogue.ToContent();
            options = options ?? new MatchOptions();
            if (players == null || players.Count != map.playerCount || options.InitialMoney < -1
                || options.StormWarmupRounds < 0 || options.StormRoundsPerShrink < 1)
                throw new ArgumentException("Invalid match setup.");
            int money = options.InitialMoney == -1 ? (options.BuildMode ? 4000 : 0) : options.InitialMoney;
            // Opening income cannot exceed one base payment plus every map cell's payment.
            if (money > int.MaxValue - checked(500 + map.width * map.height * 500)) throw new ArgumentException("Initial money exceeds safe limit.");
            var state = new MatchState(map.width, map.height) { BuildMode = options.BuildMode, BattleRoyaleMode = options.BattleRoyaleMode,
                RandomState = randomSeed == 0 ? 0x6D2B79F5 : randomSeed, StormWarmupRounds = options.StormWarmupRounds,
                StormRoundsPerShrink = options.StormRoundsPerShrink };
            for (int y = 0; y < map.height; y++) for (int x = 0; x < map.width; x++)
            {
                var source = map.Cell(x, y);
                state.Cells[x, y] = new CellState { Terrain = source.terrain,
                    MovementCost = source.terrain == TerrainKind.Grass ? 1 : source.terrain == TerrainKind.Forest ? 2 : 100000,
                    SupportsCastle = source.terrain == TerrainKind.Grass, HasCastle = source.castle, CastleOwner = source.castleOwner };
            }
            for (int slot = 0; slot < players.Count; slot++)
            {
                var setup = players[slot];
                var race = setup == null ? null : catalogue.Races.Find(r => string.Equals(r.Id, setup.RaceId, StringComparison.Ordinal));
                if (race == null || !string.Equals(race.LeaderTypeId, setup.LeaderTypeId, StringComparison.Ordinal))
                    throw new ArgumentException("Invalid race leader selection.");
                var type = content.Unit(race.LeaderTypeId);
                float health = LeaderStats.Health(type.Health);
                double attack = Math.Ceiling(type.Attack * 1.5f);
                if (!MatchSnapshotData.Finite(health) || attack > int.MaxValue) throw new ArgumentException("Leader statistics exceed safe limits.");
                var start = map.StartFor(slot);
                var player = new PlayerState { RaceId = race.Id, Money = money, NextUnitId = 2 };
                player.Units.Add(new UnitState { Id = 1, TypeId = type.Id, Position = new GridPosition(start.x, start.y),
                    Health = health, MaxHealth = health, Attack = LeaderStats.Attack(type.Attack), Movement = type.Movement,
                    Range = type.Range, IsLeader = true });
                state.Players.Add(player);
            }
            MatchEngine.BeginTurn(state);
            MatchSnapshotData.FromState(state); // Validate complete initial state before it leaves the server.
            return state;
        }
    }
}
