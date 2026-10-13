using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;
using Tactics.Core;

public partial class scr_game_contoller : MonoBehaviour
{
    public GameObject grass_tile;
    public GameObject forest_tile;
    public GameObject mountain_tile;

    public GameObject game_camera;

    public UIManager uiManager;

    public string status;
    public int turn = -1;

    public List<Team> teams = new List<Team>();
    public Team team3;
    public Team team4;
    public Map gameMap;

    public LineRenderer line;
    public GameObject arrow;

    public int map_width = 10;
    public int map_height = 10;

    public bool menu_lock = false;
    public bool hover_toggle = true;
    private bool loading = false;

    AudioManager audioManager;

    [SerializeField] private RaceClass defaultRace;
    [SerializeField] private RaceCatalog contentCatalog;
    private readonly Dictionary<string, RaceClass> raceCatalog = new Dictionary<string, RaceClass>();
    private GameObject loadedMatchRoot;
    public int CurrentRound => TurnRules.Round(Mathf.Max(0, turn), teams.Count);
    private UnityMatchAdapter matchAdapter;
    private LocalMatchSession localMatchSession;
    private uint randomState = 0x6D2B79F5;
    public uint RandomState => randomState;
    public int MatchRevision { get; private set; }
    public bool IsBotTurn => !IsOnlineMatch && turn >= 0 && teams.Count > 0 && teams[turn % teams.Count].isBot;
    public bool CanAdvanceBot => !loading && status == "Select_tile" && IsBotTurn
        && !LandscapeOrientation.InputBlocked && Time.timeScale > 0;
    public bool CanPlanBot => CanAdvanceBot && !teams.SelectMany(t => t.units).Any(u => u != null && u.IsMoving);
    private BotRunner botRunner;
    private CombatPresentation combatPresentation;
    public bool IsCombatAnimating => combatPresentation != null && combatPresentation.IsPlaying;
    public bool IsCombatMoving => combatPresentation != null && combatPresentation.IsMoving;
    public BotPlanner CreateBotPlanner() => new BotPlanner(MatchAdapter.Engine);
    public List<GridPosition> GetMovementPath(UnitKey key, GridPosition destination)
        => MatchAdapter.Engine.Movement(GetStateSnapshot(), key).PathTo(destination);
    // Temporary bridge for local saves and legacy component edits. Remote sessions must
    // use confirmed snapshots and must never import state captured from the scene.
    private LocalMatchSession LocalSession
    {
        get
        {
            var legacyState = MatchAdapter.Capture();
            if (localMatchSession == null)
                localMatchSession = new LocalMatchSession(MatchAdapter.Engine, legacyState, MatchRevision);
            else localMatchSession.Restore(legacyState, MatchRevision);
            return localMatchSession;
        }
    }
    public MatchState GetStateSnapshot() => IsOnlineMatch ? onlineSession.Snapshot() : LocalSession.Snapshot();
    private UnityMatchAdapter MatchAdapter => matchAdapter ?? (matchAdapter = new UnityMatchAdapter(this, raceCatalog.Values));
    public UnitKey GetUnitKey(Unit unit) => MatchAdapter.Key(unit);
    public bool IsActionLegal(MatchAction action) => (!IsOnlineMatch || CanControlOnlineTurn && action != null && action.Actor.Player == onlinePlayerSlot)
        && MatchAdapter.Engine.Validate(GetStateSnapshot(), action).Success;
    public bool CanStartBuild(Unit unit, Tile destination)
        => MatchAdapter.Engine.ValidateBuildStart(GetStateSnapshot(), GetUnitKey(unit), new GridPosition(destination.position_x, destination.position_y)).Success;
    public HashSet<GridPosition> GetBuildTargets(Unit unit, Tile destination)
        => MatchAdapter.Engine.BuildTargets(GetStateSnapshot(), GetUnitKey(unit), new GridPosition(destination.position_x, destination.position_y));

