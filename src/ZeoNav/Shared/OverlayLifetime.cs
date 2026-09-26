using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace Zeo.Shared
{
    // One launcher owns one child. Never find or terminate another process by name.
    internal sealed class OverlayProcessOwner : IDisposable
    {
        private Process child;
        private EventWaitHandle stop;
        private bool disposed;
        internal bool Running { get { try { return child != null && !child.HasExited; } catch { return false; } } }

        internal void Start(ProcessStartInfo info, long gameWindow = 0)
        {
            if (disposed) throw new ObjectDisposedException("OverlayProcessOwner");
            if (Running) return;
            Release();
            string token = Guid.NewGuid().ToString("N");
            stop = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\ZeoOverlayStop_" + token);
            using (var owner = Process.GetCurrentProcess())
                info.Arguments += " --owner-pid " + owner.Id.ToString(CultureInfo.InvariantCulture) +
                    " --owner-start " + owner.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) +
                    " --owner-stop " + token + " --owner-window " +
                    (gameWindow != 0 ? gameWindow : owner.MainWindowHandle.ToInt64()).ToString(CultureInfo.InvariantCulture);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            try { child = Process.Start(info); if (child == null) throw new InvalidOperationException("Overlay did not start."); }
            catch { Release(); throw; }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { if (stop != null) stop.Set(); } catch { }
            try
            {
                if (child != null && !child.WaitForExit(1000))
                {
                    child.Kill(); // Only the process handle returned by our own launch.
                    child.WaitForExit(1000);
                }
            }
            catch { }
            Release();
        }

        private void Release()
        {
            if (child != null) { child.Dispose(); child = null; }
            if (stop != null) { stop.Dispose(); stop = null; }
        }
    }

    internal sealed class OverlayLifetime : IDisposable
    {
        private readonly Process owner;
        private readonly EventWaitHandle stop;
        private readonly Action requestClose;
        private readonly Thread monitor;
        private IntPtr window;
        private bool sawWindow;
        private Stopwatch missingWindow;
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr handle);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
        private volatile bool disposed;

        private OverlayLifetime(Process process, EventWaitHandle signal, long gameWindow, Action close)
        {
            owner = process; stop = signal; requestClose = close;
            window = new IntPtr(gameWindow); sawWindow = gameWindow != 0;
            monitor = new Thread(Watch) { IsBackground = true, Name = "ZeoOverlay-OwnerLifetime" };
            monitor.Start();
        }

        // Fail closed for missing/malformed ownership. The plugin supplies all three
        // values before launch. Start time prevents attachment to a recycled PID.
        internal static OverlayLifetime Attach(string[] args, Action requestClose)
        {
            Process process = null; EventWaitHandle signal = null;
            try
            {
                int pid = 0; long ticks = 0, gameWindow = 0; string token = null;
                bool havePid = false, haveStart = false, haveStop = false;
                for (int i = 0; args != null && i < args.Length; i++)
                {
                    string arg = args[i];
                    if (arg == "--owner-pid")
                    { if (havePid || ++i >= args.Length || !int.TryParse(args[i], out pid)) return null; havePid = true; }
                    else if (arg == "--owner-start")
                    { if (haveStart || ++i >= args.Length || !long.TryParse(args[i], out ticks)) return null; haveStart = true; }
                    else if (arg == "--owner-stop")
                    { if (haveStop || ++i >= args.Length) return null; token = args[i]; haveStop = true; }
                    else if (arg == "--owner-window")
                    { if (++i >= args.Length || !long.TryParse(args[i], out gameWindow)) return null; }
                }
                Guid parsed;
                if (pid <= 0 || ticks <= 0 || token == null || token.Length != 32 || !Guid.TryParseExact(token, "N", out parsed)) return null;
                process = Process.GetProcessById(pid);
                var handle = process.Handle; // Hold this exact process, not a future PID reuse.
                if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != ticks) { process.Dispose(); return null; }
                signal = EventWaitHandle.OpenExisting("Local\\ZeoOverlayStop_" + token);
                if (signal.WaitOne(0)) { process.Dispose(); signal.Dispose(); return null; }
                return new OverlayLifetime(process, signal, gameWindow, requestClose);
            }
            catch { if (process != null) process.Dispose(); if (signal != null) signal.Dispose(); return null; }
        }

        private void Watch()
        {
            while (!disposed)
            {
                bool ended;
                try { ended = stop.WaitOne(200) || owner.HasExited || GameWindowEnded(); }
                catch { ended = true; }
                if (!ended) continue;
                if (disposed) return;
                // The UI may be blocked or a hide-on-close menu may cancel Exit.
                // Give normal cleanup a chance, then terminate only THIS overlay.
                ThreadPool.QueueUserWorkItem(delegate { try { requestClose(); } catch { } });
                Thread.Sleep(2000);
                if (!disposed) Environment.Exit(0);
                return;
            }
        }

        private bool ValidWindow(IntPtr handle)
        {
            uint pid;
            return handle != IntPtr.Zero && IsWindow(handle) &&
                GetWindowThreadProcessId(handle, out pid) != 0 && pid == owner.Id;
        }

        private bool GameWindowEnded()
        {
            // IsWindow does not depend on focus, visibility or minimization.
            if (ValidWindow(window)) { sawWindow = true; missingWindow = null; return false; }
            owner.Refresh();
            var replacement = owner.MainWindowHandle;
            if (ValidWindow(replacement)) { window = replacement; sawWindow = true; missingWindow = null; return false; }
            if (!sawWindow) return false; // Startup: no game window observed yet.
            if (missingWindow == null) missingWindow = Stopwatch.StartNew();
            return missingWindow.ElapsedMilliseconds >= 10000;
        }

        public void Dispose()
        {
            disposed = true;
            if (Thread.CurrentThread != monitor) monitor.Join(2500);
            stop.Dispose(); owner.Dispose();
        }
    }
}
