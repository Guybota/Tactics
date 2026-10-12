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
    {
        EnsurePreviews();
        attackerPreview.Set(unit1);
        defenderPreview.Set(unit2);

        float unit1_strength = unit1.Calculate_strength();
        float unit2_strength = unit2.Calculate_strength();

        CombatResult result = CombatRules.Resolve(unit1.CombatStats, unit2.CombatStats, attack_distance);
        float unit2_strength2 = CombatRules.Strength(result.DefenderHealth, unit2.max_health);

        attackerHealthPreview.Update_predicted_health(unit1.hp, result.AttackerHealth, unit1.max_health);
        defenderHealthPreview.Update_predicted_health(unit2.hp, result.DefenderHealth, unit2.max_health);

        p1_stats_info.GetComponent<TextMeshProUGUI>().text = Math.Ceiling(unit1.hp).ToString() + " / " + unit1.max_health.ToString() + "\nAttack: " + unit1.attack.ToString() + "\nStrength: " + Math.Ceiling(unit1_strength * 100) + "%";
        p2_stats_info.GetComponent<TextMeshProUGUI>().text = Math.Ceiling(unit2.hp).ToString() + " / " + unit2.max_health.ToString() + "\nAttack: " + unit2.attack.ToString() + "\nStrength: " + Math.Ceiling(unit2_strength * 100) + "(" + Math.Ceiling(unit2_strength2 * 100) + ")%";
    }

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
