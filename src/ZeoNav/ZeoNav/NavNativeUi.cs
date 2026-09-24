using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace ZeoNav
{
    internal static class NavNativeUi
    {
        private static NavNativeSettingsScreen screen;
        private static NavHudLayoutScreen editor;
        public static bool IsOpen { get { return (screen!=null && screen.State!=MyGuiScreenState.CLOSED) || (editor!=null && editor.State!=MyGuiScreenState.CLOSED); } }
        public static bool Toggle(NavUiHost host)
        {
            try
            {
                if(editor!=null && editor.State!=MyGuiScreenState.CLOSED) { editor.CloseScreen(); return true; }
                if(screen!=null && screen.State!=MyGuiScreenState.CLOSED) { screen.CloseScreen(); return true; }
                var next=new NavNativeSettingsScreen(host);
                next.Closed+=delegate { if(ReferenceEquals(screen,next)) screen=null; };
                screen=next; MyGuiSandbox.AddScreen(next); host.Log("Native compact Nav settings opened."); return true;
            }
            catch(Exception ex) { screen=null; host.Log("Native Nav UI failed: "+ex); return false; }
        }
        public static void BeginLayout(NavUiHost host)
        {
            try
            {
                host.EnsureOverlay();
                var next=new NavHudLayoutScreen(host); editor=next;
                next.Closed+=delegate { if(ReferenceEquals(editor,next)) editor=null; };
                MyGuiSandbox.AddScreen(next);
            }
            catch(Exception ex) { host.Layout=null; host.Bounds=null; host.Log("Nav layout UI failed: "+ex); }
        }
        public static void Close()
        {
            try { if(screen!=null) screen.CloseScreen(true); } catch { }
            try { if(editor!=null) editor.CloseScreen(true); } catch { }
            screen=null; editor=null;
        }
    }

    internal sealed class NavNativeSettingsScreen : MyGuiScreenBase
    {
        internal const int RowsPerView=6;
        private readonly NavUiHost host;
        private readonly NavUiModel _model;
        private readonly List<Func<bool>> _editors=new List<Func<bool>>();
        private static readonly int[] LastViews=new int[5];
        private static int LastPage;
        private int _page;
        private bool _building,_rebuild,_committing;
        private string _message="ENTER / APPLY saves. Closing discards invalid edits.";
        private MyGuiControlLabel _status,_ship,_trip,_signal,_approach;
        private NavNativeColorScreen _colorScreen;
        private DateTime liveAt=DateTime.MinValue;
        private string _gpsQuery="";
        private bool _filteringGps;
        private MyGuiControlCombobox _gpsCombo;
        private MyGuiControlTextbox _gpsSearch;
        private List<GpsDto> _gpsChoices=new List<GpsDto>();
        private List<GpsDto> _gpsSource=new List<GpsDto>();
        internal NavNativeSettingsScreen(NavUiHost host)
            : base(new Vector2(.5f,.5f),new Vector4(.105f,.145f,.165f,.97f),new Vector2(.80f,.78f),true)
        {
            this.host=host; _model=new NavUiModel(host); _page=LastPage;
            DrawMouseCursor=true; CloseButtonEnabled=true; EnabledBackgroundFade=true;
            CanHideOthers=false; CanBeHidden=false; BuildControls();
        }
        public override string GetFriendlyName() { return "ZeoNavNativeSettings"; }
        public override bool Update(bool hasFocus)
        {
            bool result=base.Update(hasFocus);
            if(_rebuild && !_building && hasFocus && State==MyGuiScreenState.OPENED)
            {
                _rebuild=false;
                try { BuildControls(); }
                catch(Exception ex) { Message("Settings could not reload: "+ex.Message); host.Log("Native Nav reload failed: "+ex.Message); }
            }
            if((DateTime.UtcNow-liveAt).TotalMilliseconds>=250)
            {
                liveAt=DateTime.UtcNow;
                NavSnapshot s=host.Snapshot();
                if(_gpsCombo!=null && !GpsSearch.SameList(_gpsSource,s.Gps)) RefreshGpsChoices();
                if(_ship!=null) { _ship.Text=Short("MAIN "+s.ForwardWorkingMainDriveCount+"/"+s.ForwardMainDriveCount+" READY // "+s.Phase+" // "+s.Ship,64); _ship.SetToolTip(s.DriveScanSummary??""); }
                if(_trip!=null) { _trip.Text="SPD "+s.SpeedMps.ToString("0")+" m/s  //  ETA "+(s.EtaSeconds>=0 ? TimeSpan.FromSeconds(s.EtaSeconds).ToString(@"hh\:mm\:ss") : "WAIT")+"  //  CMD "+(s.ForwardCommandRatio*100).ToString("0")+"%"; }
                if(_signal!=null) { _signal.Text=Short(s.SpectrumKmReady ? "OWN SIG "+s.SpectrumDriveKm.ToString("0.0")+" / "+s.MaxDriveSigKm.ToString("0")+" km  //  "+s.SignalGovernorState : s.SignalGovernorState,83); _signal.SetToolTip((s.WarningText??"")+"\n"+s.SpectrumKmSource+"\nSpherical strong/weak: "+s.SphericalStrongKm.ToString("0.0")+" / "+s.SphericalWeakKm.ToString("0.0")+" km\nDirectional strong/weak: "+s.DirectionalStrongKm.ToString("0.0")+" / "+s.DirectionalWeakKm.ToString("0.0")+" km"); }
                if(_approach!=null)
                {
                    _approach.Text="DIST "+(s.DistanceMeters/1000).ToString("0.0")+" km  //  FLIP "+(s.FlipInSeconds>=0 ? s.FlipInSeconds.ToString("0")+" s" : "--")+"  //  STOP "+(s.StopDistanceMeters/1000).ToString("0.0")+" km";
                    _approach.SetToolTip("Cap "+s.SpeedCapMps.ToString("0.0")+" m/s: "+s.SpeedCapSource+"\nOwn grid "+s.SpectrumSelfEmitterId+" / sample age "+s.SpectrumSelfAgeFrames+" frames\n"+s.WarningText);
                }
            }
            return result;
        }
        public override bool CloseScreen(bool isUnloading=false)
        {
            if(_colorScreen!=null && _colorScreen.State!=MyGuiScreenState.CLOSED)
            {
                _colorScreen.CloseScreen(isUnloading); _colorScreen=null;
            }
            // Closing must always return control to the pilot, including after invalid
            // input or a failed settings write. Explicit Apply/navigation still validate.
            if(!isUnloading) NavUiExit.SaveValid(CommitEditors, host.Log);
            _rebuild=false;
            return base.CloseScreen(isUnloading);
        }
        private void BuildControls()
        {
            _building=true;
            try
            {
                _model.Reload(); FocusedControl=null; Controls.Clear(); _editors.Clear(); _ship=_trip=_signal=_approach=null;
                _gpsCombo=null; _gpsSearch=null;
                AddCaption("ZEO NAV // FLIGHT CONTROL",new Vector4(.82f,.91f,.94f,1),new Vector2(0,-.346f),.82f);
                for(int i=0;i<NavUiCatalog.Pages.Length;i++)
                {
                    int target=i;
                    var tab=Button(-.29f+i*.145f,-.287f,.137f,.044f,NavUiCatalog.Pages[i],delegate { if(!CommitEditors()) return; _page=target; LastPage=target; _rebuild=true; },.60f);
                    tab.Selected=i==_page;
                    tab.ColorMask=i==_page ? new Vector4(.75f,.95f,1,1) : new Vector4(.48f,.58f,.63f,1);
                }
                var rows=NavUiCatalog.Options.Where(o=>o.Page==NavUiCatalog.Pages[_page]).ToArray();
                int views=Math.Max(1,(rows.Length+RowsPerView-1)/RowsPerView);
                int view=LastViews[_page]=Math.Max(0,Math.Min(views-1,LastViews[_page]));
                Label(-.354f,-.231f,NavUiCatalog.Pages[_page]+(_page==0 ? "  //  v1.0.2" : "  /  "+(view+1)+" OF "+views),.68f);
                if(_page==0)
                {
                    BuildGps();
                    for(int i=0;i<rows.Length;i++) AddRow(rows[i],-.095f+i*.059f);
                    _ship=Label(-.354f,.103f,"",.56f);
                    _trip=Label(-.354f,.134f,"",.58f);
                    _signal=Label(-.354f,.165f,"",.48f);
                    _approach=Label(-.354f,.196f,"",.48f);
                    Button(-.18f,.241f,.30f,.044f,"START ROUTE",delegate { RunAction(delegate { host.Start(); }); },.65f);
                    Button(.18f,.241f,.30f,.044f,"MANUAL 180 FLIP",delegate { RunAction(delegate { host.Command(new NavCommand {Type="FLIP"}); }); },.62f);
                }
                else
                {
                    for(int i=0;i<RowsPerView && view*RowsPerView+i<rows.Length;i++) AddRow(rows[view*RowsPerView+i],-.163f+i*.059f);
                    Button(-.263f,.224f,.18f,.040f,"PREVIOUS",delegate { Navigate(-1); },.58f).Enabled=view>0;
                    Button(.263f,.224f,.18f,.040f,"NEXT",delegate { Navigate(1); },.58f).Enabled=view+1<views;
                    if(_page==1) Button(0,.224f,.29f,.040f,"EDIT HUD POSITION",delegate { if(CommitEditors() && CloseScreen()) NavNativeUi.BeginLayout(host); },.55f);
                }
                Label(-.354f,.281f,_page==2 ? "RGB / hex colors. Menu colors apply to the legacy window." : "Native settings are capturable. External HUD keeps streamer mode.",.46f);
                _status=Label(-.354f,.309f,Short(_message,92),.46f);
                Button(-.231f,.353f,.252f,.043f,"FULL / LEGACY SETTINGS",OpenExternal,.53f);
                Button(.086f,.353f,.155f,.043f,"ABORT",delegate { host.Command(new NavCommand {Type="ABORT"}); Message("Flight control released."); },.62f);
                Button(.281f,.353f,.155f,.043f,"CLOSE",delegate { CloseScreen(); },.62f);
            }
            finally { _building=false; }
        }
        private void BuildGps()
        {
            Label(-.354f,-.197f,"SEARCH GPS",.42f);
            _gpsSearch=new MyGuiControlTextbox(new Vector2(-.238f,-.166f),_gpsQuery,128,null,.60f);
            _gpsSearch.Size=new Vector2(.232f,.043f);
            _gpsSearch.SetToolTip("Type any part of a GPS name. Multiple words narrow the list. Clear to show all. Choose a result before START ROUTE.");
            Controls.Add(_gpsSearch);
            var combo=_gpsCombo=new MyGuiControlCombobox(new Vector2(.123f,-.166f),new Vector2(.462f,.043f),openAreaItemsCount:7,
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,isAutoscaleEnabled:true,isAutoEllipsisEnabled:true,minTextScale:.55f);
            combo.ItemSelected+=delegate {
                int i=(int)combo.GetSelectedKey();
                if(_building || _filteringGps) return;
                if(i<0) { host.Select(null); return; }
                if(i>=_gpsChoices.Count) return;
                if(CommitEditors()) host.Select(_gpsChoices[i]);
                else RefreshGpsChoices();
            };
            Controls.Add(combo);
            RefreshGpsChoices();
            _gpsSearch.TextChanged+=delegate {
                if(_building) return;
                _gpsQuery=_gpsSearch.Text; RefreshGpsChoices();
            };
            _gpsSearch.EnterPressed+=delegate { FocusedControl=combo; };
        }
        private void RefreshGpsChoices()
        {
            if(_gpsCombo==null) return;
            _filteringGps=true;
            try
            {
                var all=host.Snapshot().Gps;
                _gpsSource=GpsSearch.Filter(all,"");
                _gpsChoices=GpsSearch.Filter(all,_gpsQuery);
                int selected=GpsSearch.SelectedIndex(_gpsChoices,host.Selected);
                // Filtering never silently selects a different destination or starts a route.
                if(selected<0) host.Select(null);
                _gpsCombo.ClearItems();
                _gpsCombo.AddItem(-1,_gpsChoices.Count==0 ? (all==null || all.Count==0 ? "No GPS destinations available" : "No matching GPS — clear search") : "Select GPS ("+_gpsChoices.Count+" matches)");
                for(int i=0;i<_gpsChoices.Count;i++)
                {
                    var g=_gpsChoices[i];
                    _gpsCombo.AddItem(i,(g.Name ?? "Unnamed GPS")+"  ("+(g.Distance/1000).ToString("0.0")+" km)");
                }
                _gpsCombo.SelectItemByKey(selected);
                _gpsCombo.Enabled=_gpsChoices.Count>0;
            }
            finally { _filteringGps=false; }
        }
        private void RunAction(Action action)
        {
            if(!CommitEditors()) return;
            try { action(); CloseScreen(); } catch(Exception ex) { Message(ex.Message); }
        }
        private void Navigate(int delta) { if(!CommitEditors()) return; LastViews[_page]+=delta; _rebuild=true; }
        private bool CommitEditors()
        {
            if(_building || _committing) return true;
            _committing=true;
            try { foreach(var commit in _editors) if(!commit()) return false; return true; }
            finally { _committing=false; }
        }
        private bool Apply(NavOption option,object value)
        {
            try
            {
                if(option.Key=="@LAYOUT") { if(CloseScreen()) NavNativeUi.BeginLayout(host); return true; }
                _model.Apply(option,value); Message("Saved: "+option.Label); return true;
            }
            catch(Exception ex) { Message(option.Label+": "+ex.Message); host.Log("Native Nav setting failed: "+ex.Message); return false; }
        }
                private void AddRow(NavOption option,float y)
        {
            var label=Label(-0.35364f,y-0.004f,Short(option.Label,38),0.62f);
            label.SetToolTip(option.Section+"\n"+option.Label+Help(option));
            Label(-0.35364f,y+0.015f,option.Section,0.40f);
            if(option.Kind==NavOptionKind.Boolean)
            {
                bool value=Convert.ToBoolean(option.Read(_model.Current));
                MyGuiControlButton on=null,off=null;
                var state=Label(0.07392f,y,value ? "ON" : "OFF",0.65f);
                Action<bool> choose=delegate(bool selected) {
                    if(!CommitEditors() || !Apply(option,selected)) return;
                    SetToggleState(on,off,state,selected);
                    if(option.Key=="StreamerMode" || option.Key=="CaptureSafeHud" || option.Key=="CaptureSafeMenu") _rebuild=true;
                };
                on=Button(0.19488f,y,0.07980f,0.041f,"ON",delegate { choose(true); });
                off=Button(0.28812f,y,0.07980f,0.041f,"OFF",delegate { choose(false); });
                SetToggleState(on,off,state,value);
            }
            else if(option.Kind==NavOptionKind.Choice) Choice(option,0.23100f,y,0.24696f);
            else if(option.Kind==NavOptionKind.Action)
                Button(0.23100f,y,0.24696f,0.041f,option.Label,delegate {
                    if(CommitEditors() && Apply(option,null)) _rebuild=true;
                },0.59f);
            else AddEditor(option,y);
        }
        private static void SetToggleState(MyGuiControlButton on,MyGuiControlButton off,MyGuiControlLabel state,bool value)
        {
            on.Text=value ? "[X] ON" : "ON";
            off.Text=value ? "OFF" : "[X] OFF";
            on.Selected=value; off.Selected=!value;
            on.ColorMask=value ? new Vector4(0.75f,0.95f,1f,1f) : new Vector4(0.35f,0.43f,0.47f,1f);
            off.ColorMask=!value ? new Vector4(0.75f,0.95f,1f,1f) : new Vector4(0.35f,0.43f,0.47f,1f);
            state.Text=value ? "ON" : "OFF";
        }

        private static string Help(NavOption option)
        {
            if(option.Key=="SpeedCapOverride") return "\n0 = ShipCore cap, or this server's 50,000 m/s limit if unavailable. Explicit range: 0-50000 m/s.";
            if(option.Kind==NavOptionKind.Number) return "\nRange: "+option.Min+" to "+option.Max+". Step: "+option.Step;
            if(option.Key=="PredictTrackMotion") return "\nPredicts motion between sensor updates. Does not enable overlay marker smoothing.";
            if(option.Key=="CaptureSafeMenu") return "\nApplies to FULL / LEGACY SETTINGS only. Native game screens remain capturable.";
            return "";
        }
        private void AddEditor(NavOption option,float y)
        {
            bool color=option.Kind==NavOptionKind.Color;
            string saved=option.Format(_model.Current);
            var edit=new MyGuiControlTextbox(new Vector2(color ? 0.16632f : 0.17640f,y),saved,32,null,0.64f);
            edit.OriginAlign=MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER;
            edit.Size=new Vector2(color ? 0.13020f : 0.11760f,0.040f);
            edit.SetToolTip(option.Label+Help(option)+"\nENTER or APPLY saves. Page changes save valid edits. Closing discards invalid edits.");
            Controls.Add(edit);
            Func<bool> commit=delegate {
                if(edit.Text==saved) return true;
                try
                {
                    object value=option.Parse(edit.Text);
                    if(!Apply(option,value)) return false;
                    saved=option.Format(_model.Current); edit.Text=saved;
                    return true;
                }
                catch(Exception ex) { Message(option.Label+": "+ex.Message); FocusedControl=edit; return false; }
            };
            _editors.Add(commit);
            edit.EnterPressed+=delegate { if(CommitEditors()) _rebuild=true; };
            edit.TextChanged+=delegate { if(!_building && edit.Text!=saved) Message("Editing "+option.Label+". ENTER or APPLY saves."); };
            Button(0.32508f,y,0.06216f,0.041f,"APPLY",delegate { if(CommitEditors()) _rebuild=true; },0.49f);
            if(color)
            {
                var pick=Button(0.26292f,y,0.05628f,0.041f,"PICK",delegate {
                    if(!CommitEditors()) return;
                    _colorScreen=new NavNativeColorScreen(option.Label,option.Format(_model.Current),delegate(string hex) {
                        if(!Apply(option,hex)) return false;
                        _rebuild=true; return true;
                    });
                    MyGuiSandbox.AddScreen(_colorScreen);
                },0.50f);
                pick.ColorMask=HexColor(saved);
            }
            else
            {
                Action<int> step=delegate(int direction) {
                    try {
                        double value=Convert.ToDouble(option.Parse(edit.Text));
                        value=Math.Max(option.Min,Math.Min(option.Max,value+direction*option.Step));
                        edit.Text=value.ToString("F"+option.Decimals,CultureInfo.InvariantCulture);
                        commit();
                    } catch(Exception ex) { Message(option.Label+": "+ex.Message); }
                };
                Button(0.09492f,y,0.03528f,0.041f,"-",delegate { step(-1); });
                Button(0.26124f,y,0.03528f,0.041f,"+",delegate { step(1); });
            }
        }
        private void Choice(NavOption option,float x,float y,float width)
        {
            var combo=new MyGuiControlCombobox(new Vector2(x,y),new Vector2(width,0.041f),openAreaItemsCount:6,
                toolTip:option.Label,originAlign:MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
                isAutoscaleEnabled:true,isAutoEllipsisEnabled:true,minTextScale:0.55f);
            for(int i=0;i<option.Choices.Length;i++) combo.AddItem(i,option.Choices[i]);
            combo.SelectItemByKey(option.Key == "@PRESET" ? 0 : Math.Max(0,Array.FindIndex(option.Choices, v => v.Equals(Convert.ToString(option.Read(_model.Current)),StringComparison.OrdinalIgnoreCase))));
            combo.ItemSelected+=delegate {
                if(_building) return;
                if(!CommitEditors() || !Apply(option,option.Choices[(int)combo.GetSelectedKey()])) return;
                _rebuild=true;
            };
            Controls.Add(combo);
        }
        private void OpenExternal()
        {
            if(!CommitEditors()) return;
            if(CloseScreen() && host.Legacy!=null) host.Legacy();
        }
        private void Message(string text)
        {
            _message=text;
            if(_status!=null) { _status.Text=Short(text,108); _status.SetToolTip(text); }
        }
        private static string Short(string text,int length) { text=text??""; return text.Length<=length ? text : text.Substring(0,length-3)+"..."; }
        internal static Vector4 HexColor(string hex)
        {
            try { return new Vector4(Convert.ToInt32(hex.Substring(1,2),16)/255f,Convert.ToInt32(hex.Substring(3,2),16)/255f,Convert.ToInt32(hex.Substring(5,2),16)/255f,1f); }
            catch { return Vector4.One; }
        }
        private MyGuiControlLabel Label(float x,float y,string text,float scale)
        {
            var label=new MyGuiControlLabel(new Vector2(x,y),null,text,new Vector4(0.82f,0.91f,0.94f,1f),scale,null,MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
            Controls.Add(label); return label;
        }
        private MyGuiControlButton Button(float x,float y,float width,float height,string text,Action action,float scale=0.65f)
        {
            var button=new MyGuiControlButton(new Vector2(x,y),MyGuiControlButtonStyleEnum.Rectangular,new Vector2(width,height),null,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,null,new StringBuilder(text),scale,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,MyGuiControlHighlightType.WHEN_CURSOR_OVER,
                delegate(MyGuiControlButton _) { action(); });
            Controls.Add(button); return button;
        }

    }
        internal sealed class NavNativeColorScreen : MyGuiScreenBase
    {
        private readonly MyGuiControlSlider[] _rgb=new MyGuiControlSlider[3];
        private readonly MyGuiControlTextbox _hex;
        private readonly MyGuiControlButton _swatch;
        private readonly MyGuiControlLabel _error;
        internal NavNativeColorScreen(string label,string value,Func<string,bool> save)
            : base(new Vector2(0.5f,0.5f),new Vector4(0.105f,0.145f,0.165f,1f),new Vector2(0.56f,0.48f),true)
        {
            DrawMouseCursor=true; CloseButtonEnabled=true; EnabledBackgroundFade=true;
            CanHideOthers=false; CanBeHidden=false;
            AddCaption(label.ToUpperInvariant(),null,new Vector2(0,-0.194f),0.75f);
            Vector4 color=NavNativeSettingsScreen.HexColor(value);
            float[] values={color.X*255,color.Y*255,color.Z*255};
            string[] names={"RED","GREEN","BLUE"};
            for(int i=0;i<3;i++)
            {
                float y=-0.105f+i*0.06f;
                Controls.Add(new MyGuiControlLabel(new Vector2(-0.23f,y),null,names[i],null,0.65f,null,MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
                _rgb[i]=new MyGuiControlSlider(new Vector2(0.07f,y),0,255,0.31f,intValue:true,showLabel:true,labelDecimalPlaces:0,labelScale:0.6f,
                    originAlign:MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER);
                _rgb[i].Value=values[i]; Controls.Add(_rgb[i]);
            }
            _hex=new MyGuiControlTextbox(new Vector2(-0.12f,0.083f),value,7,null,0.70f);
            _hex.OriginAlign=MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER;
            _hex.Size=new Vector2(0.18f,0.04f); Controls.Add(_hex);
            _swatch=ActionButton(0.11f,0.083f,0.20f,"PREVIEW",delegate { SyncFromHex(); });
            _swatch.ColorMask=color;
            _error=new MyGuiControlLabel(new Vector2(-0.23f,0.127f),null,"RGB sliders or #RRGGBB. APPLY saves.",null,0.49f,null,MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
            Controls.Add(_error);
            foreach(var slider in _rgb) slider.ValueChanged+=delegate { SyncFromRgb(); };
            _hex.EnterPressed+=delegate { SyncFromHex(); };
            ActionButton(-0.12f,0.184f,0.19f,"CANCEL",delegate { CloseScreen(); });
            ActionButton(0.12f,0.184f,0.19f,"APPLY",delegate {
                if(!SyncFromHex()) return;
                if(save(_hex.Text)) CloseScreen();
                else _error.Text="Save failed. Check the settings folder.";
            });
        }
        public override string GetFriendlyName() { return "ZeoNavNativeColor"; }
        private void SyncFromRgb()
        {
            _hex.Text=string.Format("#{0:X2}{1:X2}{2:X2}",(int)Math.Round(_rgb[0].Value),(int)Math.Round(_rgb[1].Value),(int)Math.Round(_rgb[2].Value));
            _swatch.ColorMask=NavNativeSettingsScreen.HexColor(_hex.Text);
        }
        private bool SyncFromHex()
        {
            try {
                var option=new NavOption { Kind=NavOptionKind.Color };
                string hex=(string)option.Parse(_hex.Text);
                Vector4 value=NavNativeSettingsScreen.HexColor(hex);
                _rgb[0].Value=value.X*255; _rgb[1].Value=value.Y*255; _rgb[2].Value=value.Z*255;
                _hex.Text=hex; _swatch.ColorMask=value; return true;
            } catch(Exception ex) { _error.Text=ex.Message; return false; }
        }
        private MyGuiControlButton ActionButton(float x,float y,float width,string text,Action action)
        {
            var button=new MyGuiControlButton(new Vector2(x,y),MyGuiControlButtonStyleEnum.Rectangular,new Vector2(width,0.045f),null,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,null,new StringBuilder(text),0.65f,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,MyGuiControlHighlightType.WHEN_CURSOR_OVER,
                delegate(MyGuiControlButton _) { action(); });
            Controls.Add(button); return button;
        }
    }

}
