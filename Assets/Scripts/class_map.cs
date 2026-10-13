using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Tactics.Core;

public class Map : MonoBehaviour
{
    public Tile[,] tiles = new Tile[50, 50];
    public int max_x;
    public int max_y;
    public List<Transform> points = new List<Transform>();
    public List<Tile> path = new List<Tile>();
    public List<Team> teams = new List<Team>();
    public Transform unitsRoot;
    public GameObject selected_tile;
    public GameObject hover_tile;

    public UIManager uiManager;
    public SpriteRenderer bg;
    public SpriteRenderer table;

    public bool is_unit_selected;
    public bool selecting_attack;
    public bool is_building = false;
    public LineRenderer line;
    public LineRenderer arrow;
    public MovementPathRenderer movementPath;
    public scr_game_contoller g_controller;
    public scr_camera game_camera;
    private Team current_team;
    private MovementResult selectedMovement;
    private Unit selectedRenderUnit;
    private SpriteRenderer[] raisedRenderers;
    private int[] originalSortingLayers, originalSortingOrders;
    public float cooldown = 0;
    // Runtime settings: legacy scene serialization must not replace the defaults with zero.
    public int stormWarmupRounds { get; set; } = 1;
    public int stormRoundsPerShrink { get; set; } = 1;

    AudioManager audioManager;
    private MapCreator mapCreator;

    private void Awake()
    {
        audioManager = GameObject.FindGameObjectWithTag("Audio").GetComponent<AudioManager>();
    }

    private void LateUpdate() => UpdateSelectedUnitSorting();
    private void OnDisable() => RestoreSelectedUnitSorting();

    private void RestoreSelectedUnitSorting()
    {
        if (raisedRenderers != null)
            for (int i = 0; i < raisedRenderers.Length; i++)
                if (raisedRenderers[i] != null)
                {
                    raisedRenderers[i].sortingLayerID = originalSortingLayers[i];
                    raisedRenderers[i].sortingOrder = originalSortingOrders[i];
                }
        selectedRenderUnit = null;
        raisedRenderers = null;
    }

    private void UpdateSelectedUnitSorting()
    {
        Unit selected = selected_tile != null && selected_tile.GetComponent<Tile>().unit != null
            ? selected_tile.GetComponent<Tile>().unit.GetComponent<Unit>() : null;
        if (selected == selectedRenderUnit) return;
        RestoreSelectedUnitSorting();
        if (selected == null || selected.has_died) return;
        var template = movementPath != null ? movementPath.segmentTemplate : null;
        int pathOrder = template != null ? template.sortingOrder : line != null ? line.sortingOrder : 30;
        int pathLayer = template != null ? template.sortingLayerID : line != null ? line.sortingLayerID : 0;
        raisedRenderers = new[] { selected.sprite, selected.color };
        originalSortingLayers = new int[2]; originalSortingOrders = new int[2];
        int minimum = int.MaxValue;
        for (int i = 0; i < raisedRenderers.Length; i++)
        {
            var renderer = raisedRenderers[i];
            if (renderer == null) continue;
            originalSortingLayers[i] = renderer.sortingLayerID;
            originalSortingOrders[i] = renderer.sortingOrder;
            minimum = Mathf.Min(minimum, renderer.sortingOrder);
        }
        int offset = minimum == int.MaxValue ? 0 : Mathf.Max(0, pathOrder + 1 - minimum);
        foreach (var renderer in raisedRenderers)
            if (renderer != null)
            {
                renderer.sortingLayerID = pathLayer;
                renderer.sortingOrder += offset;
            }
        selectedRenderUnit = selected;
    }

    

