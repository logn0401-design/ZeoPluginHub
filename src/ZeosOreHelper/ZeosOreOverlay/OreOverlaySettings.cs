using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
namespace ZeosOreOverlay
{
    internal sealed class OreOverlaySettings
    {
        internal static readonly string[] KnownOres={"Uranium","Tungsten","Titanium","Platinum","Gold","Lead","Copper","Silver","Cobalt","Magnesium","Nickel","Iron","Silicon","Boron","Organic","Ice"};
        private readonly Dictionary<string,string> _v=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);private Dictionary<string,string> _baseline; private DateTime _stamp=DateTime.MinValue;internal string PathName{get;private set;}
        internal OreOverlaySettings(string path){PathName=path;Seed();Load();_baseline=File.Exists(path)?new Dictionary<string,string>(_v,StringComparer.OrdinalIgnoreCase):null;}
        private void Seed()
        {
            Set("SearchSort","Most estimated ore");Set("LearningMode","Auto-learn");Set("BenchmarkPercent",80);Set("HideBelowThreshold",true);Set("PingDetailLimit",3);Set("MaxOffscreenPings",2);Set("PingMaxDistanceKm",1000);Set("PingTextScale",1.2);Set("PingLabelStyle","Simple + target detail");Set("PingTextOpacity",245);Set("PingTextOutline",true);Set("PingTextBackground",true);Set("PingTextUseMarkerColor",true);Set("PingTextColor","#D1E8EF");Set("PingLabelOffsetX",4);Set("PingLabelOffsetY",0);Set("PingLabelSpacing",4);Set("PingHideOverlap",true);Set("PingShowAmount",true);Set("PingShowScanStatus",true);Set("ShowColAmount",true);Set("ShowColScanStatus",true);Set("VerifyNewRecords",true);Set("SelectionSlot","1");Set("SelectionName","My selection");
            Set("Enabled",false);Set("StreamerMode",false);Set("StreamerFailClosed",true);Set("AutoStartOverlay",true);Set("MenuKey","PageUp");Set("MenuPage",0);Set("SelectedOreIndex",0);Set("ActivePreset","Balanced");Set("UiScale",1.0);Set("UiWindowX",-1);Set("UiWindowY",-1);Set("UiWindowWidth",660);Set("UiWindowHeight",900);Set("MenuBackgroundColor","#07090B");Set("MenuPanelColor","#111419");Set("MenuTextColor","#F1F3F5");Set("MenuAccentColor","#D52B2B");Set("MaxLoadedRange",false);Set("MinimumDistanceMeters",0);Set("SurveyRangeMeters",45000);Set("MinimumDiameterMeters",0);Set("MaximumDiameterMeters",0);Set("WantedOnly",false);Set("ShowNoOre",false);Set("RequireAllWantedOres",false);Set("MinimumGrade","D");Set("MinimumWantedOrePercent",0);Set("RankingMode","quality");Set("UseSizeFactor",true);Set("WorthTripGrade","A");Set("GradeS",60);Set("GradeA",35);Set("GradeB",20);Set("GradeC",8);Set("PureIceMustPercent",100);
            Set("DiscoverEveryFrames",60);Set("ScanEveryFrames",20);Set("RescanAfterFrames",36000);Set("PreferredLod",3);Set("MaxSampleCells",300000);
            Set("ListEnabled",true);Set("PanelX",-0.72);Set("PanelY",-0.64);Set("PanelScale",1);Set("TextScale",1);Set("PanelWidthScale",1);Set("RowScale",1);Set("ListRows",8);Set("HudLayout","Prospector");Set("FrameStyle","Hex Command");Set("PanelOpacity",190);Set("FrameOpacity",235);Set("TextOpacity",245);Set("ShowHeader",true);Set("ShowStatusBar",true);Set("ShowColumnHeader",true);
            foreach(string k in new[]{"ShowColNumber","ShowColPin","ShowColGrade","ShowColDistance","ShowColDiameter","ShowColTopOre","ShowColOrePercent","ShowColSecondOre"})Set(k,true);Set("ShowColQuality",false);Set("ShowColStatus",false);
            Set("PreferSdxScans",true);Set("DepositMarkers",true);Set("DepositRangeKm",5);Set("MaxDepositMarkers",4);Set("DepositOutlines",true);Set("DepositShowSize",true);
            Set("MarkersEnabled",true);Set("MaxMarkers",30);Set("MarkerScale",1);Set("SelectedMarkerScale",1.25);Set("OffscreenMarkerScale",1);Set("PingSizeMode","Distance + Grade");Set("PingColorMode","Grade");Set("PingCustomColor","#E7ECF2");Set("PingShowNumber",true);Set("PingShowGrade",true);Set("PingShowDistance",true);Set("PingShowDiameter",false);Set("PingShowTopOre",true);Set("PingShowOrePercent",true);Set("PingOffscreenArrows",true);Set("PinnedAlwaysVisible",true);
            Set("ThemePreset","WAR ROOM");Set("HudBackgroundColor","#090607");Set("HudPanelColor","#120D0E");Set("HudAccentColor","#D83B3B");Set("HudTextColor","#E8EAED");Set("HudDimColor","#8C9299");Set("HudBorderColor","#5A2024");Set("SelectedColor","#FFE16B");Set("ScanningColor","#69D6EE");Set("ErrorColor","#EB5A5A");Set("GradeSColor","#4BFF73");Set("GradeAColor","#73EB87");Set("GradeBColor","#F5DA50");Set("GradeCColor","#F59B46");Set("GradeDColor","#A5AAAF");Set("GradeXColor","#965555");
            Set("LcdEnabled",false);Set("LcdTag","Zeo Ore LCD");Set("LcdRole","Ranking");Set("LcdRows",10);Set("LcdFontScale",0.75);Set("LcdTheme","WAR ROOM");Set("LcdLinkHudTheme",true);Set("LcdBackgroundColor","#090607");Set("LcdTextColor","#E8EAED");Set("LcdAccentColor","#D83B3B");Set("CacheEnabled",true);Set("CacheMinimumGrade","A");Set("CachePerSector",30);Set("CacheRetentionDays",14);Set("CacheMaxMb",5);
            int[] weights={10,9,8,8,6,5,5,4,3,2,1,1,1,4,3,6};string[] colors={"#65FF73","#E2E6EA","#8EBBFF","#D8E4FF","#FFD65A","#8A91A8","#E48B55","#D8DDE2","#4E86E8","#E8E8E8","#A9B7A0","#B57D68","#C8B4E8","#68D49A","#83C56A","#9BE8FF"};
            for(int i=0;i<KnownOres.Length;i++){string o=KnownOres[i];Set("OreEnabled:"+o,true);Set("OreWeight:"+o,weights[i]);Set("OreMinPercent:"+o,0);Set("OreShowList:"+o,true);Set("OreShowPing:"+o,true);Set("OreColor:"+o,colors[i]);Set("OreBenchmarkPercent:"+o,0);}
        }
        internal void Load(){try{if(!File.Exists(PathName))return;foreach(var raw in File.ReadAllLines(PathName)){string line=raw.Trim();if(line.Length==0||line.StartsWith("#")||line.StartsWith(";"))continue;int e=line.IndexOf('=');if(e<=0)continue;_v[line.Substring(0,e).Trim()]=line.Substring(e+1).Trim();}_stamp=File.GetLastWriteTimeUtc(PathName);_baseline=new Dictionary<string,string>(_v,StringComparer.OrdinalIgnoreCase);}catch{}}
        internal bool ReloadIfChanged(){try{if(!File.Exists(PathName))return false;var s=File.GetLastWriteTimeUtc(PathName);if(s<=_stamp)return false;Load();return true;}catch{return false;}}
        internal void Save(){var merged=ZeoOreShared.OreIni.Save(PathName,_v,_baseline); foreach(var p in merged)_v[p.Key]=p.Value;_baseline=new Dictionary<string,string>(_v,StringComparer.OrdinalIgnoreCase);_stamp=File.GetLastWriteTimeUtc(PathName);}
        internal string Get(string k,string d=""){string v;return _v.TryGetValue(k,out v)?v:d;}
        internal bool B(string k,bool d=false){bool v;return bool.TryParse(Get(k),out v)?v:d;}
        internal int I(string k,int d=0){int v;return int.TryParse(Get(k),NumberStyles.Integer,CultureInfo.InvariantCulture,out v)?v:d;}
        internal double D(string k,double d=0){double v;return double.TryParse(Get(k),NumberStyles.Float,CultureInfo.InvariantCulture,out v)&&!double.IsNaN(v)&&!double.IsInfinity(v)?v:d;}
        internal void Set(string k,object value){if(value is double)_v[k]=((double)value).ToString("0.###",CultureInfo.InvariantCulture);else if(value is float)_v[k]=((float)value).ToString("0.###",CultureInfo.InvariantCulture);else _v[k]=(value??"").ToString();}
        internal void SetSave(string k,object value){Set(k,value);Save();}
        internal void ApplyPreset(string name)
        {
            Action disable=()=>{foreach(string o in KnownOres)Set("OreEnabled:"+o,false);};Action<string,int> enable=(o,w)=>{Set("OreEnabled:"+o,true);Set("OreWeight:"+o,w);};string n=(name??"").ToLowerInvariant();
            if(n.Contains("reactor")){disable();enable("Uranium",20);enable("Tungsten",18);enable("Ice",12);}
            else if(n.Contains("ice")){disable();enable("Ice",20);}
            else if(n.Contains("advanced")){disable();enable("Uranium",20);enable("Tungsten",20);enable("Titanium",18);enable("Platinum",18);enable("Gold",14);enable("Silver",10);enable("Copper",9);enable("Lead",9);enable("Ice",8);}
            else if(n.Contains("mass")){disable();enable("Iron",14);enable("Nickel",12);enable("Cobalt",14);enable("Silicon",12);enable("Magnesium",10);enable("Copper",10);enable("Lead",8);enable("Ice",8);}
            else if(n.Contains("rare")){disable();enable("Uranium",20);enable("Tungsten",20);enable("Titanium",18);enable("Platinum",18);enable("Gold",15);enable("Silver",10);}
            else if(n.Contains("everything")){foreach(string o in KnownOres){Set("OreEnabled:"+o,true);if(I("OreWeight:"+o,0)<=0)Set("OreWeight:"+o,5);}}
            else{int[] w={10,9,8,8,6,5,5,4,3,2,1,1,1,4,3,6};for(int i=0;i<KnownOres.Length;i++){Set("OreEnabled:"+KnownOres[i],true);Set("OreWeight:"+KnownOres[i],w[i]);}name="Balanced";}
            Set("ActivePreset",name);Save();
        }
        internal void ApplyLcdTheme(string name)
        {
            string n=(name??"").Trim().ToUpperInvariant();
            if(n=="SE NATIVE"){Set("LcdBackgroundColor","#1B252A");Set("LcdTextColor","#D1E8EF");Set("LcdAccentColor","#BFF2FF");}
            else if(n=="GRAPHITE"){Set("LcdBackgroundColor","#202327");Set("LcdTextColor","#D7D9DC");Set("LcdAccentColor","#AEB5BC");}
            else if(n=="MONOCHROME"){Set("LcdBackgroundColor","#1F1F1F");Set("LcdTextColor","#D8D8D8");Set("LcdAccentColor","#A8A8A8");}
            else if(n=="AMBER"){Set("LcdBackgroundColor","#211F1A");Set("LcdTextColor","#E3D6B0");Set("LcdAccentColor","#C1AE75");}
            else if(n=="HIGH CONTRAST"){Set("LcdBackgroundColor","#101419");Set("LcdTextColor","#E8ECF1");Set("LcdAccentColor","#F2C94C");}
            else {Set("LcdBackgroundColor","#07090B");Set("LcdTextColor","#F1F3F5");Set("LcdAccentColor","#D52B2B");name="WAR ROOM";}
            Set("LcdTheme",name);Save();
        }

