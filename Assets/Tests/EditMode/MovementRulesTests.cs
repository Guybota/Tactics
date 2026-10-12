using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tactics.Core;

public class MovementRulesTests
{
    private static GridPosition At(int x, int y) => new GridPosition(x, y);

    private static MovementCell[,] Board(int width, int height)
    {
        var board = new MovementCell[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                board[x, y] = new MovementCell(1);
        return board;
    }

    private static void AssertOrthogonalPath(IList<GridPosition> path)
    {
        for (int index = 1; index < path.Count; index++)
            Assert.That(path[index - 1].DistanceTo(path[index]), Is.EqualTo(1));
    }

    [Test]
    public void OriginRemainsAvailableWithoutSpendingMovement()
    {
        var board = Board(2, 1);
        board[0, 0] = new MovementCell(2, occupant: CellOccupant.Ally);
        var movement = MovementRules.Calculate(board, At(0, 0), 0);
        Assert.That(movement.CostTo(At(0, 0)), Is.Zero);
        Assert.That(movement.CanStop(At(0, 0)), Is.True);
        Assert.That(movement.PathTo(At(0, 0)), Is.EqualTo(new[] { At(0, 0) }));
        Assert.That(movement.CanStop(At(1, 0)), Is.False);
    }

    [Test]
    public void DiagonalDestinationNeedsTwoOrthogonalSteps()
    {
        var board = Board(2, 2);
        Assert.That(MovementRules.Calculate(board, At(0, 0), 1).CanStop(At(1, 1)), Is.False);
        var movement = MovementRules.Calculate(board, At(0, 0), 2);
        Assert.That(movement.CostTo(At(1, 1)), Is.EqualTo(2));
        var path = movement.PathTo(At(1, 1));
        Assert.That(path.Count, Is.EqualTo(3));
        AssertOrthogonalPath(path);
    }

    [Test]
    public void AlliedUnitCanBeCrossedButCannotBeDestination()
    {
        var board = Board(3, 1);
        board[1, 0] = new MovementCell(1, occupant: CellOccupant.Ally);
        var movement = MovementRules.Calculate(board, At(0, 0), 2);
        Assert.That(movement.CostTo(At(1, 0)), Is.EqualTo(1));
        Assert.That(movement.CanStop(At(1, 0)), Is.False);
        Assert.That(movement.PathTo(At(1, 0)), Is.Empty);
        Assert.That(movement.PathTo(At(2, 0)), Is.EqualTo(new[] { At(0, 0), At(1, 0), At(2, 0) }));
    }

    [Test]
    public void EnemyBlocksTraversalAndDestination()
    {
        var board = Board(3, 1);
        board[1, 0] = new MovementCell(1, occupant: CellOccupant.Enemy);
        var movement = MovementRules.Calculate(board, At(0, 0), 10);
        Assert.That(movement.CanStop(At(1, 0)), Is.False);
        Assert.That(movement.PathTo(At(2, 0)), Is.Empty);
    }

    [Test]
    public void MountainBlocksEvenWithExcessMovementBudget()
    {
        var board = Board(3, 1);
        board[1, 0] = new MovementCell(1, isWall: true);
        var movement = MovementRules.Calculate(board, At(0, 0), 100);
        Assert.That(movement.CanStop(At(1, 0)), Is.False);
        Assert.That(movement.CanStop(At(2, 0)), Is.False);
    }

    [Test]
    public void ForestChargesEntryCostAndBudgetIsInclusive()
    {
        var board = Board(3, 1);
        board[1, 0] = new MovementCell(2);
        var movement = MovementRules.Calculate(board, At(0, 0), 2);
        Assert.That(movement.CostTo(At(1, 0)), Is.EqualTo(2));
        Assert.That(movement.CanStop(At(1, 0)), Is.True);
        Assert.That(movement.CanStop(At(2, 0)), Is.False);
        Assert.That(MovementRules.Calculate(board, At(0, 0), 3).CostTo(At(2, 0)), Is.EqualTo(3));
    }

    [Test]
    public void CheapestWeightedRouteCanBeLongerThanStraightRoute()
    {
        var board = Board(5, 2);
        board[1, 0] = new MovementCell(2);
        board[2, 0] = new MovementCell(2);
        board[3, 0] = new MovementCell(2);
        var movement = MovementRules.Calculate(board, At(0, 0), 6);
        var path = movement.PathTo(At(4, 0));
        Assert.That(movement.CostTo(At(4, 0)), Is.EqualTo(6));
        Assert.That(path.Count, Is.EqualTo(7));
        Assert.That(path, Has.Member(At(2, 1)));
        AssertOrthogonalPath(path);
    }

    [Test]
    public void RoutesAroundEnemyWhenAnotherRouteFitsBudget()
    {
        var board = Board(3, 2);
        board[1, 0] = new MovementCell(1, occupant: CellOccupant.Enemy);
        var movement = MovementRules.Calculate(board, At(0, 0), 4);
        Assert.That(movement.CostTo(At(2, 0)), Is.EqualTo(4));
        Assert.That(movement.PathTo(At(2, 0)), Has.No.Member(At(1, 0)));
        AssertOrthogonalPath(movement.PathTo(At(2, 0)));
    }

    [Test]
    public void EqualCostPathsStayDeterministicAcrossQueriesAndCalculations()
    {
        var board = Board(4, 4);
        var first = MovementRules.Calculate(board, At(0, 0), 6);
        var expected = first.PathTo(At(3, 3));
        first.PathTo(At(0, 3));
        first.TryAttackOrigin(At(3, 3), 1, out _);
        Assert.That(first.PathTo(At(3, 3)), Is.EqualTo(expected));
        for (int index = 0; index < 5; index++)
            Assert.That(MovementRules.Calculate(board, At(0, 0), 6).PathTo(At(3, 3)), Is.EqualTo(expected));
    }

    [Test]
    public void SnapshotSurvivesBoardMutationAndReturnedPathMutation()
    {
        var board = Board(3, 1);
        var movement = MovementRules.Calculate(board, At(0, 0), 2);
        board[1, 0] = new MovementCell(10, isWall: true);
        board[2, 0] = new MovementCell(1, occupant: CellOccupant.Ally);
        movement.PathTo(At(2, 0)).Clear();
        Assert.That(movement.CostTo(At(2, 0)), Is.EqualTo(2));
        Assert.That(movement.CanStop(At(2, 0)), Is.True);
        Assert.That(movement.PathTo(At(2, 0)), Is.EqualTo(new[] { At(0, 0), At(1, 0), At(2, 0) }));
        Assert.That(MovementRules.Calculate(board, At(0, 0), 2).CanStop(At(2, 0)), Is.False);
    }

    [Test]
    public void RangedAttackFromOriginIgnoresMountainAndOccupiedCells()
    {
        var board = Board(4, 1);
        board[1, 0] = new MovementCell(1, isWall: true);
        board[2, 0] = new MovementCell(1, occupant: CellOccupant.Ally);
        board[3, 0] = new MovementCell(1, occupant: CellOccupant.Enemy);
        var movement = MovementRules.Calculate(board, At(0, 0), 0);
        Assert.That(movement.TryAttackOrigin(At(3, 0), 3, out var origin), Is.True);
        Assert.That(origin, Is.EqualTo(At(0, 0)));
        Assert.That(movement.TryAttackOrigin(At(3, 0), 2, out _), Is.False);
    }

    [Test]
    public void AdjacentRangedAttackHasNoMinimumRange()
    {
        var board = Board(2, 1);
        board[1, 0] = new MovementCell(1, occupant: CellOccupant.Enemy);
        Assert.That(MovementRules.Calculate(board, At(0, 0), 0).TryAttackOrigin(At(1, 0), 3, out var origin), Is.True);
        Assert.That(origin, Is.EqualTo(At(0, 0)));
    }

    [Test]
    public void AttackOriginMinimizesMovementRatherThanDistanceToTarget()
    {
        var board = Board(5, 1);
        board[4, 0] = new MovementCell(1, occupant: CellOccupant.Enemy);
        var movement = MovementRules.Calculate(board, At(0, 0), 3);
        Assert.That(movement.TryAttackOrigin(At(4, 0), 2, out var origin), Is.True);
        Assert.That(origin, Is.EqualTo(At(2, 0)));
        Assert.That(movement.CostTo(origin), Is.EqualTo(2));
        Assert.That(movement.PathTo(origin), Is.EqualTo(new[] { At(0, 0), At(1, 0), At(2, 0) }));
    }

    [Test]
    public void AlliedAttackOriginIsSkippedEvenWhenItIsCheaper()
    {
        var board = Board(4, 1);
        board[1, 0] = new MovementCell(1, occupant: CellOccupant.Ally);
        board[3, 0] = new MovementCell(1, occupant: CellOccupant.Enemy);
        var movement = MovementRules.Calculate(board, At(0, 0), 2);
        Assert.That(movement.TryAttackOrigin(At(3, 0), 2, out var origin), Is.True);
        Assert.That(origin, Is.EqualTo(At(2, 0)));
        Assert.That(movement.CanStop(origin), Is.True);
    }

    [Test]
    public void AttackCannotUseOccupiedAllyWhenNoEmptyOriginIsReachable()
    {
        var board = Board(3, 1);
        board[1, 0] = new MovementCell(1, occupant: CellOccupant.Ally);
        board[2, 0] = new MovementCell(1, occupant: CellOccupant.Enemy);
        Assert.That(MovementRules.Calculate(board, At(0, 0), 1).TryAttackOrigin(At(2, 0), 1, out _), Is.False);
    }

    [Test]
    public void EqualAttackOriginsAreDeterministicAndHaveValidPaths()
    {
        var board = Board(3, 3);
        board[2, 2] = new MovementCell(1, occupant: CellOccupant.Enemy);
        var movement = MovementRules.Calculate(board, At(0, 0), 3);
        Assert.That(movement.TryAttackOrigin(At(2, 2), 1, out var expected), Is.True);
        Assert.That(movement.CostTo(expected), Is.EqualTo(3));
        Assert.That(movement.CanStop(expected), Is.True);
        AssertOrthogonalPath(movement.PathTo(expected));
        for (int index = 0; index < 5; index++)
        {
            var repeated = MovementRules.Calculate(board, At(0, 0), 3);
            Assert.That(repeated.TryAttackOrigin(At(2, 2), 1, out var actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }
    }

    [Test]
    public void OutOfBoardQueriesReturnNoPathOrAttackPlan()
    {
        var movement = MovementRules.Calculate(Board(2, 2), At(0, 0), 3);
        foreach (var position in new[] { At(-1, 0), At(0, -1), At(2, 0), At(0, 2) })
        {
            Assert.That(movement.CanStop(position), Is.False);
            Assert.That(movement.PathTo(position), Is.Empty);
            Assert.That(movement.TryAttackOrigin(position, 2, out _), Is.False);
        }
    }

    [Test]
    public void InvalidMovementInputsAreRejectedInsteadOfProducingZeroCostPaths()
    {
        Assert.Throws<ArgumentNullException>(() => MovementRules.Calculate(null, At(0, 0), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MovementRules.Calculate(Board(1, 1), At(0, 0), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MovementRules.Calculate(Board(1, 1), At(1, 0), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MovementRules.Calculate(new MovementCell[0, 0], At(0, 0), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementCell(0));
        Assert.Throws<ArgumentException>(() => MovementRules.Calculate(new MovementCell[1, 1], At(0, 0), 1));
        var movement = MovementRules.Calculate(Board(1, 1), At(0, 0), 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => movement.TryAttackOrigin(At(0, 0), 0, out _));
    }

    [Test]
    public void ExactMaximumIntegerBudgetStillAllowsAReachableDestination()
    {
        var directBoard = Board(2, 1);
        directBoard[1, 0] = new MovementCell(int.MaxValue);
        var direct = MovementRules.Calculate(directBoard, At(0, 0), int.MaxValue);
        Assert.That(direct.IsReachable(At(1, 0)), Is.True);
        Assert.That(direct.CanStop(At(1, 0)), Is.True);
        Assert.That(direct.CostTo(At(1, 0)), Is.EqualTo(int.MaxValue));
        Assert.That(direct.PathTo(At(1, 0)), Is.EqualTo(new[] { At(0, 0), At(1, 0) }));

        var summedBoard = Board(3, 1);
        summedBoard[1, 0] = new MovementCell(int.MaxValue - 1);
        var summed = MovementRules.Calculate(summedBoard, At(0, 0), int.MaxValue);
        Assert.That(summed.IsReachable(At(2, 0)), Is.True);
        Assert.That(summed.CanStop(At(2, 0)), Is.True);
        Assert.That(summed.CostTo(At(2, 0)), Is.EqualTo(int.MaxValue));
        Assert.That(summed.PathTo(At(2, 0)), Is.EqualTo(new[] { At(0, 0), At(1, 0), At(2, 0) }));
    }

    [Test]
    public void VeryLargeCostsCannotOverflowIntoReachableNegativeCosts()
    {
        var board = Board(3, 1);
        board[1, 0] = new MovementCell(int.MaxValue - 1);
        board[2, 0] = new MovementCell(2);
        var movement = MovementRules.Calculate(board, At(0, 0), int.MaxValue);
        Assert.That(movement.CostTo(At(1, 0)), Is.EqualTo(int.MaxValue - 1));
        Assert.That(movement.CanStop(At(1, 0)), Is.True);
        Assert.That(movement.CanStop(At(2, 0)), Is.False);
        Assert.That(movement.PathTo(At(2, 0)), Is.Empty);
    }
}