    public void Set(int max_x, int max_y, scr_game_contoller g_controller)
    {
        if (unitsRoot == null)
        {
            unitsRoot = new GameObject("Units").transform;
            unitsRoot.SetParent(transform, false);
        }
        this.max_x = max_x;
        this.max_y = max_y;
        tiles = new Tile[max_x, max_y];
        line = g_controller.line;
        arrow = g_controller.arrow.GetComponent<LineRenderer>();
        if (movementPath == null) movementPath = line.GetComponent<MovementPathRenderer>();
        line.enabled = false;
        arrow.enabled = false;
        game_camera = g_controller.game_camera.GetComponent<scr_camera>();
        game_camera.SetMax(max_x * 64, max_y * 64);
        game_camera.maxZoom = (Math.Max(max_y, max_x) / 10) * 500;

        teams = g_controller.teams;

        uiManager = g_controller.uiManager;
        this.g_controller = g_controller;
        SetBg();
    }

    public void SetCreate(int max_x, int max_y, MapCreator mapCreator)
    {
        this.max_x = max_x;
        this.max_y = max_y;
        tiles = new Tile[max_x, max_y];
        game_camera = mapCreator.game_camera.GetComponent<scr_camera>();
        game_camera.SetMax(max_x * 64, max_y * 64);
        game_camera.maxZoom = (Math.Max(max_y, max_x) / 10) * 500;

        this.mapCreator = mapCreator;

        for (int i = 0; i < max_x; i++)
        {
            for (int j = 0; j < max_y; j++)
            {
                GameObject tilePrefab = mapCreator.grass_tile;

                // Instantiate and initialize tile
                GameObject tileGO = Instantiate(tilePrefab, new Vector3(i * 64, j * 64), Quaternion.identity, transform);
                Tile tile = tileGO.GetComponent<Tile>();
                tile.position_x = i;
                tile.position_y = j;
                tile.unit = null;

                // Store tile reference in the map
                tiles[i, j] = tile;
            }
        }

        SetBg();
    }

    public void SetBg()
    {
        bg.size = new Vector2(max_x * 64 + 150, max_y * 64 + 150);
        bg.transform.position = new Vector3((max_x - 1) * 32, (max_y - 1) * 32 - 20, 1);

        table.transform.position = new Vector3((max_x - 1) * 32, (max_y - 1) * 32 - 50, 1);
        table.size = new Vector2((max_x + 5) * 64 + 350, max_y * 64 + 350);

    }

    public void GenerateMap(GameObject grassTile, GameObject forestTile, GameObject mountainTile)
        => GenerateMap(grassTile, forestTile, mountainTile, new System.Random());

    public void GenerateMap(GameObject grassTile, GameObject forestTile, GameObject mountainTile, int seed)
        => GenerateMap(grassTile, forestTile, mountainTile, new System.Random(seed));

    private void GenerateMap(GameObject grassTile, GameObject forestTile, GameObject mountainTile, System.Random random)
    {
        var layout = GeneratedMapLayout.Create(max_x, max_y, MainMenu.build_mode, random);

        for (int i = 0; i < max_x; i++)
        {
            for (int j = 0; j < max_y; j++)
            {
                GameObject tilePrefab = grassTile;

                // Randomly assign tile types
                if (layout[i, j].Terrain == TerrainKind.Mountain)
                {
                    tilePrefab = mountainTile;
                }
                else if (layout[i, j].Terrain == TerrainKind.Forest)
                {
                    tilePrefab = forestTile;
                }

                // Instantiate and initialize tile
                GameObject tileGO = Instantiate(tilePrefab, new Vector3(i * 64, j * 64), Quaternion.identity, transform);
                Tile tile = tileGO.GetComponent<Tile>();
                tile.position_x = i;
                tile.position_y = j;
                tile.unit = null;

                if (layout[i, j].HasCastle) tile.Activate_castle();

                // Store tile reference in the map
                tiles[i, j] = tile;
            }
        }
    }

    public Tile GetTileAtPosition(int x, int y)
    {
        if (x >= 0 && x < max_x && y >= 0 && y < max_y)
            return tiles[x, y];
        else
            return null;
    }

