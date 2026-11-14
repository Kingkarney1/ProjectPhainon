using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Phainon.Displays
{
    /// <summary>A horizontal bar gauge with an optional text value, coloured by the caller.</summary>
    public class BarGauge : MonoBehaviour
    {
        [SerializeField] internal RectTransform fill;
        [SerializeField] internal Image fillImage;
        [SerializeField] internal TMP_Text label;
        [SerializeField] internal TMP_Text value;

        private string lastText;
        private string lastLabel;

        public void SetLabel(string text)
        {
            if (label == null || text == lastLabel) return;
            lastLabel = text;
            label.text = text;
        }

        public void Set(float fraction, Color color, string text = null)
        {
            fraction = Mathf.Clamp01(fraction);
            Vector2 max = fill.anchorMax;
            if (!Mathf.Approximately(max.x, fraction))
            {
                max.x = fraction;
                fill.anchorMax = max;
            }
            if (fillImage.color != color) fillImage.color = color;

            if (value != null && text != null && text != lastText)
            {
                lastText = text;
                value.text = text;
            }
        }
    }
}
