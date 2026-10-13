using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CustomMapSelect : MonoBehaviour
{
    private MainMenu menu;
    public Button choose, manage;
    public TMP_Text summary;
    public MapUiDialogs dialogs;
    public void Initialize(MainMenu target)
    {
        menu = target;
        // The editor may hide the container; its inactive modal children stay hidden.
        dialogs.gameObject.SetActive(true);
        choose.onClick.AddListener(OpenLibrary); manage.onClick.AddListener(OpenManager);
        Refresh();
    }
    public void Refresh()
    {
        var data = MainMenu.SelectedCustomMap; bool custom = data != null;
        menu.width_slider.transform.parent.gameObject.SetActive(!custom); menu.height_slider.transform.parent.gameObject.SetActive(!custom);
        choose.GetComponentInChildren<TMP_Text>().text = custom ? "Map: " + data.name : "Map: Generated";
        summary.gameObject.SetActive(custom);
        if (custom) summary.text = data.width + " × " + data.height + "\n" + data.playerCount + " participants\nCustom map";
        foreach (var button in menu.mapMenu.GetComponentsInChildren<Button>(true))
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                string method = button.onClick.GetPersistentMethodName(i);
                if (method == "Add_player" || method == "Remove_player") button.gameObject.SetActive(!custom);
            }
        }
    }
    private void OpenLibrary()
    {
        dialogs.library.Show(true, id =>
        {
            try { menu.SelectCustomMap(new CustomMapRepository(Application.persistentDataPath).Read(id)); }
            catch (Exception exception) { ShowError(exception.Message); }
        }, () => { }, () => menu.SelectCustomMap(null), menu.EditCustomMap, MapChanged);
    }
    private void OpenManager()
    {
        dialogs.library.Show(false, menu.EditCustomMap, () => { }, edit: menu.EditCustomMap, mapsChanged: MapChanged);
    }
    private void MapChanged(string id)
    {
        var selected = MainMenu.SelectedCustomMap;
        if (selected == null || selected.id != id) return;
        var repository = new CustomMapRepository(Application.persistentDataPath);
        menu.SelectCustomMap(repository.Exists(id) ? repository.Read(id) : null);
    }
    public void ShowError(string error)
    {
        dialogs.error.Show(error, dialogs.error.Hide);
    }
}
