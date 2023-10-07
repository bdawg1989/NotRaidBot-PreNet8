using System;
using System.IO;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms
{
    internal static class Program
    {
        public static readonly string WorkingDirectory = Application.StartupPath;
        public static string ConfigPath { get; private set; } = Path.Combine(WorkingDirectory, "config.json");

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
#if NETCOREAPP
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
#endif
            var cmd = Environment.GetCommandLineArgs();
            var cfg = Array.Find(cmd, z => z.EndsWith(".json"));
            if (cfg != null)
                ConfigPath = cmd[0];

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Event Handler for exiting app
            Application.ApplicationExit += new EventHandler(OnApplicationExit);

            Application.Run(new Main());
        }

        // Delete cache folder on exit.
        private static void OnApplicationExit(object sender, EventArgs e)
        {
            var folder = Path.Combine(Directory.GetCurrentDirectory(), "cache");
            if (Directory.Exists(folder))
            {
                try
                {
                    Directory.Delete(folder, true);
                }
                catch (Exception ex)
                {
                    // Handle or log the error
                }
            }
        }
    }
}
