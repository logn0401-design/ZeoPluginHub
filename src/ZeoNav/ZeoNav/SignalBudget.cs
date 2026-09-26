using System;
using System.Collections.Generic;

namespace ZeoNav
{
    // Pure arithmetic shared by the live controller and the isolated flight tests.
    // Spectrum range is proportional to sqrt(emission), so budgets are in km squared.
    internal sealed class SignalBudget
    {
        internal sealed class Bucket
        {
            public bool Directional;
            public readonly double[] Cost = new double[6];
            public readonly double[] Reserve = new double[6];
        }

        public readonly Dictionary<string, Bucket> Buckets = new Dictionary<string, Bucket>();
        public double SphericalBaseSquared, DirectionalBaseSquared;
        public double TargetKm;
        public double FeedbackScale = 1;
        public bool Ready;
        public const double RangeMargin = .97;

        public static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        public static double RangeKm(double strength, double threshold)
        {
            if (!Finite(strength) || strength < 0 || !Finite(threshold) || threshold <= 0) return double.NaN;
            // Match Spectrum.Detector.GetDetectionRange's float result, before HUD rounding.
            return (float)Math.Sqrt(strength / threshold / (4 * Math.PI)) / 1000.0;
        }

        public void Add(string key, bool directional, int axis, double kmSquared, double reserve = 0)
        {
            if (!Finite(kmSquared) || kmSquared < 0) throw new ArgumentException("Invalid emission coefficient");
            Bucket b;
            if (!Buckets.TryGetValue(key, out b)) Buckets[key] = b = new Bucket { Directional = directional };
            b.Cost[axis] += kmSquared;
            b.Reserve[axis] += reserve;
        }

        public double Limit(int axis, double requested, double[] otherCommands)
        {
            if (!Ready || !Finite(TargetKm) || TargetKm <= 0 || !Finite(requested)) return 0;
            double ceiling = TargetKm * RangeMargin;
            ceiling *= ceiling;
            if (Math.Max(SphericalBaseSquared, DirectionalBaseSquared) >= ceiling) return 0;
            double allowed = Math.Max(0, Math.Min(1, requested));
            foreach (Bucket b in Buckets.Values)
            {
                double remaining = ceiling - (b.Directional ? DirectionalBaseSquared : SphericalBaseSquared);
                for (int i = 0; i < 6; i++)
                    if (i != axis) remaining -= (b.Cost[i] * otherCommands[i] + (otherCommands[i] > 0 ? b.Reserve[i] : 0)) * FeedbackScale;
                if (requested > 0) remaining -= b.Reserve[axis] * FeedbackScale;
                double cost = b.Cost[axis] * FeedbackScale;
                if (cost > 0) allowed = Math.Min(allowed, Math.Max(0, remaining / cost));
                else if (remaining < 0) return 0;
            }
            return allowed;
        }

        public double PredictedSquared(double[] commands)
        {
            double peak = Math.Max(SphericalBaseSquared, DirectionalBaseSquared);
            foreach (Bucket b in Buckets.Values)
            {
                double value = b.Directional ? DirectionalBaseSquared : SphericalBaseSquared;
                for (int i = 0; i < 6; i++) value += (b.Cost[i] * commands[i] + (commands[i] > 0 ? b.Reserve[i] : 0)) * FeedbackScale;
                peak = Math.Max(peak, value);
            }
            return peak;
        }
    }

    internal sealed class OwnSignalTracker
    {
        public long GridId { get; private set; }
        public int Generation { get; private set; }
        public int FreshCount { get; private set; }
        public double ReceivedAt { get; private set; }
        public double SphericalStrong, SphericalWeak, DirectionalStrong, DirectionalWeak;
        public double Maximum { get { return Math.Max(Math.Max(SphericalStrong, SphericalWeak), Math.Max(DirectionalStrong, DirectionalWeak)); } }
        public bool Fresh(double now) { return GridId != 0 && FreshCount >= 3 && now >= ReceivedAt && now - ReceivedAt <= 3; }
        public void Reset(long grid)
        {
            GridId = grid; FreshCount = 0; ReceivedAt = double.NegativeInfinity;
            SphericalStrong = SphericalWeak = DirectionalStrong = DirectionalWeak = 0;
        }
        public bool Accept(long grid, double sphere, double directional, double weak, double strong, double now)
        {
            if (grid == 0 || grid != GridId) return false;
            if (!SignalBudget.Finite(sphere) || sphere < 0 || !SignalBudget.Finite(directional) || directional < 0 ||
                !SignalBudget.Finite(weak) || weak <= 0 || !SignalBudget.Finite(strong) || strong < weak || !SignalBudget.Finite(now))
            { Reset(grid); return false; }
            if (now - ReceivedAt < .25) return false; // Ignore duplicate/batched arrivals during quarantine.
            if (now - ReceivedAt > 3) FreshCount = 0;
            SphericalWeak = SignalBudget.RangeKm(sphere, weak);
            SphericalStrong = SignalBudget.RangeKm(sphere, strong);
            DirectionalWeak = SignalBudget.RangeKm(directional, weak);
            DirectionalStrong = SignalBudget.RangeKm(directional, strong);
            ReceivedAt = now; FreshCount++; Generation++;
            return true;
        }
    }
}

