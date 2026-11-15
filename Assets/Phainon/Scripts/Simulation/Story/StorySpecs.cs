using System;

namespace Phainon.Simulation
{
    /// <summary>What has to be true of the spacecraft for a story objective to count as done.</summary>
    public enum ObjectiveCondition
    {
        /// <summary>No red warning is waiting for the crew to acknowledge it.</summary>
        MasterAlarmSilenced = 0,
        ApuOnline = 1,
        CabinNominal = 2,
        NavAligned = 3,
        LinkEstablished = 4,
        /// <summary>The crew have opened the log entry named by the objective's parameter.</summary>
        RecordRead = 5,
        /// <summary>NAV MODE is on the setting given by the objective's value (a <see cref="NavMode"/>).</summary>
        NavModeSelected = 6,
        FeedReady = 7,
        /// <summary>Guidance is holding the attitude the selected NAV MODE asks for.</summary>
        AttitudeHeld = 8,
        /// <summary>The orbit is closed and inside the mission's limits, with the engine off.</summary>
        OrbitStable = 9,
        InstrumentPowered = 10,
        /// <summary>At least the objective's value in observations have been completed.</summary>
        ObservationsCollected = 11,
        /// <summary>Every completed observation has been sent home.</summary>
        DataTransmitted = 12,
        /// <summary>The story flag named by the objective's parameter is set.</summary>
        FlagSet = 13
    }

    /// <summary>One step of a story mission. Missions are lists of these, so a new mission is new data, not new code.</summary>
    [Serializable]
    public class ObjectiveSpec
    {
        public string id;
        /// <summary>Short name for the objective list on the mission display.</summary>
        public string title;
        /// <summary>Heading shown above the guide while this objective is current.</summary>
        public string stage;
        /// <summary>
        /// What the crew are told to do. May quote {tpe} time to periapsis, {lead} when to light the
        /// engine, {burn} burn length, {capture} speed to lose, {dv} speed available, {peri} and {apo}
        /// in kilometres, {science} the payload's status and {count} observations completed.
        /// </summary>
        public string text;
        public ControlId control;
        public ObjectiveCondition condition;
        public string parameter;
        public double value;
        /// <summary>A fault this objective is about. While it is active, its repair procedure is the guide. -1 for none.</summary>
        public int repairFault = -1;
        /// <summary>False keeps the guide on this objective even when some other fault is active.</summary>
        public bool yieldsToFaults = true;
        /// <summary>Story flag set when the objective is completed.</summary>
        public string setsFlag;

        public ObjectiveSpec() { }
        public ObjectiveSpec(string id, string stage, string title, string text, ControlId control, ObjectiveCondition condition)
        {
            this.id = id;
            this.stage = stage;
            this.title = title;
            this.text = text;
            this.control = control;
            this.condition = condition;
        }

        public ObjectiveSpec With(string parameter, double value = 0.0) { this.parameter = parameter; this.value = value; return this; }
        public ObjectiveSpec Repairs(FaultId fault) { repairFault = (int)fault; return this; }
        public ObjectiveSpec Holds() { yieldsToFaults = false; return this; }
        public ObjectiveSpec Sets(string flag) { setsFlag = flag; return this; }
    }

    public enum JournalCategory { MissionEvent = 0, Communication = 1, Recording = 2, Discovery = 3, Anomaly = 4, Record = 5 }

    /// <summary>How far an entry can be trusted. The log never presents a guess as a fact.</summary>
    public enum JournalReliability { Confirmed = 0, Unverified = 1, Corrupted = 2 }

    /// <summary>An authored log entry: a recovered record, a recording, a clue.</summary>
    [Serializable]
    public class JournalEntrySpec
    {
        public string id;
        public JournalCategory category;
        public JournalReliability reliability;
        public string title;
        public string body;
        /// <summary>Story flag that makes the entry appear. Empty means it is there from the start.</summary>
        public string unlockFlag;

        public JournalEntrySpec() { }
        public JournalEntrySpec(string id, JournalCategory category, JournalReliability reliability, string title, string body, string unlockFlag = null)
        {
            this.id = id;
            this.category = category;
            this.reliability = reliability;
            this.title = title;
            this.body = body;
            this.unlockFlag = unlockFlag;
        }
    }

