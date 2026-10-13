using System;
using System.Collections.Generic;

namespace Tactics.Core
{
    internal sealed class BotAnalysis
    {
        internal sealed class Enemy
        {
            public UnitKey Key;
            public UnitState Unit;
            public bool[,] Threatens;
        }
        private readonly MatchEngine engine;
        private readonly BotProfile profile;
        internal readonly MatchState State;
        internal readonly List<Enemy> Enemies = new List<Enemy>();
        internal readonly List<GridPosition> OwnedCastles = new List<GridPosition>();
        private int[,] castleDistance, enemyDistance, safeDistance;
        private readonly Dictionary<(UnitKey, GridPosition), float> positionCache = new Dictionary<(UnitKey, GridPosition), float>();
        private readonly Dictionary<(UnitKey, UnitKey, int, int), CombatPrediction> combatCache = new Dictionary<(UnitKey, UnitKey, int, int), CombatPrediction>();
        private StormBounds nextSafe;
        internal int OwnArmy;

        internal BotAnalysis(MatchEngine engine, MatchState state, BotProfile profile)
        { this.engine = engine; State = state; this.profile = profile; }

        internal IEnumerable<BotDecision> Prepare()
        {
            var castles = new List<GridPosition>();
            var targets = new List<GridPosition>();
            var safety = new List<GridPosition>();
            nextSafe = StormRules.SafeZone(State.Width, State.Height, State.Round + 1, State.StormWarmupRounds, State.StormRoundsPerShrink);
            for (int x = 0; x < State.Width; x++)
            {
                for (int y = 0; y < State.Height; y++)
                {
                    var p = new GridPosition(x, y); var cell = State.Cells[x, y];
                    if (cell.Terrain == TerrainKind.Mountain) continue;
                    if (cell.HasCastle && !cell.HasStorm)
                    {
                        if (cell.CastleOwner == State.ActivePlayer) OwnedCastles.Add(p);
                        else if (!State.BattleRoyaleMode || nextSafe.Contains(x, y)) castles.Add(p);
                    }
                    if (!cell.HasStorm && nextSafe.Contains(x, y)) safety.Add(p);
                }
                yield return null;
            }
            for (int player = 0; player < State.Players.Count; player++)
            {
                if (State.Players[player].HasLost) continue;
                foreach (var unit in State.Players[player].Units)
                {
                    if (!unit.IsAlive) continue;
                    if (player == State.ActivePlayer) { OwnArmy++; continue; }
                    targets.Add(unit.Position);
                    var enemy = new Enemy { Key = new UnitKey(player, unit.Id), Unit = unit,
                        Threatens = new bool[State.Width, State.Height] };
                    // HasActed is intentionally ignored: enemies reset before their next turn.
                    var movement = engine.ThreatMovement(State, enemy.Key);
                    yield return null;
                    foreach (var origin in movement.Reachable)
                    {
                        if (!movement.CanStop(origin)) continue;
                        for (int dx = -unit.Range; dx <= unit.Range; dx++)
                            for (int dy = -unit.Range + Math.Abs(dx); dy <= unit.Range - Math.Abs(dx); dy++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                var target = new GridPosition(origin.X + dx, origin.Y + dy);
                                if (State.Contains(target)) enemy.Threatens[target.X, target.Y] = true;
                            }
                        yield return null;
                    }
                    Enemies.Add(enemy);
                }
            }
            foreach (var step in DistanceField(castles, field => castleDistance = field)) yield return step;
            foreach (var step in DistanceField(targets, field => enemyDistance = field)) yield return step;
            foreach (var step in DistanceField(safety, field => safeDistance = field)) yield return step;
        }

        private readonly struct Frontier : IComparable<Frontier>
        {
            public readonly int Cost;
            public readonly GridPosition Position;
            public Frontier(int cost, GridPosition position) { Cost = cost; Position = position; }
            public int CompareTo(Frontier other)
            {
                int value = Cost.CompareTo(other.Cost);
                if (value == 0) value = Position.X.CompareTo(other.Position.X);
                return value == 0 ? Position.Y.CompareTo(other.Position.Y) : value;
            }
        }

        // Reverse Dijkstra: distance to an objective accounts for the terrain being entered.
        // Occupancy is excluded here because this is a strategic route over several turns.
        private IEnumerable<BotDecision> DistanceField(List<GridPosition> goals, Action<int[,]> assign)
        {
            var field = new int[State.Width, State.Height];
            for (int x = 0; x < State.Width; x++) for (int y = 0; y < State.Height; y++) field[x, y] = int.MaxValue;
            var frontier = new SortedSet<Frontier>();
            foreach (var p in goals) { field[p.X, p.Y] = 0; frontier.Add(new Frontier(0, p)); }
            int processed = 0;
            while (frontier.Count > 0)
            {
                var node = frontier.Min; frontier.Remove(node);
                if (field[node.Position.X, node.Position.Y] != node.Cost) continue;
                for (int direction = 0; direction < 4; direction++)
                {
                    int dx = direction == 0 ? -1 : direction == 1 ? 1 : 0;
                    int dy = direction == 2 ? -1 : direction == 3 ? 1 : 0;
                    var p = new GridPosition(node.Position.X + dx, node.Position.Y + dy);
                    if (!State.Contains(p) || State.Cells[p.X, p.Y].Terrain == TerrainKind.Mountain) continue;
                    int cost = node.Cost + Math.Max(1, State.Cells[node.Position.X, node.Position.Y].MovementCost);
                    if (cost >= field[p.X, p.Y]) continue;
                    field[p.X, p.Y] = cost; frontier.Add(new Frontier(cost, p));
                }
                if (++processed % 32 == 0) yield return null;
            }
            assign(field);
            yield return null;
        }

