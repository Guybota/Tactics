using System;
using System.IO;
using NUnit.Framework;
using Tactics.Core;

public class CoreRulesTests
{
    [Test]
    public void CounterUsesDefendersHealthAfterDamage()
    {
        var result = CombatRules.Resolve(new Combatant(100, 100, 55, 1), new Combatant(100, 100, 55, 2), 1, 55, 55, 0, 0);
        Assert.That(result.DefenderHealth, Is.EqualTo(45));
        Assert.That(result.CounterDamage, Is.EqualTo(27));
        Assert.That(result.AttackerHealth, Is.EqualTo(73));
    }
    [Test]
    public void LethalAttackNeverReceivesCounterDamage()
    {
        var result = CombatRules.Resolve(new Combatant(13, 13, 50, 2), new Combatant(8, 8, 20, 2), 1);
        Assert.That(result.DefenderHealth, Is.Zero);
        Assert.That(result.CounterDamage, Is.Zero);
        Assert.That(result.AttackerHealth, Is.EqualTo(13));
    }
    [Test]
    public void CounterRequiresDefenderRange()
    {
        var result = CombatRules.Resolve(new Combatant(13, 13, 4, 3), new Combatant(8, 8, 4, 1), 2);
        Assert.That(result.CounterDamage, Is.Zero);
    }
    [Test]
    public void CannotAttackBeyondOwnRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatRules.Resolve(new Combatant(13, 13, 4, 1), new Combatant(8, 8, 4, 2), 2));
    }
    [Test]
    public void CannotAttackWithDeadUnit()
    {
        Assert.Throws<InvalidOperationException>(() => CombatRules.Resolve(new Combatant(0, 13, 4, 1), new Combatant(8, 8, 4, 2), 1));
    }
    [TestCase(2)] [TestCase(3)] [TestCase(4)]
    public void EveryParticipantUsesSameRoundNumber(int participants)
    {
        for (int index = 0; index < participants; index++)
        {
            Assert.That(TurnRules.Round(index, participants), Is.EqualTo(1));
            Assert.That(TurnRules.Round(index + participants, participants), Is.EqualTo(2));
        }
    }
    [Test]
    public void TurnRotationSkipsEliminatedSeatsAndPreservesRoundBoundary()
    {
        var lost = new[] { false, true, false, true };
        Assert.That(TurnRules.NextActiveTurn(0, lost), Is.EqualTo(2));
        Assert.That(TurnRules.NextActiveTurn(2, lost), Is.EqualTo(4));
        Assert.That(TurnRules.Round(4, 4), Is.EqualTo(2));
    }
    [Test]
    public void NoTurnExistsWhenEveryoneIsEliminated()
    {
        Assert.That(TurnRules.NextActiveTurn(0, new[] { true, true }), Is.EqualTo(-1));
    }
    [TestCase("saveManager")] [TestCase("SAVEMANAGER")] [TestCase("../escape")] [TestCase("")] [TestCase("line\n")]
    public void UnsafeOrReservedSaveNamesAreRejected(string name)
    {
        Assert.That(SaveFileStore.IsValidName(name), Is.False);
    }
    [Test]
    public void OverwriteKeepsLastGoodBackupAndLeavesNoTemporaryFiles()
    {
        string regressionRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../Library/RegressionTests"));
        string directory = Path.GetFullPath(Path.Combine(regressionRoot, Guid.NewGuid().ToString("N")));
        Assert.That(directory.StartsWith(regressionRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
        try
        {
            var store = new SaveFileStore(directory);
            store.Write("Match1", "old");
            store.Write("Match1", "new");
            Assert.That(store.Read("Match1"), Is.EqualTo("new"));
            Assert.That(File.ReadAllText(Path.Combine(directory, "Match1.json.bak")), Is.EqualTo("old"));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
            Assert.Throws<ArgumentNullException>(() => store.Write("Match1", null));
            Assert.That(store.Read("Match1"), Is.EqualTo("new"));
            store.WriteIndex("index");
            Assert.Throws<ArgumentException>(() => store.Write("saveManager", "collision"));
            Assert.That(store.ReadIndex(), Is.EqualTo("index"));
            store.Delete("Match1");
            Assert.That(store.ListNames(), Is.Empty);
            Assert.That(File.Exists(Path.Combine(directory, "Match1.json.bak")), Is.False);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
