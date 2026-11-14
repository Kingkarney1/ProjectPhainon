using System;

namespace Phainon.Simulation
{
    /// <summary>Cabin environment: oxygen reserve, cabin pressure and temperature control.</summary>
    public class LifeSupportSystem
    {
        private readonly SystemsSpec spec;

        public LifeSupportSystem(SystemsSpec spec)
        {
            this.spec = spec;
            OxygenFraction = spec.oxygenStartFraction;
            CabinPressureKpa = spec.initialCabinPressureKpa;
            CabinTemperatureC = spec.initialCabinTempC;
            TemperatureSetpointC = spec.cabinTempDefaultC;
        }

        public bool Enabled { get; private set; }
        public double OxygenFraction { get; private set; }
        public double CabinPressureKpa { get; private set; }
        public double CabinTemperatureC { get; private set; }
        public double TemperatureSetpointC { get; private set; }

        /// <summary>True when the cabin loop is running and pressure is within limits.</summary>
        public bool Nominal { get { return Enabled && CabinPressureKpa >= spec.lowCabinPressureKpa; } }

        public CommandResult Toggle(bool busPowered)
        {
            if (Enabled)
            {
                Enabled = false;
                return CommandResult.Ok("CABIN SYSTEMS OFF");
            }
            if (!busPowered) return CommandResult.Rejected("CABIN SYSTEMS - MAIN BUS UNPOWERED");
            Enabled = true;
            return CommandResult.Ok("CABIN SYSTEMS ON");
        }

        /// <summary>Sets the temperature setpoint from a 0..1 dial position.</summary>
        public void SetTemperatureDial(double normalized)
        {
            TemperatureSetpointC = SimMath.Lerp(spec.cabinTempMinC, spec.cabinTempMaxC, normalized);
        }

        public void Tick(double dt, bool busPowered)
        {
            if (!busPowered) Enabled = false;

            double pressureTarget = Enabled ? spec.nominalCabinPressureKpa : spec.unpoweredCabinPressureKpa;
            double pressureTau = Enabled ? spec.cabinPressureTimeConstant : spec.cabinLeakTimeConstant;
            CabinPressureKpa = SimMath.Approach(CabinPressureKpa, pressureTarget, pressureTau, dt);

            double tempTarget = Enabled ? TemperatureSetpointC : spec.unconditionedCabinTempC;
            double tempTau = Enabled ? spec.cabinTempTimeConstant : spec.cabinWarmTimeConstant;
            CabinTemperatureC = SimMath.Approach(CabinTemperatureC, tempTarget, tempTau, dt);

            // Oxygen is only metered into the cabin while the loop is running.
            if (Enabled) OxygenFraction = Math.Max(0.0, OxygenFraction - spec.oxygenUsePerSecond * dt);
        }
    }
}
