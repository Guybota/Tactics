using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PaperButtonSizeAuthoring
{
    [MenuItem("Tactics/UI/Set paper buttons to multiplier 1 and minimum height 70")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Button>(true).Any(IsPaper))
            .OrderBy(path => path == "Assets/Prefabs/UI/Map Entry.prefab" ? 0 : 1).ToArray();
        foreach (string path in paths)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { Adjust(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var original = SceneManager.GetActiveScene();
        foreach (string path in new[] { "Assets/Scenes/Main Menu.unity", "Assets/Scenes/Main Scene.unity", "Assets/Scenes/Map Creator.unity" })
        {
            var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                if (scene.isDirty)
                    EditorSceneManager.SaveScene(scene, "Assets/_Recovery/Before minimum button height - " + scene.name + " - " + Guid.NewGuid().ToString("N") + ".unity", true);
                foreach (var root in scene.GetRootGameObjects()) Adjust(root);
                Canvas.ForceUpdateCanvases();
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        if (original.isLoaded) SceneManager.SetActiveScene(original);
    }

    private static bool IsPaper(Button button)
    {
        var image = button.targetGraphic as Image;
        return image != null && image.sprite != null && image.sprite.name.StartsWith("UI_Paper_Button_");
    }

    private static void Adjust(GameObject root)
    {
        foreach (var button in root.GetComponentsInChildren<Button>(true).Where(IsPaper))
        {
            var image = (Image)button.targetGraphic; var rect = (RectTransform)button.transform;
            Undo.RecordObjects(new UnityEngine.Object[] { image, rect }, "Set paper button dimensions");
            image.pixelsPerUnitMultiplier = 1;
            if (rect.rect.height < 70) rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 70);
            var layout = button.GetComponent<LayoutElement>();
            var parentLayout = rect.parent != null ? rect.parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
            if (layout != null || parentLayout != null && parentLayout.childControlHeight)
            {
                if (layout == null) layout = Undo.AddComponent<LayoutElement>(button.gameObject);
                Undo.RecordObject(layout, "Keep minimum height in native layouts");
                layout.minHeight = Mathf.Max(70, layout.minHeight);
                if (layout.preferredHeight >= 0) layout.preferredHeight = Mathf.Max(70, layout.preferredHeight);
                Record(layout);
            }
            Record(image); Record(rect);
        }
        // Two 70-high actions need separate space inside their editable list template.
        foreach (var row in root.GetComponentsInChildren<CustomMapRowView>(true))
        {
            var edit = (RectTransform)row.edit.transform; var rename = (RectTransform)row.rename.transform;
            bool stacked = !Mathf.Approximately(edit.anchoredPosition.y, rename.anchoredPosition.y);
            float gap = (edit.rect.height + rename.rect.height) * .5f + 8;
            if (stacked && edit.anchoredPosition.y - rename.anchoredPosition.y < gap)
            {
                Undo.RecordObjects(new UnityEngine.Object[] { edit, rename }, "Separate map row actions");
                float center = (edit.anchoredPosition.y + rename.anchoredPosition.y) * .5f;
                edit.anchoredPosition = new Vector2(edit.anchoredPosition.x, center + gap * .5f);
                rename.anchoredPosition = new Vector2(rename.anchoredPosition.x, center - gap * .5f);
                Record(edit); Record(rename);
            }
            var rect = (RectTransform)row.transform; var layout = row.GetComponent<LayoutElement>();
            Undo.RecordObject(rect, "Fit map row actions");
            float minimum = stacked ? 160 : 120;
            if (rect.rect.height < minimum) rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, minimum);
            if (layout != null)
            {
                Undo.RecordObject(layout, "Fit map row actions in the list");
                layout.minHeight = Mathf.Max(minimum, layout.minHeight);
                layout.preferredHeight = Mathf.Max(minimum, layout.preferredHeight); Record(layout);
            }
            Record(rect);
        }
    }

    private static void Record(UnityEngine.Object value)
    { if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value); }
}
