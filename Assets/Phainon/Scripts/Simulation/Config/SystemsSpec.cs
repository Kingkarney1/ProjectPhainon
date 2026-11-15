using System;

namespace Phainon.Simulation
{
    /// <summary>Tunable numbers for the spacecraft's housekeeping systems and warning limits.</summary>
    [Serializable]
    public class SystemsSpec
    {
        // Electrical.
        public double nominalBusVoltage = 28.0;
        public double batteryCapacityWattHours = 900;
        public double baseLoadWatts = 900;
        public double avionicsLoadWatts = 650;
        public double commsLoadWatts = 300;
        public double cabinLoadWatts = 550;
        public double fuelPumpLoadWatts = 1400;
        public double apuOutputWatts = 5200;
        public double apuStartSeconds = 2.5;
        public double batteryRechargeWatts = 400;

        // Propellant feed.
        public double tankPressurizeSeconds = 4.0;
        public double tankVentSeconds = 6.0;
        public double feedReadyThreshold = 0.95;

        /// <summary>Seconds the feed lines hold pressure after the pumps stop.</summary>
        public double feedHoldUpSeconds = 1.5;

        // Faults.
        /// <summary>A random fault is scheduled between these mission elapsed times, in seconds.</summary>
        public double faultEarliestSeconds = 25;
        public double faultLatestSeconds = 300;
        public double faultMinAltitudeMeters = 3000;
        /// <summary>Seconds for a failing pump to lose all feed pressure. The engine starves long before that.</summary>
        public double pumpFaultDecaySeconds = 300;
        public double batteryFaultLoadWatts = 9000;
        public double weakEngineThrustFactor = 0.88;

        // Navigation and communications.
        public double navAlignSeconds = 6.0;
        public double commsAcquireSeconds = 3.5;

        // Life support.
        public double oxygenStartFraction = 0.98;
        /// <summary>The reserve lasts about three weeks.</summary>
        public double oxygenUsePerSecond = 0.00000054;
        /// <summary>Share of the reserve lost each second through a stuck regulator.</summary>
        public double regulatorLeakPerSecond = 0.00012;
        /// <summary>Seconds the cabin air stays breathable once nothing is refreshing it.</summary>
        public double cabinAirEnduranceSeconds = 10800;
        public double lowCabinAirFraction = 0.25;
        /// <summary>Below this pressure the crew cannot stay conscious.</summary>
        public double hypoxiaPressureKpa = 55.0;
        public double lowCabinTempC = 8.0;
        /// <summary>Outside these temperatures the crew cannot keep working.</summary>
        public double survivableMinTempC = -8.0;
        public double survivableMaxTempC = 46.0;
        /// <summary>Seconds the crew last in a cabin that cannot support them before the flight is lost.</summary>
        public double crewIncapacitationSeconds = 240;
        public double nominalCabinPressureKpa = 101.3;
        public double unpoweredCabinPressureKpa = 94.0;
        /// <summary>Seconds for pressure to settle with the cabin loop running.</summary>
        public double cabinPressureTimeConstant = 5.0;
        /// <summary>Seconds for pressure to bleed down with the cabin loop off.</summary>
        public double cabinLeakTimeConstant = 400.0;
        public double initialCabinPressureKpa = 99.0;
        public double cabinTempMinC = 17.0;
        public double cabinTempMaxC = 27.0;
        public double cabinTempDefaultC = 21.0;
        public double unconditionedCabinTempC = 31.0;
        /// <summary>Seconds for temperature to reach the setpoint with the cabin loop running.</summary>
        public double cabinTempTimeConstant = 10.0;
        /// <summary>Seconds for the cabin to warm up with the loop off.</summary>
        public double cabinWarmTimeConstant = 240.0;
        public double initialCabinTempC = 26.5;

        // Warning limits.
        public double lowBatteryFraction = 0.25;
        public double lowFuelFraction = 0.10;
        public double maxDynamicPressurePa = 21000;
        public double maxGLoad = 4.0;
        public double lowCabinPressureKpa = 97.0;
        public double lowOxygenFraction = 0.20;
        public double highCabinTempC = 28.5;
        public double lowBusVoltage = 24.0;
        /// <summary>Sinking below this altitude during ascent raises the trajectory warning.</summary>
        public double trajectoryLowAltitudeMeters = 120000;
    }
}
