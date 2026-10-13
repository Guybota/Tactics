using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Tactics.Core;

public class MultiplayerAuthorityTests
{
    private MatchEngine engine;

    [SetUp]
    public void SetUp()
    {
        var content = new MatchContent();
        content.AddUnit(new UnitTypeStats("leader", 20, 5, 4, 1, 1000));
        content.AddRace("race", new[] { "leader" });
        content.SetDamage("leader", "leader", 55);
        engine = new MatchEngine(content);
    }

    private static MatchState InitialState()
    {
        var state = new MatchState(8, 8) { RandomState = 12345 };
        for (int player = 0; player < 2; player++)
        {
            var slot = new PlayerState { RaceId = "race", Money = 5000, NextUnitId = 2 };
            slot.Units.Add(new UnitState { Id = 1, TypeId = "leader", IsLeader = true,
                Health = 20, MaxHealth = 20, Attack = 5, Movement = 4, Range = 1,
                Position = player == 0 ? new GridPosition(1, 1) : new GridPosition(6, 6) });
            state.Players.Add(slot);
        }
        return state;
    }

    private AuthoritativeMatch Authority(int capacity = 1024)
        => new AuthoritativeMatch("match", engine, InitialState(),
            new Dictionary<string, int> { { "alice", 0 }, { "bob", 1 } }, maxCommandReceipts: capacity);

    private static MatchCommand End(string id, int revision, int player)
        => new MatchCommand("match", id, revision, MatchAction.EndTurn(player));

    [Test]
    public void AuthenticationBindingRejectsSpoofedActingSlotAndHidesStateFromStrangers()
    {
        var match = Authority();
        Assert.That(match.Submit("bob", End("spoof", 0, 0)).Success, Is.False);
        Assert.That(match.Snapshot("alice").Revision, Is.Zero);
        var unknown = match.Submit("mallory", End("unknown", 0, 0));
        Assert.That(unknown.Success, Is.False);
        Assert.That(unknown.State, Is.Null);
        Assert.That(unknown.Revision, Is.EqualTo(-1));
        Assert.That(match.Snapshot("mallory").State, Is.Null);
    }

    [Test]
    public void ExactRetryAfterBothPlayersActReturnsOriginalReceiptWithoutExecutingAgain()
    {
        var match = Authority(); var command = End("first", 0, 0);
        Assert.That(match.Submit("alice", command).Success, Is.True);
        Assert.That(match.Submit("bob", End("second", 1, 1)).Success, Is.True);
        var repeated = match.Submit("alice", End("first", 0, 0));
        Assert.That(repeated.Success, Is.True);
        Assert.That(repeated.Revision, Is.EqualTo(1));
        Assert.That(repeated.State.Turn, Is.EqualTo(1));
        Assert.That(match.Snapshot("alice").Revision, Is.EqualTo(2));
        Assert.That(match.Snapshot("alice").State.Turn, Is.EqualTo(2));
    }

    [Test]
    public void DuplicateIdRejectsDifferentPayloadRevisionAndIdentity()
    {
        var match = Authority();
        Assert.That(match.Submit("alice", End("id", 0, 0)).Success, Is.True);
        Assert.That(match.Submit("alice", End("id", 1, 0)).Success, Is.False);
        Assert.That(match.Submit("bob", End("id", 0, 0)).Success, Is.False);
        var changedAction = new MatchAction(MatchActionKind.Wait, new UnitKey(0, 1), new GridPosition(2, 1));
        Assert.That(match.Submit("alice", new MatchCommand("match", "id", 0, changedAction)).Success, Is.False);
        Assert.That(match.Snapshot("alice").Revision, Is.EqualTo(1));
    }

    [Test]
    public void StaleRevisionIsRejectedAndRejectedReceiptCannotLaterExecute()
    {
        var match = Authority();
        var premature = End("premature", 0, 1);
        Assert.That(match.Submit("bob", premature).Success, Is.False);
        Assert.That(match.Submit("alice", End("first", 0, 0)).Success, Is.True);
        var retry = match.Submit("bob", premature);
        Assert.That(retry.Success, Is.False);
        Assert.That(retry.Revision, Is.Zero);
        var stale = match.Submit("bob", End("stale", 0, 1));
        Assert.That(stale.Success, Is.False);
        Assert.That(stale.Error, Does.Contain("revision"));
        Assert.That(match.Snapshot("alice").Revision, Is.EqualTo(1));
    }

    [Test]
    public void SessionCopiesInitialRestoreSnapshotAndResultStates()
    {
        var initial = InitialState(); var session = new LocalMatchSession(engine, initial);
        initial.Players[0].Money = -1;
        var snapshot = session.Snapshot(); snapshot.Players[0].Money = -2;
        var result = session.Submit(MatchAction.EndTurn(0), 0);
        Assert.That(result.Success, Is.True);
        result.State.Players[0].Units[0].Health = 0;
        result.State.Cells[0, 0].HasStorm = true;
        Assert.That(result.State.Players[0].Units[0].Health, Is.EqualTo(20));
        Assert.That(session.Snapshot().Cells[0, 0].HasStorm, Is.False);
        Assert.That(session.Snapshot().Players[0].Money, Is.EqualTo(5000));
        var restored = InitialState(); session.Restore(restored, 10); restored.Turn = 5;
        Assert.That(session.Revision, Is.EqualTo(10));
        Assert.That(session.Snapshot().Turn, Is.Zero);
    }

