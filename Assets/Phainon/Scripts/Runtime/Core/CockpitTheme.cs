using Phainon.Simulation;
using UnityEngine;

namespace Phainon
{
    /// <summary>
    /// The cockpit's colour language in one place: instrument typography, status colours and lamp colours.
    /// Displays, lamps and the HUD all read from this asset so the look stays consistent.
    /// </summary>
    [CreateAssetMenu(menuName = "Phainon/Cockpit Theme", fileName = "CockpitTheme")]
    public class CockpitTheme : ScriptableObject
    {
        [Header("Instrument typography")]
        public Color textPrimary = new Color(0.90f, 0.93f, 0.95f);
        public Color textDim = new Color(0.52f, 0.58f, 0.63f);
        public Color nominalGreen = new Color(0.30f, 0.92f, 0.47f);
        public Color accentBlue = new Color(0.27f, 0.66f, 1.00f);
        public Color cautionAmber = new Color(1.00f, 0.72f, 0.12f);
        public Color warningRed = new Color(1.00f, 0.24f, 0.20f);

        [Header("Display surfaces")]
        public Color screenBackground = new Color(0.012f, 0.020f, 0.030f);
        public Color panelFill = new Color(0.045f, 0.065f, 0.085f);
        public Color panelOutline = new Color(0.16f, 0.24f, 0.31f);
        public Color gaugeTrack = new Color(0.10f, 0.14f, 0.18f);

        [Header("Lamps")]
        public float lampIntensity = 2.6f;
        public float blinkHz = 2.2f;

        public Color ForSeverity(WarningSeverity severity)
        {
            switch (severity)
            {
                case WarningSeverity.Warning: return warningRed;
                case WarningSeverity.Caution: return cautionAmber;
                default: return accentBlue;
            }
        }

        /// <summary>Green when go, amber otherwise. Used by every GO / NO-GO style readout.</summary>
        public Color GoColor(bool go) { return go ? nominalGreen : cautionAmber; }

        /// <summary>Square-wave blink shared by lamps and display elements so they pulse in step.</summary>
        public bool BlinkPhase(float time) { return Mathf.Repeat(time * blinkHz, 1f) < 0.5f; }
    }
}
