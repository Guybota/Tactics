using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    public TextMeshProUGUI width_value;
    public Slider width_slider;
    public TextMeshProUGUI height_value;
    public Slider height_slider;

    public Slider create_width_slider;
    public Slider create_height_slider;
    public TextMeshProUGUI create_width_value;
    public TextMeshProUGUI create_height_value;
    public static int map_width = 10;
    public static int map_height = 10;

    public static bool player3IsOn = false;
    public static bool player4IsOn = false;
    public static bool build_mode = false;
    public static bool battle_royale_mode = false;

    public static List<RaceClass> classes;
    public  List<RaceClass> SerializedClasses;

    public Toggle build_toggle;
    public Toggle br_toggle;

    public GameObject firstMenu;
    public GameObject mapMenu;
    public GameObject createmapMenu;
    public GameObject player3_info;
    public GameObject player4_info;

    public static RaceClass[] SelectedRaces = new RaceClass[4];
    public static List<RuntimeRaceDefinition> RaceDefinitions = new List<RuntimeRaceDefinition>();
    public static string[] SelectedRaceNames = new string[4];

    private void Awake()
    {
        classes = SerializedClasses;
        RaceDefinitions = new List<RuntimeRaceDefinition>();
        foreach (RaceClass race in classes) RaceDefinitions.Add(new RuntimeRaceDefinition(race));
    }

    public static void SelectRace(int playerIndex, RaceClass race)
    {
        SelectedRaces[playerIndex] = race;
        SelectedRaceNames[playerIndex] = race.className;
    }

    public void Start()
    {
        classes = SerializedClasses;
        for (int i = 0; i < 4; i++)
        {
            SelectRace(i, classes[0]);
        }
        map_height = 10;
        map_width = 10;
        player3IsOn = false;
        player4IsOn = false;
        build_mode = false;
        battle_royale_mode = false;
        firstMenu.SetActive(true);
        mapMenu.SetActive(false);
        createmapMenu.SetActive(false);
    }

    public void PressPlay()
    {
        firstMenu.SetActive(false);
        createmapMenu.SetActive(false);
        mapMenu.SetActive(true);
    }

    public void PressCreateMap()
    {
        firstMenu.SetActive(false);
        mapMenu.SetActive(false);
        createmapMenu.SetActive(true);
    }

    public void PressLoad()
    {
        firstMenu.SetActive(false);
        mapMenu.SetActive(true);
    }

    public void PressStart()
    {
        SceneManager.LoadScene(1);
    }

    public void PressStartCreateMap()
    {
        SceneManager.LoadScene(2);
    }

    public void PressBack()
    {
        firstMenu.SetActive(true);
        mapMenu.SetActive(false);
        createmapMenu.SetActive(false);
    }

    public void Update_height()
    {
        map_height = Convert.ToInt32(Math.Floor(height_slider.value));
        height_value.text = "Height: " + map_height;
    }

    public void Update_width()
    {
        map_width = Convert.ToInt32(Math.Floor(width_slider.value));
        width_value.text = "Width: " + map_width;
    }

    public void UpdateCreateWidth(float value)
    {
        map_width = Convert.ToInt32(Math.Floor(value));
        create_width_value.text = "Width: " + map_width;
    }

    public void UpdateCreateHeight(float value)
    {
        map_height = Convert.ToInt32(Math.Floor(value));
        create_height_value.text = "Height: " + map_height;
    }

    public void Add_player(int option)
    {
        switch (option)
        {
            case 3: 
                player3_info.SetActive(true); 
                player3IsOn = true;
                break;
            case 4:
                player4_info.SetActive(true);
                player4IsOn = true;
                break;
            default:
                break;
        }
    }

    public void Remove_player(int option)
    {
        switch(option)
        {
            case 3:
                player3_info.SetActive(false);
                player3IsOn = false;
                break;
            case 4:
                player4_info.SetActive(false);
                player4IsOn = false;
                break;
            default:
                break;
        }
    }

    public void Toggle_build_mode()
    {
        if (build_toggle.isOn)
        {
            build_mode = true;
        }
        else
        {
            build_mode = false;
            br_toggle.isOn = false;
            battle_royale_mode = false;
        }
    }

    public void Toggle_br_mode()
    {
        if (br_toggle.isOn)
        {
            build_toggle.isOn = true;
            build_mode = true;
            battle_royale_mode = true;
        }
        else
        {
            battle_royale_mode = false;
        }
    }

    public void Quit_game()
    {
        Application.Quit();
    }
}
