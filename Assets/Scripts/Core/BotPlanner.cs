using System;
using System.Collections.Generic;
using System.Linq;

namespace Tactics.Core
{
    public sealed class BotPlanner
    {
        private readonly MatchEngine engine;
        private readonly BotProfile profile;
        public BotPlanner(MatchEngine engine, BotProfile profile = null)
        { this.engine = engine ?? throw new ArgumentNullException(nameof(engine)); this.profile = (profile ?? BotProfile.Normal).Copy(); }
        public BotPlanningSession Begin(BotObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            return new BotPlanningSession(Candidates(observation.State));
        }
        public BotDecision Choose(BotObservation observation)
        {
            using (var search = Begin(observation))
            { while (search.Advance()) { } return search.Best; }
        }
        private IEnumerable<BotDecision> Candidates(MatchState state)
        {
            if (state.IsOver) yield break;
            yield return new BotDecision(MatchAction.EndTurn(state.ActivePlayer), 0, "No beneficial action remains.");
            if (state.Players[state.ActivePlayer].HasLost) yield break;
            var analysis = new BotAnalysis(engine, state, profile);
            foreach (var step in analysis.Prepare()) yield return step;
            foreach (var unit in state.Players[state.ActivePlayer].Units.OrderBy(u => u.Id))
            {
                if (!unit.IsAlive || unit.HasActed) continue;
                var key = new UnitKey(state.ActivePlayer, unit.Id);
                var movement = engine.Movement(state, key);
                yield return null;
                float originalSafety = analysis.PositionScore(key, unit.Position);
                foreach (var action in engine.LegalUnitActions(state, key, movement))
                {
                    float score = analysis.PositionScore(key, action.Destination) - originalSafety
                        + analysis.Progress(unit, action.Destination) - 1;
                    string reason = "Advance towards an objective or improve safety.";
                    if (!action.Destination.Equals(unit.Position) && state.Cells[unit.Position.X, unit.Position.Y].HasCastle
                        && state.Cells[unit.Position.X, unit.Position.Y].CastleOwner == key.Player) score += 30;
                    switch (action.Kind)
                    {
                        case MatchActionKind.Attack:
                            var enemy = state.FindUnit(action.TargetUnit);
                            var prediction = analysis.Predict(key, action.TargetUnit, action.Destination);
                            // Midpoints are heuristics, not a peek at the actual random outcome.
                            float remaining = (prediction.MinDefenderHealth + prediction.MaxDefenderHealth) * .5f;
                            float ownHealth = unit.IsLeader ? prediction.MinAttackerHealth
                                : (prediction.MinAttackerHealth + prediction.MaxAttackerHealth) * .5f;
                            score = analysis.PositionScore(key, action.Destination, ownHealth, action.TargetUnit, remaining / enemy.Health)
                                - originalSafety + analysis.Progress(unit, action.Destination);
                            score += (enemy.Health - remaining) / enemy.MaxHealth * analysis.Material(enemy);
                            score -= (unit.Health - ownHealth) / unit.MaxHealth * analysis.Material(unit) * (unit.IsLeader ? 1.25f : 1);
                            if (prediction.MaxDefenderHealth <= 0) score += enemy.IsLeader ? 10000 : analysis.Material(enemy) * .4f;
                            if (enemy.IsLeader && prediction.MaxDefenderHealth <= 0 && analysis.LastEnemyLeader(action.TargetUnit))
                                score += prediction.MinAttackerHealth > 0 ? 2000000 : 10000;
                            if (unit.IsLeader && prediction.MinAttackerHealth <= 0) score -= 1000000;
                            reason = "Compare damage, counterattack and exposure after combat.";
                            break;
                        case MatchActionKind.Capture:
                            score += profile.Capture * (analysis.CastleWillSurvive(action.Destination) ? 1 : .2f);
                            reason = "Capture income and a recruitment or healing position.";
                            break;
                        case MatchActionKind.BuildCastle:
                            score += analysis.CastleWillSurvive(action.Target) ? profile.Build / (1 + analysis.OwnedCastles.Count * .5f) : -1000;
                            reason = "Establish a castle with enough time to provide value.";
                            break;
                    }
                    yield return new BotDecision(action, score, reason);
                }
            }
            var player = state.Players[state.ActivePlayer];
            foreach (var castle in analysis.OwnedCastles)
                foreach (var type in engine.Content.UnitTypes.OrderBy(t => t.Id, StringComparer.Ordinal))
                {
                    var action = MatchAction.Recruit(state.ActivePlayer, castle, type.Id);
                    if (!engine.Validate(state, action).Success) continue;
                    float desiredArmy = 3 + analysis.Enemies.Count * profile.ArmyPerEnemy;
                    float quality = (type.Attack * type.Range + type.Movement) * 100f / Math.Max(100, type.Cost);
                    float score = profile.Recruit + quality * 30 - type.Cost * .015f;
                    if (analysis.OwnArmy >= desiredArmy) score -= 1000;
                    if (!analysis.CastleWillSurvive(castle)) score -= 1000;
                    // Newly recruited units cannot escape or fight until the next own turn.
                    var recruit = new UnitState { TypeId = type.Id, Health = type.Health, MaxHealth = type.Health,
                        Attack = type.Attack, Range = type.Range };
                    float incoming = 0;
                    foreach (var enemy in analysis.Enemies)
                        if (enemy.Threatens[castle.X, castle.Y])
                            incoming += CombatRules.Resolve(new Combatant(enemy.Unit.Health, enemy.Unit.MaxHealth, enemy.Unit.Attack, enemy.Unit.Range),
                                new Combatant(type.Health, type.Health, type.Attack, type.Range, 3), 1,
                                engine.Content.BaseDamage(enemy.Unit, recruit), 0, 9, 0).Damage;
                    score -= incoming / type.Health * Math.Max(300, type.Cost) * profile.Threat;
                    yield return new BotDecision(action, score, "Recruit an affordable useful unit that can survive until its next turn.");
                }
        }
    }
}
