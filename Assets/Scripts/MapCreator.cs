using System;
using System.Collections.Generic;
using Tactics.Core;
using UnityEngine;

public class MapCreator : MonoBehaviour
{
    public GameObject grass_tile;
    public GameObject forest_tile;
    public GameObject mountain_tile;
    public GameObject game_camera;
    public Map gameMap;
    private string selectedTile;
    public string SelectedTerrain => selectedTile;
    public int PlayerCount { get; private set; } = 2;
    public string MapName { get; private set; } = "Untitled map";
    public bool HasUnsavedChanges { get; private set; }
    public event Action Changed;
    private string mapId;
    private readonly List<MapStartData> starts = new List<MapStartData>();
    private int startBrush = -1;
    private bool eraseStart;
    public int CastleOwnerBrush { get; private set; } = -1;
    private readonly Dictionary<GridPosition, int> castleOwners = new Dictionary<GridPosition, int>();
    [SerializeField] private Transform markerRoot;
    [SerializeField] private TMPro.TextMeshPro startMarkerTemplate;
    private MapEditorMenu editorMenu;

    private void Start()
    {
        gameMap.SetCreate(MainMenu.map_width, MainMenu.map_height, this);
        game_camera.transform.position = new Vector3((gameMap.max_x - 1) * 32, (gameMap.max_y - 1) * 32, -10);
        game_camera.GetComponent<Camera>().orthographicSize = gameMap.game_camera.maxZoom;
        gameMap.game_camera.Unlock_camera();
        selectedTile = "mountain_tile";
        mapId = Guid.NewGuid().ToString("N");
        HasUnsavedChanges = true;
        editorMenu = GetComponent<MapEditorMenu>();
        editorMenu.Initialize(this);
        game_camera.GetComponent<WorldTouchInput>().Tapped += OnTouchTap;
        var pendingMap = MainMenu.TakePendingEditorMap();
        if (pendingMap != null) ApplyMap(pendingMap);
    }
    private void OnTouchTap(Vector2 point) => PaintAt(point);

    public void SelectTerrain(string option)
    {
        if (option != "grass_tile" && option != "forest_tile" && option != "mountain_tile" && option != "castle")
            throw new ArgumentException("Unknown map terrain.", nameof(option));
        selectedTile = option;
        startBrush = -1; eraseStart = false;
        Changed?.Invoke();
    }

    public void SetMapName(string value)
    {
        if (MapName == value) return;
        MapName = value; MarkChanged();
    }
    public void SetPlayerCount(int count)
    {
        if (count < 2 || count > 4) throw new ArgumentOutOfRangeException(nameof(count));
        if (PlayerCount == count) return;
        PlayerCount = count; starts.RemoveAll(s => s.slot >= count);
        if (startBrush >= count) startBrush = -1;
        RefreshMarkers(); MarkChanged();
    }
    public void SelectStart(int slot)
    {
        if (slot < 0 || slot >= PlayerCount) throw new ArgumentOutOfRangeException(nameof(slot));
        startBrush = slot; eraseStart = false; Changed?.Invoke();
    }
    public void SelectEraseStart() { startBrush = -1; eraseStart = true; Changed?.Invoke(); }
    public void CycleCastleOwner() { CastleOwnerBrush = CastleOwnerBrush == 3 ? -1 : CastleOwnerBrush + 1; Changed?.Invoke(); }
    public string BrushLabel => eraseStart ? "Erase start" : startBrush >= 0 ? "P" + (startBrush + 1) + " start" : selectedTile.Replace("_tile", "");
    private void MarkChanged() { HasUnsavedChanges = true; Changed?.Invoke(); }

