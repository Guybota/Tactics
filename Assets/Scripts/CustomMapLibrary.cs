using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Tactics.Core;

public sealed class CustomMapLibrary : MonoBehaviour
{
    public MapDialogView dialog;
    public RectTransform content;
    public CustomMapRowView rowTemplate;
    public TMP_Text status;
    public Button close, generated;
    public MapUiDialogs dialogs;
    private CustomMapRepository repository;
    private bool forPlaying;
    private Action<string> selected, edited, changed;
    private Action closed;
    private readonly System.Collections.Generic.List<CustomMapRowView> rows = new System.Collections.Generic.List<CustomMapRowView>();
    public void Show(bool play, Action<string> select, Action onClose, Action generatedAction = null,
        Action<string> edit = null, Action<string> mapsChanged = null)
    {
        forPlaying = play; selected = select; closed = onClose; edited = edit ?? select; changed = mapsChanged;
        repository = new CustomMapRepository(Application.persistentDataPath);
        rowTemplate.gameObject.SetActive(false);
        close.onClick.RemoveAllListeners(); close.onClick.AddListener(Close);
        generated.onClick.RemoveAllListeners(); generated.gameObject.SetActive(generatedAction != null);
        if (generatedAction != null) generated.onClick.AddListener(() => { dialog.Hide(); generatedAction(); });
        dialog.GetComponent<UiBackAction>().Back = Close;
        dialog.gameObject.SetActive(true); Refresh(); Canvas.ForceUpdateCanvases();
    }
    private void Close() { dialog.Hide(); closed?.Invoke(); }
    private void Open(string id, Action<string> action)
    {
        try { action(id); dialog.Hide(); }
        catch (Exception error) { if (status != null) status.text = "Could not open map: " + error.Message; }
    }
    public void Refresh()
    {
        foreach (var row in rows) { row.gameObject.SetActive(false); Destroy(row.gameObject); }
        rows.Clear();
        status.text = "";
        try
        {
            var maps = repository.List();
            if (maps.Count == 0) status.text = "No maps saved. Create one in the map editor.";
            foreach (var map in maps)
            {
                var row = Instantiate(rowTemplate, content); row.name = "Map row " + map.id;
                rows.Add(row);
                row.Bind(map, forPlaying, () => Open(map.id, selected), () => Open(map.id, edited), () => Rename(map), () => RequestDelete(map));
                row.gameObject.SetActive(true);
            }
        }
        catch (Exception error) { status.text = "Could not read maps: " + error.Message; }
    }
    private void Rename(CustomMapData map)
    {
        var view = dialogs.rename; view.input.SetTextWithoutNotify(map.name);
        view.Show("", () =>
        {
            try { var current = repository.Read(map.id); current.name = view.input.text.Trim(); repository.Write(current); changed?.Invoke(map.id); view.Hide(); Refresh(); }
            catch (Exception error) { view.message.text = "Could not rename: " + error.Message; }
        }, view.Hide);
    }
    private void RequestDelete(CustomMapData map)
    {
        var view = dialogs.delete;
        view.Show("Permanently delete “" + map.name + "”?", () =>
        {
            try { repository.Delete(map.id); changed?.Invoke(map.id); view.Hide(); Refresh(); }
            catch (Exception error) { view.message.text = "Could not delete: " + error.Message; }
        }, view.Hide);
    }
}