    public bool TryExecuteAction(MatchAction action, out string error)
    {
        error = null;
        if (IsOnlineMatch) { error = "Use asynchronous submission for an online match."; return false; }
        if (loading || status == "Over" || status == "Menu" || status == "Saving" || status == "Loading")
        { error = "The match is not accepting actions."; return false; }
        if (action == null) { error = "Action is required."; return false; }
        if (IsCombatAnimating) { error = "Wait for the attack animation."; return false; }
        if (botRunner != null && botRunner.IsAnimating)
        { error = "Wait for the bot presentation."; return false; }
        if (action.Kind != MatchActionKind.EndTurn && teams.SelectMany(t => t.units).Any(u => u != null && u.IsMoving))
        { error = "Wait for the movement animation."; return false; }
        Unit attackActor = null, attackTarget = null;
        float actorHealth = 0, targetHealth = 0;
        var attackPath = new List<Vector3>();
        if (action.Kind == MatchActionKind.Attack && IsActionLegal(action))
        {
            attackActor = teams[action.Actor.Player].Get_unit(action.Actor.Id);
            attackTarget = teams[action.TargetUnit.Player].Get_unit(action.TargetUnit.Id);
            actorHealth = attackActor.hp; targetHealth = attackTarget.hp;
            var destination = gameMap.GetTileAtPosition(action.Destination.X, action.Destination.Y).transform.position;
            // Human movement previews have already reached the destination. Bots still need to walk there.
            if (Vector3.Distance(attackActor.transform.position, destination) > .1f)
                foreach (var point in GetMovementPath(action.Actor, action.Destination))
                {
                    var position = gameMap.GetTileAtPosition(point.X, point.Y).transform.position;
                    if (Vector3.Distance(position, attackActor.transform.position) > .1f) attackPath.Add(position);
                }
        }
        var result = LocalSession.Submit(action, MatchRevision);
        if (!result.Success) { error = result.Error; return false; }
        var next = result.State;
        if (attackActor != null && attackTarget != null)
        {
            if (combatPresentation == null) combatPresentation = gameObject.AddComponent<CombatPresentation>();
            combatPresentation.Prepare(this, attackActor, attackTarget);
        }
        MatchAdapter.Apply(next);
        randomState = next.RandomState;
        MatchRevision = result.Revision;
        gameMap.ResetInteraction();
        Update_player_info(); Set_castle_buttons();
        uiManager.turn_number.text = "Turn\n" + CurrentRound;
        if (action.Kind == MatchActionKind.EndTurn) CenterCameraOnActiveTeam();
        gameMap.Check_win();
        PlayActionSound(action.Kind);
        if (attackActor != null && attackTarget != null)
        {
            var target = next.FindUnit(action.TargetUnit);
            var actor = next.FindUnit(action.Actor);
            int distance = Mathf.Abs(action.Destination.X - target.Position.X) + Mathf.Abs(action.Destination.Y - target.Position.Y);
            combatPresentation.Play(attackPath, gameMap.GetTileAtPosition(action.Destination.X, action.Destination.Y).transform.position,
                target.IsAlive && distance <= target.Range, !target.IsAlive, !actor.IsAlive,
                target.Health < targetHealth, actor.Health < actorHealth);
        }
        return true;
    }

    private void PlayActionSound(MatchActionKind kind)
    {
        switch (kind)
        {
            case MatchActionKind.Attack:
                audioManager.PlaySFX(audioManager.attack);
                break;
            case MatchActionKind.Capture:
            case MatchActionKind.BuildCastle:
                audioManager.PlaySFX(audioManager.capture);
                break;
            case MatchActionKind.Recruit:
                audioManager.PlaySFX(audioManager.buy);
                break;
        }
    }

    private void Awake()
    {
        audioManager = GameObject.FindGameObjectWithTag("Audio").GetComponent<AudioManager>();
    }

