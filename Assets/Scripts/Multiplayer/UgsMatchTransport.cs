using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tactics.Core;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using UnityEngine;

// Injectable boundary lets transport tests run without signing in or spending cloud quota.
public interface ICloudModuleClient
{
    Task<T> CallAsync<T>(string module, string endpoint, Dictionary<string, object> arguments);
}

public sealed class UgsCloudModuleClient : ICloudModuleClient
{
    private static readonly SemaphoreSlim InitializationGate = new SemaphoreSlim(1, 1);
    private static string initializedEnvironment;
    private readonly string environment;
    public string Environment => environment;
    public string PlayerId => UnityServices.State == ServicesInitializationState.Initialized
        && AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : null;

    public UgsCloudModuleClient(string environment = "development")
    {
        if (string.IsNullOrWhiteSpace(environment) || environment.Length > 64)
            throw new ArgumentException("A UGS environment name is required.", nameof(environment));
        foreach (char c in environment)
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                throw new ArgumentException("Invalid UGS environment name.", nameof(environment));
        this.environment = environment;
    }

    public async Task InitializeAsync()
    {
        await InitializationGate.WaitAsync();
        try
        {
            if (string.IsNullOrWhiteSpace(Application.cloudProjectId))
                throw new InvalidOperationException("Link this Unity project to UGS before connecting online.");
            if (initializedEnvironment != null && initializedEnvironment != environment)
                throw new InvalidOperationException("UGS is already initialized for a different environment. Restart to switch environments.");
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync(new InitializationOptions().SetEnvironmentName(environment));
                initializedEnvironment = environment;
            }
            else if (initializedEnvironment == null)
                throw new InvalidOperationException("UGS was initialized outside the multiplayer client. Use one shared environment configuration.");
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        finally { InitializationGate.Release(); }
    }

    public async Task<T> CallAsync<T>(string module, string endpoint, Dictionary<string, object> arguments)
    {
        await InitializeAsync();
        return await CloudCodeService.Instance.CallModuleEndpointAsync<T>(module, endpoint, arguments);
    }
}

// Creation/joining precedes a playable OnlineMatchSession; waiting lobbies have no board.
public sealed class UgsMatchTransport : IMatchTransport
{
    public const string DefaultModuleName = "TacticsMultiplayer";
    private readonly ICloudModuleClient client;
    private readonly string module;

    public UgsMatchTransport(ICloudModuleClient client, string moduleName = DefaultModuleName)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrWhiteSpace(moduleName)) throw new ArgumentException("A module name is required.", nameof(moduleName));
        module = moduleName;
    }

    public async Task<MatchLobbyReply> CreateAsync(MatchCreateRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        request.Validate();
        var reply = await client.CallAsync<MatchLobbyReply>(module, "CreateMatch", new Dictionary<string, object> { { "request", request } });
        ValidateLobby(reply, null);
        return reply;
    }

    public async Task<MatchLobbyReply> JoinAsync(MatchJoinRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        request.Validate();
        var reply = await client.CallAsync<MatchLobbyReply>(module, "JoinMatch", new Dictionary<string, object> { { "request", request } });
        ValidateLobby(reply, request.MatchId);
        return reply;
    }

    public async Task<MatchLobbyReply> GetLobbyAsync(string matchId)
    {
        var reply = await client.CallAsync<MatchLobbyReply>(module, "GetLobby", MatchArguments(matchId));
        ValidateLobby(reply, matchId);
        return reply;
    }

    public Task<MatchTransportReply> ReadAsync(string matchId)
        => client.CallAsync<MatchTransportReply>(module, "GetMatch", MatchArguments(matchId));

    public Task<MatchTransportReply> SubmitAsync(MatchCommand command)
        => client.CallAsync<MatchTransportReply>(module, "SubmitAction", new Dictionary<string, object>
            { { "command", MatchCommandData.FromCommand(command) } });

    private static Dictionary<string, object> MatchArguments(string matchId)
    {
        if (string.IsNullOrWhiteSpace(matchId) || matchId.Length > 128)
            throw new ArgumentException("A match ID is required.", nameof(matchId));
        foreach (char c in matchId) if (char.IsControl(c)) throw new ArgumentException("Invalid match ID.", nameof(matchId));
        return new Dictionary<string, object> { { "matchId", matchId } };
    }

    private static void ValidateLobby(MatchLobbyReply reply, string expectedMatchId)
    {
        if (reply == null) throw new InvalidOperationException("The server returned no lobby reply.");
        reply.Validate();
        if (reply.Success && expectedMatchId != null && !string.Equals(reply.MatchId, expectedMatchId, StringComparison.Ordinal))
            throw new InvalidOperationException("The server returned a different lobby.");
    }
}
