using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZeoCore
{
    internal struct GameWindowSnapshot
    {
        public bool Valid;
        public int Left;
        public int Top;
        public int Width;
        public int Height;
        public bool Focused;
        public long WindowHandle;
        public int ProcessId;
    }

    internal static class GameWindowState
    {
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hwnd, ref POINT lpPoint);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        internal static GameWindowSnapshot Capture()
        {
            var snap = new GameWindowSnapshot();
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    IntPtr hwnd = process.MainWindowHandle;
                    RECT r = new RECT();
                    bool ok = false;

                    // v0.4.3: use the actual CLIENT area first. IMyCamera.WorldToScreen
                    // projects into the game render viewport, not the DWM title-bar/frame.
                    // Using extended frame bounds was enough to make markers appear to
                    // drift away from ships, especially in windowed/borderless layouts.
                    if (hwnd != IntPtr.Zero)
                    {
                        try
                        {
                            RECT client;
                            POINT origin = new POINT();
                            if (GetClientRect(hwnd, out client) &&
                                client.Right > client.Left && client.Bottom > client.Top &&
                                ClientToScreen(hwnd, ref origin))
                            {
                                snap.Valid = true;
                                snap.Left = origin.X;
                                snap.Top = origin.Y;
                                snap.Width = client.Right - client.Left;
                                snap.Height = client.Bottom - client.Top;
                                ok = snap.Width >= 200 && snap.Height >= 200;
                            }
                        }
                        catch { ok = false; }

                        if (!ok)
                        {
                            try
                            {
                                int hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT)));
                                ok = hr == 0 && r.Right > r.Left && r.Bottom > r.Top;
                            }
                            catch { r = new RECT(); }

                            if (!ok)
                            {
                                try { ok = GetWindowRect(hwnd, out r) && r.Right > r.Left && r.Bottom > r.Top; }
                                catch { r = new RECT(); ok = false; }
                            }

                            if (ok)
                            {
                                snap.Valid = true;
                                snap.Left = r.Left;
                                snap.Top = r.Top;
                                snap.Width = r.Right - r.Left;
                                snap.Height = r.Bottom - r.Top;
                            }
                        }
                    }

                    try
                    {
                        IntPtr actualHwnd = process.MainWindowHandle;
                        uint gamePid = 0;
                        if (actualHwnd != IntPtr.Zero)
                        {
                            GetWindowThreadProcessId(actualHwnd, out gamePid);
                            snap.WindowHandle = actualHwnd.ToInt64();
                        }
                        if (gamePid == 0) gamePid = (uint)process.Id;
                        snap.ProcessId = unchecked((int)gamePid);

                        IntPtr fg = GetForegroundWindow();
                        uint fgPid = 0;
                        if (fg != IntPtr.Zero) GetWindowThreadProcessId(fg, out fgPid);
                        snap.Focused = (actualHwnd != IntPtr.Zero && fg == actualHwnd) ||
                                       (gamePid != 0 && fgPid == gamePid);
                    }
                    catch { snap.Focused = false; }
                }
            }
            catch { }
            return snap;
        }
    }
}
