using System;

namespace Phainon.Simulation
{
    [Serializable]
    public class LifeSupportSnapshot
    {
        public bool enabled, regulatorFaulted;
        public double oxygen, pressure, temperature, setpoint, airQuality = 1.0, uninhabitableSeconds;
    }

    /// <summary>
    /// Cabin environment: oxygen reserve, cabin pressure, temperature control and whether the air
    /// can still be breathed. The crew are part of this system: a cabin that cannot support them
    /// for long enough ends the flight.
    /// </summary>
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
            CabinAirQuality = 1.0;
        }

        public bool Enabled { get; private set; }
        /// <summary>Oxygen left in the reserve tanks, 0..1.</summary>
        public double OxygenFraction { get; private set; }
        public double CabinPressureKpa { get; private set; }
        public double CabinTemperatureC { get; private set; }
        public double TemperatureSetpointC { get; private set; }
        /// <summary>How breathable the cabin air is, 1 fresh to 0 spent. It only recovers while the loop runs with oxygen to feed it.</summary>
        public double CabinAirQuality { get; private set; }
        /// <summary>Latched by a fault: the reserve vents overboard until the cabin loop is shut down.</summary>
        public bool RegulatorFaulted { get; private set; }
        /// <summary>Seconds the cabin has been unable to support the crew, counted without a break.</summary>
        public double UninhabitableSeconds { get; private set; }

        /// <summary>True when the cabin loop is running and pressure is within limits.</summary>
        public bool Nominal { get { return Enabled && CabinPressureKpa >= spec.lowCabinPressureKpa; } }

        /// <summary>True while the crew can stay conscious and working in the cabin as it is now.</summary>
        public bool Habitable
        {
            get
            {
                return CabinAirQuality > 0.0 && CabinPressureKpa >= spec.hypoxiaPressureKpa
                       && CabinTemperatureC >= spec.survivableMinTempC && CabinTemperatureC <= spec.survivableMaxTempC;
            }
        }

        /// <summary>True once the cabin has been uninhabitable for longer than the crew can last.</summary>
        public bool CrewIncapacitated { get { return UninhabitableSeconds >= spec.crewIncapacitationSeconds; } }

        /// <summary>Days of oxygen left at the normal rate of use.</summary>
        public double OxygenDaysRemaining
        {
            get { return spec.oxygenUsePerSecond > 0.0 ? OxygenFraction / spec.oxygenUsePerSecond / 86400.0 : double.PositiveInfinity; }
        }

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

        public void FaultRegulator() { RegulatorFaulted = true; }

        /// <summary>Sets the temperature setpoint from a 0..1 dial position.</summary>
        public void SetTemperatureDial(double normalized)
        {
            TemperatureSetpointC = SimMath.Lerp(spec.cabinTempMinC, spec.cabinTempMaxC, normalized);
        }

        /// <summary>Sets the cabin's condition directly. Used when a flight starts part-way through.</summary>
        public void SetState(bool enabled, double oxygenFraction, double pressureKpa, double temperatureC)
        {
            Enabled = enabled;
            OxygenFraction = SimMath.Clamp01(oxygenFraction);
            CabinPressureKpa = pressureKpa;
            CabinTemperatureC = temperatureC;
        }

        public LifeSupportSnapshot Capture()
        {
            return new LifeSupportSnapshot
            {
                enabled = Enabled, regulatorFaulted = RegulatorFaulted, oxygen = OxygenFraction, pressure = CabinPressureKpa,
                temperature = CabinTemperatureC, setpoint = TemperatureSetpointC, airQuality = CabinAirQuality,
                uninhabitableSeconds = UninhabitableSeconds
            };
        }

        public void Restore(LifeSupportSnapshot s)
        {
            Enabled = s.enabled;
            RegulatorFaulted = s.regulatorFaulted;
            OxygenFraction = s.oxygen;
            CabinPressureKpa = s.pressure;
            CabinTemperatureC = s.temperature;
            TemperatureSetpointC = s.setpoint;
            CabinAirQuality = s.airQuality;
            UninhabitableSeconds = s.uninhabitableSeconds;
        }

        public void Tick(double dt, bool busPowered)
        {
            if (!busPowered) Enabled = false;
            // Shutting the loop closes the regulator, which is what clears a stuck one.
            if (!Enabled) RegulatorFaulted = false;

            double pressureTarget = Enabled ? spec.nominalCabinPressureKpa : spec.unpoweredCabinPressureKpa;
            double pressureTau = Enabled ? spec.cabinPressureTimeConstant : spec.cabinLeakTimeConstant;
            CabinPressureKpa = SimMath.Approach(CabinPressureKpa, pressureTarget, pressureTau, dt);

            double tempTarget = Enabled ? TemperatureSetpointC : spec.unconditionedCabinTempC;
            double tempTau = Enabled ? spec.cabinTempTimeConstant : spec.cabinWarmTimeConstant;
            CabinTemperatureC = SimMath.Approach(CabinTemperatureC, tempTarget, tempTau, dt);

            // Oxygen is only metered into the cabin while the loop is running.
            if (Enabled)
            {
                double use = spec.oxygenUsePerSecond + (RegulatorFaulted ? spec.regulatorLeakPerSecond : 0.0);
                OxygenFraction = Math.Max(0.0, OxygenFraction - use * dt);
            }

            // The air is refreshed by the loop as long as there is oxygen to feed it; otherwise the crew use it up.
            if (Enabled && OxygenFraction > 0.0)
                CabinAirQuality = SimMath.Approach(CabinAirQuality, 1.0, spec.cabinPressureTimeConstant * 4.0, dt);
            else
                CabinAirQuality = Math.Max(0.0, CabinAirQuality - dt / Math.Max(1.0, spec.cabinAirEnduranceSeconds));

            UninhabitableSeconds = Habitable ? 0.0 : UninhabitableSeconds + dt;
        }
    }
}
