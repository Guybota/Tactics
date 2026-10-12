using System;

namespace Tactics.Core
{
    public readonly struct Combatant
    {
        public readonly float Health;
        public readonly float MaxHealth;
        public readonly int Attack;
        public readonly int Range;

        public Combatant(float health, float maxHealth, int attack, int range)
        {
            if (float.IsNaN(health) || float.IsInfinity(health) || maxHealth <= 0 || float.IsNaN(maxHealth) || float.IsInfinity(maxHealth) || attack < 0 || range < 0)
                throw new ArgumentOutOfRangeException(nameof(health));
            Health = Math.Max(0, Math.Min(health, maxHealth));
            MaxHealth = maxHealth;
            Attack = attack;
            Range = range;
        }
    }

    public readonly struct CombatResult
    {
        public readonly float AttackerHealth;
        public readonly float DefenderHealth;
        public readonly float Damage;
        public readonly float CounterDamage;
        public CombatResult(float attackerHealth, float defenderHealth, float damage, float counterDamage)
        {
            AttackerHealth = attackerHealth;
            DefenderHealth = defenderHealth;
            Damage = damage;
            CounterDamage = counterDamage;
        }
    }

    // Keep the existing balance until the new damage and minimum-range rules are agreed.
    public static class CombatRules
    {
        public static float Strength(float health, float maxHealth)
        {
            if (maxHealth <= 0) return 0;
            return 0.7f + 0.3f * Math.Max(0, Math.Min(health / maxHealth, 1));
        }

        public static CombatResult Resolve(Combatant attacker, Combatant defender, int distance)
        {
            if (distance < 1 || distance > attacker.Range)
                throw new ArgumentOutOfRangeException(nameof(distance));
            if (attacker.Health <= 0 || defender.Health <= 0)
                throw new InvalidOperationException("Dead units cannot participate in combat.");
            float damage = Math.Min(defender.Health, attacker.Attack * Strength(attacker.Health, attacker.MaxHealth));
            float remaining = Math.Max(0, defender.Health - damage);
            float counter = distance <= defender.Range && remaining >= 0.5f
                ? Math.Min(attacker.Health, defender.Attack * Strength(remaining, defender.MaxHealth) * 0.5f)
                : 0;
            return new CombatResult(Math.Max(0, attacker.Health - counter), remaining, damage, counter);
        }
    }
}
