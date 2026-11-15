using System;
using System.Collections.Generic;
using NUnit.Framework;
using Phainon.Simulation;

namespace Phainon.Tests
{
    /// <summary>Story Mode's first mission, flown end to end against the real simulation.</summary>
    public class StoryMissionTests
    {
        private static Spacecraft NewCraft()
        {
            return new Spacecraft(new VehicleSpec(), new SystemsSpec(), new MissionSpec(), null, 0, Mission01Jupiter.Create());
        }

        [Test]
        public void TheAstronaut_Wakes_InADamagedSpacecraft_OnTheWayInToJupiter()
        {
            Spacecraft craft = NewCraft();
            Assert.AreEqual("JUPITER", craft.Flight.Body.name);
            Assert.AreEqual(MissionPhase.OuterSpace, craft.Mission.Phase);
            Assert.IsFalse(craft.Flight.IsBound, "the path in does not stop at Jupiter by itself");
            Assert.Less(craft.Flight.RadialVelocity, 0.0, "inbound");
            Assert.AreEqual(2.0e7, craft.Flight.PeriapsisMeters, 2.0e4, "closest approach of the incoming path");
            Assert.AreEqual(2, craft.Engine.ActiveStage, "only the service module is left");

            Assert.IsTrue(craft.Power.IsBusPowered, "on batteries");
            Assert.IsFalse(craft.Power.ApuOnline);
            Assert.IsFalse(craft.Navigation.Aligned);
            Assert.IsFalse(craft.Comms.LinkEstablished);
            Assert.IsFalse(craft.LifeSupport.Enabled);
            Assert.IsTrue(craft.Warnings.MasterAlarmActive, "the crew wake to an alarm");
            Assert.AreEqual(35.0, craft.Attitude.WindowOffNadirDegrees, 4.0, "Jupiter is off to one side, in the starboard forward window");
            Assert.AreEqual(ControlId.MasterAlarm, craft.Mission.SuggestedControl);
            Assert.AreEqual(0, craft.GroundControl.History.Count, "nobody is talking");
            Assert.IsNotNull(craft.Story.Journal.Find(Mission01Jupiter.RecordInstruction), "the recovered instruction is in the log");
        }

        [Test]
        public void TheRollTheCraftWokeWith_ComesBackToTheDial_OnceTheJetsAreOn()
        {
            Spacecraft craft = NewCraft();
            double woke = craft.Attitude.RollDegrees;
            Assert.Greater(Math.Abs(woke), 30.0, "the craft has drifted in roll");
            for (int i = 0; i < 400; i++) craft.Tick(0.05);
            Assert.AreEqual(woke, craft.Attitude.RollDegrees, 0.01, "nothing turns it without the jets");
            craft.HandleControl(ControlId.Rcs, 1.0);
            for (int i = 0; i < 600; i++) craft.Tick(0.05);
            Assert.AreEqual(0.0, craft.Attitude.RollDegrees, 0.5);
            Assert.AreEqual(0.0, craft.Science.PointingErrorDegrees, 3.0, "which also brings Jupiter in front of the instruments");
        }

        [Test]
        public void Mission01_CanBeFlown_FromWakingToTheDataGoingHome()
        {
            var pilot = new StoryPilot();
            Spacecraft craft = pilot.Craft;
            var completed = new List<string>();
            craft.Story.ObjectiveCompleted += o => completed.Add(o.id);
            bool regulator = false;
            craft.Faults.Raised += id => regulator |= id == FaultId.OxygenRegulator;

            Assert.IsTrue(pilot.FlyMission(), "mission did not complete: " + (craft.Mission.FailureReason ?? craft.Mission.CurrentObjective));

            CollectionAssert.AreEqual(new[]
            {
                "alarm", "power", "cabin", "nav", "radio", "record", "approach", "propulsion", "attitude", "capture",
                "science_power", "observe", "transmit"
            }, completed);
            Assert.IsTrue(craft.Flight.IsBound);
            Assert.GreaterOrEqual(craft.Flight.PeriapsisMeters, craft.Story.Spec.orbitMinPeriapsisMeters);
            Assert.LessOrEqual(craft.Flight.ApoapsisMeters, craft.Story.Spec.orbitMaxApoapsisMeters);
            Assert.Greater(craft.Fuel.GetFraction(2), 0.2, "the capture should leave propellant for what comes next");
            Assert.GreaterOrEqual(craft.Science.CollectedCount, 3);
            Assert.AreEqual(craft.Science.CollectedCount, craft.Science.TransmittedCount);
            foreach (ObservationRecord record in craft.Science.Records)
                Assert.That(record.quality, Is.InRange(30.0, 100.0), record.title);

            Assert.IsTrue(regulator, "the scheduled regulator fault never happened");
            Assert.IsFalse(craft.Faults.IsActive(FaultId.OxygenRegulator), "the guide should have led the crew through the repair");
            Assert.IsTrue(craft.LifeSupport.Nominal);
            Assert.IsTrue(craft.Flags.Contains("mission_complete:" + Mission01Jupiter.Id));
            Assert.IsNotNull(craft.Story.Journal.Find("recording_01"), "the voice memo should have turned up");
        }

