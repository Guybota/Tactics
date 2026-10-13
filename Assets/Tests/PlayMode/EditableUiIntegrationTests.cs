using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class EditableUiIntegrationTests
{
    private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static Type Script(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Get(object value, string name) => value.GetType().GetField(name, Fields).GetValue(value);
    private static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Fields).Invoke(value, args);
    private static Component Find(string name) => (Component)UnityEngine.Object.FindFirstObjectByType(Script(name));
    private static CustomMapData Data()
    {
        var map = new CustomMapData { id = Guid.NewGuid().ToString("N"), name = "Editable UI test", width = 10, height = 10 };
        for (int i = 0; i < 100; i++) map.cells.Add(new MapCellData());
        map.starts.Add(new MapStartData { slot = 0, x = 1, y = 1 }); map.starts.Add(new MapStartData { slot = 1, x = 8, y = 8 }); return map;
    }
    [UnitySetUp] public IEnumerator Setup() { SceneManager.LoadScene("Main Menu"); yield return null; yield return null; }
    [UnityTearDown] public IEnumerator Teardown() { SceneManager.LoadScene("Main Menu"); yield return null; yield return null; }
    [UnityTest] public IEnumerator MenuAndDialogsReuseEditedSceneElements()
    {
        var menu = Find("MainMenu"); var select = Find("CustomMapSelect"); var choose = (Component)Get(select, "choose");
        var rect = (RectTransform)choose.transform; rect.anchoredPosition = new Vector2(17, 12); rect.sizeDelta = new Vector2(345, 39);
        var hub = Get(select, "dialogs"); var error = (Component)Get(hub, "error"); var panel = (RectTransform)Get(error, "panel");
        panel.anchoredPosition = new Vector2(21, -13); panel.sizeDelta = new Vector2(690, 620);
        int count = error.GetComponentsInChildren<Transform>(true).Length;
        for (int i = 0; i < 3; i++)
        {
            Call(menu, "PressPlay"); Call(select, "Refresh"); Call(select, "ShowError", "Test error"); yield return null;
            Assert.That(error.gameObject.activeSelf, Is.True); Call(error, "Hide");
            Assert.That(panel.anchoredPosition, Is.EqualTo(new Vector2(21, -13))); Assert.That(panel.sizeDelta, Is.EqualTo(new Vector2(690, 620)));
            Assert.That(error.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(count));
            Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(17, 12))); Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(345, 39)));
        }
    }
    [UnityTest] public IEnumerator LibraryRowsInheritEditedTemplateStyles()
    {
        var select = Find("CustomMapSelect"); var library = Get(Get(select, "dialogs"), "library");
        var template = (Component)Get(library, "rowTemplate"); var caption = (Component)Get(template, "caption");
        caption.GetType().GetProperty("fontSizeMax").SetValue(caption, 18f); caption.GetType().GetProperty("color").SetValue(caption, Color.cyan);
        var editRect = (RectTransform)((Component)Get(template, "edit")).transform; editRect.anchoredPosition = new Vector2(-177, 24);
        var data = Data(); var repository = Activator.CreateInstance(Script("CustomMapRepository"), Application.persistentDataPath); Call(repository, "Write", data);
        try
        {
            Call(Find("MainMenu"), "PressPlay"); Call(select, "OpenLibrary"); yield return null; yield return null;
            var row = GameObject.Find("Map row " + data.id).GetComponent(Script("CustomMapRowView")); var text = (Component)Get(row, "caption");
            Assert.That(text.GetType().GetProperty("fontSizeMax").GetValue(text), Is.EqualTo(18f));
            Assert.That(text.GetType().GetProperty("color").GetValue(text), Is.EqualTo(Color.cyan));
            Assert.That(((RectTransform)((Component)Get(row, "edit")).transform).anchoredPosition, Is.EqualTo(new Vector2(-177, 24)));
            Assert.That(template.gameObject.activeSelf, Is.False);
        }
        finally { Call(repository, "Delete", data.id); }
    }
    [UnityTest] public IEnumerator EditorMarkersCopyAuthoredTemplateAndToolbarKeepsManualPosition()
    {
        Call(Find("MainMenu"), "PressStartCreateMap"); yield return null; yield return null;
        var creator = Find("MapCreator"); var view = Find("MapEditorMenu"); var input = (Component)Get(view, "mapName");
        input.transform.parent.GetComponent<RectTransform>().anchoredPosition = new Vector2(15, -125);
        var template = (Component)Get(creator, "startMarkerTemplate"); template.GetType().GetProperty("fontSize").SetValue(template, 123f);
        ((RectTransform)template.transform).sizeDelta = new Vector2(75, 53); template.transform.localPosition = new Vector3(3, 14, -3);
        var map = Data(); Call(creator, "ApplyMap", map); yield return null;
        var root = (Transform)Get(creator, "markerRoot"); Assert.That(root.childCount, Is.EqualTo(2));
        foreach (Transform marker in root)
        {
            var text = marker.GetComponent(Type.GetType("TMPro.TextMeshPro, Unity.TextMeshPro", true));
            Assert.That(text.GetType().GetProperty("fontSize").GetValue(text), Is.EqualTo(123f));
            Assert.That(((RectTransform)marker).sizeDelta, Is.EqualTo(new Vector2(75, 53)));
        }
        Assert.That(root.GetChild(0).localPosition, Is.EqualTo(new Vector3(67, 78, -3)));
        Assert.That(input.transform.parent.GetComponent<RectTransform>().anchoredPosition, Is.EqualTo(new Vector2(15, -125)));
    }
}
