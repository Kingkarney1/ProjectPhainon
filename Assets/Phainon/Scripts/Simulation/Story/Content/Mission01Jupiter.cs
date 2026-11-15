namespace Phainon.Simulation
{
    /// <summary>
    /// Mission 01: waking on the way in to Jupiter, getting into orbit and bringing back the first
    /// observations. Everything the mission says and every number it uses is in this file.
    /// It does not say why the astronaut is here, because the astronaut does not know.
    /// </summary>
    public static class Mission01Jupiter
    {
        public const string Id = "m01_jupiter";
        public const string RecordInstruction = "record_instruction";

        public static StoryMissionSpec Create()
        {
            return new StoryMissionSpec
            {
                id = Id,
                title = "MISSION 01   JUPITER OBSERVATION",
                subtitle = "OPERATION PHAINON",
                briefing =
                    "The mission computer has restarted. One instruction survives:\n\n" +
                    "Obtain close-range observation data of Jupiter from a stable orbit. Transmit when able.\n\n" +
                    "The rest of the flight plan, and most of the flight history, could not be read.",
                completionText =
                    "The Jupiter data is on its way home. The flight plan still shows one instruction of nine. " +
                    "The spacecraft is yours: stay in orbit, keep observing, or save and rest from the pause menu.",
                scenario = Scenario(),
                systems = Systems(),
                mission = MissionText(),
                comms = Radio(),
                objectives = Objectives(),
                instruments = Instruments(),
                observations = Observations(),
                journal = Journal(),
                faults = new[] { new StoryFaultSpec(FaultId.OxygenRegulator, "capture", 420.0) },
                orbitMinPeriapsisMeters = 5.0e6,
                orbitMaxApoapsisMeters = 3.0e9,
                escapeFailRadiusMeters = 7.0e8,
                burnWindowWarningSeconds = 300.0
            };
        }

        private static ScenarioSpec Scenario()
        {
            return new ScenarioSpec
            {
                body = BodySpec.Jupiter(),
                excessSpeed = 5600.0,
                periapsisAltitudeMeters = 2.0e7,
                startAltitudeMeters = 2.4e8,
                activeStage = 2,
                propellantFractions = new[] { 0.0, 0.0, 0.72 },
                // Jupiter sits in the middle of the forward window as the seat sees it: the crew's eye
                // line through the window is 13 degrees above the window's own axis.
                // Drifting, rolled over: this attitude puts Jupiter in the starboard forward window, clear of the centre post.
                pitchDegrees = -62.0,
                rollDegrees = 76.0,
                batteryA = true, batteryB = true, mainBus = true,
                apu = true, fuelPump = false, tankPress = false,
                commAntenna = true, comms = true,
                navSystem = false, navMode = (int)NavMode.Standby, guidance = true, rcs = false,
                cabinSystems = false, cabinLights = true, externalCamera = true,
                batteryACharge = 0.38, batteryBCharge = 0.81,
                oxygenFraction = 0.74, cabinPressureKpa = 86.0, cabinTemperatureC = 11.0,
                initialFaults = new[] { FaultId.ApuTrip, FaultId.NavPlatform, FaultId.Antenna },
                sunAngleDegrees = -38.0, sunElevationDegrees = 6.0,
                earthDistanceMeters = 7.78e11,
                missionElapsedSeconds = 0.0
            };
        }

        /// <summary>The same spacecraft, far from any ground supply: a cold, slowly leaking cabin and oxygen for most of a year.</summary>
        private static SystemsSpec Systems()
        {
            return new SystemsSpec
            {
                oxygenUsePerSecond = 0.000000039,
                regulatorLeakPerSecond = 0.00012,
                unpoweredCabinPressureKpa = 30.0,
                cabinLeakTimeConstant = 30000.0,
                unconditionedCabinTempC = -35.0,
                cabinWarmTimeConstant = 20000.0,
                cabinAirEnduranceSeconds = 14400.0,
                faultMinAltitudeMeters = 0.0
            };
        }

        private static MissionSpec MissionText()
        {
            return new MissionSpec
            {
                missionName = "OPERATION PHAINON",
                missionSubtitle = "MISSION 01   JUPITER",
                distanceMarksKm = new double[0],
                phases = new[]
                {
                    new PhaseInfo(MissionPhase.OuterSpace, "JUPITER", "Obtain close-range observation data of Jupiter from a stable orbit."),
                    new PhaseInfo(MissionPhase.Failed, "MISSION LOST", "The expedition cannot continue.")
                }
            };
        }

        private static ObjectiveSpec[] Objectives()
        {
            return new[]
            {
                new ObjectiveSpec("alarm", "WAKING", "Silence the alarm",
                    "Alarms. Press MASTER ALARM to silence it, then find out what is wrong.",
                    ControlId.MasterAlarm, ObjectiveCondition.MasterAlarmSilenced).Holds(),
                new ObjectiveSpec("power", "WAKING", "Restore auxiliary power",
                    "The spacecraft is on batteries. Restore the APU.",
                    ControlId.Apu, ObjectiveCondition.ApuOnline).Repairs(FaultId.ApuTrip).Holds(),
                new ObjectiveSpec("cabin", "WAKING", "Restart the cabin loop",
                    "The cabin is cold and losing pressure. Press CABIN SYS to restart the environment loop.",
                    ControlId.CabinSystems, ObjectiveCondition.CabinNominal).Holds(),
                new ObjectiveSpec("nav", "WAKING", "Realign the nav platform",
                    "The flight computer does not know which way it is pointing. Press NAV SYSTEM to realign the platform.",
                    ControlId.NavSystem, ObjectiveCondition.NavAligned).Repairs(FaultId.NavPlatform).Holds(),
                new ObjectiveSpec("radio", "WAKING", "Recover the radio link",
                    "There is nothing on the radio. Recover the link.",
                    ControlId.CommAntenna, ObjectiveCondition.LinkEstablished).Repairs(FaultId.Antenna).Holds(),
                new ObjectiveSpec("record", "TAKING STOCK", "Read the mission record",
                    "The mission computer has recovered part of the flight plan. Press J to open the log and read it.",
                    ControlId.None, ObjectiveCondition.RecordRead).With(RecordInstruction),

                new ObjectiveSpec("approach", "JUPITER APPROACH", "Assess the approach",
                    "Jupiter is {tpe} away at closest approach, and this path does not stop there. Turn NAV MODE to RETRO: the left display shows the approach and what it takes to stay.",
                    ControlId.NavMode, ObjectiveCondition.NavModeSelected).With(null, (double)NavMode.Retro),
                new ObjectiveSpec("propulsion", "JUPITER APPROACH", "Pressurise the engine feed",
                    "Set FUEL PUMP and TANK PRESS ON and wait for feed pressure.",
                    ControlId.FuelPump, ObjectiveCondition.FeedReady),
                new ObjectiveSpec("attitude", "JUPITER APPROACH", "Turn against the flight path",
                    "Set GUIDANCE and RCS ON. The computer will turn the engine to face the way you are going.",
                    ControlId.Guidance, ObjectiveCondition.AttitudeHeld),
                new ObjectiveSpec("capture", "JUPITER APPROACH", "Capture into orbit",
                    "Coast to periapsis: {tpe} to go. Press . to speed time up. Light the engine when TIME TO PE reads {lead}; the burn is {burn} for {capture} m/s of the {dv} you have.",
                    ControlId.None, ObjectiveCondition.OrbitStable).Sets("captured"),

                new ObjectiveSpec("science_power", "JUPITER ORBIT", "Power the science payload",
                    "In orbit. Set SCIENCE PWR ON to bring the instruments up.",
                    ControlId.SciencePower, ObjectiveCondition.InstrumentPowered),
                new ObjectiveSpec("observe", "JUPITER ORBIT", "Collect three observations",
                    "Collect {need} observations ({count} so far). Choose with INSTRUMENT, aim the window at Jupiter (GUIDANCE off, RCS on, W and S), then press OBSERVE. Payload: {science}.",
                    ControlId.Observe, ObjectiveCondition.ObservationsCollected).With(null, 3.0),
                new ObjectiveSpec("transmit", "JUPITER ORBIT", "Send the data home",
                    "Press TRANSMIT to send the observations to Earth. The radio link has to hold until the last one has gone.",
                    ControlId.TransmitData, ObjectiveCondition.DataTransmitted)
            };
        }

        private static InstrumentSpec[] Instruments()
        {
            return new[]
            {
                new InstrumentSpec("camera", "IMAGING CAMERA", "CAM", 160.0, true, true, 1.0),
                new InstrumentSpec("spectrometer", "SPECTROMETER", "SPEC", 220.0, true, true, 0.8),
                new InstrumentSpec("radiation", "RADIATION SENSOR", "RAD", 60.0, false, false, 0.95)
            };
        }

        private static ObservationSpec[] Observations()
        {
            return new[]
            {
                new ObservationSpec
                {
                    id = "bands", title = "Atmospheric band structure", instrumentId = "camera",
                    durationSeconds = 45.0, maxAltitudeMeters = 9.0e8, bestAltitudeMeters = 2.5e8, transmitSeconds = 30.0,
                    finding = "An imaging sequence of the zones and belts from close range. Eastward and westward jets alternate with latitude, and the shear between them is drawn out in long streaks of cloud.",
                    poorFinding = "The frames are soft. The belts are there, but little can be measured from them."
                },
                new ObservationSpec
                {
                    id = "redspot", title = "Great Red Spot", instrumentId = "camera", needsFeature = true,
                    durationSeconds = 50.0, maxAltitudeMeters = 6.0e8, bestAltitudeMeters = 2.0e8, transmitSeconds = 30.0,
                    finding = "The storm fills the frame. Cloud at its rim turns counter-clockwise, and the turbulent wake to its west is laid out in detail.",
                    poorFinding = "The storm is in frame but smeared. Its rim cannot be traced."
                },
                new ObservationSpec
                {
                    id = "spectrum", title = "Upper atmosphere spectrum", instrumentId = "spectrometer",
                    durationSeconds = 60.0, maxAltitudeMeters = 4.5e8, bestAltitudeMeters = 1.5e8, transmitSeconds = 20.0,
                    anomaly = true, setsFlag = "anomaly_spectrum",
                    finding = "Hydrogen, helium, methane and ammonia, where the models put them. One narrow absorption line near 1.74 micrometres matches nothing in the instrument's catalogue. The self-test passes. The line is in every frame.",
                    poorFinding = "The spectrum is noisy. The main gases can be picked out and nothing else can be said."
                },
                new ObservationSpec
                {
                    id = "radiation", title = "Radiation environment", instrumentId = "radiation",
                    durationSeconds = 60.0, maxAltitudeMeters = 5.0e8, bestAltitudeMeters = 1.2e8, transmitSeconds = 15.0,
                    finding = "Charged-particle flux rises steeply inward, as it should this deep in the magnetosphere. The dose rate inside the hull is within the limit for the planned stay.",
                    poorFinding = "The counts are too few to draw the profile. The flux rises inward; how steeply is not clear."
                }
            };
        }

        private static JournalEntrySpec[] Journal()
        {
            return new[]
            {
                new JournalEntrySpec(RecordInstruction, JournalCategory.Record, JournalReliability.Confirmed,
                    "Flight plan: restored from backup",
                    "OPERATION PHAINON\nFLIGHT PLAN, RESTORED FROM THE BACKUP STORE\n\n" +
                    "Instruction 1, current:\nObtain close-range observation data of Jupiter from a stable orbit. Transmit when able.\n\n" +
                    "Instructions 2 to 9: NOT RECOVERED.\n\n" +
                    "Flight history before the restart: 2 entries readable. The number of entries lost is not known."),
                new JournalEntrySpec("record_log_fragment", JournalCategory.Record, JournalReliability.Corrupted,
                    "Flight log: fragment",
                    "...cruise nominal. I asked again what the second half of the plan is. They said it would be uplinked when [unreadable].\n\n" +
                    "...power transient on bus A. Probably nothing. Going to sleep.\n\n" +
                    "[The entries end here. The date fields did not survive.]"),
                new JournalEntrySpec("recording_01", JournalCategory.Recording, JournalReliability.Unverified,
                    "Voice memo: no date stamp",
                    "A recording in the personal store. The voice is your own.\n\n" +
                    "\"If you are hearing this, the restart worked, and you do not remember making this. They said that might happen.\n\n" +
                    "Do the Jupiter work first. I cannot tell you the rest, because I do not know it either. I only know that I agreed to it.\"\n\n" +
                    "There is no date on the file and nothing to say when, or whether, it was recorded on this flight.",
                    "done:cabin"),
                new JournalEntrySpec("record_instruction_2", JournalCategory.Record, JournalReliability.Confirmed,
                    "Flight plan: instruction 2",
                    "INSTRUCTION 2, RELEASED BY UPLINK\n\nDo not begin the return. Hold at Jupiter. A second target will be given.\n\n" +
                    "Instructions 3 to 9: SEALED.",
                    "instruction_2_released")
            };
        }

        private static CommsScriptSpec Radio()
        {
            return new CommsScriptSpec
            {
                // Forty-three minutes each way: a question asked now is answered an hour and a half from now.
                replyWindowSeconds = 900.0,
                staleAfterSeconds = 21600.0,
                groundReactsAfterLightTime = true,
                timeAccelerationWhileAwaiting = true,
                characters = new[]
                {
                    new CommsCharacter("flight", "Mara Okafor", "FLIGHT DIRECTOR"),
                    new CommsCharacter("capcom", "Dev Anand", "CAPCOM"),
                    new CommsCharacter("navigation", "Ilse Brandt", "NAVIGATION"),
                    new CommsCharacter("propulsion", "Tomas Reyes", "PROPULSION"),
                    new CommsCharacter("science", "Dr. Amara Nwosu", "SCIENCE"),
                    new CommsCharacter("specialist", "Jonah Vale", "MISSION SPECIALIST"),
                    new CommsCharacter("net", "Deep Space Net", "AUTOMATED")
                },
                lines = new[]
                {
                    // ---- first contact
                    new CommsLine("beacon", "net", CommsTrigger.LinkAcquired, 20.0,
                        "MERIDIAN DEEP SPACE NET. CARRIER SEARCH ACTIVE. ANY STATION RECEIVING THIS TRANSMISSION: KEY YOUR TRANSMITTER AND HOLD. ONE-WAY LIGHT TIME {delaymin} MINUTES.")
                        .AlreadyOnItsWay().Logged(),
                    new CommsLine("first_contact", "capcom", CommsTrigger.LinkAcquired, 30.0,
                        "Phainon, Meridian. We have your carrier. We lost you for a long time. Tell us how you are, and remember anything you say takes {delaymin} minutes to get here.")
                        .Logged().WithReplies(
                        new CommsReply("Report status", "Meridian, Phainon. Awake. The ship restarted on its own and I am putting it back together. Jupiter is ahead.", "crew_reported", "contact_reported"),
                        new CommsReply("Ask what happened", "Meridian, Phainon. I woke up to alarms with most of my flight log gone. What happened to me?", "asked_what_happened", "contact_asked")),
                    new CommsLine("contact_reported", "flight", CommsTrigger.None, 20.0,
                        "Phainon, Flight. It is good to hear your voice. The plan has not changed: Jupiter first. We will talk about the rest once you are in orbit.")
                        .Logged(),
                    new CommsLine("contact_asked", "flight", CommsTrigger.None, 20.0,
                        "Phainon, Flight. We see the same gap you do. Your telemetry stopped and then came back by itself. We did not command the restart. Say again, we did not -")
                        .Garbled(),
                    new CommsLine("contact_silent", "capcom", CommsTrigger.ReplyTimeout, 5.0,
                        "Phainon, Meridian. Nothing heard. We will keep listening for as long as it takes.")
                        .Requires("first_contact_no_reply"),

                    // ---- the approach and the capture
                    new CommsLine("capture", "navigation", CommsTrigger.StoryEvent, 15.0,
                        "Phainon, Navigation. We show you captured, {peri} by {apo} kilometres. That is an orbit. Well flown.")
                        .OnStory("objective:capture").Requires("@bound").Logged(),

                    // ---- trouble
                    new CommsLine("regulator_cleared", "propulsion", CommsTrigger.FaultCleared, 10.0,
                        "Phainon, Reyes. We watched your oxygen regulator stick and we watched you fix it before our warning could reach you. You are {delaymin} minutes ahead of anything we can tell you. Keep that in mind.")
                        .OnFault(FaultId.OxygenRegulator).Logged(),

                    // ---- the science
                    new CommsLine("science_bands", "science", CommsTrigger.StoryEvent, 20.0,
                        "Phainon, Science. Your band imagery is down. Nobody in this room has seen the jets from that close. Thank you.")
                        .OnStory("sent:bands").Logged(),
                    new CommsLine("science_spectrum", "science", CommsTrigger.StoryEvent, 20.0,
                        "Phainon, Science. About your spectrum. There is a line in it that should not be there. Run the instrument again when you can, and hold the result in storage. Do not send that one twice.")
                        .OnStory("sent:spectrum").Requires("anomaly_spectrum").Logged(),
                    new CommsLine("complete", "flight", CommsTrigger.StoryEvent, 30.0,
                        "Phainon, Flight. We have your data. I am cleared to release the next instruction. It is in your log now. I am sorry it is not the one you were hoping for.")
                        .OnStory("mission_complete").Sets("instruction_2_released").Logged(),

                    // ---- calls the crew can make
                    new CommsLine("ask_guidance", "navigation", CommsTrigger.CrewRequest, 15.0,
                        "Phainon, Navigation. For the capture: NAV MODE to RETRO, GUIDANCE and RCS on, full throttle, and light the engine half a burn before periapsis. You have {dv} metres per second. Cut off when ORBIT GO lights and keep the rest.")
                        .AsRequest("Ask for capture guidance", "Meridian, Phainon. Request guidance for getting into orbit.")
                        .Forbids("@orbit_stable").Logged(),
                    new CommsLine("ask_records", "specialist", CommsTrigger.CrewRequest, 15.0,
                        "Phainon, Vale. Instructions two to nine are sealed until the Jupiter data is on the ground. That was the arrangement you signed. It was written that way for a reason, and I am not allowed to give the reason on this channel.")
                        .AsRequest("Ask about the missing records", "Meridian, Phainon. My flight plan shows one instruction out of nine and my log has holes in it. What am I missing?")
                        .Requires("read:" + RecordInstruction).Logged(),
                    new CommsLine("ask_objective", "flight", CommsTrigger.CrewRequest, 15.0,
                        "Phainon, Flight. One objective for now: close-range data from a stable orbit, sent home. The bands, the Red Spot, a spectrum, the radiation environment, as many as you can get. Everything after that waits on it.")
                        .AsRequest("Ask to confirm the objective", "Meridian, Phainon. Confirm my objective at Jupiter.")
                        .Logged()
                }
            };
        }
    }
}
