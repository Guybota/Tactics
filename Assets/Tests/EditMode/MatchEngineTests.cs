using System;
using System.Linq;
using NUnit.Framework;
using Tactics.Core;

public class MatchEngineTests
{
    private MatchEngine engine;
    [SetUp]
    public void SetUp()
    {
        var content = new MatchContent();
        content.AddUnit(new UnitTypeStats("leader", 20, 5, 4, 1, 1000));
        content.AddUnit(new UnitTypeStats("soldier", 10, 4, 3, 2, 250));
        content.AddRace("race", new[] { "soldier" });
        foreach (var a in content.UnitTypes) foreach (var d in content.UnitTypes) content.SetDamage(a.Id, d.Id, 55);
        engine = new MatchEngine(content);
    }
    private static UnitState Leader(int x, int y) => new UnitState { Id = 1, TypeId = "leader", IsLeader = true,
        Health = 20, MaxHealth = 20, Attack = 5, Movement = 4, Range = 1, Position = new GridPosition(x, y) };
    private static MatchState State(int players = 2)
    {
        var state = new MatchState(8, 8) { RandomState = 12345 };
        var positions = new[] { new GridPosition(1, 1), new GridPosition(6, 6), new GridPosition(1, 6), new GridPosition(6, 1) };
        for (int p = 0; p < players; p++)
        {
            var player = new PlayerState { RaceId = "race", Money = 5000, NextUnitId = 2 };
            player.Units.Add(Leader(positions[p].X, positions[p].Y)); state.Players.Add(player);
        }
        return state;
    }
    private MatchState Execute(MatchState state, MatchAction action)
    {
        var result = engine.Execute(state, action, out var next);
        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(next, Is.Not.Null);
        return next;
    }
    private static MatchAction Wait(int player, int unit, int x, int y)
        => new MatchAction(MatchActionKind.Wait, new UnitKey(player, unit), new GridPosition(x, y));
    private static void Castle(MatchState state, int x, int y, int owner)
    { state.Cells[x, y].HasCastle = true; state.Cells[x, y].CastleOwner = owner; }

    [Test]
    public void InvalidActionDoesNotChangeStateMoneyPositionOrRandomStream()
    {
        var state = State();
        var action = new MatchAction(MatchActionKind.Attack, new UnitKey(0, 1), new GridPosition(1, 1), new UnitKey(1, 1));
        var result = engine.Execute(state, action, out var next);
        Assert.That(result.Success, Is.False); Assert.That(next, Is.Null);
        Assert.That(state.Players[0].Units[0].Position, Is.EqualTo(new GridPosition(1, 1)));
        Assert.That(state.Players[0].Units[0].HasActed, Is.False);
        Assert.That(state.Players[0].Money, Is.EqualTo(5000)); Assert.That(state.RandomState, Is.EqualTo(12345));
    }
    [Test]
    public void MovementPassesAlliesButCannotFinishOnThemAndCannotActTwice()
    {
        var state = State(); state.Players[0].Units.Add(new UnitState { Id = 2, TypeId = "soldier", Position = new GridPosition(2, 1), Health = 10 });
        Assert.That(engine.Validate(state, Wait(0, 1, 2, 1)).Success, Is.False);
        var next = Execute(state, Wait(0, 1, 3, 1));
        Assert.That(next.Players[0].Units[0].Position, Is.EqualTo(new GridPosition(3, 1)));
        Assert.That(engine.Validate(next, Wait(0, 1, 4, 1)).Success, Is.False);
    }
    [Test]
    public void UnitIdsAreScopedByPlayerAndWrongPlayerCannotSubmitActions()
    {
        var state = State();
        Assert.That(state.FindUnit(new UnitKey(0, 1)), Is.Not.SameAs(state.FindUnit(new UnitKey(1, 1))));
        Assert.That(engine.Validate(state, Wait(1, 1, 6, 6)).Success, Is.False);
    }
    [Test]
    public void DeepSnapshotsAllowPlanningWithoutChangingTheLiveState()
    {
        var original = State(); var plan = original.Clone();
        plan.Players[0].Money = 0; plan.Players[0].Units[0].Health = 1; plan.Cells[3, 3].HasStorm = true;
        Assert.That(original.Players[0].Money, Is.EqualTo(5000));
        Assert.That(original.Players[0].Units[0].Health, Is.EqualTo(20)); Assert.That(original.Cells[3, 3].HasStorm, Is.False);
    }
    [Test]
    public void CastleCanBeBuiltOnLeadersVacatedTileAndCostsExactlyOneAction()
    {
        var state = State(); state.BuildMode = true;
        var action = new MatchAction(MatchActionKind.BuildCastle, new UnitKey(0, 1), new GridPosition(2, 1), target: new GridPosition(1, 1));
        var next = Execute(state, action);
        Assert.That(next.Cells[1, 1].HasCastle, Is.True); Assert.That(next.Cells[1, 1].CastleOwner, Is.Zero);
        Assert.That(next.Players[0].Money, Is.EqualTo(1500)); Assert.That(next.Players[0].Units[0].HasActed, Is.True);
        Assert.That(engine.Validate(next, action).Success, Is.False);
    }
    [TestCase(TerrainKind.Forest, false)] [TestCase(TerrainKind.Mountain, false)] [TestCase(TerrainKind.Grass, true)]
    public void BuildRequiresGrassAndRejectsStorm(TerrainKind terrain, bool storm)
    {
        var state = State(); state.BuildMode = true; state.Cells[2, 2].Terrain = terrain; state.Cells[2, 2].HasStorm = storm;
        var action = new MatchAction(MatchActionKind.BuildCastle, new UnitKey(0, 1), new GridPosition(2, 1), target: new GridPosition(2, 2));
        Assert.That(engine.Validate(state, action).Success, Is.False);
    }
    [Test]
    public void NonLeaderCannotBuildAndEnemiesAndCastlesExcludeNearbyTiles()
    {
        var state = State(); state.BuildMode = true;
        state.Players[0].Units[0].IsLeader = false;
        Assert.That(engine.BuildTargets(state, new UnitKey(0, 1), new GridPosition(2, 1)), Is.Empty);
        state.Players[0].Units[0].IsLeader = true; Castle(state, 3, 3, -1);
        Assert.That(engine.Validate(state, new MatchAction(MatchActionKind.BuildCastle, new UnitKey(0, 1), new GridPosition(2, 1), target: new GridPosition(2, 2))).Success, Is.False);
    }
    [Test]
    public void ReadyLeaderCanOpenConstructionWhenEveryDestinationIsUnavailable()
    {
        var state = State(); state.BuildMode = true;
        state.Cells[1, 2].HasStorm = true; state.Cells[2, 1].HasStorm = true;
        var actor = new UnitKey(0, 1); var destination = new GridPosition(1, 1);
        Assert.That(engine.BuildTargets(state, actor, destination), Is.Empty);
        Assert.That(engine.ValidateBuildStart(state, actor, destination).Success, Is.True);
    }

