using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tactics/Combat Damage Table")]
public sealed class CombatDamageTable : ScriptableObject
{
    [Serializable]
    public sealed class Matchup
    {
        public UnitCombatDefinition attacker;
        public UnitCombatDefinition defender;
        [Min(0)] public float baseDamagePercent;
    }
    [SerializeField] private List<Matchup> matchups = new List<Matchup>();

    public float GetBaseDamage(UnitCombatDefinition attacker, UnitCombatDefinition defender)
    {
        foreach (var matchup in matchups)
            if (matchup.attacker == attacker && matchup.defender == defender) return matchup.baseDamagePercent;
        throw new InvalidOperationException("Missing damage matchup: " + attacker.Id + " -> " + defender.Id);
    }

    public void Validate()
    {
        if (matchups == null || matchups.Count == 0) throw new InvalidOperationException("Damage matchups are required.");
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        var definitions = new Dictionary<string, UnitCombatDefinition>(StringComparer.Ordinal);
        foreach (var matchup in matchups)
        {
            if (matchup == null || matchup.attacker == null || matchup.defender == null
                || matchup.baseDamagePercent < 0 || float.IsNaN(matchup.baseDamagePercent) || float.IsInfinity(matchup.baseDamagePercent))
                throw new InvalidOperationException("Invalid damage matchup.");
            foreach (var definition in new[] { matchup.attacker, matchup.defender })
            {
                definition.Validate();
                if (definitions.TryGetValue(definition.Id, out var existing) && existing != definition)
                    throw new InvalidOperationException("Duplicate unit type ID: " + definition.Id);
                definitions[definition.Id] = definition;
            }
            if (!pairs.Add(matchup.attacker.Id + "|" + matchup.defender.Id))
                throw new InvalidOperationException("Duplicate damage matchup.");
        }
        foreach (var attacker in definitions.Values)
            foreach (var defender in definitions.Values) GetBaseDamage(attacker, defender);
    }
}