    public void Display_action_menu(Tile tile)
    {
        if (hover_tile != null) hover_tile.GetComponent<Tile>().Update_hover_overlay("off");
        g_controller.menu_lock = true;

        uiManager.Update_tile_info(null);

        game_camera.GetComponent<scr_camera>().Lock_camera();
        Clear_board();
        uiManager.action_menu.SetActive(true);
        bool can_attack = false;
        bool can_capture = false;
        if (path.Count > 0 && (path[^1].position_x != Get_selected_tile().position_x || path[^1].position_y != Get_selected_tile().position_y))
        {
            path[^1].selected = true;
            can_attack = Find_attack_range(path[^1], Get_selected_unit().attack_range, true);
            if (path[^1].has_castle && (path[^1].castle.GetComponent<Castle>().current_team == null || path[^1].castle.GetComponent<Castle>().current_team.team_name != current_team.team_name))
                can_capture = true;
        }
        else
        {
            Get_selected_tile().selected =  true;
            can_attack = Find_attack_range(Get_selected_tile(), Get_selected_unit().attack_range, true);
            if (Get_selected_tile().has_castle &&(Get_selected_tile().castle.GetComponent<Castle>().current_team == null || Get_selected_tile().castle.GetComponent<Castle>().current_team.team_name != current_team.team_name))
                can_capture = true;
        }
        uiManager.action_menu.GetComponent<scr_action_menu>().Setup(can_attack, can_capture, Get_selected_unit().is_king && MainMenu.build_mode);

        uiManager.player_info.SetActive(false);
        uiManager.turn_info.SetActive(false);
        uiManager.main_menu.SetActive(false);
        uiManager.buttons.SetActive(false);
    }

    public void Display_attack_menu(Unit unit1, Unit unit2)
    {
        if (hover_tile != null) hover_tile.GetComponent<Tile>().Update_hover_overlay("off");
        g_controller.menu_lock = true;

        uiManager.tile_info.SetActive(false);

        game_camera.GetComponent<scr_camera>().Lock_camera();
        Clear_board();
        uiManager.action_menu.SetActive(false);
        uiManager.attack_menu.SetActive(true);
        if (path.Count > 0 && (path[^1].position_x != Get_selected_tile().position_x || path[^1].position_y != Get_selected_tile().position_y))
        {
            uiManager.attack_menu.GetComponent<scr_attack_menu>().SetupFromTile(unit1, unit2, path[^1].Distance_to_tile(unit1.target_attack_tile), path[^1]);
        }
        else
        {
            uiManager.attack_menu.GetComponent<scr_attack_menu>().SetupFromTile(unit1, unit2, Get_selected_tile().Distance_to_tile(unit1.target_attack_tile), Get_selected_tile());
        }
        
        uiManager.player_info.SetActive(false);
        uiManager.buttons.SetActive(false);
        uiManager.turn_info.SetActive(false);
        uiManager.main_menu.SetActive(false);
    }

    public void Finalize_move()
    {
        Unit unit = Get_selected_unit();
        if (unit.IsMoving) return;
        Tile destination = path.Count > 0 ? path[^1] : unit.current_tile;
        g_controller.DispatchAction(new MatchAction(MatchActionKind.Wait, g_controller.GetUnitKey(unit), PositionOf(destination)));
    }

    public void ResetInteraction()
    {
        // Roll back unconfirmed animation before changing turn or discarding selection.
        if (selected_tile != null && Get_selected_tile().unit != null)
        {
            Unit selected = Get_selected_unit();
            selected.StopMovement();
            if (selected.current_tile != null) selected.transform.position = selected.current_tile.transform.position;
            selected.target_attack_tile = null;
        }
        cooldown = 0.2f;
        selecting_attack = false;
        is_building = false;
        is_unit_selected = false;
        selected_tile = null;
        selectedMovement = null;
        if (hover_tile != null) hover_tile.GetComponent<Tile>().Update_hover_overlay("off");
        hover_tile = null;
        Clear_board();
        Clear_line();
        path.Clear();
        uiManager.action_menu.SetActive(false);
        uiManager.attack_menu.SetActive(false);
        uiManager.spawnMenu.gameObject.SetActive(false);
        uiManager.player_info.SetActive(true);
        uiManager.turn_info.SetActive(true);
        uiManager.main_menu.SetActive(false);
        uiManager.buttons.SetActive(true);
        g_controller.menu_lock = false;
        g_controller.hover_toggle = true;
        g_controller.status = "Select_tile";
        game_camera.Clear_follow();
        game_camera.Unlock_camera();
    }