        internal float Material(UnitState unit)
            => unit.IsLeader ? 2200 : Math.Max(300, engine.Content.Unit(unit.TypeId).Cost);

        internal CombatPrediction Predict(UnitKey actor, UnitKey target, GridPosition destination)
        {
            var key = (actor, target, destination.DistanceTo(State.FindUnit(target).Position), State.Cells[destination.X, destination.Y].DefenseStars);
            if (!combatCache.TryGetValue(key, out var prediction))
            {
                prediction = engine.Forecast(State, actor, target, destination);
                combatCache.Add(key, prediction);
            }
            return prediction;
        }

        internal float PositionScore(UnitKey key, GridPosition p, float health = -1, UnitKey? reducedEnemy = null, float remainingEnemy = 1)
        {
            bool cacheable = health < 0 && !reducedEnemy.HasValue;
            if (cacheable && positionCache.TryGetValue((key, p), out float cached)) return cached;
            var unit = State.FindUnit(key);
            if (health < 0) health = unit.Health;
            if (health <= 0) return unit.IsLeader ? -1000000 : -Material(unit);
            var cell = State.Cells[p.X, p.Y];
            float incoming = 0, strongest = 0;
            foreach (var enemy in Enemies)
            {
                if (!enemy.Threatens[p.X, p.Y]) continue;
                float factor = reducedEnemy.HasValue && reducedEnemy.Value.Equals(enemy.Key) ? remainingEnemy : 1;
                // Defeating a leader also removes that player's entire army.
                if (reducedEnemy.HasValue && State.FindUnit(reducedEnemy.Value).IsLeader && remainingEnemy <= 0
                    && enemy.Key.Player == reducedEnemy.Value.Player) factor = 0;
                if (factor <= 0) continue;
                var attacker = new Combatant(enemy.Unit.Health * factor, enemy.Unit.MaxHealth, enemy.Unit.Attack, enemy.Unit.Range);
                var defender = new Combatant(health, unit.MaxHealth, unit.Attack, unit.Range, cell.DefenseStars);
                // A legal distance of one is enough to calculate incoming damage; the map handles reach.
                float damage = CombatRules.Resolve(attacker, defender, 1, engine.Content.BaseDamage(enemy.Unit, unit), 0, 9, 0).Damage;
                incoming += damage; strongest = Math.Max(strongest, damage);
            }
            // Coverage is potential fire, not a promise that every enemy will attack this unit.
            // Keep the leader conservative; troops must be able to contest defended objectives.
            if (!unit.IsLeader) incoming = strongest + (incoming - strongest) * .15f;
            float score = -incoming / unit.MaxHealth * Material(unit) * profile.Threat * (unit.IsLeader ? profile.LeaderCaution : 1);
            if (unit.IsLeader && incoming >= health && incoming > 0) score -= 100000;
            if (cell.HasCastle && cell.CastleOwner == key.Player && !cell.HasStorm)
                score += Math.Min(unit.MaxHealth * .3f, unit.MaxHealth - health) / unit.MaxHealth * Material(unit) * .8f;
            if (State.BattleRoyaleMode)
            {
                bool closing = !nextSafe.Contains(p.X, p.Y);
                if (cell.HasStorm || closing)
                {
                    score -= profile.Storm * (unit.IsLeader ? 4 : 1) * (cell.HasStorm ? 2 : 1);
                    if (unit.IsLeader && cell.HasStorm && health <= 2 + State.Round * .25f) score -= 100000;
                    int distance = safeDistance[p.X, p.Y];
                    if (distance < int.MaxValue) score -= distance * profile.Progress * (unit.IsLeader ? 3 : 1);
                }
            }
            if (cacheable) positionCache[(key, p)] = score;
            return score;
        }

        internal float Progress(UnitState unit, GridPosition destination)
        {
            int[,] field = castleDistance[unit.Position.X, unit.Position.Y] < int.MaxValue ? castleDistance : enemyDistance;
            int before = field[unit.Position.X, unit.Position.Y], after = field[destination.X, destination.Y];
            return before == int.MaxValue || after == int.MaxValue ? 0 : (before - after) * profile.Progress;
        }
        internal bool LastEnemyLeader(UnitKey key)
        {
            if (!State.FindUnit(key).IsLeader) return false;
            foreach (var enemy in Enemies) if (enemy.Unit.IsLeader && enemy.Key.Player != key.Player) return false;
            return true;
        }
        internal bool CastleWillSurvive(GridPosition p)
            => !State.BattleRoyaleMode || StormRules.SafeZone(State.Width, State.Height, State.Round + 2,
                State.StormWarmupRounds, State.StormRoundsPerShrink).Contains(p.X, p.Y);
    }
}
