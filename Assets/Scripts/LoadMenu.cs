using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class LoadMenu : MonoBehaviour
{
    public List<GameObject> saves = new List<GameObject>();

    public GameObject panel;
    public GameObject saveSlot;
    public scr_game_contoller gameContoller;
    public MainMenu mainMenu;
    [SerializeField] private TextMeshProUGUI message;

    private void Start()
    {
        RefreshSaves();
    }

    public void RefreshSaves()
    {
        if (saveSlot.scene.IsValid()) saveSlot.SetActive(false);
        foreach (GameObject save in saves) { save.SetActive(false); Destroy(save); }
        saves.Clear();
        var repository = new SaveGameRepository(Application.persistentDataPath);
        foreach (SaveInfo info in repository.List())
        {
            GameObject slot = Instantiate(saveSlot, panel.transform);
            slot.GetComponent<SaveSlot>().SetSaveSlot(info);
            slot.GetComponent<SaveSlot>().menu = this;
            slot.SetActive(true);
            saves.Add(slot);
        }
        if (message != null)
        {
            message.text = saves.Count == 0 ? "No saved games yet." : "";
            message.rectTransform.anchoredPosition = new Vector2(0, saves.Count == 0 ? 0 : -245);
        }
        var scroll = panel.GetComponentInParent<ScrollRect>();
        if (scroll != null)
        {
            scroll.StopMovement(); Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)panel.transform);
            scroll.verticalNormalizedPosition = 1;
        }
    }

    public void LoadSave(string saveName)
    {
        if (gameContoller != null) gameContoller.LoadSave(saveName);
        else if (mainMenu != null && !mainMenu.TryLoadSavedGame(saveName, out string error))
        {
            if (message != null)
            {
                message.text = "This save could not be loaded.";
                message.rectTransform.anchoredPosition = new Vector2(0, -245);
            }
            Debug.LogWarning("Could not load the game: " + error);
        }
    }
}
