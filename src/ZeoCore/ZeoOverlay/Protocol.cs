using System.Collections.Generic;

namespace ZeoOverlay
{
    internal sealed class OverlayPacket
    {
        public string Kind { get; set; }
        public string Command { get; set; }
        public OverlayMarkerUpdate MarkerUpdate { get; set; }
        public OverlayFrame Frame { get; set; }
    }

    internal sealed class OverlayMarkerUpdate
    {
        public long Sequence { get; set; }
        public long UtcMs { get; set; }
        public List<OverlayMarker> Markers { get; set; } = new List<OverlayMarker>();
    }

    internal sealed class OverlayFrame
    {
        public string Version { get; set; }
        public ZeoOverlay.HudLayoutState Layout { get; set; }
        public long Sequence { get; set; }
        public long UtcMs { get; set; }
        public bool HudEnabled { get; set; }
        public bool HasShip { get; set; }
        public bool ObserverTosEnabled { get; set; }
        public bool GameWindowValid { get; set; }
        public int GameLeft { get; set; }
        public int GameTop { get; set; }
        public int GameWidth { get; set; }
        public int GameHeight { get; set; }
        public bool GameFocused { get; set; }
        public int GameHudState { get; set; } = 1; // ZEOCORE_V067H3_GAME_HUD_STATE
        public long GameWindowHandle { get; set; }
        public int GameProcessId { get; set; }
        public string OwnGridName { get; set; }
        public string SectorId { get; set; }
        public string SectorName { get; set; }
        public bool SectorKnown { get; set; }
        public string ServerTrustState { get; set; }
        public string ServerTrustSector { get; set; }
        public string ServerTrustDetail { get; set; }
        public string ServerExpectedEndpoint { get; set; }
        public string ServerObservedEndpoint { get; set; }
        public string ServerIdentity { get; set; }
        public bool ServerIdentityVerified { get; set; }
        public bool ServerEndpointVerified { get; set; }
        public bool ServerNetworkAllowed { get; set; }
        public string AuthState { get; set; }
        public string AuthDetail { get; set; }
        public bool AuthLinked { get; set; }
        public bool AuthAuthorized { get; set; }
        public string AuthPairingCode { get; set; }
        public string AuthUsername { get; set; }
        public string AuthFactionTag { get; set; }
        public string AuthAssignedScope { get; set; }
        public string AuthEffectiveScope { get; set; }
        public double Speed { get; set; }
        public double H2O { get; set; } = -1;
        public double O2 { get; set; } = -1;
        public double FusionPellets { get; set; }
        public double DriveHealth { get; set; } = -1;
        public double ReactorHealth { get; set; } = -1;
        public double PowerCurrent { get; set; }
        public double PowerMax { get; set; }
        public double ShipHp { get; set; } = -1;
        public bool ShowCrosshair { get; set; }
        public bool ShowFlightData { get; set; }
        public bool ShowTrackPanel { get; set; }
        public bool ShowLinkPanel { get; set; }
        public bool TxOn { get; set; }
        public bool RxOn { get; set; }
        public bool RxLinked { get; set; }
        public double RxAgeSeconds { get; set; }
        public int FleetFriendlyCount { get; set; }
        public int FleetContactCount { get; set; }
        public bool FleetSectorAware { get; set; }
        public bool ShowRosterPanel { get; set; }
        public bool DistressLocalActive { get; set; }
        public bool DistressServerReady { get; set; }
        public string DistressStatus { get; set; }
        public string SosNotification { get; set; }
        public long SosNotificationExpiresMs { get; set; }
        public int ActiveDistressCount { get; set; }
        public int TotalScopeCount { get; set; }
        public List<OverlayMarker> Markers { get; set; } = new List<OverlayMarker>();
        public List<OverlayScopeRow> ScopeRows { get; set; } = new List<OverlayScopeRow>();
        public List<OverlayAmmoRow> AmmoRows { get; set; } = new List<OverlayAmmoRow>();
        public List<OverlayRosterRow> RosterRows { get; set; } = new List<OverlayRosterRow>();
        public List<OverlayDistressAlert> DistressAlerts { get; set; } = new List<OverlayDistressAlert>();
    }

    internal sealed class OverlayRosterRow
    {
        public string Name { get; set; }
        public string SectorId { get; set; }
        public string SectorName { get; set; }
        public bool SectorKnown { get; set; }
        public bool SameSector { get; set; }
        public bool Online { get; set; }
        public double Distance { get; set; }
        public double AgeSeconds { get; set; }
        public double ShipHp { get; set; }
        public double DriveHealth { get; set; }
        public double PowerLoad { get; set; }
        public bool Distress { get; set; }
        public string DistressType { get; set; }
        public double DistressSecondsRemaining { get; set; }
    }

    internal sealed class OverlayDistressAlert
    {
        public string Name { get; set; }
        public string SectorName { get; set; }
        public bool SameSector { get; set; }
        public double Distance { get; set; }
        public double SecondsRemaining { get; set; }
        public string Type { get; set; }
        public double ShipHp { get; set; }
    }

    internal sealed class OverlayAmmoRow
    {
        public string Key { get; set; }
        public string CleanName { get; set; }
        public string ServerName { get; set; }
        public double Have { get; set; }
        public double Want { get; set; }
        public bool Relevant { get; set; }
    }

    internal sealed class OverlayMarker
    {
        public bool AttackTarget { get; set; }
        public int TrackId { get; set; }
        public int Source { get; set; }
        public bool Friendly { get; set; }
        public bool Focused { get; set; }
        public bool Stale { get; set; }
        public string Relation { get; set; }
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public bool Offscreen { get; set; }
        public double Distance { get; set; }
        public double Closing { get; set; }
        public double AgeSeconds { get; set; }
        public double Priority { get; set; }
        public bool Distress { get; set; }
        public string DistressType { get; set; }
        public double DistressSecondsRemaining { get; set; }
    }

    internal sealed class OverlayScopeRow
    {
        public int TrackId { get; set; }
        public int Source { get; set; }
        public bool Friendly { get; set; }
        public bool Focused { get; set; }
        public bool Stale { get; set; }
        public string Relation { get; set; }
        public string Name { get; set; }
        public double Distance { get; set; }
        public double Speed { get; set; }
        public double Closing { get; set; }
        public double AgeSeconds { get; set; }
        public bool Distress { get; set; }
        public string DistressType { get; set; }
        public double DistressSecondsRemaining { get; set; }
    }
}
