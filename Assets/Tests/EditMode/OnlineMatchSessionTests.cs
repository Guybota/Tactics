using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;

public class OnlineMatchSessionTests
{
    private sealed class AuthenticatedTransport : IMatchTransport
    {
        private readonly AuthoritativeMatch authority;
        private readonly string playerId;
        public readonly List<MatchCommand> Commands = new List<MatchCommand>();
        public Func<MatchCommand, Task<MatchTransportReply>> SubmitOverride;
        public Func<MatchTransportReply> ReadOverride;
        public bool LoseNextReply;

        public AuthenticatedTransport(AuthoritativeMatch authority, string playerId)
        { this.authority = authority; this.playerId = playerId; }

        public Task<MatchTransportReply> ReadAsync(string matchId)
            => Task.FromResult(ReadOverride != null ? ReadOverride() : Wire(authority.Snapshot(playerId)));

        public Task<MatchTransportReply> SubmitAsync(MatchCommand command)
        {
            Commands.Add(command);
            if (SubmitOverride != null) return SubmitOverride(command);
            var reply = Commit(command);
            if (LoseNextReply) { LoseNextReply = false; return Task.FromException<MatchTransportReply>(new TimeoutException()); }
            return Task.FromResult(reply);
        }

        public MatchTransportReply Commit(MatchCommand command) => Wire(authority.Submit(playerId, command));
        public static MatchTransportReply Wire(SessionActionResult result)
        {
            var state = result.State;
            return new MatchTransportReply { MatchId = "match", Success = result.Success, Error = result.Error,
                Revision = result.Revision, Snapshot = state == null ? null : MatchSnapshotData.FromState(state) };
        }
    }

    private AuthoritativeMatch authority;
    private MatchSnapshotData initial;

    [SetUp]
    public void SetUp()
    {
        var content = new MatchContent();
        content.AddUnit(new UnitTypeStats("leader", 20, 5, 4, 1, 1000));
        content.AddRace("race", new[] { "leader" });
        content.SetDamage("leader", "leader", 55);
        var state = new MatchState(8, 8) { RandomState = 12345 };
        for (int player = 0; player < 2; player++)
        {
            var p = new PlayerState { RaceId = "race", Money = 5000, NextUnitId = 2 };
            p.Units.Add(new UnitState { Id = 1, TypeId = "leader", IsLeader = true,
                Health = 20, MaxHealth = 20, Attack = 5, Movement = 4, Range = 1,
                Position = player == 0 ? new GridPosition(1, 1) : new GridPosition(6, 6) });
            state.Players.Add(p);
        }
        initial = MatchSnapshotData.FromState(state);
        authority = new AuthoritativeMatch("match", new MatchEngine(content), state,
            new Dictionary<string, int> { { "alice", 0 }, { "bob", 1 } });
    }

    [Test]
    public async Task TwoAuthenticatedClientsPlayUsingConfirmedSnapshots()
    {
        var alice = new OnlineMatchSession(new AuthenticatedTransport(authority, "alice"), "match", initial);
        var bob = new OnlineMatchSession(new AuthenticatedTransport(authority, "bob"), "match", initial);
        Assert.That((await alice.SubmitAsync(MatchAction.EndTurn(0), 0)).Success, Is.True);
        Assert.That(bob.Revision, Is.Zero);
        Assert.That((await bob.RefreshAsync()).Success, Is.True);
        Assert.That(bob.Snapshot().ActivePlayer, Is.EqualTo(1));
        Assert.That((await bob.SubmitAsync(MatchAction.EndTurn(1), 1)).Success, Is.True);
        Assert.That((await alice.RefreshAsync()).Success, Is.True);
        Assert.That(alice.Revision, Is.EqualTo(2));
        Assert.That(alice.Snapshot().Turn, Is.EqualTo(bob.Snapshot().Turn));
        Assert.That(alice.Snapshot().Turn, Is.EqualTo(2));
    }

