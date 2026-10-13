using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;

public class MultiplayerDataTests
{
    private static MatchContentData Catalogue()
    {
        var content = new MatchContent();
        content.AddUnit(new UnitTypeStats("soldier", 10, 4, 3, 1, 250));
        content.AddUnit(new UnitTypeStats("pacifist", 12, 0, 2, 1, 100));
        content.AddRace("race", new[] { "soldier", "pacifist" });
        foreach (var a in content.UnitTypes) foreach (var d in content.UnitTypes)
            content.SetDamage(a.Id, d.Id, a.Id == "pacifist" ? 37 : 55);
        return MatchContentData.FromContent(content, new Dictionary<string, string> { { "race", "soldier" } }, "rules-1");
    }
    private static CustomMapData Map(int playerCount = 2)
    {
        var map = new CustomMapData { id = Guid.NewGuid().ToString("N"), name = "Test", width = 10, height = 12, playerCount = playerCount };
        for (int i = 0; i < map.width * map.height; i++) map.cells.Add(new MapCellData());
        var starts = new[] { new GridPosition(1, 1), new GridPosition(8, 10), new GridPosition(8, 1), new GridPosition(1, 10) };
        for (int i = 0; i < playerCount; i++) map.starts.Add(new MapStartData { slot = i, x = starts[i].X, y = starts[i].Y });
        map.Cell(1, 1).castle = true; map.Cell(1, 1).castleOwner = 0;
        map.Cell(3, 4).terrain = TerrainKind.Forest;
        return map;
    }
    private static MatchState Initial(int playerCount = 2, MatchOptions options = null)
        => MatchInitializer.Create(Map(playerCount), Catalogue(), Enumerable.Range(0, playerCount)
            .Select(_ => new MatchPlayerSetup { RaceId = "race", LeaderTypeId = "soldier" }).ToArray(), 67890, options);

    [Test]
    public void PersistenceRoundTripPreservesPrivateRandomStreamAndRectangularBoard()
    {
        var original = Initial(4, new MatchOptions { BuildMode = true, BattleRoyaleMode = true });
        original.Turn = 7;
        original.Players[2].Units.Add(new UnitState { Id = 2, TypeId = "pacifist", Position = new GridPosition(3, 4),
            Health = 6.5f, MaxHealth = 12, Attack = 0, Movement = 2, Range = 1, HasActed = true });
        original.Players[2].NextUnitId = 3;
        var data = MatchStateData.FromState(original);
        var restored = data.ToState();
        Assert.That(restored.RandomState, Is.EqualTo(original.RandomState));
        Assert.That(restored.Turn, Is.EqualTo(7));
        Assert.That(restored.Width, Is.EqualTo(10)); Assert.That(restored.Height, Is.EqualTo(12));
        Assert.That(restored.Cells[3, 4].Terrain, Is.EqualTo(TerrainKind.Forest));
        Assert.That(data.Snapshot.Cells[4 * 10 + 3].Terrain, Is.EqualTo(TerrainKind.Forest));
        Assert.That(restored.Players[2].Units[1].Health, Is.EqualTo(6.5f));
        restored.Cells[1, 1].HasCastle = false; restored.Players[2].Units[1].Health = 1;
        Assert.That(original.Cells[1, 1].HasCastle, Is.True);
        Assert.That(data.Snapshot.Players[2].Units[1].Health, Is.EqualTo(6.5f));
    }

    [Test]
    public void PublicSnapshotNeverContainsPrivateRandomStream()
    {
        Assert.That(typeof(MatchSnapshotData).GetFields().Any(f => f.Name == "RandomState"), Is.False);
        var original = Initial();
        var publicData = MatchSnapshotData.FromState(original);
        Assert.That(publicData.ToState().RandomState, Is.Not.EqualTo(original.RandomState));
        var stored = MatchStateData.FromState(original);
        stored.RandomState = 0;
        Assert.That(stored.ToState().RandomState, Is.Zero);
    }

    [Test]
    public void StoredStateContinuesCombatWithTheSameOutcomeAndRandomStream()
    {
        var original = Initial(); original.Players[1].Units[0].Position = new GridPosition(2, 1);
        var restored = MatchStateData.FromState(original).ToState();
        var engine = new MatchEngine(Catalogue().ToContent());
        var attack = new MatchAction(MatchActionKind.Attack, new UnitKey(0, 1), new GridPosition(1, 1), new UnitKey(1, 1));
        Assert.That(engine.Execute(original, attack, out var next).Success, Is.True);
        Assert.That(engine.Execute(restored, attack, out var replay).Success, Is.True);
        Assert.That(replay.RandomState, Is.EqualTo(next.RandomState));
        for (int p = 0; p < 2; p++) Assert.That(replay.Players[p].Units[0].Health, Is.EqualTo(next.Players[p].Units[0].Health));
    }

