using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ZeoOverlay
{
    // ZEOCORE_V12_LOCKED_UI_RESTORE
    // ZEOCORE_V12B_UI_USABILITY_FIX
    // ZEOCORE_V12C_CONTENT_BOUNDS_FIX
    // Full settings surface restored from the locked v0.6.7h3a buddy baseline.
    // Only requested additions are layered into their existing pages.
    internal sealed class SettingsForm : Form
    {
        // ZEOCORE_V13B_INTERACTIVE_MENU
        // V1.3b intentionally lets the settings form activate while open. The HUD stays
        // capture-safe and the owner hands focus back to Space Engineers when the menu closes.

        // ZEOCORE_V067G_SETTINGS_BINDING
        private readonly OverlaySettings _zeoSettings;
        private readonly HudOverlayForm _owner;
        private readonly List<Action> _refreshers = new List<Action>();
        private readonly List<FlowLayoutPanel> _pages = new List<FlowLayoutPanel>();
        private readonly List<ZeoDropDown> _dropDowns = new List<ZeoDropDown>();
        private bool _sync;
        private bool _shutdown;
        private bool _dragging;
        private Point _dragOffset;
        private Panel _header;
        private Panel _nav;
        private Panel _contentHost;
        private Label _brand;
        private Label _subBrand;
        private Label _captureStatus;
        private ZeoDropDown _pageDrop;
        private ZeoDropDown _profileDrop;
        private Button _menuKeyCapture;
        private bool _menuListening,_menuEditing;
        private int _menuDraft;
        private Panel _dropPopup;
        private ZeoDropDown _openDropDown;

        // ZEOCORE_V067F_VISIBLE_HUD_CONTROLS
        // ZEOCORE_V067H1_SINGLE_FRAME_SELECTOR
        // ZEOCORE_V067H4_LAYOUT_CONTROLS
        // ZEOCORE_V067H4_NO_BOTTOM_BLOCKER
        // ZEOCORE_V067H4_COMBO_WHEEL_LOCK
        // ZEOCORE_V12_SAFE_DROPDOWN
        // No native ComboBox is used in this form. Dropdown lists are rendered
        // inside this same Zeo SettingsForm, so they cannot collapse as
        // separate native popup windows and mouse-wheel pass-by cannot change them.
        private NumericUpDown _zeoMaxMarkerSize;
        private CheckBox _zeoFollowGameHud;

        internal SettingsForm(OverlaySettings settings, HudOverlayForm owner)
        {
            _zeoSettings = settings;
            _owner = owner;

            Text = "ZeoCore";
            Width = 660;
            Height = 900;
            MinimumSize = new Size(660, 760);
            MaximumSize = new Size(660, 980);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point);

            BuildShell();
            BuildPages();
            ApplyAttachedStyle(this);
            RefreshFromSettings();

            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if(_menuEditing && e.KeyCode==Keys.Escape){_menuListening=false;_menuEditing=false;RefreshMenuBinding();e.Handled=true;e.SuppressKeyPress=true;return;}
                if(_menuListening){
                    int key=(int)e.KeyCode;
                    if(key!=0&&MenuBinding.Allowed(key)){_menuDraft=key;_menuListening=false;RefreshMenuBinding();}
                    e.Handled=true;e.SuppressKeyPress=true;return;
                }
                if (e.KeyCode == Keys.Escape)
                {
                    CloseDropDown();
                    _owner.CloseMenuToGame();
                    e.Handled = true;
                }
            };
        }

        private void RefreshMenuBinding()
        {
            int key=_menuEditing?_menuDraft:MenuBinding.Resolve(_zeoSettings.MenuKey,_zeoSettings.MenuKeyCode);
            _menuKeyCapture.Text=_menuListening?"PRESS KEY...":key==0?"ASSIGN KEY":((Keys)key).ToString().ToUpperInvariant();
        }

        private void BuildShell()
        {
            _header = new Panel();
            _header.Dock = DockStyle.Top;
            _header.Height = 50;
            _header.Tag = "HEADER";
            _header.MouseDown += HeaderMouseDown;
            _header.MouseMove += HeaderMouseMove;
            _header.MouseUp += HeaderMouseUp;
            _header.Paint += PaintHeader;
            Controls.Add(_header);

            _brand = new Label();
            _brand.AutoSize = true;
            _brand.Text = "ZEO CORE // TACTICAL HUD";
            _brand.Font = new Font(Font.FontFamily, 11.0f, FontStyle.Bold);
            _brand.Location = new Point(16, 10);
            _brand.Tag = "PRIMARY";
            _brand.MouseDown += HeaderMouseDown;
            _brand.MouseMove += HeaderMouseMove;
            _brand.MouseUp += HeaderMouseUp;
            _header.Controls.Add(_brand);

            _subBrand = new Label();
            _subBrand.AutoSize = true;
            _subBrand.Text = "V1.3c // KEEN NATIVE POLISH";
            _subBrand.Font = new Font(Font.FontFamily, 7.8f, FontStyle.Regular);
            _subBrand.Location = new Point(18, 30);
            _subBrand.Tag = "SECONDARY";
            _subBrand.MouseDown += HeaderMouseDown;
            _subBrand.MouseMove += HeaderMouseMove;
            _subBrand.MouseUp += HeaderMouseUp;
            _header.Controls.Add(_subBrand);

            _captureStatus = new Label();
            _captureStatus.AutoSize = false;
            _captureStatus.Text = "STREAM SAFE: WAIT / VERIFY";
            _captureStatus.Font = new Font(Font.FontFamily, 7.5f, FontStyle.Bold);
            _captureStatus.Location = new Point(365, 14);
            _captureStatus.Size = new Size(235, 22);
            _captureStatus.TextAlign = ContentAlignment.MiddleRight;
            _captureStatus.AutoEllipsis = false;
            _captureStatus.Tag = "SECONDARY";
            _header.Controls.Add(_captureStatus);

            Button close = new Button();
            close.Text = "X";
            close.FlatStyle = FlatStyle.Flat;
            close.FlatAppearance.BorderSize = 0;
            close.Size = new Size(38, 32);
            close.Location = new Point(614, 8);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.TabStop = false;
            close.Tag = "CLOSE";
            close.MouseDown += delegate { CloseDropDown(); };
            close.Click += delegate { _owner.CloseMenuToGame(); };
            _header.Controls.Add(close);

            _nav = new Panel();
            _nav.Dock = DockStyle.Top;
            _nav.Height = 82;
            _nav.Tag = "NAV";
            Controls.Add(_nav);
            _nav.BringToFront();

            AddNavLabel("PAGE", 16);
            AddNavLabel("PROFILE", 225);
            AddNavLabel("MENU HOTKEY", 440);

            _pageDrop = CreateDropDown(new[]
            {
                "FLIGHT", "SCOPE", "FLEET", "LAYOUT", "THEME", "MARKERS",
                "PRIVACY", "CAPTURE", "AMMO", "ROSTER", "DISTRESS"
            }, 16, 37, 190,
            delegate { return _zeoSettings.MenuPage; },
            delegate(int v)
            {
                _zeoSettings.MenuPage = v;
                ShowPage(v);
            });
            _nav.Controls.Add(_pageDrop);

            _profileDrop = CreateDropDown(new[]
            {
                "MINIMAL", "ESSENTIAL", "THREAT PRIORITY", "FULL TACTICAL", "CUSTOM"
            }, 225, 37, 195,
            delegate { return _zeoSettings.Profile; },
            delegate(int v)
            {
                _zeoSettings.ApplyProfile(v);
                RefreshFromSettings();
            });
            _nav.Controls.Add(_profileDrop);

            _menuKeyCapture=new Button {Left=440,Top=37,Width=118,Height=28,FlatStyle=FlatStyle.Flat};
            _menuKeyCapture.Click+=delegate{_menuDraft=MenuBinding.Resolve(_zeoSettings.MenuKey,_zeoSettings.MenuKeyCode);_menuEditing=true;_menuListening=true;RefreshMenuBinding();};
            var applyMenuKey=new Button {Left=562,Top=37,Width=68,Height=28,Text="APPLY",FlatStyle=FlatStyle.Flat};
            applyMenuKey.Click+=delegate{
                if(!_menuEditing||_menuListening)return;
                _zeoSettings.Reload();
                string conflict=MenuBinding.Conflict(_menuDraft,_zeoSettings.DistressEnabled,_zeoSettings.DistressKey,_zeoSettings.QuickRefillKey,_zeoSettings.TargetMarkKey);
                if(conflict!=null){MessageBox.Show(this,conflict,"Menu key");return;}
                _zeoSettings.MenuKeyCode=_menuDraft;_zeoSettings.Save();_zeoSettings.Reload();
                if(_zeoSettings.MenuKeyCode!=_menuDraft){MessageBox.Show(this,"Menu key could not be saved.","Menu key");return;}
                _menuEditing=false;RefreshMenuBinding();_owner.SettingsChanged();
            };
            _nav.Controls.Add(_menuKeyCapture);_nav.Controls.Add(applyMenuKey);
            _refreshers.Add(RefreshMenuBinding);RefreshMenuBinding();

            _contentHost = new Panel();
            // ZEOCORE_V12C_CONTENT_BOUNDS_FIX
            // The old Dock=Fill host was allowed to occupy the full client area,
            // which placed the first ~132 px of every page behind NAV + HEADER.
            // That hid CONTACT ICONS / HUD FRAME rows and made short pages such as
            // PRIVACY look empty. Keep the page host physically below the chrome.
            _contentHost.Dock = DockStyle.None;
            _contentHost.Location = new Point(0, _nav.Height + _header.Height);
            _contentHost.Size = new Size(
                ClientSize.Width,
                Math.Max(0, ClientSize.Height - _nav.Height - _header.Height));
            _contentHost.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _contentHost.Tag = "ROOT";
            Controls.Add(_contentHost);
            _contentHost.SendToBack();
            _nav.BringToFront();
            _header.BringToFront();
        }

        private void AddNavLabel(string text, int x)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(x, 14);
            l.Font = new Font(Font.FontFamily, 7.5f, FontStyle.Bold);
            l.Tag = "SECONDARY";
            _nav.Controls.Add(l);
        }

        private void BuildPages()
        {
            BuildFlightPage();
            BuildScopePage();
            BuildFleetPage();
            BuildLayoutPage();
            BuildThemePage();
            BuildMarkersPage();
            BuildPrivacyPage();
            BuildCapturePage();
            BuildAmmoPage();
            BuildRosterPage();
            BuildDistressPage();
            ShowPage(0);
        }

        private FlowLayoutPanel NewPage()
        {
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.AutoScroll = true;
            p.AutoScrollMargin = new Size(0, 36);
            p.FlowDirection = FlowDirection.TopDown;
            p.WrapContents = false;
            p.Padding = new Padding(16, 12, 16, 72);
            p.Visible = false;
            p.Tag = "PAGE";
            p.MouseDown += delegate { CloseDropDown(); };
            p.ControlAdded += delegate { UpdatePageScrollRange(p); };
            p.SizeChanged += delegate { UpdatePageScrollRange(p); };
            _contentHost.Controls.Add(p);
            _pages.Add(p);
            return p;
        }

        private Control Section(string text)
        {
            ZeoSectionHeader h = new ZeoSectionHeader();
            h.Text = text;
            h.Width = 592;
            h.Height = 36;
            h.Margin = new Padding(0, 10, 0, 4);
            h.Font = new Font(Font.FontFamily, 8.8f, FontStyle.Bold);
            h.Tag = "SECTION";
            return h;
        }

        private Control Note(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Width = 592;
            l.AutoSize = false;
            Size measured = TextRenderer.MeasureText(text ?? "", Font, new Size(568, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            l.Height = Math.Max(34, measured.Height + 16);
            l.Padding = new Padding(8, 6, 8, 6);
            l.Margin = new Padding(0, 0, 0, 5);
            l.Tag = "NOTE";
            return l;
        }

        private int MeasureNoteHeight(string text)
        {
            int lines = Math.Max(1, (text == null ? 0 : text.Length) / 92 + 1);
            return 12 + lines * 18;
        }

        private Panel RowPanel(int height)
        {
            Panel p = new Panel();
            p.Width = 592;
            p.Height = height;
            p.Margin = new Padding(0, 1, 0, 1);
            p.Tag = "ROW";
            p.MouseDown += delegate { CloseDropDown(); };
            return p;
        }

        private Label RowLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoEllipsis = true;
            l.Location = new Point(11, 11);
            l.Size = new Size(310, 22);
            l.Tag = "SECONDARY";
            return l;
        }

        private CheckBox BoolRow(string label, Func<bool> getter, Action<bool> setter)
        {
            CheckBox c = new CheckBox();
            c.Text = label;
            c.Width = 592;
            c.Height = 35;
            c.Margin = new Padding(0, 1, 0, 1);
            c.Padding = new Padding(9, 0, 0, 0);
            c.FlatStyle = FlatStyle.Flat;
            c.Tag = "ROW";
            c.CheckedChanged += delegate
            {
                if (_sync) return;
                setter(c.Checked);
                SaveNow();
            };
            _refreshers.Add(delegate
            {
                bool value = getter();
                if (c.Checked != value) c.Checked = value;
            });
            return c;
        }

        private NumericUpDown MakeNumber(decimal min, decimal max, decimal increment, int decimals)
        {
            NumericUpDown n = new NumericUpDown();
            n.DecimalPlaces = decimals;
            n.Minimum = min;
            n.Maximum = max;
            n.Increment = increment;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.Location = new Point(443, 8);
            n.Size = new Size(126, 25);
            n.Tag = "EDITOR";
            return n;
        }

        private Panel NumberRow(string label, decimal min, decimal max, decimal increment, int decimals, Func<double> getter, Action<double> setter)
        {
            Panel row = RowPanel(42);
            row.Controls.Add(RowLabel(label));
            NumericUpDown n = MakeNumber(min, max, increment, decimals);
            n.ValueChanged += delegate
            {
                if (_sync) return;
                setter((double)n.Value);
                SaveNow();
            };
            _refreshers.Add(delegate
            {
                decimal v = (decimal)getter();
                if (v < n.Minimum) v = n.Minimum;
                if (v > n.Maximum) v = n.Maximum;
                if (n.Value != v) n.Value = v;
            });
            row.Controls.Add(n);
            return row;
        }

        private Panel IntRow(string label, int min, int max, int increment, Func<int> getter, Action<int> setter)
        {
            Panel row = RowPanel(42);
            row.Controls.Add(RowLabel(label));
            NumericUpDown n = MakeNumber(min, max, increment, 0);
            n.ValueChanged += delegate
            {
                if (_sync) return;
                setter((int)n.Value);
                SaveNow();
            };
            _refreshers.Add(delegate
            {
                int v = getter();
                if (v < min) v = min;
                if (v > max) v = max;
                if ((int)n.Value != v) n.Value = v;
            });
            row.Controls.Add(n);
            return row;
        }

        // ZEOCORE_V12B_COLOR_PICKER
        private Panel ColorRow(string label, Func<string> getter, Action<string> setter)
        {
            Panel row = RowPanel(46);
            row.Controls.Add(RowLabel(label));

            Panel swatch = new Panel();
            swatch.Location = new Point(326, 8);
            swatch.Size = new Size(76, 28);
            swatch.BorderStyle = BorderStyle.FixedSingle;
            swatch.Cursor = Cursors.Hand;
            swatch.Tag = "COLOR_SWATCH";
            row.Controls.Add(swatch);

            Button pick = new Button();
            pick.Text = "PICK COLOR";
            pick.Location = new Point(412, 7);
            pick.Size = new Size(157, 30);
            pick.FlatStyle = FlatStyle.Flat;
            pick.Tag = "ACTION";
            pick.TabStop = false;
            row.Controls.Add(pick);

            Action choose = delegate
            {
                if (_sync) return;
                using (ColorDialog dialog = new ColorDialog())
                {
                    dialog.AnyColor = true;
                    dialog.FullOpen = true;
                    dialog.SolidColorOnly = false;
                    dialog.Color = _zeoSettings.ColorOf(getter(), Color.White);
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;

                    Color c = dialog.Color;
                    string value = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                    setter(value);
                    _zeoSettings.ThemePreset = 4;
                    SaveNow();
                    RefreshFromSettings();
                    swatch.BackColor = c;
                }
            };

            swatch.Click += delegate { choose(); };
            pick.Click += delegate { choose(); };

            _refreshers.Add(delegate
            {
                swatch.BackColor = _zeoSettings.ColorOf(getter(), Color.White);
                swatch.Invalidate();
            });
            return row;
        }

        private static string NormalizeHex(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string x = value.Trim();
            if (!x.StartsWith("#")) x = "#" + x;
            if (x.Length != 7) return null;
            for (int i = 1; i < x.Length; i++) if (!Uri.IsHexDigit(x[i])) return null;
            return x.ToUpperInvariant();
        }

        // ZEOCORE_V12B_STREAMER_MODE_TOGGLE
        private Panel ToggleButtonRow(string label, Func<bool> getter, Action<bool> setter)
        {
            Panel row = RowPanel(44);
            row.Controls.Add(RowLabel(label));
            Button toggle = new Button();
            toggle.Location = new Point(326, 7);
            toggle.Size = new Size(243, 30);
            toggle.FlatStyle = FlatStyle.Flat;
            toggle.TabStop = false;
            row.Controls.Add(toggle);

            Action refresh = delegate
            {
                bool on = getter();
                toggle.Text = on ? "ON // CAPTURE EXCLUDED" : "OFF // NORMAL CAPTURE";
                toggle.Tag = on ? "TOGGLE_ON" : "TOGGLE_OFF";
                ApplyAttachedStyle(toggle.Parent ?? toggle);
            };

            toggle.Click += delegate
            {
                if (_sync) return;
                setter(!getter());
                SaveNow();
                RefreshFromSettings();
            };

            _refreshers.Add(refresh);
            return row;
        }

        // ZEOCORE_V12B_SCROLL_GUARD
        private void UpdatePageScrollRange(FlowLayoutPanel p)
        {
            if (p == null || p.IsDisposed) return;
            int total = p.Padding.Top + p.Padding.Bottom + 24;
            foreach (Control c in p.Controls)
            {
                if (!c.Visible) continue;
                total += c.Height + c.Margin.Top + c.Margin.Bottom;
            }
            p.AutoScrollMinSize = new Size(0, Math.Max(0, total));
        }

        private Panel DropRow(string label, string[] items, Func<int> getter, Action<int> setter)
        {
            Panel row = RowPanel(42);
            row.Controls.Add(RowLabel(label));
            ZeoDropDown d = CreateDropDown(items, 326, 7, 243, getter, setter);
            row.Controls.Add(d);
            return row;
        }

        private ZeoDropDown CreateDropDown(string[] items, int x, int y, int width, Func<int> getter, Action<int> setter)
        {
            ZeoDropDown d = new ZeoDropDown();
            d.Location = new Point(x, y);
            d.Size = new Size(width, 28);
            d.Items = items ?? new string[0];
            d.Tag = "EDITOR";
            d.RequestOpen += delegate(ZeoDropDown sender) { ShowDropDown(sender); };
            d.SelectionChanged += delegate(int index)
            {
                if (_sync) return;
                setter(index);
                SaveNow();
            };
            _refreshers.Add(delegate
            {
                int max = Math.Max(0, d.Items.Length - 1);
                int v = Math.Max(0, Math.Min(max, getter()));
                d.SetSelectedIndex(v, false);
            });
            _dropDowns.Add(d);
            return d;
        }

        private void ShowDropDown(ZeoDropDown d)
        {
            if (d == null || d.Items == null || d.Items.Length == 0) return;
            if (_openDropDown == d && _dropPopup != null)
            {
                CloseDropDown();
                return;
            }

            CloseDropDown();
            _openDropDown = d;
            d.SetWheelArmed(true);

            Panel pop = new Panel();
            pop.Tag = "POPUP";
            pop.AutoScroll = true;
            pop.BorderStyle = BorderStyle.FixedSingle;
            int rowH = 27;
            int h = Math.Min(270, d.Items.Length * rowH + 2);
            pop.Size = new Size(d.Width, h);

            Point screen = d.PointToScreen(new Point(0, d.Height + 1));
            Point local = PointToClient(screen);
            int px = Math.Max(4, Math.Min(ClientSize.Width - pop.Width - 4, local.X));
            int py = local.Y;
            if (py + pop.Height > ClientSize.Height - 4)
            {
                Point aboveScreen = d.PointToScreen(new Point(0, -pop.Height - 1));
                py = PointToClient(aboveScreen).Y;
            }
            py = Math.Max(4, Math.Min(ClientSize.Height - pop.Height - 4, py));
            pop.Location = new Point(px, py);

            for (int i = 0; i < d.Items.Length; i++)
            {
                int capture = i;
                Button b = new Button();
                b.Text = d.Items[i];
                b.TextAlign = ContentAlignment.MiddleLeft;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.TabStop = false;
                b.Location = new Point(0, i * rowH);
                b.Size = new Size(pop.ClientSize.Width - 1, rowH);
                b.Tag = i == d.SelectedIndex ? "POPSELECT" : "POPITEM";
                b.MouseDown += delegate(object sender, MouseEventArgs e)
                {
                    if (e.Button != MouseButtons.Left) return;
                    d.SetSelectedIndex(capture, true);
                    CloseDropDown();
                };
                pop.Controls.Add(b);
            }

            _dropPopup = pop;
            Controls.Add(pop);
            ApplyAttachedStyle(pop);
            pop.BringToFront();
        }

        private void CloseDropDown()
        {
            if (_dropPopup != null)
            {
                try { Controls.Remove(_dropPopup); } catch { }
                try { _dropPopup.Dispose(); } catch { }
                _dropPopup = null;
            }
            if (_openDropDown != null)
            {
                _openDropDown.SetWheelArmed(false);
                _openDropDown = null;
            }
        }

        private void BuildFlightPage()
        {
            FlowLayoutPanel p = NewPage();
            p.Controls.Add(Section("FLIGHT DISPLAY"));
            p.Controls.Add(BoolRow("HUD enabled", delegate { return _zeoSettings.HudEnabled; }, delegate(bool v) { _zeoSettings.HudEnabled = v; }));
            p.Controls.Add(BoolRow("Center flight crosshair", delegate { return _zeoSettings.ShowCrosshair; }, delegate(bool v) { _zeoSettings.ShowCrosshair = v; }));
            p.Controls.Add(BoolRow("SHIP STATUS panel", delegate { return _zeoSettings.ShowFlightData; }, delegate(bool v) { _zeoSettings.ShowFlightData = v; }));
            p.Controls.Add(BoolRow("TRACKS ON SCOPE panel", delegate { return _zeoSettings.ShowTrackPanel; }, delegate(bool v) { _zeoSettings.ShowTrackPanel = v; }));
            p.Controls.Add(BoolRow("Optional compact FleetLink strip", delegate { return _zeoSettings.ShowLinkPanel; }, delegate(bool v) { _zeoSettings.ShowLinkPanel = v; }));
            p.Controls.Add(BoolRow("Show H20", delegate { return _zeoSettings.ShowShipH2O; }, delegate(bool v) { _zeoSettings.ShowShipH2O = v; }));
            p.Controls.Add(BoolRow("Show O2", delegate { return _zeoSettings.ShowShipO2; }, delegate(bool v) { _zeoSettings.ShowShipO2 = v; }));
            p.Controls.Add(BoolRow("Show Fusion Pellets", delegate { return _zeoSettings.ShowShipFusion; }, delegate(bool v) { _zeoSettings.ShowShipFusion = v; }));
            p.Controls.Add(IntRow("Fusion reserve target", 100, 1000000, 100, delegate { return _zeoSettings.FusionReserveTarget; }, delegate(int v) { _zeoSettings.FusionReserveTarget = v; }));
            p.Controls.Add(BoolRow("Show Drive health", delegate { return _zeoSettings.ShowShipDrive; }, delegate(bool v) { _zeoSettings.ShowShipDrive = v; }));
            p.Controls.Add(BoolRow("Show Reactor health", delegate { return _zeoSettings.ShowShipReactor; }, delegate(bool v) { _zeoSettings.ShowShipReactor = v; }));
            p.Controls.Add(BoolRow("Show Power output", delegate { return _zeoSettings.ShowShipPower; }, delegate(bool v) { _zeoSettings.ShowShipPower = v; }));
            p.Controls.Add(BoolRow("Show Ship HP", delegate { return _zeoSettings.ShowShipHp; }, delegate(bool v) { _zeoSettings.ShowShipHp = v; }));
            p.Controls.Add(BoolRow("Show Speed", delegate { return _zeoSettings.ShowShipSpeed; }, delegate(bool v) { _zeoSettings.ShowShipSpeed = v; }));

            p.Controls.Add(Section("LABEL DETAIL"));
            p.Controls.Add(BoolRow("Show names when relevant", delegate { return _zeoSettings.ShowNames; }, delegate(bool v) { _zeoSettings.ShowNames = v; }));
            p.Controls.Add(BoolRow("Show distance when relevant", delegate { return _zeoSettings.ShowDistance; }, delegate(bool v) { _zeoSettings.ShowDistance = v; }));
            p.Controls.Add(BoolRow("Show closing on priority contacts", delegate { return _zeoSettings.ShowClosingOnPriority; }, delegate(bool v) { _zeoSettings.ShowClosingOnPriority = v; }));
        }

        private void BuildScopePage()
        {
            FlowLayoutPanel p = NewPage();
            p.Controls.Add(Section("SCOPE SOURCES"));
            p.Controls.Add(BoolRow("Local Spectrum signals", delegate { return _zeoSettings.ShowLocalSpectrum; }, delegate(bool v) { _zeoSettings.ShowLocalSpectrum = v; }));
            p.Controls.Add(BoolRow("Local WeaponCore tracks", delegate { return _zeoSettings.ShowLocalWeaponCore; }, delegate(bool v) { _zeoSettings.ShowLocalWeaponCore = v; }));
            p.Controls.Add(BoolRow("Hostile contacts", delegate { return _zeoSettings.ShowHostiles; }, delegate(bool v) { _zeoSettings.ShowHostiles = v; }));
            p.Controls.Add(BoolRow("Neutral / unknown contacts", delegate { return _zeoSettings.ShowNeutrals; }, delegate(bool v) { _zeoSettings.ShowNeutrals = v; }));

            p.Controls.Add(Section("OBSERVER MODE"));
            p.Controls.Add(BoolRow("Keep TOS visible outside ship", delegate { return _zeoSettings.KeepTosOutsideShip; }, delegate(bool v) { _zeoSettings.KeepTosOutsideShip = v; }));
            p.Controls.Add(Note("When enabled, TOS continues to show local Spectrum and authorized shared tactical tracks while on foot. Crosshair, flight data, ammo and world markers stay off outside a controlled ship."));

            p.Controls.Add(Section("FLIGHT HUD SCOPE"));
            p.Controls.Add(BoolRow("Scope STATUS / speed-vector columns", delegate { return _zeoSettings.ScopeShowStatus; }, delegate(bool v) { _zeoSettings.ScopeShowStatus = v; }));
            p.Controls.Add(BoolRow("Scope CORE / name column", delegate { return _zeoSettings.ScopeShowCore; }, delegate(bool v) { _zeoSettings.ScopeShowCore = v; }));
            p.Controls.Add(IntRow("Scope capacity (tracks)", 3, 16, 1, delegate { return _zeoSettings.ScopeRows; }, delegate(int v) { _zeoSettings.ScopeRows = v; }));
            p.Controls.Add(NumberRow("Scope text scale", 0.60M, 2.50M, 0.05M, 2, delegate { return _zeoSettings.ScopeTextScale; }, delegate(double v) { _zeoSettings.ScopeTextScale = v; }));
            p.Controls.Add(NumberRow("Scope width", 0.75M, 2.50M, 0.05M, 2, delegate { return _zeoSettings.ScopeWidthScale; }, delegate(double v) { _zeoSettings.ScopeWidthScale = v; }));
            p.Controls.Add(Note("Scope width stretches the panel and column spacing horizontally without increasing row height. Capacity still controls vertical size."));

            p.Controls.Add(Section("TRACK STABILITY"));
            p.Controls.Add(BoolRow("Fast camera marker updates", delegate { return _zeoSettings.FastCameraMarkers; }, delegate(bool v) { _zeoSettings.FastCameraMarkers = v; }));
            p.Controls.Add(BoolRow("Smooth / predict track motion between sensor updates", delegate { return _zeoSettings.PredictTrackMotion; }, delegate(bool v) { _zeoSettings.PredictTrackMotion = v; }));
            p.Controls.Add(NumberRow("Prediction limit seconds", 0.10M, 2.50M, 0.05M, 2, delegate { return _zeoSettings.PredictionLimitSeconds; }, delegate(double v) { _zeoSettings.PredictionLimitSeconds = v; }));

            p.Controls.Add(Section("RANGE + DECLUTTER"));
            p.Controls.Add(IntRow("Max contact markers", 1, 40, 1, delegate { return _zeoSettings.MaxContactMarkers; }, delegate(int v) { _zeoSettings.MaxContactMarkers = v; }));
            p.Controls.Add(NumberRow("Max range km", 5M, 500M, 5M, 0, delegate { return _zeoSettings.MaxRangeKm; }, delegate(double v) { _zeoSettings.MaxRangeKm = v; }));
            p.Controls.Add(BoolRow("Declutter overlapping markers", delegate { return _zeoSettings.Declutter; }, delegate(bool v) { _zeoSettings.Declutter = v; }));
            p.Controls.Add(NumberRow("Declutter radius", 0.005M, 0.200M, 0.005M, 3, delegate { return _zeoSettings.DeclutterRadius; }, delegate(double v) { _zeoSettings.DeclutterRadius = v; }));
            p.Controls.Add(BoolRow("Suppress subgrid / wreck clutter", delegate { return _zeoSettings.SuppressSubgridClutter; }, delegate(bool v) { _zeoSettings.SuppressSubgridClutter = v; }));
            p.Controls.Add(Note("ON: mechanically connected target grids are represented by one primary grid. Pieces that detach while the target breaks apart are held out of TOS/world markers briefly so rotors, hinges and wreck fragments do not flood the HUD."));
            p.Controls.Add(IntRow("Stale after seconds", 2, 60, 1, delegate { return _zeoSettings.StaleSeconds; }, delegate(int v) { _zeoSettings.StaleSeconds = v; }));
            p.Controls.Add(IntRow("Last-known timeout", 5, 120, 1, delegate { return _zeoSettings.LastKnownSeconds; }, delegate(int v) { _zeoSettings.LastKnownSeconds = v; }));

            p.Controls.Add(Section("PERFORMANCE"));
            p.Controls.Add(BoolRow("Adaptive tactical processing", delegate { return _zeoSettings.AdaptiveTacticalRate; }, delegate(bool v) { _zeoSettings.AdaptiveTacticalRate = v; }));
            p.Controls.Add(IntRow("Processing cap / source", 24, 192, 8, delegate { return _zeoSettings.TacticalProcessingCap; }, delegate(int v) { _zeoSettings.TacticalProcessingCap = v; }));
            p.Controls.Add(Note("The overlay can still publish smoothly while expensive target fusion runs at a lower cadence. Under heavy contact counts Zeo automatically reduces fusion frequency instead of stealing Space Engineers simulation time."));
        }

        private void BuildFleetPage()
        {
            FlowLayoutPanel p = NewPage();
            p.Controls.Add(Section("FLEET / SHARED PICTURE"));
            p.Controls.Add(BoolRow("Receive shared FleetLink picture", delegate { return _zeoSettings.ReceiveFleetLink; }, delegate(bool v) { _zeoSettings.ReceiveFleetLink = v; }));
            p.Controls.Add(BoolRow("Zeo remote friendlies", delegate { return _zeoSettings.ShowFleetFriendlies; }, delegate(bool v) { _zeoSettings.ShowFleetFriendlies = v; }));
            p.Controls.Add(BoolRow("Zeo shared WeaponCore threats", delegate { return _zeoSettings.ShowSharedContacts; }, delegate(bool v) { _zeoSettings.ShowSharedContacts = v; }));
            p.Controls.Add(BoolRow("Zeo shared Spectrum signals", delegate { return _zeoSettings.ShowSharedSignals; }, delegate(bool v) { _zeoSettings.ShowSharedSignals = v; }));
            p.Controls.Add(BoolRow("Friendly world markers", delegate { return _zeoSettings.ShowFriendlyMarkers; }, delegate(bool v) { _zeoSettings.ShowFriendlyMarkers = v; }));
            p.Controls.Add(IntRow("Max friendly markers", 1, 24, 1, delegate { return _zeoSettings.MaxFriendlyMarkers; }, delegate(int v) { _zeoSettings.MaxFriendlyMarkers = v; }));
            p.Controls.Add(BoolRow("Unlimited-distance same-sector friendly markers", delegate { return _zeoSettings.RemoteFriendlyNoRangeLimit; }, delegate(bool v) { _zeoSettings.RemoteFriendlyNoRangeLimit = v; }));
            p.Controls.Add(BoolRow("Unlimited-distance shared tactical tracks", delegate { return _zeoSettings.SharedTacticalNoRangeLimit; }, delegate(bool v) { _zeoSettings.SharedTacticalNoRangeLimit = v; }));

            p.Controls.Add(Section("TACTICAL SHARING"));
            p.Controls.Add(BoolRow("Share my WeaponCore threats", delegate { return _zeoSettings.ShareWeaponCoreContacts; }, delegate(bool v) { _zeoSettings.ShareWeaponCoreContacts = v; }));
            p.Controls.Add(BoolRow("Share my Spectrum signals", delegate { return _zeoSettings.ShareSpectrumSignals; }, delegate(bool v) { _zeoSettings.ShareSpectrumSignals = v; }));
            p.Controls.Add(Note("Fleet roster publication remains limited to the ship you are actively piloting. Parked, docked, printer and unmanned friendly grids are not exported as fleet members."));

            p.Controls.Add(Section("SECTOR SAFETY"));
            p.Controls.Add(Note("Friendly world markers remain same-sector only. Shared threats/signals are same-sector tactical tracks. Distress may optionally use the proven global GPS frame across sectors."));
            p.Controls.Add(Section("DESIGN RULE"));
            p.Controls.Add(Note("Local Spectrum/WC remains the immediate local picture. FleetLink adds authorized remote pilot and sensor data; it does not replace local sensors."));
        }

        private void BuildLayoutPage()
        {
            FlowLayoutPanel p = NewPage();
            p.Controls.Add(Section("HUD FRAME STYLE"));
            p.Controls.Add(DropRow("HUD FRAME", new[]
            {
                "SE INDUSTRIAL", "FIGHTER HUD", "MARS TACTICAL", "BELTER UTILITY",
                "NAVY GLASS", "STEALTH", "WAR ROOM", "COMMAND GRID", "REDLINE",
                "BLACKSITE", "CHEVRON", "SPLIT WING", "HEX COMMAND", "RAZOR",
                "LEGACY GLASS", "KEEN SIGNAL", "WEAPON CORE"
            }, delegate { return Math.Max(0, Math.Min(16, _zeoSettings.FrameStyle)); }, delegate(int v) { _zeoSettings.FrameStyle = v; }));
            p.Controls.Add(DropRow("FONT STYLE", new[] { "MATCH HUD", "CONDENSED", "TECH", "STANDARD" }, delegate { return _zeoSettings.FontStyle; }, delegate(int v) { _zeoSettings.FontStyle = v; }));
            p.Controls.Add(Note("KEEN SIGNAL follows the clipped translucent language of the in-game signal panel. WEAPON CORE uses the charcoal plates, tiny corner cuts and pale lower data rail seen in the native WeaponCore HUD. Theme colors and panel positions stay independent."));
            p.Controls.Add(Note("MATCH HUD selects a typography family per frame style. CONDENSED uses Bahnschrift; TECH favors Consolas; STANDARD uses Segoe UI."));

            // ZEOCORE_V13B_PER_PANEL_BACKINGS
            p.Controls.Add(Section("PANEL BACKINGS"));
            p.Controls.Add(BoolRow("Ship info backing", delegate { return _zeoSettings.BackingShipInfo; }, delegate(bool v) { _zeoSettings.BackingShipInfo = v; }));
            p.Controls.Add(BoolRow("TOS / scope backing", delegate { return _zeoSettings.BackingTos; }, delegate(bool v) { _zeoSettings.BackingTos = v; }));
            p.Controls.Add(BoolRow("Fleet / network backing", delegate { return _zeoSettings.BackingFleetLink; }, delegate(bool v) { _zeoSettings.BackingFleetLink = v; }));
            p.Controls.Add(BoolRow("Ammo backing", delegate { return _zeoSettings.BackingAmmo; }, delegate(bool v) { _zeoSettings.BackingAmmo = v; }));
            p.Controls.Add(BoolRow("Fleet roster backing", delegate { return _zeoSettings.BackingRoster; }, delegate(bool v) { _zeoSettings.BackingRoster = v; }));
            p.Controls.Add(BoolRow("Distress banner backing", delegate { return _zeoSettings.BackingDistress; }, delegate(bool v) { _zeoSettings.BackingDistress = v; }));
            p.Controls.Add(Note("Backing toggles affect only the translucent fill. Frame rails/text stay visible."));

            p.Controls.Add(Section("PANEL SIZE"));
            p.Controls.Add(NumberRow("Global text scale", 0.65M, 2.50M, 0.05M, 2, delegate { return _zeoSettings.TextScale; }, delegate(double v) { _zeoSettings.TextScale = v; }));
            p.Controls.Add(NumberRow("Flight panel scale", 0.60M, 2.25M, 0.05M, 2, delegate { return _zeoSettings.FlightScale; }, delegate(double v) { _zeoSettings.FlightScale = v; }));
            p.Controls.Add(NumberRow("Scope backplate scale", 0.60M, 2.25M, 0.05M, 2, delegate { return _zeoSettings.ScopePanelScale; }, delegate(double v) { _zeoSettings.ScopePanelScale = v; }));
            p.Controls.Add(NumberRow("Fleet panel scale", 0.60M, 2.25M, 0.05M, 2, delegate { return _zeoSettings.LinkPanelScale; }, delegate(double v) { _zeoSettings.LinkPanelScale = v; }));
            p.Controls.Add(NumberRow("Ammo panel scale", 0.60M, 2.25M, 0.05M, 2, delegate { return _zeoSettings.AmmoPanelScale; }, delegate(double v) { _zeoSettings.AmmoPanelScale = v; }));
            p.Controls.Add(NumberRow("Roster panel scale", 0.60M, 2.25M, 0.05M, 2, delegate { return _zeoSettings.RosterPanelScale; }, delegate(double v) { _zeoSettings.RosterPanelScale = v; }));
            p.Controls.Add(IntRow("Panel backing opacity", 60, 245, 5, delegate { return _zeoSettings.PanelOpacity; }, delegate(int v) { _zeoSettings.PanelOpacity = v; }));
            p.Controls.Add(NumberRow("Panel inner padding", 0.60M, 2.00M, 0.05M, 2, delegate { return _zeoSettings.PanelPaddingScale; }, delegate(double v) { _zeoSettings.PanelPaddingScale = v; }));
            p.Controls.Add(NumberRow("Panel border width", 0.50M, 4.00M, 0.05M, 2, delegate { return _zeoSettings.BorderWidth; }, delegate(double v) { _zeoSettings.BorderWidth = v; }));

            p.Controls.Add(Section("PANEL POSITION"));
            p.Controls.Add(NumberRow("Flight X", -0.98M, 0.98M, 0.01M, 2, delegate { return _zeoSettings.FlightX; }, delegate(double v) { _zeoSettings.FlightX = v; }));
            p.Controls.Add(NumberRow("Flight Y", -0.98M, 0.98M, 0.01M, 2, delegate { return _zeoSettings.FlightY; }, delegate(double v) { _zeoSettings.FlightY = v; }));
            p.Controls.Add(NumberRow("Scope X", -0.98M, 0.98M, 0.01M, 2, delegate { return _zeoSettings.TrackPanelX; }, delegate(double v) { _zeoSettings.TrackPanelX = v; }));
            p.Controls.Add(NumberRow("Scope Y", -0.98M, 0.98M, 0.01M, 2, delegate { return _zeoSettings.TrackPanelY; }, delegate(double v) { _zeoSettings.TrackPanelY = v; }));
            p.Controls.Add(NumberRow("Fleet X", -0.98M, 0.98M, 0.01M, 2, delegate { return _zeoSettings.LinkPanelX; }, delegate(double v) { _zeoSettings.LinkPanelX = v; }));
            p.Controls.Add(NumberRow("Fleet Y", -0.98M, 0.98M, 0.01M, 2, delegate { return _zeoSettings.LinkPanelY; }, delegate(double v) { _zeoSettings.LinkPanelY = v; }));
            p.Controls.Add(NumberRow("Ammo X", -0.98M, 0.98M, 0.01M, 2, delegate { return _zeoSettings.AmmoX; }, delegate(double v) { _zeoSettings.AmmoX = v; }));
            p.Controls.Add(NumberRow("Ammo Y", -0.98M, 0.98M, 0.01M, 2, delegate { return _zeoSettings.AmmoY; }, delegate(double v) { _zeoSettings.AmmoY = v; }));
            p.Controls.Add(NumberRow("Roster X", -0.98M, 0.98M, 0.01M, 2, delegate { return _zeoSettings.RosterX; }, delegate(double v) { _zeoSettings.RosterX = v; }));
            p.Controls.Add(NumberRow("Roster Y", -0.98M, 0.98M, 0.01M, 2, delegate { return _zeoSettings.RosterY; }, delegate(double v) { _zeoSettings.RosterY = v; }));
            Button reset = ActionButton("RESET POLISHED LAYOUT", delegate
            {
                _zeoSettings.FlightX = -0.92; _zeoSettings.FlightY = 0.82;
                _zeoSettings.TrackPanelX = -0.72; _zeoSettings.TrackPanelY = -0.70;
                _zeoSettings.LinkPanelX = 0.62; _zeoSettings.LinkPanelY = 0.82;
                _zeoSettings.AmmoX = 0.62; _zeoSettings.AmmoY = -0.70;
                _zeoSettings.RosterX = 0.60; _zeoSettings.RosterY = 0.30;
                _zeoSettings.DistressPositionCustom=false; _zeoSettings.DistressX=0; _zeoSettings.DistressY=.96;
                SaveNow(); RefreshFromSettings();
            });
            p.Controls.Add(reset);

            // Requested relocation only: the old bottom-docked quick strip is now
            // part of the normal scrollable LAYOUT page.
            p.Controls.Add(Section("INTEGRATED HUD // LIVE VISUALS"));
            Panel maxRow = RowPanel(42);
            maxRow.Controls.Add(RowLabel("MAX MARKER SIZE"));
            _zeoMaxMarkerSize = MakeNumber(0.50M, 3.00M, 0.05M, 2);
            _zeoMaxMarkerSize.ValueChanged += delegate
            {
                if (_sync) return;
                _zeoSettings.MaxMarkerScale = (double)_zeoMaxMarkerSize.Value;
                SaveNow();
            };
            _refreshers.Add(delegate
            {
                decimal v = (decimal)Math.Max(0.50, Math.Min(3.00, _zeoSettings.MaxMarkerScale));
                if (_zeoMaxMarkerSize.Value != v) _zeoMaxMarkerSize.Value = v;
            });
            maxRow.Controls.Add(_zeoMaxMarkerSize);
            p.Controls.Add(maxRow);

            _zeoFollowGameHud = BoolRow("FOLLOW GAME HUD", delegate { return _zeoSettings.FollowGameHud; }, delegate(bool v) { _zeoSettings.FollowGameHud = v; });
            p.Controls.Add(_zeoFollowGameHud);
            p.Controls.Add(Note("CLOSE = SMALL   //   FAR = LARGE   //   HARD CAP = MAX MARKER SIZE"));
        }

        private Button ActionButton(string text, Action action)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = 592;
            b.Height = 36;
            b.Margin = new Padding(0, 4, 0, 4);
            b.FlatStyle = FlatStyle.Flat;
            b.Tag = "ACTION";
            b.TabStop = false;
            b.Click += delegate { if (!_sync) action(); };
            return b;
        }

        private void BuildThemePage()
        {
            FlowLayoutPanel p = NewPage();
            p.Controls.Add(Section("THEME PRESET"));
            p.Controls.Add(DropRow("THEME", new[] { "GRAPHITE", "MONOCHROME", "AMBER", "HIGH CONTRAST", "CUSTOM", "WAR ROOM", "KEEN NATIVE" },
                delegate { return _zeoSettings.ThemePreset; },
                delegate(int v)
                {
                    if (v == 4) _zeoSettings.ThemePreset = 4;
                    else _zeoSettings.ApplyTheme(v);
                    ApplyAttachedStyle(this);
                    RefreshFromSettings();
                }));

            p.Controls.Add(Section("HUD COLORS"));
            p.Controls.Add(ColorRow("HUD text", delegate { return _zeoSettings.HudTextColor; }, delegate(string v) { _zeoSettings.HudTextColor = v; }));
            p.Controls.Add(ColorRow("Secondary text", delegate { return _zeoSettings.HudSecondaryColor; }, delegate(string v) { _zeoSettings.HudSecondaryColor = v; }));
            p.Controls.Add(ColorRow("Panel backing", delegate { return _zeoSettings.HudPanelColor; }, delegate(string v) { _zeoSettings.HudPanelColor = v; }));
            p.Controls.Add(ColorRow("Panel border", delegate { return _zeoSettings.HudBorderColor; }, delegate(string v) { _zeoSettings.HudBorderColor = v; }));
            p.Controls.Add(ColorRow("Crosshair", delegate { return _zeoSettings.CrosshairColor; }, delegate(string v) { _zeoSettings.CrosshairColor = v; }));
            p.Controls.Add(ColorRow("Shared data tracks", () => _zeoSettings.SharedTrackColor, v => _zeoSettings.SharedTrackColor=v));
            p.Controls.Add(ColorRow("Spectrum tracks", delegate { return _zeoSettings.SpectrumColor; }, delegate(string v) { _zeoSettings.SpectrumColor = v; }));
            p.Controls.Add(ColorRow("Friendly tracks", delegate { return _zeoSettings.FriendlyColor; }, delegate(string v) { _zeoSettings.FriendlyColor = v; }));
            p.Controls.Add(ColorRow("Hostile tracks", delegate { return _zeoSettings.HostileColor; }, delegate(string v) { _zeoSettings.HostileColor = v; }));
            p.Controls.Add(ColorRow("Neutral tracks", delegate { return _zeoSettings.NeutralColor; }, delegate(string v) { _zeoSettings.NeutralColor = v; }));
            p.Controls.Add(ColorRow("Stale tracks", delegate { return _zeoSettings.StaleColor; }, delegate(string v) { _zeoSettings.StaleColor = v; }));
            p.Controls.Add(ColorRow("Focused track", delegate { return _zeoSettings.FocusColor; }, delegate(string v) { _zeoSettings.FocusColor = v; }));
            p.Controls.Add(ColorRow("Distress / SOS", delegate { return _zeoSettings.DistressColor; }, delegate(string v) { _zeoSettings.DistressColor = v; }));

            p.Controls.Add(Section("MENU COLORS"));
            p.Controls.Add(ColorRow("Menu background", delegate { return _zeoSettings.MenuBackgroundColor; }, delegate(string v) { _zeoSettings.MenuBackgroundColor = v; }));
            p.Controls.Add(ColorRow("Menu panel", delegate { return _zeoSettings.MenuPanelColor; }, delegate(string v) { _zeoSettings.MenuPanelColor = v; }));
            p.Controls.Add(ColorRow("Menu text", delegate { return _zeoSettings.MenuTextColor; }, delegate(string v) { _zeoSettings.MenuTextColor = v; }));
            p.Controls.Add(ColorRow("Menu accent", delegate { return _zeoSettings.MenuAccentColor; }, delegate(string v) { _zeoSettings.MenuAccentColor = v; }));
            p.Controls.Add(Note("Click the color swatch or PICK COLOR. Any manual color choice switches the theme to CUSTOM automatically."));
        }

        private void BuildMarkersPage()
        {
            FlowLayoutPanel p = NewPage();
            p.Controls.Add(Section("CONTACT ICONS"));
            // Locked buddy baseline behavior: MARKER STYLE, ICON PACK, MARKER SMOOTHING,
            // SCOPE LAYOUT and SCOPE SORT remain runtime-locked and are not exposed as
            // misleading controls. Marker anchor and all scale controls remain available.
            p.Controls.Add(DropRow("MARKER ANCHOR", new[] { "SENSOR POSITION", "GRID CENTER", "SPECTRUM / AUTO" }, delegate { return _zeoSettings.MarkerAnchor; }, delegate(int v) { _zeoSettings.MarkerAnchor = v; }));

            p.Controls.Add(Section("SPECTRUM RETICLE"));
            p.Controls.Add(NumberRow("Spectrum reticle scale", 0.50M, 3.00M, 0.05M, 2, delegate { return _zeoSettings.SpectrumMarkerScale; }, delegate(double v) { _zeoSettings.SpectrumMarkerScale = v; }));
            p.Controls.Add(NumberRow("Spectrum track ID scale", 0.50M, 3.00M, 0.05M, 2, delegate { return _zeoSettings.SpectrumIdScale; }, delegate(double v) { _zeoSettings.SpectrumIdScale = v; }));

            p.Controls.Add(Section("FRIENDLY MARKERS"));
            p.Controls.Add(NumberRow("Friendly icon scale", 0.50M, 3.00M, 0.05M, 2, delegate { return _zeoSettings.FriendlyMarkerScale; }, delegate(double v) { _zeoSettings.FriendlyMarkerScale = v; }));
            p.Controls.Add(NumberRow("Friendly track ID scale", 0.50M, 3.00M, 0.05M, 2, delegate { return _zeoSettings.FriendlyIdScale; }, delegate(double v) { _zeoSettings.FriendlyIdScale = v; }));

            p.Controls.Add(Section("OTHER MARKERS"));
            p.Controls.Add(NumberRow("Hostile / contact scale", 0.50M, 3.00M, 0.05M, 2, delegate { return _zeoSettings.HostileMarkerScale; }, delegate(double v) { _zeoSettings.HostileMarkerScale = v; }));
            p.Controls.Add(NumberRow("Focused target multiplier", 0.50M, 3.00M, 0.05M, 2, delegate { return _zeoSettings.FocusMarkerScale; }, delegate(double v) { _zeoSettings.FocusMarkerScale = v; }));
            p.Controls.Add(NumberRow("Off-screen multiplier", 0.50M, 3.00M, 0.05M, 2, delegate { return _zeoSettings.OffscreenMarkerScale; }, delegate(double v) { _zeoSettings.OffscreenMarkerScale = v; }));
            p.Controls.Add(NumberRow("Center crosshair scale", 0.50M, 3.00M, 0.05M, 2, delegate { return _zeoSettings.CrosshairScale; }, delegate(double v) { _zeoSettings.CrosshairScale = v; }));

            p.Controls.Add(Section("ALIGNMENT TEST"));
            p.Controls.Add(BoolRow("Show tiny center dot under every world marker", delegate { return _zeoSettings.ShowMarkerAnchorDot; }, delegate(bool v) { _zeoSettings.ShowMarkerAnchorDot = v; }));
            p.Controls.Add(Note("Use the center dot only while testing. If the dot stays on the ship but the icon does not, the issue is marker geometry. If the dot itself misses the ship, the issue is source/projection alignment."));
        }

        private void BuildPrivacyPage()
        {
            FlowLayoutPanel p = NewPage();

            // ZEOCORE_V12C_PRIVACY_VISIBLE_CONTROLS
            // Keep the primary privacy/streaming switch visible here as well as
            // on CAPTURE so users do not have to guess which page owns it.
            p.Controls.Add(Section("STREAMER PRIVACY"));
            p.Controls.Add(ToggleButtonRow("STREAMER MODE",
                delegate { return _zeoSettings.CaptureSafeHud && _zeoSettings.CaptureSafeMenu; },
                delegate(bool v)
                {
                    _zeoSettings.CaptureSafeHud = v;
                    _zeoSettings.CaptureSafeMenu = v;
                }));
            p.Controls.Add(Note("ON requests capture exclusion for both the Zeo HUD and settings menu. Advanced per-window capture controls remain on the CAPTURE page."));

            p.Controls.Add(Section("DATA EXPORT / PRIVACY"));
            p.Controls.Add(BoolRow("Transmit my telemetry to Zeo server", delegate { return _zeoSettings.TransmitTelemetry; }, delegate(bool v) { _zeoSettings.TransmitTelemetry = v; }));
            p.Controls.Add(BoolRow("Write local last-payload.json", delegate { return _zeoSettings.WriteLocalPayload; }, delegate(bool v) { _zeoSettings.WriteLocalPayload = v; }));
            p.Controls.Add(Note("TX and RX are independent. TX OFF / RX ON keeps your client from sending tactical telemetry while still receiving the faction picture."));
        }

        private void BuildCapturePage()
        {
            FlowLayoutPanel p = NewPage();
            p.Controls.Add(Section("STREAMER MODE"));
            p.Controls.Add(ToggleButtonRow("STREAMER MODE",
                delegate { return _zeoSettings.CaptureSafeHud && _zeoSettings.CaptureSafeMenu; },
                delegate(bool v)
                {
                    _zeoSettings.CaptureSafeHud = v;
                    _zeoSettings.CaptureSafeMenu = v;
                }));
            p.Controls.Add(Note("ON keeps the Zeo HUD and settings visible to you while requesting capture exclusion from supported recording/streaming software. OFF returns both windows to normal capture behavior."));

            p.Controls.Add(Section("CAPTURE-SAFE RENDERER"));
            p.Controls.Add(BoolRow("Exclude Zeo HUD from supported screen capture", delegate { return _zeoSettings.CaptureSafeHud; }, delegate(bool v) { _zeoSettings.CaptureSafeHud = v; RefreshFromSettings(); }));
            p.Controls.Add(BoolRow("Exclude Zeo settings menu from supported capture", delegate { return _zeoSettings.CaptureSafeMenu; }, delegate(bool v) { _zeoSettings.CaptureSafeMenu = v; RefreshFromSettings(); }));
            p.Controls.Add(BoolRow("Auto-launch ZeoOverlay with the plugin", delegate { return _zeoSettings.OverlayAutoLaunch; }, delegate(bool v) { _zeoSettings.OverlayAutoLaunch = v; }));
            p.Controls.Add(Note("The two controls above are the advanced per-window settings. Streamer Mode simply turns both capture-exclusion switches ON or OFF together. Always make a short OBS test recording before streaming."));
        }

        private void BuildAmmoPage()
        {
            FlowLayoutPanel p = NewPage();
            p.Controls.Add(Section("AMMO TRACKER"));
            p.Controls.Add(BoolRow("Show AMMUNITION panel", delegate { return _zeoSettings.ShowAmmoPanel; }, delegate(bool v) { _zeoSettings.ShowAmmoPanel = v; }));
            p.Controls.Add(BoolRow("Only ammo relevant / stocked on this ship", delegate { return _zeoSettings.AmmoOnlyRelevant; }, delegate(bool v) { _zeoSettings.AmmoOnlyRelevant = v; }));
            p.Controls.Add(Note("Zeo checks installed WeaponCore magazine maps, then scans only the mechanically connected ship construct. Connector-docked bases/ships are not counted."));

            p.Controls.Add(Section("WANT / HAVE TARGETS"));
            p.Controls.Add(IntRow("40mm PDC WANT", 0, 1000000, 100, delegate { return _zeoSettings.WantPdc40; }, delegate(int v) { _zeoSettings.WantPdc40 = v; }));
            p.Controls.Add(IntRow("40mm Improvised PDC WANT", 0, 1000000, 100, delegate { return _zeoSettings.WantPdc40Improvised; }, delegate(int v) { _zeoSettings.WantPdc40Improvised = v; }));
            p.Controls.Add(IntRow("50mm PDC WANT", 0, 1000000, 100, delegate { return _zeoSettings.WantPdc50; }, delegate(int v) { _zeoSettings.WantPdc50 = v; }));
            p.Controls.Add(IntRow("80mm Sabot WANT", 0, 1000000, 100, delegate { return _zeoSettings.WantSabot80; }, delegate(int v) { _zeoSettings.WantSabot80 = v; }));
            p.Controls.Add(IntRow("80mm Improvised Sabot WANT", 0, 1000000, 100, delegate { return _zeoSettings.WantSabot80Improvised; }, delegate(int v) { _zeoSettings.WantSabot80Improvised = v; }));
            p.Controls.Add(IntRow("100mm Sabot WANT", 0, 1000000, 100, delegate { return _zeoSettings.WantSabot100; }, delegate(int v) { _zeoSettings.WantSabot100 = v; }));
            p.Controls.Add(IntRow("160mm Torpedo WANT", 0, 100000, 5, delegate { return _zeoSettings.WantTorp160; }, delegate(int v) { _zeoSettings.WantTorp160 = v; }));
            p.Controls.Add(IntRow("190mm Torpedo WANT", 0, 100000, 5, delegate { return _zeoSettings.WantTorp190; }, delegate(int v) { _zeoSettings.WantTorp190 = v; }));
            p.Controls.Add(IntRow("220mm Torpedo WANT", 0, 100000, 5, delegate { return _zeoSettings.WantTorp220; }, delegate(int v) { _zeoSettings.WantTorp220 = v; }));

            // Requested addition only. Ammo/fuel pulling is intentionally NOT part of V1.2.
            p.Controls.Add(Section("AMMO TYPE VISIBILITY"));
            p.Controls.Add(BoolRow("40mm PDC", delegate { return _zeoSettings.ShowAmmoPdc40; }, delegate(bool v) { _zeoSettings.ShowAmmoPdc40 = v; }));
            p.Controls.Add(BoolRow("40mm Improvised PDC", delegate { return _zeoSettings.ShowAmmoPdc40Improvised; }, delegate(bool v) { _zeoSettings.ShowAmmoPdc40Improvised = v; }));
            p.Controls.Add(BoolRow("50mm PDC", delegate { return _zeoSettings.ShowAmmoPdc50; }, delegate(bool v) { _zeoSettings.ShowAmmoPdc50 = v; }));
            p.Controls.Add(BoolRow("80mm Sabot", delegate { return _zeoSettings.ShowAmmoSabot80; }, delegate(bool v) { _zeoSettings.ShowAmmoSabot80 = v; }));
            p.Controls.Add(BoolRow("80mm Sabot Improvised", delegate { return _zeoSettings.ShowAmmoSabot80Improvised; }, delegate(bool v) { _zeoSettings.ShowAmmoSabot80Improvised = v; }));
            p.Controls.Add(BoolRow("100mm Sabot", delegate { return _zeoSettings.ShowAmmoSabot100; }, delegate(bool v) { _zeoSettings.ShowAmmoSabot100 = v; }));
            p.Controls.Add(BoolRow("160mm Torpedo", delegate { return _zeoSettings.ShowAmmoTorp160; }, delegate(bool v) { _zeoSettings.ShowAmmoTorp160 = v; }));
            p.Controls.Add(BoolRow("190mm Torpedo", delegate { return _zeoSettings.ShowAmmoTorp190; }, delegate(bool v) { _zeoSettings.ShowAmmoTorp190 = v; }));
            p.Controls.Add(BoolRow("220mm Torpedo", delegate { return _zeoSettings.ShowAmmoTorp220; }, delegate(bool v) { _zeoSettings.ShowAmmoTorp220 = v; }));
        }

        private void BuildRosterPage()
        {
            FlowLayoutPanel p = NewPage();
            p.Controls.Add(Section("FLEET ROSTER"));
            p.Controls.Add(BoolRow("SHOW FLEET ROSTER", delegate { return _zeoSettings.ShowRosterPanel; }, delegate(bool v) { _zeoSettings.ShowRosterPanel = v; }));
            p.Controls.Add(IntRow("Roster rows", 1, 24, 1, delegate { return _zeoSettings.RosterRows; }, delegate(int v) { _zeoSettings.RosterRows = v; }));
            p.Controls.Add(BoolRow("Show allies in other sectors", delegate { return _zeoSettings.ShowCrossSectorRoster; }, delegate(bool v) { _zeoSettings.ShowCrossSectorRoster = v; }));
            p.Controls.Add(BoolRow("Unlimited-distance same-sector markers", delegate { return _zeoSettings.RemoteFriendlyNoRangeLimit; }, delegate(bool v) { _zeoSettings.RemoteFriendlyNoRangeLimit = v; }));
            p.Controls.Add(Note("Fleet roster is PILOTED ONLY. Same-sector pilots can become world markers; other-sector pilots remain roster-only. Parked/unmanned grids are not published by ZeoCore as fleet members."));
        }

        private void BuildDistressPage()
        {
            FlowLayoutPanel p = NewPage();
            p.Controls.Add(Section("DISTRESS NETWORK"));
            p.Controls.Add(BoolRow("Enable distress hotkey", delegate { return _zeoSettings.DistressEnabled; }, delegate(bool v) { _zeoSettings.DistressEnabled = v; }));
            p.Controls.Add(DropRow("DISTRESS KEY", new[] { "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12" }, delegate { return _zeoSettings.DistressKey; }, delegate(int v) { _zeoSettings.DistressKey = v; }));
            p.Controls.Add(DropRow("DISTRESS TYPE", new[] { "GENERAL SOS", "UNDER ATTACK", "DISABLED", "RECOVERY", "NEED FUEL" }, delegate { return _zeoSettings.DistressType; }, delegate(int v) { _zeoSettings.DistressType = v; }));
            p.Controls.Add(DropRow("VISIBILITY", new[] { "FACTION ONLY", "ALLIANCE" }, delegate { return _zeoSettings.DistressVisibility; }, delegate(int v) { _zeoSettings.DistressVisibility = v; }));
            p.Controls.Add(NumberRow("Hold to send seconds", 0.50M, 3.00M, 0.10M, 2, delegate { return _zeoSettings.DistressHoldSeconds; }, delegate(double v) { _zeoSettings.DistressHoldSeconds = v; }));
            p.Controls.Add(IntRow("Distress TTL minutes", 1, 60, 1, delegate { return _zeoSettings.DistressTtlMinutes; }, delegate(int v) { _zeoSettings.DistressTtlMinutes = v; }));

            p.Controls.Add(Section("HUD BEHAVIOR"));
            p.Controls.Add(BoolRow("Show distress alert banner", delegate { return _zeoSettings.ShowDistressBanner; }, delegate(bool v) { _zeoSettings.ShowDistressBanner = v; }));
            p.Controls.Add(BoolRow("Show distress world pings", delegate { return _zeoSettings.ShowDistressWorldMarkers; }, delegate(bool v) { _zeoSettings.ShowDistressWorldMarkers = v; }));
            p.Controls.Add(BoolRow("Show cross-sector distress alerts", delegate { return _zeoSettings.ShowCrossSectorDistress; }, delegate(bool v) { _zeoSettings.ShowCrossSectorDistress = v; }));
            p.Controls.Add(BoolRow("Project cross-sector distress GPS markers", delegate { return _zeoSettings.ProjectCrossSectorDistress; }, delegate(bool v) { _zeoSettings.ProjectCrossSectorDistress = v; }));
            p.Controls.Add(ColorRow("Distress color", delegate { return _zeoSettings.DistressColor; }, delegate(string v) { _zeoSettings.DistressColor = v; }));

            p.Controls.Add(Section("SAFETY"));
            p.Controls.Add(Note("Hold the selected key to ACTIVATE. Hold it again while your SOS is active to CLEAR it. Faction/alliance authorization remains enforced by the Battle Manager."));
        }

        private void ShowPage(int index)
        {
            if (_pages.Count == 0) return;
            CloseDropDown();
            index = Math.Max(0, Math.Min(_pages.Count - 1, index));
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].Visible = i == index;
                if (i == index)
                {
                    _pages[i].BringToFront();
                    UpdatePageScrollRange(_pages[i]);
                    _pages[i].PerformLayout();
                }
            }
        }

        private void SaveNow()
        {
            _zeoSettings.Save();
            try { if (_owner != null && !_owner.IsDisposed) _owner.SettingsChanged(); } catch { }
        }

        internal void RefreshFromSettings()
        {
            _sync = true;
            try
            {
                int page = Math.Max(0, Math.Min(_pages.Count - 1, _zeoSettings.MenuPage));
                if (_pageDrop != null) _pageDrop.SetSelectedIndex(page, false);
                ShowPage(page);
                for (int i = 0; i < _refreshers.Count; i++)
                {
                    try { _refreshers[i](); } catch { }
                }
                if (_captureStatus != null)
                {
                    if (_zeoSettings.CaptureSafeHud && _zeoSettings.CaptureSafeMenu)
                        _captureStatus.Text = "STREAM SAFE: ON / VERIFY";
                    else if (_zeoSettings.CaptureSafeHud || _zeoSettings.CaptureSafeMenu)
                        _captureStatus.Text = "STREAM SAFE: PARTIAL";
                    else
                        _captureStatus.Text = "STREAM SAFE: OFF";
                }
                ApplyAttachedStyle(this);
            }
            finally { _sync = false; }
        }

        internal void CloseForShutdown()
        {
            _shutdown = true;
            Close();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // ZEOCORE_V067_ATTACHED_CONTROLS
            ApplyAttachedStyle(this);
            RefreshFromSettings();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) CloseDropDown();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_shutdown)
            {
                e.Cancel = true;
                CloseDropDown();
                _owner.CloseMenuToGame();
                return;
            }
            base.OnFormClosing(e);
        }

        private void HeaderMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            CloseDropDown();
            _dragging = true;
            _dragOffset = PointToClient(Cursor.Position);
        }

        private void HeaderMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point p = Cursor.Position;
            Location = new Point(p.X - _dragOffset.X, p.Y - _dragOffset.Y);
        }

        private void HeaderMouseUp(object sender, MouseEventArgs e)
        {
            _dragging = false;
        }

        private void PaintHeader(object sender, PaintEventArgs e)
        {
            Color accent = _zeoSettings.ColorOf(_zeoSettings.MenuAccentColor, Color.FromArgb(242, 201, 76));
            using (Pen p = new Pen(accent, 1f))
            {
                e.Graphics.DrawLine(p, 12, _header.Height - 1, _header.Width - 12, _header.Height - 1);
                e.Graphics.DrawLine(p, 12, 0, 112, 0);
            }
        }

        private static Color BlendMenuColor(Color a, Color b, double amount)
        {
            amount = Math.Max(0.0, Math.Min(1.0, amount));
            int r = (int)Math.Round(a.R + ((b.R - a.R) * amount));
            int g = (int)Math.Round(a.G + ((b.G - a.G) * amount));
            int bl = (int)Math.Round(a.B + ((b.B - a.B) * amount));
            return Color.FromArgb(255, r, g, bl);
        }

        private void ApplyAttachedStyle(Control root)
        {
            // ZEOCORE_V11D_VISUAL_RESTORE
            // Labels, Panels, GroupBoxes and page containers intentionally keep
            // the locked Zeo hierarchy while the saved menu palette drives color.
            Color bg = _zeoSettings.ColorOf(_zeoSettings.MenuBackgroundColor, Color.FromArgb(16, 20, 25));
            Color panel = _zeoSettings.ColorOf(_zeoSettings.MenuPanelColor, Color.FromArgb(26, 32, 38));
            Color text = _zeoSettings.ColorOf(_zeoSettings.MenuTextColor, Color.FromArgb(232, 236, 241));
            Color accent = _zeoSettings.ColorOf(_zeoSettings.MenuAccentColor, Color.FromArgb(242, 201, 76));
            Color secondary = BlendMenuColor(text, bg, 0.36);
            Color row = BlendMenuColor(bg, panel, 0.68);
            Color header = BlendMenuColor(bg, panel, 0.35);

            BackColor = bg;
            ForeColor = text;

            ApplyPaletteRecursive(root, bg, panel, row, header, text, secondary, accent);
            if (_header != null) _header.Invalidate();
        }

        private void ApplyPaletteRecursive(Control root, Color bg, Color panel, Color row, Color header, Color text, Color secondary, Color accent)
        {
            foreach (Control c in root.Controls)
            {
                string tag = Convert.ToString(c.Tag) ?? "";
                if (tag == "HEADER" || tag == "NAV") { c.BackColor = header; c.ForeColor = text; }
                else if (tag == "ROOT" || tag == "PAGE") { c.BackColor = bg; c.ForeColor = text; }
                else if (tag == "ROW") { c.BackColor = row; c.ForeColor = text; }
                else if (tag == "NOTE") { c.BackColor = bg; c.ForeColor = secondary; }
                else if (tag == "PRIMARY") { c.BackColor = Color.Transparent; c.ForeColor = text; }
                else if (tag == "SECONDARY") { c.BackColor = Color.Transparent; c.ForeColor = secondary; }
                else if (tag == "EDITOR") { c.BackColor = panel; c.ForeColor = text; }
                else if (tag == "ACTION")
                {
                    Button b = c as Button;
                    if (b != null)
                    {
                        b.BackColor = panel; b.ForeColor = text;
                        b.FlatAppearance.BorderColor = accent; b.FlatAppearance.BorderSize = 1;
                        b.FlatAppearance.MouseOverBackColor = BlendMenuColor(panel, accent, 0.16);
                        b.FlatAppearance.MouseDownBackColor = BlendMenuColor(panel, accent, 0.26);
                    }
                }
                else if (tag == "TOGGLE_ON" || tag == "TOGGLE_OFF")
                {
                    Button b = c as Button;
                    if (b != null)
                    {
                        bool on = tag == "TOGGLE_ON";
                        b.BackColor = on ? BlendMenuColor(panel, accent, 0.26) : panel;
                        b.ForeColor = text;
                        b.FlatAppearance.BorderColor = on ? accent : secondary;
                        b.FlatAppearance.BorderSize = 1;
                        b.FlatAppearance.MouseOverBackColor = BlendMenuColor(panel, accent, on ? 0.34 : 0.14);
                        b.FlatAppearance.MouseDownBackColor = BlendMenuColor(panel, accent, on ? 0.42 : 0.22);
                    }
                }
                else if (tag == "CLOSE")
                {
                    Button b = c as Button;
                    if (b != null) { b.BackColor = header; b.ForeColor = text; }
                }
                else if (tag == "POPUP") { c.BackColor = panel; c.ForeColor = text; }
                else if (tag == "POPITEM" || tag == "POPSELECT")
                {
                    Button b = c as Button;
                    if (b != null)
                    {
                        b.BackColor = tag == "POPSELECT" ? BlendMenuColor(panel, accent, 0.18) : panel;
                        b.ForeColor = text;
                        b.FlatAppearance.MouseOverBackColor = BlendMenuColor(panel, accent, 0.18);
                        b.FlatAppearance.MouseDownBackColor = BlendMenuColor(panel, accent, 0.28);
                    }
                }

                CheckBox cb = c as CheckBox;
                if (cb != null) { cb.FlatStyle = FlatStyle.Flat; }
                NumericUpDown num = c as NumericUpDown;
                if (num != null) { num.BackColor = panel; num.ForeColor = text; }
                TextBox box = c as TextBox;
                if (box != null) { box.BackColor = panel; box.ForeColor = text; }
                ZeoDropDown dd = c as ZeoDropDown;
                if (dd != null) dd.SetPalette(panel, text, accent);
                ZeoSectionHeader sh = c as ZeoSectionHeader;
                if (sh != null) sh.SetPalette(panel, text, accent);

                ApplyPaletteRecursive(c, bg, panel, row, header, text, secondary, accent);
            }
        }

        // Kept as a harmless source invariant used by the installer compile gate.
        private static string ZeoNormalizeUiText(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            System.Text.StringBuilder b = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = char.ToUpperInvariant(value[i]);
                if (char.IsLetterOrDigit(c)) b.Append(c);
            }
            return b.ToString();
        }
    }

    internal sealed class ZeoDropDown : Control
    {
        internal string[] Items = new string[0];
        internal int SelectedIndex { get; private set; }
        internal event Action<ZeoDropDown> RequestOpen;
        internal event Action<int> SelectionChanged;
        private Color _panel = Color.FromArgb(26, 32, 38);
        private Color _text = Color.FromArgb(232, 236, 241);
        private Color _accent = Color.FromArgb(242, 201, 76);
        private bool _wheelArmed;

        internal ZeoDropDown()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            TabStop = false;
            Cursor = Cursors.Hand;
        }

        internal void SetPalette(Color panel, Color text, Color accent)
        {
            _panel = panel; _text = text; _accent = accent; Invalidate();
        }

        internal void SetWheelArmed(bool value) { _wheelArmed = value; }

        internal void SetSelectedIndex(int value, bool raise)
        {
            int max = Math.Max(0, Items.Length - 1);
            int next = Math.Max(0, Math.Min(max, value));
            if (SelectedIndex == next) { Invalidate(); return; }
            SelectedIndex = next;
            Invalidate();
            if (raise && SelectionChanged != null) SelectionChanged(next);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _wheelArmed = true;
            if (RequestOpen != null) RequestOpen(this);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // ZEOCORE_V067H4_COMBO_WHEEL_LOCK
            // Passing the mouse over a dropdown cannot alter its value. Wheel
            // selection is accepted only after that dropdown was deliberately clicked.
            if (!_wheelArmed || Items == null || Items.Length == 0) return;
            int delta = e.Delta > 0 ? -1 : 1;
            SetSelectedIndex(SelectedIndex + delta, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(_panel);
            Rectangle r = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (Pen border = new Pen(Blend(_panel, _accent, 0.42), 1f)) e.Graphics.DrawRectangle(border, r);
            using (Pen rail = new Pen(_accent, 2f)) e.Graphics.DrawLine(rail, 0, 0, 3, 0);
            string value = Items != null && SelectedIndex >= 0 && SelectedIndex < Items.Length ? Items[SelectedIndex] : "";
            TextRenderer.DrawText(e.Graphics, value, Font, new Rectangle(9, 4, Math.Max(10, Width - 34), Height - 6), _text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            Point c = new Point(Width - 15, Height / 2);
            using (Pen p = new Pen(_accent, 1.2f))
            {
                e.Graphics.DrawLine(p, c.X - 4, c.Y - 2, c.X, c.Y + 2);
                e.Graphics.DrawLine(p, c.X, c.Y + 2, c.X + 4, c.Y - 2);
            }
        }

        private static Color Blend(Color a, Color b, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(255,
                (int)Math.Round(a.R + (b.R - a.R) * amount),
                (int)Math.Round(a.G + (b.G - a.G) * amount),
                (int)Math.Round(a.B + (b.B - a.B) * amount));
        }
    }

    internal sealed class ZeoSectionHeader : Control
    {
        private Color _panel = Color.FromArgb(26, 32, 38);
        private Color _text = Color.FromArgb(232, 236, 241);
        private Color _accent = Color.FromArgb(242, 201, 76);

        internal ZeoSectionHeader()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        internal void SetPalette(Color panel, Color text, Color accent)
        {
            _panel = panel; _text = text; _accent = accent; Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(_panel);
            using (Pen p = new Pen(_accent, 1f))
            {
                e.Graphics.DrawLine(p, 0, 0, Width - 1, 0);
                e.Graphics.DrawLine(p, 0, Height - 1, Width - 1, Height - 1);
                e.Graphics.DrawLine(p, 0, 0, 0, Height - 1);
                e.Graphics.DrawLine(p, Width - 1, 0, Width - 1, Height - 1);
                e.Graphics.DrawLine(p, 0, 0, 9, 0);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(10, 1, Width - 20, Height - 2), _text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
