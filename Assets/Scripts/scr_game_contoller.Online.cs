using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Tactics.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class scr_game_contoller
{
    private OnlineMatchSession onlineSession;
    private int onlinePlayerSlot = -1;
    private bool onlineBusy;
    private float nextOnlinePoll;
    private string onlineMessage;
    private TMP_Text onlineStatus;
    [SerializeField] private OnlineMatchHud onlineHudView;
    private Button onlineRefresh, onlineRetry;
    private readonly List<Button> onlineEndTurnButtons = new List<Button>();
    public bool IsOnlineMatch => onlineSession != null;
    public int OnlinePlayerSlot => onlinePlayerSlot;
    public string OnlineError => onlineMessage;
    public bool CanControlOnlineTurn => IsOnlineMatch && !onlineBusy && !onlineSession.IsRequestRunning
        && !onlineSession.HasPendingCommand && !loading && status != "Over"
        && onlineSession.Snapshot().ActivePlayer == onlinePlayerSlot;

    public void InitializeOnlineMatch(OnlineMatchSession session, int playerSlot, string contentVersion = null)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        var state = session.Snapshot();
        if (playerSlot < 0 || playerSlot >= state.Players.Count) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        if (raceCatalog.Count == 0) BuildRaceCatalog();
        var races = state.Players.Select(p => raceCatalog.Values.SingleOrDefault(r => r.Id == p.RaceId)
            ?? throw new InvalidOperationException("Unknown online race.")).ToArray();
        var allRaces = raceCatalog.Values.Distinct().ToArray();
        var content = UnityMatchContent.Build(allRaces.Select(UnityMatchContent.Roster.From), out var prefabs);
        if (contentVersion != null)
        {
            var data = MatchContentData.FromContent(content, allRaces.ToDictionary(r => r.Id,
                r => UnityMatchAdapter.TypeId(r.Leader.GetComponent<Unit>())));
            string version;
            using (var hash = SHA256.Create())
                version = "sha256." + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data))))
                    .Replace("-", "").ToLowerInvariant();
            if (version != contentVersion) throw new InvalidOperationException("The server's content version requires a matching game build.");
        }
        foreach (var player in state.Players) foreach (var unit in player.Units)
            if (unit.IsAlive && !prefabs.ContainsKey(unit.TypeId)) throw new InvalidOperationException("Unknown online unit asset.");
        // Build the terrain and participants offscreen before replacing any local objects.
        var staging = new GameObject("Online Match");
        staging.SetActive(false);
        var tiles = new Tile[state.Width, state.Height];
        var participants = new List<Team>();
        try
        {
            for (int x = 0; x < state.Width; x++) for (int y = 0; y < state.Height; y++)
            {
                var cell = state.Cells[x, y];
                var prefab = cell.Terrain == TerrainKind.Forest ? forest_tile : cell.Terrain == TerrainKind.Mountain ? mountain_tile : grass_tile;
                var tile = Instantiate(prefab, new Vector3(x * 64, y * 64), Quaternion.identity, staging.transform).GetComponent<Tile>();
                if (cell.SupportsCastle && tile.castle == null) throw new InvalidOperationException("Missing online castle asset.");
                tile.position_x = x; tile.position_y = y; tile.movement_cost = cell.MovementCost;
                tiles[x, y] = tile;
            }
            for (int i = 0; i < state.Players.Count; i++)
            {
                var team = new GameObject("Player " + (i + 1)).AddComponent<Team>();
                team.transform.SetParent(staging.transform, false);
                team.team_name = "Player " + (i + 1); team.team_color = CustomMapUi.PlayerColor(i);
                team.raceClass = races[i]; team.isBot = false; participants.Add(team);
            }
        }
        catch { Destroy(staging); throw; }
        if (combatPresentation != null) combatPresentation.Cancel();
        if (botRunner != null) { botRunner.enabled = false; Destroy(botRunner); botRunner = null; }
        var oldTiles = gameMap.tiles;
        var oldUnits = teams.SelectMany(t => t.units).Where(u => u != null).ToArray();
        var oldRoot = loadedMatchRoot;
        gameMap.hover_tile = null; gameMap.selected_tile = null;
        teams.Clear(); teams.AddRange(participants);
        gameMap.Set(state.Width, state.Height, this); gameMap.tiles = tiles;
        foreach (var tile in tiles) tile.transform.SetParent(gameMap.transform, true);
        loadedMatchRoot = staging; staging.transform.SetParent(transform, true);
        foreach (var unit in oldUnits) { unit.gameObject.SetActive(false); Destroy(unit.gameObject); }
        foreach (var tile in oldTiles) if (tile != null) { tile.gameObject.SetActive(false); Destroy(tile.gameObject); }
        if (oldRoot != null) { oldRoot.SetActive(false); Destroy(oldRoot); }
        onlineSession = session; onlinePlayerSlot = playerSlot; localMatchSession = null; matchAdapter = null;
        map_width = state.Width; map_height = state.Height;
        MainMenu.build_mode = state.BuildMode; MainMenu.battle_royale_mode = state.BattleRoyaleMode;
        gameMap.stormWarmupRounds = state.StormWarmupRounds; gameMap.stormRoundsPerShrink = state.StormRoundsPerShrink;
        staging.SetActive(true); gameMap.Populate_adjacency_list();
        ApplyOnlineSnapshot(true);
        uiManager.Update_tile_info(null);
        BindOnlineHud(); nextOnlinePoll = Time.unscaledTime + 5;
    }

    public async void DispatchAction(MatchAction action)
    {
        if (!IsOnlineMatch) { TryExecuteAction(action, out _); return; }
        await ExecuteActionAsync(action);
    }
    private async void SpawnOnline(string option) => await gameMap.Handle_spawnAsync(option);

    public async Task<bool> ExecuteActionAsync(MatchAction action)
    {
        if (!IsOnlineMatch) return TryExecuteAction(action, out _);
        if (!CanControlOnlineTurn || action == null || action.Actor.Player != onlinePlayerSlot
            || status == "Menu" || status == "Saving" || status == "Loading" || IsCombatAnimating
            || teams.SelectMany(t => t.units).Any(u => u != null && u.IsMoving))
        { onlineMessage = "Wait for your turn and the current action to finish."; return false; }
        Unit actorVisual = null, targetVisual = null;
        float actorHealth = 0, targetHealth = 0;
        var attackPath = new List<Vector3>();
        int expectedRevision = MatchRevision;
        if (action.Kind == MatchActionKind.Attack && IsActionLegal(action))
        {
            actorVisual = teams[action.Actor.Player].Get_unit(action.Actor.Id);
            targetVisual = teams[action.TargetUnit.Player].Get_unit(action.TargetUnit.Id);
            actorHealth = actorVisual.hp; targetHealth = targetVisual.hp;
            foreach (var point in GetMovementPath(action.Actor, action.Destination))
                attackPath.Add(gameMap.GetTileAtPosition(point.X, point.Y).transform.position);
        }
        onlineBusy = true; onlineMessage = "Sending action…";
        // Preview positions are visual only. Discard them before awaiting confirmation.
        gameMap.ResetInteraction();
        try
        {
            var result = await onlineSession.SubmitAsync(action, MatchRevision);
            if (this == null) return result.Success;
            bool animateAttack = result.Success && result.Revision == expectedRevision + 1 && actorVisual != null && targetVisual != null;
            if (animateAttack)
            {
                if (combatPresentation == null) combatPresentation = gameObject.AddComponent<CombatPresentation>();
                combatPresentation.Prepare(this, actorVisual, targetVisual);
            }
            ApplyOnlineSnapshot(false);
            onlineMessage = result.Success ? null : onlineSession.HasPendingCommand
                ? "Action unconfirmed. Use Retry to recover it." : "Action refused. The confirmed board was restored.";
            if (result.Success) PlayActionSound(action.Kind);
            if (animateAttack)
            {
                var confirmed = onlineSession.Snapshot();
                var target = confirmed.FindUnit(action.TargetUnit); var actor = confirmed.FindUnit(action.Actor);
                int distance = Mathf.Abs(action.Destination.X - target.Position.X) + Mathf.Abs(action.Destination.Y - target.Position.Y);
                combatPresentation.Play(attackPath, gameMap.GetTileAtPosition(action.Destination.X, action.Destination.Y).transform.position,
                    target.IsAlive && distance <= target.Range, !target.IsAlive, !actor.IsAlive,
                    target.Health < targetHealth, actor.Health < actorHealth);
            }
            return result.Success;
        }
        catch (Exception)
        {
            if (this != null)
            {
                if (combatPresentation != null) combatPresentation.Cancel();
                onlineMessage = "Could not display the online action. Refresh or reopen the match.";
            }
            return false;
        }
        finally { onlineBusy = false; nextOnlinePoll = Time.unscaledTime + 5; }
    }

    public Task<bool> RefreshOnlineAsync() => ReadOnline(false);
    public Task<bool> RetryOnlineAsync() => ReadOnline(true);
    private async Task<bool> ReadOnline(bool retry)
    {
        if (!IsOnlineMatch || onlineBusy || onlineSession.IsRequestRunning || IsCombatAnimating) return false;
        if (retry && !onlineSession.HasPendingCommand) return false;
        onlineBusy = true;
        int oldRevision = MatchRevision;
        try
        {
            var result = retry ? await onlineSession.RetryPendingAsync() : await onlineSession.RefreshAsync();
            if (this == null) return result.Success;
            if (onlineSession.Revision != oldRevision || retry) ApplyOnlineSnapshot(false);
            onlineMessage = result.Success ? null : onlineSession.HasPendingCommand
                ? "Action unconfirmed. Use Retry to recover it." : "Connection unavailable. Use Refresh to try again.";
            nextOnlinePoll = Time.unscaledTime + (result.Success ? 5 : 30);
            return result.Success;
        }
        catch (Exception) { if (this != null) onlineMessage = "Could not refresh the board. Reopen the match to recover."; nextOnlinePoll = Time.unscaledTime + 30; return false; }
        finally { onlineBusy = false; }
    }

    private void ApplyOnlineSnapshot(bool center)
    {
        var state = onlineSession.Snapshot(); int previousTurn = turn;
        MatchAdapter.Apply(state); MatchRevision = onlineSession.Revision;
        // No local BeginTurn, income, defeat calculation or random-state import.
        gameMap.ResetInteraction(); uiManager.win_menu.SetActive(false);
        Update_player_info(); Set_castle_buttons(); uiManager.turn_number.text = "Turn\n" + CurrentRound;
        if (center || previousTurn != turn) CenterCameraOnActiveTeam();
        gameMap.Check_win();
    }

    private void UpdateOnlineMatch()
    {
        if (!IsOnlineMatch) return;
        if (onlineStatus != null)
            onlineStatus.text = onlineMessage ?? (onlineSession.HasPendingCommand ? "Action unconfirmed — Retry"
                : onlineBusy ? "Updating…" : status == "Over" ? "Match completed"
                : CanControlOnlineTurn ? "Online · Your turn (Player " + (onlinePlayerSlot + 1) + ")"
                : "Online · Waiting for Player " + (turn % teams.Count + 1));
        if (onlineRefresh != null) onlineRefresh.interactable = !onlineBusy && !onlineSession.IsRequestRunning && !IsCombatAnimating;
        if (onlineRetry != null) onlineRetry.interactable = !onlineBusy && onlineSession.HasPendingCommand;
        foreach (var button in onlineEndTurnButtons) if (button != null)
            button.interactable = CanControlOnlineTurn && !IsCombatAnimating && status != "Over";
        if (Time.unscaledTime >= nextOnlinePoll && !onlineBusy && !onlineSession.HasPendingCommand
            && !IsCombatAnimating && status != "Over" && status != "Menu" && !LandscapeOrientation.InputBlocked && Time.timeScale > 0)
        { nextOnlinePoll = Time.unscaledTime + 5; PollOnline(); }
    }
    private async void PollOnline() => await RefreshOnlineAsync();
    private void BindOnlineHud()
    {
        if (onlineHudView == null) throw new InvalidOperationException("The editable Online HUD is missing from this scene.");
        onlineHudView.Bind(this);
        onlineStatus = onlineHudView.Status;
        onlineRefresh = onlineHudView.Refresh;
        onlineRetry = onlineHudView.Retry;
        onlineEndTurnButtons.Clear();
        foreach (var root in new[] { uiManager.buttons, uiManager.main_menu })
            foreach (var button in root.GetComponentsInChildren<Button>(true))
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    if (button.onClick.GetPersistentMethodName(i) == "Finalize_turn") onlineEndTurnButtons.Add(button);
    }
    public void CopyOnlineInvitation()
    {
        if (!IsOnlineMatch) return;
        GUIUtility.systemCopyBuffer = onlineSession.CaptureReconnectData().MatchId;
        onlineMessage = "Invitation copied.";
    }
}
