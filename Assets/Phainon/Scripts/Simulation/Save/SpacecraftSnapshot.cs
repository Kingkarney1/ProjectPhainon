using System;

namespace Phainon.Simulation
{
    [Serializable]
    public class SettingsSnapshot
    {
        public double displayBrightness = 0.85;
        public bool cabinLights = true;
        public bool externalCamera = true;
        public int externalView;
    }

    /// <summary>
    /// The whole state of a flight, as plain data. Every part of the spacecraft writes down only
    /// what it cannot work out again, so a flight restored from this carries on exactly as it was.
    /// </summary>
    [Serializable]
    public class SpacecraftSnapshot
    {
        public double simClock;
        public bool flameoutLatched;
        public string[] flags = new string[0];
        public SettingsSnapshot settings = new SettingsSnapshot();
        public PowerSnapshot power = new PowerSnapshot();
        public FuelSnapshot fuel = new FuelSnapshot();
        public EngineSnapshot engine = new EngineSnapshot();
        public NavigationSnapshot navigation = new NavigationSnapshot();
        public CommsSnapshot comms = new CommsSnapshot();
        public LifeSupportSnapshot lifeSupport = new LifeSupportSnapshot();
        public FlightSnapshot flight = new FlightSnapshot();
        public AttitudeSnapshot attitude = new AttitudeSnapshot();
        public WarningSnapshot warnings = new WarningSnapshot();
        public MissionSnapshot mission = new MissionSnapshot();
        public FaultSnapshot faults = new FaultSnapshot();
        public GroundControlSnapshot groundControl = new GroundControlSnapshot();
        public ScienceSnapshot science = new ScienceSnapshot();
        public StorySnapshot story = new StorySnapshot();
    }
}