    [TestCase(2)] [TestCase(3)] [TestCase(4)]
    public void InitializerCreatesLeadersAndGrantsOpeningIncomeExactlyOnce(int players)
    {
        var state = Initial(players);
        Assert.That(state.Players.Count, Is.EqualTo(players));
        Assert.That(state.Turn, Is.Zero); Assert.That(state.Players[0].Money, Is.EqualTo(1000));
        for (int slot = 0; slot < players; slot++)
        {
            var player = state.Players[slot]; var leader = player.Units.Single();
            Assert.That(player.NextUnitId, Is.EqualTo(2)); Assert.That(leader.IsLeader, Is.True);
            Assert.That(leader.MaxHealth, Is.EqualTo(LeaderStats.Health(10)));
            Assert.That(leader.Attack, Is.EqualTo(LeaderStats.Attack(4)));
            Assert.That(leader.HasActed, Is.False);
            if (slot > 0) Assert.That(player.Money, Is.Zero);
        }
        Assert.That(Initial(players, new MatchOptions { BuildMode = true }).Players[0].Money, Is.EqualTo(5000));
    }

    [Test]
    public void CataloguePreservesZeroAttackDamageRowsAndRecruits()
    {
        var data = Catalogue(); var restored = data.ToContent();
        Assert.That(restored.DamagePercent("pacifist", "soldier"), Is.EqualTo(37));
        Assert.That(restored.CanRecruit("race", "pacifist"), Is.True);
        Assert.That(restored.Unit("soldier").Cost, Is.EqualTo(250));
        Assert.That(data.Version, Is.EqualTo("rules-1"));
        Assert.That(data.Races.Single().LeaderTypeId, Is.EqualTo("soldier"));
    }

    [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(float.NegativeInfinity)]
    public void SnapshotAndCatalogueRejectNonFiniteHealthAndDamage(float value)
    {
        var snapshot = MatchSnapshotData.FromState(Initial()); snapshot.Players[0].Units[0].Health = value;
        Assert.Throws<ArgumentException>(() => snapshot.ToState());
        var catalogue = Catalogue(); catalogue.Units[0].Health = value;
        Assert.Throws<ArgumentException>(() => catalogue.ToContent());
        catalogue = Catalogue(); catalogue.Damage[0].Percent = value;
        Assert.Throws<ArgumentException>(() => catalogue.ToContent());
    }

    [Test]
    public void SnapshotRejectsMalformedListsDimensionsIdsAndOccupants()
    {
        var data = MatchSnapshotData.FromState(Initial()); data.Width = int.MaxValue;
        Assert.Throws<ArgumentException>(() => data.ToState());
        data = MatchSnapshotData.FromState(Initial()); data.Cells.RemoveAt(0);
        Assert.Throws<ArgumentException>(() => data.ToState());
        data = MatchSnapshotData.FromState(Initial()); data.Players[0].Units = null;
        Assert.Throws<ArgumentException>(() => data.ToState());
        data = MatchSnapshotData.FromState(Initial()); data.Players[0].Units.Add(data.Players[0].Units[0]);
        Assert.Throws<ArgumentException>(() => data.ToState());
        data = MatchSnapshotData.FromState(Initial()); data.Players[1].Units[0].X = 1; data.Players[1].Units[0].Y = 1;
        Assert.Throws<ArgumentException>(() => data.ToState());
        data = MatchSnapshotData.FromState(Initial()); data.Players[0].Units[0].Id = data.Players[0].NextUnitId;
        Assert.Throws<ArgumentException>(() => data.ToState());
        data = MatchSnapshotData.FromState(Initial()); data.Players[0].Units[0].X = 10;
        Assert.Throws<ArgumentException>(() => data.ToState());
    }

    [Test]
    public void CatalogueRejectsMissingOrRepeatedDamageAndUnknownRecruit()
    {
        var data = Catalogue(); data.Damage[1] = data.Damage[0];
        Assert.Throws<ArgumentException>(() => data.ToContent());
        data = Catalogue(); data.Damage.RemoveAt(0);
        Assert.Throws<ArgumentException>(() => data.ToContent());
        data = Catalogue(); data.Races[0].RecruitTypeIds[0] = "missing";
        Assert.Throws<ArgumentException>(() => data.ToContent());
    }

