using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// An explicit styling tool: keeps authored layout and action bindings intact.
public static class MapButtonUiAuthoring
{
    [MenuItem("Tactics/UI/Apply paper sprites to map buttons")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before styling UI.");
        foreach (string path in new[] { "Assets/Prefabs/UI/Map Entry.prefab", "Assets/Prefabs/UI/Map UI Dialogs.prefab", "Assets/Prefabs/Save Slot.prefab" })
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (path != "Assets/Prefabs/Save Slot.prefab")
                    foreach (var button in root.GetComponentsInChildren<Button>(true)) Style(button);
                ApplyDeleteIcons(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var original = SceneManager.GetActiveScene();
        foreach (string path in new[] { "Assets/Scenes/Main Menu.unity", "Assets/Scenes/Map Creator.unity", "Assets/Scenes/Main Scene.unity" })
        {
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                if (scene.isDirty)
                    EditorSceneManager.SaveScene(scene, "Assets/_Recovery/Before map button sprites - " + scene.name + " - " + Guid.NewGuid().ToString("N") + ".unity", true);
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (scene.name == "Map Creator")
                    {
                        foreach (var button in root.GetComponentsInChildren<Button>(true))
                            Style(button);
                    }
                    else if (scene.name == "Main Menu")
                    {
                        foreach (var dialogs in root.GetComponentsInChildren<MapUiDialogs>(true))
                            foreach (var button in dialogs.GetComponentsInChildren<Button>(true)) Style(button);
                        foreach (var select in root.GetComponentsInChildren<CustomMapSelect>(true))
                        { Style(select.choose); Style(select.manage); }
                    }
                    ApplyDeleteIcons(root);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        if (original.isLoaded) SceneManager.SetActiveScene(original);
    }

    private static void ApplyDeleteIcons(GameObject root)
    {
        foreach (var row in root.GetComponentsInChildren<CustomMapRowView>(true))
        {
            var button = row.delete;
            var child = button.transform.Find("Trash icon");
            Image icon;
            if (child == null)
            {
                var holder = new GameObject("Trash icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                Undo.RegisterCreatedObjectUndo(holder, "Add map delete icon");
                var rect = (RectTransform)holder.transform;
                rect.SetParent(button.transform, false);
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.sizeDelta = new Vector2(64, 64);
                rect.anchoredPosition = new Vector2(0, 1);
                icon = holder.GetComponent<Image>();
            }
            else icon = child.GetComponent<Image>();
            SetTrashSprite(icon);
            foreach (var caption in button.GetComponentsInChildren<TMPro.TMP_Text>(true))
            { Undo.RecordObject(caption.gameObject, "Use map delete icon"); caption.gameObject.SetActive(false); }
        }
        foreach (var slot in root.GetComponentsInChildren<SaveSlot>(true))
        {
            var button = slot.transform.Find("Delete");
            if (button == null) continue;
            foreach (var icon in button.GetComponentsInChildren<Image>(true))
                if (icon.transform != button && icon.name != "Touch hit area") SetTrashSprite(icon);
        }
    }

    private static void SetTrashSprite(Image icon)
    {
        Undo.RecordObject(icon, "Apply pixel trash icon");
        icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Controls/Trash Can Pixel.png")
            ?? throw new InvalidOperationException("Pixel trash sprite is missing.");
        icon.overrideSprite = null;
        icon.type = Image.Type.Simple;
        icon.preserveAspect = true;
        icon.color = Color.white;
        icon.raycastTarget = false;
        CustomMapUi.Stretch(icon.rectTransform, 8);
        var button = icon.GetComponentInParent<Button>(true);
        if (button != null && button.GetComponent<PaperButtonLabel>() == null) Undo.AddComponent<PaperButtonLabel>(button.gameObject);
        if (PrefabUtility.IsPartOfPrefabInstance(icon)) PrefabUtility.RecordPrefabInstancePropertyModifications(icon);
    }

    public static void Style(Button button)
    {
        if (button == null) return;
        if (IsTerrainButton(button)) { RemoveTerrainBackground(button); return; }
        var image = button.targetGraphic as Image;
        if (image == null) throw new InvalidOperationException("Button needs an Image target: " + button.name);
        Undo.RecordObjects(new UnityEngine.Object[] { button, image }, "Apply paper button sprites");
        TacticsUiSkin.Current.ApplyButton(button);
        image.overrideSprite = null;
        image.raycastTarget = true;
        image.pixelsPerUnitMultiplier = 1;
        if (image.color == CustomMapUi.Paper && button.name != "Castle owner") image.color = Color.white;
        foreach (var outline in image.GetComponents<Outline>())
        { Undo.RecordObject(outline, "Use sprite outline"); outline.enabled = false; }
        if (button.GetComponent<PaperButtonLabel>() == null) Undo.AddComponent<PaperButtonLabel>(button.gameObject);
        if (PrefabUtility.IsPartOfPrefabInstance(button))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(button);
            PrefabUtility.RecordPrefabInstancePropertyModifications(image);
        }
    }

    private static bool IsTerrainButton(Button button)
        => button.name == "Grass" || button.name == "Forest" || button.name == "Mountain" || button.name == "Tavern";

    public static void RemoveTerrainBackground(Button button)
    {
        if (!IsTerrainButton(button)) return;
        var image = button.targetGraphic as Image;
        if (image == null) return;
        Undo.RecordObjects(new UnityEngine.Object[] { button, image }, "Remove terrain button frame");
        var preview = button.transform.Find("Terrain preview");
        if (preview != null)
        {
            image.sprite = preview.GetComponent<Image>().sprite;
            image.color = Color.white;
            Undo.DestroyObjectImmediate(preview.gameObject);
        }
        image.overrideSprite = null;
        if (image.sprite != null && image.sprite.name.StartsWith("UI_Paper_Button_"))
        {
            // Remove a paper frame if there is no original terrain sprite to restore.
            image.sprite = null;
            image.color = Color.clear;
            image.type = Image.Type.Simple;
        }
        image.type = Image.Type.Simple;
        image.preserveAspect = false;
        image.pixelsPerUnitMultiplier = 1;
        image.raycastTarget = true;
        button.transition = Selectable.Transition.ColorTint;
        button.spriteState = new SpriteState();
        var caption = button.GetComponent<PaperButtonLabel>();
        if (caption != null) Undo.DestroyObjectImmediate(caption);
    }
}
