using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Tactics/Unit Combat Definition")]
public sealed class UnitCombatDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField] private float baselineHealth;
    [SerializeField] private int baselineAttack;
    public string Id => stableId;
    public float BaselineHealth => baselineHealth;
    public int BaselineAttack => baselineAttack;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(stableId) || baselineHealth <= 0 || float.IsNaN(baselineHealth)
            || float.IsInfinity(baselineHealth) || baselineAttack <= 0)
            throw new InvalidOperationException("Invalid combat definition: " + name);
    }
}
