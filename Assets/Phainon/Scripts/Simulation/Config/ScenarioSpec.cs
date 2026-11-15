using System;

namespace Phainon.Simulation
{
    /// <summary>
    /// Where and in what condition a flight begins when it does not begin on the launch pad:
    /// the body, the incoming path, which stage is left, what is switched on and what is broken.
    /// </summary>
    [Serializable]
    public class ScenarioSpec
    {
        public BodySpec body = BodySpec.Jupiter();

        // The incoming path, given the way a navigator would: speed far away, closest approach, where we are now.
        /// <summary>Speed relative to the body before its gravity took hold, m/s.</summary>
        public double excessSpeed = 5600.0;
        /// <summary>Closest approach of the incoming path above the surface, m.</summary>
        public double periapsisAltitudeMeters = 2.0e7;
        /// <summary>Altitude at the start of the flight, on the way in, m.</summary>
        public double startAltitudeMeters = 2.4e8;

        /// <summary>Stage that is still attached and active. Earlier ones are long gone.</summary>
        public int activeStage = 2;
        /// <summary>How full each stage's tanks are, 0..1, by stage index.</summary>
        public double[] propellantFractions = { 0.0, 0.0, 0.72 };

        // Attitude at the start, in the local level frame.
        public double pitchDegrees = -60.0;
        public double yawDegrees;
        public double rollDegrees;

        // Switch positions.
        public bool batteryA = true;
        public bool batteryB = true;
        public bool mainBus = true;
        public bool apu = true;
        public bool fuelPump;
        public bool tankPress;
        public bool commAntenna = true;
        public bool comms = true;
        public bool navSystem;
        public int navMode;
        public bool guidance = true;
        public bool rcs;
        public bool cabinSystems;
        public bool cabinLights = true;
        public bool externalCamera = true;
        public double throttle;

        // Condition of the consumables and the cabin.
        public double batteryACharge = 0.38;
        public double batteryBCharge = 0.81;
        public double oxygenFraction = 0.74;
        public double cabinPressureKpa = 86.0;
        public double cabinTemperatureC = 11.0;

        /// <summary>Faults already present when the flight starts.</summary>
        public FaultId[] initialFaults = { FaultId.ApuTrip, FaultId.NavPlatform, FaultId.Antenna };

        /// <summary>
        /// Direction to the Sun in the flight plane, in degrees, measured the way the vehicle travels
        /// from the point above which the flight starts.
        /// </summary>
        public double sunAngleDegrees = -38.0;
        /// <summary>How far the Sun stands out of the flight plane, in degrees.</summary>
        public double sunElevationDegrees = 6.0;
        /// <summary>Distance to the ground station on Earth, m. It sets the radio's light delay.</summary>
        public double earthDistanceMeters = 7.78e11;
        /// <summary>What the mission clock reads at the start, in seconds.</summary>
        public double missionElapsedSeconds;

        /// <summary>Radial and tangential velocity at the starting altitude on the inbound leg.</summary>
        public void ComputeStartVelocity(out double radial, out double tangential)
        {
            double mu = body.mu;
            double periapsisRadius = body.radiusMeters + periapsisAltitudeMeters;
            double radius = body.radiusMeters + startAltitudeMeters;
            // Conservation of energy and angular momentum between here and closest approach.
            double periapsisSpeed = Math.Sqrt(excessSpeed * excessSpeed + 2.0 * mu / periapsisRadius);
            double speed = Math.Sqrt(excessSpeed * excessSpeed + 2.0 * mu / radius);
            tangential = periapsisRadius * periapsisSpeed / radius;
            radial = -Math.Sqrt(Math.Max(0.0, speed * speed - tangential * tangential));
        }
    }
}
