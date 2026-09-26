using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ZeoOverlay
{
    internal sealed class OverlaySettings
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private string _path;
        private DateTime _lastWrite;

        public int SchemaVersion { get; set; }
        public int Profile { get; set; }
        public int MenuKey { get; set; }
        // Null preserves the existing five-key setting; zero explicitly disables it.
        public int? MenuKeyCode { get; set; }
        public int MenuPage { get; set; }
        public int MarkerStyle { get; set; } = 3;
        public int ScopeSort { get; set; } = 1;
        public int MarkerAnchor { get; set; } = 2;
        public int FrameStyle { get; set; } = 6;
        public int FontStyle { get; set; } = 0;
        public int IconPack { get; set; } = 0;
        public int MarkerSmoothing { get; set; } = 2;
        public int ScopeLayout { get; set; } = 1;
        public int ShipLayout { get; set; } = 1;
        public int ThemePreset { get; set; } = 5;

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
        public bool TargetMarksEnabled { get; set; }
        public bool TargetMarksAlliance { get; set; }
        public bool TargetMarkPulse { get; set; } = true;
        public int RefillMovesPerPass { get; set; } = 4;
        public double RefillUnitsPerTransfer { get; set; } = 10000000;
        public int TargetMarkKey { get; set; }
        public int TargetMarkModifier { get; set; } = 1;
        public double MarkerPreviewDistanceKm { get; set; } = 2;
        public int QuickRefillKey { get; set; }
        public int QuickRefillModifier { get; set; } = 1;
        // ZEOCORE_V067H4_AMMO_TYPE_VISIBILITY
        // ZEOCORE_V12_AMMO_VISIBILITY_EXT
        // Overlay-only visibility preferences are kept out of the shared plugin JSON
        // so the protected V1.0/h3a plugin cannot erase them when it saves settings.
        [ScriptIgnore] public bool ShowAmmoPdc40 { get; set; } = true;
        [ScriptIgnore] public bool ShowAmmoPdc40Improvised { get; set; } = true;
        [ScriptIgnore] public bool ShowAmmoPdc50 { get; set; } = true;
        [ScriptIgnore] public bool ShowAmmoSabot80 { get; set; } = true;
        [ScriptIgnore] public bool ShowAmmoSabot80Improvised { get; set; } = true;
        [ScriptIgnore] public bool ShowAmmoSabot100 { get; set; } = true;
        [ScriptIgnore] public bool ShowAmmoTorp160 { get; set; } = true;
        [ScriptIgnore] public bool ShowAmmoTorp190 { get; set; } = true;
        [ScriptIgnore] public bool ShowAmmoTorp220 { get; set; } = true;
        public bool AmmoOnlyRelevant { get; set; } = true;
        public int AmmoNameStyle { get; set; } = 0;
        public int AmmoValueOrder { get; set; } = 0;
        public int WantPdc40 { get; set; } = 2000; public int WantPdc40Improvised { get; set; } = 1000; public int WantPdc50 { get; set; } = 1500;
        public int WantSabot80 { get; set; } = 1000; public int WantSabot80Improvised { get; set; } = 1000; public int WantSabot100 { get; set; } = 600;
        public int WantTorp160 { get; set; } = 20; public int WantTorp190 { get; set; } = 20; public int WantTorp220 { get; set; } = 10;
        public bool ShowTrackPanel { get; set; } = true;
        public bool KeepTosOutsideShip { get; set; } = false;
        public bool ShowLinkPanel { get; set; } = false;
        public bool ShowRosterPanel { get; set; } = true;
        public int RosterRows { get; set; } = 8;
        public bool RemoteFriendlyNoRangeLimit { get; set; } = true;
        public bool ShowCrossSectorRoster { get; set; } = true;
        // Legacy master retained for config compatibility. V1.3b renders backing fill per panel.
        public bool ShowPanelBackings { get; set; } = true;
        // ZEOCORE_V13B_PER_PANEL_BACKINGS
        [ScriptIgnore] public bool BackingShipInfo { get; set; } = true;
        [ScriptIgnore] public bool BackingTos { get; set; } = true;
        [ScriptIgnore] public bool BackingFleetLink { get; set; } = true;
        [ScriptIgnore] public bool BackingAmmo { get; set; } = true;
        [ScriptIgnore] public bool BackingRoster { get; set; } = true;
        [ScriptIgnore] public bool BackingDistress { get; set; } = true;
        public bool DistressEnabled { get; set; } = true;
        public int DistressKey { get; set; } = 1; // F6
        public int DistressType { get; set; } = 0;
        public int DistressVisibility { get; set; } = 1; // alliance
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
        public int MaxSharedTracks { get; set; } = 96;
        public double MaxSharedTrackDistanceKm { get; set; } = 0;
        public bool ProjectCrossSectorDistress { get; set; } = true;
        public bool ShowHostiles { get; set; } = true;
        public bool ShowNeutrals { get; set; }
        public bool ShowFriendlyMarkers { get; set; } = true;
        public bool ShowNames { get; set; } = false;
        public bool ShowDistance { get; set; } = false;
        public bool ShowClosingOnPriority { get; set; } = true;
        public bool ScopeShowCore { get; set; } = true;
        public bool ScopeShowStatus { get; set; } = true;
        public bool Declutter { get; set; } = true;
        public bool SuppressSubgridClutter { get; set; } = true;
        public bool AdaptiveTacticalRate { get; set; } = true;
        public int TacticalProcessingCap { get; set; } = 96;
        public int MaxFriendlyMarkers { get; set; } = 8;
        public int MaxContactMarkers { get; set; } = 12;
        public int ScopeRows { get; set; } = 8;
        public double MaxRangeKm { get; set; } = 200;
        public double TextScale { get; set; } = 1.2;
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
        public double FlightX { get; set; } = -0.92;
        public double FlightY { get; set; } = 0.82;
        public double TrackPanelX { get; set; } = -0.72;
        public double TrackPanelY { get; set; } = -0.70;
        public double LinkPanelX { get; set; } = 0.62;
        public double LinkPanelY { get; set; } = 0.82;
        public double AmmoX { get; set; } = 0.62; public double AmmoY { get; set; } = -0.70; public double AmmoPanelScale { get; set; } = 1.00;
        public double RosterX { get; set; } = 0.60; public double RosterY { get; set; } = 0.30; public double RosterPanelScale { get; set; } = 1.00;
        public int PanelOpacity { get; set; } = 205;
        public double PanelPaddingScale { get; set; } = 1.0;
        public double BorderWidth { get; set; } = 1.15;
        public bool PredictTrackMotion { get; set; } = false;
        public double PredictionLimitSeconds { get; set; } = 1.0;
        public bool ShowMarkerAnchorDot { get; set; } = false;

        public string HudTextColor { get; set; } = "#E8ECF1";
        public string HudSecondaryColor { get; set; } = "#B8BEC5";
        public string HudPanelColor { get; set; } = "#101419";
        public string HudBorderColor { get; set; } = "#7B858E";
        public string CrosshairColor { get; set; } = "#D3D8DD";
        public double ShipBoxTextScale { get; set; } = 1;
        public double ScopeBoxTextScale { get; set; } = 1;
        public double FleetBoxTextScale { get; set; } = 1;
        public double AmmoBoxTextScale { get; set; } = 1;
        public double RosterBoxTextScale { get; set; } = 1;
        public double DistressBoxTextScale { get; set; } = 1;
        public int FriendlyMarkerRevision { get; set; } = 0;
        public double SharedMarkerScale { get; set; } = 1;
        public double SharedIdScale { get; set; } = 1;
        public string SharedTrackColor { get; set; } = "#BC9CFF";
        public string SpectrumColor { get; set; } = "#FFB84A";
        public string FriendlyColor { get; set; } = "#2EAB33";
        public string HostileColor { get; set; } = "#FF5A5F";
        public string NeutralColor { get; set; } = "#B48CFF";
        public string StaleColor { get; set; } = "#70879A";
        public string FocusColor { get; set; } = "#FFE16B";
        public string MenuBackgroundColor { get; set; } = "#101419";
        public string MenuPanelColor { get; set; } = "#1A2026";
        public string MenuTextColor { get; set; } = "#E8ECF1";
        public string MenuAccentColor { get; set; } = "#F2C94C";

        public bool CaptureSafeHud { get; set; } = true;
        public bool CaptureSafeMenu { get; set; } = true;
        public bool DistressPositionCustom { get; set; }
        public double DistressX { get; set; }
        public double DistressY { get; set; } = .96;
        public bool OverlayAutoLaunch { get; set; } = true;
        public int NativeHudStartupRevision { get; set; }
        public bool FastCameraMarkers { get; set; } = true;
        public bool PrivacyInitialized { get; set; }
        public bool TransmitTelemetry { get; set; } = true;
        public bool WriteLocalPayload { get; set; } = true;

        internal string PathName { get { return _path; } }

        internal static OverlaySettings Load(string path)
        {
            var s = new OverlaySettings { _path = path };
            s.Reload();
            return s;
        }

        internal bool ReloadIfChanged()
        {
            try
            {
                var t = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
                if (t <= _lastWrite) return false;
                Reload();
                return true;
            }
            catch { return false; }
        }

        internal void Reload()
        {
            if (!string.IsNullOrWhiteSpace(_path) && File.Exists(_path))
            {
                try
                {
                    var loaded = Json.Deserialize<OverlaySettings>(File.ReadAllText(_path));
                    if (loaded != null)
                    {
                        loaded._path = _path;
                        CopyFrom(loaded);
                    }
                }
                catch { }
            }
            LoadUiExtension();
            Normalize();
            _lastWrite = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.UtcNow;
        }

        internal void Save()
        {
            try
            {
                Normalize();
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));
                File.WriteAllText(_path, Json.Serialize(this), new UTF8Encoding(false));
                SaveUiExtension();
                _lastWrite = File.GetLastWriteTimeUtc(_path);
            }
            catch { }
        }

        public double ShipHudWidth { get; set; } = 1;
        public double ShipHudHeight { get; set; } = 1;
        public double ScopeHudWidth { get; set; } = 1;
        public double ScopeHudHeight { get; set; } = 1;
        public double FleetHudWidth { get; set; } = 1;
        public double FleetHudHeight { get; set; } = 1;
        public double AmmoHudWidth { get; set; } = 1;
        public double AmmoHudHeight { get; set; } = 1;
        public double RosterHudWidth { get; set; } = 1;
        public double RosterHudHeight { get; set; } = 1;
        public double DistressHudWidth { get; set; } = 1;
        public double DistressHudHeight { get; set; } = 1;

        private sealed class UiExtension
        {
            public double? ShipBoxTextScale { get; set; }
            public double? ScopeBoxTextScale { get; set; }
            public double? FleetBoxTextScale { get; set; }
            public double? AmmoBoxTextScale { get; set; }
            public double? RosterBoxTextScale { get; set; }
            public double? DistressBoxTextScale { get; set; }

        public double ShipHudWidth { get; set; } = 1;
        public double ShipHudHeight { get; set; } = 1;
        public double ScopeHudWidth { get; set; } = 1;
        public double ScopeHudHeight { get; set; } = 1;
        public double FleetHudWidth { get; set; } = 1;
        public double FleetHudHeight { get; set; } = 1;
        public double AmmoHudWidth { get; set; } = 1;
        public double AmmoHudHeight { get; set; } = 1;
        public double RosterHudWidth { get; set; } = 1;
        public double RosterHudHeight { get; set; } = 1;
        public double DistressHudWidth { get; set; } = 1;
        public double DistressHudHeight { get; set; } = 1;

            public bool DistressPositionCustom { get; set; }
            public double DistressX { get; set; }
            public double DistressY { get; set; } = .96;
            public bool ShowAmmoPdc40 { get; set; } = true;
            public bool ShowAmmoPdc40Improvised { get; set; } = true;
            public bool ShowAmmoPdc50 { get; set; } = true;
            public bool ShowAmmoSabot80 { get; set; } = true;
            public bool ShowAmmoSabot80Improvised { get; set; } = true;
            public bool ShowAmmoSabot100 { get; set; } = true;
            public bool ShowAmmoTorp160 { get; set; } = true;
            public bool ShowAmmoTorp190 { get; set; } = true;
            public bool ShowAmmoTorp220 { get; set; } = true;
            public bool BackingShipInfo { get; set; } = true;
            public bool BackingTos { get; set; } = true;
            public bool BackingFleetLink { get; set; } = true;
            public bool BackingAmmo { get; set; } = true;
            public bool BackingRoster { get; set; } = true;
            public bool BackingDistress { get; set; } = true;
        }

        private string UiExtensionPath
        {
            get { return string.IsNullOrWhiteSpace(_path) ? null : _path + ".zeo-ui.json"; }
        }

        private void LoadUiExtension()
        {
            try
            {
                string p = UiExtensionPath;
                if (string.IsNullOrWhiteSpace(p)) return;
                if (!File.Exists(p))
                {
                    // One-time migration from the old all-panels backing switch.
                    BackingShipInfo = ShowPanelBackings; BackingTos = ShowPanelBackings;
                    BackingFleetLink = ShowPanelBackings; BackingAmmo = ShowPanelBackings;
                    BackingRoster = ShowPanelBackings; BackingDistress = ShowPanelBackings;
                    return;
                }
                UiExtension x = Json.Deserialize<UiExtension>(File.ReadAllText(p));
                if (x == null) return;
                if(x.ShipBoxTextScale.HasValue)ShipBoxTextScale=SafeBoxText(x.ShipBoxTextScale.Value);
                if(x.ScopeBoxTextScale.HasValue)ScopeBoxTextScale=SafeBoxText(x.ScopeBoxTextScale.Value);
                if(x.FleetBoxTextScale.HasValue)FleetBoxTextScale=SafeBoxText(x.FleetBoxTextScale.Value);
                if(x.AmmoBoxTextScale.HasValue)AmmoBoxTextScale=SafeBoxText(x.AmmoBoxTextScale.Value);
                if(x.RosterBoxTextScale.HasValue)RosterBoxTextScale=SafeBoxText(x.RosterBoxTextScale.Value);
                if(x.DistressBoxTextScale.HasValue)DistressBoxTextScale=SafeBoxText(x.DistressBoxTextScale.Value);

                ShipHudWidth=HudLayoutState.SafeSize(x.ShipHudWidth);
                ShipHudHeight=HudLayoutState.SafeSize(x.ShipHudHeight);
                ScopeHudWidth=HudLayoutState.SafeSize(x.ScopeHudWidth);
                ScopeHudHeight=HudLayoutState.SafeSize(x.ScopeHudHeight);
                FleetHudWidth=HudLayoutState.SafeSize(x.FleetHudWidth);
                FleetHudHeight=HudLayoutState.SafeSize(x.FleetHudHeight);
                AmmoHudWidth=HudLayoutState.SafeSize(x.AmmoHudWidth);
                AmmoHudHeight=HudLayoutState.SafeSize(x.AmmoHudHeight);
                RosterHudWidth=HudLayoutState.SafeSize(x.RosterHudWidth);
                RosterHudHeight=HudLayoutState.SafeSize(x.RosterHudHeight);
                DistressHudWidth=HudLayoutState.SafeSize(x.DistressHudWidth);
                DistressHudHeight=HudLayoutState.SafeSize(x.DistressHudHeight);
                DistressPositionCustom=x.DistressPositionCustom;
                DistressX=Clamp(x.DistressX,-.98,.98); DistressY=Clamp(x.DistressY,-.98,.98);
                ShowAmmoPdc40 = x.ShowAmmoPdc40;
                ShowAmmoPdc40Improvised = x.ShowAmmoPdc40Improvised;
                ShowAmmoPdc50 = x.ShowAmmoPdc50;
                ShowAmmoSabot80 = x.ShowAmmoSabot80;
                ShowAmmoSabot80Improvised = x.ShowAmmoSabot80Improvised;
                ShowAmmoSabot100 = x.ShowAmmoSabot100;
                ShowAmmoTorp160 = x.ShowAmmoTorp160;
                ShowAmmoTorp190 = x.ShowAmmoTorp190;
                ShowAmmoTorp220 = x.ShowAmmoTorp220;
                BackingShipInfo = x.BackingShipInfo; BackingTos = x.BackingTos;
                BackingFleetLink = x.BackingFleetLink; BackingAmmo = x.BackingAmmo;
                BackingRoster = x.BackingRoster; BackingDistress = x.BackingDistress;
            }
            catch { }
        }

        private void SaveUiExtension()
        {
            try
            {
                string p = UiExtensionPath;
                if (string.IsNullOrWhiteSpace(p)) return;
                var x = new UiExtension
                {
                    ShipBoxTextScale=SafeBoxText(ShipBoxTextScale),
                    ScopeBoxTextScale=SafeBoxText(ScopeBoxTextScale),
                    FleetBoxTextScale=SafeBoxText(FleetBoxTextScale),
                    AmmoBoxTextScale=SafeBoxText(AmmoBoxTextScale),
                    RosterBoxTextScale=SafeBoxText(RosterBoxTextScale),
                    DistressBoxTextScale=SafeBoxText(DistressBoxTextScale),

                    ShipHudWidth=HudLayoutState.SafeSize(ShipHudWidth),
                    ShipHudHeight=HudLayoutState.SafeSize(ShipHudHeight),
                    ScopeHudWidth=HudLayoutState.SafeSize(ScopeHudWidth),
                    ScopeHudHeight=HudLayoutState.SafeSize(ScopeHudHeight),
                    FleetHudWidth=HudLayoutState.SafeSize(FleetHudWidth),
                    FleetHudHeight=HudLayoutState.SafeSize(FleetHudHeight),
                    AmmoHudWidth=HudLayoutState.SafeSize(AmmoHudWidth),
                    AmmoHudHeight=HudLayoutState.SafeSize(AmmoHudHeight),
                    RosterHudWidth=HudLayoutState.SafeSize(RosterHudWidth),
                    RosterHudHeight=HudLayoutState.SafeSize(RosterHudHeight),
                    DistressHudWidth=HudLayoutState.SafeSize(DistressHudWidth),
                    DistressHudHeight=HudLayoutState.SafeSize(DistressHudHeight),
                    DistressPositionCustom=DistressPositionCustom,
                    DistressX=DistressX, DistressY=DistressY,
                    ShowAmmoPdc40 = ShowAmmoPdc40,
                    ShowAmmoPdc40Improvised = ShowAmmoPdc40Improvised,
                    ShowAmmoPdc50 = ShowAmmoPdc50,
                    ShowAmmoSabot80 = ShowAmmoSabot80,
                    ShowAmmoSabot80Improvised = ShowAmmoSabot80Improvised,
                    ShowAmmoSabot100 = ShowAmmoSabot100,
                    ShowAmmoTorp160 = ShowAmmoTorp160,
                    ShowAmmoTorp190 = ShowAmmoTorp190,
                    ShowAmmoTorp220 = ShowAmmoTorp220,
                    BackingShipInfo = BackingShipInfo, BackingTos = BackingTos,
                    BackingFleetLink = BackingFleetLink, BackingAmmo = BackingAmmo,
                    BackingRoster = BackingRoster, BackingDistress = BackingDistress
                };
                File.WriteAllText(p, Json.Serialize(x), new UTF8Encoding(false));
            }
            catch { }
        }

        private void CopyFrom(OverlaySettings o)
        {
            foreach (var p in typeof(OverlaySettings).GetProperties())
            {
                if (!p.CanRead || !p.CanWrite) continue;
                try { p.SetValue(this, p.GetValue(o, null), null); } catch { }
            }
        }

        internal void ApplyProfile(int profile)
        {
            Profile = profile;
            if (profile == 0)
            {
                ShowCrosshair = true; ShowFlightData = true; ShowTrackPanel = false; ShowLinkPanel = false;
                ShowNames = false; ShowDistance = false; ShowClosingOnPriority = false; Declutter = true;
                MaxFriendlyMarkers = 4; MaxContactMarkers = 6; ScopeRows = 5; MarkerStyle = 3; ScopeSort = 0;
            }
            else if (profile == 1)
            {
                ShowCrosshair = true; ShowFlightData = true; ShowTrackPanel = true; ShowLinkPanel = false;
                ShowNames = false; ShowDistance = false; ShowClosingOnPriority = false; Declutter = true;
                MaxFriendlyMarkers = 6; MaxContactMarkers = 8; ScopeRows = 8; MarkerStyle = 3; ScopeSort = 0;
            }
            else if (profile == 2)
            {
                ShowCrosshair = true; ShowFlightData = true; ShowTrackPanel = true; ShowLinkPanel = false;
                ShowNames = false; ShowDistance = false; ShowClosingOnPriority = true; Declutter = true;
                MaxFriendlyMarkers = 8; MaxContactMarkers = 12; ScopeRows = 8; MarkerStyle = 3; ScopeSort = 1;
            }
            else if (profile == 3)
            {
                ShowCrosshair = true; ShowFlightData = true; ShowTrackPanel = true; ShowLinkPanel = true;
                ShowNames = true; ShowDistance = true; ShowClosingOnPriority = true; Declutter = false;
                MaxFriendlyMarkers = 16; MaxContactMarkers = 24; ScopeRows = 12; MarkerStyle = 3; ScopeSort = 1;
            }
            Save();
        }

        internal void ApplyTheme(int preset)
        {
            ThemePreset = preset;
            if (preset == 1)
            {
                HudTextColor="#D8D8D8"; HudSecondaryColor="#A5A5A5"; HudPanelColor="#272727"; HudBorderColor="#686868";
                CrosshairColor="#D8D8D8"; SpectrumColor="#D8D8D8"; FriendlyColor="#2EAB33"; HostileColor="#F0F0F0"; NeutralColor="#B8B8B8"; StaleColor="#777777"; FocusColor="#FFFFFF";
                MenuBackgroundColor="#1F1F1F"; MenuPanelColor="#292929"; MenuTextColor="#D8D8D8"; MenuAccentColor="#A8A8A8";
            }
            else if (preset == 2)
            {
                HudTextColor="#E3D6B0"; HudSecondaryColor="#B5A77E"; HudPanelColor="#2D2A24"; HudBorderColor="#746A50";
                CrosshairColor="#E3D6B0"; SpectrumColor="#D8BE76"; FriendlyColor="#2EAB33"; HostileColor="#E48168"; NeutralColor="#D8BE76"; StaleColor="#817864"; FocusColor="#F1D88C";
                MenuBackgroundColor="#211F1A"; MenuPanelColor="#2B2923"; MenuTextColor="#E3D6B0"; MenuAccentColor="#C1AE75";
            }
            else if (preset == 3)
            {
                HudTextColor="#E8ECF1"; HudSecondaryColor="#B8BEC5"; HudPanelColor="#101419"; HudBorderColor="#7B858E";
                CrosshairColor="#D3D8DD"; SpectrumColor="#FFB84A"; FriendlyColor="#2EAB33"; HostileColor="#FF5A5F"; NeutralColor="#B48CFF"; StaleColor="#70879A"; FocusColor="#FFE16B";
                MenuBackgroundColor="#101419"; MenuPanelColor="#1A2026"; MenuTextColor="#E8ECF1"; MenuAccentColor="#F2C94C";
            }
            else if (preset == 5)
            {
                HudTextColor="#F1F3F5"; HudSecondaryColor="#9CA3AB"; HudPanelColor="#090B0E"; HudBorderColor="#414850";
                CrosshairColor="#E9ECEF"; SpectrumColor="#FFB84A"; FriendlyColor="#2EAB33"; HostileColor="#F04444"; NeutralColor="#F1F3F5"; StaleColor="#5E6670"; FocusColor="#FFFFFF";
                MenuBackgroundColor="#07090B"; MenuPanelColor="#111419"; MenuTextColor="#F1F3F5"; MenuAccentColor="#D52B2B";
            }
            else if (preset == 6) // KEEN NATIVE // ZEOCORE_V13B_KEEN_NATIVE_THEME
            {
                HudTextColor="#D9E6EA"; HudSecondaryColor="#91A6AE"; HudPanelColor="#26333B"; HudBorderColor="#607781";
                CrosshairColor="#C9DADF"; SpectrumColor="#A7C7D0"; FriendlyColor="#2EAB33"; HostileColor="#D86A65"; NeutralColor="#AAB9BE"; StaleColor="#6E8087"; FocusColor="#DCECF0";
                MenuBackgroundColor="#1B2931"; MenuPanelColor="#263740"; MenuTextColor="#D9E6EA"; MenuAccentColor="#91B5C0";
            }
            else if (preset == 0)
            {
                HudTextColor="#D7D9DC"; HudSecondaryColor="#A9ADB2"; HudPanelColor="#2B2E32"; HudBorderColor="#666C72";
                CrosshairColor="#D7D9DC"; SpectrumColor="#D7A04B"; FriendlyColor="#2EAB33"; HostileColor="#E07872"; NeutralColor="#D0C187"; StaleColor="#7E858B"; FocusColor="#E6D28A";
                MenuBackgroundColor="#202327"; MenuPanelColor="#2A2E33"; MenuTextColor="#D7D9DC"; MenuAccentColor="#AEB5BC";
            }
            Save();
        }

        internal Color ColorOf(string hex, Color fallback)
        {
            try { return ColorTranslator.FromHtml(hex); } catch { return fallback; }
        }

        private static double SafeBoxText(double v){return double.IsNaN(v)||double.IsInfinity(v)?1:Math.Max(.6,Math.Min(3,v));}
        private void Normalize()
        {
            RefillMovesPerPass=Math.Max(1,Math.Min(8,RefillMovesPerPass));
            RefillUnitsPerTransfer=double.IsNaN(RefillUnitsPerTransfer)||double.IsInfinity(RefillUnitsPerTransfer)?10000000:Math.Max(1,Math.Min(10000000,RefillUnitsPerTransfer));
            TargetMarkKey=QuickRefillBinding.NormalizeKey(TargetMarkKey);
            TargetMarkModifier=QuickRefillBinding.NormalizeModifier(TargetMarkModifier);
            MarkerPreviewDistanceKm=double.IsNaN(MarkerPreviewDistanceKm)||double.IsInfinity(MarkerPreviewDistanceKm)?2:Math.Max(0,Math.Min(100,MarkerPreviewDistanceKm));
            QuickRefillKey=QuickRefillBinding.NormalizeKey(QuickRefillKey);
            QuickRefillModifier=QuickRefillBinding.NormalizeModifier(QuickRefillModifier);
            PanelOpacity = Math.Max(60, Math.Min(245, PanelOpacity));
            ScopeRows = Math.Max(3, Math.Min(16, ScopeRows));
            RosterRows = Math.Max(1, Math.Min(24, RosterRows));
            DistressKey = Math.Max(0, Math.Min(7, DistressKey));
            DistressType = Math.Max(0, Math.Min(4, DistressType));
            DistressVisibility = Math.Max(0, Math.Min(1, DistressVisibility));
            DistressHoldSeconds = Clamp(DistressHoldSeconds,.5,3.0);
            DistressTtlMinutes = Math.Max(1, Math.Min(60, DistressTtlMinutes));
            FusionReserveTarget = Math.Max(100, Math.Min(1000000, FusionReserveTarget));
            ShipLayout=Math.Max(0,Math.Min(2,ShipLayout)); AmmoNameStyle=Math.Max(0,Math.Min(2,AmmoNameStyle)); AmmoValueOrder=Math.Max(0,Math.Min(1,AmmoValueOrder));
            WantPdc40=ClampInt(WantPdc40,0,1000000); WantPdc40Improvised=ClampInt(WantPdc40Improvised,0,1000000); WantPdc50=ClampInt(WantPdc50,0,1000000);
            WantSabot80=ClampInt(WantSabot80,0,1000000); WantSabot80Improvised=ClampInt(WantSabot80Improvised,0,1000000); WantSabot100=ClampInt(WantSabot100,0,1000000);
            WantTorp160=ClampInt(WantTorp160,0,100000); WantTorp190=ClampInt(WantTorp190,0,100000); WantTorp220=ClampInt(WantTorp220,0,100000);
            MarkerStyle = Math.Max(0, Math.Min(3, MarkerStyle));
            ScopeSort = Math.Max(0, Math.Min(5, ScopeSort));
            MarkerAnchor = Math.Max(0, Math.Min(2, MarkerAnchor));
            FrameStyle = Math.Max(0, Math.Min(16, FrameStyle));
            FontStyle = Math.Max(0, Math.Min(3, FontStyle));
            IconPack = Math.Max(0, Math.Min(4, IconPack));
            MarkerSmoothing = Math.Max(0, Math.Min(3, MarkerSmoothing));
            ScopeLayout = Math.Max(0, Math.Min(1, ScopeLayout));
            ThemePreset = Math.Max(0, Math.Min(6, ThemePreset));
            MaxFriendlyMarkers = Math.Max(1, Math.Min(24, MaxFriendlyMarkers));
            MaxContactMarkers = Math.Max(1, Math.Min(40, MaxContactMarkers));
            MaxSharedTracks=Math.Max(0,Math.Min(192,MaxSharedTracks));
            MaxSharedTrackDistanceKm=double.IsNaN(MaxSharedTrackDistanceKm)||double.IsInfinity(MaxSharedTrackDistanceKm)?0:Math.Max(0,Math.Min(1000000,MaxSharedTrackDistanceKm));
            TacticalProcessingCap = Math.Max(24, Math.Min(192, TacticalProcessingCap));
            TextScale = Clamp(TextScale,.65,2.5); FlightScale=Clamp(FlightScale,.6,2.25); ScopePanelScale=Clamp(ScopePanelScale,.6,2.25); ScopeTextScale=Clamp(ScopeTextScale,.6,2.5); ScopeWidthScale=Clamp(ScopeWidthScale,.75,2.5); LinkPanelScale=Clamp(LinkPanelScale,.6,2.25);
            SpectrumMarkerScale=Clamp(SpectrumMarkerScale,.5,3); SpectrumIdScale=Clamp(SpectrumIdScale,.5,3); FriendlyMarkerScale=Clamp(FriendlyMarkerScale,.5,3); FriendlyIdScale=Clamp(FriendlyIdScale,.5,3);
            HostileMarkerScale=Clamp(HostileMarkerScale,.5,3); FocusMarkerScale=Clamp(FocusMarkerScale,.5,3); OffscreenMarkerScale=Clamp(OffscreenMarkerScale,.5,3); MaxMarkerScale=Clamp(MaxMarkerScale,.50,3.0); CrosshairScale=Clamp(CrosshairScale,.5,3);
            PanelPaddingScale=Clamp(PanelPaddingScale,.6,2); BorderWidth=Clamp(BorderWidth,.5,4); PredictionLimitSeconds=Clamp(PredictionLimitSeconds,.1,2.5); AmmoPanelScale=Clamp(AmmoPanelScale,.6,2.25); RosterPanelScale=Clamp(RosterPanelScale,.6,2.25);
            DistressColor = SafeHex(DistressColor, "#FF3B30");
            SharedTrackColor = SafeHex(SharedTrackColor, "#BC9CFF");
            if(FriendlyMarkerRevision<1){FriendlyColor="#2EAB33";FriendlyMarkerRevision=1;}
            ShipBoxTextScale = SafeBoxText(ShipBoxTextScale);
            ScopeBoxTextScale = SafeBoxText(ScopeBoxTextScale);
            FleetBoxTextScale = SafeBoxText(FleetBoxTextScale);
            AmmoBoxTextScale = SafeBoxText(AmmoBoxTextScale);
            RosterBoxTextScale = SafeBoxText(RosterBoxTextScale);
            DistressBoxTextScale = SafeBoxText(DistressBoxTextScale);
            SharedMarkerScale=SafeBoxText(SharedMarkerScale); SharedIdScale=SafeBoxText(SharedIdScale);
        }
        private static string SafeHex(string value,string fallback)
        {
            if(string.IsNullOrWhiteSpace(value)) return fallback; string x=value.Trim(); if(!x.StartsWith("#")) x="#"+x;
            if(x.Length!=7) return fallback; for(int i=1;i<7;i++) if(!Uri.IsHexDigit(x[i])) return fallback; return x.ToUpperInvariant();
        }
        private static double Clamp(double v,double min,double max){return Math.Max(min,Math.Min(max,v));}
        private static int ClampInt(int v,int min,int max){return Math.Max(min,Math.Min(max,v));}
    }
}
