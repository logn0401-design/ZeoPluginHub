using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ZeoPdcOverlay
{
    [DataContract]
    public sealed class PdcConfig
    {
        [DataMember] public int ConfigVersion = 15;
        [DataMember] public int ManagerPresetVersion;
        [DataMember] public bool HeatRangeBanksEnabled=true;
        [DataMember] public double HeatRangeOuterMeters=3000, HeatRangeMiddleMeters=2900, HeatRangeInnerMeters=2800;
        [DataMember] public double HeatRangeHoldSeconds=5, HeatRangeSwapMargin=5;
        [DataMember] public int HeatRangeVersion=1;
        [DataMember] public bool DecoyCyclingEnabled=true;
        [DataMember] public double DecoyCycleSeconds=2;
        [DataMember] public bool HeatWarningEnabled=true, HeatWarningFlash=true;
        [DataMember] public double HeatWarningPercent=90, HeatWarningResetPercent=82;
        [DataMember] public int HudFontStyle=0;
        [DataMember] public bool ManagedDefenseEnabled;
        [DataMember] public string ClientMode="OBSERVE";
        [DataMember] public bool ClientProbeEnabled;
        [DataMember] public int PreaimMaxGuns=64;
        [DataMember] public double PreaimRangeMeters=24000;
        // Lab is explicitly armed for a process-local, bounded session; never auto-started.
        [DataMember] public bool LabEnabled;
        [DataMember] public bool LabHeatCurveEnabled, LabBurstEnabled;
        [DataMember] public double LabHeatStartPercent=50, LabHeatSlopePercent=2;
        [DataMember] public double LabHeatUrgentRangeMeters=900, LabHeatUrgentTtiSeconds=.95;
        [DataMember] public int LabBurstInitialRounds=21, LabBurstTopUpRounds=6;
        [DataMember] public double LabBurstMargin=1.5, LabBurstPauseSeconds=.25;
        [DataMember] public double LabBurstUrgentRangeMeters=1200, LabBurstUrgentTtiSeconds=1.25;
        [DataMember] public bool LabNativeOnly = true;
        [DataMember] public bool LabRangeControl;
        [DataMember] public double LabSpreadDegrees = -1;
        [DataMember] public double LabToleranceDegrees = -1;
        [DataMember] public int LabPrediction = -1;
        [DataMember] public bool LabAdvancedProjectileSolver;
        [DataMember] public string LabClosestGunIds = "";
        [DataMember] public int LabDistribution = -1;
        [DataMember] public string LabRequestGunIds = "";
        [DataMember] public double LabLeaseSeconds = .75;
        [DataMember] public double LabRequestIntervalSeconds = .30;
        [DataMember] public double LabNoShotTimeoutSeconds = 1;
        [DataMember] public double LabSwitchAdvantageSeconds = .5;
        [DataMember] public string MenuKey = "PageDown";
        [DataMember] public bool StreamerMode = true;
        [DataMember] public bool HudEnabled = true;
        [DataMember] public bool FollowZeoCoreAppearance = true;
        [DataMember] public bool ControlEnabled = true;
        [DataMember] public bool AutoRepeatTest = true;
        [DataMember] public bool ContinuousTelemetryEnabled = false;
        [DataMember] public int ExpectedInbound = 32;
        [DataMember] public double HitRadiusMeters = 150.0;
        [DataMember] public double ColdHeatPercent = 1.0;
        [DataMember] public double EmergencyTtiSeconds = 0.65;
        [DataMember] public double EmergencyRangeMeters = 600.0;
        [DataMember] public int EmergencyShooters = 4;
        [DataMember] public double TwoShooterTtiSeconds = 1.35;
        [DataMember] public double TwoShooterRangeMeters = 1300.0;
        [DataMember] public double ThreeShooterTtiSeconds = 0.95;
        [DataMember] public double ThreeShooterRangeMeters = 900.0;
        [DataMember] public double EngagementRangeMeters = 3000.0;
        [DataMember] public double HeatThrottleStartPercent = 60.0;
        [DataMember] public double BaseRof = 0.52;
        [DataMember] public double ThreeShooterRof = 0.88;
        [DataMember] public double EmergencyRof = 0.95;
        [DataMember] public double FullEmergencyRof = 1.00;
        [DataMember] public double FullEmergencyTtiSeconds = 0.65;
        [DataMember] public double FullEmergencyRangeMeters = 600.0;
        [DataMember] public double HeatEmergencyOverrideTtiSeconds = 1.00;
        // v0.3.4 COLD-SHIELD scheduler. Stable projectile IDs are authoritative
        // whenever the live CoreSystems endpoint is available. Gun seats are reserved
        // for urgent in-range threats before future/far threats can consume the battery.
        [DataMember] public bool PkWindowEnabled = true;
        // Optional bank policy; off preserves the installed control baseline.
        [DataMember] public bool BankRolesEnabled = false;
        [DataMember] public bool BankRotationEnabled = false;
        [DataMember] public double BankMiddleRangeMeters = 2600;
        [DataMember] public double BankCloseRangeMeters = 2200;
        [DataMember] public double BankMiddleRof = .72;
        [DataMember] public double BankCloseRof = .90;
        [DataMember] public double BankRoleHoldSeconds = 5;
        [DataMember] public double BankSwapHeatMargin = 5;
        [DataMember] public double BankRotateHeatPercent = 65;
        [DataMember] public double BankRestHeatPercent = 75;
        [DataMember] public double BankResumeHeatPercent = 55;
        [DataMember] public int BankThreatsPerOuterGun = 16;
        // Optional next-run experiments. Disabled on migration; preserve all inner controls.
        [DataMember] public bool WeaponAwareEnabled = true;
        [DataMember] public bool NativeLeadEnabled = false;
        [DataMember] public bool PreemptiveLookEnabled = false;
        [DataMember] public bool PreemptiveFireEnabled = false;
        [DataMember] public double PreemptiveRangeMeters = 5000;
        [DataMember] public int PreemptiveMaxGuns = 2;
        [DataMember] public double PreemptiveStopHeatPercent = 35;
        [DataMember] public double PreemptiveResumeHeatPercent = 25;
        [DataMember] public double PreemptiveBurstSeconds = .10;
        [DataMember] public double PreemptiveCooldownSeconds = 1.5;
        [DataMember] public int PreemptiveMaxCallbacksPerBurst = 2;
        [DataMember] public int PreemptiveMaxCallbacksPerTarget = 4;
        [DataMember] public double PreemptiveRof = .50;
        [DataMember] public bool NativeThreatDecisions = false;
        [DataMember] public bool BypassAlignmentGate = true;
        [DataMember] public bool TargetedRequests = false;
        [DataMember] public bool StableIdAuthoritative = true;
        [DataMember] public double StableFallbackGraceSeconds = 0.25;
        [DataMember] public double PkForcedRangeMeters = 1000.0;
        [DataMember] public double PkForcedTtiSeconds = 1.05;
        [DataMember] public double PkFarAngleDeg = 0.65;
        [DataMember] public double PkMidFarAngleDeg = 0.80;
        [DataMember] public double PkMidAngleDeg = 0.95;
        [DataMember] public double PkNearAngleDeg = 1.10;
        [DataMember] public double FarRof = 0.52;
        [DataMember] public double MidFarRof = 0.60;
        [DataMember] public double MidRof = 0.72;
        [DataMember] public double CloseRof = 0.88;
        [DataMember] public double NearRof = 1.00;
        [DataMember] public double UrgentCoverageRangeMeters = 1800.0;
        [DataMember] public double UrgentCoverageTtiSeconds = 1.90;
        [DataMember] public int ShotHelpSecond = 8;
        [DataMember] public int ShotHelpThird = 14;
        [DataMember] public int ShotHelpFourth = 20;
        [DataMember] public double ColdBoostHeatPercent = 35.0;
        [DataMember] public double ColdRofBoost = 0.05;
        [DataMember] public double SurvivalHeatOverrideRangeMeters = 1500.0;
        [DataMember] public double SurvivalHeatOverrideTtiSeconds = 1.60;
        [DataMember] public double ManeuverSupportDegPerSec = 3.0;
        [DataMember] public double ManeuverRangeBoostMeters = 250.0;
        [DataMember] public double ScopeMatchMeters = 140.0;
        [DataMember] public double TrackLostSeconds = 0.30;
        [DataMember] public double BridgeStaleSeconds = 0.90;
        [DataMember] public string BridgeTag = "[ZPDC BRIDGE]";
        [DataMember] public string Frame = "WAR ROOM";
        [DataMember] public string Theme = "WAR ROOM";
        [DataMember] public double HudX = -0.92;
        [DataMember] public double HudY = 0.72;
        [DataMember] public double GlobalScale = 1.0;
        [DataMember] public double PanelScale = 1.0;
        [DataMember] public int BackingOpacity = 178;
        [DataMember] public double BorderWidth = 1.2;

        [DataMember] public string HudVisibilityMode = "IN WORLD";
        [DataMember] public double HudWidth = 1, HudHeight = 1, HudTextScale = 1;
        [DataMember] public bool HudShowArray = true;
        [DataMember] public bool HudShowHeat = true;
        [DataMember] public bool HudShowAverageHeat = true;
        [DataMember] public bool HudShowIntegrity = true;
        [DataMember] public bool HudShowInbound = true;
        [DataMember] public bool HudShowAmmo = true;
        [DataMember] public bool HudShowDecoys = true;
        [DataMember] public bool HudShowManager = true;
        [DataMember] public bool HudShowPreaim = true;
        [DataMember] public bool HudShowFeedAge = true;

        public static PdcConfig Clamp(PdcConfig c)
        {
            if (c == null) c = new PdcConfig();
            c.ClientMode=c.ClientMode=="ADAPTIVE ROF"?"ADAPTIVE ROF":"OBSERVE";
            if(c.HeatRangeVersion<1){c.HeatRangeVersion=1;c.HeatRangeBanksEnabled=true;c.HeatRangeOuterMeters=3000;c.HeatRangeMiddleMeters=2900;c.HeatRangeInnerMeters=2800;c.HeatRangeHoldSeconds=5;c.HeatRangeSwapMargin=5;}
            c.HeatRangeOuterMeters=SafeClamp(c.HeatRangeOuterMeters,500,6000,3000);
            c.HeatRangeMiddleMeters=SafeClamp(c.HeatRangeMiddleMeters,500,c.HeatRangeOuterMeters,Math.Min(2900,c.HeatRangeOuterMeters));
            c.HeatRangeInnerMeters=SafeClamp(c.HeatRangeInnerMeters,500,c.HeatRangeMiddleMeters,Math.Min(2800,c.HeatRangeMiddleMeters));
            c.HeatRangeHoldSeconds=SafeClamp(c.HeatRangeHoldSeconds,3,60,5);
            c.HeatRangeSwapMargin=SafeClamp(c.HeatRangeSwapMargin,2,30,5);
            c.ClientProbeEnabled=false; c.LabEnabled=false; c.PreemptiveFireEnabled=false;

            if(c.ConfigVersion<13) {
                c.LabHeatCurveEnabled=c.LabBurstEnabled=false;
                c.LabHeatStartPercent=50; c.LabHeatSlopePercent=2;
                c.LabHeatUrgentRangeMeters=900; c.LabHeatUrgentTtiSeconds=.95;
                c.LabBurstInitialRounds=21; c.LabBurstTopUpRounds=6;
                c.LabBurstMargin=1.5; c.LabBurstPauseSeconds=.25;
                c.LabBurstUrgentRangeMeters=1200; c.LabBurstUrgentTtiSeconds=1.25;
            }
            c.PreaimMaxGuns=Math.Max(1,Math.Min(64,c.PreaimMaxGuns));
            c.PreaimRangeMeters=SafeClamp(c.PreaimRangeMeters,3000,30000,24000);
            c.LabHeatStartPercent=SafeClamp(c.LabHeatStartPercent,35,80,50);
            c.LabHeatSlopePercent=SafeClamp(c.LabHeatSlopePercent,.25,3,2);
            c.LabHeatUrgentRangeMeters=SafeClamp(c.LabHeatUrgentRangeMeters,900,1800,900);
            c.LabHeatUrgentTtiSeconds=SafeClamp(c.LabHeatUrgentTtiSeconds,.95,2,.95);
            c.LabBurstInitialRounds=Math.Max(4,Math.Min(32,c.LabBurstInitialRounds));
            c.LabBurstTopUpRounds=Math.Max(2,Math.Min(16,c.LabBurstTopUpRounds));
            c.LabBurstMargin=SafeClamp(c.LabBurstMargin,1,3,1.5);
            c.LabBurstPauseSeconds=SafeClamp(c.LabBurstPauseSeconds,.1,.5,.25);
            c.LabBurstUrgentRangeMeters=SafeClamp(c.LabBurstUrgentRangeMeters,1200,2200,1200);
            c.LabBurstUrgentTtiSeconds=SafeClamp(c.LabBurstUrgentTtiSeconds,1.25,3,1.25);
            c.LabSpreadDegrees = SafeClamp(c.LabSpreadDegrees, -1, 2, -1);
            c.LabToleranceDegrees = SafeClamp(c.LabToleranceDegrees, -1, 60, -1);
            if(c.LabToleranceDegrees>=0 && c.LabToleranceDegrees<.05) c.LabToleranceDegrees=.05;
            c.LabPrediction=Math.Max(-1,Math.Min(3,c.LabPrediction));
            c.LabDistribution=Math.Max(-1,Math.Min(1,c.LabDistribution));
            c.LabLeaseSeconds=SafeClamp(c.LabLeaseSeconds,.25,5,.75);
            c.LabRequestIntervalSeconds=SafeClamp(c.LabRequestIntervalSeconds,.2,3,.3);
            c.LabNoShotTimeoutSeconds=SafeClamp(c.LabNoShotTimeoutSeconds,.5,3,1);
            c.LabSwitchAdvantageSeconds=SafeClamp(c.LabSwitchAdvantageSeconds,.1,3,.5);
            c.ExpectedInbound = Math.Max(1, Math.Min(256, c.ExpectedInbound));
            c.HitRadiusMeters = ClampD(c.HitRadiusMeters, 10, 1000);
            c.ColdHeatPercent = ClampD(c.ColdHeatPercent, 0, 20);
            if (c.ConfigVersion < 6)
            {
                c.ConfigVersion = 6;
                c.ControlEnabled = true;
                c.EmergencyTtiSeconds = 0.65;
                c.EmergencyRangeMeters = 600.0;
                c.EmergencyShooters = 4;
                c.TwoShooterTtiSeconds = 1.35;
                c.TwoShooterRangeMeters = 1300.0;
                c.ThreeShooterTtiSeconds = 0.95;
                c.ThreeShooterRangeMeters = 900.0;
                c.EngagementRangeMeters = 3000.0;
                c.HeatThrottleStartPercent = 60.0;
                c.HeatEmergencyOverrideTtiSeconds = 1.00;
                c.BaseRof = 0.52;
                c.ThreeShooterRof = 0.88;
                c.EmergencyRof = 0.95;
                c.FullEmergencyRof = 1.00;
                c.FullEmergencyTtiSeconds = 0.65;
                c.FullEmergencyRangeMeters = 600.0;
                c.PkWindowEnabled = true;
                c.StableIdAuthoritative = true;
                c.StableFallbackGraceSeconds = 0.25;
                c.PkForcedRangeMeters = 1000.0;
                c.PkForcedTtiSeconds = 1.05;
                c.PkFarAngleDeg = 0.65;
                c.PkMidFarAngleDeg = 0.80;
                c.PkMidAngleDeg = 0.95;
                c.PkNearAngleDeg = 1.10;
                c.FarRof = 0.52;
                c.MidFarRof = 0.60;
                c.MidRof = 0.72;
                c.CloseRof = 0.88;
                c.NearRof = 1.00;
                c.UrgentCoverageRangeMeters = 1800.0;
                c.UrgentCoverageTtiSeconds = 1.90;
                c.ShotHelpSecond = 8;
                c.ShotHelpThird = 14;
                c.ShotHelpFourth = 20;
                c.ColdBoostHeatPercent = 35.0;
                c.ColdRofBoost = 0.05;
                c.SurvivalHeatOverrideRangeMeters = 1500.0;
                c.SurvivalHeatOverrideTtiSeconds = 1.60;
                c.ManeuverSupportDegPerSec = 3.0;
                c.ManeuverRangeBoostMeters = 250.0;
            }
            if (c.ConfigVersion < 7)
            {
                c.ConfigVersion = 7;
                c.BypassAlignmentGate = false;
                c.PkWindowEnabled = true;
            c.TargetedRequests = false;
            }
            // Retired single-cycle experiment. Preserve the selected A/B baseline.
            if (c.ConfigVersion < 8) c.ConfigVersion = 8;
            if (c.ConfigVersion < 9)
            {
                c.ConfigVersion = 9; c.BankRolesEnabled = c.BankRotationEnabled = false;
                c.BankMiddleRangeMeters = 2600; c.BankCloseRangeMeters = 2200;
                c.BankMiddleRof = .72; c.BankCloseRof = .90; c.BankRoleHoldSeconds = 5;
                c.BankSwapHeatMargin = 5; c.BankRotateHeatPercent = 65;
                c.BankRestHeatPercent = 75; c.BankResumeHeatPercent = 55; c.BankThreatsPerOuterGun = 16;
            }
            if (c.ConfigVersion < 11)
            {
                // v0.3.17 changes outer control semantics. Require a fresh explicit preset.
                c.PreemptiveLookEnabled = c.PreemptiveFireEnabled = false;
            }
            if (c.ConfigVersion < 10)
            {
                c.ConfigVersion = 10;
                c.NativeLeadEnabled = c.PreemptiveLookEnabled = c.PreemptiveFireEnabled = false;
                c.PreemptiveRangeMeters = 5000; c.PreemptiveMaxGuns = 2;
                c.PreemptiveStopHeatPercent = 35; c.PreemptiveResumeHeatPercent = 25;
                c.PreemptiveBurstSeconds = .10; c.PreemptiveCooldownSeconds = 1.5;
                c.PreemptiveMaxCallbacksPerBurst = 2; c.PreemptiveMaxCallbacksPerTarget = 4;
                c.PreemptiveRof = .50;
            }
            if (c.ConfigVersion < 11) c.ConfigVersion = 11;
            if (c.ConfigVersion < 12) { c.ConfigVersion = 12; c.WeaponAwareEnabled = true; }
            if(c.ConfigVersion<13) c.ConfigVersion=13;
            if(c.ConfigVersion<14) {
                c.DecoyCyclingEnabled=true; c.DecoyCycleSeconds=2;
                c.HeatWarningEnabled=true; c.HeatWarningFlash=true;
                c.HeatWarningPercent=90; c.HeatWarningResetPercent=82; c.HudFontStyle=0;
                c.ConfigVersion=14;
            }
            c.DecoyCycleSeconds=SafeClamp(c.DecoyCycleSeconds, .5, 30, 2);
            c.HeatWarningPercent=SafeClamp(c.HeatWarningPercent,50,100,90);
            c.HeatWarningResetPercent=SafeClamp(c.HeatWarningResetPercent,0,c.HeatWarningPercent-2,82);
            c.HudFontStyle=Math.Max(0,Math.Min(3,c.HudFontStyle));
            c.TargetedRequests = false;
            c.EmergencyTtiSeconds = ClampD(c.EmergencyTtiSeconds, .25, 10);
            c.EmergencyRangeMeters = ClampD(c.EmergencyRangeMeters, 50, 5000);
            c.EmergencyShooters = Math.Max(1, Math.Min(8, c.EmergencyShooters));
            c.TwoShooterTtiSeconds = ClampD(c.TwoShooterTtiSeconds, .5, 6);
            c.TwoShooterRangeMeters = ClampD(c.TwoShooterRangeMeters, 300, 5000);
            c.ThreeShooterTtiSeconds = ClampD(c.ThreeShooterTtiSeconds, .25, 3);
            c.ThreeShooterRangeMeters = ClampD(c.ThreeShooterRangeMeters, 150, 3000);
            c.EngagementRangeMeters = ClampD(c.EngagementRangeMeters, 500, 6000);
            c.PreemptiveRangeMeters = SafeClamp(c.PreemptiveRangeMeters, 500, 6000, 5000);
            c.PreemptiveMaxGuns = Math.Max(1, Math.Min(2, c.PreemptiveMaxGuns));
            c.PreemptiveStopHeatPercent = SafeClamp(c.PreemptiveStopHeatPercent, 15, 50, 35);
            c.PreemptiveResumeHeatPercent = SafeClamp(c.PreemptiveResumeHeatPercent, 5, c.PreemptiveStopHeatPercent - 5, 10);
            c.PreemptiveBurstSeconds = SafeClamp(c.PreemptiveBurstSeconds, .05, .20, .10);
            c.PreemptiveCooldownSeconds = SafeClamp(c.PreemptiveCooldownSeconds, .5, 10, 1.5);
            c.PreemptiveMaxCallbacksPerBurst = Math.Max(1, Math.Min(4, c.PreemptiveMaxCallbacksPerBurst));
            c.PreemptiveMaxCallbacksPerTarget = Math.Max(1, Math.Min(12, c.PreemptiveMaxCallbacksPerTarget));
            c.PreemptiveRof = SafeClamp(c.PreemptiveRof, .50, .60, .50);
            c.BankMiddleRangeMeters = ClampD(c.BankMiddleRangeMeters, 500, c.EngagementRangeMeters);
            c.BankCloseRangeMeters = ClampD(c.BankCloseRangeMeters, 500, c.BankMiddleRangeMeters);
            c.BankMiddleRof = ClampD(c.BankMiddleRof, .50, 1);
            c.BankCloseRof = ClampD(c.BankCloseRof, .50, 1);
            c.BankRoleHoldSeconds = ClampD(c.BankRoleHoldSeconds, 1, 30);
            c.BankSwapHeatMargin = ClampD(c.BankSwapHeatMargin, 2, 30);
            c.BankRotateHeatPercent = ClampD(c.BankRotateHeatPercent, 40, 85);
            c.BankRestHeatPercent = ClampD(c.BankRestHeatPercent, c.BankRotateHeatPercent, 95);
            c.BankResumeHeatPercent = ClampD(c.BankResumeHeatPercent, 10, c.BankRotateHeatPercent - 5);
            c.BankThreatsPerOuterGun = Math.Max(1, Math.Min(64, c.BankThreatsPerOuterGun));
            c.HeatThrottleStartPercent = ClampD(c.HeatThrottleStartPercent, 50, 95);
            c.HeatEmergencyOverrideTtiSeconds = ClampD(c.HeatEmergencyOverrideTtiSeconds, .25, 3);
            c.BaseRof = ClampD(c.BaseRof, .50, 1.00);
            c.ThreeShooterRof = ClampD(c.ThreeShooterRof, c.BaseRof, 1.00);
            c.EmergencyRof = ClampD(c.EmergencyRof, c.ThreeShooterRof, 1.00);
            c.FullEmergencyRof = ClampD(c.FullEmergencyRof, c.EmergencyRof, 1.00);
            c.FullEmergencyTtiSeconds = ClampD(c.FullEmergencyTtiSeconds, .10, 1.00);
            c.FullEmergencyRangeMeters = ClampD(c.FullEmergencyRangeMeters, 100, 800);
            c.StableFallbackGraceSeconds = ClampD(c.StableFallbackGraceSeconds, .10, 1.50);
            c.PkForcedRangeMeters = ClampD(c.PkForcedRangeMeters, 500, 2000);
            c.PkForcedTtiSeconds = ClampD(c.PkForcedTtiSeconds, .50, 2.50);
            c.PkFarAngleDeg = ClampD(c.PkFarAngleDeg, .20, 5.00);
            c.PkMidFarAngleDeg = ClampD(c.PkMidFarAngleDeg, c.PkFarAngleDeg, 6.00);
            c.PkMidAngleDeg = ClampD(c.PkMidAngleDeg, c.PkMidFarAngleDeg, 8.00);
            c.PkNearAngleDeg = ClampD(c.PkNearAngleDeg, c.PkMidAngleDeg, 12.00);
            c.FarRof = ClampD(c.FarRof, .50, .70);
            c.MidFarRof = ClampD(c.MidFarRof, c.FarRof, .75);
            c.MidRof = ClampD(c.MidRof, c.MidFarRof, .85);
            c.CloseRof = ClampD(c.CloseRof, c.MidRof, .95);
            c.NearRof = ClampD(c.NearRof, c.CloseRof, 1.00);
            c.UrgentCoverageRangeMeters = ClampD(c.UrgentCoverageRangeMeters, 900, c.EngagementRangeMeters);
            c.UrgentCoverageTtiSeconds = ClampD(c.UrgentCoverageTtiSeconds, .75, 4.00);
            c.ShotHelpSecond = Math.Max(3, Math.Min(30, c.ShotHelpSecond));
            c.ShotHelpThird = Math.Max(c.ShotHelpSecond + 1, Math.Min(40, c.ShotHelpThird));
            c.ShotHelpFourth = Math.Max(c.ShotHelpThird + 1, Math.Min(60, c.ShotHelpFourth));
            c.ColdBoostHeatPercent = ClampD(c.ColdBoostHeatPercent, 5, 60);
            c.ColdRofBoost = ClampD(c.ColdRofBoost, 0, .15);
            c.SurvivalHeatOverrideRangeMeters = ClampD(c.SurvivalHeatOverrideRangeMeters, 600, 1800);
            c.SurvivalHeatOverrideTtiSeconds = ClampD(c.SurvivalHeatOverrideTtiSeconds, .60, 2.00);
            c.ManeuverSupportDegPerSec = ClampD(c.ManeuverSupportDegPerSec, .5, 20);
            c.ManeuverRangeBoostMeters = ClampD(c.ManeuverRangeBoostMeters, 0, 1000);
            c.ScopeMatchMeters = ClampD(c.ScopeMatchMeters, 10, 1000);
            c.TrackLostSeconds = ClampD(c.TrackLostSeconds, .05, 3.0);
            c.BridgeStaleSeconds = ClampD(c.BridgeStaleSeconds, .25, 5.0);
            if(c.ConfigVersion<15) {
                c.HudWidth=c.HudHeight=c.HudTextScale=1;
                c.HudShowArray=true;
                c.HudShowHeat=true;
                c.HudShowAverageHeat=true;
                c.HudShowIntegrity=true;
                c.HudShowInbound=true;
                c.HudShowAmmo=true;
                c.HudShowDecoys=true;
                c.HudShowManager=true;
                c.HudShowPreaim=true;
                c.HudShowFeedAge=true;
                c.ConfigVersion=15;
            }
            if(c.HudVisibilityMode!="CONTROLLING SHIP")c.HudVisibilityMode="IN WORLD";
            c.HudWidth=SafeClamp(c.HudWidth,.5,3,1); c.HudHeight=SafeClamp(c.HudHeight,.5,3,1); c.HudTextScale=SafeClamp(c.HudTextScale,.75,2,1);
            c.HudX = ClampD(c.HudX, -.98, .98);
            c.HudY = ClampD(c.HudY, -.98, .98);
            c.GlobalScale = ClampD(c.GlobalScale, .50, 2.75);
            c.PanelScale = ClampD(c.PanelScale, .50, 2.50);
            c.BackingOpacity = Math.Max(0, Math.Min(245, c.BackingOpacity));
            c.BorderWidth = ClampD(c.BorderWidth, .25, 4.0);
            if (string.IsNullOrWhiteSpace(c.MenuKey)) c.MenuKey = "PageDown";
            if (string.IsNullOrWhiteSpace(c.BridgeTag)) c.BridgeTag = "[ZPDC BRIDGE]";
            if (string.IsNullOrWhiteSpace(c.Frame)) c.Frame = "WAR ROOM";
            if (string.IsNullOrWhiteSpace(c.Theme)) c.Theme = "WAR ROOM";
            return c;
        }
        private static double SafeClamp(double x, double a, double b, double fallback) { return double.IsNaN(x) || double.IsInfinity(x) ? fallback : ClampD(x,a,b); }
        private static double ClampD(double x, double a, double b) { return x < a ? a : x > b ? b : x; }
    }

    [DataContract]
    public sealed class PdcRow
    {
        [DataMember] public long EntityId;
        [DataMember] public int Part;
        [DataMember] public string Name;
        [DataMember] public double HpPercent;
        [DataMember] public double HeatPercent;
        [DataMember] public int Ammo;
        [DataMember] public bool Functional;
        [DataMember] public bool Allowed;
        [DataMember] public int TrackId;
        [DataMember] public double MatchErrorMeters;
        [DataMember] public double Rof;
        [DataMember] public double KillWindowScore;
        [DataMember] public double AlignmentDeg;
        [DataMember] public string PkState;
        [DataMember] public double RangeMeters;
        [DataMember] public long Shots;
    }

    [DataContract]
    public sealed class TrackRow
    {
        [DataMember] public int TrackId;
        [DataMember] public double RangeMeters;
        [DataMember] public double HullDistanceMeters;
        [DataMember] public double ClosingMps;
        [DataMember] public double TtiSeconds;
        [DataMember] public double MinHullDistanceMeters;
        [DataMember] public string State;
        [DataMember] public int AssignedShooters;
    }

    [DataContract]
    public sealed class PdcSnapshot
    {
        [DataMember] public string PreaimStatus;
        [DataMember] public string RangeBankStatus;
        [DataMember] public string DecoyStatus;
        [DataMember] public int DecoyManaged, DecoyEligible;
        [DataMember] public bool CriticalHeat;
        [DataMember] public double CriticalHeatAgeSeconds;
        [DataMember] public string BankObserver = "OBSERVER waiting";
        [DataMember] public string Version;
        [DataMember] public long SnapshotSeq;
        [DataMember] public long SnapshotUtcTicks;
        [DataMember] public long GameHwnd;
        [DataMember] public int GamePid;
        [DataMember] public int ClientX;
        [DataMember] public int ClientY;
        [DataMember] public int ClientW;
        [DataMember] public int ClientH;
        [DataMember] public bool MenuVisible;
        [DataMember] public int HudClipTopPixels;
        [DataMember] public bool HudVisible;
        [DataMember] public bool WorldLoaded;
        [DataMember] public string Ship;
        [DataMember] public bool CoreReady;
        [DataMember] public int CoreEndpointCount;
        [DataMember] public bool DirectControlActive;
        [DataMember] public string ControlState;
        [DataMember] public bool ShotMonitorReady;
        [DataMember] public bool StableProjectileTracking;
        [DataMember] public int CurrentVolley;
        [DataMember] public int CompletedVolleys;
        [DataMember] public string BridgeState;
        [DataMember] public long BridgeSeq;
        [DataMember] public long BridgeAckSeq;
        [DataMember] public double BridgeAckAgeSeconds;
        [DataMember] public string PbMode;
        [DataMember] public bool PbControlFresh;
        [DataMember] public int PdcCount;
        [DataMember] public int PdcOnline;
        [DataMember] public double AverageHpPercent;
        [DataMember] public double AverageHeatPercent;
        [DataMember] public double HottestHeatPercent;
        [DataMember] public string HottestPdc;
        [DataMember] public int TotalAmmo;
        [DataMember] public long TotalShots;
        [DataMember] public int ActiveInbound;
        [DataMember] public int WaveSeen;
        [DataMember] public int WaveResolved;
        [DataMember] public int Intercepts;
        [DataMember] public int Hits;
        [DataMember] public int Misses;
        [DataMember] public int Unknown;
        [DataMember] public string GoalProgress;
        [DataMember] public string TestState;
        [DataMember] public int WaveId;
        [DataMember] public bool TestArmed;
        [DataMember] public bool FireLockout;
        [DataMember] public string TestFolder;
        [DataMember] public string LastEvent;
        [DataMember] public List<PdcRow> Pdcs = new List<PdcRow>();
        [DataMember] public List<TrackRow> Tracks = new List<TrackRow>();
        [DataMember] public PdcConfig Config;
    }

    [DataContract] public sealed class LabPatch
    {
        [DataMember] public string Key, Value;
    }
    [DataContract]
    public sealed class PdcCommand
    {
        [DataMember] public string RequestId, Session, ShipId;
        [DataMember] public int Revision;
        [DataMember] public LabPatch[] Patch;
        [DataMember] public string Type;
        [DataMember] public string Key;
        [DataMember] public string Text;
        [DataMember] public double Value;
    }

    public static class HudVisibility
    {
        public static bool ConfigAllows(PdcConfig c,bool worldLoaded,bool controllingShip,bool layoutPreview)
        {return c!=null&&worldLoaded&&c.HudEnabled&&(layoutPreview||c.HudVisibilityMode!="CONTROLLING SHIP"||controllingShip);}

        public static bool Fresh(PdcSnapshot s,int ownerPid,double receiptAgeSeconds,long utcTicks)
        {
            if(s==null || ownerPid<=0 || s.GamePid!=ownerPid || s.SnapshotSeq<=0 ||
                double.IsNaN(receiptAgeSeconds)||double.IsInfinity(receiptAgeSeconds)||receiptAgeSeconds<0||receiptAgeSeconds>2) return false;
            double age=(utcTicks-(double)s.SnapshotUtcTicks)/TimeSpan.TicksPerSecond;
            return s.SnapshotUtcTicks>0 && age>=-1 && age<=2;
        }
        public static bool MenuBlocksWorld(string screenName) { return !string.IsNullOrEmpty(screenName) && (screenName.IndexOf("MainMenu",StringComparison.OrdinalIgnoreCase)>=0 || screenName.IndexOf("Loading",StringComparison.OrdinalIgnoreCase)>=0); }
        public static bool Show(PdcSnapshot s,bool foregroundAllowed)
        { return s!=null && s.WorldLoaded && s.HudVisible && foregroundAllowed && s.GameHwnd!=0 && s.ClientW>10 && s.ClientH>10; }
    }

    public struct PdcHudBounds
    {
        public int Left, Top, Width, Height;
        public bool Contains(double x,double y) { return x>=Left && y>=Top && x<=Left+Width && y<=Top+Height; }
    }
    public static class PdcHudGeometry
    {
        public const double BaseWidth=460;
        public static string[] Rows(PdcConfig c)
        {
            var rows=new List<string>();
            if(c.HudShowArray)rows.Add("Array");if(c.HudShowHeat)rows.Add("Heat");
            if(c.HudShowAverageHeat)rows.Add("AverageHeat");if(c.HudShowIntegrity)rows.Add("Integrity");
            if(c.HudShowInbound)rows.Add("Inbound");if(c.HudShowAmmo)rows.Add("Ammo");if(c.HudShowDecoys)rows.Add("Decoys");
            if(c.HudShowManager)rows.Add("Manager");if(c.HudShowPreaim)rows.Add("Preaim");
            if(c.HeatWarningEnabled)rows.Add("Warning");if(c.HudShowFeedAge)rows.Add("FeedAge");return rows.ToArray();
        }
        public static double TextSize(PdcConfig c) {return Safe(c.HudTextScale,.75,2);}
        public static double RowHeight(string row,PdcConfig c) {return (row=="Heat"?48:row=="Warning"?32:26)*TextSize(c);}
        public static double BaseHeight(PdcConfig c) {double h=56;foreach(var row in Rows(c))h+=RowHeight(row,c);return h+12;}
        public static double Safe(double x,double min,double max) {return double.IsNaN(x)||double.IsInfinity(x)||x<=0?1:Math.Max(min,Math.Min(max,x));}
        public static PdcHudBounds Bounds(int width,int height,PdcConfig cfg)
        {
            width=Math.Max(1,width);height=Math.Max(1,height);
            double scale=Safe(cfg.PanelScale*cfg.GlobalScale,.25,6.875);
            double w=BaseWidth*scale*Safe(cfg.HudWidth,.5,3),h=BaseHeight(cfg)*scale*Safe(cfg.HudHeight,.5,3);
            double fit=Math.Min(1,Math.Min(Math.Max(1,width-24)/w,Math.Max(1,height-24)/h));
            int iw=Math.Max(1,(int)Math.Round(w*fit)),ih=Math.Max(1,(int)Math.Round(h*fit));
            return new PdcHudBounds{Width=iw,Height=ih,Left=(int)Math.Round((cfg.HudX+1)*.5*(width-iw)),Top=(int)Math.Round((1-cfg.HudY)*.5*(height-ih))};
        }
        // Core-style independent axes; start geometry and scales are frozen at mouse-down.
        public static void ResizeAxes(PdcHudBounds start,int edges,double dx,double dy,int width,int height,PdcConfig cfg,out double x,out double y,out double sx,out double sy)
        {
            x=cfg.HudX;y=cfg.HudY;sx=Safe(cfg.HudWidth,.5,3);sy=Safe(cfg.HudHeight,.5,3);
            if(edges==0||width<200||height<200||start.Width<1||start.Height<1||double.IsNaN(dx)||double.IsNaN(dy)||double.IsInfinity(dx)||double.IsInfinity(dy))return;
            double aw=(edges&1)!=0?start.Left+start.Width-Math.Max(10,width*.01):width-10-start.Left;
            double ah=(edges&4)!=0?start.Top+start.Height-Math.Max(10,height*.01):height-10-start.Top;
            double nw=start.Width,nh=start.Height;
            if((edges&3)!=0)nw=Math.Max(start.Width*.5/sx,Math.Min(Math.Min(start.Width*3/sx,aw),start.Width+((edges&1)!=0?-dx:dx)));
            if((edges&12)!=0)nh=Math.Max(start.Height*.5/sy,Math.Min(Math.Min(start.Height*3/sy,ah),start.Height+((edges&4)!=0?-dy:dy)));
            sx=Safe(sx*nw/start.Width,.5,3);sy=Safe(sy*nh/start.Height,.5,3);
            var draft=JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(cfg));draft.HudWidth=sx;draft.HudHeight=sy;
            var actual=Bounds(width,height,draft);
            Position((edges&1)!=0?start.Left+start.Width-actual.Width:start.Left,(edges&4)!=0?start.Top+start.Height-actual.Height:start.Top,width,height,draft,out x,out y);
        }
        public static int ResizeEdges(PdcHudBounds b,double x,double y)
        {
            const double grip=10;
            if(x<b.Left-grip||x>b.Left+b.Width+grip||y<b.Top-grip||y>b.Top+b.Height+grip) return 0;
            int edges=0;
            if(Math.Abs(x-b.Left)<=grip) edges|=1; else if(Math.Abs(x-b.Left-b.Width)<=grip) edges|=2;
            if(Math.Abs(y-b.Top)<=grip) edges|=4; else if(Math.Abs(y-b.Top-b.Height)<=grip) edges|=8;
            return edges;
        }
        // Frozen mouse-down geometry avoids compounding asynchronous preview updates.
        public static void Resize(PdcHudBounds start,int edges,double dx,double dy,int width,int height,PdcConfig cfg,out double x,out double y,out double size)
        {
            x=cfg.HudX;y=cfg.HudY;size=cfg.PanelScale;
            if(edges==0||start.Width<1||start.Height<1||double.IsNaN(dx)||double.IsNaN(dy)||double.IsInfinity(dx)||double.IsInfinity(dy))return;
            double dw=(edges&1)!=0?-dx:dx, dh=(edges&4)!=0?-dy:dy;
            bool horizontal=(edges&3)!=0,vertical=(edges&12)!=0;
            double ratio=horizontal&&vertical?1+(dw*start.Width+dh*start.Height)/((double)start.Width*start.Width+(double)start.Height*start.Height):1+(horizontal?dw/start.Width:dh/start.Height);
            double global=cfg.GlobalScale;
            if(double.IsNaN(global)||double.IsInfinity(global)||global<=0)global=1;
            // Derive from rendered size, including viewport fitting, to prevent a jump on small screens.
            double renderedSize=start.Width/BaseWidth/global;
            size=Math.Max(.5,Math.Min(2.5,renderedSize*ratio));
            double fit=Math.Min(width/BaseWidth,height/BaseHeight(cfg))/global;
            size=Math.Max(.5,Math.Min(size,fit));
            var draft=JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(cfg)); draft.PanelScale=size;draft.GlobalScale=global;
            var resized=Bounds(width,height,draft);
            double left=(edges&1)!=0?start.Left+start.Width-resized.Width:start.Left;
            double top=(edges&4)!=0?start.Top+start.Height-resized.Height:start.Top;
            Position(left,top,width,height,draft,out x,out y);
        }
        public static void Position(double left,double top,int width,int height,PdcConfig cfg,out double x,out double y)
        {
            var bounds=Bounds(width,height,cfg);
            x=width<=bounds.Width?0:Math.Max(-.98,Math.Min(.98,2*left/(width-bounds.Width)-1));
            y=height<=bounds.Height?0:Math.Max(-.98,Math.Min(.98,1-2*top/(height-bounds.Height)));
        }
    }

    internal static class JsonIo
    {
        public static byte[] ToBytes<T>(T obj)
        {
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(ms, obj);
                return ms.ToArray();
            }
        }
        public static T FromBytes<T>(byte[] bytes) where T : class
        {
            try
            {
                using (var ms = new MemoryStream(bytes))
                    return new DataContractJsonSerializer(typeof(T)).ReadObject(ms) as T;
            }
            catch { return null; }
        }
        public static T Load<T>(string path) where T : class
        {
            try { return File.Exists(path) ? FromBytes<T>(File.ReadAllBytes(path)) : null; }
            catch { return null; }
        }
        public static void Save<T>(string path, T obj)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, ToBytes(obj));
        }
    }
}
