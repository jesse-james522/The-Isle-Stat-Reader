using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TheIsleStatReader.Core;

namespace TheIsleStatReader.UI
{
    /// <summary>
    /// All-dinosaur comparison chart. For every dinosaur, shows the main stats
    /// (weight, speeds, damage, survival times, rest regen) at four growth
    /// points: juvenile (0.25), adult (0.75), peak (wherever the curve actually
    /// peaks), and 1.0 split into "Frail" (elder curve) and "Prime" (senior curve).
    ///
    /// Survival stats (starve/dehydrate/underwater), sprint duration and rest
    /// regen are growth-independent, so they appear in their own "Scalar Stats"
    /// group at the right. Sprint duration in particular is only really accurate
    /// at adult — the header includes a disclaimer to that effect.
    /// </summary>
    internal sealed class SummaryWindow : Form
    {
        private readonly DataGridView _grid;
        private readonly ToolStripStatusLabel _status;
        private readonly ComboBox _statCombo;

        // Which stat row is currently being displayed. Populated in OnLoad.
        private List<DinoSummary> _summaries = new();
        private readonly List<string> _statNames = new();

        public SummaryWindow()
        {
            Text = "All-Species Comparison Chart";
            Size = new Size(1400, 720);
            MinimumSize = new Size(1000, 500);
            StartPosition = FormStartPosition.CenterParent;

            // ---- Top bar: stat picker + disclaimer ----
            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(8, 6, 8, 6),
                BackColor = Color.FromArgb(240, 245, 255)
            };

            var statLabel = new Label
            {
                Text = "Stat:",
                AutoSize = true,
                Left = 8,
                Top = 10
            };

