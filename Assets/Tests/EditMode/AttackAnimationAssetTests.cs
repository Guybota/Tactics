using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public sealed class AttackAnimationAssetTests
{
    private const string CharacterFolder = "Assets/Prefabs/Characters";
    private static Type UnitType => Type.GetType("Unit, Assembly-CSharp", true);

    private static GameObject[] Characters()
    {
        var paths = AssetDatabase.FindAssets("t:Prefab", new[] { CharacterFolder });
        Assert.That(paths.Length, Is.EqualTo(17), "The full playable roster must be covered.");
        return paths.Select(guid => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid))).ToArray();
    }

    private static SpriteRenderer[] Renderers(GameObject root)
    {
        var unit = root.GetComponent(UnitType);
        Assert.That(unit, Is.Not.Null, root.name);
        var renderers = new[]
        {
            (SpriteRenderer)UnitType.GetField("sprite").GetValue(unit),
            (SpriteRenderer)UnitType.GetField("color").GetValue(unit)
        };
        foreach (var renderer in renderers)
        {
            Assert.That(renderer, Is.Not.Null, root.name);
            Assert.That(renderer.sprite, Is.Not.Null, root.name + "/" + renderer.name);
        }
        return renderers;
    }

    private static AnimatorStateMachine StateMachine(SpriteRenderer renderer, string context)
    {
        var animator = renderer.GetComponent<Animator>();
        Assert.That(animator, Is.Not.Null, context);
        Assert.That(animator.enabled, Is.True, context);
        var controller = animator.runtimeAnimatorController as AnimatorController;
        Assert.That(controller, Is.Not.Null, context);
        Assert.That(controller.layers, Is.Not.Empty, context);
        return controller.layers[0].stateMachine;
    }

    private static AnimatorState State(AnimatorStateMachine machine, string name, string context)
    {
        var states = machine.states.Where(s => s.state.name == name).Select(s => s.state).ToArray();
        Assert.That(states.Length, Is.EqualTo(1), context + " must have exactly one " + name + " state.");
        return states[0];
    }

    [Test]
    public void EveryCharacterReferencesBothAnimatorsAndItsBodyReactionClips()
    {
        foreach (var root in Characters())
        {
            var unit = root.GetComponent(UnitType);
            var renderers = Renderers(root);
            for (int layer = 0; layer < renderers.Length; layer++)
            {
                var animator = renderers[layer].GetComponent<Animator>();
                Assert.That(UnitType.GetField("animator" + (layer + 1)).GetValue(unit), Is.SameAs(animator), root.name);
                var machine = StateMachine(renderers[layer], root.name + "/" + renderers[layer].name);
                Assert.That(machine.defaultState, Is.SameAs(State(machine, "Idle", root.name)), root.name);
            }
            var bodyMachine = StateMachine(renderers[0], root.name);
            foreach (var reaction in new[] { "Attack", "Hurt", "Death" })
            {
                var clip = UnitType.GetField(char.ToLowerInvariant(reaction[0]) + reaction.Substring(1) + "Animation").GetValue(unit);
                Assert.That(clip, Is.Not.Null, root.name + "/" + reaction);
                Assert.That(clip, Is.SameAs(State(bodyMachine, reaction, root.name).motion), root.name + "/" + reaction);
            }
        }
    }

    [TestCase("Idle", true)]
    [TestCase("Walk", true)]
    [TestCase("Attack", false)]
    [TestCase("Hurt", false)]
    [TestCase("Death", false)]
    public void EveryCharacterHasMatchingBodyAndTeamColourFrames(string stateName, bool loop)
    {
        foreach (var root in Characters())
        {
            var renderers = Renderers(root);
            var curves = new ObjectReferenceKeyframe[2][];
            var clips = new AnimationClip[2];
            for (int layer = 0; layer < renderers.Length; layer++)
            {
                string context = root.name + "/" + renderers[layer].name + "/" + stateName;
                var machine = StateMachine(renderers[layer], context);
                var state = State(machine, stateName, context);
                clips[layer] = state.motion as AnimationClip;
                Assert.That(clips[layer], Is.Not.Null, context);
                Assert.That(clips[layer].length, Is.GreaterThan(0), context);
                Assert.That(clips[layer].frameRate, Is.GreaterThan(0), context);
                Assert.That(AnimationUtility.GetAnimationClipSettings(clips[layer]).loopTime, Is.EqualTo(loop), context);
                var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clips[layer]);
                Assert.That(bindings.Length, Is.EqualTo(1), context);
                Assert.That(bindings[0].type, Is.EqualTo(typeof(SpriteRenderer)), context);
                Assert.That(bindings[0].propertyName, Is.EqualTo("m_Sprite"), context);
                Assert.That(bindings[0].path, Is.Empty, context + " must animate its own renderer.");
                curves[layer] = AnimationUtility.GetObjectReferenceCurve(clips[layer], bindings[0]);
                Assert.That(curves[layer].Length, Is.GreaterThan(1), context);
                Assert.That(curves[layer][0].time, Is.Zero, context);
                for (int frame = 0; frame < curves[layer].Length; frame++)
                {
                    var sprite = curves[layer][frame].value as Sprite;
                    Assert.That(sprite, Is.Not.Null, context + " frame " + frame);
                    Assert.That(sprite.texture, Is.SameAs(renderers[layer].sprite.texture), context + " frame " + frame);
                    if (frame > 0)
                        Assert.That(curves[layer][frame].time, Is.GreaterThan(curves[layer][frame - 1].time), context);
                }

                if (stateName == "Attack" || stateName == "Hurt")
                {
                    Assert.That(state.transitions.Length, Is.EqualTo(1), context);
                    var transition = state.transitions[0];
                    Assert.That(transition.destinationState, Is.SameAs(State(machine, "Idle", context)), context);
                    Assert.That(transition.hasExitTime, Is.True, context);
                    Assert.That(transition.exitTime, Is.EqualTo(1).Within(.001f), context);
                    Assert.That(transition.duration, Is.Zero, context);
                    Assert.That(transition.conditions, Is.Empty, context + " must finish without a gameplay trigger.");
                }
                else if (stateName == "Death")
                    Assert.That(state.transitions, Is.Empty, context + " must retain the final death frame.");
            }

            string pair = root.name + "/" + stateName;
            Assert.That(clips[1].length, Is.EqualTo(clips[0].length).Within(.001f), pair);
            Assert.That(clips[1].frameRate, Is.EqualTo(clips[0].frameRate), pair);
            Assert.That(curves[1].Length, Is.EqualTo(curves[0].Length), pair);
            for (int frame = 0; frame < curves[0].Length; frame++)
            {
                var body = (Sprite)curves[0][frame].value;
                var colour = (Sprite)curves[1][frame].value;
                Assert.That(curves[1][frame].time, Is.EqualTo(curves[0][frame].time).Within(.0001f), pair + " frame " + frame);
                Assert.That(colour.rect, Is.EqualTo(body.rect), pair + " frame " + frame);
                Assert.That(colour.pivot, Is.EqualTo(body.pivot), pair + " frame " + frame);
                Assert.That(colour.pixelsPerUnit, Is.EqualTo(body.pixelsPerUnit), pair + " frame " + frame);
            }
        }
    }
}
