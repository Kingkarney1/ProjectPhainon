using System;

namespace Phainon.Simulation
{
    /// <summary>
    /// The body the spacecraft is flying around. The flight model has one centre of gravity at a
    /// time; which one is decided by the mission (Earth for the launch, Jupiter for the expedition).
    /// </summary>
    [Serializable]
    public class BodySpec
    {
        public string name = "EARTH";
        /// <summary>Gravitational parameter, m^3/s^2.</summary>
        public double mu = 3.986004418e14;
        /// <summary>Radius of the surface, or of the cloud tops on a gas giant, in metres.</summary>
        public double radiusMeters = 6371000.0;
        /// <summary>Sidereal rotation period in seconds. The body turns in the same sense as a prograde orbit.</summary>
        public double rotationPeriodSeconds = 86164.0;
        /// <summary>Air density at zero altitude, kg/m^3. Zero for a body with no atmosphere.</summary>
        public double surfaceDensity = 1.225;
        public double scaleHeightMeters = 7200.0;
        /// <summary>
        /// Sinking below this altitude after reaching space ends the flight. Zero leaves the limit
        /// to the mission's own re-entry altitude.
        /// </summary>
        public double destructionAltitudeMeters;
        /// <summary>Body-fixed longitude of a landmark that observations can ask for, in degrees.</summary>
        public double featureLongitudeDegrees;

        public static BodySpec Earth() { return new BodySpec(); }

        public static BodySpec Jupiter()
        {
            return new BodySpec
            {
                name = "JUPITER",
                mu = 1.26686534e17,
                radiusMeters = 71492000.0,
                rotationPeriodSeconds = 35730.0,
                // Zero altitude is the one-bar level. The air above it thins quickly on this scale.
                surfaceDensity = 0.16,
                scaleHeightMeters = 27000.0,
                destructionAltitudeMeters = 250000.0,
                // Where the Great Red Spot sits on the Cassini map the scene draws.
                featureLongitudeDegrees = -49.0
            };
        }
    }
}
