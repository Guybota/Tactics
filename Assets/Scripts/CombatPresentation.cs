using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Visual copies let a confirmed lethal attack finish without keeping dead gameplay
// units alive or delaying the authoritative state used by saves and the planner.
[DisallowMultipleComponent]
public sealed class CombatPresentation : MonoBehaviour
{
    private scr_game_contoller controller;
    private Visual attacker, defender;
    private scr_camera followedCamera;
    private Transform focusedTarget;
    public bool IsPlaying => attacker != null;
    public bool IsMoving { get; private set; }
    public bool IsCounterAttacking { get; private set; }
    private bool Paused => Time.timeScale <= 0 || LandscapeOrientation.InputBlocked
        || controller.status == "Menu" || controller.status == "Saving" || controller.status == "Loading";

    public void Prepare(scr_game_contoller match, Unit actor, Unit target)
    {
        Cancel();
        controller = match;
        followedCamera = match.game_camera != null ? match.game_camera.GetComponent<scr_camera>() : null;
        attacker = new Visual(actor, transform, "Attack presentation");
        defender = new Visual(target, transform, "Defender presentation");
    }

    public void Play(List<Vector3> path, Vector3 destination, bool counterAttack, bool targetDied,
        bool actorDied, bool targetTookDamage, bool actorTookDamage)
        => StartCoroutine(Sequence(path, destination, counterAttack, targetDied, actorDied, targetTookDamage, actorTookDamage));

    private IEnumerator Sequence(List<Vector3> path, Vector3 destination, bool counterAttack, bool targetDied,
        bool actorDied, bool targetTookDamage, bool actorTookDamage)
    {
        FocusBot(attacker);
        IsMoving = path.Count > 0;
        if (IsMoving) attacker.BeginState("Walk");
        foreach (var point in path)
        {
            attacker.Face(point);
            while (Vector3.Distance(attacker.Root.transform.position, point) > .1f)
            {
                if (!Paused)
                {
                    attacker.Root.transform.position = Vector3.MoveTowards(attacker.Root.transform.position, point, 400 * Time.deltaTime);
                    if (followedCamera != null && followedCamera.PresentationTarget == attacker.Root.transform)
                        followedCamera.RefreshPresentationFocus();
                    attacker.Tick(Time.deltaTime);
                }
                yield return null;
            }
        }
        attacker.Root.transform.position = destination;
        if (followedCamera != null && followedCamera.PresentationTarget == attacker.Root.transform)
            followedCamera.RefreshPresentationFocus();
        IsMoving = false;
        attacker.Face(defender.Root.transform.position);
        defender.Face(destination);
        yield return Animate(attacker, "Attack");
        if (targetDied || targetTookDamage) yield return Animate(defender, targetDied ? "Death" : "Hurt");
        if (targetDied) defender.Root.SetActive(false);
        if (counterAttack)
        {
            IsCounterAttacking = true;
            FocusBot(defender);
            yield return Animate(defender, "Attack");
            if (actorDied || actorTookDamage) yield return Animate(attacker, actorDied ? "Death" : "Hurt");
        }
        Cancel();
    }

    private void FocusBot(Visual visual)
    {
        if (followedCamera == null || !visual.IsBot) return;
        focusedTarget = visual.Root.transform;
        followedCamera.FollowPresentation(focusedTarget);
    }

    private IEnumerator Animate(Visual visual, string state)
    {
        visual.BeginState(state);
        float elapsed = 0;
        float duration = visual.Duration(state);
        while (elapsed < duration)
        {
            yield return null;
            if (Paused) continue;
            float step = Mathf.Min(Time.deltaTime, duration - elapsed);
            visual.Tick(step);
            elapsed += step;
        }
        if (state != "Death") visual.BeginState("Idle");
    }

    public void Cancel()
    {
        StopAllCoroutines();
        if (followedCamera != null && focusedTarget != null && followedCamera.PresentationTarget == focusedTarget)
            followedCamera.Clear_follow();
        followedCamera = null; focusedTarget = null;
        attacker?.Dispose(); defender?.Dispose();
        attacker = defender = null;
        IsCounterAttacking = false;
        IsMoving = false;
    }
    private void OnDisable() => Cancel();

