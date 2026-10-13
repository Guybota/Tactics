using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Explicit native prefab/scene authoring. Only the new defense row is positioned;
// the existing thumbnail, names, team label and unit controls retain their layout.
public static class TerrainDefenseAuthoring
{
    private const string SpriteFolder = "Assets/Sprites/Terrain Defense";
    private const string PrefabPath = "Assets/Prefabs/PreviewTile.prefab";
    private const string ScenePath = "Assets/Scenes/Main Scene.unity";
    private const string RowName = "Terrain Defense";

    [MenuItem("Tactics/UI/Install terrain defense shields")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring UI.");
        Directory.CreateDirectory(SpriteFolder);
        var empty = CreateShield(false);
        var filled = CreateShield(true);
        var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var preview = prefab.GetComponent<PreviewTile>();
            var canvas = prefab.GetComponentInChildren<Canvas>(true);
            if (preview == null || canvas == null) throw new InvalidOperationException("PreviewTile requires its existing Canvas.");
            var row = InstallRow(preview, canvas.transform, empty, filled);
            var rect = (RectTransform)row.transform;
            rect.anchoredPosition = new Vector2(0, -80);
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        var scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        var original = SceneManager.GetActiveScene();
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            foreach (var root in scene.GetRootGameObjects())
            foreach (var preview in root.GetComponentsInChildren<PreviewTile>(true))
            {
                var row = preview.terrainDefense;
                if (row == null) throw new InvalidOperationException("PreviewTile defense prefab reference did not update.");
                var panel = preview.tile_name_text != null ? preview.tile_name_text.transform.parent as RectTransform : null;
                if (panel == null || panel.name != "Tile Info")
                    throw new InvalidOperationException("Cannot locate the existing Tile Info panel.");
                var rect = (RectTransform)row.transform;
                // The existing thumbnail has an inherited scale. Keep every new
                // Transform at Scale 1 and express the row's geometry through
                // RectTransform dimensions in the parent's coordinate system.
                rect.rotation = panel.rotation;
                Vector3 scale = rect.lossyScale;
                var ratio = new Vector2(panel.lossyScale.x / scale.x, panel.lossyScale.y / scale.y);
                rect.sizeDelta = Vector2.Scale(new Vector2(266, 32), ratio);
                rect.position = panel.TransformPoint(new Vector3(65, -62, 0));
                var label = row.transform.Find("Defense").GetComponent<TextMeshProUGUI>();
                CopyLabelStyle(label, preview.tile_name_text);
                label.rectTransform.sizeDelta = Vector2.Scale(new Vector2(80, 32), ratio);
                label.rectTransform.anchoredPosition = Vector2.Scale(new Vector2(-92, 0), ratio);
                label.fontSize = 22 * ratio.y;
                for (int slot = 0; slot < TerrainDefenseDisplay.SlotCount; slot++)
                {
                    var shield = row.shields[slot].rectTransform;
                    shield.sizeDelta = Vector2.Scale(new Vector2(32, 32), ratio);
                    shield.anchoredPosition = Vector2.Scale(new Vector2(-23 + slot * 40, 0), ratio);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(shield);
                }
                row.SetStars(0);
                EditorUtility.SetDirty(preview);
                EditorUtility.SetDirty(row);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
                PrefabUtility.RecordPrefabInstancePropertyModifications(label.rectTransform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                PrefabUtility.RecordPrefabInstancePropertyModifications(preview);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Terrain defense display configured: three empty/filled shields, using Tile.DefenseStars.");
    }

    private static TerrainDefenseDisplay InstallRow(PreviewTile preview, Transform parent, Sprite empty, Sprite filled)
    {
        var existing = parent.Find(RowName);
        var rect = existing as RectTransform;
        if (rect == null)
        {
            var go = new GameObject(RowName, typeof(RectTransform), typeof(TerrainDefenseDisplay));
            go.layer = parent.gameObject.layer;
            rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
        }
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(266, 32);
        var display = rect.GetComponent<TerrainDefenseDisplay>();
        if (display == null) display = rect.gameObject.AddComponent<TerrainDefenseDisplay>();
        display.emptyShield = empty;
        display.filledShield = filled;
        display.shields = new Image[TerrainDefenseDisplay.SlotCount];
        for (int slot = 0; slot < TerrainDefenseDisplay.SlotCount; slot++)
        {
            string name = "Shield " + (slot + 1);
            var child = rect.Find(name) as RectTransform;
            if (child == null)
            {
                child = (RectTransform)new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).transform;
                child.gameObject.layer = rect.gameObject.layer;
                child.SetParent(rect, false);
            }
            child.anchorMin = child.anchorMax = child.pivot = new Vector2(.5f, .5f);
            child.anchoredPosition = new Vector2(-23 + slot * 40, 0);
            child.sizeDelta = new Vector2(32, 32);
            var image = child.GetComponent<Image>();
            image.color = Color.white;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
            display.shields[slot] = image;
        }
        var labelRect = rect.Find("Defense") as RectTransform;
        if (labelRect == null)
        {
            labelRect = (RectTransform)new GameObject("Defense", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).transform;
            labelRect.gameObject.layer = rect.gameObject.layer;
            labelRect.SetParent(rect, false);
        }
        labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(.5f, .5f);
        labelRect.anchoredPosition = new Vector2(-92, 0);
        labelRect.sizeDelta = new Vector2(80, 32);
        CopyLabelStyle(labelRect.GetComponent<TextMeshProUGUI>(), parent.GetComponentInChildren<TextMeshProUGUI>(true));
        preview.terrainDefense = display;
        display.SetStars(0);
        return display;
    }

    private static void CopyLabelStyle(TextMeshProUGUI label, TextMeshProUGUI source)
    {
        if (source != null && source != label)
        {
            label.font = source.font;
            label.fontSharedMaterial = source.fontSharedMaterial;
            label.color = source.color;
        }
        else label.color = new Color32(67, 67, 67, 255);
        label.text = "Defense";
        label.fontSize = 22;
        label.enableAutoSizing = false;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
    }

    // A 16-pixel shield with a dark rim. Empty slots keep a hollow center;
    // filled slots use steel-blue plating and a bright upper-left bevel.
    private static Sprite CreateShield(bool filled)
    {
        string path = SpriteFolder + (filled ? "/Shield Filled.png" : "/Shield Empty.png");
        if (!File.Exists(path))
        {
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var mask = new bool[16, 16];
            for (int y = 2; y <= 13; y++)
            {
                int inset = y >= 7 ? 2 : 2 + (7 - y);
                for (int x = inset; x <= 15 - inset; x++) mask[x, y] = true;
            }
            var outline = new Color32(62, 52, 49, 255);
            var rim = new Color32(210, 182, 115, 255);
            var highlight = new Color32(179, 220, 226, 255);
            var steel = new Color32(88, 149, 169, 255);
            var shadow = new Color32(57, 101, 126, 255);
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                Color32 pixel = new Color32(0, 0, 0, 0);
                if (mask[x, y])
                {
                    bool edge = !mask[x - 1, y] || !mask[x + 1, y] || !mask[x, y - 1] || !mask[x, y + 1];
                    bool nearEdge = !mask[x - 2, y] || !mask[x + 2, y] || !mask[x, y - 2] || !mask[x, y + 2];
                    pixel = edge ? outline : nearEdge ? rim : !filled ? new Color32(0, 0, 0, 0)
                        : x <= 5 || y >= 11 ? highlight : x >= 10 ? shadow : steel;
                }
                texture.SetPixel(x, y, pixel);
            }
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 16;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
