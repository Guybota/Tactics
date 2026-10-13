using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    [Header("--------- Audio Source ---------")]
    [SerializeField] AudioSource musicSource;
    [SerializeField] AudioSource SFXSource;

    [Header("--------- Audio Clip ---------")]
    public AudioClip background;
    public AudioClip death;
    public AudioClip buy;
    public AudioClip button;
    public AudioClip select;
    public AudioClip attack;
    public AudioClip capture;

    private void OnEnable() { GamePreferences.Changed += ApplyVolumes; ApplyVolumes(); }
    private void OnDisable() { GamePreferences.Changed -= ApplyVolumes; }
    private void ApplyVolumes()
    {
        if (musicSource != null) musicSource.volume = GamePreferences.MusicVolume;
        if (SFXSource != null) SFXSource.volume = GamePreferences.EffectsVolume;
    }

    private void Start()
    {
        musicSource.clip = background;
        musicSource.Play();
    }

    public void PlaySFX(AudioClip clip)
    {
        SFXSource.PlayOneShot(clip);
    }
}
