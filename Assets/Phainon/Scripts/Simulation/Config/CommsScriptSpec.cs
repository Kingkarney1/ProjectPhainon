using System;

namespace Phainon.Simulation
{
    /// <summary>The simulation event that makes Ground Control transmit a line.</summary>
    public enum CommsTrigger
    {
        /// <summary>Never fires by itself: the line is only reachable as the follow-up to a reply.</summary>
        None = 0,
        LinkAcquired = 1,
        LinkRestored = 2,
        NavAligned = 3,
        AllStationsGo = 4,
        CountdownStarted = 5,
        CountdownHold = 6,
        MainEngineIgnition = 7,
        Liftoff = 8,
        MaxQ = 9,
        BoosterCutoff = 10,
        StageSeparation = 11,
        UpperStageIgnition = 12,
        OrbitGo = 13,
        OrbitAchieved = 14,
        MissionFailed = 15,
        /// <summary>A caution or warning was raised; see <see cref="CommsLine.warning"/>.</summary>
        Warning = 16,
        /// <summary>Sent <see cref="CommsLine.delaySeconds"/> after orbit is achieved.</summary>
        OrbitTimer = 17,
        /// <summary>The crew let a question go unanswered.</summary>
        ReplyTimeout = 18,
        /// <summary>Something broke; see <see cref="CommsLine.fault"/>. Only spoken while that fault is still active.</summary>
        Fault = 19,
        /// <summary>The crew recovered from a fault; see <see cref="CommsLine.fault"/>.</summary>
        FaultCleared = 20,
        UpperStageJettison = 21,
        DepartureBurn = 22,
        EarthEscape = 23,
        /// <summary>A distance from Earth was passed. Lines choose theirs with the @km:&lt;distance&gt; condition.</summary>
        DistanceMark = 24,
        /// <summary>Something happened in a story mission; see <see cref="CommsLine.storyEvent"/>.</summary>
        StoryEvent = 25,
        /// <summary>Never sent unprompted: the answer to a call the crew choose to make. See <see cref="CommsLine.requestLabel"/>.</summary>
        CrewRequest = 26
    }

    [Serializable]
    public class CommsCharacter
    {
        public string id;
        public string name;
        public string role;

        public CommsCharacter() { }
        public CommsCharacter(string id, string name, string role)
        {
            this.id = id;
            this.name = name;
            this.role = role;
        }
    }

    /// <summary>One answer the crew can give to a transmission.</summary>
    [Serializable]
    public class CommsReply
    {
        /// <summary>Short text for the reply prompt.</summary>
        public string label;
        /// <summary>What the crew actually transmit.</summary>
        public string line;
        /// <summary>Story flag set by choosing this reply. Later lines can require or forbid it.</summary>
        public string setsFlag;
        /// <summary>Id of the line (or lines) Ground Control answers with.</summary>
        public string followUp;

        public CommsReply() { }
        public CommsReply(string label, string line, string setsFlag, string followUp)
        {
            this.label = label;
            this.line = line;
            this.setsFlag = setsFlag;
            this.followUp = followUp;
        }
    }

    /// <summary>
    /// One authored transmission from Ground Control. Each line is sent at most once. Text may quote
    /// live telemetry with {alt} {vel} {fuel} {thrust} {apo} {peri} {dist} {delay} and {window}; the values are read from the
    /// simulation at the moment the line is delivered.
    /// </summary>
    [Serializable]
    public class CommsLine
    {
        /// <summary>Lines may share an id to offer variants; flags decide which one is sent.</summary>
        public string id;
        /// <summary>Id of the character speaking.</summary>
        public string speaker;
        public CommsTrigger trigger;
        /// <summary>Which warning, when the trigger is Warning.</summary>
        public WarningId warning;
        /// <summary>Which fault, when the trigger is Fault or FaultCleared.</summary>
        public FaultId fault;
        /// <summary>Pause before Ground Control starts speaking, in seconds.</summary>
        public double delaySeconds;
        /// <summary>
        /// Only sent if this flag is set. Names starting with @ test the spacecraft at the moment of
        /// speaking, so an instruction is dropped when the crew have already carried it out:
        /// @guidance_active, @counting, @lifted_off, @upper_stage, @burning, @in_orbit, @escaped and
        /// @km:&lt;n&gt; (at least n kilometres from Earth). Several conditions may be given, separated by spaces.
        /// </summary>
        public string requiresFlag;
        /// <summary>Not sent if this flag is set.</summary>
        public string forbidsFlag;
        public string text;
        public CommsReply[] replies;
        /// <summary>Which story event, when the trigger is StoryEvent: "wake", "objective:&lt;id&gt;", "observation:&lt;id&gt;", "sent:&lt;id&gt;", "mission_complete".</summary>
        public string storyEvent;
        /// <summary>True if the transmission is kept in the crew's log.</summary>
        public bool logged;
        /// <summary>True if the transmission arrived damaged. The log marks it as not to be relied on.</summary>
        public bool garbled;
        /// <summary>For a CrewRequest line: the short name of the call the crew can make.</summary>
        public string requestLabel;
        /// <summary>For a CrewRequest line: what the crew transmit.</summary>
        public string requestLine;
        /// <summary>Story flag set when the crew hear this line.</summary>
        public string setsFlag;
        /// <summary>
        /// True for a transmission that was sent before the ground could know anything, such as an
        /// automatic beacon: it arrives after its delay alone, with no round trip added.
        /// </summary>
        public bool alreadyOnItsWay;

