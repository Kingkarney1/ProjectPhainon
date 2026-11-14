using TMPro;
using UnityEngine;

namespace Phainon.Displays
{
    /// <summary>A labelled numeric readout: caption, value and unit.</summary>
    public class ValueReadout : MonoBehaviour
    {
        [SerializeField] internal TMP_Text label;
        [SerializeField] internal TMP_Text value;
        [SerializeField] internal TMP_Text unit;

        private string lastText;

        public void Set(string text)
        {
            // Skip the mesh rebuild when the text has not changed since the last refresh.
            if (text == lastText) return;
            lastText = text;
            value.text = text;
        }

        public void Set(string text, Color color)
        {
            Set(text);
            if (value.color != color) value.color = color;
        }
    }
}
