using System;
using System.Collections.Generic;
using NUnit.Framework;
using Phainon.Simulation;

namespace Phainon.Tests
{
    /// <summary>Beyond orbit: the service module, prograde hold, the escape burn and the long coast.</summary>
    public class DepartureTests
    {
        private static MissionPilot InOrbit()
        {
            var pilot = new MissionPilot();
            Assert.IsTrue(pilot.FlyFullMission(), "mission did not complete: " + pilot.Craft.Mission.FailureReason);
            pilot.Advance(5.0);
            return pilot;
        }

        /// <summary>Follows the departure procedure from the objective guide until the engine is burning.</summary>
        private static void StartDeparture(MissionPilot pilot)
        {
            Spacecraft craft = pilot.Craft;
            Assert.IsTrue(pilot.Press(ControlId.StageSeparation).Accepted, "upper stage jettison in orbit");
            pilot.Set(ControlId.NavMode, (double)NavMode.Orbit);
            pilot.Set(ControlId.Rcs, 1);
            pilot.Set(ControlId.Throttle, 1.0);
            pilot.Advance(20.0);    // RCS brings the nose onto the flight path
            Assert.AreEqual(craft.Flight.FlightPathAngleDegrees, craft.Attitude.PitchDegrees, 0.5, "prograde hold while coasting");
            Assert.AreEqual(ControlId.EngineIgnition, craft.Mission.SuggestedControl);
            Assert.IsTrue(pilot.Press(ControlId.EngineIgnition).Accepted, "service module ignition");
        }

        private static double SpecificEnergy(FlightModel flight)
        {
            return flight.Speed * flight.Speed * 0.5 - FlightModel.EarthMu / flight.Radius;
        }

        [Test]
        public void TheServiceModule_AddsNoMass_ToTheAscent()
        {
            var craft = new Spacecraft(new VehicleSpec(), new SystemsSpec(), new MissionSpec());
            Assert.AreEqual(3, craft.VehicleSpec.stages.Length);
            Assert.AreEqual(797000.0, craft.Flight.ComputeMass(0, craft.Fuel), 1e-6, "liftoff mass must match the two-stage vehicle");
            Assert.AreEqual(26000.0, craft.Flight.ComputeMass(2, craft.Fuel), 1e-6, "what the upper stage carries to orbit");
        }

        [Test]
        public void TheUpperStage_CannotBeDropped_BeforeOrbit()
        {
            var pilot = new MissionPilot();
            Assert.IsTrue(pilot.PowerUp() && pilot.Launch());
            Spacecraft craft = pilot.Craft;
            CommandResult? attempt = null;
            pilot.OnTick = () =>
            {
                if (attempt != null || craft.Mission.Phase != MissionPhase.OrbitalInsertion) return;
                // Mid-insertion, with the engine cut and the thrust gone, is the tempting moment.
                if (craft.Engine.IsBurning) { if (craft.Fuel.GetFraction(1) < 0.6) pilot.Press(ControlId.EngineCutoff); }
                else if (craft.Engine.ThrustNewtons <= 0.0) attempt = pilot.Press(ControlId.StageSeparation);
            };
            pilot.FlyToOrbit(400.0);
            Assert.IsNotNull(attempt, "the insertion burn was never interrupted");
            Assert.IsFalse(attempt.Value.Accepted);
            StringAssert.Contains("NOT YET IN ORBIT", attempt.Value.Message);
            Assert.AreEqual(1, craft.Engine.ActiveStage);
        }

        [Test]
        public void TheDepartureBurn_HoldsPrograde_AndReachesEscape_WithPropellantToSpare()
        {
            MissionPilot pilot = InOrbit();
            Spacecraft craft = pilot.Craft;
            var called = new List<MissionEventId>();
            craft.Mission.EventReached += e => called.Add(e.Id);

            StartDeparture(pilot);
            double worstError = 0.0;
            Assert.IsTrue(pilot.RunUntil(() => craft.Mission.HasEscaped, 400.0, () =>
            {
                if (craft.Engine.State == EngineState.Running)
                    worstError = Math.Max(worstError, Math.Abs(craft.Attitude.PitchDegrees - craft.Flight.FlightPathAngleDegrees));
            }), "the service module should reach escape velocity");

            Assert.Less(worstError, 2.0, "the nose must stay on the flight path through the burn");
            Assert.Greater(craft.Fuel.GetFraction(2), 0.05, "escape should not take the whole tank");
            Assert.LessOrEqual(craft.Flight.GLoad, craft.SystemsSpec.maxGLoad);
            CollectionAssert.AreEqual(
                new[] { MissionEventId.UpperStageJettison, MissionEventId.DepartureBurn, MissionEventId.EarthEscape }, called);
            Assert.AreEqual(MissionPhase.OuterSpace, craft.Mission.Phase);
            Assert.IsNotNull(craft.TimeWarpInhibit, "no time acceleration under thrust");

            Assert.IsTrue(pilot.Press(ControlId.EngineCutoff).Accepted);
            pilot.Advance(3.0);
            Assert.IsNull(craft.TimeWarpInhibit);
        }

