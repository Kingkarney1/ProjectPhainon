using System;

namespace Phainon.Simulation
{
    /// <summary>Cockpit settings that affect presentation only, kept with the simulation so there is one source of truth.</summary>
    public class CockpitSettings
    {
        public double DisplayBrightness = 0.85;
        public bool CabinLightsOn = true;
        public bool ExternalCameraOn = true;
        public ExternalView ExternalCameraView = ExternalView.AftHull;
    }

    /// <summary>
    /// Composition root of the simulation. Owns every subsystem, advances them in a fixed order
    /// and is the single entry point for crew commands and indicator state.
    /// </summary>
    public class Spacecraft
    {
        /// <param name="faultSeed">Seed for this flight's random fault. Zero means nothing breaks unless injected.</param>
        public Spacecraft(VehicleSpec vehicleSpec, SystemsSpec systemsSpec, MissionSpec missionSpec, CommsScriptSpec commsScript = null, int faultSeed = 0)
        {
            VehicleSpec = vehicleSpec;
            SystemsSpec = systemsSpec;

            Settings = new CockpitSettings();
            Power = new PowerSystem(systemsSpec);
            Fuel = new FuelSystem(vehicleSpec, systemsSpec);
            Engine = new EngineSystem(vehicleSpec, Fuel);
            Navigation = new NavigationSystem(systemsSpec);
            Comms = new CommsSystem(systemsSpec);
            LifeSupport = new LifeSupportSystem(systemsSpec);
            Flight = new FlightModel(vehicleSpec);
            Attitude = new AttitudeController(vehicleSpec);
            Warnings = new WarningSystem();
            Mission = new MissionController(missionSpec, this);
            Faults = new FaultSystem(this, faultSeed);
            GroundControl = new GroundControl(commsScript, this);

            Engine.ShutDown += OnEngineShutDown;
            Engine.Ignited += OnEngineIgnited;
            Flight.Tick(0.0, 0.0, Flight.ComputeMass(0, Fuel), 90.0, 0.0);
        }

        public VehicleSpec VehicleSpec { get; private set; }
        public SystemsSpec SystemsSpec { get; private set; }
        public CockpitSettings Settings { get; private set; }
        public PowerSystem Power { get; private set; }
        public FuelSystem Fuel { get; private set; }
        public EngineSystem Engine { get; private set; }
        public NavigationSystem Navigation { get; private set; }
        public CommsSystem Comms { get; private set; }
        public LifeSupportSystem LifeSupport { get; private set; }
        public FlightModel Flight { get; private set; }
        public AttitudeController Attitude { get; private set; }
        public WarningSystem Warnings { get; private set; }
        public MissionController Mission { get; private set; }
        public FaultSystem Faults { get; private set; }
        public GroundControl GroundControl { get; private set; }

        private bool flameoutLatched;

        /// <summary>Raised for every crew command, accepted or not, so the cockpit can give feedback.</summary>
        public event Action<ControlId, CommandResult> CommandHandled;

        /// <summary>Advances the whole spacecraft by one time step.</summary>
        public void Tick(double dt)
        {
            if (dt <= 0.0) return;

            Power.Tick(dt, ComputeElectricalLoad());
            bool powered = Power.IsBusPowered;

            Fuel.Tick(dt, powered);
            Navigation.Tick(dt, powered);
            Comms.Tick(dt, powered, Flight.AltitudeMeters, Flight.DynamicPressurePa);
            LifeSupport.Tick(dt, powered);

            Mission.PreTick(dt);
            Engine.Tick(dt);

            double mass = Flight.ComputeMass(Engine.ActiveStage, Fuel);
            Attitude.Tick(dt, Flight, Engine, Navigation.GuidanceActive, Navigation.Mode == NavMode.Orbit, powered, Power.ApuOnline);
            Flight.Tick(dt, Engine.ThrustNewtons, mass, Attitude.PitchDegrees, Attitude.YawDegrees);

            Mission.PostTick(dt);
            Faults.Tick(dt);
            EvaluateWarnings();
            // The ground station is taken to be directly below, so the signal path is the altitude.
            GroundControl.Tick(dt, Flight.AltitudeMeters);
        }

