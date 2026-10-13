using System;
using System.IO;
using System.Threading.Tasks;
using Tactics.Core;
using UnityEngine;

// Explicit entry points only: constructing this object never initializes services or polls.
public sealed class UgsMatchConnector
{
    private readonly UgsCloudModuleClient client;
    private readonly string recoveryDirectory;
    public UgsMatchTransport Transport { get; }

    public UgsMatchConnector(UgsCloudModuleClient client, string recoveryDirectory = null,
        string moduleName = UgsMatchTransport.DefaultModuleName)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        Transport = new UgsMatchTransport(client, moduleName);
        this.recoveryDirectory = recoveryDirectory ?? Path.Combine(Application.persistentDataPath, "OnlineMatches");
    }

    public Task<MatchLobbyReply> CreateAsync(MatchCreateRequest request) => Transport.CreateAsync(request);
    public Task<MatchLobbyReply> JoinAsync(MatchJoinRequest request) => Transport.JoinAsync(request);

    public async Task<OnlineMatchSession> OpenAsync(string matchId)
    {
        var lobby = await Transport.GetLobbyAsync(matchId);
        if (!lobby.Success) throw new InvalidOperationException(lobby.Error ?? "Could not open the online match.");
        if (lobby.Status == MatchLobbyStatus.Waiting)
            throw new InvalidOperationException("The match is waiting for the second player.");
        var journal = new OnlineSessionFileJournal(recoveryDirectory, Application.cloudProjectId,
            client.Environment, client.PlayerId, lobby.MatchId);
        var recovered = journal.Read();
        if (recovered == null)
            return new OnlineMatchSession(Transport, lobby.MatchId, lobby.Snapshot, lobby.Revision, journal);
        if (recovered.Revision > lobby.Revision)
            throw new InvalidOperationException("The server revision is older than the locally confirmed match. Recovery was preserved.");
        var session = OnlineMatchSession.Restore(Transport, recovered, journal);
        var refreshed = await session.RefreshAsync();
        if (!refreshed.Success) throw new InvalidOperationException(refreshed.Error);
        if (session.HasPendingCommand)
        {
            await session.RetryPendingAsync();
            if (session.HasPendingCommand)
                throw new InvalidOperationException("The previous action is still unconfirmed. Its recovery record was preserved.");
        }
        return session;
    }
}
