using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tactics.Core;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, PropertyNameCaseInsensitive = true };
    private static int calls, assertions;
    private static void Check(bool condition, string description)
    { if (!condition) throw new CheckFailure(description); Interlocked.Increment(ref assertions); }

    private sealed class CheckFailure(string description) : Exception(description);
    private sealed class HttpFailure(string operation, int status) : Exception
    {
        public string Operation { get; } = operation;
        public int Status { get; } = status;
    }
    private sealed record Options(string ProjectId, string Environment, string EnvironmentId, string Module, string RaceId);
    private sealed record Player(string Id, string Token);

    private static async Task<JsonElement> Post(HttpClient client, HttpRequestMessage request, string operation)
    {
        if (Interlocked.Increment(ref calls) > 50) throw new CheckFailure("The live test exceeded its 50-call budget.");
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode) throw new HttpFailure(operation, (int)response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<Player> Authenticate(HttpClient client, Options options)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://player-auth.services.api.unity.com/v1/authentication/anonymous");
        request.Headers.Add("ProjectId", options.ProjectId);
        request.Headers.Add("UnityEnvironment", options.Environment);
        request.Headers.Add("Error-Version", "v1");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        var json = await Post(client, request, "Anonymous authentication");
        string id = json.GetProperty("userId").GetString(), token = json.GetProperty("idToken").GetString();
        Check(!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(token), "Authentication returned player credentials.");
        // SDK AuthenticationServiceInternal extracts environment from aud's envId:<id>.
        // This is a routing sanity check, not local JWT signature verification. Tokens
        // are obtained over HTTPS and verified by Unity on every module invocation.
        string payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
        using var jwt = JsonDocument.Parse(Convert.FromBase64String(payload));
        var audience = jwt.RootElement.GetProperty("aud");
        Check(audience.ValueKind == JsonValueKind.Array && audience.EnumerateArray().Any(a => a.GetString() == "envId:" + options.EnvironmentId),
            "Authentication must route to the explicitly approved environment ID.");
        return new Player(id, token);
    }

    private sealed class ModuleClient : IDisposable
    {
        private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(40) };
        private readonly Options options;
        private readonly Player player;
        public ModuleClient(Options options, Player player) { this.options = options; this.player = player; }
        public void Dispose() => client.Dispose();

        public async Task<T> Call<T>(string function, object arguments)
        {
            string url = $"https://cloud-code.services.api.unity.com/v1/projects/{options.ProjectId}/modules/{options.Module}/{function}";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", player.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, object> { { "params", arguments } }, Json), Encoding.UTF8, "application/json");
            var envelope = await Post(client, request, function);
            var output = envelope.GetProperty("output");
            // Always inspect public output only; never print response bodies or tokens.
            Check(!ContainsPrivateField(output), function + " must not expose RNG, player bindings or receipts.");
            return JsonSerializer.Deserialize<T>(output.GetRawText(), Json) ?? throw new CheckFailure("Missing module output.");
        }

        public Task<MatchTransportReply> Read(string id) => Call<MatchTransportReply>("GetMatch", new { matchId = id });
        public Task<MatchTransportReply> Submit(MatchCommandData command) => Call<MatchTransportReply>("SubmitAction", new { command });
    }

    private static bool ContainsPrivateField(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name.Equals("RandomState", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("PlayerId", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("Receipts", StringComparison.OrdinalIgnoreCase)) return true;
                if (ContainsPrivateField(property.Value)) return true;
            }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) if (ContainsPrivateField(item)) return true;
        return false;
    }

    private static string Fingerprint(MatchSnapshotData snapshot) => JsonSerializer.Serialize(snapshot, Json);
    private static MatchCommandData Command(string matchId, int revision, MatchAction action, string commandId = null)
        => MatchCommandData.FromCommand(new MatchCommand(matchId, commandId ?? Guid.NewGuid().ToString("N"), revision, action));
    private static MatchJoinRequest JoinRequest(string id, Options options)
        => new() { SchemaVersion = MatchJoinRequest.CurrentVersion, MatchId = id, RaceId = options.RaceId };

    private static async Task Run(Options options)
    {
        using var authentication = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        var aliceIdentity = await Authenticate(authentication, options);
        var bobIdentity = await Authenticate(authentication, options);
        Check(aliceIdentity.Id != bobIdentity.Id, "Fresh anonymous profiles must have distinct identities.");
        var alice = new ModuleClient(options, aliceIdentity);
        using var bob = new ModuleClient(options, bobIdentity);
        try
        {
            var created = await alice.Call<MatchLobbyReply>("CreateMatch", new { request = new MatchCreateRequest
            { SchemaVersion = MatchCreateRequest.CurrentVersion, MapId = "default-1v1", RaceId = options.RaceId } });
            created.Validate();
            Check(created.Success && created.Status == MatchLobbyStatus.Waiting && created.PlayerSlot == 0, "Creator enters private waiting slot zero.");
            string id = created.MatchId;
            var outsider = await bob.Call<MatchLobbyReply>("GetLobby", new { matchId = id });
            Check(!outsider.Success && outsider.Snapshot == null, "Uninvited identity cannot inspect the lobby.");
            Check(!(await alice.Read(id)).Success, "Waiting match has no playable state.");
            var joined = await bob.Call<MatchLobbyReply>("JoinMatch", new { request = JoinRequest(id, options) });
            joined.Validate();
            Check(joined.Success && joined.PlayerSlot == 1 && joined.Status == MatchLobbyStatus.Active, "Invitation starts authenticated private 1v1.");
            var hostLobby = await alice.Call<MatchLobbyReply>("GetLobby", new { matchId = id });
            hostLobby.Validate();
            Check(hostLobby.Success && hostLobby.Revision == joined.Revision, "Creator reads started persistent lobby.");
            var duplicateJoin = await bob.Call<MatchLobbyReply>("JoinMatch", new { request = JoinRequest(id, options) });
            Check(duplicateJoin.Success && duplicateJoin.Revision == joined.Revision
                && Fingerprint(duplicateJoin.Snapshot) == Fingerprint(joined.Snapshot), "Repeated join does not grant opening income again.");
            Console.WriteLine("PASS two anonymous players, private invitation, identity isolation, persisted start");

            var initial = await alice.Read(id);
            Check(initial.Success && initial.Snapshot.Turn == 0, "Started match begins on the first player's turn.");
            var spoof = await bob.Submit(Command(id, initial.Revision, MatchAction.EndTurn(0)));
            Check(!spoof.Success && spoof.Revision == initial.Revision, "Second identity cannot spoof the first player's slot.");
            var leader = initial.Snapshot.Players[0].Units.Single(u => u.IsLeader);
            var wait = Command(id, initial.Revision, new MatchAction(MatchActionKind.Wait, new UnitKey(0, leader.Id), new GridPosition(leader.X, leader.Y)));
            var committed = await alice.Submit(wait);
            Check(committed.Success && committed.Revision == initial.Revision + 1, "A legal wait commits exactly one revision.");
            var otherView = await bob.Read(id);
            Check(otherView.Success && Fingerprint(otherView.Snapshot) == Fingerprint(committed.Snapshot), "Independent client reads the same committed board.");
            // Simulate loss of the first response and a client restart: throw away the
            // HTTP client, retain identity and the original immutable command in memory.
            alice.Dispose(); alice = new ModuleClient(options, aliceIdentity);
            var retried = await alice.Submit(wait);
            Check(retried.Success && retried.Revision == committed.Revision
                && Fingerprint(retried.Snapshot) == Fingerprint(committed.Snapshot), "New HTTP client retries exact command without duplicating its effect.");
            var mismatch = Command(id, committed.Revision, MatchAction.EndTurn(0), wait.CommandId);
            Check(!(await alice.Submit(mismatch)).Success, "Existing command ID rejects a changed payload.");
            var end = await alice.Submit(Command(id, committed.Revision, MatchAction.EndTurn(0)));
            Check(end.Success && end.Snapshot.Turn == 1, "First player ends turn on the server.");
            var stale = await bob.Submit(Command(id, committed.Revision, MatchAction.EndTurn(1)));
            Check(!stale.Success && stale.Revision == end.Revision, "Stale second-client revision receives confirmed current state.");
            var concurrent = await Task.WhenAll(bob.Submit(Command(id, end.Revision, MatchAction.EndTurn(1))),
                bob.Submit(Command(id, end.Revision, MatchAction.EndTurn(1))));
            Check(concurrent.Count(r => r.Success) == 1, "Concurrent real Cloud Code requests commit only one same-revision action.");
            var current = await alice.Read(id);
            Check(current.Success && current.Revision == end.Revision + 1 && current.Snapshot.Turn == 2, "Server persistence retains exactly two completed turns.");
            var oldReceipt = await alice.Submit(wait);
            Check(oldReceipt.Success && oldReceipt.Revision == current.Revision
                && Fingerprint(oldReceipt.Snapshot) == Fingerprint(current.Snapshot), "Historical receipt returns current state without rollback.");
            Console.WriteLine("PASS real persisted actions, fresh HTTP client retry, spoof/stale rejection, concurrent CAS");
            Console.WriteLine($"Live UGS smoke passed: {assertions} assertions, {calls}/50 calls; match {id}, environment {options.Environment}.");
            Console.WriteLine("Private RNG is absent from public replies; this test verifies public state stability and does not inspect server RNG.");
        }
        finally { alice.Dispose(); }
    }

    private static async Task<int> Main(string[] args)
    {
        if (!args.Contains("--run-live"))
        {
            Console.WriteLine("Built live verifier. No remote calls made.");
            Console.WriteLine("After approved development deployment: --run-live --project-id <UUID> --environment <name> --environment-id <UUID> [--module TacticsMultiplayer] [--race-id race.elf]");
            return 0;
        }
        try
        {
            string Read(string key, string fallback = null)
            {
                int index = Array.IndexOf(args, key);
                if (index < 0) return fallback ?? throw new CheckFailure("Missing required public configuration: " + key);
                if (index + 1 >= args.Length) throw new CheckFailure("Missing configuration value: " + key);
                return args[index + 1];
            }
            var options = new Options(Read("--project-id"), Read("--environment"), Read("--environment-id"), Read("--module", "TacticsMultiplayer"), Read("--race-id", "race.elf"));
            Check(Guid.TryParse(options.ProjectId, out _) && Guid.TryParse(options.EnvironmentId, out _), "Project/environment IDs must be UUIDs.");
            Check(options.Environment.Length <= 64 && options.Module.Length <= 64
                && options.Environment.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.')
                && options.Module.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.'), "Public configuration contains invalid routing characters.");
            Check(!options.Environment.Equals("production", StringComparison.OrdinalIgnoreCase), "This verifier is restricted to an approved development environment.");
            await Run(options); return 0;
        }
        catch (CheckFailure error) { Console.Error.WriteLine("Live test check failed: " + error.Message); return 1; }
        catch (HttpFailure error) { Console.Error.WriteLine($"Live test HTTP failure: {error.Operation}, status {error.Status}. Response body and credentials omitted."); return 1; }
        catch (Exception error) { Console.Error.WriteLine("Live test stopped: " + error.GetType().Name + ". Response body and credentials omitted."); return 1; }
    }
}
