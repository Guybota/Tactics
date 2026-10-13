using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

// Assembly-CSharp stays outside this test assembly; exercise its public UI through reflection.
public sealed class OnlineMenuIntegrationTests
{
    private Component mainMenu;
    private object onlineMenu;
    private bool previousBackground;
    private static Type Script(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Field(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    private static object Property(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
    private static object Call(object target, string name, params object[] args) => target.GetType()
        .GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
    private static object ServicesState() => Type.GetType("Unity.Services.Core.UnityServices, Unity.Services.Core", true)
        .GetProperty("State", BindingFlags.Public | BindingFlags.Static).GetValue(null);

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        previousBackground = Application.runInBackground;
        Application.runInBackground = true;
        SceneManager.LoadScene("Main Menu");
        yield return null; yield return null;
        mainMenu = (Component)UnityEngine.Object.FindFirstObjectByType(Script("MainMenu"));
        onlineMenu = Property(mainMenu, "OnlineMenu");
        Assert.That(onlineMenu, Is.Not.Null);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.LoadScene("Main Menu");
        yield return null; yield return null;
        Application.runInBackground = previousBackground;
    }

    [UnityTest]
    public IEnumerator OnlineEntryHasNoClonedPlayListenersAndOpensWithoutConnecting()
    {
        Assert.That(Property(onlineMenu, "IsVisible"), Is.False);
        Assert.That(Property(onlineMenu, "IsBusy"), Is.False);
        var firstMenu = (GameObject)Field(mainMenu, "firstMenu");
        Button entry = null;
        foreach (var button in firstMenu.GetComponentsInChildren<Button>(true))
            if (button.name == "Online multiplayer button") entry = button;
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry.onClick.GetPersistentEventCount(), Is.Zero,
            "Cloning Play must not retain its persistent scene/menu actions.");
        Assert.That(entry.transform.parent.GetComponent<VerticalLayoutGroup>(), Is.Not.Null,
            "The Online entry must participate in the existing vertical menu layout.");
        object before = ServicesState();
        entry.onClick.Invoke();
        yield return null;
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Main Menu"));
        Assert.That(Property(onlineMenu, "IsVisible"), Is.True);
        Assert.That(Property(onlineMenu, "IsBusy"), Is.False);
        Assert.That(Property(onlineMenu, "CurrentLobby"), Is.Null);
        Assert.That(ServicesState(), Is.EqualTo(before), "Opening the menu must not initialize UGS.");
        Assert.That(firstMenu.activeSelf, Is.False);
        Assert.That(((GameObject)Field(mainMenu, "mapMenu")).activeSelf, Is.False);
    }

    [UnityTest]
    public IEnumerator InvalidInvitationFailsLocallyAndBackClosesTheModal()
    {
        Call(mainMenu, "PressOnline");
        var input = (TMP_InputField)Field(onlineMenu, "invitation");
        input.text = "not-an-invitation";
        object before = ServicesState();
        var operation = (Task)Call(onlineMenu, "JoinAsync");
        Assert.That(operation.IsCompleted, Is.True, "Invalid invitation syntax must fail before starting a cloud request.");
        Assert.That(operation.IsFaulted, Is.False);
        Assert.That(Property(onlineMenu, "IsBusy"), Is.False);
        Assert.That(Property(onlineMenu, "CurrentLobby"), Is.Null);
        Assert.That(((TMP_Text)Field(onlineMenu, "status")).text, Does.Contain("32-character"));
        Assert.That(ServicesState(), Is.EqualTo(before));
        Assert.That((bool)Script("UiBackAction").GetMethod("TryCloseTop").Invoke(null, null), Is.True);
        yield return null;
        Assert.That(Property(onlineMenu, "IsVisible"), Is.False);
        Assert.That(((GameObject)Field(mainMenu, "firstMenu")).activeSelf, Is.True);
        Assert.That(ServicesState(), Is.EqualTo(before), "Back must not initiate authentication or a lobby poll.");
    }

    [UnityTest]
    public IEnumerator ModalFitsSmallAndLargeCanvasReferencesAndKeepsNativeControls()
    {
        Call(mainMenu, "PressOnline");
        var overlay = (RectTransform)Field(onlineMenu, "overlay");
        var panel = (RectTransform)Field(onlineMenu, "panel");
        var canvas = overlay.GetComponentInParent<Canvas>().rootCanvas;
        var scaler = canvas.GetComponent<CanvasScaler>();
        Vector2 originalReference = scaler.referenceResolution;
        float originalMatch = scaler.matchWidthOrHeight;
        try
        {
            foreach (var reference in new[] { new Vector2(360, 640), new Vector2(1920, 1080), new Vector2(1280, 720) })
            {
                scaler.referenceResolution = reference;
                scaler.matchWidthOrHeight = 1;
                yield return null;
                Canvas.ForceUpdateCanvases();
                Call(onlineMenu, "FitPanel");
                var root = (RectTransform)canvas.transform;
                var corners = new Vector3[4];
                panel.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var local = root.InverseTransformPoint(corner);
                    Assert.That(local.x, Is.InRange(root.rect.xMin - 1, root.rect.xMax + 1));
                    Assert.That(local.y, Is.InRange(root.rect.yMin - 1, root.rect.yMax + 1));
                }
                Assert.That(panel.localScale.x, Is.EqualTo(panel.localScale.y).Within(.001f));
            }
            Assert.That(((TMP_InputField)Field(onlineMenu, "invitation")).characterLimit, Is.EqualTo(64));
            Assert.That(((Button)Field(onlineMenu, "create")).interactable, Is.True);
            Assert.That(((Button)Field(onlineMenu, "join")).interactable, Is.True);
            Assert.That(((Button)Field(onlineMenu, "resume")).interactable, Is.True);
            Assert.That(Property(onlineMenu, "IsBusy"), Is.False);
        }
        finally
        {
            scaler.referenceResolution = originalReference;
            scaler.matchWidthOrHeight = originalMatch;
        }
    }
}
