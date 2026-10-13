using System;
using System.Collections.Generic;

namespace Tactics.Core
{
    public sealed class UnitTypeStats
    {
        public readonly string Id;
        public readonly float Health;
        public readonly int Attack, Movement, Range, Cost;
        public UnitTypeStats(string id, float health, int attack, int movement, int range, int cost)
        {
            if (string.IsNullOrWhiteSpace(id) || health <= 0 || float.IsNaN(health) || float.IsInfinity(health)
                || attack < 0 || movement < 0 || range < 1 || cost < 0) throw new ArgumentException("Invalid unit type.");
            Id = id; Health = health; Attack = attack; Movement = movement; Range = range; Cost = cost;
        }
    }

    public sealed class MatchContent
    {
        private readonly Dictionary<string, UnitTypeStats> units = new Dictionary<string, UnitTypeStats>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> recruits = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, float>> damage = new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);
        public IEnumerable<UnitTypeStats> UnitTypes => units.Values;
        public IEnumerable<string> RaceIds => new List<string>(recruits.Keys).AsReadOnly();
        public IEnumerable<string> Recruits(string race)
            => new List<string>(recruits[race]).AsReadOnly();
        public float DamagePercent(string attacker, string defender) => damage[attacker][defender];
        public void AddUnit(UnitTypeStats unit) => units.Add(unit.Id, unit);
        public UnitTypeStats Unit(string id) => units[id];
        public bool TryUnit(string id, out UnitTypeStats unit) { unit = null; return id != null && units.TryGetValue(id, out unit); }
        public void AddRace(string id, IEnumerable<string> allowedRecruits) => recruits.Add(id, new HashSet<string>(allowedRecruits, StringComparer.Ordinal));
        public bool CanRecruit(string race, string unit) => race != null && unit != null && recruits.TryGetValue(race, out var allowed) && allowed.Contains(unit);
        public void SetDamage(string attacker, string defender, float percent)
        {
            if (!units.ContainsKey(attacker) || !units.ContainsKey(defender) || percent < 0 || float.IsNaN(percent) || float.IsInfinity(percent))
                throw new ArgumentException("Invalid damage matchup.");
            if (!damage.TryGetValue(attacker, out var row)) damage.Add(attacker, row = new Dictionary<string, float>(StringComparer.Ordinal));
            row[defender] = percent;
        }
        public float BaseDamage(UnitState attacker, UnitState defender)
        {
            var a = Unit(attacker.TypeId); var d = Unit(defender.TypeId);
            float baseline = damage[attacker.TypeId][defender.TypeId];
            return a.Attack == 0 ? 0 : baseline * (attacker.Attack / (float)a.Attack) * (d.Health / defender.MaxHealth);
        }
        public void Validate()
        {
            foreach (var attacker in units.Keys) foreach (var defender in units.Keys)
                if (!damage.TryGetValue(attacker, out var row) || !row.ContainsKey(defender)) throw new InvalidOperationException("Incomplete damage table.");
            foreach (var race in recruits.Values) foreach (var id in race)
                if (!units.ContainsKey(id)) throw new InvalidOperationException("Unknown recruit type.");
        }
    }
}
