using System;
using System.Collections.Generic;
using VRageMath;

namespace ZeoCore
{
    internal enum HudProfile
    {
        Minimal = 0,
        Essential = 1,
        ThreatPriority = 2,
        FullTactical = 3,
        Custom = 4
    }

    internal enum HudMenuKey
    {
        Home = 0,
        Insert = 1,
        PageUp = 2,
        PageDown = 3,
        End = 4
    }

    internal enum HudMenuPage
    {
        Flight = 0,
        Scope = 1,
        Fleet = 2,
        Layout = 3,
        Theme = 4,
        Markers = 5,
        Privacy = 6,
        Capture = 7,
        Ammo = 8,
        Roster = 9,
        Distress = 10
    }

    internal enum HudMarkerStyle
    {
        ClassicReticle = 0,
        Compact = 1,
        Detailed = 2,
        FourWayFlight = 3
    }

    internal enum HudScopeSort
    {
        Nearest = 0,
        Threat = 1,
        TrackId = 2,
        Fastest = 3,
        ClosingFastest = 4,
        FocusedFirst = 5
    }

    internal enum HudMarkerAnchor
    {
        DetectionPosition = 0,
        GridCenter = 1,
        Auto = 2
    }

    internal enum HudFrameStyle
    {
        SeIndustrial = 0,
        FighterHud = 1,
        MarsTactical = 2,
        BelterUtility = 3,
        NavyGlass = 4,
        Stealth = 5,
        WarRoom = 6,
        CommandGrid = 7,
        Redline = 8,
        Blacksite = 9,
        Chevron = 10,
        SplitWing = 11,
        HexCommand = 12,
        Razor = 13,
        LegacyGlass = 14,
        KeenSignal = 15,
        WeaponCore = 16
    }

    internal enum HudFontStyle
    {
        MatchHud = 0,
        Condensed = 1,
        Tech = 2,
        Standard = 3
    }

    internal enum HudIconPack
    {
        ZeoTactical = 0,
        FlightHud = 1,
        Diamonds = 2,
        AirCombat = 3,
        Minimal = 4
    }

    internal enum HudMarkerSmoothing
    {
        Off = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    internal enum HudShipLayout
    {
        SingleColumn = 0,
        TwoColumn = 1,
        CompactGrid = 2
    }

    internal enum HudScopeLayout
    {
        Compact = 0,
        Detailed = 1
    }

    internal enum HudDistressKey
    {
        F5 = 0,
        F6 = 1,
        F7 = 2,
        F8 = 3,
        F9 = 4,
        F10 = 5,
        F11 = 6,
        F12 = 7
    }

    internal enum HudDistressType
    {
        GeneralSos = 0,
        UnderAttack = 1,
        Disabled = 2,
        Recovery = 3,
        Fuel = 4
    }

    internal enum HudDistressVisibility
    {
        FactionOnly = 0,
        Alliance = 1
    }

    internal enum HudThemePreset
    {
        Graphite = 0,
        Monochrome = 1,
        Amber = 2,
        HighContrast = 3,
        Custom = 4,
        WarRoom = 5,
        KeenNative = 6
    }

    internal enum HudTrackSource
    {
        Spectrum,
        WeaponCore,
        FleetFriendly,
        FleetContact,
        FleetSignal
    }

    internal sealed class HudTrack
    {
        public string Key;
        public long EntityId;
        public long ReporterSourceId;
        public int TrackId;
        public string Name;
        public string Relation;
        public string ContactType;
        public HudTrackSource Source;
        public Vector3D Position;
        public Vector3D Velocity;
        public double Threat;
        public double? SignalStrength;
        public string RawEmitterId;
        public bool Focused;
        public bool Stale;
        public double AgeSeconds;
        public bool Friendly;
        public string SectorId;
        public string SectorName;
        public bool SectorKnown;
        public bool SameSector;
        public bool Online = true;
        public bool HasPosition = true;
        public double ShipHp = -1;
        public double DriveHealth = -1;
        public double PowerLoad = -1;
        public bool IsDistress;
        public string DistressType;
        public long DistressExpiresMs;
        public double DistressSecondsRemaining;
        public double Distance;
        public double Closing;
        public double Priority;

        public HudTrack Clone()
        {
            return (HudTrack)MemberwiseClone();
        }
    }

    internal sealed class HudAmmoStock
    {
        public string Key;
        public string CleanName;
        public string ServerName;
        public string Subtype;
        public double Have;
        public bool Relevant;
        public bool WeaponCompatible;
        public long[] WeaponIds = new long[0];

        public HudAmmoStock Clone() { return (HudAmmoStock)MemberwiseClone(); }
    }