        internal void ApplyTheme(string name)
        {
            string n=(name??"").Trim().ToUpperInvariant();
            if(n=="SE NATIVE"){Set("HudBackgroundColor","#1B252A");Set("HudPanelColor","#1B252A");Set("HudAccentColor","#BFF2FF");Set("HudTextColor","#D1E8EF");Set("HudDimColor","#829CA8");Set("HudBorderColor","#566E79");}
            else if(n=="GRAPHITE")
            {
                Set("HudBackgroundColor","#202327");Set("HudPanelColor","#2A2E33");Set("HudAccentColor","#AEB5BC");Set("HudTextColor","#D7D9DC");Set("HudDimColor","#A9ADB2");Set("HudBorderColor","#666C72");
                Set("MenuBackgroundColor","#202327");Set("MenuPanelColor","#2A2E33");Set("MenuTextColor","#D7D9DC");Set("MenuAccentColor","#AEB5BC");
            }
            else if(n=="MONOCHROME")
            {
                Set("HudBackgroundColor","#1F1F1F");Set("HudPanelColor","#292929");Set("HudAccentColor","#A8A8A8");Set("HudTextColor","#D8D8D8");Set("HudDimColor","#A5A5A5");Set("HudBorderColor","#686868");
                Set("MenuBackgroundColor","#1F1F1F");Set("MenuPanelColor","#292929");Set("MenuTextColor","#D8D8D8");Set("MenuAccentColor","#A8A8A8");
            }
            else if(n=="AMBER")
            {
                Set("HudBackgroundColor","#211F1A");Set("HudPanelColor","#2B2923");Set("HudAccentColor","#C1AE75");Set("HudTextColor","#E3D6B0");Set("HudDimColor","#B5A77E");Set("HudBorderColor","#746A50");
                Set("MenuBackgroundColor","#211F1A");Set("MenuPanelColor","#2B2923");Set("MenuTextColor","#E3D6B0");Set("MenuAccentColor","#C1AE75");
            }
            else if(n=="HIGH CONTRAST")
            {
                Set("HudBackgroundColor","#101419");Set("HudPanelColor","#1A2026");Set("HudAccentColor","#F2C94C");Set("HudTextColor","#E8ECF1");Set("HudDimColor","#B8BEC5");Set("HudBorderColor","#7B858E");
                Set("MenuBackgroundColor","#101419");Set("MenuPanelColor","#1A2026");Set("MenuTextColor","#E8ECF1");Set("MenuAccentColor","#F2C94C");
            }
            else
            {
                Set("HudBackgroundColor","#07090B");Set("HudPanelColor","#111419");Set("HudAccentColor","#D52B2B");Set("HudTextColor","#F1F3F5");Set("HudDimColor","#9CA3AB");Set("HudBorderColor","#414850");
                Set("MenuBackgroundColor","#07090B");Set("MenuPanelColor","#111419");Set("MenuTextColor","#F1F3F5");Set("MenuAccentColor","#D52B2B");name="WAR ROOM";
            }
            Set("ThemePreset",name);Save();
        }
    }
}
