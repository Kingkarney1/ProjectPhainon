using System;
using NUnit.Framework;
using Phainon.Simulation;

namespace Phainon.Tests
{
    /// <summary>One test for each fault found in the first review of the launch mission, so none of them comes back.</summary>
    public class RegressionTests
    {
        [Test]
        public void AHoldAfterIgnition_ShutsTheEngineDown_AndTheCountCanBePickedUp()
        {
            var pilot = new MissionPilot();
            Spacecraft craft = pilot.Craft;
            Assert.IsTrue(pilot.PowerUp());
            pilot.Press(ControlId.LaunchSequence);
            pilot.Set(ControlId.Throttle, 1.0);
            Assert.IsTrue(pilot.RunUntil(() => craft.Engine.IgnitionInterlockOpen, 60.0));
            Assert.IsTrue(pilot.Press(ControlId.EngineIgnition).Accepted);
            Assert.IsTrue(pilot.RunUntil(() => craft.Mission.CountdownRemaining <= 3.0, 60.0));

            // The transmitter drops out for a second with the engine already running.
            pilot.Set(ControlId.CommAntenna, 0);
            pilot.Advance(1.0);
            Assert.AreEqual(LaunchSequenceState.Hold, craft.Mission.LaunchState);
            Assert.IsFalse(craft.Engine.IsBurning, "a hold must not leave the engine burning the launch load away");
            pilot.Set(ControlId.CommAntenna, 1);

            // The ground supply refills the tanks, the count resumes by itself, and the crew light the engine again.
            Assert.IsTrue(pilot.RunUntil(() => craft.Mission.LaunchState != LaunchSequenceState.Hold, 120.0), "the count never resumed: " + craft.Mission.CurrentObjective);
            Assert.IsTrue(pilot.RunUntil(() => craft.Engine.IgnitionInterlockOpen, 60.0));
            Assert.IsTrue(pilot.Press(ControlId.EngineIgnition).Accepted, "second ignition");
            Assert.IsTrue(pilot.RunUntil(() => craft.Mission.HasLiftedOff, 60.0), "liftoff after the recycle");
            Assert.IsTrue(pilot.FlyToOrbit(), "orbit after a held count: " + craft.Mission.FailureReason);
        }

        [Test]
        public void ARecycledCount_IsAcceptedAgain_OnceTheTanksAreTopped()
        {
            var pilot = new MissionPilot();
            Spacecraft craft = pilot.Craft;
            Assert.IsTrue(pilot.PowerUp());
            pilot.Press(ControlId.LaunchSequence);
            pilot.Set(ControlId.Throttle, 0.3);
            pilot.RunUntil(() => craft.Engine.IgnitionInterlockOpen, 60.0);
            pilot.Press(ControlId.EngineIgnition);
            pilot.Advance(45.0);        // sits at T-0 on low thrust, burning propellant
            Assert.Less(craft.Fuel.GetFraction(0), craft.Mission.Spec.launchMinimumPropellantFraction);
            pilot.Press(ControlId.EngineCutoff);
            pilot.Advance(3.0);
            Assert.IsTrue(pilot.Press(ControlId.LaunchSequence).Accepted, "recycle");
            Assert.IsTrue(pilot.RunUntil(() => craft.Mission.AllGo, 120.0), "the tanks never came back up to the launch load");
            Assert.IsTrue(pilot.Press(ControlId.LaunchSequence).Accepted, "the new count");
        }

        [Test]
        public void TheEngine_CannotBeRelit_AfterAnAbort()
        {
            var pilot = new MissionPilot();
            Assert.IsTrue(pilot.PowerUp() && pilot.Launch());
            pilot.Advance(30.0);
            Assert.IsTrue(pilot.Press(ControlId.Abort).Accepted);
            pilot.Advance(2.0);
            Assert.IsFalse(pilot.Press(ControlId.EngineIgnition).Accepted, "ignition after an abort");
            pilot.Advance(5.0);
            Assert.IsFalse(pilot.Craft.Engine.IsBurning);
            Assert.AreEqual(IndicatorState.Off, pilot.Craft.GetIndicator(IndicatorId.EngineIgnition), "the ignition lamp must not invite a relight");
        }

