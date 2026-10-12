using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class MatchRegressionTests
{
    private bool originalBackground;
    private Component controller;
    private static Type Script(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Get(object target, string field) => target.GetType().GetField(field).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field).SetValue(target, value);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method).Invoke(target, args);
    private IList Teams => (IList)Get(controller, "teams");
    private object Map => Get(controller, "gameMap");
    private object King(int team) => Call(Teams[team], "Get_king");
    private object Snapshot() => Activator.CreateInstance(Script("GameSave"), new object[] { controller, "Regression" });
    private bool Load(object save, out string error)
    {
        object[] args = { save, null };
        bool success = (bool)controller.GetType().GetMethod("TryLoadSnapshot").Invoke(controller, args);
        error = (string)args[1];
        return success;
    }
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        originalBackground = Application.runInBackground;
        Application.runInBackground = true;
        SceneManager.LoadScene("Main Menu");
        yield return null;
        yield return null;
        var menu = UnityEngine.Object.FindFirstObjectByType(Script("MainMenu"));
        Call(menu, "PressStart");
        yield return null;
        yield return null;
        controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        Assert.That(controller, Is.Not.Null);
        Assert.That(Get(controller, "status"), Is.EqualTo("Select_tile"));
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
    public IEnumerator RepeatedSaveLoadPreservesEntireMatchWithoutLeaderInflation()
    {
        var snapshot = Snapshot();
        var savedState = Get(snapshot, "controller");
        string expected = JsonUtility.ToJson(savedState);
        float maximum = (float)Get(King(0), "max_health");
        for (int iteration = 0; iteration < 3; iteration++)
        {
            var deserialized = JsonUtility.FromJson(JsonUtility.ToJson(snapshot), Script("GameSave"));
            string error;
            Assert.That(Load(deserialized, out error), Is.True, error);
            yield return null;
            Assert.That((float)Get(King(0), "max_health"), Is.EqualTo(maximum));
            Assert.That(((Component)King(0)).transform.parent, Is.SameAs(Get(Map, "unitsRoot")), "Loading must preserve the configured actor hierarchy.");
            Assert.That(JsonUtility.ToJson(Get(Snapshot(), "controller")), Is.EqualTo(expected));
            Assert.That(Get(controller, "status"), Is.EqualTo("Select_tile"));
        }
    }
    [UnityTest]
    public IEnumerator InvalidSaveNeverReplacesTheLiveMapOrUnits()
    {
        object originalTiles = Get(Map, "tiles");
        object originalKing = King(0);
        var snapshot = Snapshot();
        var savedMap = Get(Get(snapshot, "controller"), "gameMap");
        ((IList)Get(savedMap, "serializedTiles")).RemoveAt(0);
        string error;
        Assert.That(Load(snapshot, out error), Is.False);
        Assert.That(error, Is.Not.Empty);
        Assert.That(Get(Map, "tiles"), Is.SameAs(originalTiles));
        Assert.That(King(0), Is.SameAs(originalKing));
        Assert.That(((Component)originalKing).gameObject.activeSelf, Is.True);
        yield return null;
        Assert.That(Get(controller, "status"), Is.EqualTo("Select_tile"));
    }
    [UnityTest]
    public IEnumerator DefeatingLeaderEliminatesArmyAndReleasesCastles()
    {
        var tiles = (Array)Get(Map, "tiles");
        object tile = null;
        foreach (object candidate in tiles)
            if ((GameObject)Get(candidate, "castle") != null && (GameObject)Get(candidate, "unit") == null) { tile = candidate; break; }
        Assert.That(tile, Is.Not.Null, "The map must contain an unoccupied tile supporting castles.");
        object team = Teams[0];
        var race = Get(team, "raceClass");
        var prefab = (GameObject)((IList)Get(race, "AvailableUnits"))[0];
        var soldierGo = UnityEngine.Object.Instantiate(prefab);
        var soldier = soldierGo.GetComponent(Script("Unit"));
        Call(soldier, "Change_current_tile", tile);
        Call(team, "Add", soldier);
        Call(tile, "Activate_castle");
        var castle = ((GameObject)Get(tile, "castle")).GetComponent(Script("Castle"));
        Call(castle, "Capture_castle", team);
        Call(King(0), "Take_damage", 10000f);
        Call(Map, "Check_win");
        Assert.That((bool)Get(team, "has_lost"), Is.True);
        Assert.That((bool)Get(soldier, "has_died"), Is.True);
        Assert.That(Get(tile, "unit"), Is.Null);
        Assert.That(Get(castle, "current_team"), Is.Null);
        Assert.That(Get(controller, "status"), Is.EqualTo("Over"));
        yield return null;
    }
    [UnityTest]
    public IEnumerator BothLeaderDeathsInOneResolutionProduceDraw()
    {
        Call(King(0), "Take_damage", 10000f);
        Call(King(1), "Take_damage", 10000f);
        Call(Map, "Check_win");
        var ui = Get(controller, "uiManager");
        var label = (Component)Get(ui, "win_text");
        Assert.That(label.GetType().GetProperty("text").GetValue(label), Is.EqualTo("Draw"));
        Assert.That(Get(controller, "status"), Is.EqualTo("Over"));
        yield return null;
    }
    [UnityTest]
    public IEnumerator FourthPlayerWithoutThirdPlayerCreatesThreeValidTeams()
    {
        SceneManager.LoadScene("Main Menu");
        yield return null;
        yield return null;
        Script("MainMenu").GetField("player3IsOn").SetValue(null, false);
        Script("MainMenu").GetField("player4IsOn").SetValue(null, true);
        var menu = UnityEngine.Object.FindFirstObjectByType(Script("MainMenu"));
        Call(menu, "PressStart");
        yield return null;
        yield return null;
        controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        Assert.That(Teams.Count, Is.EqualTo(3));
        foreach (var team in Teams) Assert.That(Call(team, "Get_king"), Is.Not.Null);
        Assert.That(Get(controller, "status"), Is.EqualTo("Select_tile"));
    }

    [UnityTest]
    public IEnumerator DirectSceneStartupUsesConfiguredFallbackRace()
    {
        ((IList)Script("MainMenu").GetField("RaceDefinitions").GetValue(null)).Clear();
        SceneManager.LoadScene("Main Scene");
        yield return null;
        yield return null;
        controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        Assert.That(Get(controller, "status"), Is.EqualTo("Select_tile"));
        Assert.That(King(0), Is.Not.Null);
        Assert.That(King(1), Is.Not.Null);
    }
    [UnityTest]
    public IEnumerator TurnSkipsDefeatedPlayerWithoutResettingInactivePlayers()
    {
        SceneManager.LoadScene("Main Menu");
        yield return null;
        yield return null;
        Script("MainMenu").GetField("player3IsOn").SetValue(null, true);
        Script("MainMenu").GetField("player4IsOn").SetValue(null, true);
        Call(UnityEngine.Object.FindFirstObjectByType(Script("MainMenu")), "PressStart");
        yield return null;
        yield return null;
        controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        Set(King(3), "has_moved", true);
        int eliminatedMoney = (int)Get(Teams[1], "money");
        Call(King(1), "Take_damage", 10000f);
        Call(controller, "Finalize_turn");
        Assert.That(Get(controller, "turn"), Is.EqualTo(2));
        Assert.That(Get(King(3), "has_moved"), Is.EqualTo(true));
        Assert.That(Get(Teams[1], "money"), Is.EqualTo(eliminatedMoney));
        Assert.That(Get(controller, "status"), Is.EqualTo("Select_tile"));
    }
    [UnityTest]
    public IEnumerator LegacySaveOfFinishedMatchRestoresDefeatAndTurn()
    {
        Call(King(0), "Take_damage", 10000f);
        Call(Map, "Check_win");
        int endedTurn = (int)Get(controller, "turn");
        var snapshot = Snapshot();
        Set(snapshot, "schemaVersion", 0);
        string error;
        Assert.That(Load(snapshot, out error), Is.True, error);
        yield return null;
        Assert.That(Get(Teams[0], "has_lost"), Is.EqualTo(true));
        Assert.That(Get(controller, "status"), Is.EqualTo("Over"));
        Assert.That(Get(controller, "turn"), Is.EqualTo(endedTurn));
    }

    private object TileAt(int x, int y) => ((Array)Get(Map, "tiles")).GetValue(x, y);
    private void SetUpCorridor()
    {
        foreach (var tile in (Array)Get(Map, "tiles")) Set(tile, "movement_cost", 100000);
        for (int x = 2; x <= 4; x++)
        {
            var tile = TileAt(x, 2);
            Set(tile, "movement_cost", 1);
            Set(tile, "mountain", null);
            Set(tile, "forest", null);
        }
        var leader = (Component)King(0);
        Call(leader, "Change_current_tile", TileAt(2, 2));
        Set(leader, "movement", 3);
        Set(leader, "has_moved", false);
        Call(Map, "ResetInteraction");
        Call(Map, "Select_tile", TileAt(2, 2), Teams[0]);
    }
    private Component AddSoldier(int x, int y)
    {
        var race = Get(Teams[0], "raceClass");
        var prefab = (GameObject)((IList)Get(race, "AvailableUnits"))[0];
        var soldier = UnityEngine.Object.Instantiate(prefab).GetComponent(Script("Unit"));
        Call(soldier, "Change_current_tile", TileAt(x, y));
        Call(Teams[0], "Add", soldier);
        return soldier;
    }
    [UnityTest]
    public IEnumerator ClickUsesDestinationInsteadOfPreviousHoverPath()
    {
        SetUpCorridor();
        Set(Map, "hover_tile", ((Component)TileAt(3, 2)).gameObject);
        Call(Map, "Draw_path_line");
        Assert.That(((IList)Get(Map, "path")).Count, Is.Zero, "Hover must not commit a movement plan.");
        // Corrupt the visual state too: it must not authorize movement.
        Set(TileAt(4, 2), "state", null);
        Call(Map, "Select_tile", TileAt(4, 2), Teams[0]);
        var plan = (IList)Get(Map, "path");
        Assert.That(plan[plan.Count - 1], Is.SameAs(TileAt(4, 2)));
        Call(King(0), "StopMovement");
        Call(Map, "Finalize_move");
        Assert.That(Get(King(0), "current_tile"), Is.SameAs(TileAt(4, 2)));
        Assert.That(Get(TileAt(2, 2), "unit"), Is.Null);
        Assert.That(Get(TileAt(4, 2), "unit"), Is.SameAs(((Component)King(0)).gameObject));
        yield return null;
    }
    [UnityTest]
    public IEnumerator MovementTraversesAllyWithoutReplacingItsOccupancy()
    {
        SetUpCorridor();
        var soldier = AddSoldier(3, 2);
        Call(Map, "Select_tile", TileAt(4, 2), Teams[0]);
        var plan = (IList)Get(Map, "path");
        Assert.That(plan.Count, Is.EqualTo(3));
        Assert.That(plan[1], Is.SameAs(TileAt(3, 2)));
        Call(King(0), "StopMovement");
        Call(Map, "Finalize_move");
        Assert.That(Get(TileAt(3, 2), "unit"), Is.SameAs(soldier.gameObject));
        Assert.That(Get(soldier, "current_tile"), Is.SameAs(TileAt(3, 2)));
        yield return null;
    }
    [UnityTest]
    public IEnumerator OccupiedDestinationNeverClearsOriginalOccupancy()
    {
        SetUpCorridor();
        var soldier = AddSoldier(3, 2);
        var exception = Assert.Throws<TargetInvocationException>(() => Call(King(0), "Change_current_tile", TileAt(3, 2)));
        Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(Get(TileAt(2, 2), "unit"), Is.SameAs(((Component)King(0)).gameObject));
        Assert.That(Get(TileAt(3, 2), "unit"), Is.SameAs(soldier.gameObject));
        yield return null;
    }
    [UnityTest]
    public IEnumerator CancelStopsAnimationAndReturnsToOriginalTileWithoutUsingAction()
    {
        SetUpCorridor();
        Call(Map, "Select_tile", TileAt(4, 2), Teams[0]);
        Assert.That(((IList)Get(King(0), "current_path")).Count, Is.GreaterThan(0));
        Call(Map, "Cancel_move");
        yield return null;
        Assert.That(((IList)Get(King(0), "current_path")).Count, Is.Zero);
        Assert.That(Get(King(0), "current_tile"), Is.SameAs(TileAt(2, 2)));
        Assert.That(((Component)King(0)).transform.position, Is.EqualTo(((Component)TileAt(2, 2)).transform.position));
        Assert.That(Get(King(0), "has_moved"), Is.EqualTo(false));
        Assert.That(Get(controller, "status"), Is.EqualTo("Select_tile"));
    }
    [UnityTest]
    public IEnumerator RangedClickIgnoresMountainAndDoesNotUseHoveredDestination()
    {
        SetUpCorridor();
        Set(King(0), "movement", 0);
        Set(King(0), "attack_range", 3);
        var obstacle = new GameObject("Range test wall");
        SceneManager.MoveGameObjectToScene(obstacle, controller.gameObject.scene);
        Set(TileAt(3, 2), "mountain", obstacle);
        Call(King(1), "Change_current_tile", TileAt(4, 2));
        float previousHealth = (float)Get(King(1), "hp");
        Call(Map, "Select_tile", TileAt(4, 2), Teams[0]);
        var plan = (IList)Get(Map, "path");
        Assert.That(plan.Count, Is.EqualTo(1));
        Assert.That(plan[0], Is.SameAs(TileAt(2, 2)));
        Assert.That(Get(King(0), "target_attack_tile"), Is.SameAs(TileAt(4, 2)));
        Call(King(0), "StopMovement");
        Call(Map, "Handle_attack");
        Assert.That((float)Get(King(1), "hp"), Is.LessThan(previousHealth));
        Assert.That(Get(King(0), "current_tile"), Is.SameAs(TileAt(2, 2)));
        yield return null;
    }

    [UnityTest]
    public IEnumerator EndingTurnRollsBackUnconfirmedMovement()
    {
        SetUpCorridor();
        var original = (Component)King(0);
        Call(Map, "Select_tile", TileAt(4, 2), Teams[0]);
        original.transform.position += new Vector3(32, 0, 0);
        Call(controller, "Finalize_turn");
        yield return null;
        Assert.That(Get(controller, "turn"), Is.EqualTo(1));
        Assert.That(((IList)Get(original, "current_path")).Count, Is.Zero);
        Assert.That(Get(original, "current_tile"), Is.SameAs(TileAt(2, 2)));
        Assert.That(original.transform.position, Is.EqualTo(((Component)TileAt(2, 2)).transform.position));
        Assert.That(Get(TileAt(4, 2), "unit"), Is.Null);
    }
    private object PrepareBuildTile()
    {
        SetUpCorridor();
        var oldTile = (Component)TileAt(3, 2);
        var replacement = UnityEngine.Object.Instantiate((GameObject)Get(controller, "grass_tile"), oldTile.transform.position, Quaternion.identity, ((Component)Map).transform);
        var tile = replacement.GetComponent(Script("Tile"));
        Set(tile, "position_x", 3);
        Set(tile, "position_y", 2);
        ((Array)Get(Map, "tiles")).SetValue(tile, 3, 2);
        oldTile.gameObject.SetActive(false);
        UnityEngine.Object.Destroy(oldTile.gameObject);
        foreach (var candidate in (Array)Get(Map, "tiles"))
        {
            if (Math.Abs((int)Get(candidate, "position_x") - 3) > 2 || Math.Abs((int)Get(candidate, "position_y") - 2) > 2) continue;
            Set(candidate, "has_castle", false);
            var castle = (GameObject)Get(candidate, "castle");
            if (castle != null) castle.SetActive(false);
        }
        Script("MainMenu").GetField("build_mode").SetValue(null, true);
        Set(Teams[0], "money", 10000);
        Set(Map, "cooldown", 0f);
        Call(Map, "Select_tile", TileAt(2, 2), Teams[0]);
        Call(King(0), "StopMovement");
        return tile;
    }
    [UnityTest]
    public IEnumerator BuildConsumesLeaderActionAndChargesOnce()
    {
        object tile = PrepareBuildTile();
        Call(Map, "Start_build");
        Assert.That(Get(tile, "can_build"), Is.EqualTo("can_build"));
        Call(Map, "Finalize_build", tile);
        Assert.That(Get(tile, "has_castle"), Is.EqualTo(true));
        Assert.That(Get(Teams[0], "money"), Is.EqualTo(6500));
        Assert.That(Get(King(0), "has_moved"), Is.EqualTo(true));
        Assert.That(Get(controller, "status"), Is.EqualTo("Select_tile"));
        Call(Map, "Finalize_build", tile);
        Assert.That(Get(Teams[0], "money"), Is.EqualTo(6500));
        yield return null;
    }
    [UnityTest]
    public IEnumerator BuildRevalidatesStormEvenWhenOverlayWasPreviouslyLegal()
    {
        object tile = PrepareBuildTile();
        Call(Map, "Start_build");
        Assert.That(Get(tile, "can_build"), Is.EqualTo("can_build"));
        Call(tile, "Activate_storm");
        Call(Map, "Finalize_build", tile);
        Assert.That(Get(tile, "has_castle"), Is.EqualTo(false));
        Assert.That(Get(Teams[0], "money"), Is.EqualTo(10000));
        Assert.That(Get(King(0), "has_moved"), Is.EqualTo(false));
        Call(Map, "Start_build");
        Assert.That(Get(tile, "can_build"), Is.EqualTo("false"));
        yield return null;
    }

    [UnityTest]
    public IEnumerator OpeningMainMenuRollsBackUnconfirmedMovement()
    {
        SetUpCorridor();
        var original = (Component)King(0);
        Call(Map, "Select_tile", TileAt(4, 2), Teams[0]);
        original.transform.position += new Vector3(32, 0, 0);
        Call(controller, "Menu_button");
        yield return null;
        Assert.That(Get(controller, "status"), Is.EqualTo("Menu"));
        Assert.That(((IList)Get(original, "current_path")).Count, Is.Zero);
        Assert.That(original.transform.position, Is.EqualTo(((Component)TileAt(2, 2)).transform.position));
        Assert.That(Get(original, "has_moved"), Is.EqualTo(false));
        Call(controller, "Back_button");
        Assert.That(Get(controller, "status"), Is.EqualTo("Select_tile"));
    }
    private int CountStormTiles()
    {
        int count = 0;
        foreach (var tile in (Array)Get(Map, "tiles")) if ((bool)Get(tile, "has_storm")) count++;
        return count;
    }
    [UnityTest]
    public IEnumerator StormClosesOncePerRoundForTwoThreeAndFourPlayers()
    {
        for (int players = 2; players <= 4; players++)
        {
            SceneManager.LoadScene("Main Menu");
            yield return null;
            yield return null;
            Assert.That(Script("MainMenu").GetField("battle_royale_mode").GetValue(null), Is.EqualTo(false), "The menu must reset the previous match's mode.");
            Script("MainMenu").GetField("player3IsOn").SetValue(null, players >= 3);
            Script("MainMenu").GetField("player4IsOn").SetValue(null, players >= 4);
            Script("MainMenu").GetField("battle_royale_mode").SetValue(null, true);
            Call(UnityEngine.Object.FindFirstObjectByType(Script("MainMenu")), "PressStart");
            yield return null;
            yield return null;
            controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
            Assert.That(Teams.Count, Is.EqualTo(players));
            Assert.That(CountStormTiles(), Is.Zero);
            for (int seat = 1; seat < players; seat++)
            {
                Call(controller, "Finalize_turn");
                Assert.That(CountStormTiles(), Is.Zero);
            }
            Call(controller, "Finalize_turn");
            int width = (int)Get(Map, "max_x"), height = (int)Get(Map, "max_y");
            int perimeter = width * height - (width - 2) * (height - 2);
            Assert.That(CountStormTiles(), Is.EqualTo(perimeter));
            for (int seat = 1; seat < players; seat++)
            {
                Call(controller, "Finalize_turn");
                Assert.That(CountStormTiles(), Is.EqualTo(perimeter));
            }
        }
    }

    private static bool IsUIAt(Vector2 point) => (bool)Script("WorldPointer").GetMethod("IsOverUI").Invoke(null, new object[] { point });
    [UnityTest]
    public IEnumerator WorldDecorationsDoNotBlockScreenClickOnUnit()
    {
        var ui = Get(controller, "uiManager");
        foreach (string field in new[] { "player_info", "turn_info", "buttons", "tile_info" }) ((GameObject)Get(ui, field)).SetActive(false);
        var leader = (Component)King(0);
        Camera.main.transform.position = leader.transform.position + new Vector3(0, 0, -10);
        Canvas.ForceUpdateCanvases();
        Physics.SyncTransforms();
        yield return null;
        Vector2 point = Camera.main.WorldToScreenPoint(leader.transform.position);
        Assert.That(IsUIAt(point), Is.False, "The world backdrop and tile labels must not intercept a unit click.");
        Vector2 healthPoint = Camera.main.WorldToScreenPoint(leader.transform.position + new Vector3(0, -20, 0));
        Assert.That(IsUIAt(healthPoint), Is.False, "The unit's health bar must not block world input.");
        Set(Map, "cooldown", 0f);
        Assert.That(Call(controller, "SelectWorldAt", point), Is.EqualTo(true));
        Assert.That(Get(Map, "selected_tile"), Is.SameAs(((Component)Get(leader, "current_tile")).gameObject));
        Assert.That(Get(Map, "is_unit_selected"), Is.EqualTo(true));
    }
    [UnityTest]
    public IEnumerator InteractiveButtonStillBlocksSelectionOfTheWorldBehindIt()
    {
        Call(Map, "ResetInteraction");
        Set(Map, "cooldown", 0f);
        var ui = Get(controller, "uiManager");
        var buttons = ((GameObject)Get(ui, "buttons")).GetComponentsInChildren<UnityEngine.UI.Button>(true);
        Assert.That(buttons.Length, Is.GreaterThan(0));
        Canvas.ForceUpdateCanvases();
        yield return null;
        var rect = (RectTransform)buttons[0].transform;
        Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        Assert.That(IsUIAt(point), Is.True);
        Assert.That(Call(controller, "SelectWorldAt", point), Is.EqualTo(false));
        Assert.That(Get(Map, "selected_tile"), Is.Null);
    }
    [UnityTest]
    public IEnumerator OpeningCastleKeepsRecruitmentListInsideItsViewport()
    {
        object tile = null;
        foreach (object candidate in (Array)Get(Map, "tiles"))
            if ((GameObject)Get(candidate, "castle") != null && (GameObject)Get(candidate, "unit") == null) { tile = candidate; break; }
        Assert.That(tile, Is.Not.Null);
        Call(tile, "Activate_castle");
        Call(((GameObject)Get(tile, "castle")).GetComponent(Script("Castle")), "Capture_castle", Teams[0]);
        Call(Map, "Select_tile", tile, Teams[0]);
        var spawn = (Component)Get(Get(controller, "uiManager"), "spawnMenu");
        var content = (RectTransform)((GameObject)Get(spawn, "spawnPanel")).transform;
        var scroll = content.GetComponentInParent<UnityEngine.UI.ScrollRect>();
        Canvas.ForceUpdateCanvases();
        yield return null;
        Assert.That(Get(controller, "status"), Is.EqualTo("Castle"));
        Assert.That(content.childCount, Is.GreaterThan(0));
        Assert.That(Mathf.Abs(content.anchoredPosition.y), Is.LessThan(1000f), "Reset must use ScrollRect rather than hiding content at -100000.");
        Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1f).Within(.01f));
        var first = content.GetChild(0).GetComponent<UnityEngine.UI.Button>();
        Assert.That(first, Is.Not.Null);
        first.onClick.Invoke();
        yield return null;
        var spawnButton = (UnityEngine.UI.Button)spawn.GetType().GetField("spawnButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(spawn);
        Assert.That(spawnButton.interactable, Is.True);
    }
}
