using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace ZeoPDC
{
    internal sealed partial class PdcEngine
    {
        // Replaced atomically before commands so shot callbacks need not enumerate tracks.
        private sealed class ShotContext
        {
            public int Track, Volley, LastObservedFrame, CommandFrame;
            public ulong Projectile;
            public ulong ObservedNativeProjectile;
            public string NativeRead;
            public int NativeFrame;
            public bool Allowed;
            public double Hull, Tti;
            public string Gate, Mode;
        }

        private sealed class Gun
        {
            public LabGunState Lab = new LabGunState();
            public LabGunState Manager = new LabGunState();
            public ClientGunState Client=new ClientGunState();
            public HeatRangeState HeatRange=new HeatRangeState();
            public WeaponProfile Profile = new WeaponProfile();
            public int ProfileFrame = -1;
            public string CadenceReason = "UNREAD", RangeReason = "UNREAD";
            public bool ProfileYielded, ManagedRofWrite;
            public bool RofOwned, RangeOwned;
            public double SavedRof, SavedRange, OwnedRof, OwnedRange;
            public PreemptivePolicy.State Outer = new PreemptivePolicy.State();
            public OuterAimAdapter.Lease OuterLease = new OuterAimAdapter.Lease();
            public OuterIntercept.Solution OuterSolution = new OuterIntercept.Solution();
            public bool OuterSelected, OuterPathClear;
            public ulong OuterTrack;
            public int OuterCycleStartFrame = -1;
            public int OuterAimStable, OuterReleaseFrame = -1, OuterNativeReacquiredFrame = -1;
            public long OuterCycleRequests, OuterReleases;
            public string OuterProfile = "UNREAD", OuterReleaseStatus = "UNOWNED";
            public bool OuterRangeOwned, OuterStopPending, NativeAligned, NativeManual, NativeControlKnown;
            public string OuterState = "OFF", NativeControlStatus = "UNREAD";
            public double OuterScopeError = double.NaN;
            public ulong NativeProjectile;
            public string NativeRead = "UNREAD";
            public IMySlimBlock Slim;
            public Sandbox.ModAPI.IMyTerminalBlock Block;
            public MyEntity Entity;
            public long EntityId;
            public int Part;
            public string Name;
            public double Hp;
            public double Heat;
            public bool HeatKnown;
            public BankRolePolicy.Slot BankRole = new BankRolePolicy.Slot();
            public int Ammo;
            public bool Functional;
            public bool TargetFlag1, TargetFlag2, TargetFlag3, ReadyValue, ShootingValue;
            public string TargetRead = "UNREAD", ReadyRead = "UNREAD", ShootingRead = "UNREAD";
            public bool RofReadOk, RangeReadOk, HpReadOk;
            public int TelemetryFrame = -1;
            public string CommandResult = "NOT_SENT";
            public int PreviousTrack;
            public bool PreviousAllowed;
            public string PreviousGate;
            public ShotContext Context;

            public bool TargetedActive;
            public string RequestResult = "NOT_USED";
            public bool TargetedConfig;
            public int LastObservedShotFrame = -100000, PriorOwner;
            public long ObservedShotMark;
            public bool ScopeValid;
            public Vector3D ScopeOrigin;
            public Vector3D ScopeDirection;
            public bool Allowed;
            public double Rof = 1.0;
            // Effective tracking range is read separately; it is not a hardware cap.
            public double DesiredRange = 3000.0;
            public int TrackId;
            public int DecisionTrackId;
            public string DecisionBasis = "NO_ACTIVE_DECISION";
            public double MatchError = double.PositiveInfinity;
            public double AlignmentDeg = 999.0;
            public double KillWindowScore;
            public string PkState = "HOLD";
            public bool ExactTargetMatch;
            public long Shots;
            public long WcTargetId;
            public string WcTargetType = "NONE";
            public Vector3D WcTargetPos;
            public bool WcTargetValid;
            public bool WcTargetIsGrid;
            public Vector3D WcPredictedPos;
            public bool WcPredictedValid;
            public bool PbTelemetry; // legacy field retained for CSV compatibility; unused in direct mode
            public double ActualRof = 1.0;
            public double ActualRange;
            public bool DirectConfigured;
            public bool ShotHooked;
            public Action<long, int, ulong, long, Vector3D, bool> ShotCallback;
            public readonly HashSet<ulong> SeenProjectiles = new HashSet<ulong>();
            public readonly Queue<ulong> SeenProjectileOrder = new Queue<ulong>();
            // Advisory expenditure from locally observed native ownership at both
            // ends of one allocator interval. Not shot-time attribution or hits.
            public long BudgetShotMark;
            public int BudgetTrackId;
            public bool BudgetWasAllowed;
            public ulong BudgetNativeProjectile;
            public int BudgetFrame = -1;
        }

        private sealed class Track
        {
            public int Id;
            public ThreatProfileObservation ThreatProfile;
            public int ThreatProfileFrame=-1;
            public double Health = double.NaN;
            public string NativeState, NativeStateRead = "UNREAD";
            public int NativeStateFrame = -100000;
            public string HealthRead = "UNREAD", HealthSource = "UNREAD", AmmoName = "";
            public int PositionObservationFrame = -1;
            public string PositionObservationSource = "NOT_RECORDED";
            public int HealthFrame = -100000;
            public Vector3D StateVelocity, Acceleration;
            public Vector3D Pos;
            public Vector3D PrevPos;
            public Vector3D Vel;
            public int FirstFrame;
            public int LastFrame;
            public int LostFrames;
            public double Range;
            public double Hull;
            public double PrevHull;
            public double HullClosing;
            public double MinHull = double.PositiveInfinity;
            public double Tti = double.PositiveInfinity;
            public bool Resolved;
            public string State = "TRACKING";
            public int Assigned;
            public bool PredictionLogged;
            public bool DangerEntered;
            public ulong ProjectileId;
            public bool StableId;
            public int VolleyIndex;
            public bool CountedPhysical;
            public bool InitialCohort;
            public bool OutcomeCounted;
            public int Reacquisitions;
            public int GoalOrdinal;
            public int ShotsSpent;
        }

        private sealed class PairCandidate
        {
            public Gun Gun;
            public double Miss = double.PositiveInfinity;
            public double AngleDeg = 999.0;
            public bool ExactTarget;
            public double Score;
        }

        private sealed class VolleyStats
        {
            public int Index;
            public int Seen;
            public int Resolved;
            public int Intercepts;
            public int Hits;
            public int Misses;
            public int Unknown;
            public int FirstFrame = -1;
            public int LastFrame = -1;
            public double StartHeat;
            public double EndHeat;
            public double PeakHeat;
            public long ShotsStart;
            public long ShotsEnd;
            public bool Written;
        }

        private readonly CoreSystemsApi wc;
        private readonly Func<PdcConfig> config;
        private readonly Action<string> log;
        private readonly string dataDir;
        private readonly List<IMyCubeGrid> constructGrids = new List<IMyCubeGrid>();
        private readonly List<Gun> guns = new List<Gun>();
        private readonly List<Track> tracks = new List<Track>();
        private readonly List<Vector3D> lockedPositions = new List<Vector3D>();
        private readonly List<MyTuple<ulong, Vector3D, int, long>> smartProjectiles = new List<MyTuple<ulong, Vector3D, int, long>>();
        private readonly Dictionary<ulong, Track> stableTrackMap = new Dictionary<ulong, Track>();
        private readonly Dictionary<int, VolleyStats> volleyStats = new Dictionary<int, VolleyStats>();
        private readonly object shotGate = new object();
        private readonly List<MyTuple<MyEntity, float>> threats = new List<MyTuple<MyEntity, float>>();
        private Sandbox.ModAPI.IMyShipController controller;
        private IMyCubeGrid grid;
        private MyEntity gridEntity;
        private Sandbox.ModAPI.IMyTerminalBlock bridgeBlock;
        private Sandbox.ModAPI.IMyTextPanel bridgePanel;
        private int frame;
        private int nextTrackId = 1;
        private long bridgeSeq;
        private long bridgeAckSeq;
        private int bridgeAckFrame = -100000;
        private string bridgePbMode = "NONE";
        private int bridgePbModeFrame = -100000;
        private int bridgeLastWriteFrame = -100000;
        private bool bridgeLastWriteOk;
        private string bridgeLastWriteError = "NEVER";
        private bool directControlActive;
        private bool nativeFallbackApplied;
        private string directControlState = "STARTING";
        private int directControlFailures;
        private int sensorGapCycles;
        private int stableFallbackGapFrames;
        private int fallbackTelemetrySeen;
        private int fallbackFused;
        private long directShotSerial;
        private int completedVolleys;
        private int lastCompletedVolley;
        private int lastScanFrame = -100000;
        private int lastApiGeneration;
        private string lastEvent = "STARTING";
        private bool testArmed;
        private bool finishRequested;
        private int finishFrame, clearSinceFrame = -1;
        public string RecorderState { get { return finishRequested ? "FINISH_PENDING" : testState; } }
        public string ExperimentMode { get { var c=config(); if(c.ManagedDefenseEnabled && !c.LabEnabled) return "MANAGER_NATIVE_DISTRIBUTION"; if(c.LabEnabled) return c.LabNativeOnly?"LAB_NATIVE_ROF_PREAIM":"LAB_MANAGED"; return c.NativeLeadEnabled ?
            (c.PreemptiveLookEnabled ? (c.PreemptiveFireEnabled ? "OUTER_BURSTS" : "PREAIM") : "NATIVE_LEAD") :
            (c.BypassAlignmentGate ? "B_GATE_BYPASS" : "A_ORIGINAL_GATE"); } }
        public void ReportSettingBlocked() { lastEvent = "Finish or stop the test before changing defense settings."; }

        private bool initialSnapshotTaken, stableSensorHealthy;
        private bool goalSensorGap;
        private int goalSeen;
        private const int GoalTarget = 160;

        private bool fireLockout;
        private string testState = "IDLE";
        private int waveId;
        private int waveSeen;
        private int waveResolved;
        private int intercepts;
        private int hits;
        private int misses;
        private int unknown;
        private int startAmmo;
        private long startShots;
        private long pbTotalShots;
        private double peakHeatWave;
        private int coolingStartFrame = -1;
        private long lastShotEventSerial;
        private TestRecorder recorder;
        private PdcConfig pendingArmRequest;
        public void CaptureArmRequest(PdcConfig requested) { pendingArmRequest = JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(requested)); }
        public void RecordConfiguration(PdcConfig requested)
        {
            try { recorder?.Configuration(config(), requested); liveFeed.ObserveConfiguration(config(), frame); }
            catch (Exception ex) { log("Configuration provenance failed: " + ex.Message); }
        }
        public void SetTelemetryTick(int currentFrame) { frame = currentFrame; }

        public PdcEngine(CoreSystemsApi api, Func<PdcConfig> configProvider, string root, Action<string> logger)
        {
            wc = api;
            config = configProvider;
            dataDir = root;
            liveFeed = new LiveTelemetryFeed(root);
            log = logger ?? delegate { };
        }

        public bool HasShip { get { return controller != null && grid != null; } }
        public string ShipName { get { try { return grid == null ? "NO SHIP" : (grid.DisplayName ?? "Controlled Ship"); } catch { return "Controlled Ship"; } } }
        public string LastEvent { get { return lastEvent; } }
        public bool TestArmed { get { return testArmed; } }
        public bool FireLockout { get { return fireLockout; } }
        public string TestState { get { return testState; } }
        public int WaveId { get { return waveId; } }
        public string TestFolder { get { return recorder == null ? "" : recorder.Folder; } }

        public void Update(int currentFrame)
        {
            frame = currentFrame;
            if (wc.Generation != lastApiGeneration)
            {
                lastApiGeneration = wc.Generation;
                lastScanFrame = -100000;
            }

            RefreshControlledShip();
            ServiceClientReturns();
            ServiceHeatRangeReturns();
            RefreshLiveContext();
            UpdateDecoyManager();
            if (!HasShip) return;
            if (testArmed && finishRequested && frame - finishFrame >= 3600) EndTest("FINISH_TIMEOUT");
            if (recorder != null && frame % 60 == 0) recorder.Flush();
            if (frame - lastScanFrame >= 180) ScanConstruct();

            // The plugin is now the PDC manager. The bridge is only a tiny optional
            // heartbeat for the PB watchdog; no gun command or test telemetry travels through it.
            if (frame % 12 == 0) WriteWatchdogHeartbeat();

            if (!wc.Ready)
            {
                if (testArmed) goalSensorGap = true;
                directControlActive = false;
                directControlState = "WC WAIT / NATIVE FALLBACK";
                FailOpenNative("WC WAIT");
                if (testArmed) UpdateTestState();
                return;
            }

            if ((frame & 1) == 0) UpdateTracks();
            if (frame % 6 == 0)
            {
                RefreshGunTelemetry();
                RefreshShipCadenceThreats();
                UpdateTestState();
                LabObserve();
                Allocate();
                ApplyPreemptiveLayer();
                ApplyDirectControl();
                ApplyHeatRangeBanks();
                UpdateOpenVolleyPeakHeat();
                if (testArmed && recorder != null && frame % 12 == 0)
                {
                    recorder.Sample(frame / 60.0, guns, ShipTurnDegPerSec());
                    recorder.TrackSample(frame / 60.0, tracks);
                    recorder.Diagnostics(frame, guns, ExperimentMode, directControlState);
                    ObserveBanks();
                }
                EvaluateFinish(stableSensorHealthy && directControlActive);
            }
            ServicePreemptiveStops();

        }

        private void EvaluateFinish(bool canConfirmClear)
        {
            if (!testArmed || !finishRequested) return;
            bool live = tracks.Any(t => !t.Resolved) || !canConfirmClear;
            if (live) clearSinceFrame = -1;
            else if (clearSinceFrame < 0) clearSinceFrame = frame;
            if (!live && frame - clearSinceFrame >= 60) EndTest("FINISHED_CLEAR");
            else if (frame - finishFrame >= 3600) EndTest("FINISH_TIMEOUT");
        }

        public void ArmTest()
        {
            if (testArmed) { lastEvent = "TEST ALREADY ACTIVE // finish or stop first"; return; }
            if (recorder != null) recorder.Close();
            finishRequested = false;
            clearSinceFrame = -1;
            if (!HasShip)
            {
                lastEvent = "TEST ARM REJECTED: NO CONTROLLED SHIP";
                return;
            }
            if (PhysicalPdcCount() == 0)
            {
                lastEvent = "TEST ARM REJECTED: NO PDCs // see %APPDATA%\\Pulsar\\ZeoPDC\\pdc-scan.txt";
                return;
            }

            ResetWaveCounters();
            waveId = Math.Max(1, waveId + 1);
            config().ControlEnabled = true;
            liveFeed.ObserveConfiguration(config(), frame);
            testArmed = true;
            fireLockout = false;
            testState = !wc.Ready ? "WAIT_WC" : (!wc.DirectControlReady ? "WAIT_DIRECT_API" : (directControlActive ? "ARMED_WAIT" : "WAIT_DIRECT_CONTROL"));
            startAmmo = TotalAmmo();
            startShots = pbTotalShots;
            peakHeatWave = HottestHeat();
            completedVolleys = 0;
            lastCompletedVolley = 0;
            volleyStats.Clear();
            stableFallbackGapFrames = 0;
            fallbackTelemetrySeen = 0;
            fallbackFused = 0;
            recorder = new TestRecorder(dataDir, waveId, config(), ShipName, wc.EndpointCount, log, () => frame, pendingArmRequest);
            pendingArmRequest = null;
            CopyTestDiagnostics(recorder);
            recorder.Inventory(guns);
            recorder.Event("TEST_ARM", "state=" + testState + " pdcBlocks=" + PhysicalPdcCount() + " weaponParts=" + guns.Count +
                " control=DIRECT expectedPerVolley=" + config().ExpectedInbound + " startHeat=" + peakHeatWave.ToString("0.0", CultureInfo.InvariantCulture));
            lastEvent = "DIRECT TEST ARMED // SESSION " + waveId + " // " + testState;
        }

        public void FinishTest()
        {
            if (!testArmed || finishRequested) return;
            finishRequested = true;
            finishFrame = frame;
            clearSinceFrame = -1;
            recorder.Event("FINISH_REQUESTED", "Stop launching. Recording continues until all incoming clears for one second; timeout is 60 simulation seconds.");
            lastEvent = "FINISH PENDING // STOP LAUNCHING // waiting for incoming to clear";
        }

        public void AbortTest() { EndTest("STOPPED_EARLY"); }

        private void EndTest(string reason)
        {
            lock (shotGate)
            {
                if (!testArmed) return;
                // Stop callbacks from adding session shots while totals are finalized.
                testArmed = false;
                finishRequested = false;
                if (recorder != null)
                {
                    recorder.Event("TEST_END", "reason=" + reason + " mode=" + ExperimentMode + " seen=" + waveSeen + " resolved=" + waveResolved);
                    recorder.FinalLedger(tracks, GoalText(), initialSnapshotTaken);
                    recorder.Unresolved(tracks, frame, reason);
                    recorder.SessionSummary(config().ExpectedInbound, completedVolleys, waveSeen, waveResolved, intercepts, hits, misses, unknown,
                        startAmmo, TotalAmmo(), Math.Max(peakHeatWave, HottestHeat()), startShots, pbTotalShots, guns, fallbackTelemetrySeen, fallbackFused);
                    recorder.Close();
                }
            }
            fireLockout = false;
            testState = "IDLE";
            // Keep active defense tracks intact when recording ends.
            lastEvent = "TEST SAVED // " + reason + " // defense remains active";
        }

        public void OpenTestFolder()
        {
            try
            {
                string p = recorder != null ? recorder.Folder : Path.Combine(dataDir, "Tests");
                Directory.CreateDirectory(p);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + p + "\"");
            }
            catch { }
        }

        private void RefreshControlledShip()
        {
            Sandbox.ModAPI.IMyShipController next = null;
            try
            {
                var ce = MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null ? null : MyAPIGateway.Session.Player.Controller == null ? null : MyAPIGateway.Session.Player.Controller.ControlledEntity;
                if (ce != null) next = ce.Entity as Sandbox.ModAPI.IMyShipController;
            }
            catch { }

            if (next == null || next.CubeGrid == null)
            {
                if (controller != null)
                {
                    EndTest("PLAYER_LEFT_CONTROL");
                    lastEvent = "PLAYER LEFT SHIP CONTROL // NATIVE WC RESTORED";
                    FailOpenNative("PLAYER LEFT CONTROL");
                    UnhookAllShots();
                }
                controller = null;
                grid = null;
                gridEntity = null;
                constructGrids.Clear();
                guns.Clear();
                tracks.Clear();
                stableTrackMap.Clear();
                bridgeBlock = null;
                bridgePanel = null;
                return;
            }

            if (controller == null || controller.EntityId != next.EntityId || grid == null || grid.EntityId != next.CubeGrid.EntityId)
            {
                if (controller != null)
                {
                    EndTest("SHIP_CHANGED");
                    FailOpenNative("SHIP CHANGE");
                    UnhookAllShots();
                }
                controller = next;
                grid = next.CubeGrid;
                gridEntity = grid as MyEntity;
                tracks.Clear();
                stableTrackMap.Clear();
                nextTrackId = 1;
                lastScanFrame = -100000;
                nativeFallbackApplied = false;
                directControlActive = false;
                lastEvent = "CONTROLLED SHIP -> " + ShipName;
                log(lastEvent + " grid=" + grid.EntityId);
            }
        }

        private void ScanConstruct()
        {
            lastScanFrame = frame;

            // Scan transaction guard: live MP/WeaponCore occasionally returns a transient
            // empty construct snapshot. v0.3.2b proved this can briefly erase all 12 PDCs
            // from the manager. Preserve the last known-good gun/bridge/construct set and
            // restore it if this scan produces zero PDCs while those entity references are
            // still alive. The next scheduled scan will try again normally.
            var priorGuns = new List<Gun>(guns);
            var priorConstructGrids = new List<IMyCubeGrid>(constructGrids);
            var priorBridgeBlock = bridgeBlock;
            var priorBridgePanel = bridgePanel;

            foreach (var prior in priorGuns) ReleaseOuter(prior, "RESCAN_RELEASE");
            foreach (var prior in priorGuns.Where(g => g.Outer.Active))
            {
                PreemptivePolicy.Stop(prior.Outer, frame, prior.Shots, config());
                prior.OuterStopPending = !wc.ToggleWeaponFire(prior.Entity, prior.Part, false);
            }

            UnhookAllShots();
            guns.Clear();
            bridgeBlock = null;
            bridgePanel = null;
            constructGrids.Clear();
            GetMechanicalConstructGrids(grid, controller, constructGrids);
            var seen = new HashSet<string>();
            var blocks = new List<IMySlimBlock>();
            var map = new Dictionary<string, int>();
            PdcConfig cfg = config();

            for (int gi = 0; gi < constructGrids.Count; gi++)
            {
                IMyCubeGrid member = constructGrids[gi];
                if (member == null || member.Closed) continue;
                blocks.Clear();
                try { member.GetBlocks(blocks, b => b != null && b.FatBlock != null); }
                catch { continue; }

                for (int bi = 0; bi < blocks.Count; bi++)
                {
                    IMySlimBlock slim = blocks[bi];
                    var fat = slim == null ? null : slim.FatBlock as Sandbox.ModAPI.IMyTerminalBlock;
                    if (fat == null) continue;

                    bool isBridge = fat.CustomName != null && fat.CustomName.IndexOf(cfg.BridgeTag, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isBridge)
                    {
                        if (bridgeBlock == null)
                        {
                            bridgeBlock = fat;
                            bridgePanel = fat as Sandbox.ModAPI.IMyTextPanel;
                        }
                        // The tag [ZPDC BRIDGE] itself contains "PDC". Never let
                        // the bridge LCD fall through into PDC text classification.
                        continue;
                    }

                    MyEntity entity = fat as MyEntity;
                    if (entity == null) continue;

                    // Do not make HasCoreWeaponBase a hard gate. On some live CoreSystems
                    // builds that endpoint can be absent/different even while the weapon-map
                    // and ammo endpoints work. Gather all evidence first, then classify.
                    bool wcWeapon = wc.HasCoreWeapon(entity);
                    map.Clear();
                    bool mapped = wc.TryGetWeaponMap(entity, map);
                    var parts = new HashSet<int>();
                    if (mapped)
                    {
                        foreach (var kv in map) parts.Add(kv.Value);
                    }
                    if (parts.Count == 0) parts.Add(0);

                    bool looksPdc = LooksLikePdc(fat, entity, parts, wcWeapon || (mapped && map.Count > 0));
                    if (!looksPdc) continue;

                    foreach (int part in parts) AddGun(slim, fat, entity, part, seen);
                }
            }

            // A successful rescan creates fresh Gun wrappers. Carry persistent shot and
            // terminal state forward so the 8/14/20-round stubborn-target budget does
            // not reset every 3-second scan and projectile-monitor dedupe stays intact.
            CarryPersistentGunState(priorGuns);

            guns.Sort(delegate(Gun a, Gun b)
            {
                int c = a.EntityId.CompareTo(b.EntityId);
                return c != 0 ? c : a.Part.CompareTo(b.Part);
            });

            bool retainedTransientZero = false;
            if (guns.Count == 0 && priorGuns.Count > 0)
            {
                bool anyPriorAlive = false;
                for (int i = 0; i < priorGuns.Count; i++)
                {
                    Gun pg = priorGuns[i];
                    if (pg != null && pg.Entity != null && !pg.Entity.Closed)
                    {
                        anyPriorAlive = true;
                        break;
                    }
                }
                if (anyPriorAlive)
                {
                    guns.AddRange(priorGuns);
                    constructGrids.Clear();
                    constructGrids.AddRange(priorConstructGrids);
                    bridgeBlock = priorBridgeBlock;
                    bridgePanel = priorBridgePanel;
                    retainedTransientZero = true;
                }
            }

            foreach (var removed in priorGuns.Where(g => g.OuterRangeOwned && !guns.Any(n => n.EntityId == g.EntityId && n.Part == g.Part)))
            {
                ApplyGunRange(removed, cfg.EngagementRangeMeters);
                removed.OuterRangeOwned = false;
            }
            RefreshGunTelemetry();
            HookDirectShotMonitors();
            WritePdcScanCensus();
            int blocksFound = PhysicalPdcCount();
            lastEvent = (retainedTransientZero ? "PDC SCAN_TRANSIENT_ZERO_RETAINED // " : "PDC SCAN // ") +
                "grids=" + constructGrids.Count + " blocks=" + blocksFound + " parts=" + guns.Count +
                " direct=" + (wc.DirectControlReady ? "READY" : "WAIT") + " watchdog=" + (bridgeBlock == null ? "OPTIONAL/MISSING" : "FOUND");
            log(lastEvent);
            if (recorder != null) recorder.Event("SCAN", lastEvent);
        }

        private void CarryPersistentGunState(List<Gun> prior)
        {
            if (prior == null || prior.Count == 0 || guns.Count == 0) return;
            for (int i = 0; i < guns.Count; i++)
            {
                Gun g = guns[i];
                Gun old = null;
                for (int j = 0; j < prior.Count; j++)
                {
                    Gun p = prior[j];
                    if (p != null && p.EntityId == g.EntityId && p.Part == g.Part)
                    {
                        old = p;
                        break;
                    }
                }
                if (old == null) continue;
                g.Outer = old.Outer;
                g.OuterCycleRequests = old.OuterCycleRequests; g.OuterReleases = old.OuterReleases;
                g.OuterReleaseFrame = old.OuterReleaseFrame; g.OuterReleaseStatus = old.OuterReleaseStatus;
                g.OuterNativeReacquiredFrame = old.OuterNativeReacquiredFrame;
                g.OuterRangeOwned = old.OuterRangeOwned;
                g.OuterStopPending = old.OuterStopPending;
                g.OuterState = old.OuterState;
                g.Shots = old.Shots;
                g.BudgetShotMark = old.BudgetShotMark;
                g.BudgetTrackId = old.BudgetTrackId;
                g.BudgetWasAllowed = old.BudgetWasAllowed;
                g.BudgetNativeProjectile = old.BudgetNativeProjectile;
                g.BudgetFrame = old.BudgetFrame;
                g.Lab = old.Lab; g.Manager=old.Manager;g.Client=old.Client;g.HeatRange=old.HeatRange;
                g.Profile = old.Profile; g.ProfileFrame = old.ProfileFrame;
                g.ProfileYielded = old.ProfileYielded;
                g.RofOwned = old.RofOwned; g.RangeOwned = old.RangeOwned;
                g.SavedRof = old.SavedRof; g.SavedRange = old.SavedRange;
                g.OwnedRof = old.OwnedRof; g.OwnedRange = old.OwnedRange;
                g.ActualRof = old.ActualRof;
                g.ActualRange = old.ActualRange;
                g.DesiredRange = old.DesiredRange;
                g.BankRole = old.BankRole;
                g.DirectConfigured = old.DirectConfigured;
                foreach (ulong id in old.SeenProjectileOrder)
                {
                    if (!g.SeenProjectiles.Add(id)) continue;
                    g.SeenProjectileOrder.Enqueue(id);
                }
            }
        }

        private void AddGun(IMySlimBlock slim, Sandbox.ModAPI.IMyTerminalBlock fat, MyEntity entity, int part, HashSet<string> seen)
        {
            string key = entity.EntityId.ToString(CultureInfo.InvariantCulture) + ":" + part;
            if (!seen.Add(key)) return;
            guns.Add(new Gun
            {
                Slim = slim,
                Block = fat,
                Entity = entity,
                EntityId = entity.EntityId,
                Part = part,
                Name = string.IsNullOrWhiteSpace(fat.CustomName) ? fat.BlockDefinition.SubtypeName : fat.CustomName
            });
        }

        private bool LooksLikePdc(Sandbox.ModAPI.IMyTerminalBlock block, MyEntity entity, IEnumerable<int> parts, bool weaponEvidence)
        {
            string text = ((block.CustomName ?? "") + " " + block.DefinitionDisplayNameText + " " + block.BlockDefinition.SubtypeName).ToLowerInvariant();
            if (PdcTextMatch(text)) return true;

            // Ammo is only accepted as PDC evidence when CoreSystems has already told us
            // this is a weapon/mapped weapon. This avoids cargo containers named for ammo.
            if (weaponEvidence)
            {
                foreach (int part in parts)
                {
                    string ammo = (wc.GetActiveAmmo(entity, part) ?? "").ToLowerInvariant();
                    if (PdcAmmoMatch(ammo)) return true;
                }
            }
            return false;
        }

        private static bool PdcTextMatch(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Contains("pdc") || text.Contains("point defense") || text.Contains("point defence")) return true;
            if (text.Contains("pdcmcrnadv") || text.Contains("pdcmcrn") || text.Contains("pdcimprovised")) return true;
            string[] known = { "protogen", "maegnas", "maegnus", "ostman", "jazinski",
                               "mikazuki", "redfield", "nariman", "hashari" };
            for (int i = 0; i < known.Length; i++) if (text.Contains(known[i])) return true;
            return false;
        }

        private static bool PdcAmmoMatch(string ammo)
        {
            if (string.IsNullOrEmpty(ammo)) return false;
            return ammo.Contains("50mm light") || ammo.Contains("50mm heavy") ||
                   ammo.Contains("50mm pdc") || ammo.Contains("40mm pdc") ||
                   ammo.Contains("40mm lead-steel") || ammo.Contains("pdc50mm") ||
                   ammo.Contains("pdc40mm");
        }

        private int PhysicalPdcCount()
        {
            return guns.Select(g => g.EntityId).Distinct().Count();
        }

        private int PhysicalOnlineCount()
        {
            return guns.GroupBy(g => g.EntityId).Count(grp => grp.Any(g => g.Functional));
        }

        private void WritePdcScanCensus()
        {
            try
            {
                var sb = new StringBuilder(32768);
                sb.AppendLine("ZEO PDC // PDC SCAN CENSUS");
                sb.AppendLine("Version=0.3.30-TIGHT-BANKS");
                sb.AppendLine("Ship=" + ShipName);
                sb.AppendLine("PhysicalPdcBlocks=" + PhysicalPdcCount());
                sb.AppendLine("WeaponParts=" + guns.Count);
                sb.AppendLine("CoreSystemsReady=" + wc.Ready + " endpoints=" + wc.EndpointCount);
                sb.AppendLine("DirectControlReady=" + wc.DirectControlReady);
                sb.AppendLine("ShotMonitorReady=" + wc.ShotMonitorReady);
                sb.AppendLine("StableProjectileTracking=" + wc.StableProjectileReady);
                sb.AppendLine();
                sb.AppendLine("ACCEPTED PDC BLOCKS");
                foreach (var grp in guns.GroupBy(g => g.EntityId).OrderBy(g => g.Key))
                {
                    Gun first = grp.First();
                    sb.Append("ACCEPT id=").Append(grp.Key)
                      .Append(" name=").Append(first.Name)
                      .Append(" subtype=").Append(first.Block.BlockDefinition.SubtypeName)
                      .Append(" parts=").Append(string.Join(",", grp.Select(x => x.Part).OrderBy(x => x)))
                      .Append(" ammo=");
                    bool firstAmmo = true;
                    foreach (Gun g in grp.OrderBy(x => x.Part))
                    {
                        string ammo = wc.GetActiveAmmo(g.Entity, g.Part) ?? "";
                        if (!firstAmmo) sb.Append(";");
                        firstAmmo = false;
                        sb.Append(g.Part).Append(":").Append(ammo);
                    }
                    sb.AppendLine();
                }

                sb.AppendLine();
                sb.AppendLine("CANDIDATE / WC WEAPON AUDIT");
                int listed = 0;
                var blocks = new List<IMySlimBlock>();
                var map = new Dictionary<string, int>();
                for (int gi = 0; gi < constructGrids.Count && listed < 600; gi++)
                {
                    IMyCubeGrid member = constructGrids[gi];
                    if (member == null || member.Closed) continue;
                    blocks.Clear();
                    try { member.GetBlocks(blocks, b => b != null && b.FatBlock != null); }
                    catch { continue; }
                    for (int bi = 0; bi < blocks.Count && listed < 600; bi++)
                    {
                        IMySlimBlock slim = blocks[bi];
                        var fat = slim == null ? null : slim.FatBlock as Sandbox.ModAPI.IMyTerminalBlock;
                        MyEntity entity = fat as MyEntity;
                        if (fat == null || entity == null) continue;
                        bool isBridgeAudit = fat.CustomName != null && fat.CustomName.IndexOf(config().BridgeTag, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (isBridgeAudit)
                        {
                            listed++;
                            sb.Append("REJECT_BRIDGE id=").Append(entity.EntityId)
                              .Append(" name=").Append(fat.CustomName ?? "")
                              .Append(" subtype=").Append(fat.BlockDefinition.SubtypeName)
                              .AppendLine(" reason=BRIDGE_TAG");
                            continue;
                        }
                        string text = ((fat.CustomName ?? "") + " " + fat.DefinitionDisplayNameText + " " + fat.BlockDefinition.SubtypeName).ToLowerInvariant();
                        bool textMatch = PdcTextMatch(text);
                        bool wcWeapon = wc.HasCoreWeapon(entity);
                        map.Clear();
                        bool mapped = wc.TryGetWeaponMap(entity, map);
                        var parts = new HashSet<int>();
                        if (mapped) foreach (var kv in map) parts.Add(kv.Value);
                        if (parts.Count == 0) parts.Add(0);
                        bool ammoMatch = false;
                        var ammoText = new StringBuilder();
                        foreach (int part in parts.OrderBy(x => x))
                        {
                            string ammo = wc.GetActiveAmmo(entity, part) ?? "";
                            if (ammoText.Length > 0) ammoText.Append(';');
                            ammoText.Append(part).Append(':').Append(ammo);
                            if (PdcAmmoMatch(ammo.ToLowerInvariant())) ammoMatch = true;
                        }
                        bool weaponEvidence = wcWeapon || (mapped && map.Count > 0);
                        bool accept = textMatch || (weaponEvidence && ammoMatch);
                        if (!textMatch && !wcWeapon && !(mapped && map.Count > 0) && !ammoMatch) continue;
                        listed++;
                        sb.Append(accept ? "ACCEPT_CANDIDATE " : "REJECT_CANDIDATE ")
                          .Append("id=").Append(entity.EntityId)
                          .Append(" wc=").Append(wcWeapon ? '1' : '0')
                          .Append(" mapped=").Append(mapped ? '1' : '0')
                          .Append(" mapCount=").Append(map.Count)
                          .Append(" textMatch=").Append(textMatch ? '1' : '0')
                          .Append(" ammoMatch=").Append(ammoMatch ? '1' : '0')
                          .Append(" name=").Append(fat.CustomName ?? "")
                          .Append(" display=").Append(fat.DefinitionDisplayNameText ?? "")
                          .Append(" subtype=").Append(fat.BlockDefinition.SubtypeName)
                          .Append(" ammo=").Append(ammoText)
                          .AppendLine();
                    }
                }
                if (listed >= 600) sb.AppendLine("AUDIT_TRUNCATED=600");
                File.WriteAllText(Path.Combine(dataDir, "pdc-scan.txt"), sb.ToString());
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(dataDir, "pdc-scan.txt"), "PDC SCAN CENSUS FAILED: " + ex); } catch { }
            }
        }

        private void RefreshGunTelemetry()
        {
            for (int i = 0; i < guns.Count; i++)
            {
                Gun g = guns[i];
                try
                {
                    var fb = g.Block as Sandbox.ModAPI.IMyFunctionalBlock;
                    g.Functional = fb != null && fb.IsFunctional && fb.IsWorking && fb.Enabled;
                }
                catch { g.Functional = false; }

                try
                {
                    double max = Math.Max(1e-6, g.Slim.MaxIntegrity);
                    g.Hp = 100.0 * Math.Max(0, g.Slim.BuildIntegrity - g.Slim.CurrentDamage) / max;
                    g.HpReadOk = true;
                }
                catch { g.Hp = 0; g.HpReadOk = false; }

                float h = wc.GetWeaponHeat(g.Entity, g.Part);
                int mh = wc.GetMaxWeaponHeat(g.Entity, g.Part);
                float mr = wc.GetMaxWeaponRange(g.Entity, g.Part);
                g.DesiredRange = TrackingRangePolicy.Request(config().EngagementRangeMeters);
                double bankHeatRead;
                g.HeatKnown = wc.TryReadHeatPercent(g.Entity, g.Part, out bankHeatRead);
                g.Heat = mh > 0 ? Math.Max(0, Math.Min(100, 100.0 * h / mh)) : 0;
                if (g.HeatKnown) g.Heat = Math.Max(0,Math.Min(100,bankHeatRead));
                int ammo = wc.GetAmmoCount(g.Entity, g.Part);
                g.Ammo = ammo >= 0 ? ammo : InventoryAmount(g.Block);
                if (testArmed && g.Heat > peakHeatWave) peakHeatWave = g.Heat;

                Vector3D o, d;
                g.ScopeValid = wc.TryGetWeaponScope(g.Entity, g.Part, out o, out d);
                if (g.ScopeValid) { g.ScopeOrigin = o; g.ScopeDirection = d; }

                MyEntity target;
                g.NativeRead = wc.NativeObserver.Read(g.Entity, g.Part, out g.NativeProjectile);
                g.Profile = wc.NativeObserver.ReadProfile(g.Entity, g.Part);
                g.ProfileFrame = frame;
                g.NativeControlStatus = wc.NativeObserver.ReadControl(g.Entity, g.Part, out g.NativeAligned, out g.NativeManual);
                g.NativeControlKnown = g.NativeControlStatus == "OK";
                if (!g.OuterSelected && g.OuterReleaseFrame >= 0 && g.OuterNativeReacquiredFrame < 0 && g.NativeRead == "PROJECTILE")
                    g.OuterNativeReacquiredFrame = frame;
                g.TargetRead = wc.ReadTargetTelemetry(g.Entity, g.Part, out g.TargetFlag1, out g.TargetFlag2, out g.TargetFlag3, out target);
                g.WcTargetValid = g.TargetRead == "OK" && target != null;
                g.WcTargetIsGrid = target is IMyCubeGrid || target is IMyCubeBlock;
                g.ReadyRead = wc.ReadFireTelemetry(g.Entity, g.Part, true, out g.ReadyValue);
                g.ShootingRead = wc.ReadFireTelemetry(g.Entity, g.Part, false, out g.ShootingValue);
                g.WcTargetId = target == null ? 0 : target.EntityId;
                g.WcTargetType = target != null ? target.GetType().Name : g.TargetRead != "OK" ? "UNAVAILABLE" :
                    g.TargetFlag2 ? "PROJECTILE_ID_UNAVAILABLE" : g.TargetFlag3 ? "FAKE_TARGET" : g.TargetFlag1 ? "TARGET_ID_UNAVAILABLE" : "NONE";
                if (target != null)
                {
                    try { g.WcTargetPos = target.PositionComp == null ? target.WorldMatrix.Translation : target.PositionComp.WorldAABB.Center; } catch { g.WcTargetPos = target.WorldMatrix.Translation; }
                    Vector3D pred;
                    g.WcPredictedValid = wc.TryGetPredictedTargetPosition(g.Entity, target, g.Part, out pred);
                    if (g.WcPredictedValid) g.WcPredictedPos = pred;
                }
                else
                {
                    g.WcTargetPos = Vector3D.Zero;
                    g.WcPredictedValid = false;
                    g.WcPredictedPos = Vector3D.Zero;
                }

                float rofRead; double rangeRead;
                g.RofReadOk = TryReadTerminal(g.Block, "Weapon ROF", out rofRead);
                g.RangeReadOk = TrackingRangePolicy.TryObserved(mr, out rangeRead);
                if (g.RofReadOk) g.ActualRof = rofRead;
                if (g.RangeReadOk) g.ActualRange = rangeRead;
                g.TelemetryFrame = frame;
                if (!g.ShotHooked && wc.ShotMonitorReady) HookDirectShotMonitor(g);
            }
        }

        private static int InventoryAmount(Sandbox.ModAPI.IMyTerminalBlock block)
        {
            try
            {
                double total = 0;
                var items = new List<MyInventoryItem>();
                for (int i = 0; i < block.InventoryCount; i++)
                {
                    items.Clear();
                    block.GetInventory(i).GetItems(items);
                    for (int j = 0; j < items.Count; j++) total += (double)items[j].Amount;
                }
                return (int)Math.Min(int.MaxValue, Math.Max(0, Math.Round(total)));
            }
            catch { return 0; }
        }

        private void UpdateTracks()
        {
            if (gridEntity == null) return;

            PdcConfig cfg = config();
            lockedPositions.Clear();
            wc.GetLockedPositions(gridEntity, lockedPositions);

            // Preferred path: stable CoreSystems smart-projectile IDs. v0.3.4 treats
            // this path as authoritative while it is healthy. The v0.3.2 recorder
            // created fallback position twins for already-known stable projectiles;
            // those twins also consumed scheduler seats. That is now prevented.
            smartProjectiles.Clear();
            stableSensorHealthy = wc.StableProjectileReady && wc.TryGetAllSmartProjectiles(smartProjectiles);
            if (testArmed && !stableSensorHealthy) goalSensorGap = true;
            // A failed enumeration is not evidence that every incoming projectile died.
            if (wc.StableProjectileReady && !stableSensorHealthy) { if (testArmed) goalSensorGap = true; return; }
            if (testArmed || config().ContinuousTelemetryEnabled || config().ManagedDefenseEnabled || config().PreemptiveLookEnabled) wc.NativeProjectiles.Capture(smartProjectiles);
            bool initialPoll = testArmed && stableSensorHealthy && !initialSnapshotTaken;

            var matchedStable = new Dictionary<ulong, Vector3D>();
            var observedStates = new Dictionary<ulong, CoreSystemsApi.ProjectileObservation>();
            var positionSources = new Dictionary<ulong, string>();
            var usedLocked = new bool[lockedPositions.Count];
            var constructIds = new HashSet<long>();
            for (int i = 0; i < constructGrids.Count; i++) if (constructGrids[i] != null) constructIds.Add(constructGrids[i].EntityId);
            if (grid != null) constructIds.Add(grid.EntityId);

            for (int si = 0; si < smartProjectiles.Count; si++)
            {
                var sp = smartProjectiles[si];
                ulong id = sp.Item1;
                if (id == 0) continue;
                Vector3D pos = sp.Item2;
                var observation = wc.ObserveProjectile(id);
                bool haveState = observation.Status == "OK";
                long targetId = observation.Target;
                if (haveState) pos = observation.Position;
                positionSources[id] = haveState ? "GetProjectileState" : "GetAllSmartProjectiles";
                // Keep original public sensor data for defense matching/positions.
                // The native fallback feeds observer health/velocity only.
                observedStates[id] = (testArmed || config().ContinuousTelemetryEnabled || config().ManagedDefenseEnabled || config().PreemptiveLookEnabled) && observation.Status != "OK" ? wc.NativeProjectiles.Read(id) : observation;

                bool targetMatches = targetId != 0 && constructIds.Contains(targetId);
                int best = -1; double bestD = double.MaxValue;
                for (int li = 0; li < lockedPositions.Count; li++)
                {
                    if (usedLocked[li]) continue;
                    double d = Vector3D.Distance(pos, lockedPositions[li]);
                    if (d < bestD) { bestD = d; best = li; }
                }
                bool positionMatches = best >= 0 && bestD <= 180.0;
                // Keep already identified threats while they turn or change focus.
                if (!targetMatches && !positionMatches && !stableTrackMap.ContainsKey(id)) continue;
                if (positionMatches) usedLocked[best] = true;
                matchedStable[id] = pos;
            }

            foreach (var kv in matchedStable)
            {
                Track t;
                if (stableTrackMap.TryGetValue(kv.Key, out t))
                {
                    ReopenTrack(t);
                    UpdateTrack(t, kv.Value);
                }
                else
                {
                    t = new Track
                    {
                        Id = nextTrackId++, ProjectileId = kv.Key, StableId = true, CountedPhysical = true, InitialCohort = initialPoll,
                        Pos = kv.Value, PrevPos = kv.Value, FirstFrame = frame, LastFrame = frame, LostFrames = 0
                    };
                    UpdateTrackGeometry(t, true);
                    tracks.Add(t);
                    stableTrackMap[kv.Key] = t;
                    RegisterNewTrack(t);
                }
                t.PositionObservationFrame = frame;
                t.PositionObservationSource = positionSources[kv.Key];
                var observation = observedStates[kv.Key];
                // Baseline/inner decisions remain independent. Optional outer bursts
                // require a fresh Alive state; this observation never establishes kills.
                var nativeState = wc.NativeProjectiles.Read(kv.Key);
                t.ThreatProfile=nativeState.ThreatProfile; t.ThreatProfileFrame=frame;
                t.NativeState = nativeState.NativeState;
                t.NativeStateRead = nativeState.NativeStateStatus ?? nativeState.Status;
                t.NativeStateFrame = t.NativeStateRead == "OK" ? frame : -100000;
                if (t.StableId) liveFeed.ObserveNativeState(frame, liveTrackerEpoch, t.Id, t.ProjectileId,
                    t.NativeState, t.NativeStateRead, t.NativeStateFrame);
                if (observation.Status == "OK")
                {
                    if (testArmed && recorder != null && (t.HealthRead != "OK" || t.Health != observation.Health))
                        recorder.ObserverRow("health_events.csv", frame / 60.0, t.Id, t.ProjectileId, t.HealthRead == "OK" ? t.Health : double.NaN,
                            observation.Health, t.HealthRead == "OK" ? t.Health - observation.Health : double.NaN, "UNATTRIBUTED_HEALTH_CHANGE", observation.Source);
                    double dt = (frame - t.HealthFrame) / 60.0;
                    t.Acceleration = t.HealthRead == "OK" && dt > 0 && dt <= .2 ? (observation.Velocity - t.StateVelocity) / dt : Vector3D.Zero;
                    t.StateVelocity = observation.Velocity; t.Health = observation.Health;
                    t.HealthFrame = frame; t.AmmoName = observation.Ammo;
                }
                else { t.Health = double.NaN; t.Acceleration = Vector3D.Zero; }
                t.HealthRead = observation.Status;
                t.HealthSource = observation.Source;
            }

            for (int i = 0; i < tracks.Count; i++)
            {
                Track t = tracks[i];
                if (t.Resolved || !t.StableId || !stableSensorHealthy) continue;
                if (matchedStable.ContainsKey(t.ProjectileId)) continue;
                t.LostFrames += 2;
                if (t.LostFrames / 60.0 >= cfg.TrackLostSeconds) ResolveLostTrack(t);
            }

            if (initialPoll) initialSnapshotTaken = true;
            // Tracks first observed during WAIT_DIRECT_CONTROL must still enter accounting.
            if (testArmed) foreach (Track pending in tracks)
                if (!pending.Resolved && pending.VolleyIndex == 0 && pending.CountedPhysical) RegisterNewTrack(pending);

            bool stableAuthority = cfg.StableIdAuthoritative && wc.StableProjectileReady;
            bool stableHealthy = stableAuthority && (matchedStable.Count > 0 || tracks.Any(t => t != null && !t.Resolved && t.StableId));
            if (stableAuthority && lockedPositions.Count > 0 && !stableHealthy) stableFallbackGapFrames += 2;
            else stableFallbackGapFrames = 0;

            // Stable IDs remain authoritative for physical accounting, but safety wins:
            // if WeaponCore reports a locked projectile that cannot be matched to any
            // current/recent stable-ID position, keep an UNCOUNTED position fallback in
            // the defense queue. Position twins near a stable track are fused/suppressed.
            if (stableHealthy)
            {
                for (int i = 0; i < tracks.Count; i++)
                {
                    Track t = tracks[i];
                    if (t == null || t.Resolved || t.StableId) continue;
                    if (!NearStableTrack(t.Pos, 350.0, 30)) continue;
                    t.Resolved = true;
                    t.State = "FUSED_STABLE";
                    t.LastFrame = frame;
                    fallbackFused++;
                    if (recorder != null)
                        recorder.Event("TRACK_FUSED", "T" + t.Id.ToString("00") + " fallback suppressed by stable-ID proximity");
                }
            }

            bool unmatchedLocked = false;
            for (int pi = 0; pi < lockedPositions.Count; pi++)
            {
                if (usedLocked[pi]) continue;
                if (stableAuthority && NearStableTrack(lockedPositions[pi], 350.0, 30)) continue;
                unmatchedLocked = true;
                break;
            }

            bool allowFallback = !stableAuthority ||
                (unmatchedLocked && (stableHealthy || stableFallbackGapFrames / 60.0 >= cfg.StableFallbackGraceSeconds));

            if (allowFallback)
            {
                double dtDefault = 2.0 / 60.0;
                for (int ti = 0; ti < tracks.Count; ti++)
                {
                    Track t = tracks[ti];
                    if (t.Resolved || t.StableId) continue;
                    int best = -1; double bestD = double.MaxValue;
                    double dt = Math.Max(dtDefault, (frame - t.LastFrame) / 60.0);
                    Vector3D predicted = t.Pos + t.Vel * dt;
                    double gate = Math.Max(80.0, t.Vel.Length() * dt * 2.5 + 35.0);
                    for (int pi = 0; pi < lockedPositions.Count; pi++)
                    {
                        if (usedLocked[pi]) continue;
                        if (stableAuthority && NearStableTrack(lockedPositions[pi], 350.0, 30)) continue;
                        double d = Vector3D.Distance(predicted, lockedPositions[pi]);
                        if (d < bestD && d <= gate) { bestD = d; best = pi; }
                    }
                    if (best >= 0)
                    {
                        usedLocked[best] = true;
                        UpdateTrack(t, lockedPositions[best]);
                    }
                    else
                    {
                        t.LostFrames += 2;
                        if (t.LostFrames / 60.0 >= cfg.TrackLostSeconds) ResolveLostTrack(t);
                    }
                }

                for (int pi = 0; pi < lockedPositions.Count; pi++)
                {
                    if (usedLocked[pi]) continue;
                    if (stableAuthority && NearStableTrack(lockedPositions[pi], 350.0, 30)) continue;
                    Track t = new Track
                    {
                        Id = nextTrackId++, StableId = false, ProjectileId = 0,
                        PositionObservationFrame = frame, PositionObservationSource = "GetProjectilesLockedOnPos",
                        CountedPhysical = !stableAuthority,
                        Pos = lockedPositions[pi], PrevPos = lockedPositions[pi],
                        FirstFrame = frame, LastFrame = frame, LostFrames = 0
                    };
                    UpdateTrackGeometry(t, true);
                    tracks.Add(t);
                    fallbackTelemetrySeen++;
                    RegisterNewTrack(t);
                }
            }

            if (!testArmed)
            {
                foreach (Track retired in tracks.Where(t => t.Resolved && frame - t.LastFrame > 600).ToList())
                {
                    if (retired.StableId) stableTrackMap.Remove(retired.ProjectileId);
                    tracks.Remove(retired);
                }
            }
        }

        private bool NearStableTrack(Vector3D pos, double maxDistance, int recentFrames)
        {
            double max2 = maxDistance * maxDistance;
            for (int i = 0; i < tracks.Count; i++)
            {
                Track s = tracks[i];
                if (s == null || !s.StableId || s.ProjectileId == 0) continue;
                if (s.Resolved && frame - s.LastFrame > recentFrames) continue;
                if (Vector3D.DistanceSquared(pos, s.Pos) <= max2) return true;
            }
            return false;
        }

        private void RegisterNewTrack(Track t)
        {
            if (t == null || t.VolleyIndex > 0 || !testArmed || !(testState == "ARMED_WAIT" || testState == "ACTIVE")) return;

            // Stable-ID mode is the physical accounting authority. Short fallback
            // trackers may still exist during an API gap for defense, but they must
            // never inflate volley counts or steal the meaning of "32 torpedoes".
            if (!t.CountedPhysical)
            {
                t.VolleyIndex = 0;
                if (recorder != null)
                    recorder.Event("TRACK_FALLBACK_UNCOUNTED", TrackText(t) + " projectile=fallback");
                return;
            }

            if (testState == "ARMED_WAIT")
            {
                testState = "ACTIVE";
                startAmmo = TotalAmmo();
                peakHeatWave = HottestHeat();
            }

            int perVolley = Math.Max(1, config().ExpectedInbound);
            int volley = (waveSeen / perVolley) + 1;
            t.VolleyIndex = volley;
            VolleyStats vs = GetVolleyStats(volley);
            if (vs.Seen == 0)
            {
                vs.FirstFrame = frame;
                vs.ShotsStart = pbTotalShots;
                vs.StartHeat = HottestHeat();
                vs.EndHeat = vs.StartHeat;
                vs.PeakHeat = vs.StartHeat;
                if (recorder != null) recorder.Event("VOLLEY_START", "volley=" + volley + " startHeat=" + vs.PeakHeat.ToString("0.0", CultureInfo.InvariantCulture) + " totalSeen=" + waveSeen);
            }
            vs.Seen++;
            waveSeen++;
            if (t.StableId && !t.InitialCohort && goalSeen < GoalTarget) t.GoalOrdinal = ++goalSeen;
            if (recorder != null) recorder.Event("TRACK_NEW", "V" + volley + " " + TrackText(t) + " projectile=" + (t.StableId ? t.ProjectileId.ToString(CultureInfo.InvariantCulture) : "fallback"));
        }

        private void UpdateTrack(Track t, Vector3D pos)
        {
            double dt = Math.Max(1.0 / 60.0, (frame - t.LastFrame) / 60.0);
            Vector3D raw = (pos - t.Pos) / dt;
            t.Vel = t.Vel.LengthSquared() < 1e-6 ? raw : t.Vel * 0.65 + raw * 0.35;
            t.PrevPos = t.Pos;
            t.Pos = pos;
            t.LastFrame = frame;
            t.PositionObservationFrame = frame;
            if (!t.StableId) t.PositionObservationSource = "GetProjectilesLockedOnPos";
            t.LostFrames = 0;
            UpdateTrackGeometry(t, false);
            if (testArmed && recorder != null && !t.PredictionLogged && t.Vel.Length() > 10)
            {
                t.PredictionLogged = true;
                recorder.Prediction(t.Id, t.Range, t.Hull, t.Vel.Length(), t.HullClosing, t.Tti);
            }
        }

        private void UpdateTrackGeometry(Track t, bool first)
        {
            Vector3D center = controller == null ? Vector3D.Zero : controller.WorldAABB.Center;
            t.Range = Vector3D.Distance(center, t.Pos);
            t.PrevHull = first ? HullDistance(t.Pos) : t.Hull;
            t.Hull = HullDistance(t.Pos);
            if (t.Hull < t.MinHull) t.MinHull = t.Hull;
            double dt = first ? 0 : Math.Max(1.0 / 60.0, (frame - t.LastFrame + 2) / 60.0);
            if (!first) t.HullClosing = (t.PrevHull - t.Hull) / Math.Max(.001, dt);
            t.Tti = t.HullClosing > 1.0 ? t.Hull / t.HullClosing : double.PositiveInfinity;

            // IMPORTANT: entering the 150 m envelope is a telemetry event, NOT a reason
            // to stop defending the torpedo. Older test builds resolved the track here,
            // which could remove the most dangerous leak from the allocator. v0.3 keeps
            // firing until the projectile actually disappears or goes outbound.
            if (!t.Resolved && testArmed && !t.DangerEntered && t.Hull <= config().HitRadiusMeters)
            {
                t.DangerEntered = true;
                t.State = "DANGER";
                if (recorder != null)
                    recorder.Event("DANGER_ENTRY", "V" + t.VolleyIndex + " " + TrackText(t) +
                        " projectile=" + (t.StableId ? t.ProjectileId.ToString(CultureInfo.InvariantCulture) : "fallback"));
                lastEvent = "V" + Math.Max(1, t.VolleyIndex) + " T" + t.Id.ToString("00") + " DANGER ENTRY // KEEP FIRING";
            }

            // An observed guided projectile remains a threat while turning outward.
            // Never retire an ID solely for increasing range.
            if (!t.Resolved && !t.DangerEntered) t.State = t.HullClosing < -20 ? "OUTBOUND_OBSERVED" : "TRACKING";
            ObserveLiveTrackLifecycle(t, false);
        }

        private void ResolveLostTrack(Track t)
        {
            if (t.Resolved) return;
            if (testArmed && t.MinHull <= config().HitRadiusMeters)
                Resolve(t, "DANGER", "150M_ENVELOPE");
            else if (testArmed && t.HullClosing > 0 && t.MinHull <= config().EngagementRangeMeters + 100)
                Resolve(t, "INTERCEPT", "PROBABLE");
            else if (testArmed)
                Resolve(t, "UNKNOWN", "DISAPPEARED_WITHOUT_INTERCEPT_EVIDENCE");
            else
            {
                t.Resolved = true;
                t.State = "LOST";
                ObserveLiveTrackLifecycle(t, true);
            }
        }

        private void Resolve(Track t, string state, string confidence)
        {
            if (t.Resolved) return;
            t.Resolved = true;
            t.State = state;
            t.LastFrame = frame;
            ObserveLiveTrackLifecycle(t, true);

            bool countPhysical = testArmed && t.CountedPhysical && t.VolleyIndex > 0;
            if (countPhysical)
            {
                t.OutcomeCounted = true;
                if (state == "HIT" || state == "DANGER") hits++;
                else if (state == "INTERCEPT") intercepts++;
                else if (state == "MISS_OUTBOUND") misses++;
                else unknown++;
                waveResolved++;

                VolleyStats vs = GetVolleyStats(t.VolleyIndex);
                vs.Resolved++;
                vs.LastFrame = frame;
                vs.ShotsEnd = pbTotalShots;
                vs.EndHeat = HottestHeat();
                if (state == "HIT" || state == "DANGER") vs.Hits++;
                else if (state == "INTERCEPT") vs.Intercepts++;
                else if (state == "MISS_OUTBOUND") vs.Misses++;
                else vs.Unknown++;
                double h = HottestHeat();
                if (h > vs.PeakHeat) vs.PeakHeat = h;
                TryFinalizeVolley(vs);
            }

            string text = TrackText(t) + " result=" + state + " confidence=" + confidence;
            lastEvent = (t.VolleyIndex > 0 ? "V" + t.VolleyIndex + " " : "") +
                "T" + t.Id.ToString("00") + " " + state + " @ " + t.MinHull.ToString("0") + "m";
            if (recorder != null)
            {
                recorder.Event("TRACK_RESOLVE", "V" + t.VolleyIndex + " " + text + " projectile=" +
                    (t.StableId ? t.ProjectileId.ToString(CultureInfo.InvariantCulture) : "fallback") +
                    " shotsSpent=" + t.ShotsSpent);
                if (countPhysical)
                    recorder.Track(t.Id, t.ProjectileId, t.VolleyIndex, state, confidence, t.Range, t.Hull, t.MinHull,
                        t.HullClosing, t.Tti, t.Vel.Length(), t.ShotsSpent);
            }
        }

        private void ReopenTrack(Track t)
        {
            if (!t.Resolved) return;
            string old = t.State;
            if (t.OutcomeCounted)
            {
                waveResolved--;
                VolleyStats vs = GetVolleyStats(t.VolleyIndex);
                vs.Resolved--;
                if (old == "INTERCEPT") { intercepts--; vs.Intercepts--; }
                else if (old == "HIT" || old == "DANGER") { hits--; vs.Hits--; }
                else if (old == "MISS_OUTBOUND") { misses--; vs.Misses--; }
                else { unknown--; vs.Unknown--; }
                if (vs.Written) { vs.Written = false; completedVolleys--; }
                t.OutcomeCounted = false;
            }
            t.Resolved = false;
            t.LostFrames = 0;
            t.Reacquisitions++;
            t.State = t.DangerEntered ? "DANGER" : "REACQUIRED";
            if (testArmed && recorder != null) recorder.Event("TRACK_REACQUIRED", "track=" + t.Id + " projectile=" + t.ProjectileId + " retracted=" + old);
        }

        private string GoalText()
        {
            var cohort = tracks.Where(t => t.GoalOrdinal > 0).ToList();
            int good = cohort.Count(t => t.Resolved && t.State == "INTERCEPT" && !t.DangerEntered);
            int danger = cohort.Count(t => t.DangerEntered || t.State == "DANGER" || t.State == "HIT");
            int pending = cohort.Count(t => !t.Resolved);
            int unclear = cohort.Count - good - danger - cohort.Count(t => !t.Resolved && !t.DangerEntered);
            string state = danger > 0 ? "DANGER" : goalSensorGap ? "SENSOR GAP" : unclear > 0 ? "UNKNOWN" : good == GoalTarget ? "PROBABLE CLEAR" : "INCOMPLETE";
            return "160 GOAL: " + state + " | seen " + cohort.Count + "/160 | probable " + good + " | danger " + danger + " | pending " + pending;
        }

        private void UpdateTestState()
        {
            if (!testArmed) return;

            if (!wc.Ready)
            {
                testState = "WAIT_WC";
                return;
            }
            if (!wc.DirectControlReady)
            {
                testState = "WAIT_DIRECT_API";
                return;
            }
            if (testState == "WAIT_WC" || testState == "WAIT_DIRECT_API" || testState == "WAIT_DIRECT_CONTROL")
            {
                if (!directControlActive) { testState = "WAIT_DIRECT_CONTROL"; return; }
                testState = tracks.Any(t => !t.Resolved) ? "ACTIVE" : "ARMED_WAIT";
                lastEvent = "DIRECT CONTROL READY // TEST -> " + testState;
                if (recorder != null) recorder.Event("DIRECT_CONTROL_CONFIRMED", "state=" + testState + " heat=" + HottestHeat().ToString("0.0", CultureInfo.InvariantCulture));
            }

            // Continuous saturation test: never lock or cool between 32-torpedo volleys.
            // The user stops the session manually; every group of ExpectedInbound unique
            // tracks is summarized as its own volley in volley_summary.csv.
            fireLockout = false;
        }

        private VolleyStats GetVolleyStats(int index)
        {
            VolleyStats vs;
            if (!volleyStats.TryGetValue(index, out vs))
            {
                vs = new VolleyStats { Index = index };
                volleyStats[index] = vs;
            }
            return vs;
        }

        private void UpdateOpenVolleyPeakHeat()
        {
            if (!testArmed) return;
            double heat = HottestHeat();
            foreach (VolleyStats vs in volleyStats.Values)
            {
                if (vs == null || vs.Written) continue;
                if (heat > vs.PeakHeat) vs.PeakHeat = heat;
            }
        }

        private void TryFinalizeVolley(VolleyStats vs)
        {
            if (vs == null || vs.Written) return;
            int expected = Math.Max(1, config().ExpectedInbound);
            if (vs.Seen < expected || vs.Resolved < expected) return;
            vs.Written = true;
            completedVolleys++;
            lastCompletedVolley = Math.Max(lastCompletedVolley, vs.Index);
            vs.ShotsEnd = pbTotalShots;
            double rate = expected <= 0 ? 0 : 100.0 * vs.Intercepts / expected;
            double seconds = vs.FirstFrame < 0 || vs.LastFrame < 0 ? 0 : Math.Max(0, (vs.LastFrame - vs.FirstFrame) / 60.0);
            if (recorder != null)
            {
                recorder.Event("VOLLEY_COMPLETE", "volley=" + vs.Index + " int=" + vs.Intercepts + " danger=" + vs.Hits +
                    " miss=" + vs.Misses + " unknown=" + vs.Unknown +
                    " heat=" + vs.StartHeat.ToString("0.0", CultureInfo.InvariantCulture) + "->" + vs.EndHeat.ToString("0.0", CultureInfo.InvariantCulture) +
                    " peak=" + vs.PeakHeat.ToString("0.0", CultureInfo.InvariantCulture) +
                    " shots=" + Math.Max(0, vs.ShotsEnd - vs.ShotsStart));
                recorder.VolleySummary(vs.Index, expected, vs.Intercepts, vs.Hits, vs.Misses, vs.Unknown,
                    vs.StartHeat, vs.EndHeat, vs.PeakHeat, Math.Max(0, vs.ShotsEnd - vs.ShotsStart), seconds, rate);
            }
            lastEvent = "VOLLEY " + vs.Index + " DONE // INT " + vs.Intercepts + " DANGER " + vs.Hits + " // HEAT " + vs.PeakHeat.ToString("0") + "%";
        }

        private void ResetWaveCounters()
        {
            liveTrackerEpoch++;
            tracks.Clear();
            stableTrackMap.Clear();
            nextTrackId = 1;
            initialSnapshotTaken = false;
            goalSensorGap = false;
            goalSeen = 0;
            foreach (Gun g in guns) { g.BudgetTrackId = 0; g.BudgetWasAllowed = false; g.BudgetShotMark = g.Shots; g.BudgetNativeProjectile = 0; g.BudgetFrame = -1; }
            waveSeen = waveResolved = intercepts = hits = misses = unknown = 0;
            completedVolleys = 0;
            lastCompletedVolley = 0;
            volleyStats.Clear();
            stableFallbackGapFrames = 0;
            fallbackTelemetrySeen = 0;
            fallbackFused = 0;
        }

        private void Allocate()
        {
            PdcConfig cfg = config();
            if((cfg.LabEnabled && cfg.LabNativeOnly) || (!cfg.LabEnabled && cfg.ManagedDefenseEnabled)) { AllocateLabNative(); return; }
            AccumulateTrackShotBudgets();

            // v0.3.2 allowed future waves at 6-8 km to occupy otherwise-free gun seats.
            // In the supplied run, urgent <1200 m survivors repeatedly had only 3-8 guns
            // while the rest were "assigned" to torpedoes far outside the 3000 m weapon
            // envelope. v0.3.4 never allocates a gun outside the real engagement horizon.
            double engageHorizon = Math.Max(500.0, cfg.EngagementRangeMeters + 100.0);
            List<Track> active = tracks
                .Where(t => t != null && !t.Resolved && t.Hull <= engageHorizon)
                .OrderBy(t => t.Tti).ThenBy(t => t.Hull).ToList();

            for (int i = 0; i < guns.Count; i++)
            {
                Gun g = guns[i];
                g.PriorOwner = g.TrackId;
                if (g.Shots > g.ObservedShotMark) g.LastObservedShotFrame = frame;
                g.ObservedShotMark = g.Shots;
                g.Allowed = false;
                g.TrackId = 0;
                g.DecisionTrackId = 0;
                g.DecisionBasis = "NO_ACTIVE_DECISION";
                g.MatchError = double.PositiveInfinity;
                g.AlignmentDeg = 999.0;
                g.KillWindowScore = 0;
                g.PkState = "HOLD";
                g.ExactTargetMatch = false;
                g.Rof = cfg.FarRof;
            }
            for (int i = 0; i < tracks.Count; i++) if (tracks[i] != null && !tracks[i].Resolved) tracks[i].Assigned = 0;

            UpdateBankRoles(cfg, active);

            if (!cfg.ControlEnabled || fireLockout || (active.Count == 0 && !cfg.NativeLeadEnabled))
            {
                SaveBudgetOwners();
                return;
            }

            double turnDeg = ShipTurnDegPerSec();
            double turnFactor = 0.0;
            if (turnDeg > cfg.ManeuverSupportDegPerSec)
                turnFactor = Math.Max(0.0, Math.Min(1.0, (turnDeg - cfg.ManeuverSupportDegPerSec) / 6.0));
            double maneuverBoost = cfg.ManeuverRangeBoostMeters * turnFactor;
            double urgentRange = Math.Min(cfg.EngagementRangeMeters, cfg.UrgentCoverageRangeMeters + maneuverBoost);
            double urgentTti = cfg.UrgentCoverageTtiSeconds + .25 * turnFactor;
            double bankHeat = HottestHeat();

            var candidatesByTrack = new Dictionary<int, List<PairCandidate>>();
            for (int ti = 0; ti < active.Count; ti++)
            {
                Track t = active[ti];
                var list = new List<PairCandidate>();
                for (int gi = 0; gi < guns.Count; gi++)
                {
                    Gun g = guns[gi];
                    if (g == null || !g.Functional || g.Hp <= 5) continue;
                    PairCandidate pc = BuildPairCandidate(g, t, cfg);
                    if (pc != null && BankAllows(g, t, cfg)) list.Add(pc);
                }
                list.Sort((a, b) => a.Score.CompareTo(b.Score));
                candidatesByTrack[t.Id] = list;
            }

            var used = new HashSet<long>();
            List<Track> urgent = active.Where(t => IsUrgent(t, urgentRange, urgentTti)).ToList();
            List<Track> far = active.Where(t => !IsUrgent(t, urgentRange, urgentTti)).ToList();

            // PASS 1: one owner for every urgent in-range threat.
            CoverOneEach(urgent, candidatesByTrack, used, cfg);

            // PASS 2: urgent survivors get support BEFORE future/far threats are allowed
            // to consume spare seats. Shot budget can request help even before the normal
            // range/TTI dogpile bands.
            AddSupport(urgent, candidatesByTrack, used, cfg, bankHeat, turnFactor);

            // PASS 3: only now cover the remaining far-but-shootable threats.
            CoverOneEach(far, candidatesByTrack, used, cfg);

            // PASS 4: any final spare guns can help stubborn far tracks.
            AddSupport(far, candidatesByTrack, used, cfg, bankHeat, turnFactor);

            // A gun can pre-track without firing. Tight PK gates are retained at long
            // range; inside the survival band heat throttling is disabled so a hot battery
            // still prioritizes killing the inbound torpedo over preserving temperature.
            for (int gi = 0; gi < guns.Count; gi++)
            {
                Gun g = guns[gi];
                Track assigned = g.TrackId == 0 ? null : active.FirstOrDefault(t => t.Id == g.TrackId);
                DecideGunThreat(g, assigned, cfg, bankHeat, turnFactor, turnDeg, engageHorizon);
            }

            SaveBudgetOwners();
        }

        private void DecideGunThreat(Gun g, Track assigned, PdcConfig cfg, double bankHeat, double turnFactor, double turnDeg, double horizon)
        {
            Track threat = assigned;
            g.DecisionBasis = "SCHEDULER_BASELINE";
            if (cfg.NativeThreatDecisions || cfg.NativeLeadEnabled)
            {
                Track native = ObservedNativeTrack(g);
                if (native == null)
                {
                    g.DecisionBasis = cfg.NativeLeadEnabled ? "NATIVE_LEAD_UNAVAILABLE" : "SCHEDULER_FALLBACK_NATIVE_UNAVAILABLE";
                    if (cfg.NativeLeadEnabled) threat = null;
                }
                else
                {
                    threat = native.Hull <= horizon ? native : null;
                    g.DecisionBasis = threat == null ? "NATIVE_OUTSIDE_HORIZON" : "OBSERVED_NATIVE";
                    if (threat != null)
                    {
                        // Refresh gate geometry for the actual threat; retain the
                        // advisory assignment separately rather than changing aim.
                        PairCandidate pair = BuildPairCandidate(g, threat, cfg);
                        g.AlignmentDeg = pair == null ? 999.0 : pair.AngleDeg;
                        g.MatchError = pair == null ? double.PositiveInfinity : pair.Miss;
                        g.ExactTargetMatch = pair != null && pair.ExactTarget;
                    }
                }
            }
            g.DecisionTrackId = threat == null ? 0 : threat.Id;
            g.Rof = CombatRof(g.Heat, bankHeat, threat, cfg, turnFactor);
            g.Allowed = g.Functional && cfg.ControlEnabled && !fireLockout && threat != null &&
                ShouldFireKillWindow(g, threat, cfg, turnDeg);
            if (cfg.NativeLeadEnabled)
            {
                g.Allowed &= g.NativeControlKnown && !g.NativeManual && g.NativeAligned && g.ReadyRead == "OK" && g.ReadyValue;
                if (!g.Allowed && threat != null) g.PkState = "NATIVE_WAIT_ALIGNMENT_OR_READY";
            }
            ApplyBankDecision(g, threat, cfg);
        }

        private void CoverOneEach(List<Track> list, Dictionary<int, List<PairCandidate>> candidatesByTrack,
            HashSet<long> used, PdcConfig cfg)
        {
            for (int ti = 0; ti < list.Count; ti++)
            {
                Track t = list[ti];
                if (t == null || t.Resolved || t.Assigned > 0) continue;
                PairCandidate pick = PickUnusedCandidate(t, candidatesByTrack, used);
                bool forced = IsForced(t, cfg);
                if (pick == null && forced) pick = PickUnusedBootstrapGun(t, used, cfg);
                if (pick == null) continue;
                AssignGun(pick, t, used);
            }
        }

        private void AddSupport(List<Track> list, Dictionary<int, List<PairCandidate>> candidatesByTrack,
            HashSet<long> used, PdcConfig cfg, double bankHeat, double turnFactor)
        {
            for (int ti = 0; ti < list.Count; ti++)
            {
                Track t = list[ti];
                if (t == null || t.Resolved || t.Assigned == 0) continue;
                int need = ShootersFor(t, cfg, bankHeat, turnFactor);
                while (t.Assigned < need)
                {
                    PairCandidate pick = PickUnusedCandidate(t, candidatesByTrack, used);
                    bool forced = IsForced(t, cfg);
                    if (pick == null && forced) pick = PickUnusedBootstrapGun(t, used, cfg);
                    if (pick == null) break;
                    AssignGun(pick, t, used);
                }
            }
        }

        private static bool IsUrgent(Track t, double range, double tti)
        {
            if (t == null) return false;
            return t.Hull <= range || (!double.IsInfinity(t.Tti) && t.Tti <= tti);
        }

        private static bool IsForced(Track t, PdcConfig cfg)
        {
            return t != null && (t.Hull <= cfg.PkForcedRangeMeters ||
                (!double.IsInfinity(t.Tti) && t.Tti <= cfg.PkForcedTtiSeconds));
        }

        private void AccumulateTrackShotBudgets()
        {
            for (int i = 0; i < guns.Count; i++)
            {
                Gun g = guns[i];
                long now = Math.Max(0, g.Shots);
                long delta = now - g.BudgetShotMark;
                if (delta > 0 && g.BudgetWasAllowed && g.BudgetTrackId != 0 &&
                    frame > g.BudgetFrame && frame - g.BudgetFrame <= 6)
                {
                    Track t = ObservedNativeTrack(g);
                    if (t != null && t.Id == g.BudgetTrackId && t.ProjectileId == g.BudgetNativeProjectile)
                        t.ShotsSpent += (int)Math.Min(1000L, delta);
                }
                g.BudgetShotMark = now;
                g.BudgetTrackId = 0;
                g.BudgetWasAllowed = false;
                g.BudgetNativeProjectile = 0;
                g.BudgetFrame = -1;
            }
        }

        private void SaveBudgetOwners()
        {
            for (int i = 0; i < guns.Count; i++)
            {
                Gun g = guns[i];
                Track native = ObservedNativeTrack(g);
                g.BudgetTrackId = native == null ? 0 : native.Id;
                g.BudgetNativeProjectile = native == null ? 0 : native.ProjectileId;
                g.BudgetWasAllowed = g.Allowed && native != null;
                g.BudgetFrame = frame;
            }
        }

        private Track ObservedNativeTrack(Gun g)
        {
            if (g == null || g.TelemetryFrame != frame || g.NativeRead != "PROJECTILE" || g.NativeProjectile == 0)
                return null;
            return tracks.FirstOrDefault(t => t != null && !t.Resolved && t.StableId &&
                t.ProjectileId == g.NativeProjectile && t.LostFrames == 0 && t.LastFrame == frame);
        }

        private double ShipTurnDegPerSec()
        {
            try
            {
                if (controller == null) return 0;
                return controller.GetShipVelocities().AngularVelocity.Length() * 180.0 / Math.PI;
            }
            catch { return 0; }
        }

        private PairCandidate BuildPairCandidate(Gun g, Track t, PdcConfig cfg)
        {
            if (g == null || t == null || !ManagedRangeAllows(g,t,cfg)) return null;
            // Dense volleys contain distinct projectiles within the old 250 m
            // positional tolerance. Only a locally current identity is exact.
            bool exact = g.TelemetryFrame == frame && g.NativeRead == "PROJECTILE" &&
                g.NativeProjectile != 0 && t.StableId && g.NativeProjectile == t.ProjectileId;

            double angle = 999.0;
            double miss = double.PositiveInfinity;
            bool forward = false;
            if (g.ScopeValid)
            {
                Vector3D rel = t.Pos - g.ScopeOrigin;
                double len = rel.Length();
                if (len > 1e-3)
                {
                    double fwd = Vector3D.Dot(rel, g.ScopeDirection);
                    forward = fwd > 0;
                    if (forward)
                    {
                        Vector3D unit = rel / len;
                        double dot = Math.Max(-1.0, Math.Min(1.0, Vector3D.Dot(unit, g.ScopeDirection)));
                        angle = Math.Acos(dot) * 180.0 / Math.PI;
                        Vector3D closest = g.ScopeOrigin + g.ScopeDirection * fwd;
                        miss = Vector3D.Distance(closest, t.Pos);
                    }
                }
            }

            // Outside the forced band, don't pretend a gun on the opposite side of the
            // ship is a high-PK candidate. Near the ship we keep it available as a last
            // resort because survival matters more than elegant geometry.
            bool coarseGeometry = forward && (angle <= 10.0 || miss <= Math.Max(400.0, cfg.ScopeMatchMeters * 3.0));
            if (!exact && !coarseGeometry && t.Hull > cfg.PkForcedRangeMeters) return null;

            // Alignment is much more important than it was in v0.3.2. The 0.3.2b run
            // showed a sharp falloff once first-fire geometry drifted past roughly one
            // degree, so cool-but-badly-aimed guns no longer beat well-aligned guns.
            double a = angle >= 998 ? 18.0 : Math.Min(18.0, angle);
            double m = double.IsInfinity(miss) ? 600.0 : Math.Min(600.0, miss);
            double score = a * 18.0 + m * .020 + g.Heat * .35 + (100.0 - g.Hp) * .6;

            if (exact) score -= 35.0;
            else if (g.WcTargetValid) score += 14.0; // already looking at another WC target
            return new PairCandidate { Gun = g, Miss = miss, AngleDeg = angle, ExactTarget = exact, Score = score };
        }

        private PairCandidate PickUnusedCandidate(Track t, Dictionary<int, List<PairCandidate>> candidatesByTrack, HashSet<long> used)
        {
            if (t == null) return null;
            List<PairCandidate> list;
            if (!candidatesByTrack.TryGetValue(t.Id, out list) || list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                PairCandidate pc = list[i];
                Gun g = pc == null ? null : pc.Gun;
                if (g == null || !g.Functional || g.Hp <= 5 || used.Contains(GunKey(g))) continue;
                return pc;
            }
            return null;
        }

        private PairCandidate PickUnusedBootstrapGun(Track t, HashSet<long> used, PdcConfig cfg)
        {
            PairCandidate best = null;
            double bestScore = double.MaxValue;
            for (int i = 0; i < guns.Count; i++)
            {
                Gun g = guns[i];
                if (g == null || !g.Functional || g.Hp <= 5 || used.Contains(GunKey(g)) || !ManagedRangeAllows(g,t,cfg) || !BankAllows(g, t, cfg)) continue;

                double angle = 999.0;
                double miss = double.PositiveInfinity;
                if (g.ScopeValid && t != null)
                {
                    Vector3D rel = t.Pos - g.ScopeOrigin;
                    double len = rel.Length();
                    if (len > 1e-3)
                    {
                        double fwd = Vector3D.Dot(rel, g.ScopeDirection);
                        if (fwd > 0)
                        {
                            Vector3D unit = rel / len;
                            double dot = Math.Max(-1.0, Math.Min(1.0, Vector3D.Dot(unit, g.ScopeDirection)));
                            angle = Math.Acos(dot) * 180.0 / Math.PI;
                            Vector3D closest = g.ScopeOrigin + g.ScopeDirection * fwd;
                            miss = Vector3D.Distance(closest, t.Pos);
                        }
                    }
                }

                double a = angle >= 998 ? 30.0 : Math.Min(30.0, angle);
                double m = double.IsInfinity(miss) ? 800.0 : Math.Min(800.0, miss);
                double score = a * 16.0 + m * .015 + g.Heat * .40 + (100.0 - g.Hp) * .5;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = new PairCandidate { Gun = g, Miss = miss, AngleDeg = angle, ExactTarget = false, Score = score };
                }
            }
            return best;
        }

        private static void AssignGun(PairCandidate pc, Track t, HashSet<long> used)
        {
            if (pc == null || pc.Gun == null || t == null) return;
            Gun g = pc.Gun;
            long key = GunKey(g);
            if (!used.Add(key)) return;
            g.TrackId = t.Id;
            g.MatchError = pc.Miss;
            g.AlignmentDeg = pc.AngleDeg;
            g.ExactTargetMatch = pc.ExactTarget;
            t.Assigned++;
        }

        private bool ShouldFireKillWindow(Gun g, Track t, PdcConfig cfg, double turnDegPerSec)
        {
            if (g == null || t == null) return false;
            if (!ManagedRangeAllows(g,t,cfg)) { g.PkState="WEAPON_ENVELOPE_HOLD"; return false; }
            bool fullEmergency = ((!double.IsInfinity(t.Tti) && t.Tti <= cfg.FullEmergencyTtiSeconds) || t.Hull <= cfg.FullEmergencyRangeMeters);
            if (fullEmergency)
            {
                g.KillWindowScore = 100;
                g.PkState = "LEAK EMERGENCY";
                return true;
            }

            bool forced = IsForced(t, cfg);
            if (cfg.BypassAlignmentGate || !cfg.PkWindowEnabled || forced)
            {
                g.KillWindowScore = KillWindowScore(g, t, cfg, turnDegPerSec);
                g.PkState = cfg.BypassAlignmentGate ? "BYPASS_ANGLE" : (forced ? "FORCED" : "OPEN");
                return true;
            }

            if (g.AlignmentDeg >= 998.0)
            {
                g.KillWindowScore = 0;
                g.PkState = "ACQUIRE";
                return false;
            }

            double gate = KillWindowAngle(t, cfg, turnDegPerSec);
            g.KillWindowScore = KillWindowScore(g, t, cfg, turnDegPerSec);
            bool green = g.AlignmentDeg <= gate;
            g.PkState = green ? "GREEN" : "TRACK/HOLD";
            return green;
        }

        private static double KillWindowAngle(Track t, PdcConfig cfg, double turnDegPerSec)
        {
            double r = t == null ? 99999 : t.Hull;
            double gate;
            if (r > 2200) gate = cfg.PkFarAngleDeg;
            else if (r > 1800) gate = cfg.PkMidFarAngleDeg;
            else if (r > 1500) gate = cfg.PkMidAngleDeg;
            else gate = cfg.PkNearAngleDeg;

            // During a turn the firing window can sweep past quickly. Widen it only
            // slightly; the v0.3.2b data says large-angle first fire is expensive.
            if (turnDegPerSec > cfg.ManeuverSupportDegPerSec)
            {
                double f = Math.Max(0.0, Math.Min(1.0, (turnDegPerSec - cfg.ManeuverSupportDegPerSec) / 8.0));
                gate += .12 * f;
            }
            return gate;
        }

        private static double KillWindowScore(Gun g, Track t, PdcConfig cfg, double turnDegPerSec)
        {
            if (g == null || t == null || g.AlignmentDeg >= 998) return 0;
            double gate = Math.Max(.1, KillWindowAngle(t, cfg, turnDegPerSec));
            double align = 1.0 - Math.Min(1.0, g.AlignmentDeg / (gate * 1.35));
            double range = 1.0 - Math.Min(1.0, Math.Max(0.0, t.Hull) / 3600.0);
            double thermal = 1.0 - Math.Min(.85, Math.Max(0.0, g.Heat) / 120.0);
            double stubborn = Math.Min(1.0, Math.Max(0, t.ShotsSpent) / 20.0);
            double score = 100.0 * (.76 * align + .12 * range + .07 * thermal + .05 * stubborn);
            if (g.ExactTargetMatch) score += 8.0;
            return Math.Max(0.0, Math.Min(100.0, score));
        }

        private static int ShootersFor(Track t, PdcConfig cfg, double bankHeat, double turnFactor)
        {
            int n = 1;
            if (t == null) return n;

            double rangeBoost = cfg.ManeuverRangeBoostMeters * turnFactor;
            double twoRange = cfg.TwoShooterRangeMeters + rangeBoost;
            double threeRange = cfg.ThreeShooterRangeMeters + rangeBoost * .75;
            double emergencyRange = cfg.EmergencyRangeMeters + rangeBoost * .50;

            if ((!double.IsInfinity(t.Tti) && t.Tti <= cfg.TwoShooterTtiSeconds + .15 * turnFactor) || t.Hull <= twoRange) n = 2;
            if ((!double.IsInfinity(t.Tti) && t.Tti <= cfg.ThreeShooterTtiSeconds + .12 * turnFactor) || t.Hull <= threeRange) n = 3;
            if ((!double.IsInfinity(t.Tti) && t.Tti <= cfg.EmergencyTtiSeconds + .10 * turnFactor) || t.Hull <= emergencyRange) n = Math.Max(4, cfg.EmergencyShooters);

            // Stubborn-target escalation. In v0.3.2b danger tracks consumed ~17 shots
            // on average while successful kills used ~10. A cold bank can afford to ask
            // for help sooner; once warm, the normal thresholds preserve heat. Coverage
            // has already been protected before this support pass runs.
            bool coldBank = bankHeat <= cfg.ColdBoostHeatPercent;
            int help2 = coldBank ? Math.Max(4, cfg.ShotHelpSecond - 2) : cfg.ShotHelpSecond;
            int help3 = coldBank ? Math.Max(help2 + 1, cfg.ShotHelpThird - 4) : cfg.ShotHelpThird;
            int help4 = coldBank ? Math.Max(help3 + 1, cfg.ShotHelpFourth - 6) : cfg.ShotHelpFourth;
            if (t.ShotsSpent >= help2) n = Math.Max(n, 2);
            if (t.ShotsSpent >= help3) n = Math.Max(n, 3);
            if (t.ShotsSpent >= help4) n = Math.Max(n, 4);

            // COLD SHIELD: after every urgent threat has one owner, spend otherwise-idle
            // thermal headroom to double-cover the remaining urgent set. This is deliberately
            // more aggressive on the first cold volley, then automatically backs off as heat
            // rises above ColdBoostHeatPercent.
            if (coldBank &&
                (t.Hull <= cfg.UrgentCoverageRangeMeters + rangeBoost ||
                 (!double.IsInfinity(t.Tti) && t.Tti <= cfg.UrgentCoverageTtiSeconds + .25 * turnFactor)))
                n = Math.Max(n, 2);

            return Math.Max(1, Math.Min(8, n));
        }

        private static double CombatRof(double heat, double bankHeat, Track t, PdcConfig cfg, double turnFactor)
        {
            if (t == null) return cfg.FarRof;

            bool fullEmergency = ((!double.IsInfinity(t.Tti) && t.Tti <= cfg.FullEmergencyTtiSeconds) || t.Hull <= cfg.FullEmergencyRangeMeters);
            double rof;
            if (fullEmergency) rof = cfg.FullEmergencyRof;
            else if (t.Hull <= 900 || (!double.IsInfinity(t.Tti) && t.Tti <= .95)) rof = cfg.NearRof;
            else if (t.Hull <= 1200 || (!double.IsInfinity(t.Tti) && t.Tti <= 1.25)) rof = cfg.CloseRof;
            else if (t.Hull <= 1500 || (!double.IsInfinity(t.Tti) && t.Tti <= 1.60)) rof = cfg.MidRof;
            else if (t.Hull <= 2200 || (!double.IsInfinity(t.Tti) && t.Tti <= 2.25)) rof = cfg.MidFarRof;
            else rof = cfg.FarRof;

            if (bankHeat <= cfg.ColdBoostHeatPercent && t.Hull > 1000.0)
                rof = Math.Min(1.0, rof + cfg.ColdRofBoost);

            double survivalRange = cfg.SurvivalHeatOverrideRangeMeters + cfg.ManeuverRangeBoostMeters * turnFactor;
            double survivalTti = cfg.SurvivalHeatOverrideTtiSeconds + .20 * turnFactor;
            bool survivalOverride = t.Hull <= survivalRange ||
                (!double.IsInfinity(t.Tti) && t.Tti <= survivalTti);

            // Heat conservation applies only while there is still room to wait. Once a
            // torpedo is in the survival band, do not cripple ROF just because the bank
            // is hot; the user's requirement is to stop the inbound threat first.
            if (!survivalOverride)
            {
                if (heat >= 96.0) rof = Math.Min(rof, .52);
                else if (heat >= 90.0) rof = Math.Min(rof, .55);
                else if (heat >= 80.0) rof = Math.Min(rof, .60);
                else if (heat >= cfg.HeatThrottleStartPercent) rof = Math.Min(rof, .65);
            }
            return Math.Max(.50, Math.Min(1.0, rof));
        }

        private static long GunKey(Gun g)
        {
            return g.EntityId ^ ((long)g.Part << 48);
        }

        private void WriteWatchdogHeartbeat()
        {
            if (bridgeBlock == null) return;
            bridgeSeq++;
            var sb = new StringBuilder(1024);
            sb.AppendLine("ZPDC_DIRECT1");
            sb.AppendLine("SEQ=" + bridgeSeq);
            sb.AppendLine("UTC_TICKS=" + DateTime.UtcNow.Ticks);
            sb.AppendLine("SHIP=" + (grid == null ? 0 : grid.EntityId));
            sb.AppendLine("MODE=" + (directControlActive ? "DIRECT" : "FALLBACK"));
            sb.AppendLine("CORE=" + (wc.Ready ? "READY" : "WAIT"));
            sb.AppendLine("CONTROL=" + directControlState);
            sb.AppendLine("TEST=" + testState);
            sb.AppendLine("INBOUND=" + tracks.Count(t => !t.Resolved));
            sb.AppendLine("HEAT=" + HottestHeat().ToString("0.0", CultureInfo.InvariantCulture));
            sb.AppendLine("ROF_FAR=" + config().FarRof.ToString("0.000", CultureInfo.InvariantCulture));
            try
            {
                bridgeBlock.CustomData = sb.ToString();
                bridgeLastWriteFrame = frame;
                bridgeLastWriteOk = true;
                bridgeLastWriteError = "OK";
            }
            catch (Exception ex)
            {
                bridgeLastWriteFrame = frame;
                bridgeLastWriteOk = false;
                bridgeLastWriteError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        private void ApplyDirectControl()
        {
            if(JoiningServer()){ApplyClientControl();return;}
            if(config().LabEnabled) { if(ReleaseManagerHardware("LAB_OWNERSHIP_TRANSFER")) ApplyLabControl(); return; }
            if(config().ManagedDefenseEnabled) { ApplyManagerControl(); return; }
            if(!ReleaseManagerHardware("MANAGER_DISABLED")) return;
            if (config().NativeLeadEnabled && (wc.NativeObserver.Status != "SCHEMA_READY" || guns.Any(g => g.Functional && !g.NativeControlKnown)))
            {
                FailOpenNative("NATIVE LEAD OBSERVER UNAVAILABLE");
                return;
            }
            if (!config().ControlEnabled || !wc.Ready || !wc.DirectControlReady)
            {
                directControlActive = false;
                directControlState = !config().ControlEnabled ? "DISABLED / NATIVE" : !wc.Ready ? "WC WAIT / NATIVE" : "DIRECT API MISSING / NATIVE";
                FailOpenNative(directControlState);
                return;
            }
            if (!stableSensorHealthy && stableTrackMap.Count > 0)
            {
                FailOpenNative("STABLE SENSOR UNAVAILABLE");
                return;
            }

            // Safety interlock: if WeaponCore itself reports projectiles locked on but
            // Zeo has no live track to allocate, do NOT hold the battery. Two consecutive
            // mismatched control cycles fail open to native WC until tracking recovers.
            // This makes direct control safer than a blind all-HOLD condition.
            int wcLocked = 0;
            try
            {
                var lc = wc.GetLockedCount(gridEntity);
                wcLocked = Math.Max(0, lc.Item2);
            }
            catch { wcLocked = 0; }
            int activeTracks = tracks.Count(t => t != null && !t.Resolved);
            if (wcLocked > 0 && activeTracks == 0)
            {
                sensorGapCycles++;
                if (sensorGapCycles >= 2)
                {
                    FailOpenNative("TRACKER GAP // WC LOCKS " + wcLocked);
                    return;
                }
            }
            else sensorGapCycles = 0;

            bool allOk = true;
            int applied = 0;
            int yielded = 0;
            for (int i = 0; i < guns.Count; i++)
            {
                Gun g = guns[i];
                if (g == null || g.Block == null || g.Entity == null || !g.Functional) continue;
                if ((config().NativeLeadEnabled || config().WeaponAwareEnabled) && g.NativeControlKnown && g.NativeManual)
                {
                    ReleaseOuter(g, "MANUAL_CONTROL_YIELD");
                    PreemptivePolicy.Stop(g.Outer, frame, g.Shots, config());
                    if (g.OuterRangeOwned) ApplyGunRange(g, config().EngagementRangeMeters);
                    g.OuterRangeOwned = false;
                    g.CommandResult = "MANUAL_CONTROL_YIELD";
                    g.DirectConfigured = false;
                    yielded++;
                    continue;
                }
                if (config().WeaponAwareEnabled && !FreshProfile(g))
                {
                    YieldUnknownProfile(g);
                    if(!g.ProfileYielded) allOk=false;
                    yielded++;
                    continue;
                }
                g.ProfileYielded=false;
                ResolveManagedCadence(g);
                bool allow = !fireLockout && g.Allowed && !g.OuterStopPending;
                if (g.Outer.Active && PreemptivePolicy.Expired(g.Outer, frame, g.Shots, config())) allow = g.Allowed = false;
                Track owner = tracks.FirstOrDefault(t => t.Id == g.DecisionTrackId && !t.Resolved);
                var context = new ShotContext { Track = g.DecisionTrackId, Volley = owner == null ? 0 : owner.VolleyIndex,
                    Projectile = owner == null ? 0 : owner.ProjectileId, LastObservedFrame = owner == null ? frame : owner.LastFrame,
                    ObservedNativeProjectile = g.NativeProjectile, NativeRead = g.NativeRead, NativeFrame = frame,
                    CommandFrame = frame, Hull = owner == null ? double.NaN : owner.Hull, Tti = owner == null ? double.NaN : owner.Tti,
                    Allowed = allow, Gate = g.PkState, Mode = ExperimentMode };
                System.Threading.Volatile.Write(ref g.Context, context);
                if (testArmed && recorder != null && (g.PreviousTrack != g.TrackId || g.PreviousAllowed != allow || g.PreviousGate != g.PkState))
                    recorder.Assignment(frame, g, context);
                g.PreviousTrack = g.TrackId; g.PreviousAllowed = allow; g.PreviousGate = g.PkState;
                bool ok = ConfigureDirectGun(g);
                ok &= ApplyGunRof(g, g.Rof);
                ok &= ApplyGunRange(g, g.DesiredRange);
                if (!ok && g.OuterSelected)
                {
                    ReleaseOuter(g, "WRITE_FAILED");
                    allow = g.Allowed = context.Allowed = false;
                }
                if (!ok && g.OuterRangeOwned)
                {
                    allow = g.Allowed = context.Allowed = false;
                    PreemptivePolicy.Stop(g.Outer, frame, g.Shots, config());
                    g.OuterState = "WRITE_FAILED";
                }
                ok &= ApplyFireCommand(g, owner, allow);
                g.CommandResult = ok ? "OK" : "WRITE_FAILED";
                if (ok) applied++; else allOk = false;
            }

            directControlActive = allOk && applied + yielded > 0;
            nativeFallbackApplied = false;
            if (directControlActive)
            {
                directControlFailures = 0;
                directControlState = "DIRECT ACTIVE " + applied + "/" + guns.Count + (yielded > 0 ? " MANUAL YIELD " + yielded : "");
            }
            else
            {
                directControlFailures++;
                directControlState = "DIRECT DEGRADED " + applied + "/" + guns.Count;
                // Do not instantly fail open for a single transient write. After three
                // consecutive control cycles, restore autonomous WC rather than leave a dead battery.
                if (directControlFailures >= 3) FailOpenNative("DIRECT WRITE FAIL");
            }
        }

        private bool ApplyFireCommand(Gun g, Track owner, bool allow)
        {
            if (g.OuterStopPending) return wc.ToggleWeaponFire(g.Entity, g.Part, false);
            if (g.OuterSelected)
            {
                // Only the per-update outer controller may submit one-cycle requests.
                // An ON here would turn a bounded request into continuous firing.
                return g.Outer.Active || wc.ToggleWeaponFire(g.Entity, g.Part, false);
            }
            // Keep native continuous firing. The retired C loop is unreachable even
            // if a stale external client sends its old configuration flag.
            g.TargetedActive = false;
            g.RequestResult = allow ? "NATIVE_CONTINUOUS" : "HOLD";
            return wc.ToggleWeaponFire(g.Entity, g.Part, allow);
        }

        private bool ConfigureDirectGun(Gun g)
        {
            bool targeted = false;
            if (g.DirectConfigured && g.TargetedConfig == targeted) return true;
            bool ok = true;
            // Target-category filters belong exclusively to the player.
            ok &= SetTerminal(g.Block, "WC_Shoot Mode", 2L);
            SetTerminal(g.Block, "WC_Supporting PD", true);
            bool distributionOk = SetTerminal(g.Block, "WC_EnableFireDistribution", true);
            if (testArmed && recorder != null) recorder.Event("FIRE_DISTRIBUTION_WRITE", "gun=" + g.EntityId + " success=" + distributionOk);
            // Installed WC obtains a projectile index before its optional distance sort.
            // Disable that sort for ID requests; verify the terminal setting before use.
            bool sortOk = SetTerminal(g.Block, "WC_TargetClosest", !targeted);
            bool sortValue;
            g.TargetedConfig = targeted && sortOk && TryReadTerminal(g.Block, "WC_TargetClosest", out sortValue) && !sortValue;
            g.DirectConfigured = ok;
            return ok;
        }

        private bool ApplyGunRof(Gun g, double rof)
        {
            if (config().WeaponAwareEnabled && (!g.ManagedRofWrite || !FreshProfile(g) || !g.Profile.Adjustable || !g.RofReadOk || !BankPlanner.Finite(rof))) return true;
            float desired = (float)Math.Max(config().WeaponAwareEnabled ? g.Profile.MinimumRof : .50, Math.Min(1.0, rof));
            if (Math.Abs(g.ActualRof - desired) <= .006) return true;
            if (!g.RofOwned && g.RofReadOk) { g.SavedRof=g.ActualRof; g.RofOwned=true; }
            bool ok = SetTerminal(g.Block, "Weapon ROF", desired);
            if (ok)
            {
                float observed;
                g.RofReadOk=TryReadTerminal(g.Block,"Weapon ROF",out observed);
                if(g.RofReadOk) g.ActualRof=observed;
                g.OwnedRof=desired;
                ok=g.RofReadOk && Math.Abs(g.ActualRof-desired)<=.006;
            }
            return ok;
        }

        private bool ApplyGunRange(Gun g, double range)
        {
            if (config().WeaponAwareEnabled && !FreshProfile(g)) return true;
            double bounded=config().WeaponAwareEnabled?WeaponProfilePolicy.Range(g.Profile,range):range;
            if(!BankPlanner.Finite(bounded)) return false;
            float desired = TrackingRangePolicy.Request(bounded);
            if (g.RangeReadOk && Math.Abs(g.ActualRange - desired) <= 5.0) return true;
            if(!g.RangeOwned && g.RangeReadOk) { g.SavedRange=g.ActualRange; g.RangeOwned=true; }
            bool ok = wc.SetTrackingRange(g.Entity, desired);
            if(ok) g.OwnedRange=desired;
            // Mirror the terminal slider where supported so the user can visibly verify control.
            SetTerminal(g.Block, "Weapon Range", desired);
            double effectiveRange;
            g.RangeReadOk = TrackingRangePolicy.TryObserved(wc.GetMaxWeaponRange(g.Entity, g.Part), out effectiveRange);
            if (g.RangeReadOk) g.ActualRange = effectiveRange;
            return ok && (!config().WeaponAwareEnabled || (g.RangeReadOk && Math.Abs(g.ActualRange-desired)<=5));
        }

        private void FailOpenNative(string why)
        {
            ReleaseHeatRanges(why);
            if(clientOwned.Count>0||JoiningServer()){ReleaseClientControl(why);WriteClientDebug(DateTime.UtcNow);if(JoiningServer())return;}
            if(config().ManagedDefenseEnabled && !config().LabEnabled) { ReleaseManagerHardware(why); foreach(var g in guns) ReleaseOuter(g,why); directControlState="MANAGER_NATIVE_RELEASE // "+why; return; }
            if(config().LabEnabled) { ReleaseLabHardware(why); directControlActive=false; directControlState="LAB_NATIVE_RELEASE // "+why; return; }
            foreach (var gun in guns)
            {
                ReleaseOuter(gun, "NATIVE_FALLBACK");
                PreemptivePolicy.Stop(gun.Outer, frame, gun.Shots, config());
                if (gun.OuterRangeOwned)
                {
                    gun.DesiredRange = config().EngagementRangeMeters;
                    ApplyGunRange(gun, gun.DesiredRange);
                    gun.OuterRangeOwned = false;
                }
            }
            if (nativeFallbackApplied && guns.Count > 0) return;
            for (int i = 0; i < guns.Count; i++)
            {
                Gun g = guns[i];
                if (g == null || g.Block == null) continue;
                if ((config().NativeLeadEnabled || config().WeaponAwareEnabled) && g.NativeControlKnown && g.NativeManual) continue;
                try
                {
                    System.Threading.Volatile.Write(ref g.Context, null);
                    g.CommandResult = "NATIVE_FALLBACK";
                    g.TargetedActive = false;
                    g.TargetedConfig = false;
                    SetTerminal(g.Block, "WC_TargetClosest", true);
                    SetTerminal(g.Block, "WC_Shoot Mode", 0L);
                    SetTerminal(g.Block, "WC_Supporting PD", true);
                    if (!config().WeaponAwareEnabled || (FreshProfile(g) && g.Profile.Adjustable)) SetTerminal(g.Block, "Weapon ROF", 1f);
                    if (!config().WeaponAwareEnabled || FreshProfile(g)) ApplyGunRange(g, g.DesiredRange > 0 ? g.DesiredRange : 3000);
                    SetTerminal(g.Block, "WC_Shoot", true);
                    if (g.Entity != null && wc.Ready) wc.ToggleWeaponFire(g.Entity, g.Part, true);
                    g.DirectConfigured = false;
                }
                catch { }
            }
            nativeFallbackApplied = true;
            directControlActive = false;
            directControlState = "NATIVE FALLBACK // " + why;
        }

        private static bool SetTerminal<T>(Sandbox.ModAPI.IMyTerminalBlock block, string id, T value)
        {
            if (block == null || string.IsNullOrWhiteSpace(id) || PlayerTargetingPolicy.IsFilter(id)) return false;
            try
            {
                // Official ModAPI terminal-property helper lives in Sandbox.ModAPI.Interfaces.
                // Calling it statically avoids PB-only extension-resolution differences.
                Sandbox.ModAPI.Interfaces.TerminalPropertyExtensions.SetValue<T>(block, id, value);
                return true;
            }
            catch { return false; }
        }

        private static bool TryReadTerminal<T>(Sandbox.ModAPI.IMyTerminalBlock block, string id, out T value)
        {
            value = default(T);
            try
            {
                value = Sandbox.ModAPI.Interfaces.TerminalPropertyExtensions.GetValue<T>(block, id);
                return true;
            }
            catch { return false; }
        }

        private static T ReadTerminal<T>(Sandbox.ModAPI.IMyTerminalBlock block, string id, T fallback)
        {
            if (block == null || string.IsNullOrWhiteSpace(id)) return fallback;
            try
            {
                return Sandbox.ModAPI.Interfaces.TerminalPropertyExtensions.GetValue<T>(block, id);
            }
            catch { return fallback; }
        }

        private void HookDirectShotMonitors()
        {
            if (!wc.ShotMonitorReady) return;
            for (int i = 0; i < guns.Count; i++) HookDirectShotMonitor(guns[i]);
        }

        private void HookDirectShotMonitor(Gun g)
        {
            if (g == null || g.Entity == null || g.ShotHooked || !wc.ShotMonitorReady) return;
            string liveWorld = liveFeed.WorldIdentity;
            string liveShip = grid == null ? null : Id(grid.EntityId);
            g.ShotCallback = delegate(long parent, int part, ulong projectile, long target, Vector3D position, bool exists)
            {
                if (!exists || projectile == 0) return;
                lock (shotGate)
                {
                    if (!g.SeenProjectiles.Add(projectile)) return;
                    g.SeenProjectileOrder.Enqueue(projectile);
                    while (g.SeenProjectileOrder.Count > 2048)
                    {
                        ulong old = g.SeenProjectileOrder.Dequeue();
                        g.SeenProjectiles.Remove(old);
                    }
                    g.Shots++;
                    var burstContext=System.Threading.Volatile.Read(ref g.Context);
                    var burstProfile=g.Profile;
                    if(labBurst.Enabled && burstContext!=null && burstContext.NativeRead=="PROJECTILE" &&
                        frame>=burstContext.NativeFrame && frame-burstContext.NativeFrame<=6 && burstProfile!=null && burstProfile.Valid)
                        labBurst.Observe(burstContext.ObservedNativeProjectile,frame,burstProfile.HealthDamage);
                    pbTotalShots++;
                    long serial = ++directShotSerial;
                    liveFeed.Shot(frame, g.EntityId, g.Part, projectile, target, Vector(position), liveWorld, liveShip);
                    if (testArmed && recorder != null)
                        recorder.Shot(serial, frame / 60.0, g.EntityId, g.Part, projectile, target, position,
                            System.Threading.Volatile.Read(ref g.Context), g.CommandResult);
                }
            };
            g.ShotHooked = wc.AddProjectileMonitor(g.Entity, g.Part, g.ShotCallback);
        }

        private void UnhookAllShots()
        {
            for (int i = 0; i < guns.Count; i++)
            {
                Gun g = guns[i];
                if (g == null || !g.ShotHooked || g.Entity == null || g.ShotCallback == null) continue;
                try { wc.RemoveProjectileMonitor(g.Entity, g.Part, g.ShotCallback); } catch { }
                g.ShotHooked = false;
                g.ShotCallback = null;
            }
        }

        public void Dispose()
        {
            DisposeDecoys();
            ReleaseHeatRanges("DISPOSE_RANGE_RESTORE");
            ReleaseClientControl("DISPOSE_RESTORE_REQUESTED_CHECK_SERVER");WriteClientDebug(DateTime.UtcNow);clientJournal.Dispose();
            ReleaseManagerHardware("DISPOSE");
            ReleaseLabHardware("DISPOSE");
            CloseLabDiagnostics();
            StopLiveTelemetry();
            try { EndTest("PLUGIN_DISPOSED"); } catch { }
            try { if (recorder != null) recorder.Close(); } catch { }
            try { FailOpenNative("PLUGIN DISPOSE"); } catch { }
            try { UnhookAllShots(); } catch { }
        }

        private void WriteBridgeDiagnostic()
        {
            try
            {
                var d = new StringBuilder(1024);
                d.AppendLine("ZEO PDC BRIDGE DIAGNOSTIC");
                d.AppendLine("UTC=" + DateTime.UtcNow.ToString("o"));
                d.AppendLine("SHIP=" + (grid == null ? "NONE" : grid.EntityId.ToString(CultureInfo.InvariantCulture)));
                d.AppendLine("CORE=" + (wc.Ready ? "READY" : "WAIT") + " ENDPOINTS=" + wc.EndpointCount);
                d.AppendLine("BRIDGE=" + (bridgeBlock == null ? "MISSING" : "FOUND"));
                d.AppendLine("BRIDGE_NAME=" + (bridgeBlock == null ? "" : (bridgeBlock.CustomName ?? "")));
                d.AppendLine("BRIDGE_ENTITY=" + (bridgeBlock == null ? "0" : bridgeBlock.EntityId.ToString(CultureInfo.InvariantCulture)));
                d.AppendLine("TEXT_PANEL=" + (bridgePanel == null ? "NO" : "YES"));
                d.AppendLine("TX_SEQ=" + bridgeSeq);
                d.AppendLine("TX_OK=" + bridgeLastWriteOk);
                d.AppendLine("TX_AGE_S=" + (bridgeLastWriteFrame < 0 ? "999" : Math.Max(0, (frame - bridgeLastWriteFrame) / 60.0).ToString("0.00", CultureInfo.InvariantCulture)));
                d.AppendLine("TX_RESULT=" + bridgeLastWriteError);
                d.AppendLine("ACK_SEQ=" + bridgeAckSeq);
                d.AppendLine("PB_MODE=" + bridgePbMode);
                d.AppendLine("PB_CONTROL_FRESH=" + BridgeControlFresh());
                d.AppendLine("TEST=" + testState + " ARMED=" + testArmed);
                d.AppendLine("PDC_BLOCKS=" + PhysicalPdcCount() + " PARTS=" + guns.Count);
                File.WriteAllText(Path.Combine(dataDir, "bridge-diagnostic.txt"), d.ToString());
            }
            catch { }
        }

        private void ReadBridgeAck()
        {
            if (bridgePanel == null) return;
            try
            {
                string text = bridgePanel.GetText();
                if (string.IsNullOrWhiteSpace(text)) return;
                string[] lines = text.Replace("\r", "").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("ACKSEQ=", StringComparison.OrdinalIgnoreCase))
                    {
                        long v;
                        if (long.TryParse(line.Substring(7).Trim(), out v) && v >= bridgeAckSeq)
                        {
                            if (v > bridgeAckSeq) bridgeAckFrame = frame;
                            bridgeAckSeq = v;
                        }
                        continue;
                    }
                    if (line.StartsWith("MODE=", StringComparison.OrdinalIgnoreCase))
                    {
                        bridgePbMode = line.Substring(5).Trim();
                        bridgePbModeFrame = frame;
                        continue;
                    }
                    if (line.StartsWith("SHOTS=", StringComparison.OrdinalIgnoreCase))
                    {
                        long.TryParse(line.Substring(6).Trim(), out pbTotalShots);
                        continue;
                    }
                    if (line.StartsWith("S=", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] sp = line.Substring(2).Split('|');
                        if (sp.Length >= 9)
                        {
                            long serial, gunId, target; int shotPart; ulong projectile; double st, x, y, z;
                            if (long.TryParse(sp[0], out serial) && serial > lastShotEventSerial &&
                                double.TryParse(sp[1], NumberStyles.Float, CultureInfo.InvariantCulture, out st) &&
                                long.TryParse(sp[2], out gunId) && int.TryParse(sp[3], out shotPart) && ulong.TryParse(sp[4], out projectile) && long.TryParse(sp[5], out target) &&
                                double.TryParse(sp[6], NumberStyles.Float, CultureInfo.InvariantCulture, out x) && double.TryParse(sp[7], NumberStyles.Float, CultureInfo.InvariantCulture, out y) && double.TryParse(sp[8], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                            {
                                lastShotEventSerial = serial;
                                if (testArmed && recorder != null) recorder.Shot(serial, st, gunId, shotPart, projectile, target, new Vector3D(x,y,z));
                            }
                        }
                        continue;
                    }
                    if (!line.StartsWith("P=", StringComparison.OrdinalIgnoreCase)) continue;
                    string[] p = line.Substring(2).Split('|');
                    if (p.Length < 6) continue;
                    long id; int part; double hp; int ammo; long shots;
                    if (!long.TryParse(p[0], out id) || !int.TryParse(p[1], out part)) continue;
                    Gun g = guns.FirstOrDefault(x => x.EntityId == id && x.Part == part);
                    if (g == null) continue;
                    if (double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out hp)) g.Heat = Math.Max(0, Math.Min(100, hp));
                    if (int.TryParse(p[3], out ammo)) g.Ammo = ammo;
                    if (long.TryParse(p[4], out shots)) g.Shots = shots;
                    g.Functional = p[5] == "1";
                    if (p.Length >= 17)
                    {
                        long wt; double tx, ty, tz, px, py, pz;
                        if (long.TryParse(p[8], out wt)) g.WcTargetId = wt;
                        g.WcTargetType = p[9] ?? "NONE";
                        if (double.TryParse(p[10], NumberStyles.Float, CultureInfo.InvariantCulture, out tx) &&
                            double.TryParse(p[11], NumberStyles.Float, CultureInfo.InvariantCulture, out ty) &&
                            double.TryParse(p[12], NumberStyles.Float, CultureInfo.InvariantCulture, out tz))
                        {
                            g.WcTargetPos = new Vector3D(tx,ty,tz);
                            g.WcTargetValid = g.WcTargetId != 0 || (g.WcTargetType.IndexOf("Projectile", StringComparison.OrdinalIgnoreCase) >= 0 || g.WcTargetType.IndexOf("Missile", StringComparison.OrdinalIgnoreCase) >= 0);
                        }
                        g.WcPredictedValid = p[13] == "1";
                        if (double.TryParse(p[14], NumberStyles.Float, CultureInfo.InvariantCulture, out px) &&
                            double.TryParse(p[15], NumberStyles.Float, CultureInfo.InvariantCulture, out py) &&
                            double.TryParse(p[16], NumberStyles.Float, CultureInfo.InvariantCulture, out pz))
                            g.WcPredictedPos = new Vector3D(px,py,pz);
                    }
                    g.PbTelemetry = true;
                }
            }
            catch { }
        }

        private bool BridgeControlFresh()
        {
            double age = bridgeAckFrame < 0 ? 999 : Math.Max(0, (frame - bridgeAckFrame) / 60.0);
            return bridgeAckSeq > 0 && age <= config().BridgeStaleSeconds && bridgePbMode.Equals("PLUGIN", StringComparison.OrdinalIgnoreCase);
        }

        public PdcSnapshot BuildSnapshot()
        {
            var s = new PdcSnapshot();
            s.Ship = ShipName;
            AddDefenseStatus(s);
            s.BankObserver = bankObserverStatus;
            s.CoreReady = wc.Ready;
            s.CoreEndpointCount = wc.EndpointCount;
            s.DirectControlActive = directControlActive;
            s.ControlState = directControlState;
            s.ShotMonitorReady = wc.ShotMonitorReady;
            s.StableProjectileTracking = wc.StableProjectileReady;
            s.CurrentVolley = testArmed ? Math.Max(1, (waveSeen / Math.Max(1, config().ExpectedInbound)) + (waveSeen % Math.Max(1, config().ExpectedInbound) == 0 && waveSeen > 0 ? 0 : 1)) : 0;
            s.CompletedVolleys = completedVolleys;

            // Legacy bridge fields are retained for overlay/backward compatibility, but
            // they now describe the OPTIONAL watchdog heartbeat only.
            s.BridgeSeq = bridgeSeq;
            s.BridgeAckSeq = 0;
            s.BridgeAckAgeSeconds = bridgeLastWriteFrame < 0 ? 999 : Math.Max(0, (frame - bridgeLastWriteFrame) / 60.0);
            s.BridgeState = bridgeBlock == null ? "OPTIONAL / NONE" : (bridgeLastWriteOk ? "WATCHDOG HEARTBEAT" : "WATCHDOG TX ERROR");
            s.PbMode = "WATCHDOG OPTIONAL";
            s.PbControlFresh = directControlActive;

            s.PdcCount = PhysicalPdcCount();
            s.PdcOnline = PhysicalOnlineCount();
            s.AverageHpPercent = guns.Count == 0 ? 0 : guns.Average(g => g.Hp);
            s.AverageHeatPercent = guns.Count == 0 ? 0 : guns.Average(g => g.Heat);
            s.HottestHeatPercent = HottestHeat();
            Gun hottest = guns.OrderByDescending(g => g.Heat).FirstOrDefault();
            s.HottestPdc = hottest == null ? "--" : hottest.Name;
            s.TotalAmmo = TotalAmmo();
            s.TotalShots = pbTotalShots;
            s.ActiveInbound = tracks.Count(t => !t.Resolved);
            s.WaveSeen = waveSeen;
            s.WaveResolved = waveResolved;
            s.Intercepts = intercepts;
            s.Hits = hits;
            s.Misses = misses;
            s.Unknown = unknown;
            s.GoalProgress = GoalText();
            s.TestState = RecorderState;
            s.WaveId = waveId;
            s.TestArmed = testArmed;
            s.FireLockout = fireLockout;
            s.TestFolder = TestFolder;
            s.LastEvent = lastEvent;
            foreach (var grp in guns.GroupBy(g => g.EntityId).OrderBy(g => g.Key))
            {
                Gun first = grp.First();
                s.Pdcs.Add(new PdcRow
                {
                    EntityId = grp.Key, Part = -1, Name = first.Name,
                    HpPercent = grp.Average(g => g.Hp),
                    HeatPercent = grp.Max(g => g.Heat),
                    Ammo = grp.Sum(g => Math.Max(0, g.Ammo)),
                    Functional = grp.Any(g => g.Functional),
                    Allowed = grp.Any(g => g.Allowed),
                    TrackId = grp.Where(g => g.TrackId != 0).Select(g => g.TrackId).FirstOrDefault(),
                    MatchErrorMeters = grp.Min(g => g.MatchError),
                    // UI reports terminal readback when available so the player can verify
                    // that plugin-direct control actually reached the weapon.
                    Rof = grp.Min(g => g.ActualRof > 0 ? g.ActualRof : g.Rof),
                    KillWindowScore = grp.Max(g => g.KillWindowScore),
                    AlignmentDeg = grp.Min(g => g.AlignmentDeg),
                    PkState = grp.OrderByDescending(g => g.Allowed).ThenByDescending(g => g.KillWindowScore).Select(g => g.PkState).FirstOrDefault(),
                    RangeMeters = grp.Max(g => g.ActualRange > 0 ? g.ActualRange : g.DesiredRange),
                    Shots = grp.Sum(g => g.Shots)
                });
            }
            foreach (Track t in tracks.Where(x => !x.Resolved).OrderBy(x => x.Tti).ThenBy(x => x.Hull).Take(12))
            {
                s.Tracks.Add(new TrackRow
                {
                    TrackId = t.Id, RangeMeters = t.Range, HullDistanceMeters = t.Hull,
                    ClosingMps = t.HullClosing, TtiSeconds = t.Tti, MinHullDistanceMeters = t.MinHull,
                    State = t.State, AssignedShooters = t.Assigned
                });
            }
            return s;
        }

        private void CopyTestDiagnostics(TestRecorder r)
        {
            if (r == null) return;
            try
            {
                string src = Path.Combine(dataDir, "coresystems-endpoints.txt");
                if (File.Exists(src)) File.Copy(src, Path.Combine(r.Folder, "coresystems-endpoints.txt"), true);
                src = Path.Combine(dataDir, "pdc-scan.txt");
                if (File.Exists(src)) File.Copy(src, Path.Combine(r.Folder, "pdc-scan.txt"), true);
            }
            catch { }
        }

        private double HullDistance(Vector3D p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < constructGrids.Count; i++)
            {
                IMyCubeGrid g = constructGrids[i];
                if (g == null || g.Closed) continue;
                BoundingBoxD b;
                try { b = g.WorldAABB; } catch { continue; }
                double x = p.X < b.Min.X ? b.Min.X : p.X > b.Max.X ? b.Max.X : p.X;
                double y = p.Y < b.Min.Y ? b.Min.Y : p.Y > b.Max.Y ? b.Max.Y : p.Y;
                double z = p.Z < b.Min.Z ? b.Min.Z : p.Z > b.Max.Z ? b.Max.Z : p.Z;
                double d = Vector3D.Distance(p, new Vector3D(x, y, z));
                if (d < best) best = d;
            }
            return best == double.MaxValue ? (controller == null ? 0 : Vector3D.Distance(p, controller.WorldAABB.Center)) : best;
        }

        private int TotalAmmo()
        {
            long total = 0;
            for (int i = 0; i < guns.Count; i++) total += Math.Max(0, guns[i].Ammo);
            return (int)Math.Min(int.MaxValue, total);
        }

        private double HottestHeat()
        {
            double h = 0;
            for (int i = 0; i < guns.Count; i++) if (guns[i].Heat > h) h = guns[i].Heat;
            return h;
        }

        private static string TrackText(Track t)
        {
            return "T" + t.Id.ToString("00") + " range=" + t.Range.ToString("0.0", CultureInfo.InvariantCulture) +
                   " hull=" + t.Hull.ToString("0.0", CultureInfo.InvariantCulture) +
                   " min=" + t.MinHull.ToString("0.0", CultureInfo.InvariantCulture) +
                   " close=" + t.HullClosing.ToString("0.0", CultureInfo.InvariantCulture) +
                   " tti=" + (double.IsInfinity(t.Tti) ? "INF" : t.Tti.ToString("0.000", CultureInfo.InvariantCulture));
        }

        private static void GetMechanicalConstructGrids(IMyCubeGrid root, Sandbox.ModAPI.IMyShipController ctrl, List<IMyCubeGrid> result)
        {
            if (root == null || result == null) return;
            AddGridUnique(result, root);
            try
            {
                if (MyAPIGateway.GridGroups != null)
                {
                    var grouped = new List<IMyCubeGrid>();
                    MyAPIGateway.GridGroups.GetGroup(root, GridLinkTypeEnum.Mechanical, grouped);
                    for (int i = 0; i < grouped.Count; i++) AddGridUnique(result, grouped[i]);
                }
            }
            catch { }
        }

        private static void AddGridUnique(List<IMyCubeGrid> list, IMyCubeGrid g)
        {
            if (g == null) return;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].EntityId == g.EntityId) return;
            list.Add(g);
        }

        private sealed class TestRecorder
        {
            public readonly string Folder;
            private readonly object ioGate = new object();
            private readonly Dictionary<string, StreamWriter> streams = new Dictionary<string, StreamWriter>();
            private bool closed;
            private readonly TelemetryProvenance provenance;
            public void Configuration(PdcConfig effective, PdcConfig requested)
            {
                lock (ioGate)
                {
                    if (closed) return;
                    try { provenance.RecordConfiguration(effective, requested, "SETTINGS_APPLIED"); }
                    catch (Exception ex) { writeErrors++; log("Configuration provenance failed: " + ex.Message); Close(); }
                }
            }
            private void WriteText(string path, string text)
            {
                lock (ioGate)
                {
                    if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) text = provenance.StampCsv(path, text, true);
                    else if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) text += provenance.TextFields();
                    File.WriteAllText(path, text);
                }
            }
            private int writeErrors;
            private void Append(string path, string text)
            {
                lock (ioGate)
                {
                    if (closed) return;
                    try
                    {
                        StreamWriter writer;
                        if (!streams.TryGetValue(path, out writer))
                        {
                            writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false), 32768);
                            streams.Add(path, writer);
                        }
                        if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) text = provenance.StampCsv(path, text, false);
                        writer.Write(text);
                    }
                    catch (Exception ex) { writeErrors++; log("Recorder write failed: " + ex.Message); }
                }
            }
            public void Flush()
            {
                lock (ioGate) foreach (var writer in streams.Values)
                    try { writer.Flush(); } catch (Exception ex) { writeErrors++; log("Recorder flush failed: " + ex.Message); }
            }
            public void Close()
            {
                lock (ioGate)
                {
                    if (closed) return;
                    foreach (var writer in streams.Values)
                        try { writer.Dispose(); } catch (Exception ex) { writeErrors++; log("Recorder close failed: " + ex.Message); }
                    streams.Clear(); closed = true;
                    try { WriteText(Path.Combine(Folder, "recorder_status.txt"), "Closed=True\r\nWriteErrors=" + writeErrors + "\r\n"); } catch (Exception ex) { log("Recorder status write failed: " + ex.Message); }
                }
            }

            private readonly string eventsPath;
            private readonly string tracksPath;
            private readonly string predictionsPath;
            private readonly string shotsPath;
            private readonly string samplesPath;
            private readonly string trackSamplesPath;
            private readonly Action<string> log;

            public TestRecorder(string dataDir, int wave, PdcConfig cfg, string ship, int endpoints, Action<string> logger)
                : this(dataDir, wave, cfg, ship, endpoints, logger, () => null, null) { }
            public TestRecorder(string dataDir, int wave, PdcConfig cfg, string ship, int endpoints, Action<string> logger, Func<int?> clock, PdcConfig requested)
            {
                log = logger ?? delegate { };
                Folder = Path.Combine(dataDir, "Tests", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_SESSION_" + wave.ToString("000") + "_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Folder);
                provenance = new TelemetryProvenance(Folder, clock, cfg, requested);
                eventsPath = Path.Combine(Folder, "events.jsonl");
                tracksPath = Path.Combine(Folder, "torpedoes.csv");
                predictionsPath = Path.Combine(Folder, "predictions.csv");
                shotsPath = Path.Combine(Folder, "shots.csv");
                samplesPath = Path.Combine(Folder, "pdc_samples.csv");
                trackSamplesPath = Path.Combine(Folder, "track_samples.csv");
                WriteText(tracksPath, "track,projectile_id,volley,result,confidence,range_m,last_hull_m,min_hull_m,closing_mps,tti_s,speed_mps,shots_spent\r\n");
                WriteText(predictionsPath, "track,range_m,hull_m,speed_mps,hull_closing_mps,predicted_tti_s\r\n");
                WriteText(shotsPath, "serial,plugin_time_s,gun_entity,part,projectile_id,target_id,x,y,z,assigned_track,assigned_projectile,assigned_volley,assignment_allowed,gate_state,experiment_mode,assignment_hull_m,assignment_tti_s,observation_age_s,command_age_s,last_command_result,target_association,native_observed_projectile,native_read_status,native_observation_age_s,native_association," + string.Join(",", TelemetryProvenance.ObservationColumns.Split(',').Select(name => "round_" + name)) + "\r\n");
                WriteText(samplesPath, "time_s,gun_entity,part,hp_pct,heat_pct,ammo,shots,functional,allowed,track,match_error_m,alignment_deg,kill_window_score,pk_state,desired_rof,actual_rof,desired_range_m,actual_range_m,wc_target_id,wc_target_type,target_x,target_y,target_z,predicted_valid,pred_x,pred_y,pred_z,ship_turn_dps\r\n");
                WriteText(trackSamplesPath, "time_s,track,projectile_id,volley,x,y,z,vx,vy,vz,speed_mps,range_m,hull_m,min_hull_m,hull_closing_mps,tti_s,assigned,state,shots_spent,stable_id,counted_physical," + TelemetryProvenance.ObservationColumns + "\r\n");
                WriteText(Path.Combine(Folder, "pdc_diagnostics.csv"), "time_s,gun_entity,part,mode,control_state,target_api_status,target_flag1,target_flag2,target_flag3,ready_api_status,ready_value,shooting_api_status,shooting_value,scope_valid,scope_x,scope_y,scope_z,dir_x,dir_y,dir_z,rof_read_ok,range_read_ok,last_command_result,assigned_track,assigned_projectile,assigned_volley,observation_age_s,target_association\r\n");
                WriteText(Path.Combine(Folder, "assignments.csv"), "time_s,gun_entity,part,old_track,new_track,assigned_projectile,volley,allowed,gate_state,hull_m,tti_s,observation_age_s,mode\r\n");
                WriteText(Path.Combine(Folder, "volley_summary.csv"), "volley,expected,intercepts_probable,danger_entries_150m,miss_outbound,unknown,defense_success_pct,start_heat_pct,end_heat_pct,peak_heat_pct,battery_shots_during_volley_nonadditive,duration_s\r\n");
                WriteText(Path.Combine(Folder, "target_requests.csv"), "time_s,gun_entity,part,requested_projectile,assigned_track,result,gun_shots,target_association\r\n");
                ObserverHeaders();
                WriteText(Path.Combine(Folder, "settings.txt"),
                    "Version=0.3.30-TIGHT-BANKS\r\n" +
                    "Profile=NATIVE_BASELINE_BANK_OBSERVER\r\n" +
                    "ExperimentMode=" + (cfg.BypassAlignmentGate ? "B_GATE_BYPASS" : "A_ORIGINAL_GATE") + "\r\n" +
                    "TelemetrySchema=6\r\nBankPlanner=OBSERVATION_ONLY\r\nTargetedRequestServiceHz=0\r\nControlCadenceHz=10\r\nSampleCadenceHz=5\r\nTargetAssociation=SCHEDULER_ONLY_UNCONFIRMED\r\n" +
                    "RunUtc=" + DateTime.UtcNow.ToString("o") + "\r\n" +
                    "Ship=" + ship + "\r\nSession=" + wave + "\r\nExpectedInboundPerVolley=" + cfg.ExpectedInbound +
                    "\r\nControl=PLUGIN_DIRECT" +
                    "\r\nPBRequired=0" +
                    "\r\nHitRadiusMeters=" + cfg.HitRadiusMeters.ToString(CultureInfo.InvariantCulture) +
                    "\r\nEngagementRangeMeters=" + cfg.EngagementRangeMeters.ToString(CultureInfo.InvariantCulture) +
                    "\r\nTwoShooterTtiSeconds=" + cfg.TwoShooterTtiSeconds.ToString(CultureInfo.InvariantCulture) +
                    "\r\nTwoShooterRangeMeters=" + cfg.TwoShooterRangeMeters.ToString(CultureInfo.InvariantCulture) +
                    "\r\nThreeShooterTtiSeconds=" + cfg.ThreeShooterTtiSeconds.ToString(CultureInfo.InvariantCulture) +
                    "\r\nThreeShooterRangeMeters=" + cfg.ThreeShooterRangeMeters.ToString(CultureInfo.InvariantCulture) +
                    "\r\nEmergencyTtiSeconds=" + cfg.EmergencyTtiSeconds.ToString(CultureInfo.InvariantCulture) +
                    "\r\nEmergencyRangeMeters=" + cfg.EmergencyRangeMeters.ToString(CultureInfo.InvariantCulture) +
                    "\r\nEmergencyShooters=" + cfg.EmergencyShooters +
                    "\r\nBaseRof=" + cfg.BaseRof.ToString(CultureInfo.InvariantCulture) +
                    "\r\nThreeShooterRof=" + cfg.ThreeShooterRof.ToString(CultureInfo.InvariantCulture) +
                    "\r\nEmergencyRof=" + cfg.EmergencyRof.ToString(CultureInfo.InvariantCulture) +
                    "\r\nFullEmergencyRof=" + cfg.FullEmergencyRof.ToString(CultureInfo.InvariantCulture) +
                    "\r\nFullEmergencyTtiSeconds=" + cfg.FullEmergencyTtiSeconds.ToString(CultureInfo.InvariantCulture) +
                    "\r\nFullEmergencyRangeMeters=" + cfg.FullEmergencyRangeMeters.ToString(CultureInfo.InvariantCulture) +
                    "\r\nHeatThrottleStartPercent=" + cfg.HeatThrottleStartPercent.ToString(CultureInfo.InvariantCulture) +
                    "\r\nPkWindowEnabled=" + cfg.PkWindowEnabled +
                    "\r\nStableIdAuthoritative=" + cfg.StableIdAuthoritative +
                    "\r\nStableFallbackGraceSeconds=" + cfg.StableFallbackGraceSeconds.ToString(CultureInfo.InvariantCulture) +
                    "\r\nUrgentCoverageRangeMeters=" + cfg.UrgentCoverageRangeMeters.ToString(CultureInfo.InvariantCulture) +
                    "\r\nUrgentCoverageTtiSeconds=" + cfg.UrgentCoverageTtiSeconds.ToString(CultureInfo.InvariantCulture) +
                    "\r\nShotHelpThresholds=" + cfg.ShotHelpSecond + "," + cfg.ShotHelpThird + "," + cfg.ShotHelpFourth +
                    "\r\nColdBoostHeatPercent=" + cfg.ColdBoostHeatPercent.ToString(CultureInfo.InvariantCulture) +
                    "\r\nColdRofBoost=" + cfg.ColdRofBoost.ToString(CultureInfo.InvariantCulture) +
                    "\r\nSurvivalHeatOverrideRangeMeters=" + cfg.SurvivalHeatOverrideRangeMeters.ToString(CultureInfo.InvariantCulture) +
                    "\r\nSurvivalHeatOverrideTtiSeconds=" + cfg.SurvivalHeatOverrideTtiSeconds.ToString(CultureInfo.InvariantCulture) +
                    "\r\nManeuverSupportDegPerSec=" + cfg.ManeuverSupportDegPerSec.ToString(CultureInfo.InvariantCulture) +
                    "\r\nManeuverRangeBoostMeters=" + cfg.ManeuverRangeBoostMeters.ToString(CultureInfo.InvariantCulture) +
                    "\r\nWeaponCoreFireDistribution=ENABLED" +
                    "\r\nTransientZeroScanGuard=ENABLED" +
                    "\r\nManeuverTelemetry=ship_turn_dps" +
                    "\r\nPkForcedRangeMeters=" + cfg.PkForcedRangeMeters.ToString(CultureInfo.InvariantCulture) +
                    "\r\nPkForcedTtiSeconds=" + cfg.PkForcedTtiSeconds.ToString(CultureInfo.InvariantCulture) +
                    "\r\nPkAnglesDeg=" + cfg.PkFarAngleDeg.ToString(CultureInfo.InvariantCulture) + "," + cfg.PkMidFarAngleDeg.ToString(CultureInfo.InvariantCulture) + "," + cfg.PkMidAngleDeg.ToString(CultureInfo.InvariantCulture) + "," + cfg.PkNearAngleDeg.ToString(CultureInfo.InvariantCulture) +
                    "\r\nRangeRof=" + cfg.FarRof.ToString(CultureInfo.InvariantCulture) + "," + cfg.MidFarRof.ToString(CultureInfo.InvariantCulture) + "," + cfg.MidRof.ToString(CultureInfo.InvariantCulture) + "," + cfg.CloseRof.ToString(CultureInfo.InvariantCulture) + "," + cfg.NearRof.ToString(CultureInfo.InvariantCulture) +
                    "\r\nCoreSystemsEndpoints=" + endpoints + "\r\n");
            }

            public void ObserverRow(string file, params object[] values)
            {
                string row = string.Join(",", values.Select(v => v is double ? ObserverNumber((double)v) :
                    v is float ? ObserverNumber((float)v) : Csv(Convert.ToString(v, CultureInfo.InvariantCulture))));
                if (file == "projectile_health.csv" || file == "health_events.csv" || file == "bank_ship.csv" || file == "native_targets.csv")
                {
                    if (values[0] is string) row += "," + TelemetryProvenance.ObservationColumns;
                    else
                    {
                        int logged = (int)Math.Round(Convert.ToDouble(values[0], CultureInfo.InvariantCulture) * 60);
                        int? observed = logged;
                        string source = file == "bank_ship.csv" ? "CONTROLLER_WORLD_MATRIX_AND_VELOCITY" :
                            file == "native_targets.csv" ? "NATIVE_TARGET_OBSERVER" : Convert.ToString(values[values.Length - 1], CultureInfo.InvariantCulture);
                        if (file == "projectile_health.csv")
                        {
                            double age = Convert.ToDouble(values[4], CultureInfo.InvariantCulture);
                            observed = Convert.ToString(values[3], CultureInfo.InvariantCulture) == "OK" && BankPlanner.Finite(age) && age >= 0 && age * 60 <= logged ? (int?)(logged - (int)Math.Round(age * 60)) : null;
                        }
                        if (file == "native_targets.csv" && Convert.ToString(values[3], CultureInfo.InvariantCulture) != "PROJECTILE") observed = null;
                        row += "," + TelemetryProvenance.ObservationCsv(logged, observed, source);
                    }
                }
                Append(Path.Combine(Folder, file), row + "\r\n");
            }
            private static string ObserverNumber(double value) { return BankPlanner.Finite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : ""; }
            private void ObserverHeaders()
            {
                ObserverRow("health_events.csv", "time_s", "track", "projectile_id", "previous_health", "health", "drop", "association", "source");
                ObserverRow("projectile_health.csv", "time_s", "track", "projectile_id", "read_status", "age_s", "health", "ammo", "vx", "vy", "vz", "ax", "ay", "az", "source");
                ObserverRow("native_targets.csv", "time_s", "gun_entity", "part", "status", "native_projectile", "scheduler_projectile", "shooting", "ready", "schema", "assembly");
                ObserverRow("bank_guns.csv", "time_s", "gun_entity", "part", "bank", "profile", "ammo", "x", "y", "z", "vx", "vy", "vz", "dir_x", "dir_y", "dir_z", "usable", "speed_assumed", "rof_assumed", "health_per_hit_assumed", "range", "heat_pct");
                ObserverRow("bank_hulls.csv", "time_s", "grid_entity", "min_x", "min_y", "min_z", "max_x", "max_y", "max_z", "padding_m");
                ObserverRow("bank_ship.csv", "time_s", "x", "y", "z", "vx", "vy", "vz", "omega_x", "omega_y", "omega_z", "right_x", "right_y", "right_z", "up_x", "up_y", "up_z", "back_x", "back_y", "back_z");
                ObserverRow("bank_queue.csv", "time_s", "track", "projectile_id", "path", "side", "entry_s", "cpa_s", "cpa_center_m", "health", "primary_gun", "primary_part", "bank", "rank", "backup_gun", "backup_part", "start_s", "first_hit_s", "finish_s", "slack_s", "minimum_hits", "scenario_shots", "assumed_p_hit", "scenario_p_kill", "intercept_local_x", "intercept_local_y", "intercept_local_z", "status", "control");
            }

            public void Event(string type, string detail)
            {
                try
                {
                    lock (ioGate) Append(eventsPath, "{" + provenance.JsonFields() + ",\"utc\":\"" + DateTime.UtcNow.ToString("o") + "\",\"type\":\"" + Esc(type) + "\",\"detail\":\"" + Esc(detail) + "\"}\r\n");
                }
                catch (Exception ex) { log("Test event write failed: " + ex.Message); }
            }

            public void Track(int id, ulong projectileId, int volley, string result, string confidence, double range, double hull, double min, double closing, double tti, double speed, int shotsSpent)
            {
                try
                {
                    Append(tracksPath, id + "," + projectileId + "," + volley + "," + result + "," + confidence + "," + F(range) + "," + F(hull) + "," + F(min) + "," + F(closing) + "," + (double.IsInfinity(tti) ? "" : F(tti)) + "," + F(speed) + "," + shotsSpent + "\r\n");
                }
                catch { }
            }

            public void Inventory(List<Gun> guns)
            {
                try
                {
                    var b = new StringBuilder("entity,part,name,hp_pct,heat_pct,ammo,shots,functional\r\n");
                    for (int i=0;i<guns.Count;i++)
                    {
                        Gun g=guns[i]; b.Append(g.EntityId).Append(',').Append(g.Part).Append(',').Append(Csv(g.Name)).Append(',').Append(F(g.Hp)).Append(',').Append(F(g.Heat)).Append(',').Append(g.Ammo).Append(',').Append(g.Shots).Append(',').Append(g.Functional?1:0).AppendLine();
                    }
                    WriteText(Path.Combine(Folder,"ship_inventory.txt"),b.ToString());
                }
                catch { }
            }

            public void Sample(double time, List<Gun> guns, double shipTurnDegPerSec)
            {
                try
                {
                    var b=new StringBuilder();
                    for(int i=0;i<guns.Count;i++)
                    {
                        Gun g=guns[i]; b.Append(F(time)).Append(',').Append(g.EntityId).Append(',').Append(g.Part).Append(',').Append(F(g.Hp)).Append(',').Append(F(g.Heat)).Append(',').Append(g.Ammo).Append(',').Append(g.Shots).Append(',').Append(g.Functional?1:0).Append(',').Append(g.Allowed?1:0).Append(',').Append(g.TrackId).Append(',').Append(double.IsInfinity(g.MatchError)?"":F(g.MatchError)).Append(',')
                         .Append(g.AlignmentDeg>=998?"":F(g.AlignmentDeg)).Append(',').Append(F(g.KillWindowScore)).Append(',').Append(Csv(g.PkState)).Append(',')
                         .Append(F(g.Rof)).Append(',').Append(F(g.ActualRof)).Append(',')
                         .Append(F(g.DesiredRange)).Append(',').Append(F(g.ActualRange)).Append(',')
                         .Append(g.WcTargetId).Append(',').Append(Csv(g.WcTargetType)).Append(',')
                         .Append(F(g.WcTargetPos.X)).Append(',').Append(F(g.WcTargetPos.Y)).Append(',').Append(F(g.WcTargetPos.Z)).Append(',')
                         .Append(g.WcPredictedValid?1:0).Append(',').Append(F(g.WcPredictedPos.X)).Append(',').Append(F(g.WcPredictedPos.Y)).Append(',').Append(F(g.WcPredictedPos.Z)).Append(',').Append(F(shipTurnDegPerSec)).AppendLine();
                    }
                    Append(samplesPath,b.ToString());
                } catch { }
            }

            public void TrackSample(double time, List<Track> rows)
            {
                try
                {
                    var b = new StringBuilder();
                    for (int i = 0; i < rows.Count; i++)
                    {
                        Track t = rows[i];
                        if (t == null || t.Resolved) continue;
                        b.Append(F(time)).Append(',').Append(t.Id).Append(',').Append(t.ProjectileId).Append(',').Append(t.VolleyIndex).Append(',')
                         .Append(F(t.Pos.X)).Append(',').Append(F(t.Pos.Y)).Append(',').Append(F(t.Pos.Z)).Append(',')
                         .Append(F(t.Vel.X)).Append(',').Append(F(t.Vel.Y)).Append(',').Append(F(t.Vel.Z)).Append(',')
                         .Append(F(t.Vel.Length())).Append(',').Append(F(t.Range)).Append(',').Append(F(t.Hull)).Append(',')
                         .Append(F(t.MinHull)).Append(',').Append(F(t.HullClosing)).Append(',')
                         .Append(double.IsInfinity(t.Tti) ? "" : F(t.Tti)).Append(',').Append(t.Assigned).Append(',').Append(t.State).Append(',')
                         .Append(t.ShotsSpent).Append(',').Append(t.StableId ? 1 : 0).Append(',').Append(t.CountedPhysical ? 1 : 0).Append(',')
                         .Append(TelemetryProvenance.ObservationCsv((int)Math.Round(time * 60), t.PositionObservationFrame < 0 ? (int?)null : t.PositionObservationFrame, t.PositionObservationSource)).AppendLine();
                    }
                    if (b.Length > 0) Append(trackSamplesPath, b.ToString());
                }
                catch { }
            }

            public void Shot(long serial,double time,long gun,int part,ulong projectile,long target,Vector3D pos, ShotContext context = null, string commandResult = "UNAVAILABLE")
            {
                string extra = context == null ? "0,0,0,0,UNAVAILABLE,UNAVAILABLE,,,,," + Csv(commandResult) + ",UNKNOWN" :
                    context.Track + "," + context.Projectile + "," + context.Volley + "," + (context.Allowed ? 1 : 0) + "," + Csv(context.Gate) + "," + Csv(context.Mode) + "," +
                    F(context.Hull) + "," + F(context.Tti) + "," + F(Math.Max(0, time - context.LastObservedFrame / 60.0)) + "," + F(Math.Max(0, time - context.CommandFrame / 60.0)) + "," + Csv(commandResult) + ",SCHEDULER_ONLY_UNCONFIRMED";
                string native = context == null ? "0,UNAVAILABLE,,UNKNOWN" : context.ObservedNativeProjectile + "," + Csv(context.NativeRead ?? "UNREAD") + "," +
                    F(Math.Max(0, time - context.NativeFrame / 60.0)) + ",LAST_NATIVE_OBSERVATION_NOT_HIT_CONFIRMATION";
                Append(shotsPath,serial+","+F(time)+","+gun+","+part+","+projectile+","+target+","+F(pos.X)+","+F(pos.Y)+","+F(pos.Z)+","+extra+","+native+","+TelemetryProvenance.ObservationCsv((int)Math.Round(time*60), (int)Math.Round(time*60), "SHOT_EVENT_RECEIPT")+"\r\n");
            }

            public void VolleySummary(int volley, int expected, int intercepted, int danger, int miss, int unk, double startHeat, double endHeat, double peakHeat, long shots, double duration, double rate)
            {
                try
                {
                    Append(Path.Combine(Folder, "volley_summary.csv"),
                        volley + "," + expected + "," + intercepted + "," + danger + "," + miss + "," + unk + "," + F(rate) + "," +
                        F(startHeat) + "," + F(endHeat) + "," + F(peakHeat) + "," + shots + "," + F(duration) + "\r\n");
                }
                catch { }
            }

            public void Diagnostics(int frame, List<Gun> guns, string mode, string control)
            {
                var b = new StringBuilder();
                foreach (Gun g in guns)
                {
                    var c = System.Threading.Volatile.Read(ref g.Context);
                    b.Append(string.Join(",", F(frame/60.0), g.EntityId, g.Part, Csv(mode), Csv(control), g.TargetRead,
                        g.TargetFlag1 ? 1 : 0, g.TargetFlag2 ? 1 : 0, g.TargetFlag3 ? 1 : 0,
                        g.ReadyRead, g.ReadyRead == "OK" ? (g.ReadyValue ? "1" : "0") : "",
                        g.ShootingRead, g.ShootingRead == "OK" ? (g.ShootingValue ? "1" : "0") : "", g.ScopeValid ? 1 : 0,
                        F(g.ScopeOrigin.X), F(g.ScopeOrigin.Y), F(g.ScopeOrigin.Z), F(g.ScopeDirection.X), F(g.ScopeDirection.Y), F(g.ScopeDirection.Z),
                        g.RofReadOk ? 1 : 0, g.RangeReadOk ? 1 : 0, g.CommandResult, c == null ? 0 : c.Track, c == null ? 0 : c.Projectile,
                        c == null ? 0 : c.Volley, c == null ? "" : F((frame-c.LastObservedFrame)/60.0), "SCHEDULER_ONLY_UNCONFIRMED")).AppendLine();
                }
                Append(Path.Combine(Folder, "pdc_diagnostics.csv"), b.ToString());
            }

            public void Assignment(int frame, Gun gun, ShotContext c)
            {
                Append(Path.Combine(Folder, "assignments.csv"), string.Join(",", F(frame/60.0), gun.EntityId, gun.Part, gun.PreviousTrack,
                    c.Track, c.Projectile, c.Volley, c.Allowed ? 1 : 0, Csv(c.Gate), F(c.Hull), F(c.Tti), F((frame-c.LastObservedFrame)/60.0), Csv(c.Mode)) + "\r\n");
            }

            public void Request(int frame, Gun g, ulong projectile, string result)
            {
                Append(Path.Combine(Folder, "target_requests.csv"), string.Join(",", F(frame / 60.0), g.EntityId, g.Part, projectile,
                    g.TrackId, result, g.Shots, "REQUEST_ONLY_UNCONFIRMED") + "\r\n");
            }

            // Authoritative final ledger: earlier event/resolve rows may be retracted.
            public void FinalLedger(List<Track> tracks, string goal, bool initialObserved)
            {
                var b = new StringBuilder("track,projectile_id,batch,initial_cohort,goal_ordinal,resolved,result,danger_entered,min_hull_m,reacquisitions\r\n");
                foreach (Track t in tracks.Where(t => t.CountedPhysical && t.VolleyIndex > 0).OrderBy(t => t.Id))
                    b.Append(string.Join(",", t.Id, t.ProjectileId, t.VolleyIndex, t.InitialCohort ? 1 : 0, t.GoalOrdinal, t.Resolved ? 1 : 0,
                        t.Resolved ? t.State : "UNRESOLVED", t.DangerEntered ? 1 : 0, F(t.MinHull), t.Reacquisitions)).AppendLine();
                WriteText(Path.Combine(Folder, "final_tracks.csv"), b.ToString());
                var batches = new StringBuilder("batch,seen,intercept_probable,danger,unknown,unresolved\r\n");
                foreach (var group in tracks.Where(t => t.CountedPhysical && t.VolleyIndex > 0).GroupBy(t => t.VolleyIndex).OrderBy(g => g.Key))
                    batches.Append(string.Join(",", group.Key, group.Count(), group.Count(t => t.Resolved && t.State == "INTERCEPT"),
                        group.Count(t => t.DangerEntered || t.State == "DANGER"), group.Count(t => t.Resolved && t.State == "UNKNOWN"), group.Count(t => !t.Resolved))).AppendLine();
                WriteText(Path.Combine(Folder, "final_batches.csv"), batches.ToString());
                WriteText(Path.Combine(Folder, "goal_160.txt"), goal + "\r\nInitialStableSnapshotObserved=" + initialObserved +
                    "\r\nInitialSnapshotExcluded=True\r\nSelection=FIRST_160_NEW_STABLE_IDS_AFTER_INITIAL_SNAPSHOT\r\nLaunchVolleysVerified=False\r\nConfirmedInterceptions=NOT_MEASURED\r\nActualShipDamage=NOT_MEASURED\r\nAuthoritativeResults=final_tracks.csv\r\nEarlierResolveRows=PROVISIONAL_MAY_BE_RETRACTED\r\n");
            }

            public void Unresolved(List<Track> tracks, int frame, string reason)
            {
                var b = new StringBuilder("track,projectile_id,volley,hull_m,danger_entered,observation_age_s,end_reason\r\n");
                foreach (var t in tracks.Where(t => !t.Resolved && t.CountedPhysical && t.VolleyIndex > 0))
                    b.Append(string.Join(",", t.Id, t.ProjectileId, t.VolleyIndex, F(t.Hull), t.DangerEntered ? 1 : 0, F((frame-t.LastFrame)/60.0), Csv(reason))).AppendLine();
                WriteText(Path.Combine(Folder, "unresolved.csv"), b.ToString());
                WriteText(Path.Combine(Folder, "session_end.txt"), "Reason=" + reason + "\r\nActualShipDamage=NOT_MEASURED\r\n");
            }

            public void SessionSummary(int expectedPerVolley, int completedVolleys, int seen, int resolved, int intercepted, int danger, int miss, int unk, int ammoStart, int ammoEnd, double peakHeat, long shotsStart, long shotsEnd, List<Gun> guns, int fallbackSeen, int fallbackFusedCount)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("ZEO PDC v0.3.10 LIVE OBSERVER // CONTINUOUS VOLLEY SESSION");
                    sb.AppendLine("UTC=" + DateTime.UtcNow.ToString("o"));
                    sb.AppendLine("EXPECTED_PER_VOLLEY=" + expectedPerVolley);
                    sb.AppendLine("VOLLEY_GROUPING=OBSERVATION_BATCHES_NOT_VERIFIED_LAUNCH_VOLLEYS");
                    sb.AppendLine("AUTHORITATIVE_TRACK_RESULTS=final_tracks.csv");
                    sb.AppendLine("COMPLETED_VOLLEYS=" + completedVolleys);
                    sb.AppendLine("TRACKING_MODE=STABLE_ID_AUTHORITATIVE_WHEN_AVAILABLE");
                    sb.AppendLine("TOTAL_PHYSICAL_SEEN=" + seen);
                    sb.AppendLine("TOTAL_PHYSICAL_RESOLVED=" + resolved);
                    sb.AppendLine("TOTAL_PHYSICAL_UNRESOLVED=" + Math.Max(0, seen - resolved));
                    sb.AppendLine("VOLLEY_SHOT_COUNTS=OVERLAPPING_BATTERY_WINDOWS_NOT_ADDITIVE");
                    sb.AppendLine("TARGET_ASSOCIATION=SCHEDULER_ONLY_UNCONFIRMED");
                    sb.AppendLine("ACTUAL_SHIP_DAMAGE=NOT_MEASURED");
                    sb.AppendLine("FALLBACK_TELEMETRY_SEEN=" + fallbackSeen);
                    sb.AppendLine("FALLBACK_FUSED_OR_SUPPRESSED=" + fallbackFusedCount);
                    sb.AppendLine("INTERCEPT_PROBABLE=" + intercepted);
                    sb.AppendLine("DANGER_ENTRIES_150M=" + danger);
                    sb.AppendLine("MISSED_OUTBOUND=" + miss);
                    sb.AppendLine("UNKNOWN=" + unk);
                    sb.AppendLine("AMMO_START=" + ammoStart);
                    sb.AppendLine("AMMO_END=" + ammoEnd);
                    sb.AppendLine("PLUGIN_MONITORED_PDC_SHOTS=" + Math.Max(0, shotsEnd - shotsStart));
                    sb.AppendLine("SESSION_PEAK_HEAT=" + peakHeat.ToString("0.0", CultureInfo.InvariantCulture) + "%");
                    WriteText(Path.Combine(Folder, "summary.txt"), sb.ToString());

                    var ps = new StringBuilder("entity,part,name,hp_pct,heat_pct,ammo,shots,functional,allowed,track,rof,range\r\n");
                    for (int i = 0; i < guns.Count; i++)
                    {
                        Gun g = guns[i];
                        ps.Append(g.EntityId).Append(',').Append(g.Part).Append(',').Append(Csv(g.Name)).Append(',')
                          .Append(F(g.Hp)).Append(',').Append(F(g.Heat)).Append(',').Append(g.Ammo).Append(',').Append(g.Shots).Append(',')
                          .Append(g.Functional ? 1 : 0).Append(',').Append(g.Allowed ? 1 : 0).Append(',').Append(g.TrackId).Append(',').Append(F(g.Rof)).Append(',').Append(F(g.DesiredRange)).AppendLine();
                    }
                    WriteText(Path.Combine(Folder, "pdc_stats.csv"), ps.ToString());
                }
                catch (Exception ex) { log("Session summary write failed: " + ex.Message); }
            }

            public void Cooldown(double seconds,double finalHeat)
            {
                try { Append(Path.Combine(Folder,"summary.txt"),"COOLDOWN_TO_COLD_SECONDS="+F(seconds)+"\r\nFINAL_COLD_HEAT="+F(finalHeat)+"%\r\n"); Event("COOLDOWN_COMPLETE","seconds="+F(seconds)+" heat="+F(finalHeat)); } catch { }
            }

            public void Prediction(int id, double range, double hull, double speed, double closing, double tti)
            {
                try { Append(predictionsPath, id+","+F(range)+","+F(hull)+","+F(speed)+","+F(closing)+","+(double.IsInfinity(tti)?"":F(tti))+"\r\n"); } catch { }
            }

            public void Summary(int expected, int intercepted, int hit, int miss, int unk, int ammoStart, int ammoEnd, double peakHeat, long shotsStart, long shotsEnd, List<Gun> guns, double rate)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("ZEO PDC v0.3.10 LIVE OBSERVER");
                    sb.AppendLine("UTC=" + DateTime.UtcNow.ToString("o"));
                    sb.AppendLine("EXPECTED=" + expected);
                    sb.AppendLine("INTERCEPT_PROBABLE=" + intercepted);
                    sb.AppendLine("HITS_150M=" + hit);
                    sb.AppendLine("MISSED_OUTBOUND=" + miss);
                    sb.AppendLine("UNKNOWN=" + unk);
                    sb.AppendLine("INTERCEPT_RATE=" + rate.ToString("0.00", CultureInfo.InvariantCulture) + "%");
                    sb.AppendLine("AMMO_START=" + ammoStart);
                    sb.AppendLine("AMMO_END=" + ammoEnd);
                    sb.AppendLine("AMMO_USED=" + Math.Max(0, ammoStart - ammoEnd));
                    sb.AppendLine("WC_PROJECTILES_FIRED=" + Math.Max(0, shotsEnd - shotsStart));
                    sb.AppendLine("HOTTEST_AT_COMPLETE=" + peakHeat.ToString("0.0", CultureInfo.InvariantCulture) + "%");
                    WriteText(Path.Combine(Folder, "summary.txt"), sb.ToString());

                    var ps = new StringBuilder("entity,part,name,hp_pct,heat_pct,ammo,shots,functional,allowed,track,rof\r\n");
                    for (int i = 0; i < guns.Count; i++)
                    {
                        Gun g = guns[i];
                        ps.Append(g.EntityId).Append(',').Append(g.Part).Append(',').Append(Csv(g.Name)).Append(',')
                          .Append(F(g.Hp)).Append(',').Append(F(g.Heat)).Append(',').Append(g.Ammo).Append(',').Append(g.Shots).Append(',')
                          .Append(g.Functional ? 1 : 0).Append(',').Append(g.Allowed ? 1 : 0).Append(',').Append(g.TrackId).Append(',').Append(F(g.Rof)).AppendLine();
                    }
                    WriteText(Path.Combine(Folder, "pdc_stats.csv"), ps.ToString());
                }
                catch (Exception ex) { log("Summary write failed: " + ex.Message); }
            }

            private static string F(double v) { return double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture); }
            private static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " "); }
            private static string Csv(string s) { return "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; }
        }
    }
}

