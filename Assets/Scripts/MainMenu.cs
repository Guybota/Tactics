using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Tactics.Core;

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
    [SerializeField] private RaceCatalog contentCatalog;

    public Toggle build_toggle;
    public Toggle br_toggle;

    public GameObject firstMenu;
    public GameObject mapMenu;
    public GameObject createmapMenu;
    public GameObject player3_info;
    public GameObject player4_info;
    [SerializeField] private LoadMenu loadMenu;
    [SerializeField] private SettingsMenu settingsMenu;
    private static GameSave pendingSave;
    private static CustomMapData selectedCustomMap;
    private static CustomMapData pendingEditorMap;
    public static CustomMapData SelectedCustomMap => selectedCustomMap?.Clone();
    private CustomMapSelect customMapSelect;
    internal static GameSave TakePendingSave() { var save = pendingSave; pendingSave = null; return save; }
    internal static CustomMapData TakePendingEditorMap() { var map = pendingEditorMap; pendingEditorMap = null; return map; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEditorRequest() => pendingEditorMap = null;
    public void EditCustomMap(string id)
    {
        pendingEditorMap = new CustomMapRepository(Application.persistentDataPath).Read(id);
        map_width = pendingEditorMap.width; map_height = pendingEditorMap.height;
        SceneManager.LoadScene(2);
    }

    public static RaceClass[] SelectedRaces = new RaceClass[4];
    public static List<RuntimeRaceDefinition> RaceDefinitions = new List<RuntimeRaceDefinition>();
    public static string[] SelectedRaceNames = new string[4];
    public static bool[] BotPlayers = new bool[4];

    public static void SetBotPlayer(int slot, bool isBot)
    {
        if (slot < 0 || slot >= 4) throw new ArgumentOutOfRangeException(nameof(slot));
        BotPlayers[slot] = isBot;
    }

    private void Awake()
    {
        selectedCustomMap = null;
        for (int i = 0; i < 4; i++) BotPlayers[i] = false;
        classes = SerializedClasses;
        if (contentCatalog != null)
        {
            contentCatalog.Validate();
            classes = new List<RaceClass>();
            foreach (var definition in contentCatalog.Races)
            {
                var race = SerializedClasses.Find(candidate => candidate != null && candidate.Definition == definition);
                if (race == null)
                {
                    var holder = new GameObject(definition.DisplayName + " Definition");
                    holder.transform.SetParent(transform, false);
                    race = holder.AddComponent<RaceClass>();
                }
                race.Bind(definition);
                classes.Add(race);
            }
        }
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
        build_toggle.SetIsOnWithoutNotify(false);
        br_toggle.SetIsOnWithoutNotify(false);
        firstMenu.SetActive(true);
        mapMenu.SetActive(false);
        createmapMenu.SetActive(false);
        customMapSelect = GetComponent<CustomMapSelect>(); customMapSelect.Initialize(this);
    }

    public void PressPlay()
    {
        HideExtraMenus();
        firstMenu.SetActive(false);
        createmapMenu.SetActive(false);
        mapMenu.SetActive(true);
    }
    private void Update()
    {
        if (Input.GetButtonUp("Cancel") && !UiBackAction.TryCloseTop() && !firstMenu.activeSelf) PressBack();
    }

    public void PressCreateMap()
    {
        HideExtraMenus();
        firstMenu.SetActive(false);
        mapMenu.SetActive(false);
        createmapMenu.SetActive(true);
    }

    public void PressLoad()
    {
        firstMenu.SetActive(false);
        mapMenu.SetActive(false); createmapMenu.SetActive(false);
        if (settingsMenu != null) settingsMenu.gameObject.SetActive(false);
        loadMenu.gameObject.SetActive(true); loadMenu.RefreshSaves();
    }

    public void PressSettings()
    {
        firstMenu.SetActive(false); mapMenu.SetActive(false); createmapMenu.SetActive(false);
        if (loadMenu != null) loadMenu.gameObject.SetActive(false);
        settingsMenu.gameObject.SetActive(true);
    }
    private void HideExtraMenus()
    {
        if (loadMenu != null) loadMenu.gameObject.SetActive(false);
        if (settingsMenu != null) settingsMenu.gameObject.SetActive(false);
    }
    public bool TryLoadSavedGame(string name, out string error)
    {
        error = null;
        try
        {
            var save = new SaveGameRepository(Application.persistentDataPath).Read(name);
            var races = new Dictionary<string, RaceClass>();
            foreach (var race in classes) races[race.className] = race;
            SaveGameValidator.Validate(save, races);
            pendingSave = save;
            SceneManager.LoadScene(1);
            return true;
        }
        catch (Exception exception) { error = exception.Message; return false; }
    }

    public void PressStart()
    {
        if (selectedCustomMap != null)
        {
            try { CustomMapValidator.ValidatePlayable(selectedCustomMap); ApplyMapParticipantCount(selectedCustomMap.playerCount); }
            catch (Exception exception) { customMapSelect.ShowError(exception.Message); return; }
        }
        pendingSave = null;
        SceneManager.LoadScene(1);
    }

    public void SelectCustomMap(CustomMapData data)
    {
        if (data != null) CustomMapValidator.ValidateStructure(data);
        selectedCustomMap = data?.Clone();
        if (data != null)
        {
            map_width = data.width; map_height = data.height; ApplyMapParticipantCount(data.playerCount);
        }
        else { Update_width(); Update_height(); }
        customMapSelect?.Refresh();
    }
    private void ApplyMapParticipantCount(int count)
    {
        player3IsOn = count >= 3; player4IsOn = count >= 4;
        player3_info.SetActive(player3IsOn); player4_info.SetActive(player4IsOn);
    }

    public void PressStartCreateMap()
    {
        pendingEditorMap = null;
        SceneManager.LoadScene(2);
    }

    public void PressBack()
    {
        HideExtraMenus();
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
        if (selectedCustomMap != null) return;
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
        if (selectedCustomMap != null) return;
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
