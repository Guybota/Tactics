using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SpawnMenu : MonoBehaviour
{
    [SerializeField] private scr_game_contoller gameContoller;

    [SerializeField] public GameObject spawnPanel;
    [SerializeField] private ScrollRect spawnScrollRect;
    [SerializeField] private GameObject unitButton;
    [SerializeField] private GameObject unitInfo;
    [SerializeField] private Button spawnButton;
    [SerializeField] private PreviewUnit previewUnit;

    [SerializeField] private TextMeshProUGUI unitHp;
    [SerializeField] private TextMeshProUGUI unitMovement;
    [SerializeField] private TextMeshProUGUI unitRange;
    [SerializeField] private TextMeshProUGUI unitAttack;

    private List<GameObject> buttons = new List<GameObject>();

    internal void SetCastleButtons(List<GameObject> units, scr_game_contoller game_Contoller)
    {
        if (unitButton.scene.IsValid()) unitButton.SetActive(false);
        if (buttons.Count > 0)
        {
            foreach (GameObject button in buttons)
            {
                Destroy(button);
            }
            buttons.Clear();
        }

        units = units.OrderBy(o => o.GetComponent<Unit>().unit_cost).ToList();

        foreach (GameObject unit in units)
        {
            if (!unit.GetComponent<Unit>().is_king)
            {
                GameObject button = Instantiate(unitButton, spawnPanel.transform);
                button.GetComponent<UnitButton>().Set(this, unit.GetComponent<Unit>());
                button.SetActive(true);
                buttons.Add(button);
            }
        }
    }

    public void ResetMenu()
    {
        unitInfo.SetActive(false);
        spawnButton.interactable = false;
        if (spawnScrollRect == null) spawnScrollRect = spawnPanel.GetComponentInParent<ScrollRect>();
        if (spawnScrollRect == null) return;
        spawnScrollRect.StopMovement();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)spawnPanel.transform);
        spawnScrollRect.verticalNormalizedPosition = 1f;
    }

    public void ProcessSpawnOption()
    {
        gameContoller.ProcessSpawnOption(previewUnit.unit_name);
    }

    internal void SelectUnit(PreviewUnit unit)
    {
        previewUnit.Set(unit);
        unitHp.text = "Hp: " + unit.max_health;
        unitMovement.text = "Mv: " + unit.movement;
        unitRange.text = "Rg: " + unit.attack_range;
        unitAttack.text = "At: " + unit.attack;
        unitInfo.SetActive(true);

        spawnButton.interactable = true;
    }
}
