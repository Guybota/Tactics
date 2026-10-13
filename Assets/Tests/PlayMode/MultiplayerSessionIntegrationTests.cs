using System;
using System.Collections;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class MultiplayerSessionIntegrationTests
{
    private Component controller;
    private bool originalBackground;
    private static Type ControllerType => Type.GetType("scr_game_contoller, Assembly-CSharp", true);
    private MatchState Snapshot() => (MatchState)ControllerType.GetMethod("GetStateSnapshot").Invoke(controller, null);
    private int Revision => (int)ControllerType.GetProperty("MatchRevision").GetValue(controller);
    private bool Submit(MatchAction action)
    {
        object[] arguments = { action, null };
        return (bool)ControllerType.GetMethod("TryExecuteAction").Invoke(controller, arguments);
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        originalBackground = Application.runInBackground;
        Application.runInBackground = true;
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        var menuType = Type.GetType("MainMenu, Assembly-CSharp", true);
        menuType.GetMethod("PressStart").Invoke(UnityEngine.Object.FindFirstObjectByType(menuType), null);
        yield return null; yield return null;
        controller = (Component)UnityEngine.Object.FindFirstObjectByType(ControllerType);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        Application.runInBackground = originalBackground;
    }

    [UnityTest]
    public IEnumerator ConfirmedActionAdvancesOneRevisionAndRejectionPreservesRandomStream()
    {
        int previous = Revision;
        var before = Snapshot();
        Assert.That(Submit(MatchAction.EndTurn(before.ActivePlayer)), Is.True);
        Assert.That(Revision, Is.EqualTo(previous + 1));
        var confirmed = Snapshot();
        Assert.That(confirmed.Turn, Is.GreaterThan(before.Turn));
        Assert.That(Submit(MatchAction.EndTurn(before.ActivePlayer)), Is.False);
        Assert.That(Revision, Is.EqualTo(previous + 1));
        Assert.That(Snapshot().RandomState, Is.EqualTo(confirmed.RandomState));
        yield return null;
    }

    [UnityTest]
    public IEnumerator LoadingReplacesSessionWithoutRepeatingOpeningIncome()
    {
        var before = Snapshot();
        var saveType = Type.GetType("GameSave, Assembly-CSharp", true);
        var save = Activator.CreateInstance(saveType, new object[] { controller, "SessionRestore" });
        Assert.That(Submit(MatchAction.EndTurn(0)), Is.True);
        Assert.That(Submit(MatchAction.EndTurn(1)), Is.True);
        Assert.That(Snapshot().Players[0].Money, Is.GreaterThan(before.Players[0].Money));
        int previous = Revision;
        object[] arguments = { save, null };
        Assert.That((bool)ControllerType.GetMethod("TryLoadSnapshot").Invoke(controller, arguments), Is.True, arguments[1] as string);
        var restored = Snapshot();
        Assert.That(Revision, Is.EqualTo(previous + 1));
        Assert.That(restored.Turn, Is.EqualTo(before.Turn));
        Assert.That(restored.RandomState, Is.EqualTo(before.RandomState));
        Assert.That(restored.Players[0].Money, Is.EqualTo(before.Players[0].Money));
        Assert.That(Submit(MatchAction.EndTurn(restored.ActivePlayer)), Is.True);
        Assert.That(Revision, Is.EqualTo(previous + 2));
        yield return null;
    }
}
