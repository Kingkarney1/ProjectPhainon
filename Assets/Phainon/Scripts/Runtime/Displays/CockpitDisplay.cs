using Phainon.Simulation;
using TMPro;
using UnityEngine;

namespace Phainon.Displays
{
    /// <summary>
    /// Shared behaviour of a cockpit display unit: it is dark without bus power, runs a short boot
    /// sequence when power arrives, follows the brightness dial and refreshes its content at a fixed
    /// rate. Each concrete display only decides what to show.
    /// </summary>
    public abstract class CockpitDisplay : MonoBehaviour
    {
        private enum PowerState { Off, Booting, On }

        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal CockpitTheme theme;
        [SerializeField] internal CanvasGroup content;
        [SerializeField] internal CanvasGroup bootScreen;
        [SerializeField] internal TMP_Text bootText;
        [SerializeField] internal string unitName = "DISPLAY UNIT";
        [SerializeField] internal float refreshInterval = 0.1f;
        [SerializeField] internal float bootSeconds = 1.8f;
        [Tooltip("Content opacity at the lowest brightness setting.")]
        [SerializeField] internal float minBrightness = 0.22f;
        [Tooltip("A soft light in front of the screen: what the lit display throws onto the panel around it.")]
        [SerializeField] internal Light glow;
        [SerializeField] internal float glowIntensity = 0.14f;

        private PowerState state = PowerState.Off;
        private float bootTimer;
        private float refreshTimer;
        private int bootStep = -1;

        protected CockpitTheme Theme { get { return theme; } }

        /// <summary>Called at the refresh rate while the display is on. Update text and gauges here.</summary>
        protected abstract void Refresh(Spacecraft craft, float dt);

        /// <summary>Called every frame while the display is on, for elements that must move smoothly.</summary>
        protected virtual void Animate(Spacecraft craft, float dt) { }

        protected virtual void Start()
        {
            content.alpha = 0f;
            bootScreen.alpha = 0f;
        }

        private void Update()
        {
            Spacecraft craft = spacecraft.Craft;
            bool powered = craft.Power.IsBusPowered;
            float brightness = Mathf.Lerp(minBrightness, 1f, (float)craft.Settings.DisplayBrightness);

            switch (state)
            {
                case PowerState.Off:
                    if (powered)
                    {
                        state = PowerState.Booting;
                        bootTimer = 0f;
                        bootStep = -1;
                    }
                    break;

                case PowerState.Booting:
                    bootTimer += Time.deltaTime;
                    UpdateBootText();
                    if (!powered) state = PowerState.Off;
                    else if (bootTimer >= bootSeconds)
                    {
                        state = PowerState.On;
                        refreshTimer = refreshInterval;
                    }
                    break;

                case PowerState.On:
                    if (!powered) { state = PowerState.Off; break; }
                    refreshTimer += Time.deltaTime;
                    if (refreshTimer >= refreshInterval)
                    {
                        Refresh(craft, refreshTimer);
                        refreshTimer = 0f;
                    }
                    Animate(craft, Time.deltaTime);
                    break;
            }

            float contentTarget = state == PowerState.On ? brightness : 0f;
            float bootTarget = state == PowerState.Booting ? brightness : 0f;
            float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
            content.alpha = Mathf.Lerp(content.alpha, contentTarget, k);
            bootScreen.alpha = Mathf.Lerp(bootScreen.alpha, bootTarget, k);
            if (glow != null) glow.intensity = glowIntensity * Mathf.Max(content.alpha, bootScreen.alpha * 0.5f);
        }

        private void UpdateBootText()
        {
            int step = Mathf.Min(4, (int)(bootTimer / bootSeconds * 5f));
            if (step == bootStep) return;
            bootStep = step;

            string text = "PHAINON FLIGHT SYSTEMS\n" + unitName + "\n\n";
            if (step >= 1) text += "MEMORY CHECK ........ OK\n";
            if (step >= 2) text += "DATA BUS ............ OK\n";
            if (step >= 3) text += "GRAPHICS ............ OK\n";
            if (step >= 4) text += "LOADING FORMAT";
            bootText.text = text;
        }

        // ---------------------------------------------------------------- formatting helpers

        protected static string Kilometres(double metres)
        {
            double km = metres / 1000.0;
            if (km < 1000.0) return km.ToString("0.0");
            // Readouts are five characters wide: deep-space distances are shown in thousands of km.
            return km < 100000.0 ? km.ToString("0") : (km / 1000.0).ToString("0") + "k";
        }

        protected static string Signed(double value, string format)
        {
            return (value >= 0 ? "+" : "") + value.ToString(format);
        }
    }
}
