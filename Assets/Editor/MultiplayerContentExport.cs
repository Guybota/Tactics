using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Tactics.Core;
using UnityEditor;
using UnityEngine;

public static class MultiplayerContentExport
{
    public const string CatalogAssetPath = "Assets/Data/RaceCatalog.asset";
    public const string OutputPath = "Server/Content/match-content.json";

    public static MatchContentData Build(RaceCatalog catalog)
    {
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        catalog.Validate();
        var content = UnityMatchContent.Build(catalog.Races.Select(UnityMatchContent.Roster.From), out _);
        var leaders = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var race in catalog.Races)
            leaders.Add(race.Id, UnityMatchAdapter.TypeId(race.Leader.GetComponent<Unit>()));
        var data = MatchContentData.FromContent(content, leaders);
        // Sorted DTO entries make this revision stable across repeated exports.
        using (var hash = SHA256.Create())
            data.Version = "sha256." + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data))))
                .Replace("-", "").ToLowerInvariant();
        data.ToContent();
        return data;
    }

    [MenuItem("Tools/Tactics/Multiplayer/Export Server Content")]
    public static void Export()
    {
        var data = Build(AssetDatabase.LoadAssetAtPath<RaceCatalog>(CatalogAssetPath));
        string destination = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputPath));
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.WriteAllText(destination, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
        Debug.Log("Multiplayer content exported to " + destination + " (" + data.Version + ").");
    }
}
