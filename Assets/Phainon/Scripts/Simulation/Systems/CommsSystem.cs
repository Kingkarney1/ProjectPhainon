using System;

namespace Phainon.Simulation
{
    [Serializable]
    public class CommsSnapshot
    {
        public bool enabled, antenna, antennaFaulted;
        public double acquisition, signal, clock;
    }

    /// <summary>Radio link to mission control: transceiver power, antenna transmit switch and link acquisition.</summary>
    public class CommsSystem
    {
        private readonly SystemsSpec spec;
        private double clock;

        public CommsSystem(SystemsSpec spec) { this.spec = spec; }

        public bool Enabled { get; private set; }
        public bool AntennaEnabled { get; set; }
        /// <summary>Latched by a fault: the transmitter is dead until its switch is cycled.</summary>
        public bool AntennaFaulted { get; private set; }

        public void FaultAntenna() { AntennaFaulted = true; }
        public double AcquisitionProgress { get; private set; }
        public bool LinkEstablished { get { return AcquisitionProgress >= 1.0; } }
        /// <summary>Signal strength, 0..1.</summary>
        public double SignalStrength { get; private set; }

        public event Action<bool> LinkChanged;

        public CommandResult Toggle(bool busPowered)
        {
            if (Enabled)
            {
                Enabled = false;
                return CommandResult.Ok("COMMS OFF");
            }
            if (!busPowered) return CommandResult.Rejected("COMMS - MAIN BUS UNPOWERED");
            Enabled = true;
            return CommandResult.Ok(AntennaEnabled ? "COMMS ON - ACQUIRING SIGNAL" : "COMMS ON - S-BAND XMIT IS OFF");
        }

        public CommsSnapshot Capture()
        {
            return new CommsSnapshot
            {
                enabled = Enabled, antenna = AntennaEnabled, antennaFaulted = AntennaFaulted,
                acquisition = AcquisitionProgress, signal = SignalStrength, clock = clock
            };
        }

        public void Restore(CommsSnapshot s)
        {
            Enabled = s.enabled;
            AntennaEnabled = s.antenna;
            AntennaFaulted = s.antennaFaulted;
            AcquisitionProgress = s.acquisition;
            SignalStrength = s.signal;
            clock = s.clock;
        }

        /// <param name="altitudeMeters">Length of the signal path to the ground station.</param>
        public void Tick(double dt, bool busPowered, double altitudeMeters, double dynamicPressurePa)
        {
            clock += dt;
            if (!busPowered) Enabled = false;

            bool wasLinked = LinkEstablished;
            if (!AntennaEnabled) AntennaFaulted = false;
            bool acquiring = Enabled && AntennaEnabled && !AntennaFaulted;
            if (acquiring)
                AcquisitionProgress = Math.Min(1.0, AcquisitionProgress + dt / Math.Max(0.1, spec.commsAcquireSeconds));
            else
                AcquisitionProgress = 0.0;

            // Strength falls gently with range and dips under high aerodynamic load.
            double target = 0.0;
            if (LinkEstablished)
            {
                double rangeLoss = SimMath.Clamp01(altitudeMeters / 2500000.0) * 0.25;
                // Beyond low orbit the loss grows with each factor of ten in distance: about half strength at the Moon.
                if (altitudeMeters > 2500000.0) rangeLoss += SimMath.Clamp01(Math.Log10(altitudeMeters / 2500000.0) / 3.0) * 0.35;
                double plasmaLoss = SimMath.Clamp01(dynamicPressurePa / 40000.0) * 0.3;
                double flutter = 0.02 * Math.Sin(clock * 1.7) + 0.015 * Math.Sin(clock * 4.3);
                target = SimMath.Clamp01(0.96 - rangeLoss - plasmaLoss + flutter);
            }
            SignalStrength = SimMath.Approach(SignalStrength, target, 0.6, dt);

            if (wasLinked != LinkEstablished && LinkChanged != null) LinkChanged(LinkEstablished);
        }
    }
}
