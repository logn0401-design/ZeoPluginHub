using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ZeoOverlay
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "Local\\ZeoOverlay_v0_4", out created))
            {
                if (!created) return;

                try { NativeMethods.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                string settings = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Pulsar", "ZeoCore", "hud-settings.json");
                int port = 37841;

                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].Equals("--settings", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        settings = args[++i];
                    else if (args[i].Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        int p;
                        if (int.TryParse(args[++i], out p) && p > 1024 && p < 65535) port = p;
                    }
                }

                var model = OverlaySettings.Load(settings);
                Application.Run(new HudOverlayForm(model, port));
            }
        }
    }
}
