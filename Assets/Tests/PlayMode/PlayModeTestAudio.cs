using NUnit.Framework;
using UnityEngine;

// Keep automated gameplay tests silent, and restore the previous volume afterwards.
[SetUpFixture]
public sealed class PlayModeTestAudio
{
    private float previousVolume;

    [OneTimeSetUp]
    public void MuteTestAudio()
    {
        previousVolume = AudioListener.volume;
        AudioListener.volume = 0f;
    }

    [OneTimeTearDown]
    public void RestoreAudioVolume()
    {
        AudioListener.volume = previousVolume;
    }
}
