using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Team : MonoBehaviour
{
    public string team_name;
    public Color team_color;
    public List<Unit> units = new List<Unit>();
    public int money;
    public bool has_lost;
    public bool isBot;
    public RaceClass raceClass;
    private int nextUnitId = 1;
    public int NextUnitId
    {
        get
        {
            int maximum = 0;
            foreach (var unit in units) if (unit != null) maximum = System.Math.Max(maximum, unit.id);
            return System.Math.Max(nextUnitId, checked(maximum + 1));
        }
    }
    public void RestoreNextUnitId(int value) { nextUnitId = System.Math.Max(value, NextUnitId); }





    public void Add(Unit unit)
    {
        if (unit == null || units.Contains(unit)) return;
        if (unit.id <= 0 || units.Exists(existing => existing != null && existing.id == unit.id))
        {
            unit.id = NextUnitId;
        }
        nextUnitId = System.Math.Max(nextUnitId, checked(unit.id + 1));
        unit.Set_team(this);
        units.Add(unit);
    }

    public void Remove(Unit unit)
    {
        units.Remove(unit);
    }

    public bool Check_loss()
    {
        Unit king = Get_king();
        if (king != null && (king.has_died || king.hp <= 0)) has_lost = true;
        return has_lost;
    }

    public bool Is_in_team(Unit unit)
    {
        if (unit != null && unit.team == this)
            return true;

        return false;
    }
    public Unit Get_unit(int unit_id)
    {
        foreach (Unit unit in units)
        {
            if (unit.id == unit_id)
                return unit;
        }
        return null;
    }
    public Unit Get_king()
    {
        foreach (Unit unit in units)
        {
            if (unit.is_king)
                return unit;
        }
        return null;
    }
}
