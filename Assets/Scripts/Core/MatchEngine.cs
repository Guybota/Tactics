using System;
using System.Collections.Generic;
using System.Linq;

namespace Tactics.Core
{
    public sealed class MatchEngine
    {
        private readonly MatchContent content;
        internal MatchContent Content => content;
        public MatchEngine(MatchContent content)
        {
            this.content = content ?? throw new ArgumentNullException(nameof(content));
            content.Validate();
        }

        public MovementResult Movement(MatchState state, UnitKey key)
            => Movement(state, key, -1);

        // Threats include routes that could open when the active player's units move away.
        internal MovementResult ThreatMovement(MatchState state, UnitKey enemy)
            => Movement(state, enemy, state.ActivePlayer);

        private MovementResult Movement(MatchState state, UnitKey key, int ignoreOccupantsOfPlayer)
        {
            var unit = state.FindUnit(key) ?? throw new ArgumentException("Unknown unit.");
            var board = new MovementCell[state.Width, state.Height];
            var occupants = new Dictionary<GridPosition, int>();
            for (int player = 0; player < state.Players.Count; player++)
                foreach (var other in state.Players[player].Units)
                    if (other.IsAlive && other != unit && player != ignoreOccupantsOfPlayer) occupants.Add(other.Position, player);
            for (int x = 0; x < state.Width; x++) for (int y = 0; y < state.Height; y++)
            {
                var p = new GridPosition(x, y); var cell = state.Cells[x, y];
                var occupant = occupants.TryGetValue(p, out int owner)
                    ? owner == key.Player ? CellOccupant.Ally : CellOccupant.Enemy : CellOccupant.Empty;
                board[x, y] = new MovementCell(Math.Max(1, cell.MovementCost), cell.Terrain == TerrainKind.Mountain, occupant);
            }
            return MovementRules.Calculate(board, unit.Position, unit.Movement);
        }

        private static Combatant Stats(MatchState state, UnitState unit, GridPosition position)
            => new Combatant(unit.Health, unit.MaxHealth, unit.Attack, unit.Range, state.Cells[position.X, position.Y].DefenseStars);

        public CombatPrediction Predict(MatchState state, UnitKey actor, UnitKey target, GridPosition destination)
        {
            var validation = Validate(state, new MatchAction(MatchActionKind.Attack, actor, destination, target));
            if (!validation.Success) throw new InvalidOperationException(validation.Error);
            return Forecast(state, actor, target, destination);
        }

        // Internal forecasts are used only for actions generated against this same snapshot.
        internal CombatPrediction Forecast(MatchState state, UnitKey actor, UnitKey target, GridPosition destination)
        {
            var a = state.FindUnit(actor); var d = state.FindUnit(target);
            return CombatRules.Predict(Stats(state, a, destination), Stats(state, d, d.Position), destination.DistanceTo(d.Position),
                content.BaseDamage(a, d), content.BaseDamage(d, a));
        }

        internal IEnumerable<MatchAction> LegalUnitActions(MatchState state, UnitKey key, MovementResult movement)
        {
            var unit = state.FindUnit(key);
            if (unit == null || !unit.IsAlive || unit.HasActed || key.Player != state.ActivePlayer) yield break;
            foreach (var position in movement.Reachable)
            {
                if (!movement.CanStop(position)) continue;
                yield return new MatchAction(MatchActionKind.Wait, key, position);
                var capture = new MatchAction(MatchActionKind.Capture, key, position);
                if (Validate(state, capture, movement).Success) yield return capture;
                for (int player = 0; player < state.Players.Count; player++)
                    if (player != key.Player && !state.Players[player].HasLost)
                        foreach (var target in state.Players[player].Units)
                        {
                            if (!target.IsAlive) continue;
                            var attack = new MatchAction(MatchActionKind.Attack, key, position, new UnitKey(player, target.Id));
                            if (Validate(state, attack, movement).Success) yield return attack;
                        }
                if (!state.BuildMode || !unit.IsLeader || state.Players[key.Player].Money < 3500) continue;
                for (int direction = 0; direction < 4; direction++)
                {
                    int dx = direction == 0 ? -1 : direction == 1 ? 1 : 0;
                    int dy = direction == 2 ? -1 : direction == 3 ? 1 : 0;
                    var build = new MatchAction(MatchActionKind.BuildCastle, key, position, target: new GridPosition(position.X + dx, position.Y + dy));
                    if (Validate(state, build, movement).Success) yield return build;
                }
            }
        }

