using System;

namespace Phainon.Simulation
{
    /// <summary>
    /// A simple stand-in for the meteorology behind condensation clouds on a launch vehicle. Vapour
    /// forms where the air is humid and the flow around the vehicle is near the speed of sound, when
    /// local pressure drops are sharpest. Both conditions are approximated: humidity falls off
    /// exponentially with altitude, and the transonic effect is a bell curve centred on a Mach number.
    /// </summary>
    public static class VapourModel
    {
        /// <summary>Returns 0 (no condensation) to 1 (dense vapour).</summary>
        public static double CondensationFactor(double mach, double altitudeMeters,
            double humidityScaleHeightMeters = 6500.0, double peakMach = 1.0, double machWidth = 0.22)
        {
            if (altitudeMeters < 0.0) altitudeMeters = 0.0;
            double humidity = Math.Exp(-altitudeMeters / Math.Max(1.0, humidityScaleHeightMeters));
            double offset = (mach - peakMach) / Math.Max(0.01, machWidth);
            double transonic = Math.Exp(-offset * offset);
            // Scaled so that transonic flight in the lower atmosphere saturates, and it is half strength near 9 km.
            return SimMath.Clamp01(transonic * humidity * 2.2);
        }
    }
}
