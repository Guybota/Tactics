using System;
using System.IO;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;

public class OnlineSessionFileJournalTests
{
    private string directory;
    [SetUp]
    public void SetUp()
    {
        directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "OnlineJournalTests-" + Guid.NewGuid().ToString("N")));
    }
    [TearDown]
    public void TearDown()
    {
        string testRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp")) + Path.DirectorySeparatorChar;
        if (directory.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    private OnlineSessionFileJournal Journal(string player = "alice", string environment = "development")
        => new OnlineSessionFileJournal(directory, "project", environment, player, "match");

    private static OnlineMatchSessionData Data(bool pending)
    {
        var state = new MatchState(10, 10) { RandomState = 12345 };
        for (int slot = 0; slot < 2; slot++)
        {
            var player = new PlayerState { RaceId = "race", NextUnitId = 2 };
            player.Units.Add(new UnitState { Id = 1, TypeId = "leader", IsLeader = true,
                Health = 10, MaxHealth = 10, Attack = 5, Movement = 3, Range = 1,
                Position = new GridPosition(slot == 0 ? 1 : 8, 2) });
            state.Players.Add(player);
        }
        return new OnlineMatchSessionData { SchemaVersion = 1, MatchId = "match", Revision = 0,
            HasPendingCommand = pending,
            Snapshot = MatchSnapshotData.FromState(state), PendingCommand = pending
                ? MatchCommandData.FromCommand(new MatchCommand("match", "original-command", 0, MatchAction.EndTurn(0))) : null };
    }

    [Test]
    public void PendingCommandAndClearedConfirmationSurviveJournalRecreation()
    {
        Journal().Save(Data(true));
        var pending = Journal().Read();
        Assert.That(pending.PendingCommand.ToCommand().CommandId, Is.EqualTo("original-command"));
        Journal().Save(Data(false));
        Assert.That(Journal().Read().PendingCommand, Is.Null);
        Assert.That(Journal("bob").Read(), Is.Null);
        Assert.That(Journal(environment: "production").Read(), Is.Null);
        string json = File.ReadAllText(Directory.GetFiles(directory, "*.json")[0]);
        Assert.That(json, Does.Not.Contain("RandomState"));
        Assert.That(json, Does.Not.Contain("AccessToken"));
    }

    [Test]
    public void CorruptRecoveryDoesNotSilentlyReplaceThePendingCommand()
    {
        Journal().Save(Data(true));
        string path = Directory.GetFiles(directory, "*.json")[0];
        File.WriteAllText(path, "{\"SchemaVersion\":999}");
        Assert.Throws<ArgumentException>(() => Journal().Read());
        Assert.That(File.ReadAllText(path), Does.Contain("999"));
    }
}
