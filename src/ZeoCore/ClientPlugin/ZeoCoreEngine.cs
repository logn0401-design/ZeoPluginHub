using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Sandbox.Definitions;
using System.Web.Script.Serialization;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using VRage.ModAPI;
using VRageMath;

namespace ZeoCore
{
    internal sealed class ZeoCoreEngine : IDisposable
    {
        private readonly CoreSystemsApi _wc = new CoreSystemsApi();
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        private readonly Stopwatch _sourceClock = Stopwatch.StartNew();
        private readonly List<MyTuple<MyEntity, float>> _threats = new List<MyTuple<MyEntity, float>>(256);
        private readonly List<Vector3D> _incoming = new List<Vector3D>(128);
        private readonly HashSet<long> _reportedIds = new HashSet<long>();
        private readonly List<IMySlimBlock> _selfBlocks = new List<IMySlimBlock>(4096);
        private readonly List<IMySlimBlock> _shipStatusBlocks = new List<IMySlimBlock>(8192);
        private readonly List<IMyCubeGrid> _shipStatusGrids = new List<IMyCubeGrid>(32);
        private readonly List<MyInventoryItem> _shipStatusItems = new List<MyInventoryItem>(256);
        private readonly Dictionary<string,double> _shipAmmoCounts = new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,string> _shipAmmoLiveNames = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _shipRelevantAmmo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<GasTankRef> _shipGasTanks = new List<GasTankRef>(64);
        private readonly Dictionary<string, GasKind> _gasKindByDefinition = new Dictionary<string, GasKind>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<long> _hudSeenIds = new HashSet<long>();
        // V1.3 target-group cache. Mechanical subgrids are canonicalized to one
        // representative and recently detached children are temporarily suppressed.
        private readonly List<IMyCubeGrid> _targetMechanicalGroup = new List<IMyCubeGrid>(32);
        private readonly Dictionary<long, IMyCubeGrid> _targetCanonicalGrid = new Dictionary<long, IMyCubeGrid>();
        private readonly Dictionary<long, long> _formerSubgridParent = new Dictionary<long, long>();
        private readonly Dictionary<long, int> _formerSubgridFrame = new Dictionary<long, int>();
        private readonly List<long> _subgridCleanup = new List<long>(64);
        private bool _suppressSubgridClutter = true;
        private bool _adaptiveTacticalRate = true;
        private int _tacticalProcessingCap = 96;
        private int _lastHudRawThreatCount;
        private int _lastHudPerfLogFrame = -100000;
        private readonly Stopwatch _hudPerfClock = new Stopwatch();
        private readonly object _hudSync = new object();
        private readonly object _tacticalShareSync = new object();
        private readonly List<SpectrumClient.DetectionData> _sharedSpectrum = new List<SpectrumClient.DetectionData>(128);
        private bool _shareWeaponCoreContacts = true;
        private bool _shareSpectrumSignals = true;
        private LocalHudSnapshot _hudSnapshot = new LocalHudSnapshot();

        private ZeoConfig _config;
        private DeviceTelemetrySender _deviceSender;
        private AccountLinkClient _account;
        private bool _chatRegistered;
        private bool _startupNotified;
        private int _lastPublishFrame = -100000;
        private int _lastHudFrame = -100000;
        private int _lastShipStatusFrame = -100000;
        private int _lastGasStatusFrame = -100000;
        private int _lastServerTrustFrame = -100000;
        private int _lastApiRequestFrame = -100000;
        private long _shipStatusGridId;
        private double _shipH2O = -1;
        private double _shipO2 = -1;
        private double _shipFusion;
        private double _shipDrive = -1;
        private double _shipReactor = -1;
        private double _shipPowerCurrent;
        private double _shipPowerMax;
        private double _shipHp = -1;
        // Integrity baselines keep destroyed blocks in the denominator. Without these,
        // a destroyed drive/reactor would simply disappear from the current block list
        // and could make the displayed health incorrectly rise again. Baselines reset
        // when the controlled primary grid changes and can grow if the ship is expanded.
        private double _shipHpBaselineMax;
        private double _shipDriveBaselineMax;
        private double _shipReactorBaselineMax;
        private long _seq;
        private long _activeGridId;
        private int _cachedBlockCount;
        private int _blockCountFrame = -100000;
        private string _lastBuildError = "";
        private int _lastContactCount;
        private int _shipH20TankCount;
        private int _shipO2TankCount;
        private int _shipUnknownGasTankCount;
        private ServerTrustSnapshot _serverTrust = new ServerTrustSnapshot();
        private string _lastTrustFingerprint = "";
        private string _lastAccountFingerprint = "";
        private string _lastPairCodeNotified = "";

        private enum GasKind { Unknown = 0, H20 = 1, Oxygen = 2 }
        private sealed class GasTankRef
        {
            public IMyGasTank Tank;
            public GasKind Kind;
        }

        internal ZeoConfig Config { get { return _config; } }
        internal bool TelemetryExportPreferenceEnabled { get { return _config != null && _config.TransmitTelemetry != false; } }
        internal bool TelemetryExportActive { get { return _deviceSender != null && TelemetryExportPreferenceEnabled && HasGameFaction; } }
        internal ServerTrustSnapshot GetServerTrust() { return (_serverTrust ?? new ServerTrustSnapshot()).Clone(); }
        internal AccountLinkSnapshot GetAccountLink()
        {
            string faction = GetLocalFactionTag();
            return new AccountLinkSnapshot
            {
                State = "OPEN TEST",
                Detail = "Account/device/DX authorization bypassed for test phase",
                Linked = true,
                Authorized = true,
                FactionTag = faction,
                AssignedScope = "faction",
                EffectiveScope = "faction"
            };
        }
        internal bool HasGameFaction { get { return GetLocalFactionTag() != "UNAFFILIATED"; } }
        internal bool AccountLinked { get { return true; } }
        internal bool AccountAuthorized { get { return true; } }
        internal string GetTelemetryDiagnostic()
        {
            if (!HasGameFaction) return "LOCAL_ONLY_NO_FACTION";
            if (_deviceSender == null) return "OFF";
            if (!TelemetryExportPreferenceEnabled) return "PREF_OFF";
            int status = _deviceSender.LastStatus;
            string error = _deviceSender.LastError ?? "";
            if (status >= 200 && status < 300 && string.IsNullOrWhiteSpace(error))
                return "OK/" + status + "/V" + (_deviceSender.ServerVersion ?? "?") + "/A" + _deviceSender.LastAccepted + "/R" + _deviceSender.LastRejected;
            if (status >= 200 && status < 300) return "BAD/" + status + "/" + error;
            if (status > 0) return "HTTP" + status + "/" + error;
            return string.IsNullOrWhiteSpace(_deviceSender.LastError) ? "WAIT" : _deviceSender.LastError;
        }
        internal string GetLocalFactionTag()
        {
            try
            {
                var session = MyAPIGateway.Session;
                var player = session == null ? null : session.Player;
                if (session != null && session.Factions != null && player != null)
                {
                    var faction = session.Factions.TryGetPlayerFaction(player.IdentityId);
                    if (faction != null && !string.IsNullOrWhiteSpace(faction.Tag))
                        return Safe(faction.Tag).Trim().ToUpperInvariant();
                }
            }
            catch { }
            return "UNAFFILIATED";
        }
        internal bool LocalPayloadLoggingEnabled { get { return _config != null && _config.WriteLastPayload; } }

