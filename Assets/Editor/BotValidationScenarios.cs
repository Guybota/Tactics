using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tactics.Core;
using UnityEditor;
using UnityEngine;

[Serializable] public sealed class BotBatchConfiguration
{
    public int version = 1;
    public string outputDirectory = "BotValidationResults";
    public SimulationLimits limits = new SimulationLimits();
    public BotProfile profile = BotProfile.Normal;
    public List<BotValidationScenario> scenarios = new List<BotValidationScenario>();
}
[Serializable] public sealed class BotValidationScenario
{
    public string name, map = "Open", customMapPath;
    public string[] races;
    public int width = 10, height = 10, terrainSeed = 8675309, startingMoney = 1000;
    public uint luckSeed = 12345;
    public bool buildMode, battleRoyaleMode, assignStartingCastles;
    public bool useInitialState;
    // Diagnostic treatment only; an empty ID leaves the real content unchanged.
    public string priceUnitId, priceUnitName, priceRaceId, pricePairId, priceOpponentUnitId;
    public int priceCost = -1, priceBaselineCost = -1, pricePlayer = -1, priceArmyBudget;
    public bool priceFocusRecruitment;
    // Optional exact state, already at the beginning of the first decision.
    public SimulationSnapshot initial;
}

public static class BotValidationScenarios
{
    public static RaceCatalog Catalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<RaceCatalog>("Assets/Data/RaceCatalog.asset");
        if (catalog == null) throw new InvalidOperationException("Race catalog is missing.");
        catalog.Validate(); return catalog;
    }
    public static BotBatchConfiguration Smoke()
    {
        var ids = Catalog().Races.Select(r => r.Id).ToArray();
        var config = new BotBatchConfiguration();
        for (int players = 2; players <= 4; players++) for (int mode = 0; mode < 4; mode++)
        {
            var scenario = new BotValidationScenario { name = $"smoke-p{players}-mode{mode}",
                buildMode = (mode & 1) != 0, battleRoyaleMode = (mode & 2) != 0,
                map = new[] { "Open", "Forest", "Asymmetric", "Generated" }[mode],
                races = Enumerable.Range(0, players).Select(p => ids[(p + players - 2) % ids.Length]).ToArray() };
            config.scenarios.Add(scenario);
        }
        return config;
    }
    public static BotBatchConfiguration Reference()
    {
        var ids = Catalog().Races.Select(r => r.Id).ToArray();
        var config = new BotBatchConfiguration();
        foreach (var basis in Smoke().scenarios) foreach (var map in new[] { "Symmetric", "Forest", "Asymmetric", "Generated" })
        {
            // Identical races isolate seat/order effects; rotations reduce race/seat confusion.
            for (int variant = 0; variant < ids.Length * 2; variant++) foreach (uint seed in new uint[] { 12345, 67890 })
            {
                config.scenarios.Add(new BotValidationScenario { name = $"{basis.name}-{map}-v{variant}-luck{seed}",
                    buildMode = basis.buildMode, battleRoyaleMode = basis.battleRoyaleMode, map = map, luckSeed = seed,
                    races = Enumerable.Range(0, basis.races.Length).Select(p => ids[variant < ids.Length ? variant : (p + variant - ids.Length) % ids.Length]).ToArray() });
            }
        }
        return config;
    }
    public static MatchState Create(BotValidationScenario scenario, IReadOnlyList<RaceDefinition> races)
    {
        // JsonUtility materializes null inline classes as empty instances on round-trip.
        if (scenario.useInitialState) return scenario.initial.Restore();
        if (scenario.races == null || scenario.races.Length < 2 || scenario.races.Length > 4 || scenario.startingMoney < 0)
            throw new ArgumentException("Scenario requires 2–4 race IDs and nonnegative starting money.");
        var starts = new List<GridPosition>();
        CustomMapData custom = null;
        if (scenario.map == "Custom")
        {
            custom = JsonUtility.FromJson<CustomMapData>(File.ReadAllText(scenario.customMapPath));
            CustomMapValidator.ValidatePlayable(custom);
            if (custom.playerCount != scenario.races.Length) throw new ArgumentException("Custom map participant count differs from the scenario.");
        }
        int width = custom?.width ?? scenario.width, height = custom?.height ?? scenario.height;
        if (width < 10 || height < 10) throw new ArgumentException("Reference map dimensions must be at least 10.");
        var state = new MatchState(width, height) { BuildMode = scenario.buildMode, BattleRoyaleMode = scenario.battleRoyaleMode, RandomState = scenario.luckSeed };
        for (int p = 0; p < scenario.races.Length; p++)
            starts.Add(custom != null ? new GridPosition(custom.StartFor(p).x, custom.StartFor(p).y)
                : new GridPosition(p == 1 || p == 3 ? width - 1 : 0, p == 1 || p == 2 ? height - 3 : 2));
        if (scenario.map == "Generated")
        {
            var cells = GeneratedMapLayout.Create(width, height, state.BuildMode, new System.Random(scenario.terrainSeed));
            for (int x = 0; x < width; x++) for (int y = 0; y < height; y++) state.Cells[x, y] = cells[x, y];
            // Live SpawnLeader replaces a mountain under a start with grass.
            foreach (var p in starts) if (state.Cells[p.X, p.Y].Terrain == TerrainKind.Mountain) state.Cells[p.X, p.Y] = new CellState();
        }
        else if (custom != null)
        {
            for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
            {
                var cell = custom.Cell(x, y);
                state.Cells[x, y] = new CellState { Terrain = cell.terrain, HasCastle = cell.castle, CastleOwner = cell.castleOwner };
            }
        }
        else
        {
            if (scenario.map != "Open" && scenario.map != "Symmetric" && scenario.map != "Forest" && scenario.map != "Asymmetric") throw new ArgumentException("Unknown map variant.");
            for (int x = 1; x < width - 1; x++) for (int y = 1; y < height - 1; y++)
            {
                if (scenario.map == "Forest" && (x == width / 2 || x == width - 1 - width / 2) && y != height / 2 && y != height - 1 - height / 2)
                    state.Cells[x, y].Terrain = TerrainKind.Forest;
                if (scenario.map == "Asymmetric" && x == width / 3 && y > 2 && y < height - 2) state.Cells[x, y].Terrain = TerrainKind.Mountain;
            }
            if (!state.BuildMode)
            {
                foreach (var start in starts)
                { var castle = state.Cells[start.X == 0 ? 1 : width - 2, start.Y]; castle.HasCastle = true; }
                state.Cells[width / 2, height / 2].HasCastle = true;
                if (scenario.map == "Asymmetric") state.Cells[width - 3, height / 2].HasCastle = true;
                if (scenario.map == "Symmetric")
                {
                    // Castle geometry is invariant under 180 degrees, including 3-player maps.
                    // Starts still match the live game's seats; three occupied corners differ in access.
                    foreach (int x in new[] { 1, width - 2 }) foreach (int y in new[] { 2, height - 3 }) state.Cells[x, y].HasCastle = true;
                    foreach (int x in new[] { (width - 1) / 2, width / 2 }) foreach (int y in new[] { (height - 1) / 2, height / 2 }) state.Cells[x, y].HasCastle = true;
                }
            }
        }
        var tiles = new Dictionary<TerrainKind, Tile> {
            { TerrainKind.Grass, AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/grass_tile.prefab").GetComponent<Tile>() },
            { TerrainKind.Forest, AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/forest_tile.prefab").GetComponent<Tile>() },
            { TerrainKind.Mountain, AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/mountain_tile.prefab").GetComponent<Tile>() } };
        foreach (var cell in state.Cells) { cell.MovementCost = Math.Max(1, tiles[cell.Terrain].movement_cost); cell.SupportsCastle = tiles[cell.Terrain].castle != null; }
        if (scenario.assignStartingCastles)
            for (int p = 0; p < starts.Count; p++)
            {
                var castle = state.Cells[starts[p].X, starts[p].Y];
                castle.Terrain = TerrainKind.Grass; castle.MovementCost = Math.Max(1, tiles[TerrainKind.Grass].movement_cost);
                castle.SupportsCastle = tiles[TerrainKind.Grass].castle != null; castle.HasCastle = true; castle.CastleOwner = p;
            }
        for (int p = 0; p < starts.Count; p++)
        {
            var race = races.FirstOrDefault(r => r.Id == scenario.races[p]) ?? throw new ArgumentException("Unknown race: " + scenario.races[p]);
            var unit = race.Leader.GetComponent<Unit>();
            var player = new PlayerState { RaceId = race.Id, Money = state.BuildMode ? 4000 : scenario.startingMoney, NextUnitId = 2 };
            player.Units.Add(new UnitState { Id = 1, TypeId = UnityMatchAdapter.TypeId(unit), Position = starts[p],
                MaxHealth = LeaderStats.Health(unit.max_health), Health = LeaderStats.Health(unit.hp), Attack = LeaderStats.Attack(unit.attack),
                Movement = unit.movement, Range = unit.attack_range, IsLeader = true });
            state.Players.Add(player);
        }
        // Validate connectivity with the same authoring validator before accepting a batch map.
        var check = new CustomMapData { id = new string('0', 32), name = "Validation", width = width, height = height, playerCount = starts.Count };
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) check.cells.Add(new MapCellData { terrain = state.Cells[x, y].Terrain, castle = state.Cells[x, y].HasCastle, castleOwner = state.Cells[x, y].CastleOwner });
        for (int p = 0; p < starts.Count; p++) check.starts.Add(new MapStartData { slot = p, x = starts[p].X, y = starts[p].Y });
        CustomMapValidator.ValidatePlayable(check);
        MatchEngine.BeginTurn(state);
        return state;
    }
}
