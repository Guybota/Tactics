using System;
using System.Collections.Generic;

[System.Serializable]
public class SaveManager
{
    public List<SaveInfo> Saves = new List<SaveInfo>();
}

[System.Serializable]
public class SaveInfo
{
    public string saveName;
    public SerializeableDateTime date;

    public SaveInfo(GameSave gameSave) 
    { 
        saveName = gameSave.saveName;
        date = gameSave.date;
    }
}

[System.Serializable]
public class SerializeableDateTime
{
    public int day;
    public int month;
    public int year;
    public int hour;
    public int minute;

    public SerializeableDateTime(DateTime date)
    {
        day = date.Day;
        month = date.Month;
        year = date.Year;
        hour = date.Hour;
        minute = date.Minute;
    }

    public override string ToString()
    {
        string minuteString = minute.ToString();
        if (minute < 10)
        {
            minuteString = "0" + minute.ToString();
        }
        return day.ToString() + "/" + month.ToString() + "/" + year.ToString() + " - " + hour.ToString() + ":" + minuteString;
    }
}

[System.Serializable]
public class GameSave
{
    public int schemaVersion;
    public SerializableGameController controller;
    public string saveName;
    public SerializeableDateTime date;

    public GameSave(scr_game_contoller gameController, string saveName)
    {
        schemaVersion = 1;
        controller = new SerializableGameController(gameController);
        this.saveName = saveName;
        date = new SerializeableDateTime(DateTime.Now);
    }
}

[System.Serializable]
public class SerializableGameController
{
    public int turn;
    public List<SerializableTeam> teams;
    public SerializableMap gameMap;
    public bool build_mode;
    public bool battle_royale_mode;

    // Constructor to convert scr_game_contoller instance to SerializableGameController
    public SerializableGameController(scr_game_contoller gameController)
    {
        turn = gameController.turn;
        teams = new List<SerializableTeam>();
        foreach (Team team in gameController.teams)
        {
            teams.Add(new SerializableTeam(team));
        }
        gameMap = new SerializableMap(gameController.gameMap);
        build_mode = MainMenu.build_mode;
        battle_royale_mode = MainMenu.battle_royale_mode;
    }
}

[System.Serializable]
public class SerializableTeam
{
    public string teamName;
    public string raceClass;
    public float teamColorR;
    public float teamColorG;
    public float teamColorB;
    public List<SerializableUnit> units;
    public int money;
    public bool hasLost;

    // Constructor to convert Team instance to SerializableTeam
    public SerializableTeam(Team team)
    {
        teamName = team.team_name;
        raceClass = team.raceClass.className;
        teamColorR = team.team_color.r;
        teamColorG = team.team_color.g;
        teamColorB = team.team_color.b;
        money = team.money;
        hasLost = team.has_lost;

        units = new List<SerializableUnit>();
        foreach (Unit unit in team.units)
        {
            units.Add(new SerializableUnit(unit));
        }
    }
}

[System.Serializable]
public class SerializableUnit
{
    public string unitName;
    public bool isKing;
    public int id;
    public bool hasMoved;
    public bool hasDied;
    public float maxHealth;
    public float health;
    public int attack;
    public int movement;
    public int attackRange;
    public int positionX;
    public int positionY;

    // Constructor to convert Unit instance to SerializableUnit
    public SerializableUnit(Unit unit)
    {
        unitName = unit.unit_name;
        isKing = unit.is_king;
        id = unit.id;
        hasMoved = unit.has_moved;
        hasDied = unit.has_died;
        maxHealth = unit.max_health;
        health = unit.hp;
        attack = unit.attack;
        movement = unit.movement;
        attackRange = unit.attack_range;
        positionX = unit.current_tile.position_x;
        positionY = unit.current_tile.position_y;
    }
}

[System.Serializable]
public class SerializableMap
{
    public int maxX;
    public int maxY;
    public List<SerializableTile> serializedTiles;

    // Constructor to convert Map instance to SerializableMap
    public SerializableMap(Map map)
    {
        maxX = map.max_x;
        maxY = map.max_y;
        serializedTiles = new List<SerializableTile>();

        // Serialize tiles
        for (int x = 0; x < maxX; x++)
        {
            for (int y = 0; y < maxY; y++)
            {
                Tile tile = map.tiles[x, y];
                if (tile != null)
                {
                    SerializableTile serializedTile = new SerializableTile(tile);
                    serializedTile.position_x = x;
                    serializedTile.position_y = y;
                    serializedTiles.Add(serializedTile);
                }
            }
        }
    }

    public SerializableTile GetTileAtPosition(int x, int y)
    {
        if (x >= 0 && x < maxX && y >= 0 && y < maxY)
        {
            foreach (SerializableTile tile in serializedTiles)
            {
                if (tile.position_x == x && tile.position_y == y)
                    return tile;
            }
        }

        return null;
    }
}


[System.Serializable]
public class SerializableTile
{
    public string tileName;
    public bool hasStorm;
    public bool hasCastle;
    public string castleTeam;
    public int position_x;
    public int position_y;

    // Constructor to convert Tile instance to SerializableTile
    public SerializableTile(Tile tile)
    {
        tileName = tile.tile_name;

        hasStorm = tile.has_storm;

        hasCastle = tile.has_castle;

        if (hasCastle && tile.castle.GetComponent<Castle>().current_team != null)
        {
            castleTeam = tile.castle.GetComponent<Castle>().current_team.team_name;
        } else
        {
            castleTeam = "";
        }

        position_x = tile.position_x;
        position_y = tile.position_y;
    }
}