        private double ComputeElectricalLoad()
        {
            double load = SystemsSpec.baseLoadWatts;
            if (Navigation.Enabled) load += SystemsSpec.avionicsLoadWatts;
            if (Comms.Enabled) load += SystemsSpec.commsLoadWatts;
            if (LifeSupport.Enabled) load += SystemsSpec.cabinLoadWatts;
            if (Fuel.PumpsEnabled) load += SystemsSpec.fuelPumpLoadWatts;
            if (Power.BatteryAFaulted && Power.BatteryAEnabled) load += SystemsSpec.batteryFaultLoadWatts;
            return load;
        }

        // ---------------------------------------------------------------- crew commands

        /// <summary>
        /// Routes a control input to the system it operates. Buttons send 1 on press, switches send
        /// 0 or 1, continuous controls send 0..1 and detented dials send their detent index.
        /// </summary>
        public CommandResult HandleControl(ControlId id, double value)
        {
            bool on = value > 0.5;
            bool powered = Power.IsBusPowered;
            CommandResult result;

            switch (id)
            {
                case ControlId.MainPower: result = Power.ToggleMasterBus(); break;
                case ControlId.NavSystem: result = Navigation.Toggle(powered); break;
                case ControlId.Comms: result = Comms.Toggle(powered); break;
                case ControlId.CabinSystems: result = LifeSupport.Toggle(powered); break;
                case ControlId.LaunchSequence:
                    result = powered ? Mission.ToggleLaunchSequence() : CommandResult.Rejected("LAUNCH SEQUENCER UNPOWERED");
                    break;
                case ControlId.EngineIgnition: result = Engine.TryIgnite(powered, Power.ApuOnline); break;
                case ControlId.EngineCutoff: result = Engine.Cutoff(EngineShutdownReason.Commanded); break;
                case ControlId.StageSeparation: result = Mission.RequestStageSeparation(); break;
                case ControlId.Abort: result = Mission.Abort(); break;
                case ControlId.MasterAlarm: result = Warnings.Acknowledge(); break;
                case ControlId.CameraSelect:
                    Settings.ExternalCameraView = Settings.ExternalCameraView == ExternalView.AftHull ? ExternalView.Chase : ExternalView.AftHull;
                    result = CommandResult.Ok(Settings.ExternalCameraView == ExternalView.Chase ? "CAMERA 2 - CHASE" : "CAMERA 1 - AFT HULL");
                    break;

                case ControlId.BatteryA: Power.BatteryAEnabled = on; result = Switched("BATT A", on); break;
                case ControlId.BatteryB: Power.BatteryBEnabled = on; result = Switched("BATT B", on); break;
                case ControlId.Apu: Power.ApuEnabled = on; result = Switched("APU", on); break;
                case ControlId.FuelPump: Fuel.PumpsEnabled = on; result = Switched("FUEL PUMP", on); break;
                case ControlId.TankPress: Fuel.TankPressEnabled = on; result = Switched("TANK PRESS", on); break;
                case ControlId.CommAntenna: Comms.AntennaEnabled = on; result = Switched("S-BAND XMIT", on); break;
                case ControlId.Guidance: Navigation.GuidanceEnabled = on; result = Switched("GUIDANCE", on); break;
                case ControlId.Rcs: Attitude.RcsEnabled = on; result = Switched("RCS", on); break;
                case ControlId.ExternalCamera: Settings.ExternalCameraOn = on; result = Switched("EXT CAMERA", on); break;
                case ControlId.CabinLights: Settings.CabinLightsOn = on; result = Switched("CABIN LIGHTS", on); break;

                case ControlId.Throttle:
                    Engine.ThrottleCommand = SimMath.Clamp01(value);
                    result = CommandResult.Ok(null);
                    break;
                case ControlId.ThrustLimit:
                    Engine.ThrustLimit = SimMath.Lerp(VehicleSpec.minThrustLimit, 1.0, value);
                    result = CommandResult.Ok(null);
                    break;
                case ControlId.NavMode:
                    Navigation.Mode = (NavMode)(int)SimMath.Clamp(Math.Round(value), 0, 2);
                    result = CommandResult.Ok("NAV MODE " + Navigation.Mode.ToString().ToUpperInvariant());
                    break;
                case ControlId.DisplayBrightness:
                    Settings.DisplayBrightness = SimMath.Clamp01(value);
                    result = CommandResult.Ok(null);
                    break;
                case ControlId.CabinTemperature:
                    LifeSupport.SetTemperatureDial(value);
                    result = CommandResult.Ok(null);
                    break;
                case ControlId.PitchTrim:
                    Attitude.PitchTrim = SimMath.Clamp(value * 2.0 - 1.0, -1.0, 1.0);
                    result = CommandResult.Ok(null);
                    break;
                case ControlId.RollCommand:
                    Attitude.RollDial = SimMath.Clamp(value * 2.0 - 1.0, -1.0, 1.0);
                    result = CommandResult.Ok(null);
                    break;
                default:
                    result = CommandResult.Rejected("UNASSIGNED CONTROL");
                    break;
            }

            if (CommandHandled != null) CommandHandled(id, result);
            return result;
        }

