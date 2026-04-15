using System.Drawing;
using System.Windows.Forms;

namespace TheIsleStatReader.UI
{
    /// <summary>
    /// Modal scrollable text viewer for DataLoader.GetDiagnostics() output.
    /// </summary>
    internal sealed class DiagnosticsWindow : Form
    {
        public DiagnosticsWindow(string text)
        {
            Text = "Provider Diagnostics";
            Size = new Size(900, 650);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(500, 400);

            var txt = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericMonospace, 9f),
                Text = text
            };

            var bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40
            };

            var copyBtn = new Button
            {
                Text = "Copy to Clipboard",
                Left = 10, Top = 8, Width = 140
            };
            copyBtn.Click += (_, _) =>
            {
                try { Clipboard.SetText(txt.Text); } catch { }
            };

            var closeBtn = new Button
            {
                Text = "Close",
                Left = 160, Top = 8, Width = 90,
                DialogResult = DialogResult.OK
            };

            bottom.Controls.Add(copyBtn);
            bottom.Controls.Add(closeBtn);

            Controls.Add(txt);
            Controls.Add(bottom);
            AcceptButton = closeBtn;
        }
    }
}
