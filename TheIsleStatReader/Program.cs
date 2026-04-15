using System;
using System.Windows.Forms;
using TheIsleStatReader.UI;

namespace TheIsleStatReader
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Config.Load();
            Application.Run(new MainForm());
        }
    }
}
