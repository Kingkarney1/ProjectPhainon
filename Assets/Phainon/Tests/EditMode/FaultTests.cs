using System;
using System.Collections.Generic;
using NUnit.Framework;
using Phainon.Simulation;

namespace Phainon.Tests
{
    /// <summary>Faults: that they are reproducible, that each one really breaks its system, and that each is survivable.</summary>
    public class FaultTests
    {
        private static MissionPilot NewPilot(int faultSeed = 0)
        {
            return new MissionPilot(new Spacecraft(new VehicleSpec(), new SystemsSpec(), new MissionSpec(), null, faultSeed));
        }

        /// <summary>Powers up, launches and flies the first part of the ascent.</summary>
        private static MissionPilot Airborne(double seconds = 60.0)
        {
            MissionPilot pilot = NewPilot();
            Assert.IsTrue(pilot.PowerUp() && pilot.Launch());
            pilot.FlyToOrbit(seconds);
            Assert.IsTrue(pilot.Craft.Engine.IsBurning);
            return pilot;
        }

        /// <summary>A crew member who does whatever the objective guide points at, after a human pause.</summary>
        private static void FollowTheGuide(MissionPilot pilot)
        {
            Spacecraft craft = pilot.Craft;
            double nextAction = 0.0;
            craft.Faults.Raised += id => nextAction = craft.Mission.MissionElapsedSeconds + 3.0;
            pilot.OnTick = () =>
            {
                double met = craft.Mission.MissionElapsedSeconds;
                if (met < nextAction) return;
                switch (craft.Mission.SuggestedControl)
                {
                    case ControlId.Apu: pilot.Set(ControlId.Apu, craft.Power.ApuEnabled ? 0 : 1); break;
                    case ControlId.FuelPump: pilot.Set(ControlId.FuelPump, craft.Fuel.PumpsEnabled ? 0 : 1); break;
                    case ControlId.BatteryA: pilot.Set(ControlId.BatteryA, 0); break;
                    case ControlId.CommAntenna: pilot.Set(ControlId.CommAntenna, craft.Comms.AntennaEnabled ? 0 : 1); break;
                    case ControlId.NavSystem: pilot.Press(ControlId.NavSystem); break;
                    case ControlId.Comms: pilot.Press(ControlId.Comms); break;
                    case ControlId.CabinSystems: pilot.Press(ControlId.CabinSystems); break;
                    case ControlId.EngineIgnition:
                        // Relight after a flameout. Staging ignition is the base pilot's job.
                        if (craft.Mission.Phase != MissionPhase.Ascent && craft.Mission.Phase != MissionPhase.OrbitalInsertion) return;
                        pilot.Press(ControlId.EngineIgnition);
                        break;
                    default: return;
                }
                nextAction = met + 0.6;
            };
        }

        [Test]
        public void TheSameSeed_GivesTheSameFault_AtTheSameTime()
        {
            var seen = new HashSet<FaultId>();
            for (int seed = 1; seed <= 60; seed++)
            {
                FaultSystem a = NewPilot(seed).Craft.Faults;
                FaultSystem b = NewPilot(seed).Craft.Faults;
                Assert.AreEqual(a.Scheduled, b.Scheduled);
                Assert.AreEqual(a.ScheduledMetSeconds, b.ScheduledMetSeconds);
                Assert.That(a.ScheduledMetSeconds, Is.InRange(25.0, 300.0));
                seen.Add(a.Scheduled);
            }
            Assert.AreEqual(FaultSystem.RandomPoolCount, seen.Count, "every fault a launch can draw should be reachable from some seed");

            double[] raisedAt = new double[2];
            for (int run = 0; run < 2; run++)
            {
                MissionPilot pilot = NewPilot(4242);
                int index = run;
                raisedAt[index] = -1.0;
                pilot.Craft.Faults.Raised += id => raisedAt[index] = pilot.Craft.Mission.MissionElapsedSeconds;
                FollowTheGuide(pilot);
                pilot.FlyFullMission();
            }
            Assert.Greater(raisedAt[0], 0.0, "the scheduled fault never happened");
            Assert.AreEqual(raisedAt[0], raisedAt[1], 1e-9);
        }

