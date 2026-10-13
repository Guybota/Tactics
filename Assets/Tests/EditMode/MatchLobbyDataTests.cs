using System;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;

public class MatchLobbyDataTests
{
    [Test]
    public void CreationAndJoinWireRoundTripRequiresExplicitSchemaAndHasNoIdentity()
    {
        var create = new MatchCreateRequest { SchemaVersion = 1, MapId = "map", RaceId = "race", BuildMode = true, BattleRoyaleMode = true };
        string json = JsonUtility.ToJson(create);
        var restored = JsonUtility.FromJson<MatchCreateRequest>(json); restored.Validate();
        Assert.That(restored.BuildMode && restored.BattleRoyaleMode, Is.True);
        Assert.That(json, Does.Not.Contain("PlayerId"));
        Assert.Throws<ArgumentException>(() => JsonUtility.FromJson<MatchCreateRequest>(json.Replace("\"SchemaVersion\":1,", "")).Validate());
        var join = new MatchJoinRequest { SchemaVersion = 1, MatchId = "match", RaceId = "race" };
        json = JsonUtility.ToJson(join);
        JsonUtility.FromJson<MatchJoinRequest>(json).Validate();
        Assert.Throws<ArgumentException>(() => JsonUtility.FromJson<MatchJoinRequest>(json.Replace("\"SchemaVersion\":1,", "")).Validate());
    }

    [Test]
    public void WaitingReplyValidatesPublicLobbyAndRejectsMalformedStatusAndSlot()
    {
        var reply = new MatchLobbyReply { SchemaVersion = 1, Success = true, MatchId = "match", MapId = "map",
            ContentVersion = "rules1", Status = MatchLobbyStatus.Waiting, PlayerSlot = 0, Revision = 0 };
        string json = JsonUtility.ToJson(reply);
        var restored = JsonUtility.FromJson<MatchLobbyReply>(json); restored.Validate();
        Assert.That(restored.Snapshot, Is.Null);
        Assert.That(json, Does.Not.Contain("RandomState")); Assert.That(json, Does.Not.Contain("PlayerId"));
        reply.Status = (MatchLobbyStatus)99; Assert.Throws<ArgumentException>(() => reply.Validate());
        reply.Status = MatchLobbyStatus.Waiting; reply.PlayerSlot = -1; Assert.Throws<ArgumentException>(() => reply.Validate());
        reply.PlayerSlot = 0; reply.Status = MatchLobbyStatus.Active; Assert.Throws<ArgumentException>(() => reply.Validate());
        reply.Status = MatchLobbyStatus.Completed; Assert.Throws<ArgumentException>(() => reply.Validate());
        Assert.Throws<ArgumentException>(() => JsonUtility.FromJson<MatchLobbyReply>(json.Replace("\"SchemaVersion\":1,", "")).Validate());
    }

    [Test]
    public void WaitingAndFailureCannotHidePayloadfulSnapshotBehindMissingVersion()
    {
        var reply = new MatchLobbyReply { SchemaVersion = 1, Success = true, MatchId = "match", MapId = "map",
            ContentVersion = "rules1", Status = MatchLobbyStatus.Waiting, PlayerSlot = 0, Revision = 0,
            Snapshot = new MatchSnapshotData { Width = 8 } };
        Assert.Throws<ArgumentException>(() => reply.Validate());
        reply.Success = false;
        Assert.Throws<ArgumentException>(() => reply.Validate());
        reply.Snapshot.Width = 0; reply.Snapshot.Cells.Add(new MatchCellData());
        Assert.Throws<ArgumentException>(() => reply.Validate());
    }
}