    // Start is called before the first frame update
    void Start()
    {
        BuildRaceCatalog();
        map_height = Mathf.Clamp(MainMenu.map_height, 10, 50);
        map_width = Mathf.Clamp(MainMenu.map_width, 10, 50);
        Hide_load_menu();
        Hide_save_menu();
        uiManager.action_menu.SetActive(false);
        game_camera.GetComponent<WorldTouchInput>().Tapped += OnTouchTap;
        foreach (var button in uiManager.main_menu.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                if (button.onClick.GetPersistentMethodName(i) == "Finalize_turn")
                {
                    button.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
                    button.onClick.AddListener(EndTurnFromMenu);
                }
        gameObject.AddComponent<TouchMatchControls>().Initialize(this);
        var online = OnlineMatchRuntime.TakePending();
        if (online != null)
        {
            try { InitializeOnlineMatch(online.Session, online.PlayerSlot, online.ContentVersion); }
            catch (Exception) { Debug.LogWarning("Could not open the online board. Return to Online to retry."); SceneManager.LoadScene(0); }
            return;
        }
        var pending = MainMenu.TakePendingSave();
        if (pending != null)
        {
            if (!TryLoadSnapshot(pending, out string error))
            {
                Debug.LogWarning("Could not open the saved game: " + error);
                SceneManager.LoadScene(0);
                return;
            }
            botRunner = gameObject.AddComponent<BotRunner>(); botRunner.Initialize(this);
            return;
        }
        var customMap = MainMenu.SelectedCustomMap;
        if (customMap != null)
        {
            try { CustomMapValidator.ValidatePlayable(customMap); }
            catch (Exception exception) { Debug.LogWarning("Cannot start custom map: " + exception.Message); SceneManager.LoadScene(0); return; }
            map_width = customMap.width; map_height = customMap.height;
        }
        gameMap.Set(map_width, map_height, this);
        if (customMap == null) gameMap.GenerateMap(grass_tile, forest_tile, mountain_tile);
        else CustomMapTiles.Apply(gameMap, customMap, grass_tile, forest_tile, mountain_tile);

        var first = customMap?.StartFor(0); var second = customMap?.StartFor(1);
        SpawnLeader(teams[0], 0, first?.x ?? 0, first?.y ?? 2);
        SpawnLeader(teams[1], 1, second?.x ?? map_width - 1, second?.y ?? map_height - 3);
        if (customMap != null ? customMap.playerCount >= 3 : MainMenu.player3IsOn)
        {
            teams.Add(team3);
            var start = customMap?.StartFor(2); SpawnLeader(team3, 2, start?.x ?? 0, start?.y ?? map_height - 3);
        }
        if (customMap != null ? customMap.playerCount >= 4 : MainMenu.player4IsOn)
        {
            teams.Add(team4);
            var start = customMap?.StartFor(3); SpawnLeader(team4, 3, start?.x ?? map_width - 1, start?.y ?? 2);
        }
        if (customMap != null)
            for (int y = 0; y < customMap.height; y++) for (int x = 0; x < customMap.width; x++)
            {
                var cell = customMap.Cell(x, y);
                if (cell.castle) gameMap.tiles[x, y].castle.GetComponent<Castle>().Capture_castle(cell.castleOwner < 0 ? null : teams[cell.castleOwner]);
            }
        foreach (Team team in teams) if (MainMenu.build_mode) team.money = 4000;
        uiManager.Update_tile_info(null);
        gameMap.Populate_adjacency_list();
        turn = 0;
        randomState = (uint)UnityEngine.Random.Range(1, int.MaxValue);
        Initialize_turn();
        botRunner = gameObject.AddComponent<BotRunner>();
        botRunner.Initialize(this);
    }

    private void BuildRaceCatalog()
    {
        if (contentCatalog != null)
        {
            contentCatalog.Validate();
            foreach (var definition in contentCatalog.Races)
            {
                RaceClass race;
                if (defaultRace != null && defaultRace.Definition == definition) race = defaultRace;
                else
                {
                    var holder = new GameObject(definition.DisplayName + " Definition");
                    holder.transform.SetParent(transform, false);
                    race = holder.AddComponent<RaceClass>();
                }
                race.Bind(definition);
                raceCatalog.Add(race.className, race);
            }
        }
        else if (MainMenu.RaceDefinitions.Count > 0)
        {
            foreach (var definition in MainMenu.RaceDefinitions)
            {
                var holder = new GameObject(definition.Name + " Definition");
                holder.transform.SetParent(transform);
                var race = holder.AddComponent<RaceClass>();
                if (definition.Asset != null) race.Bind(definition.Asset);
                else
                {
                    race.className = definition.Name;
                    race.Leader = definition.Leader;
                    race.AvailableUnits = new List<GameObject>(definition.Units);
                }
                raceCatalog.Add(race.className, race);
            }
        }
        else
        {
            foreach (RaceClass race in FindObjectsByType<RaceClass>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (race.Leader != null && !raceCatalog.ContainsKey(race.className)) raceCatalog.Add(race.className, race);
        }
        if (defaultRace != null) raceCatalog[defaultRace.className] = defaultRace;
        if (raceCatalog.Count == 0) throw new InvalidOperationException("No race definitions are configured.");
    }

    private void SpawnLeader(Team team, int playerSlot, int x, int y)
    {
        RaceClass race;
        string name = MainMenu.SelectedRaceNames[playerSlot];
        if (name == null || !raceCatalog.TryGetValue(name, out race)) race = defaultRace != null ? defaultRace : raceCatalog.Values.First();
        team.raceClass = race;
        team.isBot = MainMenu.BotPlayers[playerSlot];
        team.has_lost = false;
        Tile tile = gameMap.GetTileAtPosition(x, y);
        if (tile.mountain != null)
        {
            tile.gameObject.SetActive(false);
            Destroy(tile.gameObject);
            tile = Instantiate(grass_tile, new Vector3(x * 64, y * 64), Quaternion.identity, gameMap.transform).GetComponent<Tile>();
            tile.position_x = x;
            tile.position_y = y;
            gameMap.tiles[x, y] = tile;
        }
        Unit unit = Instantiate(race.Leader, tile.transform.position, Quaternion.identity, gameMap.unitsRoot).GetComponent<Unit>();
        unit.Change_current_tile(tile);
        unit.Set_king(true);
        team.Add(unit);
    }

    // Update is called once per frame
    void Update()
    {
        UpdateOnlineMatch();
        if (status == "Over" || loading) return;
        if (Input.GetButtonUp("Cancel"))
        {
            HandleBack();
        }

        if (gameMap.cooldown <= 0)
        {
            if (status == "Select_tile" && !IsBotTurn && !IsCombatAnimating && (!IsOnlineMatch || CanControlOnlineTurn))
            {
                if (!WorldTouchInput.SuppressMouse)
                { if (Input.mousePresent) gameMap.Hover_mouse(hover_toggle); Select_tile(); }
            }
        }
        else
        {
            gameMap.cooldown -= Time.deltaTime;
        }
    }

    public void ProcessMenuOption(string option)
    {
        if (status == "Over" || loading || IsBotTurn || IsCombatAnimating || IsOnlineMatch && !CanControlOnlineTurn) return;
        // Handle the chosen menu option (e.g., "Wait" or "Attack")
        switch (option)
        {
            case "Wait":
                // Finalize unit's move (e.g., end its turn)
                gameMap.Finalize_move();
                break;
            case "Attack":
                // Implement attack logic (e.g., trigger combat)
                gameMap.Handle_attack();
                break;
            case "Select attack":
                gameMap.cooldown = .5f;
                status = "Select_tile";
                uiManager.action_menu.SetActive(false);
                gameMap.selecting_attack = true;
                break;
            case "Start build":
                // Finalize unit's move (e.g., end its turn)
                if (teams[turn % teams.Count].money >= 3500)
                    gameMap.Start_build();
                break;
            case "Capture":
                gameMap.Handle_capture();
                break;
            case "Cancel castle":
                gameMap.Cancel_castle();
                break;
            case "Cancel":
                gameMap.Cancel_move();
                break;
            default:
                break;
        }
    }

    public void ProcessSpawnOption(string option)
    {
        if (IsBotTurn || IsCombatAnimating || IsOnlineMatch && !CanControlOnlineTurn) return;
        if (IsOnlineMatch) SpawnOnline(option);
        else gameMap.Handle_spawn(option);
    }
    void Select_tile()
    {
        if (Input.GetMouseButtonUp(1) && !gameMap.selecting_attack && !gameMap.is_building && !WorldPointer.IsOverUI(Input.mousePosition))
        {
            gameMap.Clear_board();
            gameMap.path.Clear();
            gameMap.is_unit_selected = false;
            gameMap.selected_tile = null;
            gameMap.Clear_line();
        }
        if (Input.GetMouseButtonUp(0)) SelectWorldAt(Input.mousePosition);
    }

    public bool SelectWorldAt(Vector2 screenPosition)
    {
        if (loading || IsCombatAnimating || IsBotTurn || IsOnlineMatch && !CanControlOnlineTurn || status != "Select_tile" || gameMap.cooldown > 0 || !WorldPointer.TryGetTile(screenPosition, out Tile tile)) return false;
        gameMap.HoverAt(screenPosition);
        gameMap.Select_tile(tile, teams[turn % teams.Count]);
        return true;
    }
    private void OnTouchTap(Vector2 point) => SelectWorldAt(point);
    public void HandleBack()
    {
        if (UiBackAction.TryCloseTop()) return;
        if (loading || status == "Over") return;
        if (status == "Saving") { Hide_save_menu(); status = "Menu"; return; }
        if (status == "Loading") { Hide_load_menu(); status = "Menu"; return; }
        if (status == "Menu") { Back_button(); return; }
        if (IsBotTurn) { Menu_button(); return; }
        if (status == "Castle") { gameMap.Cancel_castle(); return; }
        if (gameMap.selected_tile != null)
        {
            if (gameMap.is_building || gameMap.selecting_attack || uiManager.action_menu.activeSelf || uiManager.attack_menu.activeSelf)
                gameMap.Cancel_move();
            else gameMap.ResetInteraction();
            return;
        }
        Menu_button();
    }

    public void Finalize_turn()
    {
        if (status == "Over" || IsBotTurn || IsCombatAnimating || loading || IsOnlineMatch && !CanControlOnlineTurn || status == "Menu" || status == "Saving" || status == "Loading") return;
        gameMap.Finalize_turn();
        DispatchAction(MatchAction.EndTurn(turn % teams.Count));
    }
    public void EndTurnFromMenu()
    {
        if (status != "Menu" || IsBotTurn || loading) return;
        Back_button(); Finalize_turn();
    }

    public void ResolveDefeats()
        => ResolveOutcome();

    public MatchState ResolveOutcome()
    {
        if (IsOnlineMatch) return onlineSession.Snapshot();
        var state = GetStateSnapshot();
        MatchEngine.ResolveDefeats(state);
        MatchAdapter.ApplyEliminations(state);
        localMatchSession.Restore(state, MatchRevision);
        return state;
    }

    public void Update_player_info()
    {
        uiManager.turn_image.GetComponent<UnityEngine.UI.Image>().color = teams[turn % teams.Count].team_color - new Color(0, 0, 0, 0.3f);
        uiManager.turn_name.GetComponent<TextMeshProUGUI>().text = teams[turn % teams.Count].team_name + (IsBotTurn ? " (Bot)" : "");
        uiManager.money_text.GetComponent<TextMeshProUGUI>().text = "Money: " + teams[turn % teams.Count].money.ToString() + "$";
    }

    public void Set_castle_buttons()
    {
        uiManager.SetCastleButtons(teams[turn % teams.Count].raceClass.AvailableUnits, this);
    }

    public void Initialize_turn()
    {
        if (IsOnlineMatch) return;
        var state = GetStateSnapshot();
        MatchEngine.ResolveDefeats(state);
        if (!state.IsOver)
        {
            if (state.Players[state.ActivePlayer].HasLost)
                state.Turn = TurnRules.NextActiveTurn(state.Turn, state.Players.Select(p => p.HasLost).ToArray());
            MatchEngine.BeginTurn(state);
        }
        MatchAdapter.Apply(state);
        MatchRevision++;
        localMatchSession.Restore(state, MatchRevision);
        gameMap.ResetInteraction();
        Set_castle_buttons(); Update_player_info();
        uiManager.turn_number.text = "Turn\n" + CurrentRound;
        CenterCameraOnActiveTeam(); gameMap.Check_win();
    }

    private void CenterCameraOnActiveTeam()
    {
        Vector3 center = Vector3.zero;
        int count = 0;
        foreach (Unit unit in teams[turn % teams.Count].units)
            if (unit != null && !unit.has_died) { center += unit.transform.position; count++; }
        if (count > 0) game_camera.transform.position = center / count - new Vector3(0, 0, 10);
    }

    public void Menu_button()
    {
        gameMap.ResetInteraction();
        gameMap.cooldown = 0.1f;
        Hide_buttons();
        Show_Menu();
    }

    public void Back_button()
    {
        gameMap.cooldown = 0.1f;
        Show_buttons();
        Hide_Menu();
    }

    public void Return_to_menu()
    {
        SceneManager.LoadScene(0);
    }

    public void Show_Menu() 
    {
        gameMap.Clear_board();
        uiManager.tile_info.SetActive(false);
        uiManager.main_menu.SetActive(true);
        menu_lock = false;
        uiManager.buttons.SetActive(false);
        status = "Menu";
        game_camera.GetComponent<scr_camera>().Lock_camera();
    }

    public void Hide_Menu()
    {
        uiManager.main_menu.SetActive(false);
        menu_lock = false;
        uiManager.buttons.SetActive(true);
        status = "Select_tile";
        game_camera.GetComponent<scr_camera>().Unlock_camera();
    }

    public void Show_buttons() 
    {
        uiManager.buttons.SetActive(true);
        menu_lock = false;
    }

    public void Hide_buttons()
    {
        uiManager.buttons.SetActive(false);
        menu_lock = true;
    }

    public void Show_save_menu() 
    {
        if (IsOnlineMatch) { onlineMessage = "Online progress is saved automatically."; return; }
        game_camera.GetComponent<scr_camera>().Lock_camera();
        uiManager.player_info.SetActive(false);
        uiManager.turn_info.SetActive(false);
        uiManager.main_menu.SetActive(false);
        Hide_buttons();
        status = "Saving";
        uiManager.saveMenu.warningText.SetActive(false);
        uiManager.saveMenu.lastInput = "";
        uiManager.saveMenu.SaveName.text = "";
        uiManager.saveMenu.gameObject.SetActive(true);
    }

    public void Hide_save_menu()
    {
        uiManager.saveMenu.gameObject.SetActive(false);
        uiManager.player_info.SetActive(true);
        uiManager.turn_info.SetActive(true);
        uiManager.main_menu.SetActive(true);
        Show_buttons();
    }

    public void Show_load_menu()
    {
        if (IsOnlineMatch) { onlineMessage = "Return to Online to resume a match."; return; }
        game_camera.GetComponent<scr_camera>().Lock_camera();
        uiManager.player_info.SetActive(false);
        uiManager.turn_info.SetActive(false);
        uiManager.main_menu.SetActive(false);
        Hide_buttons();
        status = "Loading";
        uiManager.loadMenu.RefreshSaves();
        uiManager.loadMenu.gameObject.SetActive(true);
    }

    public void Hide_load_menu()
    {
        uiManager.loadMenu.gameObject.SetActive(false);
        uiManager.player_info.SetActive(true);
        uiManager.turn_info.SetActive(true);
        uiManager.main_menu.SetActive(true);
        Show_buttons();
    }

    // Function to save game data to JSON
    public void SaveGameData()
    {
        if (IsOnlineMatch) return;
        string name = uiManager.saveMenu.SaveName.text;
        if (!SaveFileStore.IsValidName(name))
        {
            uiManager.saveMenu.warningText.SetActive(true);
            Debug.LogWarning("Use 1–64 letters or digits. The name saveManager is reserved.");
            return;
        }
        try
        {
            var repository = new SaveGameRepository(Application.persistentDataPath);
            if (repository.Exists(name) && name != uiManager.saveMenu.lastInput)
            {
                uiManager.saveMenu.lastInput = name;
                uiManager.saveMenu.warningText.SetActive(true);
                return;
            }
            var snapshot = new GameSave(this, name);
            SaveGameValidator.Validate(snapshot, raceCatalog);
            repository.Write(name, snapshot);
            Hide_save_menu();
            Hide_Menu();
        }
        catch (Exception exception)
        {
            uiManager.saveMenu.warningText.SetActive(true);
            Debug.LogWarning("Could not save the game: " + exception.Message);
        }
    }

    public void LoadSave(string saveName)
    {
        if (IsOnlineMatch) return;
        try
        {
            var snapshot = new SaveGameRepository(Application.persistentDataPath).Read(saveName);
            string error;
            if (!TryLoadSnapshot(snapshot, out error)) Debug.LogWarning("Could not load the game: " + error);
        }
        catch (Exception exception) { Debug.LogWarning("Could not load the game: " + exception.Message); }
    }

    public bool TryLoadSnapshot(GameSave snapshot, out string error)
    {
        error = null;
        if (IsOnlineMatch) { error = "A local save cannot replace an online match."; return false; }
        GameObject staging = null;
        bool committed = false;
        try
        {
            SaveGameValidator.Validate(snapshot, raceCatalog);
            if (combatPresentation != null) combatPresentation.Cancel();
            loading = true;
            var state = snapshot.controller;
            staging = new GameObject("Loaded Match");
            staging.SetActive(false);
            var stagedTiles = new Tile[state.gameMap.maxX, state.gameMap.maxY];
            var stagedTeams = new List<Team>();
            var owners = new Dictionary<string, Team>();
            foreach (var saved in state.gameMap.serializedTiles)
            {
                GameObject prefab = saved.tileName == "Forest" ? forest_tile : saved.tileName == "Mountain" ? mountain_tile : grass_tile;
                Tile tile = Instantiate(prefab, new Vector3(saved.position_x * 64, saved.position_y * 64, 0), Quaternion.identity, staging.transform).GetComponent<Tile>();
                tile.position_x = saved.position_x;
                tile.position_y = saved.position_y;
                tile.unit = null;
                if (saved.hasStorm) tile.Activate_storm();
                else if (saved.hasCastle) tile.Activate_castle();
                stagedTiles[saved.position_x, saved.position_y] = tile;
            }
            foreach (var saved in state.teams)
            {
                var holder = new GameObject(saved.teamName);
                holder.transform.SetParent(staging.transform);
                var team = holder.AddComponent<Team>();
                team.team_name = saved.teamName;
                team.team_color = new Color(saved.teamColorR, saved.teamColorG, saved.teamColorB);
                team.money = saved.money;
                team.has_lost = saved.hasLost;
                team.isBot = snapshot.schemaVersion >= 3 && saved.isBot;
                team.raceClass = SaveGameValidator.ResolveRace(saved, raceCatalog, snapshot.schemaVersion);
                stagedTeams.Add(team);
                owners.Add(team.team_name, team);
                foreach (var savedUnit in saved.units)
                {
                    if (savedUnit.hasDied || savedUnit.health <= 0) continue;
                    GameObject prefab = SaveGameValidator.ResolveUnit(savedUnit, team.raceClass, snapshot.schemaVersion);
                    Tile tile = stagedTiles[savedUnit.positionX, savedUnit.positionY];
                    Unit unit = Instantiate(prefab, tile.transform.position, Quaternion.identity, staging.transform).GetComponent<Unit>();
                    unit.id = savedUnit.id;
                    unit.RestoreLeaderStatus(savedUnit.isKing);
                    unit.max_health = savedUnit.maxHealth;
                    unit.hp = savedUnit.health;
                    unit.attack = savedUnit.attack;
                    unit.movement = savedUnit.movement;
                    unit.attack_range = savedUnit.attackRange;
                    unit.has_moved = savedUnit.hasMoved;
                    unit.has_died = false;
                    unit.Change_current_tile(tile);
                    team.Add(unit);
                    if (unit.has_moved) unit.Finalize_turn();
                    unit.UpdateHealth();
                }
                int maximumSavedId = saved.units.Count == 0 ? 0 : saved.units.Max(u => u.id);
                team.RestoreNextUnitId(Math.Max(saved.nextUnitId, checked(maximumSavedId + 1)));
            }
            foreach (var saved in state.gameMap.serializedTiles)
                if (saved.hasCastle && !string.IsNullOrEmpty(saved.castleTeam))
                    stagedTiles[saved.position_x, saved.position_y].castle.GetComponent<Castle>().Capture_castle(owners[saved.castleTeam]);

            // Everything is reconstructed before replacing the live match.
            var oldTiles = gameMap.tiles;
            var oldUnits = teams.SelectMany(t => t.units).Where(u => u != null).ToArray();
            var oldRoot = loadedMatchRoot;
            gameMap.Set(state.gameMap.maxX, state.gameMap.maxY, this);
            gameMap.tiles = stagedTiles;
            teams.Clear();
            teams.AddRange(stagedTeams);
            turn = state.turn;
            randomState = state.randomState == 0 ? 0x6D2B79F5 : state.randomState;
            if (snapshot.schemaVersion >= 2)
            {
                gameMap.stormWarmupRounds = state.stormWarmupRounds;
                gameMap.stormRoundsPerShrink = state.stormRoundsPerShrink;
            }
            MainMenu.build_mode = state.build_mode;
            MainMenu.battle_royale_mode = state.battle_royale_mode;
            loadedMatchRoot = staging;
            staging.transform.SetParent(transform, true);
            foreach (Unit unit in stagedTeams.SelectMany(t => t.units)) unit.transform.SetParent(gameMap.unitsRoot, true);
            foreach (Tile tile in stagedTiles) tile.transform.SetParent(gameMap.transform, true);
            gameMap.Populate_adjacency_list();
            foreach (Unit old in oldUnits) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
            foreach (Tile old in oldTiles) if (old != null) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
            if (oldRoot != null) { oldRoot.SetActive(false); Destroy(oldRoot); }
            committed = true;
            MatchRevision++;
            localMatchSession = null;
            staging.SetActive(true);
            ResolveDefeats();
            if (teams[turn % teams.Count].has_lost && teams.Count(t => !t.has_lost) > 1)
            {
                int next = TurnRules.NextActiveTurn(turn, teams.Select(t => t.has_lost).ToArray());
                if (next >= 0) turn = next;
            }
            uiManager.win_menu.SetActive(false);
            uiManager.loadMenu.gameObject.SetActive(false);
            uiManager.saveMenu.gameObject.SetActive(false);
            gameMap.ResetInteraction();
            Update_player_info();
            Set_castle_buttons();
            CenterCameraOnActiveTeam();
            gameMap.Check_win();
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            if (!committed && staging != null) Destroy(staging);
            return false;
        }
        finally { loading = false; }
    }
}
