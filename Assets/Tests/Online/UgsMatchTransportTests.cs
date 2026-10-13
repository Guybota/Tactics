using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Tactics.Core;

public class UgsMatchTransportTests
{
    private sealed class FakeClient : ICloudModuleClient
    {
        public string Module, Endpoint;
        public Dictionary<string, object> Arguments;
        public int Calls;
        public object Reply;
        public Task<T> CallAsync<T>(string module, string endpoint, Dictionary<string, object> arguments)
        {
            Calls++; Module = module; Endpoint = endpoint; Arguments = arguments;
            return Task.FromResult((T)Reply);
        }
    }

    private static MatchLobbyReply Waiting(string matchId = "match") => new MatchLobbyReply
    {
        SchemaVersion = MatchLobbyReply.CurrentVersion, Success = true, MatchId = matchId,
        MapId = "default-1v1", ContentVersion = "content-1", PlayerSlot = 0,
        Revision = 0, Status = MatchLobbyStatus.Waiting
    };

    [Test]
    public async Task LifecycleCallsUseExactModuleParametersWithoutCallerIdentity()
    {
        var client = new FakeClient { Reply = Waiting() };
        var transport = new UgsMatchTransport(client);
        Assert.That(client.Calls, Is.Zero);
        var create = new MatchCreateRequest { SchemaVersion = 1, MapId = "default-1v1", RaceId = "race.human" };
        Assert.That((await transport.CreateAsync(create)).Success, Is.True);
        Assert.That(client.Module, Is.EqualTo("TacticsMultiplayer"));
        Assert.That(client.Endpoint, Is.EqualTo("CreateMatch"));
        Assert.That(client.Arguments.Keys, Is.EquivalentTo(new[] { "request" }));
        Assert.That(client.Arguments["request"], Is.SameAs(create));
        var join = new MatchJoinRequest { SchemaVersion = 1, MatchId = "match", RaceId = "race.elf" };
        await transport.JoinAsync(join);
        Assert.That(client.Endpoint, Is.EqualTo("JoinMatch"));
        await transport.GetLobbyAsync("match");
        Assert.That(client.Endpoint, Is.EqualTo("GetLobby"));
        Assert.That(client.Arguments.Keys, Is.EquivalentTo(new[] { "matchId" }));
    }

    [Test]
    public async Task SubmittedActionUsesVersionedWireEnvelopeAndReadUsesMatchIdOnly()
    {
        var client = new FakeClient { Reply = new MatchTransportReply { MatchId = "match", Error = "Waiting" } };
        var transport = new UgsMatchTransport(client, "TestModule");
        var command = new MatchCommand("match", "command", 0, MatchAction.EndTurn(0));
        await transport.SubmitAsync(command);
        Assert.That(client.Module, Is.EqualTo("TestModule"));
        Assert.That(client.Endpoint, Is.EqualTo("SubmitAction"));
        Assert.That(client.Arguments.Keys, Is.EquivalentTo(new[] { "command" }));
        var wire = (MatchCommandData)client.Arguments["command"];
        Assert.That(wire.ToCommand().CommandId, Is.EqualTo("command"));
        Assert.That(wire.Action.Kind, Is.EqualTo(MatchActionKind.EndTurn));
        await transport.ReadAsync("match");
        Assert.That(client.Endpoint, Is.EqualTo("GetMatch"));
        Assert.That(client.Arguments.Keys, Is.EquivalentTo(new[] { "matchId" }));
    }

    [Test]
    public void MalformedRequestsNeverReachCloudClient()
    {
        var client = new FakeClient { Reply = Waiting() };
        var transport = new UgsMatchTransport(client);
        Assert.ThrowsAsync<ArgumentException>(async () => await transport.CreateAsync(new MatchCreateRequest()));
        Assert.ThrowsAsync<ArgumentException>(async () => await transport.JoinAsync(new MatchJoinRequest()));
        Assert.Throws<ArgumentException>(() => transport.ReadAsync("bad\nmatch"));
        Assert.That(client.Calls, Is.Zero);
    }

    [Test]
    public void WrongLobbyOrMissingSchemaCannotBeUsedAsAConnection()
    {
        var client = new FakeClient { Reply = Waiting("another") };
        var transport = new UgsMatchTransport(client);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await transport.GetLobbyAsync("match"));
        client.Reply = new MatchLobbyReply();
        Assert.ThrowsAsync<ArgumentException>(async () => await transport.GetLobbyAsync("match"));
    }
}
