using System;
using System.Collections.Generic;
using System.Linq;
using Tactics.Core;

[Serializable] public sealed class UnitPriceCaseResult
{
    public string scenario, unitId, unitName, race, pairId, initialHash, contentHash;
    public int baselineCost, cost, player, armyBudget, initialTroops, recruited, spentGold, firstRecruitRound = -1;
    public int attacks, captures, unitKills, leaderKills, losses, survivors;
    public float damageDealt, damageReceived, enemyGoldDamage;
    public bool focusRecruitment;
    public SimulationOutcome outcome;
    public int winner;
    public bool UnitPresent => initialTroops + recruited > 0;
}
[Serializable] public sealed class UnitPriceRow
{
    public string unitId, unitName, race;
    public int baselineCost, cost, armyBudget, games, wins, losses, draws, inconclusive, failures, casesWithoutUnit;
    public int recruited, initialTroops, spentGold, captures, attacks, unitKills, leaderKills, troopLosses;
    public int pairedCompletedGames, pairedWinsDelta, pairedUnitCountDelta, pairedWithoutUnit;
    public int gamesWithRecruitment;
    public float meanFirstRecruitRound, damageDealt, damageReceived, enemyGoldDamage;
    public bool focusRecruitment;
}
[Serializable] public sealed class UnitPriceSummary
{
    public string interpretation = "Experimental comparisons, not approved prices. Wins depend on map, roster, leader and bot. Inconclusive games are separate; zero usage is not evidence of fair cost. Damage valued by gold is a heuristic and can include repeated healed damage.";
    public List<UnitPriceRow> rows = new List<UnitPriceRow>();
    public static UnitPriceSummary From(IEnumerable<UnitPriceCaseResult> results)
    {
        var cases = results.ToList(); var report = new UnitPriceSummary();
        foreach (var group in cases.GroupBy(c => new { c.unitId, c.race, c.baselineCost, c.cost, c.armyBudget, c.focusRecruitment }))
        {
            var first = group.First();
            var row = new UnitPriceRow { unitId = first.unitId, unitName = first.unitName, race = first.race,
                baselineCost = first.baselineCost, cost = first.cost, armyBudget = first.armyBudget, focusRecruitment = first.focusRecruitment };
            foreach (var c in group)
            {
                row.games++;
                bool completed = Complete(c);
                if (c.outcome == SimulationOutcome.Draw) row.draws++;
                else if (c.outcome == SimulationOutcome.Victory) { if (c.winner == c.player) row.wins++; else row.losses++; }
                else if (c.outcome == SimulationOutcome.LimitReached || c.outcome == SimulationOutcome.Cancelled) row.inconclusive++;
                else row.failures++;
                if (!c.UnitPresent) row.casesWithoutUnit++;
                row.recruited += c.recruited; row.initialTroops += c.initialTroops; row.spentGold += c.spentGold;
                row.captures += c.captures; row.attacks += c.attacks; row.unitKills += c.unitKills;
                row.leaderKills += c.leaderKills; row.troopLosses += c.losses;
                row.damageDealt += c.damageDealt; row.damageReceived += c.damageReceived; row.enemyGoldDamage += c.enemyGoldDamage;
                if (c.firstRecruitRound >= 0) { row.gamesWithRecruitment++; row.meanFirstRecruitRound += c.firstRecruitRound; }
                if (c.cost == c.baselineCost) continue;
                var baseline = cases.Find(b => b.pairId == c.pairId && b.cost == b.baselineCost);
                if (baseline == null || !completed || !Complete(baseline)) continue;
                // Exact starts are required for recruitment pairs. Budget armies change by treatment.
                if (c.armyBudget == 0 && baseline.initialHash != c.initialHash) continue;
                row.pairedCompletedGames++;
                row.pairedWinsDelta += Win(c) - Win(baseline);
                row.pairedUnitCountDelta += c.recruited + c.initialTroops - baseline.recruited - baseline.initialTroops;
                if (!c.UnitPresent || !baseline.UnitPresent) row.pairedWithoutUnit++;
            }
            if (row.gamesWithRecruitment > 0) row.meanFirstRecruitRound /= row.gamesWithRecruitment;
            else row.meanFirstRecruitRound = -1;
            report.rows.Add(row);
        }
        return report;
    }
    private static bool Complete(UnitPriceCaseResult c) => c.outcome == SimulationOutcome.Victory || c.outcome == SimulationOutcome.Draw;
    private static int Win(UnitPriceCaseResult c) => c.outcome == SimulationOutcome.Victory && c.winner == c.player ? 1 : 0;
}