        internal void SetTelemetryExportEnabled(bool enabled)
        {
            if (_config == null) return;
            _config.TransmitTelemetry = enabled;
            try { ZeoConfig.Save(_config); } catch (Exception ex) { Plugin.Log("Telemetry preference save failed: " + ex.Message); }
            Plugin.Log("Telemetry export preference -> " + (enabled ? "ON" : "OFF"));
        }

        internal void SetLocalPayloadLoggingEnabled(bool enabled)
        {
            if (_config == null) return;
            _config.WriteLastPayload = enabled;
            try { ZeoConfig.Save(_config); } catch (Exception ex) { Plugin.Log("Payload logging preference save failed: " + ex.Message); }
            Plugin.Log("Local last-payload logging -> " + (enabled ? "ON" : "OFF"));
        }

        internal void SetTacticalSharing(bool shareWeaponCoreContacts, bool shareSpectrumSignals)
        {
            _shareWeaponCoreContacts = shareWeaponCoreContacts;
            _shareSpectrumSignals = shareSpectrumSignals;
            if (!shareSpectrumSignals)
            {
                lock (_tacticalShareSync) _sharedSpectrum.Clear();
            }
        }

        internal void SetHudPerformanceOptions(bool suppressSubgridClutter, bool adaptiveTacticalRate, int tacticalProcessingCap)
        {
            _suppressSubgridClutter = suppressSubgridClutter;
            _adaptiveTacticalRate = adaptiveTacticalRate;
            _tacticalProcessingCap = Math.Max(24, Math.Min(192, tacticalProcessingCap));
            if (!suppressSubgridClutter)
            {
                _formerSubgridParent.Clear();
                _formerSubgridFrame.Clear();
            }
        }

        internal void SetSharedSpectrumDetections(IList<SpectrumClient.DetectionData> detections)
        {
            lock (_tacticalShareSync)
            {
                _sharedSpectrum.Clear();
                if (!_shareSpectrumSignals || detections == null) return;
                int count = Math.Min(128, detections.Count);
                for (int i = 0; i < count; i++)
                {
                    SpectrumClient.DetectionData d = detections[i];
                    if (d.SelfOwned || d.EmitterId == 0) continue;
                    _sharedSpectrum.Add(d);
                }
            }
        }

        public ZeoCoreEngine()
        {
            _config = ZeoConfig.LoadOrMigrate();

            _account = null;
            Plugin.Log("OPEN TEST: AccountLink/device authorization disabled. Local SE faction routes test traffic.");

            try
            {
                _deviceSender = new DeviceTelemetrySender(_config);
                Plugin.Log("OPEN TEST telemetry armed. host=" + _deviceSender.Host + " auth=OFF");
            }
            catch (Exception ex)
            {
                _lastBuildError = "telemetry: " + ex.Message;
                Plugin.Log("OPEN TEST telemetry DISABLED: " + ex.Message);
            }

        }

        public void Update()
        {
            var session = MyAPIGateway.Session;
            if (session == null || MyAPIGateway.Utilities == null)
                return;

            EnsureChat();
            _wc.EnsureLoaded();

            int frame = 0;
            try { frame = session.GameplayFrameCounter; } catch { return; }

            if (frame < _lastServerTrustFrame || frame - _lastServerTrustFrame >= 60)
            {
                _lastServerTrustFrame = frame;
                ServerTrustSnapshot trust = ServerTrust.Capture();
                string fingerprint = trust.Fingerprint();
                if (!string.Equals(fingerprint, _lastTrustFingerprint, StringComparison.Ordinal))
                {
                    _lastTrustFingerprint = fingerprint;
                    Plugin.Log("Server trust -> " + trust.State + " sector=" + trust.Sector +
                               " steam=" + trust.ServerId + " expected=" + trust.ExpectedEndpoint +
                               (string.IsNullOrWhiteSpace(trust.ObservedEndpoint) ? "" : " observed=" + trust.ObservedEndpoint) +
                               " endpoint=" + (trust.EndpointVerified ? "VERIFIED" : "N/A") + " // " + trust.Detail);
                }
                _serverTrust = trust;
            }

            // v0.6.1 ALIGNED OPEN TEST: no account/device/DX authorization polling.

            if (!_wc.Ready && frame - _lastApiRequestFrame >= 180)
            {
                _lastApiRequestFrame = frame;
                _wc.RequestApi();
            }

            if (!_startupNotified)
            {
                _startupNotified = true;
                if (_deviceSender != null)
                    Plugin.Log("ZeoCore runtime ready // OPEN TEST TX armed // TX preference " + (TelemetryExportPreferenceEnabled ? "ON" : "OFF"));
                else
                    Plugin.Log("OPEN TEST TX disabled | local HUD remains available");
            }

            int hudCadence = 10;
            if (_adaptiveTacticalRate)
            {
                if (_lastHudRawThreatCount >= 96) hudCadence = 16;
                else if (_lastHudRawThreatCount >= 48) hudCadence = 12;
            }
            if (frame < _lastHudFrame || frame - _lastHudFrame >= hudCadence)
            {
                _lastHudFrame = frame;
                RefreshHudSnapshot(frame);
            }

            if (_deviceSender == null && !_config.WriteLastPayload)
                return;

            int every = Math.Max(10, _config.PublishSimulationFrames);
            if (frame < _lastPublishFrame || frame - _lastPublishFrame >= every)
            {
                _lastPublishFrame = frame;
                Publish(frame);
            }
        }

        private void UpdateAccountLink(int frame)
        {
            if (_account == null) return;

            ulong steamId = 0;
            long identityId = 0;
            string displayName = "ZeoCore Device";
            try
            {
                var player = MyAPIGateway.Session == null ? null : MyAPIGateway.Session.Player;
                if (player != null)
                {
                    try { steamId = player.SteamUserId; } catch { }
                    try { identityId = player.IdentityId; } catch { }
                    try
                    {
                        string playerName = player.DisplayName;
                        if (!string.IsNullOrWhiteSpace(playerName))
                            displayName = playerName + " PC";
                    }
                    catch { }
                }
            }
            catch { }

            ServerTrustSnapshot trust = _serverTrust ?? new ServerTrustSnapshot();
            _account.Update(frame, trust, steamId, identityId, displayName);

            AccountLinkSnapshot link = _account.Snapshot();
            string fingerprint = link.Fingerprint();
            if (!string.Equals(fingerprint, _lastAccountFingerprint, StringComparison.Ordinal))
            {
                _lastAccountFingerprint = fingerprint;
                Plugin.Log("AccountLink -> " + link.State +
                           " linked=" + link.Linked + " authorized=" + link.Authorized +
                           (string.IsNullOrWhiteSpace(link.FactionTag) ? "" : " faction=" + link.FactionTag) +
                           (link.LastHttpStatus > 0 ? " http=" + link.LastHttpStatus : "") +
                           " // " + link.Detail);
            }

            if (!string.IsNullOrWhiteSpace(link.PairingCode) &&
                !string.Equals(link.PairingCode, _lastPairCodeNotified, StringComparison.Ordinal))
            {
                _lastPairCodeNotified = link.PairingCode;
                Plugin.Notify("ZEO ACCOUNT // LINK REQUIRED // code shown in capture-safe ZEO NETWORK panel", 7000, "White");
            }
            else if (link.Authorized && !string.IsNullOrWhiteSpace(_lastPairCodeNotified))
            {
                _lastPairCodeNotified = "";
                Plugin.Notify("ZEO LINKED // " + (string.IsNullOrWhiteSpace(link.FactionTag) ? "ACCOUNT" : link.FactionTag) +
                              " // " + (link.EffectiveScope ?? "self").ToUpperInvariant(), 6000, "Green");
            }
        }

        internal LocalHudSnapshot GetHudSnapshot()
        {
            lock (_hudSync) return _hudSnapshot.Clone();
        }