    [Test]
    public void AuthorityCopiesBindingAndResponseAndCachedReceipt()
    {
        var binding = new Dictionary<string, int> { { "alice", 0 }, { "bob", 1 } };
        var match = new AuthoritativeMatch("match", engine, InitialState(), binding);
        binding["alice"] = 1;
        var result = match.Submit("alice", End("id", 0, 0));
        Assert.That(result.Success, Is.True);
        result.State.Players[0].Money = 999999;
        Assert.That(match.Submit("alice", End("id", 0, 0)).State.Players[0].Money, Is.EqualTo(5000));
        match.Snapshot("alice").State.RandomState = 0;
        Assert.That(match.Snapshot("alice").State.RandomState, Is.EqualTo(12345));
    }

    [Test]
    public void SimultaneousActionsAtSameRevisionCommitOnlyOnce()
    {
        var match = Authority();
        var tasks = Enumerable.Range(0, 12).Select(i => Task.Run(() => match.Submit("alice", End("cmd" + i, 0, 0)))).ToArray();
        Task.WaitAll(tasks);
        Assert.That(tasks.Count(t => t.Result.Success), Is.EqualTo(1));
        Assert.That(match.Snapshot("alice").Revision, Is.EqualTo(1));
        Assert.That(match.Snapshot("alice").State.Turn, Is.EqualTo(1));
    }

    [Test]
    public void SimultaneousRetriesShareOneCommit()
    {
        var match = Authority();
        var tasks = Enumerable.Range(0, 12).Select(i => Task.Run(() => match.Submit("alice", End("same", 0, 0)))).ToArray();
        Task.WaitAll(tasks);
        Assert.That(tasks.All(t => t.Result.Success && t.Result.Revision == 1), Is.True);
        Assert.That(match.Snapshot("alice").Revision, Is.EqualTo(1));
    }

    [Test]
    public void ReceiptLimitRejectsNewIdsButKeepsOldRetrySafe()
    {
        var match = Authority(1);
        Assert.That(match.Submit("alice", End("first", 0, 0)).Success, Is.True);
        var full = match.Submit("bob", End("second", 1, 1));
        Assert.That(full.Success, Is.False);
        Assert.That(full.Error, Does.Contain("capacity"));
        Assert.That(match.Submit("alice", End("first", 0, 0)).Success, Is.True);
        Assert.That(match.Snapshot("alice").Revision, Is.EqualTo(1));
    }

    [Test]
    public void MalformedEnvelopesAndExtremeCoordinatesAreRejectedWithoutStateChanges()
    {
        var match = Authority();
        Assert.That(match.Submit("alice", null).Success, Is.False);
        Assert.That(match.Submit("alice", new MatchCommand("another", "id", 0, MatchAction.EndTurn(0))).Success, Is.False);
        Assert.That(match.Submit("alice", End(new string('x', 129), 0, 0)).Success, Is.False);
        Assert.That(match.Submit("alice", new MatchCommand("match", "id", 0, null)).Success, Is.False);
        var invalid = new MatchAction((MatchActionKind)999, new UnitKey(0, 1), default);
        Assert.That(match.Submit("alice", new MatchCommand("match", "invalid", 0, invalid)).Success, Is.False);
        var extreme = new MatchAction(MatchActionKind.BuildCastle, new UnitKey(0, 1), new GridPosition(1, 1), target: new GridPosition(int.MinValue, int.MaxValue));
        Assert.That(match.Submit("alice", new MatchCommand("match", "extreme", 0, extreme)).Success, Is.False);
        Assert.That(match.Snapshot("alice").Revision, Is.Zero);
    }

    [Test]
    public async Task LocalSessionSupportsAsyncContract()
    {
        IMatchSession session = new LocalMatchSession(engine, InitialState());
        Assert.That((await session.SubmitAsync(MatchAction.EndTurn(0), 0)).Success, Is.True);
        Assert.That(session.Revision, Is.EqualTo(1));
    }

    [TestCase(false)] [TestCase(true)]
    public void ArithmeticOverflowReturnsStableReceiptAndPreservesCanonicalState(bool turnOverflow)
    {
        var initial = InitialState();
        if (turnOverflow) initial.Turn = int.MaxValue;
        else initial.Players[1].Money = int.MaxValue;
        int actor = initial.ActivePlayer;
        string identity = actor == 0 ? "alice" : "bob";
        var match = new AuthoritativeMatch("match", engine, initial,
            new Dictionary<string, int> { { "alice", 0 }, { "bob", 1 } });
        var command = End("overflow", 0, actor);
        SessionActionResult result = null;
        Assert.DoesNotThrow(() => result = match.Submit(identity, command));
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("arithmetic"));
        Assert.That(result.Revision, Is.Zero);
        var snapshot = match.Snapshot(identity).State;
        Assert.That(snapshot.Turn, Is.EqualTo(initial.Turn));
        Assert.That(snapshot.Players[1].Money, Is.EqualTo(initial.Players[1].Money));
        Assert.That(snapshot.Players[0].Money, Is.EqualTo(initial.Players[0].Money));
        Assert.That(snapshot.RandomState, Is.EqualTo(initial.RandomState));
        Assert.That(snapshot.Players[0].Units[0].HasActed, Is.False);
        var retry = match.Submit(identity, End("overflow", 0, actor));
        Assert.That(retry, Is.SameAs(result));
        Assert.That(retry.Success, Is.False);
        Assert.That(match.Snapshot(identity).Revision, Is.Zero);
    }
}