        private static CommandResult Switched(string label, bool on)
        {
            return CommandResult.Ok(label + (on ? " ON" : " OFF"));
        }

        /// <summary>Hand-controller rates, each -1..1. Call every frame while the crew are flying manually.</summary>
        public void SetAttitudeInput(double pitch, double yaw, double roll)
        {
            Attitude.PitchInput = SimMath.Clamp(pitch, -1.0, 1.0);
            Attitude.YawInput = SimMath.Clamp(yaw, -1.0, 1.0);
            Attitude.RollInput = SimMath.Clamp(roll, -1.0, 1.0);
        }

        // ---------------------------------------------------------------- time acceleration

        /// <summary>Why time may not be accelerated right now, or null when it may.</summary>
        public string TimeWarpInhibit
        {
            get
            {
                if (Mission.Phase != MissionPhase.OuterSpace) return "ONLY IN ORBIT OR BEYOND";
                if (Engine.IsBurning || Engine.ThrustNewtons > 0.0) return "ENGINE RUNNING";
                if (Warnings.MasterAlarmActive) return "MASTER ALARM";
                if (GroundControl.AwaitingReply != null) return "GROUND CONTROL IS WAITING FOR A REPLY";
                return null;
            }
        }

        /// <summary>Highest sensible rate here: close to Earth the view would strobe at the top rates.</summary>
        public int MaxTimeWarp
        {
            get { return Flight.AltitudeMeters < 2000000.0 ? 100 : (Flight.AltitudeMeters < 50000000.0 ? 1000 : 10000); }
        }

        // ---------------------------------------------------------------- indicators