        [Test]
        public void ApuTrip_TakesGimbalAway_UntilTheSwitchIsCycled()
        {
            MissionPilot pilot = Airborne();
            Spacecraft craft = pilot.Craft;
            craft.Faults.Inject(FaultId.ApuTrip);
            pilot.Advance(1.0);
            Assert.IsFalse(craft.Power.ApuOnline);
            Assert.IsFalse(craft.Attitude.HasControlAuthority, "no APU means no engine gimbal");
            Assert.IsTrue(craft.Warnings.IsActive(WarningId.ApuOffline));
            Assert.IsTrue(craft.Power.IsBusPowered, "the batteries carry the bus");

            pilot.Advance(10.0);
            Assert.IsFalse(craft.Power.ApuOnline, "a tripped APU must not restart by itself");

            pilot.Set(ControlId.Apu, 0);
            pilot.Advance(0.5);
            pilot.Set(ControlId.Apu, 1);
            pilot.Advance(4.0);
            Assert.IsTrue(craft.Power.ApuOnline);
            Assert.IsTrue(craft.Attitude.HasControlAuthority);
            Assert.IsFalse(craft.Faults.IsActive(FaultId.ApuTrip));
        }

        [Test]
        public void PumpFault_WarnsWellBeforeTheEngineStarves_AndCyclingThePumpSavesIt()
        {
            MissionPilot ignored = Airborne();
            ignored.Craft.Faults.Inject(FaultId.FuelPump);
            ignored.Advance(1.0);
            Assert.IsTrue(ignored.Craft.Warnings.IsActive(WarningId.FeedPressureDropping));
            ignored.Advance(9.0);
            Assert.IsTrue(ignored.Craft.Engine.IsBurning, "the crew must get at least ten seconds of warning");
            ignored.Advance(15.0);
            Assert.IsFalse(ignored.Craft.Engine.IsBurning, "an ignored pump fault ends in a flameout");

            MissionPilot repaired = Airborne();
            Spacecraft craft = repaired.Craft;
            craft.Faults.Inject(FaultId.FuelPump);
            repaired.Advance(8.0);
            repaired.Set(ControlId.FuelPump, 0);
            repaired.Advance(0.8);
            repaired.Set(ControlId.FuelPump, 1);
            repaired.Advance(6.0);
            Assert.IsTrue(craft.Engine.IsBurning, "a quick off-and-on must not starve the engine");
            Assert.IsTrue(craft.Fuel.FeedReady);
            Assert.IsFalse(craft.Faults.IsActive(FaultId.FuelPump));
            Assert.IsFalse(craft.Warnings.IsActive(WarningId.FeedPressureDropping));
        }

        [Test]
        public void BatteryFault_DrainsTheOtherBattery_UntilIsolated()
        {
            MissionPilot pilot = Airborne();
            Spacecraft craft = pilot.Craft;
            craft.Faults.Inject(FaultId.BatteryA);
            double before = craft.Power.BatteryBCharge;
            pilot.Advance(30.0);
            Assert.Less(craft.Power.BatteryBCharge, before - 0.01);
            Assert.IsTrue(craft.Warnings.IsActive(WarningId.BatteryFault));

            pilot.Set(ControlId.BatteryA, 0);
            pilot.Advance(1.0);
            double isolated = craft.Power.BatteryBCharge;
            pilot.Advance(30.0);
            Assert.GreaterOrEqual(craft.Power.BatteryBCharge, isolated);
            Assert.IsFalse(craft.Faults.IsActive(FaultId.BatteryA));
            Assert.IsFalse(craft.Warnings.IsActive(WarningId.BatteryFault));
        }

        [Test]
        public void NavTrip_StopsGuidance_UntilThePlatformIsRealigned()
        {
            MissionPilot pilot = Airborne();
            Spacecraft craft = pilot.Craft;
            craft.Faults.Inject(FaultId.NavPlatform);
            pilot.Advance(1.0);
            Assert.IsFalse(craft.Navigation.GuidanceActive);
            double held = craft.Attitude.PitchDegrees;
            pilot.Advance(3.0);
            Assert.AreEqual(held, craft.Attitude.PitchDegrees, 1e-9, "with no guidance and no hand input the pitch holds");
            Assert.IsTrue(craft.Warnings.IsActive(WarningId.NavNotAligned));

            pilot.Press(ControlId.NavSystem);
            pilot.Advance(craft.SystemsSpec.navAlignSeconds + 1.0);
            Assert.IsTrue(craft.Navigation.GuidanceActive);
            Assert.IsFalse(craft.Faults.IsActive(FaultId.NavPlatform));
        }

