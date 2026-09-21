using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZeoNav
{
    internal sealed class NavUiHost
    {
        public NavUiStore Store;
        public Func<NavSnapshot> Snapshot;
        public Action<NavCommand> Command;
        public Action Legacy, EnsureOverlay;
        public Action<string> Log;
        public GpsDto Selected;
        public NavLayoutDraft Layout;
        public NavLayoutBounds Bounds;
        public DateTime BoundsUtc;
        public void Receive(NavLayoutBounds bounds)
        {
            if(Layout==null || bounds==null || bounds.Token!=Layout.Token || bounds.ViewportW<200 || bounds.ViewportH<200 ||
                !Finite(bounds.X) || !Finite(bounds.Y) || !Finite(bounds.Width) || !Finite(bounds.Height) || bounds.Width<=0 || bounds.Height<=0) return;
            Bounds=bounds; BoundsUtc=DateTime.UtcNow;
        }
        private static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        public void Select(GpsDto gps)
        {
            Selected=gps;
            if(gps!=null) Command(new NavCommand { Type="SELECT_GPS",Name=gps.Name,X=gps.X,Y=gps.Y,Z=gps.Z });
        }
        public void Start()
        {
            if(Selected==null) throw new InvalidOperationException("Select a GPS destination first.");
            var g=Selected;
            Command(new NavCommand { Type="START",Name=g.Name,X=g.X,Y=g.Y,Z=g.Z,Value=Store.Read().BufferKm });
        }
        public void SyncCore()
        {
            string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Pulsar","ZeoCore","hud-settings.json");
            if(!File.Exists(path)) throw new IOException("ZeoCore appearance file was not found.");
            Store.Apply(CoreChanges(File.ReadAllText(path)));
        }
        // Same field mapping as Nav's legacy MATCH action. Core is read-only.
        internal static Dictionary<string,object> CoreChanges(string json)
        {
            var changes=new Dictionary<string,object>();
            Action<string,string,string[]> choice=(source,target,values)=> {
                double n=Number(json,source); if(!double.IsNaN(n) && n>=0 && n<values.Length && n==Math.Floor(n)) changes[target]=values[(int)n];
            };
            choice("FrameStyle","Frame",NavUiCatalog.Frames); choice("ThemePreset","Theme",NavUiCatalog.Themes); choice("FontStyle","FontStyle",NavUiCatalog.Fonts);
            string[] input={"TextScale","FlightScale","PanelPaddingScale","BorderWidth","PanelOpacity"};
            string[] output={"GlobalScale","PanelScale","InnerPadding","BorderWidth","BackingOpacity"};
            for(int i=0;i<input.Length;i++) { double n=Number(json,input[i]); if(!double.IsNaN(n) && !double.IsInfinity(n)) changes[output[i]]=n; }
            if(Number(json,"ThemePreset")==4)
            {
                string[] from={"HudTextColor","HudSecondaryColor","HudPanelColor","HudBorderColor","SpectrumColor","FriendlyColor","FocusColor","HostileColor","MenuBackgroundColor","MenuPanelColor","MenuTextColor","MenuAccentColor"};
                string[] to={"HudText","SecondaryText","PanelBacking","PanelBorder","Accent","Good","Warning","Danger","MenuBackground","MenuPanel","MenuText","MenuAccent"};
                for(int i=0;i<from.Length;i++)
                {
                    var match=Regex.Match(json,"\""+from[i]+"\"\\s*:\\s*\"(#[0-9A-Fa-f]{6})\"",RegexOptions.IgnoreCase);
                    if(match.Success) changes[to[i]]=match.Groups[1].Value.ToUpperInvariant();
                }
            }
            if(changes.Count==0) throw new InvalidDataException("No compatible Core appearance fields found.");
            return changes;
        }
        private static double Number(string json,string name)
        {
            var match=Regex.Match(json,"\""+name+"\"\\s*:\\s*([-+0-9.eE]+)",RegexOptions.IgnoreCase);
            double value;
            return match.Success && double.TryParse(match.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out value) ? value : double.NaN;
        }
    }

    internal sealed class NavUiModel
    {
        private readonly NavUiHost host;
        public NavConfig Current { get; private set; }
        public NavUiModel(NavUiHost host) { this.host=host; Reload(); }
        public void Reload() { Current=host.Store.Read(); }
        public void Apply(NavOption option,object value)
        {
            if(option.Key=="@PRESET") Current=host.Store.Apply(NavUiCatalog.Preset(Array.IndexOf(option.Choices,(string)value)));
            else if(option.Key=="@RESET_TRIP") Current=host.Store.Apply(NavUiCatalog.ResetTrip());
            else if(option.Key=="@SYNC_CORE") { host.SyncCore(); Reload(); }
            else Current=host.Store.Apply(NavUiCatalog.Changes(option,value));
        }
    }
}