    [Test]
    public void CapturingNeutralCastleMovesUnitAndConsumesAction()
    {
        var state = State(); Castle(state, 2, 1, -1);
        var next = Execute(state, new MatchAction(MatchActionKind.Capture, new UnitKey(0, 1), new GridPosition(2, 1)));
        Assert.That(next.Cells[2, 1].CastleOwner, Is.Zero); Assert.That(next.Players[0].Units[0].HasActed, Is.True);
    }
    [Test]
    public void RecruitmentUsesPersistentIdsAndWaitsUntilNextOwnTurn()
    {
        var state = State(); Castle(state, 3, 3, 0); state.Players[0].NextUnitId = 10;
        state.Players[0].Units.Add(new UnitState { Id = 4, TypeId = "soldier", IsDead = true });
        var next = Execute(state, MatchAction.Recruit(0, new GridPosition(3, 3), "soldier"));
        var recruit = next.FindUnit(new UnitKey(0, 10));
        Assert.That(recruit, Is.Not.Null); Assert.That(next.Players[0].Money, Is.EqualTo(4750));
        Assert.That(engine.Validate(next, Wait(0, 10, 3, 3)).Success, Is.False);
        next = Execute(next, MatchAction.EndTurn(0)); next = Execute(next, MatchAction.EndTurn(1));
        Assert.That(next.FindUnit(new UnitKey(0, 10)).HasActed, Is.False);
    }
    [Test]
    public void RecruitmentRejectsOtherRacesUnaffordableOrOccupiedCastles()
    {
        var state = State(); Castle(state, 3, 3, 0);
        Assert.That(engine.Validate(state, MatchAction.Recruit(0, new GridPosition(3, 3), "leader")).Success, Is.False);
        state.Players[0].Money = 249;
        Assert.That(engine.Validate(state, MatchAction.Recruit(0, new GridPosition(3, 3), "soldier")).Success, Is.False);
        state.Players[0].Money = 5000; Castle(state, 1, 1, 0);
        Assert.That(engine.Validate(state, MatchAction.Recruit(0, new GridPosition(1, 1), "soldier")).Success, Is.False);
    }
    [Test]
    public void IncomeHealingAndResetAffectOnlyThePlayerStartingTheirTurn()
    {
        var state = State(); state.Turn = 1; Castle(state, 1, 1, 0); Castle(state, 2, 2, 0);
        state.Players[0].Units[0].Health = 3; state.Players[0].Units[0].HasActed = true; state.Players[1].Units[0].HasActed = true;
        var next = Execute(state, MatchAction.EndTurn(1));
        Assert.That(next.Players[0].Money, Is.EqualTo(6500)); Assert.That(next.Players[0].Units[0].Health, Is.EqualTo(9));
        Assert.That(next.Players[0].Units[0].HasActed, Is.False); Assert.That(next.Players[1].Units[0].HasActed, Is.True);
        Assert.That(next.Players[1].Money, Is.EqualTo(5000));
    }
    [Test]
    public void EliminatedActivePlayerCanEndTurnAndCastlesAreReleased()
    {
        var state = State(3); state.Players[0].HasLost = true; state.Players[0].Units[0].Health = 0; Castle(state, 2, 2, 0);
        var next = Execute(state, MatchAction.EndTurn(0));
        Assert.That(next.ActivePlayer, Is.EqualTo(1)); Assert.That(next.Cells[2, 2].CastleOwner, Is.EqualTo(-1));
    }
    [TestCase(2)] [TestCase(3)] [TestCase(4)]
    public void StormDamagesEveryPlayerOnceAtRoundBoundary(int players)
    {
        var state = State(players); state.BattleRoyaleMode = true;
        foreach (var player in state.Players) { player.Units[0].Health = 10; var p = player.Units[0].Position; state.Cells[p.X, p.Y].HasStorm = true; }
        for (int p = 0; p < players; p++)
        {
            state = Execute(state, MatchAction.EndTurn(p));
            foreach (var player in state.Players) Assert.That(player.Units[0].Health, Is.EqualTo(p == players - 1 ? 7.75f : 10f));
        }
    }
    [Test]
    public void StormCanEliminateBothLeadersBeforeVictoryIsChecked()
    {
        var state = State(); state.BattleRoyaleMode = true; state.Turn = 1;
        foreach (var player in state.Players) { player.Units[0].Health = 1; var p = player.Units[0].Position; state.Cells[p.X, p.Y].HasStorm = true; }
        var next = Execute(state, MatchAction.EndTurn(1));
        Assert.That(next.IsOver, Is.True); Assert.That(next.Winner, Is.EqualTo(-1));
        Assert.That(next.Players.All(p => p.HasLost), Is.True);
    }
    [Test]
    public void ReplayingSameAttackFromSameRandomStateProducesSameOutcomeInsidePrediction()
    {
        var state = State(); state.Players[1].Units[0].Position = new GridPosition(3, 1);
        var action = new MatchAction(MatchActionKind.Attack, new UnitKey(0, 1), new GridPosition(2, 1), new UnitKey(1, 1));
        var interval = engine.Predict(state, action.Actor, action.TargetUnit, action.Destination);
        Assert.That(state.RandomState, Is.EqualTo(12345));
        var first = Execute(state, action); var replay = Execute(state, action);
        Assert.That(first.RandomState, Is.EqualTo(replay.RandomState));
        Assert.That(first.Players[0].Units[0].Health, Is.EqualTo(replay.Players[0].Units[0].Health));
        Assert.That(first.Players[1].Units[0].Health, Is.InRange(interval.MinDefenderHealth, interval.MaxDefenderHealth));
        Assert.That(first.Players[0].Units[0].Health, Is.InRange(interval.MinAttackerHealth, interval.MaxAttackerHealth));
    }
    [Test]
    public void CompleteActionSequenceRunsWithoutAnySceneOrGameObject()
    {
        var state = State(); state.BuildMode = true;
        state = Execute(state, new MatchAction(MatchActionKind.BuildCastle, new UnitKey(0, 1), new GridPosition(3, 1), target: new GridPosition(3, 2)));
        state = Execute(state, MatchAction.Recruit(0, new GridPosition(3, 2), "soldier"));
        state = Execute(state, MatchAction.EndTurn(0));
        state = Execute(state, Wait(1, 1, 4, 4));
        state = Execute(state, MatchAction.EndTurn(1));
        state = Execute(state, new MatchAction(MatchActionKind.Attack, new UnitKey(0, 1), new GridPosition(4, 3), new UnitKey(1, 1)));
        Assert.That(state.Players[0].Units.Count, Is.EqualTo(2));
        Assert.That(state.Cells[3, 2].CastleOwner, Is.Zero);
        Assert.That(state.Players[0].Money, Is.EqualTo(2250));
        Assert.That(state.Players[0].Units[0].HasActed, Is.True);
    }
}
