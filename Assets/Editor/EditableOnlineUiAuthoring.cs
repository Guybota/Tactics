using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Explicit one-time authoring only. Existing authored views and manual edits are preserved.
public static class EditableOnlineUiAuthoring
{
    [MenuItem("Tactics/UI/Add editable online interface to scenes")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring UI.");
        var original = SceneManager.GetActiveScene();
        foreach (var path in new[] { "Assets/Scenes/Main Menu.unity", "Assets/Scenes/Main Scene.unity" })
        {
            var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                if (scene.isDirty)
                {
                    System.IO.Directory.CreateDirectory("Assets/_Recovery");
                    EditorSceneManager.SaveScene(scene, "Assets/_Recovery/Before editable online UI - " + scene.name + " - " + Guid.NewGuid().ToString("N") + ".unity", true);
                }
                if (scene.name == "Main Menu") BuildMenu(InScene<MainMenu>(scene));
                else BuildHud(InScene<scr_game_contoller>(scene));
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        if (original.isLoaded) SceneManager.SetActiveScene(original);
        Debug.Log("Editable Online menu and HUD saved in Main Menu and Main Scene. Existing views are never rebuilt.");
    }
    private static T InScene<T>(Scene scene) where T : Component
        => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).First();
    private static UnityEngine.Object Read(UnityEngine.Object target, string name)
        => new SerializedObject(target).FindProperty(name).objectReferenceValue;
    private static void Write(UnityEngine.Object target, string name, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(target); serialized.FindProperty(name).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(target);
    }
    private static Button Button(Transform parent, string name, string caption, Vector2 size, Vector2 position, UnityAction action = null)
    {
        var button = CustomMapUi.Button(parent, name, caption, size, position, () => { });
        button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        if (action != null) UnityEventTools.AddPersistentListener(button.onClick, action);
        TacticsUiSkin.Current.ApplyButton(button); ((Image)button.targetGraphic).color = Color.white;
        button.GetComponent<Outline>().enabled = false; button.gameObject.AddComponent<PaperButtonLabel>();
        BakeTouchTarget(button);
        return button;
    }
    private static void BakeTouchTarget(Selectable control)
    {
        var target = control.GetComponent<MobileTouchTarget>() ?? control.gameObject.AddComponent<MobileTouchTarget>();
        if (Read(target, "area") != null) return;
        var hit = CustomMapUi.Rect(control.transform, "Touch hit area", Vector2.zero, Vector2.zero);
        CustomMapUi.Stretch(hit); hit.SetAsFirstSibling(); hit.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Write(target, "area", CustomMapUi.Background(hit, Color.clear));
    }
    private static TMP_InputField Input(Transform parent, string name, Vector2 position, Vector2 size, string placeholder)
    {
        var input = CustomMapUi.NameInput(parent, size, position); input.name = name;
        input.characterLimit = 64; input.lineType = TMP_InputField.LineType.SingleLine;
        ((TMP_Text)input.placeholder).text = placeholder;
        input.GetComponent<Image>().color = new Color(.99f, .92f, .77f);
        input.gameObject.AddComponent<Outline>().effectColor = CustomMapUi.Ink;
        BakeTouchTarget(input); return input;
    }
    private static void BuildMenu(MainMenu menu)
    {
        var view = menu.GetComponent<OnlineMatchMenu>() ?? Undo.AddComponent<OnlineMatchMenu>(menu.gameObject);
        Write(menu, "onlineMenu", view);
        if (Read(menu, "onlineButton") == null)
        {
            var template = menu.firstMenu.GetComponentsInChildren<Button>(true).Single(b =>
                Enumerable.Range(0, b.onClick.GetPersistentEventCount()).Any(i => b.onClick.GetPersistentMethodName(i) == "PressPlay"));
            var entry = UnityEngine.Object.Instantiate(template, template.transform.parent);
            entry.name = "Online multiplayer button";
            entry.onClick = new Button.ButtonClickedEvent(); UnityEventTools.AddPersistentListener(entry.onClick, menu.PressOnline);
            entry.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            entry.GetComponentInChildren<TMP_Text>().text = "Online";
            Write(menu, "onlineButton", entry);
        }
        if (Read(view, "overlay") != null) return;
        var canvas = menu.firstMenu.GetComponentInParent<Canvas>().rootCanvas;
        var overlay = CustomMapUi.Rect(canvas.transform, "Online match menu", Vector2.zero, Vector2.zero);
        overlay.gameObject.SetActive(false); CustomMapUi.Stretch(overlay);
        CustomMapUi.Background(overlay, new Color(0, 0, 0, .7f));
        var sort = overlay.gameObject.AddComponent<Canvas>(); sort.overrideSorting = true; sort.sortingOrder = 90;
        overlay.gameObject.AddComponent<GraphicRaycaster>(); overlay.gameObject.AddComponent<UiBackAction>();
        var layout = overlay.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 20, 20); layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var panel = CustomMapUi.Rect(overlay, "Online menu paper", new Vector2(940, 920), Vector2.zero);
        CustomMapUi.PaperBackground(panel);
        var sizing = panel.gameObject.AddComponent<LayoutElement>(); sizing.preferredWidth = 940; sizing.preferredHeight = 920;
        sizing.minWidth = sizing.minHeight = 0; sizing.flexibleWidth = sizing.flexibleHeight = 0;
        var viewport = CustomMapUi.Rect(panel, "Viewport", Vector2.zero, Vector2.zero); CustomMapUi.Stretch(viewport, 16);
        CustomMapUi.Background(viewport, Color.clear); viewport.gameObject.AddComponent<RectMask2D>();
        var content = CustomMapUi.Rect(viewport, "Online menu content", new Vector2(900, 880), Vector2.zero, new Vector2(.5f, 1));
        content.pivot = new Vector2(.5f, 1);
        var scroll = panel.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 35;
        panel.gameObject.AddComponent<SmoothMenuScroll>();
        var heading = CustomMapUi.Label(content, "Online menu title", "Online matches", new Vector2(780, 65), new Vector2(0, 375), 54);
        var description = CustomMapUi.Label(content, "Online menu description", "Private 1v1 matches\nYour progress stays saved between turns.", new Vector2(780, 85), new Vector2(0, 310), 30);
        var setup = CustomMapUi.Rect(content, "Online match setup", Vector2.zero, Vector2.zero); CustomMapUi.Stretch(setup);
        var races = AssetDatabase.LoadAssetAtPath<RaceCatalog>("Assets/Data/RaceCatalog.asset").Races;
        var serialized = new SerializedObject(view); var buttons = serialized.FindProperty("raceButtons"); buttons.arraySize = races.Count;
        float width = 760f / races.Count;
        for (int i = 0; i < races.Count; i++)
        {
            var army = Button(setup, "Online army " + i, races[i].DisplayName, new Vector2(width - 10, 70), new Vector2(-380 + width * (i + .5f), 220));
            UnityEventTools.AddIntPersistentListener(army.onClick, view.ChooseArmy, i);
            buttons.GetArrayElementAtIndex(i).objectReferenceValue = army;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Write(view, "build", Button(setup, "Online build mode", "Build castles: Off", new Vector2(350, 70), new Vector2(-185, 140), view.ToggleBuildMode));
        Write(view, "storm", Button(setup, "Online storm mode", "Storm: Off", new Vector2(350, 70), new Vector2(185, 140), view.ToggleStormMode));
        Write(view, "create", Button(setup, "Online create match", "Create match", new Vector2(720, 70), new Vector2(0, 55), view.CreateFromButton));
        CustomMapUi.Label(setup, "Invitation prompt", "Have an invitation?", new Vector2(720, 45), new Vector2(0, -25), 32);
        Write(view, "invitation", Input(setup, "Online invitation input", new Vector2(-95, -85), new Vector2(530, 70), "Paste invitation"));
        Write(view, "join", Button(setup, "Online join match", "Join", new Vector2(170, 70), new Vector2(280, -85), view.JoinFromButton));
        Write(view, "resume", Button(setup, "Online resume match", "Resume last match", new Vector2(720, 70), new Vector2(0, -175), view.ResumeFromButton));
        var lobby = CustomMapUi.Rect(content, "Online waiting lobby", Vector2.zero, Vector2.zero); CustomMapUi.Stretch(lobby);
        CustomMapUi.Label(lobby, "Your invitation label", "Invitation", new Vector2(720, 40), new Vector2(0, 245), 32);
        var invitation = Input(lobby, "Online lobby invitation", new Vector2(0, 195), new Vector2(720, 70), ""); invitation.readOnly = true;
        Write(view, "lobbyInvitation", invitation);
        Write(view, "copy", Button(lobby, "Online copy invitation", "Copy invitation", new Vector2(720, 70), new Vector2(0, 110), view.CopyInvitation));
        Write(view, "refresh", Button(lobby, "Online refresh lobby", "Check lobby / retry", new Vector2(720, 70), new Vector2(0, 25), view.RefreshFromButton));
        Write(view, "open", Button(lobby, "Online open board", "Open match", new Vector2(720, 70), new Vector2(0, -60), view.OpenFromButton));
        Write(view, "another", Button(lobby, "Online another match", "Choose another match", new Vector2(720, 70), new Vector2(0, -175), view.ChooseAnotherMatch));
        Write(view, "status", CustomMapUi.Label(content, "Online menu status", "Create a private match, or enter an invitation from a friend.", new Vector2(720, 125), new Vector2(0, -285), 32));
        Button(content, "Online back", "Back", new Vector2(330, 70), new Vector2(0, -385), menu.PressBack);
        Write(view, "overlay", overlay); Write(view, "panel", panel); Write(view, "setup", setup); Write(view, "lobbyView", lobby);
        Write(view, "heading", heading); Write(view, "description", description); Write(view, "viewScroll", scroll);
        lobby.gameObject.SetActive(false); scroll.horizontalNormalizedPosition = .5f; scroll.verticalNormalizedPosition = 1;
    }
    private static void BuildHud(scr_game_contoller controller)
    {
        if (Read(controller, "onlineHudView") != null) return;
        var canvas = controller.uiManager.turn_info.GetComponentInParent<Canvas>();
        var panel = CustomMapUi.Rect(canvas.transform, "Online status", new Vector2(650, 140), new Vector2(-345, 85), new Vector2(1, 0));
        panel.gameObject.SetActive(false); CustomMapUi.PaperBackground(panel);
        var view = panel.gameObject.AddComponent<OnlineMatchHud>();
        Write(view, "status", CustomMapUi.Label(panel, "Status", "Online", new Vector2(620, 45), new Vector2(0, 40), 30));
        Write(view, "refresh", Button(panel, "Refresh", "Refresh", new Vector2(185, 70), new Vector2(-210, -25), view.RefreshFromButton));
        Write(view, "retry", Button(panel, "Retry", "Retry", new Vector2(185, 70), new Vector2(0, -25), view.RetryFromButton));
        Write(view, "copy", Button(panel, "Copy invite", "Copy invite", new Vector2(185, 70), new Vector2(210, -25), view.CopyFromButton));
        Write(controller, "onlineHudView", view);
    }
}
