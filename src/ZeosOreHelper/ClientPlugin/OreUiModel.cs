using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using ZeoOreShared;
using ZeosOreOverlay;
namespace ZeosOreHelper {
 internal enum OreOptionKind { Boolean,Number,Choice,Color,Text,Action }
 internal sealed class OreOption {
  public string Page,Section,Label,Key;
  public string Tab {get{return Page=="SEARCH"||Page=="LEARNING"||Page=="PINGS"?Page:Page=="HUD"||Page=="THEME"?"DISPLAY":Page=="ORES"?"ORE DETAIL":"ADVANCED";}}
  public string Group {get{return Tab=="ADVANCED"?Page+" / "+Section:Section;}} public OreOptionKind Kind;
  public double Min,Max,Step,Multiplier=1; public int Decimals; public string[] Choices;
  public string ActualKey {get{return Key.EndsWith(":")?Key+OreUiModel.SelectedOre:Key;}}
  public object Read(OreOverlaySettings s) { if(Kind==OreOptionKind.Boolean)return s.B(ActualKey); if(Kind==OreOptionKind.Number)return s.D(ActualKey)/Multiplier;return s.Get(ActualKey); }
  public string Format(OreOverlaySettings s) {return Kind==OreOptionKind.Number?Convert.ToDouble(Read(s)).ToString("F"+Decimals,CultureInfo.InvariantCulture):Convert.ToString(Read(s),CultureInfo.InvariantCulture);}
  public object Parse(string text) {
   text=(text??"").Trim();
   if(Kind==OreOptionKind.Number) {double v;if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out v)||double.IsNaN(v)||double.IsInfinity(v)||v<Min||v>Max)throw new ArgumentException("Enter a number from "+Min+" to "+Max);if(Decimals==0&&v!=Math.Truncate(v))throw new ArgumentException("Enter a whole number");return Math.Round(v,Decimals);}
   if(Kind==OreOptionKind.Color) {if(!text.StartsWith("#"))text="#"+text;int v;if(text.Length!=7||!int.TryParse(text.Substring(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out v))throw new ArgumentException("Use six hex digits: #RRGGBB");return text.ToUpperInvariant();}
   if(text.IndexOfAny(new[]{'\r','\n','='})>=0)throw new ArgumentException("Use a single line without =");return text;
  }
 }
 internal sealed class OreUiModel {
  internal static string SelectedOre="Uranium";
  internal OreOverlaySettings Current;
  internal readonly Plugin Host;
  private readonly string path;
  internal OreUiModel(Plugin host,string settingsPath=null){Host=host;path=settingsPath??HudSettings.SettingsPath;Reload();}
  internal void Reload(){Current=new OreOverlaySettings(path);}
  internal void SelectAll(bool enabled){Reload();foreach(var ore in OreOverlaySettings.KnownOres)Current.Set("OreEnabled:"+ore,enabled);Current.Set("ActivePreset","Custom");Current.Save();if(Host!=null)Host.NativeSettingsChanged();Reload();}
  internal void Selection(bool save){Reload();var file=Path.Combine(Path.GetDirectoryName(path),"selection-profiles.ini");if(save)OreSelectionProfiles.Save(file,Current.Get("SelectionSlot","1"),Current);else OreSelectionProfiles.Load(file,Current.Get("SelectionSlot","1"),Current);if(Host!=null)Host.NativeSettingsChanged();Reload();}
  internal void Apply(OreOption o,object value) {
   if(o.Kind==OreOptionKind.Action){Host.NativeAction(o.Key.Substring(1));return;}
   Reload();string key=o.ActualKey;
   if(o.Key=="ActivePreset")Current.ApplyPreset((string)value);
   else if((o.Key=="ThemePreset"||o.Key=="LcdTheme") && (string)value!="CUSTOM") {if(o.Key=="ThemePreset")Current.ApplyTheme((string)value);else Current.ApplyLcdTheme((string)value);}
   else {
    if(o.Kind==OreOptionKind.Number)value=(double)o.Parse(Convert.ToString(value,CultureInfo.InvariantCulture))*o.Multiplier;
    if(o.Kind==OreOptionKind.Color)value=o.Parse((string)value);
    Current.Set(key,value);
    if(o.Kind==OreOptionKind.Color) {if(o.Page=="THEME")Current.Set("ThemePreset","CUSTOM");if(o.Page=="LCD")Current.Set("LcdTheme","CUSTOM");}
    if(key.StartsWith("Ore") && key.Contains(":"))Current.Set("ActivePreset","Custom");
    if(key=="GradeS"||key=="GradeA"||key=="GradeB"||key=="GradeC") {
     var c=Current.D("GradeC"); var b=Current.D("GradeB");var a=Current.D("GradeA");var s=Current.D("GradeS");
     if(b<c+.5||a<b+.5||s<a+.5)throw new ArgumentException("Keep S > A > B > C by at least 0.5");
    }
    Current.Save();
   }
   if(Host!=null)Host.NativeSettingsChanged();Reload();
  }
 }
 internal static class OreUiCatalog {
  internal static readonly string[] Pages=new[]{"SEARCH","DISPLAY","PINGS","LEARNING","ORE DETAIL","ADVANCED"};
  internal static readonly OreOption[] Options={new OreOption{Page="LEARNING",Section="SDX2 SCANS",Label="Prefer SDX2 estimates (saved scans)",Key="PreferSdxScans",Kind=OreOptionKind.Boolean,},
new OreOption{Page="PINGS",Section="NEARBY DEPOSITS",Label="Show nearby ore deposits",Key="DepositMarkers",Kind=OreOptionKind.Boolean,},
new OreOption{Page="PINGS",Section="NEARBY DEPOSITS",Label="Deposit range (km, max 5)",Key="DepositRangeKm",Kind=OreOptionKind.Number,Min=0.1,Max=5,Step=0.1,Decimals=1,},
new OreOption{Page="PINGS",Section="NEARBY DEPOSITS",Label="Deposit limit (shares total cap)",Key="MaxDepositMarkers",Kind=OreOptionKind.Number,Min=0,Max=20,Step=1,Decimals=0,},
new OreOption{Page="PINGS",Section="NEARBY DEPOSITS",Label="Show approximate extent outlines",Key="DepositOutlines",Kind=OreOptionKind.Boolean,},
new OreOption{Page="PINGS",Section="NEARBY DEPOSITS",Label="Show extent size in labels",Key="DepositShowSize",Kind=OreOptionKind.Boolean,},
new OreOption {Page="PINGS",Section="PING INFORMATION",Label="Ping label style",Key="PingLabelStyle",Kind=OreOptionKind.Choice,Choices=new[]{"Simple","Simple + target detail","Custom"}},
new OreOption { Page="SEARCH", Section="SEARCH MODE", Label="Find asteroids by", Key="SearchSort", Kind=OreOptionKind.Choice, Choices=new[]{"Most estimated ore","Richest ore","Nearest matching","Legacy quality"} },
new OreOption { Page="SEARCH", Section="MINIMUM TO PING", Label="Minimum of best seen (%)", Key="BenchmarkPercent", Kind=OreOptionKind.Number, Min=1, Max=100, Step=5, Decimals=0 },
new OreOption { Page="LEARNING", Section="BASELINES", Label="Learning mode", Key="LearningMode", Kind=OreOptionKind.Choice, Choices=new[]{"Auto-learn","Frozen"} },
new OreOption { Page="LEARNING", Section="BASELINES", Label="Hide below minimum", Key="HideBelowThreshold", Kind=OreOptionKind.Boolean },
new OreOption { Page="LEARNING", Section="BASELINES", Label="Verify potential new records", Key="VerifyNewRecords", Kind=OreOptionKind.Boolean },
new OreOption { Page="LEARNING", Section="BASELINES", Label="Verify best loaded asteroids", Key="@verifytop", Kind=OreOptionKind.Action },
new OreOption { Page="LEARNING", Section="BASELINES", Label="Use latest verified bests", Key="@usebenchmarks", Kind=OreOptionKind.Action },
new OreOption { Page="LEARNING", Section="BASELINES", Label="Reset this world learning", Key="@resetlearning", Kind=OreOptionKind.Action },
new OreOption { Page="ORES", Section="SELECTED ORE", Label="Minimum of best (0 = global)", Key="OreBenchmarkPercent:", Kind=OreOptionKind.Number, Min=0, Max=100, Step=5, Decimals=0 },
new OreOption { Page="ORES", Section="SAVED SELECTIONS", Label="Selection slot", Key="SelectionSlot", Kind=OreOptionKind.Choice, Choices=new[]{"1","2","3"} },
new OreOption { Page="ORES", Section="SAVED SELECTIONS", Label="Selection name", Key="SelectionName", Kind=OreOptionKind.Text },
new OreOption { Page="ORES", Section="SAVED SELECTIONS", Label="Save ore selection", Key="@saveselection", Kind=OreOptionKind.Action },
new OreOption { Page="ORES", Section="SAVED SELECTIONS", Label="Load ore selection", Key="@loadselection", Kind=OreOptionKind.Action },
new OreOption { Page="PINGS", Section="CLUTTER", Label="Detailed labels", Key="PingDetailLimit", Kind=OreOptionKind.Number, Min=0, Max=100, Step=1, Decimals=0 },
new OreOption { Page="PINGS", Section="CLUTTER", Label="Offscreen ping limit", Key="MaxOffscreenPings", Kind=OreOptionKind.Number, Min=0, Max=100, Step=1, Decimals=0 },
new OreOption { Page="PINGS", Section="CLUTTER", Label="Ping range (km)", Key="PingMaxDistanceKm", Kind=OreOptionKind.Number, Min=1, Max=1000, Step=5, Decimals=0 },
new OreOption { Page="PINGS", Section="CLUTTER", Label="Hide overlapping labels", Key="PingHideOverlap", Kind=OreOptionKind.Boolean },
new OreOption { Page="PINGS", Section="TEXT STYLE", Label="Ping text size", Key="PingTextScale", Kind=OreOptionKind.Number, Min=0.5, Max=3, Step=0.1, Decimals=2 },
new OreOption { Page="PINGS", Section="TEXT STYLE", Label="Ping text opacity", Key="PingTextOpacity", Kind=OreOptionKind.Number, Min=0, Max=255, Step=5, Decimals=0 },
new OreOption { Page="PINGS", Section="TEXT STYLE", Label="Label horizontal offset", Key="PingLabelOffsetX", Kind=OreOptionKind.Number, Min=-200, Max=200, Step=2, Decimals=0 },
new OreOption { Page="PINGS", Section="TEXT STYLE", Label="Label vertical offset", Key="PingLabelOffsetY", Kind=OreOptionKind.Number, Min=-200, Max=200, Step=2, Decimals=0 },
new OreOption { Page="PINGS", Section="TEXT STYLE", Label="Label spacing", Key="PingLabelSpacing", Kind=OreOptionKind.Number, Min=0, Max=40, Step=1, Decimals=0 },
new OreOption { Page="PINGS", Section="TEXT STYLE", Label="Text outline", Key="PingTextOutline", Kind=OreOptionKind.Boolean },
new OreOption { Page="PINGS", Section="TEXT STYLE", Label="Text background", Key="PingTextBackground", Kind=OreOptionKind.Boolean },
new OreOption { Page="PINGS", Section="TEXT STYLE", Label="Use marker color for text", Key="PingTextUseMarkerColor", Kind=OreOptionKind.Boolean },
new OreOption { Page="PINGS", Section="TEXT STYLE", Label="Custom text color", Key="PingTextColor", Kind=OreOptionKind.Color },
new OreOption { Page="PINGS", Section="PING INFORMATION", Label="Estimated ore volume", Key="PingShowAmount", Kind=OreOptionKind.Boolean },
new OreOption { Page="PINGS", Section="PING INFORMATION", Label="Scan confidence", Key="PingShowScanStatus", Kind=OreOptionKind.Boolean },
new OreOption { Page="HUD", Section="VISIBLE COLUMNS", Label="Estimated ore volume", Key="ShowColAmount", Kind=OreOptionKind.Boolean },
new OreOption { Page="HUD", Section="VISIBLE COLUMNS", Label="Scan confidence", Key="ShowColScanStatus", Kind=OreOptionKind.Boolean },
new OreOption { Page="HOME", Section="MASTER CONTROL", Label="Ore Helper enabled", Key="Enabled", Kind=OreOptionKind.Boolean },
new OreOption { Page="HOME", Section="MASTER CONTROL", Label="Streamer Mode", Key="StreamerMode", Kind=OreOptionKind.Boolean },
new OreOption { Page="HOME", Section="MASTER CONTROL", Label="Fail closed if capture exclusion fails", Key="StreamerFailClosed", Kind=OreOptionKind.Boolean },
new OreOption { Page="HOME", Section="MASTER CONTROL", Label="Auto-launch ZeosOreOverlay", Key="AutoStartOverlay", Kind=OreOptionKind.Boolean },
new OreOption { Page="HOME", Section="UI HOTKEY", Label="Ui Hotkey", Key="MenuKey", Kind=OreOptionKind.Choice, Choices=new[]{"PageUp","PageDown","Insert","Delete","End","F7","F8","F9","F10"} },
new OreOption { Page="FILTERS", Section="DISTANCE", Label="MAX LOADED range", Key="MaxLoadedRange", Kind=OreOptionKind.Boolean },
new OreOption { Page="FILTERS", Section="DISTANCE", Label="Minimum distance (km)", Key="MinimumDistanceMeters", Kind=OreOptionKind.Number, Min=0.0, Max=1000.0, Step=0.5, Decimals=1, Multiplier=1000 },
new OreOption { Page="FILTERS", Section="DISTANCE", Label="Maximum distance (km)", Key="SurveyRangeMeters", Kind=OreOptionKind.Number, Min=1.0, Max=1000.0, Step=1.0, Decimals=1, Multiplier=1000 },
new OreOption { Page="FILTERS", Section="ASTEROID SIZE", Label="Minimum diameter (m)", Key="MinimumDiameterMeters", Kind=OreOptionKind.Number, Min=0.0, Max=10000.0, Step=25.0, Decimals=0, Multiplier=1 },
new OreOption { Page="FILTERS", Section="ASTEROID SIZE", Label="Maximum diameter (m, 0 = ANY)", Key="MaximumDiameterMeters", Kind=OreOptionKind.Number, Min=0.0, Max=10000.0, Step=25.0, Decimals=0, Multiplier=1 },
new OreOption { Page="FILTERS", Section="ORE MATCH", Label="Wanted ores only", Key="WantedOnly", Kind=OreOptionKind.Boolean },
new OreOption { Page="FILTERS", Section="ORE MATCH", Label="Require ALL wanted ores", Key="RequireAllWantedOres", Kind=OreOptionKind.Boolean },
new OreOption { Page="FILTERS", Section="ORE MATCH", Label="Show no-ore / X asteroids", Key="ShowNoOre", Kind=OreOptionKind.Boolean },
new OreOption { Page="FILTERS", Section="ORE MATCH", Label="Global minimum ore %", Key="MinimumWantedOrePercent", Kind=OreOptionKind.Number, Min=0.0, Max=100.0, Step=0.01, Decimals=3, Multiplier=1 },
new OreOption { Page="FILTERS", Section="MINIMUM GRADE", Label="Minimum Grade", Key="MinimumGrade", Kind=OreOptionKind.Choice, Choices=new[]{"X","D","C","B","A","S"} },
new OreOption { Page="RANKING", Section="RANKING MODE", Label="Ranking Mode", Key="RankingMode", Kind=OreOptionKind.Choice, Choices=new[]{"quality","quality_distance","nearest"} },
new OreOption { Page="RANKING", Section="RANKING MODE", Label="Use asteroid size factor", Key="UseSizeFactor", Kind=OreOptionKind.Boolean },
new OreOption { Page="RANKING", Section="WORTH TRIP GRADE", Label="Worth Trip Grade", Key="WorthTripGrade", Kind=OreOptionKind.Choice, Choices=new[]{"D","C","B","A","S"} },
new OreOption { Page="RANKING", Section="GRADE THRESHOLDS", Label="S threshold", Key="GradeS", Kind=OreOptionKind.Number, Min=1.5, Max=250.0, Step=0.5, Decimals=1, Multiplier=1 },
new OreOption { Page="RANKING", Section="GRADE THRESHOLDS", Label="A threshold", Key="GradeA", Kind=OreOptionKind.Number, Min=1.0, Max=249.5, Step=0.5, Decimals=1, Multiplier=1 },
new OreOption { Page="RANKING", Section="GRADE THRESHOLDS", Label="B threshold", Key="GradeB", Kind=OreOptionKind.Number, Min=0.5, Max=249.0, Step=0.5, Decimals=1, Multiplier=1 },
new OreOption { Page="RANKING", Section="GRADE THRESHOLDS", Label="C threshold", Key="GradeC", Kind=OreOptionKind.Number, Min=0.0, Max=248.5, Step=0.5, Decimals=1, Multiplier=1 },
new OreOption { Page="RANKING", Section="GRADE THRESHOLDS", Label="Ice MUST threshold %", Key="PureIceMustPercent", Kind=OreOptionKind.Number, Min=0.0, Max=100.0, Step=1.0, Decimals=1, Multiplier=1 },
new OreOption { Page="HUD", Section="HUD LAYOUT", Label="Hud Layout", Key="HudLayout", Kind=OreOptionKind.Choice, Choices=new[]{"Prospector","Tactical","Compact","Minimal","Wide"} },
new OreOption { Page="HUD", Section="FRAME", Label="Frame", Key="FrameStyle", Kind=OreOptionKind.Choice, Choices=new[]{"Hex Command","Chevron","Split Wing","Razor","War Room","Minimal"} },
new OreOption { Page="HUD", Section="HUD VISIBILITY", Label="Ranking list", Key="ListEnabled", Kind=OreOptionKind.Boolean },
new OreOption { Page="HUD", Section="HUD VISIBILITY", Label="Header", Key="ShowHeader", Kind=OreOptionKind.Boolean },
new OreOption { Page="HUD", Section="HUD VISIBILITY", Label="Status bar", Key="ShowStatusBar", Kind=OreOptionKind.Boolean },
new OreOption { Page="HUD", Section="HUD VISIBILITY", Label="Column header", Key="ShowColumnHeader", Kind=OreOptionKind.Boolean },
new OreOption { Page="HUD", Section="HUD GEOMETRY", Label="Position X", Key="PanelX", Kind=OreOptionKind.Number, Min=-1.0, Max=1.0, Step=0.01, Decimals=2, Multiplier=1 },
new OreOption { Page="HUD", Section="HUD GEOMETRY", Label="Position Y", Key="PanelY", Kind=OreOptionKind.Number, Min=-1.0, Max=1.0, Step=0.01, Decimals=2, Multiplier=1 },
new OreOption { Page="HUD", Section="HUD GEOMETRY", Label="Panel scale", Key="PanelScale", Kind=OreOptionKind.Number, Min=0.5, Max=2.0, Step=0.05, Decimals=2, Multiplier=1 },
new OreOption { Page="HUD", Section="HUD GEOMETRY", Label="Text scale", Key="TextScale", Kind=OreOptionKind.Number, Min=0.5, Max=2.0, Step=0.05, Decimals=2, Multiplier=1 },
new OreOption { Page="HUD", Section="HUD GEOMETRY", Label="Width scale", Key="PanelWidthScale", Kind=OreOptionKind.Number, Min=0.65, Max=2.5, Step=0.05, Decimals=2, Multiplier=1 },
new OreOption { Page="HUD", Section="HUD GEOMETRY", Label="Row height scale", Key="RowScale", Kind=OreOptionKind.Number, Min=0.65, Max=1.75, Step=0.05, Decimals=2, Multiplier=1 },
new OreOption { Page="HUD", Section="HUD GEOMETRY", Label="List rows", Key="ListRows", Kind=OreOptionKind.Number, Min=1.0, Max=20.0, Step=1.0, Decimals=0, Multiplier=1 },
new OreOption { Page="HUD", Section="HUD GEOMETRY", Label="Panel opacity", Key="PanelOpacity", Kind=OreOptionKind.Number, Min=0.0, Max=255.0, Step=5.0, Decimals=0, Multiplier=1 },
new OreOption { Page="HUD", Section="HUD GEOMETRY", Label="Frame opacity", Key="FrameOpacity", Kind=OreOptionKind.Number, Min=0.0, Max=255.0, Step=5.0, Decimals=0, Multiplier=1 },
new OreOption { Page="HUD", Section="HUD GEOMETRY", Label="Text opacity", Key="TextOpacity", Kind=OreOptionKind.Number, Min=0.0, Max=255.0, Step=5.0, Decimals=0, Multiplier=1 },
new OreOption { Page="HUD", Section="VISIBLE COLUMNS", Label="Roid #", Key="ShowColNumber", Kind=OreOptionKind.Boolean },
new OreOption { Page="HUD", Section="VISIBLE COLUMNS", Label="Pin", Key="ShowColPin", Kind=OreOptionKind.Boolean },
new OreOption { Page="HUD", Section="VISIBLE COLUMNS", Label="Grade", Key="ShowColGrade", Kind=OreOptionKind.Boolean },
new OreOption { Page="HUD", Section="VISIBLE COLUMNS", Label="Distance", Key="ShowColDistance", Kind=OreOptionKind.Boolean },
new OreOption { Page="HUD", Section="VISIBLE COLUMNS", Label="Diameter", Key="ShowColDiameter", Kind=OreOptionKind.Boolean },
new OreOption { Page="HUD", Section="VISIBLE COLUMNS", Label="Top ore", Key="ShowColTopOre", Kind=OreOptionKind.Boolean },
new OreOption { Page="HUD", Section="VISIBLE COLUMNS", Label="Ore %", Key="ShowColOrePercent", Kind=OreOptionKind.Boolean },
new OreOption { Page="HUD", Section="VISIBLE COLUMNS", Label="Second ore", Key="ShowColSecondOre", Kind=OreOptionKind.Boolean },
new OreOption { Page="HUD", Section="VISIBLE COLUMNS", Label="Quality", Key="ShowColQuality", Kind=OreOptionKind.Boolean },
new OreOption { Page="HUD", Section="VISIBLE COLUMNS", Label="Status", Key="ShowColStatus", Kind=OreOptionKind.Boolean },
new OreOption { Page="PINGS", Section="WORLD PINGS", Label="World pings", Key="MarkersEnabled", Kind=OreOptionKind.Boolean },
new OreOption { Page="PINGS", Section="WORLD PINGS", Label="Offscreen arrows", Key="PingOffscreenArrows", Kind=OreOptionKind.Boolean },
new OreOption { Page="PINGS", Section="WORLD PINGS", Label="Pinned always visible", Key="PinnedAlwaysVisible", Kind=OreOptionKind.Boolean },
new OreOption { Page="PINGS", Section="WORLD PINGS", Label="Maximum pings", Key="MaxMarkers", Kind=OreOptionKind.Number, Min=0.0, Max=100.0, Step=1.0, Decimals=0, Multiplier=1 },
new OreOption { Page="PINGS", Section="WORLD PINGS", Label="Base ping scale", Key="MarkerScale", Kind=OreOptionKind.Number, Min=0.35, Max=3.0, Step=0.05, Decimals=2, Multiplier=1 },
new OreOption { Page="PINGS", Section="WORLD PINGS", Label="Selected scale", Key="SelectedMarkerScale", Kind=OreOptionKind.Number, Min=0.5, Max=3.0, Step=0.05, Decimals=2, Multiplier=1 },
new OreOption { Page="PINGS", Section="WORLD PINGS", Label="Offscreen scale", Key="OffscreenMarkerScale", Kind=OreOptionKind.Number, Min=0.35, Max=3.0, Step=0.05, Decimals=2, Multiplier=1 },
new OreOption { Page="PINGS", Section="PING SIZE MODE", Label="Ping Size Mode", Key="PingSizeMode", Kind=OreOptionKind.Choice, Choices=new[]{"Fixed","Distance","Grade","Distance + Grade"} },
new OreOption { Page="PINGS", Section="PING COLOR MODE", Label="Ping Color Mode", Key="PingColorMode", Kind=OreOptionKind.Choice, Choices=new[]{"Grade","Top Ore","Theme","Custom"} },
new OreOption { Page="PINGS", Section="PING COLOR MODE", Label="Custom ping color", Key="PingCustomColor", Kind=OreOptionKind.Color },
new OreOption { Page="PINGS", Section="PING INFORMATION", Label="Roid #", Key="PingShowNumber", Kind=OreOptionKind.Boolean },
new OreOption { Page="PINGS", Section="PING INFORMATION", Label="Grade", Key="PingShowGrade", Kind=OreOptionKind.Boolean },
new OreOption { Page="PINGS", Section="PING INFORMATION", Label="Distance", Key="PingShowDistance", Kind=OreOptionKind.Boolean },
new OreOption { Page="PINGS", Section="PING INFORMATION", Label="Diameter", Key="PingShowDiameter", Kind=OreOptionKind.Boolean },
new OreOption { Page="PINGS", Section="PING INFORMATION", Label="Top ore", Key="PingShowTopOre", Kind=OreOptionKind.Boolean },
new OreOption { Page="PINGS", Section="PING INFORMATION", Label="Ore %", Key="PingShowOrePercent", Kind=OreOptionKind.Boolean },
new OreOption { Page="THEME", Section="HUD THEME", Label="Hud Theme", Key="ThemePreset", Kind=OreOptionKind.Choice, Choices=new[]{"GRAPHITE","MONOCHROME","AMBER","HIGH CONTRAST","CUSTOM","WAR ROOM"} },
new OreOption { Page="THEME", Section="HUD COLORS", Label="Background", Key="HudBackgroundColor", Kind=OreOptionKind.Color },
new OreOption { Page="THEME", Section="HUD COLORS", Label="Panel", Key="HudPanelColor", Kind=OreOptionKind.Color },
new OreOption { Page="THEME", Section="HUD COLORS", Label="Accent", Key="HudAccentColor", Kind=OreOptionKind.Color },
new OreOption { Page="THEME", Section="HUD COLORS", Label="Text", Key="HudTextColor", Kind=OreOptionKind.Color },
new OreOption { Page="THEME", Section="HUD COLORS", Label="Dim text", Key="HudDimColor", Kind=OreOptionKind.Color },
new OreOption { Page="THEME", Section="HUD COLORS", Label="Border", Key="HudBorderColor", Kind=OreOptionKind.Color },
new OreOption { Page="THEME", Section="HUD COLORS", Label="Selected", Key="SelectedColor", Kind=OreOptionKind.Color },
new OreOption { Page="THEME", Section="HUD COLORS", Label="Scanning", Key="ScanningColor", Kind=OreOptionKind.Color },
new OreOption { Page="THEME", Section="HUD COLORS", Label="Error", Key="ErrorColor", Kind=OreOptionKind.Color },
new OreOption { Page="THEME", Section="GRADE COLORS", Label="Grade S", Key="GradeSColor", Kind=OreOptionKind.Color },
new OreOption { Page="THEME", Section="GRADE COLORS", Label="Grade A", Key="GradeAColor", Kind=OreOptionKind.Color },
new OreOption { Page="THEME", Section="GRADE COLORS", Label="Grade B", Key="GradeBColor", Kind=OreOptionKind.Color },
new OreOption { Page="THEME", Section="GRADE COLORS", Label="Grade C", Key="GradeCColor", Kind=OreOptionKind.Color },
new OreOption { Page="THEME", Section="GRADE COLORS", Label="Grade D", Key="GradeDColor", Kind=OreOptionKind.Color },
new OreOption { Page="THEME", Section="GRADE COLORS", Label="Grade X", Key="GradeXColor", Kind=OreOptionKind.Color },
new OreOption { Page="LCD", Section="LCD OUTPUT", Label="LCD output enabled", Key="LcdEnabled", Kind=OreOptionKind.Boolean },
new OreOption { Page="LCD", Section="LCD OUTPUT", Label="LCD name / tag", Key="LcdTag", Kind=OreOptionKind.Text },
new OreOption { Page="LCD", Section="LCD ROLE", Label="Lcd Role", Key="LcdRole", Kind=OreOptionKind.Choice, Choices=new[]{"Ranking","Target","Status","Ore Summary"} },
new OreOption { Page="LCD", Section="LCD ROLE", Label="Rows", Key="LcdRows", Kind=OreOptionKind.Number, Min=1.0, Max=30.0, Step=1.0, Decimals=0, Multiplier=1 },
new OreOption { Page="LCD", Section="LCD ROLE", Label="Font scale", Key="LcdFontScale", Kind=OreOptionKind.Number, Min=0.35, Max=2.0, Step=0.05, Decimals=2, Multiplier=1 },
new OreOption { Page="LCD", Section="LCD ROLE", Label="Link LCD to HUD theme", Key="LcdLinkHudTheme", Kind=OreOptionKind.Boolean },
new OreOption { Page="LCD", Section="LCD THEME", Label="Lcd Theme", Key="LcdTheme", Kind=OreOptionKind.Choice, Choices=new[]{"GRAPHITE","MONOCHROME","AMBER","HIGH CONTRAST","CUSTOM","WAR ROOM"} },
new OreOption { Page="LCD", Section="LCD THEME", Label="LCD background", Key="LcdBackgroundColor", Kind=OreOptionKind.Color },
new OreOption { Page="LCD", Section="LCD THEME", Label="LCD text", Key="LcdTextColor", Kind=OreOptionKind.Color },
new OreOption { Page="LCD", Section="LCD THEME", Label="LCD accent", Key="LcdAccentColor", Kind=OreOptionKind.Color },
new OreOption { Page="STORAGE", Section="ROID CACHE", Label="Cache enabled", Key="CacheEnabled", Kind=OreOptionKind.Boolean },
new OreOption { Page="STORAGE", Section="MINIMUM CACHED GRADE", Label="Minimum Cached Grade", Key="CacheMinimumGrade", Kind=OreOptionKind.Choice, Choices=new[]{"D","C","B","A","S"} },
new OreOption { Page="STORAGE", Section="MINIMUM CACHED GRADE", Label="Normal entries per sector", Key="CachePerSector", Kind=OreOptionKind.Number, Min=5.0, Max=200.0, Step=1.0, Decimals=0, Multiplier=1 },
new OreOption { Page="STORAGE", Section="MINIMUM CACHED GRADE", Label="Retention days", Key="CacheRetentionDays", Kind=OreOptionKind.Number, Min=1.0, Max=365.0, Step=1.0, Decimals=0, Multiplier=1 },
new OreOption { Page="STORAGE", Section="MINIMUM CACHED GRADE", Label="Maximum cache size (MB)", Key="CacheMaxMb", Kind=OreOptionKind.Number, Min=1.0, Max=100.0, Step=1.0, Decimals=0, Multiplier=1 },
new OreOption { Page="ADVANCED", Section="SURVEY ENGINE", Label="Discovery cadence (frames)", Key="DiscoverEveryFrames", Kind=OreOptionKind.Number, Min=15.0, Max=600.0, Step=5.0, Decimals=0, Multiplier=1 },
new OreOption { Page="ADVANCED", Section="SURVEY ENGINE", Label="Survey cadence (frames)", Key="ScanEveryFrames", Kind=OreOptionKind.Number, Min=5.0, Max=300.0, Step=5.0, Decimals=0, Multiplier=1 },
new OreOption { Page="ADVANCED", Section="SURVEY ENGINE", Label="Rescan age (frames)", Key="RescanAfterFrames", Kind=OreOptionKind.Number, Min=600.0, Max=216000.0, Step=600.0, Decimals=0, Multiplier=1 },
new OreOption { Page="ADVANCED", Section="SURVEY ENGINE", Label="Preferred voxel LOD", Key="PreferredLod", Kind=OreOptionKind.Number, Min=0.0, Max=6.0, Step=1.0, Decimals=0, Multiplier=1 },
new OreOption { Page="ADVANCED", Section="SURVEY ENGINE", Label="Maximum sample cells", Key="MaxSampleCells", Kind=OreOptionKind.Number, Min=25000.0, Max=1000000.0, Step=10000.0, Decimals=0, Multiplier=1 },
new OreOption { Page="HOME", Section="MINING PROFILE", Label="Mining profile", Key="ActivePreset", Kind=OreOptionKind.Choice, Choices=new[]{"Balanced","Rare Metals","Reactor Run","Ice Run","Advanced Materials","Mass Build","Everything"} },
new OreOption { Page="ORES", Section="SELECTED ORE", Label="Wanted ore", Key="OreEnabled:", Kind=OreOptionKind.Boolean, Min=0, Max=0, Step=0, Decimals=0 },
new OreOption { Page="ORES", Section="SELECTED ORE", Label="Priority", Key="OreWeight:", Kind=OreOptionKind.Number, Min=0, Max=20, Step=1, Decimals=0 },
new OreOption { Page="ORES", Section="SELECTED ORE", Label="Minimum sampled %", Key="OreMinPercent:", Kind=OreOptionKind.Number, Min=0, Max=100, Step=0.01, Decimals=3 },
new OreOption { Page="ORES", Section="SELECTED ORE", Label="Show in list", Key="OreShowList:", Kind=OreOptionKind.Boolean, Min=0, Max=0, Step=0, Decimals=0 },
new OreOption { Page="ORES", Section="SELECTED ORE", Label="Show on pings", Key="OreShowPing:", Kind=OreOptionKind.Boolean, Min=0, Max=0, Step=0, Decimals=0 },
new OreOption { Page="ORES", Section="SELECTED ORE", Label="Ore color", Key="OreColor:", Kind=OreOptionKind.Color, Min=0, Max=0, Step=0, Decimals=0 },
new OreOption { Page="ACTIONS", Section="SURVEY ACTIONS", Label="Refresh scan queue", Key="@scan", Kind=OreOptionKind.Action },
new OreOption { Page="ACTIONS", Section="SURVEY ACTIONS", Label="Rescan all loaded asteroids", Key="@rescan", Kind=OreOptionKind.Action },
new OreOption { Page="ACTIONS", Section="SURVEY ACTIONS", Label="Pin / unpin targeted asteroid", Key="@pin", Kind=OreOptionKind.Action },
new OreOption { Page="ACTIONS", Section="SURVEY ACTIONS", Label="Skip targeted asteroid", Key="@skip", Kind=OreOptionKind.Action },
new OreOption { Page="ACTIONS", Section="SURVEY ACTIONS", Label="Clear skipped asteroids", Key="@clearskips", Kind=OreOptionKind.Action },
new OreOption { Page="ACTIONS", Section="SURVEY ACTIONS", Label="Write survey report", Key="@dump", Kind=OreOptionKind.Action },
new OreOption { Page="HUD", Section="LAYOUT", Label="Drag HUD position", Key="@LAYOUT", Kind=OreOptionKind.Action },
new OreOption { Page="ADVANCED", Section="LEGACY MENU", Label="Menu Background Color", Key="MenuBackgroundColor", Kind=OreOptionKind.Color },
new OreOption { Page="ADVANCED", Section="LEGACY MENU", Label="Menu Panel Color", Key="MenuPanelColor", Kind=OreOptionKind.Color },
new OreOption { Page="ADVANCED", Section="LEGACY MENU", Label="Menu Text Color", Key="MenuTextColor", Kind=OreOptionKind.Color },
new OreOption { Page="ADVANCED", Section="LEGACY MENU", Label="Menu Accent Color", Key="MenuAccentColor", Kind=OreOptionKind.Color },
new OreOption { Page="ADVANCED", Section="LEGACY MENU", Label="Legacy menu scale", Key="UiScale", Kind=OreOptionKind.Number, Min=0.75, Max=1.35, Step=0.05, Decimals=2 },
new OreOption { Page="ADVANCED", Section="LEGACY MENU", Label="Legacy menu width", Key="UiWindowWidth", Kind=OreOptionKind.Number, Min=620, Max=760, Step=10, Decimals=0 },
new OreOption { Page="ADVANCED", Section="LEGACY MENU", Label="Legacy menu height", Key="UiWindowHeight", Kind=OreOptionKind.Number, Min=720, Max=1120, Step=10, Decimals=0 }};
 }
}
