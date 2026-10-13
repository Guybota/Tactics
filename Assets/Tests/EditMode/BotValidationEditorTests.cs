using System;
using System.Collections;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;

public class BotValidationEditorTests
{
    private static Type EditorType(string name) => Type.GetType(name + ", Assembly-CSharp-Editor", true);
    [Test]
    public void SmokeConfigurationSurvivesJsonAndCreatesAllTwelveRealContentStarts()
    {
        var factory = EditorType("BotValidationScenarios");
        var config = factory.GetMethod("Smoke").Invoke(null, null);
        var restored = JsonUtility.FromJson(JsonUtility.ToJson(config), config.GetType());
        var scenarios = (IList)restored.GetType().GetField("scenarios").GetValue(restored);
        var catalog = factory.GetMethod("Catalog").Invoke(null, null);
        var races = catalog.GetType().GetProperty("Races").GetValue(catalog);
        Assert.That(scenarios.Count, Is.EqualTo(12));
        var beforeRandom = UnityEngine.Random.state;
        foreach (var scenario in scenarios)
        {
            var state = (MatchState)factory.GetMethod("Create").Invoke(null, new[] { scenario, races });
            int participants = ((string[])scenario.GetType().GetField("races").GetValue(scenario)).Length;
            Assert.That(state.Players.Count, Is.EqualTo(participants));
            Assert.That(state.Players.TrueForAll(p => p.Units.Count == 1 && p.Units[0].IsLeader && p.Units[0].IsAlive), Is.True);
            Assert.That(state.Players[0].Money, Is.EqualTo(state.BuildMode ? 4500 : 1500));
            var second = (MatchState)factory.GetMethod("Create").Invoke(null, new[] { scenario, races });
            Assert.That(SimulationSnapshot.Fingerprint(state), Is.EqualTo(SimulationSnapshot.Fingerprint(second)));
        }
        Assert.That(UnityEngine.Random.state, Is.EqualTo(beforeRandom));
    }
    [Test]
    public void AssignedCastleScenarioUsesActualIncomeAndOwners()
    {
        var factory = EditorType("BotValidationScenarios");
        var config = factory.GetMethod("Smoke").Invoke(null, null);
        var scenarios = (IList)config.GetType().GetField("scenarios").GetValue(config);
        var scenario = scenarios[0]; scenario.GetType().GetField("assignStartingCastles").SetValue(scenario, true);
        var catalog = factory.GetMethod("Catalog").Invoke(null, null);
        var races = catalog.GetType().GetProperty("Races").GetValue(catalog);
        var state = (MatchState)factory.GetMethod("Create").Invoke(null, new[] { scenario, races });
        Assert.That(state.Players[0].Money, Is.EqualTo(2000));
        for (int p = 0; p < state.Players.Count; p++)
        {
            var position = state.Players[p].Units[0].Position;
            Assert.That(state.Cells[position.X, position.Y].CastleOwner, Is.EqualTo(p));
        }
    }
    [TestCase(2)] [TestCase(3)] [TestCase(4)]
    public void SymmetricReferencePreservesRotationalTerrainAndCastleGeometry(int participants)
    {
        var factory = EditorType("BotValidationScenarios");
        var config = factory.GetMethod("Smoke").Invoke(null, null);
        var scenarios = (IList)config.GetType().GetField("scenarios").GetValue(config);
        var scenario = scenarios[(participants - 2) * 4];
        scenario.GetType().GetField("map").SetValue(scenario, "Symmetric");
        var catalog = factory.GetMethod("Catalog").Invoke(null, null);
        var races = catalog.GetType().GetProperty("Races").GetValue(catalog);
        var state = (MatchState)factory.GetMethod("Create").Invoke(null, new[] { scenario, races });
        for (int x = 0; x < state.Width; x++) for (int y = 0; y < state.Height; y++)
        {
            var original = state.Cells[x, y]; var rotated = state.Cells[state.Width - 1 - x, state.Height - 1 - y];
            Assert.That(rotated.Terrain, Is.EqualTo(original.Terrain));
            Assert.That(rotated.HasCastle, Is.EqualTo(original.HasCastle));
            Assert.That(rotated.MovementCost, Is.EqualTo(original.MovementCost));
        }
    }
}
