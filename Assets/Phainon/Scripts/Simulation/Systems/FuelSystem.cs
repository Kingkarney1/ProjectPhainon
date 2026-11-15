using System;

namespace Phainon.Simulation
{
    [Serializable]
    public class FuelSnapshot
    {
        public bool pumps, tankPress, pumpFaulted;
        public double feedPressure, holdUp;
        public double[] propellantKg;
    }

    /// <summary>Propellant quantity per stage and the pump/pressurisation state that feeds the engine.</summary>
    public class FuelSystem
    {
        private readonly SystemsSpec spec;
        private readonly double[] capacityKg;
        private readonly double[] propellantKg;
        private double holdUp;

        public FuelSystem(VehicleSpec vehicle, SystemsSpec spec)
        {
            this.spec = spec;
            int count = vehicle.stages.Length;
            capacityKg = new double[count];
            propellantKg = new double[count];
            for (int i = 0; i < count; i++)
            {
                capacityKg[i] = vehicle.stages[i].propellantMassKg;
                propellantKg[i] = capacityKg[i];
            }
        }

        public bool PumpsEnabled { get; set; }
        public bool TankPressEnabled { get; set; }
        /// <summary>Latched by a fault: the pump runs but cannot hold pressure until its switch is cycled.</summary>
        public bool PumpFaulted { get; private set; }

        public void FaultPump() { PumpFaulted = true; }

        /// <summary>Feed line pressure as a 0..1 fraction of nominal.</summary>
        public double FeedPressure { get; private set; }
        public bool FeedReady { get { return FeedPressure >= spec.feedReadyThreshold; } }
        public double LastFlowKgPerSec { get; private set; }

        public double GetPropellantKg(int stage) { return IsValid(stage) ? propellantKg[stage] : 0.0; }

        public double GetFraction(int stage)
        {
            if (!IsValid(stage) || capacityKg[stage] <= 0) return 0.0;
            return propellantKg[stage] / capacityKg[stage];
        }

        public bool HasPropellant(int stage) { return IsValid(stage) && propellantKg[stage] > 0.0; }

        /// <summary>Sets how full a stage's tanks are, 0..1.</summary>
        public void SetFraction(int stage, double fraction)
        {
            if (IsValid(stage)) propellantKg[stage] = capacityKg[stage] * SimMath.Clamp01(fraction);
        }

        /// <summary>Adds propellant from a ground supply, up to the tank's capacity.</summary>
        public void TopUp(int stage, double fraction)
        {
            if (IsValid(stage)) propellantKg[stage] = Math.Min(capacityKg[stage], propellantKg[stage] + capacityKg[stage] * fraction);
        }

        public FuelSnapshot Capture()
        {
            return new FuelSnapshot
            {
                pumps = PumpsEnabled, tankPress = TankPressEnabled, pumpFaulted = PumpFaulted,
                feedPressure = FeedPressure, holdUp = holdUp, propellantKg = (double[])propellantKg.Clone()
            };
        }

        public void Restore(FuelSnapshot s)
        {
            PumpsEnabled = s.pumps;
            TankPressEnabled = s.tankPress;
            PumpFaulted = s.pumpFaulted;
            FeedPressure = s.feedPressure;
            holdUp = s.holdUp;
            if (s.propellantKg != null)
                for (int i = 0; i < propellantKg.Length && i < s.propellantKg.Length; i++) propellantKg[i] = s.propellantKg[i];
        }

        public void Tick(double dt, bool busPowered)
        {
            if (!PumpsEnabled) PumpFaulted = false;
            bool pressurising = PumpsEnabled && TankPressEnabled && busPowered;
            if (pressurising && !PumpFaulted)
            {
                holdUp = spec.feedHoldUpSeconds;
                FeedPressure = SimMath.MoveTowards(FeedPressure, 1.0, dt / Math.Max(0.1, spec.tankPressurizeSeconds));
            }
            else if (pressurising)
            {
                // A failing pump loses pressure slowly, which is the crew's warning.
                FeedPressure = SimMath.MoveTowards(FeedPressure, 0.0, dt / Math.Max(0.1, spec.pumpFaultDecaySeconds));
            }
            else
            {
                // The lines hold pressure for a moment after the pumps stop, long enough to cycle a switch.
                holdUp -= dt;
                if (holdUp <= 0.0) FeedPressure = SimMath.MoveTowards(FeedPressure, 0.0, dt / Math.Max(0.1, spec.tankVentSeconds));
            }
            LastFlowKgPerSec = 0.0;
        }

        /// <summary>Removes propellant from a stage and returns the amount actually drawn.</summary>
        public double Consume(int stage, double kg, double dt)
        {
            if (!IsValid(stage) || kg <= 0) return 0.0;
            double drawn = Math.Min(kg, propellantKg[stage]);
            propellantKg[stage] -= drawn;
            if (dt > 0) LastFlowKgPerSec = drawn / dt;
            return drawn;
        }

        private bool IsValid(int stage) { return stage >= 0 && stage < propellantKg.Length; }
    }
}
