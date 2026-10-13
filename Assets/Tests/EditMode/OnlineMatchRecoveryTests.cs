using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;

public class OnlineMatchRecoveryTests
{
    private sealed class Journal : IOnlineSessionJournal
    {
        public OnlineMatchSessionData Stored;
        public int Saves, FailOnSave;
        public bool FailWrites;
        public void Save(OnlineMatchSessionData data)
        {
            Saves++;
            if (FailWrites || Saves == FailOnSave) throw new IOException("Storage unavailable.");
            Stored = JsonUtility.FromJson<OnlineMatchSessionData>(JsonUtility.ToJson(data));
            Stored.Validate();
        }
    }
    private sealed class Transport : IMatchTransport
    {
        private readonly AuthoritativeMatch authority;
        public readonly List<MatchCommand> Commands = new List<MatchCommand>();
        public bool LoseNextReply;
        public Action<MatchCommand> BeforeSend;
        public Transport(AuthoritativeMatch authority) { this.authority = authority; }
        public Task<MatchTransportReply> ReadAsync(string matchId) => Task.FromResult(Wire(authority.Snapshot("alice")));
        public Task<MatchTransportReply> SubmitAsync(MatchCommand command)
        {
            BeforeSend?.Invoke(command);
            Commands.Add(command);
            var reply = Wire(authority.Submit("alice", command));
            if (LoseNextReply) { LoseNextReply = false; return Task.FromException<MatchTransportReply>(new TimeoutException()); }
            return Task.FromResult(reply);
        }
        private static MatchTransportReply Wire(SessionActionResult result)
            => new MatchTransportReply { MatchId = "match", Success = result.Success, Error = result.Error,
                Revision = result.Revision, Snapshot = result.State == null ? null : MatchSnapshotData.FromState(result.State) };
    }

    private AuthoritativeMatch authority;
    private MatchSnapshotData initial;
    [SetUp]
    public void SetUp()
    {
        var content = new MatchContent(); content.AddUnit(new UnitTypeStats("leader", 20, 5, 4, 1, 1000));
        content.AddRace("race", new[] { "leader" }); content.SetDamage("leader", "leader", 55);
        var state = new MatchState(8, 8) { RandomState = 12345 };
        for (int p = 0; p < 2; p++)
        {
            var player = new PlayerState { RaceId = "race", Money = 5000, NextUnitId = 2 };
            player.Units.Add(new UnitState { Id = 1, TypeId = "leader", IsLeader = true, Health = 20, MaxHealth = 20,
                Attack = 5, Movement = 4, Range = 1, Position = p == 0 ? new GridPosition(1, 1) : new GridPosition(6, 6) });
            state.Players.Add(player);
        }
        initial = MatchSnapshotData.FromState(state);
        authority = new AuthoritativeMatch("match", new MatchEngine(content), state,
            new Dictionary<string, int> { { "alice", 0 }, { "bob", 1 } });
    }

    [Test]
    public async Task RestartAfterCommitAndLostReplyRetriesPersistedExactIdOnlyOnce()
    {
        var journal = new Journal(); var transport = new Transport(authority) { LoseNextReply = true };
        transport.BeforeSend = command => Assert.That(journal.Stored.PendingCommand.CommandId, Is.EqualTo(command.CommandId));
        var session = new OnlineMatchSession(transport, "match", initial, journal: journal);
        Assert.That(journal.Stored.PendingCommand, Is.Null);
        Assert.That((await session.SubmitAsync(MatchAction.EndTurn(0), 0)).Success, Is.False);
        var json = JsonUtility.ToJson(journal.Stored);
        Assert.That(json, Does.Not.Contain("RandomState")); Assert.That(json, Does.Not.Contain("alice"));
        var restarted = OnlineMatchSession.Restore(transport, JsonUtility.FromJson<OnlineMatchSessionData>(json), journal);
        Assert.That(restarted.HasPendingCommand, Is.True); Assert.That(restarted.IsRequestRunning, Is.False);
        Assert.That((await restarted.SubmitAsync(MatchAction.EndTurn(0), 0)).Success, Is.False);
        Assert.That(transport.Commands.Count, Is.EqualTo(1));
        Assert.That((await restarted.RetryPendingAsync()).Success, Is.True);
        Assert.That(transport.Commands[1].CommandId, Is.EqualTo(transport.Commands[0].CommandId));
        Assert.That(authority.Snapshot("alice").Revision, Is.EqualTo(1));
        Assert.That(journal.Stored.PendingCommand, Is.Null); Assert.That(journal.Stored.Revision, Is.EqualTo(1));
    }

    [Test]
    public async Task JournalFailureBeforeSendingBlocksTransportAndKeepsSameIdForRetry()
    {
        var journal = new Journal(); var transport = new Transport(authority);
        var session = new OnlineMatchSession(transport, "match", initial, journal: journal);
        journal.FailWrites = true;
        Assert.That((await session.SubmitAsync(MatchAction.EndTurn(0), 0)).Success, Is.False);
        Assert.That(transport.Commands, Is.Empty); Assert.That(session.IsRequestRunning, Is.False);
        Assert.That(session.HasPendingCommand, Is.True);
        string id = session.CaptureReconnectData().PendingCommand.CommandId;
        Assert.That((await session.RetryPendingAsync()).Success, Is.False); Assert.That(transport.Commands, Is.Empty);
        journal.FailWrites = false;
        Assert.That((await session.RetryPendingAsync()).Success, Is.True);
        Assert.That(transport.Commands[0].CommandId, Is.EqualTo(id));
    }