    public CustomMapData CaptureMap()
    {
        var data = new CustomMapData { id = mapId, name = MapName.Trim(), width = gameMap.max_x,
            height = gameMap.max_y, playerCount = PlayerCount };
        for (int y = 0; y < data.height; y++) for (int x = 0; x < data.width; x++)
        {
            var tile = gameMap.tiles[x, y];
            data.cells.Add(new MapCellData { terrain = tile.mountain != null ? TerrainKind.Mountain : tile.forest != null ? TerrainKind.Forest : TerrainKind.Grass,
                castle = tile.has_castle, castleOwner = tile.has_castle && castleOwners.TryGetValue(new GridPosition(x, y), out int owner) ? owner : -1 });
        }
        foreach (var start in starts) data.starts.Add(new MapStartData { slot = start.slot, x = start.x, y = start.y });
        return data;
    }
    public bool TrySaveMap(out string error)
    {
        error = null;
        try
        {
            var data = CaptureMap(); new CustomMapRepository(Application.persistentDataPath).Write(data);
            MapName = data.name; HasUnsavedChanges = false; Changed?.Invoke(); return true;
        }
        catch (Exception exception) { error = exception.Message; return false; }
    }
    public bool TryLoadMap(string id, out string error)
    {
        error = null;
        try { ApplyMap(new CustomMapRepository(Application.persistentDataPath).Read(id)); return true; }
        catch (Exception exception) { error = exception.Message; return false; }
    }
    public void SavedMapChanged(string id)
    {
        if (id != mapId) return;
        var repository = new CustomMapRepository(Application.persistentDataPath);
        if (!repository.Exists(id)) { mapId = Guid.NewGuid().ToString("N"); MarkChanged(); }
        else if (!HasUnsavedChanges) { MapName = repository.Read(id).name; Changed?.Invoke(); }
    }
    public void ApplyMap(CustomMapData data)
    {
        CustomMapValidator.ValidateStructure(data);
        var copy = data.Clone();
        CustomMapTiles.Apply(gameMap, copy, grass_tile, forest_tile, mountain_tile);
        mapId = copy.id; MapName = copy.name; PlayerCount = copy.playerCount;
        starts.Clear(); starts.AddRange(copy.starts); startBrush = -1; eraseStart = false;
        castleOwners.Clear();
        for (int y = 0; y < copy.height; y++) for (int x = 0; x < copy.width; x++)
            if (copy.Cell(x, y).castle)
            {
                castleOwners[new GridPosition(x, y)] = copy.Cell(x, y).castleOwner;
                ColorCastle(gameMap.tiles[x, y], copy.Cell(x, y).castleOwner);
            }
        MainMenu.map_width = copy.width; MainMenu.map_height = copy.height;
        RefreshMarkers(); HasUnsavedChanges = false; Changed?.Invoke();
    }
    public bool PaintTile(Tile tile)
    {
        if (tile == null || gameMap.GetTileAtPosition(tile.position_x, tile.position_y) != tile) return false;
        if (eraseStart) starts.RemoveAll(s => s.x == tile.position_x && s.y == tile.position_y);
        else if (startBrush >= 0)
        {
            if (tile.mountain != null) return false;
            starts.RemoveAll(s => s.slot == startBrush || (s.x == tile.position_x && s.y == tile.position_y));
            starts.Add(new MapStartData { slot = startBrush, x = tile.position_x, y = tile.position_y });
        }
        else
        {
            var position = new GridPosition(tile.position_x, tile.position_y);
            gameMap.ChangeTile(tile, selectedTile);
            castleOwners.Remove(position);
            if (selectedTile == "castle")
            {
                castleOwners[position] = CastleOwnerBrush;
                ColorCastle(gameMap.tiles[position.X, position.Y], CastleOwnerBrush);
            }
        }
        RefreshMarkers(); MarkChanged(); return true;
    }
    private static void ColorCastle(Tile tile, int owner)
        => tile.castle.GetComponent<Castle>().castle_roof.color = owner < 0 ? Color.white : CustomMapUi.PlayerColor(owner);
    private void RefreshMarkers()
    {
        while (markerRoot.childCount > 0)
        {
            var marker = markerRoot.GetChild(0); marker.gameObject.SetActive(false);
            marker.SetParent(null, false); Destroy(marker.gameObject);
        }
        foreach (var start in starts)
        {
            var marker = Instantiate(startMarkerTemplate, markerRoot); marker.name = "P" + (start.slot + 1) + " Start";
            marker.transform.localPosition = new Vector3(start.x * 64, start.y * 64, 0) + startMarkerTemplate.transform.localPosition;
            marker.text = "P" + (start.slot + 1); marker.color = CustomMapUi.PlayerColor(start.slot); marker.gameObject.SetActive(true);
        }
    }

    public bool PaintAt(Vector2 screenPosition)
    {
        if (!WorldPointer.TryGetTile(screenPosition, out Tile tile)) return false;
        if (editorMenu != null && editorMenu.IsModalOpen) return false;
        return PaintTile(tile);
    }

    private void Update()
    {
        if (editorMenu != null && editorMenu.IsModalOpen) return;
        if (WorldTouchInput.SuppressMouse) return;
        if (!MobileUiMode.Enabled && !WorldPointer.IsOverUI(Input.mousePosition)) gameMap.Hover_mouse(true);
        if (Input.GetMouseButtonUp(0)) PaintAt(Input.mousePosition);
    }
}
