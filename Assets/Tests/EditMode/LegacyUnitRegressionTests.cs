using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

// Regression coverage for scene scripts while they still live in Assembly-CSharp.
public class LegacyUnitRegressionTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private Component Create(string typeName)
    {
        var go = new GameObject("Regression " + typeName);
        objects.Add(go);
        return go.AddComponent(Type.GetType(typeName + ", Assembly-CSharp", true));
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field).SetValue(target, value);
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field).GetValue(target);
    private static object Call(object target, string method, params object[] values) => target.GetType().GetMethod(method).Invoke(target, values);
    private Component Unit(int id, bool king = false)
    {
        var unit = Create("Unit");
        var body = new GameObject("Body"); body.transform.SetParent(unit.transform);
        var tint = new GameObject("Tint"); tint.transform.SetParent(unit.transform);
        Set(unit, "sprite", body.AddComponent<SpriteRenderer>());
        Set(unit, "color", tint.AddComponent<SpriteRenderer>());
        Set(unit, "id", id); Set(unit, "hp", 20f); Set(unit, "max_health", 20f);
        Set(unit, "attack", 6); Set(unit, "attack_range", 1); Set(unit, "is_king", king);
        return unit;
    }
    [TearDown]
    public void Cleanup()
    {
        foreach (var go in objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
        objects.Clear();
    }
    [Test]
    public void LeaderPromotionIsIdempotentAndRestoreNeverAppliesBonusAgain()
    {
        var unit = Unit(1);
        Call(unit, "Set_king", true);
        Call(unit, "Set_king", true);
        Assert.That(Get<float>(unit, "max_health"), Is.EqualTo(30));
        Assert.That(Get<int>(unit, "attack"), Is.EqualTo(9));
        Set(unit, "max_health", 20f); Set(unit, "hp", 12f); Set(unit, "attack", 6);
        Call(unit, "RestoreLeaderStatus", true);
        Call(unit, "Set_king", true);
        Assert.That(Get<float>(unit, "max_health"), Is.EqualTo(20));
        Assert.That(Get<float>(unit, "hp"), Is.EqualTo(12));
        Assert.That(Get<int>(unit, "attack"), Is.EqualTo(6));
    }
    [Test]
    public void AnyLethalDamageClearsTileAndDeadUnitsCannotBeHealed()
    {
        var unit = Unit(1, true);
        var tile = Create("Tile");
        var team = Create("Team");
        Set(tile, "unit", unit.gameObject); Set(unit, "current_tile", tile);
        Call(team, "Add", unit);
        Call(unit, "Take_damage", 100f);
        Assert.That(Get<bool>(unit, "has_died"), Is.True);
        Assert.That(Get<GameObject>(tile, "unit"), Is.Null);
        Assert.That(Get<bool>(team, "has_lost"), Is.True);
        Call(unit, "Heal", 100f);
        Assert.That(Get<float>(unit, "hp"), Is.Zero);
    }
    [Test]
    public void RemovalAndRecruitmentNeverReuseAnExistingId()
    {
        var team = Create("Team");
        var first = Unit(1); var second = Unit(2); var third = Unit(3);
        Call(team, "Add", first); Call(team, "Add", second); Call(team, "Add", third);
        Call(team, "Remove", second);
        var recruit = Unit(3);
        Call(team, "Add", recruit);
        Assert.That(Get<int>(recruit, "id"), Is.EqualTo(4));
        Assert.That(((IList)team.GetType().GetField("units").GetValue(team)).Count, Is.EqualTo(3));
    }
    [Test]
    public void UnitExecutionUsesTheSameCombatResultAsPreview()
    {
        var attacker = Unit(1); var defender = Unit(2);
        Set(attacker, "hp", 6.5f); Set(attacker, "max_health", 13f); Set(attacker, "attack", 4);
        Set(defender, "hp", 8f); Set(defender, "max_health", 8f); Set(defender, "attack", 4);
        var expected = Tactics.Core.CombatRules.Resolve(new Tactics.Core.Combatant(6.5f, 13, 4, 1), new Tactics.Core.Combatant(8, 8, 4, 1), 1);
        Call(attacker, "Attack_unit", defender, 1);
        Assert.That(Get<float>(attacker, "hp"), Is.EqualTo(expected.AttackerHealth).Within(0.0001f));
        Assert.That(Get<float>(defender, "hp"), Is.EqualTo(expected.DefenderHealth).Within(0.0001f));
    }
}
