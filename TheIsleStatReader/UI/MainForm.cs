using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TheIsleStatReader.Core;

namespace TheIsleStatReader.UI
{
    /// <summary>
    /// Main application window.  Left panel contains dino / file selectors;
    /// right panel is a grey placeholder.
    /// </summary>
    internal sealed class MainForm : Form
    {
        // ------------------------------------------------------------------
        // Controls
        // ------------------------------------------------------------------
        private readonly ListBox _dinoListBox;
        private readonly ListBox _fileListBox;
        private readonly Button _plotButton;
        private readonly Button _balanceButton;
        private readonly ComboBox _targetWindowCombo;
        private readonly StatusStrip _statusStrip;
        private readonly ToolStripStatusLabel _statusLabel;
        private readonly Panel _leftPanel;
        private readonly Panel _rightPanel;
        private readonly Label _rightPlaceholder;
        private readonly MenuStrip _menuStrip;

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------
        private readonly List<PlotWindow> _plotWindows = new();
        private bool _loaded;

        // ------------------------------------------------------------------
        // Constructor / Layout
        // ------------------------------------------------------------------
        public MainForm()
        {
            Text = "The Isle Stat Reader";
            Size = new Size(1000, 700);
            MinimumSize = new Size(800, 550);

            // ---- Menu bar ----
            _menuStrip = new MenuStrip();
            var fileMenu = new ToolStripMenuItem("&File");
            var settingsItem = new ToolStripMenuItem("&Settings…");
            settingsItem.Click += SettingsMenuItem_Click;
            var exitItem = new ToolStripMenuItem("E&xit");
            exitItem.Click += (_, _) => Close();
            fileMenu.DropDownItems.Add(settingsItem);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(exitItem);

            var viewMenu = new ToolStripMenuItem("&View");
            var summaryItem = new ToolStripMenuItem("&All-Species Comparison…");
            summaryItem.Click += SummaryMenuItem_Click;
            viewMenu.DropDownItems.Add(summaryItem);

            var helpMenu = new ToolStripMenuItem("&Help");
            var diagItem = new ToolStripMenuItem("&Diagnostics…");
            diagItem.Click += DiagnosticsMenuItem_Click;
            helpMenu.DropDownItems.Add(diagItem);

            _menuStrip.Items.Add(fileMenu);
            _menuStrip.Items.Add(viewMenu);
            _menuStrip.Items.Add(helpMenu);
            MainMenuStrip = _menuStrip;

            // ---- Status bar ----
            _statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel("Initialising…")
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _statusStrip.Items.Add(_statusLabel);

            // ---- Left panel (300 px wide) ----
            _leftPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 300,
                Padding = new Padding(6)
            };

            var dinoLabel = new Label
            {
                Text = "Dinosaurs",
                Dock = DockStyle.Top,
                Height = 20,
                Font = new Font(Font, FontStyle.Bold)
            };

            _dinoListBox = new ListBox
            {
                Dock = DockStyle.Top,
                Height = 220,
                IntegralHeight = false
            };
            _dinoListBox.SelectedIndexChanged += DinoListBox_SelectedIndexChanged;

            var fileLabel = new Label
            {
                Text = "Asset Files",
                Dock = DockStyle.Top,
                Height = 20,
                Font = new Font(Font, FontStyle.Bold)
            };

            _fileListBox = new ListBox
            {
                Dock = DockStyle.Top,
                Height = 180,
                IntegralHeight = false,
                SelectionMode = SelectionMode.MultiExtended
            };
            _fileListBox.SelectedIndexChanged += (s, _) =>
            {
                _plotButton.Enabled = _loaded && _fileListBox.SelectedItems.Count > 0;
            };

            // ---- Button row ----
            var buttonPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36
            };

            _plotButton = new Button
            {
                Text = "Plot",
                Left = 0,
                Top = 4,
                Width = 90,
                Height = 28,
                Enabled = false
            };
            _plotButton.Click += PlotButton_Click;

            _balanceButton = new Button
            {
                Text = "Balance Attrs",
                Left = 96,
                Top = 4,
                Width = 110,
                Height = 28,
                Enabled = false
            };
            _balanceButton.Click += BalanceButton_Click;

