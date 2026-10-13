using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Scene/prefab authoring, never a runtime layout override.
public static class SavedMapRowUiAuthoring
{
    [MenuItem("Tactics/UI/Use save-style map rows")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        foreach (string path in new[] { "Assets/Prefabs/UI/Map Entry.prefab", "Assets/Prefabs/UI/Map UI Dialogs.prefab", "Assets/Prefabs/Save Slot.prefab" })
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { Adjust(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var original = SceneManager.GetActiveScene();
        foreach (string path in new[] { "Assets/Scenes/Main Menu.unity", "Assets/Scenes/Map Creator.unity", "Assets/Scenes/Main Scene.unity" })
        {
            var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                if (scene.isDirty)
                    EditorSceneManager.SaveScene(scene, "Assets/_Recovery/Before save-style map rows - " + scene.name + " - " + Guid.NewGuid().ToString("N") + ".unity", true);
                foreach (var root in scene.GetRootGameObjects()) Adjust(root);
                Canvas.ForceUpdateCanvases(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        if (original.isLoaded) SceneManager.SetActiveScene(original);
    }

    private static void Adjust(GameObject root)
    {
        foreach (var row in root.GetComponentsInChildren<CustomMapRowView>(true)) StyleRow(row);
        foreach (var slot in root.GetComponentsInChildren<SaveSlot>(true))
            foreach (var image in slot.GetComponentsInChildren<Image>(true))
                if (image.sprite != null && image.sprite.name == "Trash Can Pixel") StretchIcon(image);
    }

    public static void StyleRow(CustomMapRowView row)
    {
        var root = (RectTransform)row.transform;
        Undo.RecordObject(root, "Use save-style map row");
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 120);
        var layout = row.GetComponent<LayoutElement>() ?? Undo.AddComponent<LayoutElement>(row.gameObject);
        Undo.RecordObject(layout, "Set map row height"); layout.minHeight = layout.preferredHeight = 120;
        var image = row.GetComponent<Image>() ?? Undo.AddComponent<Image>(row.gameObject);
        Undo.RecordObject(image, "Use paper map background");
        image.sprite = TacticsUiSkin.Current.Panel; image.type = Image.Type.Sliced;
        image.color = Color.white; image.pixelsPerUnitMultiplier = 1; image.raycastTarget = false;

        var caption = row.caption.rectTransform;
        Undo.RecordObject(caption, "Move map details onto the paper row");
        if (caption.parent != root) Undo.SetTransformParent(caption, root, "Move map details onto the paper row");
        caption.anchorMin = caption.anchorMax = new Vector2(0, .5f);
        caption.pivot = new Vector2(0, .5f); caption.sizeDelta = new Vector2(128, 100);
        caption.anchoredPosition = new Vector2(56, 0);
        row.caption.alignment = TMPro.TextAlignmentOptions.MidlineLeft; row.caption.raycastTarget = false;

        Place(row.select, 90, -63); Place(row.delete, 72, 26);
        Place(row.edit, 90, 115); Place(row.rename, 112, 224);
        var saves = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Save Slot.prefab");
        foreach (var pair in new[] { new { button = row.select, name = "Load" }, new { button = row.delete, name = "Delete" } })
        {
            var target = pair.button.targetGraphic as Image;
            var source = saves.transform.Find(pair.name).GetComponent<Button>().targetGraphic as Image;
            Undo.RecordObject(target, "Use save action colors"); target.color = source.color; Record(target);
        }
        var loadCaption = row.select.transform.Find("Load label");
        if (loadCaption == null)
        {
            var label = CustomMapUi.Label(row.select.transform, "Load label", "Load", new Vector2(90, 80), Vector2.zero, 30);
            CustomMapUi.Stretch(label.rectTransform, 4);
        }
        foreach (var icon in row.delete.GetComponentsInChildren<Image>(true))
            if (icon.sprite != null && icon.sprite.name == "Trash Can Pixel") StretchIcon(icon);
        Record(root); Record(layout); Record(image); Record(caption);
    }

    private static void Place(Button button, float width, float x)
    {
        var rect = (RectTransform)button.transform;
        Undo.RecordObject(rect, "Place map row action");
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(width, 80);
        rect.anchoredPosition = new Vector2(x, 0);
        MapButtonUiAuthoring.Style(button); Record(rect);
    }

    private static void StretchIcon(Image icon)
    {
        Undo.RecordObject(icon.rectTransform, "Fit trash icon to button height");
        CustomMapUi.Stretch(icon.rectTransform, 8);
        var button = icon.GetComponentInParent<Button>(true);
        if (button != null && button.GetComponent<PaperButtonLabel>() == null) Undo.AddComponent<PaperButtonLabel>(button.gameObject);
        Record(icon.rectTransform);
    }

    private static void Record(UnityEngine.Object value)
    { if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value); }
}
