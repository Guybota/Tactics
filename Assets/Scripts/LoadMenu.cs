using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class LoadMenu : MonoBehaviour
{
    public List<GameObject> saves = new List<GameObject>();

    public GameObject panel;
    public GameObject saveSlot;
    public scr_game_contoller gameContoller;

    private void Start()
    {
        RefreshSaves();
    }

    public void RefreshSaves()
    {
        foreach (GameObject save in saves) Destroy(save);
        saves.Clear();
        var repository = new SaveGameRepository(Application.persistentDataPath);
        foreach (SaveInfo info in repository.List())
        {
            GameObject slot = Instantiate(saveSlot, panel.transform);
            slot.GetComponent<SaveSlot>().SetSaveSlot(info);
            slot.GetComponent<SaveSlot>().menu = this;
            saves.Add(slot);
        }
    }

    public void LoadSave(string saveName)
    {
        gameContoller.LoadSave(saveName);
    }
}
