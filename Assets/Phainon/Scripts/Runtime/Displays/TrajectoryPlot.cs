using TMPro;
using UnityEngine;

namespace Phainon.Displays
{
    /// <summary>
    /// Ascent profile: altitude against downrange distance, drawn as the flight unfolds, with the
    /// target orbit altitude as a reference line. The axes rescale as the vehicle travels.
    /// </summary>
    public class TrajectoryPlot : MonoBehaviour
    {
        private const int Capacity = 512;

        [SerializeField] internal LineGraph path;
        [SerializeField] internal RectTransform marker;
        [SerializeField] internal RectTransform targetLine;
        [SerializeField] internal TMP_Text rangeLabel;
        [SerializeField] internal TMP_Text altitudeLabel;
        [SerializeField] internal float sampleInterval = 1f;
        [SerializeField] internal float minRangeKm = 300f;
        [SerializeField] internal float minAltitudeKm = 250f;

        private readonly Vector2[] samples = new Vector2[Capacity];   // x = downrange km, y = altitude km
        private readonly Vector2[] plotted = new Vector2[Capacity];
        private int count;
        private float sinceSample;
        private float interval;
        private float shownRange = -1f;
        private float shownAltitude = -1f;

        private void Awake() { interval = sampleInterval; }

        /// <summary>Feeds the plot the current position. Call at the display's refresh rate.</summary>
        public void Record(float downrangeKm, float altitudeKm, float dt)
        {
            sinceSample += dt;
            if (count > 0 && sinceSample < interval) return;
            sinceSample = 0f;

            if (count >= Capacity)
            {
                // Buffer full: keep every other sample and halve the sampling rate from here on.
                for (int i = 0; i < Capacity / 2; i++) samples[i] = samples[i * 2];
                count = Capacity / 2;
                interval *= 2f;
            }
            samples[count++] = new Vector2(downrangeKm, altitudeKm);
        }

        public void Redraw(float downrangeKm, float altitudeKm, float targetAltitudeKm)
        {
            float range = Mathf.Max(minRangeKm, Mathf.Ceil(downrangeKm * 1.15f / 100f) * 100f);
            float ceiling = Mathf.Max(minAltitudeKm, Mathf.Ceil(altitudeKm * 1.2f / 50f) * 50f);

            for (int i = 0; i < count; i++)
                plotted[i] = new Vector2(samples[i].x / range, samples[i].y / ceiling);
            path.SetPoints(plotted, count);

            Vector2 position = new Vector2(Mathf.Clamp01(downrangeKm / range), Mathf.Clamp01(altitudeKm / ceiling));
            marker.anchorMin = marker.anchorMax = position;

            float target = Mathf.Clamp01(targetAltitudeKm / ceiling);
            targetLine.anchorMin = new Vector2(0f, target);
            targetLine.anchorMax = new Vector2(1f, target);

            if (!Mathf.Approximately(range, shownRange))
            {
                shownRange = range;
                rangeLabel.text = range.ToString("0") + " KM";
            }
            if (!Mathf.Approximately(ceiling, shownAltitude))
            {
                shownAltitude = ceiling;
                altitudeLabel.text = ceiling.ToString("0") + " KM";
            }
        }
    }
}