        [Test]
        public void TheTimeToOrbit_IsFixedAtTheMomentOfOrbit()
        {
            var pilot = new MissionPilot();
            Assert.IsTrue(pilot.FlyFullMission());
            double atOrbit = pilot.Craft.Mission.OrbitAchievedMetSeconds;
            Assert.AreEqual(pilot.Craft.Mission.MissionElapsedSeconds, atOrbit, 0.1);
            pilot.Advance(90.0);
            Assert.AreEqual(atOrbit, pilot.Craft.Mission.OrbitAchievedMetSeconds, 1e-9, "the recorded time moved after orbit");
        }

        [Test]
        public void Periapsis_OnAnEscapePath_IsTheClosestApproach_NotTheCurrentAltitude()
        {
            var body = BodySpec.Earth();
            var flight = new FlightModel(new VehicleSpec(), body);
            // Well past a 300 km periapsis on a path with 3 km/s to spare at infinity.
            double periapsisRadius = body.radiusMeters + 300000.0;
            double periapsisSpeed = Math.Sqrt(3000.0 * 3000.0 + 2.0 * body.mu / periapsisRadius);
            double radius = body.radiusMeters + 5.0e7;
            double speed = Math.Sqrt(3000.0 * 3000.0 + 2.0 * body.mu / radius);
            double tangential = periapsisRadius * periapsisSpeed / radius;
            double radial = Math.Sqrt(speed * speed - tangential * tangential);
            flight.SetState(5.0e7, radial, tangential, 0.0);

            Assert.IsFalse(flight.IsBound);
            Assert.IsTrue(double.IsInfinity(flight.ApoapsisMeters));
            Assert.AreEqual(300000.0, flight.PeriapsisMeters, 50.0);
            Assert.Greater(flight.Eccentricity, 1.0);
            Assert.Greater(flight.TrueAnomaly, 0.0, "outbound");
            Assert.IsTrue(double.IsNaN(flight.TimeToPeriapsisSeconds), "there is no next periapsis on the way out");
        }

        [Test]
        public void ACabinThatCannotSupportTheCrew_EndsTheFlight()
        {
            var pilot = new MissionPilot();
            Spacecraft craft = pilot.Craft;
            Assert.IsTrue(pilot.FlyFullMission());
            pilot.Advance(5.0);
            // The crew shut the cabin loop down and leave it off.
            pilot.Press(ControlId.CabinSystems);
            Assert.IsFalse(craft.LifeSupport.Enabled);

            bool warned = false;
            craft.Warnings.Raised += entry => warned |= entry.Id == WarningId.CabinAirLow;
            double limit = craft.SystemsSpec.cabinAirEnduranceSeconds + craft.SystemsSpec.crewIncapacitationSeconds + 120.0;
            for (double t = 0.0; t < limit && !craft.Mission.IsFailed; t += 1.0)
            {
                if (craft.GroundControl.AwaitingReply != null) craft.GroundControl.Reply(0);
                craft.Tick(1.0);
            }
            Assert.IsTrue(warned, "the crew were never warned that the air was going");
            Assert.IsTrue(craft.Mission.IsFailed, "an unbreathable cabin had no consequence");
            StringAssert.Contains("cabin", craft.Mission.FailureReason);
        }

        [Test]
        public void TheOxygenReserve_OutlastsTheSlowestEscape()
        {
            var spec = new SystemsSpec();
            double days = spec.oxygenStartFraction * (1.0 - spec.lowOxygenFraction) / spec.oxygenUsePerSecond / 86400.0;
            Assert.Greater(days, 12.0, "the low-oxygen warning must not come before a minimum-energy coast reaches its last distance mark");
        }
    }
}
