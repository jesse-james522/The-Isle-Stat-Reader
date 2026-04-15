using System;
using System.Collections.Generic;
using CUE4Parse.UE4.Objects.Engine.Curves;

namespace TheIsleStatReader.Core
{
    /// <summary>
    /// Processes FRichCurve key data into sampled (time, value) arrays using
    /// cubic Hermite or linear interpolation per segment, matching the original
    /// Python implementation's algorithm.
    /// </summary>
    internal static class CurveProcessor
    {
        /// <summary>
        /// Processes a list of FRichCurveKey values into sampled time/value arrays.
        /// The InterpMode on each key applies to the OUTGOING segment from that key.
        /// </summary>
        /// <param name="keys">Strongly-typed curve keys from CUE4Parse.</param>
        /// <param name="conversionFactor">Multiply all values by this factor (e.g., speed → km/h).</param>
        public static (double[] Times, double[] Values) ProcessCurve(
            IReadOnlyList<FRichCurveKey> keys,
            double conversionFactor = 1.0)
        {
            if (keys == null || keys.Count == 0)
                return (Array.Empty<double>(), Array.Empty<double>());

            // Copy + sort by time (defensive — editor can save out-of-order keys)
            var parsed = new List<FRichCurveKey>(keys);
            parsed.Sort((a, b) => a.Time.CompareTo(b.Time));

            var timePts = new List<double>();
            var valuePts = new List<double>();

            // Pre-extrapolation: if first key is at t > 0, prepend t=0 with the first key's value
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
                    var k2 = parsed[i + 1];
                    double dt = k2.Time - k.Time;
                    if (dt <= 0.0) continue;

                    bool isCubic = k.InterpMode == ERichCurveInterpMode.RCIM_Cubic;

                    if (isCubic)
                    {
                        // Cubic Hermite interpolation
                        // m1 = LeaveTangent of k (outgoing)
                        // m2 = ArriveTangent of k2 (incoming)
                        double m1 = k.LeaveTangent * conversionFactor;
                        double m2 = k2.ArriveTangent * conversionFactor;
                        double p1 = k.Value * conversionFactor;
                        double p2 = k2.Value * conversionFactor;

                        for (int step = 1; step < Config.PointsPerSegment; step++)
                        {
                            double s = step / (double)Config.PointsPerSegment;
                            double s2 = s * s;
                            double s3 = s2 * s;

                            double h00 = 2.0 * s3 - 3.0 * s2 + 1.0;
                            double h10 = s3 - 2.0 * s2 + s;
                            double h01 = -2.0 * s3 + 3.0 * s2;
                            double h11 = s3 - s2;

                            double t = k.Time + s * dt;
                            double v = h00 * p1 + h10 * m1 * dt + h01 * p2 + h11 * m2 * dt;

                            timePts.Add(t);
                            valuePts.Add(v);
                        }
                    }
                    else
                    {
                        // Linear interpolation (also used for RCIM_Linear, RCIM_Constant, RCIM_None)
                        double p1 = k.Value * conversionFactor;
                        double p2 = k2.Value * conversionFactor;

                        for (int step = 1; step < Config.PointsPerSegment; step++)
                        {
                            double s = step / (double)Config.PointsPerSegment;
                            double t = k.Time + s * dt;
                            double v = p1 + s * (p2 - p1);
                            timePts.Add(t);
                            valuePts.Add(v);
                        }
                    }
                }
            }

            // Post-infinity constant extrapolation: if the last key is before 1.0,
            // extend flat to 1.0 so the curve reaches full growth on the x-axis.
            // This mirrors UE's default PostInfinity = RCCE_Constant behaviour.
            if (timePts.Count > 0 && timePts[timePts.Count - 1] < 1.0)
            {
                timePts.Add(1.0);
                valuePts.Add(valuePts[valuePts.Count - 1]);
            }

            return (timePts.ToArray(), valuePts.ToArray());
        }

        /// <summary>
        /// Processes the dual-curve system used by ATT_ files:
        /// channel 0 = senior (full range), channel 1 = elder (0.75–1.0, NaN before 0.75).
        /// Channels 2 and 3 are ignored.
        /// </summary>
        public static List<(double[] Times, double[] Values)> ProcessDualCurves(
            FRichCurve[] floatCurves,
            double conversionFactor = 1.0)
        {
            var result = new List<(double[], double[])>();

            if (floatCurves == null || floatCurves.Length == 0)
                return result;

            // Channel 0: senior — full range
            if (floatCurves.Length > 0 && floatCurves[0]?.Keys is { Length: > 0 } seniorKeys)
            {
                var (times, values) = ProcessCurve(seniorKeys, conversionFactor);
                if (times.Length > 0)
                    result.Add((times, values));
            }

            // Channel 1: elder — only show t >= 0.75, NaN for t < 0.75
            if (floatCurves.Length > 1 && floatCurves[1]?.Keys is { Length: > 0 } elderKeys)
            {
                var (times, values) = ProcessCurve(elderKeys, conversionFactor);
                if (times.Length > 0)
                {
                    var maskedValues = new double[values.Length];
                    for (int i = 0; i < times.Length; i++)
                        maskedValues[i] = times[i] < Config.ElderThreshold ? double.NaN : values[i];
                    result.Add((times, maskedValues));
                }
            }

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
    }
}
