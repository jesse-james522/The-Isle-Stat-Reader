using System;
using System.Collections.Generic;

namespace TheIsleStatReader.Core
{
    /// <summary>
    /// Returned by <see cref="DataLoader.GetCurveData"/>. Curves are always
    /// Prime-first, Frail-second.  Both channels are always present (the missing
    /// one is filled in by duplicating the available channel), so
    /// <see cref="PrimeLabel"/> and <see cref="FrailLabel"/> are always "Prime"
    /// and "Frail".  <see cref="HasDistinctChannels"/> is <c>false</c> when the
    /// source only had one real channel (swap button should not be shown).
    /// </summary>
    internal sealed record CurveDataResult(
        List<(double[] Times, double[] Values)> Curves,
        string YLabel,
        /// <summary>"Prime" (always set when Curves is non-empty).</summary>
        string PrimeLabel,
        /// <summary>"Frail" (always set when Curves is non-empty).</summary>
        string FrailLabel,
        /// <summary>
        /// True when Prime and Frail carry genuinely different data above 0.75.
        /// False when they were duplicated from a single source channel.
        /// </summary>
        bool HasDistinctChannels = false)
    {
        /// <summary>True when the source had only one distinct channel (no swap makes sense).</summary>
        public bool IsSingleChannel => !HasDistinctChannels;
    }


    /// <summary>
    /// A single stat sampled at several growth points
    /// (juvenile / subadult / adult / elder 87.5% / senior 87.5% / peak / prime / frail)
    /// plus the peak location.
    /// "Elder" = the stronger adult path (Prime Elder); "Senior" = the weaker path (Frail Elder).
    /// </summary>
    internal sealed class StatRow
    {
        /// <summary>Display name, e.g. "Sprint Speed".</summary>
        public string Name { get; init; } = "";

        /// <summary>Unit suffix shown next to values, e.g. "km/h", "kg", "".</summary>
        public string Unit { get; init; } = "";

        /// <summary>Format string for numeric values (e.g. "F1", "F0").</summary>
        public string Format { get; init; } = "F1";

        /// <summary>Elder (prime) curve at growth = 0.0 (newborn / hatchling). NaN if unavailable.</summary>
        public double Growth0 { get; set; } = double.NaN;

        /// <summary>Elder (prime) curve at growth = 0.25 (juvenile). NaN if unavailable.</summary>
        public double Juvenile { get; set; } = double.NaN;

        /// <summary>Elder (prime) curve at growth = 0.50 (subadult). NaN if unavailable.</summary>
        public double Subadult { get; set; } = double.NaN;

        /// <summary>Elder (prime) curve at growth = 0.75 (adult).</summary>
        public double Adult { get; set; } = double.NaN;

        /// <summary>Elder (prime) curve at growth = 0.875.</summary>
        public double Elder875 { get; set; } = double.NaN;

        /// <summary>Senior (frail) curve at growth = 0.875 (NaN if no senior/frail curve).</summary>
        public double Senior875 { get; set; } = double.NaN;

        /// <summary>True peak value sampled across both channels (may be before or after 0.875).</summary>
        public double Peak { get; set; } = double.NaN;

        /// <summary>Where on the x-axis the peak occurs (e.g. 0.875).</summary>
        public double PeakAt { get; set; } = double.NaN;

        /// <summary>Elder (prime) curve at growth = 1.0 — the stronger adult path.</summary>
        public double Prime { get; set; } = double.NaN;

        /// <summary>Senior (frail) curve at growth = 1.0 — the weaker adult path. NaN if no frail curve.</summary>
        public double Frail { get; set; } = double.NaN;

        /// <summary>True for rows derived from Damage.X balance attributes (attack damage).</summary>
        public bool IsAttack { get; init; }

        /// <summary>
        /// The raw attack key extracted from "Damage.X", e.g. "Bite", "Claw".
        /// Empty for non-attack rows.
        /// </summary>
        public string AttackKey { get; init; } = "";
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

        // ── Stamina durations (at adult / 75% growth) ────────────────────────────

        /// <summary>Sprint duration at adult growth (from Stamina.Spending.Sprinting).</summary>
        public double SprintDurationSec { get; set; } = double.NaN;

        /// <summary>Trot duration at adult growth (from Stamina.Spending.Trotting). NaN if no stamina cost.</summary>
        public double TrotDurationSec { get; set; } = double.NaN;

        /// <summary>Fast-swim duration at adult growth (from Stamina.Spending.FastSwimming).</summary>
        public double FastSwimDurationSec { get; set; } = double.NaN;

        /// <summary>Slow-swim duration at adult growth (from Stamina.Spending.SlowSwimming).</summary>
        public double SlowSwimDurationSec { get; set; } = double.NaN;

        // ── Max ranges at adult growth ────────────────────────────────────────────

        /// <summary>Max sprint range in metres (SprintSpeed × SprintDuration, at 75% growth).</summary>
        public double SprintRangeM { get; set; } = double.NaN;

        /// <summary>Max fast-swim range in metres.</summary>
        public double FastSwimRangeM { get; set; } = double.NaN;

