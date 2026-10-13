using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SettingsMenu : MonoBehaviour
{
    [SerializeField] private Slider music, effects, zoom, pan, menuScroll;
    [SerializeField] private TextMeshProUGUI musicValue, effectsValue, zoomValue, panValue, menuScrollValue;
    private void Awake()
    {
        music.onValueChanged.AddListener(GamePreferences.SetMusic);
        effects.onValueChanged.AddListener(GamePreferences.SetEffects);
        zoom.onValueChanged.AddListener(GamePreferences.SetZoom);
        pan.onValueChanged.AddListener(GamePreferences.SetPan);
        menuScroll.onValueChanged.AddListener(GamePreferences.SetMenuScroll);
    }
    private void OnEnable() { GamePreferences.Changed += Refresh; Refresh(); }
    private void OnDisable() { GamePreferences.Changed -= Refresh; GamePreferences.Save(); }
    private void Refresh()
    {
        var values = GamePreferences.Current;
        music.SetValueWithoutNotify(values.musicVolume); effects.SetValueWithoutNotify(values.effectsVolume);
        zoom.SetValueWithoutNotify(values.zoomSensitivity); pan.SetValueWithoutNotify(values.panSpeed);
        menuScroll.SetValueWithoutNotify(values.menuScrollSensitivity);
        musicValue.text = Mathf.RoundToInt(values.musicVolume * 100) + "%";
        effectsValue.text = Mathf.RoundToInt(values.effectsVolume * 100) + "%";
        zoomValue.text = values.zoomSensitivity.ToString("0.00") + "x";
        panValue.text = values.panSpeed.ToString("0.00") + "x";
        menuScrollValue.text = values.menuScrollSensitivity.ToString("0.00") + "x";
    }
    public void ResetDefaults() => GamePreferences.ResetDefaults();
}
