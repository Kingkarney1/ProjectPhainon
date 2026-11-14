using System;

namespace Phainon.Simulation
{
    /// <summary>
    /// Small maths helpers for the simulation assembly, which deliberately has no
    /// UnityEngine dependency so it can run in tests, tools or a future server build.
    /// </summary>
    public static class SimMath
    {
        public const double Deg2Rad = Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / Math.PI;

        public static double Clamp(double value, double min, double max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        public static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        public static float Clamp01(float value) { return Clamp(value, 0f, 1f); }
        public static double Clamp01(double value) { return Clamp(value, 0.0, 1.0); }

        public static double Lerp(double a, double b, double t) { return a + (b - a) * Clamp01(t); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }

        public static double InverseLerp(double a, double b, double value)
        {
            if (Math.Abs(b - a) < 1e-12) return 0.0;
            return Clamp01((value - a) / (b - a));
        }

        public static double MoveTowards(double current, double target, double maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Math.Sign(target - current) * maxDelta;
        }

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Math.Sign(target - current) * maxDelta;
        }

        /// <summary>Frame-rate independent exponential approach towards a target.</summary>
        public static double Approach(double current, double target, double timeConstant, double dt)
        {
            if (timeConstant <= 1e-6) return target;
            return target + (current - target) * Math.Exp(-dt / timeConstant);
        }

        /// <summary>Wraps an angle in degrees to the range [-180, 180).</summary>
        public static double WrapDegrees(double degrees)
        {
            degrees = (degrees + 180.0) % 360.0;
            if (degrees < 0) degrees += 360.0;
            return degrees - 180.0;
        }
    }
}
