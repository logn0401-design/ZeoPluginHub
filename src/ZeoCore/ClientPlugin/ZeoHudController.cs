using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Input;
using VRageMath;

namespace ZeoCore
{
    internal sealed partial class ZeoHudController : IDisposable
    {
        private sealed class SpectrumSeen
        {
            public SpectrumClient.DetectionData Detection;
            public int LastSeenFrame;
            public Vector3D Acceleration;
        }

        private readonly ZeoCoreEngine _engine;
        private readonly ExactTrackFusion _exactFusion=new ExactTrackFusion();
        private int _exactFusionMerged,_exactFusionPeak,_exactFusionLogFrame;
        private readonly Func<long,long> _resolveTrackIdentity;
        private readonly Dictionary<long,long?> _liveTrackIdentities=new Dictionary<long,long?>();
        private static readonly Action<HudTrack,HudTrack> MergeKnowledge=MergeTrackKnowledge;
        private readonly HudSettings _settings;
        private readonly SpectrumClient _spectrum = new SpectrumClient();
        private readonly FleetLinkClient _fleet;
        private readonly OverlayBridge _overlay = new OverlayBridge();
        private readonly TrackIdAllocator _ids = new TrackIdAllocator();
        private readonly List<HudTrack> _tracks = new List<HudTrack>(128);
        private readonly Dictionary<long, HudTrack> _trackByEntity = new Dictionary<long, HudTrack>(128);
        private readonly Dictionary<long, List<HudTrack>> _fusionBuckets = new Dictionary<long, List<HudTrack>>();
        private readonly List<Vector2D> _occupied = new List<Vector2D>(64);

        private readonly HudPerformance _performance=new HudPerformance();
        private int _lastSpectrumFrame = -100000;
        private int _lastTrackBuildFrame = -100000;
        private DateTime _lastTrackBuildUtc = DateTime.MinValue;
        private int _lastPerfLogFrame = -100000;
        private readonly System.Diagnostics.Stopwatch _trackPerfClock = new System.Diagnostics.Stopwatch();
        private int _lastSettingsFrame = -100000;
        private int _lastOverlayFrame = -100000;
        // ZEOCORE_V067H3_DIRECT_HUD_SYNC
        private int _lastGameHudStateFrame = -100000;
        private int _gameHudState = 1;
        private List<HudTrack> _markerTracks = new List<HudTrack>();
        private bool _markerShipActive;
        private long _overlaySequence;
        private DateTime _lastLayoutPublishUtc;
        private bool _wasEditingLayout;
        private readonly Dictionary<long,SpectrumSeen> _spectrumByGrid=new Dictionary<long,SpectrumSeen>();
        private readonly Func<long,Vector3D?> _nativeSignalPosition;
        private readonly Dictionary<long, SpectrumSeen> _spectrumPersistent = new Dictionary<long, SpectrumSeen>();
        private readonly List<SpectrumClient.DetectionData> _spectrumCache = new List<SpectrumClient.DetectionData>();
        private bool _readyLogged;
        private bool _lastTx;
        private bool _lastWritePayload;
        private int _lastMenuKeyFrame = -100000;
        private readonly ZeoOverlay.QuickRefillKeyLatch _refillKey=new ZeoOverlay.QuickRefillKeyLatch();
        private string _lastSectorId = "";
        private readonly DistressSender _distress;
        private readonly DistressGpsBridge _distressGps = new DistressGpsBridge();
        private int _distressHoldStartFrame = -1;
        private bool _distressTriggeredThisHold;
        private bool _localDistressActive;
        private long _localDistressExpiresMs;
        private string _pendingDistressAction = "";
        private long _pendingDistressExpiresMs;
        private string _distressStatus = "READY / NOT SENT";

        public ZeoHudController(ZeoCoreEngine engine, ZeoConfig coreConfig)
        {
            _nativeSignalPosition=ReadNativeSignalPosition;
            _resolveTrackIdentity=ResolveCachedTrackIdentity;
            _engine = engine;
            _targetConfig=coreConfig;
            _settings = HudSettings.Load();

            if (!_settings.PrivacyInitialized)
            {
                _settings.TransmitTelemetry = _engine.TelemetryExportPreferenceEnabled;
                _settings.WriteLocalPayload = _engine.LocalPayloadLoggingEnabled;
                _settings.PrivacyInitialized = true;
                _settings.Save();
            }

            _lastTx = !_settings.TransmitTelemetry;
            _lastWritePayload = !_settings.WriteLocalPayload;
            ApplyRuntimePrivacy();

            try
            {
                Uri fleetEndpoint = coreConfig.GetDeviceNetworkEndpoint();
                _fleet = new FleetLinkClient(fleetEndpoint, coreConfig.DeviceId);
                Plugin.Log("OPEN TEST unified tactical network armed. auth=OFF host=" + _fleet.Host);

                Uri distressEndpoint = coreConfig.GetDeviceDistressEndpoint();
                _distress = new DistressSender(distressEndpoint, coreConfig.DeviceId);
                Plugin.Log("OPEN TEST distress sender armed. auth=OFF");
            }
            catch (Exception ex)
            {
                Plugin.Log("Unified tactical network disabled: " + ex.Message);
                _distressStatus = "NETWORK NOT READY";
            }

            _overlay.EnsureRunning(_settings.OverlayAutoLaunch);
        }

        public HudSettings Settings { get { return _settings; } }

