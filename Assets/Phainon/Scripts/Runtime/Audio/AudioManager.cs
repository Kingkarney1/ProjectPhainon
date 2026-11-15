using Phainon.Cockpit;
using Phainon.Simulation;
using UnityEngine;

namespace Phainon.Audio
{
    /// <summary>
    /// All game audio. One-shot sounds are fired by simulation and control events; continuous sounds
    /// (engine, rumble, wind, hum, alarm) are loops whose volume and pitch track live spacecraft
    /// state every frame, so nothing can keep playing after its cause has gone away.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal GameManager game;
        [SerializeField] internal CockpitControlRouter router;
        [SerializeField] internal CockpitCameraController cockpitCamera;
        [SerializeField] internal AudioLibrary library;
        [Range(0f, 1f)]
        [SerializeField] internal float masterVolume = 0.9f;
        [Tooltip("Number of pooled sources for overlapping one-shot sounds.")]
        [SerializeField] internal int oneShotVoices = 8;
        [SerializeField] internal float loopFadeSpeed = 4f;

        private AudioSource[] voices;
        private int nextVoice;

        private AudioSource ambient, hum, apu, engine, rumble, wind, rcs, alarm, breathing, radioStatic;

        /// <summary>How awake the astronaut is, 0..1. Everything is quiet and close while they come round.</summary>
        public float Consciousness { get; set; } = 1f;

        private void Awake()
        {
            // A fixed pool avoids creating and destroying audio sources while flying.
            voices = new AudioSource[Mathf.Max(1, oneShotVoices)];
            for (int i = 0; i < voices.Length; i++) voices[i] = CreateSource("Voice " + i, false);

            ambient = CreateLoop("Cabin Ambient", library.cabinAmbient);
            hum = CreateLoop("Electrical Hum", library.electricalHum);
            apu = CreateLoop("APU", library.apuLoop);
            engine = CreateLoop("Engine", library.engineLoop);
            rumble = CreateLoop("Rumble", library.rumbleLoop);
            wind = CreateLoop("Wind", library.windLoop);
            rcs = CreateLoop("RCS", library.rcsLoop);
            alarm = CreateLoop("Master Alarm", library.masterAlarm);
            breathing = CreateLoop("Breathing", library.breathingLoop);
            radioStatic = CreateLoop("Radio Static", library.radioStaticLoop);
        }

        private void Start()
        {
            Spacecraft craft = spacecraft.Craft;

            craft.Power.BusPowerChanged += powered => Play(powered ? library.powerUp : library.powerDown);
            craft.Engine.Ignited += stage => Play(library.engineIgnition);
            craft.Engine.ShutDown += (stage, reason) => Play(library.engineCutoff);
            craft.Engine.StageSeparated += stage => Play(library.stageSeparation);
            craft.Mission.Notification += message => Play(library.notification);
            craft.Mission.CountdownTick += seconds => { if (seconds <= 10) Play(library.countdownBeep); };
            craft.Mission.Completed += () => Play(library.missionComplete);
            craft.Mission.Failed += reason => Play(library.missionFailed);
            craft.GroundControl.MessageReceived += message => Play(library.notification);
            craft.Warnings.Raised += OnWarningRaised;

            router.ControlSound += OnControlSound;
            router.CommandRejected += (id, message) => Play(library.denied);
        }

        private void OnWarningRaised(WarningEntry entry)
        {
            // Red warnings are announced by the master alarm loop; lesser alerts get a single chime.
            if (entry.Severity != WarningSeverity.Warning) Play(library.cautionChime);
        }

        private void OnControlSound(Interactable control, InteractionSound sound)
        {
            switch (sound)
            {
                case InteractionSound.ButtonPress: Play(library.buttonPress); break;
                case InteractionSound.SwitchOn: Play(library.switchOn); break;
                case InteractionSound.SwitchOff: Play(library.switchOff); break;
                case InteractionSound.DialTick: Play(library.dialTick); break;
                case InteractionSound.GuardOpen: Play(library.guardOpen); break;
                case InteractionSound.GuardClose: Play(library.guardClose); break;
                case InteractionSound.LeverMove: Play(library.leverMove); break;
            }
        }

