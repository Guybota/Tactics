using System;
using UnityEngine;
using UnityEngine.UI;

// Asset references are included in builds and shared by dynamically created menus.
[CreateAssetMenu(menuName = "Tactics/UI Skin")]
public sealed class TacticsUiSkin : ScriptableObject
{
    [SerializeField] private Sprite buttonNormal, buttonHighlighted, buttonPressed, buttonSelected, panel;
    private static TacticsUiSkin cached;
    public static TacticsUiSkin Current => cached != null ? cached : cached = Resources.Load<TacticsUiSkin>("UI/TacticsUiSkin")
        ?? throw new InvalidOperationException("The Tactics UI skin asset is missing.");
    public Sprite Panel => panel;

    public void ApplyButton(Button button)
    {
        var image = button.targetGraphic as Image;
        if (image == null) return;
        image.sprite = buttonNormal;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1;
        var rect = (RectTransform)button.transform;
        if (rect.rect.height < 70) rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 70);
        button.transition = Selectable.Transition.SpriteSwap;
        button.spriteState = new SpriteState
        {
            highlightedSprite = buttonHighlighted,
            pressedSprite = buttonPressed,
            selectedSprite = buttonSelected
        };
    }
}
