using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// UI raycasts use the click position rather than a cached hover from the previous frame.
public static class WorldPointer
{
    public static bool IsOverUI(Vector2 screenPosition)
    {
        if (LandscapeOrientation.InputBlocked) return true;
        EventSystem events = EventSystem.current;
        if (events == null) return false;
        var pointer = new PointerEventData(events) { position = screenPosition };
        var hits = new List<RaycastResult>();
        events.RaycastAll(pointer, hits);
        return hits.Count > 0;
    }

    public static bool TryGetTile(Vector2 screenPosition, out Tile tile)
    {
        tile = null;
        if (IsOverUI(screenPosition) || Camera.main == null) return false;
        Ray ray = Camera.main.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return false;
        tile = hit.collider.GetComponent<Tile>();
        return tile != null;
    }
}
