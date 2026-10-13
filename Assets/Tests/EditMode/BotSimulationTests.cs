using System;
using System.Linq;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;

public class BotSimulationTests
{
    private static MatchContent Content()
    {
        var c = new MatchContent(); c.AddUnit(new UnitTypeStats("leader", 20, 5, 3, 1, 0));
        c.AddUnit(new UnitTypeStats("soldier", 10, 5, 3, 1, 250)); c.AddRace("race", new[] { "soldier" });
        foreach (var a in c.UnitTypes) foreach (var d in c.UnitTypes) c.SetDamage(a.Id, d.Id, 55);
        return c;
    }
    private static MatchState State(int players = 2, bool build = false, bool storm = false)
    {
        var state = new MatchState(10, 10) { RandomState = 12345, BuildMode = build, BattleRoyaleMode = storm };
        var positions = new[] { new GridPosition(1, 1), new GridPosition(8, 8), new GridPosition(1, 8), new GridPosition(8, 1) };
        for (int p = 0; p < players; p++)
        {
            var player = new PlayerState { RaceId = "race", Money = build ? 4000 : 1000, NextUnitId = 2 };
            player.Units.Add(new UnitState { Id = 1, TypeId = "leader", Position = positions[p], IsLeader = true,
                Health = 20, MaxHealth = 20, Attack = 5, Movement = 3, Range = 1 }); state.Players.Add(player);
            if (!build) { state.Cells[positions[p].X, positions[p].Y].HasCastle = true; state.Cells[positions[p].X, positions[p].Y].CastleOwner = p; }
        }
        MatchEngine.BeginTurn(state); return state;
    }
    private static void Run(BotSimulation simulation)
    {
        int steps = 0;
        while (!simulation.IsFinished) { simulation.Advance(); if (++steps > 3000000) Assert.Fail("Simulation failed to reach its safety limits."); }
    }
    [Test]
    public void JsonSnapshotPreservesFullStateAndRestoresOwnedCopies()
    {
        var state = State(4, true, true); state.Turn = 7; state.RandomState = uint.MaxValue;
        state.Cells[4, 5] = new CellState { Terrain = TerrainKind.Forest, MovementCost = 7, SupportsCastle = false, HasStorm = true };
        state.Cells[3, 3].HasCastle = true; state.Cells[3, 3].CastleOwner = 2;
        state.Players[2].Units[0].Health = 3.125f; state.Players[2].Units[0].HasActed = true;
        state.Players[1].NextUnitId = 117; state.Players[1].Money = 123;
        var saved = JsonUtility.FromJson<SimulationSnapshot>(JsonUtility.ToJson(SimulationSnapshot.Capture(state)));
        var restored = saved.Restore();
        Assert.That(SimulationSnapshot.Fingerprint(restored), Is.EqualTo(SimulationSnapshot.Fingerprint(state)));
        restored.Players[1].Money++; restored.Cells[3, 3].CastleOwner = -1;
        Assert.That(state.Players[1].Money, Is.EqualTo(123)); Assert.That(saved.cells[33].CastleOwner, Is.EqualTo(2));
    }
    [TestCase(2, false, false)] [TestCase(2, true, false)] [TestCase(2, false, true)] [TestCase(2, true, true)]
    [TestCase(3, false, false)] [TestCase(3, true, false)] [TestCase(3, false, true)] [TestCase(3, true, true)]
    [TestCase(4, false, false)] [TestCase(4, true, false)] [TestCase(4, false, true)] [TestCase(4, true, true)]
    public void SerializedInputsReplayEveryModeThroughLegalActions(int players, bool build, bool storm)
    {
        var state = State(players, build, storm); string before = SimulationSnapshot.Fingerprint(state);
        var engine = new MatchEngine(Content());
        using (var first = new BotSimulation("first", state, engine))
        {
            Run(first);
            var data = JsonUtility.FromJson<SimulationSnapshot>(JsonUtility.ToJson(SimulationSnapshot.Capture(state)));
            var content = JsonUtility.FromJson<SimulationContentSnapshot>(JsonUtility.ToJson(SimulationContentSnapshot.Capture(Content(), new[] { "race" })));
            using (var replay = new BotSimulation("replay", data.Restore(), new MatchEngine(content.Restore())))
            {
                Run(replay);
                Assert.That(first.Result.outcome == SimulationOutcome.Victory || first.Result.outcome == SimulationOutcome.Draw, Is.True);
                Assert.That(replay.Result.finalHash, Is.EqualTo(first.Result.finalHash));
                Assert.That(replay.Result.actionCounts, Is.EqualTo(first.Result.actionCounts));
                Assert.That(first.Result.rejectedActions, Is.Zero);
                Assert.That(first.Result.nullDecisions, Is.Zero);
                Assert.That(SimulationSnapshot.Fingerprint(state), Is.EqualTo(before));
            }
        }
    }
    [Test]
    public void ActionLimitIsInconclusiveAndCancellationPreservesPartialState()
    {
        using (var simulation = new BotSimulation("limit", State(), new MatchEngine(Content()), limits: new SimulationLimits { maxActions = 1 }))
        {
            Run(simulation); Assert.That(simulation.Result.outcome, Is.EqualTo(SimulationOutcome.LimitReached));
            Assert.That(simulation.State.IsOver, Is.False); Assert.That(simulation.Result.winner, Is.EqualTo(-1));
            Assert.That(simulation.Result.lastDecisions.Count, Is.EqualTo(1));
        }
        using (var simulation = new BotSimulation("cancel", State(), new MatchEngine(Content())))
        {
            while (simulation.Result.actions == 0) simulation.Advance();
            string partial = SimulationSnapshot.Fingerprint(simulation.State);
            simulation.Cancel(); simulation.Advance();
            Assert.That(simulation.Result.outcome, Is.EqualTo(SimulationOutcome.Cancelled));
            Assert.That(simulation.Result.finalHash, Is.EqualTo(partial)); Assert.That(simulation.Result.actions, Is.EqualTo(1));
        }
    }
    [Test]
    public void SimultaneousStormLeaderDeathsAreARealDrawWithDirectDamageMetrics()
    {
        var state = State(2, false, true); state.Turn = 1;
        foreach (var player in state.Players) { player.Units[0].Health = 1; player.Units[0].HasActed = true; }
        foreach (var cell in state.Cells) cell.HasStorm = true;
        using (var simulation = new BotSimulation("draw", state, new MatchEngine(Content())))
        {
            Run(simulation); Assert.That(simulation.Result.outcome, Is.EqualTo(SimulationOutcome.Draw));
            Assert.That(simulation.Result.leaderLosses, Is.EqualTo(2)); Assert.That(simulation.Result.stormDeaths, Is.EqualTo(2));
            Assert.That(simulation.Result.stormDamage, Is.EqualTo(2));
        }
    }
    [TestCase(false)] [TestCase(true)]
    public void SeededGeneratorKeepsTheEstablishedTerrainAndCastleFixture(bool build)
    {
        var layout = GeneratedMapLayout.Create(10, 10, build, new System.Random(8675309));
        // X-major fixture, verified against the legacy generator before extraction.
        string expected = build
            ? ".....M.....................F..FM..M....MM................M...MMF........F......F....M...M.MM...M..M."
            : "...............FM.M..MM........M.CMF....F...F..M.C.M.C.M.......F.M.M.....M.....MC..C.....FM........F";
        var actual = new System.Text.StringBuilder();
        for (int x = 0; x < 10; x++) for (int y = 0; y < 10; y++)
            actual.Append(layout[x, y].HasCastle ? 'C' : layout[x, y].Terrain == TerrainKind.Mountain ? 'M' : layout[x, y].Terrain == TerrainKind.Forest ? 'F' : '.');
        Assert.That(actual.ToString(), Is.EqualTo(expected));
    }
}
