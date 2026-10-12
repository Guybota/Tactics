using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PreviewTile : MonoBehaviour
{
    public string tile_name;
    public Image tile_image;
    public Image forest;
    public Image mountain;
    public bool has_castle = false;
    public Image castle;
    public Image castleRoof;

    public Image bg;
    public Image crown;

    public GameObject unit_info;

    public PreviewUnit preview_unit;
    public TextMeshProUGUI tile_name_text;
    public TextMeshProUGUI tile_team_text;
    public TextMeshProUGUI unit_name_text;
    public TextMeshProUGUI unit_team_text;
    public TextMeshProUGUI unit_hp_text;
    public TextMeshProUGUI unit_attack_text;
    public TextMeshProUGUI unit_mv_text;
    public TextMeshProUGUI unit_rg_text;

    public void Set(Tile tile)
    {
        if (tile != null)
        {
            tile_name = tile.tile_name;

            tile_image.sprite = tile.GetComponent<SpriteRenderer>().sprite;
            if (tile.forest != null)
            {
                forest.sprite = tile.forest.GetComponent<SpriteRenderer>().sprite;
                forest.gameObject.SetActive(true);
            }
            else
            {
                forest.gameObject.SetActive(false);
            }

            if (tile.mountain != null)
            {
                mountain.sprite = tile.mountain.GetComponent<SpriteRenderer>().sprite;
                mountain.gameObject.SetActive(true);
            }
            else
            {
                mountain.gameObject.SetActive(false);
            }

            if (tile.has_castle)
            {
                tile_name = "Castle";
                castle.gameObject.SetActive(true);
                if (tile.castle.GetComponent<Castle>().current_team != null)
                {
                    castleRoof.color = tile.castle.GetComponent<Castle>().current_team.team_color;
                    tile_team_text.text = tile.castle.GetComponent<Castle>().current_team.team_name;
                    tile_team_text.color = tile.castle.GetComponent<Castle>().current_team.team_color;
                    tile_team_text.gameObject.SetActive(true);
                } 
                else
                {
                    castleRoof.color = Color.white;
                    tile_team_text.text = "{No Team}";
                    tile_team_text.color = Color.gray;
                    tile_team_text.gameObject.SetActive(true);
                }
            }
            else
            {
                castle.gameObject.SetActive(false);
                tile_team_text.gameObject.SetActive(false);
            }

            if (tile.unit != null)
            {
                preview_unit.Set(tile.unit.GetComponent<Unit>());
                unit_name_text.text = preview_unit.unit_name;
                unit_hp_text.text = "Hp: " + Math.Round(preview_unit.hp, 1, MidpointRounding.AwayFromZero).ToString() + "/"+ Math.Round(preview_unit.max_health, 0, MidpointRounding.AwayFromZero).ToString();
                unit_mv_text.text = "Mv: " + preview_unit.movement.ToString();
                unit_rg_text.text = "Rg: " + preview_unit.attack_range.ToString();
                unit_attack_text.text = "At: " + preview_unit.attack.ToString();
                unit_team_text.text = preview_unit.team_name;
                unit_team_text.color = tile.unit.GetComponent<Unit>().team.team_color;
                unit_info.gameObject.SetActive(true);
                bg.rectTransform.localPosition = new Vector3(0, -120, 0);
                bg.rectTransform.sizeDelta = new Vector2(408, 390);
                if (preview_unit.isKing)
                {
                    crown.gameObject.SetActive(true);
                }
                else
                {
                    crown.gameObject.SetActive(false);
                }
            }
            else
            {
                unit_info.gameObject.SetActive(false);
                bg.rectTransform.localPosition = Vector3.zero;
                bg.rectTransform.sizeDelta = new Vector2(408, 150);
            }

            tile_name_text.text = tile_name;
        }
    }
}
