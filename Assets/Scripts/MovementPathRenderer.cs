using System.Collections.Generic;
using UnityEngine;

/// <summary>Tile-sized pixel sprites, pooled from the editable scene template.</summary>
public sealed class MovementPathRenderer : MonoBehaviour
{
    public SpriteRenderer segmentTemplate;
    public Sprite start;
    public Sprite straight;
    public Sprite corner;
    public Sprite tip;

    private readonly List<SpriteRenderer> segments = new List<SpriteRenderer>();

    private void Awake()
    {
        if (segmentTemplate != null) segmentTemplate.gameObject.SetActive(false);
    }

    public void Show(IReadOnlyList<Transform> points)
    {
        Clear();
        if (segmentTemplate == null || points == null || points.Count < 2) return;
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 direction = i == 0
                ? points[1].position - points[0].position
                : points[i].position - points[i - 1].position;
            if (direction.sqrMagnitude == 0 || direction.x != 0 && direction.y != 0)
            {
                Clear();
                return;
            }
            var sprite = i == 0 ? start : i == points.Count - 1 ? tip : straight;
            bool flip = false;
            if (i > 0 && i < points.Count - 1)
            {
                Vector2 next = points[i + 1].position - points[i].position;
                float turn = direction.x * next.y - direction.y * next.x;
                if (turn != 0)
                {
                    sprite = corner;
                    flip = turn < 0;
                }
            }
            if (i == segments.Count)
            {
                var segment = Instantiate(segmentTemplate, transform);
                segment.name = "Path segment " + i;
                segments.Add(segment);
            }
            var renderer = segments[i];
            renderer.sprite = sprite;
            renderer.flipY = flip;
            renderer.flipX = false;
            renderer.transform.SetPositionAndRotation(points[i].position,
                Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg));
            renderer.gameObject.SetActive(true);
        }
    }

    public void Clear()
    {
        foreach (var segment in segments)
            if (segment != null) segment.gameObject.SetActive(false);
    }

    private void OnDisable() { Clear(); }
}
