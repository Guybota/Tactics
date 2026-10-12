using UnityEngine;

/// <summary>Bounds a stretched UI panel within its parent while preserving designer-set anchors.</summary>
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
public sealed class ResponsivePanel : MonoBehaviour, UnityEngine.UI.ILayoutSelfController
{
    [Min(0)] public Vector2 maxSize = new Vector2(600, 700);
    [Min(0)] public float padding = 16;
    public bool fitContentHeight;
    public Vector2 alignment = new Vector2(.5f, .5f);
    private bool applying;
    public void SetLayoutHorizontal() => ApplyLayout();
    public void SetLayoutVertical() => ApplyLayout();
    private void OnEnable() => ApplyLayout();
    private void OnRectTransformDimensionsChange() => ApplyLayout();
    private void OnValidate() => ApplyLayout();
    public void ApplyLayout()
    {
        if (applying) return;
        var rect = (RectTransform)transform;
        var parent = rect.parent as RectTransform;
        if (parent == null) return;
        applying = true;
        var available = Vector2.Scale(parent.rect.size, rect.anchorMax - rect.anchorMin);
        var margin = Vector2.Min(Vector2.one * Mathf.Max(0, padding), Vector2.Max(Vector2.zero, available) * .5f);
        var size = new Vector2(
            Mathf.Max(0, Mathf.Min(available.x - 2 * margin.x, maxSize.x > 0 ? maxSize.x : available.x)),
            Mathf.Max(0, Mathf.Min(available.y - 2 * margin.y, maxSize.y > 0 ? maxSize.y : available.y)));
        if (fitContentHeight)
        {
            float preferred = UnityEngine.UI.LayoutUtility.GetPreferredHeight(rect);
            if (preferred > 0) size.y = Mathf.Min(size.y, preferred);
        }
        var remaining = Vector2.Max(Vector2.zero, available - size - margin * 2);
        var align = new Vector2(Mathf.Clamp01(alignment.x), Mathf.Clamp01(alignment.y));
        rect.offsetMin = margin + Vector2.Scale(remaining, align);
        rect.offsetMax = -margin - Vector2.Scale(remaining, Vector2.one - align);
        applying = false;
    }
}
