using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Castle : MonoBehaviour
{
    public SpriteRenderer castle_base;
    public SpriteRenderer castle_roof;

    public Team current_team;

    public void Capture_castle(Team team)
    {
        current_team = team;
        castle_roof.color = team == null ? Color.white : new Color(team.team_color[0] + 0.1f, team.team_color[1] + 0.1f, team.team_color[2] + 0.1f, 1);

    }
}
