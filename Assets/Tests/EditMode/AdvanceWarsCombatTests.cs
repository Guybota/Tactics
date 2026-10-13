using System;
using NUnit.Framework;
using Tactics.Core;

public class AdvanceWarsCombatTests
{
    [TestCase(0, 55)] [TestCase(1, 49)] [TestCase(2, 44)] [TestCase(3, 38)]
    public void TerrainStarsReduceDamageAtFullHealth(int stars, float expected)
    {
        var result = CombatRules.Resolve(new Combatant(100, 100, 1, 1), new Combatant(100, 100, 1, 1, stars), 1, 55, 55, 0, 0);
        Assert.That(result.Damage, Is.EqualTo(expected));
    }
    [Test]
    public void TerrainDefenseScalesWithDefendersRoundedHealth()
    {
        var result = CombatRules.Resolve(new Combatant(100, 100, 1, 1), new Combatant(50, 100, 1, 1, 2), 1, 55, 55, 0, 0);
        Assert.That(result.Damage, Is.EqualTo(49));
    }
    [TestCase(100, 10)] [TestCase(91, 10)] [TestCase(90, 9)] [TestCase(1, 1)] [TestCase(0, 0)]
    public void VisualHealthUsesTenRoundedUpBuckets(float health, int expected)
        => Assert.That(CombatRules.VisualHealth(health, 100), Is.EqualTo(expected));
    [Test]
    public void MatchingHealthPercentagesUseTheSameFormulaAcrossDifferentHealthPools()
    {
        var result = CombatRules.Resolve(new Combatant(15, 30, 1, 1), new Combatant(20, 20, 1, 1), 1, 55, 55, 0, 0);
        Assert.That(result.Damage, Is.EqualTo(5.4f).Within(.0001));
    }
    [Test]
    public void EveryLuckOutcomeFitsPredictionIncludingUncertainKillsAndCounters()
    {
        var a = new Combatant(100, 100, 1, 2, 1);
        var d = new Combatant(60, 100, 1, 2, 2);
        var interval = CombatRules.Predict(a, d, 1, 65, 55);
        Assert.That(interval.MinCounterDamage, Is.Zero);
        Assert.That(interval.MaxCounterDamage, Is.GreaterThan(0));
        for (int attackLuck = 0; attackLuck <= 9; attackLuck++)
            for (int counterLuck = 0; counterLuck <= 9; counterLuck++)
            {
                var result = CombatRules.Resolve(a, d, 1, 65, 55, attackLuck, counterLuck);
                Assert.That(result.AttackerHealth, Is.InRange(interval.MinAttackerHealth, interval.MaxAttackerHealth));
                Assert.That(result.DefenderHealth, Is.InRange(interval.MinDefenderHealth, interval.MaxDefenderHealth));
            }
    }
    [Test]
    public void TinyDamageCanRoundToZeroAndCounterHasNoHalfDamagePenalty()
    {
        var result = CombatRules.Resolve(new Combatant(1, 100, 1, 1), new Combatant(100, 100, 1, 1), 1, 1, 1, 0, 0);
        Assert.That(result.Damage, Is.Zero);
        Assert.That(result.CounterDamage, Is.EqualTo(1));
    }
    [TestCase(-1)] [TestCase(10)]
    public void LuckOutsideAdvanceWarsRangeIsRejected(int luck)
        => Assert.Throws<ArgumentOutOfRangeException>(() => CombatRules.Resolve(new Combatant(100, 100, 1, 1), new Combatant(100, 100, 1, 1), 1, 55, 55, luck, 0));
}