    public void Cancel_move()
    {
        Tile origin = Get_selected_tile();
        Unit unit = origin.unit.GetComponent<Unit>();
        unit.StopMovement();
        unit.transform.position = origin.transform.position;
        unit.target_attack_tile = null;
        ResetInteraction();
        Select_tile(origin, current_team);
    }

    public void Handle_attack()
    {
        Unit attacker = Get_selected_unit();
        if (attacker.IsMoving || attacker.has_died || attacker.target_attack_tile == null || attacker.target_attack_tile.unit == null) return;
        Unit defender = attacker.target_attack_tile.unit.GetComponent<Unit>();
        Tile destination = path.Count > 0 ? path[^1] : Get_selected_tile();
        g_controller.DispatchAction(new MatchAction(MatchActionKind.Attack, g_controller.GetUnitKey(attacker),
            PositionOf(destination), g_controller.GetUnitKey(defender)));
    }

    public void Handle_capture()
    {
        Unit unit = Get_selected_unit();
        if (unit.IsMoving || unit.has_died) return;
        Tile destination = path.Count > 0 ? path[^1] : Get_selected_tile();
        g_controller.DispatchAction(new MatchAction(MatchActionKind.Capture, g_controller.GetUnitKey(unit), PositionOf(destination)));
    }

    public bool Handle_spawn(string option)
    {
        if (selected_tile == null || current_team == null || current_team.has_lost) return false;
        Tile tile = Get_selected_tile();
        GameObject prefab = current_team.raceClass.AvailableUnits.Find(p => p.GetComponent<Unit>().unit_name == option);
        if (prefab == null) return false;
        return g_controller.TryExecuteAction(MatchAction.Recruit(teams.IndexOf(current_team), PositionOf(tile),
            UnityMatchAdapter.TypeId(prefab.GetComponent<Unit>())), out _);
    }

    public System.Threading.Tasks.Task<bool> Handle_spawnAsync(string option)
    {
        if (selected_tile == null || current_team == null || current_team.has_lost) return System.Threading.Tasks.Task.FromResult(false);
        var prefab = current_team.raceClass.AvailableUnits.Find(p => p.GetComponent<Unit>().unit_name == option);
        if (prefab == null) return System.Threading.Tasks.Task.FromResult(false);
        return g_controller.ExecuteActionAsync(MatchAction.Recruit(teams.IndexOf(current_team), PositionOf(Get_selected_tile()),
            UnityMatchAdapter.TypeId(prefab.GetComponent<Unit>())));
    }

    public void Cancel_castle()
    {
        // Disable mouse input temporarily
        cooldown = .2f;
        g_controller.status = "Canceling";

        // Clear board and line
        Clear_board();
        Clear_line();

        uiManager.player_info.SetActive(true);
        uiManager.buttons.SetActive(true);
        g_controller.menu_lock = false;
        uiManager.spawnMenu.gameObject.SetActive(false);
        g_controller.status = "Select_tile"; // Re-enable tile selection
        g_controller.hover_toggle = true;
        selected_tile = null;
        game_camera.GetComponent<scr_camera>().Unlock_camera();
    }