    internal sealed class LocalHudSnapshot
    {
        public bool HasShip;
        public bool HasObserver;
        public Vector3D ObserverPosition;
        public Vector3D ObserverVelocity;
        public long OwnGridId;
        public long FocusEntityId;
        public int WcRawThreatCount;
        public int WcNormalizedTrackCount;
        public bool WcFocusFallbackAdded;
        public string OwnGridName;
        public string SectorId;
        public string SectorName;
        public bool SectorKnown;
        public Vector3D OwnPosition;
        public Vector3D OwnVelocity;

        // Ship-status snapshot. Percent values use 0..1; -1 means unavailable.
        public double H2O = -1;
        public double O2 = -1;
        public double FusionPellets;
        public double DriveHealth = -1;
        public double ReactorHealth = -1;
        public double PowerCurrent;
        public double PowerMax;
        public double ShipHp = -1;
        public readonly List<HudAmmoStock> Ammo = new List<HudAmmoStock>();

        public readonly List<HudTrack> WeaponCoreTracks = new List<HudTrack>();
        public DateTime CapturedUtc = DateTime.UtcNow;

        public LocalHudSnapshot Clone()
        {
            var next = new LocalHudSnapshot
            {
                HasShip = HasShip,
                HasObserver = HasObserver,
                ObserverPosition = ObserverPosition,
                ObserverVelocity = ObserverVelocity,
                OwnGridId = OwnGridId,
                FocusEntityId = FocusEntityId,
                WcRawThreatCount = WcRawThreatCount,
                WcNormalizedTrackCount = WcNormalizedTrackCount,
                WcFocusFallbackAdded = WcFocusFallbackAdded,
                OwnGridName = OwnGridName,
                SectorId = SectorId,
                SectorName = SectorName,
                SectorKnown = SectorKnown,
                OwnPosition = OwnPosition,
                OwnVelocity = OwnVelocity,
                H2O = H2O,
                O2 = O2,
                FusionPellets = FusionPellets,
                DriveHealth = DriveHealth,
                ReactorHealth = ReactorHealth,
                PowerCurrent = PowerCurrent,
                PowerMax = PowerMax,
                ShipHp = ShipHp,
                CapturedUtc = CapturedUtc
            };
            for (int i = 0; i < WeaponCoreTracks.Count; i++)
                next.WeaponCoreTracks.Add(WeaponCoreTracks[i].Clone());
            for (int i = 0; i < Ammo.Count; i++)
                next.Ammo.Add(Ammo[i].Clone());
            return next;
        }
    }

    internal sealed class FleetPictureSnapshot
    {
        public string World = "default";
        public long ServerTimeMs;
        public long ReceivedUtcMs;
        public double SimSpeed = 1.0;
        public readonly List<HudTrack> Friendlies = new List<HudTrack>();
        public readonly List<HudTrack> Contacts = new List<HudTrack>();
        public readonly List<HudTrack> Roster = new List<HudTrack>();
        public readonly List<HudTrack> Distress = new List<HudTrack>();
        public bool SectorAware;

        public FleetPictureSnapshot Clone()
        {
            var next = new FleetPictureSnapshot
            {
                World = World,
                ServerTimeMs = ServerTimeMs,
                ReceivedUtcMs = ReceivedUtcMs,
                SimSpeed = SimSpeed,
                SectorAware = SectorAware
            };
            for (int i = 0; i < Friendlies.Count; i++) next.Friendlies.Add(Friendlies[i].Clone());
            for (int i = 0; i < Contacts.Count; i++) next.Contacts.Add(Contacts[i].Clone());
            for (int i = 0; i < Roster.Count; i++) next.Roster.Add(Roster[i].Clone());
            for (int i = 0; i < Distress.Count; i++) next.Distress.Add(Distress[i].Clone());
            return next;
        }
    }

    internal sealed class TrackIdAllocator
    {
        private sealed class Slot
        {
            public int Id;
            public DateTime Seen;
        }

        private readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
        public int Get(string key, string alias = null)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            Slot slot;
            if (!_slots.TryGetValue(key, out slot) && (alias == null || !_slots.TryGetValue(alias, out slot)))
            {
                var used = new HashSet<int>();
                foreach (var value in _slots.Values) used.Add(value.Id);
                int id = 1;
                while (used.Contains(id)) id++;
                slot = new Slot { Id = id };
            }
            slot.Seen = DateTime.UtcNow;
            _slots[key] = slot;
            if (!string.IsNullOrEmpty(alias)) _slots[alias] = slot;
            return slot.Id;
        }

        public void Clear()
        {
            _slots.Clear();
        }

        public void Trim(double seconds)
        {
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-Math.Max(2, seconds));
            var remove = new List<string>();
            foreach (var pair in _slots)
                if (pair.Value.Seen < cutoff) remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++)
            {
                int id = _slots[remove[i]].Id;
                _slots.Remove(remove[i]);
            }
        }
    }
}
