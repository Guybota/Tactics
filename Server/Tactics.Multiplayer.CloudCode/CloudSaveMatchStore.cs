using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tactics.Multiplayer.Backend;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;

namespace Tactics.Multiplayer.CloudCode;

// One private custom-data key holds the complete document, including revision and
// receipts. Every update uses the exact write lock obtained with that document.
public sealed class CloudSaveMatchStore : IMatchStore
{
    public const string RecordKey = "record";
    private readonly IExecutionContext context;
    private readonly IGameApiClient api;

    public CloudSaveMatchStore(IExecutionContext context, IGameApiClient api)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        if (string.IsNullOrWhiteSpace(context.ProjectId) || string.IsNullOrWhiteSpace(context.ServiceToken))
            throw new InvalidOperationException("Server service context is unavailable.");
    }

    public async Task<StoredMatch> ReadAsync(string matchId)
    {
        string customId = CustomId(matchId);
        try
        {
            var response = await api.CloudSaveData.GetPrivateCustomItemsAsync(context, context.ServiceToken,
                context.ProjectId, customId, new List<string> { RecordKey });
            var item = response.Data.Results.SingleOrDefault(candidate => candidate.Key == RecordKey);
            if (item == null) return null;
            string json = item.Value is string text ? text
                : item.Value is JValue value && value.Type == JTokenType.String ? value.Value<string>() : null;
            RequireJson(json);
            if (string.IsNullOrWhiteSpace(item.WriteLock)) throw new InvalidDataException("Stored match has no write lock.");
            return new StoredMatch(json, item.WriteLock);
        }
        catch (ApiException exception) when (exception.Response?.StatusCode == HttpStatusCode.NotFound)
        { return null; }
    }

    // Cloud Save has no documented atomic create-if-absent operation. The backend
    // supplies a fresh cryptographic UUID that clients cannot choose. This preflight
    // refuses an existing record; unique UUID allocation prevents another creator
    // targeting the same record. It is not a database uniqueness constraint.
    public async Task<bool> CreateAsync(string matchId, string json)
    {
        string customId = CustomId(matchId);
        RequireJson(json);
        if (await ReadAsync(matchId) != null) return false;
        try
        {
            await api.CloudSaveData.SetPrivateCustomItemAsync(context, context.ServiceToken,
                context.ProjectId, customId, new SetItemBody(RecordKey, json));
            return true;
        }
        catch (ApiException exception) when (exception.Response?.StatusCode == HttpStatusCode.Conflict)
        { return false; }
    }

    public async Task<bool> CompareExchangeAsync(string matchId, string expectedWriteLock, string json)
    {
        string customId = CustomId(matchId);
        RequireJson(json);
        // Never omit or send a null lock for an update: either bypasses Cloud Save CAS.
        if (string.IsNullOrWhiteSpace(expectedWriteLock)) throw new ArgumentException("A write lock is required.");
        try
        {
            await api.CloudSaveData.SetPrivateCustomItemAsync(context, context.ServiceToken,
                context.ProjectId, customId, new SetItemBody(RecordKey, json, expectedWriteLock));
            return true;
        }
        catch (ApiException exception) when (exception.Response?.StatusCode == HttpStatusCode.Conflict
            || exception.Response?.StatusCode == HttpStatusCode.NotFound)
        { return false; }
        // Timeout, rate limit and other SDK failures propagate as ambiguous failures.
        // They cannot be reported as definitive non-commits after a possible write.
    }

    private static string CustomId(string matchId)
    {
        if (!Guid.TryParseExact(matchId, "N", out var id) || matchId != id.ToString("N"))
            throw new ArgumentException("Invalid match identifier.");
        return "tactics_match_" + matchId; // 45 characters, within Cloud Save's 50-character bound.
    }

    private static void RequireJson(string json)
    {
        // Reject excessive input before additional string serialization/allocation.
        if (json == null || json.Length > MatchDocumentCodec.MaxJsonBytes
            || Encoding.UTF8.GetByteCount(json) > MatchDocumentCodec.MaxJsonBytes)
            throw new InvalidDataException("Stored match exceeds the supported record size.");
        // JSON is stored as one string value; escaping adds bytes. Keep even that
        // representation below the per-custom-item 5 MiB Private Data quota.
        if (Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(json)) > 5 * 1024 * 1024 - 1024)
            throw new InvalidDataException("Encoded match exceeds the private storage quota.");
    }
}
