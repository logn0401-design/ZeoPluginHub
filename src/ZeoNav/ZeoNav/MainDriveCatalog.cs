using System;
using System.Collections.Generic;

namespace ZeoNav
{
    internal static class MainDriveCatalog
    {
        // SDX Workshop 2815514917, CubeBlocks/Drives and Drives/mes; audited 2026-09-24.
        // Identity only. Actual power/fuel/damage and available thrust still come from the game.
        private static readonly HashSet<string> Subtypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sdx_driveCivilian3x3",
            "sdx_driveCivilian3x3_small",
            "sdx_driveCivilian3x3_smallMes",
            "sdx_driveCivilian3x3Mes",
            "sdx_driveCivilian5x5",
            "sdx_driveCivilian5x5Mes",
            "sdx_driveCivilian7x7",
            "sdx_driveCivilian7x7Mes",
            "sdx_driveIndustrial7x7",
            "sdx_driveIndustrial7x7Mes",
            "sdx_driveIndustrial9x9",
            "sdx_driveIndustrial9x9Mes",
            "sdx_driveMcrnMilitary3x3",
            "sdx_driveMcrnMilitary3x3Mes",
            "sdx_driveMcrnMilitary5x5",
            "sdx_driveMcrnMilitary5x5Mes",
            "sdx_driveMcrnMilitary7x7",
            "sdx_driveMcrnMilitary7x7Mes",
            "sdx_driveOpaMilitary3x3",
            "sdx_driveOpaMilitary3x3Mes",
            "sdx_driveOpaMilitary5x5",
            "sdx_driveOpaMilitary5x5Mes",
            "sdx_driveOpaMilitary7x7",
            "sdx_driveOpaMilitary7x7Mes",
            "sdx_driveTorch3x3_small",
            "sdx_driveUnnMilitary3x3",
            "sdx_driveUnnMilitary3x3Mes",
            "sdx_driveUnnMilitary5x5",
            "sdx_driveUnnMilitary5x5Mes",
            "sdx_driveUnnMilitary7x7",
            "sdx_driveUnnMilitary7x7Mes",
        };
        internal static int Count { get { return Subtypes.Count; } }
        internal static bool Contains(string subtype) { return subtype != null && Subtypes.Contains(subtype); }
    }
}
