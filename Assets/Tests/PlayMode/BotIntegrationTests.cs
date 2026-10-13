using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class BotIntegrationTests
{
    private Component controller;
    private bool previousBackground;
    private static Type Script(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method).Invoke(target, args);
    private IList Teams => (IList)Get(controller, "teams");
    private MatchState State() => (MatchState)Call(controller, "GetStateSnapshot");
    private object Snapshot() => Activator.CreateInstance(Script("GameSave"), new object[] { controller, "BotRegression" });
    private Component Runner => controller.GetComponent(Script("BotRunner"));
    private bool Load(object saved, out string error)
    {
        object[] args = { saved, null }; bool success = (bool)Call(controller, "TryLoadSnapshot", args);
        error = (string)args[1]; return success;
    }
    private void BeginBotTurn()
    {
        Set(Teams[1], "isBot", true);
        Set(Runner, "actionDelay", 0f); Set(Runner, "delay", 0f);
        Call(controller, "Finalize_turn");
        Assert.That(State().ActivePlayer, Is.EqualTo(1));
    }
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        previousBackground = Application.runInBackground; Application.runInBackground = true;
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        Call(UnityEngine.Object.FindFirstObjectByType(Script("MainMenu")), "PressStart"); yield return null; yield return null;
        controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
    }
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        Application.runInBackground = previousBackground;
    }
    [UnityTest]
    public IEnumerator FourNativeMenuButtonsConfigureHumanAndBotTeams()
    {
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        var menu = UnityEngine.Object.FindFirstObjectByType(Script("MainMenu"));
        Call(menu, "PressPlay");
        Call(menu, "Add_player", 3); Call(menu, "Add_player", 4);
        yield return null;
        var selectors = UnityEngine.Object.FindObjectsByType(Script("ClassSelect"), FindObjectsInactive.Include, FindObjectsSortMode.None);
        Assert.That(selectors.Length, Is.EqualTo(4));
        foreach (var selector in selectors)
        {
            int slot = (int)selector.GetType().GetProperty("PlayerSlot").GetValue(selector);
            var button = Get(selector, "controlButton"); Assert.That(button, Is.Not.Null);
            if (slot == 0) continue;
            var click = button.GetType().GetProperty("onClick").GetValue(button);
            Call(click, "Invoke");
        }
        Call(menu, "PressStart");
        yield return null; yield return null;
        controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        Assert.That(Teams.Count, Is.EqualTo(4));
        for (int p = 0; p < 4; p++) Assert.That(Get(Teams[p], "isBot"), Is.EqualTo(p != 0));
    }
    [UnityTest]
    public IEnumerator BotAttackWithoutMovementPlaysSound() => VerifyBotAttackSound(3);

    [UnityTest]
    public IEnumerator BotAttackAfterMovementPlaysSound() => VerifyBotAttackSound(4);

    private IEnumerator VerifyBotAttackSound(int botStartX)
    {
        ((Behaviour)Runner).enabled = false;
        var saved = Snapshot(); var savedState = Get(saved, "controller");
        foreach (object tile in (IList)Get(Get(savedState, "gameMap"), "serializedTiles"))
        {
            int x = (int)Get(tile, "position_x"), y = (int)Get(tile, "position_y");
            if (y != 2 || x < 2 || x > 4) continue;
            Set(tile, "tileName", "Grass field"); Set(tile, "hasCastle", false);
            Set(tile, "castleTeam", ""); Set(tile, "hasStorm", false);
        }
        for (int player = 0; player < 2; player++)
        {
            var leader = ((IList)Get(((IList)Get(savedState, "teams"))[player], "units"))[0];
            Set(leader, "positionX", player == 0 ? 2 : botStartX); Set(leader, "positionY", 2);
        }
        Assert.That(Load(saved, out string error), Is.True, error);
        var audio = Get(controller, "audioManager");
        var source = (AudioSource)Get(audio, "SFXSource");
        var originalClip = (AudioClip)Get(audio, "attack");
        var testClip = AudioClip.Create("Bot attack regression", 88200, 1, 44100, false);
        try
        {
            Set(audio, "attack", testClip); source.Stop();
            BeginBotTurn();
            Assert.That(source.isPlaying, Is.False, "Ending a turn must not play an action sound.");
            var before = State(); var bot = before.Players[1].Units[0]; var human = before.Players[0].Units[0];
            var action = new MatchAction(MatchActionKind.Attack, new UnitKey(1, bot.Id),
                new GridPosition(3, 2), new UnitKey(0, human.Id));
            Runner.GetType().GetMethod("Execute", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Runner, new object[] { action });
            Assert.That(State().FindUnit(new UnitKey(0, human.Id)).Health, Is.LessThan(human.Health));
            Assert.That(source.isPlaying, Is.True, "A confirmed bot attack must reach the SFX source.");
            Assert.That(Runner.GetType().GetProperty("IsAnimating").GetValue(Runner), Is.EqualTo(botStartX != 3));
            yield return null;
        }
        finally
        {
            source.Stop(); Set(audio, "attack", originalClip); UnityEngine.Object.Destroy(testClip);
        }
    }

    [UnityTest]
    public IEnumerator BotCompletesItsTurnAndReturnsControlToHuman()
    {
        var human = State().Players[0].Units[0];
        BeginBotTurn();
        float deadline = Time.realtimeSinceStartup + 20;
        while (State().ActivePlayer == 1 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(State().ActivePlayer, Is.EqualTo(0), "Bot failed to finish its turn.");
        Assert.That(State().Turn, Is.EqualTo(2));
        Assert.That(State().Players[0].Units[0].Position, Is.EqualTo(human.Position));
        var camera = ((GameObject)Get(controller, "game_camera")).GetComponent(Script("scr_camera"));
        Assert.That(Get(camera, "is_following"), Is.False, "Human must regain camera control after bot movement.");
        Assert.That(Runner.GetType().GetProperty("LastDecision").GetValue(Runner), Is.Not.Null);
    }
    [UnityTest]
    public IEnumerator BotCanControlFirstPlayerAndPausesItsMovementInMenu()
    {
        Set(Teams[0], "isBot", true); Set(Runner, "delay", 0f); Set(Runner, "actionDelay", 0f);
        float deadline = Time.realtimeSinceStartup + 15;
        while (!(bool)Runner.GetType().GetProperty("IsAnimating").GetValue(Runner) && State().Turn == 0
            && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(Runner.GetType().GetProperty("IsAnimating").GetValue(Runner), Is.True);
        var actor = (Component)Get(Runner, "animatedUnit");
        Call(controller, "Show_Menu"); var pausedPosition = actor.transform.position;
        yield return new WaitForSecondsRealtime(.25f);
        Assert.That(actor.transform.position, Is.EqualTo(pausedPosition));
        Call(controller, "Hide_Menu");
        deadline = Time.realtimeSinceStartup + 15;
        while (State().Turn == 0 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(State().Turn, Is.EqualTo(1));
        Assert.That(Get(Teams[1], "isBot"), Is.False);
    }
    [UnityTest]
    public IEnumerator MenuPausesBotAndHumanCannotSkipOrIssueActionsDuringBotTurn()
    {
        BeginBotTurn(); Call(controller, "Show_Menu");
        string before = JsonUtility.ToJson(Get(Snapshot(), "controller"));
        Call(controller, "Finalize_turn"); Call(controller, "ProcessMenuOption", "Wait");
        Call(controller, "ProcessSpawnOption", "Soldier");
        Assert.That(Call(controller, "SelectWorldAt", Vector2.zero), Is.False);
        yield return new WaitForSecondsRealtime(.25f);
        Assert.That(JsonUtility.ToJson(Get(Snapshot(), "controller")), Is.EqualTo(before));
        Call(controller, "Hide_Menu");
        float deadline = Time.realtimeSinceStartup + 20;
        while (State().Turn == 1 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(State().Turn, Is.EqualTo(2));
    }
    [UnityTest]
    public IEnumerator SaveRestoresBotControlAndVersionTwoLoadsAllTeamsAsHuman()
    {
        Set(Teams[1], "isBot", true);
        var saved = Snapshot(); Assert.That(Get(saved, "schemaVersion"), Is.EqualTo(3));
        Set(Teams[1], "isBot", false);
        Assert.That(Load(saved, out string error), Is.True, error);
        Assert.That(Get(Teams[1], "isBot"), Is.True);
        Set(saved, "schemaVersion", 2);
        Assert.That(Load(saved, out error), Is.True, error);
        foreach (var team in Teams) Assert.That(Get(team, "isBot"), Is.False);
        yield return null;
    }
    [UnityTest]
    public IEnumerator LoadingHumanTurnDiscardsAnInProgressBotPlan()
    {
        var humanTurn = Snapshot(); Set(Runner, "planningStepsPerFrame", 1); BeginBotTurn();
        float deadline = Time.realtimeSinceStartup + 10;
        while (Get(Runner, "search") == null && State().Turn == 1 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(Get(Runner, "search"), Is.Not.Null);
        Assert.That(Load(humanTurn, out string error), Is.True, error);
        string restored = JsonUtility.ToJson(Get(Snapshot(), "controller"));
        yield return new WaitForSecondsRealtime(.25f);
        Assert.That(JsonUtility.ToJson(Get(Snapshot(), "controller")), Is.EqualTo(restored));
        Assert.That(Get(Runner, "search"), Is.Null);
    }
    [UnityTest]
    public IEnumerator PortraitNoticePausesPlanningAndResumesInLandscape()
    {
        var orientation = (Behaviour)UnityEngine.Object.FindFirstObjectByType(Script("LandscapeOrientation"));
        Assert.That(orientation, Is.Not.Null);
        orientation.enabled = false;
        Call(orientation, "ApplyViewportSize", 400, 800);
        BeginBotTurn(); string before = JsonUtility.ToJson(Get(Snapshot(), "controller"));
        yield return new WaitForSecondsRealtime(.25f);
        Assert.That(JsonUtility.ToJson(Get(Snapshot(), "controller")), Is.EqualTo(before));
        Call(orientation, "ApplyViewportSize", 1600, 900); orientation.enabled = true;
        float deadline = Time.realtimeSinceStartup + 20;
        while (State().Turn == 1 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(State().Turn, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator RealRacesAndGeneratedMapCompleteAnIsolatedFourPlayerBotMatch()
    {
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        var menu = UnityEngine.Object.FindFirstObjectByType(Script("MainMenu"));
        var races = (IList)Script("MainMenu").GetField("classes").GetValue(null);
        for (int p = 0; p < 4; p++) Script("MainMenu").GetMethod("SelectRace").Invoke(null, new[] { (object)p, races[p % races.Count] });
        Call(menu, "Add_player", 3); Call(menu, "Add_player", 4);
        Script("MainMenu").GetField("build_mode").SetValue(null, true);
        Script("MainMenu").GetField("battle_royale_mode").SetValue(null, true);
        var previousRandom = UnityEngine.Random.state;
        UnityEngine.Random.InitState(8675309);
        Call(menu, "PressStart"); yield return null; yield return null;
        UnityEngine.Random.state = previousRandom;
        controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
        var planner = (BotPlanner)Call(controller, "CreateBotPlanner");
        var adapter = Get(controller, "matchAdapter");
        var engine = (MatchEngine)adapter.GetType().GetProperty("Engine").GetValue(adapter);
        var state = State();
        string live = JsonUtility.ToJson(Get(Snapshot(), "controller"));
        int actions = 0;
        while (!state.IsOver && state.Round <= 100 && actions < 5000)
        {
            var decision = planner.Choose(BotObservation.Capture(state));
            Assert.That(decision, Is.Not.Null);
            var result = engine.Execute(state, decision.Action, out var next);
            Assert.That(result.Success, Is.True, result.Error);
            state = next;
            if (++actions % 20 == 0) yield return null;
        }
        TestContext.WriteLine($"Real roster: {actions} actions, {state.Round} rounds, winner {state.Winner}.");
        Assert.That(state.IsOver, Is.True, "Real-content bot match stalled.");
        Assert.That(JsonUtility.ToJson(Get(Snapshot(), "controller")), Is.EqualTo(live), "Planning must leave the displayed match intact.");
    }

    [UnityTest]
    public IEnumerator SimulatorUsesRealContentWithoutChangingTheDisplayedMatch()
    {
        var adapter = Get(controller, "matchAdapter");
        var engine = (MatchEngine)adapter.GetType().GetProperty("Engine").GetValue(adapter);
        var state = State(); string live = JsonUtility.ToJson(Get(Snapshot(), "controller"));
        var expected = state.Clone(); var planner = (BotPlanner)Call(controller, "CreateBotPlanner");
        for (int i = 0; i < 12 && !expected.IsOver; i++)
        {
            var decision = planner.Choose(BotObservation.Capture(expected));
            Assert.That(engine.Execute(expected, decision.Action, out var next).Success, Is.True); expected = next;
        }
        using (var simulation = new BotSimulation("real-content", state, engine, limits: new SimulationLimits { maxActions = 12 }))
        {
            int steps = 0;
            while (!simulation.IsFinished) { simulation.Advance(); if (++steps % 100 == 0) yield return null; }
            Assert.That(simulation.Result.finalHash, Is.EqualTo(SimulationSnapshot.Fingerprint(expected)));
            Assert.That(simulation.Result.rejectedActions, Is.Zero);
            Assert.That(JsonUtility.ToJson(Get(Snapshot(), "controller")), Is.EqualTo(live));
        }
    }
}
