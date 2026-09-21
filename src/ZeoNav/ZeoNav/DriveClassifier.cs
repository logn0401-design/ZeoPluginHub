using System;

namespace ZeoNav
{
    internal static class DriveClassifier
    {
        // Definition family matching covers sizes, factions, industrial/civilian
        // variants and damaged variants. Pilot-assigned block names are not identity.
        public static bool IsMain(string definition, string displayName, double ratedThrust)
        {
            string id = (definition ?? "").ToLowerInvariant();
            string display = (displayName ?? "").ToLowerInvariant();
            int slash = id.LastIndexOf('/');
            string subtype = slash >= 0 ? id.Substring(slash + 1) : id;
            if (subtype.StartsWith("sdx_drive", StringComparison.Ordinal)) return true;
            if (id.Contains("epstein") || display.Contains("epstein")) return true;
            if (id.Contains("rcs") || display.Contains("rcs") ||
                id.Contains("reaction control") || display.Contains("reaction control")) return false;
            // Other modded main thrusters remain eligible without a named family.
            return SignalBudget.Finite(ratedThrust) && ratedThrust >= 1000000;
        }
    }
}
