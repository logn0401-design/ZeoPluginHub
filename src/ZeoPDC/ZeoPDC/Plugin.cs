using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.Plugins;

namespace ZeoPDC
{
    public sealed partial class Plugin : IPlugin
    {
        public const string Version = "0.3.31";
        private readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulsar", "ZeoPDC");
        private string configPath, logPath, endpointPath;
        private PdcConfig config;
        private CoreSystemsApi wc;
        private PdcEngine engine;
        private UdpClient tx, rx;
        private IPEndPoint overlayEndpoint;
        private int commandPort, frame, gamePid;
        private long snapshotSeq;
        private long gameHwnd;
        private Process overlayProcess;
        private DateTime lastOverlayLaunch = DateTime.MinValue;
        private bool menuVisible, disposed;
        private bool openSettingsRequested;
        private PdcSettingsModel settingsModel;
        private PdcNativeSettingsUi nativeMenu;
        private PdcHudLayoutScreen layoutScreen;

        public void Init(object gameInstance)
        {
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(Path.Combine(dataDir, "Tests"));
            configPath = Path.Combine(dataDir, "config.json");
            logPath = Path.Combine(dataDir, "zeopdc.log");
            endpointPath = Path.Combine(dataDir, "coresystems-endpoints.txt");
            config = PdcConfig.Clamp(JsonIo.Load<PdcConfig>(configPath) ?? new PdcConfig());
            if(config.ManagerPresetVersion<1 && File.Exists(configPath)) {
                var backups=Path.Combine(dataDir,"Backups"); Directory.CreateDirectory(backups);
                File.Copy(configPath,Path.Combine(backups,DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")+"_before_manager_config.json"));
            }
            config=ManagerSettings.Upgrade(config);
            config.ClientProbeEnabled=false; // production mode persists; no test resumes
            config.LabEnabled=false; // never resume experimental ownership after a restart
            JsonIo.Save(configPath, config);
            RefreshWindowIdentity();
            tx = new UdpClient(AddressFamily.InterNetwork);
            rx = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            commandPort = ((IPEndPoint)rx.Client.LocalEndPoint).Port;
            rx.Client.Blocking = false;
            wc = new CoreSystemsApi(Log, endpointPath);
            wc.EnsureLoaded();
            engine = new PdcEngine(wc, () => config, dataDir, Log);

            InitializeSettings();
            LaunchOverlay();
            Log("============================================================");
            Log("Zeo PDC Intelligence " + Version + " initialized // menu=" + config.MenuKey);
            Log("MANAGER: native WC targeting/aim/fire with distribution, Zeo preaim and adaptive ROF. Range-only heat banks available; grid filters remain player controlled.");
        }

        public void Update()
        {
            if (disposed) return;
            frame++;
            try
            {
                engine.SetTelemetryTick(frame);
                PollCommands();
                if (frame % 30 == 0) RefreshWindowIdentity();
                if (!wc.Ready)
                {
                    // IPlugin.Init can run before MyAPIGateway.Utilities exists.
                    // Retry registration from the live update loop, matching the
                    // known-good ZeoCore runtime pattern.
                    wc.EnsureLoaded();
                    if (frame % 180 == 0) wc.RequestApi();
                }
                if (openSettingsRequested) { openSettingsRequested = false; ToggleSettings(); }
                HandleHotkeys();
                engine.Update(frame);

                engine.PublishLiveTelemetry();
                if (frame % 3 == 0) SendSnapshot();
                if (frame % 600 == 0) LaunchOverlay();
            }
            catch (Exception ex)
            {
                Log("UPDATE ERROR: " + ex);
            }
        }

        public void OpenConfigDialog() { openSettingsRequested = true; }

        private void InitializeSettings()
        {
            settingsModel = new PdcSettingsModel(configPath, () => config, () => engine != null && engine.TestArmed, next => {
                // Preserve the existing live config instance for engine and callbacks.
                foreach (var field in typeof(PdcConfig).GetFields(BindingFlags.Public | BindingFlags.Instance)) field.SetValue(config, field.GetValue(next));
                engine.RecordConfiguration(settingsModel.LastRequested);
                SendSnapshot();
            });
            nativeMenu = new PdcNativeSettingsUi(settingsModel, MenuSnapshot, RunMenuCommand, OpenLegacyMenu, BeginLayout);
        }
        private PdcSnapshot MenuSnapshot()
        {
            var s = engine.BuildSnapshot(); s.Config = config; s.Version = Version; s.GameHwnd = gameHwnd;
            var r = WinRect.GetClientScreenRect(new IntPtr(gameHwnd)); s.ClientX=r.X; s.ClientY=r.Y; s.ClientW=r.W; s.ClientH=r.H;
            return s;
        }
        private void ToggleSettings()
        {
            if (layoutScreen != null) { layoutScreen.CloseScreen(); return; }
            if (menuVisible) { menuVisible=false; SendSnapshot(); return; }
            try { if(nativeMenu==null) InitializeSettings(); nativeMenu.Toggle(); }
            catch(Exception ex) { Log("Native settings unavailable: " + ex.Message); OpenLegacyMenu(); }
        }
        private void OpenLegacyMenu() { menuVisible=true; LaunchOverlay(); SendSnapshot(); }
        private void BeginLayout()
        {
            var next=new PdcHudLayoutScreen(settingsModel,MenuSnapshot); layoutScreen=next;
            next.Closed+=delegate { if(ReferenceEquals(layoutScreen,next)) layoutScreen=null; SendSnapshot(); };
            Sandbox.Graphics.GUI.MyGuiSandbox.AddScreen(next); LaunchOverlay(); SendSnapshot();
        }
        private void RunMenuCommand(string type)
        {
            if(type=="ARM_TEST" || type.StartsWith("LAB_"))return;
            if (type=="ARM_TEST") { engine.CaptureArmRequest(config); config.ControlEnabled=true; config=PdcConfig.Clamp(config); JsonIo.Save(configPath,config); engine.ArmTest(); }
            else if(type=="FINISH_TEST") engine.FinishTest();
            else if(type=="ABORT_TEST") engine.AbortTest();
            else if(type=="OPEN_FOLDER") engine.OpenTestFolder();
            SendSnapshot();
        }

        public void Dispose()
        {
            disposed = true;
            try { nativeMenu?.Close(); layoutScreen?.CloseScreen(true); } catch { }
            try { engine?.Dispose(); } catch { }
            try { wc?.Dispose(); } catch { }
            try { rx?.Close(); } catch { }
            try { tx?.Close(); } catch { }
            try
            {
                if (overlayProcess != null && !overlayProcess.HasExited)
                {
                    overlayProcess.Kill();
                    overlayProcess.WaitForExit(750);
                }
            }
            catch { }
            Log("Disposed.");
        }

        private void HandleHotkeys()
        {
            if(PdcNativeSettingsScreen.EditingBinding) return;
            if (KeyNew(config.MenuKey))
            {
                var focus=Sandbox.Graphics.GUI.MyScreenManager.GetScreenWithFocus();
                var menu=focus as PdcNativeSettingsScreen;
                if(menu==null && !(focus is Sandbox.Game.Gui.MyGuiScreenGamePlay))return;
                if(menu!=null && menu.IsEditingText)return;
                if(MyAPIGateway.Gui==null || MyAPIGateway.Gui.ChatEntryVisible)return;
                ToggleSettings();
                return;
            }
        }

        private bool KeyNew(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Equals("None", StringComparison.OrdinalIgnoreCase)) return false;
            MyKeys k;
            if (!Enum.TryParse(key.Replace(" ", ""), true, out k))
            {
                if (key.Equals("PageDown", StringComparison.OrdinalIgnoreCase) || key.Equals("PAGE DOWN", StringComparison.OrdinalIgnoreCase)) k = MyKeys.PageDown;
                else if (key.Equals("PageUp", StringComparison.OrdinalIgnoreCase) || key.Equals("PAGE UP", StringComparison.OrdinalIgnoreCase)) k = MyKeys.PageUp;
                else return false;
            }
            try { return MyAPIGateway.Input != null && MyAPIGateway.Input.IsNewKeyPressed(k); } catch { return false; }
        }

        private void PollCommands()
        {
            if (rx == null) return;
            int guard = 0;
            while (rx.Available > 0 && guard++ < 30)
            {
                IPEndPoint ep = new IPEndPoint(IPAddress.Loopback, 0);
                byte[] bytes = rx.Receive(ref ep);
                PdcCommand cmd = JsonIo.FromBytes<PdcCommand>(bytes);
                if (cmd == null) continue;
                string type = (cmd.Type ?? "").Trim().ToUpperInvariant();
                if (type.StartsWith("LAB_",StringComparison.Ordinal) || type=="ARM_TEST" || type=="DEFENSE_PRESET") { Log("Retired test command ignored: "+type); continue; }
                if (type == "HELLO")
                {
                    int port = (int)Math.Round(cmd.Value);
                    if (port > 1024 && port < 65535)
                    {
                        bool changed = overlayEndpoint == null || overlayEndpoint.Port != port;
                        overlayEndpoint = new IPEndPoint(IPAddress.Loopback, port);
                        if (changed) Log("Overlay handshake -> snapshots 127.0.0.1:" + port + " / commands:" + commandPort);
                        SendSnapshot();
                    }
                }
                else if (type == "ARM_TEST")
                {
                    Log("CMD RX // ARM_TEST // DIRECT CONTROL + CONTINUOUS 32-TORPEDO VOLLEYS");
                    engine.CaptureArmRequest(config);
                    config.ControlEnabled = true;
                    config = PdcConfig.Clamp(config);
                    JsonIo.Save(configPath, config);
                    engine.ArmTest();
                    SendSnapshot();
                }
                else if (type == "FINISH_TEST") { engine.FinishTest(); SendSnapshot(); }
                else if (type == "ABORT_TEST") { Log("CMD RX // ABORT_TEST"); engine.AbortTest(); SendSnapshot(); }
                else if (type == "OPEN_FOLDER") engine.OpenTestFolder();
                else if (type == "MENU_CLOSE") menuVisible = false;
                else if (type == "MENU_TOGGLE") ToggleSettings();
                else if (type == "SET") ApplySetting(cmd.Key, cmd.Text, cmd.Value);
                else if (type == "DEFENSE_PRESET")
                {
                    try
                    {
                        if (settingsModel == null) InitializeSettings();
                        if(config.LabEnabled) throw new InvalidOperationException("Use LAB_PATCH during lab session.");
                        settingsModel.ApplyDefensePreset(cmd.Key);
                        Log("DEFENSE_PRESET " + cmd.Key + " saved; verify effective telemetry before counting a cohort.");
                    }
                    catch (Exception ex) { Log("DEFENSE_PRESET rejected: " + ex.Message); }
                }
            }
        }

        private void ApplySetting(string key, string text, double value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if(key.StartsWith("Lab",StringComparison.OrdinalIgnoreCase) || (config.LabEnabled && !(PdcSettingsCatalog.Find(key)?.Appearance ?? false))) { Log("Use LAB_PATCH for active lab changes."); return; }
            try
            {
                var option=PdcSettingsCatalog.Find(key); if(option==null) return;
                if(engine.TestArmed && !option.Appearance && !option.ObservationOnly) { engine.ReportSettingBlocked(); SendSnapshot(); return; }
                if(settingsModel==null) InitializeSettings();
                string input=option.Field.FieldType==typeof(string)?text:option.Field.FieldType==typeof(bool)?(value>=.5?"true":"false"):value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                settingsModel.Apply(option.Key,input);
            }
            catch(Exception ex) { Log("SET " + key + " failed: " + ex.Message); }
        }

        private static bool WorldReadyForHud()
        {
            try { var focused=Sandbox.Graphics.GUI.MyScreenManager.GetScreenWithFocus();
                if(HudVisibility.MenuBlocksWorld(focused==null?null:focused.GetType().Name))return false;
                return MyAPIGateway.Session!=null && Sandbox.Game.World.MySession.Static!=null &&
                Sandbox.Game.World.MySession.Static.Ready && Sandbox.Game.Gui.MyGuiScreenGamePlay.Static!=null; }
            catch { return false; }
        }

        private static bool ControllingShipForHud()
        {
            try {
                var player=MyAPIGateway.Session==null?null:MyAPIGateway.Session.Player;
                var controlled=player==null||player.Controller==null?null:player.Controller.ControlledEntity;
                var ship=controlled==null?null:controlled.Entity as Sandbox.ModAPI.IMyShipController;
                return ship!=null&&ship.CubeGrid!=null&&!ship.Closed;
            } catch {return false;}
        }

        private void SendSnapshot()
        {
            if (overlayEndpoint == null || engine == null) return;
            try
            {
                bool loaded=WorldReadyForHud();
                PdcSnapshot s = loaded?engine.BuildSnapshot():new PdcSnapshot();
                s.WorldLoaded=loaded;
                s.Version = Version;
                s.SnapshotSeq = ++snapshotSeq;
                s.SnapshotUtcTicks = DateTime.UtcNow.Ticks;
                s.GameHwnd = gameHwnd;
                s.GamePid = gamePid;
                ScreenRect r = WinRect.GetClientScreenRect(new IntPtr(gameHwnd));
                s.ClientX = r.X; s.ClientY = r.Y; s.ClientW = r.W; s.ClientH = r.H;
                s.MenuVisible = menuVisible;
                s.HudVisible = HudVisibility.ConfigAllows(config,loaded,loaded&&ControllingShipForHud(),layoutScreen!=null);
                s.Config = config;
                if (loaded && layoutScreen != null)
                {
                    s.Config=JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(config));
                    s.Config.HudX=layoutScreen.X; s.Config.HudY=layoutScreen.Y; s.Config.PanelScale=layoutScreen.PanelScale;s.Config.HudWidth=layoutScreen.HudWidth;s.Config.HudHeight=layoutScreen.HudHeight;
                    s.HudClipTopPixels=(int)Sandbox.Graphics.MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(new VRageMath.Vector2(.5f,.18f)).Y + 8;
                }
                byte[] b = JsonIo.ToBytes(s);
                tx.Send(b, b.Length, overlayEndpoint);
            }
            catch (Exception ex) { if (frame % 300 == 0) Log("Snapshot send failed: " + ex.Message); }
        }