        public ActionResult Validate(MatchState state, MatchAction action)
            => Validate(state, action, null);

        private ActionResult Validate(MatchState state, MatchAction action, MovementResult movement)
        {
            if (state == null || action == null || state.Players.Count < 2 || state.Players.Count > 4 || state.Turn < 0)
                return Fail("Invalid match or action.");
            if (state.IsOver) return Fail("The match is over.");
            if (action.Actor.Player != state.ActivePlayer)
                return Fail("Only the active player can act.");
            if (action.Kind == MatchActionKind.EndTurn) return new ActionResult(true);
            if (state.Players[state.ActivePlayer].HasLost) return Fail("Eliminated players cannot act.");
            if (!state.Contains(action.Destination)) return Fail("Destination is outside the map.");
            var player = state.Players[action.Actor.Player];
            if (action.Kind == MatchActionKind.Recruit)
            {
                var castle = state.Cells[action.Destination.X, action.Destination.Y];
                if (!castle.HasCastle || castle.HasStorm || castle.CastleOwner != action.Actor.Player || state.UnitAt(action.Destination) != null)
                    return Fail("Recruitment requires an empty owned castle.");
                if (!content.CanRecruit(player.RaceId, action.RecruitTypeId) || !content.TryUnit(action.RecruitTypeId, out var type))
                    return Fail("This race cannot recruit that unit.");
                if (player.Money < type.Cost || player.NextUnitId >= int.MaxValue || player.Units.Exists(u => u.Id >= int.MaxValue - 1))
                    return Fail("Recruitment is not affordable or no unit IDs remain.");
                return new ActionResult(true);
            }
            var unit = state.FindUnit(action.Actor);
            if (unit == null || !unit.IsAlive || unit.HasActed) return Fail("Unit cannot act.");
            if (!unit.Position.Equals(action.Destination) && !(movement ?? Movement(state, action.Actor)).CanStop(action.Destination))
                return Fail("Destination is occupied or unreachable.");
            switch (action.Kind)
            {
                case MatchActionKind.Wait: return new ActionResult(true);
                case MatchActionKind.Attack:
                    var enemy = state.FindUnit(action.TargetUnit);
                    if (enemy == null || !enemy.IsAlive || action.TargetUnit.Player == action.Actor.Player
                        || state.Players[action.TargetUnit.Player].HasLost) return Fail("Invalid enemy target.");
                    if (!content.TryUnit(unit.TypeId, out _) || !content.TryUnit(enemy.TypeId, out _)) return Fail("Unknown combat unit type.");
                    int distance = action.Destination.DistanceTo(enemy.Position);
                    return distance >= 1 && distance <= unit.Range ? new ActionResult(true) : Fail("Enemy is outside attack range.");
                case MatchActionKind.Capture:
                    var capture = state.Cells[action.Destination.X, action.Destination.Y];
                    return capture.HasCastle && !capture.HasStorm && capture.CastleOwner != action.Actor.Player
                        ? new ActionResult(true) : Fail("There is no capturable castle.");
                case MatchActionKind.BuildCastle:
                    if (!state.BuildMode || !unit.IsLeader || player.Money < 3500 || !state.Contains(action.Target)
                        || action.Destination.DistanceTo(action.Target) != 1) return Fail("Leader cannot build here.");
                    var build = state.Cells[action.Target.X, action.Target.Y];
                    var buildOccupant = state.UnitAt(action.Target);
                    if (build.Terrain != TerrainKind.Grass || !build.SupportsCastle || build.HasCastle || build.HasStorm
                        || (buildOccupant != null && (buildOccupant != unit || unit.Position.Equals(action.Destination)))
                        || action.Target.X == 0 || action.Target.Y == 0
                        || action.Target.X == state.Width - 1 || action.Target.Y == state.Height - 1)
                        return Fail("Invalid castle location.");
                    for (int x = Math.Max(0, action.Target.X - 2); x <= Math.Min(state.Width - 1, action.Target.X + 2); x++)
                        for (int y = Math.Max(0, action.Target.Y - 2); y <= Math.Min(state.Height - 1, action.Target.Y + 2); y++)
                            if (state.Cells[x, y].HasCastle) return Fail("Too close to another castle.");
                    for (int other = 0; other < state.Players.Count; other++)
                        if (other != action.Actor.Player) foreach (var enemyUnit in state.Players[other].Units)
                            if (enemyUnit.IsAlive && Math.Abs(enemyUnit.Position.X - action.Target.X) <= 2 && Math.Abs(enemyUnit.Position.Y - action.Target.Y) <= 2)
                                return Fail("Too close to an enemy.");
                    return new ActionResult(true);
                default: return Fail("Unknown action.");
            }
        }

