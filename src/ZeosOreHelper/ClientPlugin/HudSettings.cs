using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using VRageMath;

namespace ZeosOreHelper
{
    public sealed class HudSettings
    {
        public static readonly string[] KnownOres =
        {
            "Uranium","Tungsten","Titanium","Platinum","Gold","Lead","Copper",
            "Silver","Cobalt","Magnesium","Nickel","Iron","Silicon","Boron","Organic","Ice"
        };

        // Master/runtime
        public bool Enabled = false;
        public bool StreamerMode = false;
        public bool StreamerFailClosed = true;
        public bool AutoStartOverlay = true;
        public string MenuKey = "PageUp";
        public string ActivePreset = "Balanced";

        // Survey/filter
        public bool MaxLoadedRange = false;
        public double MinimumDistanceMeters = 0.0;
        public double SurveyRangeMeters = 45000.0; // maximum distance when MaxLoadedRange=false
        public double MinimumDiameterMeters = 0.0;
        public double MaximumDiameterMeters = 0.0; // 0 = ANY
        public bool WantedOnly = false;
        public bool ShowNoOre = false;
        public bool RequireAllWantedOres = false;
        public string MinimumGrade = "D";
        public double MinimumWantedOrePercent = 0.0;

        // Ranking
        public string RankingMode = "quality"; // quality | quality_distance | nearest
        public bool UseSizeFactor = true;
        public string WorthTripGrade = "A";
        public double GradeS = 60.0;
        public double GradeA = 35.0;
        public double GradeB = 20.0;
        public double GradeC = 8.0;
        public double PureIceMustPercent = 100.0;

        // Survey engine advanced
        public int DiscoverEveryFrames = 60;
        public int ScanEveryFrames = 20;
        public int RescanAfterFrames = 36000;
        public int PreferredLod = 3;
        public int MaxSampleCells = 300000;

        // HUD list
        public bool ListEnabled = true;
        public Vector2D PanelOrigin = new Vector2D(-0.72, -0.64);
        public float PanelScale = 1.00f;
        public float TextScale = 1.00f;
        public float PanelWidthScale = 1.00f;
        public float RowScale = 1.00f;
        public int ListRows = 8;
        public string HudLayout = "Prospector";
        public string FrameStyle = "Hex Command";
        public int PanelOpacity = 190;
        public int FrameOpacity = 235;
        public int TextOpacity = 245;
        public bool ShowHeader = true;
        public bool ShowStatusBar = true;
        public bool ShowColumnHeader = true;
        public bool ShowColNumber = true;
        public bool ShowColPin = true;
        public bool ShowColGrade = true;
        public bool ShowColDistance = true;
        public bool ShowColDiameter = true;
        public bool ShowColTopOre = true;
        public bool ShowColOrePercent = true;
        public bool ShowColSecondOre = true;
        public bool ShowColQuality = false;
        public bool ShowColStatus = false;

        // Pings/world markers
        public bool MarkersEnabled = true;
        public int MaxMarkers = 30;
        public float MarkerScale = 1.00f;
        public float SelectedMarkerScale = 1.25f;
        public float OffscreenMarkerScale = 1.00f;
        public string PingSizeMode = "Distance + Grade"; // Fixed | Distance | Grade | Distance + Grade
        public string PingColorMode = "Grade"; // Grade | Top Ore | Theme | Custom
        public string PingCustomColor = "#E7ECF2";
        public bool PingShowNumber = true;
        public bool PingShowGrade = true;
        public bool PingShowDistance = true;
        public bool PingShowDiameter = false;
        public bool PingShowTopOre = true;
        public bool PingShowOrePercent = true;
        public bool PingOffscreenArrows = true;
        public bool PinnedAlwaysVisible = true;

        // HUD theme/colors
        public string ThemePreset = "Zeo War Room";
        public string HudBackgroundColor = "#090607";
        public string HudPanelColor = "#120D0E";
        public string HudAccentColor = "#D83B3B";
        public string HudTextColor = "#E8EAED";
        public string HudDimColor = "#8C9299";
        public string HudBorderColor = "#5A2024";
        public string SelectedColor = "#FFE16B";
        public string ScanningColor = "#69D6EE";
        public string ErrorColor = "#EB5A5A";
        public string GradeSColor = "#4BFF73";
        public string GradeAColor = "#73EB87";
        public string GradeBColor = "#F5DA50";
        public string GradeCColor = "#F59B46";
        public string GradeDColor = "#A5AAAF";
        public string GradeXColor = "#965555";

        // LCD output. Disabled by default; StreamerMode forcibly suppresses it.
        public bool LcdEnabled = false;
        public string LcdTag = "Zeo Ore LCD";
        public string LcdRole = "Ranking"; // Ranking | Target | Status | Ore Summary
        public int LcdRows = 10;
        public float LcdFontScale = 0.75f;
        public string LcdTheme = "Zeo War Room";
        public bool LcdLinkHudTheme = true;
        public string LcdBackgroundColor = "#090607";
        public string LcdTextColor = "#E8EAED";
        public string LcdAccentColor = "#D83B3B";

