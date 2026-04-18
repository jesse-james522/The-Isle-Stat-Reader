using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace TheIsleStatReader.UI
{
    /// <summary>
    /// An independent chart window that can hold one or more LineSeries curves.
    /// Multiple instances are supported simultaneously.
    /// </summary>
    internal sealed class PlotWindow : Form
    {
        // ------------------------------------------------------------------
        // OxyPlot
        // ------------------------------------------------------------------
        private readonly PlotView _plotView;
        private readonly PlotModel _model;
        private readonly LinearAxis _xAxis;
        private readonly LinearAxis _yAxis;

        // ------------------------------------------------------------------
        // Growth-line annotations (created once, visibility toggled)
        // ------------------------------------------------------------------
        private readonly LineAnnotation _elderLine;
        private readonly LineAnnotation _subadultLine;
        private readonly LineAnnotation _juviLine;

        // ------------------------------------------------------------------
        // Bottom control panel
        // ------------------------------------------------------------------
        private readonly CheckBox _elderCheck;
        private readonly CheckBox _subadultCheck;
        private readonly CheckBox _juviCheck;
        private readonly TextBox _xTickBox;
        private readonly TextBox _yTickBox;
        private readonly Panel _curveCheckPanel;   // scrollable host
        private readonly FlowLayoutPanel _curveCheckFlow;

        // ------------------------------------------------------------------
        // Curve tracking
        // ------------------------------------------------------------------
        private readonly List<LineSeries> _series = new();
        private readonly List<CheckBox> _curveCheckBoxes = new();
        private readonly List<string> _curveLabels = new();

        // One entry per AddCurves call — tracks paired series + swap state.
        private sealed record CurveGroup(
            LineSeries PrimeSeries,
            LineSeries? FrailSeries,
            CheckBox PrimeCheck,
            CheckBox? FrailCheck,
            Button? SwapButton,
            string? SwapKey);
        private readonly List<CurveGroup> _curveGroups = new();

        // Y-axis labels — union of all distinct labels added so far
        private readonly HashSet<string> _yLabels = new(StringComparer.OrdinalIgnoreCase);

        // ------------------------------------------------------------------
        // Constructor
        // ------------------------------------------------------------------
        public PlotWindow(string title)
        {
            Text = title;
            Size = new Size(900, 650);
            MinimumSize = new Size(700, 500);

            // ================================================================
            // Build OxyPlot model
            // ================================================================
            _model = new PlotModel
            {
                Background = OxyColors.White,
                PlotAreaBorderColor = OxyColors.Gray
            };

            // OxyPlot 2.x uses a separate Legend object
            var legend = new Legend
            {
                LegendPlacement = LegendPlacement.Outside,
                LegendPosition = LegendPosition.RightTop,
                LegendBackground = OxyColor.FromArgb(220, 255, 255, 255),
                LegendBorder = OxyColors.Gray
            };
            _model.Legends.Add(legend);

            _xAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Growth",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromArgb(40, 0, 0, 0),
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = OxyColor.FromArgb(20, 0, 0, 0),
                StringFormat = "P0",   // will update when curves are added
                MajorStep = 0.1
            };

            _yAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Value",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromArgb(40, 0, 0, 0),
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = OxyColor.FromArgb(20, 0, 0, 0),
                MajorStep = 5.0
            };

            _model.Axes.Add(_xAxis);
            _model.Axes.Add(_yAxis);

            // ---- Growth lines (always present but may be hidden) ----
            _elderLine = new LineAnnotation
            {
                Type = LineAnnotationType.Vertical,
                X = Config.ElderThreshold,
                Color = OxyColors.Red,
                LineStyle = LineStyle.Dash,
                StrokeThickness = 1.5,
                Text = "Elder 75%",
                TextColor = OxyColors.Red,
                FontSize = 10
            };

            _subadultLine = new LineAnnotation
            {
                Type = LineAnnotationType.Vertical,
                X = Config.SubadultThreshold,
                Color = OxyColors.RoyalBlue,
                LineStyle = LineStyle.Dash,
                StrokeThickness = 1.5,
                Text = "Subadult 50%",
                TextColor = OxyColors.RoyalBlue,
                FontSize = 10
            };

            _juviLine = new LineAnnotation
            {
                Type = LineAnnotationType.Vertical,
                X = Config.JuvenileThreshold,
                Color = OxyColors.ForestGreen,
                LineStyle = LineStyle.Dash,
                StrokeThickness = 1.5,
                Text = "Juvi 25%",
                TextColor = OxyColors.ForestGreen,
                FontSize = 10
            };

            _model.Annotations.Add(_elderLine);
            _model.Annotations.Add(_subadultLine);
            _model.Annotations.Add(_juviLine);

            // ================================================================
            // PlotView
            // ================================================================
            _plotView = new PlotView
            {
                Dock = DockStyle.Fill,
                Model = _model
            };

            // ================================================================
            // Bottom control panel
            // ================================================================
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 130,
                Padding = new Padding(6, 4, 6, 4),
                BackColor = SystemColors.Control
            };

            // Row 1: growth-line checkboxes
            var row1 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 30,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false
            };

            var growthLabel = new Label
            {
                Text = "Growth Lines:",
                AutoSize = true,
                Margin = new Padding(0, 6, 4, 0)
            };

            _elderCheck = new CheckBox
            {
                Text = "Elder 75%",
                Checked = true,
                AutoSize = true,
                Margin = new Padding(0, 4, 8, 0),
                ForeColor = Color.Red
            };
            _elderCheck.CheckedChanged += (s, e) => UpdateGrowthLines();

            _subadultCheck = new CheckBox
            {
                Text = "Subadult 50%",
                Checked = true,
                AutoSize = true,
                Margin = new Padding(0, 4, 8, 0),
                ForeColor = Color.RoyalBlue
            };
            _subadultCheck.CheckedChanged += (s, e) => UpdateGrowthLines();

            _juviCheck = new CheckBox
            {
                Text = "Juvi 25%",
                Checked = true,
                AutoSize = true,
                Margin = new Padding(0, 4, 8, 0),
                ForeColor = Color.ForestGreen
            };
            _juviCheck.CheckedChanged += (s, e) => UpdateGrowthLines();

            row1.Controls.Add(growthLabel);
            row1.Controls.Add(_elderCheck);
            row1.Controls.Add(_subadultCheck);
            row1.Controls.Add(_juviCheck);

            // Row 2: tick interval inputs
            var row2 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 30,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false
            };

            row2.Controls.Add(new Label { Text = "X Tick:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            _xTickBox = new TextBox { Width = 60, Margin = new Padding(0, 4, 10, 0) };
            row2.Controls.Add(_xTickBox);

            row2.Controls.Add(new Label { Text = "Y Tick:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            _yTickBox = new TextBox { Width = 60, Margin = new Padding(0, 4, 10, 0) };
            row2.Controls.Add(_yTickBox);

            var applyBtn = new Button
            {
                Text = "Apply",
                Width = 60,
                Height = 26,
                Margin = new Padding(0, 2, 0, 0)
            };
            applyBtn.Click += (s, e) => ApplyManualTicks();
            row2.Controls.Add(applyBtn);

            // Row 3: curve checkboxes + remove button
            var row3 = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
            };

            _curveCheckFlow = new FlowLayoutPanel
            {
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Dock = DockStyle.Fill
            };

            _curveCheckPanel = new Panel
            {
                Left = 0,
                Top = 0,
                Width = 700,
                Height = 60,
                Dock = DockStyle.Left,
                AutoScroll = true
            };
            _curveCheckPanel.Controls.Add(_curveCheckFlow);
            _curveCheckFlow.Dock = DockStyle.Fill;

            var removeBtn = new Button
            {
                Text = "Remove Selected",
                Dock = DockStyle.Right,
                Width = 120,
                Height = 28
            };
            removeBtn.Click += (s, e) => RemoveSelectedCurves();

            row3.Controls.Add(_curveCheckPanel);
            row3.Controls.Add(removeBtn);

            // Assemble bottom panel (add in reverse due to Dock.Top stacking)
            bottomPanel.Controls.Add(row3);
            bottomPanel.Controls.Add(row2);
            bottomPanel.Controls.Add(row1);

            // ================================================================
            // Add controls to form
            // ================================================================
            Controls.Add(_plotView);
            Controls.Add(bottomPanel);

            // Initial growth-line visibility
            UpdateGrowthLines();
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Adds one or two curves to the plot.
        /// </summary>
        /// <param name="curves">Prime-first curve list from
        ///     <see cref="Core.DataLoader.GetCurveData"/> or virtual attack data.</param>
        /// <param name="label">Display name base (e.g. "Rex SprintSpeed").</param>
        /// <param name="yLabel">Y-axis unit label.</param>
        /// <param name="primeLabel">Channel label for curve[0], e.g. "Senior" or "Elder".
        ///     Pass empty/null for single-channel curves.</param>
        /// <param name="frailLabel">Channel label for curve[1], e.g. "Elder" or "Senior".</param>
        /// <param name="swapKey">Config key used to toggle the channel override
        ///     ("{DinoName}|{CurveSuffix}"). Null disables the Swap button.</param>
        public void AddCurves(
            List<(double[] Times, double[] Values)> curves,
            string label,
            string yLabel,
            string primeLabel = "",
            string frailLabel = "",
            string? swapKey = null)
        {
            if (curves == null || curves.Count == 0)
                return;

            if (_yLabels.Add(yLabel))
                _yAxis.Title = string.Join(" / ", _yLabels);

            var colour = PickNextColour();
            bool isDual = curves.Count >= 2
                && !string.IsNullOrEmpty(primeLabel)
                && !string.IsNullOrEmpty(frailLabel);

            LineSeries? primeSeries = null;
            LineSeries? frailSeries = null;
            CheckBox? primeCheck  = null;
            CheckBox? frailCheck  = null;

            for (int i = 0; i < Math.Min(curves.Count, 2); i++)
            {
                var (times, values) = curves[i];
                if (times.Length == 0) continue;

                string channelSuffix = isDual
                    ? (i == 0 ? $" ({primeLabel})" : $" ({frailLabel})")
                    : "";
                string seriesLabel = label + channelSuffix;

                var ls = new LineSeries
                {
                    Title                     = seriesLabel,
                    Color                     = colour,
                    StrokeThickness           = i == 0 ? 2.0 : 1.5,
                    LineStyle                 = i == 0 ? LineStyle.Solid : LineStyle.Dash,
                    TrackerFormatString       = "{0}\nGrowth: {2:F4}\n{3}: {4:F2}",
                    CanTrackerInterpolatePoints = true
                };

                for (int j = 0; j < times.Length; j++)
                    ls.Points.Add(double.IsNaN(values[j])
                        ? DataPoint.Undefined
                        : new DataPoint(times[j], values[j]));

                _model.Series.Add(ls);
                _series.Add(ls);
                _curveLabels.Add(seriesLabel);

                var cb = new CheckBox
                {
                    Text = seriesLabel,
                    Checked = false,
                    AutoSize = true,
                    Margin = new Padding(4, 2, 4, 2)
                };
                _curveCheckFlow.Controls.Add(cb);
                _curveCheckBoxes.Add(cb);

                if (i == 0) { primeSeries = ls; primeCheck = cb; }
                else        { frailSeries = ls; frailCheck = cb; }
            }

            // Swap button — only for dual-curve groups with a valid swap key.
            Button? swapBtn = null;
            if (isDual && swapKey != null && primeSeries != null && frailSeries != null)
            {
                bool isCurrentlySwapped =
                    TheIsleStatReader.Config.ChannelSwaps.TryGetValue(swapKey, out bool sv) && sv;

                swapBtn = new Button
                {
                    Text        = isCurrentlySwapped ? "⇅ Swapped" : "⇅ Swap",
                    AutoSize    = true,
                    Margin      = new Padding(2, 2, 8, 2),
                    BackColor   = isCurrentlySwapped
                        ? System.Drawing.Color.FromArgb(255, 220, 180)
                        : System.Drawing.SystemColors.Control,
                    Tag         = swapKey
                };

                // Capture references for the click handler.
                var capturedPrimeSeries  = primeSeries;
                var capturedFrailSeries  = frailSeries;
                var capturedPrimeCheck   = primeCheck!;
                var capturedFrailCheck   = frailCheck!;
                var capturedLabel        = label;
                var capturedPrimeLabel   = primeLabel;
                var capturedFrailLabel   = frailLabel;
                var capturedBtn          = swapBtn;
                var capturedKey          = swapKey;

                swapBtn.Click += (_, _) =>
                {
                    // Toggle override in Config.
                    bool wasSwapped =
                        TheIsleStatReader.Config.ChannelSwaps.TryGetValue(capturedKey, out bool cur) && cur;
                    TheIsleStatReader.Config.ChannelSwaps[capturedKey] = !wasSwapped;
                    TheIsleStatReader.Config.Save();

                    bool nowSwapped = !wasSwapped;

                    // Flip labels in the plot.
                    string newPrimeSuffix = nowSwapped
                        ? $" ({capturedFrailLabel})" : $" ({capturedPrimeLabel})";
                    string newFrailSuffix = nowSwapped
                        ? $" ({capturedPrimeLabel})" : $" ({capturedFrailLabel})";

                    capturedPrimeSeries.Title      = capturedLabel + newPrimeSuffix;
                    capturedFrailSeries.Title      = capturedLabel + newFrailSuffix;
                    capturedPrimeCheck.Text        = capturedPrimeSeries.Title;
                    capturedFrailCheck.Text        = capturedFrailSeries.Title;

                    // Flip line styles so the new prime is solid.
                    capturedPrimeSeries.LineStyle      = LineStyle.Solid;
                    capturedPrimeSeries.StrokeThickness = 2.0;
                    capturedFrailSeries.LineStyle      = LineStyle.Dash;
                    capturedFrailSeries.StrokeThickness = 1.5;

                    capturedBtn.Text      = nowSwapped ? "⇅ Swapped" : "⇅ Swap";
                    capturedBtn.BackColor = nowSwapped
                        ? System.Drawing.Color.FromArgb(255, 220, 180)
                        : System.Drawing.SystemColors.Control;

                    _model.InvalidatePlot(true);
                };

                _curveCheckFlow.Controls.Add(swapBtn);
            }

            _curveGroups.Add(new CurveGroup(
                primeSeries ?? new LineSeries(),
                frailSeries,
                primeCheck ?? new CheckBox(),
                frailCheck,
                swapBtn,
                swapKey));

            RecalculateAxisTicks();
            UpdateXAxisFormat();
            _model.InvalidatePlot(true);
        }

        /// <summary>
        /// Toggles growth-line annotation visibility based on checkboxes.
        /// </summary>
        public void UpdateGrowthLines()
        {
            _elderLine.Color = _elderCheck.Checked
                ? OxyColors.Red
                : OxyColors.Transparent;
            _elderLine.TextColor = _elderLine.Color;

            _subadultLine.Color = _subadultCheck.Checked
                ? OxyColors.RoyalBlue
                : OxyColors.Transparent;
            _subadultLine.TextColor = _subadultLine.Color;

            _juviLine.Color = _juviCheck.Checked
                ? OxyColors.ForestGreen
                : OxyColors.Transparent;
            _juviLine.TextColor = _juviLine.Color;

            _model.InvalidatePlot(false);
        }

        /// <summary>
        /// Removes all series whose checkbox is checked.
        /// Also removes the associated Swap button when an entire dual-curve
        /// group is removed.
        /// </summary>
        public void RemoveSelectedCurves()
        {
            var toRemove = new List<int>();
            for (int i = 0; i < _curveCheckBoxes.Count; i++)
                if (_curveCheckBoxes[i].Checked) toRemove.Add(i);

            // Remove swap buttons for any group whose prime or frail series is removed.
            var removedSeries = new HashSet<LineSeries>(toRemove.Select(i => _series[i]));
            foreach (var g in _curveGroups)
            {
                bool primeRemoved = removedSeries.Contains(g.PrimeSeries);
                bool frailRemoved = g.FrailSeries != null && removedSeries.Contains(g.FrailSeries);
                if ((primeRemoved || frailRemoved) && g.SwapButton != null)
                    _curveCheckFlow.Controls.Remove(g.SwapButton);
            }
            _curveGroups.RemoveAll(g =>
                removedSeries.Contains(g.PrimeSeries) ||
                (g.FrailSeries != null && removedSeries.Contains(g.FrailSeries)));

            for (int i = toRemove.Count - 1; i >= 0; i--)
            {
                int idx = toRemove[i];
                _model.Series.Remove(_series[idx]);
                _curveCheckFlow.Controls.Remove(_curveCheckBoxes[idx]);
                _series.RemoveAt(idx);
                _curveCheckBoxes.RemoveAt(idx);
                _curveLabels.RemoveAt(idx);
            }

            RecalculateAxisTicks();
            _model.InvalidatePlot(true);
        }

        // ------------------------------------------------------------------
        // Tick management
        // ------------------------------------------------------------------

        private void RecalculateAxisTicks()
        {
            if (_series.Count == 0) return;

            // X axis: find max time across all series
            double maxTime = _series
                .SelectMany(s => s.Points)
                .Where(p => !double.IsNaN(p.Y))
                .Select(p => p.X)
                .DefaultIfEmpty(1.0)
                .Max();

            double xStep = CalculateSafeStep(maxTime, 0.0);
            _xAxis.MajorStep = xStep;
            _xTickBox.Text = xStep.ToString("G4");

            // Y axis: find value range
            var yValues = _series
                .SelectMany(s => s.Points)
                .Where(p => !double.IsNaN(p.Y))
                .Select(p => p.Y)
                .ToList();

            if (yValues.Count > 0)
            {
                double yMin = yValues.Min();
                double yMax = yValues.Max();
                double yRange = yMax - yMin;
                double yStep = AutoYStep(yRange);
                _yAxis.MajorStep = yStep;
                _yTickBox.Text = yStep.ToString("G4");
            }

            UpdateXAxisFormat();
        }

        private void ApplyManualTicks()
        {
            if (double.TryParse(_xTickBox.Text, out double xStep) && xStep > 0)
            {
                // Safety cap: never more than 20 ticks on visible range
                double maxTime = _series.Count > 0
                    ? _series.SelectMany(s => s.Points).Where(p => !double.IsNaN(p.Y))
                        .Select(p => p.X).DefaultIfEmpty(1.0).Max()
                    : 1.0;
                xStep = SafeCapStep(xStep, 0.0, maxTime);
                _xAxis.MajorStep = xStep;
                _xTickBox.Text = xStep.ToString("G4");
            }

            if (double.TryParse(_yTickBox.Text, out double yStep) && yStep > 0)
            {
                double yMin = _series.Count > 0
                    ? _series.SelectMany(s => s.Points).Where(p => !double.IsNaN(p.Y))
                        .Select(p => p.Y).DefaultIfEmpty(0.0).Min()
                    : 0.0;
                double yMax = _series.Count > 0
                    ? _series.SelectMany(s => s.Points).Where(p => !double.IsNaN(p.Y))
                        .Select(p => p.Y).DefaultIfEmpty(10.0).Max()
                    : 10.0;
                yStep = SafeCapStep(yStep, yMin, yMax);
                _yAxis.MajorStep = yStep;
                _yTickBox.Text = yStep.ToString("G4");
            }

            _model.InvalidatePlot(true);
        }

        private void UpdateXAxisFormat()
        {
            double maxTime = _series.Count > 0
                ? _series.SelectMany(s => s.Points)
                    .Where(p => !double.IsNaN(p.Y))
                    .Select(p => p.X)
                    .DefaultIfEmpty(1.0)
                    .Max()
                : 1.0;

            _xAxis.StringFormat = maxTime <= 1.0 ? "P0" : "F2";
        }

        // ------------------------------------------------------------------
        // Static helpers
        // ------------------------------------------------------------------

        private static double AutoYStep(double range)
        {
            if (range <= 10.0) return 1.0;
            if (range <= 100.0) return 5.0;
            if (range <= 1000.0) return 50.0;
            return 500.0;
        }

        private static double CalculateSafeStep(double maxVal, double minVal)
        {
            double range = maxVal - minVal;
            if (range <= 0) range = 1.0;
            double rawStep = range / 10.0;  // aim for ~10 ticks
            return SafeCapStep(rawStep, minVal, maxVal);
        }

        private static double SafeCapStep(double step, double minVal, double maxVal)
        {
            double range = maxVal - minVal;
            if (range <= 0) return step > 0 ? step : 1.0;
            // Never more than 20 ticks
            double minStep = range / 20.0;
            return Math.Max(step, minStep);
        }

        // Colour palette cycling
        private static readonly OxyColor[] ColourPalette =
        {
            OxyColors.SteelBlue,
            OxyColors.Tomato,
            OxyColors.SeaGreen,
            OxyColors.DarkOrange,
            OxyColors.MediumPurple,
            OxyColors.DeepSkyBlue,
            OxyColors.Crimson,
            OxyColors.Olive,
            OxyColors.Teal,
            OxyColors.Chocolate
        };

        private int _colourIndex;

        private OxyColor PickNextColour()
        {
            var c = ColourPalette[_colourIndex % ColourPalette.Length];
            _colourIndex++;
            return c;
        }
    }
}
