using System;
using System.Collections.Generic;

namespace Tactics.Core
{
    public enum ContactPhase { Began, Moved, Stationary, Ended, Canceled }
    public readonly struct ContactPoint
    {
        public readonly int Id;
        public readonly float X, Y;
        public readonly ContactPhase Phase;
        public readonly bool OverUI;
        public ContactPoint(int id, float x, float y, ContactPhase phase, bool overUI = false)
        { Id = id; X = x; Y = y; Phase = phase; OverUI = overUI; }
        public bool IsLive => Phase != ContactPhase.Ended && Phase != ContactPhase.Canceled;
    }
    public readonly struct TouchGesture
    {
        public readonly bool Tap, Navigate;
        public readonly float X, Y, PreviousX, PreviousY, ZoomRatio;
        public TouchGesture(bool tap, bool navigate, float x, float y, float previousX, float previousY, float zoomRatio = 1)
        { Tap = tap; Navigate = navigate; X = x; Y = y; PreviousX = previousX; PreviousY = previousY; ZoomRatio = zoomRatio; }
    }

    // Own a complete finger sequence: swipes, pinches and canceled/UI touches never become taps.
    public sealed class TouchGestureTracker
    {
        private bool active, blocked, moved, multiple;
        private int firstId, secondId = -1, liveCount;
        private float startX, startY, lastX, lastY, lastDistance;
        public TouchGesture Process(IReadOnlyList<ContactPoint> contacts, float dragThreshold, bool inputBlocked = false)
        {
            if (contacts == null) throw new ArgumentNullException(nameof(contacts));
            if (contacts.Count == 0) { Reset(); return default; }
            int count = 0; ContactPoint first = default, second = default;
            foreach (var point in contacts)
            {
                if (point.Phase == ContactPhase.Canceled || (point.Phase == ContactPhase.Began && point.OverUI)) blocked = true;
                if (!point.IsLive) continue;
                if (count == 0) first = point;
                else if (count == 1) second = point;
                count++;
            }
            if (inputBlocked || count > 2) blocked = true;
            if (count == 0)
            {
                TouchGesture result = default;
                foreach (var point in contacts)
                    if (active && !blocked && !moved && !multiple && point.Id == firstId && point.Phase == ContactPhase.Ended
                        && !point.OverUI && Distance(point.X, point.Y, startX, startY) <= dragThreshold)
                        result = new TouchGesture(true, false, point.X, point.Y, point.X, point.Y);
                Reset(); return result;
            }
            if (blocked) { liveCount = count; return default; }
            if (!active)
            {
                if (first.Phase != ContactPhase.Began || (count == 2 && second.Phase != ContactPhase.Began))
                { blocked = true; return default; }
                active = true; firstId = first.Id; startX = lastX = first.X; startY = lastY = first.Y;
            }
            if (count == 2)
            {
                multiple = moved = true;
                if (first.Id > second.Id) { var swap = first; first = second; second = swap; }
                float x = (first.X + second.X) * .5f, y = (first.Y + second.Y) * .5f;
                float distance = Distance(first.X, first.Y, second.X, second.Y);
                var result = liveCount == 2 && firstId == first.Id && secondId == second.Id
                    ? new TouchGesture(false, true, x, y, lastX, lastY, distance > .01f && lastDistance > .01f ? lastDistance / distance : 1)
                    : default;
                firstId = first.Id; secondId = second.Id; lastX = x; lastY = y; lastDistance = distance; liveCount = 2;
                return result;
            }
            if (liveCount == 2 || firstId != first.Id)
            { firstId = first.Id; lastX = first.X; lastY = first.Y; moved = true; }
            liveCount = 1;
            if (!moved && Distance(first.X, first.Y, startX, startY) > dragThreshold) moved = true;
            var gesture = moved ? new TouchGesture(false, true, first.X, first.Y, lastX, lastY) : default;
            // Until a drag begins, keep its original anchor (including all small movements).
            if (moved) { lastX = first.X; lastY = first.Y; }
            return gesture;
        }
        private static float Distance(float x, float y, float otherX, float otherY)
            => (float)Math.Sqrt((x - otherX) * (x - otherX) + (y - otherY) * (y - otherY));
        private void Reset() { active = blocked = moved = multiple = false; liveCount = 0; secondId = -1; }
    }
}
