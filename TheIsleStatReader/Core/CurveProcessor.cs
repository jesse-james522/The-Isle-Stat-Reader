using System;
using System.Collections.Generic;
using CUE4Parse.UE4.Objects.Engine.Curves;

namespace TheIsleStatReader.Core
{
    /// <summary>
    /// Processes FRichCurve key data into sampled (time, value) arrays.
    /// Supports standard Hermite (unweighted) and UE weighted-Bezier tangents
    /// (<c>RCTWM_WeightedLeave</c>, <c>RCTWM_WeightedArrive</c>, <c>RCTWM_WeightedBoth</c>).
    /// </summary>
    internal static class CurveProcessor
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Processes a list of FRichCurveKey values into sampled time/value arrays.
        /// The InterpMode on each key applies to the OUTGOING segment from that key.
        /// Weighted-tangent segments are evaluated using UE's Bezier parameterisation.
        /// </summary>
        /// <param name="keys">Strongly-typed curve keys from CUE4Parse.</param>
        /// <param name="conversionFactor">Multiply all values (and tangents) by this factor.</param>
        public static (double[] Times, double[] Values) ProcessCurve(
            IReadOnlyList<FRichCurveKey> keys,
            double conversionFactor = 1.0)
        {
            if (keys == null || keys.Count == 0)
                return (Array.Empty<double>(), Array.Empty<double>());

            // Copy + sort by time (defensive — editor can save out-of-order keys)
            var parsed = new List<FRichCurveKey>(keys);
            parsed.Sort((a, b) => a.Time.CompareTo(b.Time));

            var timePts  = new List<double>();
            var valuePts = new List<double>();

            // Pre-extrapolation: if first key is at t > 0, prepend t=0 with first key's value
            if (parsed[0].Time > 0.0)
            {
                timePts.Add(0.0);
                valuePts.Add(parsed[0].Value * conversionFactor);
            }

            for (int i = 0; i < parsed.Count; i++)
            {
                var k = parsed[i];

                // Always include the key itself
                timePts.Add(k.Time);
                valuePts.Add(k.Value * conversionFactor);

                // Interpolate the segment between key i and key i+1
                if (i < parsed.Count - 1)
                {
                    var    k2 = parsed[i + 1];
                    double dt = k2.Time - k.Time;
                    if (dt <= 0.0) continue;

                    if (k.InterpMode == ERichCurveInterpMode.RCIM_Cubic)
                        InterpolateCubicSegment(k, k2, dt, conversionFactor, timePts, valuePts);
                    else
                        InterpolateLinearSegment(k, k2, dt, conversionFactor, timePts, valuePts);
                }
            }

            // Post-infinity constant extrapolation: extend flat to 1.0 when last key is before it.
            // Mirrors UE's default PostInfinity = RCCE_Constant behaviour.
            if (timePts.Count > 0 && timePts[timePts.Count - 1] < 1.0)
            {
                timePts.Add(1.0);
                valuePts.Add(valuePts[valuePts.Count - 1]);
            }

            return (timePts.ToArray(), valuePts.ToArray());
        }

        /// <summary>
        /// Processes the dual-curve system used by ATT_ files:
        /// channel 0 = senior/frail (full range), channel 1 = elder/prime (0.75–1.0, NaN before 0.75).
        /// Channels 2 and 3 are ignored.
        /// </summary>
        public static List<(double[] Times, double[] Values)> ProcessDualCurves(
            FRichCurve[] floatCurves,
            double conversionFactor = 1.0)
        {
            var result = new List<(double[], double[])>();

            if (floatCurves == null || floatCurves.Length == 0)
                return result;

            // Channel 1: elder/prime — only valid at t >= 0.75, NaN before that
            double[] elderTimes  = Array.Empty<double>();
            double[] elderMasked = Array.Empty<double>();
            if (floatCurves.Length > 1 && floatCurves[1]?.Keys is { Length: > 0 } elderKeys)
            {
                var (times, values) = ProcessCurve(elderKeys, conversionFactor);
                if (times.Length > 0)
                {
                    var masked = new double[values.Length];
                    for (int i = 0; i < times.Length; i++)
                        masked[i] = times[i] < Config.ElderThreshold ? double.NaN : values[i];
                    elderTimes  = times;
                    elderMasked = masked;
                }
            }

            // Channel 0: senior/frail — full range
            if (floatCurves[0]?.Keys is { Length: > 0 } seniorKeys)
            {
                var (sTimes, sValues) = ProcessCurve(seniorKeys, conversionFactor);
                if (sTimes.Length > 0)
                    result.Add((sTimes, sValues));
            }

            if (elderTimes.Length > 0)
                result.Add((elderTimes, elderMasked));

            return result;
        }