        [Test]
        public void WithoutACaptureBurn_JupiterIsLeftBehind_AndTheMissionIsLost()
        {
            var pilot = new StoryPilot { BurnsEngine = false };
            Spacecraft craft = pilot.Craft;
            Assert.IsFalse(pilot.FlyMission(3.0 * 86400.0));
            Assert.IsTrue(craft.Mission.IsFailed);
            StringAssert.Contains("fell behind", craft.Mission.FailureReason);
            Assert.IsFalse(craft.Press(ControlId.EngineIgnition).Accepted, "the engine is safed once the mission is lost");
        }

        [Test]
        public void AnAstronautWhoDoesNothing_LosesTheSpacecraft_WhenTheBatteriesRunFlat()
        {
            var pilot = new StoryPilot { Active = false };
            Spacecraft craft = pilot.Craft;
            bool warned = false;
            craft.Warnings.Raised += entry => warned |= entry.Id == WarningId.BatteryLow;
            Assert.IsTrue(pilot.RunUntil(() => craft.Mission.IsFailed, 4.0 * 3600.0), "nothing happened to an unattended spacecraft");
            Assert.IsTrue(warned, "the batteries ran down without a warning");
            StringAssert.Contains("dark", craft.Mission.FailureReason);
        }

        [Test]
        public void AnInstrument_RecordsNothing_UnlessTheSpacecraftIsReallyPointedAndInRange()
        {
            var pilot = new StoryPilot();
            Spacecraft craft = pilot.Craft;
            Assert.IsTrue(pilot.RunUntil(() => craft.Story.Current != null && craft.Story.Current.id == "approach", 3600.0));
            pilot.Active = false;
            pilot.Set(ControlId.SciencePower, 1);
            pilot.Set(ControlId.InstrumentSelect, 0);

            // Turn the window away from the planet.
            pilot.Set(ControlId.Guidance, 0);
            pilot.Set(ControlId.Rcs, 1);
            pilot.OnTick = () => craft.SetAttitudeInput(1, 0, 0);
            pilot.RunUntil(() => craft.Attitude.WindowOffNadirDegrees > 80.0, 120.0);
            pilot.OnTick = null;
            craft.SetAttitudeInput(0, 0, 0);
            CommandResult away = pilot.Press(ControlId.Observe);
            Assert.IsFalse(away.Accepted, "an observation started with the window pointing at empty space");
            StringAssert.Contains("NOT POINTED", away.Message);
            Assert.AreEqual(0, craft.Science.CollectedCount);

            // Unpowered, it does not work either.
            pilot.Set(ControlId.SciencePower, 0);
            StringAssert.Contains("UNPOWERED", pilot.Press(ControlId.Observe).Message);

            // Pointed back at the planet, it records; turned away half-way through, it stops gathering data.
            pilot.Set(ControlId.SciencePower, 1);
            pilot.OnTick = () => craft.SetAttitudeInput(craft.Attitude.WindowOffNadirDegrees > 1.0 ? -1 : 0, 0, 0);
            pilot.RunUntil(() => craft.Attitude.WindowOffNadirDegrees < 1.0, 120.0);
            pilot.OnTick = null;
            craft.SetAttitudeInput(0, 0, 0);
            Assert.IsTrue(pilot.Press(ControlId.Observe).Accepted, craft.Science.StatusText);
            pilot.RunUntil(() => craft.Science.ActiveProgress > 0.3, 60.0);
            double held = craft.Science.ActiveProgress;
            pilot.OnTick = () => craft.SetAttitudeInput(1, 0, 0);
            pilot.RunUntil(() => !craft.Science.Recording, 60.0);
            pilot.OnTick = null;
            craft.SetAttitudeInput(0, 0, 0);
            double stalled = craft.Science.ActiveProgress;
            pilot.RunUntil(() => false, 20.0);
            Assert.AreEqual(stalled, craft.Science.ActiveProgress, 1e-9, "data kept arriving with the instrument pointed away");
            Assert.GreaterOrEqual(stalled, held);
            Assert.AreEqual(0, craft.Science.CollectedCount);
        }

