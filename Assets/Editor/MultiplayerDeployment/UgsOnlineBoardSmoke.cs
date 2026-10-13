using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Tactics.Core;
using UnityEditor;
using UnityEngine;

// Explicit test of the real SDK -> menu handoff -> authored board -> server action path.
// Reflection is necessary because this Editor assembly cannot reference Assembly-CSharp.
public static class UgsOnlineBoardSmoke
{
    private const string StatusKey = "Tactics.UgsOnlineBoardSmoke";
    public static string Status => SessionState.GetString(StatusKey, "idle");
    public static void Begin(string matchId)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode for the live board check.");
        Set(new { phase = "running" }); Run(matchId);
    }
    public static void CompleteMenuRoundtrip()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode for the live menu check.");
        Set(new { phase = "running" }); RunMenu();
    }
    private static async void RunMenu()
    {
        string stage = "resume-menu";
        try
        {
            var menuType = Type.GetType("MainMenu, Assembly-CSharp", true);
            var mainMenu = UnityEngine.Object.FindFirstObjectByType(menuType);
            menuType.GetMethod("PressOnline").Invoke(mainMenu, null);
            var menu = menuType.GetProperty("OnlineMenu").GetValue(mainMenu);
            await (Task)menu.GetType().GetMethod("ResumeAsync").Invoke(menu, null);
            var lobby = (MatchLobbyReply)menu.GetType().GetProperty("CurrentLobby").GetValue(menu);
            Require(lobby != null && lobby.Success && lobby.Status == MatchLobbyStatus.Waiting, "Resume the waiting development test invitation first.");
            stage = "guest-join";
            await UgsUnityClientSmoke.JoinGuestAsync(lobby.MatchId);
            await (Task)menu.GetType().GetMethod("RefreshLobbyAsync").Invoke(menu, null);
            lobby = (MatchLobbyReply)menu.GetType().GetProperty("CurrentLobby").GetValue(menu);
            Require(lobby.Status == MatchLobbyStatus.Active, "Menu did not observe the guest.");
            stage = "menu-open";
            await (Task)menu.GetType().GetMethod("OpenBoardAsync").Invoke(menu, null);
            var controllerType = Type.GetType("scr_game_contoller, Assembly-CSharp", true);
            Component controller = null;
            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(100);
                controller = UnityEngine.Object.FindFirstObjectByType(controllerType) as Component;
                if (controller != null && (bool)controllerType.GetProperty("IsOnlineMatch").GetValue(controller)) break;
            }
            Require(controller != null && (bool)controllerType.GetProperty("IsOnlineMatch").GetValue(controller), "Menu did not open the confirmed online board.");
            stage = "menu-board-action";
            var action = (Task<bool>)controllerType.GetMethod("ExecuteActionAsync").Invoke(controller, new object[] { MatchAction.EndTurn(lobby.PlayerSlot) });
            Require(await action, "The menu's session did not submit the board action.");
            var remote = await new UgsMatchTransport(new UgsCloudModuleClient("development")).ReadAsync(lobby.MatchId);
            Require(remote.Success && remote.Snapshot.Turn == 1 && remote.Revision == lobby.Revision + 1, "Menu board action was not persisted.");
            Set(new { phase = "passed", environment = "development", matchId = lobby.MatchId,
                menuResume = true, guestJoin = true, menuOpen = true, boardAction = true, revision = remote.Revision, turn = remote.Snapshot.Turn });
        }
        catch (Exception error) { Set(new { phase = "failed", stage, errorType = error.GetType().Name }); }
    }
    private static async void Run(string matchId)
    {
        string stage = "open";
        try
        {
            var client = new UgsCloudModuleClient("development");
            var connector = new UgsMatchConnector(client);
            var lobby = await connector.Transport.GetLobbyAsync(matchId);
            var session = await connector.OpenAsync(matchId);
            Require(lobby.Success && lobby.Status == MatchLobbyStatus.Active, "A development active test match is required.");
            stage = "scene-handoff";
            var runtime = Type.GetType("OnlineMatchRuntime, Assembly-CSharp", true);
            runtime.GetMethod("Enter").Invoke(null, new object[] { lobby, session });
            var controllerType = Type.GetType("scr_game_contoller, Assembly-CSharp", true);
            Component controller = null;
            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(100);
                controller = UnityEngine.Object.FindFirstObjectByType(controllerType) as Component;
                if (controller != null && (bool)controllerType.GetProperty("IsOnlineMatch").GetValue(controller)) break;
            }
            Require(controller != null && (bool)controllerType.GetProperty("IsOnlineMatch").GetValue(controller), "Online scene did not initialize.");
            stage = "confirmed-projection";
            var projected = (MatchState)controllerType.GetMethod("GetStateSnapshot").Invoke(controller, null);
            Require(projected.Turn == session.Snapshot().Turn && projected.Players.Select(p => p.Money).SequenceEqual(session.Snapshot().Players.Select(p => p.Money)), "Projection changed server income or turn.");
            Require((int)controllerType.GetProperty("MatchRevision").GetValue(controller) == session.Revision, "Projection changed revision.");
            stage = "action";
            // Only advance a test match if this authenticated participant currently owns the turn.
            int before = session.Revision;
            bool acted = projected.ActivePlayer == lobby.PlayerSlot;
            if (acted)
            {
                var result = (Task<bool>)controllerType.GetMethod("ExecuteActionAsync").Invoke(controller, new object[] { MatchAction.EndTurn(lobby.PlayerSlot) });
                Require(await result, "The authored board could not submit an online turn.");
                var remote = await connector.Transport.ReadAsync(matchId);
                Require(remote.Success && remote.Revision == before + 1 && remote.Snapshot.Turn == projected.Turn + 1, "Server did not persist the board's turn.");
                var refused = (Task<bool>)controllerType.GetMethod("ExecuteActionAsync").Invoke(controller, new object[] { MatchAction.EndTurn(remote.Snapshot.Turn % remote.Snapshot.Players.Count) });
                Require(!await refused && session.Revision == remote.Revision, "Opponent's turn was not blocked.");
            }
            Set(new { phase = "passed", environment = "development", matchId, sceneHandoff = true,
                confirmedProjection = true, actionSubmitted = acted, revision = session.Revision, turn = session.Snapshot().Turn });
        }
        catch (Exception error) { Set(new { phase = "failed", stage, errorType = error.GetType().Name }); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Set(object value) => SessionState.SetString(StatusKey, JsonConvert.SerializeObject(value));
}
