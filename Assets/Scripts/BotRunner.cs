using System.Collections.Generic;
using System.Diagnostics;
using Tactics.Core;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BotRunner : MonoBehaviour
{
    [SerializeField, Min(0)] private float actionDelay = .3f;
    [SerializeField, Min(.1f)] private float planningMillisecondsPerFrame = 2;
    [SerializeField, Min(1)] private int planningStepsPerFrame = 64;
    private scr_game_contoller controller;
    private BotPlanningSession search;
    private int revision = -1;
    private float delay;
    private Unit animatedUnit;
    private scr_camera followedCamera;
    private readonly Queue<Vector3> waypoints = new Queue<Vector3>();
    public bool IsAnimating => (animatedUnit != null && waypoints.Count > 0) || (controller != null && controller.IsCombatMoving);
    public BotDecision LastDecision { get; private set; }
    public void Initialize(scr_game_contoller match) { controller = match; delay = actionDelay; }

    private void Update()
    {
        if (controller == null) return;
        if (revision != controller.MatchRevision)
        {
            CancelPlanning(); CancelPresentation(); revision = controller.MatchRevision;
        }
        if (!controller.CanAdvanceBot) { CancelPlanning(); return; }
        if (controller.IsCombatAnimating) return;
        if (IsAnimating) { Animate(); return; }
        if (!controller.CanPlanBot) return;
        if (delay > 0) { delay -= Time.unscaledDeltaTime; return; }
        if (search == null) search = controller.CreateBotPlanner().Begin(BotObservation.Capture(controller.GetStateSnapshot()));
        var timer = Stopwatch.StartNew();
        int steps = 0;
        // Also bound work count so a fast editor cannot evaluate an entire large search in one frame.
        do { search.Advance(); steps++; }
        while (!search.IsComplete && steps < planningStepsPerFrame && timer.Elapsed.TotalMilliseconds < planningMillisecondsPerFrame);
        if (!search.IsComplete) return;
        LastDecision = search.Best; CancelPlanning();
        if (LastDecision == null) return;
        Execute(LastDecision.Action);
        delay = actionDelay;
    }

    private void Execute(MatchAction action)
    {
        Unit actor = null;
        var points = new List<Vector3>();
        Vector3 start = Vector3.zero;
        if (action.Kind != MatchActionKind.EndTurn && action.Kind != MatchActionKind.Recruit)
        {
            actor = controller.teams[action.Actor.Player].Get_unit(action.Actor.Id);
            if (actor != null)
            {
                start = actor.transform.position;
                // Path calculation is presentation only; the executor confirms the destination first.
                var state = controller.GetStateSnapshot();
                foreach (var p in controller.GetMovementPath(action.Actor, action.Destination))
                    if (!p.Equals(state.FindUnit(action.Actor).Position))
                        points.Add(controller.gameMap.GetTileAtPosition(p.X, p.Y).transform.position);
            }
        }
        if (!controller.TryExecuteAction(action, out string error))
        {
            UnityEngine.Debug.LogWarning("Bot action rejected: " + error);
            return;
        }
        revision = controller.MatchRevision;
        if (action.Kind == MatchActionKind.Attack) return; // CombatPresentation owns movement followed by attack.
        if (actor == null || actor.has_died || points.Count == 0 || controller.status == "Over") return;
        animatedUnit = actor;
        actor.SetWalking(true);
        actor.transform.position = start;
        foreach (var point in points) waypoints.Enqueue(point);
        followedCamera = controller.game_camera.GetComponent<scr_camera>();
        followedCamera.Follow_unit(actor);
    }

    private void Animate()
    {
        bool left = waypoints.Peek().x < animatedUnit.transform.position.x;
        if (Mathf.Abs(waypoints.Peek().x - animatedUnit.transform.position.x) > .1f)
        { animatedUnit.sprite.flipX = left; animatedUnit.color.flipX = left; }
        animatedUnit.transform.position = Vector3.MoveTowards(animatedUnit.transform.position, waypoints.Peek(), 400 * Time.deltaTime);
        if (Vector3.Distance(animatedUnit.transform.position, waypoints.Peek()) > .1f) return;
        waypoints.Dequeue();
        if (waypoints.Count == 0) CancelPresentation();
    }
    private void CancelPlanning() { search?.Dispose(); search = null; }
    private void CancelPresentation()
    {
        if (animatedUnit != null)
        {
            animatedUnit.SetWalking(false);
            if (animatedUnit.current_tile != null) animatedUnit.transform.position = animatedUnit.current_tile.transform.position;
        }
        if (followedCamera != null && followedCamera.unit == animatedUnit) followedCamera.Clear_follow();
        followedCamera = null;
        animatedUnit = null; waypoints.Clear();
    }
    private void OnDisable() { CancelPlanning(); CancelPresentation(); }
}
