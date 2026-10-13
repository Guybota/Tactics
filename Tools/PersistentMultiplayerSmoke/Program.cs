using System.Text.Json;
using System.Text.Json.Nodes;
using Tactics.Core;
using Tactics.Multiplayer.Backend;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); assertions++; }

    private static ServerMatchContent Content()
    {
        var content = new MatchContent();
        content.AddUnit(new UnitTypeStats("leader", 100, 40, 3, 1, 1000));
        content.AddUnit(new UnitTypeStats("soldier", 100, 40, 3, 1, 500));
        content.AddRace("race", new[] { "soldier" });
        foreach (var a in content.UnitTypes) foreach (var d in content.UnitTypes) content.SetDamage(a.Id, d.Id, 25);
        var catalogue = MatchContentData.FromContent(content, new Dictionary<string, string> { { "race", "leader" } }, "test-content-v1");
        var map = new CustomMapData { id = "0123456789abcdef0123456789abcdef", name = "Private smoke", width = 10, height = 10, playerCount = 2 };
        for (int i = 0; i < 100; i++) map.cells.Add(new MapCellData());
        map.starts.Add(new MapStartData { slot = 0, x = 1, y = 1 });
        map.starts.Add(new MapStartData { slot = 1, x = 2, y = 1 });
        return new ServerMatchContent(catalogue, new[] { new ServerMapDefinition("smoke-map", "1", map) });
    }

    private static MatchCreateRequest Create() => new() { SchemaVersion = 1, MapId = "smoke-map", RaceId = "race" };
    private static MatchJoinRequest Join(string matchId) => new() { SchemaVersion = 1, MatchId = matchId, RaceId = "race" };
    private static MatchCommandData Command(string matchId, string id, int revision, MatchAction action)
        => MatchCommandData.FromCommand(new MatchCommand(matchId, id, revision, action));

    private static async Task Main()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "records", Guid.NewGuid().ToString("N"));
        var store = new FileMatchStore(directory);
        var content = Content();
        var first = new DurableMatchService(store, content);
        var second = new DurableMatchService(new FileMatchStore(directory), Content());
        Check(!(await first.CreateAsync("alice", new MatchCreateRequest { SchemaVersion = 1, MapId = "client-map", RaceId = "race" })).Success, "Unknown maps must be rejected.");
        var waiting = await first.CreateAsync("alice", Create());
        waiting.Validate();
        Check(waiting.Success && waiting.Status == MatchLobbyStatus.Waiting && waiting.PlayerSlot == 0 && waiting.Revision == 0 && waiting.Snapshot == null, "Creator waits in slot zero.");
        string id = waiting.MatchId;
        Check(!(await first.ReadAsync("mallory", id)).Success, "An outsider cannot read a waiting match.");
        Check(!(await first.GetLobbyAsync("mallory", id)).Success, "An outsider cannot read the lobby.");
        Check((await first.JoinAsync("alice", Join(id))).Status == MatchLobbyStatus.Waiting, "Creator cannot occupy both slots.");
        var joined = await Task.WhenAll(first.JoinAsync("bob", Join(id)), second.JoinAsync("bob", Join(id)));
        Check(joined.All(r => r.Success && r.PlayerSlot == 1 && r.Revision == 1), "Concurrent duplicate joins start once.");
        Check((await first.GetLobbyAsync("alice", id)).Status == MatchLobbyStatus.Active, "Creator sees started lobby.");
        Check(!(await second.JoinAsync("mallory", Join(id))).Success, "Third identity cannot join a started match.");
        var otherWaiting = await first.CreateAsync("alice", Create());
        var competingJoins = await Task.WhenAll(first.JoinAsync("bob", Join(otherWaiting.MatchId)), second.JoinAsync("mallory", Join(otherWaiting.MatchId)));
        Check(competingJoins.Count(r => r.Success) == 1, "Only one distinct opponent wins a concurrent invitation join.");
        string losingIdentity = competingJoins[0].Success ? "mallory" : "bob";
        Check(!(await first.ReadAsync(losingIdentity, otherWaiting.MatchId)).Success, "Losing invitation contender cannot read winner's state.");
        var opening = await first.ReadAsync("alice", id);
        Check(opening.Snapshot.Players[0].Money == 500 && opening.Snapshot.Players[1].Money == 0, "Opening income is applied once.");
        Console.WriteLine("PASS private creation, invitation join, identity isolation, duplicate start");

        var attack = Command(id, "attack", 1, new MatchAction(MatchActionKind.Attack, new UnitKey(0, 1), new GridPosition(1, 1), new UnitKey(1, 1)));
        var committed = await first.SubmitAsync("alice", attack);
        Check(committed.Success && committed.Revision == 2, "Combat committed.");
        var record = MatchDocumentCodec.Decode((await store.ReadAsync(id)).Json);
        uint rng = record.State.RandomState;
        var restarted = new DurableMatchService(new FileMatchStore(directory), Content());
        var replay = await restarted.SubmitAsync("alice", attack);
        Check(replay.Success && replay.Revision == 2, "Lost reply retry survives service restart.");
        Check(MatchDocumentCodec.Decode((await store.ReadAsync(id)).Json).State.RandomState == rng, "Combat retry does not advance RNG twice.");
        var mismatch = Command(id, "attack", 2, MatchAction.EndTurn(0));
        Check(!(await restarted.SubmitAsync("alice", mismatch)).Success, "Duplicate payload mismatch rejected.");
        Check(!(await restarted.SubmitAsync("bob", Command(id, "spoof", 2, MatchAction.EndTurn(0)))).Success, "Authenticated slot spoof rejected.");
        var race = await Task.WhenAll(first.SubmitAsync("alice", Command(id, "turn-a", 2, MatchAction.EndTurn(0))),
            second.SubmitAsync("alice", Command(id, "turn-b", 2, MatchAction.EndTurn(0))));
        Check(race.Count(r => r.Success) == 1, "Two service instances commit at most one same-revision action.");
        Check((await first.ReadAsync("bob", id)).Revision == 3, "CAS preserves canonical revision.");
        var oldReceipt = await restarted.SubmitAsync("alice", attack);
        Check(oldReceipt.Success && oldReceipt.Revision == 3 && oldReceipt.Snapshot.Turn == 1, "Compact old receipt returns current public state.");
        Check(!JsonSerializer.Serialize(oldReceipt, MatchDocumentCodec.JsonOptions).Contains("RandomState"), "Client JSON hides RNG.");
        record = MatchDocumentCodec.Decode((await store.ReadAsync(id)).Json);
        Check(record.Receipts.Count == 3 && record.Receipts.Count(r => r.Success) == 2, "Rejected concurrency command has durable receipt.");
        Check(typeof(MatchReceipt).GetFields().All(f => f.FieldType != typeof(MatchStateData) && f.FieldType != typeof(MatchSnapshotData)), "Receipts contain no historical boards.");
        Console.WriteLine("PASS persisted combat, restart/retry, compact receipts, two-instance CAS");

        var stored = await store.ReadAsync(id);
        var node = JsonNode.Parse(stored.Json).AsObject();
        node["ContentVersion"] = "different-content";
        await store.CompareExchangeAsync(id, stored.WriteLock, node.ToJsonString());
        Check(!(await first.ReadAsync("alice", id)).Success, "Pinned content mismatch rejects corrupted record.");
        stored = await store.ReadAsync(id);
        await store.CompareExchangeAsync(id, stored.WriteLock, MatchDocumentCodec.Encode(record));
        stored = await store.ReadAsync(id);
        node = JsonNode.Parse(stored.Json).AsObject();
        node["State"].AsObject().Remove("RandomState");
        await store.CompareExchangeAsync(id, stored.WriteLock, node.ToJsonString());
        Check(!(await restarted.ReadAsync("alice", id)).Success, "Missing private RNG rejected instead of defaulting.");
        stored = await store.ReadAsync(id);
        await store.CompareExchangeAsync(id, stored.WriteLock, MatchDocumentCodec.Encode(record));
        stored = await store.ReadAsync(id);
        node = JsonNode.Parse(stored.Json).AsObject(); node.Remove("SchemaVersion");
        await store.CompareExchangeAsync(id, stored.WriteLock, node.ToJsonString());
        Check(!(await restarted.GetLobbyAsync("alice", id)).Success, "Missing record schema rejected.");
        Check(!(await first.ReadAsync("mallory", id)).Success, "Corruption never leaks participant state.");
        stored = await store.ReadAsync(id);
        await store.CompareExchangeAsync(id, stored.WriteLock, MatchDocumentCodec.Encode(record));
        stored = await store.ReadAsync(id);
        node = JsonNode.Parse(stored.Json).AsObject(); node["RulesVersion"] = "unavailable-rules";
        await store.CompareExchangeAsync(id, stored.WriteLock, node.ToJsonString());
        Check(!(await restarted.ReadAsync("alice", id)).Success, "Rules-version mismatch cannot execute with changed code.");
        stored = await store.ReadAsync(id);
        await store.CompareExchangeAsync(id, stored.WriteLock, MatchDocumentCodec.Encode(record));
        stored = await store.ReadAsync(id);
        node = JsonNode.Parse(stored.Json).AsObject(); node["MapVersion"] = "new-map";
        await store.CompareExchangeAsync(id, stored.WriteLock, node.ToJsonString());
        Check(!(await restarted.ReadAsync("alice", id)).Success, "Map-version mismatch cannot execute with a changed map.");
        stored = await store.ReadAsync(id);
        await store.CompareExchangeAsync(id, stored.WriteLock, MatchDocumentCodec.Encode(record));
        stored = await store.ReadAsync(id);
        await store.CompareExchangeAsync(id, stored.WriteLock, stored.Json.Insert(1, "\"SchemaVersion\":1,"));
        Check(!(await restarted.ReadAsync("alice", id)).Success, "Duplicate stored JSON fields are rejected.");
        Console.WriteLine("PASS pinned metadata, schema/RNG field presence, corruption rejection");

        var bounded = new DurableMatchService(new FileMatchStore(directory), Content(), maxReceipts: 1);
        var boundedWaiting = await bounded.CreateAsync("alice", Create());
        await bounded.JoinAsync("bob", Join(boundedWaiting.MatchId));
        var firstCommand = Command(boundedWaiting.MatchId, "first", 1, MatchAction.EndTurn(0));
        Check((await bounded.SubmitAsync("alice", firstCommand)).Success, "Receipt budget accepts first action.");
        Check(!(await bounded.SubmitAsync("bob", Command(boundedWaiting.MatchId, "second", 2, MatchAction.EndTurn(1)))).Success, "Receipt budget refuses new commands rather than evicting history.");
        Check((await bounded.SubmitAsync("alice", firstCommand)).Success, "Old receipt remains retryable at capacity.");
        Check((await bounded.ReadAsync("alice", boundedWaiting.MatchId)).Revision == 2, "Capacity rejection never advances state.");
        Console.WriteLine("PASS bounded receipt history without eviction");
        Console.WriteLine($"Persistent multiplayer smoke passed: {assertions} assertions. Records: {directory}");
    }
}
