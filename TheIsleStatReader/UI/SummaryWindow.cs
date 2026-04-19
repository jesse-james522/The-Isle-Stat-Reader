using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using TheIsleStatReader.Core;

namespace TheIsleStatReader.UI
{
    /// <summary>
    /// All-dinosaur comparison chart.  Columns store raw <see cref="double"/> values
    /// so the built-in sort is numeric; display formatting (units, NaN → "—") is
    /// applied by the <c>CellFormatting</c> event.  CSV export omits units and uses
    /// plain numeric strings.
    /// </summary>
    internal sealed class SummaryWindow : Form
    {
        // ── Column metadata ─────────────────────────────────────────────────────
        /// <summary>
        /// Attached to each numeric column's <c>Tag</c>.
        /// <list type="bullet">
        ///   <item><see cref="Format"/> — standard numeric format string (e.g. "F1").</item>
        ///   <item><see cref="Unit"/> — optional unit suffix shown in the viewer cell
        ///       but NOT in CSV output.</item>
        ///   <item><see cref="IsGrowth"/> — true for the "Peak at" column, which
        ///       displays as a percentage (×100 + %).</item>
        /// </list>
        /// </summary>
        private sealed record ColumnFormat(
            string Format  = "G",
            string Unit    = "",
            bool IsGrowth  = false);

        // ── Controls ────────────────────────────────────────────────────────────
        private readonly DataGridView _grid;
        private readonly ToolStripStatusLabel _status;
        private readonly ComboBox _statCombo;
        private readonly CheckBox _experimentalCheck;

        // Right-side filter panel — always visible; content adapts to the selected view.
        private readonly Panel    _filterPanel;
        private readonly Label    _filterLabel;
        private readonly TreeView _filterTree;
        private bool _suppressTreeEvents;
        private bool _treeIsAttackMode = true; // sentinel: forces first populate

        // Stamina drain modifier panel (Survival & Stamina view only)
        private readonly Panel           _staminaModPanel;
        private readonly CheckedListBox  _staminaDinoList;
        private readonly NumericUpDown   _staminaModInput;
        private readonly Dictionary<string, double> _staminaMods =
            new(StringComparer.OrdinalIgnoreCase);

        // ── Data ────────────────────────────────────────────────────────────────
        private List<DinoSummary> _summaries = new();
        private readonly List<string> _statNames = new();

        // ── Constructor ─────────────────────────────────────────────────────────
        public SummaryWindow()
        {
            Text = "All-Species Comparison Chart";
            Size = new Size(1600, 760);
            MinimumSize = new Size(1000, 500);
            StartPosition = FormStartPosition.CenterParent;

            // ── Top bar ──────────────────────────────────────────────────────────
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
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _statCombo.SelectedIndexChanged += (_, _) => OnStatComboChanged();

            _experimentalCheck = new CheckBox
            {
                Text = "Show Experimental ⚠",
                AutoSize = true,
                Left = 260,
                Top = 10,
                ForeColor = Color.DarkOrange,
                Font = new Font(Font, FontStyle.Bold)
            };
            _experimentalCheck.CheckedChanged += (_, _) => RefreshStatCombo();

            var disclaimer = new Label
            {
                Text = "⚠ = experimental / estimated; values may differ in-game.",
                AutoSize = true,
                Left = 440,
                Top = 10,
                ForeColor = Color.DimGray,
                Font = new Font(Font, FontStyle.Italic)
            };

            var exportBtn = new Button
            {
                Text = "Export CSV…",
                Top = 6,
                Width = 110,
                Height = 26,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            topPanel.SizeChanged += (_, _) => exportBtn.Left = topPanel.Width - exportBtn.Width - 8;
            exportBtn.Click += ExportCsv_Click;

            topPanel.Controls.Add(statLabel);
            topPanel.Controls.Add(_statCombo);
            topPanel.Controls.Add(_experimentalCheck);
            topPanel.Controls.Add(disclaimer);
            topPanel.Controls.Add(exportBtn);

            // ── Filter TreeView (right sidebar — always visible) ──────────────────
            _filterPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 200,
                Padding = new Padding(4),
                BackColor = Color.FromArgb(245, 245, 255)
            };

            _filterLabel = new Label
            {
                Text = "Filter species:",
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font(Font, FontStyle.Bold)
            };

            _filterTree = new TreeView
            {
                Dock = DockStyle.Fill,
                CheckBoxes = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(245, 245, 255),
                ShowLines = true,
                ShowPlusMinus = true,
                FullRowSelect = false
            };
            _filterTree.AfterCheck += FilterTree_AfterCheck;

            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 28 };
            var checkAllBtn = new Button
            {
                Text = "All",
                Left = 0, Top = 2, Width = 92, Height = 24
            };
            checkAllBtn.Click += (_, _) => SetAllTreeChecks(true);

