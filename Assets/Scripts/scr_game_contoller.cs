using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;
using Tactics.Core;

public class scr_game_contoller : MonoBehaviour
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
    private bool menu_toggle;
    public bool hover_toggle = true;
    private bool loading = false;

    AudioManager audioManager;

    [SerializeField] private RaceClass defaultRace;
    private readonly Dictionary<string, RaceClass> raceCatalog = new Dictionary<string, RaceClass>();
    private GameObject loadedMatchRoot;
    private int lastStormRound = -1;
    public int CurrentRound => TurnRules.Round(Mathf.Max(0, turn), teams.Count);

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
        gameMap.Set(map_width, map_height, this);
        gameMap.GenerateMap(grass_tile, forest_tile, mountain_tile);

        SpawnLeader(teams[0], 0, 0, 2);
        SpawnLeader(teams[1], 1, map_width - 1, map_height - 3);
        if (MainMenu.player3IsOn)
        {
            teams.Add(team3);
            SpawnLeader(team3, 2, 0, map_height - 3);
        }
        if (MainMenu.player4IsOn)
        {
            teams.Add(team4);
            SpawnLeader(team4, 3, map_width - 1, 2);
        }
        foreach (Team team in teams) if (MainMenu.build_mode) team.money = 4000;
        uiManager.Update_tile_info(null);
        gameMap.Populate_adjacency_list();
        turn = 0;
        Initialize_turn();
    }

    private void BuildRaceCatalog()
    {
        if (MainMenu.RaceDefinitions.Count > 0)
        {
            foreach (var definition in MainMenu.RaceDefinitions)
            {
                var holder = new GameObject(definition.Name + " Definition");
                holder.transform.SetParent(transform);
                var race = holder.AddComponent<RaceClass>();
                race.className = definition.Name;
                race.Leader = definition.Leader;
                race.AvailableUnits = new List<GameObject>(definition.Units);
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
        if (status == "Over" || loading) return;
        if (Input.GetButtonUp("Cancel") && !menu_lock)
        {
            switch(menu_toggle)
            {
                case true:
                    uiManager.player_info.SetActive(true);
                    Hide_Menu();
                    break;
                case false:
                    uiManager.player_info.SetActive(true);
                    Menu_button();
                    break;
            }
        }

        if (gameMap.cooldown <= 0)
        {
            if (status == "Select_tile")
            {
                gameMap.Hover_mouse(hover_toggle);
                Select_tile();
            }
        }
        else
        {
            gameMap.cooldown -= Time.deltaTime;
        }
    }

    public void ProcessMenuOption(string option)
    {
        if (status == "Over" || loading) return;
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
        if (gameMap.Handle_spawn(option)) audioManager.PlaySFX(audioManager.buy);
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
        if (loading || status != "Select_tile" || gameMap.cooldown > 0 || !WorldPointer.TryGetTile(screenPosition, out Tile tile)) return false;
        gameMap.Select_tile(tile, teams[turn % teams.Count]);
        return true;
    }

    public void Finalize_turn()
    {
        if (status == "Over") return;
        gameMap.Finalize_turn();
        ResolveDefeats();
        gameMap.Check_win();
        if (status == "Over") return;
        int next = TurnRules.NextActiveTurn(turn, teams.Select(t => t.has_lost).ToArray());
        if (next < 0) return;
        turn = next;
        Initialize_turn();
    }

    public void ResolveDefeats()
    {
        foreach (Team team in teams)
        {
            if (!team.Check_loss()) continue;
            foreach (Unit unit in team.units) if (unit != null && !unit.has_died) unit.Eliminate();
            gameMap.ReleaseCastles(team);
        }
    }

    public void Update_player_info()
    {
        uiManager.turn_image.GetComponent<UnityEngine.UI.Image>().color = teams[turn % teams.Count].team_color - new Color(0, 0, 0, 0.3f);
        uiManager.turn_name.GetComponent<TextMeshProUGUI>().text = teams[turn % teams.Count].team_name;
        uiManager.money_text.GetComponent<TextMeshProUGUI>().text = "Money: " + teams[turn % teams.Count].money.ToString() + "$";
    }

    public void Set_castle_buttons()
    {
        uiManager.SetCastleButtons(teams[turn % teams.Count].raceClass.AvailableUnits, this);
    }

    public void Initialize_turn()
    {
        // Storm damage may eliminate consecutive participants; advance without recursion.
        for (int attempt = 0; attempt < teams.Count; attempt++)
        {
            ResolveDefeats();
            gameMap.Check_win();
            if (status == "Over") return;
            Team active = teams[turn % teams.Count];
            if (active.has_lost)
            {
                turn = TurnRules.NextActiveTurn(turn, teams.Select(t => t.has_lost).ToArray());
                continue;
            }
            gameMap.ResetInteraction();
            Set_castle_buttons();
            int round = CurrentRound;
            uiManager.turn_number.text = "Turn\n" + round;
            if (MainMenu.battle_royale_mode)
            {
                if (lastStormRound != round)
                {
                    gameMap.UpdateStormRadius();
                    lastStormRound = round;
                }
                foreach (Unit unit in active.units)
                    if (unit != null && !unit.has_died && unit.current_tile != null && unit.current_tile.has_storm)
                        unit.Take_damage(2 + round * 0.25f);
                ResolveDefeats();
                gameMap.Check_win();
                if (status == "Over") return;
                if (active.has_lost)
                {
                    turn = TurnRules.NextActiveTurn(turn, teams.Select(t => t.has_lost).ToArray());
                    continue;
                }
            }
            active.money += 500;
            foreach (Tile tile in gameMap.tiles)
                if (tile.has_castle && tile.castle.GetComponent<Castle>().current_team == active) active.money += 500;
            foreach (Unit unit in active.units)
            {
                if (unit == null || unit.has_died) continue;
                unit.Initialize_turn();
                if (unit.current_tile != null && unit.current_tile.has_castle && unit.current_tile.castle.GetComponent<Castle>().current_team == active)
                    unit.Heal(unit.max_health * 0.3f);
            }
            Update_player_info();
            CenterCameraOnActiveTeam();
            return;
        }
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
        menu_toggle = true;
        menu_lock = false;
        uiManager.buttons.SetActive(false);
        status = "Menu";
        game_camera.GetComponent<scr_camera>().Lock_camera();
    }

    public void Hide_Menu()
    {
        uiManager.main_menu.SetActive(false);
        menu_toggle = false;
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
        GameObject staging = null;
        bool committed = false;
        try
        {
            SaveGameValidator.Validate(snapshot, raceCatalog);
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
                team.raceClass = raceCatalog[saved.raceClass];
                stagedTeams.Add(team);
                owners.Add(team.team_name, team);
                foreach (var savedUnit in saved.units)
                {
                    if (savedUnit.hasDied || savedUnit.health <= 0) continue;
                    GameObject prefab = savedUnit.isKing ? team.raceClass.Leader : team.raceClass.AvailableUnits.Find(p => p.GetComponent<Unit>().unit_name == savedUnit.unitName);
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
            MainMenu.build_mode = state.build_mode;
            MainMenu.battle_royale_mode = state.battle_royale_mode;
            lastStormRound = CurrentRound;
            loadedMatchRoot = staging;
            staging.transform.SetParent(transform, true);
            foreach (Unit unit in stagedTeams.SelectMany(t => t.units)) unit.transform.SetParent(gameMap.unitsRoot, true);
            foreach (Tile tile in stagedTiles) tile.transform.SetParent(gameMap.transform, true);
            gameMap.Populate_adjacency_list();
            foreach (Unit old in oldUnits) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
            foreach (Tile old in oldTiles) if (old != null) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
            if (oldRoot != null) { oldRoot.SetActive(false); Destroy(oldRoot); }
            committed = true;
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
            menu_toggle = false;
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
