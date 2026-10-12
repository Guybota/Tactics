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
    public RaceClass raceClass;





    public void Add(Unit unit)
    {
        if (unit == null || units.Contains(unit)) return;
        if (unit.id <= 0 || units.Exists(existing => existing != null && existing.id == unit.id))
        {
            int maximum = 0;
            foreach (Unit existing in units) if (existing != null) maximum = System.Math.Max(maximum, existing.id);
            unit.id = maximum + 1;
        }
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
