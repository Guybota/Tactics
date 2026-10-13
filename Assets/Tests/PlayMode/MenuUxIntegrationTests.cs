using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class MenuUxIntegrationTests
{
    private Component menu;
    private object previousPreferences;
    private bool hadPreferences, previousBackground;
    private string originalPreferences;
    private readonly List<string> ownedSaves = new List<string>();
    private const string PreferencesKey = "Tactics.Settings.v1";
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static Type Script(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Get(object target, string field) => target.GetType().GetField(field, Members).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Members).SetValue(target, value);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method).Invoke(target, args);
    private static object Preferences(string method, params object[] args) => Script("GamePreferences").GetMethod(method).Invoke(null, args);
    private object Repository() => Activator.CreateInstance(Script("SaveGameRepository"), Application.persistentDataPath);
    private object Snapshot(Component controller) => Activator.CreateInstance(Script("GameSave"), new object[] { controller, "UxSnapshot" });
    private object ControllerState(Component controller) => Get(Snapshot(controller), "controller");

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        previousBackground = Application.runInBackground; Application.runInBackground = true;
        hadPreferences = PlayerPrefs.HasKey(PreferencesKey); originalPreferences = PlayerPrefs.GetString(PreferencesKey);
        previousPreferences = Script("GamePreferences").GetProperty("Current").GetValue(null);
        Preferences("ResetDefaults");
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        menu = (Component)UnityEngine.Object.FindFirstObjectByType(Script("MainMenu"));
    }
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (string save in ownedSaves) if ((bool)Call(Repository(), "Exists", save)) Call(Repository(), "Delete", save);
        ownedSaves.Clear();
        Preferences("Apply", previousPreferences);
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        if (hadPreferences) PlayerPrefs.SetString(PreferencesKey, originalPreferences); else PlayerPrefs.DeleteKey(PreferencesKey);
        PlayerPrefs.Save(); Application.runInBackground = previousBackground;
    }
    private IEnumerator StartGame()
    {
        Call(menu, "PressStart"); yield return null; yield return null;
    }
    private static void PointerToggle(Component toggle, string part, int pointerId)
    {
        var rect = (RectTransform)toggle.transform.Find(part);
        var canvas = toggle.GetComponentInParent<Canvas>().rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        var data = new PointerEventData(EventSystem.current)
        { position = point, pointerId = pointerId, button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
        Assert.That(hits, Is.Not.Empty, toggle.name + " " + part + " must receive pointer input.");
        Assert.That(hits[0].gameObject.GetComponentInParent(toggle.GetType()), Is.EqualTo(toggle), "Wrong raycast target.");
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, data, ExecuteEvents.pointerClickHandler);
    }
    private static bool ToggleValue(Component toggle) => (bool)toggle.GetType().GetProperty("isOn").GetValue(toggle);
    private static bool ModeFlag(string field) => (bool)Script("MainMenu").GetField(field).GetValue(null);
    private static void PointerDragSlider(Component slider, int pointerId)
    {
        var handle = (RectTransform)slider.GetType().GetProperty("handleRect").GetValue(slider);
        var rail = (RectTransform)handle.parent;
        var canvas = slider.GetComponentInParent<Canvas>().rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 begin = RectTransformUtility.WorldToScreenPoint(camera,
            rail.TransformPoint(new Vector2(Mathf.Lerp(rail.rect.xMin, rail.rect.xMax, .2f), rail.rect.center.y)));
        Vector2 end = RectTransformUtility.WorldToScreenPoint(camera,
            rail.TransformPoint(new Vector2(Mathf.Lerp(rail.rect.xMin, rail.rect.xMax, .8f), rail.rect.center.y)));
        var data = new PointerEventData(EventSystem.current)
        { position = begin, pressPosition = begin, pointerId = pointerId, button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
        Assert.That(hits, Is.Not.Empty, slider.name + " must receive input on the rail.");
        var target = hits[0].gameObject;
        Assert.That(target.GetComponentInParent(slider.GetType()), Is.EqualTo(slider), "Wrong slider raycast target.");
        data.pointerPressRaycast = hits[0]; data.pointerCurrentRaycast = hits[0];
        ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerDownHandler);
        Assert.That((float)slider.GetType().GetProperty("normalizedValue").GetValue(slider),
            Is.EqualTo(.2f).Within(.04f), slider.name + " rail click must change the value.");
        ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.initializePotentialDrag);
        data.delta = end - begin; data.position = end; data.dragging = true;
        ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.dragHandler);
        ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerUpHandler);
        Assert.That((float)slider.GetType().GetProperty("normalizedValue").GetValue(slider),
            Is.EqualTo(.8f).Within(.04f), slider.name + " drag must change the value.");
    }
    [UnityTest]
    public IEnumerator EveryMenuSliderReceivesMouseAndTouchClicksAndDrags()
    {
        var mode = Script("MobileUiMode").GetMethod("SetOverride");
        var sliderType = Type.GetType("UnityEngine.UI.Slider, UnityEngine.UI", true);
        try
        {
            foreach (bool mobile in new[] { false, true })
            {
                mode.Invoke(null, new object[] { mobile, 2f });
                SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
                menu = (Component)UnityEngine.Object.FindFirstObjectByType(Script("MainMenu"));
                int pointerId = mobile ? 0 : -1;
                Call(menu, "PressPlay"); yield return null; Canvas.ForceUpdateCanvases();
                foreach (Component slider in ((GameObject)Get(menu, "mapMenu")).GetComponentsInChildren(sliderType))
                    PointerDragSlider(slider, pointerId);
                Assert.That(Script("MainMenu").GetField("map_width").GetValue(null),
                    Is.EqualTo(Convert.ToInt32(Get(menu, "width_slider").GetType().GetProperty("value").GetValue(Get(menu, "width_slider")))));
                Assert.That(Script("MainMenu").GetField("map_height").GetValue(null),
                    Is.EqualTo(Convert.ToInt32(Get(menu, "height_slider").GetType().GetProperty("value").GetValue(Get(menu, "height_slider")))));
                Call(menu, "PressCreateMap"); yield return null; Canvas.ForceUpdateCanvases();
                foreach (Component slider in ((GameObject)Get(menu, "createmapMenu")).GetComponentsInChildren(sliderType))
                    PointerDragSlider(slider, pointerId);
                Call(menu, "PressSettings"); yield return null; Canvas.ForceUpdateCanvases();
                foreach (Component slider in ((Component)Get(menu, "settingsMenu")).GetComponentsInChildren(sliderType))
                    PointerDragSlider(slider, pointerId);
                var values = Script("GamePreferences").GetProperty("Current").GetValue(null);
                Assert.That(Get(values, "musicVolume"), Is.EqualTo(.8f).Within(.01f));
                Assert.That(Get(values, "effectsVolume"), Is.EqualTo(.8f).Within(.01f));
            }
        }
        finally { mode.Invoke(null, new object[] { null, 0f }); }
    }
    [UnityTest]
    public IEnumerator ModeTogglesReceiveMouseAndTouchAndPreserveTheirDependency()
    {
        var mode = Script("MobileUiMode").GetMethod("SetOverride");
        try
        {
            foreach (bool mobile in new[] { false, true })
            {
                mode.Invoke(null, new object[] { mobile, 2f });
                SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
                menu = (Component)UnityEngine.Object.FindFirstObjectByType(Script("MainMenu"));
                Call(menu, "PressPlay"); yield return null; Canvas.ForceUpdateCanvases();
                var build = (Component)Get(menu, "build_toggle"); var royale = (Component)Get(menu, "br_toggle");
                int pointerId = mobile ? 0 : -1;
                Assert.That(ToggleValue(build), Is.False); Assert.That(ToggleValue(royale), Is.False);
                PointerToggle(build, "Background", pointerId);
                Assert.That(ToggleValue(build), Is.True); Assert.That(ModeFlag("build_mode"), Is.True);
                PointerToggle(royale, "Label", pointerId);
                Assert.That(ToggleValue(royale), Is.True); Assert.That(ModeFlag("battle_royale_mode"), Is.True);
                PointerToggle(build, "Label", pointerId);
                Assert.That(ToggleValue(build), Is.False); Assert.That(ToggleValue(royale), Is.False);
                Assert.That(ModeFlag("build_mode"), Is.False); Assert.That(ModeFlag("battle_royale_mode"), Is.False);
                PointerToggle(royale, "Background", pointerId);
                Assert.That(ToggleValue(build), Is.True); Assert.That(ToggleValue(royale), Is.True);
                Assert.That(ModeFlag("build_mode"), Is.True); Assert.That(ModeFlag("battle_royale_mode"), Is.True);
                PointerToggle(royale, "Background", pointerId);
                Assert.That(ToggleValue(build), Is.True); Assert.That(ToggleValue(royale), Is.False);
                Assert.That(ModeFlag("build_mode"), Is.True); Assert.That(ModeFlag("battle_royale_mode"), Is.False);
            }
        }
        finally { mode.Invoke(null, new object[] { null, 0f }); }
    }
    [UnityTest]
    public IEnumerator SelectedModesReachTheMatchAndReturningToMenuResetsToggleArtwork()
    {
        Call(menu, "PressPlay"); yield return null; Canvas.ForceUpdateCanvases();
        PointerToggle((Component)Get(menu, "br_toggle"), "Background", -1);
        yield return StartGame();
        var controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        var state = ControllerState(controller);
        Assert.That(Get(state, "build_mode"), Is.True); Assert.That(Get(state, "battle_royale_mode"), Is.True);
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        menu = (Component)UnityEngine.Object.FindFirstObjectByType(Script("MainMenu"));
        Assert.That(ToggleValue((Component)Get(menu, "build_toggle")), Is.False);
        Assert.That(ToggleValue((Component)Get(menu, "br_toggle")), Is.False);
        Assert.That(ModeFlag("build_mode"), Is.False); Assert.That(ModeFlag("battle_royale_mode"), Is.False);
    }
    [UnityTest]
    public IEnumerator InitialMenuButtonsOpenLoadAndSettingsAndReturnToInitialMenu()
    {
        var first = (GameObject)Get(menu, "firstMenu");
        Assert.That(first.transform.Find("Vertical/Load Game Button"), Is.Not.Null);
        Assert.That(first.transform.Find("Vertical/Settings Button"), Is.Not.Null);
        Call(menu, "PressLoad"); yield return null;
        var load = (Component)Get(menu, "loadMenu");
        Assert.That(load.gameObject.activeInHierarchy, Is.True); Assert.That(first.activeSelf, Is.False);
        Assert.That(((GameObject)Get(menu, "mapMenu")).activeSelf, Is.False);
        Call(menu, "PressBack"); Assert.That(first.activeSelf, Is.True);
        Call(menu, "PressSettings"); yield return null;
        Assert.That(((Component)Get(menu, "settingsMenu")).gameObject.activeInHierarchy, Is.True);
        Assert.That(load.gameObject.activeSelf, Is.False);
        Call(menu, "PressBack"); Assert.That(first.activeSelf, Is.True);
    }
    [UnityTest]
    public IEnumerator SettingsApplyToAudioAndPersistAcrossScenes()
    {
        Call(menu, "PressSettings"); yield return null;
        var settings = Get(menu, "settingsMenu");
        Get(settings, "music").GetType().GetProperty("value").SetValue(Get(settings, "music"), .25f);
        Get(settings, "effects").GetType().GetProperty("value").SetValue(Get(settings, "effects"), .4f);
        Get(settings, "zoom").GetType().GetProperty("value").SetValue(Get(settings, "zoom"), 1.5f);
        Get(settings, "pan").GetType().GetProperty("value").SetValue(Get(settings, "pan"), 1.25f);
        Get(settings, "menuScroll").GetType().GetProperty("value").SetValue(Get(settings, "menuScroll"), 2f);
        var menuMusic = UnityEngine.Object.FindFirstObjectByType<AudioSource>();
        Assert.That(menuMusic.volume, Is.EqualTo(.25f).Within(.001f));
        Call(menu, "PressBack");
        var stored = JsonUtility.FromJson(PlayerPrefs.GetString(PreferencesKey), Script("UserSettingsData"));
        Assert.That(Get(stored, "zoomSensitivity"), Is.EqualTo(1.5f));
        Assert.That(Get(stored, "panSpeed"), Is.EqualTo(1.25f)); Assert.That(Get(stored, "menuScrollSensitivity"), Is.EqualTo(2f));
        yield return StartGame();
        var audio = UnityEngine.Object.FindFirstObjectByType(Script("AudioManager"));
        Assert.That(((AudioSource)Get(audio, "musicSource")).volume, Is.EqualTo(.25f).Within(.001f));
        Assert.That(((AudioSource)Get(audio, "SFXSource")).volume, Is.EqualTo(.4f).Within(.001f));
    }
    [UnityTest]
    public IEnumerator InitialMenuLoadsSavedMatchWithoutGivingAnotherTurnOrResettingBots()
    {
        yield return StartGame();
        var controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        var teams = (IList)Get(controller, "teams");
        Set(teams[0], "money", 12345); Set(teams[1], "isBot", true);
        var units = (IList)Get(teams[0], "units"); Set(units[0], "has_moved", true);
        var saved = Snapshot(controller); string name = "UxLoad" + Guid.NewGuid().ToString("N").Substring(0, 12);
        ownedSaves.Add(name); Call(Repository(), "Write", name, saved);
        string expected = JsonUtility.ToJson(Get(saved, "controller"));
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        menu = (Component)UnityEngine.Object.FindFirstObjectByType(Script("MainMenu"));
        object[] args = { name, null };
        Assert.That(Call(menu, "TryLoadSavedGame", args), Is.True, (string)args[1]);
        yield return null; yield return null;
        controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        Assert.That(controller, Is.Not.Null);
        Assert.That(JsonUtility.ToJson(ControllerState(controller)), Is.EqualTo(expected));
        Assert.That(Get(controller, "status"), Is.EqualTo("Select_tile"));
    }
    [UnityTest]
    public IEnumerator MissingSaveLeavesInitialLoadMenuAvailable()
    {
        Call(menu, "PressLoad"); var load = (Component)Get(menu, "loadMenu");
        object[] args = { "UxMissing" + Guid.NewGuid().ToString("N").Substring(0, 12), null };
        Assert.That(Call(menu, "TryLoadSavedGame", args), Is.False);
        Assert.That(args[1], Is.Not.Null.And.Not.Empty);
        yield return null;
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Main Menu"));
        Assert.That(load.gameObject.activeInHierarchy, Is.True);
    }
    [UnityTest]
    public IEnumerator GameSelectUsesAuthoredDimensionsAndPreservesManualEdits()
    {
        Call(menu, "PressPlay"); yield return null; Canvas.ForceUpdateCanvases();
        var map = (GameObject)Get(menu, "mapMenu");
        var rect = (RectTransform)map.transform;
        Assert.That(rect.localScale, Is.EqualTo(Vector3.one));
        Assert.That(rect.rect.width, Is.EqualTo(752f).Within(.001f));
        Assert.That(rect.rect.height, Is.EqualTo(1024f).Within(.001f));
        var start = (RectTransform)rect.Find("Start Button");
        Assert.That(start.rect.width, Is.EqualTo(272f).Within(.001f));
        Assert.That(start.rect.height, Is.EqualTo(112f).Within(.001f));
        Assert.That(start.localScale, Is.EqualTo(Vector3.one));
        Vector2 authoredSize = rect.sizeDelta + new Vector2(20, 10);
        rect.sizeDelta = authoredSize;
        Vector2 authoredPosition = new Vector2(15, -10);
        rect.anchoredPosition = authoredPosition;
        var small = new GameObject("Small Test Parent", typeof(RectTransform)); small.transform.SetParent(menu.transform, false);
        ((RectTransform)small.transform).sizeDelta = new Vector2(500, 400);
        map.transform.SetParent(small.transform, false);
        yield return null; yield return null; Canvas.ForceUpdateCanvases();
        Assert.That(rect.localScale, Is.EqualTo(Vector3.one), "Parent size must not silently change Scale.");
        Assert.That(rect.sizeDelta, Is.EqualTo(authoredSize));
        Assert.That(rect.anchoredPosition, Is.EqualTo(authoredPosition));
    }
    private void Navigate(object camera, float wheel, Vector2 pointer, bool begin = false, bool held = false, bool end = false, bool ui = false)
        => Call(camera, "ApplyNavigation", wheel, pointer, Vector2.zero, begin, held, end, ui, 1f / 60);
    private Component PrepareCamera()
    {
        var navigation = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_camera"));
        ((Behaviour)navigation).enabled = false;
        Set(navigation, "min_x", -100000f); Set(navigation, "min_y", -100000f);
        Set(navigation, "max_x", 100000f); Set(navigation, "max_y", 100000f); Set(navigation, "maxZoom", 1000f);
        navigation.transform.position = new Vector3(5000, 5000, -10);
        ((Camera)Get(navigation, "main_camera")).orthographicSize = 500;
        return navigation;
    }
    [UnityTest]
    public IEnumerator CameraZoomIsSmoothCursorAnchoredAndBlockedOverUI()
    {
        yield return StartGame(); var navigation = PrepareCamera(); var camera = (Camera)Get(navigation, "main_camera");
        var pointer = new Vector2(Screen.width * .7f, Screen.height * .6f);
        Navigate(navigation, 3, pointer, ui: true); Assert.That(camera.orthographicSize, Is.EqualTo(500));
        var world = camera.ScreenToWorldPoint(pointer);
        Navigate(navigation, 1, pointer);
        float target = (float)navigation.GetType().GetProperty("ZoomTarget").GetValue(navigation);
        Assert.That(camera.orthographicSize, Is.LessThan(500).And.GreaterThan(target));
        for (int i = 0; i < 90; i++) Navigate(navigation, 0, pointer);
        Assert.That(camera.orthographicSize, Is.EqualTo(target).Within(.02f));
        Assert.That(Vector2.Distance(world, camera.ScreenToWorldPoint(pointer)), Is.LessThan(.02f));
        Navigate(navigation, 10000, pointer);
        Assert.That(navigation.GetType().GetProperty("ZoomTarget").GetValue(navigation), Is.EqualTo(150f));
        Navigate(navigation, -10000, pointer);
        Assert.That(navigation.GetType().GetProperty("ZoomTarget").GetValue(navigation), Is.EqualTo(1000f));
    }
    [UnityTest]
    public IEnumerator CameraDragGrabsMapAndDoesNotAccelerateWhilePointerIsStill()
    {
        yield return StartGame(); var navigation = PrepareCamera(); var p = new Vector2(Screen.width * .5f, Screen.height * .5f);
        Vector3 start = navigation.transform.position;
        Navigate(navigation, 0, p, begin: true, held: true); p.x += 40; Navigate(navigation, 0, p, held: true);
        Assert.That(navigation.transform.position.x, Is.LessThan(start.x));
        Vector3 after = navigation.transform.position;
        for (int i = 0; i < 60; i++) Navigate(navigation, 0, p, held: true);
        Assert.That(Vector3.Distance(after, navigation.transform.position), Is.LessThan(.01f));
        Navigate(navigation, 0, p, end: true); Call(navigation, "Lock_camera");
        Navigate(navigation, 4, p, begin: true, held: true);
        Assert.That(Vector3.Distance(after, navigation.transform.position), Is.LessThan(.01f));
    }
    [UnityTest]
    public IEnumerator MenuWheelMovesUsefulDistanceSmoothlyAndResetCancelsMomentum()
    {
        Call(menu, "PressLoad"); yield return null;
        var load = Get(menu, "loadMenu"); var panel = (GameObject)Get(load, "panel");
        for (int i = 0; i < 16; i++)
        {
            var row = new GameObject("Test Row", typeof(RectTransform)); row.transform.SetParent(panel.transform, false);
            var element = row.AddComponent(Type.GetType("UnityEngine.UI.LayoutElement, UnityEngine.UI", true));
            element.GetType().GetProperty("preferredHeight").SetValue(element, 100f);
        }
        Canvas.ForceUpdateCanvases(); yield return null;
        var scroll = panel.GetComponentInParent(Script("SmoothMenuScroll")); Assert.That(scroll, Is.Not.Null);
        Call(scroll, "StopMovement"); scroll.GetType().GetProperty("verticalNormalizedPosition").SetValue(scroll, 1f);
        var content = (RectTransform)panel.transform; float before = content.anchoredPosition.y;
        var wheel = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -1) };
        Call(scroll, "OnScroll", wheel);
        Assert.That(content.anchoredPosition.y, Is.EqualTo(before).Within(.01f), "Wheel should not jump immediately.");
        yield return new WaitForSecondsRealtime(.3f);
        Assert.That(content.anchoredPosition.y - before, Is.GreaterThan(20));
        Call(scroll, "OnScroll", wheel); Call(scroll, "StopMovement");
        scroll.GetType().GetProperty("verticalNormalizedPosition").SetValue(scroll, 1f);
        yield return new WaitForSecondsRealtime(.3f);
        Assert.That(content.anchoredPosition.y, Is.EqualTo(before).Within(.1f));
    }
}
