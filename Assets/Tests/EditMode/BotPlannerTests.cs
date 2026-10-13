using System;
using System.Linq;
using NUnit.Framework;
using Tactics.Core;

public class BotPlannerTests
{
    private MatchEngine engine;
    private BotPlanner planner;
    [SetUp]
    public void SetUp()
    {
        var content = new MatchContent();
        content.AddUnit(new UnitTypeStats("leader", 20, 5, 3, 1, 0));
        content.AddUnit(new UnitTypeStats("soldier", 10, 5, 3, 1, 250));
        content.AddUnit(new UnitTypeStats("archer", 10, 6, 3, 2, 500));
        content.AddRace("race", new[] { "soldier", "archer" });
        foreach (var a in content.UnitTypes) foreach (var d in content.UnitTypes) content.SetDamage(a.Id, d.Id, 55);
        engine = new MatchEngine(content); planner = new BotPlanner(engine);
    }
    private static UnitState Unit(int id, int x, int y, bool leader = false) => new UnitState
    {
        Id = id, Position = new GridPosition(x, y), TypeId = leader ? "leader" : "soldier",
        Health = leader ? 20 : 10, MaxHealth = leader ? 20 : 10, Attack = 5, Movement = 3, Range = 1, IsLeader = leader
    };
    private static MatchState State(int players = 2, int size = 8)
    {
        var state = new MatchState(size, size);
        var points = new[] { new GridPosition(1, 1), new GridPosition(size - 2, size - 2),
            new GridPosition(1, size - 2), new GridPosition(size - 2, 1) };
        for (int p = 0; p < players; p++)
        {
            var player = new PlayerState { RaceId = "race", Money = 4000, NextUnitId = 2 };
            player.Units.Add(Unit(1, points[p].X, points[p].Y, true)); state.Players.Add(player);
        }
        return state;
    }
    private BotDecision Choose(MatchState state)
    {
        var decision = planner.Choose(BotObservation.Capture(state));
        Assert.That(decision, Is.Not.Null);
        Assert.That(engine.Validate(state, decision.Action).Success, Is.True);
        return decision;
    }
    private static void Castle(MatchState s, int x, int y, int owner = -1)
    { s.Cells[x, y].HasCastle = true; s.Cells[x, y].CastleOwner = owner; }
    private static string Signature(BotDecision d) => $"{d.Action.Kind}:{d.Action.Actor.Player}:{d.Action.Actor.Id}:{d.Action.Destination}:{d.Action.TargetUnit.Player}:{d.Action.TargetUnit.Id}:{d.Action.Target}:{d.Action.RecruitTypeId}:{d.Score}";