    public void Start_build()
    {
        if (!CanStartBuild()) return;
        cooldown = .2f;
        uiManager.action_menu.SetActive(false);
        Clear_board();
        Clear_line();
        var unit = Get_selected_unit();
        var destination = path.Count > 0 ? path[^1] : unit.current_tile;
        var buildTargets = g_controller.GetBuildTargets(unit, destination);
        foreach (Tile tile in tiles)
        {
            bool allowed = buildTargets.Contains(PositionOf(tile));
            tile.can_build = allowed ? "can_build" : "false";
            tile.Update_overlay(allowed ? "selected" : "attack");
        }
        g_controller.status = "Select_tile";
        is_building = true;
    }

    private bool CanStartBuild()
    {
        if (!MainMenu.build_mode || selected_tile == null || Get_selected_tile().unit == null || current_team == null || current_team.has_lost) return false;
        Unit unit = Get_selected_unit();
        Tile destination = path.Count > 0 ? path[^1] : Get_selected_tile();
        return !unit.IsMoving && g_controller.CanStartBuild(unit, destination);
    }

    private bool CanBuildOn(Tile tile)
    {
        if (tile == null || selected_tile == null || Get_selected_tile().unit == null || Get_selected_unit().IsMoving) return false;
        var unit = Get_selected_unit();
        var destination = path.Count > 0 ? path[^1] : unit.current_tile;
        return g_controller.IsActionLegal(new MatchAction(MatchActionKind.BuildCastle, g_controller.GetUnitKey(unit),
            PositionOf(destination), target: PositionOf(tile)));
    }

    public void Finalize_build(Tile tile)
    {
        if (!is_building || !CanBuildOn(tile)) return;
        Unit unit = Get_selected_unit();
        var destination = path.Count > 0 ? path[^1] : unit.current_tile;
        g_controller.DispatchAction(new MatchAction(MatchActionKind.BuildCastle, g_controller.GetUnitKey(unit),
            PositionOf(destination), target: PositionOf(tile)));
    }

    public void ChangeTile(Tile tile, string option)
    {
        GameObject selectedTile = null;
        switch (option)
        {
            case "grass_tile":
                selectedTile = mapCreator.grass_tile;
                break;
            case "castle":
                selectedTile = mapCreator.grass_tile;
                break;
            case "forest_tile":
                selectedTile = mapCreator.forest_tile;
                break;
            case "mountain_tile":
                selectedTile = mapCreator.mountain_tile;
                break;
            default:
                Debug.Log("Incorrect option");
                break;
        }

        int position_x = tile.position_x;
        int position_y = tile.position_y;

        if (hover_tile != null) hover_tile.GetComponent<Tile>().Update_hover_overlay("off");
        hover_tile = null;
        tile.gameObject.SetActive(false);
        Destroy(tile.gameObject);
        selectedTile = Instantiate(selectedTile, new Vector3(position_x * 64, position_y * 64), Quaternion.identity, transform);
        selectedTile.GetComponent<Tile>().position_x = position_x;
        selectedTile.GetComponent<Tile>().position_y = position_y;

        if (option == "castle")
        {
            selectedTile.GetComponent<Tile>().Activate_castle();
        }
        tiles[position_x, position_y] = selectedTile.GetComponent<Tile>();
    }

