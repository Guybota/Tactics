using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Tactics.Core;

public sealed class MapEditorMenu : MonoBehaviour
{
    private MapCreator creator;
    public TMP_InputField mapName;
    public TMP_Text status;
    public Button participants, owner, save, load, validate, mainMenu, eraseStart;
    public Button[] startButtons = new Button[4];
    public MapUiDialogs dialogs;
    public Color neutralOwnerColor = new Color(.92f, .82f, .64f);
    private bool modalOpen;
    public bool IsModalOpen
    {
        get => modalOpen;
        private set
        {
            modalOpen = value;
            var camera = creator.game_camera.GetComponent<scr_camera>();
            if (value) camera.Lock_camera(); else camera.Unlock_camera();
        }
    }
    public void Initialize(MapCreator target)
    {
        creator = target;
        dialogs.gameObject.SetActive(true);
        mapName.onValueChanged.AddListener(creator.SetMapName);
        participants.onClick.AddListener(() => creator.SetPlayerCount(creator.PlayerCount == 4 ? 2 : creator.PlayerCount + 1));
        save.onClick.AddListener(Save); load.onClick.AddListener(Load); validate.onClick.AddListener(Validate);
        mainMenu.onClick.AddListener(() => ConfirmDiscard(() => SceneManager.LoadScene(0)));
        eraseStart.onClick.AddListener(creator.SelectEraseStart); owner.onClick.AddListener(creator.CycleCastleOwner);
        for (int i = 0; i < startButtons.Length; i++) { int slot = i; startButtons[i].onClick.AddListener(() => creator.SelectStart(slot)); }
        creator.Changed += Refresh; Refresh();
    }
    private void OnDestroy() { if (creator != null) creator.Changed -= Refresh; }
    private void Update()
    {
        if (Input.GetButtonUp("Cancel") && !UiBackAction.TryCloseTop()) ConfirmDiscard(() => SceneManager.LoadScene(0));
    }
    private void Refresh()
    {
        mapName.SetTextWithoutNotify(creator.MapName);
        participants.GetComponentInChildren<TMP_Text>().text = creator.PlayerCount + " participants";
        for (int i = 0; i < startButtons.Length; i++) startButtons[i].interactable = i < creator.PlayerCount;
        owner.GetComponentInChildren<TMP_Text>().text = "Castle owner\n" + (creator.CastleOwnerBrush < 0 ? "Neutral" : "P" + (creator.CastleOwnerBrush + 1));
        owner.image.color = creator.CastleOwnerBrush < 0 ? neutralOwnerColor : Color.Lerp(neutralOwnerColor, CustomMapUi.PlayerColor(creator.CastleOwnerBrush), .45f);
        status.text = (creator.HasUnsavedChanges ? "Unsaved · " : "Saved · ") + creator.BrushLabel + " · " + creator.gameMap.max_x + " × " + creator.gameMap.max_y;
    }
    private void Save()
    {
        if (creator.TrySaveMap(out string error)) status.text = "Map saved" + (CustomMapValidator.PlayIssues(creator.CaptureMap()).Count > 0 ? " as a draft." : ". Ready to play.");
        else status.text = "Could not save: " + error;
    }
    private void Validate()
    {
        var issues = CustomMapValidator.PlayIssues(creator.CaptureMap()); IsModalOpen = true;
        var view = dialogs.validation; Action close = () => { view.Hide(); IsModalOpen = false; };
        view.Show(issues.Count == 0 ? "Ready to play." : string.Join("\n", issues), close, close);
    }
    private void Load() => ConfirmDiscard(() =>
    {
        IsModalOpen = true;
        dialogs.library.Show(false, id =>
        {
            IsModalOpen = false;
            if (!creator.TryLoadMap(id, out string error)) status.text = "Could not load: " + error;
        }, () => IsModalOpen = false, mapsChanged: creator.SavedMapChanged);
    });
    private void ConfirmDiscard(Action continuation)
    {
        if (!creator.HasUnsavedChanges) { continuation(); return; }
        var view = dialogs.unsaved; IsModalOpen = true;
        Action close = () => { view.Hide(); IsModalOpen = false; };
        view.Show("Save your changes before continuing?", () =>
        {
            if (!creator.TrySaveMap(out string error)) { view.message.text = "Could not save: " + error; return; }
            close(); continuation();
        }, close, () => { close(); continuation(); });
    }
}
