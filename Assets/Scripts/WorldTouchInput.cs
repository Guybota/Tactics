using System;
using System.Collections.Generic;
using Tactics.Core;
using UnityEngine;
using ContactPoint = Tactics.Core.ContactPoint;

[DefaultExecutionOrder(-200), DisallowMultipleComponent, RequireComponent(typeof(scr_camera))]
public sealed class WorldTouchInput : MonoBehaviour
{
    public event Action<Vector2> Tapped;
    private readonly TouchGestureTracker gestures = new TouchGestureTracker();
    private readonly List<ContactPoint> contacts = new List<ContactPoint>(3);
    private scr_camera navigation;
    private static float mouseSuppressedUntil;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => mouseSuppressedUntil = 0;
    public static bool SuppressMouse => Input.touchCount > 0 || Time.unscaledTime < mouseSuppressedUntil;
    private void Awake() => navigation = GetComponent<scr_camera>();
    private void Update()
    {
        contacts.Clear();
        for (int i = 0; i < Input.touchCount; i++)
        {
            var touch = Input.GetTouch(i);
            contacts.Add(new ContactPoint(touch.fingerId, touch.position.x, touch.position.y, (ContactPhase)touch.phase,
                WorldPointer.IsOverUI(touch.position)));
        }
        ProcessContacts(contacts);
    }
    public void ProcessContacts(IReadOnlyList<ContactPoint> points)
    {
        if (points.Count > 0) mouseSuppressedUntil = Time.unscaledTime + .15f;
        float threshold = Mathf.Max(10, MobileUiMode.Density * 12);
        var gesture = gestures.Process(points, threshold, LandscapeOrientation.InputBlocked || !Application.isFocused && !Application.runInBackground);
        if (gesture.Navigate) navigation.ApplyTouchNavigation(new Vector2(gesture.PreviousX, gesture.PreviousY), new Vector2(gesture.X, gesture.Y), gesture.ZoomRatio);
        if (gesture.Tap) Tapped?.Invoke(new Vector2(gesture.X, gesture.Y));
    }
    private void OnApplicationFocus(bool focused) { if (!focused) gestures.Process(new[] { new ContactPoint(0, 0, 0, ContactPhase.Canceled) }, 10, true); }
}