    [Test]
    public async Task CommitThenLoseReplyRetriesExactEnvelopeAndDoesNotDuplicateTurn()
    {
        var transport = new AuthenticatedTransport(authority, "alice") { LoseNextReply = true };
        var session = new OnlineMatchSession(transport, "match", initial);
        Assert.That((await session.SubmitAsync(MatchAction.EndTurn(0), 0)).Success, Is.False);
        Assert.That(session.Revision, Is.Zero);
        Assert.That(session.HasPendingCommand, Is.True);
        Assert.That(authority.Snapshot("alice").Revision, Is.EqualTo(1));
        Assert.That((await session.RetryPendingAsync()).Success, Is.True);
        Assert.That(transport.Commands.Count, Is.EqualTo(2));
        Assert.That(transport.Commands[1], Is.SameAs(transport.Commands[0]));
        Assert.That(session.HasPendingCommand, Is.False);
        Assert.That(session.Revision, Is.EqualTo(1));
        Assert.That(authority.Snapshot("alice").State.Turn, Is.EqualTo(1));
    }

    [Test]
    public async Task OlderReceiptAfterRefreshResolvesPendingWithoutRollingBackNewerSnapshot()
    {
        var aliceTransport = new AuthenticatedTransport(authority, "alice") { LoseNextReply = true };
        var alice = new OnlineMatchSession(aliceTransport, "match", initial);
        var bob = new OnlineMatchSession(new AuthenticatedTransport(authority, "bob"), "match", initial);
        await alice.SubmitAsync(MatchAction.EndTurn(0), 0);
        await bob.RefreshAsync();
        Assert.That((await bob.SubmitAsync(MatchAction.EndTurn(1), 1)).Success, Is.True);
        await alice.RefreshAsync();
        Assert.That(alice.Revision, Is.EqualTo(2));
        Assert.That(alice.HasPendingCommand, Is.True);
        Assert.That((await alice.RetryPendingAsync()).Success, Is.True);
        Assert.That(alice.HasPendingCommand, Is.False);
        Assert.That(alice.Revision, Is.EqualTo(2));
        Assert.That(alice.Snapshot().Turn, Is.EqualTo(2));
        Assert.That(authority.Snapshot("alice").Revision, Is.EqualTo(2));
    }

    [Test]
    public async Task StaleSubmissionReceivesNewStateAndNextActionUsesNewRevision()
    {
        var alice = new OnlineMatchSession(new AuthenticatedTransport(authority, "alice"), "match", initial);
        var bob = new OnlineMatchSession(new AuthenticatedTransport(authority, "bob"), "match", initial);
        await alice.SubmitAsync(MatchAction.EndTurn(0), 0);
        var stale = await bob.SubmitAsync(MatchAction.EndTurn(1), 0);
        Assert.That(stale.Success, Is.False);
        Assert.That(stale.Error, Does.Contain("revision"));
        Assert.That(bob.HasPendingCommand, Is.False);
        Assert.That(bob.Revision, Is.EqualTo(1));
        Assert.That(bob.Snapshot().Turn, Is.EqualTo(1));
        Assert.That((await bob.SubmitAsync(MatchAction.EndTurn(1), bob.Revision)).Success, Is.True);
        Assert.That(bob.Revision, Is.EqualTo(2));
    }

    [Test]
    public async Task InFlightRequestBlocksNewActionsAndUnknownPendingSurvivesRefresh()
    {
        var completion = new TaskCompletionSource<MatchTransportReply>();
        var transport = new AuthenticatedTransport(authority, "alice") { SubmitOverride = _ => completion.Task };
        var session = new OnlineMatchSession(transport, "match", initial);
        var first = session.SubmitAsync(MatchAction.EndTurn(0), 0);
        Assert.That(session.IsRequestRunning, Is.True);
        Assert.That((await session.SubmitAsync(MatchAction.EndTurn(0), 0)).Success, Is.False);
        Assert.That((await session.RefreshAsync()).Success, Is.False);
        Assert.That(transport.Commands.Count, Is.EqualTo(1));
        completion.SetException(new TimeoutException());
        Assert.That((await first).Success, Is.False);
        Assert.That(session.IsRequestRunning, Is.False);
        Assert.That((await session.RefreshAsync()).Success, Is.True);
        Assert.That(session.HasPendingCommand, Is.True);
        Assert.That((await session.SubmitAsync(MatchAction.EndTurn(0), 0)).Success, Is.False);
        transport.SubmitOverride = null;
        Assert.That((await session.RetryPendingAsync()).Success, Is.True);
        Assert.That(transport.Commands[1], Is.SameAs(transport.Commands[0]));
        Assert.That(session.HasPendingCommand, Is.False);
    }

