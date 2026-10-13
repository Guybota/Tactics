using System.Net;
using System.Reflection;
using System.Text.Json;
using Newtonsoft.Json;
using Tactics.Core;
using Tactics.Multiplayer.CloudCode;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Api;
using Unity.Services.CloudSave.Model;

internal static class Program
{
    private static int checks;
    private static async Task<int> Main()
    {
        try
        {
            var cloud = DispatchProxy.Create<ICloudSaveDataApi, CloudSaveProxy>();
            var fake = (CloudSaveProxy)cloud;
            var api = DispatchProxy.Create<IGameApiClient, GameApiProxy>();
            ((GameApiProxy)api).CloudSave = cloud;
            var alice = new Context("alice-private-id");
            var bob = new Context("bob-private-id");
            var store = new CloudSaveMatchStore(alice, api);
            string id = Guid.NewGuid().ToString("N");
            Check(await store.ReadAsync(id) == null, "Missing record returns null.");
            Check(await store.CreateAsync(id, "{\"test\":1}"), "UUID creation writes a new private record.");
            int writes = fake.Writes;
            Check(!await store.CreateAsync(id, "{\"test\":2}") && fake.Writes == writes,
                "Creation refuses existing records without issuing a write.");
            var old = await store.ReadAsync(id);
            Check(await store.CompareExchangeAsync(id, old.WriteLock, "{\"test\":3}"), "Correct write lock commits.");
            Check(!await store.CompareExchangeAsync(id, old.WriteLock, "{\"test\":4}"), "Stale write lock returns noncommit.");
            Check((await store.ReadAsync(id)).Json == "{\"test\":3}", "A rejected CAS does not alter saved JSON.");
            Check(await Throws<ArgumentException>(() => store.CompareExchangeAsync(id, null, "{}")),
                "Update cannot bypass CAS with a null lock.");
            Check(await Throws<ArgumentException>(() => store.ReadAsync("client-selected-key")),
                "Private record names only accept server UUID format.");
            fake.FailNextSetAfterCommit = true;
            string currentLock = (await store.ReadAsync(id)).WriteLock;
            Check(await Throws<IOException>(() => store.CompareExchangeAsync(id, currentLock, "{}")),
                "Ambiguous SDK failures propagate instead of reporting noncommit.");

            using var resource = typeof(MultiplayerModule).Assembly.GetManifestResourceStream("Tactics.Multiplayer.Content.catalogue.json");
            var catalogue = System.Text.Json.JsonSerializer.Deserialize<MatchContentData>(resource,
                new JsonSerializerOptions { IncludeFields = true });
            Check(EmbeddedMatchContent.Value.ContentVersion == catalogue.Version, "Published module loads the embedded real catalogue.");
            var module = new MultiplayerModule();
            var created = await module.CreateMatch(alice, api, new MatchCreateRequest
            { SchemaVersion = 1, MapId = "default-1v1", RaceId = catalogue.Races[0].Id });
            created.Validate();
            Check(created.Success && created.Status == MatchLobbyStatus.Waiting && created.Snapshot == null,
                "Authenticated creation exposes a private waiting lobby without a game snapshot.");
            var joined = await new MultiplayerModule().JoinMatch(bob, api, new MatchJoinRequest
            { SchemaVersion = 1, MatchId = created.MatchId, RaceId = catalogue.Races[1].Id });
            joined.Validate();
            Check(joined.Success && joined.PlayerSlot == 1 && joined.Status == MatchLobbyStatus.Active,
                "Another invocation joins and initializes the persisted match.");
            var lobby = await new MultiplayerModule().GetLobby(alice, api, created.MatchId);
            Check(lobby.Success && lobby.PlayerSlot == 0 && lobby.Revision == 1,
                "Creator retrieves the active lobby through a fresh service invocation.");
            var outsider = await module.GetMatch(new Context("outsider"), api, created.MatchId);
            Check(!outsider.Success && outsider.Snapshot == null, "Outsiders receive no private match snapshot.");
            Check(!(await module.CreateMatch(new Context(null), api, null)).Success,
                "Service calls without a player context cannot create matches.");
            var command = MatchCommandData.FromCommand(new MatchCommand(created.MatchId, "lost-reply", 1, MatchAction.EndTurn(0)));
            fake.FailNextSetAfterCommit = true;
            try { await module.SubmitAction(alice, api, command); throw new InvalidOperationException("Expected uncertain commit."); }
            catch (Exception error) when (error.Message == "The multiplayer service could not confirm this request. Retry with the same command ID.")
            { Check(error.InnerException == null, "Module strips SDK details while preserving uncertain-request semantics."); }
            var confirmed = await new MultiplayerModule().SubmitAction(alice, api, command);
            Check(confirmed.Success && confirmed.Revision == 2 && confirmed.Snapshot.Turn == 1,
                "Fresh invocation retries a committed command using persisted receipts exactly once.");
            Check((await module.GetMatch(bob, api, created.MatchId)).Revision == 2,
                "Opponent observes the confirmed persisted revision.");
            string publicJson = JsonConvert.SerializeObject(confirmed);
            Check(!publicJson.Contains("RandomState", StringComparison.Ordinal)
                && !publicJson.Contains("alice-private-id", StringComparison.Ordinal)
                && !publicJson.Contains("bob-private-id", StringComparison.Ordinal),
                "Actual module response serializer reveals neither RNG nor private identity bindings.");
            Check(fake.Calls > 0 && fake.BadTokenCalls == 0 && fake.NonPrivateCalls == 0,
                "SDK adapter calls only private custom methods with the service token.");
            Console.WriteLine($"PASS: {checks} Cloud Code adapter/module assertions against official SDK interfaces (fake service, no remote calls).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
    }

    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static async Task<bool> Throws<T>(Func<Task> operation) where T : Exception
    { try { await operation(); return false; } catch (T) { return true; } }

    private sealed class Context : IExecutionContext
    {
        public Context(string player) { PlayerId = player; }
        public string PlayerId { get; }
        public string ProjectId => "test-project";
        public string ServiceToken => "test-service-token";
        public string AccessToken => "player-token-must-not-be-used";
        public string EnvironmentId => "test-environment";
        public string EnvironmentName => "test";
        public string UserId => null;
        public string Issuer => null;
        public string AnalyticsUserId => null;
        public string UnityInstallationId => null;
        public string CorrelationId => "test-correlation";
        public string ScopeId => "test-scope";
        public int CallDepth => 0;
        public ISession Session => null;
    }
}

public class GameApiProxy : DispatchProxy
{
    public ICloudSaveDataApi CloudSave;
    protected override object Invoke(MethodInfo method, object[] args)
        => method.Name == "get_CloudSaveData" ? CloudSave : throw new NotSupportedException(method.Name);
}

public class CloudSaveProxy : DispatchProxy
{
    private readonly Dictionary<string, (string Json, string Lock)> records = new();
    public int Writes, Calls, BadTokenCalls, NonPrivateCalls;
    public bool FailNextSetAfterCommit;
    protected override object Invoke(MethodInfo method, object[] args)
    {
        Calls++;
        if ((string)args[1] != "test-service-token") BadTokenCalls++;
        string id = (string)args[3];
        if (method.Name == "GetPrivateCustomItemsAsync")
        {
            var items = new List<Item>();
            if (records.TryGetValue(id, out var record)) items.Add(new Item("record", record.Json, record.Lock,
                new ModifiedMetadata(DateTime.UtcNow), new ModifiedMetadata(DateTime.UtcNow)));
            return Task.FromResult(Response(new GetItemsResponse(items, new GetItemsResponseLinks("")), HttpStatusCode.OK));
        }
        if (method.Name == "SetPrivateCustomItemAsync")
        {
            var body = (SetItemBody)args[4];
            if (body.Key != "record") throw new InvalidOperationException("Unexpected private key.");
            if (body.WriteLock != null && (!records.TryGetValue(id, out var old) || old.Lock != body.WriteLock))
                return Task.FromException<ApiResponse<SetItemResponse>>(new ApiException(default, "Private conflict detail",
                    Response<object>(null, HttpStatusCode.Conflict)));
            string nextLock = Guid.NewGuid().ToString("N");
            records[id] = ((string)body.Value, nextLock);
            Writes++;
            if (FailNextSetAfterCommit)
            {
                FailNextSetAfterCommit = false;
                return Task.FromException<ApiResponse<SetItemResponse>>(new IOException("Private SDK detail must never reach clients."));
            }
            return Task.FromResult(Response(new SetItemResponse(nextLock), HttpStatusCode.OK));
        }
        NonPrivateCalls++;
        throw new NotSupportedException(method.Name);
    }

    // The real SDK populates these internal setters while decoding HTTP responses.
    // This fake populates them reflectively, without replacing SDK production types.
    private static ApiResponse<T> Response<T>(T data, HttpStatusCode status)
    {
        var result = new ApiResponse<T>();
        typeof(ApiResponse<T>).GetProperty(nameof(result.Data)).SetValue(result, data);
        typeof(ApiResponse<T>).GetProperty(nameof(result.StatusCode)).SetValue(result, status);
        return result;
    }
}
