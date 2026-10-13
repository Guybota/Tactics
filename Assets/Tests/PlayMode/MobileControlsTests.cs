using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ContactPoint = Tactics.Core.ContactPoint;

public class MobileControlsTests
{
    private Component menu;
    private bool background;
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static Type Script(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static Type Ui(string name) => Type.GetType("UnityEngine.UI." + name + ", UnityEngine.UI", true);
    private static object Get(object target, string field) => target.GetType().GetField(field, Members).GetValue(target);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Members).Invoke(target, args);
    private static Component Find(string name) => (Component)UnityEngine.Object.FindFirstObjectByType(Script(name));
    private static void Mode(bool? enabled) => Script("MobileUiMode").GetMethod("SetOverride").Invoke(null, new object[] { enabled, 2f });
    [UnitySetUp] public IEnumerator SetUp()
    {
        background = Application.runInBackground; Application.runInBackground = true; Mode(true);
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        Canvas.ForceUpdateCanvases(); menu = Find("MainMenu");
    }
    [UnityTearDown] public IEnumerator TearDown()
    {
        Script("MobileUiMode").GetMethod("SetOverride").Invoke(null, new object[] { null, 0f });
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null; Application.runInBackground = background;
    }
    private IEnumerator Game()
    { Call(menu, "PressStart"); yield return null; yield return null; yield return new WaitForSeconds(.25f); }
    private IEnumerator FixedGame()
    {
        var data = new CustomMapData { id = Guid.NewGuid().ToString("N"), name = "Mobile test", width = 10, height = 10 };
        for (int i = 0; i < 100; i++) data.cells.Add(new MapCellData());
        data.starts.Add(new MapStartData { slot = 0, x = 3, y = 3 }); data.starts.Add(new MapStartData { slot = 1, x = 4, y = 3 });
        data.Cell(3, 4).castle = true; data.Cell(3, 4).castleOwner = 0;
        Call(menu, "SelectCustomMap", data); yield return Game();
    }
    private static Rect ScreenBounds(Component component)
    {
        var rect = (RectTransform)component.transform; var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var canvas = rect.GetComponentInParent<Canvas>();
        var camera = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
        var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]); var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static void NativeClick(GameObject target)
    {
        var button = target.GetComponent(Ui("Button")); var click = button.GetType().GetProperty("onClick").GetValue(button);
        click.GetType().GetMethod("Invoke").Invoke(click, null);
    }
    private static object TouchInput() => Find("WorldTouchInput");
    private static void ShowSameUnitCard()
    {
        var controller = Find("scr_game_contoller"); var team = ((IList)Get(controller, "teams"))[0];
        var king = Call(team, "Get_king");
        Call(Get(controller, "uiManager"), "Update_tile_info", Get(king, "current_tile"));
        Canvas.ForceUpdateCanvases();
    }
    private static string[] Geometry(Transform root)
    {
        var result = new List<string>();
        foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (rect.name == "Touch hit area") continue;
            string path = rect.name;
            for (var parent = rect.parent; parent != null && parent != root.parent; parent = parent.parent) path = parent.name + "/" + path;
            result.Add(path + "|" + rect.anchorMin + "|" + rect.anchorMax + "|" + rect.pivot + "|" + rect.anchoredPosition3D + "|" + rect.sizeDelta + "|" + rect.localScale);
        }
        result.Sort(StringComparer.Ordinal); return result.ToArray();
    }
    private IEnumerator Menu(bool mobile)
    {
        Mode(mobile); SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        Canvas.ForceUpdateCanvases(); menu = Find("MainMenu");
    }
    private IEnumerator MenuGeometry(List<string[]> results)
    {
        results.Add(Geometry(((GameObject)Get(menu, "firstMenu")).transform));
        Call(menu, "PressPlay"); Call(menu, "Add_player", 3); Call(menu, "Add_player", 4); yield return null; Canvas.ForceUpdateCanvases();
        results.Add(Geometry(((GameObject)Get(menu, "mapMenu")).transform));
        Call(menu, "PressSettings"); yield return null; Canvas.ForceUpdateCanvases(); results.Add(Geometry(Find("SettingsMenu").transform));
        Call(menu, "PressLoad"); yield return null; yield return null; Canvas.ForceUpdateCanvases(); results.Add(Geometry(Find("LoadMenu").transform));
    }
    [UnityTest] public IEnumerator MobilePreservesAllMenuGeometry()
    {
        var desktop = new List<string[]>(); var mobile = new List<string[]>();
        yield return Menu(false); yield return MenuGeometry(desktop);
        yield return Menu(true); yield return MenuGeometry(mobile);
        CollectionAssert.AreEqual(desktop[0], mobile[0], "Initial menu");
        CollectionAssert.AreEqual(desktop[1], mobile[1], "Game Select");
        CollectionAssert.AreEqual(desktop[2], mobile[2], "Settings");
        CollectionAssert.AreEqual(desktop[3], mobile[3], "Load game");
    }
    [UnityTest] public IEnumerator MobilePreservesMatchAndEditorGeometry()
    {
        yield return Menu(false); yield return FixedGame(); Canvas.ForceUpdateCanvases();
        ShowSameUnitCard();
        var desktopMatch = Geometry(((Component)Get(Find("scr_game_contoller"), "uiManager")).transform);
        float zoom = Camera.main.orthographicSize;
        yield return Menu(true); yield return FixedGame(); Canvas.ForceUpdateCanvases();
        ShowSameUnitCard();
        CollectionAssert.AreEqual(desktopMatch, Geometry(((Component)Get(Find("scr_game_contoller"), "uiManager")).transform));
        Assert.That(Camera.main.orthographicSize, Is.EqualTo(zoom), "Initial camera zoom");
        yield return Menu(false); Call(menu, "PressStartCreateMap"); yield return null; yield return null; Canvas.ForceUpdateCanvases();
        var desktopEditor = Geometry(GameObject.Find("Castle owner").GetComponentInParent<Canvas>().rootCanvas.transform);
        yield return Menu(true); Call(menu, "PressStartCreateMap"); yield return null; yield return null; Canvas.ForceUpdateCanvases();
        var owner = GameObject.Find("Castle owner");
        // Compare the whole authored canvas; designers can reparent/reposition controls.
        CollectionAssert.AreEqual(desktopEditor, Geometry(owner.GetComponentInParent<Canvas>().rootCanvas.transform));
        NativeClick(owner); Assert.That(Find("MapCreator").GetType().GetProperty("CastleOwnerBrush").GetValue(Find("MapCreator")), Is.EqualTo(0));
    }
    [UnityTest] public IEnumerator PlayerControlsReceiveInvisibleTouchAreasWithoutResizing()
    {
        Call(menu, "PressPlay"); Call(menu, "Add_player", 3); Call(menu, "Add_player", 4); yield return null; yield return null;
        var panel = (GameObject)Get(menu, "mapMenu"); var selectors = panel.GetComponentsInChildren(Script("ClassSelect"), true);
        Assert.That(selectors.Length, Is.EqualTo(4));
        foreach (var selector in selectors)
            foreach (var button in selector.transform.parent.GetComponentsInChildren(Ui("Button"), true))
            {
                Assert.That(button.GetComponent(Script("MobileTouchTarget")), Is.Not.Null);
                var area = button.transform.Find("Touch hit area"); Assert.That(area, Is.Not.Null);
                var image = area.GetComponent(Ui("Image"));
                Assert.That((Color)image.GetType().GetProperty("color").GetValue(image), Is.EqualTo(Color.clear));
            }
    }
    private static void Touch(params ContactPoint[] points) => Call(TouchInput(), "ProcessContacts", (object)points);
    private static void Tap(Vector2 point, bool ui = false)
    {
        Touch(new ContactPoint(0, point.x, point.y, ContactPhase.Began, ui));
        Touch(new ContactPoint(0, point.x, point.y, ContactPhase.Ended, ui));
    }
    private static Component FindUsableTile(Component controller)
    {
        var map = Get(controller, "gameMap"); var tiles = (Array)Get(map, "tiles");
        foreach (Component tile in tiles)
        {
            var point = (Vector2)Camera.main.WorldToScreenPoint(tile.transform.position);
            if (point.x < 100 || point.x > Screen.width - 100 || point.y < 200 || point.y > Screen.height - 200) continue;
            if (!(bool)Script("WorldPointer").GetMethod("IsOverUI").Invoke(null, new object[] { point })) return tile;
        }
        Assert.Fail("No map tile is accessible between the mobile controls."); return null;
    }
    [UnityTest] public IEnumerator TouchTapSelectsTileAndUpdatesInspectionWithoutHover()
    {
        yield return Game(); var controller = Find("scr_game_contoller"); var tile = FindUsableTile(controller);
        Tap(Camera.main.WorldToScreenPoint(tile.transform.position));
        Assert.That(Get(Get(controller, "gameMap"), "selected_tile"), Is.EqualTo(tile.gameObject));
        Assert.That(((GameObject)Get(Get(controller, "uiManager"), "tile_info")).activeSelf, Is.True);
    }

    [UnityTest] public IEnumerator TappingSpentEnemyShowsItsMovementRangeWithoutActing()
    {
        yield return FixedGame();
        var controller = Find("scr_game_contoller");
        var map = Get(controller, "gameMap");
        var teams = (IList)Get(controller, "teams");
        var enemy = (Component)Call(teams[1], "Get_king");
        enemy.GetType().GetField("has_moved").SetValue(enemy, true);
        var origin = (Component)Get(enemy, "current_tile");
        Camera.main.transform.position = origin.transform.position + new Vector3(0, 0, -10);
        map.GetType().GetField("cooldown").SetValue(map, 0f);
        int revision = (int)controller.GetType().GetProperty("MatchRevision").GetValue(controller);
        var originalPosition = enemy.transform.position;
        Tap(Camera.main.WorldToScreenPoint(origin.transform.position));
        Assert.That(Get(map, "selected_tile"), Is.EqualTo(origin.gameObject));
        Assert.That(Get(map, "is_unit_selected"), Is.True, "An enemy's spent turn must not hide its inspection range.");
        int reachable = 0;
        foreach (Component tile in (Array)Get(map, "tiles"))
            if ((string)Get(tile, "state") == "Can_move") reachable++;
        Assert.That(reachable, Is.GreaterThan(1));
        Assert.That(((GameObject)Get(Get(controller, "uiManager"), "tile_info")).activeSelf, Is.True);
        Assert.That(((IList)Get(map, "path")).Count, Is.Zero);
        Assert.That(controller.GetType().GetProperty("MatchRevision").GetValue(controller), Is.EqualTo(revision));
        Assert.That(enemy.transform.position, Is.EqualTo(originalPosition));
        // Clicking a reachable square while inspecting an enemy must remain an inspection.
        var destination = (Component)((Array)Get(map, "tiles")).GetValue(5, 3);
        map.GetType().GetField("cooldown").SetValue(map, 0f);
        Tap(Camera.main.WorldToScreenPoint(destination.transform.position));
        Assert.That(Get(map, "selected_tile"), Is.EqualTo(destination.gameObject));
        Assert.That(Get(map, "is_unit_selected"), Is.False);
        Assert.That(Get(enemy, "current_tile"), Is.SameAs(origin));
        Assert.That(controller.GetType().GetProperty("MatchRevision").GetValue(controller), Is.EqualTo(revision));
    }
    [UnityTest] public IEnumerator TouchDragPansWithoutSelectingTileOrMutatingMatch()
    {
        yield return Game(); var controller = Find("scr_game_contoller"); var tile = FindUsableTile(controller);
        var point = (Vector2)Camera.main.WorldToScreenPoint(tile.transform.position); var initial = Camera.main.transform.position;
        int revision = (int)controller.GetType().GetProperty("MatchRevision").GetValue(controller);
        Touch(new ContactPoint(0, point.x, point.y, ContactPhase.Began));
        Touch(new ContactPoint(0, point.x - 100, point.y, ContactPhase.Moved));
        Touch(new ContactPoint(0, point.x - 100, point.y, ContactPhase.Ended));
        Assert.That(Camera.main.transform.position.x, Is.GreaterThan(initial.x));
        Assert.That(Get(Get(controller, "gameMap"), "selected_tile"), Is.Null);
        Assert.That(controller.GetType().GetProperty("MatchRevision").GetValue(controller), Is.EqualTo(revision));
    }
    [UnityTest] public IEnumerator PinchZoomKeepsMidpointAnchoredAndNeverSelectsOnRelease()
    {
        yield return Game(); var controller = Find("scr_game_contoller"); var tile = FindUsableTile(controller);
        var focus = (Vector2)Camera.main.WorldToScreenPoint(tile.transform.position); var world = Camera.main.ScreenToWorldPoint(focus);
        float size = Camera.main.orthographicSize;
        Touch(new ContactPoint(0, focus.x - 40, focus.y, ContactPhase.Began), new ContactPoint(1, focus.x + 40, focus.y, ContactPhase.Began));
        Touch(new ContactPoint(1, focus.x + 60, focus.y, ContactPhase.Moved), new ContactPoint(0, focus.x - 60, focus.y, ContactPhase.Moved));
        Assert.That(Camera.main.orthographicSize, Is.LessThan(size));
        Assert.That(Vector2.Distance(world, Camera.main.ScreenToWorldPoint(focus)), Is.LessThan(.01f));
        Touch(new ContactPoint(0, focus.x - 60, focus.y, ContactPhase.Ended), new ContactPoint(1, focus.x + 60, focus.y, ContactPhase.Ended));
        Assert.That(Get(Get(controller, "gameMap"), "selected_tile"), Is.Null);
    }
    [UnityTest] public IEnumerator TouchStartedOnUiCannotPanOrActOnWorld()
    {
        yield return Game(); var controller = Find("scr_game_contoller"); var initial = Camera.main.transform.position;
        var tile = FindUsableTile(controller); var point = (Vector2)Camera.main.WorldToScreenPoint(tile.transform.position);
        Touch(new ContactPoint(0, 20, 20, ContactPhase.Began, true)); Touch(new ContactPoint(0, point.x, point.y, ContactPhase.Moved));
        Touch(new ContactPoint(0, point.x, point.y, ContactPhase.Ended));
        Assert.That(Camera.main.transform.position, Is.EqualTo(initial)); Assert.That(Get(Get(controller, "gameMap"), "selected_tile"), Is.Null);
    }
    [UnityTest] public IEnumerator LockedCameraRejectsTouchNavigation()
    {
        yield return Game(); var camera = Find("scr_camera"); var pos = camera.transform.position; float zoom = Camera.main.orthographicSize;
        Call(camera, "Lock_camera"); Call(camera, "ApplyTouchNavigation", new Vector2(400, 300), new Vector2(500, 300), .5f);
        Assert.That(camera.transform.position, Is.EqualTo(pos)); Assert.That(Camera.main.orthographicSize, Is.EqualTo(zoom));
    }
    [UnityTest] public IEnumerator AndroidBackClosesSelectionAndMenus()
    {
        yield return Game(); var controller = Find("scr_game_contoller"); var tile = FindUsableTile(controller);
        Tap(Camera.main.WorldToScreenPoint(tile.transform.position)); yield return null;
        Assert.That(GameObject.Find("Cancel selection"), Is.Null); Call(controller, "HandleBack");
        Assert.That(Get(Get(controller, "gameMap"), "selected_tile"), Is.Null);
        Call(controller, "Menu_button"); Call(controller, "Show_load_menu"); Call(controller, "HandleBack");
        Assert.That(Get(controller, "status"), Is.EqualTo("Menu")); Call(controller, "HandleBack"); Assert.That(Get(controller, "status"), Is.EqualTo("Select_tile"));
    }
    [UnityTest] public IEnumerator EditorDragDoesNotPaintAndTapPaintsExactlyOneTile()
    {
        Call(menu, "PressStartCreateMap"); yield return null; yield return null; var creator = Find("MapCreator"); var map = Get(creator, "gameMap");
        Component tile = null; Vector2 point = default;
        foreach (Component candidate in (Array)Get(map, "tiles"))
        {
            point = Camera.main.WorldToScreenPoint(candidate.transform.position);
            if (point.y > Screen.height * .3f && point.y < Screen.height * .6f && !(bool)Script("WorldPointer").GetMethod("IsOverUI").Invoke(null, new object[] {point})) { tile = candidate; break; }
        }
        Assert.That(tile, Is.Not.Null); string before = JsonUtility.ToJson(Call(creator, "CaptureMap"));
        Touch(new ContactPoint(0, point.x, point.y, ContactPhase.Began)); Touch(new ContactPoint(0, point.x + 100, point.y, ContactPhase.Moved));
        Touch(new ContactPoint(0, point.x + 100, point.y, ContactPhase.Ended)); Assert.That(JsonUtility.ToJson(Call(creator, "CaptureMap")), Is.EqualTo(before));
        point = Camera.main.WorldToScreenPoint(tile.transform.position); Tap(point);
        var data = (CustomMapData)Call(creator, "CaptureMap"); int mountains = 0; foreach (var cell in data.cells) if (cell.terrain == TerrainKind.Mountain) mountains++;
        Assert.That(mountains, Is.EqualTo(1));
    }
    [UnityTest] public IEnumerator ExpandedTouchAreasPickTheClosestControl()
    {
        var canvas = ((GameObject)Get(menu, "firstMenu")).GetComponentInParent<Canvas>();
        var uiType = Script("CustomMapUi"); int leftClicks = 0, rightClicks = 0;
        UnityEngine.Events.UnityAction left = () => leftClicks++, right = () => rightClicks++;
        float side = ((RectTransform)canvas.transform).rect.width * .5f - 140;
        var leftButton = (Component)uiType.GetMethod("Button").Invoke(null, new object[] { canvas.transform, "Small left", "L", new Vector2(40, 30), new Vector2(side, 0), left, null });
        var rightButton = (Component)uiType.GetMethod("Button").Invoke(null, new object[] { canvas.transform, "Small right", "R", new Vector2(40, 30), new Vector2(side + 50, 0), right, null });
        yield return null; yield return null; Canvas.ForceUpdateCanvases();
        var bounds = ScreenBounds(rightButton); var point = new Vector2(bounds.xMin - 2, bounds.center.y);
        var data = new PointerEventData(EventSystem.current) { position = point, pointerId = 0, button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
        Assert.That(hits.Count, Is.GreaterThan(0));
        var target = hits[0].gameObject.GetComponentInParent(Ui("Button")); Assert.That(target, Is.EqualTo(rightButton));
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, data, ExecuteEvents.pointerClickHandler);
        Assert.That(rightClicks, Is.EqualTo(1)); Assert.That(leftClicks, Is.EqualTo(0));
    }
    [UnityTest] public IEnumerator AndroidBackClosesMapLibraryAndUnlocksEditorModal()
    {
        Call(menu, "PressPlay"); NativeClick(GameObject.Find("Choose map")); yield return null;
        Assert.That((bool)Script("UiBackAction").GetMethod("TryCloseTop").Invoke(null, null), Is.True);
        Assert.That(GameObject.Find("Custom maps Overlay"), Is.Null);
        Call(menu, "PressStartCreateMap"); yield return null; yield return null;
        NativeClick(GameObject.Find("Validate map")); yield return null;
        Assert.That(Find("MapEditorMenu").GetType().GetProperty("IsModalOpen").GetValue(Find("MapEditorMenu")), Is.True);
        Script("UiBackAction").GetMethod("TryCloseTop").Invoke(null, null);
        Assert.That(Find("MapEditorMenu").GetType().GetProperty("IsModalOpen").GetValue(Find("MapEditorMenu")), Is.False);
    }
    [UnityTest] public IEnumerator CombatPreviewRetainsConfirmCancelAndForecast()
    {
        yield return FixedGame(); var controller = Find("scr_game_contoller"); var map = Get(controller, "gameMap"); var teams = (IList)Get(controller, "teams");
        var attacker = Call(teams[0], "Get_king"); var defender = Call(teams[1], "Get_king");
        Call(map, "Select_tile", Get(attacker, "current_tile"), teams[0]);
        attacker.GetType().GetField("target_attack_tile").SetValue(attacker, Get(defender, "current_tile"));
        Call(map, "Display_attack_menu", attacker, defender); yield return null; Canvas.ForceUpdateCanvases();
        var ui = Get(controller, "uiManager"); var panel = (GameObject)Get(ui, "attack_menu");
        foreach (var button in panel.GetComponentsInChildren(Ui("Button"))) Assert.That(button.GetComponent(Script("MobileTouchTarget")), Is.Not.Null);
        var attack = panel.GetComponent(Script("scr_attack_menu"));
        var stats = ((GameObject)Get(attack, "p1_stats_info")).GetComponent(Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro", true));
        Assert.That((string)stats.GetType().GetProperty("text").GetValue(stats), Does.Contain("Damage:"));
        NativeClick(panel.transform.Find("Cancel Button").gameObject); yield return null;
        Assert.That(panel.activeSelf, Is.False);
    }
    [UnityTest] public IEnumerator RecruitmentSupportsTouchTargetsAndBack()
    {
        yield return FixedGame(); var controller = Find("scr_game_contoller"); var map = Get(controller, "gameMap"); var teams = (IList)Get(controller, "teams");
        Call(map, "Select_tile", Call(map, "GetTileAtPosition", 3, 4), teams[0]); yield return null; Canvas.ForceUpdateCanvases();
        var spawn = Find("SpawnMenu"); Assert.That(((Component)Get(spawn, "spawnButton")).GetComponent(Script("MobileTouchTarget")), Is.Not.Null);
        foreach (var button in spawn.GetComponentsInChildren(Ui("Button"))) if (button.name.Contains("Cancel")) Assert.That(button.GetComponent(Script("MobileTouchTarget")), Is.Not.Null);
        Call(controller, "HandleBack"); Assert.That(Get(controller, "status"), Is.EqualTo("Select_tile"));
    }
    [UnityTest] public IEnumerator EndTurnButtonInMenuUsesValidatedAction()
    {
        yield return FixedGame(); var controller = Find("scr_game_contoller"); Call(controller, "Menu_button");
        var panel = (GameObject)Get(Get(controller, "uiManager"), "main_menu");
        var button = panel.transform.Find("Buttons/End turn button"); Assert.That(button, Is.Not.Null); NativeClick(button.gameObject); yield return null;
        Assert.That(Get(controller, "turn"), Is.EqualTo(1)); Assert.That(Get(controller, "status"), Is.EqualTo("Select_tile"));
    }
    [UnityTest] public IEnumerator UiTouchCanceledByPortraitDoesNotBecomeClickAfterLandscapeReturns()
    {
        var target = ((GameObject)Get(menu, "firstMenu")).transform.Find("Vertical/Play Button").GetComponent(Script("MobileTouchTarget"));
        var point = ScreenBounds(target).center;
        Script("MobileUiMode").GetMethod("UpdateTouchGate").Invoke(null, new object[] { 1, true });
        Script("MobileUiMode").GetMethod("UpdateTouchGate").Invoke(null, new object[] { 1, false });
        Assert.That(Call(target, "IsRaycastLocationValid", point, null), Is.False);
        Script("MobileUiMode").GetMethod("UpdateTouchGate").Invoke(null, new object[] { 0, false });
        Assert.That(Call(target, "IsRaycastLocationValid", point, null), Is.True); yield return null;
    }
    [UnityTest] public IEnumerator NewSessionClearsStaleTouchBlockAndAllowsCameraAndUnitSelection()
    {
        yield return FixedGame();
        var touch = Script("WorldTouchInput");
        touch.GetField("mouseSuppressedUntil", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, Time.unscaledTime + 1000);
        Assert.That(touch.GetProperty("SuppressMouse").GetValue(null), Is.True);
        touch.GetMethod("ResetSession", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        Script("MobileUiMode").GetMethod("ResetSession", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        Assert.That(touch.GetProperty("SuppressMouse").GetValue(null), Is.False);
        var controller = Find("scr_game_contoller"); var camera = Find("scr_camera"); var before = camera.transform.position;
        Call(camera, "ApplyNavigation", 0f, new Vector2(500, 300), Vector2.right, false, false, false, false, .1f);
        Assert.That(camera.transform.position.x, Is.GreaterThan(before.x));
        var king = (Component)Call(((IList)Get(controller, "teams"))[0], "Get_king");
        Assert.That(Call(controller, "SelectWorldAt", (Vector2)Camera.main.WorldToScreenPoint(king.transform.position)), Is.True);
        Assert.That(Get(Get(controller, "gameMap"), "selected_tile"), Is.EqualTo(((Component)Get(king, "current_tile")).gameObject));
    }
    [UnityTest] public IEnumerator PaperCaptionFollowsSpriteFaceWithoutMovingButton()
    {
        yield return FixedGame(); var ui = (Component)Get(Find("scr_game_contoller"), "uiManager");
        var button = ((GameObject)Get(ui, "buttons")).transform.Find("End turn button");
        var rect = (RectTransform)button; var position = rect.anchoredPosition; var size = rect.sizeDelta;
        var caption = button.GetComponent(Script("PaperButtonLabel")); Assert.That(caption, Is.Not.Null);
        var image = button.GetComponent(Ui("Image")); var text = button.GetComponentInChildren(Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro", true));
        Call(caption, "Refresh"); var normal = (Vector4)text.GetType().GetProperty("margin").GetValue(text);
        var skin = Script("TacticsUiSkin").GetProperty("Current").GetValue(null);
        image.GetType().GetProperty("overrideSprite").SetValue(image, Get(skin, "buttonPressed")); Call(caption, "Refresh");
        var pressed = (Vector4)text.GetType().GetProperty("margin").GetValue(text);
        Assert.That(normal.w, Is.GreaterThan(normal.y)); Assert.That(pressed.w, Is.LessThan(pressed.y));
        Assert.That(rect.anchoredPosition, Is.EqualTo(position)); Assert.That(rect.sizeDelta, Is.EqualTo(size));
    }
    [UnityTest] public IEnumerator MapAndSaveTrashIconsFollowButtonHeightAndPressedFace()
    {
        var select = Find("CustomMapSelect");
        var library = Get(Get(select, "dialogs"), "library");
        var mapTemplate = ((Component)Get(library, "rowTemplate")).gameObject;
        var saveTemplate = (GameObject)Get(Get(menu, "loadMenu"), "saveSlot");
        var canvas = menu.GetComponent<Canvas>().rootCanvas;
        var skin = Script("TacticsUiSkin").GetProperty("Current").GetValue(null);
        foreach (var template in new[] { mapTemplate, saveTemplate })
        {
            var clone = UnityEngine.Object.Instantiate(template, canvas.transform); clone.SetActive(true);
            yield return null; Canvas.ForceUpdateCanvases();
            var button = clone.GetComponent(Script("CustomMapRowView")) != null
                ? ((Component)Get(clone.GetComponent(Script("CustomMapRowView")), "delete")).transform
                : clone.transform.Find("Delete");
            var icon = button.Find("Trash icon") ?? button.Find("Text (TMP)");
            var iconRect = (RectTransform)icon; var buttonRect = (RectTransform)button;
            var image = button.GetComponent(Ui("Image")); var label = button.GetComponent(Script("PaperButtonLabel"));
            Assert.That(iconRect.anchorMin, Is.EqualTo(Vector2.zero)); Assert.That(iconRect.anchorMax, Is.EqualTo(Vector2.one));
            image.GetType().GetProperty("overrideSprite").SetValue(image, Get(skin, "buttonNormal")); Call(label, "Refresh");
            float normal = iconRect.anchoredPosition.y; float originalHeight = iconRect.rect.height;
            var buttonPosition = buttonRect.anchoredPosition;
            buttonRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, buttonRect.rect.height + 40);
            Canvas.ForceUpdateCanvases();
            Assert.That(iconRect.rect.height, Is.EqualTo(originalHeight + 40).Within(.01f));
            image.GetType().GetProperty("overrideSprite").SetValue(image, Get(skin, "buttonPressed")); Call(label, "Refresh");
            Assert.That(iconRect.anchoredPosition.y, Is.LessThan(normal));
            Assert.That(buttonRect.anchoredPosition, Is.EqualTo(buttonPosition));
            UnityEngine.Object.Destroy(clone); yield return null;
        }
    }
}
