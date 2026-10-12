using System;
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

    private void Start()
    {
        gameMap.SetCreate(MainMenu.map_width, MainMenu.map_height, this);
        selectedTile = "mountain_tile";
    }

    public void SelectTerrain(string option)
    {
        if (option != "grass_tile" && option != "forest_tile" && option != "mountain_tile" && option != "castle")
            throw new ArgumentException("Unknown map terrain.", nameof(option));
        selectedTile = option;
    }

    public bool PaintAt(Vector2 screenPosition)
    {
        if (!WorldPointer.TryGetTile(screenPosition, out Tile tile)) return false;
        gameMap.ChangeTile(tile, selectedTile);
        return true;
    }

    private void Update()
    {
        if (!WorldPointer.IsOverUI(Input.mousePosition)) gameMap.Hover_mouse(true);
        if (Input.GetMouseButtonUp(0)) PaintAt(Input.mousePosition);
    }
}
