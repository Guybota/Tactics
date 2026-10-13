using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tactics/Race Catalog")]
public sealed class RaceCatalog : ScriptableObject
{
    [SerializeField] private List<RaceDefinition> races = new List<RaceDefinition>();
    public IReadOnlyList<RaceDefinition> Races => races.AsReadOnly();

    public void Validate()
    {
        if (races == null || races.Count == 0)
            throw new InvalidOperationException("At least one race must be configured.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var legacyNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var race in races)
        {
            if (race == null) throw new InvalidOperationException("Missing race definition.");
            race.Validate();
            if (!ids.Add(race.Id) || !legacyNames.Add(race.LegacySaveName))
                throw new InvalidOperationException("Duplicate race ID or legacy save name: " + race.Id);
        }
    }
}
