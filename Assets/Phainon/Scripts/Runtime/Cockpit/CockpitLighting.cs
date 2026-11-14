using Phainon.Simulation;
using UnityEngine;

namespace Phainon.Cockpit
{
    /// <summary>
    /// Cabin lighting driven by spacecraft state: flood lights follow the CABIN LIGHTS switch and
    /// main bus power, panel edge lighting follows the bus, a red beacon flashes with the master
    /// alarm and the engine throws a flickering glow into the cabin while it burns in the atmosphere.
    /// </summary>
    public class CockpitLighting : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal CockpitTheme theme;
        [SerializeField] internal Light[] floodLights;
        [SerializeField] internal float floodIntensity = 1.1f;
        [Tooltip("Dim emergency-level lighting available from the batteries alone.")]
        [SerializeField] internal float standbyIntensity = 0.12f;
        [SerializeField] internal Renderer[] floodFixtures;
        [SerializeField, ColorUsage(false, true)] internal Color fixtureEmission = new Color(1.6f, 1.5f, 1.3f);
        [SerializeField] internal Light[] panelLights;
        [SerializeField] internal float panelIntensity = 0.5f;
        [SerializeField] internal Light alarmLight;
        [SerializeField] internal float alarmIntensity = 1.8f;
        [SerializeField] internal Light engineGlow;
        [SerializeField] internal float engineGlowIntensity = 0.6f;
        [SerializeField] internal float fadeSpeed = 3.5f;

        private float flood;
        private float panel;
        private MaterialPropertyBlock block;

        private void Update()
        {
            Spacecraft craft = spacecraft.Craft;
            bool powered = craft.Power.IsBusPowered;
            float k = 1f - Mathf.Exp(-fadeSpeed * Time.deltaTime);

            float floodTarget = powered && craft.Settings.CabinLightsOn ? floodIntensity : standbyIntensity;
            flood = Mathf.Lerp(flood, floodTarget, k);
            for (int i = 0; i < floodLights.Length; i++)
                if (floodLights[i] != null) floodLights[i].intensity = flood;

            if (floodFixtures != null && floodFixtures.Length > 0)
            {
                if (block == null) block = new MaterialPropertyBlock();
                Color emission = fixtureEmission * Mathf.Clamp01(flood / Mathf.Max(0.01f, floodIntensity));
                for (int i = 0; i < floodFixtures.Length; i++)
                {
                    if (floodFixtures[i] == null) continue;
                    floodFixtures[i].GetPropertyBlock(block);
                    block.SetColor(EmissionColorId, emission);
                    floodFixtures[i].SetPropertyBlock(block);
                }
            }

            panel = Mathf.Lerp(panel, powered ? panelIntensity : 0f, k);
            for (int i = 0; i < panelLights.Length; i++)
                if (panelLights[i] != null) panelLights[i].intensity = panel;

            if (alarmLight != null)
            {
                bool flash = powered && craft.Warnings.MasterAlarmActive && (theme == null || theme.BlinkPhase(Time.unscaledTime));
                alarmLight.intensity = flash ? alarmIntensity : 0f;
            }

            if (engineGlow != null)
            {
                // Plume light scatters back into the cabin only while there is air to scatter it.
                float air = (float)craft.Flight.AirDensityFraction;
                float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 17f, 0.5f);
                engineGlow.intensity = engineGlowIntensity * (float)craft.Engine.OutputFraction * Mathf.Clamp01(air * 1.5f) * flicker;
            }
        }
    }
}
