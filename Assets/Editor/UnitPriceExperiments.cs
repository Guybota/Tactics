using System;
using System.Collections.Generic;
using System.Linq;
using Tactics.Core;
using UnityEngine;

[Serializable] public sealed class UnitPriceSettings
{
    public float[] multipliers = { .9f, 1f, 1.1f };
    public uint[] luckSeeds = { 12345 };
    public string[] maps = { "Symmetric" };
    public string[] unitIds = Array.Empty<string>();
    public bool focusRecruitment = true;
    public int armyBudget;
}
[Serializable] public sealed class UnitPriceCaseManifest { public string contentHash; public BotValidationScenario scenario; }
[Serializable] public sealed class UnitPriceStrikeExport { public string unitId; public List<UnitPriceStrike> strikes; }
[Serializable] public sealed class UnitPriceStrike
{
    public string attacker, defender; public int distance, defenseStars, cost, defenderCost;
    public float minDamage, maxDamage, minCounterDamage, maxCounterDamage;
    public float minEnemyGoldDamagePerGold, maxEnemyGoldDamagePerGold;
    public bool damageCanBeFullyHealedOnOwnedCastle;
}

public static class UnitPriceExperiments
{
    public static BotBatchConfiguration Recruitment() => Create(new UnitPriceSettings());
    public static BotBatchConfiguration EqualBudget() => Create(new UnitPriceSettings { armyBudget = 3000 });
    public static BotBatchConfiguration Create(UnitPriceSettings settings)
    {
        if (settings == null || settings.multipliers == null || settings.multipliers.Length == 0
            || settings.multipliers.Any(m => m <= 0 || float.IsNaN(m) || float.IsInfinity(m))
            || settings.luckSeeds == null || settings.luckSeeds.Length == 0 || settings.maps == null || settings.maps.Length == 0
            || settings.armyBudget < 0) throw new ArgumentException("Invalid price experiment settings.");
        var catalog = BotValidationScenarios.Catalog();
        var content = UnityMatchContent.Build(catalog.Races.Select(UnityMatchContent.Roster.From), out _);
        var config = new BotBatchConfiguration();
        var wanted = new HashSet<string>(settings.unitIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var found = new HashSet<string>();
        foreach (var race in catalog.Races) foreach (var prefab in race.Units)
        {
            var unit = prefab.GetComponent<Unit>(); string id = UnityMatchAdapter.TypeId(unit);
            if (unit.is_king || prefab == race.Leader || !content.CanRecruit(race.Id, id) || (wanted.Count > 0 && !wanted.Contains(id))) continue;
            found.Add(id);
            int baseline = content.Unit(id).Cost;
            if (baseline <= 0) throw new ArgumentException("Price trials require a positive recruit cost: " + id);
            var costs = settings.multipliers.Select(m => CheckedCost(baseline, m)).Concat(new[] { baseline }).Distinct().OrderBy(c => c).ToArray();
            foreach (var opponent in catalog.Races.Where(r => r.Id != race.Id))
            {
                // An explicit affordable reference troop for precomposed armies.
                var opponentType = opponent.Units.Select(p => p.GetComponent<Unit>())
                    .Where(u => content.CanRecruit(opponent.Id, UnityMatchAdapter.TypeId(u)) && content.Unit(UnityMatchAdapter.TypeId(u)).Cost > 0)
                    .OrderBy(u => content.Unit(UnityMatchAdapter.TypeId(u)).Cost).ThenBy(u => UnityMatchAdapter.TypeId(u), StringComparer.Ordinal).First();
                foreach (var map in settings.maps.Distinct()) foreach (uint seed in settings.luckSeeds.Distinct()) for (int seat = 0; seat < 2; seat++)
                {
                    string pair = $"price-{id}-vs-{opponent.Id}-seat{seat}-{map}-luck{seed}-focus{settings.focusRecruitment}-budget{settings.armyBudget}";
                    foreach (int cost in costs)
                    {
                        var scenario = new BotValidationScenario { name = pair + "-cost" + cost, map = map, luckSeed = seed,
                            races = seat == 0 ? new[] { race.Id, opponent.Id } : new[] { opponent.Id, race.Id },
                            priceUnitId = id, priceUnitName = unit.unit_name, priceRaceId = race.Id, pricePlayer = seat,
                            priceCost = cost, priceBaselineCost = baseline, pricePairId = pair,
                            priceFocusRecruitment = settings.focusRecruitment, priceArmyBudget = settings.armyBudget,
                            priceOpponentUnitId = UnityMatchAdapter.TypeId(opponentType) };
                        // Ordinary recruitment trials have identical full starts across prices.
                        // Equal-budget trials deliberately change the affordable army and unspent gold.
                        if (settings.armyBudget > 0)
                        {
                            var state = BotValidationScenarios.Create(scenario, catalog.Races);
                            var candidate = ContentFor(SimulationContentSnapshot.Capture(content, catalog.Races.Select(r => r.Id)), scenario).Restore();
                            AddBudgetArmy(state, seat, candidate.Unit(id), settings.armyBudget);
                            AddBudgetArmy(state, 1 - seat, candidate.Unit(scenario.priceOpponentUnitId), settings.armyBudget);
                            scenario.initial = SimulationSnapshot.Capture(state); scenario.useInitialState = true;
                        }
                        config.scenarios.Add(scenario);
                    }
                }
            }
        }
        if (wanted.Except(found).Any()) throw new ArgumentException("Unknown or non-recruitable trial unit ID.");
        return config;
    }
    private static int CheckedCost(int baseline, float multiplier)
    {
        double value = Math.Round(baseline * (double)multiplier, MidpointRounding.AwayFromZero);
        if (value < 1 || value > int.MaxValue) throw new ArgumentException("Trial price must be a positive integer.");
        return (int)value;
    }
    public static SimulationContentSnapshot ContentFor(SimulationContentSnapshot baseline, BotValidationScenario scenario)
    {
        // Never mutate the captured baseline, prefabs, definitions or damage table.
        var copy = JsonUtility.FromJson<SimulationContentSnapshot>(JsonUtility.ToJson(baseline));
        if (string.IsNullOrEmpty(scenario.priceUnitId)) return copy;
        var type = copy.units.Find(u => u.id == scenario.priceUnitId);
        var race = copy.races.Find(r => r.id == scenario.priceRaceId);
        if (type == null || race == null || !race.recruits.Contains(type.id) || type.cost != scenario.priceBaselineCost
            || scenario.priceCost < 1 || scenario.pricePlayer < 0 || scenario.pricePlayer > 3 || string.IsNullOrEmpty(scenario.pricePairId))
            throw new ArgumentException("Invalid individual price treatment.");
        type.cost = scenario.priceCost;
        if (scenario.priceFocusRecruitment) race.recruits = new List<string> { type.id };
        copy.Restore().Validate(); return copy;
    }
    private static void AddBudgetArmy(MatchState state, int seat, UnitTypeStats type, int budget)
    {
        int count = budget / type.Cost;
        if (count < 1) throw new ArgumentException("Army budget cannot afford the selected troop.");
        var player = state.Players[seat]; var origin = player.Units.Find(u => u.IsLeader).Position;
        var spaces = new List<GridPosition>();
        for (int x = 0; x < state.Width; x++) for (int y = 0; y < state.Height; y++)
        {
            var p = new GridPosition(x, y);
            // Keep each army in its own half and avoid every participant's occupied cell.
            if ((origin.X < state.Width / 2 ? x >= state.Width / 2 : x < state.Width / 2)
                || state.Cells[x, y].Terrain == TerrainKind.Mountain || state.UnitAt(p) != null) continue;
            spaces.Add(p);
        }
        if (count > spaces.Count) throw new ArgumentException("Army budget requires more deployment cells than the map supports.");
        foreach (var p in spaces.OrderBy(p => p.DistanceTo(origin)).ThenBy(p => p.X).ThenBy(p => p.Y).Take(count))
            player.Units.Add(new UnitState { Id = player.NextUnitId++, TypeId = type.Id, Position = p,
                Health = type.Health, MaxHealth = type.Health, Attack = type.Attack, Movement = type.Movement, Range = type.Range });
        player.Money = checked(player.Money + budget - count * type.Cost);
    }
    public static List<UnitPriceStrike> Strikes(SimulationContentSnapshot content, string id)
    {
        var engine = new MatchEngine(content.Restore()); var attacker = content.units.Find(u => u.id == id);
        if (attacker == null) throw new ArgumentException("Unknown price trial unit.");
        var ids = new HashSet<string>(content.races.SelectMany(r => r.recruits));
        var result = new List<UnitPriceStrike>();
        foreach (var defender in content.units.Where(u => ids.Contains(u.id))) foreach (int stars in new[] { 1, 2, 3 })
            foreach (int distance in new[] { 1, attacker.range }.Distinct())
            {
                var state = new MatchState(Math.Max(10, distance + 3), 10);
                var a = new PlayerState { RaceId = content.races[0].id }; var d = new PlayerState { RaceId = content.races[0].id };
                a.Units.Add(CombatUnit(attacker, new GridPosition(1, 1))); d.Units.Add(CombatUnit(defender, new GridPosition(1 + distance, 1)));
                state.Players.Add(a); state.Players.Add(d);
                foreach (var unit in new[] { a.Units[0], d.Units[0] })
                {
                    var cell = state.Cells[unit.Position.X, unit.Position.Y];
                    cell.Terrain = stars == 2 ? TerrainKind.Forest : TerrainKind.Grass; cell.HasCastle = stars == 3;
                }
                var prediction = engine.Predict(state, new UnitKey(0, 1), new UnitKey(1, 1), a.Units[0].Position);
                result.Add(new UnitPriceStrike { attacker = id, defender = defender.id, distance = distance, defenseStars = stars,
                    cost = attacker.cost, defenderCost = defender.cost, minDamage = prediction.MinDamage, maxDamage = prediction.MaxDamage,
                    minCounterDamage = prediction.MinCounterDamage, maxCounterDamage = prediction.MaxCounterDamage,
                    minEnemyGoldDamagePerGold = prediction.MinDamage / defender.health * defender.cost / Math.Max(1, attacker.cost),
                    maxEnemyGoldDamagePerGold = prediction.MaxDamage / defender.health * defender.cost / Math.Max(1, attacker.cost),
                    damageCanBeFullyHealedOnOwnedCastle = stars == 3 && prediction.MaxDamage <= defender.health * .3f });
            }
        return result;
    }
    private static UnitState CombatUnit(SimulationType type, GridPosition position) => new UnitState { Id = 1, TypeId = type.id,
        Position = position, Health = type.health, MaxHealth = type.health, Attack = type.attack, Range = type.range, Movement = type.movement };
}