        [Test]
        public void DataQuality_IsBetterCloseIn_ThanFromFarOut()
        {
            double far = QualityFrom(8.0e8);
            double near = QualityFrom(1.5e8);
            Assert.Greater(near, far + 15.0, "range made no real difference to the data");
        }

        private static double QualityFrom(double altitude)
        {
            StoryMissionSpec mission = Mission01Jupiter.Create();
            mission.scenario.startAltitudeMeters = altitude;
            mission.scenario.pitchDegrees = -90.0 + new VehicleSpec().windowTiltDegrees;    // window dead on the planet
            mission.scenario.rollDegrees = 0.0;
            mission.scenario.initialFaults = new FaultId[0];
            mission.scenario.batteryACharge = 1.0;
            mission.scenario.batteryBCharge = 1.0;
            var craft = new Spacecraft(new VehicleSpec(), new SystemsSpec(), new MissionSpec(), null, 0, mission);
            craft.HandleControl(ControlId.SciencePower, 1);
            craft.HandleControl(ControlId.InstrumentSelect, 0);
            Assert.IsTrue(craft.HandleControl(ControlId.Observe, 1).Accepted, craft.Science.StatusText);
            for (int i = 0; i < 6000 && craft.Science.CollectedCount == 0; i++) craft.Tick(0.02);
            Assert.AreEqual(1, craft.Science.CollectedCount, "the observation never finished");
            return craft.Science.Records[0].quality;
        }

        [Test]
        public void GroundControl_IsMoreThanAnHourAway_AndSilentWithoutALink()
        {
            var pilot = new StoryPilot();
            Spacecraft craft = pilot.Craft;
            double linkedAt = -1.0;
            craft.Comms.LinkChanged += up => { if (up && linkedAt < 0.0) linkedAt = craft.SimClockSeconds; };
            var heard = new List<KeyValuePair<string, double>>();
            craft.GroundControl.MessageReceived += m => heard.Add(new KeyValuePair<string, double>(m.Source.id, craft.SimClockSeconds));

            Assert.IsTrue(pilot.RunUntil(() => linkedAt >= 0.0, 3600.0), "the link never came back");
            Assert.AreEqual(0, heard.Count, "something was heard with no link");
            double delay = craft.GroundControl.LightDelaySeconds;
            Assert.That(delay, Is.InRange(2400.0, 2800.0), "one-way light time to Earth from Jupiter");

            Assert.IsTrue(pilot.RunUntil(() => heard.Exists(h => h.Key == "first_contact"), 4.0 * 3600.0));
            // The automatic beacon was already on its way; a person on the ground needs the round trip.
            Assert.AreEqual("beacon", heard[0].Key);
            Assert.Less(heard[0].Value - linkedAt, 60.0);
            double contactAt = heard.Find(h => h.Key == "first_contact").Value;
            Assert.GreaterOrEqual(contactAt - linkedAt, 2.0 * delay, "the ground answered before it could have known");

            // The answer to the crew's reply takes another round trip.
            Assert.IsTrue(pilot.RunUntil(() => heard.Exists(h => h.Key == "contact_reported"), 4.0 * 3600.0));
            Assert.GreaterOrEqual(heard.Find(h => h.Key == "contact_reported").Value - contactAt, 2.0 * delay);
        }

        [Test]
        public void TheCrew_CanCallTheGround_AndTheAnswerQuotesTheSpacecraftAsItIs()
        {
            var pilot = new StoryPilot();
            Spacecraft craft = pilot.Craft;
            Assert.AreEqual(0, craft.GroundControl.AvailableRequests.Count == 0 ? 0 : 0);
            Assert.IsFalse(craft.GroundControl.Request(0).Accepted, "a call went out with no link");
            Assert.IsTrue(pilot.RunUntil(() => craft.Story.Current != null && craft.Story.Current.id == "approach", 3600.0));

            int index = -1;
            for (int i = 0; i < craft.GroundControl.AvailableRequests.Count; i++)
                if (craft.GroundControl.AvailableRequests[i].id == "ask_records") index = i;
            Assert.GreaterOrEqual(index, 0, "asking about the records should be on offer once the record has been read");
            Assert.IsTrue(craft.GroundControl.Request(index).Accepted);
            string answer = null;
            craft.GroundControl.MessageReceived += m => { if (m.Source.id == "ask_records") answer = m.Text; };
            pilot.Active = false;
            double asked = craft.SimClockSeconds;
            Assert.IsTrue(pilot.RunUntil(() => answer != null, 3.0 * 3600.0));
            Assert.GreaterOrEqual(craft.SimClockSeconds - asked, 2.0 * craft.GroundControl.LightDelaySeconds);
            StringAssert.Contains("sealed", answer);
            Assert.IsNotNull(craft.Story.Journal.Find("radio:ask_records"), "the answer belongs in the log");
        }