        private void Update()
        {
            Spacecraft craft = spacecraft.Craft;
            bool audible = game.State != GameState.MainMenu && game.State != GameState.Briefing && game.State != GameState.Intro;
            AudioListener.volume = GameSettings.MasterVolume * Mathf.Lerp(0.25f, 1f, Consciousness);
            bool powered = craft.Power.IsBusPowered;
            float k = 1f - Mathf.Exp(-loopFadeSpeed * Time.unscaledDeltaTime);

            float output = (float)craft.Engine.OutputFraction;
            float air = (float)craft.Flight.AirDensityFraction;
            float stage = craft.Engine.ActiveStage == 0 ? 1f : 0.55f;
            float buffet = Mathf.Clamp01((float)(craft.Flight.DynamicPressurePa / craft.SystemsSpec.maxDynamicPressurePa));
            float vibration = cockpitCamera != null ? cockpitCamera.Vibration : 0f;

            // In vacuum the engine is only heard through the structure, so it loses its roar but keeps its rumble.
            float engineLevel = output * stage * Mathf.Lerp(0.45f, 1f, air);

            Drive(ambient, library.cabinAmbient, audible ? (powered ? 1f : 0.35f) : 0f, 1f, k);
            Drive(hum, library.electricalHum, audible && powered ? 1f : 0f, 1f, k);
            Drive(apu, library.apuLoop, audible ? (float)craft.Power.ApuSpoolFraction : 0f,
                Mathf.Lerp(0.55f, 1f, (float)craft.Power.ApuSpoolFraction), k);
            Drive(engine, library.engineLoop, audible ? engineLevel : 0f, Mathf.Lerp(0.7f, 1.12f, output), k);
            Drive(rumble, library.rumbleLoop, audible ? Mathf.Clamp01(vibration * 1.3f) : 0f, Mathf.Lerp(0.85f, 1.1f, vibration), k);
            Drive(wind, library.windLoop, audible ? buffet : 0f, Mathf.Lerp(0.8f, 1.3f, buffet), k);
            Drive(rcs, library.rcsLoop, audible && craft.Attitude.RcsFiring ? 1f : 0f, 1f, k * 3f);

            Drive(breathing, library.breathingLoop, audible ? 1f - Consciousness : 0f, 1f, k);
            // An open receiver with nobody on the other end hisses.
            bool hiss = audible && powered && craft.Scenario != null && craft.Comms.Enabled && !craft.Comms.LinkEstablished;
            Drive(radioStatic, library.radioStaticLoop, hiss ? 1f : 0f, 1f, k);

            // The alarm sounds only while a red warning is unacknowledged and there is power to drive it.
            bool alarming = audible && powered && craft.Warnings.MasterAlarmActive && game.State != GameState.Paused;
            Drive(alarm, library.masterAlarm, alarming ? 1f : 0f, 1f, 1f);
        }

        /// <summary>Moves a loop towards a target level, starting and stopping the source as needed.</summary>
        private void Drive(AudioSource source, SoundEntry entry, float level, float pitch, float k)
        {
            if (source == null || entry == null || entry.clip == null) return;

            float target = Mathf.Clamp01(level) * entry.volume * masterVolume;
            source.volume = Mathf.Lerp(source.volume, target, k);
            source.pitch = pitch;

            bool shouldPlay = target > 0.001f || source.volume > 0.002f;
            if (shouldPlay && !source.isPlaying) source.Play();
            else if (!shouldPlay && source.isPlaying) source.Stop();
        }

        public void Play(SoundEntry entry)
        {
            if (entry == null || entry.clip == null) return;
            if (game.State == GameState.MainMenu || game.State == GameState.Briefing || game.State == GameState.Intro) return;

            AudioSource voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            voice.pitch = 1f;
            voice.PlayOneShot(entry.clip, entry.volume * masterVolume);
        }

        private AudioSource CreateLoop(string sourceName, SoundEntry entry)
        {
            AudioSource source = CreateSource(sourceName, true);
            source.clip = entry != null ? entry.clip : null;
            source.volume = 0f;
            return source;
        }

        private AudioSource CreateSource(string sourceName, bool loop)
        {
            GameObject child = new GameObject(sourceName);
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;        // the crew sit inside the sound; everything is 2D
            source.ignoreListenerPause = false;
            return source;
        }
    }
}
