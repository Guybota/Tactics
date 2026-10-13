using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Keep the caption on the raised face, including the pressed/selected sprite offsets.
public sealed class PaperButtonLabel : MonoBehaviour
{
    private Image image;
    private TMP_Text label;
    private RectTransform icon;
    private Vector2 originalIconPosition;
    private Vector4 originalMargin;
    private Sprite lastSprite;
    private float lastPixelsPerUnit;
    private void Awake()
    {
        image = GetComponent<Button>().targetGraphic as Image;
        foreach (var text in GetComponentsInChildren<TMP_Text>(true))
            if (text.transform.parent == transform && text.rectTransform.anchorMin == Vector2.zero && text.rectTransform.anchorMax == Vector2.one)
            { label = text; label.text = label.text.TrimEnd('\r', '\n'); originalMargin = text.margin; break; }
        foreach (var graphic in GetComponentsInChildren<Image>(true))
            if (graphic.transform.parent == transform && graphic.sprite != null && graphic.sprite.name == "Trash Can Pixel")
            { icon = graphic.rectTransform; originalIconPosition = icon.anchoredPosition; break; }
    }
    private void OnEnable() { lastSprite = null; Refresh(); }
    private void OnDisable()
    {
        if (label != null) label.margin = originalMargin;
        if (icon != null) icon.anchoredPosition = originalIconPosition;
    }
    private void LateUpdate() => Refresh();
    public void Refresh()
    {
        if (image == null || label == null && icon == null) return;
        var sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
        if (sprite == null) return;
        float pixelsPerUnit = image.pixelsPerUnit * image.pixelsPerUnitMultiplier;
        if (sprite == lastSprite && Mathf.Approximately(pixelsPerUnit, lastPixelsPerUnit)) return;
        float offset = (sprite.border.y - sprite.border.w) / (2 * Mathf.Max(.001f, pixelsPerUnit));
        // Symmetric positive/negative margins shift the caption without shrinking it.
        if (label != null) label.margin = originalMargin + new Vector4(0, -offset, 0, offset);
        if (icon != null) icon.anchoredPosition = originalIconPosition + Vector2.up * offset;
        lastSprite = sprite; lastPixelsPerUnit = pixelsPerUnit;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    { SceneManager.sceneLoaded -= Install; SceneManager.sceneLoaded += Install; }
    private static void Install(Scene scene, LoadSceneMode mode)
    {
        foreach (var root in scene.GetRootGameObjects()) foreach (var button in root.GetComponentsInChildren<Button>(true))
        {
            var image = button.targetGraphic as Image;
            if (image != null && image.sprite != null && image.sprite.name.StartsWith("UI_Paper_Button_Small_Lock_01a")
                && button.GetComponent<PaperButtonLabel>() == null) button.gameObject.AddComponent<PaperButtonLabel>();
        }
    }
}
