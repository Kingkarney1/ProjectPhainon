using UnityEngine;

namespace Phainon.Displays
{
    /// <summary>
    /// Artificial horizon. The sky/ground card slides with pitch and rotates with roll behind a
    /// fixed vehicle symbol; a small marker shows where the velocity vector points.
    /// </summary>
    public class AttitudeIndicator : MonoBehaviour
    {
        [SerializeField] internal RectTransform rollRoot;
        [SerializeField] internal RectTransform pitchRoot;
        [SerializeField] internal RectTransform flightPathMarker;
        [SerializeField] internal float pixelsPerDegree = 4f;
        [SerializeField] internal float smoothing = 10f;

        private float shownPitch = 90f;
        private float shownRoll;

        public void Animate(float pitchDegrees, float rollDegrees, float flightPathDegrees, bool showFlightPath, float dt)
        {
            float k = 1f - Mathf.Exp(-smoothing * dt);
            shownPitch = Mathf.Lerp(shownPitch, pitchDegrees, k);
            shownRoll = Mathf.LerpAngle(shownRoll, rollDegrees, k);

            // Nose up moves the horizon down the instrument; rolling right tilts it left.
            pitchRoot.anchoredPosition = new Vector2(0f, -shownPitch * pixelsPerDegree);
            rollRoot.localRotation = Quaternion.Euler(0f, 0f, shownRoll);

            if (flightPathMarker != null)
            {
                if (flightPathMarker.gameObject.activeSelf != showFlightPath) flightPathMarker.gameObject.SetActive(showFlightPath);
                float offset = Mathf.Clamp((flightPathDegrees - shownPitch) * pixelsPerDegree, -120f, 120f);
                flightPathMarker.anchoredPosition = new Vector2(0f, offset);
            }
        }
    }
}
