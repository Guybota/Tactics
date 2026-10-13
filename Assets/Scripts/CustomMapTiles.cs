using Tactics.Core;
using UnityEngine;

// Unity presentation boundary; the file format contains no scene or prefab references.
public static class CustomMapTiles
{
    public static void Apply(Map map, CustomMapData data, GameObject grass, GameObject forest, GameObject mountain)
    {
        for (int x = 0; x < map.max_x; x++) for (int y = 0; y < map.max_y; y++)
        {
            var old = map.tiles[x, y];
            if (old == null) continue;
            old.gameObject.SetActive(false); Object.Destroy(old.gameObject);
        }
        map.max_x = data.width; map.max_y = data.height;
        map.tiles = new Tile[data.width, data.height];
        map.hover_tile = null; map.selected_tile = null;
        for (int y = 0; y < data.height; y++) for (int x = 0; x < data.width; x++)
        {
            var cell = data.Cell(x, y);
            var prefab = cell.terrain == TerrainKind.Forest ? forest : cell.terrain == TerrainKind.Mountain ? mountain : grass;
            var tile = Object.Instantiate(prefab, new Vector3(x * 64, y * 64), Quaternion.identity, map.transform).GetComponent<Tile>();
            tile.position_x = x; tile.position_y = y; tile.unit = null;
            if (cell.castle) tile.Activate_castle();
            map.tiles[x, y] = tile;
        }
        map.SetBg();
        map.game_camera.Clear_follow();
        map.game_camera.SetMax(data.width * 64, data.height * 64);
        map.game_camera.maxZoom = Mathf.Max(data.width, data.height) / 10f * 500;
        map.game_camera.transform.position = new Vector3((data.width - 1) * 32, (data.height - 1) * 32, -10);
        map.game_camera.Unlock_camera();
    }
}