    public void Select_tile(Tile tile, Team team)
    {
        if (tile == null || team == null || team.has_lost || g_controller.status == "Over") return;
        current_team = team;
        if (is_building)
        {
            if (tile.can_build == "can_build") Finalize_build(tile);
            else Cancel_move();
            return;
        }
        if (selecting_attack)
        {
            Unit attacker = Get_selected_unit();
            Tile destination = path.Count > 0 ? path[^1] : Get_selected_tile();
            int distance = destination.Distance_to_tile(tile);
            if (tile.unit != null && tile.unit.GetComponent<Unit>().team != attacker.team && distance >= 1 && distance <= attacker.attack_range)
            {
                attacker.target_attack_tile = tile;
                Display_attack_menu(attacker, tile.unit.GetComponent<Unit>());
            }
            else Cancel_move();
            return;
        }
        if (is_unit_selected && selected_tile != null && Get_selected_tile().unit != null &&
            current_team.Is_in_team(Get_selected_unit()) && !Get_selected_unit().has_moved)
        {
            Unit unit = Get_selected_unit();
            if (unit.IsMoving) return;
            if (tile == Get_selected_tile() && cooldown > 0) return;
            // Revalidate the live board on click. Hover and overlay colours are presentation only.
            MovementResult movement = CalculateMovement(unit, unit.movement);
            if (TryPlanPath(tile, movement, out List<Tile> plan))
            {
                path = plan;
                g_controller.status = "Moving";
                unit.Move_along_path(path, game_camera);
                if (tile.unit != null && tile.unit != unit.gameObject)
                {
                    unit.target_attack_tile = tile;
                    Display_attack_menu(unit, tile.unit.GetComponent<Unit>());
                }
                else Display_action_menu(tile);
                return;
            }
        }
        SelectForInspection(tile);
    }

    private void SelectForInspection(Tile tile)
    {
        Clear_board();
        Clear_line();
        path.Clear();
        selectedMovement = null;
        selected_tile = tile.gameObject;
        tile.selected = true;
        tile.Update_overlay("selected");
        var inspected = tile.unit != null ? tile.unit.GetComponent<Unit>() : null;
        // An enemy's spent action belongs to its own turn. Inspection still shows
        // its movement/threat range on tap or click without granting control.
        is_unit_selected = inspected != null && !inspected.has_died
            && (inspected.team != current_team || !inspected.has_moved);
        UpdateSelectedUnitSorting();
        if (is_unit_selected)
        {
            audioManager.PlaySFX(audioManager.select);
            Calculate_movement();
        }
        else if (tile.unit == null && tile.has_castle && tile.castle.GetComponent<Castle>().current_team == current_team)
        {
            g_controller.status = "Castle";
            g_controller.hover_toggle = false;
            uiManager.player_info.SetActive(true);
            uiManager.main_menu.SetActive(false);
            uiManager.tile_info.SetActive(false);
            uiManager.buttons.SetActive(false);
            g_controller.menu_lock = true;
            uiManager.spawnMenu.gameObject.SetActive(true);
            uiManager.spawnMenu.ResetMenu();
            game_camera.transform.position = new Vector3(tile.transform.position.x, tile.transform.position.y, -10);
            game_camera.Lock_camera();
        }
    }

    public void Calculate_movement()
    {
        Find_selectable_tiles(Get_selected_tile(), Get_selected_unit().movement, Get_selected_unit().attack_range);
    }

    public void Find_selectable_tiles(Tile tile, int max_movement, int range)
    {
        Unit unit = tile.unit.GetComponent<Unit>();
        selectedMovement = CalculateMovement(unit, max_movement);
        tile.selected = true;
        foreach (Tile candidate in tiles)
        {
            var position = PositionOf(candidate);
            int cost = selectedMovement.CostTo(position);
            if (selectedMovement.IsReachable(position))
            {
                candidate.current_path_cost = cost;
                candidate.state = selectedMovement.CanStop(position) ? "Can_move" : "Ally";
                candidate.Update_overlay("move");
            }
            else if (selectedMovement.TryAttackOrigin(position, range, out _))
            {
                bool ally = candidate.unit != null && candidate.unit.GetComponent<Unit>().team == unit.team;
                candidate.state = ally ? "Ally" : "Can_attack";
                if (!ally) candidate.Update_overlay("attack");
            }
        }
        tile.Update_overlay("selected");
    }

    private static GridPosition PositionOf(Tile tile) => new GridPosition(tile.position_x, tile.position_y);