    [TestCase("missing")] [TestCase("invalid")] [TestCase("unconfirmed")] [TestCase("wrong-match")]
    public async Task MalformedSuccessKeepsPendingCommandForSafeRetry(string fault)
    {
        var transport = new AuthenticatedTransport(authority, "alice");
        transport.SubmitOverride = command =>
        {
            var reply = transport.Commit(command);
            if (fault == "missing") reply.Snapshot = null;
            if (fault == "invalid") reply.Snapshot.SchemaVersion = 999;
            if (fault == "unconfirmed") reply.Revision = command.ExpectedRevision;
            if (fault == "wrong-match") reply.MatchId = "another";
            return Task.FromResult(reply);
        };
        var session = new OnlineMatchSession(transport, "match", initial);
        Assert.That((await session.SubmitAsync(MatchAction.EndTurn(0), 0)).Success, Is.False);
        Assert.That(session.HasPendingCommand, Is.True);
        Assert.That(session.Revision, Is.Zero);
        Assert.That(session.Snapshot().Turn, Is.Zero);
        transport.SubmitOverride = null;
        Assert.That((await session.RetryPendingAsync()).Success, Is.True);
        Assert.That(session.Revision, Is.EqualTo(1));
        Assert.That(authority.Snapshot("alice").Revision, Is.EqualTo(1));
    }

    [Test]
    public void ClientReplyJsonHasNoServerRandomStreamAndRoundTripsProjection()
    {
        var reply = AuthenticatedTransport.Wire(authority.Snapshot("alice"));
        string json = JsonUtility.ToJson(reply);
        Assert.That(json, Does.Not.Contain("RandomState"));
        Assert.That(json, Does.Not.Contain("12345"));
        var decoded = JsonUtility.FromJson<MatchTransportReply>(json);
        Assert.That(decoded.Snapshot.ToState().Players[0].Money, Is.EqualTo(5000));
        Assert.That(authority.Snapshot("alice").State.RandomState, Is.EqualTo(12345));
    }

    private static MatchSnapshotData ChangedIdentity(MatchSnapshotData original, string fault)
    {
        var state = original.ToState();
        if (fault == "width" || fault == "height")
        {
            var resized = new MatchState(fault == "width" ? 7 : 8, fault == "height" ? 7 : 8);
            foreach (var player in state.Players) resized.Players.Add(player.Clone());
            state = resized;
        }
        if (fault == "players")
        {
            var third = state.Players[0].Clone(); third.Units[0].Position = new GridPosition(1, 6); state.Players.Add(third);
        }
        if (fault == "race") state.Players[0].RaceId = "different-race";
        if (fault == "unit-type") state.Players[0].Units[0].TypeId = "different-unit";
        if (fault == "terrain")
        {
            state.Cells[3, 3].Terrain = TerrainKind.Forest; state.Cells[3, 3].MovementCost = 2; state.Cells[3, 3].SupportsCastle = false;
        }
        if (fault == "rules") state.BuildMode = !state.BuildMode;
        return MatchSnapshotData.FromState(state);
    }

