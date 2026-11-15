using UnityEngine;

namespace Phainon.Audio
{
    /// <summary>A clip with its mix level, so volume balancing lives in data rather than code.</summary>
    [System.Serializable]
    public class SoundEntry
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.8f;
    }

    /// <summary>
    /// Every sound the game uses. The shipped clips are synthesised placeholders (see
    /// Tools/generate_audio.sh); swap any entry for recorded audio without touching code.
    /// </summary>
    [CreateAssetMenu(menuName = "Phainon/Audio Library", fileName = "AudioLibrary")]
    public class AudioLibrary : ScriptableObject
    {
        [Header("Cockpit controls")]
        public SoundEntry buttonPress = new SoundEntry { volume = 0.55f };
        public SoundEntry switchOn = new SoundEntry { volume = 0.6f };
        public SoundEntry switchOff = new SoundEntry { volume = 0.6f };
        public SoundEntry dialTick = new SoundEntry { volume = 0.45f };
        public SoundEntry guardOpen = new SoundEntry { volume = 0.55f };
        public SoundEntry guardClose = new SoundEntry { volume = 0.5f };
        public SoundEntry leverMove = new SoundEntry { volume = 0.4f };
        public SoundEntry denied = new SoundEntry { volume = 0.5f };

        [Header("Events")]
        public SoundEntry powerUp = new SoundEntry { volume = 0.55f };
        public SoundEntry powerDown = new SoundEntry { volume = 0.5f };
        public SoundEntry engineIgnition = new SoundEntry { volume = 0.9f };
        public SoundEntry engineCutoff = new SoundEntry { volume = 0.7f };
        public SoundEntry stageSeparation = new SoundEntry { volume = 0.95f };
        public SoundEntry notification = new SoundEntry { volume = 0.4f };
        public SoundEntry countdownBeep = new SoundEntry { volume = 0.45f };
        public SoundEntry cautionChime = new SoundEntry { volume = 0.5f };
        public SoundEntry missionComplete = new SoundEntry { volume = 0.6f };
        public SoundEntry missionFailed = new SoundEntry { volume = 0.6f };

        [Header("Loops")]
        public SoundEntry cabinAmbient = new SoundEntry { volume = 0.32f };
        public SoundEntry electricalHum = new SoundEntry { volume = 0.1f };
        public SoundEntry apuLoop = new SoundEntry { volume = 0.16f };
        public SoundEntry engineLoop = new SoundEntry { volume = 0.85f };
        public SoundEntry rumbleLoop = new SoundEntry { volume = 0.75f };
        public SoundEntry windLoop = new SoundEntry { volume = 0.5f };
        public SoundEntry rcsLoop = new SoundEntry { volume = 0.3f };
        public SoundEntry masterAlarm = new SoundEntry { volume = 0.38f };
        public SoundEntry breathingLoop = new SoundEntry { volume = 0.5f };
        public SoundEntry radioStaticLoop = new SoundEntry { volume = 0.12f };
    }
}
