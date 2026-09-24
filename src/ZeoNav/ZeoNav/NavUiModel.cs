using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using VRage.Input;

namespace ZeoNav
{
    internal static class NavUiExit
    {
        // Validation and I/O failures must not veto returning to the cockpit.
        public static void SaveValid(Func<bool> commit, Action<string> log)
        {
            try { if(!commit()) log("Native Nav closed; invalid/unsaved edits discarded."); }
            catch(Exception ex) { log("Native Nav closed without saving edits: " + ex.Message); }
        }
    }

    internal enum NavOptionKind { Number, Boolean, Choice, Color, Action }
    internal sealed class NavOption
    {
        public string Page, Section, Key, Label;
        public NavOptionKind Kind;
        public double Min, Max, Step;
        public int Decimals;
        public string[] Choices;
        public object Read(NavConfig c) { return typeof(NavConfig).GetField(Key).GetValue(c); }
        public string Format(NavConfig c)
        {
            object value = Read(c);
            return Kind == NavOptionKind.Number ? Convert.ToDouble(value).ToString("F" + Decimals, CultureInfo.InvariantCulture) : Convert.ToString(value);
        }
        public object Parse(string text)
        {
            if (Kind == NavOptionKind.Color)
            {
                if (!Regex.IsMatch(text ?? "", "^#[0-9A-Fa-f]{6}$")) throw new ArgumentException("Use #RRGGBB (six hex digits).");
                return text.ToUpperInvariant();
            }
            if (Kind == NavOptionKind.Choice)
            {
                string found = Choices.FirstOrDefault(v => v.Equals(text, StringComparison.OrdinalIgnoreCase));
                if (found == null) throw new ArgumentException("Choose a listed value.");
                return found;
            }
            double number;
            if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
                 !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out number)) ||
                 double.IsNaN(number) || double.IsInfinity(number) || number < Min || number > Max)
                throw new ArgumentException("Enter " + Min + " to " + Max + ".");
            if (Decimals == 0 && number != Math.Round(number)) throw new ArgumentException("Use a whole number.");
            return Math.Round(number, Decimals);
        }
    }

    internal static class NavUiCatalog
    {
        public static readonly string[] Pages = { "ROUTE", "TRIP HUD", "STYLE", "KEYS", "ADVANCED" };
        public static readonly string[] Frames = { "SE INDUSTRIAL", "FIGHTER HUD", "MARS TACTICAL", "BELTER UTILITY", "NAVY GLASS", "STEALTH", "WAR ROOM", "COMMAND GRID", "REDLINE", "BLACKSITE", "CHEVRON", "SPLIT WING", "HEX COMMAND", "RAZOR" };
        public static readonly string[] Themes = { "GRAPHITE", "MONOCHROME", "AMBER", "HIGH CONTRAST", "CUSTOM", "WAR ROOM" };
        public static readonly string[] Fonts = { "MATCH HUD", "CONDENSED", "TECH", "STANDARD" };
        public static readonly string[] Presets = { "CUSTOM / KEEP CURRENT", "TOP LEFT", "TOP CENTER", "TOP RIGHT", "CENTER LEFT", "CENTER", "CENTER RIGHT", "BOTTOM LEFT", "BOTTOM CENTER", "BOTTOM RIGHT" };
        public static readonly List<NavOption> Options = Build();
        private static List<NavOption> Build()
        {
            var rows = new List<NavOption>();
            Action<string,string,string,string,double,double,double,int> number = (p,s,k,l,min,max,step,dec) => rows.Add(new NavOption { Page=p,Section=s,Key=k,Label=l,Kind=NavOptionKind.Number,Min=min,Max=max,Step=step,Decimals=dec });
            Action<string,string,string,string> boolean = (p,s,k,l) => rows.Add(new NavOption { Page=p,Section=s,Key=k,Label=l,Kind=NavOptionKind.Boolean });
            Action<string,string,string,string,string[]> choice = (p,s,k,l,values) => rows.Add(new NavOption { Page=p,Section=s,Key=k,Label=l,Kind=NavOptionKind.Choice,Choices=values });
            Action<string,string,string,string> action = (p,s,k,l) => rows.Add(new NavOption { Page=p,Section=s,Key=k,Label=l,Kind=NavOptionKind.Action });
            number("ROUTE","SIGNATURE","MaxDriveSigKm","MAX SIG (km)",5,750,5,0);
            number("ROUTE","DESTINATION","BufferKm","Arrival buffer (km)",0,10,.1,1);
            boolean("ROUTE","CAPTURE","StreamerMode","Streamer mode (external HUD)");
            action("TRIP HUD","PLACEMENT","@LAYOUT","EDIT HUD POSITION");
            choice("TRIP HUD","VISIBILITY","TripPanelVisibility","Trip visibility",new[] {"AUTO","ALWAYS","HIDDEN"});
            boolean("TRIP HUD","APPEARANCE","TripUseHudTheme","Follow ZeoCore appearance");
            choice("TRIP HUD","PLACEMENT","@PRESET","Position preset",Presets);
            number("TRIP HUD","PLACEMENT","HudX","Position X",-.98,.98,.01,2);
            number("TRIP HUD","PLACEMENT","HudY","Position Y",-.98,.98,.01,2);
            number("TRIP HUD","SIZING","HudWidth","Frame width",.5,3,.05,2);
            number("TRIP HUD","SIZING","HudHeight","Frame height / text",.5,3,.05,2);
            number("TRIP HUD","SIZING","PanelScale","Panel scale",.45,2.5,.05,2);
            number("TRIP HUD","SIZING","GlobalScale","Text scale",.5,2.75,.05,2);
            number("TRIP HUD","BACKING","BackingOpacity","Backing opacity",0,245,5,0);
            number("TRIP HUD","BACKING","InnerPadding","Inner padding",.4,2.25,.05,2);
            number("TRIP HUD","BACKING","BorderWidth","Border width",.25,4,.25,2);
            boolean("TRIP HUD","COLOR","StateColors","State-aware navigation colors");
            string[] tripKeys={"TripHudText","TripSecondaryText","TripPanelBacking","TripPanelBorder","TripAccent"};
            string[] tripLabels={"Trip text","Trip secondary text","Trip background","Trip border","Trip accent"};
            for(int i=0;i<tripKeys.Length;i++) rows.Add(new NavOption { Page="TRIP HUD",Section="CUSTOM COLORS",Key=tripKeys[i],Label=tripLabels[i],Kind=NavOptionKind.Color });
            string[] sizes={"Destination","Distance","Speed","Signal","Eta","Phase","Flip","Stop","Progress","Warning"};
            string[] sizeLabels={"Destination","Distance","Speed","Spectrum / signature","ETA","Flight state","Flip countdown","Stop distance","Route progress","Warning text"};
            for(int i=0;i<sizes.Length;i++) number("TRIP HUD","DATA SIZES",sizes[i]+"Scale",sizeLabels[i]+" scale",.5,3,.05,2);
            action("TRIP HUD","RESET","@RESET_TRIP","RESET TRIP HUD");
            choice("STYLE","FRAME","Frame","HUD frame",Frames);
            choice("STYLE","FRAME","FontStyle","HUD font",Fonts);
            choice("STYLE","PALETTE","Theme","Theme preset",Themes);
            action("STYLE","MATCH CORE","@SYNC_CORE","MATCH ZEOCORE APPEARANCE");
            string[] colors={"HudText","SecondaryText","PanelBacking","PanelBorder","Accent","Good","Warning","Danger","MenuBackground","MenuPanel","MenuText","MenuAccent"};
            string[] labels={"HUD text","Secondary text","Panel backing","Panel border","Normal accent","Arrived / good","Flip / warning","Brake / abort","Legacy menu background","Legacy menu panel","Legacy menu text","Legacy menu accent"};
            for(int i=0;i<colors.Length;i++) rows.Add(new NavOption { Page="STYLE",Section=i<8 ? "HUD COLORS" : "LEGACY COLORS",Key=colors[i],Label=labels[i],Kind=NavOptionKind.Color });
            boolean("STYLE","FRAME","AccentRail","Accent rail");
            number("STYLE","FRAME","CornerCut","Corner cut",.25,2.5,.05,2);
            number("STYLE","FRAME","HeaderHeight","Header height",.5,2,.05,2);
            number("STYLE","FRAME","PatternIntensity","Pattern intensity",0,2,.05,2);
            string[] keys={"MenuKey","StartKey","AbortKey","ManualFlipKey","SignalUpKey","SignalDownKey"};
            string[] keyLabels={"Open / close Nav","Start selected route","Abort / release","Manual 180 flip","MAX SIG +5 km","MAX SIG -5 km"};
            string[] values=new[] {"None"}.Concat(Enum.GetNames(typeof(MyKeys)).Where(x=>x!="None").OrderBy(x=>x)).ToArray();
            for(int i=0;i<keys.Length;i++) choice("KEYS","KEYBOARD",keys[i],keyLabels[i],values);
            number("ADVANCED","FLIGHT","FlipTimeSeconds","180 flip allowance (s)",1,60,.5,1);
            number("ADVANCED","FLIGHT","BrakeSafety","Brake safety multiplier",1,2,.01,2);
            number("ADVANCED","ARRIVAL","ArrivalRadiusMeters","Arrival radius (m)",1,100,1,0);
            number("ADVANCED","ARRIVAL","ArrivalSpeedMps","Arrival speed (m/s)",.05,5,.05,2);
            number("ADVANCED","SPEED","SpeedCapOverride","Speed cap (m/s, 0 = AUTO)",0,50000,25,0);
            boolean("ADVANCED","INPUT","AbortOnManualInput","Abort on manual pilot input");
            return rows;
        }
        public static Dictionary<string,object> Changes(NavOption option, object value)
        {
            var changes=new Dictionary<string,object>(); changes[option.Key]=value;
            if(option.Kind==NavOptionKind.Color)
            {
                if(option.Key.StartsWith("Trip",StringComparison.Ordinal)) changes["TripUseHudTheme"]=false;
                else changes["Theme"]="CUSTOM";
            }
            return changes;
        }
        public static Dictionary<string,object> Preset(int index)
        {
            if(index<1 || index>9) return new Dictionary<string,object>();
            double[] x={-.95,-.15,.72}; double[] y={.90,.15,-.72};
            return new Dictionary<string,object> { {"HudX",x[(index-1)%3]}, {"HudY",y[(index-1)/3]} };
        }
        public static Dictionary<string,object> ResetTrip()
        {
            var defaults=new NavConfig();
            string[] keys={"TripPanelVisibility","TripUseHudTheme","HudX","HudY","HudWidth","HudHeight","GlobalScale","PanelScale","BackingOpacity","InnerPadding","BorderWidth","DestinationScale","DistanceScale","SpeedScale","SignalScale","EtaScale","PhaseScale","FlipScale","StopScale","ProgressScale","WarningScale"};
            return keys.ToDictionary(k=>k,k=>typeof(NavConfig).GetField(k).GetValue(defaults));
        }
    }

    internal sealed class NavUiStore
    {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        private static extern bool MoveFileEx(string existing,string destination,uint flags);
        private readonly string path;
        private readonly Func<NavConfig> fallback;
        private readonly Action<NavConfig> applied;
        public NavUiStore(string path,Func<NavConfig> fallback,Action<NavConfig> applied) { this.path=path;this.fallback=fallback;this.applied=applied; }
        public NavConfig Read()
        {
            if(!File.Exists(path)) return fallback().Copy();
            var current=JsonIo.Load<NavConfig>(path);
            if(current==null) throw new IOException("Cannot read current config; existing file was preserved.");
            return ConfigRules.Clamp(current);
        }
        public NavConfig Apply(Dictionary<string,object> changes)
        {
            // All plugin writes run on the game update/UI thread. Reload first so a
            // native page or draft does not overwrite other edits with its old snapshot.
            NavConfig current=Read();
            if(changes.Count==0) { applied(current); return current; }
            foreach(var item in changes)
            {
                FieldInfo field=typeof(NavConfig).GetField(item.Key);
                if(field==null) throw new ArgumentException("Unknown setting: "+item.Key);
                field.SetValue(current,Convert.ChangeType(item.Value,field.FieldType,CultureInfo.InvariantCulture));
            }
            current=ConfigRules.Clamp(current);
            string temp=path+".native-"+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                JsonIo.Save(temp,current);
                if(JsonIo.Load<NavConfig>(temp)==null) throw new IOException("Settings serialization failed.");
                // Atomic same-directory rename. Unlike File.Replace this does not need
                // to copy ACL/metadata permissions, which may be restricted by the host.
                if(!MoveFileEx(temp,path,1|8)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                var verified=Read();
                foreach(string key in changes.Keys)
                {
                    var field=typeof(NavConfig).GetField(key);
                    if(!Equals(field.GetValue(verified),field.GetValue(current))) throw new IOException("Setting did not persist: "+key);
                }
                applied(verified); return verified;
            }
            finally { if(File.Exists(temp)) File.Delete(temp); }
        }
    }
}