        public CommsLine() { }
        public CommsLine(string id, string speaker, CommsTrigger trigger, double delaySeconds, string text)
        {
            this.id = id;
            this.speaker = speaker;
            this.trigger = trigger;
            this.delaySeconds = delaySeconds;
            this.text = text;
        }

        public CommsLine OnWarning(WarningId id) { warning = id; return this; }
        public CommsLine OnFault(FaultId id) { fault = id; return this; }
        public CommsLine Requires(string flag) { requiresFlag = flag; return this; }
        public CommsLine Forbids(string flag) { forbidsFlag = flag; return this; }
        public CommsLine WithReplies(params CommsReply[] options) { replies = options; return this; }
        public CommsLine OnStory(string eventId) { storyEvent = eventId; return this; }
        public CommsLine Logged() { logged = true; return this; }
        public CommsLine Garbled() { garbled = true; logged = true; return this; }
        public CommsLine AsRequest(string label, string line) { requestLabel = label; requestLine = line; return this; }
        public CommsLine Sets(string flag) { setsFlag = flag; return this; }
        public CommsLine AlreadyOnItsWay() { alreadyOnItsWay = true; return this; }
    }

    /// <summary>Everything Ground Control can say on a mission: who is on the loop and their lines.</summary>
    [Serializable]
    public class CommsScriptSpec
    {
        /// <summary>How long Ground Control waits for an answer before moving on, in seconds.</summary>
        public double replyWindowSeconds = 40;
        /// <summary>A transmission that cannot get through for this long is abandoned.</summary>
        public double staleAfterSeconds = 90;
        /// <summary>
        /// True when the ground only learns what happened aboard after the signal has crossed the
        /// distance, so its reaction arrives a full round trip after the event.
        /// </summary>
        public bool groundReactsAfterLightTime;
        /// <summary>True lets the crew speed time up with a question still open; it then lapses unanswered.</summary>
        public bool timeAccelerationWhileAwaiting;

        public CommsCharacter[] characters = DefaultCharacters();
        public CommsLine[] lines = DefaultLines();

        public static CommsCharacter[] DefaultCharacters()
        {
            return new[]
            {
                new CommsCharacter("flight", "Mara Okafor", "FLIGHT DIRECTOR"),
                new CommsCharacter("capcom", "Dev Anand", "CAPCOM"),
                new CommsCharacter("guidance", "Ilse Brandt", "GUIDANCE"),
                new CommsCharacter("propulsion", "Tomas Reyes", "PROPULSION"),
                new CommsCharacter("surgeon", "Dr. Hana Sato", "FLIGHT SURGEON")
            };
        }

