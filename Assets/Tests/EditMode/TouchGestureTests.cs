using NUnit.Framework;
using Tactics.Core;

public class TouchGestureTests
{
    private TouchGestureTracker tracker;
    private static ContactPoint Point(int id, float x, float y, ContactPhase phase, bool ui = false) => new ContactPoint(id, x, y, phase, ui);
    private TouchGesture Frame(params ContactPoint[] points) => tracker.Process(points, 12);
    [SetUp] public void SetUp() => tracker = new TouchGestureTracker();
    [Test] public void SingleStationaryFingerProducesOneTapAtRelease()
    {
        Assert.That(Frame(Point(0, 100, 100, ContactPhase.Began)).Tap, Is.False);
        Assert.That(Frame(Point(0, 104, 100, ContactPhase.Moved)).Navigate, Is.False);
        Assert.That(Frame(Point(0, 104, 100, ContactPhase.Ended)).Tap, Is.True);
        Assert.That(Frame().Tap, Is.False);
    }
    [Test] public void SlowAccumulatedMovementIsADragAndCannotSelectOnRelease()
    {
        Frame(Point(0, 100, 100, ContactPhase.Began)); Frame(Point(0, 106, 100, ContactPhase.Moved));
        var drag = Frame(Point(0, 115, 100, ContactPhase.Moved));
        Assert.That(drag.Navigate, Is.True); Assert.That(drag.PreviousX, Is.EqualTo(100));
        Assert.That(Frame(Point(0, 115, 100, ContactPhase.Ended)).Tap, Is.False);
    }
    [Test] public void MovementOnlyAtReleaseIsNotATap()
    { Frame(Point(0, 100, 100, ContactPhase.Began)); Assert.That(Frame(Point(0, 150, 100, ContactPhase.Ended)).Tap, Is.False); }
    [Test] public void TouchBeginningOnUiStaysOwnedByUi()
    {
        Frame(Point(0, 100, 100, ContactPhase.Began, true));
        Assert.That(Frame(Point(0, 250, 100, ContactPhase.Moved)).Navigate, Is.False);
        Assert.That(Frame(Point(0, 250, 100, ContactPhase.Ended)).Tap, Is.False);
    }
    [Test] public void WorldDragMayCrossUiWithoutJumping()
    {
        Frame(Point(0, 100, 100, ContactPhase.Began));
        var drag = Frame(Point(0, 150, 100, ContactPhase.Moved, true));
        Assert.That(drag.Navigate, Is.True); Assert.That(drag.PreviousX, Is.EqualTo(100));
    }
    [Test] public void SecondFingerOnUiCancelsEntireWorldGestureUntilRelease()
    {
        Frame(Point(0, 100, 100, ContactPhase.Began));
        Frame(Point(0, 100, 100, ContactPhase.Stationary), Point(1, 200, 100, ContactPhase.Began, true));
        Assert.That(Frame(Point(0, 150, 100, ContactPhase.Moved), Point(1, 250, 100, ContactPhase.Moved)).Navigate, Is.False);
        Assert.That(Frame(Point(0, 150, 100, ContactPhase.Ended), Point(1, 250, 100, ContactPhase.Ended)).Tap, Is.False);
    }
    [Test] public void PinchUsesDistanceRatioAndTracksMidpoint()
    {
        Frame(Point(7, 100, 100, ContactPhase.Began), Point(3, 200, 100, ContactPhase.Began));
        var pinch = Frame(Point(3, 250, 100, ContactPhase.Moved), Point(7, 100, 100, ContactPhase.Stationary));
        Assert.That(pinch.Navigate, Is.True); Assert.That(pinch.ZoomRatio, Is.EqualTo(2f / 3).Within(.001f));
        Assert.That(pinch.PreviousX, Is.EqualTo(150)); Assert.That(pinch.X, Is.EqualTo(175));
    }
    [Test] public void LiftingOneFingerAfterPinchDoesNotJumpOrBecomeATap()
    {
        Frame(Point(0, 100, 100, ContactPhase.Began), Point(1, 200, 100, ContactPhase.Began));
        var transition = Frame(Point(0, 100, 100, ContactPhase.Stationary), Point(1, 200, 100, ContactPhase.Ended));
        Assert.That(transition.X - transition.PreviousX, Is.EqualTo(0));
        Assert.That(Frame(Point(0, 100, 100, ContactPhase.Ended)).Tap, Is.False);
    }
    [Test] public void CanceledTouchCannotActAndNextSequenceRecovers()
    {
        Frame(Point(0, 100, 100, ContactPhase.Began)); Assert.That(Frame(Point(0, 100, 100, ContactPhase.Canceled)).Tap, Is.False);
        Frame(Point(4, 200, 100, ContactPhase.Began)); Assert.That(Frame(Point(4, 200, 100, ContactPhase.Ended)).Tap, Is.True);
    }
    [Test] public void BlockedSequenceDoesNotResumeWhenMenuOrPortraitClosesMidTouch()
    {
        tracker.Process(new[] { Point(0, 100, 100, ContactPhase.Began) }, 12, true);
        Assert.That(Frame(Point(0, 150, 100, ContactPhase.Moved)).Navigate, Is.False);
        Assert.That(Frame(Point(0, 150, 100, ContactPhase.Ended)).Tap, Is.False);
    }
    [Test] public void ThreeFingerGestureDoesNothing()
    {
        Frame(Point(0, 0, 0, ContactPhase.Began), Point(1, 100, 0, ContactPhase.Began), Point(2, 200, 0, ContactPhase.Began));
        Assert.That(Frame(Point(0, 30, 0, ContactPhase.Moved)).Navigate, Is.False);
    }
    [Test] public void MissingBeganCannotSelectAfterLosingFocus()
    { Frame(Point(0, 100, 100, ContactPhase.Moved)); Assert.That(Frame(Point(0, 100, 100, ContactPhase.Ended)).Tap, Is.False); }
}
