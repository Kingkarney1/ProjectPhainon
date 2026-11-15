using System;

namespace Phainon.Simulation
{
    /// <summary>Two-body orbit formulas shared by the flight model, the displays and the mission logic.</summary>
    public static class OrbitMath
    {
        /// <summary>Speed on an orbit of semi-major axis <paramref name="semiMajor"/> at radius <paramref name="radius"/>.</summary>
        public static double VisViva(double mu, double radius, double semiMajor)
        {
            return Math.Sqrt(Math.Max(0.0, mu * (2.0 / radius - 1.0 / semiMajor)));
        }

        /// <summary>Period of a bound orbit in seconds.</summary>
        public static double Period(double mu, double semiMajor)
        {
            return 2.0 * Math.PI * Math.Sqrt(semiMajor * semiMajor * semiMajor / mu);
        }

        /// <summary>
        /// Seconds until the next periapsis passage. On an escape path that is only defined on the
        /// way in; once past periapsis the answer is NaN, because the vehicle never comes back.
        /// </summary>
        /// <param name="energy">Specific orbital energy, v^2/2 - mu/r.</param>
        /// <param name="trueAnomaly">Radians from periapsis; negative on the inbound leg of an escape path.</param>
        public static double TimeToPeriapsis(double mu, double energy, double eccentricity, double trueAnomaly)
        {
            if (Math.Abs(energy) < 1e-9 || Math.Abs(eccentricity - 1.0) < 1e-9) return double.NaN;

            if (energy < 0.0)
            {
                double a = -mu / (2.0 * energy);
                double period = Period(mu, a);
                if (eccentricity < 1e-9) return 0.0;
                double e = eccentricity;
                double eccentricAnomaly = 2.0 * Math.Atan2(Math.Sqrt(1.0 - e) * Math.Sin(trueAnomaly * 0.5),
                                                           Math.Sqrt(1.0 + e) * Math.Cos(trueAnomaly * 0.5));
                double meanAnomaly = eccentricAnomaly - e * Math.Sin(eccentricAnomaly);
                if (meanAnomaly < 0.0) meanAnomaly += 2.0 * Math.PI;
                double sincePeriapsis = meanAnomaly / (2.0 * Math.PI) * period;
                return period - sincePeriapsis;
            }

            if (trueAnomaly >= 0.0) return double.NaN;
            double h = eccentricity;
            double negA = mu / (2.0 * energy);
            double tanHalf = Math.Sqrt((h - 1.0) / (h + 1.0)) * Math.Tan(trueAnomaly * 0.5);
            tanHalf = SimMath.Clamp(tanHalf, -0.999999999, 0.999999999);
            double hyperbolicAnomaly = 2.0 * Atanh(tanHalf);
            double mean = h * Math.Sinh(hyperbolicAnomaly) - hyperbolicAnomaly;
            return -mean * Math.Sqrt(negA * negA * negA / mu);
        }

        /// <summary>
        /// Speed to lose at periapsis so that the orbit's far point comes down to
        /// <paramref name="targetApoapsisRadius"/>. Negative when the orbit is already inside it.
        /// </summary>
        public static double CaptureDeltaV(double mu, double energy, double periapsisRadius, double targetApoapsisRadius)
        {
            if (periapsisRadius <= 0.0 || targetApoapsisRadius <= periapsisRadius) return double.NaN;
            double now = Math.Sqrt(Math.Max(0.0, 2.0 * (energy + mu / periapsisRadius)));
            double wanted = VisViva(mu, periapsisRadius, (periapsisRadius + targetApoapsisRadius) * 0.5);
            return now - wanted;
        }

        /// <summary>Velocity change a stage can still make: the rocket equation.</summary>
        public static double AvailableDeltaV(double specificImpulseSeconds, double massNowKg, double propellantKg)
        {
            double dry = massNowKg - propellantKg;
            if (dry <= 0.0 || propellantKg <= 0.0) return 0.0;
            return specificImpulseSeconds * EngineSystem.StandardGravity * Math.Log(massNowKg / dry);
        }

        /// <summary>Seconds of burn at full thrust needed for a velocity change.</summary>
        public static double BurnSeconds(double deltaV, double specificImpulseSeconds, double massNowKg, double thrustNewtons)
        {
            if (deltaV <= 0.0 || thrustNewtons <= 0.0) return 0.0;
            double exhaust = specificImpulseSeconds * EngineSystem.StandardGravity;
            double burned = massNowKg * (1.0 - Math.Exp(-deltaV / exhaust));
            return burned / (thrustNewtons / exhaust);
        }

        private static double Atanh(double x) { return 0.5 * Math.Log((1.0 + x) / (1.0 - x)); }
    }
}
