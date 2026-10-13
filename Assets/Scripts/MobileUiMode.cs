using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MobileUiMode
{
    private static bool? modeOverride;
    private static float densityOverride;
    private static bool ignoreTouchSequence;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() { modeOverride = null; densityOverride = 0; ignoreTouchSequence = false; }
    public static bool Enabled => modeOverride ?? (Application.isMobilePlatform || Input.touchSupported);
    public static float Density => densityOverride > 0 ? densityOverride : Screen.dpi >= 100 && Screen.dpi <= 1000 ? Screen.dpi / 160 : 1;
    public static bool UIInputBlocked => LandscapeOrientation.InputBlocked || ignoreTouchSequence;
    public static void SetOverride(bool? enabled, float density = 0) { modeOverride = enabled; densityOverride = density; ignoreTouchSequence = false; }
    public static void UpdateTouchGate(int count, bool blocked)
    {
        if (count == 0) ignoreTouchSequence = false;
        else if (blocked) ignoreTouchSequence = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= Install; SceneManager.sceneLoaded += Install;
    }
    private static void Install(Scene scene, LoadSceneMode mode)
    {
        foreach (var root in scene.GetRootGameObjects()) foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            if (canvas.isRootCanvas && canvas.renderMode != RenderMode.WorldSpace && canvas.GetComponent<CanvasScaler>() != null
                && canvas.sortingOrder < 1000 && canvas.GetComponent<MobileUiRoot>() == null) canvas.gameObject.AddComponent<MobileUiRoot>();
    }
}