    [Test]
    public void InitializerRejectsUnauthorizedLeaderAndInvalidStartsOrOptions()
    {
        var setups = new[] { new MatchPlayerSetup { RaceId = "race", LeaderTypeId = "pacifist" },
            new MatchPlayerSetup { RaceId = "race", LeaderTypeId = "soldier" } };
        Assert.Throws<ArgumentException>(() => MatchInitializer.Create(Map(), Catalogue(), setups, 1));
        setups[0].LeaderTypeId = "soldier";
        var map = Map(); map.starts[1].x = 1; map.starts[1].y = 1;
        Assert.Throws<ArgumentException>(() => MatchInitializer.Create(map, Catalogue(), setups, 1));
        Assert.Throws<ArgumentException>(() => Initial(2, new MatchOptions { InitialMoney = int.MaxValue }));
        Assert.Throws<ArgumentException>(() => Initial(2, new MatchOptions { StormRoundsPerShrink = 0 }));
    }

    [Test]
    public void InitializerCopiesMapAndAppliesConfiguredStorm()
    {
        var map = Map();
        var setups = new[] { new MatchPlayerSetup { RaceId = "race", LeaderTypeId = "soldier" },
            new MatchPlayerSetup { RaceId = "race", LeaderTypeId = "soldier" } };
        var state = MatchInitializer.Create(map, Catalogue(), setups, 0,
            new MatchOptions { BattleRoyaleMode = true, StormWarmupRounds = 0, InitialMoney = 200 });
        Assert.That(state.RandomState, Is.Not.Zero);
        Assert.That(state.Cells[0, 0].HasStorm, Is.True);
        Assert.That(state.Players[0].Money, Is.EqualTo(1200));
        state.Cells[1, 1].HasCastle = false;
        Assert.That(map.Cell(1, 1).castle, Is.True);
    }

    [Test]
    public void MissingVersionInActualJsonRejectsSnapshotPrivateStateAndCatalogue()
    {
        var snapshotJson = JsonUtility.ToJson(MatchSnapshotData.FromState(Initial())).Replace("\"SchemaVersion\":1,", "");
        var snapshot = JsonUtility.FromJson<MatchSnapshotData>(snapshotJson);
        Assert.That(snapshot.SchemaVersion, Is.Zero);
        Assert.Throws<ArgumentException>(() => snapshot.ToState());
        var stateJson = JsonUtility.ToJson(MatchStateData.FromState(Initial())).Replace("\"SchemaVersion\":1,", "");
        var stored = JsonUtility.FromJson<MatchStateData>(stateJson);
        Assert.That(stored.SchemaVersion, Is.Zero);
        Assert.Throws<ArgumentException>(() => stored.ToState());
        var catalogueJson = JsonUtility.ToJson(Catalogue()).Replace("\"SchemaVersion\":1,", "");
        var catalogue = JsonUtility.FromJson<MatchContentData>(catalogueJson);
        Assert.That(catalogue.SchemaVersion, Is.Zero);
        Assert.Throws<ArgumentException>(() => catalogue.ToContent());
    }

    [Test]
    public void ActivePlayersMustHaveExactlyOneLivingLeader()
    {
        var data = MatchSnapshotData.FromState(Initial()); data.Players[0].Units.Clear();
        Assert.Throws<ArgumentException>(() => data.ToState());
        data = MatchSnapshotData.FromState(Initial()); data.Players[0].Units[0].IsLeader = false;
        Assert.Throws<ArgumentException>(() => data.ToState());
        data = MatchSnapshotData.FromState(Initial());
        data.Players[0].Units[0].Health = 0; data.Players[0].Units[0].IsDead = true;
        Assert.Throws<ArgumentException>(() => data.ToState());
        var state = Initial(); var duplicate = state.Players[0].Units[0].Clone();
        duplicate.Id = 2; duplicate.Position = new GridPosition(2, 1);
        state.Players[0].NextUnitId = 3; state.Players[0].Units.Add(duplicate);
        Assert.Throws<ArgumentException>(() => MatchSnapshotData.FromState(state));
    }

    [Test]
    public void NonterminalMatchNeedsTwoSurvivorsButLostCapturedRosterCanBeEmpty()
    {
        var data = MatchSnapshotData.FromState(Initial());
        data.Players[1].HasLost = true; data.Players[1].Units.Clear();
        Assert.Throws<ArgumentException>(() => data.ToState());
        data.IsOver = true; data.Winner = 0;
        var restored = data.ToState();
        Assert.That(restored.Players[1].Units, Is.Empty);
        Assert.That(restored.Winner, Is.Zero);
    }
}
