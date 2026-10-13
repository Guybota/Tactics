using System;
using System.Linq;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;

public class MultiplayerCommandDataTests
{
    private static MatchAction Action(MatchActionKind kind)
    {
        if (kind == MatchActionKind.EndTurn) return MatchAction.EndTurn(2);
        if (kind == MatchActionKind.Recruit) return MatchAction.Recruit(2, new GridPosition(7, 9), "race.soldier");
        if (kind == MatchActionKind.Attack)
            return new MatchAction(kind, new UnitKey(2, 17), new GridPosition(7, 9), new UnitKey(3, 22));
        return new MatchAction(kind, new UnitKey(2, 17), new GridPosition(7, 9), target: new GridPosition(8, 9));
    }
    private static MatchCommandData Data(MatchActionKind kind = MatchActionKind.Wait)
        => MatchCommandData.FromCommand(new MatchCommand("match-123", "request-456", 37, Action(kind)));

    [TestCase(MatchActionKind.Wait)] [TestCase(MatchActionKind.Attack)] [TestCase(MatchActionKind.Capture)]
    [TestCase(MatchActionKind.BuildCastle)] [TestCase(MatchActionKind.Recruit)] [TestCase(MatchActionKind.EndTurn)]
    public void CommandAndEveryActionKindRoundTripThroughActualJsonWire(MatchActionKind kind)
    {
        var original = new MatchCommand("match-123", "request-456", 37, Action(kind));
        string json = JsonUtility.ToJson(MatchCommandData.FromCommand(original));
        Assert.That(json, Does.Contain("MatchId")); Assert.That(json, Does.Contain("DestinationX"));
        var restored = JsonUtility.FromJson<MatchCommandData>(json).ToCommand();
        Assert.That(restored.MatchId, Is.EqualTo(original.MatchId));
        Assert.That(restored.CommandId, Is.EqualTo(original.CommandId));
        Assert.That(restored.ExpectedRevision, Is.EqualTo(original.ExpectedRevision));
        Assert.That(restored.Action.Kind, Is.EqualTo(original.Action.Kind));
        Assert.That(restored.Action.Actor, Is.EqualTo(original.Action.Actor));
        Assert.That(restored.Action.Destination, Is.EqualTo(original.Action.Destination));
        Assert.That(restored.Action.TargetUnit, Is.EqualTo(original.Action.TargetUnit));
        Assert.That(restored.Action.Target, Is.EqualTo(original.Action.Target));
        Assert.That(restored.Action.RecruitTypeId, Is.EqualTo(original.Action.RecruitTypeId));
    }

    [Test]
    public void PayloadDoesNotCarryAnAuthenticatedIdentityOrRandomStream()
    {
        Assert.That(typeof(MatchCommandData).GetFields().Select(f => f.Name), Does.Not.Contain("PlayerId"));
        Assert.That(typeof(MatchCommandData).GetFields().Select(f => f.Name), Does.Not.Contain("AuthenticatedPlayerId"));
        Assert.That(JsonUtility.ToJson(Data()), Does.Not.Contain("RandomState"));
    }

    [TestCase(0)] [TestCase(2)] [TestCase(int.MaxValue)]
    public void RejectsUnsupportedEnvelopeAndActionVersions(int version)
    {
        var data = Data(); data.SchemaVersion = version;
        Assert.Throws<ArgumentException>(() => data.ToCommand());
        data = Data(); data.Action.SchemaVersion = version;
        Assert.Throws<ArgumentException>(() => data.ToCommand());
    }

    [Test]
    public void RejectsMalformedEnvelopeAndMissingAction()
    {
        var data = Data(); data.MatchId = " "; Assert.Throws<ArgumentException>(() => data.ToCommand());
        data = Data(); data.CommandId = new string('x', 129); Assert.Throws<ArgumentException>(() => data.ToCommand());
        data = Data(); data.CommandId = "request\n456"; Assert.Throws<ArgumentException>(() => data.ToCommand());
        data = Data(); data.ExpectedRevision = -1; Assert.Throws<ArgumentException>(() => data.ToCommand());
        data = Data(); data.Action = null; Assert.Throws<ArgumentException>(() => data.ToCommand());
        Assert.Throws<ArgumentException>(() => JsonUtility.FromJson<MatchCommandData>("{}").ToCommand());
    }

    [TestCase(-1)] [TestCase(6)] [TestCase(int.MaxValue)]
    public void RejectsUnknownActionEnum(int value)
    {
        var data = Data(); data.Action.Kind = (MatchActionKind)value;
        Assert.Throws<ArgumentException>(() => data.ToCommand());
    }

    [TestCase(-1)] [TestCase(50)] [TestCase(int.MaxValue)]
    public void RejectsInvalidCoordinatesBeforeEngineDistanceArithmetic(int value)
    {
        var data = Data(); data.Action.DestinationX = value;
        Assert.Throws<ArgumentException>(() => data.ToCommand());
        data = Data(); data.Action.TargetY = value;
        Assert.Throws<ArgumentException>(() => data.ToCommand());
    }

    [Test]
    public void RejectsInvalidSlotsAndUnitIdsAndMissingRecruitType()
    {
        var data = Data(); data.Action.ActorPlayer = 4; Assert.Throws<ArgumentException>(() => data.ToCommand());
        data = Data(); data.Action.ActorId = 0; Assert.Throws<ArgumentException>(() => data.ToCommand());
        data = Data(); data.Action.ActorId = int.MaxValue; Assert.Throws<ArgumentException>(() => data.ToCommand());
        data = Data(MatchActionKind.Attack); data.Action.TargetPlayer = -1; Assert.Throws<ArgumentException>(() => data.ToCommand());
        data = Data(MatchActionKind.Attack); data.Action.TargetUnitId = 0; Assert.Throws<ArgumentException>(() => data.ToCommand());
        data = Data(MatchActionKind.EndTurn); data.Action.ActorId = 1; Assert.Throws<ArgumentException>(() => data.ToCommand());
        data = Data(MatchActionKind.Recruit); data.Action.RecruitTypeId = null; Assert.Throws<ArgumentException>(() => data.ToCommand());
    }

    [Test]
    public void MissingEnvelopeAndActionVersionInActualJsonIsRejected()
    {
        string json = JsonUtility.ToJson(Data()).Replace("\"SchemaVersion\":1,", "");
        var data = JsonUtility.FromJson<MatchCommandData>(json);
        Assert.That(data.SchemaVersion, Is.Zero); Assert.That(data.Action.SchemaVersion, Is.Zero);
        Assert.Throws<ArgumentException>(() => data.ToCommand());
        data.SchemaVersion = MatchCommandData.CurrentVersion;
        Assert.Throws<ArgumentException>(() => data.ToCommand());
        string actionJson = JsonUtility.ToJson(Data().Action).Replace("\"SchemaVersion\":1,", "");
        var action = JsonUtility.FromJson<MatchActionData>(actionJson);
        Assert.That(action.SchemaVersion, Is.Zero);
        Assert.Throws<ArgumentException>(() => action.ToAction());
    }
}
