using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Tactics.Core;

public class Unit : MonoBehaviour
{
    public string unit_name;
    public GameObject health_bar;
    public SpriteRenderer sprite;
    public SpriteRenderer color;
    public bool is_king;
    public int id = 0;
    public Team team;
    public bool has_moved;
    public bool has_died = false;

    public Sprite firstSprite;
    public Sprite firstSpriteC;
    public Animator animator1;
    public Animator animator2;
    public AnimationClip attackAnimation;
    public AnimationClip hurtAnimation;
    public AnimationClip deathAnimation;
    private bool walking;

    public int unit_cost;
    public float max_health;
    public float hp;
    public int attack;
    public int movement;
    public int attack_range;
    [SerializeField] private UnitCombatDefinition combatDefinition;
    [SerializeField] private CombatDamageTable damageTable;
    public UnitCombatDefinition CombatDefinition => combatDefinition;

    public int movDir = 1;

    public Tile current_tile;
    public Tile target_attack_tile;
    public List<Tile> current_path = new List<Tile>();
    public scr_camera game_camera;
    private bool healthBarInitialized;
    private bool leaderBonusApplied;
    public bool IsMoving => current_path.Count > 0;

    // Start is called before the first frame update
    void Start()
    {
        if (animator1 == null && sprite != null) animator1 = sprite.GetComponent<Animator>();
        if (animator2 == null && color != null) animator2 = color.GetComponent<Animator>();
        PlayVisualState("Idle");
        firstSprite = sprite.sprite;
        firstSpriteC = color.sprite;
        UpdateHealth();
    }

    // Update is called once per frame
    void Update()
    {
        if (current_path.Count > 0)
        {
            SetWalking(true);

            if (Vector3.Distance(transform.position, current_path[0].transform.position) > 1)
            {
                game_camera.Follow_unit(this);
                game_camera.Lock_camera();

                if (transform.position.x - current_path[0].transform.position.x > 0 && movDir != -1)
                {
                    sprite.flipX = true;
                    color.flipX = true;
                    movDir = -1;
                }
                else if(transform.position.x - current_path[0].transform.position.x < 0 && movDir != 1)
                {
                    sprite.flipX = false;
                    color.flipX = false;
                    movDir = 1;
                }

                transform.position = Vector3.MoveTowards(transform.position, current_path[0].transform.position, 400f * Time.deltaTime);
            }
            else
            {
                transform.position = current_path[0].transform.position;
                current_path.RemoveAt(0);
                if (current_path.Count == 0)
                {
                    game_camera.Clear_follow();
                    SetWalking(false);
                }
            }
        }
    }

    public void Eliminate()
    {
        if (has_died) return;
        // Confirmed combat already owns a visual copy. Other deaths (including the storm)
        // get a copy here before the gameplay object is immediately deactivated.
        if (sprite != null && !sprite.forceRenderingOff && deathAnimation != null)
            UnitReactionPresentation.PlayDeath(this);
        hp = 0;
        has_died = true;
        StopMovement();
        if (current_tile != null && current_tile.unit == gameObject) current_tile.unit = null;
        if (healthBarInitialized && health_bar != null) Destroy(health_bar);
        gameObject.SetActive(false);
        if (team != null) team.Check_loss();
    }

    public void StopMovement()
    {
        current_path.Clear();
        SetWalking(false);
        if (game_camera != null && game_camera.unit == this) game_camera.Clear_follow();
    }

