using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class BotValidationWindow : EditorWindow
{
    private string configuration, error;
    private Vector2 scroll;
    [MenuItem("Tactics/Bots/Validation Batch")]
    public static void Open() => GetWindow<BotValidationWindow>("Bot Validation");
    private void OnEnable() { if (string.IsNullOrEmpty(configuration)) Load(BotValidationScenarios.Smoke()); }
    private void Load(BotBatchConfiguration config) => configuration = JsonUtility.ToJson(config, true);
    private void OnInspectorUpdate() => Repaint();
    private void OnGUI()
    {
        EditorGUILayout.LabelField("Reproducible bot validation", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Explicit isolated core simulations. No displayed match, saves or preferences are modified. Safety limits are inconclusive. Seat and turn order are correlated in normal starts.", MessageType.Info);
        var batch = BotValidationBatch.Active;
        if (batch != null)
        {
            EditorGUILayout.LabelField($"{batch.Summary.status}: {batch.Summary.finished}/{batch.Summary.scheduled}");
            if (batch.Current != null) EditorGUILayout.LabelField($"{batch.Current.scenario}: {batch.Current.actions} actions");
            EditorGUILayout.SelectableLabel(batch.OutputDirectory, GUILayout.Height(35));
            if (batch.Running && GUILayout.Button("Cancel and preserve diagnostics")) batch.Cancel();
        }
        using (new EditorGUI.DisabledScope(batch != null && batch.Running))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("12-game smoke configuration")) Load(BotValidationScenarios.Smoke());
            if (GUILayout.Button("Full reference matrix")) Load(BotValidationScenarios.Reference());
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Individual troop prices")) Try(() => Load(UnitPriceExperiments.Recruitment()));
            if (GUILayout.Button("Individual prices: equal armies")) Try(() => Load(UnitPriceExperiments.EqualBudget()));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Load JSON")) Try(() => { string path = EditorUtility.OpenFilePanel("Batch configuration", "", "json"); if (path.Length > 0) configuration = File.ReadAllText(path); });
            if (GUILayout.Button("Save JSON")) Try(() => { string path = EditorUtility.SaveFilePanel("Batch configuration", "", "bot-batch.json", "json"); if (path.Length > 0) File.WriteAllText(path, configuration); });
            if (GUILayout.Button("Replay match directory")) Try(() => { string path = EditorUtility.OpenFolderPanel("Match directory containing initial.json/result.json", "", ""); if (path.Length > 0) BotValidationBatch.Replay(path); });
            EditorGUILayout.EndHorizontal();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            configuration = EditorGUILayout.TextArea(configuration, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Start batch")) Try(() => BotValidationBatch.Start(JsonUtility.FromJson<BotBatchConfiguration>(configuration)));
        }
        if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
    }
    private void Try(Action action) { try { action(); error = null; } catch (Exception exception) { error = exception.Message; } }
}