    [Test]
    public async Task JournalFailureAfterConfirmationKeepsUncertainEnvelopeAndOldStateForSafeRestart()
    {
        var journal = new Journal { FailOnSave = 3 }; var transport = new Transport(authority);
        var session = new OnlineMatchSession(transport, "match", initial, journal: journal);
        Assert.That((await session.SubmitAsync(MatchAction.EndTurn(0), 0)).Success, Is.False);
        Assert.That(session.Revision, Is.Zero); Assert.That(session.HasPendingCommand, Is.True);
        Assert.That(journal.Stored.PendingCommand, Is.Not.Null); Assert.That(journal.Stored.Revision, Is.Zero);
        Assert.That(authority.Snapshot("alice").Revision, Is.EqualTo(1));
        var restarted = OnlineMatchSession.Restore(transport, journal.Stored, journal);
        Assert.That((await restarted.RetryPendingAsync()).Success, Is.True);
        Assert.That(restarted.Revision, Is.EqualTo(1)); Assert.That(restarted.HasPendingCommand, Is.False);
        Assert.That(transport.Commands[1].CommandId, Is.EqualTo(transport.Commands[0].CommandId));
        Assert.That(authority.Snapshot("alice").Revision, Is.EqualTo(1));
    }

    [Test]
    public async Task FailedRefreshJournalCannotInstallUnpersistedStateOrErasePending()
    {
        var journal = new Journal(); var transport = new Transport(authority) { LoseNextReply = true };
        var session = new OnlineMatchSession(transport, "match", initial, journal: journal);
        await session.SubmitAsync(MatchAction.EndTurn(0), 0);
        journal.FailWrites = true;
        Assert.That((await session.RefreshAsync()).Success, Is.False);
        Assert.That(session.Revision, Is.Zero); Assert.That(session.HasPendingCommand, Is.True);
        journal.FailWrites = false;
        Assert.That((await session.RefreshAsync()).Success, Is.True);
        Assert.That(session.Revision, Is.EqualTo(1)); Assert.That(journal.Stored.PendingCommand, Is.Not.Null);
        var restarted = OnlineMatchSession.Restore(transport, journal.Stored, journal);
        Assert.That((await restarted.RetryPendingAsync()).Success, Is.True);
        Assert.That(restarted.Revision, Is.EqualTo(1));
    }

    [Test]
    public void InitialJournalFailureThrowsAndCaptureAndRestoreDoNotShareMutableDtos()
    {
        var transport = new Transport(authority);
        Assert.Throws<IOException>(() => new OnlineMatchSession(transport, "match", initial, journal: new Journal { FailWrites = true }));
        var session = new OnlineMatchSession(transport, "match", initial);
        var data = session.CaptureReconnectData();
        var restored = OnlineMatchSession.Restore(transport, data);
        data.Snapshot.Players[0].Money = 0;
        Assert.That(session.Snapshot().Players[0].Money, Is.EqualTo(5000));
        Assert.That(restored.Snapshot().Players[0].Money, Is.EqualTo(5000));
    }

    [Test]
    public void RestoreRejectsMissingVersionForeignPendingAndImpossiblePendingRevision()
    {
        var transport = new Transport(authority); var session = new OnlineMatchSession(transport, "match", initial);
        var data = session.CaptureReconnectData(); data.SchemaVersion = 0;
        Assert.Throws<ArgumentException>(() => OnlineMatchSession.Restore(transport, data));
        data = session.CaptureReconnectData(); data.HasPendingCommand = true;
        data.PendingCommand = MatchCommandData.FromCommand(new MatchCommand("other", "id", 0, MatchAction.EndTurn(0)));
        Assert.Throws<ArgumentException>(() => OnlineMatchSession.Restore(transport, data));
        data = session.CaptureReconnectData(); data.HasPendingCommand = true;
        data.PendingCommand = MatchCommandData.FromCommand(new MatchCommand("match", "id", 1, MatchAction.EndTurn(0)));
        Assert.Throws<ArgumentException>(() => OnlineMatchSession.Restore(transport, data));
        data = session.CaptureReconnectData(); data.HasPendingCommand = true;
        data.PendingCommand = MatchCommandData.FromCommand(new MatchCommand("match", "id", 0, MatchAction.EndTurn(3)));
        Assert.Throws<ArgumentException>(() => OnlineMatchSession.Restore(transport, data));
        var json = JsonUtility.ToJson(session.CaptureReconnectData()).Replace("\"SchemaVersion\":1,", "");
        Assert.Throws<ArgumentException>(() => OnlineMatchSession.Restore(transport, JsonUtility.FromJson<OnlineMatchSessionData>(json)));
    }

    [Test]
    public void ExplicitPendingFlagRejectsPayloadfulUnexpectedOrMalformedPending()
    {
        var transport = new Transport(authority); var session = new OnlineMatchSession(transport, "match", initial);
        var data = session.CaptureReconnectData();
        data.PendingCommand = MatchCommandData.FromCommand(new MatchCommand("match", "id", 0, MatchAction.EndTurn(0)));
        Assert.That(data.HasPendingCommand, Is.False);
        Assert.Throws<ArgumentException>(() => data.Validate());
        data.PendingCommand.SchemaVersion = 0;
        Assert.Throws<ArgumentException>(() => data.Validate());
        data.HasPendingCommand = true;
        Assert.Throws<ArgumentException>(() => data.Validate());
        data.PendingCommand = null;
        Assert.Throws<ArgumentException>(() => data.Validate());
    }
}