    [TestCase("width")] [TestCase("height")] [TestCase("players")] [TestCase("race")]
    [TestCase("unit-type")] [TestCase("terrain")] [TestCase("rules")]
    public async Task ImmutableIdentityChangeAfterCommitKeepsPendingAndCanRecoverSameCommand(string fault)
    {
        var transport = new AuthenticatedTransport(authority, "alice");
        transport.SubmitOverride = command =>
        {
            var reply = transport.Commit(command); reply.Snapshot = ChangedIdentity(reply.Snapshot, fault); return Task.FromResult(reply);
        };
        var session = new OnlineMatchSession(transport, "match", initial);
        Assert.That((await session.SubmitAsync(MatchAction.EndTurn(0), 0)).Success, Is.False);
        Assert.That(session.HasPendingCommand, Is.True); Assert.That(session.Revision, Is.Zero);
        Assert.That(JsonUtility.ToJson(MatchSnapshotData.FromState(session.Snapshot())), Is.EqualTo(JsonUtility.ToJson(initial)));
        transport.SubmitOverride = null;
        Assert.That((await session.RetryPendingAsync()).Success, Is.True);
        Assert.That(transport.Commands[1].CommandId, Is.EqualTo(transport.Commands[0].CommandId));
        Assert.That(session.HasPendingCommand, Is.False); Assert.That(session.Revision, Is.EqualTo(1));
        Assert.That(authority.Snapshot("alice").Revision, Is.EqualTo(1));
    }

    [TestCase("width")] [TestCase("height")] [TestCase("players")] [TestCase("race")]
    [TestCase("unit-type")] [TestCase("terrain")] [TestCase("rules")]
    public async Task ImmutableIdentityChangeOnRefreshPreservesConfirmedSnapshotAndRevision(string fault)
    {
        var transport = new AuthenticatedTransport(authority, "alice");
        transport.ReadOverride = () => new MatchTransportReply { MatchId = "match", Success = true, Revision = 1,
            Snapshot = ChangedIdentity(initial, fault) };
        var session = new OnlineMatchSession(transport, "match", initial);
        Assert.That((await session.RefreshAsync()).Success, Is.False);
        Assert.That(session.Revision, Is.Zero); Assert.That(session.HasPendingCommand, Is.False);
        Assert.That(JsonUtility.ToJson(MatchSnapshotData.FromState(session.Snapshot())), Is.EqualTo(JsonUtility.ToJson(initial)));
    }

    [Test]
    public async Task ImmutableChecksAllowRecruitmentAndDynamicCastleStormAndUnitState()
    {
        var content = new MatchContent(); content.AddUnit(new UnitTypeStats("leader", 20, 5, 4, 1, 1000));
        content.AddRace("race", new[] { "leader" }); content.SetDamage("leader", "leader", 55);
        var state = initial.ToState(); state.Cells[2, 2].HasCastle = true; state.Cells[2, 2].CastleOwner = 0;
        var server = new AuthoritativeMatch("match", new MatchEngine(content), state,
            new Dictionary<string, int> { { "alice", 0 }, { "bob", 1 } });
        var transport = new AuthenticatedTransport(server, "alice");
        var session = new OnlineMatchSession(transport, "match", MatchSnapshotData.FromState(state));
        Assert.That((await session.SubmitAsync(MatchAction.Recruit(0, new GridPosition(2, 2), "leader"), 0)).Success, Is.True);
        Assert.That(session.Snapshot().Players[0].Units.Count, Is.EqualTo(2));
        var dynamicState = session.Snapshot();
        var recruited = dynamicState.Players[0].Units[1]; recruited.Health = 0; recruited.IsDead = true;
        dynamicState.Cells[2, 2].HasCastle = false; dynamicState.Cells[2, 2].CastleOwner = -1; dynamicState.Cells[2, 2].HasStorm = true;
        transport.ReadOverride = () => new MatchTransportReply { MatchId = "match", Success = true, Revision = 2,
            Snapshot = MatchSnapshotData.FromState(dynamicState) };
        Assert.That((await session.RefreshAsync()).Success, Is.True);
        Assert.That(session.Revision, Is.EqualTo(2));
        Assert.That(session.Snapshot().Players[0].Units[1].IsDead, Is.True);
        Assert.That(session.Snapshot().Cells[2, 2].HasStorm, Is.True);
    }
}
