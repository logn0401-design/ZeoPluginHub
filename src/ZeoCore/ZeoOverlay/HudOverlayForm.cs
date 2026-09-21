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
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ZeoOverlay
{
    internal sealed partial class HudOverlayForm : Form
    {
        private const int HotkeyId = 0x5A40;
        private readonly OverlaySettings _settings;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        private readonly object _frameLock = new object();
        private OverlayFrame _frame = new OverlayFrame();
        private OverlayMarkerUpdate _cameraMarkers;
        private long _lastFrameUtcMs;
        private long _lastRenderedSequence = -1;
        private UdpClient _udp;
        private Thread _rxThread;
        private volatile bool _running = true;
        private readonly System.Windows.Forms.Timer _renderTimer;
        private readonly System.Windows.Forms.Timer _settingsTimer;
        private SettingsForm _menu;
        private IntPtr _gameHwnd = IntPtr.Zero;
        private NativeMethods.RECT _lastRect;
        private DateTime _gameMissingSince = DateTime.MinValue;
        private DateTime _lastGameForegroundUtc = DateTime.MinValue;
        private bool _focusStateInitialized;
        private bool _lastFocusState;
        private int _registeredVk;
        private bool _captureHudApplied;
        private bool _captureMenuApplied;
        private DateTime _lastMenuToggleUtc = DateTime.MinValue;
        private int _gameProcessId;
        private bool _loggedGameFound;
        private bool _loggedFirstFrame;
        private static readonly object OverlayLogLock = new object();

        private sealed class MarkerSmoothState
        {
            public PointF Point;
            public DateTime LastUtc;
            public DateTime SeenUtc;
            public bool Offscreen;
        }

        private readonly Dictionary<string, MarkerSmoothState> _markerSmooth = new Dictionary<string, MarkerSmoothState>(StringComparer.Ordinal);
        private DateTime _lastSmoothPruneUtc = DateTime.MinValue;

        internal bool CaptureHudApplied { get { return _captureHudApplied; } }
        internal bool CaptureMenuApplied { get { return _captureMenuApplied; } }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED |
                              NativeMethods.WS_EX_TRANSPARENT |
                              NativeMethods.WS_EX_TOOLWINDOW |
                              NativeMethods.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        internal HudOverlayForm(OverlaySettings settings, int port)
        {
            _settings = settings;
            LogOverlay("ZeoOverlay V1.4f NATIVE SE UI BRIDGE + WEAPON CORE starting. authoritative game HWND/PID + WC/distress diagnostics active.");
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            // v0.4.4: the capture-safe HUD is an external layered window.
            // It MUST participate in the topmost band or fullscreen/borderless Space
            // Engineers can cover it whenever the settings menu is closed. The HUD
            // remains click-through + no-activate, so it never steals pilot input.
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(0, 0, 1, 1);

            _menu = new SettingsForm(_settings, this);

            StartReceiver(port);

            _renderTimer = new System.Windows.Forms.Timer { Interval = _settings.FastCameraMarkers ? 16 : 33 };
            _renderTimer.Tick += delegate { RenderTick(); };
            _renderTimer.Start();

            _settingsTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _settingsTimer.Tick += delegate
            {
                if (_settings.ReloadIfChanged())
                {
                    _renderTimer.Interval=_settings.FastCameraMarkers ? 16 : 33;
                    RegisterMenuHotkey();
                    ApplyCaptureAffinity();
                    if (_menu != null && !_menu.IsDisposed) _menu.RefreshFromSettings();
                    _lastRenderedSequence = -1;
                }
            };
            _settingsTimer.Start();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterMenuHotkey();
            ApplyCaptureAffinity();
        }

        private void StartReceiver(int port)
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            _rxThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "ZeoOverlay-RX" };
            _rxThread.Start();
        }

        private void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Loopback, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _udp.Receive(ref remote);
                    if (remote.Address == null || !IPAddress.IsLoopback(remote.Address)) continue;
                    string text = Encoding.UTF8.GetString(data);
                    var packet = _json.Deserialize<OverlayPacket>(text);
                    if (packet == null) continue;

                    if (string.Equals(packet.Kind, "frame", StringComparison.OrdinalIgnoreCase) && packet.Frame != null)
                    {
                        lock (_frameLock)
                        {
                            if(_frame!=null && (packet.Frame.GameProcessId!=_frame.GameProcessId ||
                                (packet.Frame.Sequence<_frame.Sequence && packet.Frame.UtcMs>=_frame.UtcMs)))
                                _cameraMarkers=null;
                            _frame = packet.Frame;
                            if(packet.Frame.Layout!=null) _layoutReplyEndpoint=new IPEndPoint(remote.Address,remote.Port);
                            _lastFrameUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            if (!_loggedFirstFrame)
                            {
                                _loggedFirstFrame = true;
                                LogOverlay("First ZeoCore HUD frame received over localhost UDP. clientRect=" + packet.Frame.GameLeft + "," + packet.Frame.GameTop + " " + packet.Frame.GameWidth + "x" + packet.Frame.GameHeight + " focused=" + packet.Frame.GameFocused + " gamePid=" + packet.Frame.GameProcessId + " gameHwnd=0x" + packet.Frame.GameWindowHandle.ToString("X") + " markers=" + (packet.Frame.Markers == null ? 0 : packet.Frame.Markers.Count) + ".");
                            }
                        }
                    }
                    else if (string.Equals(packet.Kind,"markers",StringComparison.OrdinalIgnoreCase) && packet.MarkerUpdate!=null)
                    {
                        lock (_frameLock)
                        {
                            if (_cameraMarkers==null || packet.MarkerUpdate.Sequence>_cameraMarkers.Sequence)
                                _cameraMarkers=packet.MarkerUpdate;
                        }
                    }
                    else if (string.Equals(packet.Kind, "command", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(packet.Command, "open-menu", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(packet.Command, "toggle-menu", StringComparison.OrdinalIgnoreCase))
                            SafeBegin(ToggleMenuDebounced);
                        else if (string.Equals(packet.Command, "shutdown", StringComparison.OrdinalIgnoreCase))
                            SafeBegin(Close);
                    }
                }
                catch (SocketException) { if (!_running) break; }
                catch (ObjectDisposedException) { break; }
                catch { Thread.Sleep(25); }
            }
        }

        private void SafeBegin(Action action)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(action); } catch { }
        }

        private void ToggleMenuDebounced()
        {
            if ((DateTime.UtcNow - _lastMenuToggleUtc).TotalMilliseconds < 220) return;
            _lastMenuToggleUtc = DateTime.UtcNow;
            ToggleMenu();
        }

        internal void ToggleMenu()
        {
            if (_menu == null || _menu.IsDisposed) _menu = new SettingsForm(_settings, this);
            if (_menu.Visible)
            {
                CloseMenuToGame();
                return;
            }

            _menu.RefreshFromSettings();
            PositionMenu();
            _menu.TopMost = true; // ZEOCORE_V11D_MENU_TOPMOST
            _menu.Show();
            _menu.BringToFront();
            _menu.Activate();
            try { NativeMethods.SetForegroundWindow(_menu.Handle); } catch { }
            ApplyCaptureAffinity(); // ZEOCORE_V13B_INTERACTIVE_MENU
        }

        internal void CloseMenuToGame()
        {
            try { if (_menu != null && !_menu.IsDisposed) _menu.Hide(); } catch { }
            try
            {
                if (_gameHwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(_gameHwnd))
                    NativeMethods.SetForegroundWindow(_gameHwnd);
            }
            catch { }
        }

        private void PositionMenu()
        {
            Rectangle target = Screen.PrimaryScreen.WorkingArea;
            if (_gameHwnd != IntPtr.Zero)
            {
                NativeMethods.RECT r;
                if (TryGetGameRect(_gameHwnd, out r))
                    target = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            }

            int x = target.Left + Math.Max(12, (target.Width - _menu.Width) / 2);
            int y = target.Top + Math.Max(12, (target.Height - _menu.Height) / 2);
            _menu.Location = new Point(x, y);
        }

        internal void SettingsChanged()
        {
            _settings.Save();
            _renderTimer.Interval=_settings.FastCameraMarkers ? 16 : 33;
            RegisterMenuHotkey();
            ApplyCaptureAffinity();
            _lastRenderedSequence = -1;
        }

        private void RegisterMenuHotkey()
        {
            // v0.4.3: menu opening is captured inside Space Engineers via
            // MyAPIGateway.Input. This avoids Windows global-hotkey conflicts and
            // prevents duplicate open/close events. The external menu itself handles
            // the configured key + ESC while it has focus.
            if (_registeredVk != 0 && IsHandleCreated)
            {
                try { NativeMethods.UnregisterHotKey(Handle, HotkeyId); } catch { }
                _registeredVk = 0;
            }
        }

        private static int MenuKeyToVk(int key)
        {
            switch (key)
            {
                case 1: return 0x2D; // INSERT
                case 2: return 0x21; // PAGE UP
                case 3: return 0x22; // PAGE DOWN
                case 4: return 0x23; // END
                default: return 0x24; // HOME
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
        }

        internal void ApplyCaptureAffinity()
        {
            if (IsHandleCreated)
            {
                uint desired = _settings.CaptureSafeHud ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE;
                bool ok = NativeMethods.SetWindowDisplayAffinity(Handle, desired);
                uint actual;
                bool verified = ok && NativeMethods.GetWindowDisplayAffinity(Handle, out actual) && actual == desired;
                _captureHudApplied = _settings.CaptureSafeHud && verified;
                LogOverlay("HUD capture affinity requested=" + _settings.CaptureSafeHud + " verified=" + verified + ".");
            }

            if (_menu != null && _menu.IsHandleCreated)
            {
                uint desired = _settings.CaptureSafeMenu ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE;
                bool ok = NativeMethods.SetWindowDisplayAffinity(_menu.Handle, desired);
                uint actual;
                bool verified = ok && NativeMethods.GetWindowDisplayAffinity(_menu.Handle, out actual) && actual == desired;
                _captureMenuApplied = _settings.CaptureSafeMenu && verified;
                LogOverlay("MENU capture affinity requested=" + _settings.CaptureSafeMenu + " verified=" + verified + ".");
            }
        }

        private void RenderTick()
        {
            if (!_running) return;

            OverlayFrame frame;
            long received;
            OverlayMarkerUpdate cameraMarkers;
            lock (_frameLock) { frame = _frame; received = _lastFrameUtcMs; cameraMarkers=_cameraMarkers; }
            long renderSequence=frame==null ? 0 : frame.Sequence;
            if (_settings.FastCameraMarkers && frame!=null && cameraMarkers!=null &&
                cameraMarkers.Sequence>frame.Sequence && cameraMarkers.UtcMs>=frame.UtcMs &&
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-cameraMarkers.UtcMs<150)
            {
                frame.Markers=cameraMarkers.Markers;
                renderSequence=cameraMarkers.Sequence;
            }
            long age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - received;
            if (frame == null || received == 0 || age > 2500 || (!frame.HudEnabled && frame.Layout==null))
            {
                HideOverlay();
                return;
            }

            if(_layoutWasActive && frame.Layout==null) { _settings.Reload(); _lastRenderedSequence=-1; }
            _layoutWasActive=frame.Layout!=null;
            NativeMethods.RECT rect;
            bool haveRect = TryFrameRect(frame, out rect);
            if (!haveRect)
            {
                // Secondary compatibility path for older frames / unusual builds.
                FindGameWindow();
                haveRect = _gameHwnd != IntPtr.Zero && TryGetGameRect(_gameHwnd, out rect);
            }
            if (!haveRect)
            {
                // Last-resort visibility path: a fresh frame proves ZeoCore is running
                // inside Space Engineers. Use the primary screen rather than showing
                // absolutely nothing. This makes failures diagnosable instead of silent.
                Rectangle b = Screen.PrimaryScreen.Bounds;
                rect = new NativeMethods.RECT { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Bottom };
                haveRect = b.Width >= 200 && b.Height >= 200;
            }

            if (!haveRect || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            {
                HideOverlay();
                return;
            }

            // Do not self-terminate when window discovery is imperfect. The plugin
            // explicitly sends shutdown on unload. This fixes the ~30 second menu loss.
            _gameMissingSince = DateTime.MinValue;

            // v0.5.9.3: the in-process ZeoCore plugin is the authoritative source for
            // the Space Engineers window identity. This avoids process-name/window
            // discovery failures in Pulsar Legacy while keeping ALT+TAB privacy intact.
            if (frame.GameWindowHandle != 0)
            {
                try
                {
                    IntPtr authoritative = new IntPtr(frame.GameWindowHandle);
                    if (NativeMethods.IsWindowVisible(authoritative))
                    {
                        _gameHwnd = authoritative;
                        _gameProcessId = frame.GameProcessId;
                    }
                }
                catch { }
            }
            if (_gameProcessId == 0 && frame.GameProcessId != 0)
                _gameProcessId = frame.GameProcessId;

            // Use the game window as the overlay owner whenever possible. This keeps
            // Zeo above the game without making it globally top-most over other apps.
            if (_gameHwnd == IntPtr.Zero) FindGameWindow();
            AttachToGameOwner();

            // v0.5.5: HUD is game-focus scoped. The capture-safe overlay used to
            // remain globally visible after ALT+TAB because it is a top-most layered
            // window. Require Space Engineers to be the foreground process, with a
            // very short grace period to absorb transient Win32 focus hand-offs.
            // The settings menu is a separate form and is not forcibly closed here.
            bool foregroundNow = IsSpaceEngineersForeground();
            bool menuForeground = IsZeoMenuForeground();
            bool effectiveGameFocus = foregroundNow || frame.GameFocused;
            if (!_focusStateInitialized || effectiveGameFocus != _lastFocusState)
            {
                _focusStateInitialized = true;
                _lastFocusState = effectiveGameFocus;
                LogOverlay("Game focus -> " + (effectiveGameFocus ? "FOREGROUND" : "BACKGROUND") +
                    " overlayDetect=" + foregroundNow + " frameDetect=" + frame.GameFocused +
                    " gamePid=" + _gameProcessId + " gameHwnd=0x" + _gameHwnd.ToInt64().ToString("X") + ".");
            }
            if (foregroundNow || frame.GameFocused || menuForeground)
                _lastGameForegroundUtc = DateTime.UtcNow;

            bool focusRecent = _lastGameForegroundUtc != DateTime.MinValue &&
                               (DateTime.UtcNow - _lastGameForegroundUtc).TotalMilliseconds <= 180;

            // Keep the HUD visible while the Zeo settings window itself owns focus.
            // If the user ALT+TABs from either game or menu to another application,
            // menuForeground becomes false and the HUD hides after the short grace.
            if ((!foregroundNow && !frame.GameFocused && !menuForeground && !focusRecent) ||
                (_gameHwnd != IntPtr.Zero && NativeMethods.IsIconic(_gameHwnd)))
            {
                HideOverlay();
                return;
            }

            bool rectChanged = rect.Left != _lastRect.Left || rect.Top != _lastRect.Top || rect.Right != _lastRect.Right || rect.Bottom != _lastRect.Bottom;
            if (!rectChanged && renderSequence == _lastRenderedSequence) return;
            _lastRect = rect;
            _lastRenderedSequence = renderSequence;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 200 || height < 200) return;

            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.CompositingMode = CompositingMode.SourceOver;
                g.Clear(Color.Transparent);
                DrawHud(g, width, height, frame);
                Present(bitmap, rect.Left, rect.Top);
                EnsureHudTopmost();
            }
        }

        private void EnsureHudTopmost()
        {
            try
            {
                if (!IsHandleCreated) return;
                // UpdateLayeredWindow updates pixels/position, but it does not reliably
                // keep a no-activate layered form above an active DirectX window.
                // Reassert only the Z-order; do not move, resize, or activate it.
                bool ok = NativeMethods.SetWindowPos(
                    Handle,
                    NativeMethods.HWND_TOPMOST,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    LogOverlay("SetWindowPos TOPMOST FAILED win32=" + err + ".");
                }
            }
            catch (Exception ex)
            {
                LogOverlay("SetWindowPos TOPMOST ERROR " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool TryFrameRect(OverlayFrame frame, out NativeMethods.RECT rect)
        {
            rect = new NativeMethods.RECT();
            if (frame == null || !frame.GameWindowValid || frame.GameWidth < 200 || frame.GameHeight < 200) return false;
            rect.Left = frame.GameLeft;
            rect.Top = frame.GameTop;
            rect.Right = frame.GameLeft + frame.GameWidth;
            rect.Bottom = frame.GameTop + frame.GameHeight;
            return rect.Right > rect.Left && rect.Bottom > rect.Top;
        }

        private void AttachToGameOwner()
        {
            try
            {
                if (!IsHandleCreated || _gameHwnd == IntPtr.Zero) return;
                NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWLP_HWNDPARENT, _gameHwnd);
            }
            catch { }
        }

        private void HideOverlay()
        {
            try { if (IsHandleCreated) NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE); } catch { }
        }

        private bool FindGameWindow()
        {
            try
            {
                if (_gameHwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(_gameHwnd))
                {
                    uint pid;
                    NativeMethods.GetWindowThreadProcessId(_gameHwnd, out pid);
                    _gameProcessId = unchecked((int)pid);
                    return true;
                }

                foreach (var p in Process.GetProcessesByName("SpaceEngineers"))
                {
                    IntPtr h = p.MainWindowHandle;
                    if (h == IntPtr.Zero || !NativeMethods.IsWindowVisible(h))
                        h = FindVisibleTopLevelWindowForProcess(p.Id);

                    if (h != IntPtr.Zero && NativeMethods.IsWindowVisible(h))
                    {
                        _gameHwnd = h;
                        _gameProcessId = p.Id;
                        if (!_loggedGameFound)
                        {
                            _loggedGameFound = true;
                            LogOverlay("Space Engineers window found. PID=" + p.Id + " HWND=0x" + h.ToInt64().ToString("X") + ".");
                        }
                        return true;
                    }
                }
            }
            catch (Exception ex) { LogOverlay("FindGameWindow error: " + ex.GetType().Name + ": " + ex.Message); }
            _gameHwnd = IntPtr.Zero;
            _gameProcessId = 0;
            return false;
        }

        private static IntPtr FindVisibleTopLevelWindowForProcess(int processId)
        {
            IntPtr found = IntPtr.Zero;
            try
            {
                NativeMethods.EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
                {
                    if (!NativeMethods.IsWindowVisible(hwnd)) return true;
                    uint pid;
                    NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
                    if (pid == (uint)processId)
                    {
                        found = hwnd;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return found;
        }

        private bool IsZeoMenuForeground()
        {
            try
            {
                if (_menu == null || _menu.IsDisposed || !_menu.Visible || !_menu.IsHandleCreated) return false;
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                if (foreground == IntPtr.Zero) return false;
                if (foreground == _menu.Handle) return true;

                // WinForms controls/dialog children can briefly own foreground while
                // still belonging to the ZeoOverlay process. Only accept that process
                // while the settings form is visible; this does not keep the HUD alive
                // after ALT+TAB to an unrelated application.
                uint pid;
                NativeMethods.GetWindowThreadProcessId(foreground, out pid);
                return pid == (uint)Process.GetCurrentProcess().Id;
            }
            catch { return false; }
        }

        private bool IsSpaceEngineersForeground()
        {
            try
            {
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                if (foreground == IntPtr.Zero) return false;
                if (_gameHwnd != IntPtr.Zero && foreground == _gameHwnd) return true;

                uint pid;
                NativeMethods.GetWindowThreadProcessId(foreground, out pid);
                if (_gameProcessId != 0 && pid == (uint)_gameProcessId) return true;

                if (pid != 0)
                {
                    using (var p = Process.GetProcessById(unchecked((int)pid)))
                        return string.Equals(p.ProcessName, "SpaceEngineers", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
            return false;
        }

        private void PollMenuKeyFallback()
        {
            // Intentionally disabled in v0.4.3. Space Engineers game-input is the
            // authoritative menu-open path; SettingsForm handles close keys.
        }

        private static void LogOverlay(string text)
        {
            try
            {
                lock (OverlayLogLock)
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulsar", "ZeoCore");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "overlay.log"), DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC' ") + text + Environment.NewLine);
                }
            }
            catch { }
        }

        private static bool TryGetGameRect(IntPtr hwnd, out NativeMethods.RECT rect)
        {
            rect = new NativeMethods.RECT();

            // Match the camera projection to the actual game CLIENT area. DWM
            // extended-frame bounds include non-client pixels and can offset every
            // world marker from the ship it belongs to.
            try
            {
                NativeMethods.RECT client;
                var origin = new NativeMethods.POINT(0, 0);
                if (NativeMethods.GetClientRect(hwnd, out client) &&
                    client.Right > client.Left && client.Bottom > client.Top &&
                    NativeMethods.ClientToScreen(hwnd, ref origin))
                {
                    rect.Left = origin.X;
                    rect.Top = origin.Y;
                    rect.Right = origin.X + (client.Right - client.Left);
                    rect.Bottom = origin.Y + (client.Bottom - client.Top);
                    return true;
                }
            }
            catch { }

            try
            {
                int hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf(typeof(NativeMethods.RECT)));
                if (hr == 0 && rect.Right > rect.Left && rect.Bottom > rect.Top) return true;
            }
            catch { }
            try { return NativeMethods.GetWindowRect(hwnd, out rect); } catch { return false; }
        }

        private void Present(Bitmap bitmap, int left, int top)
        {
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                screenDc = NativeMethods.GetDC(IntPtr.Zero);
                memDc = NativeMethods.CreateCompatibleDC(screenDc);
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

                var dst = new NativeMethods.POINT(left, top);
                var src = new NativeMethods.POINT(0, 0);
                var size = new NativeMethods.SIZE(bitmap.Width, bitmap.Height);
                var blend = new NativeMethods.BLENDFUNCTION
                {
                    BlendOp = NativeMethods.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = NativeMethods.AC_SRC_ALPHA
                };

                bool ok = NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);
                if (ok)
                {
                    NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNA);
                }
                else
                {
                    int err = Marshal.GetLastWin32Error();
                    LogOverlay("UpdateLayeredWindow FAILED win32=" + err + ".");
                }
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero && memDc != IntPtr.Zero) NativeMethods.SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
                if (memDc != IntPtr.Zero) NativeMethods.DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private void DrawHud(Graphics g, int width, int height, OverlayFrame frame)
        {
            if(frame.Layout!=null) { DrawLayoutPreview(g,width,height,frame); return; }
            if (_settings.ShowDistressBanner && (frame.DistressLocalActive || (frame.DistressAlerts != null && frame.DistressAlerts.Count > 0)))
                DrawDistressBanner(g, width, height, frame);

            if (!frame.HasShip)
            {
                // An enabled tracker explains its empty state outside a ship too.
                if (_settings.ShowAmmoPanel && (!_settings.FollowGameHud || frame.GameHudState != 0))
                    DrawAmmoPanel(g, width, height, frame);
                if (frame.ObserverTosEnabled)
                {
                    if (frame.ShowTrackPanel)
                        DrawScopePanel(g, width, height, frame);
                }
                else if (frame.ShowFlightData)
                {
                    DrawPanel(g, width, height, _settings.FlightX, _settings.FlightY,
                        _settings.TextScale * _settings.FlightScale, "ZEO // HUD",
                        new List<string> { "WAITING FOR CONTROLLED GRID" }, false, _settings.BackingShipInfo);
                }
                return;
            }

            // ZEOCORE_V067H3_DIRECT_GAME_HUD_GATE
            // FULL (1) and MINIMAL (2) both keep Zeo visible.
            // Only the game's real OFF state (0) hides Zeo.
            if (_settings.FollowGameHud && frame.GameHudState == 0)
                return;

            if (frame.ShowCrosshair)
                DrawCrosshair(g, width, height);

            if (frame.ShowFlightData)
                DrawFlightPanel(g, width, height, frame);

            if (frame.ShowTrackPanel)
                DrawScopePanel(g, width, height, frame);

            if (_settings.ShowAmmoPanel)
                DrawAmmoPanel(g, width, height, frame);

            if (frame.ShowLinkPanel)
                DrawFleetPanel(g, width, height, frame);

            if (frame.ShowRosterPanel && _settings.ShowRosterPanel)
                DrawRosterPanel(g, width, height, frame);

            if (frame.Markers != null)
                for (int i = 0; i < frame.Markers.Count; i++) DrawMarker(g, width, height, frame.Markers[i]);
        }

        private void DrawCrosshair(Graphics g, int width, int height)
        {
            float s = (float)(14.0 * _settings.CrosshairScale);
            float cx = width / 2f, cy = height / 2f;
            Color c = _settings.ColorOf(_settings.CrosshairColor, Color.Gainsboro);
            using (var p = new Pen(Color.FromArgb(220, c), Math.Max(1f, s / 14f)))
            {
                g.DrawLine(p, cx - s, cy, cx - s * .30f, cy);
                g.DrawLine(p, cx + s * .30f, cy, cx + s, cy);
                g.DrawLine(p, cx, cy - s, cx, cy - s * .30f);
                g.DrawLine(p, cx, cy + s * .30f, cx, cy + s);
            }
        }

        private void DrawFlightPanel(Graphics g, int width, int height, OverlayFrame frame)
        {
            float scale = (float)Math.Max(.60, Math.Min(2.25, _settings.TextScale * _settings.FlightScale));
            float panelScale = (float)Math.Max(.60, Math.Min(2.25, _settings.FlightScale));
            float pad = 12f * (float)_settings.PanelPaddingScale * panelScale;
            float titleSize = Math.Max(12f, 15f * scale);
            float labelSize = Math.Max(9f, 10.5f * scale);
            float rowH = Math.Max(22f, 26f * scale);
            float barH = Math.Max(6f, 8f * scale);

            var items = new List<int>();
            if (_settings.ShowShipH2O) items.Add(0);
            if (_settings.ShowShipO2) items.Add(1);
            if (_settings.ShowShipFusion) items.Add(2);
            if (_settings.ShowShipDrive) items.Add(3);
            if (_settings.ShowShipReactor) items.Add(4);
            if (_settings.ShowShipPower) items.Add(5);
            if (_settings.ShowShipHp) items.Add(6);
            if (_settings.ShowShipSpeed) items.Add(7);
            if (items.Count == 0) items.Add(7);

            bool two = _settings.ShipLayout == 1;
            bool compact = _settings.ShipLayout == 2;
            if (two || compact)
            {
                // Semantic two-column ordering: resources/speed on the left,
                // ship systems/health on the right. Missing rows simply collapse.
                var ordered = new List<int>();
                int[] preferred = { 0, 3, 1, 4, 2, 6, 7, 5 };
                for (int oi = 0; oi < preferred.Length; oi++)
                    if (items.Contains(preferred[oi])) ordered.Add(preferred[oi]);
                items = ordered;
            }
            int visualRows = (two || compact) ? (int)Math.Ceiling(items.Count / 2.0) : items.Count;
            float panelW = compact ? Math.Max(380f, 430f * scale) : (two ? Math.Max(520f, 650f * scale) : Math.Max(330f, 390f * scale));
            float panelH = pad * 2f + titleSize + 10f * scale + Math.Max(1, visualRows) * rowH;
            PointF origin = NormToPixel(width, height, _settings.FlightX, _settings.FlightY);
            RectangleF rect = ClampRect(width, height, origin.X, origin.Y, panelW, panelH);
            RecordLayoutBounds("ship",rect);
            Color accent = _settings.ColorOf(_settings.MenuAccentColor, Color.Goldenrod);
            DrawTacticalPlate(g, rect, accent, _settings.BackingShipInfo);

            Color primary = _settings.ColorOf(_settings.HudTextColor, Color.Gainsboro);
            Color secondary = _settings.ColorOf(_settings.HudSecondaryColor, Color.Silver);
            using (var titleFont = new Font(HudTitleFont(), titleSize, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var font = new Font(HudBodyFont(true), labelSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var pBrush = new SolidBrush(primary))
            using (var sBrush = new SolidBrush(secondary))
            {
                float x0 = rect.X + pad + 4f * panelScale;
                float y0 = rect.Y + pad;
                string shipName = ShortName(string.IsNullOrWhiteSpace(frame.OwnGridName) ? "SHIP STATUS" : frame.OwnGridName, two ? 52 : 34);
                SizeF shipTitle = g.MeasureString(shipName, titleFont);
                g.DrawString(shipName, titleFont, pBrush, rect.Left + (rect.Width - shipTitle.Width) / 2f, y0);
                y0 += titleFont.Height + 8f * scale;
                float colGap = 14f * scale;
                float colW = (rect.Width - pad * 2f - colGap) / 2f;

                for (int i = 0; i < items.Count; i++)
                {
                    int col = (two || compact) ? i % 2 : 0;
                    int row = (two || compact) ? i / 2 : i;
                    float x = x0 + col * (colW + colGap);
                    float y = y0 + row * rowH;
                    float widthForItem = (two || compact) ? colW : (rect.Width - pad * 2f);
                    int kind = items[i];
                    if (kind == 0) DrawStatusBar(g, rect, font, pBrush, sBrush, "H20", frame.H2O, Percent(frame.H2O), x, y, scale, barH, true, widthForItem, .50, .25);
                    else if (kind == 1) DrawStatusBar(g, rect, font, pBrush, sBrush, "O2", frame.O2, Percent(frame.O2), x, y, scale, barH, true, widthForItem, .50, .25);
                    else if (kind == 2)
                    {
                        double ratio = _settings.FusionReserveTarget > 0 ? frame.FusionPellets / _settings.FusionReserveTarget : 0;
                        DrawStatusBar(g, rect, font, pBrush, sBrush, "FUS", ratio, frame.FusionPellets.ToString("0") + " / " + _settings.FusionReserveTarget, x, y, scale, barH, true, widthForItem, .50, .25);
                    }
                    else if (kind == 3) DrawStatusBar(g, rect, font, pBrush, sBrush, "DRIVE", frame.DriveHealth, Percent(frame.DriveHealth), x, y, scale, barH, true, widthForItem, .70, .40);
                    else if (kind == 4) DrawStatusBar(g, rect, font, pBrush, sBrush, "REACT", frame.ReactorHealth, Percent(frame.ReactorHealth), x, y, scale, barH, true, widthForItem, .70, .40);
                    else if (kind == 5)
                    {
                        double ratio = frame.PowerMax > 0 ? frame.PowerCurrent / frame.PowerMax : -1;
                        DrawStatusBar(g, rect, font, pBrush, sBrush, "PWR", ratio, FormatPower(frame.PowerCurrent) + " / " + FormatPower(frame.PowerMax), x, y, scale, barH, false, widthForItem, .75, .90);
                    }
                    else if (kind == 6) DrawStatusBar(g, rect, font, pBrush, sBrush, "SHIP HP", frame.ShipHp, Percent(frame.ShipHp), x, y, scale, barH, true, widthForItem, .70, .40);
                    else
                    {
                        g.DrawString("SPD", font, sBrush, x, y + 2f * scale);
                        g.DrawString(frame.Speed.ToString("0.0") + " m/s", font, pBrush, x + 72f * scale, y + 2f * scale);
                    }
                }
            }
        }

        private void DrawStatusBar(Graphics g, RectangleF panel, Font font, Brush primary, Brush secondary, string label, double ratio, string value, float x, float y, float scale, float barH, bool highIsGood = true, float widthOverride = 0f, double warningThreshold = .50, double criticalThreshold = .25)
        {
            float available = widthOverride > 1f ? widthOverride : panel.Width;
            float labelW = 72f * scale;
            float valueW = Math.Min(128f * scale, Math.Max(90f * scale, available * .38f));
            float barX = x + labelW;
            float barW = Math.Max(55f * scale, available - labelW - valueW - 10f * scale);
            float barY = y + Math.Max(9f, font.Height * .48f);

            g.DrawString(label, font, secondary, x, y);
            Color panelColor = _settings.ColorOf(_settings.HudPanelColor, Color.Black);
            Color border = _settings.ColorOf(_settings.HudBorderColor, Color.Gray);
            using (var bg = new SolidBrush(Color.FromArgb(185, panelColor)))
            using (var bp = new Pen(Color.FromArgb(170, border), Math.Max(1f, (float)_settings.BorderWidth * .7f)))
            {
                g.FillRectangle(bg, barX, barY, barW, barH);
                g.DrawRectangle(bp, barX, barY, barW, barH);
            }

            if (ratio >= 0)
            {
                double clamped = Math.Max(0, Math.Min(1, ratio));
                Color fill = StatusColor(clamped, highIsGood, warningThreshold, criticalThreshold);
                using (var fb = new SolidBrush(Color.FromArgb(225, fill)))
                    g.FillRectangle(fb, barX + 1f, barY + 1f, Math.Max(0, (barW - 2f) * (float)clamped), Math.Max(1f, barH - 2f));
            }

            SizeF vm = g.MeasureString(value ?? "", font);
            float valueX = x + available - vm.Width;
            g.DrawString(value ?? "", font, primary, valueX, y);
        }

        private Color StatusColor(double ratio, bool highIsGood, double warningThreshold = .50, double criticalThreshold = .25)
        {
            double v = Math.Max(0, Math.Min(1, ratio));
            if (highIsGood)
            {
                if (v < criticalThreshold) return _settings.ColorOf(_settings.HostileColor, Color.IndianRed);
                if (v < warningThreshold) return _settings.ColorOf(_settings.SpectrumColor, Color.Goldenrod);
                return Color.FromArgb(235, 92, 218, 124);
            }
            if (v > criticalThreshold) return _settings.ColorOf(_settings.HostileColor, Color.IndianRed);
            if (v > warningThreshold) return _settings.ColorOf(_settings.SpectrumColor, Color.Goldenrod);
            return Color.FromArgb(235, 92, 218, 124);
        }

        private static string Percent(double value)
        {
            return value < 0 ? "N/A" : (Math.Max(0, Math.Min(1, value)) * 100.0).ToString("0") + "%";
        }

        private static string FormatPower(double mw)
        {
            if (mw >= 1000) return (mw / 1000.0).ToString("0.00") + " GW";
            return mw.ToString("0.0") + " MW";
        }

        private string AmmoEmptyMessage(OverlayFrame frame)
        {
            if (!frame.HasShip) return "ENTER A SHIP TO TRACK AMMO";
            if (frame.AmmoRows != null && frame.AmmoRows.Count > 0) return "ALL AMMO TYPES ARE HIDDEN";
            return _settings.AmmoOnlyRelevant ? "NO MATCHING SDX AMMO DETECTED" : "WAITING FOR SHIP AMMO DATA";
        }


        // ZEOCORE_V067H4_AMMO_TYPE_FILTER
        private bool AmmoTypeVisible(OverlayAmmoRow a)
        {
            if (a == null) return false;
            string n = ((a.CleanName ?? "") + " " + (a.ServerName ?? "")).ToUpperInvariant();

            if (n.Contains("40MM") && n.Contains("IMPROVISED"))
                return _settings.ShowAmmoPdc40Improvised;
            if (n.Contains("40MM") && n.Contains("PDC"))
                return _settings.ShowAmmoPdc40;
            if (n.Contains("50MM") && n.Contains("PDC"))
                return _settings.ShowAmmoPdc50;

            if (n.Contains("80MM") && n.Contains("IMPROVISED") && n.Contains("SABOT"))
                return _settings.ShowAmmoSabot80Improvised;
            if (n.Contains("80MM") && n.Contains("SABOT"))
                return _settings.ShowAmmoSabot80;
            if (n.Contains("100MM") && n.Contains("SABOT"))
                return _settings.ShowAmmoSabot100;

            if (n.Contains("160MM") && n.Contains("TORP"))
                return _settings.ShowAmmoTorp160;
            if (n.Contains("190MM") && n.Contains("TORP"))
                return _settings.ShowAmmoTorp190;
            if (n.Contains("220MM") && n.Contains("TORP"))
                return _settings.ShowAmmoTorp220;

            // Never hide a future/unknown ammo type just because this version
            // does not have a dedicated checkbox for it yet.
            return true;
        }

        private void DrawAmmoPanel(Graphics g, int width, int height, OverlayFrame frame)
        {
            var visibleAmmo = new List<OverlayAmmoRow>();
            if (frame.HasShip && frame.AmmoRows != null)
                for (int ai = 0; ai < frame.AmmoRows.Count; ai++)
                    if (AmmoTypeVisible(frame.AmmoRows[ai])) visibleAmmo.Add(frame.AmmoRows[ai]);
            // Keep the enabled panel visible when the relevance/type filters yield no rows.
            float scale = (float)Math.Max(.60, Math.Min(2.25, _settings.TextScale * _settings.AmmoPanelScale));
            float panelScale = (float)Math.Max(.60, Math.Min(2.25, _settings.AmmoPanelScale));
            float pad = 12f * (float)_settings.PanelPaddingScale * panelScale;
            int rows = Math.Min(12, visibleAmmo.Count);
            float rowH = Math.Max(21f, 24f * scale);
            float panelW = Math.Max(390f, 430f * scale);
            float panelH = pad * 2f + 28f * scale + Math.Max(2, rows) * rowH;
            PointF origin = NormToPixel(width, height, _settings.AmmoX, _settings.AmmoY);
            RectangleF rect = ClampRect(width, height, origin.X, origin.Y, panelW, panelH);
            RecordLayoutBounds("ammo",rect);
            DrawTacticalPlate(g, rect, _settings.ColorOf(_settings.SpectrumColor, Color.Goldenrod), _settings.BackingAmmo);

            Color primary = _settings.ColorOf(_settings.HudTextColor, Color.Gainsboro);
            Color secondary = _settings.ColorOf(_settings.HudSecondaryColor, Color.Silver);
            using (var titleFont = new Font(HudTitleFont(), Math.Max(12f, 14f * scale), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var font = new Font(HudBodyFont(true), Math.Max(9f, 10.5f * scale), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var pb = new SolidBrush(primary))
            using (var sb = new SolidBrush(secondary))
            {
                float x = rect.X + pad;
                float y = rect.Y + pad;
                SizeF ammoTitle = g.MeasureString("AMMUNITION", titleFont);
                g.DrawString("AMMUNITION", titleFont, pb, rect.Left + (rect.Width - ammoTitle.Width) / 2f, y);
                string hdr = _settings.AmmoValueOrder == 1 ? "WANT / HAVE" : "HAVE / WANT";
                SizeF hm = g.MeasureString(hdr, font);
                if (rows > 0) g.DrawString(hdr, font, sb, rect.Right - pad - hm.Width, y + 2f * scale);
                y += 27f * scale;
                if (rows == 0)
                {
                    g.DrawString(AmmoEmptyMessage(frame), font, pb, x, y);
                    string hint = !frame.HasShip ? "Ammo tracking requires a controlled ship." :
                        (frame.AmmoRows != null && frame.AmmoRows.Count > 0 ? "HOME > AMMO: enable an ammo type." :
                        (_settings.AmmoOnlyRelevant ? "HOME > AMMO: turn relevant-only OFF to show all." : "Waiting for the ship inventory scan."));
                    g.DrawString(hint, font, sb, x, y + rowH);
                }
                for (int i = 0; i < rows; i++)
                {
                    OverlayAmmoRow a = visibleAmmo[i];
                    double ratio = a.Want > 0 ? a.Have / a.Want : 1.0;
                    Color c = StatusColor(ratio, true, .75, .40);
                    string name = _settings.AmmoNameStyle == 1 ? a.ServerName : (_settings.AmmoNameStyle == 2 ? AmmoCompact(a.CleanName) : a.CleanName);
                    using (var cb = new SolidBrush(c)) g.DrawString(ShortName(name, 28), font, cb, x, y);
                    string value = _settings.AmmoValueOrder == 1 ? (a.Want.ToString("0") + " / " + a.Have.ToString("0")) : (a.Have.ToString("0") + " / " + a.Want.ToString("0"));
                    SizeF vm = g.MeasureString(value, font);
                    g.DrawString(value, font, pb, rect.Right - pad - vm.Width, y);
                    float barY = y + font.Height + 1f * scale;
                    float barW = rect.Width - pad * 2f;
                    using (var bg = new SolidBrush(Color.FromArgb(120, _settings.ColorOf(_settings.HudPanelColor, Color.Black)))) g.FillRectangle(bg, x, barY, barW, Math.Max(2f, 3.5f*scale));
                    using (var fb = new SolidBrush(Color.FromArgb(225, c))) g.FillRectangle(fb, x, barY, barW * (float)Math.Max(0, Math.Min(1, ratio)), Math.Max(2f, 3.5f*scale));
                    y += rowH;
                }
            }
        }

        private static string AmmoCompact(string n)
        {
            if (string.IsNullOrWhiteSpace(n)) return "AMMO";
            return n.Replace("Improvised", "IMP").Replace("Torpedo", "TORP").Replace("Sabot", "SAB");
        }

        private void DrawFleetPanel(Graphics g, int width, int height, OverlayFrame frame)
        {
            string trust = string.IsNullOrWhiteSpace(frame.ServerTrustState) ? "OFFLINE" : frame.ServerTrustState;
            string sector = string.IsNullOrWhiteSpace(frame.ServerTrustSector) ? "UNKNOWN" : frame.ServerTrustSector;
            string verify = frame.ServerEndpointVerified ? "ENDPOINT OK" : (frame.ServerIdentityVerified ? "STEAM ID" : "NO ID");
            var lines = new List<string>();

            if (!frame.AuthAuthorized)
            {
                string auth = string.IsNullOrWhiteSpace(frame.AuthState) ? "AUTH WAIT" : frame.AuthState;
                if (!string.IsNullOrWhiteSpace(frame.AuthPairingCode))
                {
                    lines.Add("AUTH LINK REQUIRED   |   CODE " + frame.AuthPairingCode);
                    lines.Add("OPEN WAR ROOM > MEMBERS > ZEOCORE DEVICES");
                }
                else
                {
                    lines.Add("AUTH " + auth);
                    if (!string.IsNullOrWhiteSpace(frame.AuthDetail)) lines.Add(ShortName(frame.AuthDetail, 58));
                }
                lines.Add(trust + "   |   " + sector + "   |   " + verify);
            }
            else
            {
                string rx = !frame.RxOn ? "RX OFF" : (frame.RxLinked ? "RX LINK " + frame.RxAgeSeconds.ToString("0.0") + "s" : "RX WAIT");
                string tx = frame.TxOn ? "TX ON" : "TX OFF";
                string faction = string.IsNullOrWhiteSpace(frame.AuthFactionTag) ? "ACCOUNT" : frame.AuthFactionTag;
                string scope = string.IsNullOrWhiteSpace(frame.AuthEffectiveScope) ? "SELF" : frame.AuthEffectiveScope.ToUpperInvariant();
                lines.Add("OPEN TEST // AUTH OFF   |   " + faction + "   |   " + scope);
                lines.Add(rx + "   |   " + tx + "   |   F " + frame.FleetFriendlyCount + "  C " + frame.FleetContactCount);
                lines.Add("SESSION CONTEXT   |   " + sector + "   |   " + verify);
            }

            DrawPanel(g, width, height, _settings.LinkPanelX, _settings.LinkPanelY,
                _settings.TextScale * _settings.LinkPanelScale, "ZEO NETWORK", lines, false, _settings.BackingFleetLink);
        }

        private void DrawRosterPanel(Graphics g, int width, int height, OverlayFrame frame)
        {
            int count = frame.RosterRows == null ? 0 : Math.Min(_settings.RosterRows, frame.RosterRows.Count);
            float scale = (float)Math.Max(.60, Math.Min(2.25, _settings.TextScale * _settings.RosterPanelScale));
            float pad = 11f * (float)_settings.PanelPaddingScale * scale;
            float rowH = Math.Max(19f, 22f * scale);
            float panelW = Math.Max(380f, 445f * scale);
            float panelH = pad * 2f + Math.Max(18f, 22f * scale) + 8f * scale + Math.Max(1, _settings.RosterRows) * rowH;
            PointF origin = NormToPixel(width, height, _settings.RosterX, _settings.RosterY);
            RectangleF rect = ClampRect(width, height, origin.X, origin.Y, panelW, panelH);
            RecordLayoutBounds("roster",rect);
            Color accent = _settings.ColorOf(_settings.FriendlyColor, Color.Cyan);
            DrawTacticalPlate(g, rect, accent, _settings.BackingRoster);

            Color primary = _settings.ColorOf(_settings.HudTextColor, Color.Gainsboro);
            Color secondary = _settings.ColorOf(_settings.HudSecondaryColor, Color.Silver);
            Color stale = _settings.ColorOf(_settings.StaleColor, Color.SlateGray);
            using (var titleFont = new Font(HudTitleFont(), Math.Max(12f, 14f * scale), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var font = new Font(HudBodyFont(true), Math.Max(9f, 10.5f * scale), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var pb = new SolidBrush(primary))
            using (var sb = new SolidBrush(secondary))
            {
                float x = rect.X + pad;
                float y = rect.Y + pad;
                SizeF rosterTitle = g.MeasureString("FLEET ROSTER", titleFont);
                g.DrawString("FLEET ROSTER", titleFont, pb, rect.Left + (rect.Width - rosterTitle.Width) / 2f, y);
                string sector = ShortName(frame.SectorName ?? "UNKNOWN SECTOR", 24);
                SizeF sm = g.MeasureString(sector, font);
                g.DrawString(sector, font, sb, rect.Right - pad - sm.Width, y + 2f * scale);
                y += titleFont.Height + 7f * scale;

                if (count == 0)
                {
                    string waiting = frame.RxLinked ? "NO SERVER ROSTER ROWS" : "FLEETLINK WAITING";
                    g.DrawString(waiting, font, sb, x, y);
                    return;
                }

                int rendered = 0;
                for (int i = 0; i < count && rendered < _settings.RosterRows; i++)
                {
                    OverlayRosterRow r = frame.RosterRows[i];
                    if (!_settings.ShowCrossSectorRoster && !r.SameSector) continue;
                    Color distress = _settings.ColorOf(_settings.DistressColor, Color.Red);
                    Color c = r.Distress ? distress : (!r.Online || r.AgeSeconds > 20 ? stale : (r.SameSector ? accent : secondary));
                    string icon = r.Distress ? "!" : (r.SameSector ? "◇" : (r.SectorKnown ? ">" : "?"));
                    string where = r.SameSector && r.Distance >= 0 ? FormatRange(r.Distance) : (r.SectorKnown ? ShortName(r.SectorName, 18) : "SECTOR ?");
                    string hp = r.ShipHp >= 0 ? (Math.Max(0, Math.Min(1, r.ShipHp)) * 100.0).ToString("0") + "%" : "--";
                    if (r.Distress && r.DistressSecondsRemaining > 0) hp = Math.Ceiling(r.DistressSecondsRemaining/60.0).ToString("0") + "m";
                    using (var cb = new SolidBrush(Color.FromArgb(!r.Online ? 145 : 240, c)))
                    {
                        g.DrawString(icon + " " + ShortName(r.Name, 22), font, cb, x, y);
                        SizeF wm = g.MeasureString(where, font);
                        g.DrawString(where, font, cb, rect.Right - pad - 62f * scale - wm.Width, y);
                        SizeF hm = g.MeasureString(hp, font);
                        g.DrawString(hp, font, cb, rect.Right - pad - hm.Width, y);
                    }
                    y += rowH;
                    rendered++;
                }
            }
        }

        private void DrawScopePanel(Graphics g, int width, int height, OverlayFrame frame)
        {
            // ZEOCORE_V067_LEGACY_SCOPE_SIZING
            float overall = (float)Math.Max(.60, Math.Min(2.25, _settings.ScopePanelScale));
            float text = (float)Math.Max(.60, Math.Min(2.50, _settings.TextScale * _settings.ScopeTextScale));
            float widthScale = (float)Math.Max(.75, Math.Min(2.50, _settings.ScopeWidthScale));
            float pad = 12f * (float)_settings.PanelPaddingScale * overall;
            int capacity = Math.Max(3, Math.Min(16, _settings.ScopeRows));
            float lineH = Math.Max(18f * overall, 22f * text);
            float panelW = Math.Max(300f * overall, 520f * overall * widthScale);

            using (var titleFont = new Font(HudTitleFont(), Math.Max(11f, 14f * text), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var headFont = new Font(HudBodyFont(true), Math.Max(8f, 9.5f * text), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var bodyFont = new Font(HudBodyFont(true), Math.Max(9f, 11.0f * text), FontStyle.Bold, GraphicsUnit.Pixel))
            {
                float headerH = titleFont.Height + 6f * overall + headFont.Height + 7f * overall;
                float panelH = pad * 2f + headerH + capacity * lineH + 5f * overall;
                PointF origin = NormToPixel(width, height, _settings.TrackPanelX, _settings.TrackPanelY);
                RectangleF rect = ClampRect(width, height, origin.X, origin.Y, panelW, panelH);
                RecordLayoutBounds("scope",rect);

                DrawTacticalPlate(g, rect, _settings.ColorOf(_settings.HostileColor, Color.OrangeRed), _settings.BackingTos); // ZEOCORE_V13E_TOS_BACKING_ARG_FIX

                Color titleColor = _settings.ColorOf(_settings.HudTextColor, Color.Gainsboro);
                Color headColor = _settings.ColorOf(_settings.HudSecondaryColor, Color.Silver);
                Color localTrackColor = _settings.ColorOf(_settings.HostileColor, Color.OrangeRed);
                using (var titleBrush = new SolidBrush(Color.FromArgb(235, titleColor)))
                using (var headBrush = new SolidBrush(Color.FromArgb(205, headColor)))
                {
                    float ty = rect.Y + pad;
                    string title = "TRACKS ON SCOPE";
                    SizeF titleMeasure = g.MeasureString(title, titleFont);
                    g.DrawString(title, titleFont, titleBrush, rect.Left + (rect.Width-titleMeasure.Width)/2f, ty);

                    string countText = Math.Max(0, frame.TotalScopeCount).ToString("00");
                    SizeF countMeasure = g.MeasureString(countText, headFont);
                    g.DrawString(countText, headFont, headBrush, rect.Right-pad-countMeasure.Width, ty + 2f*overall);

                    float usable = Math.Max(180f, rect.Width - pad * 2f);
                    float idX = rect.Left + pad;
                    float distX = idX + usable * .14f;
                    float statusX = idX + usable * .43f;
                    float coreX = idX + usable * .74f;
                    float yy = ty + titleFont.Height + 6f * overall;
                    g.DrawString("#", headFont, headBrush, idX, yy);
                    g.DrawString("DISTANCE", headFont, headBrush, distX, yy);
                    g.DrawString("TARGET SPD", headFont, headBrush, statusX, yy);
                    g.DrawString("CORE", headFont, headBrush, coreX, yy);
                    yy += headFont.Height + 7f * overall;

                    if (frame.ScopeRows == null) return;
                    int rows = Math.Min(capacity, frame.ScopeRows.Count);
                    for (int i = 0; i < rows; i++)
                    {
                        OverlayScopeRow r = frame.ScopeRows[i];
                        Color rowColor;
                        string relation = (r.Relation ?? "").Trim();
                        if (r.Distress)
                            rowColor = _settings.ColorOf(_settings.DistressColor, Color.Red);
                        else if (r.Friendly || r.Source == 2 || relation.Equals("friendly", StringComparison.OrdinalIgnoreCase))
                            rowColor = _settings.ColorOf(_settings.FriendlyColor, Color.Cyan);
                        else if (relation.Equals("hostile", StringComparison.OrdinalIgnoreCase) || relation.Equals("enemy", StringComparison.OrdinalIgnoreCase))
                            rowColor = _settings.ThemePreset == 4
                                ? _settings.ColorOf(_settings.HostileColor, Color.Red)
                                : Color.FromArgb(240, 68, 68);
                        else if (relation.Equals("neutral", StringComparison.OrdinalIgnoreCase))
                            rowColor = _settings.ThemePreset == 4
                                ? _settings.ColorOf(_settings.NeutralColor, Color.White)
                                : Color.White;
                        else
                            rowColor = _settings.ThemePreset == 4
                                ? _settings.ColorOf(_settings.SpectrumColor, Color.Orange)
                                : Color.FromArgb(255, 184, 74);

                        int alpha = r.Stale ? 145 : (r.Focused ? 255 : 235);
                        using (var rowBrush = new SolidBrush(Color.FromArgb(alpha, rowColor)))
                        {
                            string id = r.TrackId > 0 ? r.TrackId.ToString("00") : "--";
                            string status = "S" + Math.Max(0, r.Speed).ToString("0");
                            if (Math.Abs(r.Closing) < 5.0) status += " HOLD";
                            else if (r.Closing > 0) status += " C" + Math.Abs(r.Closing).ToString("0");
                            else status += " R" + Math.Abs(r.Closing).ToString("0");
                            string core = r.Distress ? ("SOS " + (r.Name ?? "DISTRESS")) : (r.Name ?? "");
                            if (string.IsNullOrWhiteSpace(core)) core = "UNKNOWN";
                            core = ShortName(core, 18);
                            g.DrawString(id, bodyFont, rowBrush, idX, yy);
                            g.DrawString(FormatRange(r.Distance), bodyFont, rowBrush, distX, yy);
                            g.DrawString(status, bodyFont, rowBrush, statusX, yy);
                            g.DrawString(core, bodyFont, rowBrush, coreX, yy);
                        }
                        yy += lineH;
                    }
                }
            }
        }

        private void DrawPanel(Graphics g, int width, int height, double nx, double ny, double scale, string title, List<string> lines, bool mono, bool backingFill)
        {
            float titleSize = (float)Math.Max(9, 12.5 * scale);
            float bodySize = (float)Math.Max(9, 11.5 * scale);
            float pad = (float)(9 * _settings.PanelPaddingScale * scale);
            float gap = Math.Max(3f, 4f * (float)scale);

            using (var titleFont = new Font(HudTitleFont(), titleSize, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var bodyFont = new Font(mono ? HudBodyFont(true) : HudBodyFont(false), bodySize, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                float maxW = string.IsNullOrEmpty(title) ? 0 : g.MeasureString(title, titleFont).Width;
                float lineH = Math.Max(bodyFont.Height + 1, bodySize + 3);
                for (int i = 0; i < lines.Count; i++) maxW = Math.Max(maxW, g.MeasureString(lines[i] ?? "", bodyFont).Width);
                float titleH = string.IsNullOrEmpty(title) ? 0 : titleFont.Height + gap;
                float panelW = maxW + pad * 2 + 8f;
                float panelH = pad + titleH + lines.Count * lineH + pad;

                PointF origin = NormToPixel(width, height, nx, ny);
                RectangleF rect = ClampRect(width, height, origin.X, origin.Y, panelW, panelH);
                if(title=="ZEO NETWORK") RecordLayoutBounds("fleet",rect);
                Color accent = _settings.ColorOf(_settings.MenuAccentColor, Color.Goldenrod);
                DrawTacticalPlate(g, rect, accent, backingFill);

                Color primary = _settings.ColorOf(_settings.HudTextColor, Color.Gainsboro);
                Color secondary = _settings.ColorOf(_settings.HudSecondaryColor, Color.Silver);
                using (var b1 = new SolidBrush(primary))
                using (var b2 = new SolidBrush(secondary))
                {
                    float x = rect.X + pad + 4f;
                    float yy = rect.Y + pad;
                    if (!string.IsNullOrEmpty(title))
                    {
                        SizeF tm = g.MeasureString(title, titleFont);
                        g.DrawString(title, titleFont, b1, rect.Left + (rect.Width - tm.Width) / 2f, yy); // ZEOCORE_V067_CENTERED_HEADER
                        yy += titleFont.Height + gap;
                    }
                    for (int i = 0; i < lines.Count; i++)
                    {
                        g.DrawString(lines[i] ?? "", bodyFont, i == 0 && mono ? b2 : b1, x, yy);
                        yy += lineH;
                    }
                }
            }
        }

        private RectangleF ClampRect(int width, int height, float x, float y, float panelW, float panelH)
        {
            if (x + panelW > width - 10) x = width - panelW - 10;
            if (y + panelH > height - 10) y = height - panelH - 10;
            if (x < 10) x = 10;
            if (y < 10) y = 10;
            return new RectangleF(x, y, panelW, panelH);
        }

        private void DrawTacticalPlate(Graphics g, RectangleF rect, Color accent, bool backingFill)
        {
            // ZEOCORE_V13B_PER_PANEL_BACKINGS - backing fill is independent per HUD.
            // Rails remain visible even when a panel's fill is disabled.
            Color panel = _settings.ColorOf(_settings.HudPanelColor, Color.FromArgb(18, 22, 26));
            Color border = _settings.ColorOf(_settings.HudBorderColor, Color.DimGray);
            // War Room-era frames use the menu/command accent consistently so
            // panel content colors do not turn the frame rails cyan/amber/etc.
            if (_settings.FrameStyle >= 6 && _settings.FrameStyle != 16)
                accent = _settings.ColorOf(_settings.MenuAccentColor, Color.Firebrick);
            float bw = (float)_settings.BorderWidth;
            int opacity = backingFill ? Math.Max(60, Math.Min(245, _settings.PanelOpacity)) : 0;
            float x = rect.Left, y = rect.Top, r = rect.Right, b = rect.Bottom;

            if (_settings.FrameStyle == 14) // LEGACY GLASS // ZEOCORE_V067_LEGACY_GLASS
            {
                float cut = Math.Max(7f, Math.Min(20f, Math.Min(rect.Width, rect.Height) * .065f));
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[] { new PointF(x+cut,y), new PointF(r-cut,y), new PointF(r,y+cut), new PointF(r,b-cut), new PointF(r-cut,b), new PointF(x+cut,b), new PointF(x,b-cut), new PointF(x,y+cut) });
                    using (var fill = new SolidBrush(Color.FromArgb(backingFill ? Math.Max(42, Math.Min(128, Math.Max(60, _settings.PanelOpacity)/2)) : 0, panel))) g.FillPath(fill, path);
                    using (var rail = new Pen(Color.FromArgb(150, border), Math.Max(.8f, bw))) g.DrawPath(rail, path);
                    using (var ap = new Pen(Color.FromArgb(185, accent), Math.Max(1f, bw)))
                    {
                        float railLen = Math.Min(52f, rect.Width * .14f);
                        g.DrawLine(ap, x+cut+5f, y, x+cut+5f+railLen, y);
                        g.DrawLine(ap, r-cut-5f-railLen, b, r-cut-5f, b);
                    }
                }
                return;
            }

            if (_settings.FrameStyle == 15) // KEEN SIGNAL // ZEOCORE_V13B_KEEN_SIGNAL_FRAME
            {
                float cut = Math.Max(11f, Math.Min(28f, rect.Height * .18f));
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[] {
                        new PointF(x+cut,y), new PointF(r-cut*.65f,y), new PointF(r,y+cut*.65f),
                        new PointF(r,b-cut*.65f), new PointF(r-cut*.65f,b), new PointF(x+cut,b),
                        new PointF(x,b-cut), new PointF(x,y+cut) });
                    using (var fill = new SolidBrush(Color.FromArgb(backingFill ? Math.Min(176, Math.Max(72, opacity)) : 0, panel))) g.FillPath(fill,path);
                    using (var edge = new Pen(Color.FromArgb(118,border), Math.Max(.8f,bw*.85f))) g.DrawPath(edge,path);
                }
                using (var rail = new Pen(Color.FromArgb(172, accent), Math.Max(1f,bw)))
                using (var ghost = new Pen(Color.FromArgb(72, border), Math.Max(.8f,bw*.75f)))
                {
                    float topLen=Math.Min(76f,rect.Width*.22f);
                    g.DrawLine(rail,x+cut+8f,y+1f,x+cut+8f+topLen,y+1f);
                    g.DrawLine(ghost,r-cut-52f,b-1f,r-cut-8f,b-1f);
                    float cy=(y+b)*.5f;
                    for(int i=0;i<4;i++)
                    {
                        float xx=r-18f-i*10f;
                        g.DrawLine(ghost,xx,cy-7f-(i*1.5f),xx,cy+7f+(i*1.5f));
                    }
                }
                return;
            }

            if (_settings.FrameStyle == 16) // WEAPON CORE // ZEOCORE_V14A_WEAPON_CORE_FRAME
            {
                // Native WeaponCore language: nearly black tactical plate, very small
                // corner cuts, restrained edge treatment and the thin pale lower data rail.
                // No War Room accent rails are used in this style.
                float cut = Math.Max(4f, Math.Min(9f, rect.Height * .09f));
                Color wcPanel = Color.FromArgb(18, 23, 26);
                Color wcEdge = Color.FromArgb(142, 151, 154);
                Color wcRail = Color.FromArgb(202, 210, 211);

                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[] {
                        new PointF(x+cut,y), new PointF(r-cut,y), new PointF(r,y+cut),
                        new PointF(r,b-cut), new PointF(r-cut,b), new PointF(x+cut,b),
                        new PointF(x,b-cut), new PointF(x,y+cut) });

                    using (var fill = new SolidBrush(Color.FromArgb(
                        backingFill ? Math.Min(218, Math.Max(138, opacity)) : 0, wcPanel)))
                        g.FillPath(fill, path);

                    // WeaponCore's outline is intentionally subtle; the lower rail does
                    // most of the visual framing.
                    using (var edge = new Pen(Color.FromArgb(88, wcEdge), Math.Max(.7f, bw * .70f)))
                        g.DrawPath(edge, path);
                }

                using (var rail = new Pen(Color.FromArgb(205, wcRail), Math.Max(1f, bw * .90f)))
                using (var dim = new Pen(Color.FromArgb(90, wcEdge), Math.Max(.7f, bw * .65f)))
                {
                    float yy = b - 4f;
                    float left = x + cut + 7f;
                    float right = r - cut - 8f;
                    g.DrawLine(rail, left, yy, right, yy);

                    // Tiny break/tick details echo WC's data-card separators without
                    // introducing decorative Zeo/War-Room rails.
                    g.DrawLine(dim, left + 22f, yy - 2f, left + 30f, yy - 2f);
                    g.DrawLine(dim, right - 42f, yy + 2f, right - 28f, yy + 2f);
                    g.DrawLine(rail, left, yy - 2f, left, yy + 2f);
                }
                return;
            }

            if (_settings.FrameStyle == 0) // SE INDUSTRIAL - preserved known-good plate
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

            if (_settings.FrameStyle == 1) // FIGHTER HUD - open frame, corner gates + sight rails
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

            if (_settings.FrameStyle == 2) // MARS TACTICAL - asymmetric armored wedge + hard data spine
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

            if (_settings.FrameStyle == 3) // BELTER UTILITY - broken rails / scaffold, intentionally asymmetric
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

            if (_settings.FrameStyle == 4) // NAVY GLASS - double floating glass panes, no heavy armor border
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

            if (_settings.FrameStyle == 6) // WAR ROOM - website-matched command center plate
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

            if (_settings.FrameStyle == 7) // COMMAND GRID - dark tactical grid + locked red corners
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

            if (_settings.FrameStyle == 8) // REDLINE - open premium rails, minimum panel mass
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

            if (_settings.FrameStyle == 9) // BLACKSITE - matte classified terminal / asymmetric data spine
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

            if (_settings.FrameStyle == 10) // CHEVRON - clipped command capsule with inward side cues
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

            if (_settings.FrameStyle == 11) // SPLIT WING - detached left/right command wings with open center rails
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

            if (_settings.FrameStyle == 12) // HEX COMMAND - wide six-sided command cell
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

            if (_settings.FrameStyle == 13) // RAZOR - asymmetric blade cuts and offset rails
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

        private string HudTitleFont()
        {
            if (_settings.FontStyle == 1) return "Bahnschrift SemiCondensed";
            if (_settings.FontStyle == 2) return "Consolas";
            if (_settings.FontStyle == 3) return "Segoe UI Semibold";
            if (_settings.FrameStyle == 15) return "Bahnschrift SemiCondensed";
            if (_settings.FrameStyle == 16) return "Consolas";
            if (_settings.FrameStyle == 1 || _settings.FrameStyle == 2 || _settings.FrameStyle == 4 || _settings.FrameStyle == 5 || (_settings.FrameStyle >= 6 && _settings.FrameStyle != 16)) return "Bahnschrift SemiCondensed";
            if (_settings.FrameStyle == 3) return "Consolas";
            return "Segoe UI Semibold";
        }

        private string HudBodyFont(bool technical)
        {
            if (_settings.FontStyle == 1) return "Bahnschrift SemiCondensed";
            if (_settings.FontStyle == 2) return "Consolas";
            if (_settings.FontStyle == 3) return technical ? "Consolas" : "Segoe UI";
            if (_settings.FrameStyle == 1) return technical ? "Bahnschrift SemiCondensed" : "Bahnschrift";
            if (_settings.FrameStyle == 2 || _settings.FrameStyle == 3) return "Consolas";
            if (_settings.FrameStyle == 4) return technical ? "Bahnschrift" : "Segoe UI";
            if (_settings.FrameStyle == 5 || _settings.FrameStyle == 7 || _settings.FrameStyle == 9) return technical ? "Consolas" : "Bahnschrift SemiCondensed";
            if (_settings.FrameStyle == 6 || _settings.FrameStyle == 8 || _settings.FrameStyle == 10 || _settings.FrameStyle == 12) return technical ? "Bahnschrift SemiCondensed" : "Segoe UI Semibold";
            if (_settings.FrameStyle == 15) return technical ? "Bahnschrift SemiCondensed" : "Segoe UI";
            if (_settings.FrameStyle == 16) return "Consolas";
            if (_settings.FrameStyle == 11 || _settings.FrameStyle == 13) return technical ? "Consolas" : "Bahnschrift SemiCondensed";
            return technical ? "Consolas" : "Segoe UI Semibold";
        }


        private Color ScopeRowColor(OverlayScopeRow r)
        {
            // ZEOCORE_V067H4_SEMANTIC_TRACK_COLORS
            if (r.Distress) return _settings.ColorOf(_settings.DistressColor, Color.Red);
            if (r.Focused) return _settings.ColorOf(_settings.FocusColor, Color.Gold);
            if (r.Stale) return _settings.ColorOf(_settings.StaleColor, Color.SlateGray);
            if (r.Friendly || string.Equals(r.Relation, "friendly", StringComparison.OrdinalIgnoreCase))
                return _settings.ColorOf(_settings.FriendlyColor, Color.Cyan);

            string relation = (r.Relation ?? "").Trim();
            if (relation.Equals("hostile", StringComparison.OrdinalIgnoreCase) ||
                relation.Equals("enemy", StringComparison.OrdinalIgnoreCase))
                return _settings.ThemePreset == 4
                    ? _settings.ColorOf(_settings.HostileColor, Color.Red)
                    : Color.FromArgb(240, 68, 68);

            if (relation.Equals("neutral", StringComparison.OrdinalIgnoreCase))
                return _settings.ThemePreset == 4
                    ? _settings.ColorOf(_settings.NeutralColor, Color.White)
                    : Color.White;

            return _settings.ThemePreset == 4
                ? _settings.ColorOf(_settings.SpectrumColor, Color.Orange)
                : Color.FromArgb(255, 184, 74);
        }

        private void DrawMarker(Graphics g, int width, int height, OverlayMarker m)
        {
            PointF raw = NormToPixel(width, height, m.X, m.Y);
            PointF p = raw; // v0.6.7 legacy lock: no second screen-space smoothing pass
            Color color = MarkerColor(m);
            double scale = MarkerScale(m);
            if (m.Offscreen) scale *= _settings.OffscreenMarkerScale;
            if (m.Focused) scale *= _settings.FocusMarkerScale;
            scale = Math.Min(scale, _settings.MaxMarkerScale); // ZEOCORE_V067_FINAL_MARKER_CAP
            float sz = (float)scale;

            if (m.Offscreen)
            {
                DrawOffscreen(g, width, height, raw, m, color, sz);
                return;
            }

            DrawMarkerByIconPack(g, p, m, color, sz);

            if (_settings.ShowMarkerAnchorDot)
            {
                using (var b = new SolidBrush(Color.FromArgb(245, color)))
                    g.FillEllipse(b, raw.X - 2.5f, raw.Y - 2.5f, 5f, 5f);
            }

            // Local combat tracks remain the old minimal persistent-ID + > < cue.
            // Detail text is reserved for friendly/shared/distress information.
            bool semanticDataMarker =
                m.Distress || m.Friendly || m.Source == 2 || m.Source == 3 || m.Source == 4;
            if (semanticDataMarker &&
                (_settings.MarkerStyle == 2 || _settings.ShowNames || _settings.ShowDistance))
                DrawMarkerDetail(g, p, m, color, sz);
        }

        private PointF SmoothMarkerPoint(OverlayMarker m, PointF raw)
        {
            if (_settings.MarkerSmoothing <= 0 || m.Offscreen) return raw;
            DateTime now = DateTime.UtcNow;
            string key = m.Source.ToString() + ":" + m.TrackId.ToString() + ":" + (m.Friendly ? "F" : "C") + ":" + (m.Name ?? "");
            MarkerSmoothState st;
            if (!_markerSmooth.TryGetValue(key, out st) || st == null || st.Offscreen != m.Offscreen)
            {
                st = new MarkerSmoothState { Point = raw, LastUtc = now, SeenUtc = now, Offscreen = m.Offscreen };
                _markerSmooth[key] = st;
                return raw;
            }

            double dt = Math.Max(.001, Math.Min(.10, (now - st.LastUtc).TotalSeconds));
            float dx = raw.X - st.Point.X, dy = raw.Y - st.Point.Y;
            double dist = Math.Sqrt(dx*dx + dy*dy);
            // Large camera slews/track jumps snap immediately. Small sensor jitter is filtered.
            if (dist > 240.0)
            {
                st.Point = raw;
            }
            else if (dist >= .35)
            {
                double tau = _settings.MarkerSmoothing == 1 ? .055 : (_settings.MarkerSmoothing == 2 ? .095 : .155);
                double alpha = 1.0 - Math.Exp(-dt / tau);
                // Dynamic catch-up prevents high smoothing from trailing fast targets.
                alpha = Math.Max(alpha, Math.Min(.82, dist / 360.0));
                st.Point = new PointF((float)(st.Point.X + dx * alpha), (float)(st.Point.Y + dy * alpha));
            }
            st.LastUtc = now; st.SeenUtc = now; st.Offscreen = m.Offscreen;

            if ((now - _lastSmoothPruneUtc).TotalSeconds > 2.0)
            {
                _lastSmoothPruneUtc = now;
                var dead = new List<string>();
                foreach (var kv in _markerSmooth) if ((now - kv.Value.SeenUtc).TotalSeconds > 4.0) dead.Add(kv.Key);
                for (int i=0;i<dead.Count;i++) _markerSmooth.Remove(dead[i]);
            }
            return st.Point;
        }

        private void DrawDistressMarker(Graphics g, PointF p, OverlayMarker m, Color color, float scale)
        {
            float r=Math.Max(9f,12f*scale);
            float stroke=Math.Max(1.7f,2.1f*scale);
            using(var pen=new Pen(Color.FromArgb(245,color),stroke))
            using(var shadow=new Pen(Color.FromArgb(180,0,0,0),stroke+2.2f))
            using(var font=new Font("Bahnschrift SemiCondensed",Math.Max(10f,13f*scale),FontStyle.Bold,GraphicsUnit.Pixel))
            using(var brush=new SolidBrush(Color.FromArgb(245,color)))
            {
                PointF[] hex={
                    new PointF(p.X-r*.55f,p.Y-r),new PointF(p.X+r*.55f,p.Y-r),new PointF(p.X+r,p.Y),
                    new PointF(p.X+r*.55f,p.Y+r),new PointF(p.X-r*.55f,p.Y+r),new PointF(p.X-r,p.Y)
                };
                PointF[] sh=new PointF[hex.Length]; for(int i=0;i<hex.Length;i++) sh[i]=new PointF(hex[i].X+1f,hex[i].Y+1f);
                g.DrawPolygon(shadow,sh); g.DrawPolygon(pen,hex);
                g.DrawLine(shadow,p.X-r*.42f+1f,p.Y+1f,p.X+r*.42f+1f,p.Y+1f); g.DrawLine(shadow,p.X+1f,p.Y-r*.42f+1f,p.X+1f,p.Y+r*.42f+1f);
                g.DrawLine(pen,p.X-r*.42f,p.Y,p.X+r*.42f,p.Y); g.DrawLine(pen,p.X,p.Y-r*.42f,p.X,p.Y+r*.42f);
                string label="SOS";
                SizeF lm=g.MeasureString(label,font); g.DrawString(label,font,brush,p.X-lm.Width/2f,p.Y+r+3f*scale);
            }
        }

        private void DrawDistressBanner(Graphics g, int width, int height, OverlayFrame frame)
        {
            OverlayDistressAlert d = frame.DistressAlerts != null && frame.DistressAlerts.Count > 0 ? frame.DistressAlerts[0] : null;
            if(d==null && !frame.DistressLocalActive) return;
            Color c=_settings.ColorOf(_settings.DistressColor,Color.Red);
            float scale=(float)Math.Max(.75,Math.Min(1.7,_settings.TextScale));
            float w=Math.Max(420f,520f*scale), h=Math.Max(62f,74f*scale);
            RectangleF rect=DistressRectangle(width,height,w,h,scale);
            RecordLayoutBounds("distress",rect);
            using(var fill=new SolidBrush(Color.FromArgb(_settings.BackingDistress ? Math.Min(225,_settings.PanelOpacity) : 0,_settings.ColorOf(_settings.HudPanelColor,Color.Black)))) g.FillRectangle(fill,rect); // ZEOCORE_V13B_PER_PANEL_BACKINGS
            using(var pen=new Pen(Color.FromArgb(245,c),Math.Max(1.5f,2f*scale))) g.DrawRectangle(pen,rect.X,rect.Y,rect.Width,rect.Height);
            using(var title=new Font(HudTitleFont(),Math.Max(12f,15f*scale),FontStyle.Bold,GraphicsUnit.Pixel))
            using(var body=new Font(HudBodyFont(true),Math.Max(9f,11f*scale),FontStyle.Bold,GraphicsUnit.Pixel))
            using(var cb=new SolidBrush(c))
            using(var wb=new SolidBrush(_settings.ColorOf(_settings.HudTextColor,Color.White)))
            {
                if(d==null)
                {
                    string left="! YOUR DISTRESS // ACTIVE";
                    g.DrawString(left,title,cb,rect.X+12f*scale,rect.Y+8f*scale);
                    g.DrawString((frame.DistressStatus ?? "WAITING FOR SERVER")+"   //   HOLD KEY AGAIN TO CLEAR",body,wb,rect.X+12f*scale,rect.Y+36f*scale);
                }
                else
                {
                    string left="! DISTRESS // "+ShortName(d.Name,24)+" // "+ShortName(d.Type,18);
                    g.DrawString(left,title,cb,rect.X+12f*scale,rect.Y+8f*scale);
                    string where=d.SameSector && d.Distance>=0 ? FormatRange(d.Distance) : ShortName(d.SectorName,24);
                    string ttl=d.SecondsRemaining>0 ? Math.Ceiling(d.SecondsRemaining/60.0).ToString("0")+"m TTL" : "ACTIVE";
                    string hp=d.ShipHp>=0 ? "HP "+(Math.Max(0,Math.Min(1,d.ShipHp))*100).ToString("0")+"%" : "HP --";
                    g.DrawString(where+"   //   "+hp+"   //   "+ttl,body,wb,rect.X+12f*scale,rect.Y+36f*scale);
                }
            }
        }

        private void DrawMarkerByIconPack(Graphics g, PointF p, OverlayMarker m, Color color, float scale)
        {
            // ZEOCORE_V066_MARKER_DISPATCH
            // One local combat language: classic Zeo Flight HUD reticle + persistent ID.
            // Only friendly, shared-data and distress contacts retain dedicated shapes.
            if (m.Distress)
            {
                DrawDistressMarker(g, p, m, color, scale);
                return;
            }

            // FleetFriendly/source 2: keep the friendly diamond.
            if (m.Friendly || m.Source == 2)
            {
                DrawGeometricMarker(g, p, m, color, scale, 0);
                if (m.Focused) DrawFocusBrackets(g, p, color, scale);
                return;
            }

            // FleetContact/source 3: shared tactical data remains visually distinct.
            // Confirmed shared hostile = triangle; shared unknown/neutral = square.
            if (m.Source == 3)
            {
                DrawGeometricMarker(g, p, m, color, scale, IsHostile(m) ? 1 : 2);
                if (m.Focused) DrawFocusBrackets(g, p, color, scale);
                return;
            }

            // FleetSignal/source 4: shared Spectrum-style signal keeps the four-way cue.
            if (m.Source == 4)
            {
                DrawFourWayReticle(g, p, m, color, scale);
                return;
            }

            // Local Spectrum (0), local WeaponCore (1), hostile/unknown/focus all use
            // the old classic reticle. Focus changes color only; it does not change shape.
            DrawClassicReticle(g, p, m, color, scale);
        }

        private static bool IsHostile(OverlayMarker m)
        {
            return string.Equals(m.Relation, "hostile", StringComparison.OrdinalIgnoreCase) || string.Equals(m.Relation, "enemy", StringComparison.OrdinalIgnoreCase);
        }

        // shape: 0 diamond, 1 triangle, 2 square, 3 circle, 4 chevron
        private void DrawGeometricMarker(Graphics g, PointF p, OverlayMarker m, Color color, float scale, int shape)
        {
            float r = Math.Max(6f, 9.5f * scale);
            float idScale = (float)(m.Friendly ? _settings.FriendlyIdScale : _settings.SpectrumIdScale);
            float fs = Math.Max(9f, 12f * scale * idScale);
            int alpha = m.Stale ? 155 : 238;
            using(var pen=new Pen(Color.FromArgb(alpha,color),Math.Max(1.2f,1.55f*scale)))
            using(var font=new Font(HudBodyFont(true),fs,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var brush=new SolidBrush(Color.FromArgb(Math.Min(245,alpha+10),color)))
            {
                if (shape == 0)
                {
                    PointF[] q={new PointF(p.X,p.Y-r),new PointF(p.X+r,p.Y),new PointF(p.X,p.Y+r),new PointF(p.X-r,p.Y),new PointF(p.X,p.Y-r)}; g.DrawLines(pen,q);
                }
                else if (shape == 1)
                {
                    PointF[] q={new PointF(p.X,p.Y-r),new PointF(p.X+r*.92f,p.Y+r*.78f),new PointF(p.X-r*.92f,p.Y+r*.78f)}; g.DrawPolygon(pen,q);
                }
                else if (shape == 2) g.DrawRectangle(pen,p.X-r,p.Y-r,r*2f,r*2f);
                else if (shape == 3) g.DrawEllipse(pen,p.X-r,p.Y-r,r*2f,r*2f);
                else
                {
                    g.DrawLine(pen,p.X-r,p.Y-r*.65f,p.X,p.Y); g.DrawLine(pen,p.X-r,p.Y+r*.65f,p.X,p.Y);
                    g.DrawLine(pen,p.X+r,p.Y-r*.65f,p.X,p.Y); g.DrawLine(pen,p.X+r,p.Y+r*.65f,p.X,p.Y);
                }
                string id=m.TrackId>0?m.TrackId.ToString("00"):"--";
                g.DrawString(id,font,brush,p.X+r+4f*scale,p.Y-font.Height/2f);
            }
        }

        private static void DrawFocusBrackets(Graphics g, PointF p, Color color, float scale)
        {
            float a=15f*scale, c=7f*scale;
            using(var pen=new Pen(Color.FromArgb(240,color),Math.Max(1.4f,1.7f*scale)))
            {
                g.DrawLine(pen,p.X-a,p.Y-a,p.X-a+c,p.Y-a); g.DrawLine(pen,p.X-a,p.Y-a,p.X-a,p.Y-a+c);
                g.DrawLine(pen,p.X+a-c,p.Y-a,p.X+a,p.Y-a); g.DrawLine(pen,p.X+a,p.Y-a,p.X+a,p.Y-a+c);
                g.DrawLine(pen,p.X-a,p.Y+a-c,p.X-a,p.Y+a); g.DrawLine(pen,p.X-a,p.Y+a,p.X-a+c,p.Y+a);
                g.DrawLine(pen,p.X+a-c,p.Y+a,p.X+a,p.Y+a); g.DrawLine(pen,p.X+a,p.Y+a-c,p.X+a,p.Y+a);
            }
        }

        private void DrawFourWayReticle(Graphics g, PointF p, OverlayMarker m, Color color, float scale)
        {
            // v0.5.1: proportions modeled on the original SpectrumSignal cue used by Zeos Flight HUD.
            float gap = 15.5f * scale;
            float wing = 8.5f * scale;
            float depth = 5.8f * scale;
            float idScale = (float)((m.Source == 0 || m.Source == 4) ? _settings.SpectrumIdScale : 1.0);
            float idSize = Math.Max(11f, 14f * scale * idScale);
            float stroke = Math.Max(1.35f, 1.55f * scale);
            int alpha = m.Stale ? 155 : 245;
            using (var shadowPen = new Pen(Color.FromArgb(Math.Min(190, alpha), 0, 0, 0), stroke + 2f))
            using (var pen = new Pen(Color.FromArgb(alpha, color), stroke))
            using (var font = new Font("Consolas", idSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(Math.Min(205, alpha), 0, 0, 0)))
            using (var brush = new SolidBrush(Color.FromArgb(alpha, color)))
            {
                DrawFourWayWings(g, shadowPen, p, gap, wing, depth, 1f, 1f);
                DrawFourWayWings(g, pen, p, gap, wing, depth, 0f, 0f);
                string id = m.TrackId > 0 ? m.TrackId.ToString("00") : "--";
                SizeF idm = g.MeasureString(id, font);
                float ix = p.X - idm.Width / 2f;
                float iy = p.Y - gap - wing - idm.Height - 3f * scale;
                g.DrawString(id, font, shadowBrush, ix + 1.2f, iy + 1.2f);
                g.DrawString(id, font, brush, ix, iy);
            }
        }

        private static void DrawFourWayWings(Graphics g, Pen pen, PointF p, float gap, float wing, float depth, float ox, float oy)
        {
            g.DrawLine(pen, p.X+ox-gap-wing, p.Y+oy-depth, p.X+ox-gap, p.Y+oy);
            g.DrawLine(pen, p.X+ox-gap-wing, p.Y+oy+depth, p.X+ox-gap, p.Y+oy);
            g.DrawLine(pen, p.X+ox+gap+wing, p.Y+oy-depth, p.X+ox+gap, p.Y+oy);
            g.DrawLine(pen, p.X+ox+gap+wing, p.Y+oy+depth, p.X+ox+gap, p.Y+oy);
            g.DrawLine(pen, p.X+ox-depth, p.Y+oy-gap-wing, p.X+ox, p.Y+oy-gap);
            g.DrawLine(pen, p.X+ox+depth, p.Y+oy-gap-wing, p.X+ox, p.Y+oy-gap);
            g.DrawLine(pen, p.X+ox-depth, p.Y+oy+gap+wing, p.X+ox, p.Y+oy+gap);
            g.DrawLine(pen, p.X+ox+depth, p.Y+oy+gap+wing, p.X+ox, p.Y+oy+gap);
        }

        private void DrawClassicReticle(Graphics g, PointF p, OverlayMarker m, Color color, float scale)
        {
            // ZEOCORE_V066_CLASSIC_RETICLE
            // Old Zeo Flight HUD visual language: persistent ID + minimal > < cue.
            // No extra focus ring, triangle, square or modern shape is allowed here.
            float bracket = 19f * scale;
            float halfGap = 8.5f * scale;
            float wing = 9f * scale;
            float idScale = (float)((m.Source == 0 || m.Source == 4) ? _settings.SpectrumIdScale : 1.0);
            float idSize = Math.Max(12f, 15f * scale * idScale);
            float stroke = Math.Max(1.6f, 1.9f * scale);
            int alpha = m.Stale ? 160 : 245;

            using (var shadowPen = new Pen(Color.FromArgb(Math.Min(210, alpha), 0, 0, 0), stroke + 2.2f))
            using (var pen = new Pen(Color.FromArgb(alpha, color), stroke))
            using (var font = new Font("Consolas", idSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(Math.Min(220, alpha), 0, 0, 0)))
            using (var brush = new SolidBrush(Color.FromArgb(alpha, color)))
            {
                DrawReticleWings(g, shadowPen, p, halfGap, wing, bracket, 1f, 1f);
                DrawReticleWings(g, pen, p, halfGap, wing, bracket, 0f, 0f);

                string id = m.TrackId > 0 ? m.TrackId.ToString("00") : "--";
                SizeF idm = g.MeasureString(id, font);
                float ix = p.X - halfGap - wing - idm.Width - 12f * scale;
                float iy = p.Y - idm.Height / 2f;
                g.DrawString(id, font, shadowBrush, ix + 1.4f, iy + 1.4f);
                g.DrawString(id, font, brush, ix, iy);
            }
        }


        private static void DrawReticleWings(Graphics g, Pen pen, PointF p, float halfGap, float wing, float bracket, float ox, float oy)
        {
            g.DrawLine(pen, p.X + ox - halfGap - wing, p.Y + oy - bracket * .42f, p.X + ox - halfGap, p.Y + oy);
            g.DrawLine(pen, p.X + ox - halfGap, p.Y + oy, p.X + ox - halfGap - wing, p.Y + oy + bracket * .42f);
            g.DrawLine(pen, p.X + ox + halfGap + wing, p.Y + oy - bracket * .42f, p.X + ox + halfGap, p.Y + oy);
            g.DrawLine(pen, p.X + ox + halfGap, p.Y + oy, p.X + ox + halfGap + wing, p.Y + oy + bracket * .42f);
        }

        private void DrawFriendly(Graphics g, PointF p, OverlayMarker m, Color color, float scale)
        {
            float r = 10f * scale;
            float idScale = (float)_settings.FriendlyIdScale;
            using (var pen = new Pen(Color.FromArgb(m.Stale ? 155 : 235, color), Math.Max(1.3f, 1.6f * scale)))
            using (var font = new Font("Consolas", Math.Max(10f, 13f * scale * idScale), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Color.FromArgb(m.Stale ? 170 : 245, color)))
            {
                PointF[] diamond =
                {
                    new PointF(p.X, p.Y-r), new PointF(p.X+r, p.Y), new PointF(p.X, p.Y+r), new PointF(p.X-r, p.Y), new PointF(p.X, p.Y-r)
                };
                g.DrawLines(pen, diamond);
                string id = m.TrackId > 0 ? m.TrackId.ToString("00") : "--";
                g.DrawString(id, font, brush, p.X + r + 5f * scale, p.Y - font.Height / 2f);
                if (m.Focused) g.DrawEllipse(pen, p.X-r*1.65f, p.Y-r*1.65f, r*3.3f, r*3.3f);
            }
        }

        private void DrawOffscreen(Graphics g, int width, int height, PointF p, OverlayMarker m, Color color, float scale)
        {
            float cx = width / 2f, cy = height / 2f;
            float dx = cx - p.X, dy = cy - p.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) len = 1;
            dx /= len; dy /= len;
            float px = -dy, py = dx;
            float tip = 13f * scale, wing = 7f * scale;
            PointF a = new PointF(p.X + dx * tip, p.Y + dy * tip);
            PointF b = new PointF(p.X - dx * 4f * scale + px * wing, p.Y - dy * 4f * scale + py * wing);
            PointF c = new PointF(p.X - dx * 4f * scale - px * wing, p.Y - dy * 4f * scale - py * wing);
            using (var pen = new Pen(Color.FromArgb(230, color), Math.Max(1.2f, 1.6f * scale)))
            using (var font = new Font("Consolas", Math.Max(10f, 12f * scale), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Color.FromArgb(235, color)))
            {
                g.DrawPolygon(pen, new[] { a, b, c });
                string id = m.TrackId > 0 ? m.TrackId.ToString("00") : "--";
                g.DrawString(id, font, brush, p.X + 9f * scale, p.Y - font.Height / 2f);
            }
        }

        private void DrawMarkerDetail(Graphics g, PointF p, OverlayMarker m, Color color, float scale)
        {
            bool priority = m.Focused || m.Priority > 16000;
            if (!priority && !_settings.ShowNames && !_settings.ShowDistance) return;
            var parts = new List<string>();
            if (_settings.ShowDistance) parts.Add(FormatRange(m.Distance));
            if (_settings.ShowNames && !string.IsNullOrWhiteSpace(m.Name)) parts.Add(ShortName(m.Name, 20));
            if (_settings.ShowClosingOnPriority && priority && !m.Friendly) parts.Add((m.Closing >= 0 ? "C" : "R") + Math.Abs(m.Closing).ToString("0"));
            if (parts.Count == 0) return;
            using (var font = new Font("Segoe UI", Math.Max(9f, 10f * scale), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Color.FromArgb(m.Stale ? 155 : 220, color)))
                g.DrawString(string.Join("  ", parts.ToArray()), font, brush, p.X + 18f * scale, p.Y + 12f * scale);
        }

        private double MarkerScale(OverlayMarker m)
        {
            // ZEOCORE_V067_UNIVERSAL_DISTANCE_SCALE
            // Zeo's Flight HUD v0.7.x distance response, now universal:
            // <=2 km = tight/minimum; >=60 km = large/maximum; SmoothStep between.
            double t = (m.Distance - 2000.0) / (60000.0 - 2000.0);
            t = Math.Max(0, Math.Min(1, t));
            t = t * t * (3.0 - 2.0 * t);
            double distanceGain = 0.85 + (1.65 - 0.85) * t;

            double baseScale;
            if (m.Source == 0 || m.Source == 4)
                baseScale = _settings.SpectrumMarkerScale;
            else if (m.Friendly || m.Distress)
                baseScale = _settings.FriendlyMarkerScale;
            else
                baseScale = _settings.HostileMarkerScale;

            return baseScale * distanceGain;
        }


        private Color MarkerColor(OverlayMarker m)
        {
            // ZEOCORE_V067H4_SEMANTIC_TRACK_COLORS
            if (m.Distress) return _settings.ColorOf(_settings.DistressColor, Color.Red);
            if (m.Stale) return _settings.ColorOf(_settings.StaleColor, Color.Gray);
            if (m.Focused) return _settings.ColorOf(_settings.FocusColor, Color.Khaki);
            if (m.Friendly || string.Equals(m.Relation, "friendly", StringComparison.OrdinalIgnoreCase))
                return _settings.ColorOf(_settings.FriendlyColor, Color.Cyan);

            string relation = (m.Relation ?? "").Trim();
            if (relation.Equals("hostile", StringComparison.OrdinalIgnoreCase) ||
                relation.Equals("enemy", StringComparison.OrdinalIgnoreCase))
                return _settings.ThemePreset == 4
                    ? _settings.ColorOf(_settings.HostileColor, Color.Red)
                    : Color.FromArgb(240, 68, 68);

            if (relation.Equals("neutral", StringComparison.OrdinalIgnoreCase))
                return _settings.ThemePreset == 4
                    ? _settings.ColorOf(_settings.NeutralColor, Color.White)
                    : Color.White;

            // Unknown / unclassified / raw sensor contact.
            return _settings.ThemePreset == 4
                ? _settings.ColorOf(_settings.SpectrumColor, Color.Orange)
                : Color.FromArgb(255, 184, 74);
        }

        private static PointF NormToPixel(int width, int height, double nx, double ny)
        {
            return new PointF((float)((nx + 1.0) * 0.5 * width), (float)((1.0 - ny) * 0.5 * height));
        }

        private static string ScopeStatus(OverlayScopeRow r)
        {
            if (r.Distress)
                return r.DistressSecondsRemaining > 0
                    ? "SOS " + Math.Ceiling(r.DistressSecondsRemaining / 60.0).ToString("0") + "m"
                    : "SOS ACTIVE";
            return "S" + r.Speed.ToString("0") + " " + ScopeVector(r);
        }

        private static string ScopeVector(OverlayScopeRow r)
        {
            return r.Closing > 5 ? "C" + r.Closing.ToString("0") : (r.Closing < -5 ? "R" + (-r.Closing).ToString("0") : "HOLD");
        }

        private static string FormatRange(double meters)
        {
            if (meters >= 100000) return (meters / 1000.0).ToString("0") + "k";
            if (meters >= 10000) return (meters / 1000.0).ToString("0.0") + "k";
            if (meters >= 1000) return (meters / 1000.0).ToString("0.00") + "k";
            return meters.ToString("0") + "m";
        }

        private static string Pad(string value, int width)
        {
            value = value ?? "";
            return value.Length >= width ? value.Substring(0, width) : value.PadRight(width);
        }

        private static string ShortName(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim().Replace("\r", " ").Replace("\n", " ");
            return value.Length <= max ? value : value.Substring(0, max - 1) + "…";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _running = false;
            try { _renderTimer.Stop(); } catch { }
            try { _settingsTimer.Stop(); } catch { }
            try { if (_registeredVk != 0) NativeMethods.UnregisterHotKey(Handle, HotkeyId); } catch { }
            try { if (_menu != null && !_menu.IsDisposed) _menu.CloseForShutdown(); } catch { }
            try { if (_udp != null) _udp.Close(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
