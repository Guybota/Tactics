using System;

namespace Tactics.Core
{
    public readonly struct Combatant
    {
        public readonly float Health;
        public readonly float MaxHealth;
        public readonly int Attack;
        public readonly int Range;
        public readonly int TerrainStars;

        public Combatant(float health, float maxHealth, int attack, int range, int terrainStars = 0)
        {
            if (float.IsNaN(health) || float.IsInfinity(health) || maxHealth <= 0 || float.IsNaN(maxHealth) || float.IsInfinity(maxHealth) || attack < 0 || range < 0 || terrainStars < 0 || terrainStars > 10)
                throw new ArgumentOutOfRangeException(nameof(health));
            Health = Math.Max(0, Math.Min(health, maxHealth));
            MaxHealth = maxHealth;
            Attack = attack;
            Range = range;
            TerrainStars = terrainStars;
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

    public readonly struct CombatPrediction
    {
        public readonly float MinAttackerHealth, MaxAttackerHealth;
        public readonly float MinDefenderHealth, MaxDefenderHealth;
        public readonly float MinDamage, MaxDamage, MinCounterDamage, MaxCounterDamage;
        internal CombatPrediction(float minAttacker, float maxAttacker, float minDefender, float maxDefender,
            float minDamage, float maxDamage, float minCounter, float maxCounter)
        {
            MinAttackerHealth = minAttacker; MaxAttackerHealth = maxAttacker;
            MinDefenderHealth = minDefender; MaxDefenderHealth = maxDefender;
            MinDamage = minDamage; MaxDamage = maxDamage;
            MinCounterDamage = minCounter; MaxCounterDamage = maxCounter;
        }
    }

    // AW2 damage and luck, retaining Tactics' movement and attack-range rules.
    // Reference: https://awbw.fandom.com/wiki/Damage_Formula
    public static class CombatRules
    {
        public static float Strength(float health, float maxHealth)
        {
            if (maxHealth <= 0) return 0;
            return VisualHealth(health, maxHealth) / 10f;
        }

        public static CombatResult Resolve(Combatant attacker, Combatant defender, int distance)
            => Resolve(attacker, defender, distance, attacker.Attack / defender.MaxHealth * 100,
                defender.Attack / attacker.MaxHealth * 100, 0, 0);

        public static int VisualHealth(float health, float maxHealth)
            => maxHealth <= 0 ? 0 : (int)Math.Ceiling(Math.Max(0, Math.Min(health / maxHealth, 1)) * 10);

        private static float Damage(Combatant attacker, Combatant defender, float baseDamage, int luck)
        {
            if (float.IsNaN(baseDamage) || float.IsInfinity(baseDamage) || baseDamage < 0 || luck < 0 || luck > 9)
                throw new ArgumentOutOfRangeException(nameof(baseDamage));
            double percent = (baseDamage + luck) * (VisualHealth(attacker.Health, attacker.MaxHealth) / 10.0)
                * Math.Max(0, (100 - defender.TerrainStars * VisualHealth(defender.Health, defender.MaxHealth)) / 100.0);
            // AW2/AWBW rounding: up to a 0.05 interval, then down to a whole percent.
            double rounded = Math.Floor(Math.Ceiling(percent * 20 - 0.000001) / 20);
            return Math.Min(defender.Health, (float)(rounded / 100 * defender.MaxHealth));
        }

        public static CombatResult Resolve(Combatant attacker, Combatant defender, int distance,
            float baseDamage, float counterBaseDamage, int attackLuck, int counterLuck)
        {
            if (distance < 1 || distance > attacker.Range)
                throw new ArgumentOutOfRangeException(nameof(distance));
            if (attacker.Health <= 0 || defender.Health <= 0)
                throw new InvalidOperationException("Dead units cannot participate in combat.");
            if (counterLuck < 0 || counterLuck > 9 || counterBaseDamage < 0
                || float.IsNaN(counterBaseDamage) || float.IsInfinity(counterBaseDamage))
                throw new ArgumentOutOfRangeException(nameof(counterLuck));
            float damage = Damage(attacker, defender, baseDamage, attackLuck);
            float remaining = Math.Max(0, defender.Health - damage);
            var survivingDefender = new Combatant(remaining, defender.MaxHealth, defender.Attack, defender.Range, defender.TerrainStars);
            float counter = distance <= defender.Range && remaining > 0
                ? Damage(survivingDefender, attacker, counterBaseDamage, counterLuck)
                : 0;
            return new CombatResult(Math.Max(0, attacker.Health - counter), remaining, damage, counter);
        }

        public static CombatPrediction Predict(Combatant attacker, Combatant defender, int distance,
            float baseDamage, float counterBaseDamage)
        {
            float minA = float.MaxValue, maxA = 0, minD = float.MaxValue, maxD = 0;
            float minDamage = float.MaxValue, maxDamage = 0, minCounter = float.MaxValue, maxCounter = 0;
            for (int attackLuck = 0; attackLuck <= 9; attackLuck++)
                for (int counterLuck = 0; counterLuck <= 9; counterLuck++)
                {
                    var result = Resolve(attacker, defender, distance, baseDamage, counterBaseDamage, attackLuck, counterLuck);
                    minA = Math.Min(minA, result.AttackerHealth); maxA = Math.Max(maxA, result.AttackerHealth);
                    minD = Math.Min(minD, result.DefenderHealth); maxD = Math.Max(maxD, result.DefenderHealth);
                    minDamage = Math.Min(minDamage, result.Damage); maxDamage = Math.Max(maxDamage, result.Damage);
                    minCounter = Math.Min(minCounter, result.CounterDamage); maxCounter = Math.Max(maxCounter, result.CounterDamage);
                }
            return new CombatPrediction(minA, maxA, minD, maxD, minDamage, maxDamage, minCounter, maxCounter);
        }
    }
}
