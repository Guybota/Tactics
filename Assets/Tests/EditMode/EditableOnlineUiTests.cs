using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class EditableOnlineUiTests
{
    private static Type Script(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static Component Find(Scene scene, string type) => scene.GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren(Script(type), true)).Cast<Component>().Single();
    private static UnityEngine.Object Reference(UnityEngine.Object target, string field)
        => new SerializedObject(target).FindProperty(field).objectReferenceValue;
    private static void InScene(string path, Action<Scene> check)
    {
        var previous = SceneManager.GetActiveScene(); var scene = SceneManager.GetSceneByPath(path);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try { check(scene); }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); if (previous.isLoaded) SceneManager.SetActiveScene(previous); }
    }
    [Test]
    public void EntireOnlineMenuAndEntryExistBeforePlayMode()
    {
        Assert.That(Application.isPlaying, Is.False);
        InScene("Assets/Scenes/Main Menu.unity", scene =>
        {
            var menu = Find(scene, "MainMenu"); var online = Find(scene, "OnlineMatchMenu");
            Assert.That(Reference(menu, "onlineMenu"), Is.SameAs(online));
            Assert.That(Reference(menu, "onlineButton"), Is.Not.Null);
            foreach (string field in new[] { "overlay", "panel", "setup", "lobbyView", "heading", "description", "status",
                "invitation", "lobbyInvitation", "viewScroll", "create", "join", "resume", "build", "storm", "copy", "refresh", "open", "another" })
                Assert.That(Reference(online, field), Is.Not.Null, field + " must be editable in the saved scene.");
            var buttons = new SerializedObject(online).FindProperty("raceButtons");
            Assert.That(buttons.arraySize, Is.EqualTo(3));
            for (int i = 0; i < buttons.arraySize; i++) Assert.That(buttons.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null);
            var overlay = (RectTransform)Reference(online, "overlay");
            Assert.That(overlay.gameObject.activeSelf, Is.False);
            foreach (var rect in overlay.GetComponentsInChildren<RectTransform>(true))
                Assert.That(rect.localScale, Is.EqualTo(Vector3.one), rect.name);
            var entry = (Component)Reference(menu, "onlineButton");
            var events = entry.GetType().GetProperty("onClick").GetValue(entry);
            Assert.That(events.GetType().GetMethod("GetPersistentEventCount").Invoke(events, null), Is.EqualTo(1));
            Assert.That(events.GetType().GetMethod("GetPersistentMethodName").Invoke(events, new object[] { 0 }), Is.EqualTo("PressOnline"));
        });
    }
    [Test]
    public void EntireOnlineHudExistsBeforePlayModeAndHasSerializedCallbacks()
    {
        InScene("Assets/Scenes/Main Scene.unity", scene =>
        {
            var controller = Find(scene, "scr_game_contoller"); var hud = Find(scene, "OnlineMatchHud");
            Assert.That(Reference(controller, "onlineHudView"), Is.SameAs(hud));
            Assert.That(hud.gameObject.activeSelf, Is.False);
            Assert.That(Reference(hud, "status"), Is.Not.Null);
            foreach (var pair in new[] { new[] { "refresh", "RefreshFromButton" }, new[] { "retry", "RetryFromButton" }, new[] { "copy", "CopyFromButton" } })
            {
                var button = (Component)Reference(hud, pair[0]); Assert.That(button, Is.Not.Null);
                var events = button.GetType().GetProperty("onClick").GetValue(button);
                Assert.That(events.GetType().GetMethod("GetPersistentMethodName").Invoke(events, new object[] { 0 }), Is.EqualTo(pair[1]));
                Assert.That(events.GetType().GetMethod("GetPersistentTarget").Invoke(events, new object[] { 0 }), Is.SameAs(hud));
            }
        });
    }
}
