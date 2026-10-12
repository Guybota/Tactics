using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ClassSelect : MonoBehaviour
{
    [SerializeField] private int player;
    public PreviewUnit previewUnit;
    public RaceClass selectedClass;
    private int index;

    void Start()
    {
        index = 0;
        selectedClass = MainMenu.classes[index];
        previewUnit.Set(selectedClass.Leader.GetComponent<Unit>());
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
