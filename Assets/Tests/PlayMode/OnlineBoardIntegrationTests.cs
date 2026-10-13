using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class OnlineBoardIntegrationTests
{
    private sealed class Transport : IMatchTransport
    {
        private readonly AuthoritativeMatch authority;
        public readonly List<MatchCommand> Commands = new List<MatchCommand>();
        public Func<MatchCommand, Task<MatchTransportReply>> SubmitOverride;
        public bool LoseNextReply;
        public Transport(AuthoritativeMatch authority) { this.authority = authority; }
        public Task<MatchTransportReply> ReadAsync(string matchId) => Task.FromResult(Wire(authority.Snapshot("alice")));
        public Task<MatchTransportReply> SubmitAsync(MatchCommand command)
        {
            Commands.Add(command);
            if (SubmitOverride != null) return SubmitOverride(command);
            var reply = Commit(command);
            if (LoseNextReply) { LoseNextReply = false; return Task.FromException<MatchTransportReply>(new TimeoutException()); }
            return Task.FromResult(reply);
        }
        public MatchTransportReply Commit(MatchCommand command)
        {
            // Exercise the real wire envelope rather than passing properties through JSON.
            var decoded = JsonUtility.FromJson<MatchCommandData>(JsonUtility.ToJson(MatchCommandData.FromCommand(command))).ToCommand();
            return Wire(authority.Submit("alice", decoded));
        }
        private static MatchTransportReply Wire(SessionActionResult result)
            => new MatchTransportReply { MatchId = "online-test", Success = result.Success, Error = result.Error,
                Revision = result.Revision, Snapshot = result.State == null ? null : MatchSnapshotData.FromState(result.State) };
    }

    private Component controller;
    private MatchState confirmed;
    private OnlineMatchSession session;
    private AuthoritativeMatch authority;
    private Transport transport;
    private bool originalBackground;
    private static Type Script(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Get(object target, string name) => target.GetType().GetField(name).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name).SetValue(target, value);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name).Invoke(target, args);
    private static object Property(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
    private MatchState State() => (MatchState)Call(controller, "GetStateSnapshot");
    private IList Teams => (IList)Get(controller, "teams");
    private object Map => Get(controller, "gameMap");
    private object Leader(int slot) => Call(Teams[slot], "Get_king");
    private object Tile(int x, int y) => ((Array)Get(Map, "tiles")).GetValue(x, y);
    private Task<bool> Execute(MatchAction action) => (Task<bool>)Call(controller, "ExecuteActionAsync", action);
    private Task<bool> Refresh() => (Task<bool>)Call(controller, "RefreshOnlineAsync");
    private static IEnumerator Finish(Task task)
    {
        for (int frame = 0; frame < 120 && !task.IsCompleted; frame++) yield return null;
        Assert.That(task.IsCompleted, Is.True, "The fake transport should complete without waiting for a real service.");
        task.GetAwaiter().GetResult();
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        originalBackground = Application.runInBackground; Application.runInBackground = true;
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        Call(UnityEngine.Object.FindFirstObjectByType(Script("MainMenu")), "PressStart");
        yield return null; yield return null;
        controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        var local = State();
        var adapter = controller.GetType().GetProperty("MatchAdapter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        var engine = (MatchEngine)Property(adapter, "Engine");
        confirmed = new MatchState(12, 10) { BuildMode = true, RandomState = 12345 };
        for (int slot = 0; slot < 2; slot++)
        {
            var player = local.Players[slot].Clone();
            var leader = player.Units.Single(u => u.IsLeader); player.Units.Clear(); player.Units.Add(leader);
            leader.Position = slot == 0 ? new GridPosition(2, 2) : new GridPosition(9, 7);
            leader.HasActed = false; player.Money = slot == 0 ? 7000 : 6000;
            confirmed.Players.Add(player);
        }
        confirmed.Cells[5, 5].Terrain = TerrainKind.Forest; confirmed.Cells[5, 5].MovementCost = 2;
        confirmed.Cells[5, 5].SupportsCastle = false;
        confirmed.Cells[3, 3].HasCastle = true; confirmed.Cells[3, 3].CastleOwner = 0;
        authority = new AuthoritativeMatch("online-test", engine, confirmed,
            new Dictionary<string, int> { { "alice", 0 }, { "bob", 1 } }, revision: 7);
        transport = new Transport(authority);
        session = new OnlineMatchSession(transport, "online-test", MatchSnapshotData.FromState(confirmed), revision: 7);
        Call(controller, "InitializeOnlineMatch", session, 0, null);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        Application.runInBackground = originalBackground;
    }

    [UnityTest]
    public IEnumerator OnlineAttackStartsPresentationOnlyAfterServerConfirmation()
    {
        confirmed.Players[1].Units[0].Position = new GridPosition(4, 2);
        var adapter = controller.GetType().GetProperty("MatchAdapter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        authority = new AuthoritativeMatch("online-test", (MatchEngine)Property(adapter, "Engine"), confirmed,
            new Dictionary<string, int> { { "alice", 0 }, { "bob", 1 } }, revision: 7);
        transport = new Transport(authority);
        session = new OnlineMatchSession(transport, "online-test", MatchSnapshotData.FromState(confirmed), revision: 7);
        Call(controller, "InitializeOnlineMatch", session, 0, null);
        yield return null;
        var completion = new TaskCompletionSource<MatchTransportReply>();
        transport.SubmitOverride = _ => completion.Task;
        var action = new MatchAction(MatchActionKind.Attack, new UnitKey(0, confirmed.Players[0].Units[0].Id),
            new GridPosition(3, 2), new UnitKey(1, confirmed.Players[1].Units[0].Id));
        Assert.That(Call(controller, "IsActionLegal", action), Is.True);
        var task = Execute(action);
        Assert.That(Property(controller, "IsCombatAnimating"), Is.False);
        Assert.That(Get(Leader(0), "current_tile"), Is.SameAs(Tile(2, 2)));
        completion.SetResult(transport.Commit(transport.Commands.Single()));
        yield return Finish(task);
        Assert.That(task.Result, Is.True);
        Assert.That(Property(controller, "IsCombatAnimating"), Is.True);
        Assert.That(State().FindUnit(action.Actor).Position, Is.EqualTo(new GridPosition(3, 2)));
        Assert.That(State().FindUnit(action.TargetUnit).Health, Is.LessThan(confirmed.Players[1].Units[0].Health));
        Assert.That(Property(controller, "MatchRevision"), Is.EqualTo(8));
    }

    [UnityTest]
    public IEnumerator OnlineBindingRebuildsConfirmedBoardWithoutOpeningIncomeOrSceneRecapture()
    {
        Assert.That(Property(controller, "IsOnlineMatch"), Is.True);
        Assert.That(Property(controller, "MatchRevision"), Is.EqualTo(7));
        Assert.That(Get(Map, "max_x"), Is.EqualTo(12)); Assert.That(Get(Map, "max_y"), Is.EqualTo(10));
        Assert.That(Get(Tile(5, 5), "forest"), Is.Not.Null);
        Assert.That(Get(Tile(3, 3), "has_castle"), Is.True);
        Assert.That(Get(Teams[0], "money"), Is.EqualTo(7000));
        Assert.That(Get(Leader(0), "current_tile"), Is.SameAs(Tile(2, 2)));
        Set(Teams[0], "money", 1); Set(Leader(0), "hp", 1f);
        Assert.That(State().Players[0].Money, Is.EqualTo(7000));
        Assert.That(State().Players[0].Units[0].Health, Is.EqualTo(confirmed.Players[0].Units[0].Health));
        Call(controller, "Initialize_turn"); Call(controller, "ResolveOutcome");
        Assert.That(Property(controller, "MatchRevision"), Is.EqualTo(7));
        Assert.That(State().Players[0].Money, Is.EqualTo(7000));
        yield return null;
    }

    [UnityTest]
    public IEnumerator DelayedActionChangesBoardOnlyAfterConfirmedReply()
    {
        var completion = new TaskCompletionSource<MatchTransportReply>();
        transport.SubmitOverride = _ => completion.Task;
        var leader = confirmed.Players[0].Units[0];
        var action = new MatchAction(MatchActionKind.Wait, new UnitKey(0, leader.Id), new GridPosition(3, 2));
        var task = Execute(action);
        Assert.That(session.HasPendingCommand, Is.True); Assert.That(Property(controller, "CanControlOnlineTurn"), Is.False);
        Assert.That(Get(Leader(0), "current_tile"), Is.SameAs(Tile(2, 2)));
        Assert.That(State().FindUnit(action.Actor).Position, Is.EqualTo(new GridPosition(2, 2)));
        completion.SetResult(transport.Commit(transport.Commands.Single()));
        yield return Finish(task);
        Assert.That(task.Result, Is.True);
        Assert.That(Get(Leader(0), "current_tile"), Is.SameAs(Tile(3, 2)));
        Assert.That(Get(Leader(0), "has_moved"), Is.True);
        Assert.That(Property(controller, "MatchRevision"), Is.EqualTo(8));
        Assert.That(session.HasPendingCommand, Is.False);
    }

    [UnityTest]
    public IEnumerator ExistingMapWaitCallbackDispatchesOnlineAndWaitsForConfirmation()
    {
        var completion = new TaskCompletionSource<MatchTransportReply>();
        transport.SubmitOverride = _ => completion.Task;
        Call(Map, "Select_tile", Tile(2, 2), Teams[0]);
        var path = (IList)Get(Map, "path"); path.Clear(); path.Add(Tile(2, 2)); path.Add(Tile(3, 2));
        Call(Map, "Finalize_move");
        Assert.That(transport.Commands.Count, Is.EqualTo(1));
        Assert.That(transport.Commands[0].Action.Kind, Is.EqualTo(MatchActionKind.Wait));
        Assert.That(session.HasPendingCommand, Is.True);
        Assert.That(Get(Leader(0), "current_tile"), Is.SameAs(Tile(2, 2)));
        completion.SetResult(transport.Commit(transport.Commands[0]));
        for (int frame = 0; frame < 120 && (int)Property(controller, "MatchRevision") != 8; frame++) yield return null;
        Assert.That(Property(controller, "MatchRevision"), Is.EqualTo(8));
        Assert.That(Get(Leader(0), "current_tile"), Is.SameAs(Tile(3, 2)));
        Assert.That(session.HasPendingCommand, Is.False);
    }

    [UnityTest]
    public IEnumerator OwnershipAndOpponentTurnAreRejectedBeforeAnyTransportRequest()
    {
        var enemy = confirmed.Players[1].Units[0];
        var wrongOwner = Execute(new MatchAction(MatchActionKind.Wait, new UnitKey(1, enemy.Id), enemy.Position));
        yield return Finish(wrongOwner);
        Assert.That(wrongOwner.Result, Is.False); Assert.That(transport.Commands, Is.Empty);
        var end = Execute(MatchAction.EndTurn(0)); yield return Finish(end);
        Assert.That(end.Result, Is.True); Assert.That(Property(controller, "CanControlOnlineTurn"), Is.False);
        var opponent = Execute(MatchAction.EndTurn(1)); yield return Finish(opponent);
        Assert.That(opponent.Result, Is.False); Assert.That(transport.Commands.Count, Is.EqualTo(1));
        Assert.That(State().Turn, Is.EqualTo(1)); Assert.That(State().Players[0].Money, Is.EqualTo(7000));
    }

    [UnityTest]
    public IEnumerator DefiniteServerRejectionClearsPendingWithoutChangingUnitsOrMoney()
    {
        transport.SubmitOverride = _ => Task.FromResult(new MatchTransportReply { MatchId = "online-test", Success = false,
            Error = "Server rejected the action.", Revision = 7, Snapshot = MatchSnapshotData.FromState(confirmed) });
        var leader = confirmed.Players[0].Units[0];
        var task = Execute(new MatchAction(MatchActionKind.Wait, new UnitKey(0, leader.Id), new GridPosition(3, 2)));
        yield return Finish(task);
        Assert.That(task.Result, Is.False); Assert.That(session.HasPendingCommand, Is.False);
        Assert.That(Get(Leader(0), "current_tile"), Is.SameAs(Tile(2, 2)));
        Assert.That(Get(Leader(0), "has_moved"), Is.False); Assert.That(Get(Teams[0], "money"), Is.EqualTo(7000));
        Assert.That(Property(controller, "MatchRevision"), Is.EqualTo(7));
        Assert.That(Property(controller, "CanControlOnlineTurn"), Is.True);
    }

    [UnityTest]
    public IEnumerator LostReplyKeepsBoardPendingThenRefreshAndExactRetryRecoverOnce()
    {
        transport.LoseNextReply = true;
        var leader = confirmed.Players[0].Units[0];
        var action = new MatchAction(MatchActionKind.Wait, new UnitKey(0, leader.Id), new GridPosition(3, 2));
        var lost = Execute(action); yield return Finish(lost);
        Assert.That(lost.Result, Is.False); Assert.That(session.HasPendingCommand, Is.True);
        Assert.That(Get(Leader(0), "current_tile"), Is.SameAs(Tile(2, 2)));
        var duplicate = Execute(action); yield return Finish(duplicate);
        Assert.That(duplicate.Result, Is.False); Assert.That(transport.Commands.Count, Is.EqualTo(1));
        var refresh = Refresh(); yield return Finish(refresh);
        Assert.That(refresh.Result, Is.True); Assert.That(session.HasPendingCommand, Is.True);
        Assert.That(Get(Leader(0), "current_tile"), Is.SameAs(Tile(3, 2)));
        var retry = (Task<bool>)Call(controller, "RetryOnlineAsync"); yield return Finish(retry);
        Assert.That(retry.Result, Is.True); Assert.That(session.HasPendingCommand, Is.False);
        Assert.That(transport.Commands[1].CommandId, Is.EqualTo(transport.Commands[0].CommandId));
        Assert.That(Property(controller, "MatchRevision"), Is.EqualTo(8));
        Assert.That(authority.Snapshot("alice").Revision, Is.EqualTo(8));
    }

    [UnityTest]
    public IEnumerator LocalLoadCannotReplaceOnlineSessionOrItsBoard()
    {
        var save = Activator.CreateInstance(Script("GameSave"), new object[] { controller, "OnlineMustRejectLocalRestore" });
        var originalTiles = Get(Map, "tiles");
        object[] args = { save, null };
        Assert.That((bool)Call(controller, "TryLoadSnapshot", args), Is.False);
        Assert.That(args[1], Is.Not.Null.And.Not.Empty);
        Assert.That(Get(Map, "tiles"), Is.SameAs(originalTiles));
        Assert.That(Property(controller, "IsOnlineMatch"), Is.True); Assert.That(Property(controller, "MatchRevision"), Is.EqualTo(7));
        yield return null;
    }
}
