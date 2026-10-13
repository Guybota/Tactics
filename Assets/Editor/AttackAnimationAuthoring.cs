using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Explicit, repeatable setup. Existing sprite slices, idle clips and prefab layouts are preserved.
public static class AttackAnimationAuthoring
{
    private struct AttackRow
    {
        public string Prefab;
        public int Row, Count, DeathRow, HurtRow, WalkRow;
        public AttackRow(string prefab, int row, int count, int deathRow, int hurtRow = 2, int walkRow = 1)
        { Prefab = prefab; Row = row; Count = count; DeathRow = deathRow; HurtRow = hurtRow; WalkRow = walkRow; }
    }
    private static readonly AttackRow[] Rows =
    {
        new AttackRow("Elf/Arcane Archer", 3, 13, 6, 5),
        new AttackRow("Elf/Elf Mage", 3, 11, 7, 6),
        new AttackRow("Elf/Elf Queen", 3, 11, 7, 6),
        new AttackRow("Elf/Horse Elf", 3, 7, 5, 4),
        new AttackRow("Elf/Sword Man", 3, 6, 6, 5),
        new AttackRow("Human/Archer", 5, 10, 4),
        new AttackRow("Human/Crossbow Man", 5, 9, 4),
        new AttackRow("Human/King", 6, 9, 5, 3, 2),
        new AttackRow("Human/Knight", 2, 6, 5, 3),
        new AttackRow("Human/Prince", 5, 6, 4),
        new AttackRow("Human/Shield Unit", 6, 5, 5),
        new AttackRow("Human/Soldier", 5, 5, 4),
        new AttackRow("Orc/Goblin", 3, 4, 5, 4),
        new AttackRow("Orc/Orc", 3, 5, 5, 4),
        new AttackRow("Orc/OrcAxeThrower", 4, 6, 6, 5),
        new AttackRow("Orc/Warchief", 3, 5, 7, 6),
        new AttackRow("Orc/WargRider", 3, 6, 5, 4)
    };

    [MenuItem("Tactics/Combat/Set up unit animations")]
    public static void Apply()
    {
        // Validate every pair before modifying any asset.
        foreach (var row in Rows)
        {
            var unit = AssetDatabase.LoadAssetAtPath<GameObject>(PathFor(row)).GetComponent<Unit>();
            foreach (int index in new[] { 0, row.WalkRow, row.Row, row.HurtRow, row.DeathRow })
            {
                var frames = Frames(unit.sprite, index);
                var colors = Frames(unit.color, index);
                if (frames.Length < 2 || frames.Length != colors.Length || (index == row.Row && frames.Length != row.Count)
                    || frames.Where((f, i) => f.rect != colors[i].rect || f.pivot != colors[i].pivot).Any())
                    throw new InvalidOperationException("Animation slices do not match: " + row.Prefab + " row " + index);
            }
        }
        foreach (var row in Rows)
        {
            var root = PrefabUtility.LoadPrefabContents(PathFor(row));
            try
            {
                var unit = root.GetComponent<Unit>();
                var clip = InstallLayer(unit.sprite, row);
                InstallLayer(unit.color, row);
                unit.attackAnimation = clip;
                unit.hurtAnimation = FindClip(unit.sprite, "Hurt");
                unit.deathAnimation = FindClip(unit.sprite, "Death");
                unit.animator1 = unit.sprite.GetComponent<Animator>();
                unit.animator2 = unit.color.GetComponent<Animator>();
                PrefabUtility.SaveAsPrefabAsset(root, PathFor(row));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Idle, walk, attack, hurt and death configured for " + Rows.Length + " units (body and team colour).");
    }
    private static string PathFor(AttackRow row) => "Assets/Prefabs/Characters/" + row.Prefab + ".prefab";
    private static Sprite[] Frames(SpriteRenderer renderer, int row)
    {
        var sprites = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(renderer.sprite.texture)).OfType<Sprite>().ToArray();
        float y = sprites.Max(s => s.rect.y) - row * 32;
        return sprites.Where(s => Mathf.Approximately(s.rect.y, y)).OrderBy(s => s.rect.x).ToArray();
    }
    private static AnimationClip InstallLayer(SpriteRenderer renderer, AttackRow row)
    {
        string texture = AssetDatabase.GetAssetPath(renderer.sprite.texture);
        string folder = Path.GetDirectoryName(texture).Replace('\\', '/');
        string stem = Path.GetFileNameWithoutExtension(texture);
        var clip = CreateClip(folder + "/" + stem + "Attack.anim", Frames(renderer, row.Row), 12, false);
        var animator = renderer.GetComponent<Animator>();
        if (animator == null) animator = renderer.gameObject.AddComponent<Animator>();
        var controller = animator.runtimeAnimatorController as AnimatorController;
        if (controller == null)
        {
            string path = folder + "/" + stem + "_Attack.controller";
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            animator.runtimeAnimatorController = controller;
        }
        var machine = controller.layers[0].stateMachine;
        var idle = SetState(machine, "Idle", CreateClip(folder + "/" + stem + "Idle.anim", Frames(renderer, 0), 4, true));
        machine.defaultState = idle;
        SetState(machine, "Walk", CreateClip(folder + "/" + stem + "Walk.anim", Frames(renderer, row.WalkRow), 12, true));
        var attack = SetState(machine, "Attack", clip);
        var hurt = SetState(machine, "Hurt", CreateClip(folder + "/" + stem + "Hurt.anim", Frames(renderer, row.HurtRow), 12, false));
        SetState(machine, "Death", CreateClip(folder + "/" + stem + "Death.anim", Frames(renderer, row.DeathRow), 10, false));
        foreach (var state in new[] { attack, hurt })
        {
            var toIdle = state.AddTransition(idle);
            toIdle.hasExitTime = true; toIdle.exitTime = 1; toIdle.duration = 0;
            toIdle.hasFixedDuration = true;
        }
        EditorUtility.SetDirty(controller);
        return clip;
    }
    private static AnimationClip FindClip(SpriteRenderer renderer, string name)
    {
        var controller = (AnimatorController)renderer.GetComponent<Animator>().runtimeAnimatorController;
        return (AnimationClip)controller.layers[0].stateMachine.states.Single(s => s.state.name == name).state.motion;
    }
    private static AnimatorState SetState(AnimatorStateMachine machine, string name, AnimationClip clip)
    {
        var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name) ?? machine.AddState(name);
        state.motion = clip; state.writeDefaultValues = false;
        foreach (var transition in state.transitions) state.RemoveTransition(transition);
        return state;
    }
    private static AnimationClip CreateClip(string path, Sprite[] frames, float rate, bool loop)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.frameRate = rate;
        var keys = new ObjectReferenceKeyframe[frames.Length + 1];
        for (int i = 0; i < frames.Length; i++) keys[i] = new ObjectReferenceKeyframe { time = i / rate, value = frames[i] };
        keys[frames.Length] = new ObjectReferenceKeyframe { time = frames.Length / rate, value = frames[frames.Length - 1] };
        AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop; AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }
}