        private void RefreshHudSnapshot(int frame)
        {
            _hudPerfClock.Restart();
            _targetCanonicalGrid.Clear();
            CleanupDetachedSubgridHistory(frame);
            var next = new LocalHudSnapshot { CapturedUtc = DateTime.UtcNow };
            try
            {
                SectorSnapshot sector = SectorIdentity.Capture();
                next.SectorId = sector.Id;
                next.SectorName = sector.Name;
                next.SectorKnown = sector.Known;

                IMyPlayer player = null;
                try { player = MyAPIGateway.Session == null ? null : MyAPIGateway.Session.Player; } catch { }
                try
                {
                    if (player != null && player.Character != null)
                    {
                        next.HasObserver = true;
                        next.ObserverPosition = player.Character.GetPosition();
                        // Character velocity is deliberately optional in observer mode.
                        // A zero fallback keeps range/TOS stable across SE API versions.
                        next.ObserverVelocity = Vector3D.Zero;
                        try
                        {
                            IMyEntity characterEntity = player.Character as IMyEntity;
                            if (characterEntity != null && characterEntity.Physics != null)
                                next.ObserverVelocity = characterEntity.Physics.LinearVelocity;
                        }
                        catch { }
                    }
                }
                catch { }

                IMyCubeGrid grid;
                IMyCubeBlock controlBlock;
                IMyPlayer controlledPlayer;
                if (!TryControlledGrid(out controlledPlayer, out grid, out controlBlock))
                {
                    lock (_hudSync) _hudSnapshot = next;
                    return;
                }
                if (controlledPlayer != null) player = controlledPlayer;
                next.HasShip = true;
                next.HasObserver = true;
                next.OwnGridId = grid.EntityId;
                next.OwnGridName = Safe(grid.DisplayName);
                next.OwnPosition = grid.WorldAABB.Center;
                try { if (grid.Physics != null) next.OwnVelocity = grid.Physics.LinearVelocity; } catch { }
                next.ObserverPosition = next.OwnPosition;
                next.ObserverVelocity = next.OwnVelocity;

                RefreshShipStatus(grid, frame);
                RefreshGasStatusFast(frame);
                next.H2O = _shipH2O;
                next.O2 = _shipO2;
                next.FusionPellets = _shipFusion;
                next.DriveHealth = _shipDrive;
                next.ReactorHealth = _shipReactor;
                next.PowerCurrent = _shipPowerCurrent;
                next.PowerMax = _shipPowerMax;
                next.ShipHp = _shipHp;
                for (int ai=0; ai<AmmoCatalog.Entries.Length; ai++)
                {
                    var e=AmmoCatalog.Entries[ai]; double have; _shipAmmoCounts.TryGetValue(e.Subtype,out have);
                    string liveName; if (!_shipAmmoLiveNames.TryGetValue(e.Subtype, out liveName) || string.IsNullOrWhiteSpace(liveName)) liveName=e.ServerName;
                    bool relevant=_shipRelevantAmmo.Contains(e.Subtype) || have > 0.0001;
                    next.Ammo.Add(new HudAmmoStock { Key=e.Key, CleanName=e.CleanName, ServerName=liveName, Subtype=e.Subtype, Have=have, Relevant=relevant });
                }

                var gridEntity = grid as MyEntity;
                if (_wc.Ready && gridEntity != null)
                {
                    _threats.Clear();
                    MyEntity focusEntity = null;
                    IMyCubeGrid focusGrid = null;
                    MyEntity normalizedFocus = null;
                    long focusId = 0;
                    try { _wc.GetThreats(gridEntity, _threats); } catch { }
                    next.WcRawThreatCount = _threats.Count;
                    _lastHudRawThreatCount = _threats.Count;
                    try
                    {
                        focusEntity = _wc.GetFocus(gridEntity);
                        if (focusEntity != null)
                        {
                            focusGrid = focusEntity as IMyCubeGrid;
                            if (focusGrid == null)
                            {
                                var focusBlock = focusEntity as IMyCubeBlock;
                                if (focusBlock != null) focusGrid = focusBlock.CubeGrid;
                            }
                            if (focusGrid != null)
                            {
                                bool suppressFocus;
                                IMyCubeGrid canonicalFocus = CanonicalizeTargetGrid(focusGrid, frame, out suppressFocus);
                                if (suppressFocus)
                                {
                                    focusGrid = null;
                                    focusEntity = null;
                                }
                                else if (canonicalFocus != null) focusGrid = canonicalFocus;
                            }
                            normalizedFocus = focusGrid != null ? focusGrid as MyEntity : focusEntity;
                            focusId = normalizedFocus == null ? 0 : normalizedFocus.EntityId;
                        }
                    }
                    catch { }
                    next.FocusEntityId = focusId;

                    _hudSeenIds.Clear();
                    int acceptedThreats = 0;
                    int processCap = Math.Max(24, Math.Min(192, _tacticalProcessingCap));
                    for (int i = 0; i < _threats.Count && acceptedThreats < processCap; i++)
                    {
                        MyEntity entity = _threats[i].Item1;
                        if (entity == null) continue;

                        IMyCubeGrid trackGrid = entity as IMyCubeGrid;
                        if (trackGrid == null)
                        {
                            var block = entity as IMyCubeBlock;
                            if (block != null) trackGrid = block.CubeGrid;
                        }

                        if (trackGrid != null && _suppressSubgridClutter)
                        {
                            bool suppressDetached;
                            IMyCubeGrid canonical = CanonicalizeTargetGrid(trackGrid, frame, out suppressDetached);
                            if (suppressDetached) continue;
                            if (canonical != null) trackGrid = canonical;
                        }

                        MyEntity normalized = trackGrid != null ? trackGrid as MyEntity : entity;
                        if (normalized == null || normalized.EntityId == grid.EntityId || !_hudSeenIds.Add(normalized.EntityId))
                            continue;
                        acceptedThreats++;

                        Vector3D pos = normalized.PositionComp != null ? normalized.PositionComp.WorldAABB.Center : Vector3D.Zero;
                        Vector3D vel = Vector3D.Zero;
                        try { if (normalized.Physics != null) vel = normalized.Physics.LinearVelocity; } catch { }
                        string relation = trackGrid == null ? "unknown" : RelationForGrid(player, trackGrid);
                        bool friendly = relation == "friendly";

                        next.WeaponCoreTracks.Add(new HudTrack
                        {
                            Key = "W:" + normalized.EntityId,
                            EntityId = normalized.EntityId,
                            Name = trackGrid != null ? Safe(trackGrid.DisplayName) : "WC CONTACT",
                            Relation = relation,
                            Source = HudTrackSource.WeaponCore,
                            Position = pos,
                            Velocity = vel,
                            Threat = _threats[i].Item2,
                            Focused = normalized.EntityId == focusId,
                            Friendly = friendly,
                            Stale = false,
                            AgeSeconds = 0
                        });
                    }

                    // v0.5.9.2 LAB: some CoreSystems builds expose the current WC focus
                    // through GetAiFocusBase even when that entity is absent from
                    // GetSortedThreatsBase. Preserve that exact WC target as a WC track
                    // instead of letting a parallel Spectrum return become the only
                    // visible representation.
                    if (normalizedFocus != null && normalizedFocus.EntityId != grid.EntityId &&
                        !_hudSeenIds.Contains(normalizedFocus.EntityId))
                    {
                        Vector3D pos = normalizedFocus.PositionComp != null ? normalizedFocus.PositionComp.WorldAABB.Center : Vector3D.Zero;
                        Vector3D vel = Vector3D.Zero;
                        try { if (normalizedFocus.Physics != null) vel = normalizedFocus.Physics.LinearVelocity; } catch { }
                        string relation = focusGrid == null ? "unknown" : RelationForGrid(player, focusGrid);
                        next.WeaponCoreTracks.Add(new HudTrack
                        {
                            Key = "W:" + normalizedFocus.EntityId,
                            EntityId = normalizedFocus.EntityId,
                            Name = focusGrid != null ? Safe(focusGrid.DisplayName) : "WC FOCUS",
                            Relation = relation,
                            Source = HudTrackSource.WeaponCore,
                            Position = pos,
                            Velocity = vel,
                            Threat = 0,
                            Focused = true,
                            Friendly = relation == "friendly",
                            Stale = false,
                            AgeSeconds = 0
                        });
                        _hudSeenIds.Add(normalizedFocus.EntityId);
                        next.WcFocusFallbackAdded = true;
                    }
                    next.WcNormalizedTrackCount = next.WeaponCoreTracks.Count;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log("HUD snapshot failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            _hudPerfClock.Stop();
            if (_hudPerfClock.Elapsed.TotalMilliseconds >= 6.0 &&
                (frame < _lastHudPerfLogFrame || frame - _lastHudPerfLogFrame >= 300))
            {
                _lastHudPerfLogFrame = frame;
                Plugin.Log("PERF HUD snapshot " + _hudPerfClock.Elapsed.TotalMilliseconds.ToString("0.0") +
                           "ms rawWC=" + next.WcRawThreatCount + " canonicalWC=" + next.WeaponCoreTracks.Count +
                           " cap=" + _tacticalProcessingCap + " subgridClutter=" + (_suppressSubgridClutter ? "ON" : "OFF"));
            }
            lock (_hudSync) _hudSnapshot = next;
        }

        private IMyCubeGrid CanonicalizeTargetGrid(IMyCubeGrid grid, int frame, out bool suppressDetached)
        {
            suppressDetached = false;
            if (grid == null || !_suppressSubgridClutter) return grid;

            IMyCubeGrid cached;
            if (_targetCanonicalGrid.TryGetValue(grid.EntityId, out cached))
                return cached;

            _targetMechanicalGroup.Clear();
            try { MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, _targetMechanicalGroup); }
            catch { }

            if (_targetMechanicalGroup.Count > 1)
            {
                IMyCubeGrid primary = grid;
                double best = -1;
                for (int i = 0; i < _targetMechanicalGroup.Count; i++)
                {
                    IMyCubeGrid member = _targetMechanicalGroup[i];
                    if (member == null) continue;
                    double score = 0;
                    try { score = member.WorldAABB.Size.LengthSquared(); } catch { }
                    if (score > best || (Math.Abs(score - best) < 0.001 && member.EntityId < primary.EntityId))
                    {
                        best = score;
                        primary = member;
                    }
                }

                for (int i = 0; i < _targetMechanicalGroup.Count; i++)
                {
                    IMyCubeGrid member = _targetMechanicalGroup[i];
                    if (member == null) continue;
                    _targetCanonicalGrid[member.EntityId] = primary;
                    if (member.EntityId != primary.EntityId)
                    {
                        _formerSubgridParent[member.EntityId] = primary.EntityId;
                        _formerSubgridFrame[member.EntityId] = frame;
                    }
                }
                return primary;
            }

            long parent;
            int lastSeen;
            if (_formerSubgridParent.TryGetValue(grid.EntityId, out parent) &&
                _formerSubgridFrame.TryGetValue(grid.EntityId, out lastSeen) &&
                parent != grid.EntityId && frame >= lastSeen && frame - lastSeen <= 1200)
            {
                suppressDetached = true;
            }
            _targetCanonicalGrid[grid.EntityId] = grid;
            return grid;
        }

        private void CleanupDetachedSubgridHistory(int frame)
        {
            if (!_suppressSubgridClutter)
            {
                _formerSubgridParent.Clear();
                _formerSubgridFrame.Clear();
                return;
            }

            _subgridCleanup.Clear();
            foreach (var pair in _formerSubgridFrame)
                if (frame < pair.Value || frame - pair.Value > 1200)
                    _subgridCleanup.Add(pair.Key);
            for (int i = 0; i < _subgridCleanup.Count; i++)
            {
                long id = _subgridCleanup[i];
                _formerSubgridFrame.Remove(id);
                _formerSubgridParent.Remove(id);
            }
        }


        private void RefreshShipStatus(IMyCubeGrid grid, int frame)
        {
            // Ship-status data is intentionally slower than marker data. Scanning every
            // mechanically connected block once per second keeps the HUD responsive
            // without turning the status panel into a per-frame block audit.
            if (grid == null) return;
            if (_shipStatusGridId == grid.EntityId && frame >= _lastShipStatusFrame && frame - _lastShipStatusFrame < 60)
                return;

            _lastShipStatusFrame = frame;
            if (_shipStatusGridId != grid.EntityId)
            {
                _shipStatusGridId = grid.EntityId;
                _shipHpBaselineMax = 0;
                _shipDriveBaselineMax = 0;
                _shipReactorBaselineMax = 0;
                _lastGasStatusFrame = -100000;
                _shipGasTanks.Clear();
            }
            _shipFusion = 0; _shipDrive = -1; _shipReactor = -1;
            _shipAmmoCounts.Clear(); _shipAmmoLiveNames.Clear(); _shipRelevantAmmo.Clear();
            _shipPowerCurrent = 0; _shipPowerMax = 0; _shipHp = -1;

            double hpCur = 0, hpMax = 0;
            double driveCur = 0, driveMax = 0;
            double reactorCur = 0, reactorMax = 0;
            _shipGasTanks.Clear();
            _shipUnknownGasTankCount = 0;

            _shipStatusGrids.Clear();
            try
            {
                if (MyAPIGateway.GridGroups != null)
                    MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, _shipStatusGrids);
            }
            catch { }
            if (_shipStatusGrids.Count == 0) _shipStatusGrids.Add(grid);

            for (int gi = 0; gi < _shipStatusGrids.Count; gi++)
            {
                IMyCubeGrid member = _shipStatusGrids[gi];
                if (member == null) continue;
                _shipStatusBlocks.Clear();
                try { member.GetBlocks(_shipStatusBlocks); } catch { continue; }

                for (int i = 0; i < _shipStatusBlocks.Count; i++)
                {
                    IMySlimBlock slim = _shipStatusBlocks[i];
                    if (slim == null) continue;

                    double max = Math.Max(0, slim.MaxIntegrity);
                    double cur = Math.Max(0, Math.Min(max, slim.BuildIntegrity - slim.CurrentDamage));
                    hpCur += cur;
                    hpMax += max;

                    IMyCubeBlock fat = slim.FatBlock;
                    if (fat == null) continue;

                    string def = ((fat.DefinitionDisplayNameText ?? "") + " " +
                                  (fat.BlockDefinition.SubtypeName ?? "") + " " +
                                  (fat.DisplayNameText ?? "")).ToLowerInvariant();

                    // WeaponCore tells us which magazine the installed weapon is currently using.
                    // Probe a small weapon-id range to support multi-weapon blocks. Stocked magazines
                    // are also considered relevant below, so alternate ammo already aboard the ship stays visible.
                    try
                    {
                        var entity = fat as MyEntity;
                        if (entity != null && fat is IMyUserControllableGun && _wc.Ready && _wc.HasCoreWeapon(entity))
                        {
                            int empty=0;
                            for (int wid=0; wid<16 && empty<3; wid++)
                            {
                                var map=_wc.GetMagazineMap(entity,wid);
                                string sub=map.Item1.SubtypeName;
                                if (string.IsNullOrWhiteSpace(sub)) { empty++; continue; }
                                empty=0;
                                var known=AmmoCatalog.FindSubtype(sub);
                                if (known!=null)
                                {
                                    _shipRelevantAmmo.Add(known.Subtype);
                                    // Prefer CoreSystems' live magazine display name when available.
                                    // This keeps the SERVER name mode aligned with the SDX2 server rather
                                    // than ever exposing the inventory subtype/pull id.
                                    string live = !string.IsNullOrWhiteSpace(map.Item2) ? map.Item2 : map.Item3;
                                    if (!string.IsNullOrWhiteSpace(live)) _shipAmmoLiveNames[known.Subtype] = live;
                                }
                            }
                        }
                    }
                    catch { }

                    bool isDrive = fat is IMyThrust || def.Contains("thruster") || def.Contains(" drive") || def.Contains("epstein");
                    bool isReactor = fat is IMyReactor || def.Contains("reactor");
                    if (isDrive) { driveCur += cur; driveMax += max; }
                    if (isReactor) { reactorCur += cur; reactorMax += max; }

                    IMyGasTank tank = fat as IMyGasTank;
                    if (tank != null)
                    {
                        GasKind gasKind = ClassifyGasTank(fat);
                        if (gasKind != GasKind.Unknown)
                            _shipGasTanks.Add(new GasTankRef { Tank = tank, Kind = gasKind });
                        else
                            _shipUnknownGasTankCount++;
                    }

                    IMyPowerProducer producer = fat as IMyPowerProducer;
                    if (producer != null)
                    {
                        _shipPowerCurrent += Math.Max(0, producer.CurrentOutput);
                        _shipPowerMax += Math.Max(0, producer.MaxOutput);
                    }

                    if (!fat.HasInventory) continue;
                    for (int inv = 0; inv < fat.InventoryCount; inv++)
                    {
                        var inventory = fat.GetInventory(inv);
                        if (inventory == null) continue;
                        _shipStatusItems.Clear();
                        try { inventory.GetItems(_shipStatusItems); } catch { continue; }
                        for (int q = 0; q < _shipStatusItems.Count; q++)
                        {
                            string subtype=_shipStatusItems[q].Type.SubtypeId;
                            if (string.Equals(subtype, "Sdx_itemReactorFuel", StringComparison.OrdinalIgnoreCase))
                                _shipFusion += (double)_shipStatusItems[q].Amount;
                            var ammo=AmmoCatalog.FindSubtype(subtype);
                            if (ammo!=null)
                            {
                                double curCount; _shipAmmoCounts.TryGetValue(ammo.Subtype,out curCount);
                                _shipAmmoCounts[ammo.Subtype]=curCount+(double)_shipStatusItems[q].Amount;
                            }
                        }
                    }
                }
            }

            // Grow baselines when blocks are added, but do not shrink them when blocks
            // are destroyed/detached. This makes damage persistent and repair meaningful.
            _shipHpBaselineMax = Math.Max(_shipHpBaselineMax, hpMax);
            _shipDriveBaselineMax = Math.Max(_shipDriveBaselineMax, driveMax);
            _shipReactorBaselineMax = Math.Max(_shipReactorBaselineMax, reactorMax);

            _shipHp = _shipHpBaselineMax > 0 ? Math.Max(0, Math.Min(1, hpCur / _shipHpBaselineMax)) : -1;
            _shipDrive = _shipDriveBaselineMax > 0 ? Math.Max(0, Math.Min(1, driveCur / _shipDriveBaselineMax)) : -1;
            _shipReactor = _shipReactorBaselineMax > 0 ? Math.Max(0, Math.Min(1, reactorCur / _shipReactorBaselineMax)) : -1;
            // Gas fill is sampled by the fast path below so H20/O2 updates do not
            // wait on the heavier integrity/inventory scan.
            _lastGasStatusFrame = -100000;
            RefreshGasStatusFast(frame);
        }

        private void RefreshGasStatusFast(int frame)
        {
            if (frame >= _lastGasStatusFrame && frame - _lastGasStatusFrame < 10) return;
            _lastGasStatusFrame = frame;

            double h20Amount = 0, h20Capacity = 0, o2Amount = 0, o2Capacity = 0;
            int h20Count = 0, o2Count = 0;
            for (int i = _shipGasTanks.Count - 1; i >= 0; i--)
            {
                GasTankRef entry = _shipGasTanks[i];
                if (entry == null || entry.Tank == null)
                {
                    _shipGasTanks.RemoveAt(i);
                    continue;
                }
                try
                {
                    double cap = Math.Max(0, entry.Tank.Capacity);
                    double ratio = Math.Max(0, Math.Min(1, entry.Tank.FilledRatio));
                    if (cap <= 0) continue;
                    if (entry.Kind == GasKind.H20)
                    {
                        h20Capacity += cap;
                        h20Amount += cap * ratio;
                        h20Count++;
                    }
                    else if (entry.Kind == GasKind.Oxygen)
                    {
                        o2Capacity += cap;
                        o2Amount += cap * ratio;
                        o2Count++;
                    }
                }
                catch { }
            }
            _shipH20TankCount = h20Count;
            _shipO2TankCount = o2Count;
            _shipH2O = h20Capacity > 0 ? Math.Max(0, Math.Min(1, h20Amount / h20Capacity)) : -1;
            _shipO2 = o2Capacity > 0 ? Math.Max(0, Math.Min(1, o2Amount / o2Capacity)) : -1;
        }

        private GasKind ClassifyGasTank(IMyCubeBlock block)
        {
            if (block == null) return GasKind.Unknown;
            string definitionKey = "";
            try { definitionKey = block.BlockDefinition.ToString(); } catch { }
            GasKind cached;
            if (!string.IsNullOrWhiteSpace(definitionKey) && _gasKindByDefinition.TryGetValue(definitionKey, out cached))
                return cached;

            string gasIdentity = TryStoredGasIdentity(block);
            string fallback = "";
            try
            {
                fallback = ((block.DefinitionDisplayNameText ?? "") + " " +
                            (block.BlockDefinition.SubtypeName ?? "") + " " +
                            (block.DisplayNameText ?? "")).ToLowerInvariant();
            }
            catch { }

            GasKind kind = GasKind.Unknown;
            string token = (gasIdentity ?? "").ToLowerInvariant();
            if (IsOxygenToken(token)) kind = GasKind.Oxygen;
            else if (IsH20Token(token)) kind = GasKind.H20;
            else if (IsOxygenToken(fallback)) kind = GasKind.Oxygen;
            else if (IsH20Token(fallback)) kind = GasKind.H20;

            if (!string.IsNullOrWhiteSpace(definitionKey)) _gasKindByDefinition[definitionKey] = kind;
            return kind;
        }

        private static string TryStoredGasIdentity(IMyCubeBlock block)
        {
            try
            {
                object definition = MyDefinitionManager.Static.GetCubeBlockDefinition(block.BlockDefinition);
                if (definition == null) return null;
                Type t = definition.GetType();
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                string[] names = { "StoredGasId", "StoredResourceId", "GasId", "ResourceId" };
                for (int i = 0; i < names.Length; i++)
                {
                    PropertyInfo p = t.GetProperty(names[i], flags);
                    if (p != null && p.CanRead)
                    {
                        object value = p.GetValue(definition, null);
                        if (value != null) return value.ToString();
                    }
                    FieldInfo f = t.GetField(names[i], flags);
                    if (f != null)
                    {
                        object value = f.GetValue(definition);
                        if (value != null) return value.ToString();
                    }
                }
            }
            catch { }
            return null;
        }

        private static bool IsOxygenToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string v = value.ToLowerInvariant();
            return v.Contains("oxygen") || v.Contains("gasproperties/o2") || v.Contains("gasproperties:o2") ||
                   v == "o2" || v.StartsWith("o2 ") || v.Contains(" o2 ");
        }

