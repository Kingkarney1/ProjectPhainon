using System;
using System.Collections.Generic;
using System.Globalization;

namespace Phainon.Simulation
{
    [Serializable]
    public class ScienceSnapshot
    {
        public bool powered;
        public int selected;
        public string activeId;
        public double progress, qualitySum, qualityTime;
        public int interruptions;
        public bool wasValid;
        public double stalledSeconds;
        public double[] conditions = new double[0];
        public ObservationRecord[] records = new ObservationRecord[0];
        public bool transmitting;
        public double transmitProgress;
    }

    /// <summary>
    /// The science payload: which instrument is selected, the observation in progress and the data
    /// store. An observation only gathers data while the spacecraft really is where it has to be,
    /// pointed where it has to point, with the instrument powered; the quality of the result is
    /// measured from those same conditions.
    /// </summary>
    public class ScienceSystem
    {
        private readonly Spacecraft craft;
        private readonly InstrumentSpec[] instruments;
        private readonly ObservationSpec[] plan;
        private readonly double[] conditions;
        private readonly List<ObservationRecord> records = new List<ObservationRecord>();

        private ObservationSpec active;
        private double progress;
        private double qualitySum;
        private double qualityTime;
        private int interruptions;
        private bool wasValid;
        private double stalledSeconds;
        private double transmitProgress;

        /// <summary>An observation that has gathered nothing for this long is given up, so it cannot hold the crew up for ever.</summary>
        public const double AbandonAfterSeconds = 60.0;

        public ScienceSystem(Spacecraft craft, InstrumentSpec[] instruments, ObservationSpec[] plan)
        {
            this.craft = craft;
            this.instruments = instruments ?? new InstrumentSpec[0];
            this.plan = plan ?? new ObservationSpec[0];
            conditions = new double[this.instruments.Length];
            for (int i = 0; i < conditions.Length; i++) conditions[i] = this.instruments[i].condition;
        }

        public IReadOnlyList<InstrumentSpec> Instruments { get { return instruments; } }
        public IReadOnlyList<ObservationSpec> Plan { get { return plan; } }
        public IReadOnlyList<ObservationRecord> Records { get { return records; } }

        /// <summary>The SCIENCE PWR switch.</summary>
        public bool PowerEnabled { get; set; }
        /// <summary>True when the payload has power and can work.</summary>
        public bool Powered { get { return PowerEnabled && craft.Power.IsBusPowered && instruments.Length > 0; } }
        public int SelectedIndex { get; private set; }
        public InstrumentSpec Selected { get { return instruments.Length > 0 ? instruments[SelectedIndex] : null; } }
        public double SelectedCondition { get { return instruments.Length > 0 ? conditions[SelectedIndex] : 0.0; } }

        /// <summary>The observation being recorded, or null.</summary>
        public ObservationSpec Active { get { return active; } }
        /// <summary>Progress of the observation being recorded, 0..1.</summary>
        public double ActiveProgress { get { return active != null ? SimMath.Clamp01(progress / Math.Max(1.0, active.durationSeconds)) : 0.0; } }
        /// <summary>True while the observation in progress is gathering data this moment.</summary>
        public bool Recording { get; private set; }
        public bool Transmitting { get; private set; }
        /// <summary>Progress of the downlink of the record being sent, 0..1.</summary>
        public double TransmitProgress { get; private set; }