// Editor diagnostics observe the committed before/after states; no additional actions or luck draws.
public sealed class UnitPriceTracker
{
    public UnitPriceCaseResult Result { get; }
    private readonly MatchContent content;
    private SimulationActionRecord pending;
    private UnitState actor, target;
    private List<int> aliveBefore;
    public UnitPriceTracker(BotValidationScenario scenario, MatchState initial, MatchContent content, string contentHash)
    {
        this.content = content;
        Result = new UnitPriceCaseResult { scenario = scenario.name, unitId = scenario.priceUnitId, unitName = scenario.priceUnitName,
            race = scenario.priceRaceId, pairId = scenario.pricePairId, baselineCost = scenario.priceBaselineCost,
            cost = scenario.priceCost, player = scenario.pricePlayer, focusRecruitment = scenario.priceFocusRecruitment,
            armyBudget = scenario.priceArmyBudget, contentHash = contentHash, initialHash = SimulationSnapshot.Fingerprint(initial) };
        Result.initialTroops = initial.Players[scenario.pricePlayer].Units.Count(Tracked);
    }
    private bool Tracked(UnitState unit) => unit != null && unit.IsAlive && !unit.IsLeader && unit.TypeId == Result.unitId;
    public void Before(SimulationActionRecord record, MatchState state)
    {
        if (!record.accepted) return;
        pending = record; actor = state.FindUnit(new UnitKey(record.player, record.actorId))?.Clone();
        target = record.kind == MatchActionKind.Attack ? state.FindUnit(new UnitKey(record.targetPlayer, record.targetId))?.Clone() : null;
        aliveBefore = state.Players[Result.player].Units.Where(Tracked).Select(u => u.Id).ToList();
    }
    public void After(MatchState state)
    {
        if (pending == null) return;
        var record = pending; pending = null;
        var afterActor = actor == null ? null : state.FindUnit(new UnitKey(record.player, actor.Id));
        var afterTarget = target == null ? null : state.FindUnit(new UnitKey(record.targetPlayer, target.Id));
        bool ours = record.player == Result.player && Tracked(actor);
        if (record.kind == MatchActionKind.Recruit && record.player == Result.player && record.recruitTypeId == Result.unitId
            && state.Players[Result.player].Units.Count(Tracked) > aliveBefore.Count)
        {
            Result.recruited++; Result.spentGold = checked(Result.spentGold + Result.cost);
            if (Result.firstRecruitRound < 0) Result.firstRecruitRound = record.round;
        }
        if (ours && record.kind == MatchActionKind.Capture) Result.captures++;
        if (record.kind == MatchActionKind.Attack && actor != null && target != null)
        {
            if (ours)
            {
                Result.attacks++; Result.damageReceived += Damage(actor, afterActor);
                Dealt(target, afterTarget);
            }
            if (record.targetPlayer == Result.player && Tracked(target))
            {
                Result.damageReceived += Damage(target, afterTarget); Dealt(actor, afterActor);
            }
        }
        Result.losses += aliveBefore.Count(id => state.FindUnit(new UnitKey(Result.player, id))?.IsAlive != true);
    }
    private static float Damage(UnitState before, UnitState after) => Math.Max(0, before.Health - (after?.Health ?? 0));
    private void Dealt(UnitState before, UnitState after)
    {
        float damage = Damage(before, after); Result.damageDealt += damage;
        if (!before.IsLeader) Result.enemyGoldDamage += damage / before.MaxHealth * content.Unit(before.TypeId).Cost;
        if (before.IsAlive && after?.IsAlive != true)
        { if (before.IsLeader) Result.leaderKills++; else Result.unitKills++; }
    }
    public void Complete(MatchState state, SimulationResult result)
    {
        Result.outcome = result.outcome; Result.winner = result.winner;
        Result.survivors = state.Players[Result.player].Units.Count(Tracked);
    }
}
