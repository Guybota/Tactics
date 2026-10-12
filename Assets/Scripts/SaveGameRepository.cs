using System;
using System.Collections.Generic;
using System.IO;
using Tactics.Core;
using UnityEngine;

// Game data is authoritative; the legacy index is regenerated from readable files.
public sealed class SaveGameRepository
{
    private readonly SaveFileStore files;
    public SaveGameRepository(string directory) { files = new SaveFileStore(directory); }
    public bool Exists(string name) => files.Exists(name);
    public GameSave Read(string name)
    {
        GameSave save = JsonUtility.FromJson<GameSave>(files.Read(name));
        if (save == null || save.controller == null || save.schemaVersion < 0 || save.schemaVersion > 1)
            throw new InvalidDataException("Invalid or unsupported save format.");
        save.saveName = name;
        return save;
    }
    public List<SaveInfo> List()
    {
        var list = new List<SaveInfo>();
        foreach (string name in files.ListNames())
        {
            try
            {
                var save = Read(name);
                if (save.date != null) list.Add(new SaveInfo(save));
            }
            catch (Exception exception) { Debug.LogWarning("Cannot read save '" + name + "': " + exception.Message); }
        }
        list.Sort((a, b) => string.CompareOrdinal(a.saveName, b.saveName));
        return list;
    }
    public void Write(string name, GameSave save)
    {
        if (save == null) throw new ArgumentNullException(nameof(save));
        save.schemaVersion = 1;
        save.saveName = name;
        files.Write(name, JsonUtility.ToJson(save));
        UpdateIndex();
    }
    public void Delete(string name)
    {
        files.Delete(name);
        UpdateIndex();
    }
    private void UpdateIndex()
    {
        // An index failure must never discard a successfully written game.
        try { files.WriteIndex(JsonUtility.ToJson(new SaveManager { Saves = List() })); }
        catch (Exception exception) { Debug.LogWarning("Save index could not be updated: " + exception.Message); }
    }
}