            _statCombo = new ComboBox
            {
                Left = 50,
                Top = 6,
                Width = 180,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _statCombo.SelectedIndexChanged += (_, _) => RebuildGrid();

            var disclaimer = new Label
            {
                Text = "Sprint duration / rest regen are shown at adult growth only.",
                AutoSize = true,
                Left = 250,
                Top = 10,
                ForeColor = Color.DimGray,
                Font = new Font(Font, FontStyle.Italic)
            };

            topPanel.Controls.Add(statLabel);
            topPanel.Controls.Add(_statCombo);
            topPanel.Controls.Add(disclaimer);

            // ---- Grid ----
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(220, 220, 220),
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                EnableHeadersVisualStyles = false,
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(248, 248, 252)
                },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(210, 220, 240),
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    WrapMode = DataGridViewTriState.True
                }
            };
            _grid.ColumnHeadersHeight = 44;

            // ---- Status strip ----
            var strip = new StatusStrip();
            _status = new ToolStripStatusLabel { Text = "Loading…" };
            strip.Items.Add(_status);

            Controls.Add(_grid);
            Controls.Add(topPanel);
            Controls.Add(strip);

            Load += async (_, _) => await LoadSummariesAsync();
        }

        private async System.Threading.Tasks.Task LoadSummariesAsync()
        {
            _status.Text = "Computing summaries…";
            _grid.Enabled = false;

            try
            {
                var summaries = await System.Threading.Tasks.Task.Run(() =>
                    DataLoader.Instance.BuildAllSummaries());

                _summaries = summaries;

                // Collect every stat name we saw, preserving the order from
                // the first dino that supplied each stat.
                _statNames.Clear();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sum in summaries)
                    foreach (var stat in sum.Stats)
                        if (seen.Add(stat.Name))
                            _statNames.Add(stat.Name);

                // Always include the virtual "survival" view, which collapses
                // the scalar survival stats into one table.
                const string survivalView = "Survival & Stamina";
                _statCombo.Items.Clear();
                _statCombo.Items.Add(survivalView);
                foreach (var n in _statNames)
                    _statCombo.Items.Add(n);
                _statCombo.SelectedIndex = 0;

                _status.Text = $"Loaded {summaries.Count} species.";
            }
            catch (Exception ex)
            {
                _status.Text = $"Error: {ex.Message}";
                MessageBox.Show(this, ex.ToString(), "Summary load error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _grid.Enabled = true;
            }
        }

        /// <summary>
        /// Rebuilds the grid for the currently-selected stat (or the survival view).
        /// </summary>
        private void RebuildGrid()
        {
            _grid.Columns.Clear();
            _grid.Rows.Clear();

            if (_statCombo.SelectedItem is not string selected)
                return;

            if (selected == "Survival & Stamina")
            {
                BuildSurvivalGrid();
            }
            else
            {
                BuildStatGrid(selected);
            }

            _grid.ClearSelection();
        }

        /// <summary>
        /// Columns: Species, Juvenile, Adult, Peak, Peak At, Frail, Prime.
        /// One row per dinosaur, ordered alphabetically (same as summaries list).
        /// </summary>
        private void BuildStatGrid(string statName)
        {
            _grid.Columns.Add(MakeColumn("Species", "Species", 120, alignLeft: true));
            _grid.Columns.Add(MakeColumn("Juvenile", "25%\nJuvenile", 75));
            _grid.Columns.Add(MakeColumn("Adult", "75%\nAdult", 75));
            _grid.Columns.Add(MakeColumn("Peak", "Peak", 75));
            _grid.Columns.Add(MakeColumn("PeakAt", "Peak at\n(growth)", 75));
            _grid.Columns.Add(MakeColumn("Frail", "100%\nFrail", 75));
            _grid.Columns.Add(MakeColumn("Prime", "100%\nPrime", 75));

            foreach (var sum in _summaries)
            {
                var row = sum.Stats.FirstOrDefault(
                    s => s.Name.Equals(statName, StringComparison.OrdinalIgnoreCase));
                if (row == null) continue;

                int idx = _grid.Rows.Add(
                    sum.DinoName,
                    FormatValue(row.Juvenile, row.Format, row.Unit),
                    FormatValue(row.Adult,    row.Format, row.Unit),
                    FormatValue(row.Peak,     row.Format, row.Unit),
                    FormatGrowth(row.PeakAt),
                    FormatValue(row.Frail,    row.Format, row.Unit),
                    FormatValue(row.Prime,    row.Format, row.Unit));

                // Colour-code carnivores vs herbivores (rough split using the
                // existing aquatic-dino set isn't useful, so just alternate by
                // index).  Nothing fancy.
                if (idx % 2 == 0)
                    _grid.Rows[idx].DefaultCellStyle.BackColor = Color.FromArgb(252, 252, 255);
            }

            _status.Text = $"{statName} — {_grid.Rows.Count} species.";
        }

        /// <summary>
        /// Scalar survival &amp; stamina table: starve, dehydrate, underwater,
        /// sprint duration, rest time, rest regen rate.
        /// </summary>
        private void BuildSurvivalGrid()
        {
            _grid.Columns.Add(MakeColumn("Species", "Species", 130, alignLeft: true));
            _grid.Columns.Add(MakeColumn("Starve", "Starve\n(min)", 70));
            _grid.Columns.Add(MakeColumn("Dehydrate", "Dehydrate\n(min)", 75));
            _grid.Columns.Add(MakeColumn("Underwater", "Underwater\n(sec)", 85));
            _grid.Columns.Add(MakeColumn("Sprint", "Sprint dur.\n(sec)", 85));
            _grid.Columns.Add(MakeColumn("RestFull", "Rest→Full\n(sec)", 85));
            _grid.Columns.Add(MakeColumn("RestRate", "Rest regen\n(%/s)", 85));

            foreach (var sum in _summaries)
            {
                int idx = _grid.Rows.Add(
                    sum.DinoName,
                    FormatScalar(sum.TimeToStarveMin,    "F0"),
                    FormatScalar(sum.TimeToDehydrateMin, "F0"),
                    FormatScalar(sum.TimeUnderwaterSec,  "F0"),
                    FormatScalar(sum.SprintDurationSec,  "F0"),
                    FormatScalar(sum.RestToFullSec,      "F0"),
                    FormatScalar(sum.RestRegenPerSec,    "F3"));

                if (idx % 2 == 0)
                    _grid.Rows[idx].DefaultCellStyle.BackColor = Color.FromArgb(252, 252, 255);
            }

            _status.Text = $"Survival stats — {_grid.Rows.Count} species.";
        }

        // ------------------------------------------------------------------
        // Formatting helpers
        // ------------------------------------------------------------------

        private static DataGridViewTextBoxColumn MakeColumn(
            string name, string header, int width, bool alignLeft = false)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width,
                SortMode = DataGridViewColumnSortMode.Automatic,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = alignLeft
                        ? DataGridViewContentAlignment.MiddleLeft
                        : DataGridViewContentAlignment.MiddleRight,
                    Padding = new Padding(4, 0, 4, 0)
                }
            };
        }

        private static string FormatValue(double v, string format, string unit)
        {
            if (double.IsNaN(v)) return "—";
            string num = v.ToString(format);
            return string.IsNullOrEmpty(unit) ? num : $"{num} {unit}";
        }

        private static string FormatGrowth(double v)
        {
            if (double.IsNaN(v)) return "—";
            return $"{v * 100.0:F1}%";
        }

        private static string FormatScalar(double v, string format)
        {
            return double.IsNaN(v) ? "—" : v.ToString(format);
        }
    }
}
