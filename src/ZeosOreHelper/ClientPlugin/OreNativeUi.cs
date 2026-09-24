using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using VRage.Input;
using Sandbox.ModAPI;

namespace ZeosOreHelper
{
    internal static class OreNativeUi
    {
        private static OreNativeSettingsScreen screen;
        private static OreHudLayoutScreen editor;
        public static bool EditingBinding {get{return screen!=null && screen.State!=MyGuiScreenState.CLOSED && screen.BindingActive;}}
        public static bool IsOpen { get { return (screen!=null && screen.State!=MyGuiScreenState.CLOSED) || (editor!=null && editor.State!=MyGuiScreenState.CLOSED); } }
        public static bool Toggle(Plugin host)
        {
            try
            {
                if(editor!=null && editor.State!=MyGuiScreenState.CLOSED) { editor.CloseScreen(); return true; }
                if(screen!=null && screen.State!=MyGuiScreenState.CLOSED) { screen.CloseScreen(); return true; }
                var next=new OreNativeSettingsScreen(host);
                next.Closed+=delegate { if(ReferenceEquals(screen,next)) screen=null; };
                screen=next; MyGuiSandbox.AddScreen(next); Plugin.Log("Native compact Ore settings opened."); return true;
            }
            catch(Exception ex) { screen=null; Plugin.Log("Native Ore UI failed: "+ex); return false; }
        }
        public static void BeginLayout(Plugin host)
        {
            try
            {
                host.EnsureOverlay();
                var next=new OreHudLayoutScreen(host); editor=next;
                next.Closed+=delegate { if(ReferenceEquals(editor,next)) editor=null; };
                MyGuiSandbox.AddScreen(next);
            }
            catch(Exception ex) { host.Layout=null; Plugin.Log("Ore layout UI failed: "+ex); }
        }
        public static void Close()
        {
            try { if(screen!=null) screen.CloseScreen(true); } catch { }
            try { if(editor!=null) editor.CloseScreen(true); } catch { }
            screen=null; editor=null;
        }
    }

    internal sealed class OreNativeSettingsScreen : MyGuiScreenBase
    {
        internal const int RowsPerView=6;
        private readonly Plugin host;
        private readonly OreUiModel _model;
        private OreMenuBinding _binding; private Action _pollBinding;
        internal bool BindingActive {get{return _binding!=null && _binding.Editing;}}
        private readonly List<Func<bool>> _editors=new List<Func<bool>>();
        private static readonly int[] LastViews=new int[6];
        private static int LastPage;
        private static readonly string[] LastGroups=new string[6];
        private MyGuiControlLabel _summary; private int _ticks; private DateTime _resetArmed;
        private int _page;
        private bool _building,_rebuild,_committing;
        private string _message="Click to change settings. Use APPLY for typed values and key bindings.";
        private MyGuiControlLabel _status;
        private OreNativeColorScreen _colorScreen;
        
