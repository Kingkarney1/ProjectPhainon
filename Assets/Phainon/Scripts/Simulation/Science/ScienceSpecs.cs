using System;

namespace Phainon.Simulation
{
    /// <summary>One scientific instrument carried by the spacecraft.</summary>
    [Serializable]
    public class InstrumentSpec
    {
        public string id;
        public string name;
        /// <summary>Short name for the selector dial.</summary>
        public string label;
        public double powerWatts = 180.0;
        /// <summary>True if the instrument has to be aimed: it is sighted along the forward window.</summary>
        public bool needsPointing = true;
        /// <summary>How far off the target's edge the sight line may wander, in degrees.</summary>
        public double pointingToleranceDegrees = 6.0;
        /// <summary>True if the target has to be in sunlight.</summary>
        public bool needsSunlight;
        /// <summary>Health at the start of the mission, 0..1. A worn instrument returns poorer data.</summary>
        public double condition = 1.0;

        public InstrumentSpec() { }
        public InstrumentSpec(string id, string name, string label, double powerWatts, bool needsPointing, bool needsSunlight, double condition)
        {
            this.id = id;
            this.name = name;
            this.label = label;
            this.powerWatts = powerWatts;
            this.needsPointing = needsPointing;
            this.needsSunlight = needsSunlight;
            this.condition = condition;
        }
    }

    /// <summary>One observation in a mission's science plan.</summary>
    [Serializable]
    public class ObservationSpec
    {
        public string id;
        public string title;
        public string instrumentId;
        /// <summary>Seconds of good data needed.</summary>
        public double durationSeconds = 60.0;
        /// <summary>Beyond this altitude the instrument resolves nothing useful, m.</summary>
        public double maxAltitudeMeters = 6.0e8;
        /// <summary>At or inside this altitude the data is as good as the instrument can make it, m.</summary>
        public double bestAltitudeMeters = 1.5e8;
        /// <summary>True if the body's landmark (its <see cref="BodySpec.featureLongitudeDegrees"/>) must face the spacecraft.</summary>
        public bool needsFeature;
        /// <summary>How far round from directly below the landmark may be and still be seen, in degrees.</summary>
        public double featureWindowDegrees = 55.0;
        /// <summary>What the crew learn, written into the log when the observation completes.</summary>
        public string finding;
        /// <summary>Written instead of <see cref="finding"/> when the data is poor.</summary>
        public string poorFinding;
        /// <summary>Story flag set when the observation completes.</summary>
        public string setsFlag;
        /// <summary>True if the result is something the mission plan did not predict.</summary>
        public bool anomaly;
        /// <summary>Seconds of downlink needed to send the result home.</summary>
        public double transmitSeconds = 20.0;
    }

    /// <summary>A completed observation held in the spacecraft's data store.</summary>
    [Serializable]
    public class ObservationRecord
    {
        public string observationId;
        public string title;
        /// <summary>0 to 100.</summary>
        public double quality;
        public double metSeconds;
        public bool transmitted;
    }
}
