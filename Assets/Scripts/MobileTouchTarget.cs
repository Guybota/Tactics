using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Increase the hit region without changing artwork; choose the closest control if margins overlap.
[DisallowMultipleComponent, RequireComponent(typeof(Selectable))]
public sealed class MobileTouchTarget : MonoBehaviour, ICanvasRaycastFilter
{
    private static readonly HashSet<MobileTouchTarget> targets = new HashSet<MobileTouchTarget>();
    private Selectable control;
    [SerializeField] private Image area;
    private RectTransform rect;
    private Canvas canvas;
    private void Awake()
    {
        control = GetComponent<Selectable>(); rect = (RectTransform)transform; canvas = GetComponentInParent<Canvas>();
        if (area == null)
        {
            var hit = CustomMapUi.Rect(transform, "Touch hit area", Vector2.zero, Vector2.zero);
            CustomMapUi.Stretch(hit); hit.SetAsFirstSibling(); area = CustomMapUi.Background(hit, Color.clear);
            hit.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }
        area.raycastTarget = true;
    }
    private void OnEnable() => targets.Add(this);
    private void OnDisable() => targets.Remove(this);
    private void LateUpdate() => Refresh();
    public void Refresh()
    {
        area.enabled = MobileUiMode.Enabled;
        if (!area.enabled) return;
        var bounds = ScreenBounds(rect, canvas);
        float minimum = 48 * MobileUiMode.Density;
        float x = rect.rect.width > 0 && bounds.width > 0 ? Mathf.Max(0, minimum - bounds.width) * .5f * rect.rect.width / bounds.width : 0;
        float y = rect.rect.height > 0 && bounds.height > 0 ? Mathf.Max(0, minimum - bounds.height) * .5f * rect.rect.height / bounds.height : 0;
        area.raycastPadding = control.IsInteractable() ? new Vector4(-x, -y, -x, -y) : Vector4.zero;
    }
    public bool IsRaycastLocationValid(Vector2 point, Camera eventCamera)
    {
        if (MobileUiMode.UIInputBlocked) return false;
        if (!MobileUiMode.Enabled || !control.IsInteractable()) return true;
        float distance = DistanceSquared(ScreenBounds(rect, canvas), point);
        if (distance == 0) return true;
        foreach (var other in targets)
        {
            if (other == this || other.canvas != canvas || !other.control.IsInteractable()
                || other.area.canvasRenderer.cull) continue;
            var bounds = ScreenBounds(other.rect, other.canvas);
            float paddingX = Mathf.Max(0, 48 * MobileUiMode.Density - bounds.width) * .5f;
            float paddingY = Mathf.Max(0, 48 * MobileUiMode.Density - bounds.height) * .5f;
            var expanded = Rect.MinMaxRect(bounds.xMin - paddingX, bounds.yMin - paddingY, bounds.xMax + paddingX, bounds.yMax + paddingY);
            if (!expanded.Contains(point) || !InsideMasks(other.transform, point)) continue;
            float otherDistance = DistanceSquared(bounds, point);
            if (otherDistance < distance - .001f || Mathf.Abs(otherDistance - distance) < .001f && other.GetInstanceID() < GetInstanceID()) return false;
        }
        return true;
    }
    private static bool InsideMasks(Transform transform, Vector2 point)
    {
        foreach (var mask in transform.GetComponentsInParent<RectMask2D>())
            if (!RectTransformUtility.RectangleContainsScreenPoint(mask.rectTransform, point, null)) return false;
        return true;
    }
    public static Rect ScreenBounds(RectTransform rect, Canvas canvas)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var camera = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = min;
        foreach (var corner in corners)
        { var p = RectTransformUtility.WorldToScreenPoint(camera, corner); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static float DistanceSquared(Rect rect, Vector2 point)
    {
        float x = Mathf.Max(rect.xMin - point.x, 0, point.x - rect.xMax), y = Mathf.Max(rect.yMin - point.y, 0, point.y - rect.yMax);
        return x * x + y * y;
    }
}
