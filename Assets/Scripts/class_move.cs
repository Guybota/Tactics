using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Move : MonoBehaviour
{
    public List<Tile> path = new List<Tile>();
    public bool is_attack;
    public Tile attack_target;
}
