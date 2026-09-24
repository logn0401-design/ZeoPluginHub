using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ZeoCore
{
    internal sealed class HudSettings
    {
        private const int CurrentSchema = 20;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        internal static readonly string PathName = System.IO.Path.Combine(Plugin.DataDirectory, "hud-settings.json");
        private DateTime _lastWriteUtc = DateTime.MinValue;

        public int SchemaVersion { get; set; } = 0;
        public HudProfile Profile { get; set; } = HudProfile.ThreatPriority;
        public HudMenuKey MenuKey { get; set; } = HudMenuKey.Home;
        public HudMenuPage MenuPage { get; set; } = HudMenuPage.Scope;
        public HudMarkerStyle MarkerStyle { get; set; } = HudMarkerStyle.ClassicReticle;
        public HudScopeSort ScopeSort { get; set; } = HudScopeSort.Nearest;
        public HudMarkerAnchor MarkerAnchor { get; set; } = HudMarkerAnchor.Auto;
        public HudFrameStyle FrameStyle { get; set; } = HudFrameStyle.WarRoom;
        public HudFontStyle FontStyle { get; set; } = HudFontStyle.MatchHud;
        public HudIconPack IconPack { get; set; } = HudIconPack.FlightHud;
        public HudMarkerSmoothing MarkerSmoothing { get; set; } = HudMarkerSmoothing.Off;
        public HudScopeLayout ScopeLayout { get; set; } = HudScopeLayout.Compact;
        public HudShipLayout ShipLayout { get; set; } = HudShipLayout.TwoColumn;
        public HudThemePreset ThemePreset { get; set; } = HudThemePreset.WarRoom;

        public bool HudEnabled { get; set; } = true;
        public bool ShowCrosshair { get; set; } = true;
        public bool ShowFlightData { get; set; } = true;
        public bool ShowShipH2O { get; set; } = true;
        public bool ShowShipO2 { get; set; } = true;
        public bool ShowShipFusion { get; set; } = true;
        public bool ShowShipDrive { get; set; } = true;
        public bool ShowShipReactor { get; set; } = true;
        public bool ShowShipPower { get; set; } = true;
        public bool ShowShipHp { get; set; } = true;
        public bool ShowShipSpeed { get; set; } = true;
        public int FusionReserveTarget { get; set; } = 2000;
        public bool ShowAmmoPanel { get; set; } = true;
        public bool RefillFuel { get; set; } = true;
        public bool RefillAmmo { get; set; } = true;
        public bool RefillTanks { get; set; } = true;
        public bool RefillUnloadOtherCargo { get; set; } = false;
        public int QuickRefillKey { get; set; }
        public int QuickRefillModifier { get; set; } = 1;
        // ZEOCORE_V067H4_AMMO_TYPE_VISIBILITY
        public bool ShowAmmoPdc40 { get; set; } = true;
        public bool ShowAmmoPdc40Improvised { get; set; } = true;
        public bool ShowAmmoPdc50 { get; set; } = true;
        public bool ShowAmmoSabot80 { get; set; } = true;
        public bool ShowAmmoSabot80Improvised { get; set; } = true;
        public bool ShowAmmoSabot100 { get; set; } = true;
        public bool ShowAmmoTorp160 { get; set; } = true;
        public bool ShowAmmoTorp190 { get; set; } = true;
        public bool ShowAmmoTorp220 { get; set; } = true;
        public bool AmmoOnlyRelevant { get; set; } = true;
        public int AmmoNameStyle { get; set; } = 0; // 0 clean, 1 server, 2 compact
        public int AmmoValueOrder { get; set; } = 0; // 0 HAVE/WANT, 1 WANT/HAVE
        public int WantPdc40 { get; set; } = 2000;
        public int WantPdc40Improvised { get; set; } = 1000;
        public int WantPdc50 { get; set; } = 1500;
        public int WantSabot80 { get; set; } = 1000;
        public int WantSabot80Improvised { get; set; } = 1000;
        public int WantSabot100 { get; set; } = 600;
        public int WantTorp160 { get; set; } = 20;
        public int WantTorp190 { get; set; } = 20;
        public int WantTorp220 { get; set; } = 10;
        public bool ShowTrackPanel { get; set; } = true;
        public bool KeepTosOutsideShip { get; set; } = false;
        public bool ShowLinkPanel { get; set; } = false;
        public bool ShowRosterPanel { get; set; } = true;
        public int RosterRows { get; set; } = 8;
        public bool RemoteFriendlyNoRangeLimit { get; set; } = true;
        public bool ShowCrossSectorRoster { get; set; } = true;
        public bool ShowPanelBackings { get; set; } = true;

        // Distress network. Activation is intentionally hold-to-send to avoid accidental alliance-wide SOS events.
        public bool DistressEnabled { get; set; } = true;
        public HudDistressKey DistressKey { get; set; } = HudDistressKey.F6;
        public HudDistressType DistressType { get; set; } = HudDistressType.GeneralSos;
        public HudDistressVisibility DistressVisibility { get; set; } = HudDistressVisibility.Alliance;
        public double DistressHoldSeconds { get; set; } = 1.00;
        public int DistressTtlMinutes { get; set; } = 10;
        public bool ShowDistressBanner { get; set; } = true;
        public bool ShowDistressWorldMarkers { get; set; } = true;
        public bool ShowCrossSectorDistress { get; set; } = true;
        public string DistressColor { get; set; } = "#FF3B30";

        public bool ShowLocalSpectrum { get; set; } = true;
        public bool ShowLocalWeaponCore { get; set; } = true;
        public bool ShowFleetFriendlies { get; set; } = true;
        public bool ShowSharedContacts { get; set; } = true;
        public bool ShowSharedSignals { get; set; } = true;
        public bool ReceiveFleetLink { get; set; } = true;
        public bool ShareWeaponCoreContacts { get; set; } = true;
        public bool ShareSpectrumSignals { get; set; } = true;
        public bool SharedTacticalNoRangeLimit { get; set; } = true;
        public bool ProjectCrossSectorDistress { get; set; } = true;

        public bool ShowHostiles { get; set; } = true;
        public bool ShowNeutrals { get; set; } = false;
        public bool ShowFriendlyMarkers { get; set; } = true;
        public bool ShowNames { get; set; } = false;
        public bool ShowDistance { get; set; } = false;
        public bool ShowClosingOnPriority { get; set; } = true;
        public bool ScopeShowCore { get; set; } = true;
        public bool ScopeShowStatus { get; set; } = true;

        public bool Declutter { get; set; } = true;
        // V1.3 performance/clutter: collapse mechanically connected target subgrids
        // and temporarily suppress pieces that detach while a target is breaking apart.
        public bool SuppressSubgridClutter { get; set; } = true;
        // Tactical fusion can run slower than overlay publishing. Markers still extrapolate
        // between fusion samples, keeping the HUD smooth without doing full fusion every packet.
        public bool AdaptiveTacticalRate { get; set; } = true;
        public int TacticalProcessingCap { get; set; } = 96;
        public int MaxFriendlyMarkers { get; set; } = 8;
        public int MaxContactMarkers { get; set; } = 12;
        public int ScopeRows { get; set; } = 8;
        public double MaxRangeKm { get; set; } = 200.0;
        public double TextScale { get; set; } = 1.20;
        public double FlightScale { get; set; } = 1.12;
        public double ScopePanelScale { get; set; } = 1.00;
        public double ScopeTextScale { get; set; } = 1.18;
        public double ScopeWidthScale { get; set; } = 1.15;
        public double LinkPanelScale { get; set; } = 1.08;
        public double DeclutterRadius { get; set; } = 0.045;
        public int StaleSeconds { get; set; } = 10;
        public int LastKnownSeconds { get; set; } = 20;

        public double SpectrumMarkerScale { get; set; } = 1.38;
        public double SpectrumIdScale { get; set; } = 1.28;
        public double FriendlyMarkerScale { get; set; } = 1.28;
        public double FriendlyIdScale { get; set; } = 1.08;
        public double HostileMarkerScale { get; set; } = 1.26;
        public double FocusMarkerScale { get; set; } = 1.35;
        public double OffscreenMarkerScale { get; set; } = 1.16;
        public double CrosshairScale { get; set; } = 1.10;
        public double MaxMarkerScale { get; set; } = 1.00;
        public bool FollowGameHud { get; set; } = true;

        // Normalized HUD coordinates: left=-1, right=1, top=1, bottom=-1.
        public double FlightX { get; set; } = -0.92;
        public double FlightY { get; set; } = 0.82;
        public double TrackPanelX { get; set; } = -0.72;
        public double TrackPanelY { get; set; } = -0.70;
        public double LinkPanelX { get; set; } = 0.62;
        public double LinkPanelY { get; set; } = 0.82;
        public double AmmoX { get; set; } = 0.62;
        public double AmmoY { get; set; } = -0.70;
        public double AmmoPanelScale { get; set; } = 1.00;
        public double RosterX { get; set; } = 0.60;
        public double RosterY { get; set; } = 0.30;
        public double RosterPanelScale { get; set; } = 1.00;

        public int PanelOpacity { get; set; } = 205;
        public double PanelPaddingScale { get; set; } = 1.00;
        public double BorderWidth { get; set; } = 1.15;

        public bool PredictTrackMotion { get; set; } = false;
        public double PredictionLimitSeconds { get; set; } = 1.00;
        public bool ShowMarkerAnchorDot { get; set; } = false;

        // High-contrast graphite + muted off-white defaults.
        public string HudTextColor { get; set; } = "#E7E9EC";
        public string HudSecondaryColor { get; set; } = "#B8BEC5";
        public string HudPanelColor { get; set; } = "#12161A";
        public string HudBorderColor { get; set; } = "#7B858E";
        public string CrosshairColor { get; set; } = "#D3D8DD";
        public string SpectrumColor { get; set; } = "#FFB84A";
        public string FriendlyColor { get; set; } = "#4DE1FF";
        public string HostileColor { get; set; } = "#FF5A5F";
        public string NeutralColor { get; set; } = "#B48CFF";
        public string StaleColor { get; set; } = "#70879A";
        public string FocusColor { get; set; } = "#FFE16B";
        public string MenuBackgroundColor { get; set; } = "#101419";
        public string MenuPanelColor { get; set; } = "#1A2026";
        public string MenuTextColor { get; set; } = "#E8ECF1";
        public string MenuAccentColor { get; set; } = "#F2C94C";

        // Capture-safe external overlay is the default renderer.
        public bool CaptureSafeHud { get; set; } = true;
        public bool CaptureSafeMenu { get; set; } = true;
        public bool OverlayAutoLaunch { get; set; } = true;
        public int NativeHudStartupRevision { get; set; }
        public bool FastCameraMarkers { get; set; } = true;
        public bool PrivacyInitialized { get; set; } = false;
        public bool TransmitTelemetry { get; set; } = true;
        public bool WriteLocalPayload { get; set; } = true;

        public static HudSettings Load()
        {
            try
            {
                Directory.CreateDirectory(Plugin.DataDirectory);
                if (File.Exists(PathName))
                {
                    var loaded = Json.Deserialize<HudSettings>(File.ReadAllText(PathName));
                    if (loaded != null)
                    {
                        loaded.MigrateIfNeeded();
                        if (loaded.NativeHudStartupRevision < 1)
                        {
                            if (loaded.HudEnabled) loaded.OverlayAutoLaunch = true;
                            loaded.NativeHudStartupRevision = 1;
                        }
                        loaded.Normalize();
                        loaded._lastWriteUtc = SafeWriteTime();
                        loaded.Save();
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log("HUD settings load failed: " + ex.Message);
            }

            var next = new HudSettings();
            next.ApplyProfile(HudProfile.ThreatPriority);
            next.ApplyTheme(HudThemePreset.WarRoom);
            next.NativeHudStartupRevision = 1;
            next.SchemaVersion = CurrentSchema;
            next.Save();
            return next;
        }

        public bool ReloadIfChanged()
        {
            try
            {
                DateTime now = SafeWriteTime();
                if (now == DateTime.MinValue || now <= _lastWriteUtc) return false;
                var loaded = Json.Deserialize<HudSettings>(File.ReadAllText(PathName));
                if (loaded == null) return false;
                loaded.MigrateIfNeeded();
                loaded.Normalize();
                CopyFrom(loaded);
                _lastWriteUtc = now;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log("HUD settings external reload failed: " + ex.Message);
                return false;
            }
        }

        public void Save()
        {
            try
            {
                SchemaVersion = CurrentSchema;
                Normalize();
                Directory.CreateDirectory(Plugin.DataDirectory);
                File.WriteAllText(PathName, Json.Serialize(this), new UTF8Encoding(false));
                _lastWriteUtc = SafeWriteTime();
            }
            catch (Exception ex)
            {
                Plugin.Log("HUD settings save failed: " + ex.Message);
            }
        }

        private static DateTime SafeWriteTime()
        {
            try { return File.Exists(PathName) ? File.GetLastWriteTimeUtc(PathName) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }

        private void MigrateIfNeeded()
        {
            if (SchemaVersion >= CurrentSchema) return;
            int previousSchema = SchemaVersion;

            if (previousSchema < 18)
            {
                // v0.6.7 one-time presentation migration. Put the ZeoCore TOS back
                // in the classic lower-left location and select the legacy visual mode.
                // After SchemaVersion becomes 18 the player can move/resize the panel normally.
                MarkerStyle = HudMarkerStyle.ClassicReticle;
                IconPack = HudIconPack.FlightHud;
                ScopeLayout = HudScopeLayout.Compact;
                ScopeSort = HudScopeSort.Nearest;
                MarkerSmoothing = HudMarkerSmoothing.Off;
                PredictTrackMotion = true;
                TrackPanelX = -0.72;
                TrackPanelY = -0.70;
                ScopePanelScale = 1.00;
                ScopeTextScale = Math.Max(ScopeTextScale, 1.18);
                ScopeWidthScale = Math.Max(1.10, Math.Min(ScopeWidthScale, 1.25));
                FrameStyle = HudFrameStyle.LegacyGlass;
                FollowGameHud = true;
                MaxMarkerScale = 1.00;
            }

            if (previousSchema < 19 && ThemePreset != HudThemePreset.Custom)
            {
                // ZEOCORE_V067H4_TRACK_COLOR_DEFAULTS
                // Relationship colors are tactical semantics, not source colors.
                // Keep CUSTOM palettes untouched.
                SpectrumColor = "#FFB84A"; // unknown
                NeutralColor = "#F1F3F5";  // neutral
                HostileColor = "#F04444";  // enemy
            }

            if (previousSchema < 9)
            {
            // v0.4 moves visuals to the external capture-safe renderer and makes the
            // classic Flight HUD reticle/panel treatment the default. Old positional
            // choices are kept, but tiny v0.3 scales are raised to readable defaults.
            TextScale = Math.Max(TextScale, 1.20);
            FlightScale = Math.Max(FlightScale, 1.12);
            if (Math.Abs(ScopePanelScale - 1.18) < 0.06) ScopePanelScale = 1.00;
            else ScopePanelScale = Math.Max(ScopePanelScale, 1.00);
            ScopeTextScale = Math.Max(ScopeTextScale, 1.18);
            LinkPanelScale = Math.Max(LinkPanelScale, 1.08);
            MarkerStyle = HudMarkerStyle.ClassicReticle;
            SpectrumMarkerScale = Math.Max(SpectrumMarkerScale, 1.38);
            SpectrumIdScale = Math.Max(SpectrumIdScale, 1.28);
            FriendlyMarkerScale = Math.Max(FriendlyMarkerScale, 1.28);
            FriendlyIdScale = Math.Max(FriendlyIdScale, 1.08);
            HostileMarkerScale = Math.Max(HostileMarkerScale, 1.26);
            FocusMarkerScale = Math.Max(FocusMarkerScale, 1.35);
            OffscreenMarkerScale = Math.Max(OffscreenMarkerScale, 1.16);
            ShowPanelBackings = true;
            PanelOpacity = 205;
            BorderWidth = Math.Max(BorderWidth, 1.15);
            PredictTrackMotion = true;
            PredictionLimitSeconds = Math.Max(PredictionLimitSeconds, 1.0);
            ShowMarkerAnchorDot = false;

            // v0.5 keeps the v0.4.4 UI/renderer architecture but upgrades the
            // existing HUD paths in place. The new default reticle is four-way,
            // the flight strip becomes ship status, and scope sorting becomes tactical.
            MarkerStyle = HudMarkerStyle.ClassicReticle;
            MarkerAnchor = HudMarkerAnchor.Auto;
            FrameStyle = HudFrameStyle.SeIndustrial;
            ShipLayout = HudShipLayout.TwoColumn;
            FontStyle = HudFontStyle.MatchHud;
            IconPack = HudIconPack.FlightHud;
            MarkerSmoothing = HudMarkerSmoothing.Off;
            PredictTrackMotion = true;
            ScopeLayout = HudScopeLayout.Compact;
            ScopeSort = HudScopeSort.Nearest;
            ShowFlightData = true;
            ShowShipH2O = true;
            ShowShipO2 = true;
            ShowShipFusion = true;
            ShowShipDrive = true;
            ShowShipReactor = true;
            ShowShipPower = true;
            ShowShipHp = true;
            ShowShipSpeed = true;
            FusionReserveTarget = Math.Max(100, FusionReserveTarget <= 0 ? 2000 : FusionReserveTarget);

            // v0.4.3 returns to the proven Flight HUD presentation. The old v0.4
            // prototype flight box is disabled by default and old default scope
            // placement moves back toward the classic lower-left scope location.
            ShowFlightData = true;
            ShowLinkPanel = false;
            if (Math.Abs(TrackPanelX + 0.88) < 0.03 && Math.Abs(TrackPanelY + 0.24) < 0.05)
            {
                TrackPanelX = -0.72;
                TrackPanelY = -0.70;
            }
            CaptureSafeHud = true;
            CaptureSafeMenu = true;
            OverlayAutoLaunch = true;
            // v0.4.3 keeps HIGH CONTRAST as the default theme and gives every default
            // tactical ping a non-white source color. Preserve CUSTOM themes; refresh
            // Graphite/HighContrast to the new palette.
            MenuPage = HudMenuPage.Scope;
            if (ThemePreset == HudThemePreset.Graphite || ThemePreset == HudThemePreset.HighContrast)
                ApplyTheme(HudThemePreset.HighContrast);
            }

            // v0.5.3 / schema 10 added the fleet roster. Only seed these values
            // for settings that actually predate that schema; never reset an existing
            // v0.5.5 user's roster choices during the v0.5.6 upgrade.
            if (previousSchema < 10)
            {
                ShowRosterPanel = true;
                RosterRows = Math.Max(1, RosterRows);
                RemoteFriendlyNoRangeLimit = true;
                ShowCrossSectorRoster = true;
            }

            // v0.5.4 / schema 11 added distress. Preserve any key/TTL/display choices
            // already saved by v0.5.4a/v0.5.5 (schema 12).
            if (previousSchema < 11)
            {
                DistressEnabled = true;
                DistressKey = HudDistressKey.F6;
                DistressHoldSeconds = DistressHoldSeconds <= 0 ? 1.0 : DistressHoldSeconds;
                DistressTtlMinutes = DistressTtlMinutes <= 0 ? 10 : DistressTtlMinutes;
                ShowDistressBanner = true;
                ShowDistressWorldMarkers = true;
                ShowCrossSectorDistress = true;
            }

            // v0.5.6 / schema 13 adds width-only scope expansion and explicit tactical
            // sharing controls. New properties receive safe ON defaults while all
            // existing v0.5.5 visual/layout/distress selections stay untouched.
            if (previousSchema < 13)
            {
                if (ScopeWidthScale <= 0) ScopeWidthScale = 1.15;
                ShowSharedSignals = true;
                ShareWeaponCoreContacts = true;
                ShareSpectrumSignals = true;
                SharedTacticalNoRangeLimit = true;
                ProjectCrossSectorDistress = true;
            }
            // v0.5.9 / schema 14 adds optional observer TOS. Default OFF so
            // existing HUD behavior is unchanged until the player opts in.
            if (previousSchema < 14)
                KeepTosOutsideShip = false;

            // v0.6.1 / schema 15 establishes one known-good network baseline for
            // recovery testing. This runs once when upgrading old settings, so the
            // player can still turn individual sharing/telemetry options off later.
            if (previousSchema < 15)
            {
                TransmitTelemetry = true;
                ReceiveFleetLink = true;
                ShareWeaponCoreContacts = true;
                ShareSpectrumSignals = true;
                ShowFleetFriendlies = true;
                ShowSharedContacts = true;
                ShowSharedSignals = true;
                DistressEnabled = true;
                ShowDistressBanner = true;
                ShowDistressWorldMarkers = true;
            }

            // V1.3 / schema 20: performance guardrails and wreck/subgrid clutter suppression.
            // These are safe defaults for existing users and can be changed from SCOPE.
            if (previousSchema < 20)
            {
                SuppressSubgridClutter = true;
                AdaptiveTacticalRate = true;
                if (TacticalProcessingCap < 24) TacticalProcessingCap = 96;
            }
            SchemaVersion = CurrentSchema;
            Plugin.Log("HUD settings migrated to v0.6.1 aligned open-test network baseline. Existing visual/layout choices preserved.");
        }

        public void ApplyProfile(HudProfile profile)
        {
            Profile = profile;
            switch (profile)
            {
                case HudProfile.Minimal:
                    ShowCrosshair = true; ShowFlightData = true; ShowTrackPanel = false; ShowLinkPanel = false;
                    ShowLocalSpectrum = true; ShowLocalWeaponCore = true; ShowFleetFriendlies = true; ShowSharedContacts = true;
                    ShowHostiles = true; ShowNeutrals = false; ShowFriendlyMarkers = true; ShowNames = false; ShowDistance = false;
                    ShowClosingOnPriority = false; Declutter = true; MaxFriendlyMarkers = 4; MaxContactMarkers = 6; ScopeRows = 5;
                    MarkerStyle = HudMarkerStyle.ClassicReticle; ScopeSort = HudScopeSort.Nearest;
                    break;
                case HudProfile.Essential:
                    ShowCrosshair = true; ShowFlightData = true; ShowTrackPanel = true; ShowLinkPanel = false;
                    ShowLocalSpectrum = true; ShowLocalWeaponCore = true; ShowFleetFriendlies = true; ShowSharedContacts = true;
                    ShowHostiles = true; ShowNeutrals = false; ShowFriendlyMarkers = true; ShowNames = false; ShowDistance = false;
                    ShowClosingOnPriority = false; Declutter = true; MaxFriendlyMarkers = 6; MaxContactMarkers = 8; ScopeRows = 8;
                    MarkerStyle = HudMarkerStyle.ClassicReticle; ScopeSort = HudScopeSort.Nearest;
                    break;
                case HudProfile.ThreatPriority:
                    ShowCrosshair = true; ShowFlightData = true; ShowTrackPanel = true; ShowLinkPanel = false;
                    ShowLocalSpectrum = true; ShowLocalWeaponCore = true; ShowFleetFriendlies = true; ShowSharedContacts = true;
                    ShowHostiles = true; ShowNeutrals = false; ShowFriendlyMarkers = true; ShowNames = false; ShowDistance = false;
                    ShowClosingOnPriority = true; Declutter = true; MaxFriendlyMarkers = 8; MaxContactMarkers = 12; ScopeRows = 8;
                    MarkerStyle = HudMarkerStyle.ClassicReticle; ScopeSort = HudScopeSort.Nearest;
                    break;
                case HudProfile.FullTactical:
                    ShowCrosshair = true; ShowFlightData = true; ShowTrackPanel = true; ShowLinkPanel = true;
                    ShowLocalSpectrum = true; ShowLocalWeaponCore = true; ShowFleetFriendlies = true; ShowSharedContacts = true;
                    ShowHostiles = true; ShowNeutrals = true; ShowFriendlyMarkers = true; ShowNames = true; ShowDistance = true;
                    ShowClosingOnPriority = true; Declutter = false; MaxFriendlyMarkers = 16; MaxContactMarkers = 24; ScopeRows = 12;
                    MarkerStyle = HudMarkerStyle.ClassicReticle; ScopeSort = HudScopeSort.Nearest;
                    break;
                case HudProfile.Custom:
                    break;
            }
            Normalize();
        }

        public void ApplyTheme(HudThemePreset preset)
        {
            ThemePreset = preset;
            switch (preset)
            {
                case HudThemePreset.Monochrome:
                    HudTextColor = "#D8D8D8"; HudSecondaryColor = "#A5A5A5"; HudPanelColor = "#272727"; HudBorderColor = "#686868";
                    CrosshairColor = "#D8D8D8"; SpectrumColor = "#D8D8D8"; FriendlyColor = "#C6C6C6"; HostileColor = "#F0F0F0";
                    NeutralColor = "#B8B8B8"; StaleColor = "#777777"; FocusColor = "#FFFFFF";
                    MenuBackgroundColor = "#1F1F1F"; MenuPanelColor = "#292929"; MenuTextColor = "#D8D8D8"; MenuAccentColor = "#A8A8A8";
                    break;
                case HudThemePreset.Amber:
                    HudTextColor = "#E3D6B0"; HudSecondaryColor = "#B5A77E"; HudPanelColor = "#2D2A24"; HudBorderColor = "#746A50";
                    CrosshairColor = "#E3D6B0"; SpectrumColor = "#D8BE76"; FriendlyColor = "#A8D0A2"; HostileColor = "#E48168";
                    NeutralColor = "#D8BE76"; StaleColor = "#817864"; FocusColor = "#F1D88C";
                    MenuBackgroundColor = "#211F1A"; MenuPanelColor = "#2B2923"; MenuTextColor = "#E3D6B0"; MenuAccentColor = "#C1AE75";
                    break;
                case HudThemePreset.HighContrast:
                    HudTextColor = "#E8ECF1"; HudSecondaryColor = "#B8BEC5"; HudPanelColor = "#101419"; HudBorderColor = "#7B858E";
                    CrosshairColor = "#D3D8DD"; SpectrumColor = "#FFB84A"; FriendlyColor = "#4DE1FF"; HostileColor = "#FF5A5F";
                    NeutralColor = "#B48CFF"; StaleColor = "#70879A"; FocusColor = "#FFE16B";
                    MenuBackgroundColor = "#101419"; MenuPanelColor = "#1A2026"; MenuTextColor = "#E8ECF1"; MenuAccentColor = "#F2C94C";
                    break;
                case HudThemePreset.WarRoom:
                    // Website-matched command-center palette: near-black/charcoal,
                    // cool white data, restrained red command accents. Semantic
                    // hostile/distress red is preserved; friendlies stay cool and muted.
                    HudTextColor = "#F1F3F5"; HudSecondaryColor = "#9CA3AB"; HudPanelColor = "#090B0E"; HudBorderColor = "#414850";
                    // ZEOCORE_V13E_WARROOM_SEMANTICS
                    CrosshairColor = "#E9ECEF"; SpectrumColor = "#FFB84A"; FriendlyColor = "#B8D7E8"; HostileColor = "#F04444";
                    NeutralColor = "#F1F3F5"; StaleColor = "#5E6670"; FocusColor = "#FFFFFF";
                    MenuBackgroundColor = "#07090B"; MenuPanelColor = "#111419"; MenuTextColor = "#F1F3F5"; MenuAccentColor = "#D52B2B";
                    break;
                case HudThemePreset.KeenNative:
                    // ZEOCORE_V13B_KEEN_NATIVE_THEME - inspired by Keen/SE terminal + signal HUD language.
                    HudTextColor = "#D9E6EA"; HudSecondaryColor = "#91A6AE"; HudPanelColor = "#26333B"; HudBorderColor = "#607781";
                    CrosshairColor = "#C9DADF"; SpectrumColor = "#A7C7D0"; FriendlyColor = "#8FCAD7"; HostileColor = "#D86A65";
                    NeutralColor = "#AAB9BE"; StaleColor = "#6E8087"; FocusColor = "#DCECF0";
                    MenuBackgroundColor = "#1B2931"; MenuPanelColor = "#263740"; MenuTextColor = "#D9E6EA"; MenuAccentColor = "#91B5C0";
                    break;
                case HudThemePreset.Custom:
                    return;
                default:
                    HudTextColor = "#D7D9DC"; HudSecondaryColor = "#A9ADB2"; HudPanelColor = "#2B2E32"; HudBorderColor = "#666C72";
                    CrosshairColor = "#D7D9DC"; SpectrumColor = "#D7A04B"; FriendlyColor = "#8FD3B0"; HostileColor = "#E07872";
                    NeutralColor = "#D0C187"; StaleColor = "#7E858B"; FocusColor = "#E6D28A";
                    MenuBackgroundColor = "#202327"; MenuPanelColor = "#2A2E33"; MenuTextColor = "#D7D9DC"; MenuAccentColor = "#AEB5BC";
                    break;
            }
        }

        public void ResetLayout()
        {
            FlightX = -0.92; FlightY = 0.82;
            TrackPanelX = -0.72; TrackPanelY = -0.70;
            LinkPanelX = 0.62; LinkPanelY = 0.82;
            AmmoX = 0.62; AmmoY = -0.70; AmmoPanelScale = 1.00;
            RosterX = 0.60; RosterY = 0.30; RosterPanelScale = 1.00;
            TextScale = 1.20; FlightScale = 1.00; ScopePanelScale = 1.00; ScopeTextScale = 1.18; ScopeWidthScale = 1.15; LinkPanelScale = 1.00;
            SpectrumMarkerScale = 1.38; SpectrumIdScale = 1.28; FriendlyMarkerScale = 1.28; FriendlyIdScale = 1.08;
            HostileMarkerScale = 1.26; FocusMarkerScale = 1.35; OffscreenMarkerScale = 1.16; CrosshairScale = 1.10;
            PanelOpacity = 205; PanelPaddingScale = 1.0; BorderWidth = 1.15;
        }

        public void MarkCustom()
        {
            if (Profile != HudProfile.Custom) Profile = HudProfile.Custom;
        }

        private void CopyFrom(HudSettings o)
        {
            // JSON round-trip is the most maintainable way to keep this in-place object
            // synchronized with the external settings UI without replacing references.
            var json = Json.Serialize(o);
            var copy = Json.Deserialize<HudSettings>(json);
            if (copy == null) return;
            foreach (var p in typeof(HudSettings).GetProperties())
            {
                if (!p.CanRead || !p.CanWrite) continue;
                try { p.SetValue(this, p.GetValue(copy, null), null); } catch { }
            }
            Normalize();
        }

        private void Normalize()
        {
            Profile = (HudProfile)Clamp((int)Profile, 0, 4);
            MenuKey = (HudMenuKey)Clamp((int)MenuKey, 0, 4);
            QuickRefillKey=ZeoOverlay.QuickRefillBinding.NormalizeKey(QuickRefillKey);
            QuickRefillModifier=ZeoOverlay.QuickRefillBinding.NormalizeModifier(QuickRefillModifier);
            MenuPage = (HudMenuPage)Clamp((int)MenuPage, 0, 10);
            MarkerStyle = (HudMarkerStyle)Clamp((int)MarkerStyle, 0, 3);
            ScopeSort = (HudScopeSort)Clamp((int)ScopeSort, 0, 5);
            MarkerAnchor = (HudMarkerAnchor)Clamp((int)MarkerAnchor, 0, 2);
            FrameStyle = (HudFrameStyle)Clamp((int)FrameStyle, 0, 16);
            FontStyle = (HudFontStyle)Clamp((int)FontStyle, 0, 3);
            IconPack = (HudIconPack)Clamp((int)IconPack, 0, 4);

            // v0.6.7 visual contract: normal local tactical contacts are permanently
            // rendered with the classic Zeo Flight HUD cue. Friendly/shared/distress
            // contacts keep their dedicated semantic renderers.
            MarkerStyle = HudMarkerStyle.ClassicReticle;
            IconPack = HudIconPack.FlightHud;
            MarkerSmoothing = (HudMarkerSmoothing)Clamp((int)MarkerSmoothing, 0, 3);
            ScopeLayout = (HudScopeLayout)Clamp((int)ScopeLayout, 0, 1);
            ThemePreset = (HudThemePreset)Clamp((int)ThemePreset, 0, 6);
            MaxFriendlyMarkers = Clamp(MaxFriendlyMarkers, 1, 24);
            MaxContactMarkers = Clamp(MaxContactMarkers, 1, 40);
            TacticalProcessingCap = Clamp(TacticalProcessingCap, 24, 192);
            ScopeRows = Clamp(ScopeRows, 3, 16);
            RosterRows = Clamp(RosterRows, 1, 24);
            DistressKey = (HudDistressKey)Clamp((int)DistressKey, 0, 7);
            DistressType = (HudDistressType)Clamp((int)DistressType, 0, 4);
            DistressVisibility = (HudDistressVisibility)Clamp((int)DistressVisibility, 0, 1);
            DistressHoldSeconds = Clamp(DistressHoldSeconds, 0.5, 3.0);
            DistressTtlMinutes = Clamp(DistressTtlMinutes, 1, 60);
            FusionReserveTarget = Clamp(FusionReserveTarget, 100, 1000000);
            AmmoNameStyle = Clamp(AmmoNameStyle, 0, 2);
            AmmoValueOrder = Clamp(AmmoValueOrder, 0, 1);
            WantPdc40=Clamp(WantPdc40,0,1000000); WantPdc40Improvised=Clamp(WantPdc40Improvised,0,1000000); WantPdc50=Clamp(WantPdc50,0,1000000);
            WantSabot80=Clamp(WantSabot80,0,1000000); WantSabot80Improvised=Clamp(WantSabot80Improvised,0,1000000); WantSabot100=Clamp(WantSabot100,0,1000000);
            WantTorp160=Clamp(WantTorp160,0,100000); WantTorp190=Clamp(WantTorp190,0,100000); WantTorp220=Clamp(WantTorp220,0,100000);
            MaxRangeKm = Clamp(MaxRangeKm, 5, 500);
            TextScale = Clamp(TextScale, 0.65, 2.50);
            FlightScale = Clamp(FlightScale, 0.60, 2.25);
            ScopePanelScale = Clamp(ScopePanelScale, 0.60, 2.25);
            ScopeTextScale = Clamp(ScopeTextScale, 0.60, 2.50);
            ScopeWidthScale = Clamp(ScopeWidthScale, 0.75, 2.50);
            LinkPanelScale = Clamp(LinkPanelScale, 0.60, 2.25);
            SpectrumMarkerScale = Clamp(SpectrumMarkerScale, 0.50, 3.00);
            SpectrumIdScale = Clamp(SpectrumIdScale, 0.50, 3.00);
            FriendlyMarkerScale = Clamp(FriendlyMarkerScale, 0.50, 3.00);
            FriendlyIdScale = Clamp(FriendlyIdScale, 0.50, 3.00);
            HostileMarkerScale = Clamp(HostileMarkerScale, 0.50, 3.00);
            FocusMarkerScale = Clamp(FocusMarkerScale, 0.50, 3.00);
            OffscreenMarkerScale = Clamp(OffscreenMarkerScale, 0.50, 3.00);
            MaxMarkerScale = Clamp(MaxMarkerScale, 0.50, 1.00);
            CrosshairScale = Clamp(CrosshairScale, 0.50, 3.00);
            DeclutterRadius = Clamp(DeclutterRadius, 0.015, 0.12);
            StaleSeconds = Clamp(StaleSeconds, 2, 60);
            LastKnownSeconds = Math.Max(StaleSeconds, Clamp(LastKnownSeconds, 5, 120));
            FlightX = ClampHud(FlightX); FlightY = ClampHud(FlightY);
            TrackPanelX = ClampHud(TrackPanelX); TrackPanelY = ClampHud(TrackPanelY);
            LinkPanelX = ClampHud(LinkPanelX); LinkPanelY = ClampHud(LinkPanelY);
            AmmoX = ClampHud(AmmoX); AmmoY = ClampHud(AmmoY);
            AmmoPanelScale = Clamp(AmmoPanelScale, 0.60, 2.25);
            RosterPanelScale = Clamp(RosterPanelScale, 0.60, 2.25);
            ShipLayout = (HudShipLayout)Clamp((int)ShipLayout, 0, 2);
            PanelOpacity = Clamp(PanelOpacity, 60, 245);
            PanelPaddingScale = Clamp(PanelPaddingScale, 0.6, 2.0);
            PredictionLimitSeconds = Clamp(PredictionLimitSeconds, 0.10, 2.50);
            BorderWidth = Clamp(BorderWidth, 0.5, 4.0);
            HudTextColor = SafeHex(HudTextColor, "#E8ECF1"); HudSecondaryColor = SafeHex(HudSecondaryColor, "#B8BEC5");
            HudPanelColor = SafeHex(HudPanelColor, "#101419"); HudBorderColor = SafeHex(HudBorderColor, "#7B858E");
            CrosshairColor = SafeHex(CrosshairColor, "#D3D8DD"); SpectrumColor = SafeHex(SpectrumColor, "#FFB84A");
            FriendlyColor = SafeHex(FriendlyColor, "#4DE1FF"); HostileColor = SafeHex(HostileColor, "#FF5A5F");
            NeutralColor = SafeHex(NeutralColor, "#B48CFF"); StaleColor = SafeHex(StaleColor, "#70879A"); FocusColor = SafeHex(FocusColor, "#FFE16B"); DistressColor = SafeHex(DistressColor, "#FF3B30");
            MenuBackgroundColor = SafeHex(MenuBackgroundColor, "#101419"); MenuPanelColor = SafeHex(MenuPanelColor, "#1A2026");
            MenuTextColor = SafeHex(MenuTextColor, "#E8ECF1"); MenuAccentColor = SafeHex(MenuAccentColor, "#F2C94C");
        }

        private static string SafeHex(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            string s = value.Trim();
            if (!s.StartsWith("#")) s = "#" + s;
            if (s.Length != 7) return fallback;
            for (int i = 1; i < 7; i++)
                if (!Uri.IsHexDigit(s[i])) return fallback;
            return s.ToUpperInvariant();
        }

        private static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
        private static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }
        private static double ClampHud(double value) { return Math.Max(-0.98, Math.Min(0.98, value)); }
    }
}
