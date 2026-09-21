using System;
using System.Collections.Generic;

namespace ZeosOreHelper
{
    internal sealed class OreOverlayPacket
    {
        public string Kind { get; set; }
        public string Command { get; set; }
        public OreOverlayFrame Frame { get; set; }
    }

    internal sealed class OreOverlayFrame
    {
        public string SearchMessage {get;set;} public int QualifyingCount {get;set;} public int ShownPings {get;set;}
        public ZeoOreShared.OreLayoutDraft Layout {get;set;}
        public string Version { get; set; }
        public long Sequence { get; set; }
        public long UtcMs { get; set; }
        public bool HelperEnabled { get; set; }
        public bool StreamerMode { get; set; }
        public bool GameWindowValid { get; set; }
        public int GameLeft { get; set; }
        public int GameTop { get; set; }
        public int GameWidth { get; set; }
        public int GameHeight { get; set; }
        public bool GameFocused { get; set; }
        public int VisibleCount { get; set; }
        public int ReadyCount { get; set; }
        public int PendingCount { get; set; }
        public int ErrorCount { get; set; }
        public int CachedCount { get; set; }
        public long SelectedEntityId { get; set; }
        public List<OreOverlayDeposit> Deposits {get;set;}=new List<OreOverlayDeposit>();
        public List<OreOverlayRoid> Roids { get; set; } = new List<OreOverlayRoid>();
    }

    internal sealed class OreOverlayDeposit {
        public long EntityId {get;set;} public string Ore {get;set;} public string Color {get;set;}
        public double ScreenX {get;set;} public double ScreenY {get;set;} public double RadiusX {get;set;} public double RadiusY {get;set;}
        public double DistanceMeters {get;set;} public double AsteroidDistanceMeters {get;set;} public double DiameterMeters {get;set;} public double EstimatedVolume {get;set;}
        public bool DetailLabel {get;set;}
    }
    internal sealed class OreOverlayRoid
    {
        public double EstimatedVolume {get;set;} public string ScanStatus {get;set;} public bool DetailLabel {get;set;} public bool MeetsThreshold {get;set;} public double SearchRank {get;set;}
        public long EntityId { get; set; }
        public int Number { get; set; }
        public double ScreenX { get; set; }
        public double ScreenY { get; set; }
        public bool Offscreen { get; set; }
        public bool Selected { get; set; }
        public bool Pinned { get; set; }
        public bool PingEligible { get; set; }
        public bool ListEligible { get; set; }
        public string State { get; set; }
        public string Grade { get; set; }
        public bool MustHit { get; set; }
        public double DistanceMeters { get; set; }
        public double DiameterMeters { get; set; }
        public double Quality { get; set; }
        public string TopOre { get; set; }
        public double TopOrePercent { get; set; }
        public string SecondOre { get; set; }
        public double SecondOrePercent { get; set; }
        public string GradeColor { get; set; }
        public string OreColor { get; set; }
    }
}