        [Test]
        public void AntennaFault_DropsTheLink_UntilTheTransmitterIsCycled()
        {
            MissionPilot pilot = Airborne();
            Spacecraft craft = pilot.Craft;
            craft.Faults.Inject(FaultId.Antenna);
            pilot.Advance(10.0);
            Assert.IsFalse(craft.Comms.LinkEstablished, "a dead transmitter must not come back by itself");
            Assert.IsTrue(craft.Warnings.IsActive(WarningId.CommLoss));

            pilot.Set(ControlId.CommAntenna, 0);
            pilot.Advance(0.5);
            pilot.Set(ControlId.CommAntenna, 1);
            pilot.Advance(craft.SystemsSpec.commsAcquireSeconds + 1.0);
            Assert.IsTrue(craft.Comms.LinkEstablished);
            Assert.IsFalse(craft.Faults.IsActive(FaultId.Antenna));
        }

        [Test]
        public void WeakEngine_DeliversLessThrust_AndTheDisplayedOutputSaysSo()
        {
            MissionPilot pilot = NewPilot();
            Assert.IsTrue(pilot.PowerUp() && pilot.Launch());
            Spacecraft craft = pilot.Craft;
            double factor = craft.SystemsSpec.weakEngineThrustFactor;
            double injectedAt = -1.0;
            bool checkedOnce = false;
            pilot.OnTick = () =>
            {
                double met = craft.Mission.MissionElapsedSeconds;
                if (injectedAt < 0.0 && craft.Engine.ActiveStage == 1 && craft.Engine.State == EngineState.Running)
                {
                    injectedAt = met;
                    craft.Faults.Inject(FaultId.WeakUpperEngine);
                }
                if (injectedAt < 0.0 || checkedOnce || met < injectedAt + 2.0) return;
                checkedOnce = true;
                Assert.AreEqual(craft.Engine.EffectiveThrottle * factor, craft.Engine.OutputFraction, 1e-6);
                Assert.AreEqual(craft.Engine.ActiveStageSpec.thrustNewtons * craft.Engine.OutputFraction, craft.Engine.ThrustNewtons, 1.0);
                Assert.IsTrue(craft.Warnings.IsActive(WarningId.ThrustLow));
            };
            pilot.FlyToOrbit();
            Assert.IsTrue(checkedOnce, "the upper stage never ran with the fault");
        }

        [Test]
        public void APilotWhoFollowsTheGuide_StillReachesOrbit_WhateverBreaks()
        {
            foreach (FaultId fault in Enum.GetValues(typeof(FaultId)))
            {
                foreach (double when in new[] { 26.0, 70.0, 140.0, 260.0 })
                {
                    MissionPilot pilot = NewPilot();
                    Spacecraft craft = pilot.Craft;
                    FollowTheGuide(pilot);
                    bool injected = false;
                    Action follow = pilot.OnTick;
                    pilot.OnTick = () =>
                    {
                        bool engineReady = fault != FaultId.WeakUpperEngine
                                           || (craft.Engine.ActiveStage == 1 && craft.Engine.State == EngineState.Running);
                        if (!injected && craft.Mission.MissionElapsedSeconds >= when && engineReady)
                        {
                            injected = true;
                            craft.Faults.Inject(fault);
                        }
                        follow();
                    };

                    string label = fault + " at T+" + when;
                    Assert.IsTrue(pilot.FlyFullMission(), label + ": " + craft.Mission.FailureReason);
                    Assert.IsTrue(injected, label + ": the fault was never injected");
                    if (fault != FaultId.WeakUpperEngine)
                        Assert.IsFalse(craft.Faults.IsActive(fault), label + ": still broken in orbit");
                }
            }
        }

        [Test]
        public void GroundControl_AdvisesOnAnActiveFault_AndSaysNothingAboutAFixedOne()
        {
            MissionPilot slow = Airborne();
            slow.Craft.Faults.Inject(FaultId.BatteryA);
            slow.Advance(12.0);
            Assert.IsTrue(Heard(slow.Craft, "fault_battery"), "Ground Control should have called the battery fault");
            Assert.IsFalse(Heard(slow.Craft, "cleared_battery"));

            MissionPilot quick = Airborne();
            quick.Craft.Faults.Inject(FaultId.BatteryA);
            quick.Advance(1.0);
            quick.Set(ControlId.BatteryA, 0);
            quick.Advance(12.0);
            Assert.IsFalse(Heard(quick.Craft, "fault_battery"), "advice for a fault that is already fixed");
            Assert.IsTrue(Heard(quick.Craft, "cleared_battery"));
        }

        private static bool Heard(Spacecraft craft, string lineId)
        {
            foreach (CommsMessage message in craft.GroundControl.History)
                if (message.Source != null && message.Source.id == lineId) return true;
            return false;
        }
    }
}
