using System;

namespace Phainon.Simulation
{
    public enum NavMode { Standby = 0, Ascent = 1, Orbit = 2 }

    /// <summary>Inertial platform alignment, the navigation mode selector and the guidance enable switch.</summary>
    public class NavigationSystem
    {
        private readonly SystemsSpec spec;

        public NavigationSystem(SystemsSpec spec) { this.spec = spec; }

        public bool Enabled { get; private set; }
        public NavMode Mode { get; set; }
        public bool GuidanceEnabled { get; set; }

        /// <summary>Platform alignment progress, 0..1.</summary>
        public double AlignmentProgress { get; private set; }
        public bool Aligned { get { return AlignmentProgress >= 1.0; } }
        public bool Aligning { get { return Enabled && !Aligned && AlignmentProgress > 0.0; } }

        /// <summary>True when the flight computer is steering the vehicle.</summary>
        public bool GuidanceActive { get { return GuidanceEnabled && Aligned && Mode != NavMode.Standby; } }

        public event Action AlignmentComplete;

        public CommandResult Toggle(bool busPowered)
        {
            if (Enabled)
            {
                Enabled = false;
                return CommandResult.Ok("NAV SYSTEM OFF");
            }
            if (!busPowered) return CommandResult.Rejected("NAV SYSTEM - MAIN BUS UNPOWERED");
            Enabled = true;
            return CommandResult.Ok("NAV SYSTEM ON - ALIGNING PLATFORM");
        }

        /// <summary>A fault drops the platform out. The crew restart it with the NAV SYSTEM button.</summary>
        public void Trip()
        {
            Enabled = false;
            AlignmentProgress = 0.0;
        }

        public void Tick(double dt, bool busPowered)
        {
            if (!busPowered) Enabled = false;

            if (!Enabled)
            {
                // The platform loses its reference when unpowered.
                AlignmentProgress = 0.0;
                return;
            }

            if (!Aligned)
            {
                AlignmentProgress = Math.Min(1.0, AlignmentProgress + dt / Math.Max(0.1, spec.navAlignSeconds));
                if (Aligned && AlignmentComplete != null) AlignmentComplete();
            }
        }
    }
}
