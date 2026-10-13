using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Tactics.Core;

// Exercises the production core through two client sessions and a fake authenticated
// transport. This is an in-process proof, not a network or deployed cloud test.
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private static int assertions;

    private static async Task<int> Main(string[] args)
    {
        try
        {
            Console.WriteLine("Multiplayer foundation smoke: .NET 8, in-process authenticated transport, no Unity or cloud.");
            CheckExportedCatalogue(args);
            var catalogue = PortableCatalogue();
            var engine = new MatchEngine(catalogue.ToContent());
            var initial = MatchInitializer.Create(PortableMap(), catalogue,
                new[] { new MatchPlayerSetup { RaceId = "smoke.race", LeaderTypeId = "smoke.leader" },
                    new MatchPlayerSetup { RaceId = "smoke.race", LeaderTypeId = "smoke.leader" } }, 0x12345678);
            Check(initial.Players[0].Money == 1000 && initial.Players[1].Money == 0,
                "Initializer awards opening base and castle income only to the first player.");
            Check(initial.Players.All(p => p.Units[0].MaxHealth == LeaderStats.Health(100)),
                "Initializer applies leader health bonuses exactly once.");
            Check(initial.RandomState == 0x12345678, "Initializer uses the specified private RNG seed.");
            Pass("Portable catalogue JSON and headless map initialization");

            LocalSessionIsolation(engine, initial);
            var authority = CreateAuthority("smoke.match", engine, initial);
            var aliceTransport = new FakeTransport(authority, "alice");
            var bobTransport = new FakeTransport(authority, "bob");
            var alice = new OnlineMatchSession(aliceTransport, authority.MatchId, Public(initial));
            var bob = new OnlineMatchSession(bobTransport, authority.MatchId, Public(initial));
            var attack = new MatchAction(MatchActionKind.Attack, new UnitKey(0, 1),
                new GridPosition(3, 2), new UnitKey(1, 1));

            aliceTransport.LoseNextReplyAfterCommit = true;
            var uncertain = await alice.SubmitAsync(attack, 0);
            Check(!uncertain.Success && alice.HasPendingCommand && alice.Revision == 0,
                "A lost reply retains the uncertain command and old confirmed client revision.");
            var committed = authority.Snapshot("alice");
            Check(committed.Success && committed.Revision == 1 && committed.State.RandomState != initial.RandomState,
                "Server committed combat once and advanced its private random stream.");
            string committedHash = SimulationSnapshot.Fingerprint(committed.State);
            Check(!(await alice.SubmitAsync(MatchAction.EndTurn(0), 0)).Success,
                "Client blocks a new command while an earlier action is uncertain.");
            var retried = await alice.RetryPendingAsync();
            Check(retried.Success && !alice.HasPendingCommand && alice.Revision == 1,
                "Retry receives the original committed receipt and clears the pending command.");
            Check(aliceTransport.Submitted.Count == 2
                && aliceTransport.Submitted[0].CommandId == aliceTransport.Submitted[1].CommandId,
                "Retry reuses the original command identifier.");
            Check(authority.Snapshot("alice").Revision == 1
                && SimulationSnapshot.Fingerprint(authority.Snapshot("alice").State) == committedHash,
                "Retry never reapplies combat, damage, movement or luck.");
            Check((await bob.RefreshAsync()).Success && bob.Revision == 1,
                "The other client observes the committed action after refresh.");
            CheckPublicEqual(alice.Snapshot(), bob.Snapshot(), "Both clients converge on the confirmed public state.");
            Pass("Two clients: committed combat, lost reply, idempotent retry and refresh");

            RejectedRequests(authority, aliceTransport.Submitted[0]);
            Check((await alice.SubmitAsync(MatchAction.EndTurn(0), 1)).Success, "Alice can end her confirmed turn.");
            Check((await bob.RefreshAsync()).Success && bob.Snapshot().ActivePlayer == 1,
                "Bob refreshes into his authenticated turn.");
            var bobAttack = new MatchAction(MatchActionKind.Attack, new UnitKey(1, 1),
                new GridPosition(4, 2), new UnitKey(0, 1));
            Check((await bob.SubmitAsync(bobAttack, bob.Revision)).Success, "Bob can attack during his turn.");
            Check((await alice.RefreshAsync()).Success && alice.Revision == 3, "Alice receives Bob's confirmed combat.");
            CheckPublicEqual(alice.Snapshot(), bob.Snapshot(), "Both clients converge after both authenticated players act.");
            aliceTransport.NextReadReply = Reply(authority.MatchId, committed);
            Check((await alice.RefreshAsync()).Success && alice.Revision == 3,
                "A late older snapshot cannot roll the client back.");
            CheckPublicEqual(alice.Snapshot(), bob.Snapshot(), "Late snapshot leaves the current client board intact.");
            Pass("Identity, stale requests, payload reuse, turn exchange and late snapshot protection");

            await ConcurrentRequests(engine, initial);
            PrivateStateRestore(authority, engine);
            Console.WriteLine($"PASS: {assertions} assertions. No network, Unity Editor or deployed persistence was exercised.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: " + exception);
            return 1;
        }
    }

    private static MatchContentData PortableCatalogue()
    {
        var content = new MatchContent();
        content.AddUnit(new UnitTypeStats("smoke.leader", 100, 20, 3, 1, 0));
        content.AddUnit(new UnitTypeStats("smoke.soldier", 100, 20, 3, 1, 100));
        content.AddRace("smoke.race", new[] { "smoke.soldier" });
        foreach (var attacker in content.UnitTypes)
            foreach (var defender in content.UnitTypes) content.SetDamage(attacker.Id, defender.Id, 25);
        var exported = MatchContentData.FromContent(content,
            new Dictionary<string, string> { ["smoke.race"] = "smoke.leader" }, "smoke.v1");
        var imported = RoundTrip(exported);
        Check(imported.Version == exported.Version && ContentHash(imported) == ContentHash(exported),
            "Catalogue JSON roundtrip preserves its version and canonical SHA-256 digest.");
        var reconstructed = MatchContentData.FromContent(imported.ToContent(),
            imported.Races.ToDictionary(race => race.Id, race => race.LeaderTypeId), imported.Version);
        Check(ContentHash(reconstructed) == ContentHash(exported),
            "Catalogue ToContent/FromContent roundtrip preserves all baseline data.");
        Check(imported.Version == "smoke.v1" && imported.ToContent().CanRecruit("smoke.race", "smoke.soldier"),
            "Catalogue survives field-based JSON export/import with its version and recruit policy.");
        Check(imported.ToContent().DamagePercent("smoke.leader", "smoke.soldier") == 25,
            "Catalogue roundtrip preserves combat matchups.");
        return imported;
    }

    private static void CheckExportedCatalogue(string[] args)
    {
        string path = args.Length > 0 ? Path.GetFullPath(args[0])
            : Path.Combine(Environment.CurrentDirectory, "Server", "Content", "match-content.json");
        if (args.Length == 0 && !File.Exists(path))
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, "Server", "Content", "match-content.json");
                if (File.Exists(candidate)) { path = candidate; break; }
            }
        }
        if (!File.Exists(path))
        {
            if (args.Length > 0) throw new FileNotFoundException("Explicit catalogue path does not exist.", path);
            Console.WriteLine("SKIP: No real exported catalogue found; synthetic portable catalogue still runs.");
            return;
        }
        var exported = JsonSerializer.Deserialize<MatchContentData>(File.ReadAllText(path), Json);
        var imported = RoundTrip(exported);
        var content = imported.ToContent();
        Check(exported.Version == imported.Version && ContentHash(exported) == ContentHash(imported),
            "Real exported catalogue JSON roundtrip preserves the published version and canonical data hash.");
        var reconstructed = MatchContentData.FromContent(content,
            imported.Races.ToDictionary(race => race.Id, race => race.LeaderTypeId), imported.Version);
        Check(ContentHash(reconstructed) == ContentHash(imported),
            "Real catalogue rebuild from server rules preserves every baseline value and version.");
        Check(imported.Damage.Count == imported.Units.Count * imported.Units.Count,
            "Real exported catalogue contains a complete damage matrix.");
        foreach (var damage in imported.Damage)
            Check(content.DamagePercent(damage.AttackerTypeId, damage.DefenderTypeId) == damage.Percent,
                "Real exported damage percentage survives server deserialization.");
        foreach (var race in imported.Races)
        {
            Check(content.Unit(race.LeaderTypeId) != null, "Real exported race leader resolves on the server.");
            foreach (var recruit in race.RecruitTypeIds)
                Check(content.CanRecruit(race.Id, recruit), "Real exported recruit policy survives server deserialization.");
        }
        Console.WriteLine($"PASS: Real exported catalogue ({imported.Races.Count} races, {imported.Units.Count} units, "
            + $"{imported.Damage.Count} damage rows): {path}; version {imported.Version}.");
        // The published version hashes Unity's JSON format. This proof verifies its
        // preservation; ContentHash compares the normalized .NET representation.
    }

    private static string ContentHash(MatchContentData catalogue)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(catalogue, Json))));

    private static CustomMapData PortableMap()
    {
        var map = new CustomMapData { id = "11111111111111111111111111111111", name = "Multiplayer smoke",
            width = 10, height = 10, playerCount = 2 };
        for (int i = 0; i < 100; i++) map.cells.Add(new MapCellData { terrain = TerrainKind.Grass });
        map.starts.Add(new MapStartData { slot = 0, x = 2, y = 2 });
        map.starts.Add(new MapStartData { slot = 1, x = 4, y = 2 });
        map.Cell(6, 6).castle = true;
        map.Cell(6, 6).castleOwner = 0;
        return RoundTrip(map);
    }

    private static AuthoritativeMatch CreateAuthority(string id, MatchEngine engine, MatchState state, int revision = 0)
        => new(id, engine, state, new Dictionary<string, int> { ["alice"] = 0, ["bob"] = 1 }, revision);

    private static void LocalSessionIsolation(MatchEngine engine, MatchState original)
    {
        var input = original.Clone();
        var session = new LocalMatchSession(engine, input);
        input.Players[0].Money = 0;
        var exposed = session.Snapshot();
        exposed.Players.Clear();
        Check(session.Snapshot().Players.Count == 2 && session.Snapshot().Players[0].Money == 1000,
            "Session owns independent input and output snapshots.");
        string before = SimulationSnapshot.Fingerprint(session.Snapshot());
        Check(!session.Submit(MatchAction.EndTurn(1), 0).Success && session.Revision == 0
            && SimulationSnapshot.Fingerprint(session.Snapshot()) == before,
            "Rejected local action preserves revision, state and RNG.");
        var accepted = session.Submit(MatchAction.EndTurn(0), 0);
        Check(accepted.Success && session.Revision == 1, "Accepted local action advances exactly one revision.");
        accepted.State.Players[1].Money = 0;
        Check(session.Snapshot().Players[1].Money == 500, "Mutating an action result cannot change session economy.");
        Pass("State ownership and local execution regression checks");
    }

    private static void RejectedRequests(AuthoritativeMatch authority, MatchCommand original)
    {
        var stranger = authority.Snapshot("stranger");
        Check(!stranger.Success && stranger.State == null, "An outsider cannot read the private match.");
        Check(!authority.Submit("stranger", original).Success, "An outsider cannot submit actions.");
        string before = SimulationSnapshot.Fingerprint(authority.Snapshot("alice").State);
        Check(!authority.Submit("alice", new MatchCommand(authority.MatchId, "spoof", 1, MatchAction.EndTurn(1))).Success,
            "An authenticated player cannot spoof the opponent's acting slot.");
        Check(!authority.Submit("alice", new MatchCommand(authority.MatchId, "stale", 0, MatchAction.EndTurn(0))).Success,
            "A stale expected revision cannot commit.");
        Check(!authority.Submit("alice", new MatchCommand(authority.MatchId, original.CommandId, 0, MatchAction.EndTurn(0))).Success,
            "A command identifier cannot be reused for a different payload.");
        Check(!authority.Submit("alice", new MatchCommand("wrong.match", "wrong", 1, MatchAction.EndTurn(0))).Success,
            "A command for another match is rejected.");
        Check(authority.Snapshot("alice").Revision == 1
            && SimulationSnapshot.Fingerprint(authority.Snapshot("alice").State) == before,
            "Unauthorized, stale and conflicting requests leave state and RNG unchanged.");
    }

    private static async Task ConcurrentRequests(MatchEngine engine, MatchState initial)
    {
        var authority = CreateAuthority("concurrent.match", engine, initial);
        using var start = new ManualResetEventSlim(false);
        var jobs = Enumerable.Range(0, 2).Select(index => Task.Run(() =>
        {
            start.Wait();
            return authority.Submit("alice", new MatchCommand(authority.MatchId, "race." + index, 0, MatchAction.EndTurn(0)));
        })).ToArray();
        start.Set();
        var replies = await Task.WhenAll(jobs);
        Check(replies.Count(reply => reply.Success) == 1, "Only one concurrent request at the same revision commits.");
        Check(authority.Snapshot("alice").Revision == 1 && authority.Snapshot("alice").State.Turn == 1
            && authority.Snapshot("alice").State.Players[1].Money == 500,
            "Concurrent requests advance the turn and award income exactly once.");
        Pass("Concurrent command serialization inside one authority process");
    }

    private static void PrivateStateRestore(AuthoritativeMatch authority, MatchEngine engine)
    {
        var source = authority.Snapshot("alice");
        string publicJson = JsonSerializer.Serialize(Public(source.State), Json);
        Check(!publicJson.Contains("RandomState", StringComparison.Ordinal), "Client snapshot JSON omits the private RNG stream.");
        string storedJson = JsonSerializer.Serialize(MatchStateData.FromState(source.State), Json);
        var restored = JsonSerializer.Deserialize<MatchStateData>(storedJson, Json).ToState();
        Check(restored.RandomState == source.State.RandomState
            && SimulationSnapshot.Fingerprint(restored) == SimulationSnapshot.Fingerprint(source.State),
            "Private storage JSON restores the exact combat RNG, board, economy and turn.");
        var resumed = CreateAuthority("resumed.match", engine, restored, source.Revision);
        var next = MatchAction.EndTurn(1);
        var originalResult = authority.Submit("bob", new MatchCommand(authority.MatchId, "continue", source.Revision, next));
        var resumedResult = resumed.Submit("bob", new MatchCommand(resumed.MatchId, "continue", source.Revision, next));
        Check(originalResult.Success && resumedResult.Success
            && SimulationSnapshot.Fingerprint(originalResult.State) == SimulationSnapshot.Fingerprint(resumedResult.State),
            "Original and restored state execute the next action identically.");
        // Receipt persistence is deliberately outside this proof: AuthoritativeMatch is in memory.
        Pass("Public snapshot privacy and private snapshot restore/continuation");
    }

    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json);
    private static MatchSnapshotData Public(MatchState state) => RoundTrip(MatchSnapshotData.FromState(state));
    private static MatchTransportReply Reply(string matchId, SessionActionResult result)
        => RoundTrip(new MatchTransportReply { MatchId = matchId, Success = result.Success, Error = result.Error,
            Revision = result.Revision, Snapshot = result.State == null ? null : MatchSnapshotData.FromState(result.State) });
    private static void CheckPublicEqual(MatchState first, MatchState second, string message)
        => Check(JsonSerializer.Serialize(Public(first), Json) == JsonSerializer.Serialize(Public(second), Json), message);
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Pass(string message) => Console.WriteLine("PASS: " + message);

    private sealed class FakeTransport : IMatchTransport
    {
        private readonly AuthoritativeMatch authority;
        private readonly string authenticatedPlayer;
        public readonly List<MatchCommand> Submitted = new();
        public bool LoseNextReplyAfterCommit;
        public MatchTransportReply NextReadReply;
        public FakeTransport(AuthoritativeMatch authority, string authenticatedPlayer)
        { this.authority = authority; this.authenticatedPlayer = authenticatedPlayer; }
        public Task<MatchTransportReply> ReadAsync(string matchId)
        {
            if (matchId != authority.MatchId) throw new InvalidOperationException("Wrong match.");
            var reply = NextReadReply ?? Reply(matchId, authority.Snapshot(authenticatedPlayer));
            NextReadReply = null;
            return Task.FromResult(reply);
        }
        public Task<MatchTransportReply> SubmitAsync(MatchCommand command)
        {
            var decoded = RoundTrip(MatchCommandData.FromCommand(command)).ToCommand();
            Check(decoded.CommandId == command.CommandId && decoded.SamePayload(command),
                "Wire JSON roundtrip preserves command identity, revision and every action field.");
            Submitted.Add(decoded);
            var result = authority.Submit(authenticatedPlayer, decoded);
            if (LoseNextReplyAfterCommit && result.Success)
            {
                LoseNextReplyAfterCommit = false;
                return Task.FromException<MatchTransportReply>(new IOException("Simulated lost response after server commit."));
            }
            return Task.FromResult(Reply(authority.MatchId, result));
        }
    }
}
