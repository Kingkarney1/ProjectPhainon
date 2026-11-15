using Phainon.Cockpit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Phainon.Story
{
    /// <summary>
    /// The story's introduction: its lines shown one at a time over the real star field, with the
    /// view drifting slowly, then a fade to black for the wake-up. Any key skips it.
    /// </summary>
    public class IntroSequence : MonoBehaviour
    {
        [SerializeField] internal GameManager game;
        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal StoryCampaign campaign;
        [SerializeField] internal CockpitCameraController cockpitCamera;
        [SerializeField] internal Camera viewCamera;
        [Tooltip("What the camera draws during the introduction: the sky and nothing else.")]
        [SerializeField] internal LayerMask skyMask;
        [SerializeField] internal CanvasGroup group;
        [SerializeField] internal Image blackout;
        [SerializeField] internal TMP_Text line;
        [SerializeField] internal float fadeSeconds = 1.3f;
        [SerializeField] internal float holdSeconds = 2.2f;
        [SerializeField] internal float secondsPerCharacter = 0.035f;
        [SerializeField] internal float blackoutSeconds = 2f;
        [SerializeField] internal float driftDegreesPerSecond = 0.8f;

        private string[] lines;
        private int index;
        private float timer;
        private float yaw;
        private int normalMask;
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
            if (state != GameState.Intro || running) return;
            lines = campaign != null ? campaign.spec.introduction : new string[0];
            running = true;
            index = 0;
            timer = 0f;
            yaw = 150f;
            normalMask = viewCamera.cullingMask;
            viewCamera.cullingMask = skyMask;
            cockpitCamera.Directed = true;
            group.alpha = 1f;
            SetBlackout(0f);
            line.text = "";
        }

        private void Update()
        {
            if (!running) return;
            float dt = Time.unscaledDeltaTime;
            timer += dt;
            yaw += driftDegreesPerSecond * dt;
            // Looking up and back, away from the planet: only stars.
            cockpitCamera.DirectedYaw = yaw;
            cockpitCamera.DirectedPitch = -38f;
            cockpitCamera.DirectedRoll = 0f;

            bool skip = spacecraft.Input.Skip.WasPressedThisFrame() || spacecraft.Input.Pause.WasPressedThisFrame()
                        || spacecraft.Input.Interact.WasPressedThisFrame();
            if (skip || index >= lines.Length)
            {
                // The last thing the introduction does is close down to black.
                if (skip && index < lines.Length) { index = lines.Length; timer = 0f; }
                line.alpha = Mathf.MoveTowards(line.alpha, 0f, dt * 3f);
                SetBlackout(Mathf.Clamp01(timer / blackoutSeconds));
                if (timer >= blackoutSeconds) Finish();
                return;
            }

            string text = lines[index];
            float hold = holdSeconds + text.Length * secondsPerCharacter;
            if (line.text != text) line.text = text;
            float alpha = timer < fadeSeconds ? timer / fadeSeconds
                : (timer < fadeSeconds + hold ? 1f : 1f - (timer - fadeSeconds - hold) / fadeSeconds);
            line.alpha = Mathf.Clamp01(alpha);
            if (timer >= fadeSeconds * 2f + hold)
            {
                index++;
                timer = 0f;
            }
        }

        private void SetBlackout(float alpha)
        {
            Color color = blackout.color;
            color.a = alpha;
            blackout.color = color;
        }

        private void Finish()
        {
            running = false;
            viewCamera.cullingMask = normalMask;
            // The head stays under direction: the wake-up takes it from here.
            game.IntroFinished();
            group.alpha = 0f;
        }
    }
}
