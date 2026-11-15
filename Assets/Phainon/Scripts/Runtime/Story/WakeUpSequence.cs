using Phainon.Audio;
using Phainon.Cockpit;
using UnityEngine;
using UnityEngine.UI;

namespace Phainon.Story
{
    /// <summary>
    /// The astronaut coming round in the seat: darkness, eyes opening on a blurred cabin, the view
    /// clearing, then the head lifting to the window. It is the player's own camera in the real
    /// cockpit with the simulation running, so the alarms and lamps are the spacecraft's own, and
    /// when it ends the player simply has the controls. Any key skips it.
    /// </summary>
    public class WakeUpSequence : MonoBehaviour
    {
        [SerializeField] internal GameManager game;
        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal CockpitCameraController cockpitCamera;
        [SerializeField] internal AudioManager audioManager;
        [SerializeField] internal CanvasGroup group;
        [SerializeField] internal Image black;
        [SerializeField] internal RectTransform lidTop;
        [SerializeField] internal RectTransform lidBottom;
        [SerializeField] internal Image haze;
        [SerializeField] internal float seconds = 30f;
        [Tooltip("Length of the plain fade used when visual effects are reduced.")]
        [SerializeField] internal float reducedSeconds = 6f;
        [Tooltip("Head angles while slumped in the seat: yaw, pitch (positive looks down) and roll.")]
        [SerializeField] internal Vector3 slumped = new Vector3(-14f, 44f, 7f);
        [SerializeField] internal float displaysPitch = 14f;
        [SerializeField] internal float windowPitch = -13f;

        private float timer;
        private bool running;

        private void Awake()
        {
            group.alpha = 0f;
            game.StateChanged += OnStateChanged;
        }

        private void OnDestroy()
        {
            if (game != null) game.StateChanged -= OnStateChanged;
        }

        private void OnStateChanged(GameState state)
        {
            if (state != GameState.Cinematic || running) return;
            running = true;
            timer = 0f;
            group.alpha = 1f;
            cockpitCamera.Directed = true;
            Apply(0f, GameSettings.ReducedEffects);
        }

        private void Update()
        {
            if (!running) return;
            bool reduced = GameSettings.ReducedEffects;
            float length = reduced ? reducedSeconds : seconds;
            timer += Time.deltaTime;
            if (spacecraft.Input.Skip.WasPressedThisFrame() || spacecraft.Input.Pause.WasPressedThisFrame()) timer = length;

            float u = Mathf.Clamp01(timer / length);
            Apply(u, reduced);
            if (u >= 1f) Finish();
        }

        private void Apply(float u, bool reduced)
        {
            // Darkness first, then light.
            SetAlpha(black, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.10f, 0.30f, u)));

            // Eyelids: a first half-opening, two slow blinks, then open.
            float open = reduced ? 1f : EyesOpen(u);
            lidTop.anchorMin = new Vector2(0f, 0.5f + 0.5f * open);
            lidBottom.anchorMax = new Vector2(1f, 0.5f - 0.5f * open);

            // Haze over everything until the eyes focus.
            float blur = reduced ? 0f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.66f, u));
            SetAlpha(haze, blur * (0.42f + 0.10f * Mathf.Sin(Time.time * 1.3f)));

            // The head: slumped, then up to the displays, then up to the window.
            float toDisplays = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.60f, u));
            float toWindow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.68f, 0.92f, u));
            float sway = reduced ? 0f : (1f - toWindow) * Mathf.Sin(Time.time * 0.7f);
            cockpitCamera.DirectedYaw = Mathf.Lerp(slumped.x, 0f, toDisplays) + sway * 1.2f;
            cockpitCamera.DirectedPitch = Mathf.Lerp(Mathf.Lerp(slumped.y, displaysPitch, toDisplays), windowPitch, toWindow) + sway * 0.8f;
            cockpitCamera.DirectedRoll = reduced ? 0f : Mathf.Lerp(slumped.z, 0f, toDisplays);

            if (audioManager != null) audioManager.Consciousness = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.04f, 0.62f, u));
        }

        private static float EyesOpen(float u)
        {
            if (u < 0.12f) return 0f;
            if (u < 0.20f) return Mathf.Lerp(0f, 0.35f, (u - 0.12f) / 0.08f);
            if (u < 0.24f) return Mathf.Lerp(0.35f, 0.05f, (u - 0.20f) / 0.04f);
            if (u < 0.32f) return Mathf.Lerp(0.05f, 0.60f, (u - 0.24f) / 0.08f);
            if (u < 0.36f) return Mathf.Lerp(0.60f, 0.15f, (u - 0.32f) / 0.04f);
            return Mathf.Lerp(0.15f, 1f, Mathf.SmoothStep(0f, 1f, (u - 0.36f) / 0.12f));
        }

        private static void SetAlpha(Image image, float alpha)
        {
            Color color = image.color;
            color.a = Mathf.Clamp01(alpha);
            image.color = color;
        }

        private void Finish()
        {
            running = false;
            group.alpha = 0f;
            cockpitCamera.DirectedRoll = 0f;
            cockpitCamera.Directed = false;
            if (audioManager != null) audioManager.Consciousness = 1f;
            game.CinematicFinished();
        }
    }
}
