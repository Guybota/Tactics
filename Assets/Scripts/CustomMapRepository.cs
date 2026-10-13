using System;
using System.Collections.Generic;
using System.IO;
using Tactics.Core;
using UnityEngine;

public sealed class CustomMapRepository
{
    private readonly SaveFileStore files;
    public CustomMapRepository(string persistentDirectory)
        => files = new SaveFileStore(Path.Combine(persistentDirectory, "Maps"));
    public bool Exists(string id) => files.Exists(id);
    public void Delete(string id) => files.Delete(id);
    public CustomMapData Read(string id)
    {
        var map = JsonUtility.FromJson<CustomMapData>(files.Read(id));
        CustomMapValidator.ValidateStructure(map);
        if (map.id != id) throw new InvalidDataException("Map identifier does not match its file.");
        return map;
    }
    public void Write(CustomMapData map)
    {
        CustomMapValidator.ValidateStructure(map);
        files.Write(map.id, JsonUtility.ToJson(map));
    }
    public List<CustomMapData> List()
    {
        var maps = new List<CustomMapData>();
        foreach (string id in files.ListNames())
        {
            try { maps.Add(Read(id)); }
            catch (Exception exception) { Debug.LogWarning("Cannot read custom map '" + id + "': " + exception.Message); }
        }
        maps.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        return maps;
    }
}