        public HashSet<GridPosition> BuildTargets(MatchState state, UnitKey actor, GridPosition destination)
        {
            var targets = new HashSet<GridPosition>();
            var unit = state.FindUnit(actor);
            if (unit == null || !unit.IsAlive || !state.Contains(destination)) return targets;
            var movement = Movement(state, actor);
            int[] dx = { -1, 1, 0, 0 }, dy = { 0, 0, -1, 1 };
            for (int index = 0; index < 4; index++)
            {
                var target = new GridPosition(destination.X + dx[index], destination.Y + dy[index]);
                var action = new MatchAction(MatchActionKind.BuildCastle, actor, destination, target: target);
                if (Validate(state, action, movement).Success) targets.Add(target);
            }
            return targets;
        }

        public ActionResult ValidateBuildStart(MatchState state, UnitKey actor, GridPosition destination)
        {
            var movement = Validate(state, new MatchAction(MatchActionKind.Wait, actor, destination));
            if (!movement.Success) return movement;
            var unit = state.FindUnit(actor);
            return state.BuildMode && unit.IsLeader && state.Players[actor.Player].Money >= 3500
                ? new ActionResult(true) : Fail("Leader cannot start construction.");
        }

        // Returns a new state. Rejection changes neither the input state nor its random stream.
        public ActionResult Execute(MatchState state, MatchAction action, out MatchState next)
        {
            next = null;
            var valid = Validate(state, action);
            if (!valid.Success) return valid;
            next = state.Clone();
            if (action.Kind == MatchActionKind.EndTurn) { EndTurn(next); return valid; }
            var player = next.Players[action.Actor.Player];
            if (action.Kind == MatchActionKind.Recruit)
            {
                var type = content.Unit(action.RecruitTypeId);
                int id = Math.Max(player.NextUnitId, player.Units.Count == 0 ? 1 : player.Units.Max(u => u.Id) + 1);
                player.NextUnitId = checked(id + 1);
                player.Units.Add(new UnitState { Id = id, TypeId = type.Id, Position = action.Destination,
                    Health = type.Health, MaxHealth = type.Health, Attack = type.Attack, Movement = type.Movement,
                    Range = type.Range, HasActed = true });
                player.Money -= type.Cost;
                return valid;
            }
            var unit = next.FindUnit(action.Actor);
            unit.Position = action.Destination;
            unit.HasActed = true;
            switch (action.Kind)
            {
                case MatchActionKind.Attack:
                    var target = next.FindUnit(action.TargetUnit);
                    int attackLuck = NextLuck(next), counterLuck = NextLuck(next);
                    var result = CombatRules.Resolve(Stats(next, unit, unit.Position), Stats(next, target, target.Position),
                        unit.Position.DistanceTo(target.Position), content.BaseDamage(unit, target), content.BaseDamage(target, unit), attackLuck, counterLuck);
                    unit.Health = result.AttackerHealth; target.Health = result.DefenderHealth;
                    unit.IsDead = unit.Health <= 0; target.IsDead = target.Health <= 0;
                    break;
                case MatchActionKind.Capture:
                    next.Cells[unit.Position.X, unit.Position.Y].CastleOwner = action.Actor.Player;
                    break;
                case MatchActionKind.BuildCastle:
                    var tile = next.Cells[action.Target.X, action.Target.Y];
                    tile.HasCastle = true; tile.CastleOwner = action.Actor.Player; player.Money -= 3500;
                    break;
            }
            ResolveDefeats(next);
            return valid;
        }

