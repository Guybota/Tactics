using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PreviewUnit : MonoBehaviour
{
    public GameObject current_health_bar;
    public Image sprite;
    public Image color;
    public string team_name;
    public string unit_name;
    public float max_health = 10;
    public float hp = 10;
    public int movement;
    public int attack = 3;
    public int attack_range;
    public bool isKing;

    public TextMeshProUGUI tile_name_text;

    public void Set(Unit unit)
    {
        unit_name = unit.unit_name;

        if (unit.firstSprite != null && unit.firstSpriteC != null)
        {
            sprite.sprite = unit.firstSprite;
            color.sprite = unit.firstSpriteC;
        }
        else
        {
            sprite.sprite = unit.sprite.sprite;
            color.sprite = unit.color.sprite;
        }
        sprite.color = unit.sprite.color;


        if (unit.team != null)
        {
            team_name = unit.team.team_name;
            color.color = unit.color.color;
        }
        else
        {
            color.color = Color.grey;
        }

        hp = unit.hp;
        movement = unit.movement;
        attack = unit.attack;
        max_health = unit.max_health;
        attack_range = unit.attack_range;
        isKing = unit.is_king;
    }

    public void Set(PreviewUnit unit)
    {
        unit_name = unit.unit_name;

        sprite.sprite = unit.sprite.sprite;
        sprite.color = unit.sprite.color;

        color.sprite = unit.color.sprite;

        if (unit.team_name != null)
        {
            team_name = unit.team_name;
            color.color = unit.color.color;
        }
        else
        {
            color.color = Color.grey;
        }

        hp = unit.hp;
        movement = unit.movement;
        attack = unit.attack;
        max_health = unit.max_health;
        attack_range = unit.attack_range;
        isKing = unit.isKing;
    }
}