        public static CommsLine[] DefaultLines()
        {
            return new[]
            {
                // ---- on the pad
                new CommsLine("comm_check", "capcom", CommsTrigger.LinkAcquired, 1.5,
                    "Phainon, this is Meridian Control. Comm check. How do you read?").WithReplies(
                    new CommsReply("Loud and clear", "Meridian, Phainon. Reading you loud and clear.", "crew_talkative", "comm_check_ack"),
                    new CommsReply("Ask for a status", "Meridian, Phainon. Reading you fine. What is the status on your side?", "asked_status", "comm_check_status")),
                new CommsLine("comm_check_ack", "capcom", CommsTrigger.None, 1.0,
                    "Copy, Phainon. Good signal here too. We are with you all the way up."),
                new CommsLine("comm_check_status", "flight", CommsTrigger.None, 1.5,
                    "Phainon, Flight. The range is clear and the team is at their consoles. We are waiting on your board."),
                new CommsLine("comm_check_silent", "capcom", CommsTrigger.ReplyTimeout, 1.0,
                    "Phainon, Meridian. No copy on your last. We will keep the channel open.").Requires("comm_check_no_reply"),

                new CommsLine("nav_aligned", "guidance", CommsTrigger.NavAligned, 1.0,
                    "Phainon, Guidance. We see your platform aligned. NAV MODE to ASCENT and GUIDANCE on when you are ready.")
                    .Forbids("@guidance_active"),
                new CommsLine("go_poll", "flight", CommsTrigger.AllStationsGo, 1.0,
                    "Phainon, Flight. I have polled the room and every station is go. Your board is green. Start the launch sequence when you are ready.")
                    .Forbids("@counting"),
                new CommsLine("count_started", "capcom", CommsTrigger.CountdownStarted, 0.5,
                    "The clock is running. Throttle to full, and ignite in the window at T minus {window}.")
                    .Forbids("@lifted_off"),
                new CommsLine("count_hold", "flight", CommsTrigger.CountdownHold, 0.5,
                    "Phainon, Flight. We see the hold. Clear the no-go item on your board and the count will pick up."),

                // ---- ascent
                new CommsLine("ignition", "propulsion", CommsTrigger.MainEngineIgnition, 0.5,
                    "Propulsion sees ignition. Chamber pressure is coming up."),
                new CommsLine("liftoff", "capcom", CommsTrigger.Liftoff, 1.0,
                    "Liftoff. Phainon has cleared the tower. Good luck."),
                new CommsLine("max_q", "guidance", CommsTrigger.MaxQ, 1.0,
                    "Phainon, you are through max-Q at {alt} kilometres. Throttle up when you are ready."),
                new CommsLine("booster_cutoff", "capcom", CommsTrigger.BoosterCutoff, 0.5,
                    "Booster cutoff confirmed. Lift the guard and separate the stage.")
                    .Forbids("@upper_stage"),
                new CommsLine("separation", "guidance", CommsTrigger.StageSeparation, 0.5,
                    "Clean separation at {alt} kilometres. Light the upper stage.")
                    .Forbids("@burning"),
                new CommsLine("upper_ignition", "propulsion", CommsTrigger.UpperStageIgnition, 1.0,
                    "Upper stage is burning with good chamber pressure. Hold the burn until Guidance calls orbit.")
                    .Forbids("@in_orbit"),
                new CommsLine("orbit_go", "guidance", CommsTrigger.OrbitGo, 0.5,
                    "Phainon, Guidance. Periapsis is above the atmosphere. You are go for cutoff.")
                    .Forbids("@in_orbit"),

                // ---- reactions to trouble
                new CommsLine("warn_max_q", "propulsion", CommsTrigger.Warning, 0.5,
                    "Phainon, Propulsion. Dynamic pressure is over the limit. Ease the throttle back until the warning clears.")
                    .OnWarning(WarningId.DynamicPressureHigh),
                new CommsLine("warn_g", "surgeon", CommsTrigger.Warning, 0.5,
                    "Phainon, Surgeon. The G load is over the limit for the crew. Bring the throttle down, please.")
                    .OnWarning(WarningId.HighG),
                new CommsLine("warn_fuel", "propulsion", CommsTrigger.Warning, 0.5,
                    "Phainon, Propulsion. Upper stage is at {fuel} percent. Keep the burn going: you need all of it for orbit.")
                    .OnWarning(WarningId.FuelLow).Requires("@upper_stage").Forbids("@in_orbit"),
                new CommsLine("warn_apu", "propulsion", CommsTrigger.Warning, 0.5,
                    "Phainon, Propulsion. The APU is offline. You are on batteries and you have lost engine gimbal. Cycle the APU switch: off, then on.")
                    .OnWarning(WarningId.ApuOffline),
                new CommsLine("warn_flameout", "propulsion", CommsTrigger.Warning, 0.5,
                    "Phainon, Propulsion. We saw the engine flame out on feed pressure. Check FUEL PUMP and TANK PRESS, then relight.")
                    .OnWarning(WarningId.EngineFlameout),
                new CommsLine("warn_sink", "guidance", CommsTrigger.Warning, 0.5,
                    "Phainon, Guidance. You are sinking at {alt} kilometres. Pitch up and add thrust now.")
                    .OnWarning(WarningId.TrajectoryLow),
                new CommsLine("warn_cabin", "surgeon", CommsTrigger.Warning, 0.5,
                    "Phainon, Surgeon. Cabin pressure is dropping. Check that CABIN SYS is on.")
                    .OnWarning(WarningId.CabinPressureLow),
                new CommsLine("fault_pump", "propulsion", CommsTrigger.Fault, 3.0,
                    "Phainon, Propulsion. Feed pressure is sagging on a pump fault. Cycle FUEL PUMP, off and straight back on, before the engine starves.")
                    .OnFault(FaultId.FuelPump),
                new CommsLine("fault_battery", "capcom", CommsTrigger.Fault, 3.0,
                    "Phainon, Meridian. Battery A has shorted and it is dragging the bus down. Isolate it: BATT A off. You are fine on B and the APU.")
                    .OnFault(FaultId.BatteryA),
                new CommsLine("fault_nav", "guidance", CommsTrigger.Fault, 3.0,
                    "Phainon, Guidance. Your platform has dropped out and guidance is not steering. Press NAV SYSTEM to realign, and hold attitude by hand until it is back.")
                    .OnFault(FaultId.NavPlatform),
                new CommsLine("fault_engine", "propulsion", CommsTrigger.Fault, 4.0,
                    "Phainon, Propulsion. The engine is only making {thrust} percent. There is nothing to fix from the seat. Keep the throttle up; the burn will run long.")
                    .OnFault(FaultId.WeakUpperEngine),
                new CommsLine("cleared_apu", "propulsion", CommsTrigger.FaultCleared, 1.5,
                    "APU is back on the bus and you have gimbal again. Nicely done.").OnFault(FaultId.ApuTrip),
                new CommsLine("cleared_pump", "propulsion", CommsTrigger.FaultCleared, 1.5,
                    "Feed pressure is back in the green. Good catch, Phainon.").OnFault(FaultId.FuelPump),
                new CommsLine("cleared_battery", "capcom", CommsTrigger.FaultCleared, 1.5,
                    "We see battery A off the bus and the load back to normal. Leave it isolated.").OnFault(FaultId.BatteryA),
                new CommsLine("cleared_nav", "guidance", CommsTrigger.FaultCleared, 1.5,
                    "Platform is realigned and guidance has the vehicle again.").OnFault(FaultId.NavPlatform),
                new CommsLine("cleared_antenna", "capcom", CommsTrigger.FaultCleared, 4.0,
                    "That was your transmitter, not us. Good recovery.").OnFault(FaultId.Antenna),
                new CommsLine("link_restored", "capcom", CommsTrigger.LinkRestored, 1.0,
                    "Phainon, Meridian. We have you back. You dropped off our screens for a while."),
                new CommsLine("mission_failed", "flight", CommsTrigger.MissionFailed, 2.0,
                    "Phainon, Flight. We have lost the mission. Safe the vehicle. We will go through the data together."),

                // ---- in orbit
                new CommsLine("orbit", "flight", CommsTrigger.OrbitAchieved, 2.0,
                    "Phainon, Flight. Welcome to orbit: {peri} by {apo} kilometres. You were loud and clear on the pad and you flew it the same way. Outstanding.")
                    .Requires("crew_talkative"),
                new CommsLine("orbit", "flight", CommsTrigger.OrbitAchieved, 2.0,
                    "Phainon, Flight. Orbit confirmed at {peri} by {apo} kilometres. Well flown.")
                    .Forbids("crew_talkative"),

                new CommsLine("crew_check", "surgeon", CommsTrigger.OrbitTimer, 12.0,
                    "Phainon, Surgeon. That was a hard ride. How is the crew feeling?").WithReplies(
                    new CommsReply("All well", "Surgeon, Phainon. All well up here. The view is something else.", "crew_well", "crew_check_well"),
                    new CommsReply("A little shaken", "Surgeon, Phainon. A little shaken, but we are fine.", "crew_shaken", "crew_check_shaken")),
                new CommsLine("crew_check_well", "surgeon", CommsTrigger.None, 1.5,
                    "Copy. Your vitals agree with you. Enjoy it."),
                new CommsLine("crew_check_shaken", "surgeon", CommsTrigger.None, 1.5,
                    "Understood. Take ten minutes before any more work. Flight has agreed to hold the timeline."),

                new CommsLine("extend", "flight", CommsTrigger.OrbitTimer, 40.0,
                    "Phainon, Flight. Your service module is full and the trajectory team has a way out of Earth orbit for you. Do you want to take her out, or shall we call it a day?")
                    .Forbids("@escaped").WithReplies(
                    new CommsReply("Go for departure", "Flight, Phainon. We would like to take her out.", "extend_mission", "extend_yes"),
                    new CommsReply("Stand down", "Flight, Phainon. We are happy to call it there.", "stand_down", "extend_no")),
                new CommsLine("extend_yes", "guidance", CommsTrigger.None, 1.5,
                    "Copy, go for departure. Drop the upper stage. NAV MODE to ORBIT with GUIDANCE and RCS on, then light the service module and hold the burn until apoapsis reads ESCAPE."),
                new CommsLine("extend_no", "flight", CommsTrigger.None, 1.5,
                    "Agreed, and the Surgeon will be glad to hear it. Good work today, Phainon. The service module stays fuelled if you change your mind.").Requires("crew_shaken"),
                new CommsLine("extend_no", "flight", CommsTrigger.None, 1.5,
                    "Copy, standing down. Good work today, Phainon. The service module stays fuelled if you change your mind.").Forbids("crew_shaken"),

                // ---- leaving Earth
                new CommsLine("jettison", "propulsion", CommsTrigger.UpperStageJettison, 1.5,
                    "Upper stage is away and clear. Service module shows {fuel} percent and a good feed."),
                new CommsLine("departure_burn", "guidance", CommsTrigger.DepartureBurn, 3.0,
                    "Good burn, Phainon. The far side of your orbit is climbing. Hold it until apoapsis reads ESCAPE.")
                    .Forbids("@escaped"),
                new CommsLine("escape", "flight", CommsTrigger.EarthEscape, 2.0,
                    "Phainon, Flight. You are on an escape trajectory at {vel} metres per second. You are no longer in orbit around the Earth. This is what we came for.")
                    .Requires("extend_mission"),
                new CommsLine("escape", "flight", CommsTrigger.EarthEscape, 2.0,
                    "Phainon, Flight. We show you on an escape trajectory at {vel} metres per second. That was not in today's plan, but we are with you. Cut the engine when you are happy.")
                    .Forbids("extend_mission"),
                new CommsLine("dist_10k", "capcom", CommsTrigger.DistanceMark, 1.0,
                    "Phainon, Meridian. {dist} kilometres out. The whole Earth should fit in your window now.")
                    .Requires("@km:10000"),
                new CommsLine("dist_100k", "guidance", CommsTrigger.DistanceMark, 1.0,
                    "Phainon, Guidance. {dist} kilometres. Your signal now takes {delay} seconds to reach us, so expect a pause before we answer.")
                    .Requires("@km:100000"),
                new CommsLine("coast_check", "surgeon", CommsTrigger.DistanceMark, 20.0,
                    "Phainon, Surgeon. It gets quiet out there. How are you holding up?")
                    .Requires("@km:100000").WithReplies(
                    new CommsReply("Enjoying the quiet", "Surgeon, Phainon. Enjoying the quiet. It is a long way to look back.", "coast_content", "coast_check_content"),
                    new CommsReply("Missing home", "Surgeon, Phainon. Honestly, missing home a little.", "coast_homesick", "coast_check_homesick")),
                new CommsLine("coast_check_content", "surgeon", CommsTrigger.None, 1.5,
                    "Good. Sleep when you can. We will keep the calls short."),
                new CommsLine("coast_check_homesick", "surgeon", CommsTrigger.None, 1.5,
                    "That is the healthy answer. Home can still hear you, and we are not going anywhere."),
                new CommsLine("dist_moon", "flight", CommsTrigger.DistanceMark, 1.0,
                    "Phainon, Flight. You have just passed the distance of the Moon's orbit: {dist} kilometres. Nobody has flown this ship farther.")
                    .Requires("@km:384400"),
                new CommsLine("dist_1m", "flight", CommsTrigger.DistanceMark, 1.0,
                    "One million kilometres, Phainon. Earth is a blue point behind you, and everyone on it who knows your name is listening. We are still here.")
                    .Requires("@km:1000000 coast_homesick"),
                new CommsLine("dist_1m", "flight", CommsTrigger.DistanceMark, 1.0,
                    "One million kilometres, Phainon. Earth is a blue point behind you. Our trajectory data ends here: you are flying off the edge of our charts.")
                    .Requires("@km:1000000").Forbids("coast_homesick")
            };
        }
    }
}
