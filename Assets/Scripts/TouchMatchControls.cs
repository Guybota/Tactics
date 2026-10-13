using UnityEngine;
using UnityEngine.UI;

public sealed class TouchMatchControls : MonoBehaviour
{
    private scr_game_contoller controller;
    public void Initialize(scr_game_contoller target)
    {
        controller = target;
    }
    private void LateUpdate()
    {
        if (MobileUiMode.Enabled)
            foreach (var root in new[] { controller.uiManager.buttons, controller.uiManager.main_menu })
                foreach (var button in root.GetComponentsInChildren<Button>(true))
                    for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                        if (button.onClick.GetPersistentMethodName(i) == "Finalize_turn") button.interactable = !controller.IsBotTurn && !controller.IsCombatAnimating && controller.status != "Over" && (!controller.IsOnlineMatch || controller.CanControlOnlineTurn);
    }
}
