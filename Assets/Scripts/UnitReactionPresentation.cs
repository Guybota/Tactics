using UnityEngine;

// Deaths outside a combat sequence (storm, defeat or legacy damage) need their own
// visual host because the eliminated Unit is no longer active.
public sealed class UnitReactionPresentation : MonoBehaviour
{
    private CombatPresentation.Visual visual;
    private scr_game_contoller controller;
    private float elapsed;
    public static void PlayDeath(Unit unit)
    {
        var root = new GameObject("Unit death presentation");
        root.transform.SetParent(unit.transform.parent, false);
        var reaction = root.AddComponent<UnitReactionPresentation>();
        reaction.controller = Object.FindFirstObjectByType<scr_game_contoller>();
        reaction.visual = new CombatPresentation.Visual(unit, root.transform, "Death visual");
        reaction.visual.BeginState("Death");
    }
    private void Update()
    {
        if (visual == null || Time.timeScale <= 0 || LandscapeOrientation.InputBlocked) return;
        if (controller != null && (controller.status == "Menu" || controller.status == "Saving" || controller.status == "Loading")) return;
        float step = Mathf.Min(Time.deltaTime, visual.Duration("Death") - elapsed);
        visual.Tick(step); elapsed += step;
        if (elapsed >= visual.Duration("Death"))
        {
            visual.Dispose(); visual = null;
            Destroy(gameObject);
        }
    }
    private void OnDisable() { visual?.Dispose(); visual = null; }
}
