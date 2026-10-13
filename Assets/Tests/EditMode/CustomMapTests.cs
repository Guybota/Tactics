using System;
using System.Linq;
using NUnit.Framework;
using Tactics.Core;

public class CustomMapTests
{
    private static CustomMapData Map(int players = 2)
    {
        var map = new CustomMapData { id = Guid.NewGuid().ToString("N"), name = "Test map", width = 10, height = 10, playerCount = players };
        for (int i = 0; i < 100; i++) map.cells.Add(new MapCellData());
        for (int i = 0; i < players; i++) map.starts.Add(new MapStartData { slot = i, x = i % 2 == 0 ? 1 : 8, y = i < 2 ? 1 : 8 });
        return map;
    }
    [TestCase(2)] [TestCase(3)] [TestCase(4)]
    public void ConnectedMapSupportsAllParticipantCounts(int count) => Assert.DoesNotThrow(() => CustomMapValidator.ValidatePlayable(Map(count)));
    [Test]
    public void DraftCanBeStoredWithoutStartsButCannotBePlayed()
    {
        var map = Map(); map.starts.Clear();
        Assert.DoesNotThrow(() => CustomMapValidator.ValidateStructure(map));
        Assert.That(CustomMapValidator.PlayIssues(map).Count, Is.EqualTo(2));
    }
    [Test]
    public void StartsSeparatedByMountainWallCannotPlayUntilGapIsOpened()
    {
        var map = Map(); for (int y = 0; y < 10; y++) map.Cell(5, y).terrain = TerrainKind.Mountain;
        Assert.That(CustomMapValidator.PlayIssues(map).Single(), Does.Contain("no path"));
        map.Cell(5, 9).terrain = TerrainKind.Forest;
        Assert.DoesNotThrow(() => CustomMapValidator.ValidatePlayable(map));
    }
    [Test]
    public void DiagonalContactDoesNotCountAsAPath()
    {
        var map = Map(); map.Cell(0, 1).terrain = map.Cell(2, 1).terrain = map.Cell(1, 0).terrain = map.Cell(1, 2).terrain = TerrainKind.Mountain;
        Assert.That(CustomMapValidator.PlayIssues(map), Has.Count.EqualTo(1));
    }
    [Test]
    public void SharedAndBlockedStartsAreDrafts()
    {
        var map = Map(); map.starts[1].x = 1; map.starts[1].y = 1; map.Cell(1, 1).terrain = TerrainKind.Mountain;
        Assert.DoesNotThrow(() => CustomMapValidator.ValidateStructure(map));
        Assert.That(CustomMapValidator.PlayIssues(map).Count, Is.EqualTo(3));
    }
    [Test]
    public void NeutralAndOwnedCastlesCanBePlayedButInactiveOwnerIsDraft()
    {
        var map = Map(); map.Cell(3, 3).castle = true; map.Cell(4, 3).castle = true; map.Cell(4, 3).castleOwner = 1;
        Assert.DoesNotThrow(() => CustomMapValidator.ValidatePlayable(map));
        map.Cell(4, 3).castleOwner = 3;
        Assert.DoesNotThrow(() => CustomMapValidator.ValidateStructure(map));
        Assert.That(CustomMapValidator.PlayIssues(map).Single(), Does.Contain("inactive"));
    }
    [Test]
    public void CloneCannotMutateAuthoringTerrainOwnershipOrStarts()
    {
        var map = Map(); map.Cell(3, 3).castle = true; var copy = map.Clone();
        copy.Cell(3, 3).castleOwner = 1; copy.Cell(1, 2).terrain = TerrainKind.Mountain; copy.starts[0].x = 4;
        Assert.That(map.Cell(3, 3).castleOwner, Is.EqualTo(-1)); Assert.That(map.Cell(1, 2).terrain, Is.EqualTo(TerrainKind.Grass));
        Assert.That(map.starts[0].x, Is.EqualTo(1));
    }
    [TestCase("version")] [TestCase("id")] [TestCase("dimensions")] [TestCase("cells")] [TestCase("terrain")]
    [TestCase("castle")] [TestCase("owner")] [TestCase("slot")] [TestCase("coordinate")] [TestCase("name")]
    public void MalformedMapsAreRejected(string defect)
    {
        var map = Map();
        switch (defect)
        {
            case "version": map.schemaVersion++; break;
            case "id": map.id = "../test"; break;
            case "dimensions": map.width = 10000; break;
            case "cells": map.cells.RemoveAt(0); break;
            case "terrain": map.cells[0].terrain = (TerrainKind)99; break;
            case "castle": map.cells[0].castle = true; map.cells[0].terrain = TerrainKind.Forest; break;
            case "owner": map.cells[0].castleOwner = 0; break;
            case "slot": map.starts[1].slot = 0; break;
            case "coordinate": map.starts[0].x = 10; break;
            case "name": map.name = "\n"; break;
        }
        Assert.Throws<ArgumentException>(() => CustomMapValidator.ValidateStructure(map));
    }
}