    private MovementResult CalculateMovement(Unit unit, int budget)
    {
        var board = new MovementCell[max_x, max_y];
        foreach (Tile tile in tiles)
        {
            CellOccupant occupant = CellOccupant.Empty;
            if (tile.unit != null && tile.unit != unit.gameObject)
                occupant = tile.unit.GetComponent<Unit>().team == unit.team ? CellOccupant.Ally : CellOccupant.Enemy;
            board[tile.position_x, tile.position_y] = new MovementCell(Math.Max(1, tile.movement_cost), tile.mountain != null, occupant);
        }
        return MovementRules.Calculate(board, PositionOf(unit.current_tile), budget);
    }

    private bool TryPlanPath(Tile target, MovementResult movement, out List<Tile> plan)
    {
        plan = new List<Tile>();
        Unit unit = Get_selected_unit();
        GridPosition destination = PositionOf(target);
        if (target.unit != null && target.unit != unit.gameObject)
        {
            if (target.unit.GetComponent<Unit>().team == unit.team ||
                !movement.TryAttackOrigin(destination, unit.attack_range, out destination)) return false;
        }
        else if (!movement.CanStop(destination)) return false;
        foreach (GridPosition position in movement.PathTo(destination)) plan.Add(tiles[position.X, position.Y]);
        return plan.Count > 0;
    }

    bool Find_attack_range(Tile origin, int range, bool toggle_ui)
    {
        bool canAttack = false;
        Unit unit = Get_selected_unit();
        foreach (Tile tile in tiles)
        {
            int distance = origin.Distance_to_tile(tile);
            if (distance < 1 || distance > range) continue;
            bool ally = tile.unit != null && tile.unit.GetComponent<Unit>().team == unit.team;
            tile.state = ally ? "Ally" : "Can_attack";
            if (!ally)
            {
                if (toggle_ui) tile.Update_overlay("attack");
                if (tile.unit != null) canAttack = true;
            }
        }
        return canAttack;
    }

    public void Hover_mouse(bool is_toggled)
    {
        switch(is_toggled)
        {
            case true:
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

                RaycastHit hit;
                if (Physics.Raycast(ray, out hit))
                {
                    if (hit.collider.GetComponent<Tile>() != null
                        && (hover_tile == null || hit.collider.GetComponent<Tile>() != hover_tile.GetComponent<Tile>()))
                    {
                        if (hover_tile != null)
                        {
                            if (hover_tile != null) hover_tile.GetComponent<Tile>().Update_hover_overlay("off");
                        }
                        hit.collider.GetComponent<Tile>().Update_hover_overlay("on");
                        hover_tile = hit.collider.gameObject;

                        if(uiManager != null)
                            uiManager.Update_tile_info(hover_tile.GetComponent<Tile>());

                        if (!selecting_attack)
                            Draw_path_line();
                    }
                }
                else
                {
                    if (hover_tile != null)
                    {
                        if (hover_tile != null) hover_tile.GetComponent<Tile>().Update_hover_overlay("off");
                        hover_tile = null;
                        Clear_line();
                    }

                    if (uiManager != null)
                        uiManager.Update_tile_info(null);
                }
                break;
            default:
                break;
        }
    }
    public void HoverAt(Vector2 point)
    {
        if (!WorldPointer.TryGetTile(point, out var tile)) return;
        if (hover_tile != null) hover_tile.GetComponent<Tile>().Update_hover_overlay("off");
        hover_tile = tile.gameObject; tile.Update_hover_overlay("on");
        if (uiManager != null) uiManager.Update_tile_info(tile);
    }

    public void Draw_path_line()
    {
        Clear_line();
        if (is_building || movementPath == null || hover_tile == null || selected_tile == null || !is_unit_selected || selectedMovement == null ||
            !current_team.Is_in_team(Get_selected_unit()) || Get_selected_unit().has_moved) return;
        if (!TryPlanPath(hover_tile.GetComponent<Tile>(), selectedMovement, out List<Tile> preview)) return;
        for (int i = 0; i < preview.Count; i++)
        {
            points.Add(preview[i].transform);
        }
        movementPath.Show(points);
    }