            var uncheckAllBtn = new Button
            {
                Text = "None",
                Left = 96, Top = 2, Width = 92, Height = 24
            };
            uncheckAllBtn.Click += (_, _) => SetAllTreeChecks(false);

            btnPanel.Controls.Add(checkAllBtn);
            btnPanel.Controls.Add(uncheckAllBtn);

            _filterPanel.Controls.Add(_filterTree);
            _filterPanel.Controls.Add(_filterLabel);
            _filterPanel.Controls.Add(btnPanel);

            // ── Stamina drain modifier panel (Survival & Stamina view, Dock.Right) ──
            _staminaModPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 200,
                Padding = new Padding(4),
                BackColor = Color.FromArgb(235, 248, 255),
                Visible = false
            };

            // Bottom sub-panel: input + buttons (fixed height, docked to bottom)
            var sBottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 108 };

            var sModInputLabel = new Label
            {
                Text = "Drain mod (5 = 5%, 1.05 = ×1.05):",
                Left = 0, Top = 0, Width = 192, Height = 30,
                AutoSize = false
            };

            _staminaModInput = new NumericUpDown
            {
                Left = 0, Top = 32, Width = 192,
                Minimum = 0.01m, Maximum = 500m,
                DecimalPlaces = 2, Value = 5m, Increment = 5m
            };

            var sApplyBtn    = new Button { Text = "Apply",     Left = 0,   Top = 60, Width = 60, Height = 24 };
            var sClearSelBtn = new Button { Text = "Clear Sel", Left = 64,  Top = 60, Width = 64, Height = 24 };
            var sClearAllBtn = new Button { Text = "Clear All", Left = 132, Top = 60, Width = 60, Height = 24 };

            sApplyBtn.Click += (_, _) =>
            {
                double raw = (double)_staminaModInput.Value;
                // >= 2 → percentage (5 → ×1.05); < 2 → direct multiplier (1.05 → ×1.05)
                double factor = raw >= 2.0 ? 1.0 + raw / 100.0 : raw;
                foreach (string name in _staminaDinoList.CheckedItems)
                    _staminaMods[name] = factor;
                RebuildGrid();
            };
            sClearSelBtn.Click += (_, _) =>
            {
                foreach (string name in _staminaDinoList.CheckedItems)
                    _staminaMods.Remove(name);
                RebuildGrid();
            };
            sClearAllBtn.Click += (_, _) => { _staminaMods.Clear(); RebuildGrid(); };

            sBottomPanel.Controls.Add(sModInputLabel);
            sBottomPanel.Controls.Add(_staminaModInput);
            sBottomPanel.Controls.Add(sApplyBtn);
            sBottomPanel.Controls.Add(sClearSelBtn);
            sBottomPanel.Controls.Add(sClearAllBtn);

            // Top label
            var sModTitle = new Label
            {
                Text = "Stamina modifier:",
                Dock = DockStyle.Top, Height = 20,
                Font = new Font(Font, FontStyle.Bold)
            };

            // Species checklist fills remaining space
            _staminaDinoList = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(235, 248, 255)
            };

            // Add in reverse dock order: Bottom first, then Fill, then Top
            _staminaModPanel.Controls.Add(_staminaDinoList);
            _staminaModPanel.Controls.Add(sModTitle);
            _staminaModPanel.Controls.Add(sBottomPanel);

            // ── Grid ─────────────────────────────────────────────────────────────
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

            // Numeric display: apply format+unit on the fly; value stays a raw double.
            _grid.CellFormatting += Grid_CellFormatting;
            // Numeric sort: compare raw doubles so "100" > "20" (not string order).
            _grid.SortCompare += Grid_SortCompare;

            // ── Status strip ─────────────────────────────────────────────────────
            var strip = new StatusStrip();
            _status = new ToolStripStatusLabel { Text = "Loading…" };
            strip.Items.Add(_status);

            // Right panels: filterPanel added last = rightmost; staminaModPanel left of it.
            Controls.Add(_grid);
            Controls.Add(_staminaModPanel);
            Controls.Add(_filterPanel);
            Controls.Add(topPanel);
            Controls.Add(strip);

            Load += async (_, _) => await LoadSummariesAsync();
        }

        // ── Data loading ─────────────────────────────────────────────────────────

        private async System.Threading.Tasks.Task LoadSummariesAsync()
        {
            _status.Text = "Computing summaries…";
            _grid.Enabled = false;

            try
            {
                var summaries = await System.Threading.Tasks.Task.Run(() =>
                    DataLoader.Instance.BuildAllSummaries());

                _summaries = summaries;

                _staminaDinoList.Items.Clear();
                foreach (var sum in summaries)
                    _staminaDinoList.Items.Add(sum.DinoName, false);

                _statNames.Clear();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sum in summaries)
                    foreach (var stat in sum.Stats)
                        if (!stat.IsAttack && seen.Add(stat.Name))
                            _statNames.Add(stat.Name);

                // Populates combo (respects experimental toggle) and fires OnStatComboChanged.
                RefreshStatCombo();

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

        // ── Filter tree population ────────────────────────────────────────────────

        private void PopulateFilterTree()
        {
            bool isAttacks = _statCombo.SelectedItem is string s && s == "Attacks";
            // Note: "Health & Blood ⚠" contains the warning suffix

            _suppressTreeEvents = true;
            _filterTree.BeginUpdate();
            _filterTree.Nodes.Clear();

            if (isAttacks)
            {
                _filterLabel.Text = "Attacks per species:";
                foreach (var sum in _summaries)
                {
                    var attackRows = sum.Stats.Where(r => r.IsAttack).ToList();
                    if (attackRows.Count == 0) continue;

                    var speciesNode = new TreeNode(sum.DinoName) { Checked = true };
                    foreach (var ar in attackRows)
                        speciesNode.Nodes.Add(new TreeNode(ar.AttackKey) { Checked = true });
                    _filterTree.Nodes.Add(speciesNode);
                }
                _filterTree.ExpandAll();
            }
            else
            {
                _filterLabel.Text = "Filter species:";
                foreach (var sum in _summaries)
                    _filterTree.Nodes.Add(new TreeNode(sum.DinoName) { Checked = true });
            }

            _filterTree.EndUpdate();
            _suppressTreeEvents = false;
        }

        // ── Stat combo / tree event handlers ─────────────────────────────────────

        private void OnStatComboChanged()
        {
            bool isAttacks = _statCombo.SelectedItem is string s && s == "Attacks";
            if (isAttacks != _treeIsAttackMode)
            {
                _treeIsAttackMode = isAttacks;
                PopulateFilterTree();
            }
            _staminaModPanel.Visible =
                _statCombo.SelectedItem is string sel && sel == "Survival & Stamina";
            RebuildGrid();
        }

        private void FilterTree_AfterCheck(object? sender, TreeViewEventArgs e)
        {
            if (_suppressTreeEvents || e.Node == null) return;
            if (e.Action == TreeViewAction.Unknown) return;

            _suppressTreeEvents = true;
            try
            {
                // Propagate parent → children (no-op for single-level tree).
                if (e.Node.Parent == null)
                {
                    foreach (TreeNode child in e.Node.Nodes)
                        child.Checked = e.Node.Checked;
                }
            }
            finally { _suppressTreeEvents = false; }

            BeginInvoke((Action)RebuildGrid);
        }

        private void SetAllTreeChecks(bool check)
        {
            _suppressTreeEvents = true;
            _filterTree.BeginUpdate();
            try
            {
                foreach (TreeNode speciesNode in _filterTree.Nodes)
                {
                    speciesNode.Checked = check;
                    foreach (TreeNode attackNode in speciesNode.Nodes)
                        attackNode.Checked = check;
                }
            }
            finally
            {
                _filterTree.EndUpdate();
                _suppressTreeEvents = false;
            }
            RebuildGrid();
        }

        private HashSet<string> GetCheckedSpecies()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TreeNode node in _filterTree.Nodes)
                if (node.Checked) set.Add(node.Text);
            return set;
        }

        // ── Stat combo population ────────────────────────────────────────────────

        /// <summary>
        /// (Re)populates the stat combo, gating "Health &amp; Blood ⚠" behind the
        /// experimental checkbox. Preserves the current selection when possible,
        /// falls back to index 0 if the previously selected item was removed.
        /// Fires <see cref="OnStatComboChanged"/> via SelectedIndex assignment.
        /// </summary>
        private void RefreshStatCombo()
        {
            string? current = _statCombo.SelectedItem as string;

            _statCombo.Items.Clear();
            _statCombo.Items.Add("Survival & Stamina");
            if (_experimentalCheck.Checked)
                _statCombo.Items.Add("Health & Blood ⚠");
            _statCombo.Items.Add("Attacks");
            foreach (var n in _statNames)
                _statCombo.Items.Add(n);

            if (current != null && _statCombo.Items.Contains(current))
                _statCombo.SelectedItem = current;
            else
                _statCombo.SelectedIndex = 0;
        }

        // ── Grid rebuild dispatcher ───────────────────────────────────────────────

        private void RebuildGrid()
        {
            _grid.Columns.Clear();
            _grid.Rows.Clear();

            if (_statCombo.SelectedItem is not string selected) return;

            switch (selected)
            {
                case "Survival & Stamina":  BuildSurvivalGrid();     break;
                case "Health & Blood ⚠":   BuildHealthBloodGrid();  break;
                case "Attacks":             BuildAttacksGrid();      break;
                default:                    BuildStatGrid(selected); break;
            }

            _grid.ClearSelection();
        }

        // ── Grid event handlers ───────────────────────────────────────────────────

        /// <summary>
        /// Formats numeric cells on the fly: raw <c>double</c> → display string with
        /// optional unit.  String cells (Species, Attack) are left unchanged.
        /// </summary>
        private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex].Tag is not ColumnFormat fmt) return;
            if (e.Value is not double d) return;

            if (double.IsNaN(d))
            {
                e.Value = "—";
                e.FormattingApplied = true;
                return;
            }

            string text;
            if (fmt.IsGrowth)
                text = $"{d * 100.0:F1}%";
            else if (string.IsNullOrEmpty(fmt.Unit))
                text = d.ToString(fmt.Format);
            else
                text = $"{d.ToString(fmt.Format)} {fmt.Unit}";

            e.Value = text;
            e.FormattingApplied = true;
        }

        /// <summary>
        /// Sorts numeric columns by raw <c>double</c> value (NaN always last).
        /// Handles both boxed-double cell values and the formatted strings that
        /// CellFormatting may have placed in the cell, so sorting works regardless
        /// of which form the DataGridView exposes through CellValue1/2.
        /// String columns (no ColumnFormat tag) use the default sort.
        /// </summary>
        private void Grid_SortCompare(object? sender, DataGridViewSortCompareEventArgs e)
        {
            if (_grid.Columns[e.Column.Index].Tag is not ColumnFormat fmt) return;

            double v1 = ToSortDouble(e.CellValue1, fmt);
            double v2 = ToSortDouble(e.CellValue2, fmt);

            bool nan1 = double.IsNaN(v1), nan2 = double.IsNaN(v2);
            if (nan1 && nan2) { e.SortResult = 0;  e.Handled = true; return; }
            if (nan1)         { e.SortResult = 1;  e.Handled = true; return; }
            if (nan2)         { e.SortResult = -1; e.Handled = true; return; }
            e.SortResult = v1.CompareTo(v2);
            e.Handled = true;
        }

        /// <summary>
        /// Extracts a sort-key double from a DataGridView cell value that may be
        /// either a boxed <c>double</c>, a formatted display string (e.g. "46.8 km/h"
        /// or "87.5%"), or the dash placeholder "—".
        /// Returns <c>double.NaN</c> for unparseable / NaN / dash values.
        /// </summary>
        private static double ToSortDouble(object? cellValue, ColumnFormat fmt)
        {
            if (cellValue is double d)
                return d;

            string? s = cellValue?.ToString()?.Trim();
            if (string.IsNullOrEmpty(s) || s == "—") return double.NaN;

            // Strip unit suffix (e.g. " km/h", " kg", " dmg") when present.
            if (!string.IsNullOrEmpty(fmt.Unit))
                s = s.Replace(fmt.Unit, "").Trim();

            // Growth column: "87.5%" → 0.875
            if (fmt.IsGrowth && s.EndsWith("%"))
            {
                if (double.TryParse(s[..^1],
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double pct))
                    return pct / 100.0;
                return double.NaN;
            }

            return double.TryParse(s,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v)
                ? v : double.NaN;
        }

        // ── Shared column helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Adds the ten standard growth-point columns (0 % → 100 % + Peak + Peak-at).
        /// Numeric columns are tagged with <paramref name="fmt"/> so the cell
        /// formatter applies the right display.
        /// </summary>
        private void AddGrowthColumns(string unit, string format)
        {
            var numFmt = new ColumnFormat(format, unit);
            var pctFmt = new ColumnFormat("F1", "", IsGrowth: true);

            _grid.Columns.Add(MakeColumn("Newborn",  "0%\nNewborn",        70, fmt: numFmt));
            _grid.Columns.Add(MakeColumn("Juvenile", "25%\nJuvenile",      70, fmt: numFmt));
            _grid.Columns.Add(MakeColumn("Subadult", "50%\nSubadult",      70, fmt: numFmt));
            _grid.Columns.Add(MakeColumn("Adult",    "75%\nAdult",         70, fmt: numFmt));
            _grid.Columns.Add(MakeColumn("Prime875", "87.5%\nPrime",       70, fmt: numFmt));
            _grid.Columns.Add(MakeColumn("Frail875", "87.5%\nFrail",       70, fmt: numFmt));
            _grid.Columns.Add(MakeColumn("Prime",    "100%\nPrime",        70, fmt: numFmt));
            _grid.Columns.Add(MakeColumn("Frail",    "100%\nFrail",        70, fmt: numFmt));
            _grid.Columns.Add(MakeColumn("Peak",     "Peak",               70, fmt: numFmt));
            _grid.Columns.Add(MakeColumn("PeakAt",   "Peak at\n(growth)",  75, fmt: pctFmt));
        }

        /// <summary>
        /// Returns the ten growth-point values as raw <c>double</c>s in the exact
        /// order that <see cref="AddGrowthColumns"/> adds columns:
        /// 0%, 25%, 50%, 75%, 87.5%-Prime, 87.5%-Frail, 100%-Prime, 100%-Frail,
        /// Peak, Peak-at.
        /// </summary>
        private static object[] GrowthCells(StatRow row) => new object[]
        {
            row.Growth0,    // 0%  Newborn
            row.Juvenile,   // 25% Juvenile
            row.Subadult,   // 50% Subadult
            row.Adult,      // 75% Adult
            row.Elder875,   // 87.5% Prime
            row.Senior875,  // 87.5% Frail
            row.Prime,      // 100% Prime
            row.Frail,      // 100% Frail
            row.Peak,       // Peak value
            row.PeakAt,     // Peak at (growth %)
        };

        // ── Single-stat grid ──────────────────────────────────────────────────────

        private void BuildStatGrid(string statName)
        {
            var checkedSpecies = GetCheckedSpecies();

            // Look up unit/format from the first matching stat row.
            var firstRow = _summaries
                .Select(s => s.Stats.FirstOrDefault(
                    r => r.Name.Equals(statName, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(r => r != null);
            string unit   = firstRow?.Unit   ?? "";
            string format = firstRow?.Format ?? "F1";

            _grid.Columns.Add(MakeColumn("Species", "Species", 130, alignLeft: true));
            AddGrowthColumns(unit, format);

            bool isSpeedStat = unit == "km/h";

            foreach (var sum in _summaries)
            {
                if (!checkedSpecies.Contains(sum.DinoName)) continue;

                var row = sum.Stats.FirstOrDefault(
                    s => s.Name.Equals(statName, StringComparison.OrdinalIgnoreCase));
                if (row == null) continue;

                // Dinos with diet-slot speed buffs: expand to one row per slot count.
                if (isSpeedStat
                    && Config.DietSlotSpeedBuffs.TryGetValue(sum.DinoName, out double[]? slotSpeeds)
                    && slotSpeeds != null
                    && !double.IsNaN(row.Adult) && row.Adult > 0)
                {
                    for (int s = 0; s < slotSpeeds.Length; s++)
                    {
                        double factor = slotSpeeds[s] / row.Adult;
                        var scaled = ScaleStatRow(row, factor);
                        string label = s == 1 ? $"{sum.DinoName} (1 slot)" : $"{sum.DinoName} ({s} slots)";
                        var cells = new List<object> { label };
                        cells.AddRange(GrowthCells(scaled));
                        _grid.Rows.Add(cells.ToArray());
                    }
                    continue;
                }

                var rowCells = new List<object> { sum.DinoName };
                rowCells.AddRange(GrowthCells(row));
                _grid.Rows.Add(rowCells.ToArray());
            }

            _status.Text = $"{statName} — {_grid.Rows.Count} rows.";
        }

        // ── Attacks grid ──────────────────────────────────────────────────────────

        private void BuildAttacksGrid()
        {
            var enabled = new HashSet<(string Species, string Attack)>();
            foreach (TreeNode speciesNode in _filterTree.Nodes)
                foreach (TreeNode attackNode in speciesNode.Nodes)
                    if (attackNode.Checked)
                        enabled.Add((speciesNode.Text, attackNode.Text));

            _grid.Columns.Add(MakeColumn("Species", "Species", 130, alignLeft: true));
            _grid.Columns.Add(MakeColumn("Attack",  "Attack",  110, alignLeft: true));
            AddGrowthColumns("dmg", "F0");

            int rowCount = 0;
            foreach (var sum in _summaries)
            {
                bool dinoSectionStarted = false;
                foreach (var stat in sum.Stats.Where(s =>
                    s.IsAttack && enabled.Contains((sum.DinoName, s.AttackKey))))
                {
                    var cells = new List<object> { sum.DinoName, stat.AttackKey };
                    cells.AddRange(GrowthCells(stat));
                    int idx = _grid.Rows.Add(cells.ToArray());

                    if (dinoSectionStarted)
                        _grid.Rows[idx].DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 250);

                    dinoSectionStarted = true;
                    rowCount++;
                }
            }

            _status.Text = $"Attacks — {rowCount} rows.";
        }

        // ── Survival & Stamina grid ───────────────────────────────────────────────

        private void BuildSurvivalGrid()
        {
            var checkedSpecies = GetCheckedSpecies();

            var secFmt = new ColumnFormat("F0", "s");
            var minFmt = new ColumnFormat("F0", "min");
            var mFmt   = new ColumnFormat("F0", "m");

            _grid.Columns.Add(MakeColumn("Species",     "Species",              130, alignLeft: true));
            _grid.Columns.Add(MakeColumn("Starve",      "Starve\n(min)",         70, fmt: minFmt));
            _grid.Columns.Add(MakeColumn("Dehydrate",   "Dehydrate\n(min)",      75, fmt: minFmt));
            _grid.Columns.Add(MakeColumn("Underwater",  "Underwater\n(s)",       75, fmt: secFmt));
            _grid.Columns.Add(MakeColumn("Sprint",      "Sprint\n(s)",           70, fmt: secFmt));
            _grid.Columns.Add(MakeColumn("SprintRange", "Sprint\nRange (m)",     80, fmt: mFmt));
            _grid.Columns.Add(MakeColumn("FastSwim",    "Fast Swim ⚠\n(s)",     80, fmt: secFmt));
            _grid.Columns.Add(MakeColumn("FastSwimR",   "Fast Swim ⚠\nRange (m)", 90, fmt: mFmt));
            _grid.Columns.Add(MakeColumn("SlowSwim",    "Slow Swim ⚠\n(s)",     80, fmt: secFmt));
            _grid.Columns.Add(MakeColumn("SlowSwimR",   "Slow Swim ⚠\nRange (m)", 90, fmt: mFmt));

            foreach (var sum in _summaries)
            {
                if (!checkedSpecies.Contains(sum.DinoName)) continue;

                double mod = _staminaMods.TryGetValue(sum.DinoName, out var m) ? m : 1.0;

                // Diet-slot dinos: one row per slot, sprint duration/range scaled per slot speed.
                if (Config.DietSlotSpeedBuffs.TryGetValue(sum.DinoName, out double[]? slotSpeeds)
                    && slotSpeeds != null
                    && !double.IsNaN(sum.SprintRangeM) && !double.IsNaN(sum.SprintDurationSec))
                {
                    for (int s = 0; s < slotSpeeds.Length; s++)
                    {
                        double slotSpeedMs = slotSpeeds[s] * (1000.0 / 3600.0);
                        double slotRange   = slotSpeedMs * sum.SprintDurationSec;
                        string label = s == 1 ? $"{sum.DinoName} (1 slot)" : $"{sum.DinoName} ({s} slots)";
                        int ri = _grid.Rows.Add(
                            label,
                            sum.TimeToStarveMin,
                            sum.TimeToDehydrateMin,
                            sum.TimeUnderwaterSec,
                            SMod(sum.SprintDurationSec, mod),
                            SMod(slotRange,             mod),
                            SMod(sum.FastSwimDurationSec, mod),
                            SMod(sum.FastSwimRangeM,      mod),
                            SMod(sum.SlowSwimDurationSec, mod),
                            SMod(sum.SlowSwimRangeM,      mod));
                        if (mod != 1.0)
                            _grid.Rows[ri].DefaultCellStyle.BackColor = Color.FromArgb(255, 245, 200);
                    }
                    continue;
                }

                int rowIdx = _grid.Rows.Add(
                    sum.DinoName,
                    sum.TimeToStarveMin,
                    sum.TimeToDehydrateMin,
                    sum.TimeUnderwaterSec,
                    SMod(sum.SprintDurationSec,   mod),
                    SMod(sum.SprintRangeM,         mod),
                    SMod(sum.FastSwimDurationSec,  mod),
                    SMod(sum.FastSwimRangeM,        mod),
                    SMod(sum.SlowSwimDurationSec,  mod),
                    SMod(sum.SlowSwimRangeM,        mod));
                if (mod != 1.0)
                    _grid.Rows[rowIdx].DefaultCellStyle.BackColor = Color.FromArgb(255, 245, 200);
            }

            _status.Text = $"Survival & Stamina — {_grid.Rows.Count} species.  " +
                           "Sprint = from balance table (may be per game-tick).  " +
                           "Swim ⚠ = estimated; key names vary by game version.";
        }

        // ── Health & Blood regen grid ─────────────────────────────────────────────

        private void BuildHealthBloodGrid()
        {
            var checkedSpecies = GetCheckedSpecies();

            // All values are % of max per second — no unit suffix, formatted to 4 dp.
            var pctFmt = new ColumnFormat("F4", "%/s");

            _grid.Columns.Add(MakeColumn("Species",      "Species",                    130, alignLeft: true));
            _grid.Columns.Add(MakeColumn("HRegen",       "Health Regen\nStanding (%/s)", 120, fmt: pctFmt));
            _grid.Columns.Add(MakeColumn("HRegenRest",   "Health Regen\nResting (%/s)",  120, fmt: pctFmt));
            _grid.Columns.Add(MakeColumn("LockedRegen",  "Locked HP\nRegen (%/s)",       110, fmt: pctFmt));
            _grid.Columns.Add(MakeColumn("BRegenActive", "Blood Regen\nActive (%/s)",    120, fmt: pctFmt));
            _grid.Columns.Add(MakeColumn("BRegenRest",   "Blood Regen\nResting (%/s)",   120, fmt: pctFmt));

            foreach (var sum in _summaries)
            {
                if (!checkedSpecies.Contains(sum.DinoName)) continue;

                _grid.Rows.Add(
                    sum.DinoName,
                    sum.HealthRegenStanding,
                    sum.HealthRegenResting,
                    sum.LockedHealthRegen,
                    sum.BloodRegenStanding,
                    sum.BloodRegenResting);
            }

            _status.Text =
                $"Health & Blood ⚠ — {_grid.Rows.Count} species.  " +
                "All values = % of max per second (⚠ experimental — actual in-game rates may differ).  " +
                "Blood Regen counters bleeding; actual bleed drain is in GameEffect BPs (not in pak DTs).";
        }

        // ── CSV export ────────────────────────────────────────────────────────────

        private void ExportCsv_Click(object? sender, EventArgs e)
        {
            string suggestedName = (_statCombo.SelectedItem as string ?? "export")
                .Replace(" ", "_")
                .Replace("&", "and")
                .Replace("/", "-");

            using var dlg = new SaveFileDialog
            {
                Title           = "Export current view to CSV",
                Filter          = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName        = $"TheIsle_{suggestedName}.csv",
                DefaultExt      = "csv",
                OverwritePrompt = true
            };

            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                using var writer = new StreamWriter(dlg.FileName, append: false, Encoding.UTF8);

                // Header row — replace newlines with a space.
                var headers = _grid.Columns
                    .Cast<DataGridViewColumn>()
                    .Select(c => CsvEscape(c.HeaderText.Replace('\n', ' ')));
                writer.WriteLine(string.Join(",", headers));

                // Data rows — numeric values without units; strings quoted if needed.
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    var cells = row.Cells
                        .Cast<DataGridViewCell>()
                        .Select(c => FormatCsvCell(c));
                    writer.WriteLine(string.Join(",", cells));
                }

                _status.Text = $"Exported → {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Formats a single cell for CSV: numeric columns → number only (no unit),
        /// growth column → percentage string, NaN → empty, strings → quoted if needed.
        /// </summary>
        private string FormatCsvCell(DataGridViewCell cell)
        {
            var col = _grid.Columns[cell.ColumnIndex];
            if (col.Tag is ColumnFormat fmt && cell.Value is double d)
            {
                if (double.IsNaN(d)) return "";
                if (fmt.IsGrowth)   return $"{d * 100.0:F1}";
                return d.ToString(fmt.Format);
            }
            return CsvEscape(cell.Value?.ToString() ?? string.Empty);
        }

        private static string CsvEscape(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>Applies a duration multiplier; NaN or mod=1 → unchanged.</summary>
        private static double SMod(double v, double mod) =>
            double.IsNaN(v) || mod == 1.0 ? v : v * mod;

        /// <summary>
        /// Returns a copy of <paramref name="src"/> with all value fields multiplied
        /// by <paramref name="factor"/>. Time/position fields (<c>PeakAt</c>) are
        /// copied unchanged. NaN values remain NaN.
        /// </summary>
        private static StatRow ScaleStatRow(StatRow src, double factor)
        {
            static double S(double v, double f) => double.IsNaN(v) ? v : v * f;
            return new StatRow
            {
                Name      = src.Name,
                Unit      = src.Unit,
                Format    = src.Format,
                IsAttack  = src.IsAttack,
                AttackKey = src.AttackKey,
                Growth0   = S(src.Growth0,   factor),
                Juvenile  = S(src.Juvenile,  factor),
                Subadult  = S(src.Subadult,  factor),
                Adult     = S(src.Adult,     factor),
                Elder875  = S(src.Elder875,  factor),
                Senior875 = S(src.Senior875, factor),
                Prime     = S(src.Prime,     factor),
                Frail     = S(src.Frail,     factor),
                Peak      = S(src.Peak,      factor),
                PeakAt    = src.PeakAt,
            };
        }

        // ── Column factory ────────────────────────────────────────────────────────

        private static DataGridViewTextBoxColumn MakeColumn(
            string name, string header, int width,
            bool alignLeft = false, ColumnFormat? fmt = null)
        {
            return new DataGridViewTextBoxColumn
            {
                Name       = name,
                HeaderText = header,
                Width      = width,
                SortMode   = DataGridViewColumnSortMode.Automatic,
                Tag        = fmt,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = alignLeft
                        ? DataGridViewContentAlignment.MiddleLeft
                        : DataGridViewContentAlignment.MiddleRight,
                    Padding = new Padding(4, 0, 4, 0)
                }
            };
        }
    }
}
