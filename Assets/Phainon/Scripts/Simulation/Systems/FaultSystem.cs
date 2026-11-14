using System;

namespace Phainon.Simulation
{
    public enum FaultId { ApuTrip = 0, FuelPump = 1, BatteryA = 2, NavPlatform = 3, Antenna = 4, WeakUpperEngine = 5 }

    /// <summary>
    /// Things that break in flight. A fault is a latch inside the system it damages, so its effect is
    /// that system's real behaviour and the repair is the crew operating that system's own control.
    /// This class only decides when one happens, tracks which are active and words the procedure.
    /// </summary>
    public class FaultSystem
    {
        public static readonly int Count = Enum.GetValues(typeof(FaultId)).Length;

        private readonly Spacecraft craft;
        private readonly bool[] active = new bool[Count];
        private bool pending;

        /// <param name="seed">Picks the fault and its time. The same seed gives the same flight; 0 schedules nothing.</param>
        public FaultSystem(Spacecraft craft, int seed)
        {
            this.craft = craft;
            if (seed == 0) return;

            SystemsSpec spec = craft.SystemsSpec;
            var random = new Random(seed);
            Scheduled = (FaultId)random.Next(Count);
            ScheduledMetSeconds = SimMath.Lerp(spec.faultEarliestSeconds, spec.faultLatestSeconds, random.NextDouble());
            pending = true;
        }

        /// <summary>The fault this flight will have, and the earliest mission time it can appear.</summary>
        public FaultId Scheduled { get; private set; }
        public double ScheduledMetSeconds { get; private set; }

        public bool AnyActive { get; private set; }
        public bool IsActive(FaultId id) { return active[(int)id]; }

        public event Action<FaultId> Raised;
        public event Action<FaultId> Cleared;

        public void Tick(double dt)
        {
            if (pending && CanOccur(Scheduled))
            {
                pending = false;
                Inject(Scheduled);
            }

            bool any = false;
            for (int i = 0; i < Count; i++)
            {
                if (!active[i]) continue;
                if (Persists((FaultId)i)) { any = true; continue; }
                active[i] = false;
                if (Cleared != null) Cleared((FaultId)i);
            }
            AnyActive = any;
        }

        /// <summary>Breaks something now. Used by the schedule, by tests and by scripted scenarios.</summary>
        public void Inject(FaultId id)
        {
            if (active[(int)id]) return;
            switch (id)
            {
                case FaultId.ApuTrip: craft.Power.TripApu(); break;
                case FaultId.FuelPump: craft.Fuel.FaultPump(); break;
                case FaultId.BatteryA: craft.Power.BatteryAFaulted = true; break;
                case FaultId.NavPlatform: craft.Navigation.Trip(); break;
                case FaultId.Antenna: craft.Comms.FaultAntenna(); break;
                case FaultId.WeakUpperEngine: craft.Engine.DegradeThrust(craft.SystemsSpec.weakEngineThrustFactor); break;
            }
            active[(int)id] = true;
            AnyActive = true;
            if (Raised != null) Raised(id);
        }

        /// <summary>A fault waits for a moment when the crew can both notice it and do something about it.</summary>
        private bool CanOccur(FaultId id)
        {
            MissionController mission = craft.Mission;
            if (!mission.HasLiftedOff || mission.IsFailed) return false;
            if (mission.MissionElapsedSeconds < ScheduledMetSeconds) return false;
            if (craft.Flight.AltitudeMeters < craft.SystemsSpec.faultMinAltitudeMeters) return false;

            switch (id)
            {
                case FaultId.ApuTrip: return craft.Power.ApuOnline;
                case FaultId.FuelPump: return craft.Engine.IsBurning && craft.Fuel.FeedReady;
                case FaultId.BatteryA: return craft.Power.BatteryAEnabled && craft.Power.IsBusPowered;
                case FaultId.NavPlatform: return craft.Navigation.Aligned;
                case FaultId.Antenna: return craft.Comms.LinkEstablished;
                case FaultId.WeakUpperEngine: return craft.Engine.ActiveStage == 1 && craft.Engine.State == EngineState.Running;
                default: return false;
            }
        }

        /// <summary>True until the affected system is healthy again, or the damage has been isolated.</summary>
        private bool Persists(FaultId id)
        {
            switch (id)
            {
                case FaultId.ApuTrip: return !craft.Power.ApuOnline;
                // Not over while the pump switch is still off mid-cycle, or the guide would stop one click short.
                case FaultId.FuelPump: return craft.Fuel.PumpFaulted || !craft.Fuel.PumpsEnabled || !craft.Fuel.FeedReady;
                case FaultId.BatteryA: return craft.Power.BatteryAFaulted && craft.Power.BatteryAEnabled;
                case FaultId.NavPlatform: return !craft.Navigation.Aligned;
                case FaultId.Antenna: return craft.Comms.AntennaFaulted || !craft.Comms.LinkEstablished;
                case FaultId.WeakUpperEngine: return craft.Engine.ThrustFactor < 1.0;
                default: return false;
            }
        }

        /// <summary>The next step of the procedure for the most pressing active fault, or null when there is none.</summary>
        public string GetAdvice(out ControlId control)
        {
            control = ControlId.None;
            if (!AnyActive) return null;

            if (active[(int)FaultId.FuelPump])
            {
                control = ControlId.FuelPump;
                if (!craft.Fuel.PumpsEnabled) return "Set FUEL PUMP back ON now.";
                if (craft.Fuel.PumpFaulted) return "Feed pressure is dropping. Set FUEL PUMP OFF and straight back ON to restart the pump.";
                control = ControlId.None;
                return "Fuel pump restarted. Feed pressure is recovering.";
            }
            if (active[(int)FaultId.ApuTrip])
            {
                control = ControlId.Apu;
                if (craft.Power.ApuTripped) return "The APU has tripped. Set the APU switch OFF to reset it.";
                if (!craft.Power.ApuEnabled) return "Set the APU switch back ON to restart it.";
                control = ControlId.None;
                return "APU restarting.";
            }
            if (active[(int)FaultId.NavPlatform])
            {
                if (craft.Navigation.Enabled) return "Nav platform realigning. Guidance takes over again when it is aligned.";
                control = ControlId.NavSystem;
                return "The nav platform has dropped out. Press NAV SYSTEM to realign it.";
            }
            if (active[(int)FaultId.Antenna])
            {
                control = ControlId.CommAntenna;
                if (craft.Comms.AntennaFaulted) return "The radio link is down. Set S-BAND XMIT OFF to reset the transmitter.";
                if (!craft.Comms.AntennaEnabled) return "Set S-BAND XMIT back ON.";
                control = craft.Comms.Enabled ? ControlId.None : ControlId.Comms;
                return craft.Comms.Enabled ? "Transmitter reset. Reacquiring the signal." : "Press COMMS to bring the radio back.";
            }
            if (active[(int)FaultId.BatteryA])
            {
                control = ControlId.BatteryA;
                return "Battery A is draining the bus. Set BATT A OFF to isolate it.";
            }
            if (active[(int)FaultId.WeakUpperEngine] && craft.Engine.IsBurning)
                return "Engine thrust is low. Keep the throttle up and hold the burn: it will run longer than planned.";
            return null;
        }
    }
}
