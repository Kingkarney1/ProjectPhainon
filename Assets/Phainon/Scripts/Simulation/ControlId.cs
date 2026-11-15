namespace Phainon.Simulation
{
    /// <summary>
    /// Every crew-operable control in the cockpit. A physical control only carries one of these
    /// identifiers; <see cref="Spacecraft.HandleControl"/> decides what it does.
    /// </summary>
    public enum ControlId
    {
        None = 0,

        // Push buttons.
        MainPower = 10,
        NavSystem = 11,
        Comms = 12,
        CabinSystems = 13,
        LaunchSequence = 14,
        EngineIgnition = 15,
        EngineCutoff = 16,
        StageSeparation = 17,
        Abort = 18,
        MasterAlarm = 19,
        CameraSelect = 20,
        Observe = 21,
        TransmitData = 22,

        // Toggle switches.
        BatteryA = 30,
        BatteryB = 31,
        Apu = 32,
        FuelPump = 33,
        TankPress = 34,
        CommAntenna = 35,
        Guidance = 36,
        Rcs = 37,
        ExternalCamera = 38,
        CabinLights = 39,
        SciencePower = 40,

        // Continuous controls.
        Throttle = 50,
        ThrustLimit = 51,
        NavMode = 52,
        DisplayBrightness = 53,
        CabinTemperature = 54,
        PitchTrim = 55,
        RollCommand = 56,
        InstrumentSelect = 57
    }

    /// <summary>Panel lamps and annunciators driven by spacecraft state.</summary>
    public enum IndicatorId
    {
        None = 0,
        MainPower = 10,
        NavSystem = 11,
        Comms = 12,
        CabinSystems = 13,
        LaunchSequence = 14,
        EngineIgnition = 15,
        EngineCutoff = 16,
        StageSeparation = 17,
        Abort = 18,
        MasterAlarm = 19,
        CameraSelect = 20,
        Observe = 21,
        TransmitData = 22,

        BatteryA = 30,
        BatteryB = 31,
        Apu = 32,
        FuelPump = 33,
        TankPress = 34,
        CommAntenna = 35,
        Guidance = 36,
        Rcs = 37,
        ExternalCamera = 38,
        CabinLights = 39,
        SciencePower = 40,

        // Status-only annunciators.
        EngineRunning = 70,
        OrbitGo = 71,
        MasterCaution = 72,
        LaunchGo = 73,
        StageSepReady = 74,
        FuelLow = 75
    }

    public enum IndicatorState { Off, On, Blinking }

    /// <summary>Which external camera feeds the mission display.</summary>
    public enum ExternalView { AftHull = 0, Chase = 1 }
}
