using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Tactics.Core;
using UnityEditor;
using UnityEngine;

[Serializable] public sealed class BotBatchManifest
{
    public int version = 1;
    public string startedUtc, unityVersion, contentHash, profileHash, implementationHash;
    public BotBatchConfiguration configuration;
}
[Serializable] public sealed class BotBatchSummary
{
    public string status, outputDirectory, error;
    public int scheduled, finished, victories, draws, limits, failures, cancelled;
    public double wallMilliseconds;
    public List<BotBatchGroup> groups = new List<BotBatchGroup>();
}
[Serializable] public sealed class BotBatchGroup
{
    public string map, mapIdentity, contentHash, race; public int players, seat, initialTurnOrder, appearances, wins, draws, inconclusive, failures;
    public bool buildMode, battleRoyaleMode;
}
[Serializable] public sealed class BotReplayVerification { public string expectedFinalHash, actualFinalHash; public bool identical; }

// Explicit editor job. Does not access the displayed match or player data directories.
public sealed class BotValidationBatch
{
    public static BotValidationBatch Active { get; private set; }
    public BotBatchSummary Summary { get; private set; }
    public SimulationResult Current => simulation?.Result;
    public string OutputDirectory => Summary.outputDirectory;
    public bool Running => Summary.status == "Running";
    private BotBatchConfiguration config;
    private List<MatchEngine> engines;
    private List<string> contentHashes;
    private List<MatchState> initialStates;
    private BotSimulation simulation;
    private UnitPriceTracker priceTracker;
    private readonly List<UnitPriceCaseResult> priceResults = new List<UnitPriceCaseResult>();
    private StreamWriter trace;
    private string caseDirectory, expectedFinalHash;
    private int? replayPrefixActions;
    private int index;
    private readonly Stopwatch wall = Stopwatch.StartNew();

