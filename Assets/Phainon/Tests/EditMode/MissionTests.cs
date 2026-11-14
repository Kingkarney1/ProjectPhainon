using NUnit.Framework;
using Phainon.Simulation;

namespace Phainon.Tests
{
    /// <summary>
    /// Mission-level tests of the simulation. They drive the spacecraft through the same control
    /// interface as the cockpit, so they cover the control routing as well as the flight model.
    /// </summary>
    public class MissionTests
    {
        [Test]
        public void MainPower_IsRefused_WithoutABattery()
        {
            var pilot = new MissionPilot();
            Assert.IsFalse(pilot.Press(ControlId.MainPower).Accepted);
            Assert.IsFalse(pilot.Craft.Power.IsBusPowered);

            pilot.Set(ControlId.BatteryA, 1);
            Assert.IsTrue(pilot.Press(ControlId.MainPower).Accepted);
            pilot.Advance(1.0);
            Assert.IsTrue(pilot.Craft.Power.IsBusPowered);
            Assert.Greater(pilot.Craft.Power.BusVoltage, 20.0);
        }

        [Test]
        public void LaunchSequence_IsRefused_UntilEverySystemIsGo()
        {
            var pilot = new MissionPilot();
            pilot.PowerUp();
            pilot.Press(ControlId.Comms);          // switch comms back off
            pilot.Advance(1.0);

            CommandResult result = pilot.Press(ControlId.LaunchSequence);
            Assert.IsFalse(result.Accepted);
            StringAssert.Contains("COMMS LINK", result.Message);
            Assert.AreEqual(LaunchSequenceState.Idle, pilot.Craft.Mission.LaunchState);

            pilot.Press(ControlId.Comms);
            Assert.IsTrue(pilot.RunUntil(() => pilot.Craft.Mission.AllGo, 30.0));
            Assert.IsTrue(pilot.Press(ControlId.LaunchSequence).Accepted);
        }

        [Test]
        public void Ignition_IsInhibited_OutsideTheLaunchWindow()
        {
            var pilot = new MissionPilot();
            Assert.IsTrue(pilot.PowerUp());
            Assert.IsFalse(pilot.Press(ControlId.EngineIgnition).Accepted, "ignition before the launch sequence");

            pilot.Press(ControlId.LaunchSequence);
            pilot.Advance(2.0);
            Assert.IsFalse(pilot.Press(ControlId.EngineIgnition).Accepted, "ignition early in the count");
            Assert.IsFalse(pilot.Craft.Mission.HasLiftedOff);
        }

        [Test]
        public void Countdown_Holds_WhenASystemDropsOut_AndResumes()
        {
            var pilot = new MissionPilot();
            pilot.PowerUp();
            pilot.Press(ControlId.LaunchSequence);
            pilot.Advance(3.0);

            pilot.Set(ControlId.Guidance, 0);
            pilot.Advance(1.0);
            Assert.AreEqual(LaunchSequenceState.Hold, pilot.Craft.Mission.LaunchState);
            double heldAt = pilot.Craft.Mission.CountdownRemaining;
            pilot.Advance(5.0);
            Assert.AreEqual(heldAt, pilot.Craft.Mission.CountdownRemaining, 1e-6, "clock must not run during a hold");

            pilot.Set(ControlId.Guidance, 1);
            pilot.Advance(1.0);
            Assert.AreEqual(LaunchSequenceState.Counting, pilot.Craft.Mission.LaunchState);
            Assert.Less(pilot.Craft.Mission.CountdownRemaining, heldAt);
        }

        [Test]
        public void Vehicle_StaysOnThePad_UntilTheThrottleIsAdvanced()
        {
            var pilot = new MissionPilot();
            pilot.PowerUp();
            pilot.Press(ControlId.LaunchSequence);
            pilot.Set(ControlId.Throttle, 0.0);
            pilot.RunUntil(() => pilot.Craft.Engine.IgnitionInterlockOpen, 60.0);
            Assert.IsTrue(pilot.Press(ControlId.EngineIgnition).Accepted);
            pilot.Advance(20.0);
            Assert.IsFalse(pilot.Craft.Mission.HasLiftedOff);
            Assert.AreEqual(0.0, pilot.Craft.Flight.AltitudeMeters, 1e-9);

            pilot.Set(ControlId.Throttle, 1.0);
            Assert.IsTrue(pilot.RunUntil(() => pilot.Craft.Mission.HasLiftedOff, 10.0));
            pilot.Advance(5.0);
            Assert.Greater(pilot.Craft.Flight.AltitudeMeters, 10.0);
        }

