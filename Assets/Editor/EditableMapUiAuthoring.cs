using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Explicit editor migration. Runtime controllers only bind these serialized views.
public static class EditableMapUiAuthoring
{
    private const string Folder = "Assets/Prefabs/UI";
    [MenuItem("Tactics/UI/Add editable map interface to scenes")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring UI.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
        BakeUiPrefabs();
        var original = SceneManager.GetActiveScene();
        foreach (string path in new[] { "Assets/Scenes/Main Menu.unity", "Assets/Scenes/Map Creator.unity", "Assets/Scenes/Main Scene.unity" })
        {
            var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                if (scene.isDirty)
                    EditorSceneManager.SaveScene(scene, "Assets/_Recovery/Before editable UI - " + scene.name + " - " + Guid.NewGuid().ToString("N") + ".unity", true);
                if (scene.name == "Main Menu") BuildMenu(scene);
                if (scene.name == "Map Creator") BuildEditor(scene);
                BakeListTemplates(scene);
                BakeTouchTargets(scene.GetRootGameObjects());
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var helper in root.GetComponentsInChildren<MobileUiRoot>(true))
                    {
                        var script = MonoScript.FromMonoBehaviour(helper);
                        if (script == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(script))) Undo.DestroyObjectImmediate(helper);
                    }
                    foreach (var camera in root.GetComponentsInChildren<scr_camera>(true))
                        if (camera.GetComponent<WorldTouchInput>() == null) Undo.AddComponent<WorldTouchInput>(camera.gameObject);
                    foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                        if (canvas.isRootCanvas && canvas.GetComponent<CanvasScaler>() != null && canvas.GetComponent<MobileUiRoot>() == null)
                            Undo.AddComponent<MobileUiRoot>(canvas.gameObject);
                }
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        if (original.isLoaded) SceneManager.SetActiveScene(original);
        Debug.Log("Editable map UI saved in the scenes. Templates are under Assets/Prefabs/UI.");
    }
    private static T InScene<T>(Scene scene) where T : Component
        => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).First();
    private static RectTransform Root(Transform parent, string name)
    { var rect = CustomMapUi.Rect(parent, name, Vector2.zero, Vector2.zero); CustomMapUi.Stretch(rect); return rect; }
    private static MapDialogView Dialog(Transform parent, string title)
    {
        var shade = Root(parent, title + " Overlay"); shade.gameObject.SetActive(false);
        var canvas = shade.gameObject.AddComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingOrder = 100;
        shade.gameObject.AddComponent<GraphicRaycaster>(); shade.gameObject.AddComponent<UiBackAction>();
        CustomMapUi.Background(shade, new Color(0, 0, 0, .65f));
        var view = shade.gameObject.AddComponent<MapDialogView>();
        view.panel = CustomMapUi.Rect(shade, title, new Vector2(700, 640), Vector2.zero); CustomMapUi.PaperBackground(view.panel);
        view.title = CustomMapUi.Label(view.panel, "Title", title, new Vector2(620, 65), new Vector2(0, 265), 54);
        return view;
    }
    private static Button Button(Transform parent, string name, string caption, Vector2 size, Vector2 position)
    {
        var button = CustomMapUi.Button(parent, name, caption, size, position, () => { });
        MapButtonUiAuthoring.Style(button);
        return button;
    }
    private static MapUiDialogs Dialogs(Canvas canvas)
    {
        var existing = canvas.GetComponentInChildren<MapUiDialogs>(true); if (existing != null) return existing;
        string path = Folder + "/Map UI Dialogs.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null) return ((GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform)).GetComponent<MapUiDialogs>();
        var root = Root(canvas.transform, "Map UI Dialogs"); var hub = root.gameObject.AddComponent<MapUiDialogs>();
        var libraryView = Dialog(root, "Custom maps"); var library = libraryView.panel.gameObject.AddComponent<CustomMapLibrary>();
        hub.library = library; library.dialog = libraryView; library.dialogs = hub;
        var viewport = CustomMapUi.Rect(libraryView.panel, "Map list", new Vector2(620, 420), Vector2.zero);
        CustomMapUi.Background(viewport, new Color(.3f, .2f, .1f, .06f)); viewport.gameObject.AddComponent<RectMask2D>();
        library.content = CustomMapUi.Rect(viewport, "Content", Vector2.zero, Vector2.zero, new Vector2(.5f, 1));
        library.content.pivot = new Vector2(.5f, 1); library.content.anchorMin = new Vector2(0, 1); library.content.anchorMax = Vector2.one;
        var layout = library.content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 8; layout.padding = new RectOffset(8, 8, 8, 8);
        layout.childControlHeight = layout.childControlWidth = layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        library.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = viewport.gameObject.AddComponent<SmoothMenuScroll>(); scroll.viewport = viewport; scroll.content = library.content;
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.decelerationRate = .08f;
        var rowRoot = CustomMapUi.Rect(library.content, "Map entry template", new Vector2(600, 160), Vector2.zero);
        library.rowTemplate = rowRoot.gameObject.AddComponent<CustomMapRowView>(); var row = library.rowTemplate;
        rowRoot.gameObject.AddComponent<LayoutElement>().preferredHeight = 160;
        row.select = Button(rowRoot, "Select map", "Map name\n10 × 10 · 2 players\nReady", new Vector2(600, 100), Vector2.zero);
        CustomMapUi.Stretch((RectTransform)row.select.transform);
        row.caption = row.select.GetComponentInChildren<TMP_Text>(); row.caption.fontSizeMax = 30;
        row.caption.rectTransform.offsetMin = new Vector2(12, 4); row.caption.rectTransform.offsetMax = new Vector2(-250, -4); row.caption.alignment = TextAlignmentOptions.MidlineLeft;
        row.edit = Button(rowRoot, "Edit map", "Edit", new Vector2(108, 70), new Vector2(-180, 39));
        row.rename = Button(rowRoot, "Rename map", "Rename", new Vector2(108, 70), new Vector2(-180, -39));
        row.delete = Button(rowRoot, "Delete map", "Delete", new Vector2(108, 88), new Vector2(-60, 0));
        foreach (var button in new[] { row.edit, row.rename, row.delete }) ((RectTransform)button.transform).anchorMin = ((RectTransform)button.transform).anchorMax = new Vector2(1, .5f);
        SavedMapRowUiAuthoring.StyleRow(row);
        rowRoot.gameObject.SetActive(false); BakeTouchTargets(new[] { rowRoot.gameObject });
        PrefabUtility.SaveAsPrefabAssetAndConnect(rowRoot.gameObject, Folder + "/Map Entry.prefab", InteractionMode.AutomatedAction);
        library.status = CustomMapUi.Label(libraryView.panel, "Status", "", new Vector2(620, 48), new Vector2(0, -240), 24);
        library.close = Button(libraryView.panel, "Close", "Close", new Vector2(180, 50), new Vector2(150, -285));
        library.generated = Button(libraryView.panel, "Generated", "Generated map", new Vector2(260, 50), new Vector2(-140, -285));
        hub.error = Dialog(root, "Cannot start map"); hub.error.message = CustomMapUi.Label(hub.error.panel, "Error", "Map error", new Vector2(620, 390), Vector2.zero, 32);
        hub.error.primary = Button(hub.error.panel, "Close", "Close", new Vector2(180, 50), new Vector2(0, -285));
        hub.validation = Dialog(root, "Map validation"); hub.validation.message = CustomMapUi.Label(hub.validation.panel, "Result", "Validation result", new Vector2(620, 390), Vector2.zero, 32);
        hub.validation.primary = Button(hub.validation.panel, "Close", "Close", new Vector2(180, 50), new Vector2(0, -285));
        hub.unsaved = Dialog(root, "Unsaved map"); hub.unsaved.message = CustomMapUi.Label(hub.unsaved.panel, "Message", "Save your changes before continuing?", new Vector2(620, 160), Vector2.zero, 36);
        hub.unsaved.primary = Button(hub.unsaved.panel, "Save and continue", "Save & continue", new Vector2(230, 55), new Vector2(-200, -200));
        hub.unsaved.tertiary = Button(hub.unsaved.panel, "Discard", "Discard", new Vector2(170, 55), new Vector2(35, -200));
        hub.unsaved.secondary = Button(hub.unsaved.panel, "Cancel", "Cancel", new Vector2(150, 55), new Vector2(220, -200));
        hub.rename = Dialog(root, "Rename map"); hub.rename.input = CustomMapUi.NameInput(hub.rename.panel, new Vector2(580, 60), new Vector2(0, 80)); hub.rename.input.name = "Rename map input";
        hub.rename.message = CustomMapUi.Label(hub.rename.panel, "Rename status", "", new Vector2(580, 100), new Vector2(0, -40), 30);
        hub.rename.primary = Button(hub.rename.panel, "Confirm rename map", "Save name", new Vector2(210, 55), new Vector2(-130, -220));
        hub.rename.secondary = Button(hub.rename.panel, "Cancel rename map", "Cancel", new Vector2(180, 55), new Vector2(130, -220));
        hub.delete = Dialog(root, "Delete map"); hub.delete.message = CustomMapUi.Label(hub.delete.panel, "Delete message", "Permanently delete this map?", new Vector2(580, 180), new Vector2(0, 40), 36);
        hub.delete.primary = Button(hub.delete.panel, "Confirm delete map", "Delete", new Vector2(210, 55), new Vector2(-130, -220));
        hub.delete.secondary = Button(hub.delete.panel, "Cancel delete map", "Cancel", new Vector2(180, 55), new Vector2(130, -220));
        BakeTouchTargets(new[] { root.gameObject });
        PrefabUtility.SaveAsPrefabAssetAndConnect(root.gameObject, path, InteractionMode.AutomatedAction);
        return hub;
    }
    private static void BuildMenu(Scene scene)
    {
        var menu = InScene<MainMenu>(scene); var canvas = menu.mapMenu.GetComponentInParent<Canvas>().rootCanvas;
        var view = menu.GetComponent<CustomMapSelect>() ?? Undo.AddComponent<CustomMapSelect>(menu.gameObject);
        if (view.choose == null)
        {
            view.choose = Button(menu.mapMenu.transform, "Choose map", "Map: Generated", new Vector2(350, 38), new Vector2(0, 3));
            view.choose.GetComponentInChildren<TMP_Text>().fontSizeMax = 28;
            view.manage = Button(menu.createmapMenu.transform, "Manage saved maps", "Saved maps", new Vector2(350, 48), new Vector2(0, -170));
            view.summary = CustomMapUi.Label(menu.mapMenu.transform, "Custom map summary", "Custom map details", new Vector2(350, 145), new Vector2(0, -115), 32); view.summary.gameObject.SetActive(false);
            menu.height_value.rectTransform.sizeDelta = new Vector2(menu.height_value.rectTransform.sizeDelta.x, 32);
            menu.width_value.rectTransform.sizeDelta = new Vector2(menu.width_value.rectTransform.sizeDelta.x, 32);
            menu.height_value.fontSize = menu.width_value.fontSize = 28;
        }
        view.dialogs = Dialogs(canvas); EditorUtility.SetDirty(view);
    }
    private static void BuildEditor(Scene scene)
    {
        var creator = InScene<MapCreator>(scene); var view = creator.GetComponent<MapEditorMenu>() ?? Undo.AddComponent<MapEditorMenu>(creator.gameObject);
        var terrain = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RectTransform>(true)).First(rect => rect.name == "Terrain Tools");
        var canvas = terrain.GetComponentInParent<Canvas>(); view.dialogs = Dialogs(canvas);
        if (view.mapName == null)
        {
            var toolbar = CustomMapUi.Rect(canvas.transform, "Map editor toolbar", new Vector2(1120, 130), new Vector2(0, -120), new Vector2(.5f, 1));
            CustomMapUi.PaperBackground(toolbar);
            view.mapName = CustomMapUi.NameInput(toolbar, new Vector2(340, 50), new Vector2(-365, 25)); view.mapName.text = "Untitled map";
            view.participants = Button(toolbar, "Participants", "2 participants", new Vector2(190, 50), new Vector2(-75, 25));
            view.save = Button(toolbar, "Save map", "Save", new Vector2(110, 50), new Vector2(100, 25));
            view.load = Button(toolbar, "Load map", "Load", new Vector2(110, 50), new Vector2(225, 25));
            view.validate = Button(toolbar, "Validate map", "Validate", new Vector2(145, 50), new Vector2(370, 25));
            view.mainMenu = Button(toolbar, "Main menu", "Menu", new Vector2(100, 50), new Vector2(510, 25));
            CustomMapUi.Rect(toolbar, "Status background", new Vector2(1100, 52), new Vector2(0, -35));
            view.status = CustomMapUi.Label(toolbar, "Editor status", "Editor status", new Vector2(1100, 52), new Vector2(0, -35), 28);
            var palette = CustomMapUi.Rect(canvas.transform, "Start tools", new Vector2(650, 58), new Vector2(0, 200), new Vector2(.5f, 0));
            CustomMapUi.PaperBackground(palette);
            for (int i = 0; i < 4; i++)
            {
                view.startButtons[i] = Button(palette, "P" + (i + 1) + " start", "P" + (i + 1) + " start", new Vector2(112, 50), new Vector2(-255 + i * 126, 0));
                view.startButtons[i].GetComponentInChildren<TMP_Text>().color = CustomMapUi.PlayerColor(i) * .5f + CustomMapUi.Ink * .5f;
            }
            view.eraseStart = Button(palette, "Erase start", "Erase start", new Vector2(140, 50), new Vector2(250, 0));
            view.owner = Button(terrain.parent, "Castle owner", "Castle owner\nNeutral", new Vector2(190, 75), Vector2.zero);
            var owner = (RectTransform)view.owner.transform; owner.anchorMin = terrain.anchorMin; owner.anchorMax = terrain.anchorMax;
            owner.anchoredPosition = terrain.anchoredPosition + new Vector2(-terrain.sizeDelta.x / 2 - 110, 0);
        }
        var serialized = new SerializedObject(creator);
        if (serialized.FindProperty("markerRoot").objectReferenceValue == null)
        {
            var markers = new GameObject("Participant starts"); markers.transform.SetParent(creator.gameMap.transform, false);
            var marker = new GameObject("Participant start template").AddComponent<TextMeshPro>(); marker.transform.SetParent(creator.gameMap.transform, false);
            marker.transform.localPosition = new Vector3(0, 12, -3);
            marker.rectTransform.sizeDelta = new Vector2(64, 50); marker.fontSize = 300; marker.font = TMP_Settings.defaultFontAsset;
            marker.alignment = TextAlignmentOptions.Center; marker.text = "P1"; marker.renderer.sortingOrder = 100; marker.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAssetAndConnect(marker.gameObject, Folder + "/Participant Start.prefab", InteractionMode.AutomatedAction);
            serialized.FindProperty("markerRoot").objectReferenceValue = markers.transform;
            serialized.FindProperty("startMarkerTemplate").objectReferenceValue = marker;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(view);
    }
    private static void BakeTouchTargets(GameObject[] roots)
    {
        foreach (var root in roots) foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
        {
            var component = selectable.GetComponent<MobileTouchTarget>() ?? selectable.gameObject.AddComponent<MobileTouchTarget>();
            var serialized = new SerializedObject(component); var property = serialized.FindProperty("area");
            var existing = property.objectReferenceValue as Image;
            var hit = existing != null ? existing.rectTransform : selectable.transform.Find("Touch hit area") as RectTransform;
            if (hit == null) { hit = CustomMapUi.Rect(selectable.transform, "Touch hit area", Vector2.zero, Vector2.zero); CustomMapUi.Stretch(hit); hit.SetAsFirstSibling(); }
            var image = hit.GetComponent<Image>() ?? CustomMapUi.Background(hit, Color.clear); image.raycastTarget = true;
            var layout = hit.GetComponent<LayoutElement>() ?? hit.gameObject.AddComponent<LayoutElement>(); layout.ignoreLayout = true;
            property.objectReferenceValue = image; serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
    private static void BakeUiPrefabs()
    {
        foreach (string path in new[] { "Assets/Prefabs/Save Slot.prefab", "Assets/Prefabs/Unit Button.prefab", Folder + "/Map Entry.prefab", Folder + "/Map UI Dialogs.prefab" })
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try { BakeTouchTargets(new[] { root }); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
    private static void BakeListTemplates(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var load in root.GetComponentsInChildren<LoadMenu>(true))
            {
                if (!EditorUtility.IsPersistent(load.saveSlot)) continue;
                var sample = (GameObject)PrefabUtility.InstantiatePrefab(load.saveSlot, load.panel.transform);
                sample.name = "Save slot template"; sample.SetActive(false); load.saveSlot = sample; EditorUtility.SetDirty(load);
            }
            foreach (var spawn in root.GetComponentsInChildren<SpawnMenu>(true))
            {
                var data = new SerializedObject(spawn); var reference = data.FindProperty("unitButton");
                var prefab = reference.objectReferenceValue as GameObject; if (prefab == null || !EditorUtility.IsPersistent(prefab)) continue;
                var sample = (GameObject)PrefabUtility.InstantiatePrefab(prefab, spawn.spawnPanel.transform);
                sample.name = "Recruitment entry template"; sample.SetActive(false); reference.objectReferenceValue = sample;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
