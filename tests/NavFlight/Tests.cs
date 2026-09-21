using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using ZeoNav;

internal static partial class Tests
{
    private static string gameBin;
    private static int passed, failed;
    public static int Main(string[] args)
    {
        gameBin = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            string p = Path.Combine(gameBin, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(p) ? Assembly.LoadFrom(p) : null;
        };
        return Run();
    }
    private static void Check(string name, bool ok)
    {
        if (ok) passed++; else failed++;
        Console.WriteLine((ok ? "PASS" : "FAIL") + " | " + name);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run()
    {
        CatalogTests();
        AttitudeTests();
        ServerTests();
        double weak = .1f, strong = .4f;
        double emission = 4 * Math.PI * weak * 125000 * 125000;
        Check("Spectrum float-metre conversion returns 125 km", Math.Abs(SignalBudget.RangeKm(emission, weak) - 125) < 1e-5);
        Check("Strong range is half the weak range", Math.Abs(SignalBudget.RangeKm(emission, strong) - 62.5) < 1e-5);
        Check("Invalid telemetry is rejected", double.IsNaN(SignalBudget.RangeKm(double.NaN, weak)) && double.IsNaN(SignalBudget.RangeKm(-1, weak)));
        var feed = new OwnSignalTracker(); feed.Reset(42);
        feed.Accept(42, emission, emission * 4, weak, strong, 1);
        Check("Single packet cannot release thrust", !feed.Fresh(1));
        int gen = feed.Generation;
        feed.Accept(42, emission, emission * 4, weak, strong, 1);
        Check("Duplicate/batched arrival cannot advance quarantine", feed.Generation == gen);
        feed.Accept(42, emission, emission * 4, weak, strong, 2);
        feed.Accept(42, emission, emission * 4, weak, strong, 3);
        Check("Three fresh packets establish own grid and farthest of four", feed.Fresh(3) && Math.Abs(feed.Maximum - 250) < 1e-5);
        gen = feed.Generation;
        for (int i = 0; i < 10000; i++) { var value = feed.Maximum; feed.Fresh(3.1); }
        Check("Repeated polling cannot manufacture fresh generations", feed.Generation == gen);
        Check("Telemetry expires after three seconds", !feed.Fresh(6.01));
        feed.Accept(42, emission, emission, weak, strong, 7);
        Check("A long gap requires quarantine again", !feed.Fresh(7));
        feed.Reset(99);
        Check("Old controlled-grid packet cannot update new ship", !feed.Accept(42, emission, emission, weak, strong, 8) && !feed.Fresh(8));
        feed.Accept(99, double.NaN, emission, weak, strong, 9);
        Check("Non-finite packet invalidates the own feed", !feed.Fresh(9));

        var b = new SignalBudget { Ready = true, TargetKm = 100, SphericalBaseSquared = 100, DirectionalBaseSquared = 400 };
        b.Add("drive", true, 0, 40000);
        double low = b.Limit(0, 1, new double[6]);
        b.TargetKm = 150; double medium = b.Limit(0, 1, new double[6]);
        b.TargetKm = 300; double full = b.Limit(0, 1, new double[6]);
        Check("Higher MAX SIG raises available thrust beyond 20 percent", medium > .2 && medium > low);
        Check("Full thrust is available when the signature budget allows", full == 1);
        b.TargetKm = 100;
        Check("Lowering the ceiling immediately reduces the command", b.Limit(0, 1, new double[6]) == low);
        b.Add("drive", true, 2, 20000);
        var commands = new double[6]; commands[2] = .2; commands[0] = b.Limit(0, 1, commands);
        Check("Side and forward thrust share one emission budget", b.PredictedSquared(commands) <= Math.Pow(100 * .97, 2) + 1e-8 && commands[0] < low);
        b.Ready = false;
        Check("Missing/stale feedback permits zero output", b.Limit(0, 1, commands) == 0);
        b.Ready = true; b.TargetKm = 10;
        Check("Idle emissions over the ceiling prevent a departure burn", b.Limit(0, 1, new double[6]) == 0);
        Check("Non-finite requested throttle cannot reach actuators", b.Limit(0, double.NaN, new double[6]) == 0);

        var hauler = new SignalBudget { Ready = true, TargetKm = 490, SphericalBaseSquared = 10000, DirectionalBaseSquared = 40000 };
        hauler.Add("hauler", true, 0, 450000);
        double oldCeilingThrust = hauler.Limit(0, 1, new double[6]);
        hauler.TargetKm = 750;
        double haulerThrust = hauler.Limit(0, 1, new double[6]);
        Check("750 km permits more hauler thrust than the old 490 km ceiling", haulerThrust > oldCeilingThrust && haulerThrust == 1);
        var haulerOutput = new double[6]; haulerOutput[0] = haulerThrust;
        Check("Hauler output retains signature headroom at 750 km", hauler.PredictedSquared(haulerOutput) <= Math.Pow(750 * .97, 2));
        var targetSig = typeof(NavController).GetMethod("TargetSigKm", BindingFlags.NonPublic | BindingFlags.Static);
        Check("Flight controller receives the selected 750 km ceiling", (double)targetSig.Invoke(null, new object[] { new NavConfig { MaxDriveSigKm = 750 } }) == 750);
        Check("Flight controller caps out-of-range values at 750 km", (double)targetSig.Invoke(null, new object[] { new NavConfig { MaxDriveSigKm = 900 } }) == 750);

        var random = new Random(1919); bool property = true;
        for (int trial = 0; trial < 5000; trial++)
        {
            var budget = new SignalBudget { Ready = true, TargetKm = 5 + random.NextDouble() * 745,
                SphericalBaseSquared = random.NextDouble() * 16, DirectionalBaseSquared = random.NextDouble() * 16 };
            for (int bucket = 0; bucket < 4; bucket++) for (int axis = 0; axis < 6; axis++)
                budget.Add(bucket.ToString(), bucket % 2 == 0, axis, random.NextDouble() * 100000, random.NextDouble() * 2);
            var output = new double[6];
            for (int axis = 0; axis < 6; axis++) output[axis] = budget.Limit(axis, random.NextDouble(), output);
            if (budget.PredictedSquared(output) > Math.Pow(budget.TargetKm * .97, 2) + 1e-6) property = false;
        }
        Check("5000 mixed-axis budgets remain within modeled MAX SIG", property);

        // Exercise the actual reflection binding against the installed mod's precise
        // public/private shape, using a fixture (no game or live mod instance).
        var adapter = new SpectrumAdapter(Console.WriteLine);
        var backend = new Spectrum.ApiBackend();
        var initialize = typeof(SpectrumAdapter).GetMethod("InitializeApi", BindingFlags.NonPublic | BindingFlags.Instance);
        var endpoints = new Dictionary<string, Delegate> { { "GetClientDetections", new Func<byte[]>(backend.GetClientDetections) } };
        initialize.Invoke(adapter, new object[] { endpoints });
        Check("Exact self-packet bridge binds without contact polling", adapter.Ready && Spectrum.SelfEmissionPacket.Subscribers == 1 && backend.Calls == 0);
        initialize.Invoke(adapter, new object[] { endpoints });
        Check("Repeated handshake does not duplicate subscriptions", Spectrum.SelfEmissionPacket.Subscribers == 1);
        initialize.Invoke(adapter, new object[] { "init" });
        Check("Init self-echo does not disturb binding", adapter.Ready);
        adapter.Dispose();
        Check("Dispose removes mod event subscription and cached identity", !adapter.Ready && Spectrum.SelfEmissionPacket.Subscribers == 0 && adapter.EmitterId == 0);

        var nav = new NavController(() => null, () => new NavConfig(), () => null, Console.WriteLine);
        string capSource;
        Check("A speed cap is not raised to match an overspeed ship", SpeedCapResolver.Resolve(null, 100, 200, out capSource) == 100 && capSource == "OVERRIDE");
        var eta = typeof(NavController).GetMethod("CalculateEta", BindingFlags.NonPublic | BindingFlags.Instance);
        var total = typeof(NavController).GetField("eta", BindingFlags.NonPublic | BindingFlags.Instance);
        eta.Invoke(nav, new object[] { 1000000d, 0d, 1000d, 2d, 2d, 20d });
        double slow = (double)total.GetValue(nav);
        eta.Invoke(nav, new object[] { 1000000d, 0d, 1000d, 8d, 8d, 20d });
        double fast = (double)total.GetValue(nav);
        Check("Actual ETA model improves with more allowed acceleration", fast < slow && fast > 0);
        eta.Invoke(nav, new object[] { 1000d, 0d, 1000d, 8d, 8d, 20d });
        Check("Short routes use finite triangular accelerate/flip/brake ETA", (double)total.GetValue(nav) > 0 && (double)total.GetValue(nav) < 200);
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed. Isolated logic/schema tests; not an in-game flight test.");
        return failed == 0 ? 0 : 1;
    }
}

// Exact-schema fixture, deliberately separate from the actual installed mod.
namespace Spectrum
{
    public struct SelfEmissionSummary { public double SphericalStrength, DirectionalStrength; }
    public sealed class Session { public long ClientSelfEmissionGridId; }
    public sealed class ApiBackend
    {
        private readonly Session _session = new Session();
        public int Calls;
        public byte[] GetClientDetections() { Calls++; return BitConverter.GetBytes(_session.ClientSelfEmissionGridId); }
    }
    public sealed class SelfEmissionPacket
    {
        public SelfEmissionSummary Emission;
        public static event Action<SelfEmissionPacket> OnReceive;
        public static int Subscribers { get { return OnReceive == null ? 0 : OnReceive.GetInvocationList().Length; } }
    }
    public static class Utils { public const float BaseThreshold = .1f, StrongThreshold = .4f; }
    public static class Config
    {
        public static readonly Dictionary<string, List<object>> BlockEmitters = new Dictionary<string, List<object>>();
        public static double GridPowerDrawEmissionScalar = 1000000;
    }
}