        /// <summary>State of a panel lamp. Lamps are dark whenever the main bus is unpowered.</summary>
        public IndicatorState GetIndicator(IndicatorId id)
        {
            // Battery switch lamps are wired straight to the batteries, so they work with the bus off.
            if (id == IndicatorId.BatteryA) return Lit(Power.BatteryAEnabled);
            if (id == IndicatorId.BatteryB) return Lit(Power.BatteryBEnabled);
            if (!Power.IsBusPowered) return IndicatorState.Off;

            switch (id)
            {
                case IndicatorId.MainPower: return IndicatorState.On;
                case IndicatorId.NavSystem:
                    return Navigation.Aligned ? IndicatorState.On : (Navigation.Enabled ? IndicatorState.Blinking : IndicatorState.Off);
                case IndicatorId.Comms:
                    return Comms.LinkEstablished ? IndicatorState.On : (Comms.Enabled ? IndicatorState.Blinking : IndicatorState.Off);
                case IndicatorId.CabinSystems:
                    return LifeSupport.Nominal ? IndicatorState.On : (LifeSupport.Enabled ? IndicatorState.Blinking : IndicatorState.Off);
                case IndicatorId.LaunchSequence:
                    if (Mission.Phase != MissionPhase.PreLaunch) return IndicatorState.Off;
                    if (Mission.LaunchState == LaunchSequenceState.Idle) return Mission.AllGo ? IndicatorState.Blinking : IndicatorState.Off;
                    return Mission.LaunchState == LaunchSequenceState.Hold ? IndicatorState.Blinking : IndicatorState.On;
                case IndicatorId.EngineIgnition:
                    if (Engine.IsBurning) return IndicatorState.On;
                    bool armed = Engine.ActiveStage == 0 ? Engine.IgnitionInterlockOpen : Mission.Phase != MissionPhase.Failed;
                    // In orbit the lamp only invites ignition once the service module is the active stage.
                    return armed && Fuel.HasPropellant(Engine.ActiveStage) && (Mission.Phase != MissionPhase.OuterSpace || Engine.ActiveStage > 1)
                        ? IndicatorState.Blinking : IndicatorState.Off;
                case IndicatorId.EngineCutoff:
                    bool cutoffDue = Mission.Phase == MissionPhase.OuterSpace ? Mission.HasEscaped : Mission.OrbitGo;
                    return cutoffDue && Engine.IsBurning ? IndicatorState.Blinking : IndicatorState.Off;
                case IndicatorId.StageSeparation:
                case IndicatorId.StageSepReady:
                    return Mission.StageSeparationReady ? IndicatorState.Blinking : IndicatorState.Off;
                case IndicatorId.Abort:
                    return Mission.IsFailed ? IndicatorState.On : IndicatorState.Off;
                case IndicatorId.MasterAlarm:
                    return Warnings.MasterAlarmActive ? IndicatorState.Blinking : (Warnings.AnyWarning ? IndicatorState.On : IndicatorState.Off);
                case IndicatorId.MasterCaution:
                    return Warnings.AnyCaution ? IndicatorState.On : IndicatorState.Off;

                case IndicatorId.Apu:
                    return Power.ApuOnline ? IndicatorState.On : (Power.ApuEnabled ? IndicatorState.Blinking : IndicatorState.Off);
                case IndicatorId.FuelPump: return Lit(Fuel.PumpsEnabled);
                case IndicatorId.TankPress:
                    return Fuel.FeedReady ? IndicatorState.On : (Fuel.TankPressEnabled ? IndicatorState.Blinking : IndicatorState.Off);
                case IndicatorId.CommAntenna: return Lit(Comms.AntennaEnabled);
                case IndicatorId.Guidance:
                    return Navigation.GuidanceActive ? IndicatorState.On : (Navigation.GuidanceEnabled ? IndicatorState.Blinking : IndicatorState.Off);
                case IndicatorId.Rcs: return Attitude.RcsFiring ? IndicatorState.Blinking : Lit(Attitude.RcsEnabled);
                case IndicatorId.ExternalCamera: return Lit(Settings.ExternalCameraOn);
                case IndicatorId.CameraSelect: return Lit(Settings.ExternalCameraOn && Settings.ExternalCameraView == ExternalView.Chase);
                case IndicatorId.CabinLights: return Lit(Settings.CabinLightsOn);

                case IndicatorId.EngineRunning: return Lit(Engine.IsBurning);
                case IndicatorId.OrbitGo: return Lit(Mission.OrbitGo || Mission.IsComplete);
                case IndicatorId.LaunchGo: return Lit(Mission.Phase == MissionPhase.PreLaunch && Mission.AllGo);
                case IndicatorId.FuelLow: return Lit(Warnings.IsActive(WarningId.FuelLow));
                default: return IndicatorState.Off;
            }
        }

        private static IndicatorState Lit(bool on) { return on ? IndicatorState.On : IndicatorState.Off; }

        // ---------------------------------------------------------------- warnings

        private void OnEngineShutDown(int stage, EngineShutdownReason reason)
        {
            if (reason == EngineShutdownReason.FeedPressureLost) flameoutLatched = true;
        }