    public void SetWalking(bool value)
    {
        if (walking == value) return;
        walking = value;
        PlayVisualState(value ? "Walk" : "Idle");
    }
    public void PlayVisualState(string state)
    {
        foreach (var animator in new[] { animator1, animator2 })
            if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null
                && animator.HasState(0, Animator.StringToHash(state))) animator.Play(state, 0, 0);
    }
    public void PresentHealthChange(float health)
    {
        if (health >= hp || health <= 0 || sprite == null || sprite.forceRenderingOff) return;
        PlayVisualState("Hurt");
    }

    public Combatant CombatStats => new Combatant(hp, max_health, attack, attack_range,
        current_tile == null ? 0 : current_tile.DefenseStars);

    private float BaseDamageAgainst(Unit defender)
    {
        if (damageTable == null || combatDefinition == null || defender.combatDefinition == null)
            return attack / defender.max_health * 100;
        // Preserve leader/stat bonuses on top of the matchup's baseline.
        return damageTable.GetBaseDamage(combatDefinition, defender.combatDefinition)
            * (attack / (float)combatDefinition.BaselineAttack)
            * (defender.combatDefinition.BaselineHealth / defender.max_health);
    }

    public float BaselineDamageAgainst(Unit defender)
        => damageTable != null && combatDefinition != null && defender.combatDefinition != null
            ? damageTable.GetBaseDamage(combatDefinition, defender.combatDefinition)
            : attack / defender.max_health * 100;

    public CombatPrediction PredictCombat(Unit defender, int distance)
        => CombatRules.Predict(CombatStats, defender.CombatStats, distance, BaseDamageAgainst(defender), defender.BaseDamageAgainst(this));

    public CombatPrediction PredictCombatFrom(Unit defender, int distance, Tile destination)
    {
        var attacker = new Combatant(hp, max_health, attack, attack_range,
            destination == null ? CombatStats.TerrainStars : destination.DefenseStars);
        return CombatRules.Predict(attacker, defender.CombatStats, distance, BaseDamageAgainst(defender), defender.BaseDamageAgainst(this));
    }

    public CombatResult ResolveCombat(Unit defender, int distance, int attackLuck, int counterLuck)
        => CombatRules.Resolve(CombatStats, defender.CombatStats, distance,
            BaseDamageAgainst(defender), defender.BaseDamageAgainst(this), attackLuck, counterLuck);

    public void RestoreLeaderStatus(bool leader)
    {
        is_king = leader;
        leaderBonusApplied = leader;
    }

    public void Attack_unit(Unit unit, int attack_distance)
    {
        CombatResult result = ResolveCombat(unit, attack_distance, UnityEngine.Random.Range(0, 10), UnityEngine.Random.Range(0, 10));
        unit.Take_damage(result.Damage);
        Take_damage(result.CounterDamage);
    }

    public void Take_damage(float damage)
    {
        if (has_died) return;
        if (damage < 0 || float.IsNaN(damage) || float.IsInfinity(damage)) throw new ArgumentOutOfRangeException(nameof(damage));
        PresentHealthChange(Mathf.Max(0, hp - damage));
        hp = Mathf.Clamp(hp - damage, 0, max_health);
        UpdateHealth();
        if (hp <= 0) Eliminate();
    }

    public void Heal(float heal)
    {
        if (has_died || hp <= 0) return;
        hp = Mathf.Clamp(hp + Mathf.Max(0, heal), 0, max_health);
        UpdateHealth();
    }

    public void UpdateHealth()
    {
        if (has_died || health_bar == null) return;
        if (!healthBarInitialized)
        {
            health_bar = Instantiate(health_bar, transform);
            health_bar.transform.localPosition = new Vector3(-16, -20, 0);
            healthBarInitialized = true;
        }
        health_bar.GetComponent<Health_bar>().Update_health(hp, max_health);
    }

    public void Move_along_path(List<Tile> path, scr_camera scr_Camera)
    {
        current_path.Clear();
        foreach(Tile tile in path)
        {
            current_path.Add(tile);
        }
        game_camera = scr_Camera;
    }

    public void Finalize_turn()
    {
        has_moved = true;
        color.color = new Color(color.color.r * .6f, color.color.g * .6f, color.color.b * .6f, 1);
        sprite.color = Color.gray;
    }

    public void Initialize_turn()
    {
        if (has_died) return;
        has_moved = false;
        color.color = team.team_color;
        sprite.color = Color.white;
    }

    public void Change_current_tile(Tile tile)
    {
        if (tile == null) throw new ArgumentNullException(nameof(tile));
        if (tile.unit != null && tile.unit != gameObject) throw new InvalidOperationException("A unit cannot finish movement on an occupied tile.");
        if (current_tile != null && current_tile.unit == gameObject) current_tile.unit = null;
        current_tile = tile;
        tile.unit = gameObject;
        transform.position = tile.transform.position;
    }

    public void Set_king(bool option)
    {
        is_king = option;
        if (!option || leaderBonusApplied) return;
        max_health = LeaderStats.Health(max_health);
        hp = LeaderStats.Health(hp);
        attack = LeaderStats.Attack(attack);
        leaderBonusApplied = true;
    }

    public void Set_team(Team team) 
    { 
        this.team = team;
        color.color = team.team_color;
    }

    public float Calculate_strength()
    {
        return CombatRules.Strength(hp, max_health);
    }

    /**
    public Move Decide_move(Map map)
    {
        //check enemies attack range
        map.Clear_possible_damage();
        foreach (Team enemy_team in map.teams)
        {
            if (enemy_team.team_name != team.team_name)
            {
                foreach(Unit unit in enemy_team.units)
                {
                    map.Find_selectable_tiles(unit.current_tile, unit.movement, unit.attack_range);
                    for (int i = 0; i < map.max_x; i++)
                    {
                        for (int j = 0; j < map.max_y; j++)
                        {
                            if (map.tiles[i, j].state == "Can_move" || map.tiles[i, j].state == "Can_attack")
                            {
                                map.tiles[i, j].possible_damage += unit.attack * unit.Calculate_strength();
                            }
                        }
                    }
                    map.Clear_board();
                }
            }
        }
        //select unit
        map.Select_tile(current_tile, team);
        List<Tile> possible_tiles = new List<Tile>();
        for (int i = 0; i < map.max_x; i++) 
        { 
            for (int j = 0; j < map.max_y; j++)
            {
                if (map.tiles[i, j].state == "Can_move" && map.tiles[i, j].unit == null)
                {
                    possible_tiles.Add(map.tiles[i, j]);
                }
            }
        }
    }
    **/
}
