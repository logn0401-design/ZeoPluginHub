using Sandbox.ModAPI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace ZeoNav
{
    internal sealed class SpectrumAdapter
    {
        private const long Channel = 400790;
        private readonly Action<string> log;
        private readonly object sync = new object();
        private readonly OwnSignalTracker tracker = new OwnSignalTracker();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private Func<byte[]> api;
        private object session;
        private object gameSession;
        private FieldInfo selfGrid, packetEmission, spherical, directional;
        private EventInfo packetEvent;
        private Delegate packetHandler;
        private IDictionary emitters;
        private double weakThreshold, strongThreshold, powerScalar;
        private bool registered;
        private int lastRequestFrame = -100000;
        private int lastRegisterFrame = -100000;
        private long controlledGrid;
        private bool wasReady;
        private string status = "WAIT SPECTRUM SELF FEED";

        public bool Ready { get { return session != null && packetHandler != null; } }
        public bool HasSelfSample { get { lock (sync) return tracker.Fresh(clock.Elapsed.TotalSeconds); } }
        public bool DriveKmReady { get { return Ready && HasSelfSample; } }
        public int SampleGeneration { get { lock (sync) return tracker.Generation; } }
        public long EmitterId { get { lock (sync) return tracker.GridId; } }
        public int LastSampleFrame { get; private set; }
        public int SelfSampleAgeFrames { get { lock (sync) return tracker.FreshCount == 0 ? int.MaxValue : (int)Math.Min(int.MaxValue, Math.Max(0, clock.Elapsed.TotalSeconds - tracker.ReceivedAt) * 60); } }
        public double DriveKm { get { lock (sync) return tracker.Maximum; } }
        public double Drive { get { return 0; } } // Old raw contact tag intentionally unused.
        public double Strength { get { return 0; } }
        public double SphericalWeakKm { get { lock (sync) return tracker.SphericalWeak; } }
        public double SphericalStrongKm { get { lock (sync) return tracker.SphericalStrong; } }
        public double DirectionalWeakKm { get { lock (sync) return tracker.DirectionalWeak; } }
        public double DirectionalStrongKm { get { lock (sync) return tracker.DirectionalStrong; } }
        public string SelfMatch { get { return DriveKmReady ? "CONTROLLED GRID / SELF SUMMARY" : status; } }
        public string SelfDetail { get { return status; } }
        public string DriveKmSource { get { return "Spectrum.SelfEmissionPacket / MAX FOUR RANGES"; } }
        public string DriveKmStatus { get { return status; } }
        public SpectrumAdapter(Action<string> logger) { log = logger; }
        public void Init() { TryRegister(0); }

        private void TryRegister(int frame)
        {
            if (registered || frame - lastRegisterFrame < 60) return;
            lastRegisterFrame = frame;
            try
            {
                if (MyAPIGateway.Utilities == null) return;
                MyAPIGateway.Utilities.RegisterMessageHandler(Channel, InitializeApi);
                registered = true;
                log("Spectrum API client registered on channel " + Channel);
                RequestApi(frame);
            }
            catch (Exception ex) { log("Spectrum registration deferred: " + ex.Message); }
        }
        private void RequestApi(int frame)
        {
            lastRequestFrame = frame;
            try { MyAPIGateway.Utilities.SendModMessage(Channel, "init"); } catch { }
        }
        private void InitializeApi(object payload)
        {
            if (payload is string) return;
            Delegate endpoint = null;
            var ro = payload as IReadOnlyDictionary<string, Delegate>;
            var rw = payload as IDictionary<string, Delegate>;
            if (ro != null) ro.TryGetValue("GetClientDetections", out endpoint);
            else if (rw != null) rw.TryGetValue("GetClientDetections", out endpoint);
            if (endpoint == null || !(endpoint is Func<byte[]>)) return;
            if (api != null && api.Equals(endpoint) && Ready) return;
            Unbind();
            try
            {
                // Exact, version-checked bridge to the installed mod; no heap/HUD scanning
                // and no guesses about numeric units. Ordinary SelfOwned contacts are NOT own-ship telemetry.
                Type backend = endpoint.Target == null ? null : endpoint.Target.GetType();
                if (backend == null || backend.FullName != "Spectrum.ApiBackend") throw new InvalidOperationException("Unsupported Spectrum API target");
                Assembly asm = backend.Assembly;
                session = backend.GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(endpoint.Target);
                if (session == null || session.GetType().FullName != "Spectrum.Session") throw new InvalidOperationException("Unsupported Spectrum session");
                selfGrid = session.GetType().GetField("ClientSelfEmissionGridId");
                if (selfGrid == null || selfGrid.FieldType != typeof(long)) throw new InvalidOperationException("Self grid schema changed");
                Type packet = asm.GetType("Spectrum.SelfEmissionPacket", true);
                packetEmission = packet.GetField("Emission");
                spherical = packetEmission.FieldType.GetField("SphericalStrength");
                directional = packetEmission.FieldType.GetField("DirectionalStrength");
                if (spherical.FieldType != typeof(double) || directional.FieldType != typeof(double)) throw new InvalidOperationException("Self emission schema changed");
                Type utils = asm.GetType("Spectrum.Utils", true);
                weakThreshold = Convert.ToDouble(utils.GetField("BaseThreshold").GetValue(null));
                strongThreshold = Convert.ToDouble(utils.GetField("StrongThreshold").GetValue(null));
                if (weakThreshold <= 0 || strongThreshold < weakThreshold || !SignalBudget.Finite(strongThreshold)) throw new InvalidOperationException("Invalid thresholds");
                Type config = asm.GetType("Spectrum.Config", true);
                emitters = config.GetField("BlockEmitters").GetValue(null) as IDictionary;
                powerScalar = Convert.ToDouble(config.GetField("GridPowerDrawEmissionScalar").GetValue(null));
                if (emitters == null || !SignalBudget.Finite(powerScalar) || powerScalar < 0) throw new InvalidOperationException("Emission config unavailable");
                packetEvent = packet.GetEvent("OnReceive", BindingFlags.Public | BindingFlags.Static);
                packetHandler = Delegate.CreateDelegate(packetEvent.EventHandlerType, this,
                    GetType().GetMethod("OnSelfPacket", BindingFlags.NonPublic | BindingFlags.Instance));
                packetEvent.AddEventHandler(null, packetHandler);
                api = (Func<byte[]>)endpoint;
                status = "WAIT FRESH OWN SUMMARY";
                log("Spectrum API READY: GetClientDetections / exact self-summary bridge bound / " + asm.FullName);
            }
            catch (Exception ex)
            {
                Unbind(); status = "UNSUPPORTED SPECTRUM SELF FEED";
                log("SIG KM SOURCE REJECTED // " + ex.GetType().Name + " " + ex.Message);
            }
        }
        private void OnSelfPacket(object packet)
        {
            try
            {
                // Packets have no grid ID. Require the current cockpit to match our
                // tracked grid, and quarantine three packets after every control change.
                var entity = MyAPIGateway.Session?.Player?.Controller?.ControlledEntity?.Entity as IMyShipController;
                long grid = entity == null || entity.CubeGrid == null ? 0 : entity.CubeGrid.EntityId;
                object value = packetEmission.GetValue(packet);
                lock (sync)
                {
                    if (grid != controlledGrid) { tracker.Reset(grid); controlledGrid = grid; return; }
                    tracker.Accept(grid, (double)spherical.GetValue(value), (double)directional.GetValue(value),
                        weakThreshold, strongThreshold, clock.Elapsed.TotalSeconds);
                }
            }
            catch { lock (sync) tracker.Reset(controlledGrid); }
        }
        public void Update(int frame, ShipContext ship)
        {
            object current = MyAPIGateway.Session;
            if (gameSession != null && !ReferenceEquals(current, gameSession)) ResetSession();
            gameSession = current;
            long grid = ship == null || ship.Grid == null ? 0 : ship.Grid.EntityId;
            lock (sync)
            {
                if (grid != controlledGrid) { controlledGrid = grid; tracker.Reset(grid); wasReady = false; }
            }
            TryRegister(frame);
            if (!Ready && registered && frame - lastRequestFrame >= 300) RequestApi(frame);
            if (Ready)
            {
                try
                {
                    if ((long)selfGrid.GetValue(session) != grid) { lock (sync) tracker.Reset(grid); }
                }
                catch { Unbind(); }
            }
            bool fresh = DriveKmReady;
            status = fresh ? "OWN SIG READY" : Ready ? "WAIT FRESH OWN SUMMARY (3 PACKETS)" : "WAIT SPECTRUM SELF FEED";
            if (fresh && !wasReady) log("SIG KM SOURCE READY // controlled=" + grid + " // maxFour=" + DriveKm.ToString("0.000") + " KM");
            if (!fresh && wasReady) log("SIG KM SOURCE WAIT // own feed stale or control changed");
            wasReady = fresh;
            if (fresh) LastSampleFrame = frame - SelfSampleAgeFrames;
        }
        public void ResetSession()
        {
            Unbind();
            try { if (registered && MyAPIGateway.Utilities != null) MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, InitializeApi); } catch { }
            registered = false; gameSession = null; lastRegisterFrame = lastRequestFrame = -100000;
        }
        private void Unbind()
        {
            try { if (packetEvent != null && packetHandler != null) packetEvent.RemoveEventHandler(null, packetHandler); } catch { }
            packetEvent = null; packetHandler = null; session = null; api = null; emitters = null;
            lock (sync) { controlledGrid = 0; tracker.Reset(0); }
            wasReady = false;
        }
        public void Dispose() { ResetSession(); }

        public SignalBudget BuildBudget(ShipContext ship)
        {
            if (!Ready || ship == null || emitters == null) throw new InvalidOperationException("Spectrum emission definitions unavailable");
            var result = new SignalBudget();
            double divisor = weakThreshold * 4 * Math.PI * 1000000;
            foreach (var pair in ship.ThrusterBanks)
            {
                foreach (IMyThrust t in pair.Value)
                {
                    if (t == null || t.Closed || !t.IsWorking) continue;
                    // HUD self summary only covers the cockpit grid. Include subgrid costs
                    // conservatively too; never substitute another owned ship's reading.
                    string grid = t.CubeGrid.EntityId.ToString();
                    var sources = emitters[t.BlockDefinition.ToString()] as IEnumerable;
                    if (sources != null) foreach (object source in sources)
                    {
                        Type st = source.GetType();
                        if (st.GetField("Trigger").GetValue(source).ToString() != "ScaleWithThrust") continue;
                        double maximum = Convert.ToDouble(st.GetField("MaxStrength").GetValue(source));
                        double minimum = Convert.ToDouble(st.GetField("MinStrength").GetValue(source));
                        bool dir = (bool)st.GetField("Directional").GetValue(source);
                        double gain = dir ? Convert.ToDouble(st.GetField("Gain").GetValue(source)) : 1;
                        if (!SignalBudget.Finite(maximum) || !SignalBudget.Finite(minimum) || minimum < 0 || maximum < minimum || !SignalBudget.Finite(gain) || gain < 0)
                            throw new InvalidOperationException("Unsupported thruster emission definition");
                        string band = st.GetField("Band").GetValue(source).ToString();
                        string[] bands = band == "All" ? new[] { "Radio", "Optical", "HighEnergy" } : new[] { band };
                        foreach (string b in bands)
                        {
                            string key = grid + "/" + (dir ? t.Orientation.Forward.ToString() : "sphere") + "/" + b;
                            // For mixed directional gains, sum(deltaStrength * gain^2)
                            // bounds the increase in Spectrum's squared weighted-mean gain.
                            // Reserve the source minimum and aggregation cutoff as well.
                            // This covers sources that were below Spectrum's cutoff at idle.
                            result.Add(key, dir, (int)pair.Key, (maximum - minimum) * gain * gain / divisor,
                                (minimum + 100000) * gain * gain / divisor);
                        }
                    }
                    var definition = Sandbox.Definitions.MyDefinitionManager.Static.GetCubeBlockDefinition(t.BlockDefinition) as Sandbox.Definitions.MyThrustDefinition;
                    if (definition == null) throw new InvalidOperationException("Thruster power definition unavailable");
                    // Treat full rated consumption as electric draw even for fuel thrusters:
                    // conservative until live feedback proves the total emission behavior.
                    result.Add(grid + "/sphere/Radio", false, (int)pair.Key, definition.MaxPowerConsumption * powerScalar / divisor);
                }
            }
            return result;
        }
    }
}

