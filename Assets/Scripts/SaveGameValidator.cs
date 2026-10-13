using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class SaveGameValidator
{
    public static void Validate(GameSave save, IDictionary<string, RaceClass> races)
    {
        if (save == null || save.controller == null || save.schemaVersion < 0 || save.schemaVersion > GameSave.CurrentVersion)
            Fail("Invalid or unsupported save format.");
        var state = save.controller;
        if (state.gameMap == null || state.teams == null || state.teams.Count < 2 || state.teams.Count > 4 || state.turn < 0 || state.turn > int.MaxValue - 4)
            Fail("Invalid match state.");
        if (save.schemaVersion >= 2 && state.randomState == 0) Fail("Missing match random state.");
        if (save.schemaVersion >= 2 && (state.stormWarmupRounds < 0 || state.stormRoundsPerShrink < 1)) Fail("Invalid storm cadence.");
        var map = state.gameMap;
        if (map.maxX < 1 || map.maxX > 50 || map.maxY < 1 || map.maxY > 50 || map.serializedTiles == null || map.serializedTiles.Count != map.maxX * map.maxY)
            Fail("Invalid map dimensions or incomplete map.");
        var tileCoordinates = new HashSet<int>();
        foreach (var tile in map.serializedTiles)
        {
            if (tile == null || !Inside(tile.position_x, tile.position_y, map) || !tileCoordinates.Add(tile.position_y * map.maxX + tile.position_x))
                Fail("Invalid or duplicate tile.");
            if (tile.tileName != "Grass field" && tile.tileName != "Forest" && tile.tileName != "Mountain")
                Fail("Unknown terrain: " + tile.tileName);
            if (tile.hasStorm && tile.hasCastle) Fail("A storm tile cannot contain a castle.");
        }
        var names = new HashSet<string>();
        var occupied = new HashSet<int>();
        foreach (var team in state.teams)
        {
            if (team == null || string.IsNullOrEmpty(team.teamName) || !names.Add(team.teamName) || team.units == null || team.money < 0)
                Fail("Invalid or duplicate team.");
            if (!Finite(team.teamColorR) || !Finite(team.teamColorG) || !Finite(team.teamColorB)) Fail("Invalid team color.");
            RaceClass race = ResolveRace(team, races, save.schemaVersion);
            if (race == null || race.Leader == null || race.AvailableUnits == null)
                Fail("Unknown race: " + team.raceClass);
            var ids = new HashSet<int>();
            int livingLeaders = 0;
            foreach (var unit in team.units)
            {
                if (unit == null || unit.id < 1 || unit.id >= int.MaxValue || !ids.Add(unit.id) || !Inside(unit.positionX, unit.positionY, map)
                    || !Finite(unit.maxHealth) || !Finite(unit.health) || unit.maxHealth <= 0 || unit.health < 0 || unit.health > unit.maxHealth
                    || unit.attack < 0 || unit.movement < 0 || unit.attackRange < 1)
                    Fail("Invalid unit state.");
                if (unit.hasDied || unit.health == 0) continue;
                if (!occupied.Add(unit.positionY * map.maxX + unit.positionX)) Fail("Multiple units occupy a tile.");
                if (unit.isKing) livingLeaders++;
                GameObject prefab = ResolveUnit(unit, race, save.schemaVersion);
                if (prefab == null || prefab.GetComponent<Unit>() == null) Fail("Unknown unit: " + unit.unitName);
            }
            if (!team.hasLost && livingLeaders != 1) Fail("An active team must have exactly one living leader.");
            if (save.schemaVersion >= 2 && (team.nextUnitId < 1 || team.units.Exists(u => u.id >= team.nextUnitId)))
                Fail("Invalid next unit ID.");
        }
        foreach (var tile in map.serializedTiles)
            if (!string.IsNullOrEmpty(tile.castleTeam) && (!tile.hasCastle || !names.Contains(tile.castleTeam))) Fail("Invalid castle owner.");
    }
    public static RaceClass ResolveRace(SerializableTeam team, IDictionary<string, RaceClass> races, int version)
    {
        if (version >= 2)
        {
            foreach (var race in races.Values) if (race != null && race.Id == team.raceId && !string.IsNullOrEmpty(team.raceId)) return race;
            return null;
        }
        return !string.IsNullOrEmpty(team.raceClass) && races.TryGetValue(team.raceClass, out var legacy) ? legacy : null;
    }
    public static GameObject ResolveUnit(SerializableUnit unit, RaceClass race, int version)
    {
        if (version < 2) return unit.isKing ? race.Leader : race.AvailableUnits.Find(p => p != null && p.GetComponent<Unit>().unit_name == unit.unitName);
        if (string.IsNullOrEmpty(unit.unitTypeId)) return null;
        if (unit.isKing) return race.Leader != null && UnityMatchAdapter.TypeId(race.Leader.GetComponent<Unit>()) == unit.unitTypeId ? race.Leader : null;
        return race.AvailableUnits.Find(p => p != null && UnityMatchAdapter.TypeId(p.GetComponent<Unit>()) == unit.unitTypeId);
    }
    private static bool Inside(int x, int y, SerializableMap map) => x >= 0 && y >= 0 && x < map.maxX && y < map.maxY;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Fail(string message) { throw new InvalidDataException(message); }
}
