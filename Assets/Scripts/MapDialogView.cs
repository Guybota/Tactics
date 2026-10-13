using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MapDialogView : MonoBehaviour
{
    public RectTransform panel;
    public TMP_Text title, message;
    public TMP_InputField input;
    public Button primary, secondary, tertiary;
    private Action back;
    public void Show(string text, Action confirm, Action cancel = null, Action third = null)
    {
        if (message != null) message.text = text;
        Bind(primary, confirm); Bind(secondary, cancel); Bind(tertiary, third);
        back = cancel ?? Hide;
        GetComponent<UiBackAction>().Back = () => back();
        gameObject.SetActive(true); Canvas.ForceUpdateCanvases();
    }
    public void Hide() => gameObject.SetActive(false);
    private static void Bind(Button button, Action action)
    {
        if (button == null) return;
        button.onClick.RemoveAllListeners();
        if (action != null) button.onClick.AddListener(() => action());
        button.gameObject.SetActive(action != null);
    }
}
