using UnityEngine;

/// <summary>Keep screen UI clear of mobile cutouts and system insets.</summary>
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
public sealed class UiSafeArea : MonoBehaviour
{
    private Rect lastArea;
    private Vector2Int lastSize;
    private void OnEnable() => Refresh();
    private void Update()
    {
        if (lastArea != Screen.safeArea || lastSize.x != Screen.width || lastSize.y != Screen.height) Refresh();
    }
    public void Refresh()
    {
        var rect = (RectTransform)transform;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null || canvas.rootCanvas.renderMode == RenderMode.WorldSpace) return;
        if (Screen.width <= 0 || Screen.height <= 0) return;
        lastArea = Screen.safeArea;
        lastSize = new Vector2Int(Screen.width, Screen.height);
        rect.anchorMin = new Vector2(lastArea.xMin / Screen.width, lastArea.yMin / Screen.height);
        rect.anchorMax = new Vector2(lastArea.xMax / Screen.width, lastArea.yMax / Screen.height);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
