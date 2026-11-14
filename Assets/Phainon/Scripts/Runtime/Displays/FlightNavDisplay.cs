using Phainon.Simulation;
using TMPro;
using UnityEngine;

namespace Phainon.Displays
{
    /// <summary>
    /// Display 1: flight and navigation. Altitude, velocity, attitude, mission clock and phase, with
    /// a navigation page that follows the NAV MODE dial (ascent profile or Earth-relative orbit map).
    /// </summary>
    public class FlightNavDisplay : CockpitDisplay
    {
        [Header("Header")]
        [SerializeField] internal TMP_Text phaseText;
        [SerializeField] internal TMP_Text clockText;

        [Header("Flight data")]
        [SerializeField] internal ValueReadout altitude;
        [SerializeField] internal ValueReadout velocity;
        [SerializeField] internal ValueReadout verticalSpeed;
        [SerializeField] internal ValueReadout downrange;
        [SerializeField] internal ValueReadout apoapsis;
        [SerializeField] internal ValueReadout periapsis;
        [SerializeField] internal ValueReadout gLoad;
        [SerializeField] internal ValueReadout dynamicPressure;
        [SerializeField] internal ValueReadout mach;

        [Header("Attitude")]
        [SerializeField] internal AttitudeIndicator attitude;
        [SerializeField] internal ValueReadout pitch;
        [SerializeField] internal ValueReadout roll;
        [SerializeField] internal ValueReadout yaw;
        [SerializeField] internal StatusRow guidanceStatus;

        [Header("Navigation page")]
        [SerializeField] internal TMP_Text navTitle;
        [SerializeField] internal TrajectoryPlot trajectory;
        [SerializeField] internal OrbitMap orbitMap;
        [SerializeField] internal GameObject navOverlay;
        [SerializeField] internal TMP_Text navOverlayText;

        [Header("Orbital velocity")]
        [SerializeField] internal BarGauge orbitalVelocity;
        [SerializeField] internal StatusRow orbitStatus;

        private int shownPage = -1;

        protected override void Refresh(Spacecraft craft, float dt)
        {
            FlightModel flight = craft.Flight;
            MissionController mission = craft.Mission;
            SystemsSpec limits = craft.SystemsSpec;
            CockpitTheme t = Theme;

            phaseText.text = mission.Spec.GetPhaseInfo(mission.Phase).title;
            phaseText.color = mission.IsFailed ? t.warningRed : (mission.IsComplete ? t.nominalGreen : t.accentBlue);
            clockText.text = mission.FormatClock();

            altitude.Set(Kilometres(flight.AltitudeMeters));
            velocity.Set(flight.Speed.ToString("0"));
            verticalSpeed.Set(Signed(flight.VerticalSpeed, "0"), flight.VerticalSpeed < -5.0 ? t.cautionAmber : t.textPrimary);
            downrange.Set(Kilometres(flight.DownrangeMeters));

            bool suborbital = flight.PeriapsisMeters < 0.0;
            apoapsis.Set(double.IsInfinity(flight.ApoapsisMeters) ? "ESCAPE" : Kilometres(System.Math.Max(0.0, flight.ApoapsisMeters)));
            periapsis.Set(suborbital ? "SUB" : Kilometres(flight.PeriapsisMeters), suborbital ? t.textDim : t.nominalGreen);

            gLoad.Set(flight.GLoad.ToString("0.00"), flight.GLoad > limits.maxGLoad ? t.warningRed : t.textPrimary);
            double q = flight.DynamicPressurePa;
            dynamicPressure.Set((q / 1000.0).ToString("0.0"),
                q > limits.maxDynamicPressurePa ? t.warningRed : (q > limits.maxDynamicPressurePa * 0.85 ? t.cautionAmber : t.textPrimary));
            mach.Set(flight.AirDensityFraction > 0.0005 ? flight.MachNumber.ToString("0.0") : "--");

            pitch.Set(Signed(craft.Attitude.PitchDegrees, "0.0"));
            roll.Set(Signed(craft.Attitude.RollDegrees, "0.0"));
            yaw.Set(Signed(craft.Attitude.YawDegrees, "0.0"));

            if (craft.Navigation.GuidanceActive) guidanceStatus.Set("AUTO", t.nominalGreen);
            else if (craft.Attitude.HasControlAuthority) guidanceStatus.Set(craft.Attitude.RcsEnabled ? "MANUAL RCS" : "MANUAL TVC", t.cautionAmber);
            else guidanceStatus.Set("NO AUTHORITY", t.textDim);

            RefreshNavPage(craft, dt);

            double fraction = flight.Speed / flight.CircularOrbitSpeed;
            orbitalVelocity.Set((float)fraction, mission.OrbitGo || mission.IsComplete ? t.nominalGreen : t.accentBlue,
                flight.Speed.ToString("0") + " / " + flight.CircularOrbitSpeed.ToString("0") + " M/S");
            if (mission.IsComplete) orbitStatus.Set("IN ORBIT", t.nominalGreen);
            else if (mission.OrbitGo) orbitStatus.Set("ORBIT GO", t.BlinkPhase(Time.unscaledTime) ? t.nominalGreen : t.textDim);
            else orbitStatus.Set("SUBORBITAL", t.textDim);
        }

        private void RefreshNavPage(Spacecraft craft, float dt)
        {
            NavigationSystem nav = craft.Navigation;
            FlightModel flight = craft.Flight;
            CockpitTheme t = Theme;

            // The recorders run whenever the vehicle is flying so no history is lost while another page is up.
            if (flight.Released)
            {
                trajectory.Record((float)(flight.DownrangeMeters / 1000.0), (float)(flight.AltitudeMeters / 1000.0), dt);
                orbitMap.Record(flight, dt);
            }

            // Page 0 = message overlay, 1 = ascent profile, 2 = orbit map.
            int page;
            string message = null;
            if (!nav.Enabled) { page = 0; message = "NAV SYSTEM OFF"; }
            else if (!nav.Aligned) { page = 0; message = "ALIGNING PLATFORM\n" + (nav.AlignmentProgress * 100.0).ToString("0") + "%"; }
            else if (nav.Mode == NavMode.Standby) { page = 0; message = "NAV STANDBY\nSELECT MODE"; }
            else page = nav.Mode == NavMode.Ascent ? 1 : 2;

            if (page != shownPage)
            {
                shownPage = page;
                navOverlay.SetActive(page == 0);
                trajectory.gameObject.SetActive(page == 1);
                orbitMap.gameObject.SetActive(page == 2);
                navTitle.text = page == 1 ? "ASCENT PROFILE" : (page == 2 ? "ORBIT MAP" : "NAVIGATION");
            }

            if (page == 0)
            {
                navOverlayText.text = message;
                navOverlayText.color = nav.Enabled ? t.cautionAmber : t.textDim;
            }
            else if (page == 1)
            {
                trajectory.Redraw((float)(flight.DownrangeMeters / 1000.0), (float)(flight.AltitudeMeters / 1000.0),
                    (float)(craft.VehicleSpec.targetOrbitAltitudeMeters / 1000.0));
            }
            else
            {
                orbitMap.Redraw(flight);
            }
        }

        protected override void Animate(Spacecraft craft, float dt)
        {
            attitude.Animate((float)craft.Attitude.PitchDegrees, (float)craft.Attitude.RollDegrees,
                (float)craft.Flight.FlightPathAngleDegrees, craft.Flight.Speed > 20.0, dt);
        }
    }
}
