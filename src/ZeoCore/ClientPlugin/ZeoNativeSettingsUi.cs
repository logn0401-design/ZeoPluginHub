using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace ZeoCore
{
    internal static class ZeoNativeSettingsUi
    {
        private static ZeoNativeSettingsScreen _screen;
        private static ZeoHudLayoutScreen _editor;
        internal static bool Toggle(HudSettings settings, Action openExternalFallback, Action settingsChanged, Action ensureOverlay)
        {
            try
            {
                if(_editor!=null && _editor.State!=MyGuiScreenState.CLOSED) { _editor.CloseScreen(); return true; }
                if (_screen != null && _screen.State != MyGuiScreenState.CLOSED)
                {
                    _screen.CloseScreen();
                    return true;
                }
                var screen = new ZeoNativeSettingsScreen(settings, openExternalFallback, settingsChanged, ensureOverlay);
                screen.Closed += delegate { if (ReferenceEquals(_screen, screen)) _screen=null; };
                _screen=screen;
                MyGuiSandbox.AddScreen(screen);
                Plugin.Log("Native SE full settings screen opened: 11 pages / 148 page controls plus drag layout editor.");
                return true;
            }
            catch (Exception ex)
            {
                _screen=null;
                Plugin.Log("Native SE settings screen ERROR: " + ex);
                return false;
            }
        }
        internal static void BeginLayout(Action changed,Action ensureOverlay)
        {
            if(ensureOverlay!=null) ensureOverlay();
            var editor=new ZeoHudLayoutScreen(changed);
            _editor=editor;
            editor.Closed+=delegate { if(ReferenceEquals(_editor,editor)) _editor=null; };
            MyGuiSandbox.AddScreen(editor);
        }
        internal static void Close()
        {
            try { if (_screen != null) _screen.CloseScreen(true); } catch { }
            try { if(_editor!=null) _editor.CloseScreen(true); } catch { }
            ZeoHudLayoutSession.End(); _editor=null;
            _screen=null;
        }
    }

    internal sealed class ZeoNativeSettingsScreen : MyGuiScreenBase
    {
        internal const int RowsPerView=8;
        private readonly ZeoNativeSettingsModel _model;
        private readonly Action _openExternal;
        private readonly Action _changed,_ensureOverlay;
        private readonly List<Func<bool>> _editors=new List<Func<bool>>();
        private static readonly int[] LastViews=new int[11];
        private static int LastPage=-1;
        private int _page;
        private bool _building, _rebuild, _committing;
        private string _message="Changes apply immediately. Type a value, then press ENTER or APPLY.";
        private MyGuiControlLabel _status;
        private MyGuiControlLabel _refillStatus;
        private ZeoNativeColorScreen _colorScreen;

        internal ZeoNativeSettingsScreen(HudSettings settings, Action openExternal, Action changed, Action ensureOverlay)
            : base(new Vector2(0.5f,0.5f),new Vector4(0.105f,0.145f,0.165f,0.97f),new Vector2(0.94f,0.91f),true)
        {
            _model=new ZeoNativeSettingsModel(HudSettings.PathName,changed);
            _openExternal=openExternal; _changed=changed; _ensureOverlay=ensureOverlay;
            _page=Math.Max(0,Math.Min(10,LastPage < 0 ? (int)settings.MenuPage : LastPage));
            DrawMouseCursor=true; CloseButtonEnabled=true; EnabledBackgroundFade=true;
            CanHideOthers=false; CanBeHidden=false;
            BuildControls();
        }
        public override string GetFriendlyName() { return "ZeoCoreNativeSettings"; }
        public override bool Update(bool hasFocus)
        {
            bool result=base.Update(hasFocus);
            if (_rebuild && !_building) { _rebuild=false; BuildControls(); }
            if(_refillStatus!=null)
                _refillStatus.Text=Short((Plugin.RefillActive ? "REFILLING: " : "")+Plugin.RefillStatus,108);
            return result;
        }
        public override bool CloseScreen(bool isUnloading=false)
        {
            if (_colorScreen != null && _colorScreen.State != MyGuiScreenState.CLOSED)
            {
                _colorScreen.CloseScreen(isUnloading);
                if (!isUnloading) return false;
            }
            if (!isUnloading && !CommitEditors()) return false;
            return base.CloseScreen(isUnloading);
        }
        protected override void OnClosed()
        {
            // All edits already use the shared model. Never overwrite them with a
            // stale HudSettings snapshot when switching to the legacy window.
            Plugin.Log("Native SE settings screen closed.");
            base.OnClosed();
        }
        private void BuildControls()
        {
            _building=true;
            try
            {
                Controls.Clear(); _editors.Clear(); _model.Reload(); _refillStatus=null;
                AddCaption("ZEOCORE // TACTICAL SYSTEMS",new Vector4(0.82f,0.91f,0.94f,1f),new Vector2(0f,-0.417f),0.88f);
                for (int i=0;i<ZeoNativeCatalog.Pages.Length;i++)
                {
                    int target=i;
                    var tab=Button(-0.36f+(i%6)*0.144f,i<6 ? -0.348f : -0.293f,0.135f,0.045f,ZeoNativeCatalog.Pages[i],delegate {
                        if (!CommitEditors()) return;
                        _page=target; LastPage=target; _rebuild=true;
                    },0.61f);
                    tab.Selected=i==_page;
                }
                Label(-0.421f,-0.230f,"PROFILE",0.60f);
                Choice(ZeoNativeCatalog.Profile,-0.170f,-0.230f,0.265f);
                Label(0.050f,-0.230f,"MENU KEY",0.60f);
                Choice(ZeoNativeCatalog.MenuKey,0.302f,-0.230f,0.230f);
                var rows=ZeoNativeCatalog.Options.Where(o => o.Page==ZeoNativeCatalog.Pages[_page]).ToArray();
                int views=(rows.Length+RowsPerView-1)/RowsPerView;
                int view=LastViews[_page]=Math.Max(0,Math.Min(views-1,LastViews[_page]));
                Label(-0.421f,-0.166f,ZeoNativeCatalog.Pages[_page]+"  /  "+(view+1)+" OF "+views,0.76f);
                Label(0.160f,-0.166f,(view*RowsPerView+1)+" - "+Math.Min(rows.Length,(view+1)*RowsPerView)+" OF "+rows.Length,0.57f);
                for (int i=0;i<RowsPerView && view*RowsPerView+i<rows.Length;i++)
                    AddRow(rows[view*RowsPerView+i],-0.105f+i*0.053f);
                Button(-0.325f,0.327f,0.195f,0.043f,"PREVIOUS",delegate { Navigate(-1); },0.62f).Enabled=view>0;
                Button(0.325f,0.327f,0.195f,0.043f,"NEXT",delegate { Navigate(1); },0.62f).Enabled=view+1<views;
                if(ZeoNativeCatalog.Pages[_page]=="LAYOUT")
                    Button(0,0.327f,0.390f,0.043f,"EDIT HUD LAYOUT",delegate {
                        if(!CommitEditors()) return;
                        if(CloseScreen()) ZeoNativeSettingsUi.BeginLayout(_changed,_ensureOverlay);
                    },0.62f);
                if(ZeoNativeCatalog.Pages[_page]=="AMMO")
                    Button(0,0.327f,0.390f,0.043f,"QUICK REFILL / CANCEL",delegate {
                        if(!CommitEditors())return;
                        Plugin.ToggleRefill();
                    },0.62f);
                var hint=Label(-0.421f,0.365f,HintForPage(),0.53f);
                if(ZeoNativeCatalog.Pages[_page]=="AMMO")_refillStatus=hint;
                _status=Label(-0.421f,0.387f,Short(_message,108),0.50f);
                Button(-0.285f,0.425f,0.280f,0.044f,"FULL / LEGACY SETTINGS",OpenExternal,0.61f);
                Button(0.335f,0.425f,0.170f,0.044f,"CLOSE",delegate { CloseScreen(); },0.65f);
            }
            finally { _building=false; }
        }
        private string HintForPage()
        {
            string page=ZeoNativeCatalog.Pages[_page];
            if(page=="FLEET") return "NETWORK / SHARING: local sensors stay active; shared data depends on the server.";
            if(page=="CAPTURE" || page=="PRIVACY") return "Capture exclusion covers the external HUD and legacy menu. This native menu is visible in capture.";
            if(page=="THEME") return "PICK opens native RGB controls. Menu colors style the legacy window; this menu keeps SE styling.";
            if(page=="LAYOUT") return "EDIT HUD LAYOUT opens move and width / height resize. NEXT opens numeric sizes, positions and marker limits.";
            if(page=="SCOPE") return "Prediction and visual smoothing are separate. Existing tracking behavior is preserved.";
            if(page=="AMMO") return "Dock to refill tanks and ammo WANT deficits. Relevant-only applies; ammo needs connected ship cargo.";
            return "Hover a setting for details. ESC or the configured menu key returns to the game.";
        }
        private void Navigate(int delta)
        {
            if(!CommitEditors()) return;
            LastViews[_page]+=delta; _rebuild=true;
        }
        private bool CommitEditors()
        {
            if (_building || _committing) return true;
            _committing=true;
            try { foreach(var commit in _editors) if(!commit()) return false; return true; }
            finally { _committing=false; }
        }
        private bool Apply(NativeOption option,object value)
        {
            try { _model.Apply(option,value); Message("Saved: "+option.Label); return true; }
            catch(Exception ex)
            {
                Message(option.Label+": "+ex.Message);
                Plugin.Log("Native settings edit failed ("+option.Key+"): "+ex.Message);
                return false;
            }
        }
        private void AddRow(NativeOption option,float y)
        {
            var label=Label(-0.421f,y-0.004f,Short(option.Label,53),0.62f);
            label.SetToolTip(option.Section+"\n"+option.Label+Help(option));
            Label(-0.421f,y+0.015f,option.Section,0.40f);
            if(option.Kind==NativeOptionKind.Boolean)
            {
                bool value=Convert.ToBoolean(option.Read(_model.Current));
                MyGuiControlButton on=null,off=null;
                var state=Label(0.088f,y,value ? "ON" : "OFF",0.65f);
                Action<bool> choose=delegate(bool selected) {
                    if(!CommitEditors() || !Apply(option,selected)) return;
                    SetToggleState(on,off,state,selected);
                    if(option.Key=="StreamerMode" || option.Key=="CaptureSafeHud" || option.Key=="CaptureSafeMenu") _rebuild=true;
                };
                on=Button(0.232f,y,0.095f,0.041f,"ON",delegate { choose(true); });
                off=Button(0.343f,y,0.095f,0.041f,"OFF",delegate { choose(false); });
                SetToggleState(on,off,state,value);
            }
            else if(option.Kind==NativeOptionKind.Choice) Choice(option,0.275f,y,0.294f);
            else if(option.Kind==NativeOptionKind.Action)
                Button(0.275f,y,0.294f,0.041f,"RESET POSITIONS",delegate {
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

        private static string Help(NativeOption option)
        {
            if(option.Kind==NativeOptionKind.Number) return "\nRange: "+option.Min+" to "+option.Max+". Step: "+option.Step;
            if(option.Key=="PredictTrackMotion") return "\nPredicts motion between sensor updates. Does not enable overlay marker smoothing.";
            if(option.Key=="CaptureSafeMenu") return "\nApplies to FULL / LEGACY SETTINGS only. Native game screens remain capturable.";
            return "";
        }
        private void AddEditor(NativeOption option,float y)
        {
            bool color=option.Kind==NativeOptionKind.Color;
            string saved=option.Format(_model.Current);
            var edit=new MyGuiControlTextbox(new Vector2(color ? 0.198f : 0.210f,y),saved,32,null,0.64f);
            edit.OriginAlign=MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER;
            edit.Size=new Vector2(color ? 0.155f : 0.140f,0.040f);
            edit.SetToolTip(option.Label+Help(option)+"\nENTER or APPLY saves. Page changes also save valid edits.");
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
            Button(0.387f,y,0.074f,0.041f,"APPLY",delegate { if(CommitEditors()) _rebuild=true; },0.49f);
            if(color)
            {
                var pick=Button(0.313f,y,0.067f,0.041f,"PICK",delegate {
                    if(!CommitEditors()) return;
                    _colorScreen=new ZeoNativeColorScreen(option.Label,option.Format(_model.Current),delegate(string hex) {
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
                Button(0.113f,y,0.042f,0.041f,"-",delegate { step(-1); });
                Button(0.311f,y,0.042f,0.041f,"+",delegate { step(1); });
            }
        }
        private void Choice(NativeOption option,float x,float y,float width)
        {
            var combo=new MyGuiControlCombobox(new Vector2(x,y),new Vector2(width,0.041f),openAreaItemsCount:6,
                toolTip:option.Label,originAlign:MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
                isAutoscaleEnabled:true,isAutoEllipsisEnabled:true,minTextScale:0.55f);
            for(int i=0;i<option.Choices.Length;i++) combo.AddItem(i,option.Choices[i]);
            combo.SelectItemByKey(Convert.ToInt32(option.Read(_model.Current)));
            combo.ItemSelected+=delegate {
                if(_building) return;
                if(!CommitEditors() || !Apply(option,(int)combo.GetSelectedKey())) return;
                _rebuild=true;
            };
            Controls.Add(combo);
        }
        private void OpenExternal()
        {
            if(!CommitEditors()) return;
            if(CloseScreen() && _openExternal!=null) _openExternal();
        }
        private void Message(string text)
        {
            _message=text;
            if(_status!=null) { _status.Text=Short(text,108); _status.SetToolTip(text); }
        }
        private static string Short(string text,int length) { return text.Length<=length ? text : text.Substring(0,length-3)+"..."; }
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

    internal sealed class ZeoNativeColorScreen : MyGuiScreenBase
    {
        private readonly MyGuiControlSlider[] _rgb=new MyGuiControlSlider[3];
        private readonly MyGuiControlTextbox _hex;
        private readonly MyGuiControlButton _swatch;
        private readonly MyGuiControlLabel _error;
        internal ZeoNativeColorScreen(string label,string value,Func<string,bool> save)
            : base(new Vector2(0.5f,0.5f),new Vector4(0.105f,0.145f,0.165f,1f),new Vector2(0.56f,0.48f),true)
        {
            DrawMouseCursor=true; CloseButtonEnabled=true; EnabledBackgroundFade=true;
            CanHideOthers=false; CanBeHidden=false;
            AddCaption(label.ToUpperInvariant(),null,new Vector2(0,-0.194f),0.75f);
            Vector4 color=ZeoNativeSettingsScreen.HexColor(value);
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
        public override string GetFriendlyName() { return "ZeoCoreNativeColor"; }
        private void SyncFromRgb()
        {
            _hex.Text=string.Format("#{0:X2}{1:X2}{2:X2}",(int)Math.Round(_rgb[0].Value),(int)Math.Round(_rgb[1].Value),(int)Math.Round(_rgb[2].Value));
            _swatch.ColorMask=ZeoNativeSettingsScreen.HexColor(_hex.Text);
        }
        private bool SyncFromHex()
        {
            try {
                var option=new NativeOption { Kind=NativeOptionKind.Color };
                string hex=(string)option.Parse(_hex.Text);
                Vector4 value=ZeoNativeSettingsScreen.HexColor(hex);
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