    public void Check_win()
    {
        var outcome = g_controller.ResolveOutcome();
        if (!outcome.IsOver) return;
        g_controller.status = "Over";
        g_controller.menu_lock = true;
        g_controller.hover_toggle = false;
        uiManager.action_menu.SetActive(false);
        uiManager.attack_menu.SetActive(false);
        uiManager.spawnMenu.gameObject.SetActive(false);
        uiManager.player_info.SetActive(false);
        uiManager.turn_info.SetActive(false);
        uiManager.main_menu.SetActive(false);
        uiManager.buttons.SetActive(false);
        uiManager.win_text.text = outcome.Winner < 0 ? "Draw" : teams[outcome.Winner].team_name + "\nWins!!!";
        uiManager.win_menu.SetActive(true);
        game_camera.Clear_follow();
        game_camera.Lock_camera();
    }

    public void ReleaseCastles(Team defeated)
    {
        foreach (Tile tile in tiles)
            if (tile != null && tile.has_castle && tile.castle.GetComponent<Castle>().current_team == defeated)
                tile.castle.GetComponent<Castle>().Capture_castle(null);
    }

    public void UpdateStormRadius()
    {
        StormBounds safeZone = StormRules.SafeZone(max_x, max_y, g_controller.CurrentRound, stormWarmupRounds, stormRoundsPerShrink);
        foreach (Tile tile in tiles)
            if (!safeZone.Contains(tile.position_x, tile.position_y)) tile.Activate_storm();
    }

    public void Finalize_turn()
    {
        ResetInteraction();
    }

    int Get_path_cost(List<Tile> list)
    {
        int cost = 0;
        foreach (Tile tile in list)
        {
            cost += tile.movement_cost;
        }
        return cost;
    }

    public void Populate_adjacency_list()
    {
        for (int i = 0; i < max_x; i++)
        {
            for (int j = 0; j < max_y; j++)
            {
                tiles[i, j].adjacent_tiles.Clear();
                if (i - 1 >= 0)
                    tiles[i, j].GetComponent<Tile>().adjacent_tiles.Add(tiles[i - 1, j].GetComponent<Tile>());

                if (i + 1 < max_x)
                    tiles[i, j].GetComponent<Tile>().adjacent_tiles.Add(tiles[i + 1, j].GetComponent<Tile>());

                if (j - 1 >= 0)
                    tiles[i, j].GetComponent<Tile>().adjacent_tiles.Add(tiles[i, j - 1].GetComponent<Tile>());

                if (j + 1 < max_y)
                    tiles[i, j].GetComponent<Tile>().adjacent_tiles.Add(tiles[i, j + 1].GetComponent<Tile>());
            }
        }
    }

    public void Clear_board()
    {
        RestoreSelectedUnitSorting();
        for (int i = 0; i < max_x; i++)
        {
            for (int j = 0; j < max_y; j++)
            {
                Tile t = tiles[i, j].GetComponent<Tile>();
                t.selected = false;
                t.state = null;
                t.current_path_cost = 0;
                t.current_attack_path_cost = 0;
                //t.text.text = "";
                t.previous_tile_in_path = null;
                t.Update_overlay("off");
            }
        }
    }

    public void Clear_possible_damage()
    {
        for (int i = 0; i < max_x; i++)
        {
            for (int j = 0; j < max_y; j++)
            {
                Tile t = tiles[i, j].GetComponent<Tile>();
                t.possible_damage = 0;
            }
        }
    }

    public void Clear_line()
    {
        if (movementPath != null) movementPath.Clear();
        if (line != null && line.positionCount > 0)
        {
            line.positionCount = 0;
        }

        points.Clear();
        if (arrow != null) arrow.positionCount = 0;
    }

    Tile Get_selected_tile()
    {
        return selected_tile.GetComponent<Tile>();
    }

    Unit Get_selected_unit()
    {
        return Get_selected_tile().unit.GetComponent<Unit>();
    }
}
