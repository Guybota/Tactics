using System;
using Tactics.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

// Scene handoff only. The journal, not this static value, owns durable recovery.
public static class OnlineMatchRuntime
{
    public sealed class Launch
    {
        public OnlineMatchSession Session;
        public int PlayerSlot;
        public string ContentVersion;
    }
    private static Launch pending;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Clear() => pending = null;
    public static Launch TakePending() { var value = pending; pending = null; return value; }
    public static void Enter(MatchLobbyReply lobby, OnlineMatchSession session)
    {
        if (lobby == null || session == null) throw new ArgumentNullException();
        lobby.Validate();
        if (!lobby.Success || lobby.Status == MatchLobbyStatus.Waiting
            || session.CaptureReconnectData().MatchId != lobby.MatchId || session.Revision < lobby.Revision)
            throw new ArgumentException("An active confirmed session is required.");
        pending = new Launch { Session = session, PlayerSlot = lobby.PlayerSlot, ContentVersion = lobby.ContentVersion };
        SceneManager.LoadScene(1);
    }
}
