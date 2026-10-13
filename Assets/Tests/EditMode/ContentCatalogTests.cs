using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class ContentCatalogTests
{
    private static Type Script(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Property(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
    [Test]
    public void SharedCatalogAndAllPlayableDamageMatchupsAreValid()
    {
        var catalog = AssetDatabase.LoadAssetAtPath("Assets/Data/RaceCatalog.asset", Script("RaceCatalog"));
        Assert.That(catalog, Is.Not.Null);
        Assert.DoesNotThrow(() => Script("RaceCatalog").GetMethod("Validate").Invoke(catalog, null));
        var table = AssetDatabase.LoadAssetAtPath("Assets/Data/CombatDamageTable.asset", Script("CombatDamageTable"));
        Assert.That(table, Is.Not.Null);
        Assert.DoesNotThrow(() => Script("CombatDamageTable").GetMethod("Validate").Invoke(table, null));
        foreach (var race in (IEnumerable)Property(catalog, "Races"))
        {
            var prefabs = new ArrayList((ICollection)Property(race, "Units"));
            prefabs.Add(Property(race, "Leader"));
            foreach (GameObject prefab in prefabs)
            {
                var unit = prefab.GetComponent(Script("Unit"));
                Assert.That(Property(unit, "CombatDefinition"), Is.Not.Null, prefab.name);
                var configuredTable = Script("Unit").GetField("damageTable", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(unit);
                Assert.That(configuredTable, Is.SameAs(table), prefab.name);
            }
        }
    }
    [Test]
    public void ChangingRaceDisplayNamePreservesStableIdentityAndLegacySaveKey()
    {
        var source = AssetDatabase.LoadAssetAtPath("Assets/Data/Races/Human.asset", Script("RaceDefinition"));
        var copy = UnityEngine.Object.Instantiate(source);
        try
        {
            var serialized = new SerializedObject(copy);
            serialized.FindProperty("displayName").stringValue = "Renamed humans";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(Property(copy, "Id"), Is.EqualTo(Property(source, "Id")));
            Assert.That(Property(copy, "LegacySaveName"), Is.EqualTo(Property(source, "LegacySaveName")));
        }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
    }
    [Test]
    public void CatalogRejectsDuplicateRaceIdentities()
    {
        var source = AssetDatabase.LoadAssetAtPath("Assets/Data/RaceCatalog.asset", Script("RaceCatalog"));
        var copy = UnityEngine.Object.Instantiate(source);
        try
        {
            var serialized = new SerializedObject(copy);
            var races = serialized.FindProperty("races");
            races.GetArrayElementAtIndex(1).objectReferenceValue = races.GetArrayElementAtIndex(0).objectReferenceValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var error = Assert.Throws<TargetInvocationException>(() => Script("RaceCatalog").GetMethod("Validate").Invoke(copy, null));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
        }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
    }
}