        [Test]
        public void ASavedFlight_CarriesOn_ExactlyAsItWas()
        {
            var pilot = new StoryPilot();
            Spacecraft craft = pilot.Craft;
            // Save in orbit, with the story part-way through and things said on the radio.
            Assert.IsTrue(pilot.RunUntil(() => craft.Story.Current != null && craft.Story.Current.id == "observe" && craft.SaveInhibit == null, 30.0 * 86400.0),
                craft.Mission.FailureReason ?? craft.Mission.CurrentObjective);
            SpacecraftSnapshot saved = craft.Capture();

            Spacecraft loaded = NewCraft();
            loaded.Restore(saved);

            Assert.AreEqual(craft.Story.ObjectiveIndex, loaded.Story.ObjectiveIndex);
            Assert.AreEqual(craft.Mission.CurrentObjective, loaded.Mission.CurrentObjective);
            Assert.AreEqual(craft.Story.Journal.Entries.Count, loaded.Story.Journal.Entries.Count);
            Assert.AreEqual(craft.GroundControl.History.Count, loaded.GroundControl.History.Count);
            Assert.AreEqual(craft.Warnings.Active.Count, loaded.Warnings.Active.Count);
            CollectionAssert.AreEquivalent(craft.Flags, loaded.Flags);
            foreach (ControlId id in Enum.GetValues(typeof(ControlId)))
            {
                double a = craft.GetControlValue(id), b = loaded.GetControlValue(id);
                if (!double.IsNaN(a)) Assert.AreEqual(a, b, 1e-12, "panel position of " + id);
            }

            // Nothing is announced a second time on loading.
            int raised = 0, heard = 0;
            loaded.Warnings.Raised += e => raised++;
            loaded.GroundControl.MessageReceived += m => heard++;
            int raisedOriginal = 0, heardOriginal = 0;
            craft.Warnings.Raised += e => raisedOriginal++;
            craft.GroundControl.MessageReceived += m => heardOriginal++;

            for (int i = 0; i < 20000; i++) { craft.Tick(1.0); loaded.Tick(1.0); }

            Assert.AreEqual(craft.Flight.AltitudeMeters, loaded.Flight.AltitudeMeters, 1e-3);
            Assert.AreEqual(craft.Flight.TangentialVelocity, loaded.Flight.TangentialVelocity, 1e-6);
            Assert.AreEqual(craft.LifeSupport.OxygenFraction, loaded.LifeSupport.OxygenFraction, 1e-12);
            Assert.AreEqual(craft.Power.BatteryACharge, loaded.Power.BatteryACharge, 1e-12);
            Assert.AreEqual(craft.Fuel.GetPropellantKg(2), loaded.Fuel.GetPropellantKg(2), 1e-9);
            Assert.AreEqual(craft.Mission.MissionElapsedSeconds, loaded.Mission.MissionElapsedSeconds, 1e-6);
            Assert.AreEqual(raisedOriginal, raised, "warnings after loading");
            Assert.AreEqual(heardOriginal, heard, "radio after loading");
            Assert.AreEqual(craft.GroundControl.History.Count, loaded.GroundControl.History.Count);
        }

        [Test]
        public void SavingIsRefused_WhileTheEngineIsBurning()
        {
            var pilot = new StoryPilot();
            Spacecraft craft = pilot.Craft;
            Assert.IsNull(craft.SaveInhibit, "coasting in");
            Assert.IsTrue(pilot.RunUntil(() => craft.Engine.IsBurning, 6.0 * 3600.0), craft.Mission.CurrentObjective);
            Assert.IsNotNull(craft.SaveInhibit);
        }
    }

    internal static class CraftTestExtensions
    {
        public static CommandResult Press(this Spacecraft craft, ControlId id) { return craft.HandleControl(id, 1.0); }
    }
}