        [Test]
        public void Throttle_And_ThrustLimit_ControlEngineOutput()
        {
            var pilot = new MissionPilot();
            pilot.PowerUp();
            pilot.Launch();
            pilot.Advance(5.0);
            double full = pilot.Craft.Engine.ThrustNewtons;

            pilot.Set(ControlId.Throttle, 0.5);
            pilot.Advance(0.5);
            Assert.AreEqual(full * 0.5, pilot.Craft.Engine.ThrustNewtons, full * 0.01);

            pilot.Set(ControlId.Throttle, 1.0);
            pilot.Set(ControlId.ThrustLimit, 0.0);   // dial fully down
            pilot.Advance(0.5);
            Assert.AreEqual(full * pilot.Craft.VehicleSpec.minThrustLimit, pilot.Craft.Engine.ThrustNewtons, full * 0.01);
        }

        [Test]
        public void StageSeparation_IsRefused_WhileThrustingOrTooLow()
        {
            var pilot = new MissionPilot();
            pilot.PowerUp();
            pilot.Launch();
            pilot.Advance(10.0);
            Assert.IsFalse(pilot.Press(ControlId.StageSeparation).Accepted, "below minimum altitude");
            Assert.AreEqual(0, pilot.Craft.Engine.ActiveStage);

            pilot.RunUntil(() => pilot.Craft.Flight.AltitudeMeters > 45000, 300.0);
            Assert.IsTrue(pilot.Craft.Engine.IsBurning);
            Assert.IsFalse(pilot.Press(ControlId.StageSeparation).Accepted, "engine still thrusting");
        }

        [Test]
        public void FullMission_ReachesOrbit_WithACarefulPilot()
        {
            var pilot = new MissionPilot();
            Assert.IsTrue(pilot.FlyFullMission(), "mission did not complete: " + pilot.Craft.Mission.FailureReason);

            Spacecraft craft = pilot.Craft;
            Assert.AreEqual(MissionPhase.OuterSpace, craft.Mission.Phase);
            Assert.GreaterOrEqual(craft.Flight.PeriapsisMeters, craft.Mission.Spec.orbitMinPeriapsisMeters);
            Assert.Greater(craft.Flight.AltitudeMeters, craft.Mission.Spec.spaceBoundaryAltitudeMeters);
            Assert.Greater(craft.Flight.Speed, 7600.0);
            Assert.Less(craft.Mission.MissionElapsedSeconds, 600.0);
            Assert.AreEqual(1, craft.Engine.ActiveStage);
            foreach (MissionEvent e in craft.Mission.Events)
                Assert.AreEqual(MissionEventStatus.Done, e.Status, e.Label);
            Assert.IsFalse(craft.Warnings.IsActive(WarningId.DynamicPressureHigh));
            foreach (ChecklistItem item in craft.Mission.Checklist)
                Assert.IsTrue(item.IsGo, "systems status row " + item.Label + " shows " + item.Status + " after a nominal flight");
        }

        [Test]
        public void Orbit_IsStable_WhileCoasting()
        {
            var pilot = new MissionPilot();
            Assert.IsTrue(pilot.FlyFullMission(), "mission did not complete: " + pilot.Craft.Mission.FailureReason);

            // Let the engine's thrust tail-off finish before taking the reference orbit.
            pilot.Advance(5.0);
            Assert.AreEqual(0.0, pilot.Craft.Engine.ThrustNewtons, 1e-6);

            FlightModel flight = pilot.Craft.Flight;
            double apoapsis = flight.ApoapsisMeters;
            double periapsis = flight.PeriapsisMeters;

            pilot.Advance(1800.0);   // half an hour of unpowered flight

            Assert.AreEqual(apoapsis, flight.ApoapsisMeters, 2000.0, "apoapsis drifted");
            Assert.AreEqual(periapsis, flight.PeriapsisMeters, 2000.0, "periapsis drifted");
            Assert.GreaterOrEqual(flight.AltitudeMeters, periapsis - 2000.0);
            Assert.LessOrEqual(flight.AltitudeMeters, apoapsis + 2000.0);
            Assert.AreEqual(MissionPhase.OuterSpace, pilot.Craft.Mission.Phase, "the mission must stay complete");
        }

