using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ZeoNavOverlay
{
    [DataContract]
    public sealed class GpsDto
    {
        [DataMember] public string Name;
        [DataMember] public double X;
        [DataMember] public double Y;
        [DataMember] public double Z;
        [DataMember] public double Distance;
    }

    [DataContract]
    public sealed class NavConfig : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        public NavConfig Copy() { return (NavConfig)MemberwiseClone(); }
        // Increment when a new field needs an explicit upgrade default. DataContract
        // deserialization does not apply field initializers to members missing from an
        // older config file, so this protects one-click upgrades that preserve config.json.
        [DataMember] public int ConfigVersion = 7;

        [DataMember] public string MenuKey = "Insert";
        [DataMember] public string StartKey = "None";
        [DataMember] public string AbortKey = "End";
        [DataMember] public string ManualFlipKey = "None";
        [DataMember] public string SignalUpKey = "None";
        [DataMember] public string SignalDownKey = "None";
        [DataMember] public bool StreamerMode = true;

        [DataMember] public string Frame = "WAR ROOM";
        [DataMember] public string Theme = "WAR ROOM";
        [DataMember] public string FontStyle = "MATCH HUD";
        [DataMember] public double HudX = -0.88;
        [DataMember] public double HudY = 0.72;
        [DataMember] public double GlobalScale = 1.0;
        [DataMember] public double PanelScale = 1.0;
        [DataMember] public double DestinationScale = 1.0;
        [DataMember] public double DistanceScale = 1.0;
        [DataMember] public double SpeedScale = 1.0;
        [DataMember] public double SignalScale = 1.0;
        [DataMember] public double EtaScale = 1.0;
        [DataMember] public double PhaseScale = 1.0;
        [DataMember] public double FlipScale = 1.0;
        [DataMember] public double StopScale = 1.0;
        [DataMember] public double ProgressScale = 1.0;
        [DataMember] public double WarningScale = 1.0;
        [DataMember] public int BackingOpacity = 178;
        [DataMember] public double BorderWidth = 1.2;
        [DataMember] public double InnerPadding = 1.0;
        [DataMember] public double CornerCut = 1.0;
        [DataMember] public double HeaderHeight = 1.0;
        [DataMember] public double PatternIntensity = 1.0;
        [DataMember] public bool AccentRail = true;
        [DataMember] public bool StateColors = true;

        // Active trip/navigation panel. AUTO follows normal nav visibility; ALWAYS keeps
        // the panel up while in the cockpit; HIDDEN keeps it down. By default it inherits
        // the active Zeo HUD theme/frame so it cannot drift into a separate color language.
        [DataMember] public string TripPanelVisibility = "AUTO";
        [DataMember] public bool TripUseHudTheme = true;
        [DataMember] public string TripHudText = "#E8ECF1";
        [DataMember] public string TripSecondaryText = "#AEB8C4";
        [DataMember] public string TripPanelBacking = "#101419";
        [DataMember] public string TripPanelBorder = "#617181";
        [DataMember] public string TripAccent = "#FFB84A";

        [DataMember] public double BufferKm = 5.0;
        [DataMember] public int DriveSlider = 45; // legacy migration only
        [DataMember] public double MaxDriveSigKm = 125.0;
        [DataMember] public double FlipTimeSeconds = 20.0;
        [DataMember] public double BrakeSafety = 1.12;
        [DataMember] public double ArrivalRadiusMeters = 5.0;
        [DataMember] public double ArrivalSpeedMps = 0.35;
        [DataMember] public double SpeedCapOverride = 0.0;
        [DataMember] public bool AbortOnManualInput = true;
        [DataMember] public bool AutoFlip = true;
        [DataMember] public bool FullStop = true;
        [DataMember] public bool SpectrumFeedback = true;

        // Custom theme values. High Contrast defaults mirror ZeoCore.
        [DataMember] public string HudText = "#E8ECF1";
        [DataMember] public string SecondaryText = "#AEB8C4";
        [DataMember] public string PanelBacking = "#101419";
        [DataMember] public string PanelBorder = "#617181";
        [DataMember] public string Accent = "#FFB84A";
        [DataMember] public string Good = "#4DE1FF";
        [DataMember] public string Warning = "#FFE16B";
        [DataMember] public string Danger = "#FF5A5F";
        [DataMember] public string MenuBackground = "#07090B";
        [DataMember] public string MenuPanel = "#111419";
        [DataMember] public string MenuText = "#F1F3F5";
        [DataMember] public string MenuAccent = "#D52B2B";
    }

    [DataContract]
    public sealed class NavSnapshot
    {
        [DataMember] public string Version;
        [DataMember] public long GameHwnd;
        [DataMember] public int GamePid;
        [DataMember] public int ClientX;
        [DataMember] public int ClientY;
        [DataMember] public int ClientW;
        [DataMember] public int ClientH;
        [DataMember] public bool MenuVisible;
        [DataMember] public bool HudVisible;
        [DataMember] public string Ship;
        [DataMember] public string State;
        [DataMember] public string Phase;
        [DataMember] public string Destination;
        [DataMember] public string WarningText;
        [DataMember] public double DistanceMeters;
        [DataMember] public double RouteStartDistanceMeters;
        [DataMember] public double BufferMeters;
        [DataMember] public double SpeedMps;
        [DataMember] public double CommandSpeedMps;
        [DataMember] public double SpeedCapMps;
        [DataMember] public string SpeedCapSource;
        [DataMember] public double ClosingMps;
        [DataMember] public double LateralMps;
        [DataMember] public double DriveRatio;
        [DataMember] public double SpectrumDrive;
        [DataMember] public double SpectrumStrength;
        [DataMember] public double SpectrumTarget; // legacy raw target; no longer used for KM control
        [DataMember] public double SpectrumDriveKm;
        [DataMember] public double SphericalStrongKm, SphericalWeakKm, DirectionalStrongKm, DirectionalWeakKm;
        [DataMember] public double MaxDriveSigKm;
        [DataMember] public bool SpectrumKmReady;
        [DataMember] public string SpectrumKmSource;
        [DataMember] public string SignalGovernorState;
        [DataMember] public bool SpectrumReady;
        [DataMember] public long SpectrumSelfEmitterId;
        [DataMember] public string SpectrumSelfMatch;
        [DataMember] public int SpectrumSelfAgeFrames;
        [DataMember] public int ForwardWorkingThrusterCount;
        [DataMember] public int ForwardMainDriveCount;
        [DataMember] public double ForwardCommandRatio;
        [DataMember] public double ForwardReadbackRatio;
        [DataMember] public double EtaSeconds;
        [DataMember] public double EtaAtFullSeconds;
        [DataMember] public double TimeSavedAtFullSeconds;
        [DataMember] public double AccelSeconds;
        [DataMember] public double CoastSeconds;
        [DataMember] public double FlipBurnSeconds;
        [DataMember] public double FlipInSeconds;
        [DataMember] public double FlipAtMeters;
        [DataMember] public double StopDistanceMeters;
        [DataMember] public double Progress01;
        [DataMember] public double ManualFlipDegreesLeft;
        [DataMember] public bool ManualFlipActive;
        [DataMember] public int ConstructGridCount;
        [DataMember] public int ThrusterCount;
        [DataMember] public int MainDriveCount;
        [DataMember] public int ForwardWorkingMainDriveCount;
        [DataMember] public double ForwardMainThrustMN;
        [DataMember] public double ForwardMainRatedThrustMN;
        [DataMember] public int GyroCount;
        [DataMember] public int RcsGyroCount;
        [DataMember] public double ForwardThrustMN;
        [DataMember] public double BackwardThrustMN;
        [DataMember] public string DriveScanSummary;
        [DataMember] public double AlignmentErrorDeg;
        [DataMember] public double AngularSpeedDeg;
        [DataMember] public List<GpsDto> Gps = new List<GpsDto>();
        [DataMember] public NavConfig Config;
        [DataMember] public NavLayoutDraft Layout;
        public NavSnapshot Copy() { return (NavSnapshot)MemberwiseClone(); }
    }

    [DataContract]
    public sealed class NavCommand
    {
        [DataMember] public string Type;
        [DataMember] public string Key;
        [DataMember] public string Text;
        [DataMember] public double Value;
        [DataMember] public string Name;
        [DataMember] public double X;
        [DataMember] public double Y;
        [DataMember] public double Z;
        [DataMember] public NavLayoutBounds LayoutBounds;
    }

    [DataContract]
    public sealed class NavLayoutDraft
    {
        [DataMember] public string Token;
        [DataMember] public double X, Y;
        [DataMember] public double ToolbarX, ToolbarY, ToolbarW, ToolbarH;
        public bool ToolbarContains(double x,double y) { return x>=ToolbarX && y>=ToolbarY && x<=ToolbarX+ToolbarW && y<=ToolbarY+ToolbarH; }
    }
    [DataContract]
    public sealed class NavLayoutBounds
    {
        [DataMember] public string Token;
        [DataMember] public double X, Y, Width, Height;
        [DataMember] public int ViewportW, ViewportH;
        public bool Contains(double x,double y) { return x>=X && y>=Y && x<=X+Width && y<=Y+Height; }
    }
}



