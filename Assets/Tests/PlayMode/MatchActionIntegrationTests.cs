using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class MatchActionIntegrationTests
{
    private Component controller;
    private bool originalBackground;
    private static Type Script(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Get(object target, string field) => target.GetType().GetField(field).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field).SetValue(target, value);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method).Invoke(target, args);
    private IList Teams => (IList)Get(controller, "teams");
    private object Map => Get(controller, "gameMap");
    private object King(int player) => Call(Teams[player], "Get_king");
    private object Snapshot() => Activator.CreateInstance(Script("GameSave"), new object[] { controller, "ActionRegression" });
    private MatchState State() => (MatchState)Call(controller, "GetStateSnapshot");
    private object Tile(int x, int y) => ((Array)Get(Map, "tiles")).GetValue(x, y);
    private bool Submit(MatchAction action, out string error)
    {
        object[] arguments = { action, null };
        bool success = (bool)Call(controller, "TryExecuteAction", arguments);
        error = (string)arguments[1]; return success;
    }
    private bool Load(object snapshot, out string error)
    {
        object[] arguments = { snapshot, null };
        bool success = (bool)Call(controller, "TryLoadSnapshot", arguments);
        error = (string)arguments[1]; return success;
    }
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        originalBackground = Application.runInBackground; Application.runInBackground = true;
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        Call(UnityEngine.Object.FindFirstObjectByType(Script("MainMenu")), "PressStart");
        yield return null; yield return null;
        controller = (Component)UnityEngine.Object.FindFirstObjectByType(Script("scr_game_contoller"));
    }
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        Application.runInBackground = originalBackground;
    }
    [UnityTest]
    public IEnumerator PlanningSnapshotCannotMutateLiveUnitsMoneyOrTerrain()
    {
        string before = JsonUtility.ToJson(Get(Snapshot(), "controller"));
        var state = State(); state.Players[0].Money = 0; state.Players[0].Units[0].Health = 0;
        state.Cells[5, 5].HasStorm = !state.Cells[5, 5].HasStorm;
        state.Players.Clear();
        Assert.That(JsonUtility.ToJson(Get(Snapshot(), "controller")), Is.EqualTo(before));
        yield return null;
    }
    [UnityTest]
    public IEnumerator RejectedCommonActionPreservesMatchAndCurrentSelection()
    {
        Call(Map, "Select_tile", Get(King(0), "current_tile"), Teams[0]);
        var selected = Get(Map, "selected_tile");
        string before = JsonUtility.ToJson(Get(Snapshot(), "controller"));
        var enemy = State().Players[1].Units[0];
        string error;
        Assert.That(Submit(new MatchAction(MatchActionKind.Wait, new UnitKey(1, enemy.Id), enemy.Position), out error), Is.False);
        Assert.That(error, Is.Not.Empty);
        Assert.That(JsonUtility.ToJson(Get(Snapshot(), "controller")), Is.EqualTo(before));
        Assert.That(Get(Map, "selected_tile"), Is.SameAs(selected));
        yield return null;
    }
    [UnityTest]
    public IEnumerator VersionTwoSaveUsesStableContentIdsEvenWhenLegacyLabelsChange()
    {
        var saved = Snapshot();
        Set(saved, "schemaVersion", 2);
        Assert.That(Get(saved, "schemaVersion"), Is.EqualTo(2));
        foreach (object team in (IList)Get(Get(saved, "controller"), "teams"))
        {
            Set(team, "raceClass", "Old race display label");
            foreach (object unit in (IList)Get(team, "units")) Set(unit, "unitName", "Old unit display label");
        }
        string error;
        Assert.That(Load(saved, out error), Is.True, error);
        Assert.That(Get(controller, "status"), Is.EqualTo("Select_tile"));
        Assert.That(King(0), Is.Not.Null); Assert.That(King(1), Is.Not.Null);
        yield return null;
    }
    [UnityTest]
    public IEnumerator BothLegacySaveVersionsLoadWithoutNewIdsOrRandomMetadata()
    {
        for (int version = 0; version <= 1; version++)
        {
            var saved = Snapshot(); Set(saved, "schemaVersion", version);
            var state = Get(saved, "controller"); Set(state, "randomState", (uint)0);
            foreach (object team in (IList)Get(state, "teams"))
            {
                Set(team, "raceId", null); Set(team, "nextUnitId", 0);
                foreach (object unit in (IList)Get(team, "units")) Set(unit, "unitTypeId", null);
            }
            string error;
            Assert.That(Load(saved, out error), Is.True, error);
            yield return null;
            Assert.That(State().Players.Count, Is.EqualTo(2));
            Assert.That(Get(controller, "status"), Is.EqualTo("Select_tile"));
        }
    }
    [UnityTest]
    public IEnumerator InvalidStableIdNeverReplacesTheLiveMap()
    {
        var original = Get(Map, "tiles"); var saved = Snapshot();
        var first = ((IList)Get(Get(saved, "controller"), "teams"))[0]; Set(first, "raceId", "missing.race");
        string error;
        Assert.That(Load(saved, out error), Is.False); Assert.That(error, Is.Not.Empty);
        Assert.That(Get(Map, "tiles"), Is.SameAs(original));
        yield return null;
    }
    private object PrepareCombatSave()
    {
        var saved = Snapshot(); var state = Get(saved, "controller");
        foreach (object tile in (IList)Get(Get(state, "gameMap"), "serializedTiles"))
        {
            int x = (int)Get(tile, "position_x"), y = (int)Get(tile, "position_y");
            if (y != 2 || x < 2 || x > 4) continue;
            Set(tile, "tileName", "Grass field"); Set(tile, "hasCastle", false); Set(tile, "castleTeam", ""); Set(tile, "hasStorm", false);
        }
        for (int player = 0; player < 2; player++)
        {
            var king = ((IList)Get(((IList)Get(state, "teams"))[player], "units"))[0];
            Set(king, "positionX", player == 0 ? 2 : 4); Set(king, "positionY", 2);
        }
        return JsonUtility.FromJson(JsonUtility.ToJson(saved), Script("GameSave"));
    }
    [UnityTest]
    public IEnumerator ConfirmedAttackPlaysSoundButRejectedAttackAndLoadingStaySilent()
    {
        var audio = UnityEngine.Object.FindFirstObjectByType(Script("AudioManager"));
        var source = (AudioSource)audio.GetType().GetField("SFXSource", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(audio);
        var originalClip = (AudioClip)Get(audio, "attack");
        var testClip = AudioClip.Create("Action sound regression", 88200, 1, 44100, false);
        try
        {
            Set(audio, "attack", testClip); source.Stop();
            Assert.That(Load(PrepareCombatSave(), out string error), Is.True, error);
            Assert.That(source.isPlaying, Is.False, "Restoring state must not replay action sounds.");
            var action = new MatchAction(MatchActionKind.Attack, new UnitKey(0, 1), new GridPosition(3, 2), new UnitKey(1, 1));
            Assert.That(Submit(action, out error), Is.True, error);
            Assert.That(source.isPlaying, Is.True, "A confirmed attack must play its effect.");
            source.Stop();
            Assert.That(Submit(action, out error), Is.False);
            Assert.That(source.isPlaying, Is.False, "Rejected attacks must not play effects.");
            yield return null;
        }
        finally
        {
            source.Stop(); Set(audio, "attack", originalClip); UnityEngine.Object.Destroy(testClip);
        }
    }

    private bool CombatAnimating => (bool)controller.GetType().GetProperty("IsCombatAnimating").GetValue(controller);
    private Component Presentation => controller.GetComponent(Script("CombatPresentation"));

    [UnityTest]
    public IEnumerator AttackAnimatesBothLayersAndCounterBeforeUnlockingTheMatch()
    {
        Assert.That(Load(PrepareCombatSave(), out string error), Is.True, error);
        yield return null;
        var actor = (Component)King(0);
        var body = (SpriteRenderer)Get(actor, "sprite");
        var tint = (SpriteRenderer)Get(actor, "color");
        var teamColor = tint.color;
        Assert.That(Submit(new MatchAction(MatchActionKind.Attack, new UnitKey(0, 1), new GridPosition(3, 2), new UnitKey(1, 1)), out error), Is.True, error);
        Assert.That(CombatAnimating, Is.True);
        Assert.That(body.forceRenderingOff && tint.forceRenderingOff, Is.True);
        Assert.That(State().FindUnit(new UnitKey(0, 1)).Position, Is.EqualTo(new GridPosition(3, 2)), "Animation must not delay confirmed state.");
        Assert.That(Submit(MatchAction.EndTurn(0), out error), Is.False);
        var visual = Presentation.transform.Find("Attack presentation");
        var renderers = visual.GetComponentsInChildren<SpriteRenderer>();
        var healthCanvas = ((GameObject)Get(actor, "health_bar")).GetComponentInChildren<Canvas>();
        bool changed = false, counter = false, hurt = false;
        var first = renderers[0].sprite;
        float deadline = Time.realtimeSinceStartup + 5;
        while (CombatAnimating && Time.realtimeSinceStartup < deadline)
        {
            counter |= (bool)Presentation.GetType().GetProperty("IsCounterAttacking").GetValue(Presentation);
            hurt |= Presentation.GetComponentsInChildren<Animator>().Any(a => a.GetCurrentAnimatorStateInfo(0).IsName("Hurt"));
            changed |= renderers[0].sprite != first;
            Assert.That(renderers[1].sprite.rect, Is.EqualTo(renderers[0].sprite.rect));
            Assert.That(healthCanvas.sortingOrder, Is.GreaterThan(renderers.Max(r => r.sortingOrder)),
                "Clearing selection must not put the heart underneath a combat copy.");
            Assert.That(renderers.Single(r => r.name == tint.name).color, Is.EqualTo(teamColor));
            yield return null;
        }
        Assert.That(CombatAnimating, Is.False);
        Assert.That(changed, Is.True, "The visible sprite must advance through the attack clip.");
        Assert.That(counter, Is.True);
        Assert.That(hurt, Is.True);
        Assert.That(body.forceRenderingOff || tint.forceRenderingOff, Is.False);
    }

    [UnityTest]
    public IEnumerator LethalAttackKeepsVisualUntilImpactAndNeverCounterAttacks()
    {
        var saved = PrepareCombatSave();
        var target = ((IList)Get(((IList)Get(Get(saved, "controller"), "teams"))[1], "units"))[0];
        Set(target, "health", .01f);
        Assert.That(Load(saved, out string error), Is.True, error);
        yield return null;
        Assert.That(Submit(new MatchAction(MatchActionKind.Attack, new UnitKey(0, 1), new GridPosition(3, 2), new UnitKey(1, 1)), out error), Is.True, error);
        Assert.That(State().FindUnit(new UnitKey(1, 1)).IsAlive, Is.False);
        Assert.That(Presentation.transform.Find("Defender presentation").gameObject.activeSelf, Is.True);
        bool death = false;
        float deadline = Time.realtimeSinceStartup + 5;
        while (CombatAnimating && Time.realtimeSinceStartup < deadline)
        {
            Assert.That(Presentation.GetType().GetProperty("IsCounterAttacking").GetValue(Presentation), Is.False);
            death |= Presentation.GetComponentsInChildren<Animator>().Any(a => a.GetCurrentAnimatorStateInfo(0).IsName("Death"));
            yield return null;
        }
        Assert.That(CombatAnimating, Is.False);
        Assert.That(death, Is.True, "A confirmed kill must play the death sheet before removing its visual.");
        yield return null;
        Assert.That(Presentation.transform.Find("Defender presentation"), Is.Null);
    }

    [UnityTest]
    public IEnumerator LoadingDuringAttackCleansVisualsAndRestoresInteraction()
    {
        var saved = PrepareCombatSave();
        Assert.That(Load(saved, out string error), Is.True, error);
        yield return null;
        Assert.That(Submit(new MatchAction(MatchActionKind.Attack, new UnitKey(0, 1), new GridPosition(3, 2), new UnitKey(1, 1)), out error), Is.True, error);
        Assert.That(CombatAnimating, Is.True);
        Assert.That(Load(saved, out error), Is.True, error);
        Assert.That(CombatAnimating, Is.False);
        yield return null;
        Assert.That(Presentation.transform.Find("Attack presentation"), Is.Null);
        Assert.That(((SpriteRenderer)Get(King(0), "sprite")).forceRenderingOff, Is.False);
        Assert.That(Submit(new MatchAction(MatchActionKind.Attack, new UnitKey(0, 1), new GridPosition(3, 2), new UnitKey(1, 1)), out error), Is.True, error);
    }

    [UnityTest]
    public IEnumerator AttackAnimationPausesWithTheMenuAndResumesWithoutChangingState()
    {
        Assert.That(Load(PrepareCombatSave(), out string error), Is.True, error);
        yield return null;
        Assert.That(Submit(new MatchAction(MatchActionKind.Attack, new UnitKey(0, 1), new GridPosition(3, 2), new UnitKey(1, 1)), out error), Is.True, error);
        Set(controller, "status", "Menu");
        var visual = Presentation.transform.Find("Attack presentation");
        var position = visual.position;
        string confirmed = JsonUtility.ToJson(Get(Snapshot(), "controller"));
        yield return new WaitForSecondsRealtime(.25f);
        Assert.That(CombatAnimating, Is.True);
        Assert.That(visual.position, Is.EqualTo(position));
        Set(controller, "status", "Select_tile");
        Assert.That(JsonUtility.ToJson(Get(Snapshot(), "controller")), Is.EqualTo(confirmed));
        float deadline = Time.realtimeSinceStartup + 5;
        while (CombatAnimating && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(CombatAnimating, Is.False);
    }

    [UnityTest]
    public IEnumerator SaveLoadRestoresLuckSoRepeatedActionHasExactlyTheSameOutcome()
    {
        var saved = PrepareCombatSave(); string error;
        Assert.That(Load(saved, out error), Is.True, error); yield return null;
        var action = new MatchAction(MatchActionKind.Attack, new UnitKey(0, 1), new GridPosition(3, 2), new UnitKey(1, 1));
        Assert.That(Submit(action, out error), Is.True, error);
        string expected = JsonUtility.ToJson(Get(Snapshot(), "controller"));
        Assert.That(Load(saved, out error), Is.True, error); yield return null;
        Assert.That(Submit(action, out error), Is.True, error);
        Assert.That(JsonUtility.ToJson(Get(Snapshot(), "controller")), Is.EqualTo(expected));
    }
    [UnityTest]
    public IEnumerator SaveLoadDoesNotReuseIdOfADeadHighestNumberedRecruit()
    {
        var saved = PrepareCombatSave(); string error;
        Assert.That(Load(saved, out error), Is.True, error); yield return null;
        // Move the opponent away from the recruitment fixture.
        Call(King(1), "Change_current_tile", Tile(8, 8));
        Call(Tile(3, 2), "Activate_castle");
        Call(((GameObject)Get(Tile(3, 2), "castle")).GetComponent(Script("Castle")), "Capture_castle", Teams[0]);
        Set(Teams[0], "money", 10000);
        var race = Get(Teams[0], "raceClass"); var prefab = (GameObject)((IList)Get(race, "AvailableUnits"))[0];
        var recruit = UnityEngine.Object.Instantiate(prefab, ((Component)Map).transform).GetComponent(Script("Unit"));
        Set(recruit, "id", 99); Call(recruit, "Change_current_tile", Tile(4, 2)); Call(Teams[0], "Add", recruit);
        Call(recruit, "Take_damage", 10000f);
        Assert.That(Load(Snapshot(), out error), Is.True, error); yield return null;
        var before = State();
        Assert.That(before.Players[0].NextUnitId, Is.EqualTo(100));
        Assert.That(Submit(MatchAction.Recruit(0, new GridPosition(3, 2), before.Players[0].Units[0].TypeId), out error), Is.False, "Leaders are not recruitable.");
        var prefabUnit = prefab.GetComponent(Script("Unit"));
        var profile = prefabUnit.GetType().GetProperty("CombatDefinition").GetValue(prefabUnit);
        string unitType = (string)profile.GetType().GetProperty("Id").GetValue(profile);
        Assert.That(Submit(MatchAction.Recruit(0, new GridPosition(3, 2), unitType), out error), Is.True, error);
        Assert.That(State().FindUnit(new UnitKey(0, 100)), Is.Not.Null);
    }
    [UnityTest]
    public IEnumerator SaveLoadPreservesStormCadence()
    {
        Map.GetType().GetProperty("stormWarmupRounds").SetValue(Map, 3);
        Map.GetType().GetProperty("stormRoundsPerShrink").SetValue(Map, 2);
        var saved = Snapshot();
        Map.GetType().GetProperty("stormWarmupRounds").SetValue(Map, 1);
        Map.GetType().GetProperty("stormRoundsPerShrink").SetValue(Map, 1);
        string error; Assert.That(Load(saved, out error), Is.True, error);
        Assert.That(State().StormWarmupRounds, Is.EqualTo(3)); Assert.That(State().StormRoundsPerShrink, Is.EqualTo(2));
        yield return null;
    }
}
