using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Explicit asset/scene authoring only. Does not resize or move controls at runtime.
public static class PixelControlUiAuthoring
{
    private const string Folder = "Assets/Sprites/Controls/";

    [MenuItem("Tactics/UI/Apply pixel toggle and slider sprites")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring UI.");
        var original = SceneManager.GetActiveScene();
        foreach (string path in new[] { "Assets/Scenes/Main Menu.unity", "Assets/Scenes/Main Scene.unity" })
        {
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                if (scene.isDirty)
                    EditorSceneManager.SaveScene(scene, "Assets/_Recovery/Before pixel controls - " + scene.name + " - " + Guid.NewGuid().ToString("N") + ".unity", true);
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var toggle in root.GetComponentsInChildren<Toggle>(true)) ApplyToggle(toggle);
                    foreach (var slider in root.GetComponentsInChildren<Slider>(true)) ApplySlider(slider);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        if (original.isLoaded) SceneManager.SetActiveScene(original);
    }

    private static Sprite Sprite(string name)
        => AssetDatabase.LoadAssetAtPath<Sprite>(Folder + name + ".png")
            ?? throw new InvalidOperationException("Missing pixel UI sprite: " + name);

    private static void Style(Image image, string sprite, bool sliced)
    {
        if (image == null) return;
        Undo.RecordObject(image, "Apply pixel UI sprite");
        image.sprite = Sprite(sprite);
        image.overrideSprite = null;
        image.color = Color.white;
        image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        image.preserveAspect = !sliced;
        image.pixelsPerUnitMultiplier = 5;
    }

    private static void EnablePointerInput(Selectable control)
    {
        // A native, always-enabled raycast surface covers the whole control, including
        // its label. MobileTouchTarget can still expand this area on touch devices.
        var hit = control.GetComponent<Image>() ?? Undo.AddComponent<Image>(control.gameObject);
        Undo.RecordObject(hit, "Enable control pointer input");
        hit.color = Color.clear;
        hit.raycastTarget = true;
    }

    private static void ApplyToggle(Toggle toggle)
    {
        EnablePointerInput(toggle);
        Style(toggle.targetGraphic as Image, "Toggle frame", false);
        Style(toggle.graphic as Image, "Toggle check", false);
    }

    private static void ApplySlider(Slider slider)
    {
        EnablePointerInput(slider);
        var background = slider.transform.Find("Background");
        if (background != null)
        {
            var track = background.GetComponent<Image>();
            Style(track, "Slider track", true);
            if (track != null) track.color = new Color(.5f, .4f, .3f, 1);
        }
        if (slider.fillRect != null) Style(slider.fillRect.GetComponent<Image>(), "Slider fill", true);
        if (slider.handleRect == null) return;
        var handle = slider.handleRect.GetComponent<Image>();
        Style(handle, "Slider handle", false);
        Undo.RecordObject(slider, "Apply slider pointer states");
        slider.targetGraphic = handle;
        slider.transition = Selectable.Transition.SpriteSwap;
        slider.spriteState = new SpriteState
        {
            highlightedSprite = Sprite("Slider handle"),
            pressedSprite = Sprite("Slider handle pressed"),
            selectedSprite = Sprite("Slider handle")
        };
    }
}
