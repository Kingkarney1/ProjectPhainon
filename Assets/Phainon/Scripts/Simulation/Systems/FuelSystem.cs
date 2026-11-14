using System;

namespace Phainon.Simulation
{
    /// <summary>Propellant quantity per stage and the pump/pressurisation state that feeds the engine.</summary>
    public class FuelSystem
    {
        private readonly SystemsSpec spec;
        private readonly double[] capacityKg;
        private readonly double[] propellantKg;

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

        public void Tick(double dt, bool busPowered)
        {
            bool pressurising = PumpsEnabled && TankPressEnabled && busPowered;
            double rate = pressurising ? dt / Math.Max(0.1, spec.tankPressurizeSeconds)
                                       : dt / Math.Max(0.1, spec.tankVentSeconds);
            FeedPressure = SimMath.MoveTowards(FeedPressure, pressurising ? 1.0 : 0.0, rate);
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
