using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Tactics.Core
{
    // Diagnostic format only: does not change match saves or custom map formats.
    [Serializable]
    public sealed class SimulationSnapshot
    {
        public int version = 1, width, height, turn, winner, stormWarmupRounds, stormRoundsPerShrink;
        public bool buildMode, battleRoyaleMode, isOver;
        public uint randomState;
        public List<CellState> cells = new List<CellState>();
        public List<SimulationPlayer> players = new List<SimulationPlayer>();
        public static SimulationSnapshot Capture(MatchState state)
        {
            var data = new SimulationSnapshot { width = state.Width, height = state.Height, turn = state.Turn,
                winner = state.Winner, buildMode = state.BuildMode, battleRoyaleMode = state.BattleRoyaleMode,
                isOver = state.IsOver, randomState = state.RandomState,
                stormWarmupRounds = state.StormWarmupRounds, stormRoundsPerShrink = state.StormRoundsPerShrink };
            for (int y = 0; y < state.Height; y++) for (int x = 0; x < state.Width; x++) data.cells.Add(state.Cells[x, y].Clone());
            foreach (var player in state.Players)
            {
                var p = new SimulationPlayer { raceId = player.RaceId, money = player.Money,
                    nextUnitId = player.NextUnitId, hasLost = player.HasLost };
                foreach (var unit in player.Units) p.units.Add(new SimulationUnit { id = unit.Id, typeId = unit.TypeId,
                    x = unit.Position.X, y = unit.Position.Y, health = unit.Health, maxHealth = unit.MaxHealth,
                    attack = unit.Attack, movement = unit.Movement, range = unit.Range,
                    isLeader = unit.IsLeader, hasActed = unit.HasActed, isDead = unit.IsDead });
                data.players.Add(p);
            }
            return data;
        }
        public MatchState Restore()
        {
            if (version != 1 || cells == null || cells.Count != width * height || players == null
                || players.Count < 2 || players.Count > 4 || turn < 0) throw new ArgumentException("Invalid simulation snapshot.");
            var state = new MatchState(width, height) { Turn = turn, Winner = winner, IsOver = isOver,
                BuildMode = buildMode, BattleRoyaleMode = battleRoyaleMode, RandomState = randomState,
                StormWarmupRounds = stormWarmupRounds, StormRoundsPerShrink = stormRoundsPerShrink };
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) state.Cells[x, y] = cells[y * width + x].Clone();
            foreach (var p in players)
            {
                var player = new PlayerState { RaceId = p.raceId, Money = p.money, NextUnitId = p.nextUnitId, HasLost = p.hasLost };
                foreach (var u in p.units) player.Units.Add(new UnitState { Id = u.id, TypeId = u.typeId,
                    Position = new GridPosition(u.x, u.y), Health = u.health, MaxHealth = u.maxHealth,
                    Attack = u.attack, Movement = u.movement, Range = u.range,
                    IsLeader = u.isLeader, HasActed = u.hasActed, IsDead = u.isDead });
                state.Players.Add(player);
            }
            return state;
        }
        // Stable across processes/cultures. Includes economy, turn and luck by default.
        public static string Fingerprint(MatchState state, bool boardOnly = false)
        {
            var text = new StringBuilder();
            text.Append(state.Width).Append(',').Append(state.Height).Append(',').Append(state.ActivePlayer)
                .Append(',').Append(state.BuildMode).Append(',').Append(state.BattleRoyaleMode)
                .Append(',').Append(state.IsOver).Append(',').Append(state.Winner)
                .Append(',').Append(state.StormWarmupRounds).Append(',').Append(state.StormRoundsPerShrink);
            if (!boardOnly) text.Append(',').Append(state.Turn).Append(',').Append(state.RandomState);
            foreach (var cell in state.Cells) text.Append('|').Append((int)cell.Terrain).Append(',').Append(cell.MovementCost)
                .Append(',').Append(cell.SupportsCastle).Append(',').Append(cell.HasCastle).Append(',').Append(cell.CastleOwner).Append(',').Append(cell.HasStorm);
            foreach (var player in state.Players)
            {
                text.Append('|').Append(player.RaceId).Append(',').Append(player.HasLost);
                if (!boardOnly) text.Append(',').Append(player.Money).Append(',').Append(player.NextUnitId);
                foreach (var u in player.Units) text.Append(';').Append(u.Id).Append(',').Append(u.TypeId).Append(',')
                    .Append(u.Position.X).Append(',').Append(u.Position.Y).Append(',').Append(u.Health.ToString("R", CultureInfo.InvariantCulture))
                    .Append(',').Append(u.MaxHealth.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(u.Attack)
                    .Append(',').Append(u.Movement).Append(',').Append(u.Range).Append(',').Append(u.IsLeader).Append(',').Append(u.HasActed).Append(',').Append(u.IsDead);
            }
            ulong hash = 14695981039346656037UL;
            foreach (char c in text.ToString()) { hash ^= c; hash = unchecked(hash * 1099511628211UL); }
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
    [Serializable] public sealed class SimulationPlayer
    {
        public string raceId; public int money, nextUnitId; public bool hasLost;
        public List<SimulationUnit> units = new List<SimulationUnit>();
    }
    [Serializable] public sealed class SimulationUnit
    {
        public string typeId; public int id, x, y, attack, movement, range;
        public float health, maxHealth; public bool isLeader, hasActed, isDead;
    }
    [Serializable] public sealed class SimulationContentSnapshot
    {
        public List<SimulationType> units = new List<SimulationType>();
        public List<SimulationRace> races = new List<SimulationRace>();
        public List<SimulationDamage> damage = new List<SimulationDamage>();
        public static SimulationContentSnapshot Capture(MatchContent content, IEnumerable<string> raceIds)
        {
            var data = new SimulationContentSnapshot();
            foreach (var u in content.UnitTypes.OrderBy(t => t.Id, StringComparer.Ordinal))
                data.units.Add(new SimulationType { id = u.Id, health = u.Health, attack = u.Attack, movement = u.Movement, range = u.Range, cost = u.Cost });
            foreach (var race in raceIds.Distinct().OrderBy(r => r, StringComparer.Ordinal)) data.races.Add(new SimulationRace { id = race,
                recruits = data.units.Where(u => content.CanRecruit(race, u.id)).Select(u => u.id).ToList() });
            foreach (var a in data.units) foreach (var d in data.units) data.damage.Add(new SimulationDamage { attacker = a.id, defender = d.id,
                percent = content.BaseDamage(new UnitState { TypeId = a.id, Attack = a.attack }, new UnitState { TypeId = d.id, MaxHealth = d.health }) });
            return data;
        }
        public MatchContent Restore()
        {
            var content = new MatchContent();
            foreach (var u in units) content.AddUnit(new UnitTypeStats(u.id, u.health, u.attack, u.movement, u.range, u.cost));
            foreach (var r in races) content.AddRace(r.id, r.recruits);
            foreach (var d in damage) content.SetDamage(d.attacker, d.defender, d.percent);
            content.Validate(); return content;
        }
    }
    [Serializable] public sealed class SimulationType { public string id; public float health; public int attack, movement, range, cost; }
    [Serializable] public sealed class SimulationRace { public string id; public List<string> recruits; }
    [Serializable] public sealed class SimulationDamage { public string attacker, defender; public float percent; }
}