        private void OnEngineIgnited(int stage) { flameoutLatched = false; }

        private void EvaluateWarnings()
        {
            SystemsSpec s = SystemsSpec;
            MissionPhase phase = Mission.Phase;
            bool powered = Power.IsBusPowered;
            bool inFlight = Mission.HasLiftedOff && phase != MissionPhase.Failed;
            bool ascending = inFlight && phase != MissionPhase.OuterSpace;
            WarningSeverity cabinSeverity = inFlight ? WarningSeverity.Warning : WarningSeverity.Caution;

            Warnings.Set(WarningId.BusPowerLoss, inFlight && !powered, WarningSeverity.Warning, "MAIN BUS POWER LOSS");
            Warnings.Set(WarningId.BatteryLow, powered && !Power.ApuOnline && Power.AverageBatteryCharge < s.lowBatteryFraction,
                WarningSeverity.Caution, "BATTERY LOW");
            Warnings.Set(WarningId.ApuOffline, inFlight && powered && !Power.ApuOnline, WarningSeverity.Caution, "APU OFFLINE");
            Warnings.Set(WarningId.FuelLow, powered && Engine.IsBurning && Fuel.GetFraction(Engine.ActiveStage) < s.lowFuelFraction,
                WarningSeverity.Caution, "PROPELLANT LOW");
            Warnings.Set(WarningId.DynamicPressureHigh, powered && Flight.DynamicPressurePa > s.maxDynamicPressurePa,
                WarningSeverity.Warning, "DYNAMIC PRESSURE HIGH - THROTTLE DOWN");
            Warnings.Set(WarningId.HighG, powered && inFlight && Flight.GLoad > s.maxGLoad,
                WarningSeverity.Warning, "G LOAD HIGH - THROTTLE DOWN");
            Warnings.Set(WarningId.CabinPressureLow, powered && LifeSupport.CabinPressureKpa < s.lowCabinPressureKpa,
                cabinSeverity, "CABIN PRESSURE LOW");
            Warnings.Set(WarningId.OxygenLow, powered && LifeSupport.OxygenFraction < s.lowOxygenFraction,
                WarningSeverity.Warning, "OXYGEN RESERVE LOW");
            Warnings.Set(WarningId.CabinTempHigh, powered && LifeSupport.CabinTemperatureC > s.highCabinTempC,
                WarningSeverity.Caution, "CABIN TEMPERATURE HIGH");
            Warnings.Set(WarningId.CommLoss, inFlight && powered && !Comms.LinkEstablished, WarningSeverity.Caution, "COMM LINK LOST");
            Warnings.Set(WarningId.NavNotAligned, inFlight && powered && !Navigation.Aligned, WarningSeverity.Caution, "NAV PLATFORM NOT ALIGNED");
            Warnings.Set(WarningId.EngineFlameout, powered && flameoutLatched && ascending, WarningSeverity.Warning, "ENGINE FLAMEOUT");
            Warnings.Set(WarningId.StageSepRequired, powered && Mission.StageSeparationReady, WarningSeverity.Advisory, "STAGE SEPARATION REQUIRED");
            Warnings.Set(WarningId.FeedPressureDropping, powered && Fuel.PumpFaulted && Fuel.PumpsEnabled,
                WarningSeverity.Caution, "FEED PRESSURE DROPPING");
            Warnings.Set(WarningId.BatteryFault, powered && Power.BatteryAFaulted && Power.BatteryAEnabled,
                WarningSeverity.Caution, "BATT A FAULT - HIGH DRAIN");
            Warnings.Set(WarningId.ThrustLow, powered && Engine.State == EngineState.Running && Engine.ThrustFactor < 0.97,
                WarningSeverity.Caution, "ENGINE THRUST LOW");
            Warnings.Set(WarningId.TrajectoryLow,
                powered && ascending && Flight.VerticalSpeed < -20.0 && Flight.AltitudeMeters < s.trajectoryLowAltitudeMeters,
                WarningSeverity.Warning, "SINK RATE - TRAJECTORY LOW");
        }
    }
}
