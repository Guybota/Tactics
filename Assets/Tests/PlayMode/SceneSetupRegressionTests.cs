using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class SceneSetupRegressionTests
{
    private bool originalBackground;
    private Component menu;
    private static Type Script(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static Type Ui(string name) => Type.GetType("UnityEngine.UI." + name + ", UnityEngine.UI", true);
    private static object Get(object target, string field) => target.GetType().GetField(field).GetValue(target);
    private static object Property(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method).Invoke(target, args);
    private static void SetProperty(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        originalBackground = Application.runInBackground;
        Application.runInBackground = true;
        SceneManager.LoadScene("Main Menu");
        yield return null;
        yield return null;
        menu = (Component)UnityEngine.Object.FindFirstObjectByType(Script("MainMenu"));
        Assert.That(menu, Is.Not.Null);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.LoadScene("Main Menu");
        yield return null;
        yield return null;
        Application.runInBackground = originalBackground;
    }

    [UnityTest]
    public IEnumerator CreateMapOpensItsConfiguredPanelAndReturnsToFirstMenu()
    {
        Assert.DoesNotThrow(() => Call(menu, "PressCreateMap"));
        Assert.That(((GameObject)Get(menu, "firstMenu")).activeSelf, Is.False);
        Assert.That(((GameObject)Get(menu, "mapMenu")).activeSelf, Is.False);
        Assert.That(((GameObject)Get(menu, "createmapMenu")).activeSelf, Is.True);
        Call(menu, "PressBack");
        Assert.That(((GameObject)Get(menu, "firstMenu")).activeSelf, Is.True);
        Assert.That(((GameObject)Get(menu, "createmapMenu")).activeSelf, Is.False);
        yield return null;
    }

    [UnityTest]
    public IEnumerator EditorSlidersUseTheirOwnValuesAndStartMapWithSelectedDimensions()
    {
        Call(menu, "PressCreateMap");
        object editorWidth = Get(menu, "create_width_slider");
        object editorHeight = Get(menu, "create_height_slider");
        Assert.That(editorWidth, Is.Not.Null);
        Assert.That(editorHeight, Is.Not.Null);
        // Changing value invokes the actual serialized onValueChanged listeners.
        SetProperty(editorWidth, "value", 19f);
        SetProperty(editorHeight, "value", 23f);
        Assert.That(Property(Get(menu, "width_slider"), "value"), Is.EqualTo(10f));
        Assert.That(Property(Get(menu, "height_slider"), "value"), Is.EqualTo(10f));
        Assert.That(Script("MainMenu").GetField("map_width").GetValue(null), Is.EqualTo(19));
        Assert.That(Script("MainMenu").GetField("map_height").GetValue(null), Is.EqualTo(23));
        Assert.That(Property(Get(menu, "create_width_value"), "text"), Is.EqualTo("Width: 19"));
        Assert.That(Property(Get(menu, "create_height_value"), "text"), Is.EqualTo("Height: 23"));
        Call(menu, "PressStartCreateMap");
        yield return null;
        yield return null;
        var creator = UnityEngine.Object.FindFirstObjectByType(Script("MapCreator"));
        Assert.That(creator, Is.Not.Null);
        object map = Get(creator, "gameMap");
        Assert.That(Get(map, "max_x"), Is.EqualTo(19));
        Assert.That(Get(map, "max_y"), Is.EqualTo(23));
        var tiles = (Array)Get(map, "tiles");
        Assert.That(tiles.GetLength(0), Is.EqualTo(19));
        Assert.That(tiles.GetLength(1), Is.EqualTo(23));
        Assert.That(tiles.GetValue(18, 22), Is.Not.Null);
    }

    private static Component FindTerrainButton(Component creator, string name)
    {
        foreach (var root in creator.gameObject.scene.GetRootGameObjects())
        {
            foreach (Component button in root.GetComponentsInChildren(Ui("Button"), true))
            {
                if (button.name != name) continue;
                var click = (UnityEventBase)Property(button, "onClick");
                for (int listener = 0; listener < click.GetPersistentEventCount(); listener++)
                    if (click.GetPersistentTarget(listener) == creator && click.GetPersistentMethodName(listener) == "SelectTerrain")
                        return button;
            }
        }
        Assert.Fail("No working terrain button found for " + name);
        return null;
    }

    [UnityTest]
    public IEnumerator HotbarButtonsSelectAllFourTerrainTools()
    {
        Call(menu, "PressStartCreateMap");
        yield return null;
        yield return null;
        var creator = (Component)UnityEngine.Object.FindFirstObjectByType(Script("MapCreator"));
        Assert.That(creator, Is.Not.Null);
        var tools = new Dictionary<string, string>
        {
            { "Grass", "grass_tile" },
            { "Forest", "forest_tile" },
            { "Mountain", "mountain_tile" },
            { "Tavern", "castle" }
        };
        foreach (var tool in tools)
        {
            Component button = FindTerrainButton(creator, tool.Key);
            Assert.That(button.gameObject.activeInHierarchy, Is.True);
            Assert.That(Property(button, "interactable"), Is.EqualTo(true));
            Call(Property(button, "onClick"), "Invoke");
            Assert.That(Property(creator, "SelectedTerrain"), Is.EqualTo(tool.Value), tool.Key);
        }
    }

    [UnityTest]
    public IEnumerator PaintingThroughHotbarUiCannotReplaceTheUnderlyingTile()
    {
        Call(menu, "PressStartCreateMap");
        yield return null;
        yield return null;
        var creator = (Component)UnityEngine.Object.FindFirstObjectByType(Script("MapCreator"));
        Component button = FindTerrainButton(creator, "Forest");
        Call(Property(button, "onClick"), "Invoke");
        Canvas.ForceUpdateCanvases();
        var buttonRect = (RectTransform)button.transform;
        Canvas canvas = button.GetComponentInParent<Canvas>();
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(uiCamera, buttonRect.TransformPoint(buttonRect.rect.center));
        Assert.That((bool)Script("WorldPointer").GetMethod("IsOverUI").Invoke(null, new object[] { screenPosition }), Is.True);
        object map = Get(creator, "gameMap");
        var tiles = (Array)Get(map, "tiles");
        var originalTile = (Component)tiles.GetValue(5, 5);
        object originalTerrain = Get(originalTile, "tile_name");
        Camera camera = Camera.main;
        Assert.That(camera, Is.Not.Null);
        Ray initialRay = camera.ScreenPointToRay(screenPosition);
        Vector3 projected = initialRay.GetPoint((originalTile.transform.position.z - initialRay.origin.z) / initialRay.direction.z);
        Vector3 adjustment = originalTile.transform.position - projected;
        adjustment.z = 0;
        camera.transform.position += adjustment;
        Physics.SyncTransforms();
        Assert.That(Physics.Raycast(camera.ScreenPointToRay(screenPosition), out RaycastHit hit), Is.True);
        Assert.That(hit.collider.GetComponent(Script("Tile")), Is.SameAs(originalTile), "The UI test must cover a real tile behind the button.");
        Assert.That(Call(creator, "PaintAt", screenPosition), Is.EqualTo(false));
        Assert.That(tiles.GetValue(5, 5), Is.SameAs(originalTile));
        Assert.That(Get(originalTile, "tile_name"), Is.EqualTo(originalTerrain));
    }
}