        /// <summary>Max slow-swim range in metres.</summary>
        public double SlowSwimRangeM { get; set; } = double.NaN;

        // ── Stamina regen (from balance attributes, not rest-curve) ──────────────

        /// <summary>
        /// Stamina regen while standing still — percent of max stamina per second
        /// (Stamina.Regen.Standing). ⚠ Exact attribute name may differ by game version.
        /// </summary>
        public double StaminaRegenStanding { get; set; } = double.NaN;

        /// <summary>
        /// Stamina regen while walking/trotting — percent per second
        /// (Stamina.Regen.Moving / Stamina.Regen.Trotting). ⚠ Experimental.
        /// </summary>
        public double StaminaRegenMoving { get; set; } = double.NaN;

        // ── Adult weight (used as proxy for HP in the heal calculator) ────────────

        /// <summary>Adult weight (kg) sampled at growth = 0.75. Used as the default
        /// HP baseline in the heal-time calculator.</summary>
        public double AdultWeight { get; set; } = double.NaN;

        // ── Health &amp; Blood regen ───────────────────────────────────────────────

        /// <summary>
        /// Passive health regen rate while standing/walking — percent of max HP per second
        /// (<c>Health.Regen</c>).
        /// </summary>
        public double HealthRegenStanding { get; set; } = double.NaN;

        /// <summary>
        /// Health regen rate while resting — percent of max HP per second
        /// (<c>Health.Regen.Resting</c>). Does not include the RestCurve time-based multiplier.
        /// </summary>
        public double HealthRegenResting { get; set; } = double.NaN;

        /// <summary>
        /// Health regen when the combat health floor is lifted — percent of max HP per second
        /// (<c>LockedHealth.Regen</c>).
        /// </summary>
        public double LockedHealthRegen { get; set; } = double.NaN;

        /// <summary>
        /// Blood regen while standing/active — percent of max blood per second
        /// (<c>Blood.Regen.Standing</c>). Higher values mean faster bleed recovery.
        /// </summary>
        public double BloodRegenStanding { get; set; } = double.NaN;

        /// <summary>
        /// Blood regen while resting — percent of max blood per second
        /// (<c>Blood.Regen.Resting</c>).
        /// </summary>
        public double BloodRegenResting { get; set; } = double.NaN;
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
        /// Returns the maximum non-NaN value in the curve at times >= <paramref name="threshold"/>.
        /// Returns NaN if no valid sample exists above the threshold.
        /// </summary>
        public static double FindMaxAbove(double[] times, double[] values, double threshold = 0.75)
        {
            if (times == null || values == null) return double.NaN;
            double max = double.NegativeInfinity;
            for (int i = 0; i < times.Length && i < values.Length; i++)
            {
                if (times[i] >= threshold && !double.IsNaN(values[i]) && values[i] > max)
                    max = values[i];
            }
            return double.IsNegativeInfinity(max) ? double.NaN : max;
        }

        /// <summary>
        /// Returns true if the two curves are effectively identical at all
        /// sample points at times >= <paramref name="threshold"/>, using
        /// a relative + absolute-floor epsilon so the comparison is
        /// scale-independent.
        /// <para>
        /// Uses the dense sample points from <paramref name="t0"/> (and
        /// <paramref name="t1"/>) directly; the other channel is evaluated
        /// via <see cref="SampleAt"/> at the same time. Requires at least
        /// 3 shared valid comparisons; returns false if neither curve has
        /// valid data above the threshold.
        /// </para>
        /// </summary>
        public static bool AreSameAbove(
            double[] t0, double[] v0,
            double[] t1, double[] v1,
            double threshold = 0.75,
            double relEpsilon = -1.0)
        {
            if (relEpsilon < 0) relEpsilon = TheIsleStatReader.Config.SameCurveRelEpsilon;

            int validComparisons = 0;

            // Check all dense sample points from curve 0 against curve 1.
            for (int i = 0; i < t0.Length && i < v0.Length; i++)
            {
                if (t0[i] < threshold || double.IsNaN(v0[i])) continue;
                double s1 = SampleAt(t1, v1, t0[i]);
                if (double.IsNaN(s1)) continue;

                double floor = Math.Max(Math.Abs(v0[i]), Math.Max(Math.Abs(s1), 1e-6));
                if (Math.Abs(v0[i] - s1) > relEpsilon * floor)
                    return false;
                validComparisons++;
            }

            // Also probe the dense points of curve 1 (catches keys only in t1).
            for (int i = 0; i < t1.Length && i < v1.Length; i++)
            {
                if (t1[i] < threshold || double.IsNaN(v1[i])) continue;
                double s0 = SampleAt(t0, v0, t1[i]);
                if (double.IsNaN(s0)) continue;

                double floor = Math.Max(Math.Abs(v1[i]), Math.Max(Math.Abs(s0), 1e-6));
                if (Math.Abs(v1[i] - s0) > relEpsilon * floor)
                    return false;
                validComparisons++;
            }

            // Need a meaningful number of comparisons to declare equality.
            return validComparisons >= 3;
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
