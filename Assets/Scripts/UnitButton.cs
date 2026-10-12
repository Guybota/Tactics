using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UnitButton : MonoBehaviour
{
    public string option;
    public int cost;
    public SpawnMenu spawnMenu;
    public TextMeshProUGUI textMeshProUGUI;
    public PreviewUnit previewUnit;


    public void Set(SpawnMenu spawnMenu, Unit unit)
    {
        this.spawnMenu = spawnMenu;
        textMeshProUGUI.text = unit.unit_name + "\n" + unit.unit_cost.ToString() + "$";
        option = unit.unit_name;
        cost = unit.unit_cost;
        previewUnit.Set(unit);
    }

    public void On_click()
    {
        spawnMenu.SelectUnit(previewUnit);
    }
}
