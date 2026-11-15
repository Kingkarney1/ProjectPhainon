using System;

namespace Phainon.Simulation
{
    /// <summary>What the flight model needs written down to carry on later from the same place.</summary>
    [Serializable]
    public class FlightSnapshot
    {
        public bool released;
        public bool impacted;
        public double altitude;
        public double radialVelocity;
        public double tangentialVelocity;
        public double downrangeAngle;
    }

    /// <summary>
    /// Planar point-mass flight model around one round, non-rotating body.
    /// State is altitude plus radial and tangential velocity; thrust acts along the pitch angle.
    /// It is a believable approximation, not a full orbital mechanics solver.
    /// </summary>
    public class FlightModel
    {
        // Earth's values, kept as constants for the code that is only ever about Earth.
        public const double EarthMu = 3.986004418e14;       // m^3/s^2
        public const double EarthRadius = 6371000.0;        // m
        public const double SeaLevelDensity = 1.225;        // kg/m^3
        public const double ScaleHeight = 7200.0;           // m
        private const double MaxStep = 0.02;                // s, integration sub-step

        private readonly VehicleSpec vehicle;
        private readonly BodySpec body;

        public FlightModel(VehicleSpec vehicle, BodySpec body = null)
        {
            this.vehicle = vehicle;
            this.body = body ?? BodySpec.Earth();
        }

        /// <summary>The body whose gravity and atmosphere act on the vehicle.</summary>
        public BodySpec Body { get { return body; } }

        /// <summary>False while the vehicle is held on the pad by the launch clamps.</summary>
        public bool Released { get; private set; }
        public bool Impacted { get; private set; }

        public double AltitudeMeters { get; private set; }
        public double RadialVelocity { get; private set; }
        public double TangentialVelocity { get; private set; }
        /// <summary>Angle travelled around the body since the start of the flight, in radians.</summary>
        public double DownrangeAngle { get; private set; }
        public double DownrangeMeters { get { return DownrangeAngle * body.radiusMeters; } }

        public double Speed { get { return Math.Sqrt(RadialVelocity * RadialVelocity + TangentialVelocity * TangentialVelocity); } }
        public double VerticalSpeed { get { return RadialVelocity; } }
        public double Radius { get { return body.radiusMeters + AltitudeMeters; } }
        public double MassKg { get; private set; }
        public double DynamicPressurePa { get; private set; }
        /// <summary>Acceleration felt by the crew, in g.</summary>
        public double GLoad { get; private set; }
        /// <summary>Far point of the orbit above the surface. Infinite on an escape path.</summary>
        public double ApoapsisMeters { get; private set; }
        /// <summary>Near point of the orbit above the surface, on a closed orbit or an escape path alike.</summary>
        public double PeriapsisMeters { get; private set; }
        /// <summary>Semi-major axis of the current orbit in metres. Infinite on an escape trajectory.</summary>
        public double SemiMajorAxis { get; private set; }
        public double Eccentricity { get; private set; }
        /// <summary>
        /// Angle from periapsis to the vehicle along the orbit, in radians: 0 to 2 pi on a closed
        /// orbit, negative on the way in along an escape path and positive on the way out.
        /// </summary>
        public double TrueAnomaly { get; private set; }
        /// <summary>Specific orbital energy, v^2/2 - mu/r. Negative on a closed orbit.</summary>
        public double SpecificEnergy { get; private set; }
        public bool IsBound { get { return SpecificEnergy < 0.0; } }
        /// <summary>Time for one orbit in seconds. Infinite on an escape path.</summary>
        public double OrbitalPeriodSeconds
        {
            get { return IsBound ? OrbitMath.Period(body.mu, SemiMajorAxis) : double.PositiveInfinity; }
        }
        /// <summary>Seconds to the next periapsis; NaN when there will not be one.</summary>
        public double TimeToPeriapsisSeconds
        {
            get { return OrbitMath.TimeToPeriapsis(body.mu, SpecificEnergy, Eccentricity, TrueAnomaly); }
        }

        public double AirDensityFraction
        {
            get { return body.surfaceDensity > 0.0 ? Math.Exp(-AltitudeMeters / body.scaleHeightMeters) : 0.0; }
        }
        public double MachNumber { get { return Speed / SpeedOfSound(AltitudeMeters); } }

        /// <summary>Speed of a circular orbit at the current altitude.</summary>
        public double CircularOrbitSpeed { get { return Math.Sqrt(body.mu / Radius); } }

        /// <summary>Angle of the velocity vector above the local horizon, in degrees.</summary>
        public double FlightPathAngleDegrees
        {
            get { return Speed < 0.5 ? 90.0 : Math.Atan2(RadialVelocity, TangentialVelocity) * SimMath.Rad2Deg; }
        }

        public void ReleaseClamps() { Released = true; }

        /// <summary>Places the vehicle in free flight. Used when a mission starts away from the pad.</summary>
        public void SetState(double altitudeMeters, double radialVelocity, double tangentialVelocity, double downrangeAngle)
        {
            Released = true;
            Impacted = false;
            AltitudeMeters = altitudeMeters;
            RadialVelocity = radialVelocity;
            TangentialVelocity = tangentialVelocity;
            DownrangeAngle = downrangeAngle;
            GLoad = 0.0;
            UpdateOrbit();
        }

        public FlightSnapshot Capture()
        {
            return new FlightSnapshot
            {
                released = Released, impacted = Impacted, altitude = AltitudeMeters,
                radialVelocity = RadialVelocity, tangentialVelocity = TangentialVelocity, downrangeAngle = DownrangeAngle
            };
        }

