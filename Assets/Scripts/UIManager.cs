using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class UIManager : MonoBehaviour
{
    public new scr_camera camera;

    public GameObject action_menu;
    public GameObject attack_menu;
    public GameObject win_menu;
    public SaveMenu saveMenu;
    public LoadMenu loadMenu;
    public SpawnMenu spawnMenu;
    public TextMeshProUGUI win_text;

    public GameObject turn_image;
    public GameObject turn_name;
    public TextMeshProUGUI turn_number;

    public GameObject money_text;
    public GameObject player_info;
    public GameObject turn_info;
    public GameObject tile_info;
    public PreviewTile preview_tile;
    public GameObject main_menu;
    public GameObject buttons;

    public void Update_tile_info(Tile tile)
    {
        if (tile != null)
        {
            preview_tile.Set(tile);
            tile_info.SetActive(true);
        }
        else
        {
            tile_info.SetActive(false);
        }
    }

    public void ShowMainMenu()
    {
        camera.Lock_camera();
        buttons.SetActive(false);
        player_info.SetActive(false);
        tile_info.SetActive(false);
        main_menu.SetActive(true);
    }

    internal void SetCastleButtons(List<GameObject> units, scr_game_contoller game_Contoller)
    {
        spawnMenu.SetCastleButtons(units, game_Contoller);
    }
}
