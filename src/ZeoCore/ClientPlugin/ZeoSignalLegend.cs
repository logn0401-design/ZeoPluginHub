using System;
using System.Collections.Generic;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage.Utils;
using VRageMath;
using ZeoOverlay;

namespace ZeoCore
{
    // Menu-only geometry; no entities, sensor reads, textures or work in the HUD update loop.
    internal sealed class ZeoSignalPreview : MyGuiControlBase
    {
        private readonly string _kind;
        private readonly OverlaySettings _settings;
        private readonly List<Vector4> _lines=new List<Vector4>();
        private float _scale=1,_stroke=1.6f;
        private readonly MyGuiControlLabel _id;
        internal static bool Supports(string key){return key.EndsWith("MarkerScale",StringComparison.Ordinal)||key.EndsWith("IdScale",StringComparison.Ordinal)||key=="CrosshairScale";}
        internal ZeoSignalPreview(Vector2 position,string kind,OverlaySettings settings)
            :base(position:position,size:new Vector2(.09f,.042f))
        {
            _kind=kind;_settings=settings;CanHaveFocus=false;
            _id=new MyGuiControlLabel(new Vector2(.018f,0),null,"01",Vector4.One,.45f,null,MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
            Elements.Add(_id);Refresh(null);
        }
        private void Line(float x,float y,float a,float b){_lines.Add(new Vector4(x,y,a,b));}
        private void Polygon(float[] xy){for(int i=0;i<xy.Length;i+=2){int j=(i+2)%xy.Length;Line(xy[i],xy[i+1],xy[j],xy[j+1]);}}
        internal void Refresh(double? draft)
        {
            _lines.Clear();bool friendly=_kind.StartsWith("Friendly"),shared=_kind.StartsWith("Shared"),distress=_kind=="Distress",stale=_kind=="Stale";
            double basis=friendly?_settings.FriendlyMarkerScale:shared?_settings.SharedMarkerScale:_kind=="HostileMarkerScale"?_settings.HostileMarkerScale:_settings.SpectrumMarkerScale;
            if(draft.HasValue&&_kind.EndsWith("MarkerScale")&&_kind!="MaxMarkerScale"&&_kind!="FocusMarkerScale"&&_kind!="OffscreenMarkerScale")basis=draft.Value;
            double cap=_kind=="MaxMarkerScale"&&draft.HasValue?draft.Value:_settings.MaxMarkerScale;
            double focus=_kind=="FocusMarkerScale"?(draft??_settings.FocusMarkerScale):1;
            double off=_kind=="OffscreenMarkerScale"?(draft??_settings.OffscreenMarkerScale):1;
            _scale=(float)MarkerSizing.Scale(_kind=="MaxMarkerScale"?3:basis,_settings.MarkerPreviewDistanceKm*1000,cap,focus,off);
            _stroke=Math.Max(1.2f,1.55f*_scale);
            float r=Math.Max(6,9.5f*_scale);
            string hex=distress?_settings.DistressColor:stale?_settings.StaleColor:friendly?_settings.FriendlyColor:shared?_settings.SharedTrackColor:_kind=="HostileMarkerScale"?_settings.HostileColor:_kind=="FocusMarkerScale"?_settings.FocusColor:_settings.SpectrumColor;
            if(_kind=="SharedHostile"||_kind=="HostileMarkerScale")hex=_settings.ThemePreset==4?_settings.HostileColor:"#F04444";
            else if(!friendly&&!shared&&!distress&&!stale&&_kind!="FocusMarkerScale"&&_settings.ThemePreset!=4)hex="#FFB84A";
            if(_kind=="Attack")hex="#FFC537";if(_kind=="CrosshairScale")hex=_settings.CrosshairColor;
            ColorMask=ZeoNativeSettingsScreen.HexColor(hex);_id.ColorMask=ColorMask;
            double ids=friendly?_settings.FriendlyIdScale:shared?_settings.SharedIdScale:_settings.SpectrumIdScale;
            if(_kind.EndsWith("IdScale")&&draft.HasValue)ids=draft.Value;
            _id.TextScale=(float)Math.Max(.25,.45*_scale*ids);
            _id.Position=MyGuiManager.GetNormalizedCoordinateFromScreenCoordinate(new Vector2(r+4*_scale,0))-MyGuiManager.GetNormalizedCoordinateFromScreenCoordinate(Vector2.Zero);
            _id.Visible=_kind!="CrosshairScale"&&_kind!="Anchor";
            if(friendly){for(int i=0;i<24;i++){double a=i*Math.PI/12,b=(i+1)*Math.PI/12;Line((float)Math.Cos(a)*r,(float)Math.Sin(a)*r,(float)Math.Cos(b)*r,(float)Math.Sin(b)*r);}Polygon(new[]{0f,-r*.52f,-r*.38f,r*.364f,0,r*.13f,r*.38f,r*.364f});}
            else if(shared){if(_kind=="SharedHostile")Polygon(new[]{0f,-r,r*.92f,r*.78f,-r*.92f,r*.78f});else if(_kind=="SharedUnknown")Polygon(new[]{-r,-r,r,-r,r,r,-r,r});else Polygon(new[]{0f,-r,r,0,0,r,-r,0});}
            else if(distress){r=Math.Max(9,12*_scale);_stroke=Math.Max(1.7f,2.1f*_scale);Polygon(new[]{-r*.55f,-r,r*.55f,-r,r,0,r*.55f,r,-r*.55f,r,-r,0});Line(-r*.42f,0,r*.42f,0);Line(0,-r*.42f,0,r*.42f);_id.Text="SOS";}
            else if(_kind=="OffscreenMarkerScale"){_stroke=Math.Max(1.2f,1.6f*_scale);Polygon(new[]{13f*_scale,0,-4*_scale,7*_scale,-4*_scale,-7*_scale});}
            else if(_kind=="Anchor"){Line(-1,0,1,0);}
            else if(_kind=="CrosshairScale"){r=14*(float)(draft??_settings.CrosshairScale);_stroke=Math.Max(1,r/14);Line(-r,0,-r*.30f,0);Line(r*.30f,0,r,0);Line(0,-r,0,-r*.30f);Line(0,r*.30f,0,r);}
            else {_stroke=Math.Max(1.6f,1.9f*_scale);float a=8.5f*_scale,b=17.5f*_scale,c=7.98f*_scale;Line(-b,-c,-a,0);Line(-a,0,-b,c);Line(b,-c,a,0);Line(a,0,b,c);}
            if(!friendly&&!shared&&!distress&&_kind!="OffscreenMarkerScale"&&_kind!="Anchor"&&_kind!="CrosshairScale"){
                _id.OriginAlign=MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER;
                _id.Position=MyGuiManager.GetNormalizedCoordinateFromScreenCoordinate(new Vector2(-29.5f*_scale,0))-MyGuiManager.GetNormalizedCoordinateFromScreenCoordinate(Vector2.Zero);
            }
            if(_kind=="Attack"){float a=22*_scale,c=7*_scale;Line(-a,-a,-a+c,-a);Line(-a,-a,-a,-a+c);Line(a-c,-a,a,-a);Line(a,-a,a,-a+c);Line(-a,a-c,-a,a);Line(-a,a,-a+c,a);Line(a-c,a,a,a);Line(a,a-c,a,a);}
            SetToolTip("Actual icon pixels at "+_settings.MarkerPreviewDistanceKm+" km. Effective scale: "+_scale.ToString("0.00")+(_scale>=cap?" (MAX CAP)":"")+". IDs use the native menu font.");
        }
        public override void Draw(float transitionAlpha,float backgroundTransitionAlpha)
        {
            base.Draw(transitionAlpha,backgroundTransitionAlpha);
            var center=MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(GetPositionAbsoluteCenter());
            var color=new Color(ColorMask*new Vector4(1,1,1,transitionAlpha));
            foreach(var line in _lines){var a=center+new Vector2(line.X,line.Y);var b=center+new Vector2(line.Z,line.W);var delta=b-a;
                var point=MyGuiManager.GetNormalizedCoordinateFromScreenCoordinate((a+b)*.5f);
                var size=MyGuiManager.GetNormalizedCoordinateFromScreenCoordinate(new Vector2(delta.Length(),_stroke))-MyGuiManager.GetNormalizedCoordinateFromScreenCoordinate(Vector2.Zero);
                MyGuiManager.DrawSpriteBatch("Textures\\GUI\\Blank.dds",point,size,color,MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,rotation:(float)Math.Atan2(delta.Y,delta.X));
            }
        }
    }
    internal sealed class ZeoSignalLegendScreen : MyGuiScreenBase
    {
        internal ZeoSignalLegendScreen(OverlaySettings settings):base(new Vector2(.5f),new Vector4(.105f,.145f,.165f,.97f),new Vector2(.88f,.84f),true)
        {
            DrawMouseCursor=true;CloseButtonEnabled=true;EnabledBackgroundFade=true;
            AddCaption("ZEO // SIGNAL LEGEND",Vector4.One,new Vector2(0,-.37f),.85f);
            string[] kinds={"SpectrumMarkerScale","HostileMarkerScale","FriendlyMarkerScale","SharedMarkerScale","SharedUnknown","SharedHostile","Distress","OffscreenMarkerScale","Attack"};
            string[] labels={"LOCAL SPECTRUM  //  inward brackets > <","LOCAL ENEMY  //  same > <, hostile color","FRIENDLY  //  circle + ship symbol (not heading)","SHARED SPECTRUM  //  diamond","SHARED SENSOR CONTACT  //  non-Spectrum, square","SHARED HOSTILE  //  confirmed shared report, triangle","DISTRESS  //  hexagon + cross / SOS","OFF SCREEN  //  edge triangle + track ID","ATTACK TARGET  //  yellow/red pulse; intent, not hostility"};
            for(int i=0;i<kinds.Length;i++){float y=-.28f+i*.052f;Controls.Add(new ZeoSignalPreview(new Vector2(-.345f,y),kinds[i],settings));Controls.Add(new MyGuiControlLabel(new Vector2(-.26f,y),null,labels[i],Vector4.One,.55f,null,MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));}
            string[] notes={"Relationship / state overrides base colors. Custom palette: THEME > Custom.","Focus: highlight color only. Corner brackets are reserved for ATTACK.","Stale: faded last observation. Center dot: optional alignment aid, not another contact.","ATTACK: flashing corners + ATTACK around the current icon. Relationship unchanged."};
            for(int i=0;i<notes.Length;i++)Controls.Add(new MyGuiControlLabel(new Vector2(-.39f,.245f+i*.031f),null,notes[i],new Vector4(.75f,.83f,.85f,1),.46f,null,MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
        }
        public override string GetFriendlyName(){return "ZeoSignalLegend";}
    }
}
