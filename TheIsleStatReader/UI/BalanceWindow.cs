using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TheIsleStatReader.UI
{
    /// <summary>
    /// Displays all balance attribute rows for a dinosaur in a DataGridView,
    /// plus a panel of calculated survival statistics.
    /// </summary>
    internal sealed class BalanceWindow : Form
    {
        public BalanceWindow(
            string dinoName,
            Dictionary<string, double> attributes,
            Dictionary<string, double> calculatedStats)
        {
            Text = $"{dinoName} Balance Attributes";
            Size = new Size(520, 680);
            MinimumSize = new Size(400, 500);
            StartPosition = FormStartPosition.CenterParent;

            // ================================================================
            // Top: DataGridView of raw balance attributes
            // ================================================================
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(220, 220, 220),
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(245, 245, 250)
                }
            };

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Attribute",
                HeaderText = "Attribute",
                FillWeight = 65
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Value",
                HeaderText = "Value",
                FillWeight = 35,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleRight,
                    Format = "G6"
                }
            });

            // Populate raw attributes (sorted alphabetically)
            var sortedAttrs = new SortedDictionary<string, double>(
                attributes, StringComparer.OrdinalIgnoreCase);

            foreach (var (key, value) in sortedAttrs)
                grid.Rows.Add(key, value);

            // ================================================================
            // Bottom: calculated statistics panel (fixed height, scrollable)
            // ================================================================
            var statsPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 180,
                BackColor = Color.FromArgb(240, 245, 255),
                Padding = new Padding(10, 8, 10, 8)
            };

            var statsTitle = new Label
            {
                Text = "Calculated Stats",
                Font = new Font(Font, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 22
            };

            // Scrollable viewport around the TableLayoutPanel. AutoSize on the
            // inner table + AutoScroll on the container gives us a vertical
            // scrollbar whenever the stats list overflows the panel height.
            var statsScroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            var statsTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Padding = new Padding(0, 4, 0, 0)
            };
            // Leave ~20 px on the right for the vertical scrollbar so value
            // labels don't get clipped once it appears.
            statsTable.Width = statsScroll.ClientSize.Width - 20;
            statsScroll.SizeChanged += (_, _) =>
                statsTable.Width = Math.Max(0, statsScroll.ClientSize.Width - 20);
            statsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65f));
            statsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));

            if (calculatedStats.Count == 0)
            {
                var noStatsLabel = new Label
                {
                    Text = "No calculable stats found.",
                    ForeColor = Color.Gray,
                    AutoSize = true
                };
                statsTable.Controls.Add(noStatsLabel);
            }
            else
            {
                int row = 0;
                foreach (var (statName, statValue) in calculatedStats)
                {
                    statsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));

                    var nameLabel = new Label
                    {
                        Text = statName,
                        AutoSize = false,
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Padding = new Padding(0, 2, 4, 2)
                    };

                    var valueLabel = new Label
                    {
                        Text = $"{statValue:F2}",
                        AutoSize = false,
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleRight,
                        Font = new Font(Font, FontStyle.Bold),
                        Padding = new Padding(4, 2, 0, 2)
                    };

                    statsTable.Controls.Add(nameLabel, 0, row);
                    statsTable.Controls.Add(valueLabel, 1, row);
                    row++;
                }
            }

            // Order matters: add Fill-docked scroll viewport first, then the
            // Top-docked title so the title sits above the scrolling area.
            statsScroll.Controls.Add(statsTable);
            statsPanel.Controls.Add(statsScroll);
            statsPanel.Controls.Add(statsTitle);

            // Separator between grid and stats
            var separator = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 2,
                BackColor = Color.FromArgb(180, 180, 200)
            };

            // ================================================================
            // Assemble form (fill first so it expands)
            // ================================================================
            Controls.Add(grid);
            Controls.Add(separator);
            Controls.Add(statsPanel);
        }

        private static int CalcStatsPanelHeight(int statCount)
        {
            // Title (22) + padding (16) + rows (~24 each)
            return 22 + 4 + Math.Max(statCount, 1) * 24 + 16;
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            // 
            // BalanceWindow
            // 
            ClientSize = new Size(944, 253);
            Name = "BalanceWindow";
            ResumeLayout(false);

        }
    }
}
