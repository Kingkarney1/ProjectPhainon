using Phainon.Simulation;
using UnityEngine;

namespace Phainon.Displays
{
    /// <summary>
    /// Earth-relative position: a view down onto the flight plane showing the Earth, the ground
    /// track flown so far, the predicted orbit and the vehicle. Altitude is exaggerated so that a
    /// low orbit is distinguishable from the surface.
    /// </summary>
    public class OrbitMap : MonoBehaviour
    {
        private const int Capacity = 256;
        private const int OrbitSegments = 96;

        [SerializeField] internal RectTransform earth;
        [SerializeField] internal LineGraph track;
        [SerializeField] internal LineGraph orbit;
        [SerializeField] internal RectTransform marker;
        [SerializeField] internal RectTransform launchSite;
        [Tooltip("Earth radius as a fraction of the map's half-size.")]
        [SerializeField] internal float earthRadius = 0.56f;
        [SerializeField] internal float altitudeExaggeration = 14f;
        [SerializeField] internal float sampleInterval = 4f;

        private readonly Vector2[] samples = new Vector2[Capacity];   // x = downrange angle (rad), y = altitude (m)
        private readonly Vector2[] trackPoints = new Vector2[Capacity + 1];
        private readonly Vector2[] orbitPoints = new Vector2[OrbitSegments + 1];
        private int count;
        private float sinceSample;
        private float interval;

        private void Awake() { interval = sampleInterval; }

        public void Record(FlightModel flight, float dt)
        {
            sinceSample += dt;
            if (count > 0 && sinceSample < interval) return;
            sinceSample = 0f;

            if (count >= Capacity)
            {
                for (int i = 0; i < Capacity / 2; i++) samples[i] = samples[i * 2];
                count = Capacity / 2;
                interval *= 2f;
            }
            samples[count++] = new Vector2((float)flight.DownrangeAngle, (float)flight.AltitudeMeters);
        }

        public void Redraw(FlightModel flight)
        {
            earth.anchorMin = new Vector2(0.5f - earthRadius * 0.5f, 0.5f - earthRadius * 0.5f);
            earth.anchorMax = new Vector2(0.5f + earthRadius * 0.5f, 0.5f + earthRadius * 0.5f);

            for (int i = 0; i < count; i++) trackPoints[i] = Project(samples[i].x, samples[i].y);
            Vector2 current = Project((float)flight.DownrangeAngle, (float)flight.AltitudeMeters);
            trackPoints[count] = current;
            track.SetPoints(trackPoints, count + 1);

            marker.anchorMin = marker.anchorMax = current;
            launchSite.anchorMin = launchSite.anchorMax = Project(0f, 0f);

            DrawPredictedOrbit(flight);
        }

        private void DrawPredictedOrbit(FlightModel flight)
        {
            bool bound = !double.IsInfinity(flight.SemiMajorAxis) && flight.Speed > 50.0;
            if (orbit.gameObject.activeSelf != bound) orbit.gameObject.SetActive(bound);
            if (!bound) return;

            double a = flight.SemiMajorAxis;
            double e = flight.Eccentricity;
            double p = a * (1.0 - e * e);
            // Map angle of periapsis: where the vehicle is now, wound back by its true anomaly.
            double periapsisAngle = flight.DownrangeAngle - flight.TrueAnomaly;

            for (int i = 0; i <= OrbitSegments; i++)
            {
                double anomaly = i * (2.0 * System.Math.PI / OrbitSegments);
                double radius = p / (1.0 + e * System.Math.Cos(anomaly));
                double altitude = System.Math.Max(0.0, radius - FlightModel.EarthRadius);
                orbitPoints[i] = Project((float)(periapsisAngle + anomaly), (float)altitude);
            }
            orbit.SetPoints(orbitPoints, OrbitSegments + 1);
        }

        /// <summary>Launch site is drawn at the top of the Earth; travel downrange runs clockwise.</summary>
        private Vector2 Project(float downrangeAngle, float altitudeMeters)
        {
            float radius = earthRadius * 0.5f * (1f + altitudeMeters / (float)FlightModel.EarthRadius * altitudeExaggeration);
            float angle = Mathf.PI * 0.5f - downrangeAngle;
            return new Vector2(0.5f + Mathf.Cos(angle) * radius, 0.5f + Mathf.Sin(angle) * radius);
        }
    }
}
