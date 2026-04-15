using System;
using System.Collections.Generic;

namespace TheIsleStatReader.Core
{
    /// <summary>
    /// A single stat sampled at several growth points (juvenile / adult / peak / prime / frail)
    /// plus the peak location on the senior curve.
    /// </summary>
    internal sealed class StatRow
    {
        /// <summary>Display name, e.g. "Sprint Speed".</summary>
        public string Name { get; init; } = "";

        /// <summary>Unit suffix shown next to values, e.g. "km/h", "kg", "".</summary>
        public string Unit { get; init; } = "";

        /// <summary>Format string for numeric values (e.g. "F1", "F0").</summary>
        public string Format { get; init; } = "F1";

        /// <summary>Senior curve at growth = 0.25 (juvenile). NaN if unavailable.</summary>
        public double Juvenile { get; set; } = double.NaN;

        /// <summary>Senior curve at growth = 0.75 (adult).</summary>
        public double Adult { get; set; } = double.NaN;

        /// <summary>Senior curve sampled at its actual peak (may be before or after 0.875).</summary>
        public double Peak { get; set; } = double.NaN;

        /// <summary>Where on the x-axis the senior curve peaks (e.g. 0.875).</summary>
        public double PeakAt { get; set; } = double.NaN;

        /// <summary>Senior curve at growth = 1.0 ("prime elder" — full senior value).</summary>
        public double Prime { get; set; } = double.NaN;

        /// <summary>Elder curve at growth = 1.0 ("frail elder" — full elder value). NaN if no elder curve.</summary>
        public double Frail { get; set; } = double.NaN;
    }

    /// <summary>
    /// Per-dinosaur rollup of all stats at different growth points, plus scalar
    /// survival values (starve time, etc.) that don't vary with growth.
    /// </summary>
    internal sealed class DinoSummary
    {
        public string DinoName { get; init; } = "";

        /// <summary>
        /// Ordered stat rows. Order matches the summary table column order.
        /// </summary>
        public List<StatRow> Stats { get; } = new();

        // Scalar stats (don't vary with growth)
        public double TimeToStarveMin { get; set; } = double.NaN;
        public double TimeToDehydrateMin { get; set; } = double.NaN;
        public double TimeUnderwaterSec { get; set; } = double.NaN;

        /// <summary>Sprint duration at growth = 1.0 (from stamina spending rate).</summary>
        public double SprintDurationSec { get; set; } = double.NaN;

        /// <summary>Seconds to regen stamina from 0 to full while resting.</summary>
        public double RestToFullSec { get; set; } = double.NaN;

        /// <summary>Rest stamina regen rate in percent per second.</summary>
        public double RestRegenPerSec { get; set; } = double.NaN;
    }

    /// <summary>
    /// Utility helpers that operate on sampled curves produced by
    /// <see cref="CurveProcessor"/>.
    /// </summary>
    internal static class CurveSampler
    {
        /// <summary>
        /// Linear interpolation of a sampled curve at an arbitrary time t.
        /// Assumes times is monotonically increasing (it is, after ProcessCurve).
        /// Returns NaN if the curve is empty or t falls outside [first, last] or
        /// the interpolated value itself is NaN.
        /// </summary>
        public static double SampleAt(double[] times, double[] values, double t)
        {
            if (times == null || values == null || times.Length == 0)
                return double.NaN;

            if (t <= times[0])
                return values[0];
            if (t >= times[^1])
                return values[^1];

            // Binary search for the segment containing t
            int lo = 0, hi = times.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (times[mid] <= t) lo = mid;
                else hi = mid;
            }

            double t0 = times[lo], t1 = times[hi];
            double v0 = values[lo], v1 = values[hi];
            if (double.IsNaN(v0) || double.IsNaN(v1))
                return double.NaN;

            double dt = t1 - t0;
            if (dt <= 0) return v0;
            double s = (t - t0) / dt;
            return v0 + s * (v1 - v0);
        }

        /// <summary>
        /// Finds the maximum value on a sampled curve and the time at which
        /// it occurs. Ignores NaN values. Returns (NaN, NaN) if empty.
        /// </summary>
        public static (double PeakValue, double PeakTime) FindPeak(double[] times, double[] values)
        {
            if (times == null || values == null || times.Length == 0)
                return (double.NaN, double.NaN);

            double bestV = double.NegativeInfinity;
            double bestT = double.NaN;
            for (int i = 0; i < times.Length; i++)
            {
                if (double.IsNaN(values[i])) continue;
                if (values[i] > bestV)
                {
                    bestV = values[i];
                    bestT = times[i];
                }
            }
            return double.IsNegativeInfinity(bestV) ? (double.NaN, double.NaN) : (bestV, bestT);
        }
    }
}
