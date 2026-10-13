using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tactics.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

// Explicit live check; requires Play Mode so the actual Unity runtime SDK is registered.
public static class UgsUnityClientSmoke
{
    private const string Project = "a5e13aaf-bbbb-44ff-965e-fc9773bbada6";
    private const string EnvironmentId = "6e2fd039-7abd-412b-aa01-7f100a0b5001";
    private const string StatusKey = "Tactics.UgsUnityClientSmoke";
    public static string Status => SessionState.GetString(StatusKey, "idle");
    public static void Begin()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode for the live Unity SDK check.");
        if (Application.cloudProjectId != Project) throw new InvalidOperationException("Unexpected project.");
        Set(new { phase = "running" });
        Run();
    }

    private static async void Run()
    {
        try
        {
            var client = new UgsCloudModuleClient("development");
            var transport = new UgsMatchTransport(client);
            var created = await transport.CreateAsync(new MatchCreateRequest
                { SchemaVersion = 1, MapId = "default-1v1", RaceId = "race.human" });
            Require(created.Success && created.Status == MatchLobbyStatus.Waiting, "Native SDK creation failed.");
            string guestToken = await GuestToken();
            var joined = await GuestCall<MatchLobbyReply>(guestToken, "JoinMatch", new
                { request = new MatchJoinRequest { SchemaVersion = 1, MatchId = created.MatchId, RaceId = "race.elf" } });
            joined.Validate();
            Require(joined.Success && joined.PlayerSlot == 1, "Guest join failed.");
            var lobby = await transport.GetLobbyAsync(created.MatchId);
            Require(lobby.Success && lobby.Status == MatchLobbyStatus.Active, "Native SDK lobby failed.");
            var journal = new OnlineSessionFileJournal(Path.Combine(Application.persistentDataPath, "OnlineMatches"),
                Project, "development", client.PlayerId, created.MatchId);
            var unreliable = new LostReplyTransport(transport);
            var session = new OnlineMatchSession(unreliable, created.MatchId, lobby.Snapshot, lobby.Revision, journal);
            var leader = session.Snapshot().Players[0].Units.Single(u => u.IsLeader);
            var uncertain = await session.SubmitAsync(new MatchAction(MatchActionKind.Wait, new UnitKey(0, leader.Id), leader.Position), session.Revision);
            Require(!uncertain.Success && session.HasPendingCommand && journal.Read().HasPendingCommand, "Lost reply was not journaled.");
            var restarted = OnlineMatchSession.Restore(transport, journal.Read(), journal);
            Require((await restarted.RetryPendingAsync()).Success && !journal.Read().HasPendingCommand
                && restarted.Revision == lobby.Revision + 1, "Native SDK retry duplicated or lost the action.");
            Require((await restarted.SubmitAsync(MatchAction.EndTurn(0), restarted.Revision)).Success, "Native SDK end turn failed.");
            var guestView = await GuestCall<MatchTransportReply>(guestToken, "GetMatch", new { matchId = created.MatchId });
            Require(guestView.Success && guestView.Snapshot.Turn == 1, "Guest could not read the native client's turn.");
            var guestCommand = new MatchCommand(created.MatchId, Guid.NewGuid().ToString("N"), guestView.Revision, MatchAction.EndTurn(1));
            var guestEnd = await GuestCall<MatchTransportReply>(guestToken, "SubmitAction", new { command = MatchCommandData.FromCommand(guestCommand) });
            Require(guestEnd.Success, "Guest end turn failed.");
            Require((await restarted.RefreshAsync()).Success && restarted.Snapshot().Turn == 2, "Native SDK refresh failed.");
            // The high-level connector must find and restore the same account-scoped file.
            var reopened = await new UgsMatchConnector(client).OpenAsync(created.MatchId);
            Require(reopened.Revision == restarted.Revision && reopened.Snapshot().Turn == 2
                && !reopened.HasPendingCommand, "Native connector recovery failed.");
            Set(new { phase = "passed", environment = "development", environmentId = EnvironmentId,
                matchId = created.MatchId, revision = reopened.Revision, turn = reopened.Snapshot().Turn,
                sdk = true, diskJournal = true, duplicateRetry = true, connectorRecovery = true });
        }
        catch (Exception error) { Set(new { phase = "failed", errorType = error.GetType().Name }); }
    }

    private sealed class LostReplyTransport : IMatchTransport
    {
        private readonly IMatchTransport inner;
        private bool lose = true;
        public LostReplyTransport(IMatchTransport inner) { this.inner = inner; }
        public Task<MatchTransportReply> ReadAsync(string id) => inner.ReadAsync(id);
        public async Task<MatchTransportReply> SubmitAsync(MatchCommand command)
        {
            var reply = await inner.SubmitAsync(command);
            if (lose && reply.Success) { lose = false; throw new TimeoutException("Simulated reply loss after commit."); }
            return reply;
        }
    }

    private static async Task<string> GuestToken()
    {
        var request = JsonRequest("https://player-auth.services.api.unity.com/v1/authentication/anonymous", new JObject());
        request.SetRequestHeader("ProjectId", Project);
        request.SetRequestHeader("UnityEnvironment", "development");
        request.SetRequestHeader("Error-Version", "v1");
        var response = await Send(request);
        string token = (string)response["idToken"];
        Require(!string.IsNullOrEmpty(token), "Missing anonymous credentials.");
        string payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
        var audience = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)))["aud"] as JArray;
        Require(audience != null && audience.Any(a => (string)a == "envId:" + EnvironmentId), "Guest authenticated in an unexpected environment.");
        return token;
    }

    // Explicit development test guest; its bearer token never leaves this helper.
    public static async Task<MatchLobbyReply> JoinGuestAsync(string matchId)
    {
        string token = await GuestToken();
        var reply = await GuestCall<MatchLobbyReply>(token, "JoinMatch", new
            { request = new MatchJoinRequest { SchemaVersion = 1, MatchId = matchId, RaceId = "race.elf" } });
        reply.Validate();
        if (!reply.Success) throw new InvalidOperationException("The development test guest could not join.");
        return reply;
    }

    private static async Task<T> GuestCall<T>(string token, string endpoint, object arguments)
    {
        var request = JsonRequest("https://cloud-code.services.api.unity.com/v1/projects/" + Project + "/modules/TacticsMultiplayer/" + endpoint,
            new JObject { ["params"] = JObject.FromObject(arguments) });
        request.SetRequestHeader("Authorization", "Bearer " + token);
        var response = await Send(request);
        return response["output"].ToObject<T>();
    }

    private static UnityWebRequest JsonRequest(string url, JObject body)
    {
        var request = new UnityWebRequest(url, "POST") { uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None))),
            downloadHandler = new DownloadHandlerBuffer(), timeout = 40 };
        request.SetRequestHeader("Content-Type", "application/json");
        return request;
    }

    private static async Task<JObject> Send(UnityWebRequest request)
    {
        using (request)
        {
            var operation = request.SendWebRequest();
            var completion = new TaskCompletionSource<bool>();
            operation.completed += _ => completion.TrySetResult(true);
            if (operation.isDone) completion.TrySetResult(true);
            await completion.Task;
            if (request.result != UnityWebRequest.Result.Success) throw new InvalidOperationException("Live request failed with status " + request.responseCode);
            return JObject.Parse(request.downloadHandler.text);
        }
    }
    private static void Require(bool condition, string description) { if (!condition) throw new InvalidOperationException(description); }
    private static void Set(object value) => SessionState.SetString(StatusKey, JsonConvert.SerializeObject(value));
}
