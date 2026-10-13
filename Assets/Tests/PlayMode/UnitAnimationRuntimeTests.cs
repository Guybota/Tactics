#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class UnitAnimationRuntimeTests
{
    private GameObject container;
    private Component unit;
    private Animator[] animators;
    private SpriteRenderer[] renderers;
    private static Type Script(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Get(object target, string field) => target.GetType().GetField(field).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field).SetValue(target, value);
    private static object Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args);

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        container = new GameObject("Isolated unit animation test");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Human/Knight.prefab");
        Assert.That(prefab, Is.Not.Null);
        var actor = UnityEngine.Object.Instantiate(prefab, container.transform);
        unit = actor.GetComponent(Script("Unit"));
        // Exercise Unit's real methods without a match, health bar or movement loop.
        ((Behaviour)unit).enabled = false;
        Set(unit, "health_bar", null);
        Set(unit, "team", null);
        Set(unit, "current_tile", null);
        Set(unit, "hp", 100f);
        Set(unit, "max_health", 100f);
        renderers = new[] { (SpriteRenderer)Get(unit, "sprite"), (SpriteRenderer)Get(unit, "color") };
        animators = new[] { (Animator)Get(unit, "animator1"), (Animator)Get(unit, "animator2") };
        foreach (var animator in animators)
        {
            Assert.That(animator, Is.Not.Null);
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        yield return null;
        Call(unit, "Start");
        Advance(animators, 0);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (container != null) UnityEngine.Object.Destroy(container);
        yield return null;
    }

    private static void Advance(Animator[] targets, float duration)
    {
        foreach (var animator in targets) animator.Update(duration);
    }

    private static void AssertState(Animator[] targets, string state)
    {
        foreach (var animator in targets)
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName(state), Is.True, animator.name + " must play " + state);
        Assert.That(targets[1].GetCurrentAnimatorStateInfo(0).normalizedTime,
            Is.EqualTo(targets[0].GetCurrentAnimatorStateInfo(0).normalizedTime).Within(.001f));
    }

    [UnityTest]
    public IEnumerator WalkingStartsBothLayersAndStoppingReturnsToIdle()
    {
        AssertState(animators, "Idle");
        Call(unit, "SetWalking", true);
        Advance(animators, 0);
        AssertState(animators, "Walk");
        Advance(animators, 2f);
        AssertState(animators, "Walk");
        Call(unit, "StopMovement");
        Advance(animators, 0);
        AssertState(animators, "Idle");
        yield return null;
    }

    [UnityTest]
    public IEnumerator NonlethalDamagePlaysHurtOnBothLayersThenReturnsToIdle()
    {
        Call(unit, "Take_damage", 1f);
        Advance(animators, 0);
        AssertState(animators, "Hurt");
        Assert.That((float)Get(unit, "hp"), Is.EqualTo(99f));
        Assert.That(unit.gameObject.activeSelf, Is.True);
        float duration = ((AnimationClip)Get(unit, "hurtAnimation")).length;
        for (float elapsed = 0; elapsed < duration + .15f; elapsed += .025f) Advance(animators, .025f);
        AssertState(animators, "Idle");
        yield return null;
    }

    [UnityTest]
    public IEnumerator CombatCopyCanPlayAttackHurtAndDeathAfterGameplayUnitIsInactive()
    {
        var visualType = Script("CombatPresentation").GetNestedType("Visual", BindingFlags.NonPublic);
        Assert.That(visualType, Is.Not.Null);
        var visual = Activator.CreateInstance(visualType, unit, container.transform, "Isolated combat visual");
        try
        {
            var visualRoot = (GameObject)Get(visual, "Root");
            var copies = visualRoot.GetComponentsInChildren<Animator>();
            Assert.That(copies.Length, Is.EqualTo(2));
            foreach (var renderer in renderers) Assert.That(renderer.forceRenderingOff, Is.True);
            unit.gameObject.SetActive(false);
            foreach (var state in new[] { "Attack", "Hurt", "Death" })
            {
                Call(visual, "BeginState", state);
                AssertState(copies, state);
                var sprites = visualRoot.GetComponentsInChildren<SpriteRenderer>();
                var firstRect = sprites[0].sprite.rect;
                Call(visual, "Tick", .11f);
                AssertState(copies, state);
                Assert.That(copies[0].GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(0));
                Assert.That(sprites[0].sprite.rect, Is.Not.EqualTo(firstRect), state + " must advance its visible sprite.");
                Assert.That(sprites[1].sprite.rect, Is.EqualTo(sprites[0].sprite.rect));
                Assert.That(sprites[1].sprite.pivot, Is.EqualTo(sprites[0].sprite.pivot));
            }
        }
        finally { Call(visual, "Dispose"); }
        foreach (var renderer in renderers) Assert.That(renderer.forceRenderingOff, Is.False);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LethalDamageDeactivatesGameplayUnitAndKeepsADeathVisual()
    {
        Call(unit, "Take_damage", 1000f);
        Assert.That(unit.gameObject.activeSelf, Is.False);
        Assert.That((bool)Get(unit, "has_died"), Is.True);
        Assert.That((float)Get(unit, "hp"), Is.Zero);
        var reactions = container.GetComponentsInChildren(Script("UnitReactionPresentation"), true);
        Assert.That(reactions.Length, Is.EqualTo(1));
        var copies = reactions[0].GetComponentsInChildren<Animator>();
        Assert.That(copies.Length, Is.EqualTo(2));
        AssertState(copies, "Death");
        Assert.That(copies.All(animator => animator.gameObject.activeInHierarchy), Is.True);
        var firstRect = copies[0].GetComponent<SpriteRenderer>().sprite.rect;
        Advance(copies, .11f);
        AssertState(copies, "Death");
        Assert.That(copies[0].GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(0));
        Assert.That(copies[0].GetComponent<SpriteRenderer>().sprite.rect, Is.Not.EqualTo(firstRect));
        yield return null;
    }
}
#endif
