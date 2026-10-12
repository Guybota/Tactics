using System;
using NUnit.Framework;
using Tactics.Core;

public class StormRulesTests
{
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void AllParticipantsInARoundShareTheSameStorm(int participants)
    {
        for (int round = 1; round <= 8; round++)
        {
            StormBounds expected = StormRules.SafeZone(11, 8, round);
            for (int seat = 0; seat < participants; seat++)
            {
                int turn = (round - 1) * participants + seat;
                StormBounds actual = StormRules.SafeZone(11, 8, TurnRules.Round(turn, participants));
                AssertBounds(actual, expected.MinX, expected.MaxX, expected.MinY, expected.MaxY);
            }
        }
    }

    [Test]
    public void DefaultFirstRoundIsSafeAndSecondRoundClosesOnlyTheOuterLayer()
    {
        AssertBounds(StormRules.SafeZone(10, 9, 1), 0, 9, 0, 8);
        AssertBounds(StormRules.SafeZone(10, 9, 2), 1, 8, 1, 7);
        Assert.That(StormRules.IsStormCell(0, 4, 10, 9, 2), Is.True);
        Assert.That(StormRules.IsStormCell(1, 4, 10, 9, 2), Is.False);
    }

    [TestCase(9, 9, 4, 4, 4, 4)]
    [TestCase(10, 10, 4, 5, 4, 5)]
    [TestCase(11, 6, 5, 5, 2, 3)]
    [TestCase(6, 11, 2, 3, 5, 5)]
    [TestCase(1, 8, 0, 0, 3, 4)]
    [TestCase(2, 1, 0, 1, 0, 0)]
    [TestCase(1, 1, 0, 0, 0, 0)]
    public void StormConvergesToTheCenterForEveryDimensionParity(
        int width, int height, int minX, int maxX, int minY, int maxY)
    {
        AssertBounds(StormRules.SafeZone(width, height, int.MaxValue), minX, maxX, minY, maxY);
    }

    [Test]
    public void LongAxisKeepsShrinkingAfterTheShortAxisReachesItsCenter()
    {
        AssertBounds(StormRules.SafeZone(15, 6, 4), 3, 11, 2, 3);
        AssertBounds(StormRules.SafeZone(15, 6, 8), 7, 7, 2, 3);
    }

    [TestCase(10, 10)]
    [TestCase(11, 6)]
    [TestCase(6, 11)]
    [TestCase(1, 1)]
    public void CellsNeverReopenAndCentralCellsNeverEnterTheStorm(int width, int height)
    {
        var wasStorm = new bool[width, height];
        StormBounds center = StormRules.SafeZone(width, height, int.MaxValue);
        for (int round = 1; round <= Math.Max(width, height) + 3; round++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    bool isStorm = StormRules.IsStormCell(x, y, width, height, round);
                    if (wasStorm[x, y]) Assert.That(isStorm, Is.True);
                    if (center.Contains(x, y)) Assert.That(isStorm, Is.False);
                    wasStorm[x, y] = isStorm;
                }
            }
        }
    }

    [Test]
    public void ConfigurableWarmupAndCadenceWaitBeforeEachShrink()
    {
        AssertBounds(StormRules.SafeZone(12, 12, 1, 3, 2), 0, 11, 0, 11);
        AssertBounds(StormRules.SafeZone(12, 12, 3, 3, 2), 0, 11, 0, 11);
        AssertBounds(StormRules.SafeZone(12, 12, 4, 3, 2), 0, 11, 0, 11);
        AssertBounds(StormRules.SafeZone(12, 12, 5, 3, 2), 1, 10, 1, 10);
        AssertBounds(StormRules.SafeZone(12, 12, 6, 3, 2), 1, 10, 1, 10);
        AssertBounds(StormRules.SafeZone(12, 12, 7, 3, 2), 2, 9, 2, 9);
    }

    [Test]
    public void ZeroWarmupCanStartTheStormInTheFirstRound()
    {
        AssertBounds(StormRules.SafeZone(10, 10, 1, 0), 1, 8, 1, 8);
    }

    [TestCase(0, 10, 1, 1, 1)]
    [TestCase(10, 0, 1, 1, 1)]
    [TestCase(10, 10, 0, 1, 1)]
    [TestCase(10, 10, 1, -1, 1)]
    [TestCase(10, 10, 1, 1, 0)]
    [TestCase(10, 10, 1, 1, -1)]
    public void InvalidInputsAreRejected(int width, int height, int round, int warmup, int cadence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StormRules.SafeZone(width, height, round, warmup, cadence));
    }

    [TestCase(-1, 0)]
    [TestCase(10, 0)]
    [TestCase(0, -1)]
    [TestCase(0, 10)]
    public void OutOfBoundsCoordinatesAreRejected(int x, int y)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StormRules.IsStormCell(x, y, 10, 10, 1));
    }

    private static void AssertBounds(StormBounds actual, int minX, int maxX, int minY, int maxY)
    {
        Assert.That(actual.MinX, Is.EqualTo(minX));
        Assert.That(actual.MaxX, Is.EqualTo(maxX));
        Assert.That(actual.MinY, Is.EqualTo(minY));
        Assert.That(actual.MaxY, Is.EqualTo(maxY));
    }
}
