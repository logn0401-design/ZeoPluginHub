using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace ZeosOreHelper
{
    internal sealed class OreLcdRenderer
    {
        private readonly List<IMyTextPanel> _panels=new List<IMyTextPanel>();
        private int _lastScanFrame=-10000;
        private bool _clearedForStreamer;

        internal void Update(int frame,HudSettings s,OreOverlayFrame data)
        {
            if(frame-_lastScanFrame>=300){_lastScanFrame=frame;Scan(s);_clearedForStreamer=false;}
            if(s.StreamerMode)
            {
                if(!_clearedForStreamer){Clear();_clearedForStreamer=true;}
                return;
            }
            _clearedForStreamer=false;
            if(!s.LcdEnabled||data==null||!s.Enabled){Clear();return;}
            for(int i=0;i<_panels.Count;i++)
            {
                try{var p=_panels[i];if(p!=null)DrawPanel(p,s,data);}catch{}
            }
        }

        private void Scan(HudSettings s)
        {
            _panels.Clear();
            try
            {
                var session=MyAPIGateway.Session;var player=session==null?null:session.Player;if(player==null||player.Controller==null||player.Controller.ControlledEntity==null)return;
                var block=player.Controller.ControlledEntity.Entity as VRage.Game.ModAPI.IMyCubeBlock;if(block==null||block.CubeGrid==null)return;
                var ts=MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(block.CubeGrid);if(ts==null)return;
                ts.GetBlocksOfType<IMyTextPanel>(_panels,p=>p!=null&&p.CustomName.IndexOf(s.LcdTag??"Zeo Ore LCD",StringComparison.OrdinalIgnoreCase)>=0);
            }
            catch{}
        }

        private static void DrawPanel(IMyTextPanel p,HudSettings s,OreOverlayFrame data)
        {
            p.ContentType=ContentType.SCRIPT;
            try{p.Script="";}catch{}
            Color bg=ParseColor(s.LcdLinkHudTheme?s.HudBackgroundColor:s.LcdBackgroundColor,new Color(9,6,7));
            Color fg=ParseColor(s.LcdLinkHudTheme?s.HudTextColor:s.LcdTextColor,new Color(232,234,237));
            Color accent=ParseColor(s.LcdLinkHudTheme?s.HudAccentColor:s.LcdAccentColor,new Color(216,59,59));
            Color dim=ParseColor(s.LcdLinkHudTheme?s.HudDimColor:s.LcdTextColor,new Color(140,146,153));
            Vector2 surf=p.SurfaceSize,tex=p.TextureSize,off=(tex-surf)*0.5f;
            string body=BuildBody(s,data);
            using(var f=p.DrawFrame())
            {
                f.Add(new MySprite{Type=SpriteType.TEXTURE,Data="SquareSimple",Position=off+surf*0.5f,Size=surf,Color=bg});
                f.Add(new MySprite{Type=SpriteType.TEXTURE,Data="SquareSimple",Position=off+new Vector2(surf.X*0.5f,7f),Size=new Vector2(surf.X,6f),Color=accent});
                f.Add(new MySprite{Type=SpriteType.TEXT,Data="ZEO // ORE HELPER",Position=off+new Vector2(18f,18f),RotationOrScale=Math.Max(0.42f,s.LcdFontScale*0.82f),Color=fg,FontId="Monospace",Alignment=TextAlignment.LEFT});
                f.Add(new MySprite{Type=SpriteType.TEXT,Data=(s.LcdRole??"Ranking").ToUpperInvariant()+"  //  "+(s.ActivePreset??"Balanced").ToUpperInvariant(),Position=off+new Vector2(18f,48f),RotationOrScale=Math.Max(0.32f,s.LcdFontScale*0.55f),Color=dim,FontId="Monospace",Alignment=TextAlignment.LEFT});
                f.Add(new MySprite{Type=SpriteType.TEXTURE,Data="SquareSimple",Position=off+new Vector2(surf.X*0.5f,72f),Size=new Vector2(Math.Max(10f,surf.X-30f),2f),Color=accent});
                f.Add(new MySprite{Type=SpriteType.TEXT,Data=body,Position=off+new Vector2(18f,84f),RotationOrScale=Math.Max(0.32f,s.LcdFontScale),Color=fg,FontId="Monospace",Alignment=TextAlignment.LEFT});
            }
        }

        private static string BuildBody(HudSettings s,OreOverlayFrame f)
        {
            var b=new StringBuilder();
            if(string.Equals(s.LcdRole,"Status",StringComparison.OrdinalIgnoreCase))
            {
                b.Append("STATUS   ").Append(f.HelperEnabled?"ACTIVE":"OFF").AppendLine();
                b.Append("VISIBLE  ").Append(f.VisibleCount).AppendLine();
                b.Append("READ     ").Append(f.ReadyCount).AppendLine();
                b.Append("PENDING  ").Append(f.PendingCount).AppendLine();
                b.Append("ERRORS   ").Append(f.ErrorCount).AppendLine();
                b.Append("CACHE    ").Append(f.CachedCount);
                return b.ToString();
            }
            OreOverlayRoid selected=null;for(int i=0;i<f.Roids.Count;i++)if(f.Roids[i].Selected){selected=f.Roids[i];break;}
            if(string.Equals(s.LcdRole,"Target",StringComparison.OrdinalIgnoreCase))
            {
                if(selected==null)return "TARGET  --\n\nLook at a surveyed asteroid.";
                b.Append("TARGET  ").Append(selected.Number.ToString("00")).Append("   ").Append(selected.MustHit?"S!":selected.Grade).AppendLine();
                b.Append("DIST    ").Append(FormatRange(selected.DistanceMeters)).AppendLine();
                b.Append("DIA     ").Append(FormatSize(selected.DiameterMeters)).AppendLine();
                b.Append("TOP     ").Append(string.IsNullOrWhiteSpace(selected.TopOre)?"--":selected.TopOre+" "+selected.TopOrePercent.ToString("0.00")+"%").AppendLine();
                if(!string.IsNullOrWhiteSpace(selected.SecondOre))b.Append("SECOND  ").Append(selected.SecondOre).Append(' ').Append(selected.SecondOrePercent.ToString("0.00")).Append('%');
                return b.ToString();
            }
            int rows=Math.Max(1,Math.Min(30,s.LcdRows)),drawn=0;
            if(string.Equals(s.LcdRole,"Ore Summary",StringComparison.OrdinalIgnoreCase))b.AppendLine("#  G   DIST    ORE");
            else b.AppendLine("#  G   DIST    DIA    ORE");
            for(int i=0;i<f.Roids.Count&&drawn<rows;i++)
            {
                var r=f.Roids[i];if(!r.ListEligible)continue;if(string.Equals(s.LcdRole,"Ore Summary",StringComparison.OrdinalIgnoreCase)&&string.IsNullOrWhiteSpace(r.TopOre))continue;
                b.Append(r.Pinned?'*':' ').Append(r.Number.ToString("00")).Append(' ').Append((r.MustHit?"S!":r.Grade).PadRight(2)).Append(' ').Append(FormatRange(r.DistanceMeters).PadRight(7));
                if(!string.Equals(s.LcdRole,"Ore Summary",StringComparison.OrdinalIgnoreCase))b.Append(FormatSize(r.DiameterMeters).PadRight(7));
                if(!string.IsNullOrWhiteSpace(r.TopOre))b.Append(VoxelSurveyor.OreCode(r.TopOre)).Append(' ').Append(r.TopOrePercent.ToString("0.00")).Append('%');
                b.AppendLine();drawn++;
            }
            if(drawn==0)b.AppendLine("-- no qualifying asteroids --");
            return b.ToString();
        }

        private void Clear()
        {
            for(int i=0;i<_panels.Count;i++)try{var p=_panels[i];if(p==null)continue;p.ContentType=ContentType.TEXT_AND_IMAGE;p.WriteText("",false);}catch{}
        }
        private static string FormatRange(double m){return m>=10000?(m/1000.0).ToString("0")+"k":m>=1000?(m/1000.0).ToString("0.0")+"k":m.ToString("0")+"m";}
        private static string FormatSize(double m){return m<=0?"--":m>=1000?(m/1000.0).ToString("0.0")+"k":m.ToString("0")+"m";}
        private static Color ParseColor(string s,Color fallback){try{if(!string.IsNullOrWhiteSpace(s)&&s.Length==7&&s[0]=='#'){int v=Convert.ToInt32(s.Substring(1),16);return new Color((byte)((v>>16)&255),(byte)((v>>8)&255),(byte)(v&255));}}catch{}return fallback;}
    }
}
