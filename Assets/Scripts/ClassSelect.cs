using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ClassSelect : MonoBehaviour
{
    [SerializeField] private int player;
    public PreviewUnit previewUnit;
    public RaceClass selectedClass;
    private int index;
    [SerializeField] private Button controlButton;
    public int PlayerSlot => player - 1;
    public void ToggleControl()
    {
        MainMenu.SetBotPlayer(PlayerSlot, !MainMenu.BotPlayers[PlayerSlot]);
        RefreshControl();
    }
    private void RefreshControl()
    {
        if (controlButton != null) controlButton.GetComponentInChildren<TextMeshProUGUI>().text = MainMenu.BotPlayers[PlayerSlot] ? "Bot" : "Human";
    }

    void Start()
    {
        index = 0;
        selectedClass = MainMenu.classes[index];
        previewUnit.Set(selectedClass.Leader.GetComponent<Unit>());
        if (controlButton != null) controlButton.onClick.AddListener(ToggleControl);
        RefreshControl();
    }

    public void Next()
    {
        if (index == MainMenu.classes.Count - 1) 
        { 
            index = 0;
        }
        else
        {
            index++;
        }

        selectedClass = MainMenu.classes[index];
        MainMenu.SelectRace(player - 1, selectedClass);

        previewUnit.Set(selectedClass.Leader.GetComponent<Unit>());
    }

    public void Back()
    {
        if (index == 0)
        {
            index = MainMenu.classes.Count - 1;
        }
        else
        {
            index--;
        }

        selectedClass = MainMenu.classes[index];
        MainMenu.SelectRace(player - 1, selectedClass);

        previewUnit.Set(selectedClass.Leader.GetComponent<Unit>());
    }
}
