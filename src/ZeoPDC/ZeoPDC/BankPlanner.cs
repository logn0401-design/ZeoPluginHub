using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace ZeoPDC
{
    // Observation only. No game/API references or control writes. Bounds are
    // conservative construct boxes in the current ship frame; curved guidance,
    // future ship rotation, reload duration and LOS remain unverified.
    internal static class BankPlanner
    {
        internal sealed class Threat
        {
            public int Track;
            public ulong Id;
            public Vector3D Position, Velocity;
            public double Health = double.NaN, Age, Deadline = double.PositiveInfinity, CpaTime, CpaDistance;
            public string Side = "NONE", Path = "UNASSESSED";
        }
        internal sealed class Gun
        {
            public long Id;
            public int Part;
            public string Bank, Profile;
            public Vector3D Position, Velocity, Direction;
            public double Speed, ShotsPerSecond, HealthPerHit, SlewRadiansPerSecond, StartupSeconds, Range;
            public bool Usable;
        }
        internal sealed class Plan
        {
            public Threat Threat;
            public Gun Primary, Backup;
            public int Rank, MinimumHits, ScenarioShots;
            public Vector3D Intercept;
            public double Start, FirstHit, Finish, Slack = double.NaN;
            public string Status;
        }
        public const double Horizon = 8.0;
        // Sensitivity scenario, NOT a measured hit rate or calibrated kill probability.
        public const double AssumedHitProbability = 0.5;
        public static readonly double PerTargetConfidence = Math.Pow(0.99, 1.0 / 160.0);
        static readonly Dictionary<int, int> scenarioCache = new Dictionary<int, int>();
        public static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        public static bool Finite(Vector3D v) { return Finite(v.X) && Finite(v.Y) && Finite(v.Z); }

        public static string Side(Vector3D v)
        {
            double x = Math.Abs(v.X), y = Math.Abs(v.Y), z = Math.Abs(v.Z);
            if (x >= y && x >= z) return v.X >= 0 ? "RIGHT" : "LEFT";
            if (y >= z) return v.Y >= 0 ? "TOP" : "BOTTOM";
            return v.Z >= 0 ? "REAR" : "FRONT";
        }

        public static double Entry(Vector3D p, Vector3D v, BoundingBoxD box)
        {
            double enter = 0, exit = Horizon;
            double[] a = { p.X, p.Y, p.Z }, b = { v.X, v.Y, v.Z };
            double[] lo = { box.Min.X, box.Min.Y, box.Min.Z }, hi = { box.Max.X, box.Max.Y, box.Max.Z };
            if (!Finite(p) || !Finite(v)) return double.PositiveInfinity;
            for (int i = 0; i < 3; i++)
            {
                if (Math.Abs(b[i]) < 1e-9) { if (a[i] < lo[i] || a[i] > hi[i]) return double.PositiveInfinity; continue; }
                double t0 = (lo[i] - a[i]) / b[i], t1 = (hi[i] - a[i]) / b[i];
                if (t0 > t1) { double tmp = t0; t0 = t1; t1 = tmp; }
                enter = Math.Max(enter, t0); exit = Math.Min(exit, t1);
                if (enter > exit) return double.PositiveInfinity;
            }
            return enter;
        }

        public static void Predict(Threat t, IList<BoundingBoxD> boxes, Vector3D angular = default(Vector3D))
        {
            t.Deadline = double.PositiveInfinity; t.Side = "NONE";
            Vector3D velocity = t.Velocity - Vector3D.Cross(angular, t.Position);
            double vv = velocity.LengthSquared();
            t.CpaTime = vv < 1e-9 ? 0 : Math.Max(0, Math.Min(Horizon, -Vector3D.Dot(t.Position, velocity) / vv));
            t.CpaDistance = (t.Position + velocity * t.CpaTime).Length();
            if (t.Age > .2 || !Finite(t.Position) || !Finite(t.Velocity)) { t.Path = "STALE_OR_INVALID"; return; }
            if (boxes.Count == 0) { t.Path = "NO_HULL"; return; }
            foreach (var box in boxes)
            {
                double entry = Entry(t.Position, velocity, box);
                if (entry < t.Deadline) { t.Deadline = entry; t.Side = Side(t.Position + velocity * entry - box.Center); }
            }
            t.Path = Finite(t.Deadline) ? "PREDICTED_ENVELOPE_ENTRY" :
                Vector3D.Dot(t.Position, t.Velocity) > 0 ? "OUTWARD_NO_LINEAR_ENTRY" : "NO_LINEAR_ENTRY";
        }

        // Projectile inherits muzzle velocity in this model. It is a recorded
        // assumption until the live weapon implementation/profile is calibrated.
        public static bool Intercept(Vector3D r, Vector3D relativeVelocity, double speed, out double time)
        {
            time = double.PositiveInfinity;
            if (!Finite(r) || !Finite(relativeVelocity) || !Finite(speed) || speed <= 0) return false;
            double c = r.LengthSquared();
            if (c < 1e-12) { time = 0; return true; }
            double a = relativeVelocity.LengthSquared() - speed * speed, b = 2 * Vector3D.Dot(r, relativeVelocity);
            if (Math.Abs(a) < 1e-8)
            {
                if (Math.Abs(b) > 1e-9 && -c / b >= 0) time = -c / b;
            }
            else
            {
                double d = b * b - 4 * a * c;
                if (d >= 0)
                {
                    double q = -.5 * (b + (b >= 0 ? Math.Sqrt(d) : -Math.Sqrt(d)));
                    double t0 = q / a, t1 = Math.Abs(q) > 1e-12 ? c / q : double.PositiveInfinity;
                    if (t0 >= 0) time = t0;
                    if (t1 >= 0) time = Math.Min(time, t1);
                }
            }
            return Finite(time) && time <= Horizon;
        }

        public static int RequiredShots(int hits, double p, double confidence)
        {
            if (hits < 1 || hits > 128 || p <= 0 || p > 1 || confidence <= 0 || confidence >= 1) return 0;
            var below = new double[hits]; below[0] = 1;
            for (int n = 1; n <= 1024; n++)
            {
                for (int k = Math.Min(n, hits - 1); k >= 0; k--)
                    below[k] = below[k] * (1 - p) + (k > 0 ? below[k - 1] * p : 0);
                if (below.Sum() <= 1 - confidence) return n;
            }
            return 0;
        }

        static Plan Candidate(Threat t, Gun g, double available, int rank)
        {
            if (!g.Usable || !Finite(g.Position) || !Finite(g.Velocity) || !Finite(g.Direction) || g.Direction.LengthSquared() < .5 ||
                !Finite(g.Speed) || !Finite(g.ShotsPerSecond) || !Finite(g.HealthPerHit) || !Finite(g.SlewRadiansPerSecond) || !Finite(g.StartupSeconds) ||
                g.Speed <= 0 || g.ShotsPerSecond <= 0 || g.HealthPerHit <= 0 || g.SlewRadiansPerSecond <= 0 || t.Age > .2) return null;
            Vector3D r = t.Position - g.Position, rv = t.Velocity - g.Velocity;
            double flight;
            if (!Intercept(r + rv * available, rv, g.Speed, out flight)) return null;
            Vector3D aim = r + rv * (available + flight);
            double angle = aim.LengthSquared() < 1e-9 ? 0 : Math.Acos(Math.Max(-1, Math.Min(1, Vector3D.Dot(Vector3D.Normalize(aim), g.Direction))));
            double start = available + g.StartupSeconds + angle / g.SlewRadiansPerSecond;
            if (!Intercept(r + rv * start, rv, g.Speed, out flight) || flight * g.Speed > g.Range || start + flight > Horizon) return null;
            int hits = Finite(t.Health) && t.Health > 0 && t.Health / g.HealthPerHit <= 128 ? (int)Math.Ceiling(t.Health / g.HealthPerHit) : 0;
            int rounds = 0;
            if (hits > 0 && !scenarioCache.TryGetValue(hits, out rounds))
                scenarioCache[hits] = rounds = RequiredShots(hits, AssumedHitProbability, PerTargetConfidence);
            double burst = rounds > 0 ? (rounds - 1) / g.ShotsPerSecond : 0;
            // Flight of the final round is recalculated at its launch time.
            double finalFlight;
            if (!Intercept(r + rv * (start + burst), rv, g.Speed, out finalFlight) || finalFlight * g.Speed > g.Range) return null;
            double finish = start + burst + finalFlight;
            return new Plan { Threat = t, Primary = g, Rank = rank, Start = start, FirstHit = start + flight,
                Finish = finish, Slack = t.Deadline - finish, MinimumHits = hits, ScenarioShots = rounds,
                Intercept = t.Position + t.Velocity * (start + flight),
                Status = rounds == 0 ? "HEALTH_BUDGET_UNKNOWN" : "SCENARIO_ONLY_ARCS_LOS_RELOAD_UNCHECKED" };
        }

        public static List<Plan> Build(IList<Threat> threats, IList<Gun> guns, IList<BoundingBoxD> boxes, Vector3D angular = default(Vector3D))
        {
            foreach (var t in threats) Predict(t, boxes, angular);
            var candidates = threats.Select(t => new { Threat = t, Best = guns.Select(g => Candidate(t, g, 0, 1)).Where(x => x != null).OrderBy(x => x.Finish).FirstOrDefault() }).ToList();
            var available = new Dictionary<Gun, double>(); var ranks = new Dictionary<Gun, int>();
            var result = new List<Plan>();
            // Deadline slack precedes proximity. Outward non-intersecting tracks
            // stay tracked, behind incoming threats; a returning track is reprioritized.
            foreach (var item in candidates.OrderBy(x => x.Threat.Path == "OUTWARD_NO_LINEAR_ENTRY" ? 2 : Finite(x.Threat.Deadline) ? 0 : 1)
                .ThenBy(x => x.Best == null ? x.Threat.Deadline : x.Threat.Deadline - x.Best.Finish).ThenBy(x => x.Threat.Id))
            {
                var t = item.Threat;
                var options = t.Path == "STALE_OR_INVALID" || t.Path == "NO_HULL" ? new List<Plan>() : guns.Select(g => Candidate(t, g,
                    available.ContainsKey(g) ? available[g] : 0, ranks.ContainsKey(g) ? ranks[g] + 1 : 1)).Where(x => x != null).OrderBy(x => x.Finish).ThenBy(x => x.Primary.Id).ThenBy(x => x.Primary.Part).ToList();
                if (options.Count == 0) { result.Add(new Plan { Threat = t, Status = "NO_FEASIBLE_ESTIMATE" }); continue; }
                var best = options[0];
                best.Backup = options.Skip(1).Select(x => x.Primary).FirstOrDefault();
                // A backup is an unreserved candidate, not an additional firing order.
                available[best.Primary] = best.Finish; ranks[best.Primary] = best.Rank;
                result.Add(best);
            }
            return result;
        }
    }
}
