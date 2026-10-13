using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tactics/Race Definition")]
public sealed class RaceDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField] private string displayName;
    // Keep the original save key while saves still use legacy names.
    [SerializeField] private string legacySaveName;
    [SerializeField] private GameObject leader;
    [SerializeField] private List<GameObject> units = new List<GameObject>();

    public string Id => stableId;
    public string DisplayName => displayName;
    public string LegacySaveName => legacySaveName;
    public GameObject Leader => leader;
    public IReadOnlyList<GameObject> Units => units.AsReadOnly();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(stableId) || string.IsNullOrWhiteSpace(displayName)
            || string.IsNullOrWhiteSpace(legacySaveName))
            throw new InvalidOperationException("Race identifiers and display name are required: " + name);
        if (leader == null || leader.GetComponent<Unit>() == null || units == null)
            throw new InvalidOperationException("Race requires a leader and recruit list: " + stableId);
        var prefabs = new HashSet<GameObject>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prefab in units)
        {
            if (prefab == null || prefab.GetComponent<Unit>() == null || !prefabs.Add(prefab)
                || !names.Add(prefab.GetComponent<Unit>().unit_name))
                throw new InvalidOperationException("Invalid or duplicate recruit in race: " + stableId);
        }
    }
}