        public void Restore(FlightSnapshot s)
        {
            Released = s.released;
            Impacted = s.impacted;
            AltitudeMeters = s.altitude;
            RadialVelocity = s.radialVelocity;
            TangentialVelocity = s.tangentialVelocity;
            DownrangeAngle = s.downrangeAngle;
            UpdateOrbit();
        }

        /// <summary>Total vehicle mass for a given active stage and propellant state.</summary>
        public double ComputeMass(int activeStage, FuelSystem fuel)
        {
            double mass = vehicle.payloadMassKg;
            for (int i = activeStage; i < vehicle.stages.Length; i++)
                mass += vehicle.stages[i].dryMassKg + fuel.GetPropellantKg(i);
            return mass;
        }

        /// <summary>Advances the state. Thrust and mass are treated as constant across the call.</summary>
        public void Tick(double dt, double thrustNewtons, double massKg, double pitchDegrees, double yawDegrees)
        {
            MassKg = massKg;
            if (Impacted) return;

            int steps = Math.Max(1, (int)Math.Ceiling(dt / MaxStep));
            double h = dt / steps;
            for (int i = 0; i < steps; i++) Step(h, thrustNewtons, massKg, pitchDegrees, yawDegrees);
            UpdateOrbit();
        }

        private void Step(double dt, double thrust, double mass, double pitchDegrees, double yawDegrees)
        {
            double r = Radius;
            double v = Speed;
            double gravity = body.mu / (r * r);
            double density = body.surfaceDensity > 0.0 ? body.surfaceDensity * Math.Exp(-AltitudeMeters / body.scaleHeightMeters) : 0.0;
            double q = 0.5 * density * v * v;
            double drag = q * vehicle.dragAreaCd;
            DynamicPressurePa = q;

            if (!Released)
            {
                // Clamped to the pad: the crew feel one g plus nothing else.
                GLoad = 1.0;
                return;
            }

            double pitch = pitchDegrees * SimMath.Deg2Rad;
            double yawCos = Math.Cos(yawDegrees * SimMath.Deg2Rad);

            double dragR = v > 1.0 ? drag * RadialVelocity / v : 0.0;
            double dragT = v > 1.0 ? drag * TangentialVelocity / v : 0.0;

            // Polar equations of motion: centrifugal relief on the radial axis, Coriolis on the tangential.
            double accR = (thrust * Math.Sin(pitch) - dragR) / mass - gravity + TangentialVelocity * TangentialVelocity / r;
            double accT = (thrust * Math.Cos(pitch) * yawCos - dragT) / mass - RadialVelocity * TangentialVelocity / r;

            RadialVelocity += accR * dt;
            TangentialVelocity += accT * dt;
            AltitudeMeters += RadialVelocity * dt;
            DownrangeAngle += TangentialVelocity / r * dt;

            GLoad = Math.Sqrt(thrust * thrust + drag * drag) / mass / EngineSystem.StandardGravity;

            if (AltitudeMeters <= 0.0)
            {
                AltitudeMeters = 0.0;
                if (RadialVelocity < -8.0 || Math.Abs(TangentialVelocity) > 30.0)
                {
                    Impacted = true;
                    RadialVelocity = 0.0;
                    TangentialVelocity = 0.0;
                }
                else if (RadialVelocity < 0.0)
                {
                    // Settled back onto the pad without enough thrust to fly.
                    RadialVelocity = 0.0;
                    TangentialVelocity = 0.0;
                    GLoad = 1.0;
                }
            }
        }

        /// <summary>Derives the orbit's shape from specific energy and angular momentum.</summary>
        private void UpdateOrbit()
        {
            double mu = body.mu;
            double r = Radius;
            double v = Speed;
            double energy = v * v * 0.5 - mu / r;
            double momentum = r * TangentialVelocity;
            double eccentricity = Math.Sqrt(Math.Max(0.0, 1.0 + 2.0 * energy * momentum * momentum / (mu * mu)));
            // Semi-latus rectum: the one size that means the same thing on a closed orbit and an open one.
            double p = momentum * momentum / mu;

            SpecificEnergy = energy;
            Eccentricity = eccentricity;
            PeriapsisMeters = p / (1.0 + eccentricity) - body.radiusMeters;

            if (energy >= 0.0)
            {
                ApoapsisMeters = double.PositiveInfinity;
                SemiMajorAxis = double.PositiveInfinity;
            }
            else
            {
                double semiMajor = -mu / (2.0 * energy);
                ApoapsisMeters = semiMajor * (1.0 + eccentricity) - body.radiusMeters;
                SemiMajorAxis = semiMajor;
            }

            // True anomaly from the orbit equation r = p / (1 + e cos v); climbing means past periapsis.
            if (eccentricity > 1e-5)
            {
                double cos = SimMath.Clamp((p / r - 1.0) / eccentricity, -1.0, 1.0);
                double anomaly = Math.Acos(cos);
                if (RadialVelocity >= 0.0) TrueAnomaly = anomaly;
                else TrueAnomaly = energy >= 0.0 ? -anomaly : 2.0 * Math.PI - anomaly;
            }
            else
            {
                TrueAnomaly = 0.0;
            }
        }

        private static double SpeedOfSound(double altitude)
        {
            // Coarse standard-atmosphere fit: 340 m/s at sea level, about 295 m/s in the stratosphere.
            return SimMath.Lerp(340.0, 295.0, altitude / 11000.0);
        }
    }
}
