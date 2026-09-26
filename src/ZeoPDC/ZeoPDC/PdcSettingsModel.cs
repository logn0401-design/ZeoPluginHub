using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using VRage.Input;

namespace ZeoPDC
{
    internal sealed class PdcOption
    {
        public string Key, Label, Page;
        public string[] Choices;
        public FieldInfo Field;
        public bool Appearance, ObservationOnly;
        public bool Number { get { return Field.FieldType == typeof(double) || Field.FieldType == typeof(int); } }
        public string Format(PdcConfig cfg) { return Convert.ToString(Field.GetValue(cfg), CultureInfo.InvariantCulture); }
        public double Limit(PdcConfig cfg, bool upper)
        {
            var copy = JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(cfg));
            if (Field.FieldType == typeof(int)) Field.SetValue(copy, upper ? int.MaxValue : int.MinValue);
            else Field.SetValue(copy, upper ? 1e12 : -1e12);
            PdcConfig.Clamp(copy); return Convert.ToDouble(Field.GetValue(copy));
        }
    }

    internal static class PdcSettingsCatalog
    {
        public static readonly string[] Pages = { "OVERVIEW", "SETTINGS", "DEFENSE", "HUD" };
        public static readonly string[] AppearanceKeys = { "MenuKey", "StreamerMode", "HudEnabled", "FollowZeoCoreAppearance", "Frame", "Theme", "HudX", "HudY", "GlobalScale", "PanelScale", "BackingOpacity", "BorderWidth", "HudFontStyle", "HeatWarningEnabled", "HeatWarningFlash", "HeatWarningPercent", "HeatWarningResetPercent" };
        public static readonly string[] Frames = { "SE INDUSTRIAL", "FIGHTER HUD", "MARS TACTICAL", "BELTER UTILITY", "NAVY GLASS", "STEALTH", "WAR ROOM", "COMMAND GRID", "REDLINE", "BLACKSITE", "CHEVRON", "SPLIT WING", "HEX COMMAND", "RAZOR", "LEGACY GLASS", "KEEN SIGNAL", "WEAPON CORE" };
        static readonly string[] ProductionKeys="ControlEnabled ManagedDefenseEnabled ClientMode HeatRangeBanksEnabled HeatRangeOuterMeters HeatRangeMiddleMeters HeatRangeInnerMeters HeatRangeHoldSeconds HeatRangeSwapMargin FarRof MidFarRof MidRof CloseRof NearRof FullEmergencyRof HeatThrottleStartPercent ColdBoostHeatPercent ColdRofBoost SurvivalHeatOverrideRangeMeters SurvivalHeatOverrideTtiSeconds FullEmergencyRangeMeters FullEmergencyTtiSeconds PreemptiveLookEnabled PreaimRangeMeters PreaimMaxGuns DecoyCyclingEnabled DecoyCycleSeconds".Split(' ');
        public static readonly PdcOption[] Options = Build();
        private static PdcOption[] Build()
        {
            var options = new List<PdcOption>();
            foreach (var f in typeof(PdcConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.Name == "ConfigVersion" || f.Name == "ManagerPresetVersion" || f.Name == "TargetedRequests") continue;
                bool appearance = AppearanceKeys.Contains(f.Name) || f.Name.StartsWith("HudShow") || new[]{"HudWidth","HudHeight","HudTextScale","HudVisibilityMode"}.Contains(f.Name);
                if(!appearance && !ProductionKeys.Contains(f.Name))continue;
                string page = appearance ? "HUD" : "DEFENSE";
                if (new[] { "ControlEnabled", "BypassAlignmentGate", "PkWindowEnabled", "EngagementRangeMeters", "StableIdAuthoritative", "MenuKey" }.Contains(f.Name)) page = "SETTINGS";
                if (new[] { "AutoRepeatTest", "ExpectedInbound", "HitRadiusMeters", "ColdHeatPercent", "ContinuousTelemetryEnabled" }.Contains(f.Name)) page = "RECORDING";
                if (f.Name.StartsWith("Bridge")) page = "DIAGNOSTICS";
                string label = Regex.Replace(f.Name, "([a-z0-9])([A-Z])", "$1 $2").Replace("Meters", "(m)").Replace("Seconds", "(s)").Replace("Percent", "(%)");
                if (f.Name == "BypassAlignmentGate") label = "Angle gate bypass (B setup)";
                if (f.Name == "ControlEnabled") label = "PDC defense enabled";
                if (f.Name == "PreemptiveLookEnabled") label = "Idle-gun preaim";
                if (f.Name == "ManagedDefenseEnabled") label = "Defense manager (capabilities vary by host)";
                if(f.Name=="ClientMode"){page="SETTINGS";label="Joined-server mode";}
                if(f.Name=="ClientProbeEnabled"){page="DIAGNOSTICS";label="Run one idle-gun capability test (90 s; toggle off to stop)";}
                if (f.Name == "PreaimRangeMeters") label = "Early preaim look range (m)";
                if (f.Name == "PreaimMaxGuns") label = "Maximum idle guns to preaim";
                if (f.Name == "PreemptiveFireEnabled") label = "Zeo outer small bursts";
                if (f.Name == "PreemptiveRangeMeters") label = "Outer look horizon (m)";
                if (f.Name == "PreemptiveBurstSeconds") label = "Outer cycle timeout after startup (s)";
                if (f.Name == "HitRadiusMeters") label = "Danger reporting threshold (m)";
                if (f.Name == "ExpectedInbound") label = "Expected torpedoes per volley";
                if (f.Name == "ContinuousTelemetryEnabled") label = "Continuous live telemetry (read only)";
                if (f.Name == "AutoRepeatTest") label = "Auto repeat (legacy setting)";
                if(f.Name.StartsWith("Decoy")) { page="DEFENSE"; label=f.Name=="DecoyCyclingEnabled"?"Decoy subsystem cycling (experimental)":"Decoy category dwell (seconds)"; }
                if(f.Name=="HeatWarningPercent") label="Critical heat warning threshold (%)";
                if(f.Name=="HeatWarningResetPercent") label="Clear heat warning below (%)";
                if(f.Name=="HeatWarningFlash") label="Brief critical warning pulse";
                if(f.Name=="HudVisibilityMode")label="Show HUD when";
                if(f.Name=="HudTextScale") label="Text size (independent of panel size)";
                if(f.Name=="HudWidth")label="Panel width";if(f.Name=="HudHeight")label="Panel height";
                if(f.Name=="PanelScale")label="Overall panel size";if(f.Name=="GlobalScale")label="Legacy global size multiplier";
                if(f.Name.StartsWith("HudShow"))label="Show "+Regex.Replace(f.Name.Substring(7),"([a-z])([A-Z])","$1 $2");
                if(f.Name=="HudFontStyle") label="HUD font: 0 auto / 1 condensed / 2 mono / 3 UI";
                if(f.Name.StartsWith("HeatRange"))label=f.Name=="HeatRangeBanksEnabled"?"Heat-balanced range banks":f.Name=="HeatRangeOuterMeters"?"Outer range (m)":f.Name=="HeatRangeMiddleMeters"?"Middle range (m)":f.Name=="HeatRangeInnerMeters"?"Inner range (m)":f.Name=="HeatRangeHoldSeconds"?"Minimum time between rotations (s)":"Heat difference before rotation (%)";
                options.Add(new PdcOption { Key = f.Name, Field = f, Label = label, Page = page, Appearance = appearance, ObservationOnly = f.Name == "ContinuousTelemetryEnabled",
                    Choices = f.Name=="ClientMode"?new[]{"OBSERVE","ADAPTIVE ROF"}:f.Name=="HudVisibilityMode"?new[]{"IN WORLD","CONTROLLING SHIP"}:f.Name == "Frame" ? Frames : f.Name == "Theme" ? new[] { "WAR ROOM", "GRAPHITE", "MONOCHROME", "AMBER", "HIGH CONTRAST", "CUSTOM" } : null });
            }
            return options.ToArray();
        }
        public static string Section(PdcOption o)
        {
            string k=o.Key;
            if(o.Page=="HUD") {
                if(k.StartsWith("HudShow"))return "Displayed data";
                if(k.StartsWith("HeatWarning"))return "Heat warning";
                if(new[]{"Frame","Theme","FollowZeoCoreAppearance","BackingOpacity","BorderWidth","HudFontStyle","StreamerMode"}.Contains(k))return "Appearance";
                return "Layout & text";
            }
            if(k.StartsWith("Client"))return "Operation";
            if(k.StartsWith("HeatRange"))return "Range banks";
            if(k=="MenuKey")return "Controls & keys";
            if(k.StartsWith("Lab"))return "Advanced lab (read only)";
            if(k.StartsWith("Decoy"))return "Decoys";
            if(k.StartsWith("Preaim")||k.StartsWith("Preemptive"))return "Preaim & outer fire";
            if(k.Contains("Rof")||k.Contains("Heat")||k.StartsWith("Survival"))return "ROF & heat";
            if(k.StartsWith("Bank"))return "Bank experiments";
            return "General";
        }
        public static PdcOption Find(string key) { return Options.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase)); }
    }

    internal sealed class PdcSettingsModel
    {
        readonly string path;
        readonly Func<PdcConfig> fallback;
        readonly Func<bool> recording;
        readonly Action<PdcConfig> changed;
        public PdcConfig Current { get; private set; }
        public PdcConfig LastRequested { get; private set; }
        public bool IsRecording { get { return recording(); } }
        public bool IsLocked(PdcOption option) { return option.Key.StartsWith("Lab") || ((fallback().LabEnabled || IsRecording) && !option.Appearance && !option.ObservationOnly); }
        public PdcSettingsModel(string path, Func<PdcConfig> fallback, Func<bool> recording, Action<PdcConfig> changed)
        { this.path = path; this.fallback = fallback; this.recording = recording; this.changed = changed; Reload(); }
        public void Reload()
        {
            var read = File.Exists(path) ? JsonIo.Load<PdcConfig>(path) : fallback();
            if (read == null) throw new IOException("Cannot read current settings; no changes saved.");
            Current = PdcConfig.Clamp(JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(read)));
        }
        public void Apply(string key, string input)
        {
            var option = PdcSettingsCatalog.Find(key);
            if (option == null) throw new ArgumentException("This setting is not editable.");
            if (IsLocked(option)) throw new InvalidOperationException("Finish or stop recording before changing this setting. You can still close or change pages.");
            Reload();
            object value;
            if (option.Number)
            {
                double number;
                if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out number) || !BankPlanner.Finite(number)) throw new ArgumentException("Enter a finite number using a decimal point.");
                double min = option.Limit(Current, false), max = option.Limit(Current, true);
                if (number < min || number > max) throw new ArgumentException("Range is " + min.ToString(CultureInfo.InvariantCulture) + " to " + max.ToString(CultureInfo.InvariantCulture) + ".");
                if (option.Field.FieldType == typeof(int))
                { if (number != Math.Truncate(number)) throw new ArgumentException("Enter a whole number."); value = (int)number; }
                else value = number;
            }
            else if (option.Field.FieldType == typeof(bool))
            { bool selected; if (!bool.TryParse(input, out selected)) throw new ArgumentException("Choose ON or OFF."); value = selected; }
            else
            {
                input = (input ?? "").Trim();
                if (input.Length == 0 || input.Length > 100) throw new ArgumentException("Enter between 1 and 100 characters.");
                if (option.Key == "MenuKey")
                { MyKeys parsed; if (!input.Equals("None", StringComparison.OrdinalIgnoreCase) && (!Enum.TryParse(input.Replace(" ", ""), true, out parsed) || !Enum.IsDefined(typeof(MyKeys), parsed))) throw new ArgumentException("Use a valid game key, such as PageDown, or None."); }
                if (option.Choices != null && !option.Choices.Contains(input)) throw new ArgumentException("Choose a listed value.");
                value = input;
            }
            var next = JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(Current));
            option.Field.SetValue(next, value);
            if (option.Key == "BypassAlignmentGate") { next.PkWindowEnabled = true; next.TargetedRequests = false; }
            LastRequested = JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(next));
            PdcConfig.Clamp(next);
            if (!Equals(option.Field.GetValue(next), value)) throw new ArgumentException("Value conflicts with related settings.");
            Save(next); Current = next; changed(next);
        }
        public void ApplyLatestSetup()
        {
            if (recording()) throw new InvalidOperationException("Finish or stop recording first.");
            ApplyDefensePreset("PROVEN_MANAGER");
        }

        public void ApplyDefensePreset(string name)
        {
            if (recording()) throw new InvalidOperationException("Finish or stop recording first.");
            name = (name ?? "").Trim().ToUpperInvariant();
            if(name=="PROVEN_MANAGER") { Reload(); var proven=ManagerSettings.Proven(Current); LastRequested=proven; Save(proven); Current=proven; changed(proven); return; }
            if (!new[] { "BASELINE", "NATIVE_LEAD", "PREAIM", "OUTER_BURSTS" }.Contains(name))
                throw new ArgumentException("Unknown defense preset.");
            Reload(); var next = JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(Current));
            next.ManagedDefenseEnabled=false; next.NativeThreatDecisions = false;
            next.NativeLeadEnabled = name != "BASELINE";
            next.PreemptiveLookEnabled = name == "PREAIM" || name == "OUTER_BURSTS";
            next.PreemptiveFireEnabled = name == "OUTER_BURSTS";
            next.PreemptiveRangeMeters = 5000; next.PreemptiveMaxGuns = 2;
            next.PreemptiveStopHeatPercent = 35; next.PreemptiveResumeHeatPercent = 25;
            next.PreemptiveBurstSeconds = .10; next.PreemptiveCooldownSeconds = 1.5;
            next.PreemptiveMaxCallbacksPerBurst = 2; next.PreemptiveMaxCallbacksPerTarget = 4;
            next.PreemptiveRof = .50;
            LastRequested = JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(next));
            Save(PdcConfig.Clamp(next)); Current = next; changed(next);
        }
        public void SaveKeyBinding(string field,string key,int modifiers)
        {
            if(field!="MenuKey") throw new ArgumentException("Unknown binding.");
            MyKeys parsed;
            if(key==null || (!key.Equals("None",StringComparison.OrdinalIgnoreCase) && (!Enum.TryParse(key,true,out parsed)||!Enum.IsDefined(typeof(MyKeys),parsed)))) throw new ArgumentException("Invalid key.");
            if(modifiers<0||modifiers>7)throw new ArgumentException("Invalid modifiers.");
            Reload();var next=JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(Current));
            next.MenuKey=key;
            LastRequested=JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(next));Save(next);Current=next;changed(next);
        }
        public void SaveLayoutAxes(double x,double y,double width,double height,bool moved,bool resized)
        {
            if(!BankPlanner.Finite(x)||!BankPlanner.Finite(y)||!BankPlanner.Finite(width)||!BankPlanner.Finite(height))throw new ArgumentException("Invalid layout.");
            if(!moved&&!resized)return;
            Reload();var next=JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(Current));
            if(moved){next.HudX=x;next.HudY=y;}if(resized){next.HudWidth=width;next.HudHeight=height;}
            LastRequested=JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(next));
            if(moved){next.HudX=Math.Max(-.98,Math.Min(.98,x));next.HudY=Math.Max(-.98,Math.Min(.98,y));}
            if(resized){next.HudWidth=Math.Max(.5,Math.Min(3,width));next.HudHeight=Math.Max(.5,Math.Min(3,height));}
            Save(next);Current=next;changed(next);
        }
        public void SavePosition(double x, double y) { SaveLayout(x,y,1,true,false); }
        public void SaveLayout(double x, double y, double scale, bool positionChanged, bool sizeChanged)
        {
            if (!BankPlanner.Finite(x) || !BankPlanner.Finite(y) || !BankPlanner.Finite(scale)) throw new ArgumentException("Invalid HUD layout.");
            if (!positionChanged && !sizeChanged) return;
            Reload(); var next = JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(Current));
            if(positionChanged) { next.HudX=x; next.HudY=y; }
            if(sizeChanged) next.PanelScale=scale;
            LastRequested=JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(next));
            if(positionChanged) { next.HudX=Math.Max(-.98,Math.Min(.98,x)); next.HudY=Math.Max(-.98,Math.Min(.98,y)); }
            if(sizeChanged) next.PanelScale=Math.Max(.5,Math.Min(2.5,scale));
            Save(next); Current=next; changed(next);
        }
        private void Save(PdcConfig value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".native-write.tmp";
            byte[] bytes = JsonIo.ToBytes(value);
            try
            {
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(path)) File.Replace(temp, path, null, true); else File.Move(temp, path);
                if (!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new IOException("Saved settings verification failed.");
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
