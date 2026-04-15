using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TheIsleStatReader.UI
{
    /// <summary>
    /// Modal settings dialog: pak directory, AES key, mappings file.
    /// Reads and writes Config.* on OK.
    /// </summary>
    internal sealed class SettingsDialog : Form
    {
        private readonly TextBox _pakBox;
        private readonly TextBox _aesBox;
        private readonly TextBox _mappingsBox;

        public SettingsDialog()
        {
            Text = "Settings";
            Size = new Size(680, 260);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            const int textX = 120;
            const int textW = 450;
            const int browseX = 578;
            const int browseW = 80;

            // ---- Pak directory ----
            var pakLabel = new Label
            {
                Text = "Pak Directory:",
                Left = 12, Top = 18, Width = 110
            };
            _pakBox = new TextBox
            {
                Left = textX, Top = 14, Width = textW,
                Text = Config.PakDirectory
            };
            var pakBrowse = new Button
            {
                Text = "Browse…",
                Left = browseX, Top = 12, Width = browseW
            };
            pakBrowse.Click += (_, _) =>
            {
                using var dlg = new FolderBrowserDialog
                {
                    Description = "Select the game's Paks folder",
                    UseDescriptionForTitle = true
                };
                if (!string.IsNullOrEmpty(_pakBox.Text) && Directory.Exists(_pakBox.Text))
                    dlg.SelectedPath = _pakBox.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _pakBox.Text = dlg.SelectedPath;
            };

            // ---- AES key ----
            var aesLabel = new Label
            {
                Text = "AES Key:",
                Left = 12, Top = 58, Width = 110
            };
            _aesBox = new TextBox
            {
                Left = textX, Top = 54, Width = textW + browseW + 10,
                Text = Config.AesKey
            };

            // ---- Mappings file ----
            var mapLabel = new Label
            {
                Text = "Mappings File:",
                Left = 12, Top = 98, Width = 110
            };
            _mappingsBox = new TextBox
            {
                Left = textX, Top = 94, Width = textW,
                Text = Config.MappingsPath
            };
            var mapBrowse = new Button
            {
                Text = "Browse…",
                Left = browseX, Top = 92, Width = browseW
            };
            mapBrowse.Click += (_, _) =>
            {
                using var dlg = new OpenFileDialog
                {
                    Filter = "USMAP Mappings (*.usmap)|*.usmap|All files (*.*)|*.*",
                    Title = "Select mappings file"
                };
                if (!string.IsNullOrEmpty(_mappingsBox.Text) && File.Exists(_mappingsBox.Text))
                    dlg.InitialDirectory = Path.GetDirectoryName(_mappingsBox.Text);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _mappingsBox.Text = dlg.FileName;
            };

            // ---- Hint ----
            var hint = new Label
            {
                Text = "AES key may start with 0x. Mappings file is optional but recommended.",
                Left = 12, Top = 132, Width = 640,
                ForeColor = Color.DimGray
            };

            // ---- OK / Cancel ----
            var ok = new Button
            {
                Text = "OK",
                Left = 458, Top = 180, Width = 90,
                DialogResult = DialogResult.None
            };
            ok.Click += OnOkClicked;

            var cancel = new Button
            {
                Text = "Cancel",
                Left = 558, Top = 180, Width = 90,
                DialogResult = DialogResult.Cancel
            };

            AcceptButton = ok;
            CancelButton = cancel;

            Controls.AddRange(new Control[]
            {
                pakLabel, _pakBox, pakBrowse,
                aesLabel, _aesBox,
                mapLabel, _mappingsBox, mapBrowse,
                hint,
                ok, cancel
            });
        }

        private void OnOkClicked(object? sender, EventArgs e)
        {
            string pak = _pakBox.Text.Trim();
            string aes = _aesBox.Text.Trim();
            string map = _mappingsBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(pak) || !Directory.Exists(pak))
            {
                MessageBox.Show(this,
                    "Please select a valid pak directory.",
                    "Settings",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(aes))
            {
                MessageBox.Show(this,
                    "Please enter an AES key.",
                    "Settings",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!string.IsNullOrWhiteSpace(map) && !File.Exists(map))
            {
                var answer = MessageBox.Show(this,
                    "The mappings file does not exist. Continue without it?",
                    "Settings",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;
                map = "";
            }

            Config.PakDirectory = pak;
            Config.AesKey = aes;
            Config.MappingsPath = map;
            Config.Save();

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
