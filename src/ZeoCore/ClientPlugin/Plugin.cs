using System;
using System.IO;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Plugins;

namespace ZeoCore
{
    public sealed class Plugin : IPlugin
    {
        public const string Version = "1.0.6-HUD-CONSISTENCY";
        public static Plugin Instance { get; private set; }

        // Pulsar supplies the matching, hash-verified overlay before Init.
        // Persistent configuration stays in DataDirectory, outside its cache.
        internal static string CatalogOverlayPath { get; private set; }

        public void LoadAssets(IReadOnlyDictionary<string, string> assets)
        {
            string directory;
            if (assets == null || !assets.TryGetValue("ZeoOverlayPackage", out directory)
                || string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("Pulsar did not supply the Zeo overlay package.");
            string executable = Path.GetFullPath(Path.Combine(directory, "ZeoOverlay.exe"));
            if (!File.Exists(executable) || !File.Exists(executable + ".config"))
                throw new FileNotFoundException("The Zeo overlay package is incomplete.", executable);
            CatalogOverlayPath = executable;
        }

        private ZeoCoreEngine _engine;
        private ZeoHudController _hud;
        private readonly QuickRefillController _refill = new QuickRefillController();
        internal static bool RefillActive { get { return Instance?._refill.Active ?? false; } }
        internal static string RefillStatus { get { return Instance?._refill.Status ?? "Start a world to use Quick Refill."; } }
        internal static void ToggleRefill() { if(Instance!=null) Instance._refill.Toggle(Instance._engine?.GetHudSnapshot()); }
        private bool _runtimeReadyNotified;

        internal static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pulsar", "ZeoCore");

        internal static readonly string LogPath = Path.Combine(DataDirectory, "zeocore.log");
        internal static readonly string LastPayloadPath = Path.Combine(DataDirectory, "last-payload.json");

        private static readonly object LogLock = new object();

        public void Init(object gameInstance)
        {
            Instance = this;
            try
            {
                Directory.CreateDirectory(DataDirectory);
                Log("============================================================");
                Log("ZeoCore v" + Version + " OPEN TEST RESET + UNIFIED TACTICAL NETWORK + WAR ROOM HUD init");
                _engine = new ZeoCoreEngine();
                _hud = new ZeoHudController(_engine, _engine.Config);
            }
            catch (Exception ex)
            {
                Log("Init ERROR: " + ex);
            }
        }

        public void Update()
        {
            _refill.Update();
            try
            {
                if (_engine == null) _engine = new ZeoCoreEngine();
                if (_hud == null) _hud = new ZeoHudController(_engine, _engine.Config);

                _engine.Update();
                _hud.Update();

                if (!_runtimeReadyNotified && MyAPIGateway.Session != null && MyAPIGateway.Utilities != null)
                {
                    _runtimeReadyNotified = true;
                    Log("Runtime update loop ACTIVE // OPEN TEST canary passed.");
                    Notify("ZEOCORE 0.6.1 // ALIGNED OPEN TEST ACTIVE", 6500, "Green");
                }
            }
            catch (Exception ex)
            {
                Log("Update ERROR: " + ex);
            }
        }

        // Pulsar's Open Config action opens the same external capture-safe menu as
        // the HOME hotkey. No in-game GUI is drawn for normal configuration.
        public void OpenConfigDialog()
        {
            try
            {
                if (_engine == null) _engine = new ZeoCoreEngine();
                if (_hud == null) _hud = new ZeoHudController(_engine, _engine.Config);
                _hud.OpenMenu();
            }
            catch (Exception ex)
            {
                Log("OpenConfigDialog ERROR: " + ex);
            }
        }

        public void Dispose()
        {
            try { _refill.Dispose(); } catch { }
            try { if (_hud != null) _hud.Dispose(); } catch { }
            try { if (_engine != null) _engine.Dispose(); } catch { }
            _hud = null;
            _engine = null;
            Instance = null;
            Log("Disposed.");
        }

        internal static void OpenHudMenu()
        {
            try
            {
                if (Instance != null && Instance._hud != null) Instance._hud.OpenMenu();
            }
            catch (Exception ex) { Log("OpenHudMenu ERROR: " + ex.Message); }
        }

        internal static string HudDiagnostic()
        {
            try { return Instance != null && Instance._hud != null ? Instance._hud.Diagnostic() : "HUD=OFF"; }
            catch { return "HUD=ERR"; }
        }

        internal static void Notify(string text, int ms = 3500, string font = "White")
        {
            try
            {
                if (MyAPIGateway.Utilities != null)
                    MyAPIGateway.Utilities.ShowNotification(text, ms, font);
            }
            catch { }
        }

        internal static void Log(string text)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(DataDirectory);
                    File.AppendAllText(LogPath,
                        DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC' ") + text + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