        public int CollectedCount { get { return records.Count; } }
        public int TransmittedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < records.Count; i++) if (records[i].transmitted) n++;
                return n;
            }
        }
        /// <summary>Electrical load of the payload right now.</summary>
        public double PowerDrawWatts { get { return Powered && Selected != null ? Selected.powerWatts : 0.0; } }

        public event Action<ObservationSpec> ObservationStarted;
        public event Action<ObservationRecord, ObservationSpec> ObservationCompleted;
        /// <summary>The observation in progress lost its target for too long and was given up.</summary>
        public event Action<ObservationSpec> ObservationAbandoned;
        public event Action<ObservationRecord> RecordTransmitted;

        public bool IsCollected(string observationId) { return FindRecord(observationId) != null; }

        public ObservationRecord FindRecord(string observationId)
        {
            for (int i = 0; i < records.Count; i++) if (records[i].observationId == observationId) return records[i];
            return null;
        }

        // ---------------------------------------------------------------- crew commands

        public CommandResult SelectInstrument(int index)
        {
            if (instruments.Length == 0) return CommandResult.Ok(null);
            index = (int)SimMath.Clamp(index, 0, instruments.Length - 1);
            if (index != SelectedIndex && active != null) CancelActive();
            SelectedIndex = index;
            return CommandResult.Ok("INSTRUMENT - " + instruments[index].name);
        }

        /// <summary>Starts the next observation in the plan that the selected instrument can make from here.</summary>
        public CommandResult Observe()
        {
            if (instruments.Length == 0 || plan.Length == 0) return CommandResult.Rejected("NO OBSERVATION PLAN LOADED");
            if (!Powered) return CommandResult.Rejected("SCIENCE PAYLOAD UNPOWERED - SET SCIENCE PWR ON");
            if (active != null) return CommandResult.Rejected("OBSERVATION ALREADY RUNNING");

            string firstReason = null;
            for (int i = 0; i < plan.Length; i++)
            {
                ObservationSpec candidate = plan[i];
                if (candidate.instrumentId != Selected.id || IsCollected(candidate.id)) continue;
                string reason = Blocker(candidate);
                if (reason == null)
                {
                    active = candidate;
                    progress = 0.0;
                    qualitySum = 0.0;
                    qualityTime = 0.0;
                    interruptions = 0;
                    wasValid = true;
                    stalledSeconds = 0.0;
                    if (ObservationStarted != null) ObservationStarted(candidate);
                    return CommandResult.Ok("RECORDING - " + candidate.title);
                }
                if (firstReason == null) firstReason = reason;
            }
            return CommandResult.Rejected(firstReason ?? "NOTHING LEFT TO OBSERVE WITH THIS INSTRUMENT");
        }

        /// <summary>Starts sending the oldest result that has not gone home yet.</summary>
        public CommandResult Transmit()
        {
            if (records.Count == 0) return CommandResult.Rejected("DATA STORE EMPTY");
            if (TransmittedCount == records.Count) return CommandResult.Rejected("ALL DATA ALREADY SENT");
            if (Transmitting) return CommandResult.Rejected("DOWNLINK ALREADY RUNNING");
            if (!craft.Comms.LinkEstablished) return CommandResult.Rejected("NO LINK - DATA HELD IN STORAGE");
            Transmitting = true;
            transmitProgress = 0.0;
            return CommandResult.Ok("DOWNLINK STARTED");
        }

        // ---------------------------------------------------------------- conditions

        /// <summary>Why an observation cannot gather data right now, or null when it can.</summary>
        public string Blocker(ObservationSpec observation)
        {
            InstrumentSpec instrument = FindInstrument(observation.instrumentId);
            if (instrument == null) return "INSTRUMENT NOT FITTED";
            FlightModel flight = craft.Flight;

            if (flight.AltitudeMeters > observation.maxAltitudeMeters)
                return "OUT OF RANGE - CLOSE TO " + Thousands(observation.maxAltitudeMeters / 1000.0) + " KM";
            if (instrument.needsPointing && PointingErrorDegrees > instrument.pointingToleranceDegrees)
                return "NOT POINTED - AIM THE WINDOW AT " + flight.Body.name;
            if (instrument.needsSunlight && craft.SunlitFraction < 0.2)
                return "TARGET IN SHADOW";
            if (observation.needsFeature && Math.Abs(craft.FeatureOffsetDegrees) > observation.featureWindowDegrees)
                return "TARGET ON THE FAR SIDE - IN VIEW IN " + Duration(craft.SecondsUntilFeatureWithin(observation.featureWindowDegrees));
            return null;
        }

        /// <summary>How far the window's sight line is from the body's disc, in degrees. Zero while it is on the disc.</summary>
        public double PointingErrorDegrees
        {
            get
            {
                FlightModel flight = craft.Flight;
                double discRadius = Math.Asin(SimMath.Clamp01(flight.Body.radiusMeters / flight.Radius)) * SimMath.Rad2Deg;
                return Math.Max(0.0, craft.Attitude.WindowOffNadirDegrees - discRadius * 0.85);
            }
        }

        /// <summary>How good a sample taken this moment would be, 0..1.</summary>
        private double SampleQuality(ObservationSpec observation, InstrumentSpec instrument)
        {
            double altitude = craft.Flight.AltitudeMeters;
            double range = 1.0 - 0.6 * SimMath.InverseLerp(observation.bestAltitudeMeters, observation.maxAltitudeMeters, altitude);
            double pointing = 1.0;
            if (instrument.needsPointing)
                pointing = 1.0 - 0.5 * SimMath.Clamp01(PointingErrorDegrees / Math.Max(0.1, instrument.pointingToleranceDegrees));
            double light = instrument.needsSunlight ? 0.5 + 0.5 * SimMath.Clamp01(craft.SunlitFraction) : 1.0;
            return range * pointing * light;
        }

        // ---------------------------------------------------------------- time step

        public void Tick(double dt)
        {
            Recording = false;
            if (active != null)
            {
                if (!Powered)
                {
                    CancelActive();
                }
                else
                {
                    InstrumentSpec instrument = FindInstrument(active.instrumentId);
                    bool valid = Blocker(active) == null;
                    if (valid)
                    {
                        Recording = true;
                        stalledSeconds = 0.0;
                        progress += dt;
                        qualitySum += SampleQuality(active, instrument) * dt;
                        qualityTime += dt;
                        if (progress >= active.durationSeconds) Complete(instrument);
                    }
                    else
                    {
                        // Each break in the data costs something when it is put back together.
                        if (wasValid) interruptions++;
                        stalledSeconds += dt;
                        if (stalledSeconds >= AbandonAfterSeconds)
                        {
                            ObservationSpec lost = active;
                            CancelActive();
                            if (ObservationAbandoned != null) ObservationAbandoned(lost);
                        }
                    }
                    wasValid = valid;
                }
            }

            if (Transmitting)
            {
                ObservationRecord next = NextToSend();
                if (next == null || !craft.Comms.LinkEstablished)
                {
                    Transmitting = false;
                }
                else
                {
                    ObservationSpec spec = FindObservation(next.observationId);
                    double needed = spec != null ? spec.transmitSeconds : 20.0;
                    // A weak signal carries data more slowly.
                    transmitProgress += dt * (0.4 + 0.6 * craft.Comms.SignalStrength);
                    if (transmitProgress >= needed)
                    {
                        next.transmitted = true;
                        transmitProgress = 0.0;
                        if (RecordTransmitted != null) RecordTransmitted(next);
                        if (NextToSend() == null) Transmitting = false;
                    }
                    TransmitProgress = Transmitting ? SimMath.Clamp01(transmitProgress / Math.Max(1.0, needed)) : 0.0;
                }
            }
            if (!Transmitting) TransmitProgress = 0.0;
        }

        private void Complete(InstrumentSpec instrument)
        {
            double mean = qualityTime > 0.0 ? qualitySum / qualityTime : 0.0;
            double condition = conditions[IndexOf(instrument)];
            double continuity = Math.Max(0.5, 1.0 - 0.08 * interruptions);
            var record = new ObservationRecord
            {
                observationId = active.id,
                title = active.title,
                quality = Math.Round(100.0 * SimMath.Clamp01(mean * (0.4 + 0.6 * condition) * continuity)),
                metSeconds = craft.Mission.MissionElapsedSeconds
            };
            records.Add(record);
            ObservationSpec done = active;
            active = null;
            if (ObservationCompleted != null) ObservationCompleted(record, done);
        }

        private void CancelActive()
        {
            active = null;
            progress = 0.0;
            stalledSeconds = 0.0;
        }

        private ObservationRecord NextToSend()
        {
            for (int i = 0; i < records.Count; i++) if (!records[i].transmitted) return records[i];
            return null;
        }

        private InstrumentSpec FindInstrument(string id)
        {
            for (int i = 0; i < instruments.Length; i++) if (instruments[i].id == id) return instruments[i];
            return null;
        }

        public ObservationSpec FindObservation(string id)
        {
            for (int i = 0; i < plan.Length; i++) if (plan[i].id == id) return plan[i];
            return null;
        }

        private int IndexOf(InstrumentSpec instrument)
        {
            for (int i = 0; i < instruments.Length; i++) if (instruments[i] == instrument) return i;
            return 0;
        }

        /// <summary>One line for the display: what the selected instrument is doing or waiting for.</summary>
        public string StatusText
        {
            get
            {
                if (instruments.Length == 0) return "NOT FITTED";
                if (!PowerEnabled) return "OFF";
                if (!Powered) return "NO POWER";
                if (active != null) return Recording ? "RECORDING" : (Blocker(active) ?? "HOLDING");
                ObservationSpec next = NextFor(Selected);
                if (next == null) return "PLAN COMPLETE";
                return Blocker(next) ?? "READY - PRESS OBSERVE";
            }
        }

        /// <summary>The next observation in the plan for an instrument that has not been made yet.</summary>
        public ObservationSpec NextFor(InstrumentSpec instrument)
        {
            if (instrument == null) return null;
            for (int i = 0; i < plan.Length; i++)
                if (plan[i].instrumentId == instrument.id && !IsCollected(plan[i].id)) return plan[i];
            return null;
        }

        private static string Thousands(double value) { return value.ToString("N0", CultureInfo.InvariantCulture); }

        public static string Duration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "--";
            int total = (int)Math.Max(0.0, seconds);
            if (total >= 3600) return (total / 3600) + " H " + (total / 60 % 60).ToString("00") + " M";
            return (total / 60) + " M " + (total % 60).ToString("00") + " S";
        }

        // ---------------------------------------------------------------- save state

        public ScienceSnapshot Capture()
        {
            return new ScienceSnapshot
            {
                powered = PowerEnabled, selected = SelectedIndex, activeId = active != null ? active.id : null,
                progress = progress, qualitySum = qualitySum, qualityTime = qualityTime, interruptions = interruptions,
                wasValid = wasValid, stalledSeconds = stalledSeconds, conditions = (double[])conditions.Clone(), records = records.ToArray(),
                transmitting = Transmitting, transmitProgress = transmitProgress
            };
        }

        public void Restore(ScienceSnapshot s)
        {
            PowerEnabled = s.powered;
            SelectedIndex = instruments.Length > 0 ? (int)SimMath.Clamp(s.selected, 0, instruments.Length - 1) : 0;
            active = string.IsNullOrEmpty(s.activeId) ? null : FindObservation(s.activeId);
            progress = s.progress;
            qualitySum = s.qualitySum;
            qualityTime = s.qualityTime;
            interruptions = s.interruptions;
            wasValid = s.wasValid;
            stalledSeconds = s.stalledSeconds;
            for (int i = 0; i < conditions.Length && i < s.conditions.Length; i++) conditions[i] = s.conditions[i];
            records.Clear();
            if (s.records != null) records.AddRange(s.records);
            Transmitting = s.transmitting;
            transmitProgress = s.transmitProgress;
        }
    }
}