        // Cache
        public bool CacheEnabled = true;
        public string CacheMinimumGrade = "A";
        public int CachePerSector = 30;
        public int CacheRetentionDays = 14;
        public int CacheMaxMb = 5;

        private readonly Dictionary<string, int> _oreWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _oreEnabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _oreMinimumPercent = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _oreShowList = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _oreShowPing = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _oreColor = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string,string> _baseline;
        private DateTime _lastWriteUtc = DateTime.MinValue;

        public HudSettings() { ApplyBalancedDefaults(); ApplyDefaultOreColors(); }

        public static string SettingsDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulsar", "ZeosOreHelper"); }
        }
        public static string SettingsPath { get { return Path.Combine(SettingsDirectory, "settings.ini"); } }
        public static string LegacySettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulsar", "ToxicsOreScanner", "settings.ini"); }
        }
        public double EffectiveSurveyRangeMeters { get { return MaxLoadedRange ? 1000000000.0 : SurveyRangeMeters; } }

        public static HudSettings Load()
        {
            var s = new HudSettings();
            try
            {
                string source = SettingsPath;
                bool migrated = false;
                if (!File.Exists(source) && File.Exists(LegacySettingsPath)) { source = LegacySettingsPath; migrated = true; }
                if (File.Exists(source)) s.ReadFrom(source);
                if (migrated) { s.Save(); Plugin.Log("Migrated legacy Ore Helper settings into v0.6 schema."); }
            }
            catch (Exception ex) { Plugin.Log("Settings load failed: " + ex.Message); }
            return s;
        }

        public bool ReloadIfChanged()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return false;
                var stamp = File.GetLastWriteTimeUtc(SettingsPath);
                if (stamp <= _lastWriteUtc) return false;
                ReadFrom(SettingsPath);
                return true;
            }
            catch (Exception ex) { Plugin.Log("Settings reload failed: " + ex.Message); return false; }
        }

        private void ReadFrom(string path)
        {
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                line = line.Trim();
                if (line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string kl = k.ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                double d; float f; bool b; int n;

                if (kl == "enabled" && bool.TryParse(v, out b)) Enabled = b;
                else if (kl == "streamermode" && bool.TryParse(v, out b)) StreamerMode = b;
                else if (kl == "streamerfailclosed" && bool.TryParse(v, out b)) StreamerFailClosed = b;
                else if (kl == "autostartoverlay" && bool.TryParse(v, out b)) AutoStartOverlay = b;
                else if (kl == "menukey") MenuKey = NormalizeMenuKey(v);
                else if (kl == "activepreset") ActivePreset = v;
                else if (kl == "maxloadedrange" && bool.TryParse(v, out b)) MaxLoadedRange = b;
                else if (kl == "minimumdistancemeters" && TryDouble(v, out d)) MinimumDistanceMeters = Clamp(d, 0, 1000000);
                else if ((kl == "surveyrangemeters" || kl == "maximumdistancemeters") && TryDouble(v, out d)) SurveyRangeMeters = Clamp(d, 1000, 1000000);
                else if (kl == "minimumdiametermeters" && TryDouble(v, out d)) MinimumDiameterMeters = Clamp(d, 0, 10000);
                else if (kl == "maximumdiametermeters" && TryDouble(v, out d)) MaximumDiameterMeters = Clamp(d, 0, 10000);
                else if (kl == "wantedonly" && bool.TryParse(v, out b)) WantedOnly = b;
                else if (kl == "shownoore" && bool.TryParse(v, out b)) ShowNoOre = b;
                else if (kl == "requireallwantedores" && bool.TryParse(v, out b)) RequireAllWantedOres = b;
                else if (kl == "minimumgrade") MinimumGrade = NormalizeGrade(v, "D");
                else if (kl == "minimumwantedorepercent" && TryDouble(v, out d)) MinimumWantedOrePercent = Clamp(d, 0, 100);
                else if (kl == "rankingmode") RankingMode = NormalizeRankingMode(v);
                else if (kl == "usesizefactor" && bool.TryParse(v, out b)) UseSizeFactor = b;
                else if (kl == "worthtripgrade") WorthTripGrade = NormalizeGrade(v, "A");
                else if (kl == "grades" && TryDouble(v, out d)) GradeS = Clamp(d, 1, 250);
                else if (kl == "gradea" && TryDouble(v, out d)) GradeA = Clamp(d, 0, 250);
                else if (kl == "gradeb" && TryDouble(v, out d)) GradeB = Clamp(d, 0, 250);
                else if (kl == "gradec" && TryDouble(v, out d)) GradeC = Clamp(d, 0, 250);
                else if (kl == "pureicemustpercent" && TryDouble(v, out d)) PureIceMustPercent = Clamp(d, 0, 100);
                else if (kl == "discovereveryframes" && int.TryParse(v, out n)) DiscoverEveryFrames = Clamp(n, 10, 600);
                else if (kl == "scaneveryframes" && int.TryParse(v, out n)) ScanEveryFrames = Clamp(n, 5, 300);
                else if (kl == "rescanafterframes" && int.TryParse(v, out n)) RescanAfterFrames = Clamp(n, 600, 216000);
                else if (kl == "preferredlod" && int.TryParse(v, out n)) PreferredLod = Clamp(n, 0, 6);
                else if (kl == "maxsamplecells" && int.TryParse(v, out n)) MaxSampleCells = Clamp(n, 25000, 1000000);
                else if (kl == "listenabled" && bool.TryParse(v, out b)) ListEnabled = b;
                else if (kl == "panelx" && TryDouble(v, out d)) PanelOrigin.X = Clamp(d, -1.0, 1.0);
                else if (kl == "panely" && TryDouble(v, out d)) PanelOrigin.Y = Clamp(d, -1.0, 1.0);
                else if (kl == "panelscale" && TryFloat(v, out f)) PanelScale = Clamp(f, 0.50f, 2.00f);
                else if (kl == "textscale" && TryFloat(v, out f)) TextScale = Clamp(f, 0.50f, 2.00f);
                else if (kl == "panelwidthscale" && TryFloat(v, out f)) PanelWidthScale = Clamp(f, 0.65f, 2.50f);
                else if (kl == "rowscale" && TryFloat(v, out f)) RowScale = Clamp(f, 0.65f, 1.75f);
                else if (kl == "listrows" && int.TryParse(v, out n)) ListRows = Clamp(n, 1, 20);
                else if (kl == "hudlayout") HudLayout = NormalizeChoice(v, new[] { "Prospector","Tactical","Compact","Minimal","Wide" }, "Prospector");
                else if (kl == "framestyle") FrameStyle = NormalizeChoice(v, new[] { "Hex Command","Chevron","Split Wing","Razor","War Room","Minimal","SE Native" }, "Hex Command");
                else if (kl == "panelopacity" && int.TryParse(v, out n)) PanelOpacity = Clamp(n, 0, 255);
                else if (kl == "frameopacity" && int.TryParse(v, out n)) FrameOpacity = Clamp(n, 0, 255);
                else if (kl == "textopacity" && int.TryParse(v, out n)) TextOpacity = Clamp(n, 0, 255);
                else if (kl == "showheader" && bool.TryParse(v, out b)) ShowHeader = b;
                else if (kl == "showstatusbar" && bool.TryParse(v, out b)) ShowStatusBar = b;
                else if (kl == "showcolumnheader" && bool.TryParse(v, out b)) ShowColumnHeader = b;
                else if (kl == "showcolnumber" && bool.TryParse(v, out b)) ShowColNumber = b;
                else if (kl == "showcolpin" && bool.TryParse(v, out b)) ShowColPin = b;
                else if (kl == "showcolgrade" && bool.TryParse(v, out b)) ShowColGrade = b;
                else if (kl == "showcoldistance" && bool.TryParse(v, out b)) ShowColDistance = b;
                else if (kl == "showcoldiameter" && bool.TryParse(v, out b)) ShowColDiameter = b;
                else if (kl == "showcoltopore" && bool.TryParse(v, out b)) ShowColTopOre = b;
                else if (kl == "showcolorepercent" && bool.TryParse(v, out b)) ShowColOrePercent = b;
                else if (kl == "showcolsecondore" && bool.TryParse(v, out b)) ShowColSecondOre = b;
                else if (kl == "showcolquality" && bool.TryParse(v, out b)) ShowColQuality = b;
                else if (kl == "showcolstatus" && bool.TryParse(v, out b)) ShowColStatus = b;
                else if (kl == "markersenabled" && bool.TryParse(v, out b)) MarkersEnabled = b;
                else if (kl == "maxmarkers" && int.TryParse(v, out n)) MaxMarkers = Clamp(n, 0, 100);
                else if (kl == "markerscale" && TryFloat(v, out f)) MarkerScale = Clamp(f, 0.35f, 3.00f);
                else if (kl == "selectedmarkerscale" && TryFloat(v, out f)) SelectedMarkerScale = Clamp(f, 0.50f, 3.00f);
                else if (kl == "offscreenmarkerscale" && TryFloat(v, out f)) OffscreenMarkerScale = Clamp(f, 0.35f, 3.00f);
                else if (kl == "pingsizemode") PingSizeMode = NormalizeChoice(v, new[] { "Fixed","Distance","Grade","Distance + Grade" }, "Distance + Grade");
                else if (kl == "pingcolormode") PingColorMode = NormalizeChoice(v, new[] { "Grade","Top Ore","Theme","Custom" }, "Grade");
                else if (kl == "pingcustomcolor") PingCustomColor = NormalizeColor(v, PingCustomColor);
                else if (kl == "pingshownumber" && bool.TryParse(v, out b)) PingShowNumber = b;
                else if (kl == "pingshowgrade" && bool.TryParse(v, out b)) PingShowGrade = b;
                else if (kl == "pingshowdistance" && bool.TryParse(v, out b)) PingShowDistance = b;
                else if (kl == "pingshowdiameter" && bool.TryParse(v, out b)) PingShowDiameter = b;
                else if (kl == "pingshowtopore" && bool.TryParse(v, out b)) PingShowTopOre = b;
                else if (kl == "pingshoworepercent" && bool.TryParse(v, out b)) PingShowOrePercent = b;
                else if (kl == "pingoffscreenarrows" && bool.TryParse(v, out b)) PingOffscreenArrows = b;
                else if (kl == "pinnedalwaysvisible" && bool.TryParse(v, out b)) PinnedAlwaysVisible = b;
                else if (kl == "themepreset") ThemePreset = v;
                else if (kl == "hudbackgroundcolor") HudBackgroundColor = NormalizeColor(v, HudBackgroundColor);
                else if (kl == "hudpanelcolor") HudPanelColor = NormalizeColor(v, HudPanelColor);
                else if (kl == "hudaccentcolor") HudAccentColor = NormalizeColor(v, HudAccentColor);
                else if (kl == "hudtextcolor") HudTextColor = NormalizeColor(v, HudTextColor);
                else if (kl == "huddimcolor") HudDimColor = NormalizeColor(v, HudDimColor);
                else if (kl == "hudbordercolor") HudBorderColor = NormalizeColor(v, HudBorderColor);
                else if (kl == "selectedcolor") SelectedColor = NormalizeColor(v, SelectedColor);
                else if (kl == "scanningcolor") ScanningColor = NormalizeColor(v, ScanningColor);
                else if (kl == "errorcolor") ErrorColor = NormalizeColor(v, ErrorColor);
                else if (kl == "gradescolor") GradeSColor = NormalizeColor(v, GradeSColor);
                else if (kl == "gradeacolor") GradeAColor = NormalizeColor(v, GradeAColor);
                else if (kl == "gradebcolor") GradeBColor = NormalizeColor(v, GradeBColor);
                else if (kl == "gradeccolor") GradeCColor = NormalizeColor(v, GradeCColor);
                else if (kl == "gradedcolor") GradeDColor = NormalizeColor(v, GradeDColor);
                else if (kl == "gradexcolor") GradeXColor = NormalizeColor(v, GradeXColor);
                else if (kl == "lcdenabled" && bool.TryParse(v, out b)) LcdEnabled = b;
                else if (kl == "lcdtag") LcdTag = v;
                else if (kl == "lcdrole") LcdRole = NormalizeChoice(v, new[] { "Ranking","Target","Status","Ore Summary" }, "Ranking");
                else if (kl == "lcdrows" && int.TryParse(v, out n)) LcdRows = Clamp(n, 1, 30);
                else if (kl == "lcdfontscale" && TryFloat(v, out f)) LcdFontScale = Clamp(f, 0.35f, 2.00f);
                else if (kl == "lcdtheme") LcdTheme = v;
                else if (kl == "lcdlinkhudtheme" && bool.TryParse(v, out b)) LcdLinkHudTheme = b;
                else if (kl == "lcdbackgroundcolor") LcdBackgroundColor = NormalizeColor(v, LcdBackgroundColor);
                else if (kl == "lcdtextcolor") LcdTextColor = NormalizeColor(v, LcdTextColor);
                else if (kl == "lcdaccentcolor") LcdAccentColor = NormalizeColor(v, LcdAccentColor);
                else if (kl == "cacheenabled" && bool.TryParse(v, out b)) CacheEnabled = b;
                else if (kl == "cacheminimumgrade") CacheMinimumGrade = NormalizeGrade(v, "A");
                else if (kl == "cachepersector" && int.TryParse(v, out n)) CachePerSector = Clamp(n, 5, 250);
                else if (kl == "cacheretentiondays" && int.TryParse(v, out n)) CacheRetentionDays = Clamp(n, 1, 365);
                else if (kl == "cachemaxmb" && int.TryParse(v, out n)) CacheMaxMb = Clamp(n, 1, 100);
                else if (kl.StartsWith("oreweight:") && int.TryParse(v, out n)) _oreWeights[k.Substring("OreWeight:".Length).Trim()] = Clamp(n, 0, 20);
                else if (kl.StartsWith("oreenabled:") && bool.TryParse(v, out b)) _oreEnabled[k.Substring("OreEnabled:".Length).Trim()] = b;
                else if (kl.StartsWith("oreminpercent:") && TryDouble(v, out d)) SetOreMinimumPercent(k.Substring("OreMinPercent:".Length).Trim(), d);
                else if (kl.StartsWith("oreshowlist:") && bool.TryParse(v, out b)) _oreShowList[k.Substring("OreShowList:".Length).Trim()] = b;
                else if (kl.StartsWith("oreshowping:") && bool.TryParse(v, out b)) _oreShowPing[k.Substring("OreShowPing:".Length).Trim()] = b;
                else if (kl.StartsWith("orecolor:")) _oreColor[k.Substring("OreColor:".Length).Trim()] = NormalizeColor(v, "#D8D8D8");
            }
            NormalizeGradeThresholds();
            _baseline=ZeoOreShared.OreIni.Parse(Serialize());
            try { _lastWriteUtc = File.GetLastWriteTimeUtc(path); } catch { _lastWriteUtc = DateTime.UtcNow; }
        }

        public void Save(){SaveTo(SettingsPath);}
        private void SaveTo(string targetPath)
        {
            try
            {
                NormalizeGradeThresholds();
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                var L=Serialize();
                _baseline=ZeoOreShared.OreIni.Save(targetPath,ZeoOreShared.OreIni.Parse(L),_baseline);
                ReadFrom(targetPath);
                _lastWriteUtc = File.GetLastWriteTimeUtc(targetPath);
            }
            catch (Exception ex) { Plugin.Log("Settings save failed: " + ex.Message); }
        }

        private List<string> Serialize() {
                var L = new List<string>();
                L.Add("# Zeos Ore Helper v0.7.2 - Learned Ore Search");
                L.Add("# PgUp opens the Zeo Ore control panel by default. StreamerMode uses external capture exclusion and fails closed.");
                L.Add("");
                Add(L,"Enabled",Enabled); Add(L,"StreamerMode",StreamerMode); Add(L,"StreamerFailClosed",StreamerFailClosed); Add(L,"AutoStartOverlay",AutoStartOverlay); Add(L,"MenuKey",MenuKey); Add(L,"ActivePreset",ActivePreset);
                L.Add(""); L.Add("# SURVEY + FILTERS");
                Add(L,"MaxLoadedRange",MaxLoadedRange); Add(L,"MinimumDistanceMeters",MinimumDistanceMeters); Add(L,"SurveyRangeMeters",SurveyRangeMeters); Add(L,"MinimumDiameterMeters",MinimumDiameterMeters); Add(L,"MaximumDiameterMeters",MaximumDiameterMeters); Add(L,"WantedOnly",WantedOnly); Add(L,"ShowNoOre",ShowNoOre); Add(L,"RequireAllWantedOres",RequireAllWantedOres); Add(L,"MinimumGrade",MinimumGrade); Add(L,"MinimumWantedOrePercent",MinimumWantedOrePercent);
                Add(L,"RankingMode",RankingMode); Add(L,"UseSizeFactor",UseSizeFactor); Add(L,"WorthTripGrade",WorthTripGrade); Add(L,"GradeS",GradeS); Add(L,"GradeA",GradeA); Add(L,"GradeB",GradeB); Add(L,"GradeC",GradeC); Add(L,"PureIceMustPercent",PureIceMustPercent);
                Add(L,"DiscoverEveryFrames",DiscoverEveryFrames); Add(L,"ScanEveryFrames",ScanEveryFrames); Add(L,"RescanAfterFrames",RescanAfterFrames); Add(L,"PreferredLod",PreferredLod); Add(L,"MaxSampleCells",MaxSampleCells);
                L.Add(""); L.Add("# HUD LIST");
                Add(L,"ListEnabled",ListEnabled); Add(L,"PanelX",PanelOrigin.X); Add(L,"PanelY",PanelOrigin.Y); Add(L,"PanelScale",PanelScale); Add(L,"TextScale",TextScale); Add(L,"PanelWidthScale",PanelWidthScale); Add(L,"RowScale",RowScale); Add(L,"ListRows",ListRows); Add(L,"HudLayout",HudLayout); Add(L,"FrameStyle",FrameStyle); Add(L,"PanelOpacity",PanelOpacity); Add(L,"FrameOpacity",FrameOpacity); Add(L,"TextOpacity",TextOpacity); Add(L,"ShowHeader",ShowHeader); Add(L,"ShowStatusBar",ShowStatusBar); Add(L,"ShowColumnHeader",ShowColumnHeader);
                Add(L,"ShowColNumber",ShowColNumber); Add(L,"ShowColPin",ShowColPin); Add(L,"ShowColGrade",ShowColGrade); Add(L,"ShowColDistance",ShowColDistance); Add(L,"ShowColDiameter",ShowColDiameter); Add(L,"ShowColTopOre",ShowColTopOre); Add(L,"ShowColOrePercent",ShowColOrePercent); Add(L,"ShowColSecondOre",ShowColSecondOre); Add(L,"ShowColQuality",ShowColQuality); Add(L,"ShowColStatus",ShowColStatus);
                L.Add(""); L.Add("# WORLD PINGS");
                Add(L,"MarkersEnabled",MarkersEnabled); Add(L,"MaxMarkers",MaxMarkers); Add(L,"MarkerScale",MarkerScale); Add(L,"SelectedMarkerScale",SelectedMarkerScale); Add(L,"OffscreenMarkerScale",OffscreenMarkerScale); Add(L,"PingSizeMode",PingSizeMode); Add(L,"PingColorMode",PingColorMode); Add(L,"PingCustomColor",PingCustomColor); Add(L,"PingShowNumber",PingShowNumber); Add(L,"PingShowGrade",PingShowGrade); Add(L,"PingShowDistance",PingShowDistance); Add(L,"PingShowDiameter",PingShowDiameter); Add(L,"PingShowTopOre",PingShowTopOre); Add(L,"PingShowOrePercent",PingShowOrePercent); Add(L,"PingOffscreenArrows",PingOffscreenArrows); Add(L,"PinnedAlwaysVisible",PinnedAlwaysVisible);
                L.Add(""); L.Add("# HUD THEME");
                Add(L,"ThemePreset",ThemePreset); Add(L,"HudBackgroundColor",HudBackgroundColor); Add(L,"HudPanelColor",HudPanelColor); Add(L,"HudAccentColor",HudAccentColor); Add(L,"HudTextColor",HudTextColor); Add(L,"HudDimColor",HudDimColor); Add(L,"HudBorderColor",HudBorderColor); Add(L,"SelectedColor",SelectedColor); Add(L,"ScanningColor",ScanningColor); Add(L,"ErrorColor",ErrorColor); Add(L,"GradeSColor",GradeSColor); Add(L,"GradeAColor",GradeAColor); Add(L,"GradeBColor",GradeBColor); Add(L,"GradeCColor",GradeCColor); Add(L,"GradeDColor",GradeDColor); Add(L,"GradeXColor",GradeXColor);
                L.Add(""); L.Add("# LCD OUTPUT - forcibly hidden in StreamerMode");
                Add(L,"LcdEnabled",LcdEnabled); Add(L,"LcdTag",LcdTag); Add(L,"LcdRole",LcdRole); Add(L,"LcdRows",LcdRows); Add(L,"LcdFontScale",LcdFontScale); Add(L,"LcdTheme",LcdTheme); Add(L,"LcdLinkHudTheme",LcdLinkHudTheme); Add(L,"LcdBackgroundColor",LcdBackgroundColor); Add(L,"LcdTextColor",LcdTextColor); Add(L,"LcdAccentColor",LcdAccentColor);
                L.Add(""); L.Add("# CACHE");
                Add(L,"CacheEnabled",CacheEnabled); Add(L,"CacheMinimumGrade",CacheMinimumGrade); Add(L,"CachePerSector",CachePerSector); Add(L,"CacheRetentionDays",CacheRetentionDays); Add(L,"CacheMaxMb",CacheMaxMb);
                L.Add(""); L.Add("# PER ORE: enabled, priority 0-20, minimum sampled-solid %, list/ping visibility, color");
                for (int i=0;i<KnownOres.Length;i++)
                {
                    string ore=KnownOres[i];
                    Add(L,"OreEnabled:"+ore,IsOreEnabled(ore)); Add(L,"OreWeight:"+ore,GetOreWeight(ore)); Add(L,"OreMinPercent:"+ore,GetOreMinimumPercent(ore)); Add(L,"OreShowList:"+ore,GetOreShowList(ore)); Add(L,"OreShowPing:"+ore,GetOreShowPing(ore)); Add(L,"OreColor:"+ore,GetOreColor(ore));
                }
                return L;
        }

        public bool IsOreEnabled(string ore) { bool v; return !string.IsNullOrWhiteSpace(ore) && (!_oreEnabled.TryGetValue(ore,out v) || v); }
        public int GetOreWeight(string ore) { int v; return !string.IsNullOrWhiteSpace(ore) && _oreWeights.TryGetValue(ore,out v) ? v : 2; }
        public double GetOreMinimumPercent(string ore) { double v; return !string.IsNullOrWhiteSpace(ore) && _oreMinimumPercent.TryGetValue(ore,out v) ? v : 0.0; }
        public double EffectiveOreMinimumPercent(string ore) { return Math.Max(MinimumWantedOrePercent, GetOreMinimumPercent(ore)); }
        public bool GetOreShowList(string ore) { bool v; return !_oreShowList.TryGetValue(ore,out v) || v; }
        public bool GetOreShowPing(string ore) { bool v; return !_oreShowPing.TryGetValue(ore,out v) || v; }
        public string GetOreColor(string ore) { string v; return !string.IsNullOrWhiteSpace(ore) && _oreColor.TryGetValue(ore,out v) ? v : "#D8D8D8"; }
        public void SetOreEnabled(string ore,bool v){if(!string.IsNullOrWhiteSpace(ore))_oreEnabled[ore]=v;}
        public void SetOreWeight(string ore,int v){if(!string.IsNullOrWhiteSpace(ore))_oreWeights[ore]=Clamp(v,0,20);}
        public void SetOreMinimumPercent(string ore,double v){if(!string.IsNullOrWhiteSpace(ore))_oreMinimumPercent[ore]=Clamp(v,0,ore.Equals("Ice",StringComparison.OrdinalIgnoreCase)?100.0:100.0);}
        public void SetOreShowList(string ore,bool v){if(!string.IsNullOrWhiteSpace(ore))_oreShowList[ore]=v;}
        public void SetOreShowPing(string ore,bool v){if(!string.IsNullOrWhiteSpace(ore))_oreShowPing[ore]=v;}
        public void SetOreColor(string ore,string v){if(!string.IsNullOrWhiteSpace(ore))_oreColor[ore]=NormalizeColor(v,GetOreColor(ore));}
        public void ToggleOre(string ore){SetOreEnabled(ore,!IsOreEnabled(ore));}

        public void ApplyProfile(string name)
        {
            string n=(name??"").Trim().ToLowerInvariant();
            if(n=="reactor"||n=="reactor run") { DisableAllKnown(); Enable("Uranium",20); Enable("Tungsten",18); Enable("Ice",12); }
            else if(n=="ice"||n=="ice run") { DisableAllKnown(); Enable("Ice",20); }
            else if(n=="advanced"||n=="advanced materials") { DisableAllKnown(); Enable("Uranium",20); Enable("Tungsten",20); Enable("Titanium",18); Enable("Platinum",18); Enable("Gold",14); Enable("Silver",10); Enable("Copper",9); Enable("Lead",9); Enable("Ice",8); }
            else if(n=="mass"||n=="mass build") { DisableAllKnown(); Enable("Iron",14); Enable("Nickel",12); Enable("Cobalt",14); Enable("Silicon",12); Enable("Magnesium",10); Enable("Copper",10); Enable("Lead",8); Enable("Ice",8); }
            else if(n=="rare"||n=="rare metals") { DisableAllKnown(); Enable("Uranium",20); Enable("Tungsten",20); Enable("Titanium",18); Enable("Platinum",18); Enable("Gold",15); Enable("Silver",10); }
            else if(n=="everything") { for(int i=0;i<KnownOres.Length;i++){SetOreEnabled(KnownOres[i],true); if(GetOreWeight(KnownOres[i])<=0)SetOreWeight(KnownOres[i],5);} }
            else ApplyBalancedDefaults();
            ActivePreset = string.IsNullOrWhiteSpace(name) ? "Balanced" : name;
        }

        public void ApplyTheme(string name)
        {
            string n=(name??"").Trim().ToLowerInvariant();
            if(n=="tactical cyan") { HudBackgroundColor="#050A0D"; HudPanelColor="#07151B"; HudAccentColor="#35C9E8"; HudTextColor="#E8F8FC"; HudDimColor="#6C9DA9"; HudBorderColor="#1D7184"; }
            else if(n=="industrial amber") { HudBackgroundColor="#0C0904"; HudPanelColor="#171106"; HudAccentColor="#E0A63B"; HudTextColor="#F1E6CE"; HudDimColor="#9A8050"; HudBorderColor="#76551C"; }
            else if(n=="ice") { HudBackgroundColor="#05090D"; HudPanelColor="#0A1721"; HudAccentColor="#9BE8FF"; HudTextColor="#F2FBFF"; HudDimColor="#7596A5"; HudBorderColor="#3B768D"; }
            else if(n=="night") { HudBackgroundColor="#050505"; HudPanelColor="#0A0A0A"; HudAccentColor="#BFC5CA"; HudTextColor="#E8EAED"; HudDimColor="#747A80"; HudBorderColor="#33383D"; }
            else { HudBackgroundColor="#090607"; HudPanelColor="#120D0E"; HudAccentColor="#D83B3B"; HudTextColor="#E8EAED"; HudDimColor="#8C9299"; HudBorderColor="#5A2024"; name="Zeo War Room"; }
            ThemePreset=name;
        }

        public void NormalizeGradeThresholds()
        {
            GradeC=Clamp(GradeC,0,248.5); GradeB=Clamp(Math.Max(GradeB,GradeC+0.5),0.5,249); GradeA=Clamp(Math.Max(GradeA,GradeB+0.5),1,249.5); GradeS=Clamp(Math.Max(GradeS,GradeA+0.5),1.5,250);
        }
        private void ApplyBalancedDefaults()
        {
            _oreWeights.Clear(); _oreEnabled.Clear(); _oreMinimumPercent.Clear(); _oreShowList.Clear(); _oreShowPing.Clear();
            SetDefault("Uranium",10); SetDefault("Tungsten",9); SetDefault("Titanium",8); SetDefault("Platinum",8); SetDefault("Gold",6); SetDefault("Lead",5); SetDefault("Copper",5); SetDefault("Silver",4); SetDefault("Boron",4); SetDefault("Organic",3); SetDefault("Cobalt",3); SetDefault("Magnesium",2); SetDefault("Nickel",1); SetDefault("Iron",1); SetDefault("Silicon",1); SetDefault("Ice",6);
        }
        private void ApplyDefaultOreColors()
        {
            SetColorDefault("Uranium","#65FF73"); SetColorDefault("Tungsten","#E2E6EA"); SetColorDefault("Titanium","#8EBBFF"); SetColorDefault("Platinum","#D8E4FF"); SetColorDefault("Gold","#FFD65A"); SetColorDefault("Lead","#8A91A8"); SetColorDefault("Copper","#E48B55"); SetColorDefault("Silver","#D8DDE2"); SetColorDefault("Cobalt","#4E86E8"); SetColorDefault("Magnesium","#E8E8E8"); SetColorDefault("Nickel","#A9B7A0"); SetColorDefault("Iron","#B57D68"); SetColorDefault("Silicon","#C8B4E8"); SetColorDefault("Boron","#68D49A"); SetColorDefault("Organic","#83C56A"); SetColorDefault("Ice","#9BE8FF");
        }
        private void DisableAllKnown(){for(int i=0;i<KnownOres.Length;i++)_oreEnabled[KnownOres[i]]=false;}
        private void Enable(string ore,int weight){_oreEnabled[ore]=true;_oreWeights[ore]=Clamp(weight,0,20);}
        private void SetDefault(string ore,int weight){_oreEnabled[ore]=true;_oreWeights[ore]=weight;if(!_oreMinimumPercent.ContainsKey(ore))_oreMinimumPercent[ore]=0;if(!_oreShowList.ContainsKey(ore))_oreShowList[ore]=true;if(!_oreShowPing.ContainsKey(ore))_oreShowPing[ore]=true;}
        private void SetColorDefault(string ore,string color){if(!_oreColor.ContainsKey(ore))_oreColor[ore]=color;}

        public static int GradeRank(string grade){string g=(grade??"").Trim().ToUpperInvariant();if(g=="S")return 5;if(g=="A")return 4;if(g=="B")return 3;if(g=="C")return 2;if(g=="D")return 1;return 0;}
        public static string NextGrade(string grade,bool includeX){string g=NormalizeGrade(grade,includeX?"X":"D");if(includeX){if(g=="X")return"D";if(g=="D")return"C";if(g=="C")return"B";if(g=="B")return"A";if(g=="A")return"S";return"X";}if(g=="D")return"C";if(g=="C")return"B";if(g=="B")return"A";if(g=="A")return"S";return"D";}
        public static string NormalizeGrade(string grade,string fallback){string g=(grade??"").Trim().ToUpperInvariant();return g=="S"||g=="A"||g=="B"||g=="C"||g=="D"||g=="X"?g:fallback;}
        public static string NormalizeRankingMode(string mode){string m=(mode??"").Trim().ToLowerInvariant();if(m=="nearest")return"nearest";if(m=="quality_distance"||m=="distance"||m=="quality+distance")return"quality_distance";return"quality";}
        public static string NormalizeMenuKey(string key){return OreMenuBinding.Normalize(key);}
        public static string NormalizeColor(string value,string fallback){string s=(value??"").Trim();if(s.Length==7&&s[0]=='#'){int x;if(int.TryParse(s.Substring(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out x))return s.ToUpperInvariant();}return fallback;}
        public static string NormalizeChoice(string value,string[] choices,string fallback){for(int i=0;i<choices.Length;i++)if(string.Equals(value,choices[i],StringComparison.OrdinalIgnoreCase))return choices[i];return fallback;}
        private static bool TryDouble(string s,out double v){return double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out v)&&!double.IsNaN(v)&&!double.IsInfinity(v);}
        private static bool TryFloat(string s,out float v){return float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out v)&&!double.IsNaN(v)&&!double.IsInfinity(v);}
        private static void Add(List<string>L,string k,object v){if(v is double)L.Add(k+"="+((double)v).ToString("0.###",CultureInfo.InvariantCulture));else if(v is float)L.Add(k+"="+((float)v).ToString("0.###",CultureInfo.InvariantCulture));else L.Add(k+"="+(v??"").ToString());}
        private static double Clamp(double v,double min,double max){return v<min?min:v>max?max:v;}
        private static float Clamp(float v,float min,float max){return v<min?min:v>max?max:v;}
        private static int Clamp(int v,int min,int max){return v<min?min:v>max?max:v;}
    }
}
