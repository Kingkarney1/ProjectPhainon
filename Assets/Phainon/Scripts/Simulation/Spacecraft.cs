using System;
using System.Collections.Generic;

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
        /// <param name="story">
        /// A story mission to fly. It brings its own systems tuning, mission text, radio script and
        /// starting situation, which replace the ones passed in.
        /// </param>
        public Spacecraft(VehicleSpec vehicleSpec, SystemsSpec systemsSpec, MissionSpec missionSpec, CommsScriptSpec commsScript = null,
            int faultSeed = 0, StoryMissionSpec story = null)
        {
            if (story != null)
            {
                systemsSpec = story.systems;
                missionSpec = story.mission;
                commsScript = story.comms;
                Scenario = story.scenario;
            }
            VehicleSpec = vehicleSpec;
            SystemsSpec = systemsSpec;

            Flags = new HashSet<string>();
            Settings = new CockpitSettings();
            Power = new PowerSystem(systemsSpec);
            Fuel = new FuelSystem(vehicleSpec, systemsSpec);
            Engine = new EngineSystem(vehicleSpec, Fuel);
            Navigation = new NavigationSystem(systemsSpec);
            Comms = new CommsSystem(systemsSpec);
            LifeSupport = new LifeSupportSystem(systemsSpec);
            Flight = new FlightModel(vehicleSpec, Scenario != null ? Scenario.body : null);
            Attitude = new AttitudeController(vehicleSpec);
            Warnings = new WarningSystem();
            Mission = new MissionController(missionSpec, this);
            Faults = new FaultSystem(this, faultSeed);
            GroundControl = new GroundControl(commsScript, this);
            Science = new ScienceSystem(this, story != null ? story.instruments : null, story != null ? story.observations : null);
            if (story != null) Story = new StoryController(story, this);

            Engine.ShutDown += OnEngineShutDown;
            Engine.Ignited += OnEngineIgnited;
            Flight.Tick(0.0, 0.0, Flight.ComputeMass(0, Fuel), 90.0, 0.0);

            if (Scenario != null) ApplyScenario(Scenario);
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
        public ScienceSystem Science { get; private set; }
        /// <summary>The story mission being flown, or null on the standard launch.</summary>
        public StoryController Story { get; private set; }
        /// <summary>How the flight began, when it did not begin on the pad. Null on the standard launch.</summary>
        public ScenarioSpec Scenario { get; private set; }
        /// <summary>Things that have been said, chosen, found or done, remembered by name. Shared by the radio and the story.</summary>
        public HashSet<string> Flags { get; private set; }
        /// <summary>Seconds of simulated time since the flight began.</summary>
        public double SimClockSeconds { get; private set; }

        private bool flameoutLatched;

        /// <summary>Raised for every crew command, accepted or not, so the cockpit can give feedback.</summary>
        public event Action<ControlId, CommandResult> CommandHandled;

        /// <summary>Advances the whole spacecraft by one time step.</summary>
        public void Tick(double dt)
        {
            if (dt <= 0.0) return;
            SimClockSeconds += dt;

            Power.Tick(dt, ComputeElectricalLoad());
            bool powered = Power.IsBusPowered;

            Fuel.Tick(dt, powered);
            Navigation.Tick(dt, powered);
            Comms.Tick(dt, powered, GroundStationDistanceMeters, Flight.DynamicPressurePa);
            LifeSupport.Tick(dt, powered);

            Mission.PreTick(dt);
            Engine.Tick(dt);

            double mass = Flight.ComputeMass(Engine.ActiveStage, Fuel);
            Attitude.Tick(dt, Flight, Engine, Navigation.GuidanceActive, HoldFor(Navigation.Mode), powered, Power.ApuOnline);
            Flight.Tick(dt, Engine.ThrustNewtons, mass, Attitude.PitchDegrees, Attitude.YawDegrees);
            Science.Tick(dt);

            Mission.PostTick(dt);
            Faults.Tick(dt);
            EvaluateWarnings();
            // The story moves on after the mission has worded its guide, so the guide is worded again.
            if (Story != null) { Story.Tick(dt); Mission.RefreshGuide(); }
            GroundControl.Tick(dt, GroundStationDistanceMeters);
        }

        private static AttitudeHold HoldFor(NavMode mode)
        {
            return mode == NavMode.Orbit ? AttitudeHold.Prograde : (mode == NavMode.Retro ? AttitudeHold.Retrograde : AttitudeHold.Profile);
        }

        /// <summary>
        /// Length of the radio path. Near Earth the station is taken to be directly below, so it is
        /// the altitude; on an expedition it is the distance home.
        /// </summary>
        public double GroundStationDistanceMeters
        {
            get { return Scenario != null ? Scenario.earthDistanceMeters : Flight.AltitudeMeters; }
        }

        private double ComputeElectricalLoad()
        {
            double load = SystemsSpec.baseLoadWatts;
            if (Navigation.Enabled) load += SystemsSpec.avionicsLoadWatts;
            if (Comms.Enabled) load += SystemsSpec.commsLoadWatts;
            if (LifeSupport.Enabled) load += SystemsSpec.cabinLoadWatts;
            if (Fuel.PumpsEnabled) load += SystemsSpec.fuelPumpLoadWatts;
            if (Power.BatteryAFaulted && Power.BatteryAEnabled) load += SystemsSpec.batteryFaultLoadWatts;
            load += Science.PowerDrawWatts;
            return load;
        }

        // ---------------------------------------------------------------- starting away from the pad

        private void ApplyScenario(ScenarioSpec s)
        {
            double radial, tangential;
            s.ComputeStartVelocity(out radial, out tangential);
            Flight.SetState(s.startAltitudeMeters, radial, tangential, 0.0);
            Engine.BeginAtStage(s.activeStage);
            Engine.ThrottleCommand = SimMath.Clamp01(s.throttle);
            for (int i = 0; i < VehicleSpec.stages.Length; i++)
                Fuel.SetFraction(i, s.propellantFractions != null && i < s.propellantFractions.Length ? s.propellantFractions[i] : 0.0);
            Attitude.SetAttitude(s.pitchDegrees, s.yawDegrees, s.rollDegrees);

            Power.BatteryAEnabled = s.batteryA;
            Power.BatteryBEnabled = s.batteryB;
            Power.SetCharge(s.batteryACharge, s.batteryBCharge);
            if (s.mainBus) Power.ToggleMasterBus();
            Power.ApuEnabled = s.apu;
            bool powered = Power.IsBusPowered;

            Fuel.PumpsEnabled = s.fuelPump;
            Fuel.TankPressEnabled = s.tankPress;
            Comms.AntennaEnabled = s.commAntenna;
            if (s.comms) Comms.Toggle(powered);
            if (s.navSystem) Navigation.Toggle(powered);
            Navigation.Mode = (NavMode)(int)SimMath.Clamp(s.navMode, 0, 3);
            Navigation.GuidanceEnabled = s.guidance;
            Attitude.RcsEnabled = s.rcs;
            LifeSupport.SetState(s.cabinSystems && powered, s.oxygenFraction, s.cabinPressureKpa, s.cabinTemperatureC);
            Settings.CabinLightsOn = s.cabinLights;
            Settings.ExternalCameraOn = s.externalCamera;

            Mission.BeginInFlight(s.missionElapsedSeconds);
            if (s.initialFaults != null)
                for (int i = 0; i < s.initialFaults.Length; i++) Faults.Inject(s.initialFaults[i]);

            Flight.Tick(0.0, 0.0, Flight.ComputeMass(Engine.ActiveStage, Fuel), Attitude.PitchDegrees, Attitude.YawDegrees);
            EvaluateWarnings();
            if (Story != null) Story.Begin();
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
                case ControlId.Observe: result = Science.Observe(); break;
                case ControlId.TransmitData: result = Science.Transmit(); break;

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
                case ControlId.SciencePower: Science.PowerEnabled = on; result = Switched("SCIENCE PWR", on); break;

                case ControlId.Throttle:
                    Engine.ThrottleCommand = SimMath.Clamp01(value);
                    result = CommandResult.Ok(null);
                    break;
                case ControlId.ThrustLimit:
                    Engine.ThrustLimit = SimMath.Lerp(VehicleSpec.minThrustLimit, 1.0, value);
                    result = CommandResult.Ok(null);
                    break;
                case ControlId.NavMode:
                    Navigation.Mode = (NavMode)(int)SimMath.Clamp(Math.Round(value), 0, 3);
                    result = CommandResult.Ok("NAV MODE " + Navigation.Mode.ToString().ToUpperInvariant());
                    break;
                case ControlId.InstrumentSelect:
                    result = Science.SelectInstrument((int)Math.Round(value));
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

        /// <summary>
        /// Where a switch, dial or lever stands according to the simulation, in the same units the
        /// control sends. NaN for push buttons, which have no position. The cockpit uses it to set
        /// the panel when a flight starts or is loaded part-way through.
        /// </summary>
        public double GetControlValue(ControlId id)
        {
            switch (id)
            {
                case ControlId.BatteryA: return Power.BatteryAEnabled ? 1.0 : 0.0;
                case ControlId.BatteryB: return Power.BatteryBEnabled ? 1.0 : 0.0;
                case ControlId.Apu: return Power.ApuEnabled ? 1.0 : 0.0;
                case ControlId.FuelPump: return Fuel.PumpsEnabled ? 1.0 : 0.0;
                case ControlId.TankPress: return Fuel.TankPressEnabled ? 1.0 : 0.0;
                case ControlId.CommAntenna: return Comms.AntennaEnabled ? 1.0 : 0.0;
                case ControlId.Guidance: return Navigation.GuidanceEnabled ? 1.0 : 0.0;
                case ControlId.Rcs: return Attitude.RcsEnabled ? 1.0 : 0.0;
                case ControlId.ExternalCamera: return Settings.ExternalCameraOn ? 1.0 : 0.0;
                case ControlId.CabinLights: return Settings.CabinLightsOn ? 1.0 : 0.0;
                case ControlId.SciencePower: return Science.PowerEnabled ? 1.0 : 0.0;
                case ControlId.Throttle: return Engine.ThrottleCommand;
                case ControlId.ThrustLimit: return SimMath.InverseLerp(VehicleSpec.minThrustLimit, 1.0, Engine.ThrustLimit);
                case ControlId.NavMode: return (int)Navigation.Mode;
                case ControlId.InstrumentSelect: return Science.SelectedIndex;
                case ControlId.DisplayBrightness: return Settings.DisplayBrightness;
                case ControlId.CabinTemperature:
                    return SimMath.InverseLerp(SystemsSpec.cabinTempMinC, SystemsSpec.cabinTempMaxC, LifeSupport.TemperatureSetpointC);
                case ControlId.PitchTrim: return (Attitude.PitchTrim + 1.0) * 0.5;
                case ControlId.RollCommand: return (Attitude.RollDial + 1.0) * 0.5;
                default: return double.NaN;
            }
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
                if (Science.Active != null) return "OBSERVATION IN PROGRESS";
                if (GroundControl.AwaitingReply != null && !GroundControl.Script.timeAccelerationWhileAwaiting)
                    return "GROUND CONTROL IS WAITING FOR A REPLY";
                return null;
            }
        }

        /// <summary>Highest sensible rate here: close to the body the view would strobe at the top rates.</summary>
        public int MaxTimeWarp
        {
            get
            {
                // In body radii, so the same rule serves a low Earth orbit and a pass by Jupiter.
                double radii = Flight.AltitudeMeters / Flight.Body.radiusMeters;
                return radii < 0.3 ? 100 : (radii < 7.8 ? 1000 : 10000);
            }
        }

        // ---------------------------------------------------------------- orbit and geometry

        /// <summary>Velocity change the active stage can still make, m/s.</summary>
        public double AvailableDeltaV
        {
            get
            {
                double mass = Flight.ComputeMass(Engine.ActiveStage, Fuel);
                return OrbitMath.AvailableDeltaV(Engine.ActiveStageSpec.specificImpulseSeconds, mass, Fuel.GetPropellantKg(Engine.ActiveStage));
            }
        }

        /// <summary>How far the body has turned since the flight began, in degrees.</summary>
        public double BodySpinDegrees
        {
            get
            {
                double period = Flight.Body.rotationPeriodSeconds;
                return period > 0.0 ? SimMath.WrapDegrees(360.0 * (SimClockSeconds / period % 1.0)) : 0.0;
            }
        }

        /// <summary>Body-fixed longitude of the point directly below the spacecraft, in degrees.</summary>
        public double SubLongitudeDegrees
        {
            get { return SimMath.WrapDegrees(Flight.DownrangeAngle * SimMath.Rad2Deg % 360.0 - BodySpinDegrees); }
        }

        /// <summary>How far round the body its landmark is from directly below, in degrees. Zero is straight down.</summary>
        public double FeatureOffsetDegrees
        {
            get { return SimMath.WrapDegrees(Flight.Body.featureLongitudeDegrees - SubLongitudeDegrees); }
        }

        /// <summary>Seconds until the body's landmark comes within an angle of directly below, at the present rates.</summary>
        public double SecondsUntilFeatureWithin(double windowDegrees)
        {
            double offset = FeatureOffsetDegrees;
            if (Math.Abs(offset) <= windowDegrees) return 0.0;
            double spin = Flight.Body.rotationPeriodSeconds > 0.0 ? 360.0 / Flight.Body.rotationPeriodSeconds : 0.0;
            double orbit = Flight.TangentialVelocity / Flight.Radius * SimMath.Rad2Deg;
            // The landmark is carried round by the body's spin and left behind by the spacecraft's own motion.
            double rate = spin - orbit;
            if (Math.Abs(rate) < 1e-9) return double.PositiveInfinity;
            double travel = rate > 0.0
                ? (offset < 0.0 ? -windowDegrees - offset : 360.0 - windowDegrees - offset)
                : (offset > 0.0 ? offset - windowDegrees : 360.0 - windowDegrees + offset);
            return travel / Math.Abs(rate);
        }

        /// <summary>Share of the body's visible face that is in sunlight, 0..1. One near Earth, where it is not modelled.</summary>
        public double SunlitFraction
        {
            get
            {
                if (Scenario == null) return 1.0;
                double angle = Flight.DownrangeAngle - Scenario.sunAngleDegrees * SimMath.Deg2Rad;
                return 0.5 + 0.5 * Math.Cos(angle) * Math.Cos(Scenario.sunElevationDegrees * SimMath.Deg2Rad);
            }
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
                    bool armed = !Engine.Safed && (Engine.ActiveStage == 0 ? Engine.IgnitionInterlockOpen : Mission.Phase != MissionPhase.Failed);
                    // In orbit the lamp only invites ignition once the service module is the active stage.
                    return armed && Fuel.HasPropellant(Engine.ActiveStage) && (Mission.Phase != MissionPhase.OuterSpace || Engine.ActiveStage > 1)
                        ? IndicatorState.Blinking : IndicatorState.Off;
                case IndicatorId.EngineCutoff:
                    bool cutoffDue = Story != null ? Story.OrbitStable
                        : (Mission.Phase == MissionPhase.OuterSpace ? Mission.HasEscaped : Mission.OrbitGo);
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
                case IndicatorId.SciencePower: return Lit(Science.Powered);
                case IndicatorId.Observe:
                    if (!Science.Powered) return IndicatorState.Off;
                    if (Science.Active != null) return Science.Recording ? IndicatorState.On : IndicatorState.Blinking;
                    ObservationSpec next = Science.NextFor(Science.Selected);
                    return next != null && Science.Blocker(next) == null ? IndicatorState.Blinking : IndicatorState.Off;
                case IndicatorId.TransmitData:
                    if (Science.Transmitting) return IndicatorState.On;
                    return Science.CollectedCount > Science.TransmittedCount && Comms.LinkEstablished ? IndicatorState.Blinking : IndicatorState.Off;

                case IndicatorId.EngineRunning: return Lit(Engine.IsBurning);
                case IndicatorId.OrbitGo: return Lit(Story != null ? Story.OrbitStable : (Mission.OrbitGo || Mission.IsComplete));
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
            Warnings.Set(WarningId.CabinTempLow, powered && inFlight && LifeSupport.CabinTemperatureC < s.lowCabinTempC,
                WarningSeverity.Caution, "CABIN TEMPERATURE LOW");
            Warnings.Set(WarningId.CabinAirLow, powered && inFlight && LifeSupport.CabinAirQuality < s.lowCabinAirFraction,
                WarningSeverity.Warning, "CABIN AIR FOUL - RESTORE THE CABIN LOOP");
            Warnings.Set(WarningId.OxygenVenting, powered && LifeSupport.RegulatorFaulted,
                WarningSeverity.Warning, "O2 REGULATOR - RESERVE VENTING");
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
            Warnings.Set(WarningId.BurnWindow, powered && inFlight && Story != null && Story.BurnWindowOpen,
                WarningSeverity.Advisory, "PERIAPSIS CLOSE - CAPTURE BURN WINDOW");
        }

        // ---------------------------------------------------------------- save state

        /// <summary>True when the flight can be written down without losing anything: nothing is thrusting and nothing is being recorded.</summary>
        public string SaveInhibit
        {
            get
            {
                if (Mission.IsFailed) return "THE FLIGHT IS OVER";
                if (Engine.IsBurning || Engine.ThrustNewtons > 0.0) return "ENGINE RUNNING";
                if (Science.Active != null) return "OBSERVATION IN PROGRESS";
                return null;
            }
        }

        public SpacecraftSnapshot Capture()
        {
            var flags = new string[Flags.Count];
            Flags.CopyTo(flags);
            Array.Sort(flags, StringComparer.Ordinal);
            return new SpacecraftSnapshot
            {
                simClock = SimClockSeconds,
                flameoutLatched = flameoutLatched,
                flags = flags,
                settings = new SettingsSnapshot
                {
                    displayBrightness = Settings.DisplayBrightness, cabinLights = Settings.CabinLightsOn,
                    externalCamera = Settings.ExternalCameraOn, externalView = (int)Settings.ExternalCameraView
                },
                power = Power.Capture(),
                fuel = Fuel.Capture(),
                engine = Engine.Capture(),
                navigation = Navigation.Capture(),
                comms = Comms.Capture(),
                lifeSupport = LifeSupport.Capture(),
                flight = Flight.Capture(),
                attitude = Attitude.Capture(),
                warnings = Warnings.Capture(),
                mission = Mission.Capture(),
                faults = Faults.Capture(),
                groundControl = GroundControl.Capture(),
                science = Science.Capture(),
                story = Story != null ? Story.Capture() : new StorySnapshot()
            };
        }

        /// <summary>Puts a saved flight back. The spacecraft must have been built for the same mission.</summary>
        public void Restore(SpacecraftSnapshot s)
        {
            SimClockSeconds = s.simClock;
            flameoutLatched = s.flameoutLatched;
            Flags.Clear();
            if (s.flags != null) for (int i = 0; i < s.flags.Length; i++) Flags.Add(s.flags[i]);
            Settings.DisplayBrightness = s.settings.displayBrightness;
            Settings.CabinLightsOn = s.settings.cabinLights;
            Settings.ExternalCameraOn = s.settings.externalCamera;
            Settings.ExternalCameraView = (ExternalView)s.settings.externalView;

            Power.Restore(s.power);
            Fuel.Restore(s.fuel);
            Engine.Restore(s.engine);
            Navigation.Restore(s.navigation);
            Comms.Restore(s.comms);
            LifeSupport.Restore(s.lifeSupport);
            Flight.Restore(s.flight);
            Attitude.Restore(s.attitude);
            Faults.Restore(s.faults);
            Science.Restore(s.science);
            GroundControl.Restore(s.groundControl);
            if (Story != null && s.story != null) Story.Restore(s.story);
            Mission.Restore(s.mission);
            Warnings.Restore(s.warnings);
            Flight.Tick(0.0, Engine.ThrustNewtons, Flight.ComputeMass(Engine.ActiveStage, Fuel), Attitude.PitchDegrees, Attitude.YawDegrees);
        }
    }
}
