using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MovementPathAuthoring
{
    private const string Folder = "Assets/Sprites/Movement Path";

    [MenuItem("Tactics/Map/Install pixel movement path")]
    public static void Apply()
    {
        Directory.CreateDirectory(Folder);
        var start = CreateSprite("Start", 0);
        var straight = CreateSprite("Straight", 1);
        var corner = CreateSprite("Corner", 2);
        var tip = CreateSprite("Tip", 3);
        const string scenePath = "Assets/Scenes/Main Scene.unity";
        var scene = SceneManager.GetSceneByPath(scenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        foreach (var root in scene.GetRootGameObjects())
        foreach (var controller in root.GetComponentsInChildren<scr_game_contoller>(true))
        {
            if (controller.line == null) continue;
            var path = controller.line.GetComponent<MovementPathRenderer>();
            if (path == null) path = Undo.AddComponent<MovementPathRenderer>(controller.line.gameObject);
            if (path.segmentTemplate == null)
            {
                var template = new GameObject("Pixel path segment template");
                Undo.RegisterCreatedObjectUndo(template, "Pixel movement path");
                template.transform.SetParent(path.transform, false);
                path.segmentTemplate = template.AddComponent<SpriteRenderer>();
                path.segmentTemplate.sortingLayerName = "Default";
                path.segmentTemplate.sortingOrder = 30; // Above units and tile overlays.
                path.segmentTemplate.sprite = straight;
                template.SetActive(false);
            }
            path.segmentTemplate.sortingOrder = 30;
            if (path.start == null) path.start = start;
            if (path.straight == null) path.straight = straight;
            if (path.corner == null) path.corner = corner;
            if (path.tip == null) path.tip = tip;
            controller.gameMap.movementPath = path;
            controller.line.enabled = false;
            controller.line.positionCount = 0;
            if (controller.arrow != null)
            {
                var legacy = controller.arrow.GetComponent<LineRenderer>();
                if (legacy != null) { legacy.enabled = false; legacy.positionCount = 0; }
            }
            EditorUtility.SetDirty(path);
            EditorUtility.SetDirty(controller.gameMap);
            EditorSceneManager.MarkSceneDirty(scene);
        }
        EditorSceneManager.SaveScene(scene);
        if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        AssetDatabase.SaveAssets();
    }

    // Native pixel masks: 16x16 logical pixels, each occupying four world pixels.
    // The start only occupies the edge of the origin tile, leaving the unit clear.
    private static Sprite CreateSprite(string name, int shape)
    {
        string path = Folder + "/" + name + ".png";
        if (!File.Exists(path))
        {
            var mask = new bool[16, 16];
            for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
            {
                bool body = y >= 7 && y <= 8;
                if (shape == 0) body &= x >= 14;
                if (shape == 2) body = body && x <= 8 || x >= 7 && x <= 8 && y >= 7;
                if (shape == 3)
                {
                    body &= x <= 7;
                    if (x >= 6 && x <= 11)
                    {
                        int halfHeight = (11 - x + 1) / 2;
                        body |= y >= 7 - halfHeight && y <= 8 + halfHeight;
                    }
                }
                mask[x, y] = body;
            }
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var outline = new Color32(30, 51, 67, 255);
            var fill = new Color32(114, 216, 239, 255);
            var highlight = new Color32(205, 248, 255, 255);
            for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
            {
                bool adjacent = false;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (x + dx >= 0 && x + dx < 16 && y + dy >= 0 && y + dy < 16)
                        adjacent |= mask[x + dx, y + dy];
                Color32 color = !adjacent ? new Color32(0, 0, 0, 0) : !mask[x, y] ? outline
                    : y + 1 < 16 && mask[x, y + 1] ? fill : highlight;
                for (int px = 0; px < 4; px++)
                for (int py = 0; py < 4; py++) texture.SetPixel(x * 4 + px, y * 4 + py, color);
            }
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 1;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePivot = new Vector2(.5f, .5f);
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
