using Phainon.Simulation;
using UnityEngine;

namespace Phainon.Cockpit
{
    /// <summary>
    /// A panel lamp or annunciator. It reads its state from the spacecraft every frame, so a lamp
    /// can never disagree with the system it represents.
    /// </summary>
    public class IndicatorLight : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] internal IndicatorId indicator;
        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal CockpitTheme theme;
        [SerializeField] internal Renderer lens;
        [SerializeField] internal Color litColor = Color.green;
        [Tooltip("Lens colour when the lamp is dark.")]
        [SerializeField] internal Color unlitColor = new Color(0.05f, 0.05f, 0.05f);
        [SerializeField] internal Light glow;

        private MaterialPropertyBlock block;
        private int appliedState = -1;

        private void Awake()
        {
            // A lamp dropped into the scene from a prefab finds the spacecraft by itself.
            if (spacecraft == null) spacecraft = FindAnyObjectByType<SpacecraftController>();
            if (lens == null) lens = GetComponent<Renderer>();
        }

        private void LateUpdate()
        {
            if (spacecraft == null || spacecraft.Craft == null || lens == null) return;

            IndicatorState state = spacecraft.Craft.GetIndicator(indicator);
            bool lit = state == IndicatorState.On
                       || (state == IndicatorState.Blinking && (theme == null || theme.BlinkPhase(Time.unscaledTime)));

            int key = lit ? 1 : 0;
            if (key == appliedState) return;
            appliedState = key;

            if (block == null) block = new MaterialPropertyBlock();
            float intensity = theme != null ? theme.lampIntensity : 2.5f;
            lens.GetPropertyBlock(block);
            block.SetColor(BaseColorId, lit ? litColor : unlitColor);
            block.SetColor(EmissionColorId, lit ? litColor * intensity : Color.black);
            lens.SetPropertyBlock(block);

            if (glow != null) glow.enabled = lit;
        }
    }
}