    internal sealed class Visual
    {
        public readonly GameObject Root;
        private readonly float attackDuration, hurtDuration, deathDuration;
        private readonly SpriteRenderer[] originals, copies;
        private readonly bool[] previousHidden;
        private readonly Animator[] animators;
        private readonly Unit unit;
        private readonly Health_bar healthDisplay;
        public bool IsBot => unit != null && unit.team != null && unit.team.isBot;

        public Visual(Unit unit, Transform parent, string name)
        {
            this.unit = unit;
            healthDisplay = unit.GetComponentInChildren<Health_bar>(true);
            Root = new GameObject(name);
            Root.transform.SetParent(parent, false);
            Root.transform.position = unit.transform.position;
            attackDuration = unit.attackAnimation != null ? unit.attackAnimation.length : .4f;
            hurtDuration = unit.hurtAnimation != null ? unit.hurtAnimation.length : .25f;
            deathDuration = unit.deathAnimation != null ? unit.deathAnimation.length : .4f;
            originals = new[] { unit.sprite, unit.color };
            copies = new SpriteRenderer[2]; previousHidden = new bool[2]; animators = new Animator[2];
            for (int i = 0; i < originals.Length; i++)
            {
                var source = originals[i];
                if (source == null) continue;
                previousHidden[i] = source.forceRenderingOff;
                var layer = new GameObject(source.name);
                layer.layer = source.gameObject.layer;
                layer.transform.SetParent(Root.transform, false);
                layer.transform.position = source.transform.position;
                layer.transform.rotation = source.transform.rotation;
                layer.transform.localScale = source.transform.lossyScale;
                var copy = copies[i] = layer.AddComponent<SpriteRenderer>();
                copy.sprite = source.sprite; copy.color = source.color;
                copy.sharedMaterial = source.sharedMaterial;
                copy.sortingLayerID = source.sortingLayerID; copy.sortingOrder = source.sortingOrder;
                copy.flipX = source.flipX; copy.flipY = source.flipY;
                copy.spriteSortPoint = source.spriteSortPoint;
                var originalAnimator = source.GetComponent<Animator>();
                if (originalAnimator != null && originalAnimator.runtimeAnimatorController != null)
                {
                    var animator = animators[i] = layer.AddComponent<Animator>();
                    animator.runtimeAnimatorController = originalAnimator.runtimeAnimatorController;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.enabled = false; // Both layers advance together, including during pauses.
                }
                source.forceRenderingOff = true;
            }
            if (healthDisplay != null)
            {
                var top = copies[1] != null ? copies[1] : copies[0];
                if (top != null)
                    healthDisplay.SetPresentationSorting(top.sortingLayerID,
                        Mathf.Max(copies[0] != null ? copies[0].sortingOrder : top.sortingOrder, top.sortingOrder) + 1);
            }
        }

        public void Face(Vector3 target)
        {
            float dx = target.x - Root.transform.position.x;
            if (Mathf.Abs(dx) < .1f) return;
            if (unit != null) unit.movDir = dx < 0 ? -1 : 1;
            for (int i = 0; i < copies.Length; i++)
            {
                if (copies[i] != null) copies[i].flipX = dx < 0;
                if (originals[i] != null) originals[i].flipX = dx < 0;
            }
        }
        public float Duration(string state) => state == "Death" ? deathDuration : state == "Hurt" ? hurtDuration : attackDuration;
        public void BeginState(string state)
        {
            foreach (var animator in animators)
            {
                if (animator == null) continue;
                animator.Rebind(); animator.Update(0);
                if (!animator.HasState(0, Animator.StringToHash(state))) continue;
                animator.Play(state, 0, 0); animator.Update(0);
            }
        }
        public void Tick(float delta)
        {
            foreach (var animator in animators) if (animator != null) animator.Update(delta);
        }
        public void Dispose()
        {
            if (healthDisplay != null) healthDisplay.ClearPresentationSorting();
            for (int i = 0; i < originals.Length; i++)
                if (originals[i] != null) originals[i].forceRenderingOff = previousHidden[i];
            if (Root != null) { Root.SetActive(false); Object.Destroy(Root); }
        }
    }
}