            buttonPanel.Controls.Add(_plotButton);
            buttonPanel.Controls.Add(_balanceButton);

            // ---- Target window row ----
            var targetPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36
            };

            var targetLabel = new Label
            {
                Text = "Target Window:",
                Left = 0,
                Top = 8,
                Width = 100,
                Height = 20
            };

            _targetWindowCombo = new ComboBox
            {
                Left = 104,
                Top = 4,
                Width = 180,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _targetWindowCombo.Items.Add("New Window");
            _targetWindowCombo.SelectedIndex = 0;

            targetPanel.Controls.Add(targetLabel);
            targetPanel.Controls.Add(_targetWindowCombo);

            // Add controls to left panel (bottom-up due to Dock.Top stacking)
            _leftPanel.Controls.Add(targetPanel);
            _leftPanel.Controls.Add(buttonPanel);
            _leftPanel.Controls.Add(_fileListBox);
            _leftPanel.Controls.Add(fileLabel);
            _leftPanel.Controls.Add(_dinoListBox);
            _leftPanel.Controls.Add(dinoLabel);

            // ---- Right panel ----
            _rightPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(50, 50, 50)
            };

            _rightPlaceholder = new Label
            {
                Text = "Select a dinosaur and file, then click Plot",
                ForeColor = Color.Silver,
                Font = new Font("Segoe UI", 12f),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            _rightPanel.Controls.Add(_rightPlaceholder);

            // ---- Splitter ----
            var splitter = new Splitter
            {
                Dock = DockStyle.Left,
                Width = 4,
                BackColor = Color.Gray
            };

            // ---- Add to form ----
            Controls.Add(_rightPanel);
            Controls.Add(splitter);
            Controls.Add(_leftPanel);
            Controls.Add(_statusStrip);
            Controls.Add(_menuStrip);

            // Start loading
            Load += MainForm_Load;
        }

        // ------------------------------------------------------------------
        // Initialisation
        // ------------------------------------------------------------------
        private async void MainForm_Load(object? sender, EventArgs e)
        {
            // First-run / missing settings: prompt before attempting init.
            if (!Config.IsValid())
            {
                _statusLabel.Text = "Please configure pak directory and AES key…";
                using var dlg = new SettingsDialog();
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    _statusLabel.Text = "Settings required. Open File → Settings to configure.";
                    return;
                }
            }

            await LoadProviderAsync();
        }

        /// <summary>
        /// Initialises (or re-initialises) the DataLoader and refreshes the dino list.
        /// Safe to call multiple times — clears existing state first.
        /// </summary>
        private async Task LoadProviderAsync()
        {
            _loaded = false;
            _plotButton.Enabled = false;
            _balanceButton.Enabled = false;
            _dinoListBox.Enabled = false;
            _dinoListBox.Items.Clear();
            _fileListBox.Items.Clear();

            try
            {
                DataLoader.Instance.Reset();

                var progress = new Progress<(int Percent, string Message)>(p =>
                {
                    _statusLabel.Text = $"Loading pak files… {p.Percent}%  {p.Message}";
                });

                await DataLoader.Instance.InitializeAsync(progress);

                var dinos = DataLoader.Instance.GetDinosaurs();
                foreach (var d in dinos)
                    _dinoListBox.Items.Add(d);

                _dinoListBox.Enabled = true;
                _loaded = true;
                _statusLabel.Text = $"Ready — {dinos.Count} dinosaurs found.";

                if (DataLoader.Instance.MappingsWarning is { } warning)
                {
                    _statusLabel.Text = $"Ready (no mappings) — {dinos.Count} dinosaurs found.";
                    MessageBox.Show(
                        warning,
                        "Mappings",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Error: {ex.Message}";
                MessageBox.Show(
                    $"Failed to initialise pak provider:\n\n{ex.Message}",
                    "Initialisation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void SummaryMenuItem_Click(object? sender, EventArgs e)
        {
            if (!_loaded)
            {
                MessageBox.Show(this,
                    "Wait for the pak provider to finish loading first.",
                    "Not ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Non-modal so the user can still use the main window while
            // the summary grid is up (it computes every dino's curves,
            // which takes a second or two).
            var win = new SummaryWindow();
            win.Show(this);
        }

        private void DiagnosticsMenuItem_Click(object? sender, EventArgs e)
        {
            string text;
            try
            {
                text = DataLoader.Instance.GetDiagnostics();
            }
            catch (Exception ex)
            {
                text = $"Diagnostics failed: {ex.Message}\n\n{ex.StackTrace}";
            }

            using var win = new DiagnosticsWindow(text);
            win.ShowDialog(this);
        }

        private async void SettingsMenuItem_Click(object? sender, EventArgs e)
        {
            string prevPak = Config.PakDirectory;
            string prevAes = Config.AesKey;
            string prevMap = Config.MappingsPath;

            using var dlg = new SettingsDialog();
            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            bool changed =
                !string.Equals(prevPak, Config.PakDirectory, StringComparison.Ordinal) ||
                !string.Equals(prevAes, Config.AesKey, StringComparison.Ordinal) ||
                !string.Equals(prevMap, Config.MappingsPath, StringComparison.Ordinal);

            if (changed || !_loaded)
                await LoadProviderAsync();
        }

        // ------------------------------------------------------------------
        // Dino selected → populate file list
        // ------------------------------------------------------------------
        private void DinoListBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            _fileListBox.Items.Clear();
            _plotButton.Enabled = false;
            _balanceButton.Enabled = false;

            if (_dinoListBox.SelectedItem is not string dinoName)
                return;

            try
            {
                var files = DataLoader.Instance.GetPlottableFiles(dinoName);
                foreach (var f in files)
                    _fileListBox.Items.Add(f);

                _balanceButton.Enabled = true;
                _statusLabel.Text = $"{dinoName} — {files.Count} plottable file(s) found.";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Error enumerating files: {ex.Message}";
            }
        }

        // ------------------------------------------------------------------
        // Plot button
        // ------------------------------------------------------------------
        private async void PlotButton_Click(object? sender, EventArgs e)
        {
            if (_dinoListBox.SelectedItem is not string dinoName)
                return;
            if (_fileListBox.SelectedItems.Count == 0)
                return;

            _plotButton.Enabled = false;
            _statusLabel.Text = "Loading curve data…";

            try
            {
                // Snapshot the selection before async operations (avoid cross-thread access)
                var selectedFiles = _fileListBox.SelectedItems
                    .Cast<object?>()
                    .Select(x => x?.ToString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                if (selectedFiles.Count == 0) return;

                // Determine or create target window
                PlotWindow? targetWindow = GetOrCreateTargetWindow(selectedFiles[0]);

                // Load and add each selected file
                foreach (var item in selectedFiles)
                {
                    string fileName = item;
                    if (string.IsNullOrEmpty(fileName)) continue;

                    await Task.Run(() =>
                    {
                        List<(double[] Times, double[] Values)> curves;
                        string yLabel;

                        if (fileName.StartsWith("Virtual:", StringComparison.OrdinalIgnoreCase))
                        {
                            // Virtual attack curve
                            var allVirtual = DataLoader.Instance.GetAttackVirtualCurves(dinoName);
                            if (!allVirtual.TryGetValue(fileName, out var virtualCurves))
                                return;
                            curves = virtualCurves;
                            yLabel = "Value";
                        }
                        else
                        {
                            string? path = DataLoader.Instance.FindAssetPath(fileName);
                            if (path == null)
                            {
                                Invoke(() => _statusLabel.Text = $"Asset not found: {fileName}");
                                return;
                            }
                            (curves, yLabel) = DataLoader.Instance.GetCurveData(path, fileName);
                        }

                        if (curves.Count == 0)
                        {
                            Invoke(() => _statusLabel.Text = $"No curve data in {fileName}");
                            return;
                        }

                        Invoke(() => targetWindow!.AddCurves(curves, BuildCurveLabel(dinoName, fileName, curves.Count), yLabel));
                    });
                }

                _statusLabel.Text = "Curves plotted.";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Plot error: {ex.Message}";
                MessageBox.Show(ex.Message, "Plot Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _plotButton.Enabled = _fileListBox.SelectedItems.Count > 0;
            }
        }

        // ------------------------------------------------------------------
        // Balance Attributes button
        // ------------------------------------------------------------------
        private async void BalanceButton_Click(object? sender, EventArgs e)
        {
            if (_dinoListBox.SelectedItem is not string dinoName)
                return;

            _balanceButton.Enabled = false;
            _statusLabel.Text = "Loading balance attributes…";

            try
            {
                Dictionary<string, double> attrs = new();
                Dictionary<string, double> stats = new();

                await Task.Run(() =>
                {
                    // Local method-scope variables captured by reference in the closure
                    var a = DataLoader.Instance.GetBalanceAttributes(dinoName);
                    var s = DataLoader.Instance.GetCalculatedStats(dinoName);
                    attrs = a;
                    stats = s;
                });

                var win = new BalanceWindow(dinoName, attrs, stats);
                win.Show(this);
                _statusLabel.Text = $"Balance attributes loaded for {dinoName}.";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Balance attrs error: {ex.Message}";
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _balanceButton.Enabled = true;
            }
        }

        // ------------------------------------------------------------------
        // PlotWindow management
        // ------------------------------------------------------------------

        private PlotWindow GetOrCreateTargetWindow(string defaultNameSource)
        {
            string selected = _targetWindowCombo.SelectedItem?.ToString() ?? "New Window";

            if (selected != "New Window")
            {
                // Try to find the named window
                var existing = _plotWindows.FirstOrDefault(
                    w => w.Text == selected && !w.IsDisposed);
                if (existing != null)
                    return existing;
            }

            // Create a new window
            string windowTitle = TruncateTitle(defaultNameSource, 40);
            var win = new PlotWindow(windowTitle);
            win.Text = windowTitle;
            win.FormClosed += PlotWindow_FormClosed;
            _plotWindows.Add(win);
            win.Show(this);
            RefreshTargetCombo();
            // Select the new window so subsequent plots go there
            _targetWindowCombo.SelectedItem = windowTitle;
            return win;
        }

        private void PlotWindow_FormClosed(object? sender, FormClosedEventArgs e)
        {
            if (sender is PlotWindow pw)
            {
                _plotWindows.Remove(pw);
                RefreshTargetCombo();
            }
        }

        private void RefreshTargetCombo()
        {
            string? prev = _targetWindowCombo.SelectedItem?.ToString();
            _targetWindowCombo.Items.Clear();
            _targetWindowCombo.Items.Add("New Window");

            foreach (var w in _plotWindows.Where(w => !w.IsDisposed))
                _targetWindowCombo.Items.Add(w.Text);

            // Restore selection if still valid
            if (prev != null && _targetWindowCombo.Items.Contains(prev))
                _targetWindowCombo.SelectedItem = prev;
            else
                _targetWindowCombo.SelectedIndex = 0;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static string BuildCurveLabel(string dinoName, string fileName, int curveCount)
        {
            // Virtual names already include the dino name, e.g. "Virtual: Allosaurus Bite Attack"
            if (fileName.StartsWith("Virtual:", StringComparison.OrdinalIgnoreCase))
                return fileName.Substring("Virtual: ".Length).Trim();

            // Strip ATT_ prefix and extension for display
            string name = System.IO.Path.GetFileNameWithoutExtension(fileName);
            if (name.StartsWith($"ATT_{dinoName}_", StringComparison.OrdinalIgnoreCase))
                name = name.Substring($"ATT_{dinoName}_".Length);

            // Always prefix the dino name so curves from multiple dinos overlaid
            // on the same plot window stay distinguishable in the legend.
            return $"{dinoName} {name}";
        }

        private static string TruncateTitle(string source, int maxLen)
        {
            // Trim extension and ATT_ prefix for nicer window titles
            string s = System.IO.Path.GetFileNameWithoutExtension(source);
            if (s.Length > maxLen)
                s = s[..maxLen];
            return s;
        }
    }
}
