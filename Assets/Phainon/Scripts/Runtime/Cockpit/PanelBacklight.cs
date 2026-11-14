using TMPro;
using UnityEngine;

namespace Phainon.Cockpit
{
    /// <summary>
    /// Panel legend lighting. Control labels are edge-lit from the main bus: bright when the bus is
    /// live and a dull printed grey when it is not, so a cold cockpit looks cold.
    /// </summary>
    public class PanelBacklight : MonoBehaviour
    {
        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal TMP_Text[] labels;
        [SerializeField] internal Color litColor = new Color(0.90f, 0.93f, 0.95f);
        [SerializeField] internal Color unlitColor = new Color(0.30f, 0.32f, 0.34f);
        [SerializeField] internal float fadeSeconds = 0.5f;

        private float level = -1f;

        private void Update()
        {
            float target = spacecraft.Craft.Power.IsBusPowered ? (float)spacecraft.Craft.Settings.DisplayBrightness * 0.5f + 0.5f : 0f;
            float next = level < 0f ? target : Mathf.MoveTowards(level, target, Time.deltaTime / Mathf.Max(0.01f, fadeSeconds));
            if (Mathf.Approximately(next, level)) return;
            level = next;

            // Recolouring rebuilds each label's mesh, so this only runs while the level is changing.
            Color color = Color.Lerp(unlitColor, litColor, level);
            for (int i = 0; i < labels.Length; i++)
                if (labels[i] != null) labels[i].color = color;
        }
    }
}
