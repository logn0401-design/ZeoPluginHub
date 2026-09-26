using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using Sandbox.Graphics;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using VRage.Input;
using Sandbox.ModAPI;

namespace ZeoPDC
{
    internal sealed class PdcNativeSettingsUi
    {
        MyGuiScreenBase screen;
        readonly PdcSettingsModel model;
        readonly Func<PdcSnapshot> snapshot;
        readonly Action<string> command;
        readonly Action external, layout;
        public bool IsOpen { get { return screen != null && screen.State != MyGuiScreenState.CLOSED; } }
        public PdcNativeSettingsUi(PdcSettingsModel model, Func<PdcSnapshot> snapshot, Action<string> command, Action external, Action layout)
        { this.model = model; this.snapshot = snapshot; this.command = command; this.external = external; this.layout = layout; }
        public void Toggle()
        {
            if (IsOpen) { screen.CloseScreen(); return; }
            model.Reload();
            var next = new PdcNativeSettingsScreen(model, snapshot, command, external, layout);
            screen = next; next.Closed += delegate { if (ReferenceEquals(screen, next)) screen = null; };
            MyGuiSandbox.AddScreen(next);
        }
        public void Close() { try { screen?.CloseScreen(true); } finally { screen = null; } }
    }

    internal sealed class PdcNativeSettingsScreen : MyGuiScreenBase
    {
        internal const int RowsPerView = 8;
        internal static readonly Vector4 Surface = new Vector4(.105f, .145f, .165f, .97f);
        internal static readonly Vector4 TextTint = new Vector4(.82f, .91f, .94f, 1);
        readonly PdcSettingsModel model;
        readonly Func<PdcSnapshot> snapshot;
        readonly Action<string> command;
        readonly Action external, layout;
        internal static bool EditingBinding;
        internal bool IsEditingText => FocusedControl is MyGuiControlTextbox;
        readonly List<Action> bindingPolls=new List<Action>();
        readonly List<Func<bool>> editors = new List<Func<bool>>();
        readonly List<Action<PdcSnapshot>> live = new List<Action<PdcSnapshot>>();
        static readonly int[] Views = new int[6];
        static int lastPage = 0;
        static readonly string[] Sections=new string[6], Search=new string[6];
        int page, tick;
        bool building, rebuild, committing, recordingAtBuild;
        string message = "ENTER/APPLY saves typed values. Closing or changing pages discards unsaved text.";
        MyGuiControlLabel status;
        sealed class Row
        {
            public PdcOption Option;
            public string Label, Button;
            public string Section="General";
            public Action Action;
            public Func<PdcSnapshot, string> Read;
        }
        public PdcNativeSettingsScreen(PdcSettingsModel model, Func<PdcSnapshot> snapshot, Action<string> command, Action external, Action layout)
            : base(new Vector2(.5f, .5f), Surface, new Vector2(.94f, .91f), true)
        {
            this.model = model; this.snapshot = snapshot; this.command = command; this.external = external; this.layout = layout;
            page = lastPage; DrawMouseCursor = true; CloseButtonEnabled = true; EnabledBackgroundFade = true;
            CanHideOthers = false; CanBeHidden = false; Build();
        }
        public override string GetFriendlyName() { return "ZeoPdcNativeSettings"; }
        public override bool Update(bool hasFocus)
        {
            bool result = base.Update(hasFocus);
            if(hasFocus) foreach(var poll in bindingPolls) poll();
            if (model.IsRecording != recordingAtBuild) rebuild = true;
            if (rebuild && !building) { rebuild = false; Build(); }
            if (++tick % 12 == 0) { var s = snapshot(); foreach (var refresh in live) refresh(s); }
            return result;
        }
        // Navigation must never depend on validation or recording locks. Explicit
        // ENTER/APPLY owns text commits; closing cancels remaining drafts.
        public override bool CloseScreen(bool isUnloading = false) { EditingBinding=false; return base.CloseScreen(isUnloading); }
        List<Row> Rows()
        {
            string name = PdcSettingsCatalog.Pages[page]; var rows = new List<Row>();
            if (name == "OVERVIEW")
            {
                rows.Add(new Row {Label="Choose HUD data and text size",Button="CUSTOMIZE HUD",Action=()=>{Sections[3]="Layout & text";Search[3]="";NavigateTo(3);} });
                rows.Add(new Row {Label="Menu key",Button="KEY BINDINGS",Action=()=>{Sections[1]="Controls & keys";Search[1]="";NavigateTo(1);} });
                rows.Add(new Row { Read = s => s.Ship ?? "No controlled ship" });
                rows.Add(new Row { Read = s => "PDC " + s.PdcOnline + "/" + s.PdcCount + "     Heat " + s.AverageHeatPercent.ToString("0.0") + "%     Hottest " + s.HottestHeatPercent.ToString("0.0") + "%" });
                rows.Add(new Row { Read = s => "Incoming " + s.ActiveInbound + "     Shots " + s.TotalShots + "     " + s.ControlState });
                rows.Add(new Row { Read = s => "Decoys: " + (s.DecoyStatus ?? "WAITING") });
                rows.Add(new Row { Read = s => s.RangeBankStatus ?? "Range banks waiting" });
                foreach (var gun in snapshot().Pdcs)
                {
                    long id = gun.EntityId; int part = gun.Part;
                    rows.Add(new Row { Read = s => {
                        var g = s.Pdcs.FirstOrDefault(x => x.EntityId == id && x.Part == part);
                        return g == null ? "PDC no longer present" : g.Name + "   HP " + g.HpPercent.ToString("0") + "%   heat " + g.HeatPercent.ToString("0") + "%   ROF " + (g.Rof * 100).ToString("0") + "%   T" + g.TrackId + "   " + (g.Allowed ? "FIRE " : "HOLD ") + g.PkState;
                    } });
                }
            }
            if (name == "HUD") rows.Add(new Row { Section="Layout & text", Label = "Move or resize the PDC HUD", Button = "EDIT HUD LAYOUT", Action = () => { if (CloseScreen()) layout(); } });
            foreach (var option in PdcSettingsCatalog.Options.Where(o => o.Page == name))
                if (option.ObservationOnly) rows.Insert(0, new Row { Option = option, Section=PdcSettingsCatalog.Section(option) });
                else rows.Add(new Row { Option = option, Section=PdcSettingsCatalog.Section(option) });
            return rows;
        }
        void Build()
        {
            building = true;
            try
            {
                FocusedControl = null;
                Controls.Clear(); editors.Clear(); live.Clear(); bindingPolls.Clear(); EditingBinding=false; model.Reload();
                recordingAtBuild = model.IsRecording;
                AddCaption("ZEO PDC // POINT DEFENSE", TextTint, new Vector2(0, -.417f), .88f);
                for (int i = 0; i < PdcSettingsCatalog.Pages.Length; i++)
                {
                    int target = i;
                    var tab = Button(-.33f + i * .22f, -.348f, .205f, .045f, PdcSettingsCatalog.Pages[i], () => NavigateTo(target), .55f);
                    tab.Selected = page == i;
                    tab.ColorMask = page == i ? new Vector4(.75f, .95f, 1, 1) : new Vector4(.45f, .55f, .59f, 1);
                }
                Label(-.421f, -.267f, "PDC MANAGER: " + (model.Current.ManagedDefenseEnabled ? "ENABLED" : "DISABLED"), .65f);
                Label(.18f, -.267f, "KEY: " + model.Current.MenuKey, .60f);
                var allRows=Rows();
                var groups=new[]{"All sections"}.Concat(allRows.Select(row=>row.Section).Distinct().OrderBy(x=>x)).ToList();
                if(!groups.Contains(Sections[page]))Sections[page]="All sections";
                var group=new MyGuiControlCombobox(new Vector2(-.255f,-.223f),new Vector2(.33f,.039f),openAreaItemsCount:7,originAlign:MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER);
                for(int i=0;i<groups.Count;i++)group.AddItem(i,groups[i]);group.SelectItemByKey(groups.IndexOf(Sections[page]));Controls.Add(group);
                group.ItemSelected+=delegate {if(!building){Sections[page]=groups[(int)group.GetSelectedKey()];Views[page]=0;rebuild=true;}};
                var search=new MyGuiControlTextbox(new Vector2(.145f,-.223f),Search[page]??"",70,null,.58f){Size=new Vector2(.28f,.038f)};Controls.Add(search);
                search.SetToolTip("Find a setting on this tab by name. Press ENTER or FIND. Page changes discard unsaved edits.");
                Action find=()=>{Search[page]=search.Text.Trim();Views[page]=0;rebuild=true;};search.EnterPressed+=delegate{find();};
                Button(.328f,-.223f,.072f,.039f,"FIND",find,.47f);Button(.401f,-.223f,.065f,.039f,"CLEAR",()=>{Search[page]="";Views[page]=0;rebuild=true;},.43f);
                string query=Search[page]??"";
                var rows=allRows.Where(row=>(Sections[page]=="All sections"||row.Section==Sections[page]) && (query.Length==0||((row.Option==null?(row.Label??row.Button??"Status"):(row.Option.Label+" "+row.Option.Key)).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0))).ToList();
                int views=Math.Max(1,(rows.Count+RowsPerView-1)/RowsPerView);
                int view = Views[page] = Math.Max(0, Math.Min(views - 1, Views[page]));
                Label(-.421f, -.166f, PdcSettingsCatalog.Pages[page] + "  /  " + (view + 1) + " OF " + views, .76f);
                Label(.16f, -.166f, (rows.Count == 0 ? 0 : view * RowsPerView + 1) + " - " + Math.Min(rows.Count, (view + 1) * RowsPerView) + " OF " + rows.Count, .57f);
                if(rows.Count==0)Label(-.421f,-.105f,"No matching settings. Clear FIND or choose All sections.",.62f);
                for (int i = 0; i < RowsPerView && view * RowsPerView + i < rows.Count; i++) AddRow(rows[view * RowsPerView + i], -.105f + i * .053f);
                Button(-.325f, .327f, .195f, .043f, "PREVIOUS", () => Navigate(-1), .62f).Enabled = view > 0;
                Button(.325f, .327f, .195f, .043f, "NEXT", () => Navigate(1), .62f).Enabled = view + 1 < views;
                Label(-.421f, .365f, "Choose a section or use FIND. ENTER/APPLY saves edits; navigation cancels drafts.", .47f);
                status = Label(-.421f, .387f, Short(message, 110), .50f); status.SetToolTip(message);
                Button(.335f, .425f, .170f, .044f, "CLOSE", () => CloseScreen(), .65f);
            }
            catch (Exception ex) { Message(ex.Message); }
            finally { building = false; }
        }
        void AddRow(Row row, float y)
        {
            if (row.Read != null)
            {
                var label = Label(-.421f, y, "", .57f);
                Action<PdcSnapshot> refresh = s => { string value = row.Read(s) ?? ""; label.Text = Short(value, 104); label.SetToolTip(value); };
                live.Add(refresh); refresh(snapshot()); return;
            }
            if (row.Action != null)
            {
                Label(-.421f, y, row.Label, .58f);
                Button(.275f, y, .294f, .041f, row.Button, () => Run(row.Action), .55f); return;
            }
            var o = row.Option;
            bool editable = !model.IsLocked(o);
            var title = Label(-.421f, y - .004f, Short(o.Label, 49), .60f);
            string detail = o.Number ? "Range " + o.Limit(model.Current, false) + " to " + o.Limit(model.Current, true) : o.Appearance ? "HUD / appearance" : "Saved defense configuration";
            if(o.Key=="HudTextScale")detail="Changes text and row spacing independently. Use panel width for long readouts.";
            if(o.Key=="HudWidth"||o.Key=="HudHeight")detail="Core-style independent dimensions. You can also drag the HUD edges.";
            if(o.Key.StartsWith("HudShow"))detail="Show or hide this HUD readout; does not change defense behavior.";
            if (o.Key.EndsWith("Rof") || o.Key == "ColdRofBoost") detail += ". Fraction of full fire rate (1 = 100%).";
            if (o.ObservationOnly) detail = "Read-only local feed. Does not arm recording or enable PDC defense. Live/current.json";
            if (o.Key == "HitRadiusMeters") detail += ". Reporting threshold; not measured ship damage.";
            if (o.Key == "ExpectedInbound") detail += ". Count torpedoes, not launchers: 16 x 2 = 32.";
            if (!editable) detail = "LOCKED while recording. Use STOP NOW / SAVE first. " + detail;
            if (o.Key == "Frame" || o.Key == "Theme") detail += ". Used when Follow ZeoCore Appearance is OFF.";
            title.SetToolTip(o.Key + "\n" + detail); Label(-.421f, y + .015f, Short(detail, 73), .40f);
            if(o.Key=="MenuKey")
            {
                string draft=o.Format(model.Current);int modifiers=0,armDelay=0;bool listening=false;
                MyGuiControlButton capture=null;
                Action refresh=()=>capture.Text=listening?"PRESS A KEY...":BindingLabel(draft,modifiers);
                capture=Button(.205f,y,.185f,.041f,"",()=>{listening=true;armDelay=2;FocusedControl=null;EditingBinding=true;refresh();Message("Press the menu key. APPLY saves; ESC cancels.");},.50f);
                refresh();capture.SetToolTip("Click to assign a key, then press APPLY. CLEAR disables the binding after APPLY.");capture.Enabled=editable;
                Button(.335f,y,.065f,.041f,"CLEAR",()=>{draft="None";modifiers=0;listening=false;EditingBinding=true;refresh();},.44f).Enabled=editable;
                Button(.407f,y,.065f,.041f,"APPLY",()=>Run(()=>{if(listening){Message("Press a key before APPLY, or CLEAR to disable.");return;}model.SaveKeyBinding(o.Key,draft,modifiers);EditingBinding=false;Message("Binding saved: "+BindingLabel(draft,modifiers));}),.43f).Enabled=editable;
                bindingPolls.Add(()=>{
                    if(!listening || MyAPIGateway.Input==null)return;
                    if(armDelay>0){armDelay--;return;}
                    foreach(MyKeys key in Enum.GetValues(typeof(MyKeys))) {
                        int code=(int)key;
                        if(code<8 || code==16 || code==17 || code==18 || code==27 || code>=160&&code<=165)continue;
                        if(!MyAPIGateway.Input.IsNewKeyPressed(key))continue;
                        draft=key.ToString();modifiers=0;
                        listening=false;refresh();Message("Selected "+BindingLabel(draft,modifiers)+". Click APPLY to save.");break;
                    }
                });
            }
            else if(o.Key=="HudTextScale")
            {
                var sizes=new[]{.75,1,1.25,1.5,1.75,2}.ToList();double saved=model.Current.HudTextScale;
                if(!sizes.Contains(saved)){sizes.Add(saved);sizes.Sort();}
                var combo=new MyGuiControlCombobox(new Vector2(.275f,y),new Vector2(.294f,.041f),openAreaItemsCount:7,originAlign:MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER);
                for(int i=0;i<sizes.Count;i++)combo.AddItem(i,(sizes[i]*100).ToString("0")+"%"+(sizes[i]==1?" (normal)":sizes[i]==1.5?" (large)":sizes[i]==2?" (extra large)":""));
                combo.SelectItemByKey(sizes.IndexOf(saved));combo.Enabled=editable;
                combo.ItemSelected+=delegate{if(!building)Apply(o,sizes[(int)combo.GetSelectedKey()].ToString(CultureInfo.InvariantCulture));};Controls.Add(combo);
            }
            else if (o.Field.FieldType == typeof(bool))
            {
                bool value = (bool)o.Field.GetValue(model.Current);
                var state = Label(.088f, y, value ? "ON" : "OFF", .65f);
                var on = Button(.232f, y, .095f, .041f, value ? "[X] ON" : "ON", () => Apply(o, "true"));
                var off = Button(.343f, y, .095f, .041f, value ? "OFF" : "[X] OFF", () => Apply(o, "false"));
                on.Enabled = off.Enabled = editable;
                on.Selected = value; off.Selected = !value;
                on.ColorMask = value ? new Vector4(.75f, .95f, 1, 1) : new Vector4(.35f, .43f, .47f, 1);
                off.ColorMask = !value ? new Vector4(.75f, .95f, 1, 1) : new Vector4(.35f, .43f, .47f, 1);
            }
            else if (o.Choices != null)
            {
                var choices = o.Choices.ToList(); string selected = o.Format(model.Current);
                if (!choices.Contains(selected)) choices.Insert(0, selected); // retain legacy/custom saved value
                var combo = new MyGuiControlCombobox(new Vector2(.275f, y), new Vector2(.294f, .041f), openAreaItemsCount: 6, toolTip: o.Label,
                    originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, isAutoscaleEnabled: true, isAutoEllipsisEnabled: true, minTextScale: .55f);
                for (int i = 0; i < choices.Count; i++) combo.AddItem(i, choices[i]);
                combo.SelectItemByKey(choices.IndexOf(selected));
                combo.Enabled = editable;
                combo.ItemSelected += delegate { if (!building) Apply(o, choices[(int)combo.GetSelectedKey()]); };
                Controls.Add(combo);
            }
            else
            {
                string saved = o.Format(model.Current);
                var edit = new MyGuiControlTextbox(new Vector2(.235f, y), saved, 100, null, .62f);
                edit.OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER; edit.Size = new Vector2(.245f, .040f); Controls.Add(edit);
                edit.Enabled = editable;
                Func<bool> commit = () => {
                    if (edit.Text == saved) return true;
                    if (!Apply(o, edit.Text)) return false;
                    saved = o.Format(model.Current); edit.Text = saved; return true;
                };
                editors.Add(commit); edit.EnterPressed += delegate { Commit(); };
                Button(.397f, y, .065f, .041f, "APPLY", () => Commit(), .46f).Enabled = editable;
            }
        }
        internal static string BindingLabel(string key,int mask)
        { return key=="None"?"None / click to assign":((mask&1)!=0?"Ctrl + ":"")+((mask&2)!=0?"Alt + ":"")+((mask&4)!=0?"Shift + ":"")+key; }
        bool Apply(PdcOption option, string value)
        {
            try { model.Apply(option.Key, value); Message("Saved: " + option.Label); rebuild = true; return true; }
            catch (Exception ex) { Message(option.Label + ": " + ex.Message); return false; }
        }
        void Run(Action action) { try { action(); rebuild = true; } catch (Exception ex) { Message(ex.Message); } }
        bool Commit()
        {
            if (building || committing) return true; committing = true;
            try { foreach (var commit in editors) if (!commit()) { rebuild = false; return false; } return true; }
            finally { committing = false; }
        }
        void Navigate(int step) { Views[page] += step; rebuild = true; }
        void NavigateTo(int target) { page = lastPage = target; rebuild = true; }
        void Message(string text) { message = text; if (status != null) { status.Text = Short(text, 110); status.SetToolTip(text); } }
        static string Short(string s, int count) { return s.Length <= count ? s : s.Substring(0, count - 3) + "..."; }
        MyGuiControlLabel Label(float x, float y, string value, float scale)
        { var label = new MyGuiControlLabel(new Vector2(x, y), null, value, TextTint, scale, null, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER); Controls.Add(label); return label; }
        MyGuiControlButton Button(float x, float y, float width, float height, string text, Action action, float scale = .65f)
        {
            var button = new MyGuiControlButton(new Vector2(x, y), MyGuiControlButtonStyleEnum.Rectangular, new Vector2(width, height), null,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), scale,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, _ => action());
            Controls.Add(button); return button;
        }
    }
}
