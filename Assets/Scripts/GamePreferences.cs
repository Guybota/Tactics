using System;
using UnityEngine;

[Serializable]
public sealed class UserSettingsData
{
    public float musicVolume = 1, effectsVolume = 1;
    public float zoomSensitivity = 1, panSpeed = 1, menuScrollSensitivity = 1;
    public UserSettingsData Copy() => (UserSettingsData)MemberwiseClone();
}

public static class GamePreferences
{
    private const string Key = "Tactics.Settings.v1";
    private static UserSettingsData values = Read();
    public static event Action Changed;
    public static UserSettingsData Current => values.Copy();
    public static float MusicVolume => values.musicVolume;
    public static float EffectsVolume => values.effectsVolume;
    public static float ZoomSensitivity => values.zoomSensitivity;
    public static float PanSpeed => values.panSpeed;
    public static float MenuScrollSensitivity => values.menuScrollSensitivity;
    private static float Safe(float value, float min, float max) => float.IsNaN(value) || float.IsInfinity(value) ? 1 : Mathf.Clamp(value, min, max);
    private static UserSettingsData Read()
    {
        try
        {
            var data = JsonUtility.FromJson<UserSettingsData>(PlayerPrefs.GetString(Key, "")) ?? new UserSettingsData();
            data.musicVolume = Safe(data.musicVolume, 0, 1); data.effectsVolume = Safe(data.effectsVolume, 0, 1);
            data.zoomSensitivity = Safe(data.zoomSensitivity, .25f, 3); data.panSpeed = Safe(data.panSpeed, .25f, 3);
            data.menuScrollSensitivity = Safe(data.menuScrollSensitivity, .25f, 3);
            return data;
        }
        catch (ArgumentException) { return new UserSettingsData(); }
    }
    public static void Apply(UserSettingsData data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        values = data.Copy();
        values.musicVolume = Safe(values.musicVolume, 0, 1); values.effectsVolume = Safe(values.effectsVolume, 0, 1);
        values.zoomSensitivity = Safe(values.zoomSensitivity, .25f, 3); values.panSpeed = Safe(values.panSpeed, .25f, 3);
        values.menuScrollSensitivity = Safe(values.menuScrollSensitivity, .25f, 3);
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(values));
        Changed?.Invoke();
    }
    public static void SetMusic(float value) { var copy = Current; copy.musicVolume = value; Apply(copy); }
    public static void SetEffects(float value) { var copy = Current; copy.effectsVolume = value; Apply(copy); }
    public static void SetZoom(float value) { var copy = Current; copy.zoomSensitivity = value; Apply(copy); }
    public static void SetPan(float value) { var copy = Current; copy.panSpeed = value; Apply(copy); }
    public static void SetMenuScroll(float value) { var copy = Current; copy.menuScrollSensitivity = value; Apply(copy); }
    public static void ResetDefaults() => Apply(new UserSettingsData());
    public static void Save() => PlayerPrefs.Save();
}