        private static bool IsH20Token(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string v = value.ToLowerInvariant();
            return v.Contains("hydrogen") || v.Contains("h20") || v.Contains("h2o") || v.Contains("water") ||
                   v.Contains("gasproperties/h2") || v.Contains("gasproperties:h2") || v == "h2" || v.StartsWith("h2 ") || v.Contains(" h2 ");
        }

        private void Publish(int frame)
        {
            try
            {
                IMyPlayer player;
                IMyCubeGrid grid;
                IMyCubeBlock controlBlock;
                if (!TryControlledGrid(out player, out grid, out controlBlock))
                    return;

                if (_activeGridId != grid.EntityId)
                {
                    _activeGridId = grid.EntityId;
                    _seq = 0;
                    _sourceClock.Restart();
                    _blockCountFrame = -100000;
                    Plugin.Log("Controlled grid -> " + Safe(grid.DisplayName) + " id=" + grid.EntityId);
                }

                _seq++;
                string payloadJson = BuildEnvelope(player, grid, controlBlock, frame);
                if (_config.WriteLastPayload)
                {
                    try { File.WriteAllText(Plugin.LastPayloadPath, payloadJson, new UTF8Encoding(false)); } catch { }
                }
                if (TelemetryExportActive && _deviceSender != null)
                    _deviceSender.TrySend(payloadJson, GetLocalFactionTag());
                _lastBuildError = "";
            }
            catch (Exception ex)
            {
                _lastBuildError = ex.GetType().Name + ": " + ex.Message;
                Plugin.Log("Build payload ERROR: " + ex);
            }
        }

