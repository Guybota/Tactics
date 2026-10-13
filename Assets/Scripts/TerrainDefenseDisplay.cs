using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class TerrainDefenseDisplay : MonoBehaviour
{
    public const int SlotCount = 3;
    public Image[] shields = new Image[SlotCount];
    public Sprite filledShield;
    public Sprite emptyShield;
    public int Stars { get; private set; }

    public void SetStars(int stars)
    {
        Stars = Mathf.Clamp(stars, 0, SlotCount);
        for (int slot = 0; slot < SlotCount; slot++)
        {
            if (shields == null || slot >= shields.Length || shields[slot] == null) continue;
            var image = shields[slot];
            image.sprite = slot < Stars ? filledShield : emptyShield;
            image.raycastTarget = false;
            image.gameObject.SetActive(true);
        }
    }
}
