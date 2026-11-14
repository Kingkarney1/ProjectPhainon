using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Phainon.Displays
{
    /// <summary>A status line: lamp, caption and a short status word such as GO or OFF.</summary>
    public class StatusRow : MonoBehaviour
    {
        [SerializeField] internal Image lamp;
        [SerializeField] internal TMP_Text label;
        [SerializeField] internal TMP_Text status;

        private string lastStatus;
        private string lastLabel;

        public void SetLabel(string text)
        {
            if (text == lastLabel) return;
            lastLabel = text;
            label.text = text;
        }

        public void Set(string text, Color color)
        {
            if (text != lastStatus)
            {
                lastStatus = text;
                status.text = text;
            }
            if (status.color != color) status.color = color;
            if (lamp != null && lamp.color != color) lamp.color = color;
        }

        public void SetLabelColor(Color color)
        {
            if (label.color != color) label.color = color;
        }
    }
}
