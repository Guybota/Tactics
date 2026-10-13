using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class TerrainDefenseDisplayTests
{
    private static Type Script(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Get(object target, string name) => target.GetType().GetField(name).GetValue(target);
    private static object Property(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
    private static void SetStars(object display, int stars) => display.GetType().GetMethod("SetStars").Invoke(display, new object[] { stars });

    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(3, 3)]
    [TestCase(-1, 0)]
    [TestCase(7, 3)]
    public void DefenseAlwaysShowsExactlyThreeSlotsAndFillsOnlyTheRequestedAmount(int requested, int expected)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PreviewTile.prefab");
        var root = UnityEngine.Object.Instantiate(prefab);
        try
        {
            var preview = root.GetComponent(Script("PreviewTile"));
            var display = (Component)Get(preview, "terrainDefense");
            Assert.That(display, Is.Not.Null);
            Assert.That(display.transform.localScale, Is.EqualTo(Vector3.one));
            var slots = (Array)Get(display, "shields");
            Assert.That(slots.Length, Is.EqualTo(3));
            var empty = (Sprite)Get(display, "emptyShield");
            var filled = (Sprite)Get(display, "filledShield");
            Assert.That(empty, Is.Not.Null);
            Assert.That(filled, Is.Not.Null);
            Assert.That(filled, Is.Not.SameAs(empty));
            // Refresh an already full row to catch stale filled icons when moving
            // from a castle to a tile with less protection.
            SetStars(display, 3);
            SetStars(display, requested);
            Assert.That((int)Property(display, "Stars"), Is.EqualTo(expected));
            for (int index = 0; index < 3; index++)
            {
                var image = (Component)slots.GetValue(index);
                Assert.That(image, Is.Not.Null);
                Assert.That(Property(image, "sprite"), Is.SameAs(index < expected ? filled : empty));
                Assert.That(Property(image, "raycastTarget"), Is.False);
                Assert.That(image.gameObject.activeSelf, Is.True);
            }
            foreach (var sprite in new[] { empty, filled })
            {
                Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(16, 16)));
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite));
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(importer.mipmapEnabled, Is.False);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void MainScenePlacesDefenseBelowTerrainTextWithoutMovingTheExistingControls()
    {
        const string path = "Assets/Scenes/Main Scene.unity";
        var scene = SceneManager.GetSceneByPath(path);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            var preview = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(Script("PreviewTile"), true)).Single();
            var title = (Component)Get(preview, "tile_name_text");
            var team = (Component)Get(preview, "tile_team_text");
            var panel = title.transform.parent;
            Assert.That(((RectTransform)title.transform).anchoredPosition, Is.EqualTo(new Vector2(65, 20)));
            Assert.That(((RectTransform)team.transform).anchoredPosition, Is.EqualTo(new Vector2(60, -20)));
            var display = (Component)Get(preview, "terrainDefense");
            Assert.That(display, Is.Not.Null);
            Assert.That(display.transform.localScale, Is.EqualTo(Vector3.one));
            foreach (Component image in (Array)Get(display, "shields"))
            {
                var rect = (RectTransform)image.transform;
                Assert.That(rect.localScale, Is.EqualTo(Vector3.one));
                Assert.That(rect.rect.width * rect.lossyScale.x / panel.lossyScale.x, Is.EqualTo(32).Within(.01f));
                Assert.That(rect.rect.height * rect.lossyScale.y / panel.lossyScale.y, Is.EqualTo(32).Within(.01f));
            }
            var position = panel.InverseTransformPoint(display.transform.position);
            Assert.That(position.x, Is.EqualTo(65).Within(.01f));
            Assert.That(position.y, Is.EqualTo(-62).Within(.01f));
            var label = display.transform.Find("Defense").GetComponent(Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro", true));
            Assert.That(Property(label, "text"), Is.EqualTo("Defense"));
            Assert.That(Property(label, "font"), Is.SameAs(Property(title, "font")));
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }
}