        internal OreNativeSettingsScreen(Plugin host)
            : base(new Vector2(.5f,.5f),new Vector4(.105f,.145f,.165f,.97f),new Vector2(.80f,.84f),true)
        {
            this.host=host; _model=new OreUiModel(host); _page=LastPage;
            DrawMouseCursor=true; CloseButtonEnabled=true; EnabledBackgroundFade=true;
            CanHideOthers=false; CanBeHidden=false; BuildControls();
        }
        public override string GetFriendlyName() { return "ZeoOreNativeSettings"; }
        public override bool Update(bool hasFocus)
        {
            bool result=base.Update(hasFocus);
            if(hasFocus && State==MyGuiScreenState.OPENED && _pollBinding!=null)_pollBinding();
            if(++_ticks%30==0 && _summary!=null && host!=null) {_summary.Text=Short(Summary(),98);}
            if(_rebuild && !_building)
            {
                _rebuild=false;
                try { BuildControls(); }
                catch(Exception ex) { Message("Settings could not reload: "+ex.Message); Plugin.Log("Native Ore reload failed: "+ex.Message); }
            }
            return result;
        }
        public override bool CloseScreen(bool isUnloading=false)
        {
            if(_colorScreen!=null && _colorScreen.State!=MyGuiScreenState.CLOSED)
            {
                _colorScreen.CloseScreen(isUnloading); if(!isUnloading) return false;
            }
            if(!isUnloading && !CommitEditors()) return false;
            _binding=null;_pollBinding=null;
            return base.CloseScreen(isUnloading);
        }
        private void BuildControls()
        {
            _building=true;
            try
            {
                _model.Reload(); Controls.Clear(); _editors.Clear();_summary=null;_binding=null;_pollBinding=null;
                AddCaption("ZEO ORE HELPER",new Vector4(.82f,.91f,.94f,1),new Vector2(0,-.378f),.82f);
                for(int i=0;i<OreUiCatalog.Pages.Length;i++) {
                    int target=i;
                    var tab=Button(-.30f+(i%6)*.12f,-.323f+(i/6)*.046f,.113f,.041f,OreUiCatalog.Pages[i],delegate {if(!CommitEditors())return;_page=target;LastPage=target;_rebuild=true;},.53f);
                    tab.Selected=i==_page;tab.ColorMask=i==_page?new Vector4(.75f,.95f,1,1):new Vector4(.48f,.58f,.63f,1);
                }
                if(_page==0) BuildSearch();
                else {
                    var all=OreUiCatalog.NativeOptions(OreUiCatalog.Pages[_page]).ToArray();
                    var groups=all.Select(o=>o.Group).Distinct().ToArray();
                    string group=LastGroups[_page];if(!groups.Contains(group))group=groups[0];LastGroups[_page]=group;
                    var picker=new MyGuiControlCombobox(new Vector2(-.10f,-.248f),new Vector2(.51f,.04f),openAreaItemsCount:7,isAutoscaleEnabled:true,isAutoEllipsisEnabled:true,minTextScale:.5f);
                    for(int i=0;i<groups.Length;i++)picker.AddItem(i,groups[i]);picker.SelectItemByKey(Array.IndexOf(groups,group));
                    picker.ItemSelected+=delegate{if(!_building&&CommitEditors()){LastGroups[_page]=groups[(int)picker.GetSelectedKey()];LastViews[_page]=0;_rebuild=true;}};Controls.Add(picker);
                    if(_page==4) {
                        var ores=new MyGuiControlCombobox(new Vector2(.267f,-.248f),new Vector2(.18f,.04f),openAreaItemsCount:8,isAutoscaleEnabled:true,minTextScale:.5f);
                        for(int i=0;i<HudSettings.KnownOres.Length;i++)ores.AddItem(i,HudSettings.KnownOres[i]);
                        ores.SelectItemByKey(Array.IndexOf(HudSettings.KnownOres,OreUiModel.SelectedOre));
                        ores.ItemSelected+=delegate{if(!_building&&CommitEditors()){OreUiModel.SelectedOre=HudSettings.KnownOres[(int)ores.GetSelectedKey()];_rebuild=true;}};Controls.Add(ores);
                    }
                    var rows=all.Where(o=>o.Group==group).ToArray();int views=Math.Max(1,(rows.Length+RowsPerView-1)/RowsPerView);
                    int view=LastViews[_page]=Math.Max(0,Math.Min(views-1,LastViews[_page]));
                    for(int i=0;i<RowsPerView && view*RowsPerView+i<rows.Length;i++)AddRow(rows[view*RowsPerView+i],-.165f+i*.059f);
                    Button(-.263f,.217f,.18f,.040f,"PREVIOUS",delegate{Navigate(-1);},.58f).Enabled=view>0;
                    Button(.263f,.217f,.18f,.040f,"NEXT",delegate{Navigate(1);},.58f).Enabled=view+1<views;
                    Label(-.035f,.217f,(view+1)+" / "+views,.55f);
                    _summary=Label(-.354f,.273f,Short(host==null?"":Summary(),98),.46f);
                }
                _status=Label(-.354f,.309f,Short(_message,92),.46f);
                Button(-.205f,.363f,.30f,.043f,"MOVE / RESIZE HUD",OpenLayout,.53f);
                Button(.054f,.363f,.19f,.043f,"MENU KEY",delegate{if(!CommitEditors())return;_page=5;LastPage=5;LastGroups[5]=Option("MenuKey").Group;LastViews[5]=0;_rebuild=true;},.53f);
                Button(.258f,.363f,.19f,.043f,"CLOSE",delegate{CloseScreen();},.62f);
            }
            finally { _building=false; }
        }
        private OreOption Option(string key){return OreUiCatalog.Options.First(o=>o.Key==key);}
        private void OpenLayout(){if(CommitEditors()&&CloseScreen())OreNativeUi.BeginLayout(host);}
        private void BuildSearch(){
            _summary=null;
            bool enabled=_model.Current.B("Enabled");
            Button(-.23f,-.264f,.25f,.042f,enabled?"STOP SEARCH":"START SEARCH",delegate{if(CommitEditors()&&Apply(Option("Enabled"),!enabled))_rebuild=true;},.58f);
            Choice(Option("ActivePreset"),.143f,-.264f,.425f);
            for(int i=0;i<HudSettings.KnownOres.Length;i++){
                string ore=HudSettings.KnownOres[i];bool on=_model.Current.B("OreEnabled:"+ore,true);
                var b=Button(-.270f+(i%4)*.180f,-.207f+(i/4)*.043f,.168f,.038f,(on?"[X] ":"[ ] ")+ore,delegate{
                    if(!CommitEditors())return;var option=new OreOption{Key="OreEnabled:"+ore,Kind=OreOptionKind.Boolean,Label=ore};
                    if(Apply(option,!on))_rebuild=true;
                },.53f);b.Selected=on;b.ColorMask=on?HexColor(_model.Current.Get("OreColor:"+ore)):new Vector4(.35f,.43f,.47f,1);
                b.SetToolTip(ore+" — "+(on?"included":"excluded")+". Per-ore minimum and appearance: ORE DETAIL.");
            }
            Button(-.303f,-.019f,.103f,.036f,"ALL",delegate{SetAll(true);},.53f);
            Button(-.189f,-.019f,.103f,.036f,"NONE",delegate{SetAll(false);},.53f);
            Choice(Option("SelectionSlot"),-.05f,-.019f,.12f);
            Button(.113f,-.019f,.15f,.036f,"SAVE SET",delegate{SaveSelection(true);},.52f);
            Button(.283f,-.019f,.15f,.036f,"LOAD SET",delegate{SaveSelection(false);},.52f);
            AddRow(Option("SearchSort"),.040f);AddRow(Option("BenchmarkPercent"),.094f);
            AddRow(Option("MaxMarkers"),.148f);AddRow(Option("SurveyRangeMeters"),.202f);
            bool allOres=_model.Current.B("RequireAllWantedOres");
            var matchButton=Button(-.18f,.258f,.34f,.038f,allOres?"ORE MATCH: ALL":"ORE MATCH: ANY",delegate{if(CommitEditors()&&Apply(Option("RequireAllWantedOres"),!allOres))_rebuild=true;},.55f);
            matchButton.SetToolTip("ANY: match at least one selected ore. ALL: the asteroid must contain every selected ore.");
            double minimum=_model.Current.D("MinimumDistanceMeters");
            if(minimum>0)Button(.18f,.258f,.34f,.038f,"SHOW NEARBY ASTEROIDS",delegate{if(CommitEditors()&&Apply(Option("MinimumDistanceMeters"),0d))_rebuild=true;},.50f);
            else {bool loaded=_model.Current.B("MaxLoadedRange");Button(.18f,.258f,.34f,.038f,loaded?"RANGE: ALL LOADED":"RANGE: SET DISTANCE",delegate{if(CommitEditors()&&Apply(Option("MaxLoadedRange"),!loaded))_rebuild=true;},.51f);}
            _summary=Label(-.354f,.289f,Short(Summary(),98),.43f);
        }
        private string Summary(){if(host==null)return "";if(_page==2)return host.Deposits.Status;if(_page==3)return host.Sdx.Status;if(_page==4)return host.LearningDetail(OreUiModel.SelectedOre);double min=_model.Current.D("MinimumDistanceMeters");return (min>0?ZeoOreShared.OreSearchHints.MinimumRange(min)+" | ":"")+host.SearchSummary;}
        private void SaveSelection(bool save){try{if(!CommitEditors())return;_model.Selection(save);Message((save?"Saved":"Loaded")+" ore selection in slot "+_model.Current.Get("SelectionSlot","1"));_rebuild=true;}catch(Exception ex){Message(ex.Message);}}
        private void SetAll(bool value){try{if(!CommitEditors())return;_model.SelectAll(value);_rebuild=true;}catch(Exception ex){Message(ex.Message);}}
        private void Navigate(int delta) { if(!CommitEditors()) return; LastViews[_page]+=delta; _rebuild=true; }
        private bool CommitEditors()
        {
            if(_building || _committing) return true;
            _committing=true;
            try { foreach(var commit in _editors) if(!commit()) return false; return true; }
            finally { _committing=false; }
        }
        private bool Apply(OreOption option,object value)
        {
            try
            {
                if(option.Key=="@legacymenu"){OpenExternal();return true;}
                if(option.Key=="@LAYOUT") { if(CloseScreen()) OreNativeUi.BeginLayout(host); return true; }
                if(option.Key=="@saveselection"||option.Key=="@loadselection"){SaveSelection(option.Key=="@saveselection");return true;}
                if(option.Key=="@resetlearning" && DateTime.UtcNow>_resetArmed){_resetArmed=DateTime.UtcNow.AddSeconds(8);Message("Reset deletes this world's learned records. Click RESET again within 8 seconds to confirm.");return false;}
                _model.Apply(option,value); Message("Saved: "+option.Label); return true;
            }
            catch(Exception ex) { Message(option.Label+": "+ex.Message); Plugin.Log("Native Ore setting failed: "+ex.Message); return false; }
        }
                private void AddRow(OreOption option,float y)
        {
            var label=Label(-0.35364f,y-0.004f,Short(option.Label,38),0.62f);
            label.SetToolTip(option.Section+"\n"+option.Label+Help(option));

            if(option.Key=="MenuKey") {AddKeyBinding(option,y);}
            else if(option.Kind==OreOptionKind.Boolean)
            {
                bool value=Convert.ToBoolean(option.Read(_model.Current));
                MyGuiControlButton on=null,off=null;
                Action<bool> choose=delegate(bool selected) {
                    if(!CommitEditors() || !Apply(option,selected)) return;
                    SetToggleState(on,off,selected);
                    if(option.Key=="StreamerMode" || option.Key=="StreamerFailClosed") _rebuild=true;
                };
                on=Button(0.19488f,y,0.07980f,0.041f,"ON",delegate { choose(true); });
                off=Button(0.28812f,y,0.07980f,0.041f,"OFF",delegate { choose(false); });
                SetToggleState(on,off,value);
            }
            else if(option.Kind==OreOptionKind.Choice) Choice(option,0.23100f,y,0.24696f);
            else if(option.Kind==OreOptionKind.Action)
                Button(0.23100f,y,0.24696f,0.041f,OreUiCatalog.ActionLabel(option.Key),delegate {
                    if(CommitEditors() && Apply(option,null)) _rebuild=true;
                },0.59f);
            else AddEditor(option,y);
        }
        private static void SetToggleState(MyGuiControlButton on,MyGuiControlButton off,bool value)
        {
            on.Text=value ? "[X] ON" : "ON";
            off.Text=value ? "OFF" : "[X] OFF";
            on.Selected=value; off.Selected=!value;
            on.ColorMask=value ? new Vector4(0.75f,0.95f,1f,1f) : new Vector4(0.35f,0.43f,0.47f,1f);
            off.ColorMask=!value ? new Vector4(0.75f,0.95f,1f,1f) : new Vector4(0.35f,0.43f,0.47f,1f);
        }