        private static ActionResult Fail(string message) => new ActionResult(false, message);
        private static int NextLuck(MatchState state)
        {
            uint value = state.RandomState == 0 ? 0x6D2B79F5 : state.RandomState;
            value ^= value << 13; value ^= value >> 17; value ^= value << 5;
            state.RandomState = value;
            return (int)(value % 10);
        }
        public static void ResolveDefeats(MatchState state)
        {
            int remaining = 0, winner = -1;
            for (int index = 0; index < state.Players.Count; index++)
            {
                var player = state.Players[index];
                var leader = player.Units.Find(u => u.IsLeader);
                if (leader == null || !leader.IsAlive) player.HasLost = true;
                if (!player.HasLost) { remaining++; winner = index; continue; }
                foreach (var unit in player.Units) { unit.IsDead = true; unit.Health = 0; }
                foreach (var cell in state.Cells) if (cell.CastleOwner == index) cell.CastleOwner = -1;
            }
            state.IsOver = remaining <= 1;
            state.Winner = remaining == 1 ? winner : -1;
        }
        private static void EndTurn(MatchState state)
        {
            ResolveDefeats(state);
            if (state.IsOver) return;
            var lost = state.Players.Select(p => p.HasLost).ToArray();
            int next = TurnRules.NextActiveTurn(state.Turn, lost);
            if (state.BattleRoyaleMode && TurnRules.Round(next, state.Players.Count) != state.Round)
            {
                float damage = 2 + state.Round * .25f;
                foreach (var player in state.Players) foreach (var unit in player.Units)
                    if (!player.HasLost && unit.IsAlive && state.Cells[unit.Position.X, unit.Position.Y].HasStorm)
                    {
                        unit.Health = Math.Max(0, unit.Health - damage); unit.IsDead = unit.Health <= 0;
                    }
                ResolveDefeats(state);
                if (state.IsOver) return;
                next = TurnRules.NextActiveTurn(state.Turn, state.Players.Select(p => p.HasLost).ToArray());
            }
            state.Turn = next;
            BeginTurn(state);
        }
        public static void BeginTurn(MatchState state)
        {
            if (state.BattleRoyaleMode)
            {
                var safe = StormRules.SafeZone(state.Width, state.Height, state.Round, state.StormWarmupRounds, state.StormRoundsPerShrink);
                for (int x = 0; x < state.Width; x++) for (int y = 0; y < state.Height; y++)
                    if (!safe.Contains(x, y)) { var cell = state.Cells[x, y]; cell.HasStorm = true; cell.HasCastle = false; cell.CastleOwner = -1; }
            }
            var player = state.Players[state.ActivePlayer];
            int income = 500;
            foreach (var cell in state.Cells) if (cell.HasCastle && cell.CastleOwner == state.ActivePlayer) income = checked(income + 500);
            player.Money = checked(player.Money + income);
            foreach (var unit in player.Units)
            {
                if (!unit.IsAlive) continue;
                unit.HasActed = false;
                var cell = state.Cells[unit.Position.X, unit.Position.Y];
                if (cell.HasCastle && cell.CastleOwner == state.ActivePlayer) unit.Health = Math.Min(unit.MaxHealth, unit.Health + unit.MaxHealth * .3f);
            }
        }
    }
}
