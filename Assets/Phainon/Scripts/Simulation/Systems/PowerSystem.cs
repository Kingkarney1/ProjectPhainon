using System;

namespace Phainon.Simulation
{
    /// <summary>
    /// Electrical power: two batteries feeding a master bus, plus an auxiliary power unit
    /// that carries the load and recharges the batteries once started.
    /// </summary>
    public class PowerSystem
    {
        private readonly SystemsSpec spec;
        private double apuSpool;
        private bool wasPowered;

        public PowerSystem(SystemsSpec spec)
        {
            this.spec = spec;
            BatteryACharge = 1.0;
            BatteryBCharge = 1.0;
        }

        public bool BatteryAEnabled { get; set; }
        public bool BatteryBEnabled { get; set; }
        public bool MasterBusEnabled { get; private set; }
        public bool ApuEnabled { get; set; }
        /// <summary>Latched by a fault. The APU will not run again until its switch has been set off.</summary>
        public bool ApuTripped { get; private set; }
        /// <summary>A shorted battery A loads the bus for as long as it stays connected.</summary>
        public bool BatteryAFaulted { get; set; }

        public double BatteryACharge { get; private set; }
        public double BatteryBCharge { get; private set; }
        public double BusVoltage { get; private set; }
        public double LoadWatts { get; private set; }
        public double GenerationWatts { get; private set; }

        public bool HasBatterySource
        {
            get { return (BatteryAEnabled && BatteryACharge > 0.001) || (BatteryBEnabled && BatteryBCharge > 0.001); }
        }

        /// <summary>True when the master bus is live and can run avionics.</summary>
        public bool IsBusPowered { get { return MasterBusEnabled && (HasBatterySource || ApuOnline); } }

        /// <summary>True once the auxiliary power unit has spooled up and is delivering power.</summary>
        public bool ApuOnline { get { return apuSpool >= 1.0; } }
        public double ApuSpoolFraction { get { return apuSpool; } }
        public double AverageBatteryCharge { get { return (BatteryACharge + BatteryBCharge) * 0.5; } }

        /// <summary>Raised when the master bus goes live or dead.</summary>
        public event Action<bool> BusPowerChanged;

        /// <summary>Toggles the master bus. Closing it needs at least one battery online.</summary>
        public CommandResult ToggleMasterBus()
        {
            if (MasterBusEnabled)
            {
                MasterBusEnabled = false;
                return CommandResult.Ok("MAIN BUS OFF");
            }
            if (!HasBatterySource)
                return CommandResult.Rejected("NO POWER SOURCE - SET BATT A OR BATT B ON");
            MasterBusEnabled = true;
            return CommandResult.Ok("MAIN BUS ON");
        }

        public void TripApu()
        {
            ApuTripped = true;
            apuSpool = 0.0;
        }

        public void Tick(double dt, double loadWatts)
        {
            if (!ApuEnabled) ApuTripped = false;
            // The APU needs a live bus to start, but keeps running on its own output once online.
            bool busHasSource = MasterBusEnabled && (HasBatterySource || apuSpool >= 1.0);
            bool apuCanRun = ApuEnabled && !ApuTripped && busHasSource;
            apuSpool = SimMath.MoveTowards(apuSpool, apuCanRun ? 1.0 : 0.0, dt / Math.Max(0.1, spec.apuStartSeconds));

            bool powered = IsBusPowered;
            LoadWatts = powered ? loadWatts : 0.0;
            GenerationWatts = ApuOnline ? spec.apuOutputWatts : 0.0;

            if (powered)
            {
                double net = GenerationWatts - LoadWatts;
                if (net < 0) DrainBatteries(-net, dt);
                else ChargeBatteries(Math.Min(net, spec.batteryRechargeWatts), dt);
            }

            // Voltage sags with battery depletion when running on batteries alone.
            double targetVoltage = 0.0;
            if (powered)
            {
                double sag = ApuOnline ? 0.0 : (1.0 - AverageEnabledCharge()) * 5.0 + 0.6;
                targetVoltage = spec.nominalBusVoltage - sag;
            }
            BusVoltage = SimMath.Approach(BusVoltage, targetVoltage, 0.25, dt);

            if (powered != wasPowered)
            {
                wasPowered = powered;
                if (BusPowerChanged != null) BusPowerChanged(powered);
            }
        }

        private double AverageEnabledCharge()
        {
            double sum = 0; int count = 0;
            if (BatteryAEnabled) { sum += BatteryACharge; count++; }
            if (BatteryBEnabled) { sum += BatteryBCharge; count++; }
            return count == 0 ? 0.0 : sum / count;
        }

        private void DrainBatteries(double watts, double dt)
        {
            bool a = BatteryAEnabled && BatteryACharge > 0.0;
            bool b = BatteryBEnabled && BatteryBCharge > 0.0;
            int sources = (a ? 1 : 0) + (b ? 1 : 0);
            if (sources == 0) return;
            // Watt-seconds drawn from each battery, converted to a fraction of capacity.
            double fraction = watts / sources * dt / (spec.batteryCapacityWattHours * 3600.0);
            if (a) BatteryACharge = Math.Max(0.0, BatteryACharge - fraction);
            if (b) BatteryBCharge = Math.Max(0.0, BatteryBCharge - fraction);
        }

        private void ChargeBatteries(double watts, double dt)
        {
            double fraction = watts * 0.5 * dt / (spec.batteryCapacityWattHours * 3600.0);
            if (BatteryAEnabled) BatteryACharge = Math.Min(1.0, BatteryACharge + fraction);
            if (BatteryBEnabled) BatteryBCharge = Math.Min(1.0, BatteryBCharge + fraction);
        }
    }
}