        private static string Help(OreOption option)
        {
            if(option.Key=="BenchmarkPercent"||option.Key=="OreBenchmarkPercent:")return "\n80 means at least 80% of the best saved amount, or percentage in Highest ore percentage mode. The minimum starts after three detailed scans.";
            if(option.Key=="SurveyRangeMeters")return "\nMaximum scan distance. Ignored when RANGE: ALL LOADED is selected. Only loaded asteroids can be scanned.";
            if(option.Key=="MaxMarkers")return "\nTotal asteroid and deposit ping limit. 0 hides all pings.";
            if(option.Key=="MaxDepositMarkers")return "\nDeposit pings use this many slots from the total Maximum pings on SEARCH.";
            if(option.Key=="PreferSdxScans")return "\nUse completed SDX2 client scans when available. Saved amounts may be out of date; they are estimates.";
            if(option.Key=="@usebenchmarks")return "\nRecalculate minimums from your best saved scans for this world.";
            if(option.Key=="@resetlearning")return "\nDelete this world's learned records. Requires a second click to confirm.";
            if(option.Group=="BACKUP MENU")return "\nOnly affects the older external settings window. The native menu already contains all settings.";
            if(option.Key=="PingShowNumber"||option.Key=="PingShowGrade"||option.Key=="PingShowDistance"||option.Key=="PingShowDiameter"||option.Key=="PingShowTopOre"||option.Key=="PingShowOrePercent"||option.Key=="PingShowScanStatus")return "\nUsed when Ping label style is Custom. Simple labels show ore and distance.";
            if(option.Key=="PingLabelStyle")return "\nSimple: ore + distance. Target detail adds estimated amount only when aimed. Custom uses all information toggles below.";
            if(option.Key=="MinimumDistanceMeters")return "\nAsteroids disappear inside this radius. Use 0 to include nearby asteroids.";
            if(option.Kind==OreOptionKind.Number) return "\nRange: "+option.Min+" to "+option.Max+". Step: "+option.Step;
            if(option.Key=="StreamerMode") return "\nExternal HUD and legacy menu use capture exclusion. Native menus remain capturable; LCD output is suppressed.";
            if(option.Key=="MenuKey") return "\nClick the key, press a new key, then APPLY. CLEAR disables it after APPLY. ESC/navigation cancels this draft. Pulsar Configure or /ore menu reopens a disabled menu binding.";
            return "";
        }
        private void AddEditor(OreOption option,float y)
        {
            bool color=option.Kind==OreOptionKind.Color;
            string saved=option.Format(_model.Current);
            var edit=new MyGuiControlTextbox(new Vector2(color ? 0.16632f : 0.17640f,y),saved,128,null,0.64f);
            edit.OriginAlign=MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER;
            edit.Size=new Vector2(color ? 0.13020f : 0.11760f,0.040f);
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
            Button(0.32508f,y,0.06216f,0.041f,"APPLY",delegate { if(CommitEditors()) _rebuild=true; },0.49f);
            if(color)
            {
                var pick=Button(0.26292f,y,0.05628f,0.041f,"PICK",delegate {
                    if(!CommitEditors()) return;
                    _colorScreen=new OreNativeColorScreen(option.Label,option.Format(_model.Current),delegate(string hex) {
                        if(!Apply(option,hex)) return false;
                        _rebuild=true; return true;
                    });
                    MyGuiSandbox.AddScreen(_colorScreen);
                },0.50f);
                pick.ColorMask=HexColor(saved);
            }
            else if(option.Kind==OreOptionKind.Number)
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
        private void Choice(OreOption option,float x,float y,float width)
        {
            var combo=new MyGuiControlCombobox(new Vector2(x,y),new Vector2(width,0.041f),openAreaItemsCount:6,
                toolTip:option.Label+Help(option),originAlign:MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
                isAutoscaleEnabled:true,isAutoEllipsisEnabled:true,minTextScale:0.55f);
            var choices=option.Choices.ToList();
            string current=Convert.ToString(option.Read(_model.Current));
            int selected=choices.FindIndex(v=>v.Equals(current,StringComparison.OrdinalIgnoreCase));
            if(selected<0){selected=choices.Count;choices.Add(current.Length==0?"(unset)":current);}
            for(int i=0;i<choices.Count;i++) combo.AddItem(i,OreUiCatalog.ChoiceLabel(option.Key,choices[i]));
            combo.SelectItemByKey(selected);
            combo.ItemSelected+=delegate {
                if(_building) return;
                int index=(int)combo.GetSelectedKey();
                if(index<0 || index>=option.Choices.Length)return;
                if(!CommitEditors() || !Apply(option,option.Choices[index])) return;
                _rebuild=true;
            };
            Controls.Add(combo);
        }
        private void AddKeyBinding(OreOption option,float y)
        {
            var binding=new OreMenuBinding(_model.Current.Get("MenuKey","PageUp"));_binding=binding;
            int armDelay=0;MyGuiControlButton capture=null;
            Action refresh=()=>capture.Text=binding.Listening?"PRESS A KEY...":binding.Draft=="None"?"NONE / ASSIGN":binding.Draft;
            capture=Button(.138f,y,.160f,.041f,"",delegate{binding.Begin();armDelay=2;FocusedControl=null;refresh();Message("Press a key, then APPLY. ESC cancels. Menu shortcuts pause while editing.");},.49f);
            refresh();capture.SetToolTip("Click, press a key, then APPLY. Single keyboard key, like PDC's menu binding.");
            Button(.258f,y,.065f,.041f,"CLEAR",delegate{binding.Clear();refresh();Message("APPLY disables the shortcut. Reopen with Pulsar Configure or /ore menu.");},.42f);
            Button(.332f,y,.065f,.041f,"APPLY",delegate{
                if(binding.Listening){Message("Press a key first, or CLEAR to disable the shortcut.");return;}
                if(Apply(option,binding.Draft)){binding.Saved();refresh();Message("Menu key saved: "+binding.Draft);}
            },.42f);
            _pollBinding=delegate{
                if(!binding.Listening||MyAPIGateway.Input==null)return;
                if(armDelay>0){armDelay--;return;}
                foreach(MyKeys key in Enum.GetValues(typeof(MyKeys))) {
                    if(!OreMenuBinding.Capturable(key)||!MyAPIGateway.Input.IsNewKeyPressed(key))continue;
                    if(binding.Capture(key)){refresh();Message("Selected "+binding.Draft+". Click APPLY to save.");break;}
                }
            };
        }
        private void OpenExternal()
        {
            if(!CommitEditors()) return;
            if(CloseScreen() && host!=null) host.OpenLegacyMenu();
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
        internal sealed class OreNativeColorScreen : MyGuiScreenBase
    {
        private readonly MyGuiControlSlider[] _rgb=new MyGuiControlSlider[3];
        private readonly MyGuiControlTextbox _hex;
        private readonly MyGuiControlButton _swatch;
        private readonly MyGuiControlLabel _error;
        internal OreNativeColorScreen(string label,string value,Func<string,bool> save)
            : base(new Vector2(0.5f,0.5f),new Vector4(0.105f,0.145f,0.165f,1f),new Vector2(0.56f,0.48f),true)
        {
            DrawMouseCursor=true; CloseButtonEnabled=true; EnabledBackgroundFade=true;
            CanHideOthers=false; CanBeHidden=false;
            AddCaption(label.ToUpperInvariant(),null,new Vector2(0,-0.194f),0.75f);
            Vector4 color=OreNativeSettingsScreen.HexColor(value);
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
        public override string GetFriendlyName() { return "ZeoOreNativeColor"; }
        private void SyncFromRgb()
        {
            _hex.Text=string.Format("#{0:X2}{1:X2}{2:X2}",(int)Math.Round(_rgb[0].Value),(int)Math.Round(_rgb[1].Value),(int)Math.Round(_rgb[2].Value));
            _swatch.ColorMask=OreNativeSettingsScreen.HexColor(_hex.Text);
        }
        private bool SyncFromHex()
        {
            try {
                var option=new OreOption { Kind=OreOptionKind.Color };
                string hex=(string)option.Parse(_hex.Text);
                Vector4 value=OreNativeSettingsScreen.HexColor(hex);
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

