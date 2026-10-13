using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Tactics.Core;

// Native uGUI components shared by the authoring toolbar and map library.
public static class CustomMapUi
{
    public static readonly Color Ink = new Color(.18f, .12f, .08f);
    public static readonly Color Paper = new Color(.92f, .82f, .64f);
    public static Color PlayerColor(int slot)
    {
        var colors = new[] { new Color(.43867922f, .6896228f, 1), new Color(1, .33921948f, .2971698f),
            new Color(.19607843f, .9254902f, .54901963f), new Color(.9137255f, .7529412f, .3137255f) };
        return colors[slot];
    }
    public static RectTransform Rect(Transform parent, string name, Vector2 size, Vector2 position, Vector2? anchor = null)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = anchor ?? new Vector2(.5f, .5f);
        rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
    }
    public static void Stretch(RectTransform rect, float margin = 0)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * margin; rect.offsetMax = -Vector2.one * margin;
    }
    public static Image Background(RectTransform rect, Color color, Sprite sprite = null)
    {
        var image = rect.gameObject.AddComponent<Image>(); image.color = color;
        if (sprite != null) { image.sprite = sprite; image.type = Image.Type.Sliced; }
        return image;
    }
    public static Image PaperBackground(RectTransform rect)
        => Background(rect, Color.white, TacticsUiSkin.Current.Panel);
    public static TMP_Text Label(Transform parent, string name, string text, Vector2 size, Vector2 position, float fontSize = 30)
    {
        var label = Rect(parent, name, size, position).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Jersey10-Regular SDF") ?? TMP_Settings.defaultFontAsset;
        label.text = text; label.fontSize = fontSize; label.color = Ink; label.raycastTarget = false;
        label.richText = false; label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true; label.fontSizeMin = Mathf.Min(20, fontSize); label.fontSizeMax = fontSize;
        return label;
    }
    public static Button Button(Transform parent, string name, string text, Vector2 size, Vector2 position, UnityAction action, Sprite sprite = null)
    {
        var rect = Rect(parent, name, size, position); var image = Background(rect, Paper, sprite);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors; colors.highlightedColor = new Color(1, .94f, .78f); colors.pressedColor = new Color(.75f, .63f, .45f);
        button.colors = colors;
        button.onClick.AddListener(action);
        var label = Label(rect, "Label", text, size, Vector2.zero, 30); Stretch(label.rectTransform, 4);
        var outline = rect.gameObject.AddComponent<Outline>(); outline.effectColor = Ink; outline.effectDistance = new Vector2(1, -1);
        if (MobileUiMode.Enabled) rect.gameObject.AddComponent<MobileTouchTarget>();
        return button;
    }
    public static TMP_InputField NameInput(Transform parent, Vector2 size, Vector2 position)
    {
        var rect = Rect(parent, "Map name", size, position); Background(rect, Color.clear);
        var field = rect.gameObject.AddComponent<TMP_InputField>(); field.characterLimit = 64;
        var viewport = Rect(rect, "Text Area", size, Vector2.zero); Stretch(viewport, 8); viewport.gameObject.AddComponent<RectMask2D>();
        var text = Label(viewport, "Text", "", size, Vector2.zero, 30); Stretch(text.rectTransform);
        text.alignment = TextAlignmentOptions.MidlineLeft; text.enableAutoSizing = false;
        var placeholder = Label(viewport, "Placeholder", "Map name", size, Vector2.zero, 30); Stretch(placeholder.rectTransform);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft; placeholder.color = new Color(.4f, .3f, .2f);
        field.textViewport = viewport; field.textComponent = (TextMeshProUGUI)text; field.placeholder = placeholder;
        return field;
    }
}
