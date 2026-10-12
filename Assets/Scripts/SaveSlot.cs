using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

public class SaveSlot : MonoBehaviour
{
    public TextMeshProUGUI saveName;
    public TextMeshProUGUI saveDate;
    public LoadMenu menu;

    public void SetSaveSlot(SaveInfo saveInfo)
    {
        saveName.text = saveInfo.saveName;
        saveDate.text = saveInfo.date.ToString();
    }

    public void DeleteSave()
    {
        try
        {
            new SaveGameRepository(Application.persistentDataPath).Delete(saveName.text);
            menu.RefreshSaves();
        }
        catch (System.Exception exception) { Debug.LogWarning("Could not delete save: " + exception.Message); }
    }

    public void LoadSave()
    {
        menu.LoadSave(saveName.text);
    }
}
