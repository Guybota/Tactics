using UnityEngine;

/// <summary>Keep the game horizontal, including browser windows that cannot lock orientation.</summary>
[DisallowMultipleComponent]
public sealed class LandscapeOrientation : MonoBehaviour
{
    [SerializeField] private GameObject portraitNotice;
    private Vector2Int lastSize;
    private static LandscapeOrientation current;
    public bool IsBlocked => portraitNotice != null && portraitNotice.activeInHierarchy;
    public static bool InputBlocked => (Screen.width > 0 && Screen.height > 0 && Screen.width <= Screen.height) || (current != null && current.IsBlocked);
    private void OnDestroy() { if (current == this) current = null; }

    private void Awake()
    {
        current = this;
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
        Screen.orientation = ScreenOrientation.AutoRotation;
#endif
        ApplyViewportSize(Screen.width, Screen.height);
    }

    private void Update()
    {
        if (lastSize.x != Screen.width || lastSize.y != Screen.height)
            ApplyViewportSize(Screen.width, Screen.height);
    }

    public void ApplyViewportSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || portraitNotice == null) return;
        lastSize = new Vector2Int(width, height);
        portraitNotice.SetActive(width <= height);
    }
}
