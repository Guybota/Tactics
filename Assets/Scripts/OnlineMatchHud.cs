using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Scene-owned visuals. Runtime binding never rebuilds or changes their geometry/style.
public sealed class OnlineMatchHud : MonoBehaviour
{
    [SerializeField] private TMP_Text status;
    [SerializeField] private Button refresh, retry, copy;
    private scr_game_contoller controller;
    public TMP_Text Status => status;
    public Button Refresh => refresh;
    public Button Retry => retry;
    public void Bind(scr_game_contoller target)
    {
        if (status == null || refresh == null || retry == null || copy == null)
            throw new InvalidOperationException("The editable Online HUD references are missing.");
        controller = target;
        gameObject.SetActive(true);
    }
    public async void RefreshFromButton() { if (controller != null) await controller.RefreshOnlineAsync(); }
    public async void RetryFromButton() { if (controller != null) await controller.RetryOnlineAsync(); }
    public void CopyFromButton() { if (controller != null) controller.CopyOnlineInvitation(); }
}