    [Test]
    public void SameObservationIgnoresEveryMatchLuckSeedAndDoesNotChangeInput()
    {
        var state = State(); state.Players[0].Units.Add(Unit(2, 4, 5)); state.Players[0].NextUnitId = 3;
        string expected = Signature(Choose(state));
        foreach (uint seed in new uint[] { 1, 999, uint.MaxValue, 172345 })
        {
            state.RandomState = seed;
            Assert.That(Signature(Choose(state)), Is.EqualTo(expected));
            Assert.That(state.RandomState, Is.EqualTo(seed));
            Assert.That(state.Players[0].Units[1].Position, Is.EqualTo(new GridPosition(4, 5)));
            Assert.That(state.Players[0].Units[1].HasActed, Is.False);
        }
    }
    [Test]
    public void ObservationIsIsolatedAndIncrementalSearchMatchesSynchronousDecision()
    {
        var state = State(); Castle(state, 2, 2);
        var observation = BotObservation.Capture(state);
        string expected = Signature(planner.Choose(observation));
        state.Players[0].Units.Clear(); state.Players[0].Money = 0; state.Cells[2, 2].HasCastle = false;
        using (var search = planner.Begin(observation))
        {
            int frames = 0;
            while (!search.IsComplete) { search.Advance(); frames++; Assert.That(frames, Is.LessThan(100000)); }
            Assert.That(Signature(search.Best), Is.EqualTo(expected));
            Assert.That(search.CandidatesEvaluated, Is.GreaterThan(1));
        }
    }
    [Test]
    public void CapturesReachableCastleAndConsumesAction()
    {
        var state = State(); Castle(state, 2, 2);
        var action = Choose(state).Action;
        Assert.That(action.Kind, Is.EqualTo(MatchActionKind.Capture));
        Assert.That(engine.Execute(state, action, out var next).Success, Is.True);
        Assert.That(next.Cells[2, 2].CastleOwner, Is.EqualTo(0));
        Assert.That(next.FindUnit(action.Actor).HasActed, Is.True);
    }
    [Test]
    public void RecruitsLegalAffordableUnitWithoutChangingItsStatsOrGivingExtraMoney()
    {
        var state = State(); state.Players[0].Units[0].HasActed = true; Castle(state, 2, 2, 0);
        var action = Choose(state).Action;
        Assert.That(action.Kind, Is.EqualTo(MatchActionKind.Recruit));
        Assert.That(engine.Execute(state, action, out var next).Success, Is.True);
        Assert.That(next.Players[0].Money, Is.EqualTo(4000 - (action.RecruitTypeId == "soldier" ? 250 : 500)));
        var recruit = next.FindUnit(new UnitKey(0, 2));
        Assert.That(recruit.HasActed, Is.True); Assert.That(recruit.MaxHealth, Is.EqualTo(10));
    }
    [Test]
    public void BuildsFirstCastleWhenConstructionIsEnabledAndNeverWhenDisabled()
    {
        var state = State(); state.BuildMode = true;
        Assert.That(Choose(state).Action.Kind, Is.EqualTo(MatchActionKind.BuildCastle));
        state.BuildMode = false;
        Assert.That(Choose(state).Action.Kind, Is.Not.EqualTo(MatchActionKind.BuildCastle));
    }
    [Test]
    public void ChoosesWinningAttackAcrossAllUnitsBeforeRoutineCapture()
    {
        var state = State(); Castle(state, 2, 2);
        state.Players[0].Units.Add(Unit(2, 5, 5)); state.Players[0].NextUnitId = 3;
        state.Players[1].Units[0].Health = 1;
        var action = Choose(state).Action;
        Assert.That(action.Kind, Is.EqualTo(MatchActionKind.Attack)); Assert.That(action.Actor.Id, Is.EqualTo(2));
        Assert.That(engine.Execute(state, action, out var next).Success, Is.True);
        Assert.That(next.IsOver, Is.True); Assert.That(next.Winner, Is.EqualTo(0));
    }
    [Test]
    public void LeaderAvoidsLethalCounterattackAndRetreatsFromThreat()
    {
        var state = State(); state.Players[0].Units[0].Position = new GridPosition(3, 3);
        state.Players[0].Units[0].Health = 1; state.Players[1].Units[0].Position = new GridPosition(4, 3);
        state.Players[1].Units[0].Movement = 0;
        var action = Choose(state).Action;
        Assert.That(action.Kind, Is.EqualTo(MatchActionKind.Wait));
        Assert.That(action.Destination.DistanceTo(new GridPosition(4, 3)), Is.GreaterThan(1));
    }
    [Test]
    public void UsesRangedAttackAndEvaluatesEnemiesEvenIfTheyAlreadyActed()
    {
        var state = State(); state.Players[0].Units[0].HasActed = true;
        var archer = Unit(2, 4, 6); archer.TypeId = "archer"; archer.Range = 2; archer.Movement = 0;
        state.Players[0].Units.Add(archer); state.Players[0].NextUnitId = 3;
        state.Players[1].Units[0].HasActed = true; state.Players[1].Units[0].Movement = 0;
        var action = Choose(state).Action;
        Assert.That(action.Kind, Is.EqualTo(MatchActionKind.Attack));
        Assert.That(action.Destination.DistanceTo(state.Players[1].Units[0].Position), Is.EqualTo(2));
    }
    [Test]
    public void LeaderDoesNotTreatItsVacatedCorridorAsABarrierAgainstEnemyPursuit()
    {
        var state = State();
        foreach (var cell in state.Cells) cell.Terrain = TerrainKind.Mountain;
        for (int y = 0; y < 8; y++) state.Cells[3, y].Terrain = TerrainKind.Grass;
        for (int x = 4; x <= 6; x++) state.Cells[x, 3].Terrain = TerrainKind.Grass;
        state.Cells[6, 4].Terrain = TerrainKind.Grass;
        var leader = state.Players[0].Units[0]; leader.Position = new GridPosition(3, 3); leader.Health = 1; leader.Movement = 4;
        state.Players[1].Units[0].Position = new GridPosition(3, 1); state.Players[1].Units[0].Movement = 4;
        Castle(state, 3, 6);
        var action = Choose(state).Action;
        Assert.That(action.Kind, Is.EqualTo(MatchActionKind.Wait));
        Assert.That(engine.Execute(state, action, out var next).Success, Is.True);
        var pursuit = engine.Movement(next, new UnitKey(1, 1));
        Assert.That(pursuit.Reachable.Any(p => pursuit.CanStop(p) && p.DistanceTo(action.Destination) == 1), Is.False,
            "The enemy can pursue through the tile the leader just vacated.");
    }
    [Test]
    public void LeavesCurrentStormAndAnticipatesNextShrink()
    {
        var state = State(2, 12); state.BattleRoyaleMode = true; state.Turn = 4;
        state.Players[0].Units[0].Position = new GridPosition(0, 4);
        state.Players[1].Units[0].Position = new GridPosition(10, 10);
        state.Cells[0, 4].HasStorm = true;
        var action = Choose(state).Action;
        Assert.That(action.Kind, Is.EqualTo(MatchActionKind.Wait));
        var nextSafe = StormRules.SafeZone(12, 12, state.Round + 1);
        Assert.That(nextSafe.Contains(action.Destination.X, action.Destination.Y), Is.True);
    }
    [Test]
    public void NavigatesAroundMountainBarrierUsingRealPathCost()
    {
        var state = State(2, 10); Castle(state, 7, 1);
        state.Players[1].Units[0].HasActed = true;
        for (int y = 0; y < 7; y++) state.Cells[3, y].Terrain = TerrainKind.Mountain;
        var action = Choose(state).Action;
        Assert.That(action.Kind, Is.EqualTo(MatchActionKind.Wait));
        Assert.That(action.Destination.Y, Is.GreaterThan(1));
        Assert.That(engine.Movement(state, action.Actor).CanStop(action.Destination), Is.True);
    }
    [Test]
    public void EndTurnWhenNothingCanActAndNoRecruitmentIsPossible()
    {
        var state = State(); state.Players[0].Units[0].HasActed = true;
        Assert.That(Choose(state).Action.Kind, Is.EqualTo(MatchActionKind.EndTurn));
        state.IsOver = true; Assert.That(planner.Choose(BotObservation.Capture(state)), Is.Null);
    }
    [Test]
    public void LargeBoardPlanningIsBoundedAndCanBeCancelledWithoutChangingState()
    {
        var state = State(4, 50);
        for (int p = 0; p < 4; p++)
        {
            for (int id = 2; id <= 9; id++) state.Players[p].Units.Add(Unit(id, 3 + (id - 2) % 4 + (p % 2) * 30,
                3 + (id - 2) / 4 + (p / 2) * 30));
            state.Players[p].NextUnitId = 10;
        }
        Castle(state, 20, 20); Castle(state, 25, 25); Castle(state, 30, 30);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        using (var search = planner.Begin(BotObservation.Capture(state)))
        {
            int work = 0;
            while (search.Advance()) Assert.That(++work, Is.LessThan(100000));
            Assert.That(engine.Validate(state, search.Best.Action).Success, Is.True);
            TestContext.WriteLine($"50x50, 36 units: {timer.ElapsedMilliseconds}ms, {work} work steps, {search.CandidatesEvaluated} candidates.");
        }
        using (var interrupted = planner.Begin(BotObservation.Capture(state)))
        { interrupted.Advance(); interrupted.Dispose(); Assert.That(interrupted.Advance(), Is.False); }
        Assert.That(state.Players[0].Units.All(u => !u.HasActed), Is.True);
    }
    [TestCase(2, false, false)] [TestCase(3, false, false)] [TestCase(4, false, false)]
    [TestCase(2, true, false)] [TestCase(3, true, false)] [TestCase(4, true, false)]
    [TestCase(2, true, true)] [TestCase(3, true, true)] [TestCase(4, true, true)]
    public void AutomatedGamesRemainLegalAndReachAnOutcome(int players, bool build, bool storm)
    {
        var state = State(players, 10); state.BuildMode = build; state.BattleRoyaleMode = storm;
        state.RandomState = (uint)(12345 + players);
        if (!build) { Castle(state, 2, 2); Castle(state, 7, 7); Castle(state, 2, 7); Castle(state, 7, 2); }
        int actions = 0;
        while (!state.IsOver && state.Round <= 100 && actions < 5000)
        {
            var action = Choose(state).Action;
            Assert.That(engine.Execute(state, action, out var next).Success, Is.True);
            Assert.That(next.Players.All(p => p.Money >= 0), Is.True);
            state = next; actions++;
        }
        TestContext.WriteLine($"players={players} build={build} storm={storm}: actions={actions}, round={state.Round}, winner={state.Winner}");
        if (!state.IsOver)
            for (int p = 0; p < state.Players.Count; p++)
                TestContext.WriteLine($"player {p}: " + string.Join("; ", state.Players[p].Units.Where(u => u.IsAlive).Select(u => $"{u.Id}:{u.TypeId}@{u.Position} HP={u.Health}")));
        Assert.That(state.IsOver, Is.True, "Bot match stalled before reaching a victory or draw.");
    }
}
