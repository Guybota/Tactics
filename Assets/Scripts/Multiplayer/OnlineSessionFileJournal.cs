using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Tactics.Core;
using UnityEngine;

// Public recovery data only. The scope separates accounts, projects and UGS environments.
public sealed class OnlineSessionFileJournal : IOnlineSessionJournal
{
    private readonly SaveFileStore files;
    private readonly string fileName;
    private readonly string matchId;

    public OnlineSessionFileJournal(string directory, string projectId, string environment, string playerId, string matchId)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(environment)
            || string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(matchId))
            throw new ArgumentException("Project, environment, player and match are required for the recovery scope.");
        foreach (string part in new[] { projectId, environment, playerId, matchId })
        {
            if (part.Length > 128) throw new ArgumentException("Invalid recovery scope.");
            foreach (char c in part) if (char.IsControl(c)) throw new ArgumentException("Invalid recovery scope.");
        }
        this.matchId = matchId;
        files = new SaveFileStore(directory);
        using (var hash = SHA256.Create())
            fileName = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(
                projectId + "\n" + environment + "\n" + playerId + "\n" + matchId))).Replace("-", "").ToLowerInvariant();
    }

    public void Save(OnlineMatchSessionData data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        data.Validate();
        if (!string.Equals(data.MatchId, matchId, StringComparison.Ordinal))
            throw new InvalidDataException("Recovery data belongs to a different match.");
        files.Write(fileName, JsonUtility.ToJson(data));
    }

    public OnlineMatchSessionData Read()
    {
        if (!files.Exists(fileName)) return null;
        string json = files.Read(fileName);
        if (json.Length > 2 * 1024 * 1024) throw new InvalidDataException("The recovery file is too large.");
        var data = JsonUtility.FromJson<OnlineMatchSessionData>(json);
        if (data == null) throw new InvalidDataException("Invalid recovery data.");
        data.Validate();
        if (!string.Equals(data.MatchId, matchId, StringComparison.Ordinal))
            throw new InvalidDataException("Recovery data belongs to a different match.");
        return data;
    }
}
