using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;

public class UnitPriceExperimentTests
{
    private static Type EditorType(string name) => Type.GetType(name + ", Assembly-CSharp-Editor", true);
    private static object Field(object value, string name) => value.GetType().GetField(name).GetValue(value);
    private static void Set(object value, string name, object data) => value.GetType().GetField(name).SetValue(value, data);
    private static object Scenario(int cost = 600) => JsonUtility.FromJson("{\"priceUnitId\":\"troop\",\"priceRaceId\":\"race\",\"pricePlayer\":0,\"priceCost\":" + cost
        + ",\"priceBaselineCost\":500,\"pricePairId\":\"paired\"}", EditorType("BotValidationScenario"));
    private static SimulationContentSnapshot Baseline()
    {
        var content = new MatchContent();
        content.AddUnit(new UnitTypeStats("troop", 10, 5, 3, 1, 500));
        content.AddUnit(new UnitTypeStats("enemy", 10, 5, 3, 1, 500));
        content.AddRace("race", new[] { "troop" }); content.AddRace("enemyRace", new[] { "enemy" });
        foreach (var a in content.UnitTypes) foreach (var d in content.UnitTypes) content.SetDamage(a.Id, d.Id, 100);
        return SimulationContentSnapshot.Capture(content, new[] { "race", "enemyRace" });
    }
    private static SimulationContentSnapshot ContentFor(SimulationContentSnapshot content, object scenario)
        => (SimulationContentSnapshot)EditorType("UnitPriceExperiments").GetMethod("ContentFor").Invoke(null, new[] { content, scenario });
    [Test]
    public void PriceTreatmentChangesAffordabilityWithoutChangingCombatOrBaseline()
    {
        var baseline = Baseline(); string original = JsonUtility.ToJson(baseline);
        var variant = ContentFor(baseline, Scenario());
        Assert.That(variant.Restore().Unit("troop").Cost, Is.EqualTo(600));
        Assert.That(JsonUtility.ToJson(baseline), Is.EqualTo(original));
        Assert.That(variant.damage.Select(d => (d.attacker, d.defender, d.percent)), Is.EqualTo(baseline.damage.Select(d => (d.attacker, d.defender, d.percent))));
        var state = State(); state.Players[0].Money = 550;
        var recruit = MatchAction.Recruit(0, new GridPosition(2, 2), "troop");
        Assert.That(new MatchEngine(baseline.Restore()).Validate(state, recruit).Success, Is.True);
        Assert.That(new MatchEngine(variant.Restore()).Validate(state, recruit).Success, Is.False);
        var before = new MatchEngine(baseline.Restore()).Predict(state, new UnitKey(0, 2), new UnitKey(1, 2), new GridPosition(2, 3));
        var after = new MatchEngine(variant.Restore()).Predict(state, new UnitKey(0, 2), new UnitKey(1, 2), new GridPosition(2, 3));
        Assert.That(after.MinDamage, Is.EqualTo(before.MinDamage)); Assert.That(after.MaxCounterDamage, Is.EqualTo(before.MaxCounterDamage));
        variant.units.Find(u => u.id == "troop").cost = 500;
        Assert.That(JsonUtility.ToJson(variant), Is.EqualTo(original), "The price treatment must change only the selected cost.");
    }
    [Test]
    public void PriceMatrixCoversEveryRecruitAndPairsIdenticalStartsAcrossPrices()
    {
        var factory = EditorType("UnitPriceExperiments"); var config = factory.GetMethod("Recruitment").Invoke(null, null);
        var scenarios = ((IList)Field(config, "scenarios")).Cast<object>().ToList();
        Assert.That(scenarios.Count, Is.EqualTo(156));
        Assert.That(scenarios.Select(s => (string)Field(s, "priceUnitId")).Distinct().Count(), Is.EqualTo(13));
        var mapFactory = EditorType("BotValidationScenarios"); var catalog = mapFactory.GetMethod("Catalog").Invoke(null, null);
        var races = catalog.GetType().GetProperty("Races").GetValue(catalog);
        foreach (var pair in scenarios.GroupBy(s => (string)Field(s, "pricePairId")))
        {
            Assert.That(pair.Count(), Is.EqualTo(3));
            var starts = pair.Select(s => SimulationSnapshot.Fingerprint((MatchState)mapFactory.GetMethod("Create").Invoke(null, new[] { s, races }))).Distinct();
            Assert.That(starts.Count(), Is.EqualTo(1), pair.Key);
        }
    }
    [Test]
    public void EqualArmyBudgetConservesPaidGoldPlusUnspentGoldForEveryPrice()
    {
        var settings = Activator.CreateInstance(EditorType("UnitPriceSettings"));
        Set(settings, "unitIds", new[] { "unit.3bcea12122b096646abd82955e14b60b" }); Set(settings, "armyBudget", 3000);
        var config = EditorType("UnitPriceExperiments").GetMethod("Create").Invoke(null, new[] { settings });
        foreach (var scenario in (IList)Field(config, "scenarios"))
        {
            var state = ((SimulationSnapshot)Field(scenario, "initial")).Restore();
            int player = (int)Field(scenario, "pricePlayer"), cost = (int)Field(scenario, "priceCost");
            int troops = state.Players[player].Units.Count(u => !u.IsLeader);
            Assert.That(troops, Is.EqualTo(3000 / cost));
            Assert.That(state.Players[player].Money + troops * cost, Is.EqualTo(4000 + (player == 0 ? 500 : 0)));
            Assert.That(state.Players.SelectMany(p => p.Units).Select(u => u.Position).Distinct().Count(), Is.EqualTo(state.Players.Sum(p => p.Units.Count)));
        }
    }
    [Test]
    public void TrackerObservesRealRecruitmentAndCappedKillDamageWithoutChangingState()
    {
        var scenario = Scenario(600); var variant = ContentFor(Baseline(), scenario).Restore(); var state = State();
        var tracker = Activator.CreateInstance(EditorType("UnitPriceTracker"), scenario, state, variant, "hash");
        var record = new SimulationActionRecord { accepted = true, player = 0, actorId = 2, targetPlayer = 1, targetId = 2, kind = MatchActionKind.Attack };
        tracker.GetType().GetMethod("Before").Invoke(tracker, new object[] { record, state });
        Assert.That(new MatchEngine(variant).Execute(state, new MatchAction(MatchActionKind.Attack, new UnitKey(0, 2), new GridPosition(2, 3), new UnitKey(1, 2)), out var next).Success, Is.True);
        string hash = SimulationSnapshot.Fingerprint(next); tracker.GetType().GetMethod("After").Invoke(tracker, new object[] { next });
        var result = tracker.GetType().GetProperty("Result").GetValue(tracker);
        Assert.That(Field(result, "unitKills"), Is.EqualTo(1)); Assert.That(Field(result, "damageDealt"), Is.EqualTo(1f));
        Assert.That(Field(result, "enemyGoldDamage"), Is.EqualTo(50f)); Assert.That(SimulationSnapshot.Fingerprint(next), Is.EqualTo(hash));
        var recruitRecord = new SimulationActionRecord { accepted = true, player = 0, kind = MatchActionKind.Recruit, recruitTypeId = "troop", round = 1 };
        tracker.GetType().GetMethod("Before").Invoke(tracker, new object[] { recruitRecord, next });
        Assert.That(new MatchEngine(variant).Execute(next, MatchAction.Recruit(0, new GridPosition(2, 2), "troop"), out var recruited).Success, Is.True);
        tracker.GetType().GetMethod("After").Invoke(tracker, new object[] { recruited });
        Assert.That(Field(result, "recruited"), Is.EqualTo(1)); Assert.That(Field(result, "spentGold"), Is.EqualTo(600));
    }
    [Test]
    public void PairSummarySeparatesLimitsAndMarksUnexercisedPriceComparisons()
    {
        var type = EditorType("UnitPriceCaseResult"); var cases = Array.CreateInstance(type, 3);
        cases.SetValue(JsonUtility.FromJson("{\"unitId\":\"troop\",\"race\":\"race\",\"pairId\":\"pair\",\"initialHash\":\"same\",\"baselineCost\":500,\"cost\":500,\"player\":0,\"winner\":0,\"outcome\":1}", type), 0);
        cases.SetValue(JsonUtility.FromJson("{\"unitId\":\"troop\",\"race\":\"race\",\"pairId\":\"pair\",\"initialHash\":\"same\",\"baselineCost\":500,\"cost\":450,\"player\":0,\"winner\":1,\"outcome\":1,\"recruited\":2}", type), 1);
        cases.SetValue(JsonUtility.FromJson("{\"unitId\":\"troop\",\"race\":\"race\",\"pairId\":\"other\",\"baselineCost\":500,\"cost\":450,\"player\":0,\"winner\":-1,\"outcome\":3}", type), 2);
        var report = EditorType("UnitPriceSummary").GetMethod("From").Invoke(null, new object[] { cases });
        var row = ((IList)Field(report, "rows")).Cast<object>().Single(r => (int)Field(r, "cost") == 450);
        Assert.That(Field(row, "inconclusive"), Is.EqualTo(1)); Assert.That(Field(row, "pairedCompletedGames"), Is.EqualTo(1));
        Assert.That(Field(row, "pairedWinsDelta"), Is.EqualTo(-1)); Assert.That(Field(row, "pairedWithoutUnit"), Is.EqualTo(1));
    }
    private static MatchState State()
    {
        var state = new MatchState(10, 10) { RandomState = 12345 };
        var a = new PlayerState { RaceId = "race", Money = 1000, NextUnitId = 3 };
        var b = new PlayerState { RaceId = "enemyRace", Money = 1000, NextUnitId = 3 };
        a.Units.Add(Unit(1, "enemy", 0, 0, true)); a.Units.Add(Unit(2, "troop", 2, 3, false));
        b.Units.Add(Unit(1, "enemy", 9, 9, true)); var target = Unit(2, "enemy", 3, 3, false); target.Health = 1; b.Units.Add(target);
        state.Players.Add(a); state.Players.Add(b); state.Cells[2, 2].HasCastle = true; state.Cells[2, 2].CastleOwner = 0; return state;
    }
    private static UnitState Unit(int id, string type, int x, int y, bool leader) => new UnitState { Id = id, TypeId = type,
        Position = new GridPosition(x, y), IsLeader = leader, Health = 10, MaxHealth = 10, Attack = 5, Movement = 3, Range = 1 };
}
