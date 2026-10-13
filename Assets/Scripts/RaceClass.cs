using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RaceClass : MonoBehaviour
{
    [SerializeField] private RaceDefinition definition;
    // Compatibility adapter for scene scripts and version 0/1 saves.
    [HideInInspector] public string className;
    [HideInInspector] public List<GameObject> AvailableUnits;
    [HideInInspector] public GameObject Leader;

    public RaceDefinition Definition => definition;
    public string Id => definition != null ? definition.Id : className;
    public string DisplayName => definition != null ? definition.DisplayName : className;

    private void Awake()
    {
        if (definition != null) Bind(definition);
    }

    public void Bind(RaceDefinition source)
    {
        if (source == null) throw new System.ArgumentNullException(nameof(source));
        source.Validate();
        definition = source;
        className = source.LegacySaveName;
        Leader = source.Leader;
        AvailableUnits = new List<GameObject>(source.Units);
    }
}