        private string catalogOverlayPath;
        // Pulsar verifies/extracts this release's package before Init.
        // Configuration stays in dataDir; never copy binaries over a running overlay.
        public void LoadAssets(IReadOnlyDictionary<string,string> assets)
        {
            string directory;
            if(assets==null || !assets.TryGetValue("ZeoPdcOverlayPackage",out directory) || string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("Pulsar did not supply the PDC overlay package.");
            var executable=Path.GetFullPath(Path.Combine(directory,"ZeoPdcOverlay.exe"));
            if(!File.Exists(executable)||!File.Exists(executable+".config"))
                throw new FileNotFoundException("The PDC overlay package is incomplete.",executable);
            catalogOverlayPath=executable;
        }

        private void LaunchOverlay()
        {
            if ((DateTime.UtcNow - lastOverlayLaunch).TotalSeconds < 5) return;
            lastOverlayLaunch = DateTime.UtcNow;
            try
            {
                if (overlayProcess != null)
                {
                    try { if (!overlayProcess.HasExited) return; } catch { }
                    overlayProcess = null; overlayEndpoint = null;
                }
                string exe = catalogOverlayPath ?? Path.Combine(dataDir, "ZeoPdcOverlay.exe");
                if (!File.Exists(exe)) return;
                overlayProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--command-port " + commandPort + " --owner-pid " + gamePid,
                    WorkingDirectory = dataDir,
                    UseShellExecute = true
                });
                Log("Overlay launch requested // commandPort=" + commandPort + " ownerPid=" + gamePid);
            }
            catch (Exception ex) { Log("Overlay launch failed: " + ex.Message); }
        }

        private void RefreshWindowIdentity()
        {
            try
            {
                Process p = Process.GetCurrentProcess();
                gamePid = p.Id;
                if (p.MainWindowHandle != IntPtr.Zero) gameHwnd = p.MainWindowHandle.ToInt64();
            }
            catch { }
        }

        private void Log(string line)
        {
            try { File.AppendAllText(logPath, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC' ") + line + Environment.NewLine); } catch { }
        }
    }

    internal struct ScreenRect { public int X, Y, W, H; }
    internal static class WinRect
    {
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
        public static ScreenRect GetClientScreenRect(IntPtr hwnd)
        {
            ScreenRect s = new ScreenRect { X = 0, Y = 0, W = 1920, H = 1080 };
            try
            {
                RECT r; if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out r)) return s;
                POINT p = new POINT { X = 0, Y = 0 }; ClientToScreen(hwnd, ref p);
                s.X = p.X; s.Y = p.Y; s.W = Math.Max(1, r.R - r.L); s.H = Math.Max(1, r.B - r.T);
            }
            catch { }
            return s;
        }
    }
}
