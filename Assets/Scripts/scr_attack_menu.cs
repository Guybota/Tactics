using System;
using TMPro;
using UnityEngine;
using Tactics.Core;

public class scr_attack_menu : MonoBehaviour
{
    public GameObject preview_unit;
    private GameObject p1;
    private GameObject p2;

    public GameObject preview_health_bar;
    public GameObject current_health_bar1;
    private GameObject current_health_bar2;

    // Legacy positions are only used by scenes without authored preview components.
    public Vector3 p1_position;
    public Vector3 p2_position;

    public GameObject p1_stats_info;
    public GameObject p2_stats_info;

    [SerializeField] private PreviewUnit attackerPreview;
    [SerializeField] private PreviewUnit defenderPreview;
    [SerializeField] private Preview_health_bar attackerHealthPreview;
    [SerializeField] private Preview_health_bar defenderHealthPreview;

    public void Setup(Unit unit1, Unit unit2, int attack_distance)
        => SetupFromTile(unit1, unit2, attack_distance, unit1.current_tile);

    public void SetupFromTile(Unit unit1, Unit unit2, int attack_distance, Tile destination)
    {
        EnsurePreviews();
        attackerPreview.Set(unit1);
        defenderPreview.Set(unit2);

        CombatPrediction prediction = unit1.PredictCombatFrom(unit2, attack_distance, destination);
        // Bars show the lowest remaining health; text exposes the complete interval.
        attackerHealthPreview.Update_predicted_health(unit1.hp, prediction.MinAttackerHealth, unit1.max_health);
        defenderHealthPreview.Update_predicted_health(unit2.hp, prediction.MinDefenderHealth, unit2.max_health);
        p1_stats_info.GetComponent<TextMeshProUGUI>().text = unit1.hp.ToString("0.##") + " / " + unit1.max_health.ToString("0.##")
            + "\nDamage: " + Interval(prediction.MinDamage, prediction.MaxDamage)
            + "\nAfter: " + Interval(prediction.MinAttackerHealth, prediction.MaxAttackerHealth);
        p2_stats_info.GetComponent<TextMeshProUGUI>().text = unit2.hp.ToString("0.##") + " / " + unit2.max_health.ToString("0.##")
            + "\nCounter: " + Interval(prediction.MinCounterDamage, prediction.MaxCounterDamage)
            + "\nAfter: " + Interval(prediction.MinDefenderHealth, prediction.MaxDefenderHealth);
    }
    private static string Interval(float minimum, float maximum)
        => minimum.ToString("0.##") + "–" + maximum.ToString("0.##");

    private void EnsurePreviews()
    {
        // Scene components retain their anchors and layout. Legacy scenes create each
        // preview once and reuse it when another combat prediction is opened.
        if (attackerPreview == null)
        {
            if (p1 == null)
            {
                p1 = Instantiate(preview_unit, transform, false);
                p1.transform.localPosition = p1_position;
                p1.transform.localScale = new Vector3(2.5f, 2.5f, 1);
            }
            attackerPreview = p1.GetComponent<PreviewUnit>();
        }
        else
        {
            p1 = attackerPreview.gameObject;
        }

        if (defenderPreview == null)
        {
            if (p2 == null)
            {
                p2 = Instantiate(preview_unit, transform, false);
                p2.transform.localPosition = p2_position;
                p2.transform.localScale = new Vector3(-2.5f, 2.5f, 1);
            }
            defenderPreview = p2.GetComponent<PreviewUnit>();
        }
        else
        {
            p2 = defenderPreview.gameObject;
        }

        if (attackerHealthPreview == null)
        {
            if (current_health_bar1 == null)
            {
                current_health_bar1 = Instantiate(preview_health_bar, transform, false);
                current_health_bar1.transform.localPosition = p1_position + new Vector3(168, 86, 0);
                current_health_bar1.transform.localScale = new Vector3(2.5f, 2.5f, 1);
            }
            attackerHealthPreview = current_health_bar1.GetComponent<Preview_health_bar>();
        }
        else
        {
            current_health_bar1 = attackerHealthPreview.gameObject;
        }

        if (defenderHealthPreview == null)
        {
            if (current_health_bar2 == null)
            {
                current_health_bar2 = Instantiate(preview_health_bar, transform, false);
                current_health_bar2.transform.localPosition = p2_position + new Vector3(-188, 86, 0);
                current_health_bar2.transform.localScale = new Vector3(-2.5f, 2.5f, 1);
            }
            defenderHealthPreview = current_health_bar2.GetComponent<Preview_health_bar>();
        }
        else
        {
            current_health_bar2 = defenderHealthPreview.gameObject;
        }
    }
}
