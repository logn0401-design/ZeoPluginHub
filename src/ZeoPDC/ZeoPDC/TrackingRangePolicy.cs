using System;

namespace ZeoPDC
{
    internal static class TrackingRangePolicy
    {
        // GetMaxWeaponRange reports effective tracking range, including Set.Range.
        // Send the bounded requested range; WeaponCore enforces ammo/hardware limits.
        // A lower observed range must never lower a subsequent promotion request.
        public static float Request(double range)
        {
            if (double.IsNaN(range) || double.IsInfinity(range)) return 500f;
            return (float)Math.Max(100, Math.Min(6000, range));
        }

        public static bool TryObserved(double effectiveRange, out double observed)
        {
            observed = 0;
            if (double.IsNaN(effectiveRange) || double.IsInfinity(effectiveRange) || effectiveRange <= 0) return false;
            observed = effectiveRange;
            return true;
        }
    }
}
