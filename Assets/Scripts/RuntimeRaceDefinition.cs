using System.Collections.Generic;
using UnityEngine;

// Snapshot scene definitions before the menu scene is destroyed.
public sealed class RuntimeRaceDefinition
{
    public readonly string Name;
    public readonly GameObject Leader;
    public readonly List<GameObject> Units;
    public readonly RaceDefinition Asset;
    public RuntimeRaceDefinition(RaceClass race)
    {
        Asset = race.Definition;
        Name = race.className;
        Leader = race.Leader;
        Units = new List<GameObject>(race.AvailableUnits);
    }
}