    public static BotValidationBatch Start(BotBatchConfiguration configuration)
        => Begin(configuration, null, null);
    private static BotValidationBatch Begin(BotBatchConfiguration configuration, SimulationContentSnapshot frozen, string expected, int? replayPrefix = null)
    {
        if (Active != null && Active.Running) throw new InvalidOperationException("A validation batch is already running.");
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Stop play mode and wait for compilation before starting a batch.");
        // Own every input; editing the window/configuration cannot change an active run.
        var config = JsonUtility.FromJson<BotBatchConfiguration>(JsonUtility.ToJson(configuration));
        if (config == null || config.version != 1 || config.scenarios == null || config.scenarios.Count == 0
            || config.profile == null || config.limits == null || config.limits.maxActions < 1 || config.limits.maxRounds < 1
            || config.limits.maxPlanningMilliseconds <= 0) throw new ArgumentException("Invalid batch configuration.");
        var catalog = BotValidationScenarios.Catalog();
        var content = frozen?.Restore() ?? UnityMatchContent.Build(catalog.Races.Select(UnityMatchContent.Roster.From), out _);
        var states = config.scenarios.Select(s => BotValidationScenarios.Create(s, catalog.Races)).ToList();
        foreach (var state in states) foreach (var player in state.Players)
            foreach (var unit in player.Units) if (!content.TryUnit(unit.TypeId, out _)) throw new ArgumentException("Unknown snapshot unit: " + unit.TypeId);
        var snapshot = frozen ?? SimulationContentSnapshot.Capture(content, catalog.Races.Select(r => r.Id));
        var variants = new List<SimulationContentSnapshot>(); var hashes = new List<string>(); var caseEngines = new List<MatchEngine>();
        var enginePool = new Dictionary<string, MatchEngine>();
        for (int i = 0; i < states.Count; i++)
        {
            var scenario = config.scenarios[i];
            var variant = UnitPriceExperiments.ContentFor(snapshot, scenario);
            if (!string.IsNullOrEmpty(scenario.priceUnitId) && (scenario.pricePlayer >= states[i].Players.Count
                || states[i].Players[scenario.pricePlayer].RaceId != scenario.priceRaceId)) throw new ArgumentException("Price trial player/race mismatch.");
            string hash = Digest(JsonUtility.ToJson(variant, true));
            if (!enginePool.TryGetValue(hash, out var caseEngine)) enginePool.Add(hash, caseEngine = new MatchEngine(variant.Restore()));
            variants.Add(variant); hashes.Add(hash); caseEngines.Add(caseEngine);
        }
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string root = Path.GetFullPath(Path.IsPathRooted(config.outputDirectory) ? config.outputDirectory : Path.Combine(project, config.outputDirectory));
        string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
        string playerData = Path.GetFullPath(Application.persistentDataPath) + Path.DirectorySeparatorChar;
        if ((root + Path.DirectorySeparatorChar).StartsWith(assets, StringComparison.OrdinalIgnoreCase)
            || (root + Path.DirectorySeparatorChar).StartsWith(playerData, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a dedicated results directory outside Assets and player data.");
        string destination = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(destination);
        var batch = new BotValidationBatch { config = config, engines = caseEngines, contentHashes = hashes,
            initialStates = states, expectedFinalHash = expected, replayPrefixActions = replayPrefix,
            Summary = new BotBatchSummary { status = "Running", scheduled = states.Count, outputDirectory = destination } };
        string contentJson = JsonUtility.ToJson(snapshot, true);
        File.WriteAllText(Path.Combine(destination, "content.json"), contentJson);
        File.WriteAllText(Path.Combine(destination, "configuration.json"), JsonUtility.ToJson(config, true));
        File.WriteAllText(Path.Combine(destination, "manifest.json"), JsonUtility.ToJson(new BotBatchManifest {
            configuration = config, startedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
            contentHash = Digest(contentJson), profileHash = Digest(JsonUtility.ToJson(config.profile)), implementationHash = ImplementationHash() }, true));
        // Materialize every start before execution, including cases cancelled before they run.
        var strikeExports = new HashSet<string>();
        for (int i = 0; i < states.Count; i++)
        {
            string directory = Path.Combine(destination, "match-" + i.ToString("D4")); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "initial.json"), JsonUtility.ToJson(SimulationSnapshot.Capture(states[i]), true));
            File.WriteAllText(Path.Combine(directory, "case-manifest.json"), JsonUtility.ToJson(new UnitPriceCaseManifest { scenario = config.scenarios[i], contentHash = hashes[i] }, true));
            if (!string.IsNullOrEmpty(config.scenarios[i].priceUnitId))
            {
                File.WriteAllText(Path.Combine(directory, "content.json"), JsonUtility.ToJson(variants[i], true));
                string strikeKey = Digest(config.scenarios[i].priceUnitId + "|" + hashes[i]);
                if (strikeExports.Add(strikeKey))
                {
                    var combatContent = JsonUtility.FromJson<SimulationContentSnapshot>(JsonUtility.ToJson(variants[i]));
                    combatContent.races = snapshot.races;
                    File.WriteAllText(Path.Combine(destination, "price-strikes-" + strikeKey + ".json"),
                        JsonUtility.ToJson(new UnitPriceStrikeExport { unitId = config.scenarios[i].priceUnitId,
                            strikes = UnitPriceExperiments.Strikes(combatContent, config.scenarios[i].priceUnitId) }, true));
                }
            }
        }
        Active = batch; batch.WriteSummary();
        EditorApplication.update += batch.Tick;
        AssemblyReloadEvents.beforeAssemblyReload += batch.Cancel;
        EditorApplication.quitting += batch.Cancel;
        return batch;
    }
    public static BotValidationBatch Replay(string matchDirectory)
    {
        string root = Path.GetDirectoryName(Path.GetFullPath(matchDirectory));
        var manifest = JsonUtility.FromJson<BotBatchManifest>(File.ReadAllText(Path.Combine(root, "manifest.json")));
        string contentJson = File.ReadAllText(Path.Combine(root, "content.json"));
        if (Digest(contentJson) != manifest.contentHash) throw new InvalidOperationException("Replay content hash differs from the manifest.");
        if (Digest(JsonUtility.ToJson(manifest.configuration.profile)) != manifest.profileHash)
            throw new InvalidOperationException("Replay profile hash differs from the manifest.");
        var result = JsonUtility.FromJson<SimulationResult>(File.ReadAllText(Path.Combine(matchDirectory, "result.json")));
        var initial = JsonUtility.FromJson<SimulationSnapshot>(File.ReadAllText(Path.Combine(matchDirectory, "initial.json")));
        if (SimulationSnapshot.Fingerprint(initial.Restore()) != result.initialHash)
            throw new InvalidOperationException("Replay initial state hash differs from the result.");
        var config = manifest.configuration;
        var original = config.scenarios.Find(s => s.name == result.scenario) ?? new BotValidationScenario { map = "Snapshot" };
        string caseManifestPath = Path.Combine(matchDirectory, "case-manifest.json");
        if (File.Exists(caseManifestPath))
        {
            var caseManifest = JsonUtility.FromJson<UnitPriceCaseManifest>(File.ReadAllText(caseManifestPath));
            original = caseManifest.scenario;
            var expectedContent = UnitPriceExperiments.ContentFor(JsonUtility.FromJson<SimulationContentSnapshot>(contentJson), original);
            if (Digest(JsonUtility.ToJson(expectedContent, true)) != caseManifest.contentHash)
                throw new InvalidOperationException("Replay price treatment/content hash differs from the case manifest.");
            string caseContentPath = Path.Combine(matchDirectory, "content.json");
            if (File.Exists(caseContentPath) && Digest(File.ReadAllText(caseContentPath)) != caseManifest.contentHash)
                throw new InvalidOperationException("Replay case content file differs from its hash.");
        }
        original.name = "replay-" + result.scenario; original.initial = initial; original.useInitialState = true;
        config.scenarios = new List<BotValidationScenario> { original };
        return Begin(config, JsonUtility.FromJson<SimulationContentSnapshot>(contentJson), result.finalHash,
            result.outcome == SimulationOutcome.Cancelled ? (int?)result.actions : null);
    }
    private void Tick()
    {
        if (!Running) return;
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Cancel(); return; }
            var budget = Stopwatch.StartNew();
            while (budget.Elapsed.TotalMilliseconds < 8 && Running)
            {
                if (simulation == null)
                {
                    if (index >= initialStates.Count) { Stop("Completed"); break; }
                    caseDirectory = Path.Combine(OutputDirectory, "match-" + index.ToString("D4"));
                    trace = new StreamWriter(Path.Combine(caseDirectory, "decisions.jsonl"));
                    simulation = new BotSimulation(config.scenarios[index].name, initialStates[index], engines[index], config.profile, config.limits);
                    if (!string.IsNullOrEmpty(config.scenarios[index].priceUnitId))
                        priceTracker = new UnitPriceTracker(config.scenarios[index], initialStates[index],
                            JsonUtility.FromJson<SimulationContentSnapshot>(File.ReadAllText(Path.Combine(caseDirectory, "content.json"))).Restore(), contentHashes[index]);
                    simulation.OnDecision = d => { trace.WriteLine(JsonUtility.ToJson(d)); priceTracker?.Before(d, simulation.State); };
                }
                if (replayPrefixActions.HasValue && simulation.Result.actions >= replayPrefixActions.Value) simulation.Cancel();
                else simulation.Advance();
                priceTracker?.After(simulation.State);
                if (simulation.IsFinished) CompleteCase();
            }
        }
        catch (Exception exception)
        {
            Summary.error = exception.ToString();
            if (simulation != null) { simulation.Cancel(); CompleteCase(); }
            Stop("Failed"); UnityEngine.Debug.LogException(exception);
        }
    }
    private void CompleteCase()
    {
        trace?.Dispose(); trace = null;
        var result = simulation.Result;
        File.WriteAllText(Path.Combine(caseDirectory, "result.json"), JsonUtility.ToJson(result, true));
        File.WriteAllText(Path.Combine(caseDirectory, "final.json"), JsonUtility.ToJson(SimulationSnapshot.Capture(simulation.State), true));
        if (priceTracker != null)
        {
            priceTracker.Complete(simulation.State, result); priceResults.Add(priceTracker.Result);
            File.WriteAllText(Path.Combine(caseDirectory, "price-result.json"), JsonUtility.ToJson(priceTracker.Result, true));
            priceTracker = null;
        }
        if (expectedFinalHash != null) File.WriteAllText(Path.Combine(caseDirectory, "replay-verification.json"),
            JsonUtility.ToJson(new BotReplayVerification { expectedFinalHash = expectedFinalHash, actualFinalHash = result.finalHash,
                identical = expectedFinalHash == result.finalHash }, true));
        Summary.finished++;
        bool victory = result.outcome == SimulationOutcome.Victory, draw = result.outcome == SimulationOutcome.Draw;
        bool inconclusive = result.outcome == SimulationOutcome.LimitReached || result.outcome == SimulationOutcome.Cancelled;
        if (victory) Summary.victories++; else if (draw) Summary.draws++;
        else if (result.outcome == SimulationOutcome.LimitReached) Summary.limits++;
        else if (result.outcome == SimulationOutcome.Cancelled) Summary.cancelled++; else Summary.failures++;
        var state = initialStates[index]; var scenario = config.scenarios[index];
        string mapIdentity = MapIdentity(state);
        for (int seat = 0; seat < state.Players.Count; seat++)
        {
            int order = (seat - state.ActivePlayer + state.Players.Count) % state.Players.Count;
            string race = state.Players[seat].RaceId;
            var group = Summary.groups.FirstOrDefault(g => g.mapIdentity == mapIdentity && g.contentHash == contentHashes[index] && g.race == race && g.seat == seat && g.initialTurnOrder == order
                && g.players == state.Players.Count && g.buildMode == state.BuildMode && g.battleRoyaleMode == state.BattleRoyaleMode);
            if (group == null) { group = new BotBatchGroup { map = scenario.map, mapIdentity = mapIdentity, contentHash = contentHashes[index], race = race, seat = seat, initialTurnOrder = order,
                players = state.Players.Count, buildMode = state.BuildMode, battleRoyaleMode = state.BattleRoyaleMode }; Summary.groups.Add(group); }
            group.appearances++; if (victory && result.winner == seat) group.wins++; if (draw) group.draws++;
            if (inconclusive) group.inconclusive++; if (!victory && !draw && !inconclusive) group.failures++;
        }
        simulation.Dispose(); simulation = null; index++; WriteSummary();
    }
    public void Cancel()
    {
        if (!Running) return;
        if (simulation != null) { simulation.Cancel(); CompleteCase(); }
        Stop("Cancelled");
    }
    private void Stop(string status)
    {
        EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= Cancel; EditorApplication.quitting -= Cancel;
        trace?.Dispose(); trace = null; Summary.status = status; wall.Stop(); WriteSummary();
    }
    private void WriteSummary()
    {
        Summary.wallMilliseconds = wall.Elapsed.TotalMilliseconds;
        File.WriteAllText(Path.Combine(OutputDirectory, "summary.json"), JsonUtility.ToJson(Summary, true));
        if (priceResults.Count > 0) File.WriteAllText(Path.Combine(OutputDirectory, "price-summary.json"), JsonUtility.ToJson(UnitPriceSummary.From(priceResults), true));
    }
    private static string Digest(string text)
    { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
    private static string MapIdentity(MatchState state)
    {
        var text = new StringBuilder(); text.Append(state.Width).Append(',').Append(state.Height);
        foreach (var cell in state.Cells) text.Append('|').Append((int)cell.Terrain).Append(',').Append(cell.MovementCost)
            .Append(',').Append(cell.SupportsCastle).Append(',').Append(cell.HasCastle).Append(',').Append(cell.CastleOwner).Append(',').Append(cell.HasStorm);
        foreach (var player in state.Players)
        {
            var leader = player.Units.Find(u => u.IsLeader);
            text.Append('|').Append(leader?.Position.X).Append(',').Append(leader?.Position.Y);
        }
        return Digest(text.ToString());
    }
    private static string ImplementationHash()
    {
        var paths = Directory.GetFiles(Path.Combine(Application.dataPath, "Scripts", "Core"), "*.cs")
            .Concat(new[] { Path.Combine(Application.dataPath, "Scripts", "UnityMatchContent.cs") })
            .Concat(Directory.GetFiles(Path.Combine(Application.dataPath, "Editor"), "BotValidation*.cs"))
            .Concat(Directory.GetFiles(Path.Combine(Application.dataPath, "Editor"), "UnitPrice*.cs"))
            .OrderBy(p => p, StringComparer.Ordinal);
        return Digest(string.Join("\n", paths.Select(p => Path.GetFileName(p) + "\n" + File.ReadAllText(p))));
    }
}
