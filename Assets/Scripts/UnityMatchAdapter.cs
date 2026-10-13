using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Tactics.Core;

// Migration boundary between the existing scene components and the scene-independent match.
public sealed class UnityMatchAdapter
{
    private readonly scr_game_contoller controller;
    private readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
    public MatchEngine Engine { get; }
    public static string TypeId(Unit unit) => unit.CombatDefinition != null ? unit.CombatDefinition.Id : "legacy." + unit.unit_name;

    public UnityMatchAdapter(scr_game_contoller controller, IEnumerable<RaceClass> races)
    {
        this.controller = controller;
        var content = UnityMatchContent.Build(races.Distinct().Select(UnityMatchContent.Roster.From), out var assets);
        foreach (var asset in assets) prefabs.Add(asset.Key, asset.Value);
        Engine = new MatchEngine(content);
    }

    public MatchState Capture()
    {
        var map = controller.gameMap;
        var state = new MatchState(map.max_x, map.max_y)
        {
            Turn = Math.Max(0, controller.turn), BuildMode = MainMenu.build_mode,
            BattleRoyaleMode = MainMenu.battle_royale_mode, IsOver = controller.status == "Over",
            RandomState = controller.RandomState, StormWarmupRounds = map.stormWarmupRounds,
            StormRoundsPerShrink = map.stormRoundsPerShrink
        };
        foreach (var team in controller.teams)
        {
            var player = new PlayerState { RaceId = team.raceClass.Id, Money = team.money,
                HasLost = team.has_lost, NextUnitId = team.NextUnitId };
            foreach (var unit in team.units)
            {
                if (unit == null) continue;
                player.Units.Add(new UnitState { Id = unit.id, TypeId = TypeId(unit),
                    Position = new GridPosition(unit.current_tile.position_x, unit.current_tile.position_y),
                    Health = unit.hp, MaxHealth = unit.max_health, Attack = unit.attack, Movement = unit.movement,
                    Range = unit.attack_range, IsLeader = unit.is_king, IsDead = unit.has_died, HasActed = unit.has_moved });
            }
            state.Players.Add(player);
        }
        for (int x = 0; x < state.Width; x++) for (int y = 0; y < state.Height; y++)
        {
            var tile = map.tiles[x, y];
            state.Cells[x, y] = new CellState { Terrain = tile.mountain != null ? TerrainKind.Mountain : tile.forest != null ? TerrainKind.Forest : TerrainKind.Grass,
                MovementCost = Math.Max(1, tile.movement_cost), SupportsCastle = tile.castle != null,
                HasStorm = tile.has_storm, HasCastle = tile.has_castle,
                CastleOwner = tile.has_castle && tile.castle != null ? controller.teams.IndexOf(tile.castle.GetComponent<Castle>().current_team) : -1 };
        }
        if (state.IsOver)
        {
            var survivors = state.Players.Select((p, i) => new { p, i }).Where(e => !e.p.HasLost).ToArray();
            state.Winner = survivors.Length == 1 ? survivors[0].i : -1;
        }
        return state;
    }

    public UnitKey Key(Unit unit) => new UnitKey(controller.teams.IndexOf(unit.team), unit.id);

    public void ApplyEliminations(MatchState state)
    {
        for (int index = 0; index < state.Players.Count; index++)
        {
            var team = controller.teams[index];
            team.has_lost = state.Players[index].HasLost;
            foreach (var unit in team.units)
            {
                if (unit == null) continue;
                var data = state.FindUnit(new UnitKey(index, unit.id));
                if (data != null && !data.IsAlive) unit.Eliminate();
            }
        }
        for (int x = 0; x < state.Width; x++) for (int y = 0; y < state.Height; y++)
        {
            var tile = controller.gameMap.tiles[x, y];
            if (tile.has_castle && tile.castle != null && state.Cells[x, y].CastleOwner < 0
                && tile.castle.GetComponent<Castle>().current_team != null)
                tile.castle.GetComponent<Castle>().Capture_castle(null);
        }
    }

    public void Apply(MatchState state)
    {
        var map = controller.gameMap;
        if (state.Width != map.max_x || state.Height != map.max_y || state.Players.Count != controller.teams.Count)
            throw new InvalidOperationException("Projection requires the existing match dimensions and participants.");
        var existing = new Dictionary<UnitKey, Unit>();
        for (int player = 0; player < controller.teams.Count; player++)
            foreach (var unit in controller.teams[player].units) if (unit != null) existing.Add(new UnitKey(player, unit.id), unit);
        // Check all required assets before changing the live board.
        for (int player = 0; player < state.Players.Count; player++) foreach (var unit in state.Players[player].Units)
            if (unit.IsAlive && !existing.ContainsKey(new UnitKey(player, unit.Id)) && !prefabs.ContainsKey(unit.TypeId))
                throw new InvalidOperationException("Unknown unit asset: " + unit.TypeId);
        for (int x = 0; x < state.Width; x++) for (int y = 0; y < state.Height; y++)
            if (state.Cells[x, y].HasCastle && map.tiles[x, y].castle == null) throw new InvalidOperationException("Tile does not support castles.");

        foreach (var tile in map.tiles) tile.unit = null;
        for (int x = 0; x < state.Width; x++) for (int y = 0; y < state.Height; y++)
        {
            var tile = map.tiles[x, y]; var cell = state.Cells[x, y];
            if (cell.HasStorm) tile.Activate_storm();
            tile.has_storm = cell.HasStorm;
            tile.has_castle = cell.HasCastle;
            if (tile.castle != null)
            {
                tile.castle.SetActive(cell.HasCastle);
                tile.castle.GetComponent<Castle>().Capture_castle(cell.HasCastle && cell.CastleOwner >= 0 ? controller.teams[cell.CastleOwner] : null);
            }
        }
        for (int player = 0; player < state.Players.Count; player++)
        {
            var data = state.Players[player]; var team = controller.teams[player];
            foreach (var saved in data.Units)
            {
                var key = new UnitKey(player, saved.Id);
                bool created = !existing.TryGetValue(key, out var unit);
                if (created && !saved.IsAlive) continue;
                if (created)
                {
                    unit = UnityEngine.Object.Instantiate(prefabs[saved.TypeId], map.unitsRoot).GetComponent<Unit>();
                    unit.id = saved.Id;
                    team.Add(unit);
                }
                unit.StopMovement();
                unit.PresentHealthChange(saved.Health);
                unit.max_health = saved.MaxHealth; unit.hp = saved.Health;
                unit.attack = saved.Attack; unit.movement = saved.Movement; unit.attack_range = saved.Range;
                unit.RestoreLeaderStatus(saved.IsLeader);
                if (!saved.IsAlive) { unit.Eliminate(); continue; }
                var tile = map.tiles[saved.Position.X, saved.Position.Y];
                unit.Change_current_tile(tile);
                unit.transform.position = tile.transform.position;
                unit.target_attack_tile = null;
                if (saved.HasActed) { if (created || !unit.has_moved) unit.Finalize_turn(); }
                else unit.Initialize_turn();
                unit.UpdateHealth();
            }
            team.money = data.Money; team.has_lost = data.HasLost; team.RestoreNextUnitId(data.NextUnitId);
        }
        controller.turn = state.Turn;
    }
}
