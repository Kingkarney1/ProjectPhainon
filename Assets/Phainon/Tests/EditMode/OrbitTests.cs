using System;
using NUnit.Framework;
using Phainon.Simulation;

namespace Phainon.Tests
{
    /// <summary>The orbit formulas the displays and the story rely on, checked against the integrator itself.</summary>
    public class OrbitTests
    {
        private static FlightModel Inbound(out double mass)
        {
            var scenario = new ScenarioSpec();
            var vehicle = new VehicleSpec();
            var flight = new FlightModel(vehicle, scenario.body);
            double radial, tangential;
            scenario.ComputeStartVelocity(out radial, out tangential);
            flight.SetState(scenario.startAltitudeMeters, radial, tangential, 0.0);
            mass = 20000.0;
            return flight;
        }

        [Test]
        public void TheApproach_ReachesItsPredictedPeriapsis_AtThePredictedTime()
        {
            double mass;
            FlightModel flight = Inbound(out mass);
            double predictedAltitude = flight.PeriapsisMeters;
            double predictedTime = flight.TimeToPeriapsisSeconds;
            Assert.Greater(predictedTime, 1800.0);

            double t = 0.0, lowest = double.MaxValue, lowestAt = 0.0;
            while (t < predictedTime * 1.2)
            {
                flight.Tick(1.0, 0.0, mass, 0.0, 0.0);
                t += 1.0;
                if (flight.AltitudeMeters < lowest) { lowest = flight.AltitudeMeters; lowestAt = t; }
            }
            Assert.AreEqual(predictedAltitude, lowest, predictedAltitude * 0.002, "closest approach");
            Assert.AreEqual(predictedTime, lowestAt, predictedTime * 0.005 + 2.0, "time of closest approach");
            Assert.Greater(flight.RadialVelocity, 0.0, "outbound afterwards");
            Assert.IsFalse(flight.IsBound, "gravity alone does not capture anything");
        }

        [Test]
        public void TheCaptureBurn_TheDisplayAsksFor_GivesTheOrbitItPromises()
        {
            double mass;
            FlightModel flight = Inbound(out mass);
            BodySpec body = flight.Body;
            double targetApoapsis = body.radiusMeters + 2.0e9;
            double periapsisRadius = body.radiusMeters + flight.PeriapsisMeters;
            double dv = OrbitMath.CaptureDeltaV(body.mu, flight.SpecificEnergy, periapsisRadius, targetApoapsis);
            Assert.That(dv, Is.InRange(400.0, 2500.0));

            // Coast to periapsis, take the speed off in one go, and see where the orbit ends up.
            while (flight.RadialVelocity < 0.0) flight.Tick(0.5, 0.0, mass, 0.0, 0.0);
            flight.SetState(flight.AltitudeMeters, flight.RadialVelocity, flight.TangentialVelocity - dv, flight.DownrangeAngle);
            Assert.IsTrue(flight.IsBound);
            Assert.AreEqual(2.0e9, flight.ApoapsisMeters, 2.0e9 * 0.02);

            // And it comes back round in one period, to within a small fraction of it.
            double period = flight.OrbitalPeriodSeconds;
            double predicted = flight.TimeToPeriapsisSeconds;
            Assert.AreEqual(period, predicted, period * 0.01, "just past periapsis the next one is a whole orbit away");
            double start = flight.AltitudeMeters;
            for (double t = 0.0; t < period; t += 1.0) flight.Tick(1.0, 0.0, mass, 0.0, 0.0);
            Assert.AreEqual(start, flight.AltitudeMeters, 4.0e6, "one period later the spacecraft should be back where it was");
        }

        [Test]
        public void GuidanceCanTurnTheEngine_AgainstTheFlightPath()
        {
            var vehicle = new VehicleSpec();
            var attitude = new AttitudeController(vehicle) { RcsEnabled = true };
            double mass;
            FlightModel flight = Inbound(out mass);
            var fuel = new FuelSystem(vehicle, new SystemsSpec());
            var engine = new EngineSystem(vehicle, fuel);
            attitude.SetAttitude(-60.0, 0.0, 0.0);

            for (int i = 0; i < 6000; i++) attitude.Tick(0.02, flight, engine, true, AttitudeHold.Retrograde, true, false);
            double against = SimMath.WrapDegrees(flight.FlightPathAngleDegrees + 180.0);
            Assert.AreEqual(0.0, SimMath.WrapDegrees(attitude.PitchDegrees - against), 0.1, "retrograde hold");

            // Thrust along that attitude takes speed off.
            double before = flight.Speed;
            flight.Tick(10.0, 250000.0, mass, attitude.PitchDegrees, 0.0);
            Assert.Less(flight.Speed, before + flight.Body.mu / (flight.Radius * flight.Radius) * 10.0 - 60.0, "the burn did not slow the spacecraft");
        }
    }
}
