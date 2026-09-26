namespace ZeoPDC
{
    internal static class ManagerSettings
    {
        // One-time, explicit release migration. Later user tuning and disable choices survive.
        internal static PdcConfig Upgrade(PdcConfig current)
        { return current.ManagerPresetVersion >= 1 ? current : Proven(current); }

        internal static PdcConfig Proven(PdcConfig current)
        {
            var n=LabSettings.ProvenBest(current);
            n.ManagerPresetVersion=1;
            n.ManagedDefenseEnabled=true;
            n.LabEnabled=false;
            n.LabDistribution=-1;
            n.PreaimMaxGuns=64;
            n.PreaimRangeMeters=24000;
            return PdcConfig.Clamp(n);
        }

        // The production adapter can enable distribution only. Never pass the user's
        // lab fields through to it: aim, spread, tolerance and target priority stay native.
        internal static PdcConfig DistributionOnly()
        { return new PdcConfig { LabDistribution=1 }; }

        internal static int LookCapacity(PdcConfig c)
        { return c.LabEnabled || c.PreemptiveFireEnabled ? c.PreemptiveMaxGuns : c.PreaimMaxGuns; }
    }
}
