using System;
using System.Reflection;
using NUnit.Framework;
using Tactics.Core;
using UnityEditor;
using UnityEngine;

public class MultiplayerCatalogExportTests
{
    private static MatchContentData ExportData()
    {
        var catalogType = Type.GetType("RaceCatalog, Assembly-CSharp", true);
        var exporter = Type.GetType("MultiplayerContentExport, Assembly-CSharp-Editor", true);
        var catalog = AssetDatabase.LoadAssetAtPath("Assets/Data/RaceCatalog.asset", catalogType);
        return (MatchContentData)exporter.GetMethod("Build", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new[] { catalog });
    }

    [Test]
    public void LiveCatalogExportsDeterministicallyWithoutPrefabReferences()
    {
        var first = ExportData();
        var second = ExportData();
        Assert.That(first.Version, Does.StartWith("sha256."));
        Assert.That(JsonUtility.ToJson(first), Is.EqualTo(JsonUtility.ToJson(second)));
        Assert.That(first.Races, Is.Not.Empty);
        Assert.That(first.Damage.Count, Is.EqualTo(first.Units.Count * first.Units.Count));
        Assert.That(first.ToContent(), Is.Not.Null);
    }

    [Test]
    public void UnityJsonRoundTripPreservesEveryExportedDamageMatchupAndRecruit()
    {
        var data = ExportData();
        var decoded = JsonUtility.FromJson<MatchContentData>(JsonUtility.ToJson(data));
        var portable = decoded.ToContent();
        foreach (var entry in data.Damage)
            Assert.That(portable.DamagePercent(entry.AttackerTypeId, entry.DefenderTypeId), Is.EqualTo(entry.Percent));
        foreach (var race in data.Races)
        {
            Assert.That(portable.Unit(race.LeaderTypeId), Is.Not.Null);
            foreach (var recruit in race.RecruitTypeIds) Assert.That(portable.CanRecruit(race.Id, recruit), Is.True);
        }
    }
}
