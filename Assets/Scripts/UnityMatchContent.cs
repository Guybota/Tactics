using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Tactics.Core;

// A single content boundary for live matches and isolated editor simulations.
public static class UnityMatchContent
{
    public sealed class Roster
    {
        public string Id;
        public GameObject Leader;
        public IEnumerable<GameObject> Units;
        public static Roster From(RaceClass race) => new Roster { Id = race.Id, Leader = race.Leader, Units = race.AvailableUnits };
        public static Roster From(RaceDefinition race) => new Roster { Id = race.Id, Leader = race.Leader, Units = race.Units };
    }

    public static MatchContent Build(IEnumerable<Roster> races, out Dictionary<string, GameObject> prefabs)
    {
        prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        var content = new MatchContent();
        foreach (var race in races)
        {
            var units = race.Units.ToArray();
            foreach (var prefab in units.Concat(new[] { race.Leader }).Distinct())
            {
                var unit = prefab.GetComponent<Unit>();
                string id = UnityMatchAdapter.TypeId(unit);
                if (prefabs.ContainsKey(id)) continue;
                prefabs.Add(id, prefab);
                var profile = unit.CombatDefinition;
                content.AddUnit(new UnitTypeStats(id, profile != null ? profile.BaselineHealth : unit.max_health,
                    profile != null ? profile.BaselineAttack : unit.attack, unit.movement, unit.attack_range, unit.unit_cost));
            }
            content.AddRace(race.Id, units.Where(p => !p.GetComponent<Unit>().is_king).Select(p => UnityMatchAdapter.TypeId(p.GetComponent<Unit>())));
        }
        foreach (var attacker in content.UnitTypes) foreach (var defender in content.UnitTypes)
            content.SetDamage(attacker.Id, defender.Id, prefabs[attacker.Id].GetComponent<Unit>().BaselineDamageAgainst(prefabs[defender.Id].GetComponent<Unit>()));
        content.Validate();
        return content;
    }
}