        public void Update()
        {
            var session = MyAPIGateway.Session;
            if (session == null || MyAPIGateway.Utilities == null)
            { _distressGps.Reset();DisposeTargets();_targetContext="";_chosenIds=null; return; }

            int frame;
            try { frame = session.GameplayFrameCounter; }
            catch { return; }

            PollGameMenuKey(frame);
            if(ZeoHudLayoutSession.Current!=null) _overlay.PollLayoutFeedback();

            if (frame < _lastSettingsFrame || frame - _lastSettingsFrame >= 30)
            {
                _lastSettingsFrame = frame;
                if (_settings.ReloadIfChanged())
                {
                    ApplyRuntimePrivacy();
                    Plugin.Log("External Zeo HUD settings reloaded.");
                }
            }

            _overlay.EnsureRunning(_settings.OverlayAutoLaunch);

            SectorSnapshot sector = SectorIdentity.Capture();
            PollQuickRefillKey();
            if (!string.Equals(_lastSectorId, sector.Id, StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(_lastSectorId))
                {
                    Plugin.Log("Sector transition " + _lastSectorId + " -> " + sector.Id + " (" + sector.Name + ") // clearing local track caches");
                    _spectrumPersistent.Clear(); _spectrumByGrid.Clear();
                    _spectrumCache.Clear();
                    _ids.Clear();
                }
                _lastSectorId = sector.Id;
            }

            ServerTrustSnapshot trust = _engine.GetServerTrust();
            AccountLinkSnapshot account = _engine.GetAccountLink();
            PollDistressKey(frame, sector);
            UpdateDistressStatus();

            UpdateTargets(frame,sector,trust);
            _spectrum.Update(frame);
            // v0.5.9.1 LAB: gameplay receive is intentionally not gated by
            // AccountLink/DX state. The server lab gateway still maps this
            // device ID to its faction for test-data separation.
            if (_fleet != null && _settings.ReceiveFleetLink)
                _fleet.Update(frame, sector.Id, sector.Name, _engine.GetLocalFactionTag(),
                    trust.ServerId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                    (MyAPIGateway.Session?.Player?.IdentityId ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
            else if (_fleet != null)
                _fleet.ClearForTrustGate("RX OFF");

            _distressGps.Update(_fleet, _settings.ReceiveFleetLink, _engine.GetLocalFactionTag());

            int signalCadence = !_settings.AdaptiveTacticalRate || _spectrumCache.Count < 48 ? 3 : _spectrumCache.Count < 96 ? 6 : 10;
            if (frame < _lastSpectrumFrame || frame - _lastSpectrumFrame >= signalCadence)
            {
                _lastSpectrumFrame = frame;
                long sampleStart=System.Diagnostics.Stopwatch.GetTimestamp();
                RefreshSpectrumCache(frame);
                _performance.Record(0,sampleStart);
            }

            // 30 Hz overlay packets at a nominal 60 Hz simulation update. The overlay
            // always renders the newest packet, so this stays smooth without burdening
            // the game thread with a full screen render operation.
            bool editingLayout=ZeoHudLayoutSession.Current!=null;
            bool layoutRefresh=(editingLayout && (DateTime.UtcNow-_lastLayoutPublishUtc).TotalMilliseconds>=33) ||
                editingLayout!=_wasEditingLayout;
            if (frame < _lastOverlayFrame || frame - _lastOverlayFrame >= 2 || layoutRefresh)
            {
                _lastOverlayFrame = frame;
                _lastLayoutPublishUtc=DateTime.UtcNow;
                _wasEditingLayout=editingLayout;
                RenderToOverlay(frame);
            }
            else if (_settings.FastCameraMarkers)
            {
                PublishCameraMarkers();
            }

            string performance = _performance.Flush();
            if(performance!=null) Plugin.Log(performance);
            if (!_readyLogged && _overlay.Running)
            {
                _readyLogged = true;
                Plugin.Log("ZeoOverlay detected; capture-safe HUD renderer active. menu=" + _settings.MenuKey.ToString().ToUpperInvariant());
            }
        }

        private void PollGameMenuKey(int frame)
        {
            try
            {
                if (MyAPIGateway.Input == null) return;
                MyKeys key=(MyKeys)ZeoOverlay.MenuBinding.Resolve((int)_settings.MenuKey,_settings.MenuKeyCode);
                if(key==MyKeys.None || !MyAPIGateway.Input.IsNewKeyPressed(key))return;
                if(ZeoMenuBindingScreen.IsOpen || !GameWindowState.Capture().Focused)return;
                var focus=Sandbox.Graphics.GUI.MyScreenManager.GetScreenWithFocus();
                bool ownMenu=focus is ZeoNativeSettingsScreen;
                if(!ownMenu && !(focus is Sandbox.Game.Gui.MyGuiScreenGamePlay))return;
                if(MyAPIGateway.Gui==null || MyAPIGateway.Gui.ChatEntryVisible)return;
                if(ownMenu && ((ZeoNativeSettingsScreen)focus).IsEditingTextOrBinding)return;
                if (frame >= _lastMenuKeyFrame && frame - _lastMenuKeyFrame < 8) return;
                _lastMenuKeyFrame = frame;
                OpenMenu();
                Plugin.Log("Game-input menu key -> " + key + " // native SE settings path");
            }
            catch (Exception ex)
            {
                Plugin.Log("Game-input menu key ERROR: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void PollQuickRefillKey()
        {
            try
            {
                var input=MyAPIGateway.Input;var gui=MyAPIGateway.Gui;
                int key=_settings.QuickRefillKey,modifier=_settings.QuickRefillModifier;
                bool down=input!=null && key!=0 && input.IsKeyPress((MyKeys)key);
                bool match=input!=null && ZeoOverlay.QuickRefillBinding.MatchModifiers(modifier,
                    input.IsAnyCtrlKeyPressed(),input.IsAnyAltKeyPressed(),input.IsAnyShiftKeyPressed());
                bool allowed=key!=0 && gui!=null && !gui.ChatEntryVisible && !gui.IsCursorVisible &&
                    Sandbox.Graphics.GUI.MyScreenManager.GetScreenWithFocus() is Sandbox.Game.Gui.MyGuiScreenGamePlay &&
                    GameWindowState.Capture().Focused;
                bool conflict=ZeoOverlay.QuickRefillBinding.Conflict(key,(int)_settings.MenuKey,_settings.DistressEnabled,(int)_settings.DistressKey,_settings.MenuKeyCode)!=null;
                if(_refillKey.Poll(key,modifier,down,match,allowed,conflict))
                {
                    Plugin.ToggleRefill();
                    Plugin.Notify(Plugin.RefillStatus,5000);
                }
            }
            catch(Exception ex){Plugin.Log("Quick Refill key: "+ex.Message);}
        }

        private string _sosNotification;
        private long _sosNotificationExpiresMs;
        private void NotifyDistress(string message,int duration,string color)
        {
            _sosNotification=message;
            _sosNotificationExpiresMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+Math.Max(3500,duration);
            // Keep a native fallback when the external HUD is unavailable or disabled.
            if(!_settings.HudEnabled || !_settings.ShowDistressBanner || !_overlay.Running)
                Plugin.Notify(message,duration,color);
        }

        private void PollDistressKey(int frame, SectorSnapshot sector)
        {
            try
            {
                if (!_settings.DistressEnabled || MyAPIGateway.Input == null || ZeoMenuBindingScreen.IsOpen || !(Sandbox.Graphics.GUI.MyScreenManager.GetScreenWithFocus() is Sandbox.Game.Gui.MyGuiScreenGamePlay)) { _distressHoldStartFrame=-1; _distressTriggeredThisHold=false; return; }
                MyKeys key = DistressKeyToMyKey(_settings.DistressKey);
                bool down = MyAPIGateway.Input.IsKeyPress(key);
                if (!down)
                {
                    _distressHoldStartFrame=-1;
                    _distressTriggeredThisHold=false;
                    return;
                }
                if (_distressHoldStartFrame < 0) { _distressHoldStartFrame=frame; return; }
                if (_distressTriggeredThisHold) return;
                int needFrames=(int)Math.Ceiling(Math.Max(.5,_settings.DistressHoldSeconds)*60.0);
                if (frame < _distressHoldStartFrame || frame-_distressHoldStartFrame < needFrames) return;
                _distressTriggeredThisHold=true;

                LocalHudSnapshot local=_engine.GetHudSnapshot();
                if (!_engine.HasGameFaction)
                {
                    _distressStatus = "LOCAL ONLY - no game faction";
                    NotifyDistress("ZEO DISTRESS // NO GAME FACTION", 3500, "White");
                    return;
                }
                if (!local.HasShip)
                {
                    NotifyDistress("ZEO DISTRESS // NO CONTROLLED SHIP",3000,"Red");
                    _distressStatus="NO CONTROLLED SHIP";
                    return;
                }
                bool clear = _localDistressActive && (_localDistressExpiresMs <= 0 || _localDistressExpiresMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                SendDistress(clear ? "clear" : "activate", local, sector);
            }
            catch(Exception ex)
            {
                _distressStatus="INPUT ERROR";
                Plugin.Log("Distress hotkey ERROR: "+ex.GetType().Name+": "+ex.Message);
            }
        }

        private void SendDistress(string action, LocalHudSnapshot local, SectorSnapshot sector)
        {
            // v0.5.9.1 LAB: no client-side account/DX authorization gate for SOS.
            // This build is specifically for proving the distress transport/UI.
            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool activate=string.Equals(action,"activate",StringComparison.OrdinalIgnoreCase);
            int ttlSeconds=Math.Max(60,_settings.DistressTtlMinutes*60);
            var payload=new
            {
                action=activate ? "activate" : "clear",
                type=_settings.DistressType.ToString(),
                visibility=_settings.DistressVisibility.ToString(),
                sourceId=local.OwnGridId,
                shipId=local.OwnGridId,
                name=local.OwnGridName ?? "SHIP",
                world="default",
                sectorId=sector.Id ?? "unknown",
                sectorName=sector.Name ?? "UNKNOWN SECTOR",
                position=new[]{local.OwnPosition.X,local.OwnPosition.Y,local.OwnPosition.Z},
                velocity=new[]{local.OwnVelocity.X,local.OwnVelocity.Y,local.OwnVelocity.Z},
                shipHp=local.ShipHp,
                driveHealth=local.DriveHealth,
                powerLoad=local.PowerMax>0 ? local.PowerCurrent/local.PowerMax : -1,
                captureMs=now,
                ttlSeconds=ttlSeconds,
                expiresMs=activate ? now + ttlSeconds*1000L : now,
                clientVersion=Plugin.Version,
                factionTag=_engine.GetLocalFactionTag(),
                accessMode="open_test_no_auth"
            };

            if (_distress == null)
            {
                _distressStatus="SERVER NOT READY";
                NotifyDistress("ZEO DISTRESS // SERVER ENDPOINT NOT READY",4000,"Red");
                return;
            }
            if (!_distress.TrySend(payload))
            {
                _distressStatus=_distress.Busy ? "DISTRESS SEND BUSY" : "DISTRESS SEND FAILED";
                NotifyDistress("ZEO DISTRESS // SEND BUSY",2500,"Red");
                return;
            }

            _pendingDistressAction=activate ? "activate" : "clear";
            _pendingDistressExpiresMs=activate ? now + ttlSeconds*1000L : 0;
            _distressStatus=activate ? "SOS SENDING..." : "CLEARING SOS...";
            NotifyDistress(activate ? "ZEO DISTRESS // SENDING SOS" : "ZEO DISTRESS // CLEARING",2500,activate ? "Red" : "White");
            Plugin.Log("Distress "+(activate?"ACTIVATE":"CLEAR")+" requested sector="+(sector.Id??"unknown")+" ttl="+ttlSeconds+"s");
        }

        private void UpdateDistressStatus()
        {
            // v0.5.9.1 LAB: report the actual SOS transport state instead of
            // overwriting it with account/DX context while testing.
            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_localDistressActive && _localDistressExpiresMs > 0 && now >= _localDistressExpiresMs)
            {
                _localDistressActive=false;
                _localDistressExpiresMs=0;
            }
            if (_distress == null) return;
            int status=_distress.LastStatus;
            if (!string.IsNullOrWhiteSpace(_pendingDistressAction))
            {
                if (status >= 200 && status < 300)
                {
                    bool activated=string.Equals(_pendingDistressAction,"activate",StringComparison.OrdinalIgnoreCase);
                    _localDistressActive=activated;
                    _localDistressExpiresMs=activated ? _pendingDistressExpiresMs : 0;
                    _pendingDistressAction="";
                    _pendingDistressExpiresMs=0;
                    _distressStatus=activated ? "ACTIVE // SERVER CONFIRMED" : "CLEARED // SERVER CONFIRMED";
                    NotifyDistress(activated ? "ZEO DISTRESS // SOS CONFIRMED" : "ZEO DISTRESS // CLEAR CONFIRMED",3500,activated ? "Red" : "Green");
                }
                else if (status > 0 || (!_distress.Busy && !string.Equals(_distress.LastError,"sending",StringComparison.OrdinalIgnoreCase) && !string.Equals(_distress.LastError,"waiting",StringComparison.OrdinalIgnoreCase)))
                {
                    string failed=_pendingDistressAction;
                    _pendingDistressAction="";
                    _pendingDistressExpiresMs=0;
                    _distressStatus=status > 0 ? "SERVER HTTP "+status : "SEND ERROR // "+_distress.LastError;
                    NotifyDistress("ZEO DISTRESS // "+failed.ToUpperInvariant()+" FAILED",3500,"Red");
                }
                return;
            }
            if (status >= 200 && status < 300)
                _distressStatus=_localDistressActive ? "ACTIVE // SERVER CONFIRMED" : "SERVER READY";
            else if (status > 0)
                _distressStatus="SERVER HTTP "+status;
        }

        private static MyKeys DistressKeyToMyKey(HudDistressKey key)
        {
            switch(key)
            {
                case HudDistressKey.F5: return MyKeys.F5;
                case HudDistressKey.F7: return MyKeys.F7;
                case HudDistressKey.F8: return MyKeys.F8;
                case HudDistressKey.F9: return MyKeys.F9;
                case HudDistressKey.F10: return MyKeys.F10;
                case HudDistressKey.F11: return MyKeys.F11;
                case HudDistressKey.F12: return MyKeys.F12;
                default: return MyKeys.F6;
            }
        }

        public void OpenMenu()
        {
            // V1.4a: normal configuration is a real Space Engineers GUI screen.
            // If the native GUI cannot be created on a specific game build, fall
            // back to the proven external capture-safe settings window.
            bool nativeOpened = ZeoNativeSettingsUi.Toggle(
                _settings,
                delegate
                {
                    _overlay.EnsureRunning(true);
                    _overlay.OpenMenu();
                },
                delegate
                {
                    // Native edits use the existing overlay settings model. Reload
                    // and apply the same runtime effects as external settings edits.
                    _settings.ReloadIfChanged();
                    ApplyRuntimePrivacy();
                },
                delegate { _overlay.EnsureRunning(true); });

            if (!nativeOpened)
            {
                _overlay.EnsureRunning(true);
                _overlay.OpenMenu();
                Plugin.Log("Native settings unavailable; external menu fallback opened.");
            }
        }

        private void ApplyRuntimePrivacy()
        {
            if (_lastTx != _settings.TransmitTelemetry)
            {
                _lastTx = _settings.TransmitTelemetry;
                _engine.SetTelemetryExportEnabled(_settings.TransmitTelemetry);
            }
            if (_lastWritePayload != _settings.WriteLocalPayload)
            {
                _lastWritePayload = _settings.WriteLocalPayload;
                _engine.SetLocalPayloadLoggingEnabled(_settings.WriteLocalPayload);
            }
            _engine.SetTacticalSharing(_settings.ShareWeaponCoreContacts, _settings.ShareSpectrumSignals);
            _engine.SetHudPerformanceOptions(_settings.SuppressSubgridClutter, _settings.AdaptiveTacticalRate, _settings.TacticalProcessingCap);
        }

        private void RefreshSpectrumCache(int frame)
        {
            _spectrumCache.Clear();

            bool needSpectrum = _settings.ShowLocalSpectrum || _settings.ShareSpectrumSignals;
            if (!needSpectrum)
            {
                _spectrumPersistent.Clear(); _spectrumByGrid.Clear();
                _engine.SetSharedSpectrumDetections(null);
                return;
            }

            List<SpectrumClient.DetectionData> fresh = _spectrum.Read();
            if (!_spectrum.LastReadSucceeded)
            {
                foreach (var entry in _spectrumPersistent.Values)
                    if (SpectrumMotion.Age(frame, entry.Detection.DetectedAt) <= 15)
                        _spectrumCache.Add(entry.Detection);
                return;
            }
            ApplySpectrumSnapshot(fresh, frame);
        }

        private void ApplySpectrumSnapshot(List<SpectrumClient.DetectionData> fresh, int frame)
        {
            _spectrumCache.Clear();
            // Spectrum already retains fading signals and removes retired emitter IDs.
            // Its successful snapshot is authoritative; do not add another ghost hold.
            var active = new HashSet<long>(fresh.Select(d => d.EmitterId));
            foreach (long id in _spectrumPersistent.Keys.Where(id => !active.Contains(id)).ToArray())
                _spectrumPersistent.Remove(id);
            _engine.SetSharedSpectrumDetections(_settings.ShareSpectrumSignals ? fresh : null);

            if (!_settings.ShowLocalSpectrum)
            {
                _spectrumPersistent.Clear(); _spectrumByGrid.Clear();
                return;
            }

            _spectrumByGrid.Clear();
            for (int i = 0; i < fresh.Count; i++)
            {
                var d = fresh[i];
                SpectrumSeen seen;
                if (!_spectrumPersistent.TryGetValue(d.EmitterId, out seen))
                {
                    seen = new SpectrumSeen();
                    _spectrumPersistent[d.EmitterId] = seen;
                }
                if (seen.LastSeenFrame != 0 && d.DetectedAt != seen.Detection.DetectedAt)
                    seen.Acceleration = SpectrumMotion.Acceleration(seen.Detection.Velocity,
                        seen.Detection.DetectedAt, d.Velocity, d.DetectedAt);
                seen.Detection = d;
                seen.LastSeenFrame = frame;
                long gridId=d.EmitterId;NormalizeSpectrumGridIdentity(d.EmitterId,ref gridId);
                SpectrumSeen existingSignal;
                if(!_spectrumByGrid.TryGetValue(gridId,out existingSignal)||existingSignal.Detection.DetectedAt<=d.DetectedAt)
                    _spectrumByGrid[gridId]=seen;
            }

            int keepFrames = Math.Max(120, _settings.LastKnownSeconds * 60);
            var remove = new List<long>();
            foreach (var pair in _spectrumPersistent)
            {
                if (frame < pair.Value.LastSeenFrame || frame - pair.Value.LastSeenFrame > keepFrames)
                    remove.Add(pair.Key);
                else
                    _spectrumCache.Add(pair.Value.Detection);
            }
            for (int i = 0; i < remove.Count; i++) _spectrumPersistent.Remove(remove[i]);
        }

        private double SpectrumAgeSeconds(long emitterId, int frame)
        {
            SpectrumSeen seen;
            if (!_spectrumPersistent.TryGetValue(emitterId, out seen)) return 0;
            if (frame < seen.LastSeenFrame) return 0;
            return SpectrumMotion.Age(frame, seen.Detection.DetectedAt);
        }

        // ZEOCORE_V067H3_DIRECT_HUD_SYNC
        // IMySession.Config can allocate, so sample it at 10 Hz rather than on
        // every 30 Hz overlay packet. This still follows TAB within about 100 ms.
        private int ReadGameHudState(int frame)
        {
            if (!_settings.FollowGameHud)
            {
                _gameHudState = 1;
                _lastGameHudStateFrame = frame;
                return _gameHudState;
            }

            if (frame < _lastGameHudStateFrame || frame - _lastGameHudStateFrame >= 6)
            {
                _lastGameHudStateFrame = frame;
                try
                {
                    var session = MyAPIGateway.Session;
                    var config = session == null ? null : session.Config;
                    if (config == null)
                    {
                        _gameHudState = 1;
                    }
                    else
                    {
                        int state = config.HudState;
                        _gameHudState = state >= 0 && state <= 2 ? state : 1;
                    }
                }
                catch
                {
                    // Fail visible rather than unexpectedly hiding the player's HUD.
                    _gameHudState = 1;
                }
            }

            return _gameHudState;
        }

        private void RenderToOverlay(int frame)
        {
            LocalHudSnapshot local = _engine.GetHudSnapshot();
            FleetPictureSnapshot fleet = _fleet != null && _settings.ReceiveFleetLink ? _fleet.Snapshot(_settings.StaleSeconds, _settings.LastKnownSeconds) : new FleetPictureSnapshot();

            int rawTargetCount = local.WcRawThreatCount + _spectrumCache.Count +
                                 (fleet == null ? 0 : fleet.Contacts.Count + fleet.Friendlies.Count);
            int fusionCadence = 6; // 10 Hz at nominal 60 Hz sim.
            if (_settings.AdaptiveTacticalRate)
            {
                if (rawTargetCount >= 96) fusionCadence = 15;      // ~4 Hz under very heavy load.
                else if (rawTargetCount >= 48) fusionCadence = 10; // ~6 Hz under moderate load.
            }

            if (frame < _lastTrackBuildFrame || frame - _lastTrackBuildFrame >= fusionCadence)
            {
                _lastTrackBuildFrame = frame;
                _trackPerfClock.Restart();
                BuildTracks(frame, local, fleet);
                _trackPerfClock.Stop();
                _performance.RecordMilliseconds(1,_trackPerfClock.Elapsed.TotalMilliseconds);
                _lastTrackBuildUtc = DateTime.UtcNow;

                if (_trackPerfClock.Elapsed.TotalMilliseconds >= 6.0 &&
                    (frame < _lastPerfLogFrame || frame - _lastPerfLogFrame >= 300))
                {
                    _lastPerfLogFrame = frame;
                    Plugin.Log("PERF tactical fusion " + _trackPerfClock.Elapsed.TotalMilliseconds.ToString("0.0") +
                               "ms raw=" + rawTargetCount + " fused=" + _tracks.Count +
                               " cadence=" + fusionCadence + "f cap=" + _settings.TacticalProcessingCap);
                }
            }

            double renderPredictionAge = 0;
            if (_settings.PredictTrackMotion && _lastTrackBuildUtc != DateTime.MinValue)
                renderPredictionAge = PredictionAge(Math.Max(0, (DateTime.UtcNow - _lastTrackBuildUtc).TotalSeconds));

            GameWindowSnapshot gameWindow = GameWindowState.Capture();

            ServerTrustSnapshot trust = _engine.GetServerTrust();
            AccountLinkSnapshot account = _engine.GetAccountLink();
            var output = new OverlayFrame
            {
                Version = Plugin.Version,
                Layout = ZeoHudLayoutSession.Current,
                Sequence = ++_overlaySequence,
                UtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                HudEnabled = _settings.HudEnabled,
                HasShip = local.HasShip,
                ObserverTosEnabled = !local.HasShip && local.HasObserver && _settings.KeepTosOutsideShip,
                GameWindowValid = gameWindow.Valid,
                GameLeft = gameWindow.Left,
                GameTop = gameWindow.Top,
                GameWidth = gameWindow.Width,
                GameHeight = gameWindow.Height,
                GameFocused = gameWindow.Focused,
                GameHudState = ReadGameHudState(frame),
                GameWindowHandle = gameWindow.WindowHandle,
                GameProcessId = gameWindow.ProcessId,
                OwnGridName = local.OwnGridName ?? "",
                SectorId = local.SectorId ?? "unknown",
                SectorName = local.SectorName ?? "UNKNOWN SECTOR",
                SectorKnown = local.SectorKnown,
                ServerTrustState = trust.State ?? "OFFLINE",
                ServerTrustSector = trust.Sector ?? "UNKNOWN",
                ServerTrustDetail = trust.Detail ?? "",
                ServerExpectedEndpoint = trust.ExpectedEndpoint ?? "",
                ServerObservedEndpoint = trust.ObservedEndpoint ?? "",
                ServerIdentity = trust.ServerId == 0 ? "" : trust.ServerId.ToString(),
                ServerIdentityVerified = trust.IdentityVerified,
                ServerEndpointVerified = trust.EndpointVerified,
                ServerNetworkAllowed = trust.NetworkAllowed,
                AuthState = account.State ?? "AUTH OFF",
                AuthDetail = account.Detail ?? "",
                AuthLinked = account.Linked,
                AuthAuthorized = true, // v0.5.9.1 LAB: do not present account state as a gameplay gate
                AuthPairingCode = account.PairingCode ?? "",
                AuthUsername = account.Username ?? "",
                AuthFactionTag = account.FactionTag ?? "",
                AuthAssignedScope = account.AssignedScope ?? "self",
                AuthEffectiveScope = account.EffectiveScope ?? "self",
                Speed = local.OwnVelocity.Length(),
                H2O = local.H2O,
                O2 = local.O2,
                FusionPellets = local.FusionPellets,
                DriveHealth = local.DriveHealth,
                ReactorHealth = local.ReactorHealth,
                PowerCurrent = local.PowerCurrent,
                PowerMax = local.PowerMax,
                ShipHp = local.ShipHp,
                ShowCrosshair = _settings.ShowCrosshair,
                ShowFlightData = _settings.ShowFlightData,
                ShowTrackPanel = _settings.ShowTrackPanel,
                ShowLinkPanel = _settings.ShowLinkPanel,
                TxOn = _engine.TelemetryExportActive,
                RxOn = _settings.ReceiveFleetLink,
                RxLinked = _settings.ReceiveFleetLink && _fleet != null && _fleet.Online,
                RxAgeSeconds = fleet.ReceivedUtcMs > 0
                    ? Math.Max(0, (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - fleet.ReceivedUtcMs) / 1000.0)
                    : 0,
                FleetFriendlyCount = fleet.Friendlies.Count,
                FleetContactCount = fleet.Contacts.Count,
                FleetSectorAware = fleet.SectorAware,
                ShowRosterPanel = _settings.ShowRosterPanel,
                DistressLocalActive = _localDistressActive,
                DistressServerReady = _distress != null && _distress.LastStatus >= 200 && _distress.LastStatus < 300,
                DistressStatus = _distressStatus,
                SosNotification = _sosNotification,
                SosNotificationExpiresMs = _sosNotificationExpiresMs,
                ActiveDistressCount = fleet.Distress.Count
            };

            BuildRosterRows(output, local, fleet);
            BuildDistressAlerts(output, local, fleet);

            bool scopeActive = local.HasShip || (_settings.KeepTosOutsideShip && local.HasObserver);
            var allContacts = scopeActive ? _tracks.Where(t => !t.Friendly || t.IsDistress).ToList() : new List<HudTrack>();
            output.TotalScopeCount = allContacts.Count;
            List<HudTrack> scope = SortScope(allContacts);
            int scopeRows = Math.Min(_settings.ScopeRows, scope.Count);
            for (int i = 0; i < scopeRows; i++)
            {
                HudTrack t = scope[i];
                output.ScopeRows.Add(new OverlayScopeRow
                {
                    TrackId = t.TrackId,
                    Source = (int)t.Source,
                    Friendly = t.Friendly,
                    Focused = t.Focused,
                    Stale = t.Stale || t.AgeSeconds > _settings.StaleSeconds,
                    Relation = t.Relation ?? "",
                    Name = ScopeCore(t),
                    Distance = t.Distance,
                    Speed = t.Velocity.Length(),
                    Closing = t.Closing,
                    AgeSeconds = t.AgeSeconds,
                    Distress = t.IsDistress,
                    DistressType = t.DistressType ?? "",
                    DistressSecondsRemaining = t.DistressSecondsRemaining
                });
            }

            if (_settings.HudEnabled && local.HasShip && MyAPIGateway.Session != null && MyAPIGateway.Session.Camera != null)
            {
                IMyCamera camera = MyAPIGateway.Session.Camera;
                var friendlies = _tracks.Where(t => t.Friendly && (!t.IsDistress || _settings.ShowDistressWorldMarkers))
                    .OrderByDescending(t => t.Priority).ThenBy(t => t.Distance)
                    .Take(_settings.MaxFriendlyMarkers).ToList();
                var contacts = allContacts.Where(t => !t.IsDistress || _settings.ShowDistressWorldMarkers)
                    .OrderByDescending(t => t.Priority).ThenBy(t => t.Distance)
                    .Take(_settings.MaxContactMarkers).ToList();

                var selected = new List<HudTrack>(contacts.Count + friendlies.Count);
                selected.AddRange(contacts);
                selected.AddRange(friendlies);

                _markerTracks=selected;
                _markerShipActive=true;
                output.Markers=ProjectMarkers(camera,selected,renderPredictionAge);
            }
            else
            {
                _markerTracks.Clear();
                _markerShipActive=false;
            }

            if (local.Ammo != null)
            {
                for (int ai=0; ai<local.Ammo.Count; ai++)
                {
                    var a=local.Ammo[ai];
                    if (_settings.AmmoOnlyRelevant && !a.Relevant) continue;
                    output.AmmoRows.Add(new OverlayAmmoRow { Key=a.Key, CleanName=a.CleanName, ServerName=a.ServerName, Have=a.Have, Want=AmmoWant(a.Key), Relevant=a.Relevant });
                }
            }

            _overlay.SendFrame(output);
        }

        private List<OverlayMarker> ProjectMarkers(IMyCamera camera,List<HudTrack> selected,double renderPredictionAge)
        {
            long markerStart=System.Diagnostics.Stopwatch.GetTimestamp();
            var markers=new List<OverlayMarker>(selected.Count);
                _occupied.Clear();
                for (int i = 0; i < selected.Count; i++)
                {
                    HudTrack t = selected[i];
                    Vector2D screen;
                    bool offscreen;
                    Vector3D renderPosition;
                    if (t.Source == HudTrackSource.Spectrum && _settings.MarkerAnchor != HudMarkerAnchor.GridCenter)
                    {
                        long emitter;
                        SpectrumSeen signal;
                        if (!long.TryParse(t.RawEmitterId, out emitter) || !_spectrumPersistent.TryGetValue(emitter, out signal)) continue;
                        int tick = MyAPIGateway.Session.GameplayFrameCounter;
                        if (!FriendlyTrackPolicy.RenderableSpectrum(SpectrumMotion.Age(tick, signal.Detection.DetectedAt))) continue;
                        renderPosition = SpectrumMotion.Position(signal.Detection.Position, signal.Detection.Velocity,
                            signal.Acceleration, signal.Detection.DetectedAt, tick);
                    }
                    else if(_settings.MarkerAnchor==HudMarkerAnchor.Auto &&
                        FriendlyTrackPolicy.FreshNetworkFriendly(t,_settings.StaleSeconds) && TryFriendlySpectrumPosition(t,out renderPosition)) { }
                    else renderPosition = MarkerPositionResolver.Resolve(t, _settings.MarkerAnchor,
                        renderPredictionAge, _settings.StaleSeconds, LiveMarkerPosition, _nativeSignalPosition);
                    Project(camera, renderPosition, out screen, out offscreen);

                    if (_settings.Declutter && !t.AttackTarget && FriendlyTrackPolicy.CanDeclutter(t) && IsOccupied(screen))
                        continue;
                    _occupied.Add(screen);

                    markers.Add(new OverlayMarker
                    {
                        AttackTarget = t.AttackTarget,
                        TrackId = t.TrackId,
                        Source = (int)t.Source,
                        Friendly = t.Friendly,
                        Focused = t.Focused,
                        Stale = t.Stale || t.AgeSeconds > _settings.StaleSeconds,
                        Relation = t.Relation ?? "",
                        Name = t.Name ?? "",
                        X = screen.X,
                        Y = screen.Y,
                        Offscreen = offscreen,
                        Distance = t.Distance,
                        Closing = t.Closing,
                        AgeSeconds = t.AgeSeconds,
                        Priority = t.Priority,
                        Distress = t.IsDistress,
                        DistressType = t.DistressType ?? "",
                        DistressSecondsRemaining = t.DistressSecondsRemaining
                    });
                }
            _performance.Record(2,markerStart);
            return markers;
        }

        private bool TryFriendlySpectrumPosition(HudTrack track,out Vector3D position)
        {
            position=Vector3D.Zero;long emitter;SpectrumSeen signal;
            if(!long.TryParse(track.RawEmitterId,out emitter)||!_spectrumPersistent.TryGetValue(emitter,out signal))return false;
            int tick=MyAPIGateway.Session.GameplayFrameCounter;
            if(!FriendlyTrackPolicy.RenderableSpectrum(SpectrumMotion.Age(tick,signal.Detection.DetectedAt)))return false;
            position=SpectrumMotion.Position(signal.Detection.Position,signal.Detection.Velocity,signal.Acceleration,signal.Detection.DetectedAt,tick);
            return !(double.IsNaN(position.X)||double.IsNaN(position.Y)||double.IsNaN(position.Z)||double.IsInfinity(position.X)||double.IsInfinity(position.Y)||double.IsInfinity(position.Z));
        }

        private Vector3D? ReadNativeSignalPosition(long gridId)
        {
            SpectrumSeen signal;
            if(!_spectrumByGrid.TryGetValue(gridId,out signal))return null;
            int tick=MyAPIGateway.Session.GameplayFrameCounter;
            if(SpectrumMotion.Age(tick,signal.Detection.DetectedAt)>15)return null;
            return SpectrumMotion.Position(signal.Detection.Position,signal.Detection.Velocity,signal.Acceleration,signal.Detection.DetectedAt,tick);
        }
        private static readonly Func<long, Vector3D?> LiveMarkerPosition = ReadLiveMarkerPosition;

        private static Vector3D? ReadLiveMarkerPosition(long entityId)
        {
            if (MyAPIGateway.Entities == null) return null;
            try
            {
                IMyEntity entity;
                if (!MyAPIGateway.Entities.TryGetEntityById(entityId, out entity) || entity == null || entity.Closed) return null;
                IMyCubeGrid grid = entity as IMyCubeGrid;
                var block = entity as IMyCubeBlock;
                if (grid == null && block != null) grid = block.CubeGrid;
                if (grid == null || grid.Closed) return null;
                return grid.WorldAABB.Center;
            }
            catch { return null; }
        }

        private void PublishCameraMarkers()
        {
            if (!_settings.HudEnabled || !_markerShipActive || _markerTracks.Count == 0 || MyAPIGateway.Session == null || MyAPIGateway.Session.Camera == null) return;
            double age=_lastTrackBuildUtc==DateTime.MinValue ? 0 : PredictionAge(Math.Max(0,(DateTime.UtcNow-_lastTrackBuildUtc).TotalSeconds));
            _overlay.SendMarkers(new OverlayMarkerUpdate {
                Sequence=++_overlaySequence,
                UtcMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Markers=ProjectMarkers(MyAPIGateway.Session.Camera,_markerTracks,age)
            });
        }

        private double AmmoWant(string key)
        {
            switch (key ?? "")
            {
                case "PDC40": return _settings.WantPdc40;
                case "PDC40IMP": return _settings.WantPdc40Improvised;
                case "PDC50": return _settings.WantPdc50;
                case "SABOT80": return _settings.WantSabot80;
                case "SABOT80IMP": return _settings.WantSabot80Improvised;
                case "SABOT100": return _settings.WantSabot100;
                case "TORP160": return _settings.WantTorp160;
                case "TORP190": return _settings.WantTorp190;
                case "TORP220": return _settings.WantTorp220;
                default: return 0;
            }
        }

        private readonly OwnShipTrackFilter _ownShipTracks=new OwnShipTrackFilter();
        private readonly List<IMyCubeGrid> _ownMechanicalGrids=new List<IMyCubeGrid>(16);
        private long _ownFilterGrid;
        private int _ownFilterFrame=-120;
        private static readonly Func<long,long> ResolveMarkerGrid=ResolveMarkerGridId;
        private static long ResolveMarkerGridId(long id){long grid=id;NormalizeSpectrumGridIdentity(id,ref grid);return grid;}
        private bool IsOwnShipTrack(long id){return _ownShipTracks.Contains(id,ResolveMarkerGrid);}
        private void RefreshOwnShipFilter(int frame,LocalHudSnapshot local)
        {
            long own=local.HasShip?local.OwnGridId:0;
            if(own==_ownFilterGrid&&frame>=_ownFilterFrame&&frame-_ownFilterFrame<30)return;
            _ownFilterGrid=own;_ownFilterFrame=frame;_ownShipTracks.Reset(own);_ownMechanicalGrids.Clear();
            if(own==0)return;
            try {
                IMyEntity entity;
                if(!MyAPIGateway.Entities.TryGetEntityById(own,out entity))return;
                var grid=entity as IMyCubeGrid;if(grid==null)return;
                MyAPIGateway.GridGroups.GetGroup(grid,GridLinkTypeEnum.Mechanical,_ownMechanicalGrids);
                for(int i=0;i<_ownMechanicalGrids.Count;i++)if(_ownMechanicalGrids[i]!=null)_ownShipTracks.AddGrid(_ownMechanicalGrids[i].EntityId);
            } catch { } // Exact own-grid ID remains protected if group lookup is unavailable.
        }

        private long? ReadCachedTrackIdentity(long id)
        {
            long? result;if(!_liveTrackIdentities.TryGetValue(id,out result)){
                result=_engine.TryResolveHudTrackIdentity(id);_liveTrackIdentities[id]=result;
            }
            return result;
        }
        private long ResolveCachedTrackIdentity(long id){return ReadCachedTrackIdentity(id)??id;}
        private void BuildTracks(int frame, LocalHudSnapshot local, FleetPictureSnapshot fleet)
        {
            _liveTrackIdentities.Clear();
            RefreshOwnShipFilter(frame,local);
            _tracks.Clear();
            _trackByEntity.Clear();
            _fusionBuckets.Clear();
            int processCap = Math.Max(24, Math.Min(192, _settings.TacticalProcessingCap));

            // v0.4.3: projection is rendered at 30 Hz while WC/Spectrum/Fleet samples
            // arrive less often. Advance the last known position by velocity between
            // samples so the reticle stays attached to a moving ship instead of
            // visibly stepping/trailing it. Prediction is short and capped.
            double localAge = local.CapturedUtc == default(DateTime) ? 0 :
                Math.Max(0, (DateTime.UtcNow - local.CapturedUtc).TotalSeconds);
            localAge = PredictionAge(localAge);
            bool observerMode = !local.HasShip && _settings.KeepTosOutsideShip && local.HasObserver;
            bool haveOrigin = local.HasShip || observerMode;
            Vector3D originVelocity = local.HasShip ? local.OwnVelocity : local.ObserverVelocity;
            Vector3D originNow = (local.HasShip ? local.OwnPosition : local.ObserverPosition) + originVelocity * localAge;

            if (local.HasShip && _settings.ShowLocalWeaponCore)
            {
                for (int i = 0; i < local.WeaponCoreTracks.Count && i < processCap; i++)
                {
                    HudTrack t = local.WeaponCoreTracks[i].Clone();
                    if (IsOwnShipTrack(t.EntityId)) continue;
                    t.Key = "W:" + t.EntityId;
                    PredictPosition(t, localAge);
                    AddKinematics(t, originNow, originVelocity);
                    if (Allowed(t)) AddTrack(t);

                }
            }

            if (haveOrigin && _settings.ShowFleetFriendlies)
            {
                for (int i = 0; i < fleet.Friendlies.Count && i < processCap; i++)
                {
                    HudTrack t = fleet.Friendlies[i].Clone();
                    if (IsOwnShipTrack(t.EntityId)) continue;
                    t.Friendly = true;
                    t.Relation = "friendly";
                    ResolveFleetSector(t, local);
                    if (!CanProjectFleetTrack(t)) continue;
                    t.Key = "F:" + t.EntityId;
                    PredictPosition(t, PredictionAge(t.AgeSeconds));
                    AddKinematics(t, originNow, originVelocity);
                    HudTrack existing = FindExistingByEntityId(t.EntityId);
                    if (existing != null)
                    {
                        if(FriendlyTrackPolicy.FreshNetworkFriendly(t,_settings.StaleSeconds)){
                            MergeTrackKnowledge(t,existing);if(Allowed(t))ReplaceTrack(existing,t);
                        }else MergeTrackKnowledge(existing,t);
                        continue;
                    }
                    if (Allowed(t)) AddTrack(t);

                }
            }

            if (haveOrigin && (_settings.ShowSharedContacts || _settings.ShowSharedSignals))
            {
                for (int i = 0; i < fleet.Contacts.Count && i < processCap; i++)
                {
                    HudTrack t = fleet.Contacts[i].Clone();
                    if(IsOwnShipTrack(t.EntityId))continue;
                    bool sharedSignal = t.Source == HudTrackSource.FleetSignal ||
                                        string.Equals(t.Relation, "signal", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(t.ContactType, "signal", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(t.ContactType, "spectrum", StringComparison.OrdinalIgnoreCase);
                    if (sharedSignal && !_settings.ShowSharedSignals) continue;
                    if (!sharedSignal && !_settings.ShowSharedContacts) continue;
                    if (sharedSignal)
                    {
                        t.Source = HudTrackSource.FleetSignal;
                        if (string.IsNullOrWhiteSpace(t.Relation) ||
                            string.Equals(t.Relation, "signal", StringComparison.OrdinalIgnoreCase))
                            t.Relation = "unknown";
                    }
                    t.Friendly = string.Equals(t.Relation, "friendly", StringComparison.OrdinalIgnoreCase);
                    ResolveFleetSector(t, local);
                    if (!CanProjectFleetTrack(t)) continue;
                    t.Key = (sharedSignal ? "FS:" : "C:") + t.EntityId;
                    PredictPosition(t, PredictionAge(t.AgeSeconds));
                    AddKinematics(t, originNow, originVelocity);

                    HudTrack existing = FindExistingByEntityId(t.EntityId);
                    if (existing == null && sharedSignal)
                        existing = FindFusableExisting(t, 250.0, 45.0);
                    if (existing != null)
                    {
                        MergeTrackKnowledge(existing, t);
                        continue;
                    }

                    if (Allowed(t)) AddTrack(t);

                }
            }

            // Distress is a first-class tactical track. Keep it in the track set
            // for TOS/banner use even when normal world markers are disabled or the
            // player is using observer TOS outside a cockpit.
            if (haveOrigin)
            {
                for (int i=0; i<fleet.Distress.Count; i++)
                {
                    HudTrack t=fleet.Distress[i].Clone();
                    if (IsOwnShipTrack(t.EntityId)) continue;
                    ResolveFleetSector(t,local);
                    if (!CanProjectDistressTrack(t)) continue;
                    t.Key="D:"+t.EntityId;
                    t.Friendly=true;
                    t.Relation="distress";
                    t.IsDistress=true;
                    PredictPosition(t,PredictionAge(t.AgeSeconds));
                    AddKinematics(t,originNow,originVelocity);
                    t.Priority=250000 + Math.Max(0,t.DistressSecondsRemaining);
                    AddTrack(t);
                }
            }

            if (haveOrigin && _settings.ShowLocalSpectrum)
            {
                AccountLinkSnapshot spectrumAccount = _engine.GetAccountLink();
                string spectrumOwnFaction = spectrumAccount == null ? "" : (spectrumAccount.FactionTag ?? "").Trim();
                for (int i = 0; i < _spectrumCache.Count && i < processCap; i++)
                {
                    var d = _spectrumCache[i];
                    if (d.SelfOwned) continue;
                    double nativeAge=SpectrumAgeSeconds(d.EmitterId,frame);
                    if(!FriendlyTrackPolicy.RenderableSpectrum(nativeAge))continue;

                    string ownFaction = spectrumOwnFaction;
                    string detectedFaction = (d.FactionTag ?? "").Trim();
                    bool spectrumFriendly =
                        ownFaction.Length > 0 &&
                        detectedFaction.Length > 0 &&
                        string.Equals(ownFaction, detectedFaction, StringComparison.OrdinalIgnoreCase);

                    long spectrumId = d.EmitterId;
                    Vector3D spectrumPosition = d.Position;
                    NormalizeSpectrumGridIdentity(d.EmitterId, ref spectrumId);
                    if(IsOwnShipTrack(spectrumId))continue;
                    SpectrumSeen signal;
                    if (_spectrumPersistent.TryGetValue(d.EmitterId, out signal))
                        spectrumPosition = SpectrumMotion.Position(d.Position, d.Velocity, signal.Acceleration, d.DetectedAt, frame);

                    HudTrack t = new HudTrack
                    {
                        EntityId = spectrumId,
                        Key = "S:" + d.EmitterId,
                        Name = !string.IsNullOrWhiteSpace(d.DetailText) ? d.DetailText :
                            (!string.IsNullOrWhiteSpace(d.FactionTag) ? d.FactionTag : "SIGNAL"),
                        Relation = spectrumFriendly ? "friendly" : "unknown",
                        Source = HudTrackSource.Spectrum,
                        Position = spectrumPosition,
                        Velocity = d.Velocity,
                        Friendly = spectrumFriendly,
                        AgeSeconds = nativeAge,
                        SignalStrength = double.IsNaN(d.Strength) || double.IsInfinity(d.Strength) ? (double?)null : d.Strength,
                        RawEmitterId = d.EmitterId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    };
                    AddKinematics(t, originNow, originVelocity);

                    HudTrack existing = FindExistingByEntityId(t.EntityId);
                    if (existing == null)
                        existing = FindFusableExisting(t, 250.0, 45.0);
                    if (existing != null)
                    {
                        // ZEOCORE_V13E_SPECTRUM_CANONICAL_SPATIAL
                        // Spectrum remains the visible/canonical tactical identity while
                        // WeaponCore/shared knowledge is merged into it. This uses the
                        // V1.3 dictionary + spatial-bucket resolver and avoids the old
                        // O(N^2) post-pass that could hurt personal sim speed.
                        if (FriendlyTrackPolicy.FreshNetworkFriendly(existing,_settings.StaleSeconds) ||
                            (existing.Source == HudTrackSource.Spectrum && existing.AgeSeconds <= t.AgeSeconds))
                        {
                            MergeTrackKnowledge(existing, t);
                        }
                        else
                        {
                            MergeTrackKnowledge(t, existing);
                            if (Allowed(t)) ReplaceTrack(existing, t);
                        }
                        continue;
                    }
                    if (Allowed(t)) AddTrack(t);
                }
            }

            AddTargetTracks(local,originNow);
            int exactMerged=_exactFusion.Apply(_tracks,_resolveTrackIdentity,MergeKnowledge,_settings.StaleSeconds);
            _exactFusionMerged+=exactMerged;_exactFusionPeak=Math.Max(_exactFusionPeak,exactMerged);
            if(frame<_exactFusionLogFrame||frame-_exactFusionLogFrame>=1800){
                if(_exactFusionMerged>0)Plugin.Log("HUD TRACK FUSION exact-duplicates-removed="+_exactFusionMerged+" peak-per-refresh="+_exactFusionPeak);
                _exactFusionMerged=0;_exactFusionPeak=0;_exactFusionLogFrame=frame;
            }
            SharedTrackPolicy.Apply(_tracks,_settings.MaxSharedTracks,_settings.MaxSharedTrackDistanceKm);
            for (int i = 0; i < _tracks.Count; i++)
            {
                // Preserve WeaponCore focus even when the visible track came from
                // Spectrum/Fleet rather than the local WC threat list.
                if (local.FocusEntityId != 0 && _tracks[i].EntityId == local.FocusEntityId)
                    _tracks[i].Focused = true;
                var identified = _tracks[i];
                if(identified.Friendly)identified.AttackTarget=false;
                string stable = identified.IsDistress ? identified.Key :
                    (identified.EntityId != 0 ? "E:" + identified.EntityId : identified.Key);
                identified.TrackId = _ids.GetForAliases(stable,identified.IdentityAliases);
                identified.Priority = Score(identified);
            }

            _ids.Trim(Math.Max(_settings.LastKnownSeconds + 5, 30));
        }

        private void ResolveFleetSector(HudTrack t, LocalHudSnapshot local)
        {
            if (t == null) return;
            t.SectorKnown = !string.IsNullOrWhiteSpace(t.SectorId) && !string.Equals(t.SectorId, "unknown", StringComparison.OrdinalIgnoreCase);
            t.SameSector = t.SectorKnown && local.SectorKnown && string.Equals(t.SectorId, local.SectorId, StringComparison.Ordinal);
            if (!t.SectorKnown && IsLocallyReplicated(t.EntityId))
                t.SameSector = true;
        }

        private bool CanProjectFleetTrack(HudTrack t)
        {
            return t != null && t.HasPosition && t.SameSector;
        }

        private bool CanProjectDistressTrack(HudTrack t)
        {
            if (t == null || !t.HasPosition) return false;
            if (t.SameSector) return true;
            return _settings.ShowCrossSectorDistress && _settings.ProjectCrossSectorDistress;
        }

        private static bool IsLocallyReplicated(long entityId)
        {
            if (entityId == 0 || MyAPIGateway.Entities == null) return false;
            try
            {
                IMyEntity e;
                return MyAPIGateway.Entities.TryGetEntityById(entityId, out e) && e != null;
            }
            catch { return false; }
        }

        private void BuildRosterRows(OverlayFrame output, LocalHudSnapshot local, FleetPictureSnapshot fleet)
        {
            if (output == null || !_settings.ShowRosterPanel || fleet == null) return;
            var distressById=new Dictionary<long,HudTrack>();
            for(int di=0;di<fleet.Distress.Count;di++) if(fleet.Distress[di]!=null && fleet.Distress[di].EntityId!=0) distressById[fleet.Distress[di].EntityId]=fleet.Distress[di];

            IEnumerable<HudTrack> src = fleet.Roster.Count > 0 ? fleet.Roster : fleet.Friendlies;
            var rows = new List<HudTrack>();
            var seen=new HashSet<long>();
            foreach (HudTrack original in src)
            {
                if (original == null) continue;
                HudTrack t = original.Clone();
                if (IsOwnShipTrack(t.EntityId)) continue;
                ResolveFleetSector(t, local);
                if (!_settings.ShowCrossSectorRoster && !t.SameSector) continue;
                if (t.SameSector && t.HasPosition && (local.HasShip || (_settings.KeepTosOutsideShip && local.HasObserver)))
                    AddKinematics(t, local.HasShip ? local.OwnPosition : local.ObserverPosition, local.HasShip ? local.OwnVelocity : local.ObserverVelocity);
                HudTrack d;
                if(t.EntityId!=0 && distressById.TryGetValue(t.EntityId,out d))
                {
                    t.IsDistress=true; t.DistressType=d.DistressType; t.DistressSecondsRemaining=d.DistressSecondsRemaining;
                }
                rows.Add(t); if(t.EntityId!=0) seen.Add(t.EntityId);
            }
            if(_settings.ShowCrossSectorDistress)
            {
                for(int di=0;di<fleet.Distress.Count;di++)
                {
                    HudTrack d=fleet.Distress[di]; if(d==null || (d.EntityId!=0 && seen.Contains(d.EntityId)) || d.EntityId==local.OwnGridId) continue;
                    HudTrack t=d.Clone(); ResolveFleetSector(t,local); if(t.SameSector && t.HasPosition && (local.HasShip || (_settings.KeepTosOutsideShip && local.HasObserver))) AddKinematics(t,local.HasShip ? local.OwnPosition : local.ObserverPosition,local.HasShip ? local.OwnVelocity : local.ObserverVelocity); rows.Add(t);
                }
            }

            rows = rows.OrderByDescending(r => r.IsDistress)
                       .ThenByDescending(r => r.Online)
                       .ThenByDescending(r => r.SameSector)
                       .ThenBy(r => r.SameSector ? r.Distance : double.MaxValue)
                       .ThenBy(r => r.Name ?? "", StringComparer.OrdinalIgnoreCase)
                       .Take(Math.Max(1, _settings.RosterRows)).ToList();

            for (int i = 0; i < rows.Count; i++)
            {
                HudTrack r = rows[i];
                output.RosterRows.Add(new OverlayRosterRow
                {
                    Name = r.Name ?? "ALLY",
                    SectorId = r.SectorId ?? "unknown",
                    SectorName = string.IsNullOrWhiteSpace(r.SectorName) ? "UNKNOWN SECTOR" : r.SectorName,
                    SectorKnown = r.SectorKnown,
                    SameSector = r.SameSector,
                    Online = r.Online,
                    Distance = r.SameSector && r.HasPosition ? r.Distance : -1,
                    AgeSeconds = r.AgeSeconds,
                    ShipHp = r.ShipHp,
                    DriveHealth = r.DriveHealth,
                    PowerLoad = r.PowerLoad,
                    Distress = r.IsDistress,
                    DistressType = r.DistressType ?? "",
                    DistressSecondsRemaining = r.DistressSecondsRemaining
                });
            }
        }

        private void BuildDistressAlerts(OverlayFrame output, LocalHudSnapshot local, FleetPictureSnapshot fleet)
        {
            if(output==null || fleet==null || !_settings.ShowDistressBanner) return;
            for(int i=0;i<fleet.Distress.Count;i++)
            {
                HudTrack d=fleet.Distress[i]; if(d==null || d.EntityId==local.OwnGridId) continue;
                HudTrack t=d.Clone(); ResolveFleetSector(t,local);
                if(!t.SameSector && !_settings.ShowCrossSectorDistress) continue;
                if(t.HasPosition && (local.HasShip || (_settings.KeepTosOutsideShip && local.HasObserver)) && (t.SameSector || _settings.ProjectCrossSectorDistress))
                    AddKinematics(t,local.HasShip ? local.OwnPosition : local.ObserverPosition,local.HasShip ? local.OwnVelocity : local.ObserverVelocity);
                output.DistressAlerts.Add(new OverlayDistressAlert
                {
                    Name=t.Name ?? "DISTRESS", SectorName=t.SectorName ?? "UNKNOWN SECTOR", SameSector=t.SameSector,
                    Distance=t.HasPosition && (t.SameSector || _settings.ProjectCrossSectorDistress) ? t.Distance : -1, SecondsRemaining=t.DistressSecondsRemaining,
                    Type=t.DistressType ?? "GENERAL SOS", ShipHp=t.ShipHp
                });
            }
        }

        private static void NormalizeSpectrumGridIdentity(long emitterId, ref long entityId)
        {
            if (emitterId == 0 || MyAPIGateway.Entities == null) return;
            try
            {
                IMyEntity entity;
                if (!MyAPIGateway.Entities.TryGetEntityById(emitterId, out entity) || entity == null) return;
                IMyCubeGrid grid = entity as IMyCubeGrid;
                if (grid == null)
                {
                    var block = entity as IMyCubeBlock;
                    if (block != null) grid = block.CubeGrid;
                }
                if (grid == null) return;
                entityId = grid.EntityId;

            }
            catch { }
        }

        private bool Allowed(HudTrack t)
        {
            bool unlimitedFleetFriendly = t.Friendly && t.Source == HudTrackSource.FleetFriendly && t.SameSector && _settings.RemoteFriendlyNoRangeLimit;
            bool unlimitedSharedTactical = !t.Friendly && t.SameSector && _settings.SharedTacticalNoRangeLimit &&
                                            (t.Source == HudTrackSource.FleetContact || t.Source == HudTrackSource.FleetSignal);
            bool unlimitedDistress = t.IsDistress &&
                                     (t.SameSector || (_settings.ShowCrossSectorDistress && _settings.ProjectCrossSectorDistress));
            if (!unlimitedFleetFriendly && !unlimitedSharedTactical && !unlimitedDistress &&
                t.Distance > _settings.MaxRangeKm * 1000.0) return false;
            if (!t.IsDistress && t.AgeSeconds > _settings.LastKnownSeconds) return false;
            if (t.IsDistress && t.DistressSecondsRemaining <= 0 && t.DistressExpiresMs > 0) return false;
            if (t.Friendly) return _settings.ShowFriendlyMarkers;

            string rel = (t.Relation ?? "").ToLowerInvariant();
            if (rel == "hostile") return _settings.ShowHostiles;
            if (rel == "signal") return true;
            return _settings.ShowNeutrals;
        }

        private void AddTrack(HudTrack t)
        {
            if (t == null) return;
            _tracks.Add(t);
            if (t.EntityId != 0 && !_trackByEntity.ContainsKey(t.EntityId))
                _trackByEntity[t.EntityId] = t;

            if (!t.HasPosition) return;
            long key = FusionCellKey(t.Position, 250.0);
            List<HudTrack> bucket;
            if (!_fusionBuckets.TryGetValue(key, out bucket))
            {
                bucket = new List<HudTrack>(4);
                _fusionBuckets[key] = bucket;
            }
            bucket.Add(t);
        }

        private void ReplaceTrack(HudTrack existing, HudTrack replacement)
        {
            if (replacement == null) return;
            if (existing == null) { AddTrack(replacement); return; }

            int index = _tracks.IndexOf(existing);
            if (index >= 0) _tracks[index] = replacement;
            else _tracks.Add(replacement);

            if (existing.EntityId != 0)
            {
                HudTrack mapped;
                if (_trackByEntity.TryGetValue(existing.EntityId, out mapped) && object.ReferenceEquals(mapped, existing))
                    _trackByEntity.Remove(existing.EntityId);
            }
            if (replacement.EntityId != 0)
                _trackByEntity[replacement.EntityId] = replacement;

            if (existing.HasPosition)
            {
                List<HudTrack> oldBucket;
                if (_fusionBuckets.TryGetValue(FusionCellKey(existing.Position, 250.0), out oldBucket))
                    oldBucket.Remove(existing);
            }
            if (replacement.HasPosition)
            {
                long key = FusionCellKey(replacement.Position, 250.0);
                List<HudTrack> bucket;
                if (!_fusionBuckets.TryGetValue(key, out bucket))
                {
                    bucket = new List<HudTrack>(4);
                    _fusionBuckets[key] = bucket;
                }
                bucket.Add(replacement);
            }
        }

        private HudTrack FindExistingByEntityId(long entityId)
        {
            if (entityId == 0) return null;
            HudTrack found;
            return _trackByEntity.TryGetValue(entityId, out found) ? found : null;
        }

        private static long FusionCellKey(Vector3D p, double cell)
        {
            int x = (int)Math.Floor(p.X / cell);
            int y = (int)Math.Floor(p.Y / cell);
            int z = (int)Math.Floor(p.Z / cell);
            unchecked
            {
                return ((long)x * 73856093L) ^ ((long)y * 19349663L) ^ ((long)z * 83492791L);
            }
        }

        private HudTrack FindFusableExisting(HudTrack incoming, double meters, double velocity)
        {
            if (incoming == null || !incoming.HasPosition) return null;
            double m2 = meters * meters;
            double v2 = velocity * velocity;
            HudTrack best = null;
            double bestD2 = double.MaxValue;
            double secondD2 = double.MaxValue;

            int cx = (int)Math.Floor(incoming.Position.X / meters);
            int cy = (int)Math.Floor(incoming.Position.Y / meters);
            int cz = (int)Math.Floor(incoming.Position.Z / meters);

            for (int ox = -1; ox <= 1; ox++)
            for (int oy = -1; oy <= 1; oy++)
            for (int oz = -1; oz <= 1; oz++)
            {
                long bucketKey;
                unchecked
                {
                    bucketKey = ((long)(cx + ox) * 73856093L) ^
                                ((long)(cy + oy) * 19349663L) ^
                                ((long)(cz + oz) * 83492791L);
                }
                List<HudTrack> bucket;
                if (!_fusionBuckets.TryGetValue(bucketKey, out bucket)) continue;

                for (int i = 0; i < bucket.Count; i++)
                {
                    HudTrack candidate = bucket[i];
                    if (candidate == null || !candidate.HasPosition || candidate.IsDistress) continue;

                    // Only fuse a Spectrum-style observation into a stronger identity.
                    // Never use spatial proximity to merge two independent Spectrum signals.
                    if ((incoming.Source == HudTrackSource.Spectrum || incoming.Source == HudTrackSource.FleetSignal) &&
                        (candidate.Source == HudTrackSource.Spectrum || candidate.Source == HudTrackSource.FleetSignal))
                        continue;

                    // Do not let the older proximity fallback undo exact identity safety.
                    if(incoming.EntityId!=0&&candidate.EntityId!=0&&incoming.EntityId!=candidate.EntityId&&
                        ExactTrackFusion.Conflicts(ReadCachedTrackIdentity(incoming.EntityId),ReadCachedTrackIdentity(candidate.EntityId)))continue;

                    string a = (candidate.Relation ?? "unknown").ToLowerInvariant();
                    string b = (incoming.Relation ?? "unknown").ToLowerInvariant();
                    if (a != "unknown" && b != "unknown" && a != b) continue;

                    double d2 = Vector3D.DistanceSquared(incoming.Position, candidate.Position);
                    if (d2 > m2) continue;
                    if (Vector3D.DistanceSquared(incoming.Velocity, candidate.Velocity) > v2) continue;

                    if (d2 < bestD2)
                    {
                        secondD2 = bestD2;
                        bestD2 = d2;
                        best = candidate;
                    }
                    else if (d2 < secondD2)
                    {
                        secondD2 = d2;
                    }
                }
            }

            if (best == null) return null;
            // Very close is unambiguous. For wider offsets (large grids / emitter
            // blocks), require the best candidate to be clearly better than the next.
            if (bestD2 <= 75.0 * 75.0) return best;
            if (secondD2 == double.MaxValue || secondD2 > bestD2 * 3.24) return best;
            return null;
        }

        private static void MergeTrackKnowledge(HudTrack existing, HudTrack incoming)
        {
            if (existing == null || incoming == null) return;
            ExactTrackFusion.Remember(existing,incoming);

            TrackRelationship.Merge(existing,incoming);
            existing.AttackTarget |= incoming.AttackTarget;

            if (incoming.SignalStrength.HasValue && (!existing.SignalStrength.HasValue || incoming.AgeSeconds <= existing.AgeSeconds))
            {
                existing.SignalStrength = incoming.SignalStrength;
                if (existing.Source != HudTrackSource.Spectrum || string.IsNullOrWhiteSpace(existing.RawEmitterId))
                    existing.RawEmitterId = incoming.RawEmitterId;
            }
            if (incoming.Focused) existing.Focused = true;
            if (incoming.Threat > existing.Threat) existing.Threat = incoming.Threat;
            FriendlyTrackPolicy.MergeFreshness(existing,incoming);
            if(FriendlyTrackPolicy.NetworkFriendly(existing)&&incoming.Source==HudTrackSource.Spectrum&&!string.IsNullOrWhiteSpace(incoming.RawEmitterId))
                existing.RawEmitterId=incoming.RawEmitterId;

            bool genericName = string.IsNullOrWhiteSpace(existing.Name) ||
                               existing.Name.StartsWith("CONTACT ", StringComparison.OrdinalIgnoreCase) ||
                               existing.Name.Equals("SIGNAL", StringComparison.OrdinalIgnoreCase) ||
                               existing.Name.Equals("SPECTRUM SIGNAL", StringComparison.OrdinalIgnoreCase) ||
                               existing.Name.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase);
            if (genericName && !string.IsNullOrWhiteSpace(incoming.Name))
                existing.Name = incoming.Name;

            if ((!existing.SectorKnown || string.IsNullOrWhiteSpace(existing.SectorId)) && incoming.SectorKnown)
            {
                existing.SectorId = incoming.SectorId;
                existing.SectorName = incoming.SectorName;
                existing.SectorKnown = true;
                existing.SameSector = incoming.SameSector;
            }
        }

        private void PredictPosition(HudTrack t, double seconds)
        {
            if (!_settings.PredictTrackMotion || t == null || seconds <= 0) return;
            t.Position += t.Velocity * seconds;
        }

        private double PredictionAge(double seconds)
        {
            if (!_settings.PredictTrackMotion) return 0;
            return Math.Max(0, Math.Min(_settings.PredictionLimitSeconds, seconds));
        }

        private static void AddKinematics(HudTrack t, Vector3D ownPosition, Vector3D ownVelocity)
        {
            Vector3D delta = t.Position - ownPosition;
            t.Distance = delta.Length();
            if (t.Distance > 1)
            {
                Vector3D los = delta / t.Distance;
                Vector3D rel = t.Velocity - ownVelocity;
                t.Closing = -Vector3D.Dot(rel, los);
            }
            else t.Closing = 0;
        }

        private static double Score(HudTrack t)
        {
            double score = 0;
            if (t.IsDistress) score += 250000;
            if (t.Focused) score += 100000;
            if (t.AttackTarget) score += 80000;
            if (t.Source == HudTrackSource.WeaponCore) score += 20000;
            if ((t.Relation ?? "").Equals("hostile", StringComparison.OrdinalIgnoreCase)) score += 12000;
            if (t.Source == HudTrackSource.FleetContact) score += 5000;
            if (t.Source == HudTrackSource.FleetSignal) score += 3500;
            if (t.Friendly) score += 1000;
            score += Math.Max(0, t.Threat) * 1000;
            score += Math.Max(0, t.Closing) * 5;
            score += Math.Max(0, 100000 - t.Distance) / 100.0;
            score -= t.AgeSeconds * 200;
            return score;
        }

        private bool IsOccupied(Vector2D point)
        {
            double r2 = _settings.DeclutterRadius * _settings.DeclutterRadius;
            for (int i = 0; i < _occupied.Count; i++)
            {
                Vector2D d = point - _occupied[i];
                if (d.LengthSquared() <= r2) return true;
            }
            return false;
        }

        private static void Project(IMyCamera camera, Vector3D world, out Vector2D hud, out bool offscreen)
        {
            Vector3D to = world - camera.Position;
            double forward = Vector3D.Dot(to, camera.WorldMatrix.Forward);

            // v0.5.1: exact Zeos Flight HUD v0.7.2 projection behavior.
            // IMyCamera.WorldToScreen already returns the HudAPI-style -1..+1
            // coordinates used by the working Flight HUD. Do NOT remap a second time.
            Vector3D projected = camera.WorldToScreen(ref world);
            double x = projected.X;
            double y = projected.Y;
            if (forward <= 0) { x = -x; y = -y; }
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y))
            {
                hud = Vector2D.Zero;
                offscreen = true;
                return;
            }

            offscreen = forward <= 0 || x < -1.0 || x > 1.0 || y < -1.0 || y > 1.0;
            if (!offscreen)
            {
                hud = new Vector2D(x, y);
                return;
            }

            double right = Vector3D.Dot(to, camera.WorldMatrix.Right);
            double up = Vector3D.Dot(to, camera.WorldMatrix.Up);
            if (forward <= 0)
            {
                right = -right;
                up = -up;
            }

            double mag = Math.Max(1e-6, Math.Sqrt(right * right + up * up));
            right /= mag;
            up /= mag;

            double sx = 0.88 / Math.Max(1e-6, Math.Abs(right));
            double sy = 0.80 / Math.Max(1e-6, Math.Abs(up));
            double scale = Math.Min(sx, sy);
            hud = new Vector2D(right * scale, up * scale);
        }

        private List<HudTrack> SortScope(List<HudTrack> contacts)
        {
            IEnumerable<HudTrack> query = contacts;
            switch (_settings.ScopeSort)
            {
                case HudScopeSort.Threat:
                    query = contacts.OrderByDescending(t => t.Priority).ThenBy(t => t.Distance);
                    break;
                case HudScopeSort.TrackId:
                    query = contacts.OrderBy(t => t.TrackId).ThenBy(t => t.Distance);
                    break;
                case HudScopeSort.Fastest:
                    query = contacts.OrderByDescending(t => t.Velocity.LengthSquared()).ThenBy(t => t.Distance);
                    break;
                case HudScopeSort.ClosingFastest:
                    query = contacts.OrderByDescending(t => t.Closing).ThenBy(t => t.Distance);
                    break;
                case HudScopeSort.FocusedFirst:
                    query = contacts.OrderByDescending(t => t.Focused).ThenByDescending(t => t.Priority).ThenBy(t => t.Distance);
                    break;
                default:
                    query = contacts.OrderBy(t => t.Distance).ThenBy(t => t.TrackId);
                    break;
            }
            return query.ToList();
        }

        private static string ScopeCore(HudTrack t)
        {
            string name = ScopeName(t);
            return t.SignalStrength.HasValue ? name + " SIG " + t.SignalStrength.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : name;
        }

        private static string ScopeName(HudTrack t)
        {
            if (!string.IsNullOrWhiteSpace(t.Name)) return t.Name;
            if (t.Source == HudTrackSource.Spectrum) return "UNKNOWN";
            if (t.Source == HudTrackSource.WeaponCore) return "WC CONTACT";
            if (t.Source == HudTrackSource.FleetContact) return "ZEO CONTACT";
            if (t.Source == HudTrackSource.FleetSignal) return "ZEO SIGNAL";
            return "UNKNOWN";
        }

        public string Diagnostic()
        {
            string fleet = !_settings.ReceiveFleetLink ? "RX_OFF" : (_fleet == null ? "OFF" :
                (_fleet.Online ? "OK/" + _fleet.Received + "/V" + (_fleet.ServerVersion ?? "?") : (_fleet.LastStatus + "/" + _fleet.LastError)));
            ServerTrustSnapshot trust = _engine.GetServerTrust();
            AccountLinkSnapshot account = _engine.GetAccountLink();
            string distress = _distress == null ? "OFF" :
                (_distress.LastStatus >= 200 && _distress.LastStatus < 300 ? "OK" :
                    (_distress.LastStatus > 0 ? "HTTP" + _distress.LastStatus : _distress.LastError));
            return "MODE=OPEN_TEST HUD=EXTERNAL" +
                   " AUTH=OFF" +
                   " FACTION=" + _engine.GetLocalFactionTag() +
                   " DXCTX=" + trust.State + "/" + trust.Sector +
                   " OVERLAY=" + (_overlay.Running ? "RUNNING" : (_overlay.Installed ? "WAIT" : "MISSING")) +
                   " SPECTRUM=" + (_spectrum.Ready ? "READY" : "WAIT") +
                   " TX=" + _engine.GetTelemetryDiagnostic() +
                   " FLEET=" + fleet +
                   " SOS=" + distress +
                   " FOOT_TOS=" + (_settings.KeepTosOutsideShip ? "ON" : "OFF") +
                   (string.IsNullOrWhiteSpace(_overlay.LastError) ? "" : " OVLERR=" + _overlay.LastError);
        }

        public void Dispose()
        {
            DisposeTargets();
            try { ZeoNativeSettingsUi.Close(); } catch { }
            try { if (_distress != null) _distress.Dispose(); } catch { }
            try { _spectrum.Dispose(); } catch { }
            try { if (_fleet != null) _fleet.Dispose(); } catch { }
            try { _overlay.Dispose(); } catch { }
        }
    }
}