        [Test]
        public void FullMission_ReachesOrbit_WithASloppyPilot()
        {
            var pilot = new MissionPilot { MaxThrottle = 0.85, ReactionDelay = 12.0 };
            Assert.IsTrue(pilot.FlyFullMission(), "mission did not complete: " + pilot.Craft.Mission.FailureReason);
        }

        [Test]
        public void FullThrottle_ThroughMaxQ_RaisesTheMasterAlarm()
        {
            var pilot = new MissionPilot { RespectLimits = false };
            pilot.PowerUp();
            pilot.Launch();
            Assert.IsTrue(pilot.RunUntil(() => pilot.Craft.Warnings.IsActive(WarningId.DynamicPressureHigh), 120.0,
                () => pilot.Set(ControlId.Throttle, 1.0)));
            Assert.IsTrue(pilot.Craft.Warnings.MasterAlarmActive);
            Assert.IsTrue(pilot.Press(ControlId.MasterAlarm).Accepted);
            Assert.IsFalse(pilot.Craft.Warnings.MasterAlarmActive);
            Assert.IsTrue(pilot.Craft.Warnings.IsActive(WarningId.DynamicPressureHigh), "acknowledging must not clear the condition");
        }

        [Test]
        public void SwitchingFuelPumpsOff_InFlight_FlamesOutTheEngine()
        {
            var pilot = new MissionPilot();
            pilot.PowerUp();
            pilot.Launch();
            pilot.Advance(20.0);
            pilot.Set(ControlId.FuelPump, 0);
            Assert.IsTrue(pilot.RunUntil(() => !pilot.Craft.Engine.IsBurning, 15.0));
            pilot.Advance(1.0);
            Assert.IsTrue(pilot.Craft.Warnings.IsActive(WarningId.EngineFlameout));
        }

        [Test]
        public void NeverSeparating_FailsTheMission()
        {
            var pilot = new MissionPilot();
            pilot.PowerUp();
            pilot.Launch();
            Assert.IsTrue(pilot.RunUntil(() => pilot.Craft.Mission.IsFailed, 1500.0));
            Assert.IsNotEmpty(pilot.Craft.Mission.FailureReason);
        }

        [Test]
        public void Condensation_FormsOnlyNearTheSpeedOfSoundInHumidAir()
        {
            Assert.Greater(VapourModel.CondensationFactor(1.0, 3000.0), 0.6, "transonic at 3 km");
            Assert.Less(VapourModel.CondensationFactor(0.3, 3000.0), 0.01, "slow flight");
            Assert.Less(VapourModel.CondensationFactor(2.5, 3000.0), 0.01, "well past the speed of sound");
            Assert.Less(VapourModel.CondensationFactor(1.0, 60000.0), 0.01, "no humidity at 60 km");
        }

        [Test]
        public void CameraSelect_CyclesTheExternalView()
        {
            var pilot = new MissionPilot();
            Assert.AreEqual(ExternalView.AftHull, pilot.Craft.Settings.ExternalCameraView);
            pilot.Press(ControlId.CameraSelect);
            Assert.AreEqual(ExternalView.Chase, pilot.Craft.Settings.ExternalCameraView);
            pilot.Press(ControlId.CameraSelect);
            Assert.AreEqual(ExternalView.AftHull, pilot.Craft.Settings.ExternalCameraView);
        }

        [Test]
        public void Abort_EndsTheMission()
        {
            var pilot = new MissionPilot();
            Assert.IsFalse(pilot.Press(ControlId.Abort).Accepted, "abort is not armed on a cold vehicle");
            pilot.PowerUp();
            pilot.Launch();
            pilot.Advance(15.0);
            Assert.IsTrue(pilot.Press(ControlId.Abort).Accepted);
            pilot.Advance(1.0);
            Assert.IsTrue(pilot.Craft.Mission.IsFailed);
            Assert.IsFalse(pilot.Craft.Engine.IsBurning);
        }

        [Test]
        public void LosingTheMainBus_DarkensIndicators_AndDropsNavAlignment()
        {
            var pilot = new MissionPilot();
            pilot.PowerUp();
            Assert.AreEqual(IndicatorState.On, pilot.Craft.GetIndicator(IndicatorId.NavSystem));

            pilot.Press(ControlId.MainPower);
            pilot.Advance(0.5);
            Assert.AreEqual(IndicatorState.Off, pilot.Craft.GetIndicator(IndicatorId.NavSystem));
            Assert.IsFalse(pilot.Craft.Navigation.Aligned);
            Assert.IsFalse(pilot.Craft.Mission.AllGo);
        }
    }
}
