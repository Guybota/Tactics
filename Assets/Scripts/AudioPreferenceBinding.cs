using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
public sealed class AudioPreferenceBinding : MonoBehaviour
{
    [SerializeField] private bool music = true;
    private AudioSource source;
    private void OnEnable()
    {
        source = GetComponent<AudioSource>();
        GamePreferences.Changed += Apply;
        Apply();
    }
    private void OnDisable() => GamePreferences.Changed -= Apply;
    private void Apply() => source.volume = music ? GamePreferences.MusicVolume : GamePreferences.EffectsVolume;
}
