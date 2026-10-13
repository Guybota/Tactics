using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// Resize real Canvas/RectTransform hierarchies without changing the user's Game View.
// These tests check the resulting control bounds, rather than particular anchor values.
public class ResponsiveUiRegressionTests
{
    private bool originalBackground;
    private Component menu;
    private static readonly Vector2[] Sizes =
    {
        new Vector2(1920, 1080), new Vector2(1440, 1080),
        new Vector2(2340, 1080), new Vector2(2560, 1080), new Vector2(1280, 800)
    };
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Type Script(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static Type Ui(string name) => Type.GetType("UnityEngine.UI." + name + ", UnityEngine.UI", true);
    private static object Get(object target, string field) => target.GetType().GetField(field, Members).GetValue(target);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Members).Invoke(target, args);

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

    private static void Resize(Canvas canvas, Vector2 size)
    {
        canvas.renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)canvas.transform;
        // Match the configured CanvasScaler reference area while changing aspect ratio.
        var scaler = canvas.GetComponent(Ui("CanvasScaler"));
        Assert.That(scaler, Is.Not.Null, "Screen canvas requires its authored CanvasScaler.");
        Vector2 reference = (Vector2)scaler.GetType().GetProperty("referenceResolution").GetValue(scaler);
        float match = (float)scaler.GetType().GetProperty("matchWidthOrHeight").GetValue(scaler);
        Assert.That(reference, Is.EqualTo(new Vector2(1920, 1080)), "Keep the original design reference resolution.");
        // Unity matches width and height in logarithmic space.
        float uiScale = Mathf.Pow(size.x / reference.x, 1f - match) * Mathf.Pow(size.y / reference.y, match);
        string matchMode = scaler.GetType().GetProperty("screenMatchMode").GetValue(scaler).ToString();
        if (matchMode == "Expand") uiScale = Mathf.Min(size.x / reference.x, size.y / reference.y);
        else if (matchMode == "Shrink") uiScale = Mathf.Max(size.x / reference.x, size.y / reference.y);
        root.sizeDelta = size / uiScale;
        // Parent dimensions must settle before responsive components calculate limits.
        Canvas.ForceUpdateCanvases();
        foreach (var component in canvas.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || !component.gameObject.activeInHierarchy) continue;
            if (component.GetType().Assembly.GetName().Name != "Assembly-CSharp") continue;
            MethodInfo apply = component.GetType().GetMethod("ApplyLayout", Members, null, Type.EmptyTypes, null);
            if (apply != null) apply.Invoke(component, null);
        }
        Canvas.ForceUpdateCanvases();
    }

    private static Rect BoundsInRoot(RectTransform root, RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector3 first = root.InverseTransformPoint(corners[0]);
        float minX = first.x, maxX = first.x, minY = first.y, maxY = first.y;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 point = root.InverseTransformPoint(corners[i]);
            minX = Mathf.Min(minX, point.x); maxX = Mathf.Max(maxX, point.x);
            minY = Mathf.Min(minY, point.y); maxY = Mathf.Max(maxY, point.y);
        }
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static Rect Intersect(Rect a, Rect b)
    {
        return Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin),
            Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));
    }

    private static Rect VisibleBounds(RectTransform root, Component control)
    {
        Rect bounds = BoundsInRoot(root, (RectTransform)control.transform);
        // Scroll-list items outside their viewport are deliberately clipped, and do
        // not count as controls extending outside the screen.
        for (Transform parent = control.transform.parent; parent != null && parent != root; parent = parent.parent)
        {
            if (parent.GetComponent(Ui("RectMask2D")) != null || parent.GetComponent(Ui("Mask")) != null)
                bounds = Intersect(bounds, BoundsInRoot(root, (RectTransform)parent));
        }
        return bounds;
    }

    private static string Path(Transform target)
    {
        string result = target.name;
        while (target.parent != null) { target = target.parent; result = target.name + "/" + result; }
        return result;
    }

    private static void AssertInside(Rect outer, Rect inner, string message)
    {
        const float tolerance = 2f;
        Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - tolerance), message + " left");
        Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + tolerance), message + " right");
        Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - tolerance), message + " bottom");
        Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + tolerance), message + " top");
    }

    private static void AssertVisibleControls(Canvas canvas, string context)
    {
        var root = (RectTransform)canvas.transform;
        var visibleButtons = new List<KeyValuePair<Component, Rect>>();
        int checkedControls = 0;
        foreach (Component control in canvas.GetComponentsInChildren(Ui("Selectable"), false))
        {
            Rect bounds = VisibleBounds(root, control);
            if (bounds.width <= 0 || bounds.height <= 0) continue;
            string message = context + " " + Path(control.transform);
            AssertInside(root.rect, bounds, message);
            Assert.That(bounds.width, Is.GreaterThan(2f), message + " collapsed width");
            Assert.That(bounds.height, Is.GreaterThan(2f), message + " collapsed height");
            checkedControls++;
            if (Ui("Button").IsInstanceOfType(control))
                visibleButtons.Add(new KeyValuePair<Component, Rect>(control, bounds));
        }
        Assert.That(checkedControls, Is.GreaterThan(0), context + " must include visible controls");
        for (int i = 0; i < visibleButtons.Count; i++)
            for (int j = i + 1; j < visibleButtons.Count; j++)
            {
                Component a = visibleButtons[i].Key, b = visibleButtons[j].Key;
                if (a.transform.parent != b.transform.parent) continue;
                Rect intersection = Intersect(visibleButtons[i].Value, visibleButtons[j].Value);
                // Original button sprites include transparent edge padding; their authored
                // rectangles overlap by five reference pixels while visible faces stay apart.
                Assert.That(intersection.width <= 6f || intersection.height <= 6f, Is.True,
                    context + " overlapping sibling buttons: " + Path(a.transform) + " and " + Path(b.transform));
            }
    }

    private static void AssertPanelInside(Canvas canvas, GameObject panel, string context)
    {
        var root = (RectTransform)canvas.transform;
        // Original roots include deliberately oversized containers and 100x100
        // placement handles. Check the visible panel surface instead of the handle.
        Transform surface = panel.name == "Save Menu" ? panel.transform.Find("Bg") : panel.name == "Load Menu"
            ? panel.transform.Find("Background/Image (1)")
            : panel.transform.Find("Background") ?? panel.transform.Find("Image");
        if (surface != null)
            AssertInside(root.rect, BoundsInRoot(root, (RectTransform)surface), context + " " + panel.name + " visible background");
        AssertVisibleControls(canvas, context);
    }

    [UnityTest]
    public IEnumerator MainMenuControlsRemainVisibleWithFourPlayersAcrossAspectRatios()
    {
        Canvas canvas = menu.GetComponent<Canvas>().rootCanvas;
        foreach (Vector2 size in Sizes)
        {
            Call(menu, "PressBack");
            Resize(canvas, size);
            yield return null;
            Resize(canvas, size);
            AssertPanelInside(canvas, (GameObject)Get(menu, "firstMenu"), "first menu " + size);
            Call(menu, "PressPlay");
            Call(menu, "Add_player", 3);
            Call(menu, "Add_player", 4);
            Resize(canvas, size);
            yield return null;
            Resize(canvas, size);
            AssertPanelInside(canvas, (GameObject)Get(menu, "mapMenu"), "four-player menu " + size);
            Assert.That(((GameObject)Get(menu, "player3_info")).activeInHierarchy, Is.True);
            Assert.That(((GameObject)Get(menu, "player4_info")).activeInHierarchy, Is.True);
            Call(menu, "PressCreateMap");
            Resize(canvas, size);
            yield return null;
            Resize(canvas, size);
            AssertPanelInside(canvas, (GameObject)Get(menu, "createmapMenu"), "map settings " + size);
        }
    }

    [UnityTest]
    public IEnumerator GameplayHudAndEachModalRemainInsideTheCanvasAcrossAspectRatios()
    {
        Call(menu, "PressStart");
        yield return null;
        yield return null;
        var controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        var ui = (Component)Get(controller, "uiManager");
        Canvas canvas = ui.GetComponent<Canvas>().rootCanvas;
        var modals = new List<GameObject>
        {
            (GameObject)Get(ui, "main_menu"), ((Component)Get(ui, "saveMenu")).gameObject,
            ((Component)Get(ui, "loadMenu")).gameObject, ((Component)Get(ui, "spawnMenu")).gameObject,
            (GameObject)Get(ui, "action_menu"), (GameObject)Get(ui, "attack_menu"), (GameObject)Get(ui, "win_menu")
        };
        var teams = (IList)Get(controller, "teams");
        object leader = Call(teams[0], "Get_king");
        object enemy = Call(teams[1], "Get_king");
        object spawn = Get(ui, "spawnMenu");
        Call(spawn, "SetCastleButtons", Get(Get(teams[0], "raceClass"), "AvailableUnits"), controller);
        Call(spawn, "ResetMenu");
        Call(modals[5].GetComponent(Script("scr_attack_menu")), "Setup", leader, enemy, 1);
        foreach (GameObject modal in modals) modal.SetActive(false);
        foreach (Vector2 size in Sizes)
        {
            Resize(canvas, size);
            yield return null;
            Resize(canvas, size);
            AssertVisibleControls(canvas, "HUD " + size);
            foreach (GameObject modal in modals)
            {
                modal.SetActive(true);
                Resize(canvas, size);
                yield return null;
                Resize(canvas, size);
                AssertPanelInside(canvas, modal, "modal " + size);
                modal.SetActive(false);
            }
        }
    }

    [UnityTest]
    public IEnumerator TerrainEditorToolsRemainVisibleAcrossAspectRatios()
    {
        Call(menu, "PressStartCreateMap");
        yield return null;
        yield return null;
        Canvas canvas = null;
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Canvas candidate in root.GetComponentsInChildren<Canvas>(true))
                if (candidate.name == "Hotbar") canvas = candidate.rootCanvas;
        Assert.That(canvas, Is.Not.Null);
        foreach (Vector2 size in Sizes)
        {
            Resize(canvas, size);
            yield return null;
            Resize(canvas, size);
            AssertVisibleControls(canvas, "terrain editor " + size);
        }
    }

    [UnityTest]
    public IEnumerator TileCardShrinksForEmptyTerrainAndRestoresItsUnitContent()
    {
        Call(menu, "PressStart");
        yield return null;
        yield return null;
        var controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        // Freeze hover input while explicitly selecting the two real tiles.
        ((Behaviour)controller).enabled = false;
        var ui = (Component)Get(controller, "uiManager");
        Canvas canvas = ui.GetComponent<Canvas>().rootCanvas;
        var teams = (IList)Get(controller, "teams");
        object leader = Call(teams[0], "Get_king");
        object populated = Get(leader, "current_tile");
        object empty = null;
        foreach (object tile in (Array)Get(Get(controller, "gameMap"), "tiles"))
            if ((GameObject)Get(tile, "unit") == null && (GameObject)Get(tile, "forest") == null &&
                (GameObject)Get(tile, "mountain") == null && !(bool)Get(tile, "has_castle"))
            { empty = tile; break; }
        Assert.That(empty, Is.Not.Null, "The test needs a real unoccupied grass tile.");
        var cardBackground = (Component)Get(Get(ui, "preview_tile"), "bg");
        var card = (RectTransform)cardBackground.transform;
        var unitInfo = (GameObject)Get(Get(ui, "preview_tile"), "unit_info");
        foreach (Vector2 size in Sizes)
        {
            Call(ui, "Update_tile_info", populated);
            Resize(canvas, size);
            yield return null;
            Resize(canvas, size);
            float expandedHeight = card.rect.height;
            Assert.That(unitInfo.activeInHierarchy, Is.True, "leader content " + size);
            foreach (string field in new[] { "unit_hp_text", "unit_mv_text", "unit_rg_text", "unit_attack_text" })
            {
                var label = (Component)Get(Get(ui, "preview_tile"), field);
                Call(label, "ForceMeshUpdate", false, false);
                var labelRect = (RectTransform)label.transform;
                AssertInside(card.rect, BoundsInRoot(card, labelRect), "unit statistics inside card " + size);
                var glyphs = (Bounds)label.GetType().GetProperty("textBounds").GetValue(label);
                AssertInside(labelRect.rect, Rect.MinMaxRect(glyphs.min.x, glyphs.min.y, glyphs.max.x, glyphs.max.y), "unit statistics without clipped glyphs " + size);
            }
            AssertInside(((RectTransform)canvas.transform).rect, BoundsInRoot((RectTransform)canvas.transform, card), "leader card " + size);
            Call(ui, "Update_tile_info", empty);
            Resize(canvas, size);
            yield return null;
            Resize(canvas, size);
            Assert.That(unitInfo.activeSelf, Is.False, "empty terrain content " + size);
            Assert.That(card.rect.height, Is.LessThan(expandedHeight - 1f), "Empty terrain must release the unit section's space: " + size);
            Call(ui, "Update_tile_info", populated);
            Resize(canvas, size);
            yield return null;
            Resize(canvas, size);
            Assert.That(unitInfo.activeInHierarchy, Is.True, "restored leader content " + size);
            Assert.That(card.rect.height, Is.EqualTo(expandedHeight).Within(2f), "Returning to a unit must restore its fitted card height: " + size);
        }
    }

    private static void AssertHealthPrediction(object healthPreview, float health, float predicted, float maximum)
    {
        foreach (string field in new[] { "health_bar", "predicted_health_bar" })
        {
            object image = Get(healthPreview, field);
            Assert.That(image, Is.Not.Null, field + " must be an authored Image");
            Assert.That(image.GetType().GetProperty("sprite").GetValue(image), Is.Not.Null, field + " must render a fill sprite");
            float actual = (float)image.GetType().GetProperty("fillAmount").GetValue(image);
            float expected = Mathf.Clamp01((field == "health_bar" ? health : predicted) / maximum);
            Assert.That(actual, Is.EqualTo(expected).Within(0.00001f), field + " must show the exact shared combat prediction");
        }
    }

    [UnityTest]
    public IEnumerator CombatPredictionReusesAuthoredPreviewsAndDisplaysHealthIntervals()
    {
        Call(menu, "PressStart");
        yield return null;
        yield return null;
        var controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        var ui = (Component)Get(controller, "uiManager");
        var attackPanel = (GameObject)Get(ui, "attack_menu");
        var attack = attackPanel.GetComponent(Script("scr_attack_menu"));
        string[] fields = { "attackerPreview", "defenderPreview", "attackerHealthPreview", "defenderHealthPreview" };
        var authored = new object[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            authored[i] = Get(attack, fields[i]);
            Assert.That(authored[i], Is.Not.Null, "Scene must author " + fields[i] + " before Setup");
        }
        int descendants = attackPanel.GetComponentsInChildren<Transform>(true).Length;
        var teams = (IList)Get(controller, "teams");
        object attacker = Call(teams[0], "Get_king"), defender = Call(teams[1], "Get_king");
        attackPanel.SetActive(true);
        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
            {
                attacker.GetType().GetField("hp", Members).SetValue(attacker, (float)Get(attacker, "max_health") * .65f);
                defender.GetType().GetField("hp", Members).SetValue(defender, (float)Get(defender, "max_health") * .8f);
            }
            var attackerStats = (Tactics.Core.Combatant)attacker.GetType().GetProperty("CombatStats").GetValue(attacker);
            var defenderStats = (Tactics.Core.Combatant)defender.GetType().GetProperty("CombatStats").GetValue(defender);
            var randomState = UnityEngine.Random.state;
            var prediction = (Tactics.Core.CombatPrediction)Call(attacker, "PredictCombat", defender, 1);
            Call(attack, "Setup", attacker, defender, 1);
            Assert.That(UnityEngine.Random.state, Is.EqualTo(randomState), "Preview must not consume luck rolls.");
            Canvas.ForceUpdateCanvases();
            yield return null;
            for (int i = 0; i < fields.Length; i++)
                Assert.That(Get(attack, fields[i]), Is.SameAs(authored[i]), fields[i] + " must persist between predictions");
            Assert.That(attackPanel.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(descendants), "Opening another prediction must not allocate or destroy preview objects");
            AssertHealthPrediction(authored[2], attackerStats.Health, prediction.MinAttackerHealth, attackerStats.MaxHealth);
            AssertHealthPrediction(authored[3], defenderStats.Health, prediction.MinDefenderHealth, defenderStats.MaxHealth);
            foreach (var field in new[] { "p1_stats_info", "p2_stats_info" })
            {
                var label = ((GameObject)Get(attack, field)).GetComponent(Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro", true));
                string text = (string)label.GetType().GetProperty("text").GetValue(label);
                Assert.That(text, Does.Contain("–"), "Both stats cards must expose the luck interval.");
                Assert.That(text, Does.Contain("After:"));
            }
        }
    }

    [UnityTest]
    public IEnumerator CastleBodyAndRoofStayAlignedInsideTheTerrainPreview()
    {
        Call(menu, "PressStart");
        yield return null; yield return null;
        var controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        ((Behaviour)controller).enabled = false;
        var ui = (Component)Get(controller, "uiManager");
        Canvas canvas = ui.GetComponent<Canvas>();
        object castleTile = null;
        foreach (object tile in (Array)Get(Get(controller, "gameMap"), "tiles"))
            if ((bool)Get(tile, "has_castle")) { castleTile = tile; break; }
        Assert.That(castleTile, Is.Not.Null);
        var preview = (Component)Get(ui, "preview_tile");
        var body = (Component)Get(preview, "castle");
        var roof = (Component)Get(preview, "castleRoof");
        foreach (Vector2 size in Sizes)
        {
            Call(ui, "Update_tile_info", castleTile);
            Resize(canvas, size);
            yield return null;
            Resize(canvas, size);
            var rootRect = (RectTransform)canvas.transform;
            Rect bodyBounds = BoundsInRoot(rootRect, (RectTransform)body.transform);
            Rect roofBounds = BoundsInRoot(rootRect, (RectTransform)roof.transform);
            Assert.That(body.gameObject.activeInHierarchy, Is.True);
            Assert.That(roof.gameObject.activeInHierarchy, Is.True);
            Assert.That(bodyBounds.width, Is.GreaterThan(10f));
            Assert.That(bodyBounds.width, Is.EqualTo(bodyBounds.height).Within(1f), "Castle artwork must remain square " + size);
            Assert.That(roofBounds.center.x, Is.EqualTo(bodyBounds.center.x).Within(.5f), "roof horizontal alignment " + size);
            Assert.That(roofBounds.center.y, Is.EqualTo(bodyBounds.center.y).Within(.5f), "roof vertical alignment " + size);
            Assert.That(roofBounds.width, Is.EqualTo(bodyBounds.width).Within(.5f), "roof width " + size);
            Assert.That(roofBounds.height, Is.EqualTo(bodyBounds.height).Within(.5f), "roof height " + size);
            // The original sprite rectangles include transparent padding outside
            // the border; matching body and roof geometry is the alignment contract.
            AssertInside(rootRect.rect, bodyBounds, "castle artwork remains on screen " + size);
        }
    }

    [UnityTest]
    public IEnumerator PlayerHudKeepsNameAndLargeMoneyValueReadableInsideItsPanel()
    {
        Call(menu, "PressStart");
        yield return null; yield return null;
        var controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        ((Behaviour)controller).enabled = false;
        var teams = (IList)Get(controller, "teams");
        teams[0].GetType().GetField("money").SetValue(teams[0], 9876543);
        Call(controller, "Update_player_info");
        var ui = (Component)Get(controller, "uiManager");
        Canvas canvas = ui.GetComponent<Canvas>();
        var playerInfo = (GameObject)Get(ui, "player_info");
        var panel = (RectTransform)playerInfo.transform.Find("Image");
        Assert.That(panel, Is.Not.Null, "The original player panel must remain authored.");
        var name = ((GameObject)Get(ui, "turn_name")).GetComponent(Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro", true));
        var money = ((GameObject)Get(ui, "money_text")).GetComponent(name.GetType());
        Assert.That((string)money.GetType().GetProperty("text").GetValue(money), Does.Contain("9876543"));
        foreach (Vector2 size in Sizes)
        {
            Resize(canvas, size);
            yield return null;
            Resize(canvas, size);
            var visibleLabels = new List<Rect>();
            foreach (Component text in new[] { name, money })
            {
                Call(text, "ForceMeshUpdate", false, false);
                var rect = (RectTransform)text.transform;
                AssertInside(panel.rect, BoundsInRoot(panel, rect), "HUD label inside paper panel " + size);
                var glyphs = (Bounds)text.GetType().GetProperty("textBounds").GetValue(text);
                Assert.That(glyphs.size.x, Is.GreaterThan(1f));
                // Overflow-mode labels render font ascenders/descenders outside their small
                // placeholder rect. The visible paper is the actual clipping boundary.
                Vector3 glyphMin = panel.InverseTransformPoint(rect.TransformPoint(glyphs.min));
                Vector3 glyphMax = panel.InverseTransformPoint(rect.TransformPoint(glyphs.max));
                var visible = Rect.MinMaxRect(glyphMin.x, glyphMin.y, glyphMax.x, glyphMax.y);
                AssertInside(panel.rect, visible, "HUD glyphs inside visible paper " + size);
                visibleLabels.Add(visible);
            }
            // Authored placeholder rects can overlap while their rendered text remains separate.
            Rect overlap = Intersect(visibleLabels[0], visibleLabels[1]);
            Assert.That(overlap.width <= 0 || overlap.height <= 0, Is.True, "Player and money lines must not overlap " + size);
        }
    }

    [UnityTest]
    public IEnumerator VerticalViewportBlocksWorldInputUntilLandscapeReturns()
    {
        Call(menu, "PressStart");
        yield return null; yield return null;
        var controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        ((Behaviour)controller).enabled = false;
        var orientation = (Component)UnityEngine.Object.FindFirstObjectByType(Script("LandscapeOrientation"));
        Assert.That(orientation, Is.Not.Null);
        ((Behaviour)orientation).enabled = false; // Drive the viewport transition explicitly in this test.
        var notice = (GameObject)Get(orientation, "portraitNotice");
        var ui = (Component)Get(controller, "uiManager");
        ((GameObject)Get(ui, "tile_info")).SetActive(false);
        var leader = (Component)Call(((IList)Get(controller, "teams"))[0], "Get_king");
        Camera camera = Camera.main;
        ((Behaviour)camera.GetComponent(Script("scr_camera"))).enabled = false;
        camera.transform.position = new Vector3(leader.transform.position.x, leader.transform.position.y, camera.transform.position.z);
        Physics.SyncTransforms();
        Vector2 point = new Vector2(Screen.width * .5f, Screen.height * .5f);
        var map = Get(controller, "gameMap");
        map.GetType().GetField("cooldown").SetValue(map, 0f);
        Call(orientation, "ApplyViewportSize", 1080, 1920);
        yield return null;
        Canvas.ForceUpdateCanvases();
        Assert.That(notice.activeInHierarchy, Is.True);
        var events = UnityEngine.EventSystems.EventSystem.current;
        var hits = new List<UnityEngine.EventSystems.RaycastResult>();
        events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events) { position = point }, hits);
        Assert.That(hits.Exists(hit => hit.gameObject == notice), Is.True, "The orientation notice must also intercept UI button events");
        Assert.That(Script("WorldPointer").GetMethod("IsOverUI").Invoke(null, new object[] { point }), Is.EqualTo(true));
        Assert.That(Call(controller, "SelectWorldAt", point), Is.EqualTo(false), "The covered board cannot receive clicks");
        Call(orientation, "ApplyViewportSize", 1920, 1080);
        Canvas.ForceUpdateCanvases();
        Assert.That(notice.activeSelf, Is.False);
        Assert.That(Script("WorldPointer").GetMethod("IsOverUI").Invoke(null, new object[] { point }), Is.EqualTo(false));
        Assert.That(Call(controller, "SelectWorldAt", point), Is.EqualTo(true), "Landscape restores normal unit selection");
    }
}
