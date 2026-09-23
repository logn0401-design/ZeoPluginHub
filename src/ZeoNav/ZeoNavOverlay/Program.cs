using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;

namespace ZeoNavOverlay
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            using (var lifetime = Zeo.Shared.OverlayLifetime.Attach(args, () => Application.Exit()))
            {
                if (lifetime == null) return;

            int commandPort = 0;
            int ownerPid = 0;

            for (int i = 0; args != null && i < args.Length; i++)
            {
                if (args[i].Equals("--command-port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    int.TryParse(args[++i], out commandPort);
                else if (args[i].Equals("--owner-pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    int.TryParse(args[++i], out ownerPid);
            }

            if (commandPort <= 1024 || commandPort >= 65535) return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new OverlayContext(commandPort, ownerPid));
        
            }
        }
    }

    internal sealed class OverlayContext : ApplicationContext
    {
        private readonly SnapshotReceiver receiver = new SnapshotReceiver();
        // v0.1.19: create the click-through layered HUD lazily. Merely touching a
        // WinForms .Handle creates the native HWND; older builds did that every Tick
        // even while the trip HUD was "hidden", leaving a path for a large white
        // click-through surface at game startup.
        private HudForm hud;
        private readonly MenuForm menu = new MenuForm();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly int commandPort;
        private readonly int ownerPid;
        private NavSnapshot last;

        public OverlayContext(int commandPort, int ownerPid)
        {
            this.commandPort = commandPort;
            this.ownerPid = ownerPid;
            receiver.Start();
            Send(new NavCommand { Type = "HELLO", Value = receiver.LocalPort });
            // Do not instantiate the layered HUD here. No HUD HWND exists until an
            // actual trip panel is requested and we have a valid game client rectangle.
            menu.Command += Send;
            menu.FormClosing += (s, e) => { e.Cancel = true; menu.Hide(); Send(new NavCommand { Type = "MENU_CLOSE" }); };
            timer.Interval = 50;
            timer.Tick += Tick;
            timer.Start();
        }

        private void Tick(object sender, EventArgs e)
        {
            NavSnapshot s = receiver.Latest;
            if (s == null) return;
            last = s;
            bool streamerMode = s.Config == null || s.Config.StreamerMode;
            menu.SetStreamerMode(streamerMode);
            menu.Apply(s);

            // Never read menu.Handle/hud.Handle unless that window already owns a
            // native handle. Handle getters create HWNDs, which was the hidden white-box
            // startup path. The HUD is WS_EX_NOACTIVATE/click-through, so it never needs
            // to count as the foreground owner.
            IntPtr foreground = Native.GetForegroundWindow();
            bool menuFocus = menu.Visible && menu.IsHandleCreated && foreground == menu.Handle;
            bool gameFocus = s.GameHwnd != 0 && foreground.ToInt64() == s.GameHwnd;
            bool allowed = gameFocus || menuFocus;

            if (s.MenuVisible && allowed)
            {
                if (!menu.Visible) { menu.PrepareForShow(); menu.ApplyTheme(); menu.CenterOnGame(s); menu.Show(); menu.Activate(); }
            }
            else if ((!s.MenuVisible || !allowed) && menu.Visible) menu.Hide();

            string tripMode = s.Config == null ? "AUTO" : (s.Config.TripPanelVisibility ?? "AUTO").Trim().ToUpperInvariant();
            bool tripRequested = tripMode == "ALWAYS" ? true : tripMode == "HIDDEN" ? false : s.HudVisible;
            bool layoutPreview=s.Layout!=null;
            tripRequested=tripRequested || layoutPreview;
            if (tripRequested && allowed && s.ClientW > 10 && s.ClientH > 10)
            {
                if (hud == null || hud.IsDisposed)
                    hud = new HudForm();

                hud.SetStreamerMode(streamerMode);
                NavSnapshot rendered=LayoutPreview(s);
                hud.Apply(rendered);
                hud.Render(rendered, new Rectangle(s.ClientX, s.ClientY, s.ClientW, s.ClientH));
                if(layoutPreview)
                {
                    RectangleF bounds=hud.LastPanelBounds;
                    Send(new NavCommand { Type="LAYOUT_BOUNDS",LayoutBounds=new NavLayoutBounds {
                        Token=s.Layout.Token,X=bounds.X,Y=bounds.Y,Width=bounds.Width,Height=bounds.Height,ViewportW=s.ClientW,ViewportH=s.ClientH
                    }});
                }
            }
            else if (hud != null)
            {
                hud.EnsureHidden();

                // AUTO/HIDDEN while inactive should leave no layered HWND behind at all.
                // Recreate it on demand next time navigation actually needs the panel.
                if (!tripRequested)
                {
                    try { hud.Dispose(); } catch { }
                    hud = null;
                }
            }
        }

        private void Send(NavCommand cmd)
        {
            try
            {
                byte[] b = JsonIo.ToBytes(cmd);
                using (var u = new UdpClient()) u.Send(b, b.Length, new IPEndPoint(IPAddress.Loopback, commandPort));
            }
            catch { }
        }

        internal static NavSnapshot LayoutPreview(NavSnapshot source)
        {
            if(source.Layout==null) return source;
            var preview=source.Copy(); preview.Config=source.Config.Copy();
            preview.Config.HudX=source.Layout.X; preview.Config.HudY=source.Layout.Y;
            return preview;
        }

        protected override void ExitThreadCore()
        {
            try { receiver.Dispose(); } catch { }
            try { timer.Dispose(); } catch { }
            try { if (hud != null) hud.Dispose(); } catch { }
            try { menu.Dispose(); } catch { }
            base.ExitThreadCore();
        }
    }

    internal sealed class SnapshotReceiver : IDisposable
    {
        private UdpClient client;
        private Thread thread;
        private volatile bool run;
        private NavSnapshot latest;
        private readonly object gate = new object();
        public NavSnapshot Latest { get { lock (gate) return latest; } }
        public int LocalPort { get; private set; }

        public void Start()
        {
            client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            LocalPort = ((IPEndPoint)client.Client.LocalEndPoint).Port;
            run = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "ZeoNavOverlayRx" };
            thread.Start();
        }
        private void Loop()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (run)
            {
                try
                {
                    byte[] b = client.Receive(ref ep);
                    NavSnapshot s = JsonIo.FromBytes<NavSnapshot>(b);
                    if (s != null) lock (gate) latest = s;
                }
                catch (SocketException) { if (run) Thread.Sleep(25); }
                catch { Thread.Sleep(50); }
            }
        }
        public void Dispose() { run = false; try { client.Close(); } catch { } }
    }

    internal static class JsonIo
    {
        public static byte[] ToBytes<T>(T obj) { using (var ms = new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(ms, obj); return ms.ToArray(); } }
        public static T FromBytes<T>(byte[] b) where T : class { try { using (var ms = new MemoryStream(b)) return new DataContractJsonSerializer(typeof(T)).ReadObject(ms) as T; } catch { return null; } }
    }

    internal static class Native
    {
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const uint WDA_NONE = 0x0;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
        public const int ULW_ALPHA = 0x00000002;
        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;
        public const int SW_HIDE = 0;
        public const int SW_SHOWNA = 8;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        public const uint BI_RGB = 0;
        public const uint DIB_RGB_COLORS = 0;

        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    }

    internal sealed class ThemePalette
    {
        public Color Text, Secondary, Back, Border, Accent, Good, Warning, Danger;
        public Color MenuBack, MenuPanel, MenuText, MenuAccent;

        public static ThemePalette From(NavConfig c)
        {
            string n = (c.Theme ?? "WAR ROOM").Trim().ToUpperInvariant();
            if (n == "GRAPHITE")
                return P("#D7D9DC", "#A9ADB2", "#2B2E32", "#666C72", "#D7A04B", "#8FD3B0", "#E6D28A", "#E07872", "#202327", "#2A2E33", "#D7D9DC", "#AEB5BC");
            if (n == "MONOCHROME")
                return P("#D8D8D8", "#A5A5A5", "#272727", "#686868", "#D8D8D8", "#C6C6C6", "#FFFFFF", "#F0F0F0", "#1F1F1F", "#292929", "#D8D8D8", "#A8A8A8");
            if (n == "AMBER")
                return P("#E3D6B0", "#B5A77E", "#2D2A24", "#746A50", "#D8BE76", "#A8D0A2", "#F1D88C", "#E48168", "#211F1A", "#2B2923", "#E3D6B0", "#C1AE75");
            if (n == "HIGH CONTRAST")
                return P("#E8ECF1", "#B8BEC5", "#101419", "#7B858E", "#FFB84A", "#4DE1FF", "#FFE16B", "#FF5A5F", "#101419", "#1A2026", "#E8ECF1", "#F2C94C");
            if (n == "CUSTOM")
                return P(c.HudText, c.SecondaryText, c.PanelBacking, c.PanelBorder, c.Accent, c.Good, c.Warning, c.Danger, c.MenuBackground, c.MenuPanel, c.MenuText, c.MenuAccent);
            // Exact ZeoCore v0.6.1 WAR ROOM preset.
            return P("#F1F3F5", "#9CA3AB", "#090B0E", "#414850", "#C8CDD2", "#B8D7E8", "#FFFFFF", "#F04444", "#07090B", "#111419", "#F1F3F5", "#D52B2B");
        }

        public static ThemePalette ForTrip(NavConfig c)
        {
            ThemePalette basePalette = From(c);
            if (c == null || c.TripUseHudTheme) return basePalette;
            return new ThemePalette
            {
                Text = C(c.TripHudText), Secondary = C(c.TripSecondaryText),
                Back = C(c.TripPanelBacking), Border = C(c.TripPanelBorder), Accent = C(c.TripAccent),
                Good = basePalette.Good, Warning = basePalette.Warning, Danger = basePalette.Danger,
                MenuBack = basePalette.MenuBack, MenuPanel = basePalette.MenuPanel,
                MenuText = basePalette.MenuText, MenuAccent = basePalette.MenuAccent
            };
        }

        private static ThemePalette P(string text, string secondary, string back, string border, string accent, string good, string warning, string danger, string menuBack, string menuPanel, string menuText, string menuAccent)
        {
            return new ThemePalette
            {
                Text = C(text), Secondary = C(secondary), Back = C(back), Border = C(border), Accent = C(accent), Good = C(good), Warning = C(warning), Danger = C(danger),
                MenuBack = C(menuBack), MenuPanel = C(menuPanel), MenuText = C(menuText), MenuAccent = C(menuAccent)
            };
        }
        private static Color C(string s) { try { return ColorTranslator.FromHtml(s); } catch { return Color.White; } }
    }


    internal sealed class ZeoCoreAppearance
    {
        public int FrameStyle = -1;
        public int FontStyle = -1;
        public int PanelOpacity = -1;
        public double PanelPaddingScale = double.NaN;
        public double BorderWidth = double.NaN;
        public string HudTextColor;
        public string HudSecondaryColor;
        public string HudPanelColor;
        public string HudBorderColor;
        public string SpectrumColor;
        public string FriendlyColor;
        public string FocusColor;
        public string HostileColor;
        public string MenuBackgroundColor;
        public string MenuPanelColor;
        public string MenuTextColor;
        public string MenuAccentColor;

        private static readonly object Gate = new object();
        private static DateTime lastCheckUtc = DateTime.MinValue;
        private static DateTime lastWriteUtc = DateTime.MinValue;
        private static ZeoCoreAppearance cached;

        public static ZeoCoreAppearance Get()
        {
            lock (Gate)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - lastCheckUtc).TotalMilliseconds < 900) return cached;
                lastCheckUtc = now;
                try
                {
                    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Pulsar", "ZeoCore", "hud-settings.json");
                    if (!File.Exists(path)) { cached = null; lastWriteUtc = DateTime.MinValue; return null; }
                    DateTime write = File.GetLastWriteTimeUtc(path);
                    if (cached != null && write == lastWriteUtc) return cached;
                    string json = File.ReadAllText(path);
                    var a = new ZeoCoreAppearance();
                    a.FrameStyle = JsonInt(json, "FrameStyle", -1);
                    a.FontStyle = JsonInt(json, "FontStyle", -1);
                    a.PanelOpacity = JsonInt(json, "PanelOpacity", -1);
                    a.PanelPaddingScale = JsonDouble(json, "PanelPaddingScale", double.NaN);
                    a.BorderWidth = JsonDouble(json, "BorderWidth", double.NaN);
                    a.HudTextColor = JsonString(json, "HudTextColor");
                    a.HudSecondaryColor = JsonString(json, "HudSecondaryColor");
                    a.HudPanelColor = JsonString(json, "HudPanelColor");
                    a.HudBorderColor = JsonString(json, "HudBorderColor");
                    a.SpectrumColor = JsonString(json, "SpectrumColor");
                    a.FriendlyColor = JsonString(json, "FriendlyColor");
                    a.FocusColor = JsonString(json, "FocusColor");
                    a.HostileColor = JsonString(json, "HostileColor");
                    a.MenuBackgroundColor = JsonString(json, "MenuBackgroundColor");
                    a.MenuPanelColor = JsonString(json, "MenuPanelColor");
                    a.MenuTextColor = JsonString(json, "MenuTextColor");
                    a.MenuAccentColor = JsonString(json, "MenuAccentColor");
                    cached = a;
                    lastWriteUtc = write;
                }
                catch { cached = null; }
                return cached;
            }
        }

        public ThemePalette ToPalette(ThemePalette fallback)
        {
            return new ThemePalette
            {
                Text = ColorOf(HudTextColor, fallback.Text),
                Secondary = ColorOf(HudSecondaryColor, fallback.Secondary),
                Back = ColorOf(HudPanelColor, fallback.Back),
                Border = ColorOf(HudBorderColor, fallback.Border),
                Accent = ColorOf(SpectrumColor, fallback.Accent),
                Good = ColorOf(FriendlyColor, fallback.Good),
                Warning = ColorOf(FocusColor, fallback.Warning),
                Danger = ColorOf(HostileColor, fallback.Danger),
                MenuBack = ColorOf(MenuBackgroundColor, fallback.MenuBack),
                MenuPanel = ColorOf(MenuPanelColor, fallback.MenuPanel),
                MenuText = ColorOf(MenuTextColor, fallback.MenuText),
                MenuAccent = ColorOf(MenuAccentColor, fallback.MenuAccent)
            };
        }

        private static Color ColorOf(string value, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            try { return ColorTranslator.FromHtml(value); } catch { return fallback; }
        }

        private static string JsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;
            string needle = "\"" + key + "\"";
            int p = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (p < 0) return null;
            p = json.IndexOf(':', p + needle.Length);
            if (p < 0) return null;
            p++;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            if (p >= json.Length || json[p] != '"') return null;
            p++;
            int start = p;
            while (p < json.Length)
            {
                if (json[p] == '"' && (p == start || json[p - 1] != '\\')) return json.Substring(start, p - start);
                p++;
            }
            return null;
        }

        private static int JsonInt(string json, string key, int fallback)
        {
            double d = JsonDouble(json, key, double.NaN);
            return double.IsNaN(d) ? fallback : (int)Math.Round(d);
        }

        private static double JsonDouble(string json, string key, double fallback)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return fallback;
            string needle = "\"" + key + "\"";
            int p = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (p < 0) return fallback;
            p = json.IndexOf(':', p + needle.Length);
            if (p < 0) return fallback;
            p++;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            int start = p;
            while (p < json.Length && ("-+0123456789.eE".IndexOf(json[p]) >= 0)) p++;
            double v;
            return double.TryParse(json.Substring(start, p - start), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v) ? v : fallback;
        }
    }

    internal sealed class HudForm : Form
    {
        internal RectangleF LastPanelBounds { get; private set; }
        private NavSnapshot s;
        private bool streamerMode = true;
        public bool CaptureApplied { get; private set; }
        public HudForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(0, 0, 1, 1);
            // IMPORTANT: no TransparencyKey/chroma key. This is a true per-pixel-alpha
            // layered window, matching ZeoCore. Anti-aliased edges therefore cannot turn
            // magenta/purple when the trip HUD appears.
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyCaptureAffinity(); }

        public void EnsureHidden()
        {
            if (!IsHandleCreated) return;
            try { Native.ShowWindow(Handle, Native.SW_HIDE); } catch { }
        }

        public void SetStreamerMode(bool enabled)
        {
            if (streamerMode == enabled) return;
            streamerMode = enabled;
            ApplyCaptureAffinity();
        }
        private void ApplyCaptureAffinity()
        {
            if (!IsHandleCreated) return;
            try
            {
                uint desired = streamerMode ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE;
                bool ok = Native.SetWindowDisplayAffinity(Handle, desired);
                uint actual;
                bool verified = ok && Native.GetWindowDisplayAffinity(Handle, out actual) && actual == desired;
                CaptureApplied = streamerMode && verified;
            }
            catch { CaptureApplied = false; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The HUD is presented only through UpdateLayeredWindow. Never let normal
            // WinForms background painting expose an opaque client rectangle.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Intentionally empty; DrawHud renders into the alpha bitmap in Render().
        }

        public void Apply(NavSnapshot x) { s = x; }

        public void Render(NavSnapshot snapshot, Rectangle gameBounds)
        {
            s = snapshot;
            if (s == null || s.Config == null || gameBounds.Width < 2 || gameBounds.Height < 2) return;
            if (!IsHandleCreated)
            {
                CreateControl();
                // Fail closed during first-handle creation. Only Present() is allowed to
                // make the layered HUD visible after UpdateLayeredWindow succeeds.
                try { Native.ShowWindow(Handle, Native.SW_HIDE); } catch { }
            }

            // v0.1.15+: NEVER make the click-through layered HWND the size of the game.
            // The old full-client layered surface was the remaining path that could turn
            // into a giant white click-through rectangle if Windows/GDI lost alpha.
            // Render only the actual trip plate plus a tiny anti-alias margin.
            RectangleF panel = CalculatePanelBounds(gameBounds.Width, gameBounds.Height);
            LastPanelBounds=panel;
            const int margin = 4;
            int bitmapW = Math.Max(8, (int)Math.Ceiling(panel.Width) + margin * 2);
            int bitmapH = Math.Max(8, (int)Math.Ceiling(panel.Height) + margin * 2);
            RectangleF localPanel = new RectangleF(margin, margin, panel.Width, panel.Height);

            using (var bitmap = new Bitmap(bitmapW, bitmapH, PixelFormat.Format32bppPArgb))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingMode = CompositingMode.SourceOver;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // ClearType assumes an opaque background and is the source of colored
                // fringes on transparent overlays. AntiAliasGridFit is alpha-safe.
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                DrawPanelContent(g,localPanel,panel);
                Present(bitmap,
                    gameBounds.Left + (int)Math.Floor(panel.Left) - margin,
                    gameBounds.Top + (int)Math.Floor(panel.Top) - margin);
            }
        }

        private RectangleF CalculatePanelBounds(int width, int height)
        {
            NavConfig c = s.Config;
            ZeoCoreAppearance zeo = c.TripUseHudTheme ? ZeoCoreAppearance.Get() : null;
            double effectivePadding = zeo != null && !double.IsNaN(zeo.PanelPaddingScale) ? zeo.PanelPaddingScale : c.InnerPadding;
            float panelScale = (float)(c.PanelScale * c.GlobalScale);
            float maxHorizontal = (float)Math.Max(1.0, Math.Max(Math.Max(c.DestinationScale, c.DistanceScale), Math.Max(c.SpeedScale, c.SignalScale)));
            float pad = 16 * panelScale * (float)effectivePadding;
            float titleRow = 31 * panelScale * (float)Math.Max(c.DestinationScale, c.DistanceScale);
            float phaseRow = 34 * panelScale * (float)Math.Max(c.PhaseScale, c.EtaScale);
            float speedRow = 25 * panelScale * (float)c.SpeedScale;
            float signalRow = 30 * panelScale * (float)c.SignalScale;
            float progressRow = (11 + 8 * (float)c.ProgressScale) * panelScale;
            float flipRow = 25 * panelScale * (float)c.FlipScale;
            float stopRow = 24 * panelScale * (float)c.StopScale;
            float warningRow = 27 * panelScale * (float)c.WarningScale;
            float w = 440 * panelScale * Math.Min(1.55f, 1.0f + (maxHorizontal - 1.0f) * .22f);
            float h = pad * 2 + titleRow + phaseRow + speedRow + signalRow + progressRow + flipRow + stopRow + warningRow;
            float cx = (float)((c.HudX + 1.0) * .5 * width);
            float cy = (float)((1.0 - (c.HudY + 1.0) * .5) * height);
            return new RectangleF(
                Clamp(cx, 6, Math.Max(6, width - w - 6)),
                Clamp(cy, 6, Math.Max(6, height - h - 6)),
                Math.Min(w, Math.Max(100, width - 12)),
                Math.Min(h, Math.Max(100, height - 12)));
        }

        // Actual renderer entry point shared with offline layout fixtures. Hidden panels
        // show only an outline; previews never fabricate flight/sensor data or enable HUD visibility.
        internal void DrawPanelContent(Graphics g,RectangleF localPanel,RectangleF clientPanel)
        {
            if(s.Layout==null) { DrawHud(g,localPanel); return; }
            string mode=(s.Config.TripPanelVisibility??"AUTO").ToUpperInvariant();
            bool actual=mode!="HIDDEN" && (mode=="ALWAYS" || s.HudVisible);
            if(actual) DrawHud(g,localPanel);
            using(var pen=new Pen(Color.FromArgb(210,145,222,238),2)) g.DrawRectangle(pen,localPanel.X,localPanel.Y,localPanel.Width-1,localPanel.Height-1);
            if(!actual)
                using(var brush=new SolidBrush(Color.FromArgb(225,192,234,245)))
                using(var font=new Font("Segoe UI",10,FontStyle.Bold)) g.DrawString("TRIP HUD / POSITION PREVIEW",font,brush,localPanel.X+10,localPanel.Y+8);
            // Keep the native editor toolbar clickable/readable even behind this panel.
            RectangleF toolbar=new RectangleF((float)s.Layout.ToolbarX-clientPanel.X+localPanel.X,
                (float)s.Layout.ToolbarY-clientPanel.Y+localPanel.Y,(float)s.Layout.ToolbarW,(float)s.Layout.ToolbarH);
            var oldMode=g.CompositingMode;
            g.CompositingMode=CompositingMode.SourceCopy;
            using(var clear=new SolidBrush(Color.Transparent)) g.FillRectangle(clear,toolbar);
            g.CompositingMode=oldMode;
        }

        private void DrawHud(Graphics g, RectangleF r)
        {
            if (s == null || s.Config == null) return;
            NavConfig c = s.Config;
            ThemePalette p = ThemePalette.ForTrip(c);
            ZeoCoreAppearance zeo = c.TripUseHudTheme ? ZeoCoreAppearance.Get() : null;
            if (zeo != null) p = zeo.ToPalette(p);
            int effectiveFrame = zeo != null && zeo.FrameStyle >= 0 && zeo.FrameStyle <= 13 ? zeo.FrameStyle : FrameIndex(c.Frame);
            int effectiveFont = zeo != null && zeo.FontStyle >= 0 ? zeo.FontStyle : -1;
            double effectiveBorderWidth = zeo != null && !double.IsNaN(zeo.BorderWidth) ? zeo.BorderWidth : c.BorderWidth;
            double effectivePadding = zeo != null && !double.IsNaN(zeo.PanelPaddingScale) ? zeo.PanelPaddingScale : c.InnerPadding;
            int effectiveOpacity = zeo != null && zeo.PanelOpacity >= 0 ? zeo.PanelOpacity : c.BackingOpacity;
            float panelScale = (float)(c.PanelScale * c.GlobalScale);
            float pad = 16 * panelScale * (float)effectivePadding;
            float titleRow = 31 * panelScale * (float)Math.Max(c.DestinationScale, c.DistanceScale);
            float phaseRow = 34 * panelScale * (float)Math.Max(c.PhaseScale, c.EtaScale);
            float speedRow = 25 * panelScale * (float)c.SpeedScale;
            float signalRow = 30 * panelScale * (float)c.SignalScale;
            float progressRow = (11 + 8 * (float)c.ProgressScale) * panelScale;
            float flipRow = 25 * panelScale * (float)c.FlipScale;
            float stopRow = 24 * panelScale * (float)c.StopScale;
            float warningRow = 27 * panelScale * (float)c.WarningScale;
            DrawFrame(g, r, c, p, effectiveFrame, effectiveBorderWidth, effectiveOpacity);
            float x = r.X + pad, y = r.Y + pad;
            float usable = r.Width - pad * 2;

            DrawText(g, "ZEO NAV // " + Safe(s.Destination, "NO ROUTE"), p.Text, x, y, 15, c.DestinationScale * c.GlobalScale, FontStyle.Bold, HudTitleFont(c, effectiveFrame, effectiveFont));
            DrawTextRight(g, Dist(s.DistanceMeters), p.Secondary, x + usable, y + 2, 13, c.DistanceScale * c.GlobalScale, HudBodyFont(c, true, effectiveFrame, effectiveFont));
            y += titleRow;
            Color stateColor = StateColor(c, p, s.Phase);
            DrawText(g, s.Phase ?? "DISARMED", stateColor, x, y, 18, c.PhaseScale * c.GlobalScale, FontStyle.Bold, HudTitleFont(c, effectiveFrame, effectiveFont));
            DrawTextRight(g, "ETA " + Time(s.EtaSeconds), p.Text, x + usable, y + 2, 14, c.EtaScale * c.GlobalScale, HudBodyFont(c, true, effectiveFrame, effectiveFont));
            y += phaseRow;
            bool capReady = s.SpeedCapMps > 1 && (s.SpeedCapSource ?? "").IndexOf("WAIT", StringComparison.OrdinalIgnoreCase) < 0;
            string speedLine = capReady
                ? "SPD " + Speed(s.SpeedMps) + " / " + Speed(s.CommandSpeedMps) + "   CAP " + Speed(s.SpeedCapMps)
                : "SPD " + Speed(s.SpeedMps) + "   //   CAP WAIT SHIPCORE";
            DrawText(g, speedLine, p.Text, x, y, 13, c.SpeedScale * c.GlobalScale, FontStyle.Bold, HudBodyFont(c, true, effectiveFrame, effectiveFont));
            y += speedRow;
            string sig = s.SpectrumKmReady ? "SIG " + SigKmText(s.SpectrumDriveKm) + " / MAX " + SigKmText(s.MaxDriveSigKm) : "SIG KM WAIT / MAX " + SigKmText(s.MaxDriveSigKm);
            DrawText(g, sig, p.Accent, x, y, 12, c.SignalScale * c.GlobalScale, FontStyle.Bold, HudBodyFont(c, true, effectiveFrame, effectiveFont));
            DrawTextRight(g, "THRUST " + (s.ForwardCommandRatio * 100).ToString("0") + "%", p.Secondary, x + usable, y, 11, c.SignalScale * c.GlobalScale, HudBodyFont(c, true, effectiveFrame, effectiveFont));
            y += signalRow;

            float progH = 8 * (float)c.ProgressScale * panelScale;
            using (var bg = new SolidBrush(Color.FromArgb(100, p.Border))) g.FillRectangle(bg, x, y, usable, progH);
            using (var fg = new SolidBrush(stateColor)) g.FillRectangle(fg, x, y, usable * (float)Math.Max(0, Math.Min(1, s.Progress01)), progH);
            y += progressRow;

            if (s.ManualFlipActive)
            {
                DrawText(g, "MANUAL FLIP  " + s.ManualFlipDegreesLeft.ToString("0") + "° LEFT", p.Warning, x, y, 16, c.FlipScale * c.GlobalScale, FontStyle.Bold, HudTitleFont(c, effectiveFrame, effectiveFont));
            }
            else
            {
                DrawText(g, "FLIP " + (s.FlipInSeconds < 0 ? "--:--" : Time(s.FlipInSeconds)) + "  @ " + Dist(s.FlipAtMeters), p.Warning, x, y, 12, c.FlipScale * c.GlobalScale, FontStyle.Bold, HudBodyFont(c, true, effectiveFrame, effectiveFont));
                y += flipRow;
                DrawText(g, "STOP " + Dist(s.StopDistanceMeters) + "   LAT " + s.LateralMps.ToString("0.0") + " M/S", p.Secondary, x, y, 11, c.StopScale * c.GlobalScale, FontStyle.Regular, HudBodyFont(c, true, effectiveFrame, effectiveFont));
            }
            if (!string.IsNullOrWhiteSpace(s.WarningText))
                DrawText(g, s.WarningText, p.Danger, x, r.Bottom - warningRow, 11, c.WarningScale * c.GlobalScale, FontStyle.Bold, HudBodyFont(c, false, effectiveFrame, effectiveFont));
        }

        private void Present(Bitmap bitmap, int left, int top)
        {
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr dibBits = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            BitmapData locked = null;
            try
            {
                screenDc = Native.GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero) { EnsureHidden(); return; }

                memDc = Native.CreateCompatibleDC(screenDc);
                if (memDc == IntPtr.Zero) { EnsureHidden(); return; }

                var bmi = new Native.BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = bitmap.Width;
                // Negative height creates a top-down 32-bit DIB, matching GDI+ row order.
                bmi.bmiHeader.biHeight = -bitmap.Height;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = Native.BI_RGB;
                bmi.bmiHeader.biSizeImage = (uint)(bitmap.Width * bitmap.Height * 4);

                hBitmap = Native.CreateDIBSection(screenDc, ref bmi, Native.DIB_RGB_COLORS, out dibBits, IntPtr.Zero, 0);
                if (hBitmap == IntPtr.Zero || dibBits == IntPtr.Zero) { EnsureHidden(); return; }

                // Do not use Bitmap.GetHbitmap here. GetHbitmap can discard/normalize
                // the alpha channel on some Windows/GDI paths. Copy the premultiplied
                // BGRA bytes directly into a 32-bit DIB section instead.
                locked = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppPArgb);

                int rowBytes = bitmap.Width * 4;
                int srcStride = locked.Stride;
                byte[] row = new byte[rowBytes];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    int srcOffset = srcStride >= 0 ? y * srcStride : (bitmap.Height - 1 - y) * (-srcStride);
                    IntPtr srcRow = IntPtr.Add(locked.Scan0, srcOffset);
                    IntPtr dstRow = IntPtr.Add(dibBits, y * rowBytes);
                    Marshal.Copy(srcRow, row, 0, rowBytes);
                    Marshal.Copy(row, 0, dstRow, rowBytes);
                }
                bitmap.UnlockBits(locked);
                locked = null;

                oldBitmap = Native.SelectObject(memDc, hBitmap);
                var dst = new Native.POINT(left, top);
                var src = new Native.POINT(0, 0);
                var size = new Native.SIZE(bitmap.Width, bitmap.Height);
                var blend = new Native.BLENDFUNCTION
                {
                    BlendOp = Native.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = Native.AC_SRC_ALPHA
                };

                bool presented = Native.UpdateLayeredWindow(
                    Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);

                if (presented) Native.ShowWindow(Handle, Native.SW_SHOWNA);
                else EnsureHidden();
            }
            catch
            {
                EnsureHidden();
            }
            finally
            {
                if (locked != null) { try { bitmap.UnlockBits(locked); } catch { } }
                if (oldBitmap != IntPtr.Zero && memDc != IntPtr.Zero) Native.SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) Native.DeleteObject(hBitmap);
                if (memDc != IntPtr.Zero) Native.DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static void DrawFrame(Graphics g, RectangleF rect, NavConfig cfg, ThemePalette palette, int frameStyle, double borderWidth, int backingOpacity)
        {
            Color panel = palette.Back;
            Color border = palette.Border;
            // Active navigation panels use the HUD accent, never the menu accent.
            // The v0.1.5 War Room renderer accidentally pulled the red settings-menu
            // accent into the trip HUD, making it look like a separate UI product.
            // Match ZeoCore exactly: War Room-family frames (6+) use the command/menu
            // accent for their rails; older flight frames use the HUD/Spectrum accent.
            Color accent = frameStyle >= 6 ? palette.MenuAccent : palette.Accent;
            float bw = (float)borderWidth;
            int opacity = Math.Max(0, Math.Min(245, backingOpacity));
            float x = rect.Left, y = rect.Top, r = rect.Right, b = rect.Bottom;

            if (frameStyle == 0) // SE INDUSTRIAL - preserved known-good plate
            {
                float cut = Math.Max(6f, Math.Min(18f, Math.Min(rect.Width, rect.Height) * .07f));
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[] { new PointF(x+cut,y), new PointF(r-cut,y), new PointF(r,y+cut), new PointF(r,b-cut), new PointF(r-cut,b), new PointF(x+cut,b), new PointF(x,b-cut), new PointF(x,y+cut) });
                    using (var fill = new SolidBrush(Color.FromArgb(opacity, panel))) g.FillPath(fill, path);
                    using (var pen = new Pen(Color.FromArgb(Math.Min(210, opacity), border), bw)) g.DrawPath(pen, path);
                }
                using (var ap = new Pen(Color.FromArgb(235, accent), Math.Max(2f, bw*1.8f)))
                { g.DrawLine(ap, x+cut, y+1f, Math.Min(r-cut, x+cut+82f), y+1f); g.DrawLine(ap, x+1f, y+cut, x+1f, Math.Min(b-cut, y+cut+48f)); }
                return;
            }

            if (frameStyle == 1) // FIGHTER HUD - open frame, corner gates + sight rails
            {
                using (var fill = new LinearGradientBrush(rect, Color.FromArgb(Math.Min(opacity, 82), panel), Color.FromArgb(Math.Min(opacity, 24), panel), 90f))
                    g.FillRectangle(fill, rect);
                float c = Math.Max(18f, Math.Min(34f, rect.Height * .22f));
                using (var p = new Pen(Color.FromArgb(205, border), Math.Max(1f, bw)))
                using (var ap = new Pen(Color.FromArgb(245, accent), Math.Max(1.5f, bw*1.45f)))
                {
                    // open corners: deliberately no enclosing rectangle
                    g.DrawLine(ap,x,y,x+c,y); g.DrawLine(ap,x,y,x,y+c);
                    g.DrawLine(p,r-c,y,r,y); g.DrawLine(p,r,y,r,y+c);
                    g.DrawLine(p,x,b-c,x,b); g.DrawLine(p,x,b,x+c,b);
                    g.DrawLine(ap,r-c,b,r,b); g.DrawLine(ap,r,b-c,r,b);
                    // center sight rails + side ticks
                    float mid=(x+r)*.5f;
                    g.DrawLine(p,mid-42f,y+1f,mid-12f,y+1f); g.DrawLine(p,mid+12f,y+1f,mid+42f,y+1f);
                    for(int i=0;i<3;i++) { float yy=y+c+14f+i*16f; g.DrawLine(p,x,yy,x+7f,yy); g.DrawLine(p,r-7f,yy,r,yy); }
                }
                return;
            }

            if (frameStyle == 2) // MARS TACTICAL - asymmetric armored wedge + hard data spine
            {
                float cut = Math.Max(14f, Math.Min(30f, rect.Width*.06f));
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[]{ new PointF(x,y+8f), new PointF(x+18f,y), new PointF(r-cut,y), new PointF(r,y+cut), new PointF(r,b-8f), new PointF(r-12f,b), new PointF(x,b) });
                    using(var fill=new SolidBrush(Color.FromArgb(Math.Min(opacity,195),panel))) g.FillPath(fill,path);
                    using(var p=new Pen(Color.FromArgb(190,border),Math.Max(1f,bw))) g.DrawPath(p,path);
                }
                using(var spine=new SolidBrush(Color.FromArgb(225,accent))) g.FillRectangle(spine,x,y+18f,3.5f,Math.Max(24f,rect.Height-36f));
                using(var ap=new Pen(Color.FromArgb(235,accent),Math.Max(1.8f,bw*1.6f))) { g.DrawLine(ap,x+18f,y,r-cut,y); g.DrawLine(ap,r-cut,y,r,y+cut); }
                using(var tick=new Pen(Color.FromArgb(135,border),1f)) for(int i=0;i<4;i++) g.DrawLine(tick,r-26f-i*10f,b-7f,r-21f-i*10f,b-2f);
                return;
            }

            if (frameStyle == 3) // BELTER UTILITY - broken rails / scaffold, intentionally asymmetric
            {
                using(var fill=new SolidBrush(Color.FromArgb(Math.Min(opacity,105),panel))) g.FillRectangle(fill,x+8f,y+10f,Math.Max(1f,rect.Width-16f),Math.Max(1f,rect.Height-18f));
                using(var p=new Pen(Color.FromArgb(205,border),Math.Max(1f,bw)))
                using(var ap=new Pen(Color.FromArgb(235,accent),Math.Max(1.5f,bw*1.4f)))
                {
                    g.DrawLine(ap,x,y+6f,x+rect.Width*.62f,y+6f);
                    g.DrawLine(p,x+rect.Width*.72f,y+6f,r,y+6f);
                    g.DrawLine(ap,x+7f,y,x+7f,b-28f);
                    g.DrawLine(p,x+7f,b-18f,x+7f,b);
                    g.DrawLine(p,x+24f,b-4f,r-52f,b-4f);
                    g.DrawLine(ap,r-42f,b-4f,r,b-4f);
                    g.DrawLine(ap,r-1f,b-4f,r-1f,b-30f);
                    for(int i=0;i<5;i++) { float xx=x+26f+i*19f; g.DrawLine(p,xx,y+1f,xx+6f,y+11f); }
                }
                return;
            }

            if (frameStyle == 4) // NAVY GLASS - double floating glass panes, no heavy armor border
            {
                using(var fill=new LinearGradientBrush(rect,Color.FromArgb(Math.Min(opacity,72),panel),Color.FromArgb(Math.Min(opacity,26),panel),0f)) g.FillRectangle(fill,rect);
                using(var p=new Pen(Color.FromArgb(125,border),Math.Max(.8f,bw*.75f)))
                using(var ap=new Pen(Color.FromArgb(205,accent),Math.Max(1f,bw)))
                {
                    g.DrawLine(ap,x+16f,y,r-34f,y); g.DrawLine(ap,r-34f,y,r-20f,y+14f);
                    g.DrawLine(p,r-20f,y+14f,r-20f,b-14f); g.DrawLine(p,r-20f,b-14f,r-34f,b);
                    g.DrawLine(p,r-34f,b,x+16f,b); g.DrawLine(p,x+16f,b,x+4f,b-12f); g.DrawLine(p,x+4f,b-12f,x+4f,y+12f); g.DrawLine(p,x+4f,y+12f,x+16f,y);
                    RectangleF inner=new RectangleF(x+10f,y+7f,Math.Max(1f,rect.Width-36f),Math.Max(1f,rect.Height-14f));
                    g.DrawRectangle(p,inner.X,inner.Y,inner.Width,inner.Height);
                }
                return;
            }

            if (frameStyle == 6) // WAR ROOM - website-matched command center plate
            {
                using (var fill = new LinearGradientBrush(rect,
                    Color.FromArgb(Math.Min(opacity, 218), panel),
                    Color.FromArgb(Math.Min(opacity, 172), panel), 90f))
                    g.FillRectangle(fill, rect);

                float cut = Math.Max(8f, Math.Min(18f, Math.Min(rect.Width, rect.Height) * .08f));
                using (var outline = new GraphicsPath())
                {
                    outline.AddPolygon(new[] {
                        new PointF(x+cut,y), new PointF(r-cut,y), new PointF(r,y+cut),
                        new PointF(r,b), new PointF(x,b), new PointF(x,y+cut)
                    });
                    using (var p = new Pen(Color.FromArgb(178, border), Math.Max(.9f, bw)))
                        g.DrawPath(p, outline);
                }

                using (var inner = new Pen(Color.FromArgb(72, 235, 239, 243), 1f))
                    g.DrawRectangle(inner, x+7f, y+7f, Math.Max(1f, rect.Width-14f), Math.Max(1f, rect.Height-14f));

                using (var red = new Pen(Color.FromArgb(245, accent), Math.Max(2f, bw*1.7f)))
                using (var pale = new Pen(Color.FromArgb(160, 235, 239, 243), Math.Max(1f, bw*.85f)))
                {
                    g.DrawLine(red, x+cut, y+1f, Math.Min(r-cut, x+cut+96f), y+1f);
                    g.DrawLine(red, x+1f, y+cut, x+1f, Math.Min(b, y+cut+32f));
                    g.DrawLine(pale, r-58f, b-1f, r-14f, b-1f);
                    g.DrawLine(pale, r-14f, b-1f, r-7f, b-8f);
                    for (int i=0;i<3;i++)
                    {
                        float yy=y+18f+i*11f;
                        g.DrawLine(pale,r-7f,yy,r-2f,yy);
                    }
                }
                return;
            }

            if (frameStyle == 7) // COMMAND GRID - dark tactical grid + locked red corners
            {
                using (var fill = new SolidBrush(Color.FromArgb(Math.Min(opacity, 202), panel)))
                    g.FillRectangle(fill, rect);
                using (var grid = new Pen(Color.FromArgb(26, 220, 225, 230), 1f))
                {
                    float step = Math.Max(18f, Math.Min(28f, rect.Width / 12f));
                    for (float xx=x+step; xx<r; xx+=step) g.DrawLine(grid,xx,y,xx,b);
                    for (float yy=y+step; yy<b; yy+=step) g.DrawLine(grid,x,yy,r,yy);
                }
                using (var p = new Pen(Color.FromArgb(165, border), Math.Max(1f,bw)))
                    g.DrawRectangle(p,x,y,rect.Width,rect.Height);
                using (var red = new Pen(Color.FromArgb(245, accent), Math.Max(1.8f,bw*1.45f)))
                {
                    float c=Math.Max(15f,Math.Min(28f,rect.Height*.18f));
                    g.DrawLine(red,x,y,x+c,y); g.DrawLine(red,x,y,x,y+c);
                    g.DrawLine(red,r-c,b,r,b); g.DrawLine(red,r,b-c,r,b);
                }
                return;
            }

            if (frameStyle == 8) // REDLINE - open premium rails, minimum panel mass
            {
                using (var fill = new SolidBrush(Color.FromArgb(Math.Min(opacity, 92), panel)))
                    g.FillRectangle(fill, x+4f, y+4f, Math.Max(1f,rect.Width-8f), Math.Max(1f,rect.Height-8f));
                using (var pale = new Pen(Color.FromArgb(150, 230, 234, 238), Math.Max(.9f,bw*.8f)))
                using (var red = new Pen(Color.FromArgb(248, accent), Math.Max(2f,bw*1.7f)))
                {
                    float leftEnd=x+Math.Min(92f,rect.Width*.28f);
                    g.DrawLine(red,x,y,leftEnd,y);
                    g.DrawLine(pale,leftEnd+14f,y,r-18f,y);
                    g.DrawLine(pale,r-18f,y,r,y+18f);
                    g.DrawLine(pale,x,b-16f,x+16f,b);
                    g.DrawLine(pale,x+16f,b,r-72f,b);
                    g.DrawLine(red,r-56f,b,r,b);
                    g.DrawLine(red,r,b-26f,r,b);
                }
                return;
            }

            if (frameStyle == 9) // BLACKSITE - matte classified terminal / asymmetric data spine
            {
                float notch=Math.Max(14f,Math.Min(28f,rect.Width*.055f));
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[]{
                        new PointF(x,y), new PointF(r-notch,y), new PointF(r,y+notch),
                        new PointF(r,b-notch*.55f), new PointF(r-notch*.55f,b),
                        new PointF(x+12f,b), new PointF(x,b-12f)
                    });
                    using(var fill=new SolidBrush(Color.FromArgb(Math.Min(opacity,228),panel))) g.FillPath(fill,path);
                    using(var p=new Pen(Color.FromArgb(154,border),Math.Max(1f,bw))) g.DrawPath(p,path);
                }
                using(var red=new SolidBrush(Color.FromArgb(232,accent)))
                    g.FillRectangle(red,x+6f,y+16f,3f,Math.Max(18f,rect.Height-32f));
                using(var pale=new Pen(Color.FromArgb(120,230,234,238),1f))
                {
                    for(int i=0;i<5;i++)
                    {
                        float yy=y+15f+i*10f;
                        g.DrawLine(pale,r-20f,yy,r-8f,yy);
                    }
                    g.DrawLine(pale,x+18f,b-7f,x+72f,b-7f);
                }
                return;
            }

            if (frameStyle == 10) // CHEVRON - clipped command capsule with inward side cues
            {
                float cut=Math.Max(12f,Math.Min(30f,Math.Min(rect.Width*.055f,rect.Height*.22f)));
                using(var path=new GraphicsPath())
                {
                    path.AddPolygon(new[]{
                        new PointF(x+cut,y), new PointF(r-cut,y), new PointF(r,y+cut),
                        new PointF(r-cut*.55f,(y+b)*.5f), new PointF(r,b-cut), new PointF(r-cut,b),
                        new PointF(x+cut,b), new PointF(x,b-cut), new PointF(x+cut*.55f,(y+b)*.5f), new PointF(x,y+cut)
                    });
                    using(var fill=new SolidBrush(Color.FromArgb(Math.Min(opacity,205),panel))) g.FillPath(fill,path);
                    using(var edge=new Pen(Color.FromArgb(165,border),Math.Max(1f,bw))) g.DrawPath(edge,path);
                }
                using(var red=new Pen(Color.FromArgb(245,accent),Math.Max(1.8f,bw*1.55f)))
                using(var pale=new Pen(Color.FromArgb(130,232,236,240),Math.Max(.8f,bw*.75f)))
                {
                    g.DrawLine(red,x+cut,y+1f,x+Math.Min(rect.Width*.34f,128f),y+1f);
                    g.DrawLine(red,r-cut,b-1f,r-Math.Min(rect.Width*.22f,86f),b-1f);
                    float mid=(y+b)*.5f;
                    g.DrawLine(pale,x+3f,mid,x+13f,mid); g.DrawLine(pale,r-13f,mid,r-3f,mid);
                }
                return;
            }

            if (frameStyle == 11) // SPLIT WING - detached left/right command wings with open center rails
            {
                using(var fill=new SolidBrush(Color.FromArgb(Math.Min(opacity,116),panel)))
                    g.FillRectangle(fill,x+9f,y+7f,Math.Max(1f,rect.Width-18f),Math.Max(1f,rect.Height-14f));
                float wing=Math.Max(38f,Math.Min(88f,rect.Width*.22f));
                using(var pale=new Pen(Color.FromArgb(155,border),Math.Max(1f,bw)))
                using(var red=new Pen(Color.FromArgb(245,accent),Math.Max(1.9f,bw*1.6f)))
                {
                    // left wing
                    g.DrawLine(red,x,y+18f,x+16f,y); g.DrawLine(red,x+16f,y,x+wing,y);
                    g.DrawLine(pale,x,y+18f,x,b-18f); g.DrawLine(pale,x,b-18f,x+16f,b); g.DrawLine(pale,x+16f,b,x+wing,b);
                    // right wing
                    g.DrawLine(pale,r-wing,y,r-16f,y); g.DrawLine(pale,r-16f,y,r,y+18f);
                    g.DrawLine(red,r,b-18f,r-16f,b); g.DrawLine(red,r-16f,b,r-wing,b);
                    // deliberately open center rails
                    float mid=(x+r)*.5f;
                    g.DrawLine(pale,mid-42f,y+1f,mid-12f,y+1f); g.DrawLine(pale,mid+12f,y+1f,mid+42f,y+1f);
                    g.DrawLine(pale,mid-32f,b-1f,mid+32f,b-1f);
                }
                return;
            }

            if (frameStyle == 12) // HEX COMMAND - wide six-sided command cell
            {
                float cut=Math.Max(16f,Math.Min(34f,rect.Height*.24f));
                using(var path=new GraphicsPath())
                {
                    path.AddPolygon(new[]{
                        new PointF(x+cut,y), new PointF(r-cut,y), new PointF(r,y+rect.Height*.5f),
                        new PointF(r-cut,b), new PointF(x+cut,b), new PointF(x,y+rect.Height*.5f)
                    });
                    using(var fill=new LinearGradientBrush(rect,Color.FromArgb(Math.Min(opacity,218),panel),Color.FromArgb(Math.Min(opacity,160),panel),0f)) g.FillPath(fill,path);
                    using(var edge=new Pen(Color.FromArgb(168,border),Math.Max(1f,bw))) g.DrawPath(edge,path);
                }
                using(var inner=new Pen(Color.FromArgb(72,235,239,243),1f))
                {
                    float inset=7f;
                    g.DrawLine(inner,x+cut+inset,y+inset,r-cut-inset,y+inset);
                    g.DrawLine(inner,x+cut+inset,b-inset,r-cut-inset,b-inset);
                }
                using(var red=new Pen(Color.FromArgb(245,accent),Math.Max(2f,bw*1.65f)))
                {
                    g.DrawLine(red,x+cut,y+1f,x+cut+Math.Min(104f,rect.Width*.28f),y+1f);
                    g.DrawLine(red,r-cut-Math.Min(68f,rect.Width*.18f),b-1f,r-cut,b-1f);
                }
                return;
            }

            if (frameStyle == 13) // RAZOR - asymmetric blade cuts and offset rails
            {
                float slash=Math.Max(16f,Math.Min(32f,rect.Height*.20f));
                using(var path=new GraphicsPath())
                {
                    path.AddPolygon(new[]{
                        new PointF(x,y+slash), new PointF(x+slash,y), new PointF(r-46f,y),
                        new PointF(r,y+24f), new PointF(r,b), new PointF(x+34f,b), new PointF(x,b-22f)
                    });
                    using(var fill=new SolidBrush(Color.FromArgb(Math.Min(opacity,190),panel))) g.FillPath(fill,path);
                    using(var edge=new Pen(Color.FromArgb(150,border),Math.Max(1f,bw))) g.DrawPath(edge,path);
                }
                using(var red=new Pen(Color.FromArgb(248,accent),Math.Max(2.1f,bw*1.75f)))
                using(var pale=new Pen(Color.FromArgb(135,230,234,238),Math.Max(.9f,bw*.8f)))
                {
                    g.DrawLine(red,x+slash,y+1f,x+Math.Min(rect.Width*.40f,150f),y+1f);
                    g.DrawLine(red,r-46f,y+1f,r,y+25f);
                    g.DrawLine(pale,x+12f,b-8f,x+rect.Width*.60f,b-8f);
                    for(int i=0;i<3;i++)
                    {
                        float xx=r-18f-i*12f;
                        g.DrawLine(pale,xx,b-5f,xx+7f,b-12f);
                    }
                }
                return;
            }

            // STEALTH - only navigation ticks / corners, almost no backing
            using(var fill=new SolidBrush(Color.FromArgb(Math.Min(opacity,35),panel))) g.FillRectangle(fill,rect);
            using(var p=new Pen(Color.FromArgb(165,border),Math.Max(.8f,bw*.75f)))
            using(var ap=new Pen(Color.FromArgb(220,accent),Math.Max(1.2f,bw)))
            {
                float c=18f;
                g.DrawLine(ap,x,y,x+c,y); g.DrawLine(ap,x,y,x,y+c);
                g.DrawLine(p,r-c,y,r,y); g.DrawLine(p,r,y,r,y+c);
                g.DrawLine(p,x,b-c,x,b); g.DrawLine(p,x,b,x+c,b);
                g.DrawLine(ap,r-c,b,r,b); g.DrawLine(ap,r,b-c,r,b);
            }
        }

        private static int FrameIndex(string name)
        {
            string n = (name ?? "WAR ROOM").Trim().ToUpperInvariant();
            switch (n)
            {
                case "SE INDUSTRIAL": return 0;
                case "FIGHTER HUD": return 1;
                case "MARS TACTICAL": return 2;
                case "BELTER UTILITY": return 3;
                case "NAVY GLASS": return 4;
                case "STEALTH": return 5;
                case "WAR ROOM": return 6;
                case "COMMAND GRID": return 7;
                case "REDLINE": return 8;
                case "BLACKSITE": return 9;
                case "CHEVRON": return 10;
                case "SPLIT WING": return 11;
                case "HEX COMMAND": return 12;
                case "RAZOR": return 13;
                // Migrate v0.1.0-v0.1.2 experimental names to the nearest current ZeoCore style.
                case "FLIGHT HUD": return 1;
                case "MINIMAL": return 5;
                case "CUT CORNER": return 0;
                case "SEGMENTED ARMOR": return 0;
                default: return 6;
            }
        }

        private static Color StateColor(NavConfig c, ThemePalette p, string phase)
        {
            if (!c.StateColors) return p.Accent;
            string x = (phase ?? "").ToUpperInvariant();
            if (x.Contains("ABORT") || x.Contains("BRAKE")) return p.Danger;
            if (x.Contains("FLIP") || x.Contains("PRE")) return p.Warning;
            if (x.Contains("ARRIVED")) return p.Good;
            return p.Accent;
        }
        private static string HudTitleFont(NavConfig c, int frame, int zeoFont)
        {
            if (zeoFont == 1) return "Bahnschrift SemiCondensed";
            if (zeoFont == 2) return "Consolas";
            if (zeoFont == 3) return "Segoe UI Semibold";
            string fs = (c.FontStyle ?? "MATCH HUD").Trim().ToUpperInvariant();
            if (fs == "CONDENSED") return "Bahnschrift SemiCondensed";
            if (fs == "TECH") return "Consolas";
            if (fs == "STANDARD") return "Segoe UI Semibold";
            if (frame == 1 || frame == 2 || frame == 4 || frame == 5 || frame >= 6) return "Bahnschrift SemiCondensed";
            if (frame == 3) return "Consolas";
            return "Segoe UI Semibold";
        }

        private static string HudBodyFont(NavConfig c, bool technical, int frame, int zeoFont)
        {
            if (zeoFont == 1) return "Bahnschrift SemiCondensed";
            if (zeoFont == 2) return "Consolas";
            if (zeoFont == 3) return technical ? "Consolas" : "Segoe UI";
            string fs = (c.FontStyle ?? "MATCH HUD").Trim().ToUpperInvariant();
            if (fs == "CONDENSED") return "Bahnschrift SemiCondensed";
            if (fs == "TECH") return "Consolas";
            if (fs == "STANDARD") return technical ? "Consolas" : "Segoe UI";
            if (frame == 1) return technical ? "Bahnschrift SemiCondensed" : "Bahnschrift";
            if (frame == 2 || frame == 3) return "Consolas";
            if (frame == 4) return technical ? "Bahnschrift" : "Segoe UI";
            if (frame == 5 || frame == 7 || frame == 9) return technical ? "Consolas" : "Bahnschrift SemiCondensed";
            if (frame == 6 || frame == 8 || frame == 10 || frame == 12) return technical ? "Bahnschrift SemiCondensed" : "Segoe UI Semibold";
            if (frame == 11 || frame == 13) return technical ? "Consolas" : "Bahnschrift SemiCondensed";
            return technical ? "Consolas" : "Segoe UI Semibold";
        }

        private static void DrawText(Graphics g, string t, Color c, float x, float y, float pt, double scale, FontStyle style, string fontName)
        {
            using (var f = new Font(fontName, Math.Max(6, (float)(pt * scale)), style, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(c))
                g.DrawString(t ?? "", f, b, x, y);
        }

        private static void DrawTextRight(Graphics g, string t, Color c, float right, float y, float pt, double scale, string fontName)
        {
            using (var f = new Font(fontName, Math.Max(6, (float)(pt * scale)), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                SizeF z = g.MeasureString(t ?? "", f);
                using (var b = new SolidBrush(c)) g.DrawString(t ?? "", f, b, right - z.Width, y);
            }
        }
        private static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
        private static string Safe(string s, string d) { return string.IsNullOrWhiteSpace(s) ? d : s; }
        private static string Dist(double m) { if (m >= 1000000) return (m / 1000000).ToString("0.00") + "Mm"; if (m >= 1000) return (m / 1000).ToString("0.0") + "km"; return m.ToString("0") + "m"; }
        private static string Speed(double v) { return v.ToString(v >= 1000 ? "0" : "0.0") + " M/S"; }
        private static string SigKmText(double km) { if (km < 0) return "-- KM"; return km.ToString(km >= 100 ? "0" : "0.0") + " KM"; }
        private static string Time(double sec) { if (double.IsNaN(sec) || double.IsInfinity(sec) || sec < 0) return "--:--"; int s = (int)Math.Round(sec); return (s / 3600 > 0 ? (s / 3600).ToString("00") + ":" : "") + ((s / 60) % 60).ToString("00") + ":" + (s % 60).ToString("00"); }
    }

    internal sealed class MenuForm : Form
    {
        public event Action<NavCommand> Command;

        private NavSnapshot snapshot;
        private NavConfig cfg = new NavConfig();
        private readonly FlowLayoutPanel body;
        private bool building;
        private bool applying;
        private bool captureApplied;
        private bool streamerMode = true;
        private int pageIndex;

        private ComboBox gpsBox;
        private FlatSlider bufferSlider, driveSlider;
        private Label bufferValue, driveValue, driveCompare, trip, spectrum, shipStatus, capStatus, driveScanStatus, zeoSyncStatus;
        private Button streamerButton;
        private string captureKeyField;
        private readonly Dictionary<string, Button> bindButtons = new Dictionary<string, Button>();
        private string gpsSignature = "";
        private string selectedGpsSignature = "";
        private string lastSentGpsSignature = "";
        private string zeoSyncMessage = "ZeoCore sync: not run";

        private static readonly string[] Pages = { "NAV", "TRIP HUD", "HUD", "CONTROLS", "ADVANCED" };
        private static readonly string[] FrameStyles = { "SE INDUSTRIAL", "FIGHTER HUD", "MARS TACTICAL", "BELTER UTILITY", "NAVY GLASS", "STEALTH", "WAR ROOM", "COMMAND GRID", "REDLINE", "BLACKSITE", "CHEVRON", "SPLIT WING", "HEX COMMAND", "RAZOR" };
        private static readonly string[] Themes = { "GRAPHITE", "MONOCHROME", "AMBER", "HIGH CONTRAST", "CUSTOM", "WAR ROOM" };
        private static readonly string[] FontStyles = { "MATCH HUD", "CONDENSED", "TECH", "STANDARD" };

        public MenuForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(660, 900);
            MinimumSize = new Size(620, 720);
            MaximumSize = new Size(760, 1120);
            Padding = new Padding(16);

            body = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(14, 12, 14, 18),
                Margin = Padding.Empty
            };
            Controls.Add(body);
            KeyDown += OnMenuKeyDown;
            RefreshFromSettings();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCaptureAffinity();
        }

        public bool CaptureApplied { get { return captureApplied; } }

        public void SetStreamerMode(bool enabled)
        {
            if (streamerMode != enabled)
            {
                streamerMode = enabled;
                ApplyCaptureAffinity();
            }
            if (streamerButton != null)
                streamerButton.Text = streamerMode ? "STREAMER MODE: ON" : "STREAMER MODE: OFF";
        }

        private void ApplyCaptureAffinity()
        {
            if (!IsHandleCreated) return;
            try
            {
                uint desired = streamerMode ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE;
                bool ok = Native.SetWindowDisplayAffinity(Handle, desired);
                uint actual;
                bool verified = ok && Native.GetWindowDisplayAffinity(Handle, out actual) && actual == desired;
                captureApplied = streamerMode && verified;
            }
            catch { captureApplied = false; }
        }

        public void CenterOnGame(NavSnapshot s)
        {
            int x = s.ClientX + Math.Max(0, (s.ClientW - Width) / 2);
            int y = s.ClientY + Math.Max(0, (s.ClientH - Height) / 2);
            Location = new Point(x, y);
        }

        public void PrepareForShow()
        {
            RefreshFromSettings();
        }

        public void Apply(NavSnapshot s)
        {
            if (s == null) return;
            snapshot = s;
            if (s.Config != null) cfg = s.Config;
            if (!Visible) return;

            applying = true;
            try
            {
                RefreshGpsBox(s.Gps, false);
                if (bufferSlider != null)
                {
                    bufferSlider.Value = Math.Max(bufferSlider.Minimum, Math.Min(bufferSlider.Maximum, (int)Math.Round(cfg.BufferKm * 10.0)));
                    if (bufferValue != null) bufferValue.Text = cfg.BufferKm.ToString("0.0") + " km";
                }
                if (driveSlider != null)
                {
                    driveSlider.Value = Math.Max(driveSlider.Minimum, Math.Min(driveSlider.Maximum, (int)Math.Round(cfg.MaxDriveSigKm)));
                    if (driveValue != null) driveValue.Text = SigKmText(cfg.MaxDriveSigKm);
                }
                UpdateLiveText();
            }
            finally { applying = false; }
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            ThemePalette t = ThemePalette.From(cfg ?? new NavConfig());
            BackColor = t.MenuBack;
            body.BackColor = t.MenuBack;
            ApplyColors(body, t);
        }

        private void OnMenuKeyDown(object sender, KeyEventArgs e)
        {
            if (captureKeyField != null)
            {
                CaptureKey(e);
                return;
            }

            Keys menuKey = KeyFromName(cfg == null ? "Insert" : cfg.MenuKey);
            if (e.KeyCode == Keys.Escape || (menuKey != Keys.None && e.KeyCode == menuKey))
            {
                Hide();
                Send(new NavCommand { Type = "MENU_CLOSE" });
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void RefreshFromSettings()
        {
            if (IsDisposed) return;
            building = true;
            try
            {
                body.SuspendLayout();
                body.Controls.Clear();
                bindButtons.Clear();
                gpsBox = null; bufferSlider = null; driveSlider = null;
                bufferValue = null; driveValue = null; driveCompare = null; trip = null; spectrum = null; shipStatus = null; capStatus = null; driveScanStatus = null; zeoSyncStatus = null; streamerButton = null;

                ApplyThemeToForm();
                AddTitle("ZEO NAV // ROUTE CONTROL", "v0.1.23 // LEGACY SETTINGS");
                AddStatus(cfg.StreamerMode
                    ? (captureApplied ? "STREAMER MODE: ON // MENU CAPTURE EXCLUSION ACTIVE" : "STREAMER MODE: ON // CAPTURE EXCLUSION WAIT / VERIFY")
                    : "STREAMER MODE: OFF // OVERLAY IS CAPTURABLE", cfg.StreamerMode && captureApplied);

                AddSection("PAGE");
                AddCombo(Pages, Clamp(pageIndex, 0, Pages.Length - 1), delegate(int i)
                {
                    pageIndex = i;
                    RefreshFromSettings();
                });

                switch (Clamp(pageIndex, 0, Pages.Length - 1))
                {
                    case 1: DrawTripHud(); break;
                    case 2: DrawHud(); break;
                    case 3: DrawControls(); break;
                    case 4: DrawAdvanced(); break;
                    default: DrawNav(); break;
                }

                AddFooter();
            }
            finally
            {
                body.ResumeLayout(true);
                building = false;
            }
            ApplyTheme();
            UpdateLiveText();
            SendSelectedGpsIfChanged();
        }

        private void DrawNav()
        {
            AddSection("SHIP / STATUS");
            shipStatus = AddLiveLabel("Waiting for Space Engineers...", false);

            AddSection("DESTINATION");
            AddGpsCombo();
            AddInfo("Arrival Buffer freezes a virtual STOP SHORT point when START ROUTE is pressed. The real GPS remains unchanged.");
            bufferSlider = AddSlider("Arrival buffer", 0, 100, (int)Math.Round(cfg.BufferKm * 10.0), delegate(int v)
            {
                double km = v / 10.0;
                if (bufferValue != null) bufferValue.Text = km.ToString("0.0") + " km";
                Set("BufferKm", km);
            }, delegate(int v) { return (v / 10.0).ToString("0.0") + " km"; }, out bufferValue);

            AddSection("MAX OWN-SHIP SIGNATURE");
            AddInfo("Choose MAX SIG and press GO. Zeo Nav budgets thrust up to 100% for a fast route and a stop at your buffer. MAX SIG covers the farthest of all four Spectrum own-ship ranges. It reserves 3% range headroom, measures idle signature before departure, and cuts thrust if telemetry is stale or the measured limit is exceeded.");
            driveSlider = AddSlider("MAX SIG", 5, 750, (int)Math.Round(cfg.MaxDriveSigKm), delegate(int v)
            {
                if (driveValue != null) driveValue.Text = SigKmText(v);
                Set("MaxDriveSigKm", v);
            }, delegate(int v) { return SigKmText(v); }, out driveValue);
            driveCompare = AddLiveLabel("MAX SIG -- KM   // ACTUAL -- KM   // THR --%   // ETA --:--", true);
            spectrum = AddLiveLabel("Spectrum KM source: waiting", false);
            capStatus = AddLiveLabel("Speed cap: waiting", false);
            driveScanStatus = AddLiveLabel("Drive scan: waiting for controlled ship", false);

            AddSection("STREAMER");
            streamerButton = AddButton(cfg.StreamerMode ? "STREAMER MODE: ON" : "STREAMER MODE: OFF", delegate
            {
                bool next = !cfg.StreamerMode;
                cfg.StreamerMode = next; // instant local feedback while plugin persists the setting
                Set("StreamerMode", next ? 1 : 0);
                SetStreamerMode(next);
                RefreshFromSettings();
            });
            AddInfo("ON requests Windows capture exclusion for both the trip HUD and Zeo Nav menu. OFF makes them capturable. Alt+Tab foreground hiding remains active either way.");

            AddSection("TRIP");
            trip = AddLiveLabel("ETA --:--   //   FLIP --   //   STOP --", true);

            AddSection("ROUTE SAFETY");
            AddStatus("AUTOMATIC 180° FLIP + RETRO BURN: ON", true);
            AddStatus("FULL STOP AT BUFFERED DESTINATION: ON", true);

            AddSection("ACTIONS");
            AddButton("START ROUTE", StartRoute);
            AddButton("MANUAL FLIP", delegate { Send(new NavCommand { Type = "FLIP" }); });
            AddButton("ABORT / RELEASE", delegate { Send(new NavCommand { Type = "ABORT" }); });
        }

        private void DrawTripHud()
        {
            AddSection("TRIP HUD // VISIBILITY");
            AddCombo(new[] { "AUTO", "ALWAYS", "HIDDEN" }, IndexOf(new[] { "AUTO", "ALWAYS", "HIDDEN" }, cfg.TripPanelVisibility), delegate(int i)
            {
                SetText("TripPanelVisibility", new[] { "AUTO", "ALWAYS", "HIDDEN" }[i]);
            });
            AddCheck("Follow active ZeoCore appearance", cfg.TripUseHudTheme, v => Set("TripUseHudTheme", v ? 1 : 0));
            AddInfo("AUTO appears during route / flip / warnings. ALWAYS is useful while positioning it. HIDDEN keeps it down. The renderer is now TRUE PER-PIXEL ALPHA: there is no magenta/purple transparency key.");

            AddSection("TRIP HUD // POSITION");
            AddCombo(new[] { "CUSTOM / KEEP CURRENT", "TOP LEFT", "TOP CENTER", "TOP RIGHT", "CENTER LEFT", "CENTER", "CENTER RIGHT", "BOTTOM LEFT", "BOTTOM CENTER", "BOTTOM RIGHT" }, 0, delegate(int i)
            {
                if (i > 0) ApplyTripPreset(i);
            });
            AddNumber("Trip X", (decimal)cfg.HudX, -.98m, .98m, .01m, v => Set("HudX", (double)v), 2);
            AddNumber("Trip Y", (decimal)cfg.HudY, -.98m, .98m, .01m, v => Set("HudY", (double)v), 2);
            AddNumber("Trip panel scale", (decimal)cfg.PanelScale, .45m, 2.50m, .05m, v => Set("PanelScale", (double)v), 2);
            AddNumber("Trip text scale", (decimal)cfg.GlobalScale, .50m, 2.75m, .05m, v => Set("GlobalScale", (double)v), 2);
            AddNumber("Trip backing opacity", cfg.BackingOpacity, 0, 245, 5, v => Set("BackingOpacity", (double)v));
            AddNumber("Panel inner padding", (decimal)cfg.InnerPadding, .40m, 2.25m, .05m, v => Set("InnerPadding", (double)v), 2);
            AddNumber("Panel border width", (decimal)cfg.BorderWidth, .25m, 4.00m, .25m, v => Set("BorderWidth", (double)v), 2);

            AddSection("TRIP HUD // DESIGN");
            AddCombo(FrameStyles, IndexOf(FrameStyles, NormalizeFrame(cfg.Frame)), delegate(int i) { SetText("Frame", FrameStyles[i]); });
            AddCombo(Themes, IndexOf(Themes, NormalizeTheme(cfg.Theme)), delegate(int i) { SetText("Theme", Themes[i]); RefreshFromSettings(); });
            AddCombo(FontStyles, IndexOf(FontStyles, cfg.FontStyle), delegate(int i) { SetText("FontStyle", FontStyles[i]); });
            AddCheck("State-aware navigation colors", cfg.StateColors, v => Set("StateColors", v ? 1 : 0));
            AddInfo("With ZeoCore-follow ON, Zeo Nav reads the live ZeoCore frame/font/panel/border/accent settings. Turn it OFF to use the custom trip colors below.");

            AddSection("TRIP HUD // CUSTOM COLORS");
            AddTripColor("Trip text", cfg.TripHudText, "TripHudText");
            AddTripColor("Trip secondary text", cfg.TripSecondaryText, "TripSecondaryText");
            AddTripColor("Trip background / panel", cfg.TripPanelBacking, "TripPanelBacking");
            AddTripColor("Trip border", cfg.TripPanelBorder, "TripPanelBorder");
            AddTripColor("Trip accent", cfg.TripAccent, "TripAccent");
            AddInfo("Editing a trip color automatically turns ZeoCore-follow OFF so the change is immediately visible.");

            AddSection("TRIP HUD // DATA SIZES");
            AddNumber("Destination", (decimal)cfg.DestinationScale, .50m, 3.00m, .05m, v => Set("DestinationScale", (double)v), 2);
            AddNumber("Distance", (decimal)cfg.DistanceScale, .50m, 3.00m, .05m, v => Set("DistanceScale", (double)v), 2);
            AddNumber("Speed", (decimal)cfg.SpeedScale, .50m, 3.00m, .05m, v => Set("SpeedScale", (double)v), 2);
            AddNumber("Spectrum / signal", (decimal)cfg.SignalScale, .50m, 3.00m, .05m, v => Set("SignalScale", (double)v), 2);
            AddNumber("ETA", (decimal)cfg.EtaScale, .50m, 3.00m, .05m, v => Set("EtaScale", (double)v), 2);
            AddNumber("Flight state", (decimal)cfg.PhaseScale, .50m, 3.00m, .05m, v => Set("PhaseScale", (double)v), 2);
            AddNumber("Flip countdown", (decimal)cfg.FlipScale, .50m, 3.00m, .05m, v => Set("FlipScale", (double)v), 2);
            AddNumber("Stop distance", (decimal)cfg.StopScale, .50m, 3.00m, .05m, v => Set("StopScale", (double)v), 2);
            AddNumber("Route progress", (decimal)cfg.ProgressScale, .50m, 3.00m, .05m, v => Set("ProgressScale", (double)v), 2);
            AddNumber("Warning text", (decimal)cfg.WarningScale, .50m, 3.00m, .05m, v => Set("WarningScale", (double)v), 2);

            AddSection("TRIP HUD // RESET");
            AddButton("RESET TRIP HUD TO ZEOCORE-MATCH DEFAULT", ResetTripHud);
        }

        private void ApplyTripPreset(int i)
        {
            double x = cfg.HudX, y = cfg.HudY;
            switch (i)
            {
                case 1: x = -.95; y = .90; break;
                case 2: x = -.15; y = .90; break;
                case 3: x = .72; y = .90; break;
                case 4: x = -.95; y = .15; break;
                case 5: x = -.15; y = .15; break;
                case 6: x = .72; y = .15; break;
                case 7: x = -.95; y = -.72; break;
                case 8: x = -.15; y = -.72; break;
                case 9: x = .72; y = -.72; break;
                default: return;
            }
            Set("HudX", x);
            Set("HudY", y);
            RefreshFromSettings();
        }

        private void ResetTripHud()
        {
            SetText("TripPanelVisibility", "AUTO");
            Set("TripUseHudTheme", 1);
            Set("HudX", -.88);
            Set("HudY", .72);
            Set("GlobalScale", 1.0);
            Set("PanelScale", 1.0);
            Set("BackingOpacity", 178);
            Set("InnerPadding", 1.0);
            Set("BorderWidth", 1.2);
            Set("DestinationScale", 1.0);
            Set("DistanceScale", 1.0);
            Set("SpeedScale", 1.0);
            Set("SignalScale", 1.0);
            Set("EtaScale", 1.0);
            Set("PhaseScale", 1.0);
            Set("FlipScale", 1.0);
            Set("StopScale", 1.0);
            Set("ProgressScale", 1.0);
            Set("WarningScale", 1.0);
            RefreshFromSettings();
        }

        private void DrawHud()
        {
            AddSection("HUD FRAME STYLE");
            AddCombo(FrameStyles, IndexOf(FrameStyles, NormalizeFrame(cfg.Frame)), delegate(int i) { SetText("Frame", FrameStyles[i]); });
            AddCombo(FontStyles, IndexOf(FontStyles, cfg.FontStyle), delegate(int i) { SetText("FontStyle", FontStyles[i]); });
            AddInfo("These are the ZeoCore v0.6.1 ALIGNED frame names. The active trip HUD can follow ZeoCore live from the dedicated TRIP HUD page.");

            AddSection("THEME PRESET");
            AddCombo(Themes, IndexOf(Themes, NormalizeTheme(cfg.Theme)), delegate(int i)
            {
                SetText("Theme", Themes[i]);
                ApplyTheme();
                RefreshFromSettings();
            });
            AddButton("MATCH ACTIVE ZEOCORE APPEARANCE", SyncFromZeoCore);
            zeoSyncStatus = AddLiveLabel(zeoSyncMessage, false);

            AddSection("HUD COLORS");
            AddColor("HUD text", cfg.HudText, "HudText");
            AddColor("Secondary text", cfg.SecondaryText, "SecondaryText");
            AddColor("Panel backing", cfg.PanelBacking, "PanelBacking");
            AddColor("Panel border", cfg.PanelBorder, "PanelBorder");
            AddColor("Spectrum / normal accent", cfg.Accent, "Accent");
            AddColor("Arrived / good", cfg.Good, "Good");
            AddColor("Flip / warning", cfg.Warning, "Warning");
            AddColor("Brake / abort", cfg.Danger, "Danger");

            AddSection("MENU COLORS");
            AddColor("Menu background", cfg.MenuBackground, "MenuBackground");
            AddColor("Menu panel", cfg.MenuPanel, "MenuPanel");
            AddColor("Menu text", cfg.MenuText, "MenuText");
            AddColor("Menu accent", cfg.MenuAccent, "MenuAccent");
            AddInfo("Color format is #RRGGBB. Editing HUD/menu colors switches the base theme to CUSTOM.");
        }

        private void DrawControls()
        {
            AddSection("KEYBINDS");
            AddInfo("Click a binding, then press the keyboard key you want. ESC cancels capture. Manual Flip is independent from route Auto Flip.");
            AddBind("Open Zeo Nav", "MenuKey");
            AddBind("Start selected route", "StartKey");
            AddBind("Abort / release", "AbortKey");
            AddBind("Manual 180° flip", "ManualFlipKey");
            AddBind("Max SIG +5 KM", "SignalUpKey");
            AddBind("Max SIG -5 KM", "SignalDownKey");
        }

        private void DrawAdvanced()
        {
            AddSection("FLIGHT SAFETY / CALIBRATION");
            AddInfo("These remain off the main NAV page. Defaults preserve the conservative NavOS-style flip-and-burn behavior.");
            AddNumber("180° flip allowance (sec)", (decimal)cfg.FlipTimeSeconds, 1m, 60m, .5m, v => Set("FlipTimeSeconds", (double)v), 1);
            AddNumber("Brake safety multiplier", (decimal)cfg.BrakeSafety, 1.00m, 2.00m, .01m, v => Set("BrakeSafety", (double)v), 2);
            AddNumber("Arrival radius (m)", (decimal)cfg.ArrivalRadiusMeters, 1m, 100m, 1m, v => Set("ArrivalRadiusMeters", (double)v), 0);
            AddNumber("Arrival speed (m/s)", (decimal)cfg.ArrivalSpeedMps, .05m, 5m, .05m, v => Set("ArrivalSpeedMps", (double)v), 2);
            AddNumber("Speed cap override (m/s, 0=AUTO)", (decimal)cfg.SpeedCapOverride, 0m, 50000m, 25m, v => Set("SpeedCapOverride", (double)v), 0);
            AddCheck("Abort route on manual pilot input", cfg.AbortOnManualInput, v => Set("AbortOnManualInput", v ? 1 : 0));
            AddInfo("Spectrum MAX-SIG control is mandatory and locked ON. Zeo Nav never treats the raw Spectrum drive-emission tag as KM. The v0.1.19 flight baseline measures the controlled-grid self summary, budgets thrust from loaded emission definitions, and stops commanded thrust when own-signature feedback is stale or exceeds the ceiling.");
        }

        private void UpdateLiveText()
        {
            if (snapshot == null) return;
            if (shipStatus != null)
                shipStatus.Text = (snapshot.Ship ?? "NO SHIP") + "   //   " + (snapshot.State ?? "DISARMED") + "   //   " + (snapshot.Phase ?? "");
            if (driveCompare != null)
            {
                string actual = snapshot.SpectrumKmReady ? SigKmText(snapshot.SpectrumDriveKm) : "WAIT";
                driveCompare.Text = "MAX SIG " + SigKmText(snapshot.MaxDriveSigKm) + "   // ACTUAL " + actual +
                    "   // CMD " + (snapshot.ForwardCommandRatio * 100.0).ToString("0.0") + "%   // ETA " + Time(snapshot.EtaSeconds) +
                    "   // " + (snapshot.SignalGovernorState ?? "IDLE");
            }
            if (streamerButton != null)
                streamerButton.Text = cfg.StreamerMode ? "STREAMER MODE: ON" : "STREAMER MODE: OFF";
            if (spectrum != null)
                spectrum.Text = snapshot.SpectrumKmReady
                    ? "Own-ship signature " + SigKmText(snapshot.SpectrumDriveKm) + " / MAX " + SigKmText(snapshot.MaxDriveSigKm) +
                      "   // available thrust " + (snapshot.DriveRatio * 100.0).ToString("0.00") + "%   // KM source " + (snapshot.SpectrumKmSource ?? "UNKNOWN") +
                      "   // sphere S/W " + SigKmText(snapshot.SphericalStrongKm) + " / " + SigKmText(snapshot.SphericalWeakKm) + "   // directional S/W " + SigKmText(snapshot.DirectionalStrongKm) + " / " + SigKmText(snapshot.DirectionalWeakKm)
                    : (snapshot.SpectrumReady
                        ? "SIG KM SOURCE WAIT   // SELF " + (snapshot.SpectrumSelfMatch ?? "NONE") +
                          " #" + snapshot.SpectrumSelfEmitterId +
                          " age " + snapshot.SpectrumSelfAgeFrames +
                          "f   // raw drive " + snapshot.SpectrumDrive.ToString("0.0000") +
                          "   // " + (snapshot.SpectrumKmSource ?? "self summary pending")
                        : "Spectrum: waiting for controlled-grid self summary");
            if (capStatus != null)
            {
                bool capReady = snapshot.SpeedCapMps > 1 && (snapshot.SpeedCapSource ?? "").IndexOf("WAIT", StringComparison.OrdinalIgnoreCase) < 0;
                string capText = capReady ? Speed(snapshot.SpeedCapMps) : "WAIT SHIPCORE";
                capStatus.Text = "Speed cap " + capText + "   // " + (snapshot.SpeedCapSource ?? "UNKNOWN") +
                    "   // grids " + snapshot.ConstructGridCount + " thr " + snapshot.ThrusterCount + " main " + snapshot.MainDriveCount +
                    " fMain " + snapshot.ForwardMainDriveCount + " fWork " + snapshot.ForwardWorkingThrusterCount +
                    " gyro " + snapshot.GyroCount + " rcsG " + snapshot.RcsGyroCount + " [RCS EXCLUDED]" +
                    " F " + snapshot.ForwardThrustMN.ToString("0.0") + "MN B " + snapshot.BackwardThrustMN.ToString("0.0") + "MN";
            }
            if (driveScanStatus != null)
                driveScanStatus.Text = "Drive scan // " + (string.IsNullOrWhiteSpace(snapshot.DriveScanSummary) ? "WAIT" : snapshot.DriveScanSummary) +
                    "   // FWD CMD " + (snapshot.ForwardCommandRatio * 100.0).ToString("0.0") +
                    "% RB " + (snapshot.ForwardReadbackRatio * 100.0).ToString("0.0") +
                    "%   // SELF " + (snapshot.SpectrumSelfMatch ?? "NONE") + " #" + snapshot.SpectrumSelfEmitterId +
                    "   // ALIGN " + snapshot.AlignmentErrorDeg.ToString("0.0") + "° ROT " + snapshot.AngularSpeedDeg.ToString("0.0") + "°/s";
            if (trip != null)
                trip.Text = "ETA " + Time(snapshot.EtaSeconds) + "   //   FLIP " + (snapshot.FlipInSeconds < 0 ? "--" : Time(snapshot.FlipInSeconds)) + " @ " + Dist(snapshot.FlipAtMeters) + "   //   STOP " + Dist(snapshot.StopDistanceMeters);
        }

        private void StartRoute()
        {
            var item = gpsBox == null ? null : gpsBox.SelectedItem as GpsItem;
            if (item == null) return;
            Send(new NavCommand { Type = "START", Name = item.G.Name, X = item.G.X, Y = item.G.Y, Z = item.G.Z, Value = cfg.BufferKm });
        }

        private void AddGpsCombo()
        {
            var p = NewPanel(46);
            gpsBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(10, 8),
                Size = new Size(580, 30),
                Font = new Font("Segoe UI", 9.3f),
                FlatStyle = FlatStyle.Flat
            };
            gpsBox.SelectedIndexChanged += delegate
            {
                if (building || applying) return;
                var item = gpsBox.SelectedItem as GpsItem;
                if (item == null) return;
                selectedGpsSignature = GpsSig(item.G);
                SendSelectedGpsIfChanged();
            };
            p.Controls.Add(gpsBox);
            body.Controls.Add(p);
            RefreshGpsBox(snapshot == null ? null : snapshot.Gps, true);
        }

        private void RefreshGpsBox(List<GpsDto> list, bool force)
        {
            if (gpsBox == null) return;
            string nextSig = "";
            if (list != null) for (int i = 0; i < list.Count; i++) nextSig += GpsSig(list[i]) + ";";
            if (!force && nextSig == gpsSignature) return;
            gpsSignature = nextSig;

            string wanted = selectedGpsSignature;
            var old = gpsBox.SelectedItem as GpsItem;
            if (string.IsNullOrEmpty(wanted) && old != null) wanted = GpsSig(old.G);

            gpsBox.BeginUpdate();
            gpsBox.Items.Clear();
            if (list != null) foreach (GpsDto g in list) gpsBox.Items.Add(new GpsItem(g));
            if (gpsBox.Items.Count > 0) gpsBox.SelectedIndex = 0;
            if (!string.IsNullOrEmpty(wanted))
            {
                for (int i = 0; i < gpsBox.Items.Count; i++)
                {
                    var gi = gpsBox.Items[i] as GpsItem;
                    if (gi != null && wanted == GpsSig(gi.G)) { gpsBox.SelectedIndex = i; break; }
                }
            }
            gpsBox.EndUpdate();
            var selected = gpsBox.SelectedItem as GpsItem;
            if (selected != null) selectedGpsSignature = GpsSig(selected.G);
        }

        private void SendSelectedGpsIfChanged()
        {
            var item = gpsBox == null ? null : gpsBox.SelectedItem as GpsItem;
            if (item == null) return;
            string sig = GpsSig(item.G);
            if (sig == lastSentGpsSignature) return;
            lastSentGpsSignature = sig;
            selectedGpsSignature = sig;
            Send(new NavCommand { Type = "SELECT_GPS", Name = item.G.Name, X = item.G.X, Y = item.G.Y, Z = item.G.Z });
        }

        private void SyncFromZeoCore()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulsar", "ZeoCore", "hud-settings.json");
                if (!File.Exists(path))
                {
                    zeoSyncMessage = "ZeoCore sync: hud-settings.json not found";
                    if (zeoSyncStatus != null) zeoSyncStatus.Text = zeoSyncMessage;
                    return;
                }

                string json = File.ReadAllText(path);
                int frame = FindJsonInt(json, "FrameStyle", -1);
                int theme = FindJsonInt(json, "ThemePreset", -1);
                int font = FindJsonInt(json, "FontStyle", -1);

                if (frame >= 0 && frame < FrameStyles.Length) SetText("Frame", FrameStyles[frame]);
                if (font >= 0 && font < FontStyles.Length) SetText("FontStyle", FontStyles[font]);
                if (theme >= 0 && theme < Themes.Length) SetText("Theme", Themes[theme]);

                double textScale = FindJsonDouble(json, "TextScale", double.NaN);
                double flightScale = FindJsonDouble(json, "FlightScale", double.NaN);
                double pad = FindJsonDouble(json, "PanelPaddingScale", double.NaN);
                double bw = FindJsonDouble(json, "BorderWidth", double.NaN);
                int opacity = FindJsonInt(json, "PanelOpacity", -1);
                if (!double.IsNaN(textScale)) Set("GlobalScale", textScale);
                if (!double.IsNaN(flightScale)) Set("PanelScale", flightScale);
                if (!double.IsNaN(pad)) Set("InnerPadding", pad);
                if (!double.IsNaN(bw)) Set("BorderWidth", bw);
                if (opacity >= 0) Set("BackingOpacity", opacity);

                if (theme == 4)
                {
                    ImportHex(json, "HudTextColor", "HudText");
                    ImportHex(json, "HudSecondaryColor", "SecondaryText");
                    ImportHex(json, "HudPanelColor", "PanelBacking");
                    ImportHex(json, "HudBorderColor", "PanelBorder");
                    ImportHex(json, "SpectrumColor", "Accent");
                    ImportHex(json, "FriendlyColor", "Good");
                    ImportHex(json, "FocusColor", "Warning");
                    ImportHex(json, "HostileColor", "Danger");
                    ImportHex(json, "MenuBackgroundColor", "MenuBackground");
                    ImportHex(json, "MenuPanelColor", "MenuPanel");
                    ImportHex(json, "MenuTextColor", "MenuText");
                    ImportHex(json, "MenuAccentColor", "MenuAccent");
                }

                zeoSyncMessage = "ZeoCore sync: frame + theme + sizing imported";
                RefreshFromSettings();
                if (zeoSyncStatus != null) zeoSyncStatus.Text = zeoSyncMessage;
            }
            catch (Exception ex)
            {
                zeoSyncMessage = "ZeoCore sync failed: " + ex.GetType().Name;
                if (zeoSyncStatus != null) zeoSyncStatus.Text = zeoSyncMessage;
            }
        }

        private void ImportHex(string json, string source, string target)
        {
            string v = FindJsonString(json, source);
            if (IsHex(v)) SetText(target, v.ToUpperInvariant());
        }

        private static int FindJsonInt(string json, string key, int fallback)
        {
            double d = FindJsonDouble(json, key, double.NaN);
            return double.IsNaN(d) ? fallback : (int)Math.Round(d);
        }

        private static double FindJsonDouble(string json, string key, double fallback)
        {
            string needle = "\"" + key + "\"";
            int p = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (p < 0) return fallback;
            p = json.IndexOf(':', p + needle.Length);
            if (p < 0) return fallback;
            p++;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            int e = p;
            while (e < json.Length && (char.IsDigit(json[e]) || json[e] == '-' || json[e] == '+' || json[e] == '.' || json[e] == 'e' || json[e] == 'E')) e++;
            double v;
            return double.TryParse(json.Substring(p, e - p), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static string FindJsonString(string json, string key)
        {
            string needle = "\"" + key + "\"";
            int p = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (p < 0) return null;
            p = json.IndexOf(':', p + needle.Length);
            if (p < 0) return null;
            p++;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            if (p >= json.Length || json[p] != '"') return null;
            int e = json.IndexOf('"', p + 1);
            if (e <= p) return null;
            return json.Substring(p + 1, e - p - 1);
        }

        private void AddTitle(string title, string subtitle)
        {
            var p = NewPanel(92);
            var t = new Label { Text = title, Tag = "TEXT", AutoSize = false, Location = new Point(0, 2), Size = new Size(530, 34), Font = new Font("Segoe UI Semibold", 17f), ForeColor = MenuText(), TextAlign = ContentAlignment.MiddleLeft };
            var s = new Label { Text = subtitle, Tag = "ACCENT", AutoSize = false, Location = new Point(1, 40), Size = new Size(530, 24), Font = new Font("Segoe UI", 9f), ForeColor = MenuAccent(), TextAlign = ContentAlignment.MiddleLeft };
            var close = new Button { Text = "×", FlatStyle = FlatStyle.Flat, Size = new Size(42, 34), Location = new Point(548, 2), ForeColor = MenuText(), BackColor = MenuPanel(), TabStop = false };
            close.FlatAppearance.BorderSize = 0;
            close.Click += delegate { Hide(); Send(new NavCommand { Type = "MENU_CLOSE" }); };
            p.Controls.Add(t); p.Controls.Add(s); p.Controls.Add(close);
            body.Controls.Add(p);
        }

        private void AddFooter()
        {
            var p = NewPanel(48);
            p.Margin = new Padding(0, 12, 0, 0);
            var l = new Label
            {
                Text = (cfg == null ? "INSERT" : (cfg.MenuKey ?? "INSERT").ToUpperInvariant()) + " opens in-game / closes here   //   ESC closes   //   settings save live",
                Tag = "ACCENT", AutoSize = false, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5f), ForeColor = MenuAccent(), TextAlign = ContentAlignment.MiddleLeft
            };
            p.Controls.Add(l); body.Controls.Add(p);
        }

        private void AddSection(string text)
        {
            var l = new Label
            {
                Text = text, Tag = "ACCENT", AutoSize = false, Size = new Size(600, 32), Margin = new Padding(0, 12, 0, 4),
                Font = new Font("Segoe UI Semibold", 10f), ForeColor = MenuAccent(), TextAlign = ContentAlignment.BottomLeft
            };
            body.Controls.Add(l);
        }

        private Label AddLiveLabel(string text, bool strong)
        {
            var p = NewPanel(42);
            var l = new Label
            {
                Text = text, Tag = strong ? "TEXT" : "ACCENT", AutoSize = false, Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0),
                Font = new Font(strong ? "Segoe UI Semibold" : "Segoe UI", strong ? 9.4f : 9f), ForeColor = strong ? MenuText() : MenuAccent(), TextAlign = ContentAlignment.MiddleLeft
            };
            p.Controls.Add(l); body.Controls.Add(p); return l;
        }

        private void AddStatus(string text, bool good)
        {
            var p = NewPanel(38);
            var l = new Label
            {
                Text = text, Tag = good ? "GOOD" : "ACCENT", AutoSize = false, Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0),
                Font = new Font("Segoe UI Semibold", 9f), ForeColor = good ? Color.FromArgb(150, 220, 180) : MenuAccent(), TextAlign = ContentAlignment.MiddleLeft
            };
            p.Controls.Add(l); body.Controls.Add(p);
        }

        private void AddInfo(string text)
        {
            var l = new Label
            {
                Text = text, Tag = "ACCENT", AutoSize = false, Size = new Size(600, 58), Margin = new Padding(0, 4, 0, 4),
                Font = new Font("Segoe UI", 8.7f), ForeColor = MenuAccent(), TextAlign = ContentAlignment.MiddleLeft
            };
            body.Controls.Add(l);
        }

        private void AddCheck(string text, bool value, Action<bool> changed)
        {
            var p = NewPanel(42);
            var c = new CheckBox
            {
                Text = text, Checked = value, AutoSize = false, Location = new Point(10, 3), Size = new Size(575, 34),
                Font = new Font("Segoe UI", 9.3f), ForeColor = MenuText(), BackColor = MenuPanel(), FlatStyle = FlatStyle.Flat
            };
            c.CheckedChanged += delegate { if (!building && !applying) changed(c.Checked); };
            p.Controls.Add(c); body.Controls.Add(p);
        }

        private ComboBox AddCombo(string[] values, int selected, Action<int> changed)
        {
            var p = NewPanel(46);
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(10, 8), Size = new Size(580, 30),
                Font = new Font("Segoe UI", 9.3f), ForeColor = MenuText(), BackColor = MenuPanel(), FlatStyle = FlatStyle.Flat
            };
            c.Items.AddRange(values);
            if (values.Length > 0) c.SelectedIndex = Clamp(selected, 0, values.Length - 1);
            c.SelectedIndexChanged += delegate { if (!building && !applying && c.SelectedIndex >= 0) changed(c.SelectedIndex); };
            p.Controls.Add(c); body.Controls.Add(p); return c;
        }

        private void AddNumber(string label, decimal value, decimal min, decimal max, decimal inc, Action<decimal> changed, int decimals = 0)
        {
            var p = NewPanel(46);
            var l = new Label { Text = label, Tag = "TEXT", AutoSize = false, Location = new Point(10, 5), Size = new Size(390, 34), Font = new Font("Segoe UI", 9.2f), ForeColor = MenuText(), TextAlign = ContentAlignment.MiddleLeft };
            var n = new NumericUpDown
            {
                Location = new Point(430, 8), Size = new Size(155, 28), Minimum = min, Maximum = max,
                Increment = inc, DecimalPlaces = decimals, Value = Math.Max(min, Math.Min(max, value)),
                Font = new Font("Consolas", 9.2f), ForeColor = MenuText(), BackColor = Color.FromArgb(34, 37, 41), BorderStyle = BorderStyle.FixedSingle
            };
            n.ValueChanged += delegate { if (!building && !applying) changed(n.Value); };
            p.Controls.Add(l); p.Controls.Add(n); body.Controls.Add(p);
        }

        private FlatSlider AddSlider(string label, int min, int max, int value, Action<int> changed, Func<int, string> formatter, out Label valueLabel)
        {
            var p = NewPanel(68);
            var l = new Label { Text = label, Tag = "TEXT", AutoSize = false, Location = new Point(10, 4), Size = new Size(390, 25), Font = new Font("Segoe UI", 9.2f), ForeColor = MenuText(), TextAlign = ContentAlignment.MiddleLeft };
            var slider = new FlatSlider { Location = new Point(10, 34), Size = new Size(455, 22), Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)) };
            valueLabel = new Label { Text = formatter(slider.Value), Tag = "TEXT", AutoSize = false, Location = new Point(475, 29), Size = new Size(110, 30), Font = new Font("Consolas", 9.2f), ForeColor = MenuText(), TextAlign = ContentAlignment.MiddleRight };
            Label localValue = valueLabel;
            slider.ValueChanged += delegate { localValue.Text = formatter(slider.Value); if (!building && !applying) changed(slider.Value); };
            p.Controls.Add(l); p.Controls.Add(slider); p.Controls.Add(valueLabel); body.Controls.Add(p);
            return slider;
        }

        private void AddTripColor(string label, string value, string key)
        {
            var p = NewPanel(46);
            var l = new Label { Text = label, Tag = "TEXT", AutoSize = false, Location = new Point(10, 5), Size = new Size(330, 34), Font = new Font("Segoe UI", 9.2f), ForeColor = MenuText(), TextAlign = ContentAlignment.MiddleLeft };
            var sw = new Panel { Tag = "SWATCH", Location = new Point(350, 11), Size = new Size(34, 22), BackColor = SafeColor(value, Color.Gray) };
            var tb = new TextBox { Text = value ?? "#FFFFFF", Location = new Point(400, 9), Size = new Size(185, 28), Font = new Font("Consolas", 9.2f), ForeColor = MenuText(), BackColor = Color.FromArgb(34, 37, 41), BorderStyle = BorderStyle.FixedSingle };
            Action commit = delegate
            {
                if (building || applying) return;
                string v = NormalizeHex(tb.Text);
                if (v == null) { tb.Text = value; return; }
                SetText(key, v);
                Set("TripUseHudTheme", 0);
                sw.BackColor = SafeColor(v, Color.Gray);
            };
            tb.Leave += delegate { commit(); };
            tb.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { commit(); e.SuppressKeyPress = true; } };
            p.Controls.Add(l); p.Controls.Add(sw); p.Controls.Add(tb); body.Controls.Add(p);
        }

        private void AddColor(string label, string value, string key)
        {
            var p = NewPanel(46);
            var l = new Label { Text = label, Tag = "TEXT", AutoSize = false, Location = new Point(10, 5), Size = new Size(330, 34), Font = new Font("Segoe UI", 9.2f), ForeColor = MenuText(), TextAlign = ContentAlignment.MiddleLeft };
            var sw = new Panel { Tag = "SWATCH", Location = new Point(350, 11), Size = new Size(34, 22), BackColor = SafeColor(value, Color.Gray) };
            var tb = new TextBox { Text = value ?? "#FFFFFF", Location = new Point(400, 9), Size = new Size(185, 28), Font = new Font("Consolas", 9.2f), ForeColor = MenuText(), BackColor = Color.FromArgb(34, 37, 41), BorderStyle = BorderStyle.FixedSingle };
            Action commit = delegate
            {
                if (building || applying) return;
                string v = NormalizeHex(tb.Text);
                if (v == null) { tb.Text = value; return; }
                SetText(key, v);
                SetText("Theme", "CUSTOM");
                sw.BackColor = SafeColor(v, Color.Gray);
                ApplyTheme();
                RefreshFromSettings();
            };
            tb.Leave += delegate { commit(); };
            tb.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { commit(); e.SuppressKeyPress = true; } };
            p.Controls.Add(l); p.Controls.Add(sw); p.Controls.Add(tb); body.Controls.Add(p);
        }

        private Button AddButton(string text, Action clicked)
        {
            var p = NewPanel(52);
            var b = new Button
            {
                Text = text, Location = new Point(10, 8), Size = new Size(580, 34), FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 9.2f), ForeColor = MenuText(), BackColor = Color.FromArgb(48, 52, 57)
            };
            b.FlatAppearance.BorderColor = MenuAccent();
            b.Click += delegate { if (!building) clicked(); };
            p.Controls.Add(b); body.Controls.Add(p);
            return b;
        }

        private void AddBind(string name, string key)
        {
            var p = NewPanel(46);
            var l = new Label { Text = name, Tag = "TEXT", AutoSize = false, Location = new Point(10, 5), Size = new Size(330, 34), Font = new Font("Segoe UI", 9.2f), ForeColor = MenuText(), TextAlign = ContentAlignment.MiddleLeft };
            var b = new Button { Location = new Point(365, 7), Size = new Size(220, 30), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Semibold", 9f), ForeColor = MenuText(), BackColor = Color.FromArgb(48, 52, 57), TabStop = false };
            b.FlatAppearance.BorderColor = MenuAccent();
            b.Click += delegate { captureKeyField = key; UpdateBindings(); Focus(); };
            bindButtons[key] = b;
            p.Controls.Add(l); p.Controls.Add(b); body.Controls.Add(p);
            UpdateBindings();
        }

        private void UpdateBindings()
        {
            if (cfg == null) return;
            SetBind("MenuKey", cfg.MenuKey);
            SetBind("StartKey", cfg.StartKey);
            SetBind("AbortKey", cfg.AbortKey);
            SetBind("ManualFlipKey", cfg.ManualFlipKey);
            SetBind("SignalUpKey", cfg.SignalUpKey);
            SetBind("SignalDownKey", cfg.SignalDownKey);
        }

        private void SetBind(string key, string value)
        {
            Button b;
            if (!bindButtons.TryGetValue(key, out b)) return;
            b.Text = captureKeyField == key ? "PRESS A KEY..." : (string.IsNullOrWhiteSpace(value) || value.Equals("None", StringComparison.OrdinalIgnoreCase) ? "None" : value);
        }

        private void CaptureKey(KeyEventArgs e)
        {
            if (captureKeyField == null) return;
            if (e.KeyCode == Keys.Escape)
            {
                captureKeyField = null;
                UpdateBindings();
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
            string value = NormalizeKeyName(e.KeyCode);
            SetText(captureKeyField, value);
            captureKeyField = null;
            UpdateBindings();
            e.Handled = true; e.SuppressKeyPress = true;
        }

        private Panel NewPanel(int height)
        {
            return new Panel { Size = new Size(600, height), Margin = new Padding(0, 2, 0, 2), BackColor = MenuPanel() };
        }

        private void ApplyThemeToForm()
        {
            ThemePalette t = ThemePalette.From(cfg ?? new NavConfig());
            BackColor = t.MenuBack;
            body.BackColor = t.MenuBack;
        }

        private static void ApplyColors(Control root, ThemePalette t)
        {
            foreach (Control c in root.Controls)
            {
                string tag = c.Tag as string;
                if (c is FlowLayoutPanel) { c.BackColor = t.MenuBack; c.ForeColor = t.MenuText; }
                else if (c is Panel)
                {
                    if (!string.Equals(tag, "SWATCH", StringComparison.OrdinalIgnoreCase))
                    {
                        c.BackColor = t.MenuPanel;
                        c.ForeColor = t.MenuText;
                    }
                }
                else if (c is Label)
                {
                    if (!(c.Parent is Panel)) c.BackColor = Color.Transparent;
                    if (string.Equals(tag, "ACCENT", StringComparison.OrdinalIgnoreCase)) c.ForeColor = t.MenuAccent;
                    else if (string.Equals(tag, "GOOD", StringComparison.OrdinalIgnoreCase)) c.ForeColor = Color.FromArgb(150, 220, 180);
                    else c.ForeColor = t.MenuText;
                }
                else if (c is Button)
                {
                    c.BackColor = Color.FromArgb(48, 52, 57); c.ForeColor = t.MenuText;
                    Button b = (Button)c; b.FlatAppearance.BorderColor = t.MenuAccent;
                }
                else if (c is ComboBox) { c.BackColor = t.MenuPanel; c.ForeColor = t.MenuText; }
                else if (c is TextBox || c is NumericUpDown) { c.BackColor = Color.FromArgb(34, 37, 41); c.ForeColor = t.MenuText; }
                else if (c is CheckBox) { c.BackColor = t.MenuPanel; c.ForeColor = t.MenuText; }
                else if (c is FlatSlider) ((FlatSlider)c).SetPalette(t.MenuAccent, t.Border, t.MenuText, t.MenuPanel);
                ApplyColors(c, t);
            }
        }

        private Color MenuBackgroundColor() { return ThemePalette.From(cfg ?? new NavConfig()).MenuBack; }
        private Color MenuPanel() { return ThemePalette.From(cfg ?? new NavConfig()).MenuPanel; }
        private Color MenuText() { return ThemePalette.From(cfg ?? new NavConfig()).MenuText; }
        private Color MenuAccent() { return ThemePalette.From(cfg ?? new NavConfig()).MenuAccent; }

        private void Set(string key, double value)
        {
            if (building || applying) return;
            SetLocal(key, null, value);
            Send(new NavCommand { Type = "SET", Key = key, Value = value });
        }

        private void SetText(string key, string value)
        {
            if (building || applying) return;
            SetLocal(key, value, 0);
            Send(new NavCommand { Type = "SET", Key = key, Text = value });
        }

        private void SetLocal(string key, string text, double value)
        {
            if (cfg == null) cfg = new NavConfig();
            try
            {
                var f = typeof(NavConfig).GetField(key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (f == null) return;
                if (f.FieldType == typeof(string)) f.SetValue(cfg, text ?? "");
                else if (f.FieldType == typeof(bool)) f.SetValue(cfg, value >= .5);
                else if (f.FieldType == typeof(int)) f.SetValue(cfg, (int)Math.Round(value));
                else if (f.FieldType == typeof(double)) f.SetValue(cfg, value);
            }
            catch { }
        }

        private void Send(NavCommand command)
        {
            if (Command != null) Command(command);
        }

        private void ImportOrSetStatus(string text)
        {
            if (zeoSyncStatus != null) zeoSyncStatus.Text = text;
        }

        private static string NormalizeFrame(string value)
        {
            string n = (value ?? "WAR ROOM").Trim().ToUpperInvariant();
            if (n == "FLIGHT HUD") return "FIGHTER HUD";
            if (n == "MINIMAL") return "STEALTH";
            if (n == "CUT CORNER" || n == "SEGMENTED ARMOR" || n == "CUSTOM") return "SE INDUSTRIAL";
            return n;
        }

        private static string NormalizeTheme(string value)
        {
            string n = (value ?? "WAR ROOM").Trim().ToUpperInvariant();
            foreach (string x in Themes) if (x == n) return n;
            return "WAR ROOM";
        }

        private static int IndexOf(string[] values, string value)
        {
            for (int i = 0; i < values.Length; i++) if (values[i].Equals(value ?? "", StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }

        private static Keys KeyFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Equals("None", StringComparison.OrdinalIgnoreCase)) return Keys.None;
            string n = name.Replace(" ", "");
            Keys k;
            return Enum.TryParse<Keys>(n, true, out k) ? k : Keys.None;
        }

        private static string NormalizeKeyName(Keys k)
        {
            if (k >= Keys.D0 && k <= Keys.D9) return "D" + ((int)k - (int)Keys.D0);
            return k.ToString();
        }

        private static string GpsSig(GpsDto g)
        {
            return g == null ? "" : (g.Name ?? "") + "|" + g.X.ToString("R") + "|" + g.Y.ToString("R") + "|" + g.Z.ToString("R");
        }

        private sealed class GpsItem
        {
            public readonly GpsDto G;
            public GpsItem(GpsDto g) { G = g; }
            public override string ToString() { return G.Name + "   //   " + Dist(G.Distance); }
        }

        private static int Clamp(int v, int min, int max) { return Math.Max(min, Math.Min(max, v)); }
        private static string Dist(double m) { if (m >= 1000000) return (m / 1000000).ToString("0.00") + " Mm"; if (m >= 1000) return (m / 1000).ToString("0.0") + " km"; return m.ToString("0") + " m"; }
        private static string Speed(double v) { return v.ToString(v >= 1000 ? "0" : "0.0") + " M/S"; }
        private static string SigKmText(double km) { if (km < 0) return "-- KM"; return km.ToString(km >= 100 ? "0" : "0.0") + " KM"; }
        private static string Time(double sec) { if (double.IsNaN(sec) || double.IsInfinity(sec) || sec < 0) return "--:--"; int x = (int)Math.Round(sec); return (x / 3600 > 0 ? (x / 3600).ToString("00") + ":" : "") + ((x / 60) % 60).ToString("00") + ":" + (x % 60).ToString("00"); }
        private static Color SafeColor(string hex, Color fallback) { try { return ColorTranslator.FromHtml(hex); } catch { return fallback; } }
        private static bool IsHex(string s) { if (string.IsNullOrWhiteSpace(s) || s.Length != 7 || s[0] != '#') return false; for (int i = 1; i < 7; i++) if (!Uri.IsHexDigit(s[i])) return false; return true; }
        private static string NormalizeHex(string value) { if (string.IsNullOrWhiteSpace(value)) return null; string s = value.Trim(); if (!s.StartsWith("#")) s = "#" + s; return IsHex(s) ? s.ToUpperInvariant() : null; }

        private sealed class FlatSlider : Control
        {
            private int minimum;
            private int maximum = 100;
            private int value;
            private bool dragging;
            private Color fill = Color.Firebrick, track = Color.DimGray, knob = Color.White, back = Color.FromArgb(17, 20, 25);
            public event EventHandler ValueChanged;

            public int Minimum { get { return minimum; } set { minimum = value; if (maximum <= minimum) maximum = minimum + 1; Value = this.value; } }
            public int Maximum { get { return maximum; } set { maximum = Math.Max(minimum + 1, value); Value = this.value; } }
            public int Value { get { return value; } set { int v = Math.Max(minimum, Math.Min(maximum, value)); if (this.value == v) return; this.value = v; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); } }

            public FlatSlider()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
                Cursor = Cursors.Hand;
            }

            public void SetPalette(Color accent, Color border, Color text, Color panel)
            {
                fill = accent; track = border; knob = text; back = panel; BackColor = panel; Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(back);
                float y = Height * .5f;
                float left = 4f, right = Math.Max(left + 1f, Width - 4f);
                float t = maximum == minimum ? 0 : (float)(value - minimum) / (maximum - minimum);
                float x = left + (right - left) * t;
                using (var p = new Pen(Color.FromArgb(170, track), 2f)) e.Graphics.DrawLine(p, left, y, right, y);
                using (var p = new Pen(Color.FromArgb(240, fill), 3f)) e.Graphics.DrawLine(p, left, y, x, y);
                using (var b = new SolidBrush(knob)) e.Graphics.FillRectangle(b, x - 3.5f, y - 5f, 7f, 10f);
            }

            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); dragging = true; UpdateFromMouse(e.X); Capture = true; }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) UpdateFromMouse(e.X); }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; UpdateFromMouse(e.X); }
            private void UpdateFromMouse(int x)
            {
                float t = Math.Max(0f, Math.Min(1f, (x - 4f) / Math.Max(1f, Width - 8f)));
                Value = minimum + (int)Math.Round((maximum - minimum) * t);
            }
        }
    }

}