        private string BuildEnvelope(IMyPlayer player, IMyCubeGrid grid, IMyCubeBlock controlBlock, int frame)
        {
            var gridEntity = grid as MyEntity;

            _incoming.Clear();
            _threats.Clear();
            MyEntity focusEntity = null;
            IMyCubeGrid focusGrid = null;
            MyEntity normalizedFocus = null;
            long focusId = 0;
            if (_wc.Ready && gridEntity != null)
            {
                try { _wc.GetThreats(gridEntity, _threats); } catch { }
                try
                {
                    focusEntity = _wc.GetFocus(gridEntity);
                    if (focusEntity != null)
                    {
                        focusGrid = focusEntity as IMyCubeGrid;
                        if (focusGrid == null)
                        {
                            var focusBlock = focusEntity as IMyCubeBlock;
                            if (focusBlock != null) focusGrid = focusBlock.CubeGrid;
                        }
                        normalizedFocus = focusGrid != null ? focusGrid as MyEntity : focusEntity;
                        focusId = normalizedFocus == null ? 0 : normalizedFocus.EntityId;
                    }
                }
                catch { }
                try { _wc.GetLockedPositions(gridEntity, _incoming); } catch { }
            }

            SectorSnapshot sector = SectorIdentity.Capture();
            var self = BuildSelfRow(grid, frame, _incoming.Count);
            self["sectorId"] = sector.Id;
            self["sectorName"] = sector.Name;
            self["sectorKnown"] = sector.Known;
            self["shipHp"] = _shipHp;
            self["driveHealth"] = _shipDrive;
            self["powerLoad"] = _shipPowerMax > 0 ? _shipPowerCurrent / _shipPowerMax : -1d;
            // Fleet publication remains PILOTED ONLY. The controlled ship is the
            // reporter (`self`) and is the only grid ZeoCore publishes as a Fleet
            // member. Sensor-observed friendly grids travel through contacts[] with
            // relation=friendly, so tactical sharing never turns them into Fleet pilots.
            var friendlies = new List<object>();
            var contacts = new List<object>();
            _reportedIds.Clear();
            _reportedIds.Add(grid.EntityId);

            if (_shareWeaponCoreContacts)
            {
                CollectWeaponCoreContacts(player, grid, focusId, contacts);
                if (normalizedFocus != null && normalizedFocus.EntityId != grid.EntityId &&
                    !_reportedIds.Contains(normalizedFocus.EntityId) && contacts.Count < _config.MaxContacts)
                {
                    string focusRelation = focusGrid == null ? "unknown" : RelationForGrid(player, focusGrid);
                    contacts.Add(BuildTrackRow(normalizedFocus, focusGrid, focusRelation, 0f, true, "weaponcore"));
                    _reportedIds.Add(normalizedFocus.EntityId);
                }
            }
            if (_shareSpectrumSignals)
                CollectSpectrumSignals(contacts);

            _lastContactCount = contacts.Count;

            var bsos = new Dictionary<string, object>
            {
                { "seq", _seq },
                { "t", Math.Round(_sourceClock.Elapsed.TotalSeconds, 6) },
                { "self", self },
                { "friendlies", friendlies },
                { "contacts", contacts },
                { "fire", new Dictionary<string, object> { { "events", new object[0] } } }
            };

            var source = new Dictionary<string, object>
            {
                { "entityId", grid.EntityId },
                { "gridName", Safe(grid.DisplayName) },
                { "blockName", "ZeoCore Device Telemetry" }
            };
            // v0.6.1 ALIGNED OPEN TEST: client faction is test-routing metadata only.
            // It is intentionally NOT treated as authenticated identity.
            string openFactionTag = GetLocalFactionTag();
            source["factionTag"] = openFactionTag;
            source["sectorId"] = sector.Id;
            source["sectorName"] = sector.Name;

            var packet = new Dictionary<string, object>
            {
                { "tag", "bsos.telemetry.v1" },
                { "source", source },
                { "payload", bsos }
            };

            // v0.2.3 compatibility: the current Zeo Battle Manager UI/live picture
            // is already rooted in the legacy Conduit world id "default".
            // Keep device-auth telemetry packets in that same world so they appear in the
            // existing tactical view instead of creating a second hidden world.
            string serverId = "default";

            string sessionName = null;
            try { sessionName = MyAPIGateway.Session.Name; } catch { }

            var observer = new Dictionary<string, object>
            {
                { "identityId", player.IdentityId },
                { "steamId", player.SteamUserId },
                { "displayName", Safe(player.DisplayName) }
            };

            ServerTrustSnapshot trust = _serverTrust ?? new ServerTrustSnapshot();
            var world = new Dictionary<string, object>
            {
                { "serverId", serverId },
                { "sectorId", sector.Id },
                { "sectorName", sector.Name },
                { "sectorServerId", sector.ServerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { "serverTrustState", trust.State },
                { "serverTrustSector", trust.Sector },
                { "serverTrustServerId", trust.ServerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { "serverTrustIdentityVerified", trust.IdentityVerified },
                { "serverTrustEndpointVerified", trust.EndpointVerified },
                { "serverTrustExpectedEndpoint", trust.ExpectedEndpoint ?? "" },
                { "serverTrustObservedEndpoint", trust.ObservedEndpoint ?? "" },
                { "zeoOpenFaction", openFactionTag },
                { "zeoAccessMode", "open_test_no_auth" }
            };
            if (!string.IsNullOrWhiteSpace(sessionName)) world["sessionName"] = sessionName;

            var envelope = new Dictionary<string, object>
            {
                { "schemaVersion", "2.0" },
                { "capturedAtUtc", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'") },
                { "observer", observer },
                { "world", world },
                { "openFactionTag", openFactionTag },
                { "openClientId", _config.DeviceId ?? "" },
                { "packets", new object[] { packet } }
            };

            return _json.Serialize(envelope);
        }

        private Dictionary<string, object> BuildSelfRow(IMyCubeGrid grid, int frame, int incomingCount)
        {
            Vector3D position = grid.WorldAABB.Center;
            Vector3D velocity = Vector3D.Zero;
            try { if (grid.Physics != null) velocity = grid.Physics.LinearVelocity; } catch { }

            MatrixD wm = grid.WorldMatrix;
            var row = new Dictionary<string, object>
            {
                { "id", grid.EntityId },
                { "entityId", grid.EntityId },
                { "name", Safe(grid.DisplayName) },
                { "position", Vec(position) },
                { "velocity", Vec(velocity) },
                { "forward", Vec(wm.Forward) },
                { "up", Vec(wm.Up) },
                { "sizeM", MaxDimension(grid.WorldAABB) },
                { "gridSize", SafeGridSize(grid) },
                { "blockCount", GetBlockCount(grid, frame) },
                { "incoming", incomingCount },
                { "locked", incomingCount > 0 },
                { "piloted", true },
                { "capabilities", new Dictionary<string, object>
                    {
                        { "zeoDirect", true },
                        { "weaponCore", _wc.Ready },
                        { "reporter", "ZeoCore v" + Plugin.Version }
                    }
                }
            };
            return row;
        }


        private void CollectWeaponCoreContacts(IMyPlayer player, IMyCubeGrid ownGrid, long focusId,
            List<object> contacts)
        {
            if (!_wc.Ready) return;

            for (int i = 0; i < _threats.Count; i++)
            {
                if (contacts.Count >= _config.MaxContacts) break;

                MyEntity entity = _threats[i].Item1;
                float threat = _threats[i].Item2;
                if (entity == null) continue;

                IMyCubeGrid grid = entity as IMyCubeGrid;
                if (grid == null)
                {
                    var block = entity as IMyCubeBlock;
                    if (block != null) grid = block.CubeGrid;
                }

                MyEntity normalized = grid != null ? grid as MyEntity : entity;
                if (normalized == null || normalized.EntityId == ownGrid.EntityId || _reportedIds.Contains(normalized.EntityId))
                    continue;

                string relation = grid == null ? "unknown" : RelationForGrid(player, grid);

                // v0.5.9: keep friendly WeaponCore observations as tactical
                // observations. They are still sent through contacts[] rather than
                // friendlies[], so they never become piloted Fleet members merely
                // because this client can see them. BattleSpace preserves the row's
                // explicit relation, and the scoped LIVE endpoint authorizes them by
                // reporting source. The stricter Fleet/Universe endpoint still requires
                // the entity itself to be an authenticated reporting source.
                contacts.Add(BuildTrackRow(normalized, grid, relation, threat,
                    normalized.EntityId == focusId, "weaponcore"));
                _reportedIds.Add(normalized.EntityId);
            }
        }

        private void CollectSpectrumSignals(List<object> contacts)
        {
            if (contacts == null || !_shareSpectrumSignals) return;

            List<SpectrumClient.DetectionData> snapshot;
            lock (_tacticalShareSync) snapshot = new List<SpectrumClient.DetectionData>(_sharedSpectrum);

            var reported = new Dictionary<long, Dictionary<string, object>>();
            foreach (object entry in contacts)
            {
                var row = entry as Dictionary<string, object>;
                object rawId;
                long id;
                if (row != null && row.TryGetValue("id", out rawId) && long.TryParse(Convert.ToString(rawId,
                    System.Globalization.CultureInfo.InvariantCulture), out id)) reported[id] = row;
            }
            for (int i = 0; i < snapshot.Count; i++)
            {
                SpectrumClient.DetectionData d = snapshot[i];
                if (d.SelfOwned || d.EmitterId == 0) continue;

                long spectrumId = d.EmitterId;
                Vector3D spectrumPosition = d.Position;
                NormalizeSpectrumGridIdentityForShare(d.EmitterId, ref spectrumId, ref spectrumPosition);
                if (double.IsNaN(d.Strength) || double.IsInfinity(d.Strength)) continue;
                Dictionary<string, object> existing;
                if (reported.TryGetValue(spectrumId, out existing))
                {
                    // Keep canonical identity, WC position and classification; add measured SIG attribution.
                    existing["signalStrength"] = d.Strength;
                    existing["rawEmitterId"] = d.EmitterId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    continue;
                }
                if (_reportedIds.Contains(spectrumId) || contacts.Count >= _config.MaxContacts) continue;

                string name = !string.IsNullOrWhiteSpace(d.DetailText) ? Safe(d.DetailText) :
                              (!string.IsNullOrWhiteSpace(d.FactionTag) ? Safe(d.FactionTag) : "SPECTRUM SIGNAL");

                AccountLinkSnapshot account = GetAccountLink();
                string ownFaction = account == null ? "" : Safe(account.FactionTag);
                string detectedFaction = Safe(d.FactionTag);
                string relation =
                    !string.IsNullOrWhiteSpace(ownFaction) &&
                    !string.IsNullOrWhiteSpace(detectedFaction) &&
                    string.Equals(ownFaction, detectedFaction, StringComparison.OrdinalIgnoreCase)
                        ? "friendly"
                        : "unknown";

                var row = new Dictionary<string, object>
                {
                    { "id", spectrumId },
                    { "entityId", spectrumId },
                    { "name", name },
                    { "position", Vec(spectrumPosition) },
                    { "velocity", Vec(d.Velocity) },
                    { "relation", relation },
                    { "type", "signal" },
                    { "sensor", "spectrum" },
                    { "signalStrength", d.Strength },
                    { "rawEmitterId", d.EmitterId.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                };
                if (!string.IsNullOrWhiteSpace(d.FactionTag)) row["factionTag"] = Safe(d.FactionTag);
                contacts.Add(row);
                reported[spectrumId] = row;
                _reportedIds.Add(spectrumId);
            }
        }

        private static void NormalizeSpectrumGridIdentityForShare(long emitterId, ref long entityId, ref Vector3D position)
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
                position = grid.WorldAABB.Center;
            }
            catch { }
        }

        private Dictionary<string, object> BuildTrackRow(MyEntity entity, IMyCubeGrid grid, string relation,
            float threat, bool focused, string sensor)
        {
            Vector3D position = entity.PositionComp != null ? entity.PositionComp.WorldAABB.Center : Vector3D.Zero;
            Vector3D velocity = Vector3D.Zero;
            try { if (entity.Physics != null) velocity = entity.Physics.LinearVelocity; } catch { }
            MatrixD wm = entity.WorldMatrix;

            string name = grid != null ? Safe(grid.DisplayName) : "CONTACT " + entity.EntityId;
            var row = new Dictionary<string, object>
            {
                { "id", entity.EntityId },
                { "entityId", entity.EntityId },
                { "name", name },
                { "position", Vec(position) },
                { "velocity", Vec(velocity) },
                { "forward", Vec(wm.Forward) },
                { "up", Vec(wm.Up) },
                { "sizeM", entity.PositionComp != null ? MaxDimension(entity.PositionComp.WorldAABB) : 0d },
                { "relation", relation },
                { "type", grid != null ? "grid" : "entity" },
                { "threat", threat },
                { "sensor", sensor }
            };
            if (focused) row["focus"] = "weaponcore";
            if (grid != null) row["gridSize"] = SafeGridSize(grid);
            return row;
        }

        private static string RelationForGrid(IMyPlayer player, IMyCubeGrid grid)
        {
            try
            {
                if (player == null || grid == null || grid.BigOwners == null || grid.BigOwners.Count == 0)
                    return "unknown";

                string value = player.GetRelationTo(grid.BigOwners[0]).ToString();
                if (value.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "hostile";
                if (value.IndexOf("Owner", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("Faction", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("Friend", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "friendly";
                return "unknown";
            }
            catch { return "unknown"; }
        }

        private bool TryControlledGrid(out IMyPlayer player, out IMyCubeGrid grid, out IMyCubeBlock controlBlock)
        {
            player = null;
            grid = null;
            controlBlock = null;

            try
            {
                var session = MyAPIGateway.Session;
                player = session == null ? null : session.Player;
                var controlled = player != null && player.Controller != null ? player.Controller.ControlledEntity : null;
                var entity = controlled == null ? null : controlled.Entity;

                controlBlock = entity as IMyCubeBlock;
                if (controlBlock != null)
                    grid = controlBlock.CubeGrid;
                if (grid == null)
                    grid = entity as IMyCubeGrid;

                return player != null && grid != null;
            }
            catch { return false; }
        }

        private int GetBlockCount(IMyCubeGrid grid, int frame)
        {
            if (frame - _blockCountFrame < 120 && _cachedBlockCount >= 0)
                return _cachedBlockCount;

            _blockCountFrame = frame;
            _selfBlocks.Clear();
            try { grid.GetBlocks(_selfBlocks); _cachedBlockCount = _selfBlocks.Count; }
            catch { _cachedBlockCount = 0; }
            return _cachedBlockCount;
        }

        private static string SafeGridSize(IMyCubeGrid grid)
        {
            try { return grid.GridSizeEnum.ToString(); } catch { return null; }
        }

        private static double MaxDimension(BoundingBoxD box)
        {
            Vector3D s = box.Size;
            return Math.Max(s.X, Math.Max(s.Y, s.Z));
        }

        private static double[] Vec(Vector3D v)
        {
            return new[] { v.X, v.Y, v.Z };
        }

        private static string Safe(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim();
            return value.Length <= 240 ? value : value.Substring(0, 240);
        }

        private void EnsureChat()
        {
            if (_chatRegistered || MyAPIGateway.Utilities == null) return;
            try
            {
                MyAPIGateway.Utilities.MessageEnteredSender -= OnChatMessage;
                MyAPIGateway.Utilities.MessageEnteredSender += OnChatMessage;
                _chatRegistered = true;
            }
            catch { }
        }

        private void OnChatMessage(ulong sender, string messageText, ref bool sendToOthers)
        {
            if (string.IsNullOrWhiteSpace(messageText)) return;
            string text = messageText.Trim();
            if (!text.StartsWith("/zeo", StringComparison.OrdinalIgnoreCase)) return;
            sendToOthers = false;

            string[] a = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "status";

            if (sub == "menu")
            {
                Plugin.OpenHudMenu();
                Plugin.Notify("Zeo menu open request sent to external overlay.", 3500);
                return;
            }

            if (sub == "link" || sub == "account")
            {
                AccountLinkSnapshot link = GetAccountLink();
                string code = string.IsNullOrWhiteSpace(link.PairingCode) ? "" : " | CODE=" + link.PairingCode;
                string faction = string.IsNullOrWhiteSpace(link.FactionTag) ? "" : " | FACTION=" + link.FactionTag;
                Plugin.Notify("Zeo OPEN TEST | AUTH DISABLED" + faction + " | no device linking required", 9000, "Green");
                return;
            }

            if (sub == "trust" || sub == "server")
            {
                ServerTrustSnapshot trust = GetServerTrust();
                string observed = string.IsNullOrWhiteSpace(trust.ObservedEndpoint) ?
                    (string.IsNullOrWhiteSpace(trust.ExpectedEndpoint) ? "N/A" : trust.ExpectedEndpoint) : trust.ObservedEndpoint;
                Plugin.Notify("DX CONTEXT ONLY // NOT A GATE | " + trust.State + " | " + trust.Sector + " | " + observed,
                              9000, "White");
                Plugin.Log("Trust diagnostic // state=" + trust.State + " sector=" + trust.Sector +
                           " session=" + (trust.SessionName ?? "") + " serverId=" + trust.ServerId +
                           " expected=" + (trust.ExpectedEndpoint ?? "") + " observed=" + (trust.ObservedEndpoint ?? "") +
                           " // " + trust.Detail);
                return;
            }

            if (sub == "status" || sub == "ver")
            {
                ServerTrustSnapshot trust = GetServerTrust();
                AccountLinkSnapshot account = GetAccountLink();
                string faction = GetLocalFactionTag();
                Plugin.Notify("ZEO " + Plugin.Version + " | " + faction + " | MODE=OPEN_TEST | AUTH=OFF | TX=" +
                              GetTelemetryDiagnostic() +
                              " | DXCTX=" + (trust.ServerId != 0 ? trust.Sector : "N/A"),
                              10000, "Green");
                return;
            }

            if (sub == "network" || sub == "net")
            {
                string diag = Plugin.HudDiagnostic();
                Plugin.Notify("Zeo Network | " + diag, 12000, "Green");
                Plugin.Log("Network diagnostic // " + diag);
                return;
            }

            if (sub == "sensors" || sub == "wc")
            {
                LocalHudSnapshot local = GetHudSnapshot();
                int spectrumCount = 0;
                lock (_tacticalShareSync) spectrumCount = _sharedSpectrum.Count;
                Plugin.Notify("Zeo Sensors | WC=" + (_wc.Ready ? "READY" : "OFF") +
                              " raw=" + (local == null ? 0 : local.WcRawThreatCount) +
                              " tracks=" + (local == null ? 0 : local.WcNormalizedTrackCount) +
                              " focus=" + (local == null || local.FocusEntityId == 0 ? "NONE" : local.FocusEntityId.ToString()) +
                              " fallback=" + (local != null && local.WcFocusFallbackAdded ? "YES" : "NO") +
                              " | Spectrum=" + spectrumCount +
                              " | Share WC=" + (_shareWeaponCoreContacts ? "ON" : "OFF") +
                              " SPEC=" + (_shareSpectrumSignals ? "ON" : "OFF"),
                              12000, _wc.Ready ? "Green" : "White");
                return;
            }

            if (sub == "gas" || sub == "h20")
            {
                string h20 = _shipH2O >= 0 ? (_shipH2O * 100.0).ToString("0.0") + "%" : "N/A";
                string o2 = _shipO2 >= 0 ? (_shipO2 * 100.0).ToString("0.0") + "%" : "N/A";
                Plugin.Notify("Zeo Gas | H20=" + h20 + " tanks=" + _shipH20TankCount +
                              " | O2=" + o2 + " tanks=" + _shipO2TankCount +
                              " | unknown gas tanks=" + _shipUnknownGasTankCount, 9000);
                return;
            }

            Plugin.Notify("ZeoCore: /zeo link, /zeo status, /zeo network, /zeo sensors, /zeo trust, /zeo gas, /zeo menu", 8000);
        }

        public void Dispose()
        {
            try
            {
                if (_chatRegistered && MyAPIGateway.Utilities != null)
                    MyAPIGateway.Utilities.MessageEnteredSender -= OnChatMessage;
            }
            catch { }
            _chatRegistered = false;
            try { _wc.Dispose(); } catch { }
            try { if (_account != null) _account.Dispose(); } catch { }
            _account = null;
            try { if (_deviceSender != null) _deviceSender.Dispose(); } catch { }
            _deviceSender = null;
        }
    }
}