        /// <summary>
        /// Multiplies the values of a processed curve by a scalar.
        /// Used for generating virtual attack curves.
        /// </summary>
        public static (double[] Times, double[] Values) ScaleCurve(
            double[] times, double[] values, double scale)
        {
            var scaledValues = new double[values.Length];
            for (int i = 0; i < values.Length; i++)
                scaledValues[i] = double.IsNaN(values[i]) ? double.NaN : values[i] * scale;
            return (times, scaledValues);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Segment interpolators
        // ─────────────────────────────────────────────────────────────────────

        private static void InterpolateLinearSegment(
            FRichCurveKey k, FRichCurveKey k2, double dt, double cf,
            List<double> timePts, List<double> valuePts)
        {
            double p1 = k.Value  * cf;
            double p2 = k2.Value * cf;
            for (int step = 1; step < Config.PointsPerSegment; step++)
            {
                double s = step / (double)Config.PointsPerSegment;
                timePts.Add(k.Time + s * dt);
                valuePts.Add(p1 + s * (p2 - p1));
            }
        }

        private static void InterpolateCubicSegment(
            FRichCurveKey k, FRichCurveKey k2, double dt, double cf,
            List<double> timePts, List<double> valuePts)
        {
            // ── Determine time-weight fractions (tP, tQ) ─────────────────────
            //
            // UE stores weighted-tangent handles as a direction (slope) + length
            // in the 2-D (time, value) plane.  The normalised handle direction is
            //   (cos θ, sin θ) = (1, m) / sqrt(1 + m²)
            // so the Bezier control-point offsets are:
            //   ΔT = TangentWeight · cos θ · dt      (time fraction of segment)
            //   ΔV = TangentWeight · cos θ · m · dt  (value offset)
            //
            // For unweighted tangents the standard one-third rule gives tP = tQ = 1/3.

            bool leaveWeighted =
                k.TangentWeightMode  == ERichCurveTangentWeightMode.RCTWM_WeightedLeave ||
                k.TangentWeightMode  == ERichCurveTangentWeightMode.RCTWM_WeightedBoth;
            bool arriveWeighted =
                k2.TangentWeightMode == ERichCurveTangentWeightMode.RCTWM_WeightedArrive ||
                k2.TangentWeightMode == ERichCurveTangentWeightMode.RCTWM_WeightedBoth;

            // Raw (unscaled) slopes are needed for the cosine normalisation;
            // conversionFactor only rescales values and does not change the
            // geometric direction of the handle in (time, value/cf) space.
            double rawM1 = k.LeaveTangent;
            double rawM2 = k2.ArriveTangent;

            double tP = leaveWeighted
                ? k.LeaveTangentWeight   / Math.Sqrt(1.0 + rawM1 * rawM1)
                : 1.0 / 3.0;

            double tQ = arriveWeighted
                ? k2.ArriveTangentWeight / Math.Sqrt(1.0 + rawM2 * rawM2)
                : 1.0 / 3.0;

            // ── Scaled values and tangents ────────────────────────────────────
            double v0 = k.Value  * cf;
            double v1 = k2.Value * cf;
            double m1 = rawM1 * cf;   // LeaveTangent,  scaled
            double m2 = rawM2 * cf;   // ArriveTangent, scaled

            // Bezier VALUE control points
            double bp1 = v0 + m1 * tP * dt;
            double bp2 = v1 - m2 * tQ * dt;

            // ── Time-parameterisation cubic ───────────────────────────────────
            //
            // The Bezier TIME control points normalised to [0,1] are tP and 1-tQ.
            // Expanding alpha = B(u) = 3(1-u)²u·tP + 3(1-u)u²(1-tQ) + u³ gives:
            //   A·u³ + B·u² + C·u = alpha
            //   A = 3·tP + 3·tQ - 2
            //   B = 3    - 3·tQ - 6·tP
            //   C = 3·tP
            //
            // For unweighted (tP = tQ = 1/3): A=0, B=0, C=1 → u = alpha  ✓
            // (identical to plain Hermite)

            double A = 3.0 * tP + 3.0 * tQ - 2.0;
            double B = 3.0 - 3.0 * tQ - 6.0 * tP;
            double C = 3.0 * tP;

            for (int step = 1; step < Config.PointsPerSegment; step++)
            {
                double alpha = step / (double)Config.PointsPerSegment;
                double u     = SolveCubicBezierU(A, B, C, alpha);
                double val   = BezierInterp(v0, bp1, bp2, v1, u);
                timePts.Add(k.Time + alpha * dt);
                valuePts.Add(val);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Bezier helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Solves A·u³ + B·u² + C·u = alpha for u ∈ [0,1] via Newton-Raphson.
        /// The root is guaranteed to exist because f(0) = –alpha ≤ 0 and f(1) = 1–alpha ≥ 0.
        /// </summary>
        private static double SolveCubicBezierU(double A, double B, double C, double alpha)
        {
            // Near-linear case (standard Hermite, tP = tQ = 1/3)
            if (Math.Abs(A) < 1e-12 && Math.Abs(B) < 1e-12)
                return Math.Abs(C) > 1e-12 ? alpha / C : alpha;

            double u = alpha; // alpha is always a good initial guess
            for (int iter = 0; iter < 32; iter++)
            {
                double u2 = u * u;
                double f  = A * u2 * u + B * u2 + C * u - alpha;
                double df = 3.0 * A * u2 + 2.0 * B * u + C;
                if (Math.Abs(df) < 1e-14) break;
                double du = f / df;
                u -= du;
                if (Math.Abs(du) < 1e-12) break;
            }
            return Math.Max(0.0, Math.Min(1.0, u));
        }

        /// <summary>
        /// Cubic Bezier value interpolation given four control-point values and parameter u.
        /// </summary>
        private static double BezierInterp(double p0, double p1, double p2, double p3, double u)
        {
            double omu = 1.0 - u;
            return omu * omu * omu * p0
                 + 3.0 * omu * omu * u * p1
                 + 3.0 * omu * u   * u * p2
                 + u   * u   * u   * p3;
        }
    }
}
