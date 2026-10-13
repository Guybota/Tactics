using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Preserve ScrollRect dragging, touch inertia and scrollbar behaviour; smooth wheel input only.
public sealed class SmoothMenuScroll : ScrollRect
{
    private bool wheelActive;
    private Vector2 target, wheelVelocity;
    public override void OnScroll(PointerEventData eventData)
    {
        if (!IsActive() || content == null || LandscapeOrientation.InputBlocked) return;
        base.StopMovement();
        if (!wheelActive) { target = normalizedPosition; wheelVelocity = Vector2.zero; }
        var view = viewport != null ? viewport : (RectTransform)transform;
        var delta = eventData.scrollDelta;
        if (vertical && !horizontal && Mathf.Abs(delta.x) > Mathf.Abs(delta.y)) delta.y = delta.x;
        if (horizontal && !vertical && Mathf.Abs(delta.y) > Mathf.Abs(delta.x)) delta.x = -delta.y;
        if (vertical)
        {
            float overflow = content.rect.height - view.rect.height;
            if (overflow > .1f)
            {
                float step = Mathf.Clamp(view.rect.height * .14f, 32, 96) * GamePreferences.MenuScrollSensitivity;
                if (Mathf.Sign(target.y - verticalNormalizedPosition) != Mathf.Sign(delta.y)) wheelVelocity.y = 0;
                target.y = Mathf.Clamp01(target.y + delta.y * step / overflow);
            }
        }
        if (horizontal)
        {
            float overflow = content.rect.width - view.rect.width;
            if (overflow > .1f)
            {
                float step = Mathf.Clamp(view.rect.width * .14f, 32, 96) * GamePreferences.MenuScrollSensitivity;
                if (Mathf.Sign(target.x - horizontalNormalizedPosition) != Mathf.Sign(delta.x)) wheelVelocity.x = 0;
                target.x = Mathf.Clamp01(target.x + delta.x * step / overflow);
            }
        }
        wheelActive = true;
    }
    protected override void LateUpdate()
    {
        if (wheelActive)
        {
            base.StopMovement();
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            var current = normalizedPosition;
            if (horizontal) current.x = Mathf.SmoothDamp(current.x, target.x, ref wheelVelocity.x, .08f, Mathf.Infinity, dt);
            if (vertical) current.y = Mathf.SmoothDamp(current.y, target.y, ref wheelVelocity.y, .08f, Mathf.Infinity, dt);
            normalizedPosition = current;
            if ((current - target).sqrMagnitude < .0000001f) { normalizedPosition = target; wheelActive = false; }
        }
        base.LateUpdate();
    }
    public override void StopMovement() { wheelActive = false; wheelVelocity = Vector2.zero; base.StopMovement(); }
    public override void OnBeginDrag(PointerEventData eventData) { StopMovement(); base.OnBeginDrag(eventData); }
    protected override void OnDisable() { StopMovement(); base.OnDisable(); }
}
