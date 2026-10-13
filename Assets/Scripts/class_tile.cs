using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class Tile : MonoBehaviour
{
    public string tile_name;
    public int movement_cost;
    public int attack_cost;
    public float possible_damage;
    public GameObject unit;
    public GameObject overlay;
    public GameObject hover_overlay;
    public GameObject text_object;
    public TextMeshProUGUI text;
    public int position_x;
    public int position_y;
    public bool selected;
    public string state;
    public int current_path_cost = 0;
    public int current_attack_path_cost = 0;
    public List<Tile> adjacent_tiles;
    public Tile previous_tile_in_path;

    public GameObject forest;
    public GameObject mountain;
    public GameObject storm;
    public List<Sprite> sprites;
    public List<Sprite> forest_sprites;
    public List<Sprite> mountain_sprites;

    public bool has_storm = false;
    public string can_build = null;

    public bool has_castle = false;
    public GameObject castle;
    public int DefenseStars => has_castle ? 3 : forest != null ? 2 : 1;

    void Start() {
        text = text_object.GetComponent<TextMeshProUGUI>();

        RandomizeSprite();
    }

    public void RandomizeSprite() 
    {
        System.Random rnd = new System.Random();

        int rndm_sprite = rnd.Next(0, sprites.Count);
        GetComponent<SpriteRenderer>().sprite = sprites[rndm_sprite];

        if (mountain != null)
        {
            rndm_sprite = rnd.Next(0, mountain_sprites.Count);
            mountain.GetComponent<SpriteRenderer>().sprite = mountain_sprites[rndm_sprite];
        }
        else if (forest != null)
        {
            rndm_sprite = rnd.Next(0, forest_sprites.Count);
            forest.GetComponent<SpriteRenderer>().sprite = forest_sprites[rndm_sprite];
        }
    }

    public void Update_overlay(string mode) { 
        SpriteRenderer overlay_sprite = overlay.GetComponent<SpriteRenderer>();

        switch(mode)
        {
            case "on":
                overlay_sprite.enabled = true;
                overlay_sprite.color = Color.white;
                break;
            case "off":
                overlay_sprite.enabled = false;
                break;
            case "selected":
                overlay_sprite.enabled = true;
                overlay_sprite.color = Color.green;
                break;
            case "move":
                overlay_sprite.enabled = true;
                overlay_sprite.color = Color.blue;
                break;
            case "build":
                overlay_sprite.enabled = true;
                overlay_sprite.color = Color.yellow;
                break;
            case "attack":
                overlay_sprite.enabled = true;
                overlay_sprite.color = Color.red;
                break;
            default:
                break;
        }
    }

    public void Update_hover_overlay(string mode)
    {
        SpriteRenderer overlay_sprite = hover_overlay.GetComponent<SpriteRenderer>();

        switch (mode)
        {
            case "on":
                overlay_sprite.enabled = true;
                overlay_sprite.color = Color.white;
                break;
            case "off":
                overlay_sprite.enabled = false;
                break;
            default:
                break;
        }
    }

    public List<Tile> Get_path_to_tile()
    {
        List<Tile> path = new List<Tile>();

        if (previous_tile_in_path != null )
        {
            foreach (Tile tile in previous_tile_in_path.Get_path_to_tile())
            {
                path.Add(tile);
            }
            
            path.Add(previous_tile_in_path);
            return path;
        } else
        {
            return path;
        }
    }

    public bool Is_next_to(Tile tile)
    {
        int distance = Distance_to_tile(tile);

        if (distance == 1)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public int Distance_to_tile(Tile tile)
    {
        int distance = Math.Abs(tile.position_x - position_x) + Math.Abs(tile.position_y - position_y);
        return distance;
    }

    public bool Is_equals_to(Tile tile)
    {
        if (tile.position_x == position_x && tile.position_y == position_y)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public bool Is_in_path(List<Tile> path)
    {
        foreach(Tile tile in path)
        {
            if (Is_equals_to(tile))
                return true;
        }

        return false;
    }

    public void Activate_castle()
    {
        has_castle = true;
        castle.gameObject.SetActive(true);
    }

    public void Activate_storm()
    {
        if (has_castle)
            castle.gameObject.SetActive(false);
        has_castle = false;
        storm.SetActive(true);
        has_storm = true;
    }
}