    /// <summary>An entry as it stands in the crew's log.</summary>
    [Serializable]
    public class JournalEntry
    {
        public string id;
        public int category;
        public int reliability;
        public string title;
        public string body;
        public string clock;
        public bool read;
    }

    /// <summary>Something that breaks at a point in the story.</summary>
    [Serializable]
    public class StoryFaultSpec
    {
        public FaultId fault;
        /// <summary>The objective whose completion starts the clock.</summary>
        public string afterObjectiveId;
        public double delaySeconds;

        public StoryFaultSpec() { }
        public StoryFaultSpec(FaultId fault, string afterObjectiveId, double delaySeconds)
        {
            this.fault = fault;
            this.afterObjectiveId = afterObjectiveId;
            this.delaySeconds = delaySeconds;
        }
    }

    /// <summary>Everything that defines one story mission.</summary>
    [Serializable]
    public class StoryMissionSpec
    {
        public string id;
        public string title;
        public string subtitle;
        /// <summary>What the astronaut knows going in. Shown before the mission and in the records.</summary>
        public string briefing;

        public ScenarioSpec scenario = new ScenarioSpec();
        public SystemsSpec systems = new SystemsSpec();
        public MissionSpec mission = new MissionSpec();
        public CommsScriptSpec comms;

        public ObjectiveSpec[] objectives = new ObjectiveSpec[0];
        public InstrumentSpec[] instruments = new InstrumentSpec[0];
        public ObservationSpec[] observations = new ObservationSpec[0];
        public JournalEntrySpec[] journal = new JournalEntrySpec[0];
        public StoryFaultSpec[] faults = new StoryFaultSpec[0];

        // What counts as a stable orbit for this mission.
        public double orbitMinPeriapsisMeters = 5.0e6;
        public double orbitMaxApoapsisMeters = 3.0e9;
        /// <summary>Leaving the body behind on an open path beyond this radius loses the mission, m.</summary>
        public double escapeFailRadiusMeters = 6.0e8;
        /// <summary>Seconds before periapsis at which the crew are called to the capture burn.</summary>
        public double burnWindowWarningSeconds = 300.0;
        /// <summary>Text for the end-of-mission panel.</summary>
        public string completionText;
    }

    [Serializable]
    public class StoryChapterSpec
    {
        public string id;
        public string title;
        public string[] missionIds = new string[0];
    }

    /// <summary>The whole campaign as data: the introduction, the chapters and their missions.</summary>
    [Serializable]
    public class StoryCampaignSpec
    {
        public string title = "OPERATION PHAINON";
        public string subtitle = "THE EXPEDITION INTO THE UNKNOWN";
        /// <summary>The introduction, one screen per entry.</summary>
        public string[] introduction = new string[0];
        public StoryChapterSpec[] chapters = new StoryChapterSpec[0];
        public StoryMissionSpec[] missions = new StoryMissionSpec[0];

        public StoryMissionSpec FindMission(string id)
        {
            for (int i = 0; i < missions.Length; i++) if (missions[i].id == id) return missions[i];
            return null;
        }

        public StoryChapterSpec ChapterOf(string missionId)
        {
            for (int i = 0; i < chapters.Length; i++)
                for (int j = 0; j < chapters[i].missionIds.Length; j++)
                    if (chapters[i].missionIds[j] == missionId) return chapters[i];
            return null;
        }

        /// <summary>The mission that follows one, across chapters, or null when the story stops there for now.</summary>
        public string NextMissionId(string missionId)
        {
            bool found = false;
            for (int i = 0; i < chapters.Length; i++)
                for (int j = 0; j < chapters[i].missionIds.Length; j++)
                {
                    if (found) return chapters[i].missionIds[j];
                    if (chapters[i].missionIds[j] == missionId) found = true;
                }
            return null;
        }

        public string FirstMissionId
        {
            get { return chapters.Length > 0 && chapters[0].missionIds.Length > 0 ? chapters[0].missionIds[0] : null; }
        }
    }
}
