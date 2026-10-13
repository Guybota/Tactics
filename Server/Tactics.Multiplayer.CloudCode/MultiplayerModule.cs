using Tactics.Core;
using Tactics.Multiplayer.Backend;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace Tactics.Multiplayer.CloudCode;

// The platform supplies execution context. Requests have no identity field;
// authoritative identity is always context.PlayerId, never request data.
public sealed class MultiplayerModule
{
    [CloudCodeFunction("CreateMatch")]
    public Task<MatchLobbyReply> CreateMatch(IExecutionContext context, IGameApiClient gameApiClient, MatchCreateRequest request)
        => Lobby(context, gameApiClient, service => service.CreateAsync(context.PlayerId, request));

    [CloudCodeFunction("JoinMatch")]
    public Task<MatchLobbyReply> JoinMatch(IExecutionContext context, IGameApiClient gameApiClient, MatchJoinRequest request)
        => Lobby(context, gameApiClient, service => service.JoinAsync(context.PlayerId, request));

    [CloudCodeFunction("GetLobby")]
    public Task<MatchLobbyReply> GetLobby(IExecutionContext context, IGameApiClient gameApiClient, string matchId)
        => Lobby(context, gameApiClient, service => service.GetLobbyAsync(context.PlayerId, matchId));

    [CloudCodeFunction("GetMatch")]
    public Task<MatchTransportReply> GetMatch(IExecutionContext context, IGameApiClient gameApiClient, string matchId)
        => Match(context, gameApiClient, matchId, service => service.ReadAsync(context.PlayerId, matchId));

    [CloudCodeFunction("SubmitAction")]
    public Task<MatchTransportReply> SubmitAction(IExecutionContext context, IGameApiClient gameApiClient, MatchCommandData command)
        => Match(context, gameApiClient, command?.MatchId, service => service.SubmitAsync(context.PlayerId, command));

    private static Task<MatchLobbyReply> Lobby(IExecutionContext context, IGameApiClient api,
        Func<DurableMatchService, Task<MatchLobbyReply>> operation)
    {
        if (!Authenticated(context)) return Task.FromResult(new MatchLobbyReply
        { SchemaVersion = MatchLobbyReply.CurrentVersion, Success = false, Error = "Player authentication is required." });
        return Execute(context, api, operation);
    }

    private static Task<MatchTransportReply> Match(IExecutionContext context, IGameApiClient api, string matchId,
        Func<DurableMatchService, Task<MatchTransportReply>> operation)
    {
        if (!Authenticated(context)) return Task.FromResult(new MatchTransportReply
        { MatchId = matchId, Success = false, Revision = -1, Error = "Player authentication is required." });
        return Execute(context, api, operation);
    }

    private static bool Authenticated(IExecutionContext context)
        => context != null && !string.IsNullOrWhiteSpace(context.PlayerId);

    private static async Task<T> Execute<T>(IExecutionContext context, IGameApiClient api,
        Func<DurableMatchService, Task<T>> operation)
    {
        try
        {
            // Request-scoped store: no player identity, token or mutable match state
            // is cached across Cloud Code invocations or workers.
            var service = new DurableMatchService(new CloudSaveMatchStore(context, api), EmbeddedMatchContent.Value);
            return await operation(service);
        }
        catch (Exception)
        {
            // Never return private records, identity bindings, SDK errors or tokens.
            // Throw instead of claiming rejection: persistence may already have committed.
            throw new Exception("The multiplayer service could not confirm this request. Retry with the same command ID.");
        }
    }
}