        [Test]
        public void TheCoast_ConservesEnergy_PassesEveryMark_AndTheRadioDelayGrows()
        {
            MissionPilot pilot = InOrbit();
            Spacecraft craft = pilot.Craft;
            StartDeparture(pilot);
            // Burn the tank dry for the fastest coast.
            Assert.IsTrue(pilot.RunUntil(() => !craft.Engine.IsBurning, 600.0));
            pilot.Advance(3.0);
            Assert.IsTrue(craft.Mission.HasEscaped);

            var marks = new List<string>();
            craft.Mission.EventReached += e => { if (e.Id == MissionEventId.DistanceMark) marks.Add(e.Label); };
            double energy = SpecificEnergy(craft.Flight);
            double moonDelay = -1.0;

            // One-second slices, exactly as time acceleration ticks the simulation.
            double limit = 40.0 * 86400.0;
            for (double t = 0.0; t < limit && marks.Count < 4; t += 1.0)
            {
                // Answer the surgeon, as a crew would, so the radio is not left waiting.
                if (craft.GroundControl.AwaitingReply != null) craft.GroundControl.Reply(1);
                craft.Tick(1.0);
                if (marks.Count == 3 && moonDelay < 0.0) moonDelay = craft.GroundControl.LightDelaySeconds;
            }

            for (int i = 0; i < 30; i++) craft.Tick(1.0);   // time for the last call to cross the distance

            CollectionAssert.AreEqual(new[]
            {
                "10,000 KM FROM EARTH", "100,000 KM FROM EARTH", "384,400 KM FROM EARTH", "1,000,000 KM FROM EARTH"
            }, marks);
            Assert.AreEqual(energy, SpecificEnergy(craft.Flight), Math.Abs(energy) * 0.002 + 1000.0, "orbital energy drifted on the coast");
            Assert.AreEqual(1.282, moonDelay, 0.01, "one-way light time at the Moon's distance");
            Assert.IsTrue(craft.Comms.LinkEstablished, "the link should hold all the way out");
            Assert.That(craft.Comms.SignalStrength, Is.InRange(0.2, 0.6));
            Assert.IsFalse(craft.Warnings.IsActive(WarningId.OxygenLow), "oxygen must outlast the coast");
            Assert.IsTrue(craft.GroundControl.HasFlag("coast_homesick"));

            var heard = new List<string>();
            foreach (CommsMessage message in craft.GroundControl.History)
                if (message.Source != null) heard.Add(message.Source.id);
            foreach (string id in new[] { "jettison", "escape", "dist_10k", "dist_100k", "coast_check", "dist_moon", "dist_1m" })
                CollectionAssert.Contains(heard, id);
            StringAssert.Contains("everyone on it", craft.GroundControl.History[craft.GroundControl.History.Count - 1].Text,
                "the last call should reflect the crew's earlier answer");
        }

        [Test]
        public void TimeAcceleration_IsRefused_UntilCoastingInOrbit()
        {
            var pilot = new MissionPilot();
            Assert.IsNotNull(pilot.Craft.TimeWarpInhibit, "on the pad");
            Assert.IsTrue(pilot.PowerUp() && pilot.Launch());
            pilot.FlyToOrbit(60.0);
            Assert.IsNotNull(pilot.Craft.TimeWarpInhibit, "during the ascent");
            Assert.IsTrue(pilot.FlyToOrbit());
            pilot.Advance(120.0);   // past Ground Control's questions
            while (pilot.Craft.GroundControl.AwaitingReply != null) pilot.Craft.GroundControl.Reply(0);
            Assert.IsNull(pilot.Craft.TimeWarpInhibit, "coasting in orbit");
            Assert.AreEqual(100, pilot.Craft.MaxTimeWarp, "low orbit is limited to x100");
        }
    }
}
