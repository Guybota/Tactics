using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Tactics.Core
{
    public enum SimulationOutcome { Running, Victory, Draw, LimitReached, Cancelled, RejectedAction, NullDecision, Exception }
    [Serializable] public sealed class SimulationLimits
    {
        public int maxRounds = 100, maxActions = 5000;
        public double maxPlanningMilliseconds = 30000;
    }
    [Serializable] public sealed class SimulationActionRecord
    {
        public int index, turn, round, player, actorId, targetPlayer, targetId, x, y, targetX, targetY;
        public MatchActionKind kind;
        public string recruitTypeId, reason, stateHash, error;
        public float score;
        public double planningMilliseconds;
        public int candidates;
        public bool accepted;
    }
    [Serializable] public sealed class SimulationTurnSample
    {
        public int turn, round; public int[] money, units, castles; public bool[] defeated;
        public static SimulationTurnSample Capture(MatchState state)
        {
            var sample = new SimulationTurnSample { turn = state.Turn, round = state.Round,
                money = state.Players.Select(p => p.Money).ToArray(), units = state.Players.Select(p => p.Units.Count(u => u.IsAlive)).ToArray(),
                castles = new int[state.Players.Count], defeated = state.Players.Select(p => p.HasLost).ToArray() };
            foreach (var cell in state.Cells) if (cell.HasCastle && cell.CastleOwner >= 0) sample.castles[cell.CastleOwner]++;
            return sample;
        }
    }
    [Serializable] public sealed class SimulationResult
    {
        public string scenario, detail, initialHash, finalHash;
        public SimulationOutcome outcome;
        public int winner = -1, rounds, turns, actions, decisions, rejectedActions, nullDecisions, repeatedBoards;
        public int unitLosses, leaderLosses, stormDeaths;
        public float stormDamage;
        public int[] actionCounts = new int[6];
        public double planningMilliseconds, maxPlanningMilliseconds, executionMilliseconds, wallMilliseconds;
        public List<SimulationTurnSample> samples = new List<SimulationTurnSample>();
        public List<SimulationActionRecord> lastDecisions = new List<SimulationActionRecord>();
    }

    // Pure core; no scene objects, animation, preferences, files or Unity random.
    // Advance performs one planning step or executes one validated action.
    public sealed class BotSimulation : IDisposable
    {
        private readonly MatchEngine engine;
        private readonly BotPlanner planner;
        private readonly SimulationLimits limits;
        private BotPlanningSession search;
        private double decisionMilliseconds;
        private readonly Stopwatch wall = Stopwatch.StartNew();
        private readonly HashSet<string> boards = new HashSet<string>();
        public MatchState State { get; private set; }
        public SimulationResult Result { get; }
        public bool IsFinished => Result.outcome != SimulationOutcome.Running;
        public Action<SimulationActionRecord> OnDecision;

        public BotSimulation(string scenario, MatchState initial, MatchEngine engine, BotProfile profile = null, SimulationLimits limits = null)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.limits = limits ?? new SimulationLimits();
            if (this.limits.maxRounds < 1 || this.limits.maxActions < 1 || this.limits.maxPlanningMilliseconds <= 0)
                throw new ArgumentException("Simulation limits must be positive.");
            State = initial?.Clone() ?? throw new ArgumentNullException(nameof(initial));
            if (State.Players.Count < 2 || State.Players.Count > 4) throw new ArgumentException("Choose 2–4 participants.");
            planner = new BotPlanner(engine, profile);
            Result = new SimulationResult { scenario = scenario, initialHash = SimulationSnapshot.Fingerprint(State) };
            Result.samples.Add(SimulationTurnSample.Capture(State));
            boards.Add(SimulationSnapshot.Fingerprint(State, true));
        }
        public void Advance()
        {
            if (IsFinished) return;
            try
            {
                if (State.IsOver) { Finish(State.Winner < 0 ? SimulationOutcome.Draw : SimulationOutcome.Victory, "Match engine resolved the outcome."); return; }
                if (State.Round > limits.maxRounds || Result.actions >= limits.maxActions)
                { Finish(SimulationOutcome.LimitReached, "Round or action safety limit reached; outcome is inconclusive."); return; }
                var clock = Stopwatch.StartNew();
                bool advanced;
                try
                {
                    if (search == null) { search = planner.Begin(BotObservation.Capture(State)); decisionMilliseconds = 0; }
                    advanced = search.Advance();
                }
                finally { clock.Stop(); decisionMilliseconds += clock.Elapsed.TotalMilliseconds; }
                if (decisionMilliseconds > limits.maxPlanningMilliseconds)
                { Finish(SimulationOutcome.LimitReached, "Planning safety limit reached; outcome is inconclusive."); return; }
                if (advanced) return;
                var decision = search.Best; int candidates = search.CandidatesEvaluated; search.Dispose(); search = null;
                Result.decisions++; Result.planningMilliseconds += decisionMilliseconds;
                Result.maxPlanningMilliseconds = Math.Max(Result.maxPlanningMilliseconds, decisionMilliseconds);
                if (decision == null) { Result.nullDecisions++; Finish(SimulationOutcome.NullDecision, "Planner returned no decision."); return; }
                var action = decision.Action;
                var record = new SimulationActionRecord { index = Result.decisions, turn = State.Turn, round = State.Round,
                    player = action.Actor.Player, actorId = action.Actor.Id, targetPlayer = action.TargetUnit.Player, targetId = action.TargetUnit.Id,
                    x = action.Destination.X, y = action.Destination.Y, targetX = action.Target.X, targetY = action.Target.Y,
                    kind = action.Kind, recruitTypeId = action.RecruitTypeId, reason = decision.Reason, score = decision.Score,
                    stateHash = SimulationSnapshot.Fingerprint(State), planningMilliseconds = decisionMilliseconds, candidates = candidates };
                clock.Restart();
                var result = engine.Execute(State, action, out var next);
                clock.Stop(); Result.executionMilliseconds += clock.Elapsed.TotalMilliseconds;
                record.accepted = result.Success; record.error = result.Error;
                Result.lastDecisions.Add(record); if (Result.lastDecisions.Count > 32) Result.lastDecisions.RemoveAt(0);
                OnDecision?.Invoke(record);
                if (!result.Success) { Result.rejectedActions++; Finish(SimulationOutcome.RejectedAction, result.Error); return; }
                MeasureLosses(State, next, action);
                Result.actions++; Result.actionCounts[(int)action.Kind]++;
                bool turnEnded = action.Kind == MatchActionKind.EndTurn;
                State = next;
                if (turnEnded)
                {
                    Result.turns++;
                    Result.samples.Add(SimulationTurnSample.Capture(State));
                    if (!boards.Add(SimulationSnapshot.Fingerprint(State, true))) Result.repeatedBoards++;
                }
                if (State.IsOver) Finish(State.Winner < 0 ? SimulationOutcome.Draw : SimulationOutcome.Victory, "Match engine resolved the outcome.");
            }
            catch (Exception exception) { Finish(SimulationOutcome.Exception, exception.ToString()); }
        }
        private void MeasureLosses(MatchState before, MatchState after, MatchAction action)
        {
            bool stormTick = action.Kind == MatchActionKind.EndTurn && before.BattleRoyaleMode
                && TurnRules.Round(TurnRules.NextActiveTurn(before.Turn, before.Players.Select(p => p.HasLost).ToArray()), before.Players.Count) != before.Round;
            for (int p = 0; p < before.Players.Count; p++) foreach (var unit in before.Players[p].Units)
            {
                if (!unit.IsAlive) continue;
                var next = after.FindUnit(new UnitKey(p, unit.Id));
                if (next != null && !next.IsAlive) { Result.unitLosses++; if (unit.IsLeader) Result.leaderLosses++; }
                // Count direct storm damage only, excluding army elimination after leader death.
                if (stormTick && !before.Players[p].HasLost && before.Cells[unit.Position.X, unit.Position.Y].HasStorm)
                {
                    float damage = Math.Min(unit.Health, 2 + before.Round * .25f);
                    Result.stormDamage += damage;
                    if (damage >= unit.Health) Result.stormDeaths++;
                }
            }
        }
        private void Finish(SimulationOutcome outcome, string detail)
        {
            search?.Dispose(); search = null; wall.Stop();
            Result.outcome = outcome; Result.detail = detail; Result.rounds = State.Round;
            Result.winner = outcome == SimulationOutcome.Victory ? State.Winner : -1;
            Result.finalHash = SimulationSnapshot.Fingerprint(State); Result.wallMilliseconds = wall.Elapsed.TotalMilliseconds;
            Result.samples.Add(SimulationTurnSample.Capture(State));
        }
        public void Cancel() { if (!IsFinished) Finish(SimulationOutcome.Cancelled, "Cancelled by the user; partial diagnostics preserved."); }
        public void Dispose() { Cancel(); }
    }
}
