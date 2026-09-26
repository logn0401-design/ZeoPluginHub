using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace ZeosOreOverlay
{
    internal sealed class SettingsForm : Form
    {
        private readonly OreOverlaySettings _s;
        private readonly HudOverlayForm _host;
        private readonly FlowLayoutPanel _body;
        private bool _building;
        private bool _shutdown;

        // IMPORTANT: this intentionally mirrors the ZeoCore aligned menu shell.
        private static readonly string[] Pages = { "HOME", "PRESETS", "ORES", "FILTERS", "RANKING", "HUD", "PINGS", "THEME", "LCD", "STORAGE", "ADVANCED" };
        private static readonly string[] MenuKeys = { "PageUp", "PageDown", "Insert", "Delete", "End", "F7", "F8", "F9", "F10" };
        private static readonly string[] MiningProfiles = { "Balanced", "Rare Metals", "Reactor Run", "Ice Run", "Advanced Materials", "Mass Build", "Everything" };
        private static readonly string[] HudLayouts = { "Prospector", "Tactical", "Compact", "Minimal", "Wide" };
        private static readonly string[] FrameStyles = { "Hex Command", "Chevron", "Split Wing", "Razor", "War Room", "Minimal", "SE Native" };
        private static readonly string[] RankingModes = { "quality", "quality_distance", "nearest" };
        private static readonly string[] GradeValues = { "X", "D", "C", "B", "A", "S" };
        private static readonly string[] WorthTripGrades = { "D", "C", "B", "A", "S" };
        private static readonly string[] PingSizeModes = { "Fixed", "Distance", "Grade", "Distance + Grade" };
        private static readonly string[] PingColorModes = { "Grade", "Top Ore", "Theme", "Custom" };
        private static readonly string[] Themes = { "GRAPHITE", "MONOCHROME", "AMBER", "HIGH CONTRAST", "CUSTOM", "WAR ROOM", "SE NATIVE" };
        private static readonly string[] LcdRoles = { "Ranking", "Target", "Status", "Ore Summary" };

        internal SettingsForm(OreOverlaySettings settings, HudOverlayForm host)
        {
            _s = settings;
            _host = host;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(660, 900);
            MinimumSize = new Size(620, 720);
            MaximumSize = new Size(760, 1120);
            Padding = new Padding(16);

            _body = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(14, 12, 14, 18),
                Margin = Padding.Empty
            };
            Controls.Add(_body);

            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == MenuKeyToFormsKey(_s.Get("MenuKey", "PageUp")))
                {
                    Hide();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };

            RefreshFromSettings();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _host.ApplyCaptureAffinity();
        }

        internal void CloseForShutdown()
        {
            _shutdown = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_shutdown)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        // Compatibility with HudOverlayForm's existing refresh calls.
        internal void RefreshPage() { RefreshFromSettings(); }

        internal void RefreshFromSettings()
        {
            if (IsDisposed) return;
            _building = true;
            try
            {
                _body.SuspendLayout();
                var oldControls=_body.Controls.Cast<Control>().ToArray();_body.Controls.Clear();foreach(var control in oldControls)control.Dispose();
                ApplyThemeToForm();

                AddTitle("ZEO // ORE HELPER", "v0.7.2 // LEGACY SETTINGS");

                string capture;
                bool captureGood;
                if (!_s.B("StreamerMode"))
                {
                    capture = "STREAMER CAPTURE EXCLUSION: STANDBY";
                    captureGood = true;
                }
                else
                {
                    captureGood = _host.CaptureHudApplied && _host.CaptureMenuApplied;
                    capture = captureGood ? "STREAMER CAPTURE EXCLUSION: ACTIVE" : "STREAMER CAPTURE EXCLUSION: FAIL CLOSED / VERIFY";
                }
                AddStatus(capture, captureGood);

                AddSection("PAGE");
                AddCombo(Pages, Clamp(_s.I("MenuPage", 0), 0, Pages.Length - 1), delegate(int i)
                {
                    _s.Set("MenuPage", i);
                    Save(false);
                    RefreshFromSettings();
                });

                switch (Clamp(_s.I("MenuPage", 0), 0, Pages.Length - 1))
                {
                    case 1: DrawPresets(); break;
                    case 2: DrawOres(); break;
                    case 3: DrawFilters(); break;
                    case 4: DrawRanking(); break;
                    case 5: DrawHud(); break;
                    case 6: DrawPings(); break;
                    case 7: DrawTheme(); break;
                    case 8: DrawLcd(); break;
                    case 9: DrawStorage(); break;
                    case 10: DrawAdvanced(); break;
                    default: DrawHome(); break;
                }

                AddFooter();
            }
            finally
            {
                _body.ResumeLayout(true);
                _building = false;
            }
        }

        private void DrawHome()
        {
            AddSection("MASTER CONTROL");
            AddCheck("Ore Helper enabled", _s.B("Enabled"), v => SetSave("Enabled", v, true));
            AddCheck("Streamer Mode", _s.B("StreamerMode"), v => { SetSave("StreamerMode", v, false); _host.ApplyCaptureAffinity(); RefreshFromSettings(); });
            AddCheck("Fail closed if capture exclusion fails", _s.B("StreamerFailClosed", true), v => SetSave("StreamerFailClosed", v, false));
            AddCheck("Auto-launch ZeosOreOverlay", _s.B("AutoStartOverlay", true), v => SetSave("AutoStartOverlay", v, false));

            AddSection("UI HOTKEY");
            var currentKey=_s.Get("MenuKey","PageUp");
            var menuKeys=MenuKeys.Concat(new[]{"None",currentKey}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            AddCombo(menuKeys, IndexOf(menuKeys,currentKey), i => SetSave("MenuKey",menuKeys[i],false));

            AddSection("CURRENT STATE");
            AddStatus("ORE HELPER: " + (_s.B("Enabled") ? "ACTIVE" : "OFF"), _s.B("Enabled"));
            AddStatus("STREAMER MODE: " + (_s.B("StreamerMode") ? "ON" : "OFF"), !_s.B("StreamerMode") || (_host.CaptureHudApplied && _host.CaptureMenuApplied));
            AddInfo("Active mining preset: " + _s.Get("ActivePreset", "Balanced") + "\nSettings save live. PgUp remains the default Ore Helper menu key; HOME remains reserved for ZeoCore.");
        }

        private void DrawPresets()
        {
            AddSection("MINING PROFILE");
            AddCombo(MiningProfiles, IndexOf(MiningProfiles, _s.Get("ActivePreset", "Balanced")), delegate(int i)
            {
                _s.ApplyPreset(MiningProfiles[i]);
                _host.SettingsChanged();
                RefreshFromSettings();
            });
            AddInfo("Profiles change ore enable/priority values only. HUD layout, pings, filters, colors, and LCD settings are not overwritten.");

            AddSection("PROFILE NOTES");
            AddInfo("REACTOR RUN = Uranium / Tungsten / Ice. RARE METALS emphasizes U / W / Ti / Pt / Au. MASS BUILD favors construction ores. Select ORES to fine-tune each material after applying a preset.");
        }

        private void DrawOres()
        {
            int selected = Clamp(_s.I("SelectedOreIndex", 0), 0, OreOverlaySettings.KnownOres.Length - 1);
            AddSection("ORE");
            AddCombo(OreOverlaySettings.KnownOres, selected, delegate(int i)
            {
                _s.Set("SelectedOreIndex", i);
                Save(false);
                RefreshFromSettings();
            });

            string ore = OreOverlaySettings.KnownOres[selected];
            AddSection(ore.ToUpperInvariant() + " CONFIGURATION");
            AddCheck("Enabled", _s.B("OreEnabled:" + ore, true), v => SetOre(ore, "OreEnabled:", v));
            AddNumber("Priority", _s.I("OreWeight:" + ore, 0), 0, 20, 1, v => SetOre(ore, "OreWeight:", (int)v));
            AddNumber("Minimum sampled %", (decimal)_s.D("OreMinPercent:" + ore, 0), 0, 100, 0.01m, v => SetOre(ore, "OreMinPercent:", (double)v), 3);
            AddCheck("Show in ranking list", _s.B("OreShowList:" + ore, true), v => SetOre(ore, "OreShowList:", v));
            AddCheck("Show on world pings", _s.B("OreShowPing:" + ore, true), v => SetOre(ore, "OreShowPing:", v));
            AddColor("Ore color", _s.Get("OreColor:" + ore, "#FFFFFF"), v => { _s.Set("OreColor:" + ore, v); Save(true); });
            AddInfo("Priority 0 ignores the ore for quality weighting. Each ore can independently control its minimum sampled percentage and HUD visibility.");
        }

        private void DrawFilters()
        {
            AddSection("DISTANCE");
            AddCheck("MAX LOADED range", _s.B("MaxLoadedRange"), v => SetSave("MaxLoadedRange", v, true));
            AddNumber("Minimum distance (km)", (decimal)(_s.D("MinimumDistanceMeters") / 1000.0), 0, 1000, 0.5m, v => SetSave("MinimumDistanceMeters", (double)v * 1000.0, true), 1);
            AddNumber("Maximum distance (km)", (decimal)(_s.D("SurveyRangeMeters", 45000) / 1000.0), 1, 1000, 1, v => SetSave("SurveyRangeMeters", (double)v * 1000.0, true), 1);

            AddSection("ASTEROID SIZE");
            AddNumber("Minimum diameter (m)", _s.I("MinimumDiameterMeters", 0), 0, 10000, 25, v => SetSave("MinimumDiameterMeters", (int)v, true));
            AddNumber("Maximum diameter (m, 0 = ANY)", _s.I("MaximumDiameterMeters", 0), 0, 10000, 25, v => SetSave("MaximumDiameterMeters", (int)v, true));

            AddSection("ORE MATCH");
            AddCheck("Wanted ores only", _s.B("WantedOnly"), v => SetSave("WantedOnly", v, true));
            AddCheck("Require ALL wanted ores", _s.B("RequireAllWantedOres"), v => SetSave("RequireAllWantedOres", v, true));
            AddCheck("Show no-ore / X asteroids", _s.B("ShowNoOre"), v => SetSave("ShowNoOre", v, true));
            AddNumber("Global minimum ore %", (decimal)_s.D("MinimumWantedOrePercent"), 0, 100, 0.01m, v => SetSave("MinimumWantedOrePercent", (double)v, true), 3);

            AddSection("MINIMUM GRADE");
            AddCombo(GradeValues, IndexOf(GradeValues, _s.Get("MinimumGrade", "D")), i => SetSave("MinimumGrade", GradeValues[i], true));
        }

        private void DrawRanking()
        {
            AddSection("RANKING MODE");
            AddCombo(RankingModes, IndexOf(RankingModes, _s.Get("RankingMode", "quality")), i => SetSave("RankingMode", RankingModes[i], true));
            AddCheck("Use asteroid size factor", _s.B("UseSizeFactor", true), v => SetSave("UseSizeFactor", v, true));

            AddSection("WORTH TRIP GRADE");
            AddCombo(WorthTripGrades, IndexOf(WorthTripGrades, _s.Get("WorthTripGrade", "A")), i => SetSave("WorthTripGrade", WorthTripGrades[i], true));

            AddSection("GRADE THRESHOLDS");
            AddNumber("S threshold", (decimal)_s.D("GradeS", 60), 1.5m, 250, 0.5m, v => SetSave("GradeS", (double)v, true), 1);
            AddNumber("A threshold", (decimal)_s.D("GradeA", 35), 1, 249.5m, 0.5m, v => SetSave("GradeA", (double)v, true), 1);
            AddNumber("B threshold", (decimal)_s.D("GradeB", 20), 0.5m, 249, 0.5m, v => SetSave("GradeB", (double)v, true), 1);
            AddNumber("C threshold", (decimal)_s.D("GradeC", 8), 0, 248.5m, 0.5m, v => SetSave("GradeC", (double)v, true), 1);
            AddNumber("Ice MUST threshold %", (decimal)_s.D("PureIceMustPercent", 100), 0, 100, 1, v => SetSave("PureIceMustPercent", (double)v, true), 1);
            AddInfo("Distance never changes geological grade. Quality + Distance only changes practical sorting priority.");
        }

        private void DrawHud()
        {
            AddSection("HUD LAYOUT");
            AddCombo(HudLayouts, IndexOf(HudLayouts, _s.Get("HudLayout", "Prospector")), i => SetSave("HudLayout", HudLayouts[i], true));

            AddSection("FRAME");
            AddCombo(FrameStyles, IndexOf(FrameStyles, _s.Get("FrameStyle", "Hex Command")), i => SetSave("FrameStyle", FrameStyles[i], true));

            AddSection("HUD VISIBILITY");
            AddCheck("Ranking list", _s.B("ListEnabled", true), v => SetSave("ListEnabled", v, true));
            AddCheck("Header", _s.B("ShowHeader", true), v => SetSave("ShowHeader", v, true));
            AddCheck("Status bar", _s.B("ShowStatusBar", true), v => SetSave("ShowStatusBar", v, true));
            AddCheck("Column header", _s.B("ShowColumnHeader", true), v => SetSave("ShowColumnHeader", v, true));

            AddSection("HUD GEOMETRY");
            AddNumber("Position X", (decimal)_s.D("PanelX", -0.72), -1, 1, 0.01m, v => SetSave("PanelX", (double)v, true), 2);
            AddNumber("Position Y", (decimal)_s.D("PanelY", -0.64), -1, 1, 0.01m, v => SetSave("PanelY", (double)v, true), 2);
            AddNumber("Panel scale", (decimal)_s.D("PanelScale", 1), 0.5m, 2, 0.05m, v => SetSave("PanelScale", (double)v, true), 2);
            AddNumber("Text scale", (decimal)_s.D("TextScale", 1), 0.5m, 2, 0.05m, v => SetSave("TextScale", (double)v, true), 2);
            AddNumber("Width scale", (decimal)_s.D("PanelWidthScale", 1), 0.65m, 2.5m, 0.05m, v => SetSave("PanelWidthScale", (double)v, true), 2);
            AddNumber("Row height scale", (decimal)_s.D("RowScale", 1), 0.65m, 1.75m, 0.05m, v => SetSave("RowScale", (double)v, true), 2);
            AddNumber("List rows", _s.I("ListRows", 8), 1, 20, 1, v => SetSave("ListRows", (int)v, true));
            AddNumber("Panel opacity", _s.I("PanelOpacity", 190), 0, 255, 5, v => SetSave("PanelOpacity", (int)v, true));
            AddNumber("Frame opacity", _s.I("FrameOpacity", 235), 0, 255, 5, v => SetSave("FrameOpacity", (int)v, true));
            AddNumber("Text opacity", _s.I("TextOpacity", 245), 0, 255, 5, v => SetSave("TextOpacity", (int)v, true));

            AddSection("VISIBLE COLUMNS");
            AddCheck("Roid #", _s.B("ShowColNumber", true), v => SetSave("ShowColNumber", v, true));
            AddCheck("Pin", _s.B("ShowColPin", true), v => SetSave("ShowColPin", v, true));
            AddCheck("Grade", _s.B("ShowColGrade", true), v => SetSave("ShowColGrade", v, true));
            AddCheck("Distance", _s.B("ShowColDistance", true), v => SetSave("ShowColDistance", v, true));
            AddCheck("Diameter", _s.B("ShowColDiameter", true), v => SetSave("ShowColDiameter", v, true));
            AddCheck("Top ore", _s.B("ShowColTopOre", true), v => SetSave("ShowColTopOre", v, true));
            AddCheck("Ore %", _s.B("ShowColOrePercent", true), v => SetSave("ShowColOrePercent", v, true));
            AddCheck("Second ore", _s.B("ShowColSecondOre", true), v => SetSave("ShowColSecondOre", v, true));
            AddCheck("Quality", _s.B("ShowColQuality"), v => SetSave("ShowColQuality", v, true));
            AddCheck("Status", _s.B("ShowColStatus"), v => SetSave("ShowColStatus", v, true));
        }

        private void DrawPings()
        {
            AddSection("WORLD PINGS");
            AddCheck("World pings", _s.B("MarkersEnabled", true), v => SetSave("MarkersEnabled", v, true));
            AddCheck("Offscreen arrows", _s.B("PingOffscreenArrows", true), v => SetSave("PingOffscreenArrows", v, true));
            AddCheck("Pinned always visible", _s.B("PinnedAlwaysVisible", true), v => SetSave("PinnedAlwaysVisible", v, true));
            AddNumber("Maximum pings", _s.I("MaxMarkers", 30), 0, 100, 1, v => SetSave("MaxMarkers", (int)v, true));
            AddNumber("Base ping scale", (decimal)_s.D("MarkerScale", 1), 0.35m, 3, 0.05m, v => SetSave("MarkerScale", (double)v, true), 2);
            AddNumber("Selected scale", (decimal)_s.D("SelectedMarkerScale", 1.25), 0.5m, 3, 0.05m, v => SetSave("SelectedMarkerScale", (double)v, true), 2);
            AddNumber("Offscreen scale", (decimal)_s.D("OffscreenMarkerScale", 1), 0.35m, 3, 0.05m, v => SetSave("OffscreenMarkerScale", (double)v, true), 2);

            AddSection("PING SIZE MODE");
            AddCombo(PingSizeModes, IndexOf(PingSizeModes, _s.Get("PingSizeMode", "Distance + Grade")), i => SetSave("PingSizeMode", PingSizeModes[i], true));
            AddSection("PING COLOR MODE");
            AddCombo(PingColorModes, IndexOf(PingColorModes, _s.Get("PingColorMode", "Grade")), i => SetSave("PingColorMode", PingColorModes[i], true));
            AddColor("Custom ping color", _s.Get("PingCustomColor", "#E7ECF2"), v => { _s.Set("PingCustomColor", v); Save(true); });

            AddSection("PING INFORMATION");
            AddCheck("Roid #", _s.B("PingShowNumber", true), v => SetSave("PingShowNumber", v, true));
            AddCheck("Grade", _s.B("PingShowGrade", true), v => SetSave("PingShowGrade", v, true));
            AddCheck("Distance", _s.B("PingShowDistance", true), v => SetSave("PingShowDistance", v, true));
            AddCheck("Diameter", _s.B("PingShowDiameter"), v => SetSave("PingShowDiameter", v, true));
            AddCheck("Top ore", _s.B("PingShowTopOre", true), v => SetSave("PingShowTopOre", v, true));
            AddCheck("Ore %", _s.B("PingShowOrePercent", true), v => SetSave("PingShowOrePercent", v, true));
        }

        private void DrawTheme()
        {
            AddSection("HUD THEME");
            AddCombo(Themes, ThemeIndex(_s.Get("ThemePreset", "WAR ROOM")), delegate(int i)
            {
                if (i != 4) _s.ApplyTheme(Themes[i]);
                else { _s.Set("ThemePreset", "CUSTOM"); _s.Save(); }
                _host.SettingsChanged();
                RefreshFromSettings();
            });

            AddSection("HUD COLORS");
            AddColor("Background", _s.Get("HudBackgroundColor", "#090607"), v => SetThemeColor("HudBackgroundColor", v));
            AddColor("Panel", _s.Get("HudPanelColor", "#120D0E"), v => SetThemeColor("HudPanelColor", v));
            AddColor("Accent", _s.Get("HudAccentColor", "#D83B3B"), v => SetThemeColor("HudAccentColor", v));
            AddColor("Text", _s.Get("HudTextColor", "#E8EAED"), v => SetThemeColor("HudTextColor", v));
            AddColor("Dim text", _s.Get("HudDimColor", "#8C9299"), v => SetThemeColor("HudDimColor", v));
            AddColor("Border", _s.Get("HudBorderColor", "#5A2024"), v => SetThemeColor("HudBorderColor", v));
            AddColor("Selected", _s.Get("SelectedColor", "#FFE16B"), v => SetThemeColor("SelectedColor", v));
            AddColor("Scanning", _s.Get("ScanningColor", "#69D6EE"), v => SetThemeColor("ScanningColor", v));
            AddColor("Error", _s.Get("ErrorColor", "#EB5A5A"), v => SetThemeColor("ErrorColor", v));

            AddSection("GRADE COLORS");
            AddColor("Grade S", _s.Get("GradeSColor", "#4BFF73"), v => SetThemeColor("GradeSColor", v));
            AddColor("Grade A", _s.Get("GradeAColor", "#73EB87"), v => SetThemeColor("GradeAColor", v));
            AddColor("Grade B", _s.Get("GradeBColor", "#F5DA50"), v => SetThemeColor("GradeBColor", v));
            AddColor("Grade C", _s.Get("GradeCColor", "#F59B46"), v => SetThemeColor("GradeCColor", v));
            AddColor("Grade D", _s.Get("GradeDColor", "#A5AAAF"), v => SetThemeColor("GradeDColor", v));
            AddColor("Grade X", _s.Get("GradeXColor", "#965555"), v => SetThemeColor("GradeXColor", v));
        }

        private void DrawLcd()
        {
            AddSection("STREAMER SAFETY");
            AddInfo("Streamer Mode clears and suppresses Zeo Ore Helper LCD output. In-world LCDs are part of the game frame and cannot be capture-excluded by the external overlay.");

            AddSection("LCD OUTPUT");
            AddCheck("LCD output enabled", _s.B("LcdEnabled"), v => SetSave("LcdEnabled", v, true));
            AddText("LCD name / tag", _s.Get("LcdTag", "Zeo Ore LCD"), v => SetSave("LcdTag", v, true));
            AddSection("LCD ROLE");
            AddCombo(LcdRoles, IndexOf(LcdRoles, _s.Get("LcdRole", "Ranking")), i => SetSave("LcdRole", LcdRoles[i], true));
            AddNumber("Rows", _s.I("LcdRows", 10), 1, 30, 1, v => SetSave("LcdRows", (int)v, true));
            AddNumber("Font scale", (decimal)_s.D("LcdFontScale", 0.75), 0.35m, 2, 0.05m, v => SetSave("LcdFontScale", (double)v, true), 2);
            AddCheck("Link LCD to HUD theme", _s.B("LcdLinkHudTheme", true), v => SetSave("LcdLinkHudTheme", v, true));

            AddSection("LCD THEME");
            AddCombo(Themes, ThemeIndex(_s.Get("LcdTheme", "WAR ROOM")), delegate(int i)
            {
                if (i != 4) _s.ApplyLcdTheme(Themes[i]);
                else { _s.Set("LcdTheme", "CUSTOM"); _s.Save(); }
                _host.SettingsChanged();
                RefreshFromSettings();
            });
            AddColor("LCD background", _s.Get("LcdBackgroundColor", "#090607"), v => SetLcdColor("LcdBackgroundColor", v));
            AddColor("LCD text", _s.Get("LcdTextColor", "#E8EAED"), v => SetLcdColor("LcdTextColor", v));
            AddColor("LCD accent", _s.Get("LcdAccentColor", "#D83B3B"), v => SetLcdColor("LcdAccentColor", v));
        }

        private void DrawStorage()
        {
            AddSection("ROID CACHE");
            AddCheck("Cache enabled", _s.B("CacheEnabled", true), v => SetSave("CacheEnabled", v, true));
            AddSection("MINIMUM CACHED GRADE");
            AddCombo(WorthTripGrades, IndexOf(WorthTripGrades, _s.Get("CacheMinimumGrade", "A")), i => SetSave("CacheMinimumGrade", WorthTripGrades[i], true));
            AddNumber("Normal entries per sector", _s.I("CachePerSector", 30), 5, 200, 1, v => SetSave("CachePerSector", (int)v, true));
            AddNumber("Retention days", _s.I("CacheRetentionDays", 14), 1, 365, 1, v => SetSave("CacheRetentionDays", (int)v, true));
            AddNumber("Maximum cache size (MB)", _s.I("CacheMaxMb", 5), 1, 100, 1, v => SetSave("CacheMaxMb", (int)v, true));
            AddInfo("Pinned records remain protected by the existing cache rules.");
        }

        private void DrawAdvanced()
        {
            AddSection("SURVEY ENGINE");
            AddInfo("These values tune the proven voxel survey engine. Lower frame intervals and very large sample budgets can increase client CPU usage.");
            AddNumber("Discovery cadence (frames)", _s.I("DiscoverEveryFrames", 60), 15, 600, 5, v => SetSave("DiscoverEveryFrames", (int)v, true));
            AddNumber("Survey cadence (frames)", _s.I("ScanEveryFrames", 20), 5, 300, 5, v => SetSave("ScanEveryFrames", (int)v, true));
            AddNumber("Rescan age (frames)", _s.I("RescanAfterFrames", 36000), 600, 216000, 600, v => SetSave("RescanAfterFrames", (int)v, true));
            AddNumber("Preferred voxel LOD", _s.I("PreferredLod", 3), 0, 6, 1, v => SetSave("PreferredLod", (int)v, true));
            AddNumber("Maximum sample cells", _s.I("MaxSampleCells", 300000), 25000, 1000000, 10000, v => SetSave("MaxSampleCells", (int)v, true));

            AddSection("SETTINGS FILE");
            AddInfo(_s.PathName);
        }

        // -----------------------------------------------------------------
        // ZeoCore-aligned UI primitives. Keep these visually in lock-step
        // with ZeoCore SettingsForm rather than inventing plugin-specific UI.
        // -----------------------------------------------------------------

        private void AddTitle(string title, string subtitle)
        {
            var p = NewPanel(92);
            var t = new Label { Text = title, AutoSize = false, Location = new Point(0, 2), Size = new Size(530, 34), Font = new Font("Segoe UI Semibold", 17f), ForeColor = MenuText(), TextAlign = ContentAlignment.MiddleLeft };
            var s = new Label { Text = subtitle, AutoSize = false, Location = new Point(1, 40), Size = new Size(530, 24), Font = new Font("Segoe UI", 9f), ForeColor = MenuAccent(), TextAlign = ContentAlignment.MiddleLeft };
            var close = new Button { Text = "×", FlatStyle = FlatStyle.Flat, Size = new Size(42, 34), Location = new Point(548, 2), ForeColor = MenuText(), BackColor = MenuPanel(), TabStop = false };
            close.FlatAppearance.BorderSize = 0;
            close.Click += delegate { Hide(); };
            p.Controls.Add(t); p.Controls.Add(s); p.Controls.Add(close);
            _body.Controls.Add(p);
        }

        private void AddFooter()
        {
            var p = NewPanel(48);
            p.Margin = new Padding(0, 12, 0, 0);
            var l = new Label
            {
                Text = _s.Get("MenuKey", "PageUp").ToUpperInvariant() + " opens in-game / closes here   //   ESC closes   //   settings save live",
                AutoSize = false, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5f), ForeColor = MenuAccent(), TextAlign = ContentAlignment.MiddleLeft
            };
            p.Controls.Add(l); _body.Controls.Add(p);
        }

        private void AddSection(string text)
        {
            var l = new Label
            {
                Text = text, AutoSize = false, Size = new Size(600, 32), Margin = new Padding(0, 12, 0, 4),
                Font = new Font("Segoe UI Semibold", 10f), ForeColor = MenuAccent(), TextAlign = ContentAlignment.BottomLeft
            };
            _body.Controls.Add(l);
        }

        private void AddStatus(string text, bool good)
        {
            var p = NewPanel(38);
            var l = new Label
            {
                Text = text, AutoSize = false, Dock = DockStyle.Fill, Padding = new Padding(10,0,10,0),
                Font = new Font("Segoe UI Semibold", 9f), ForeColor = good ? Color.FromArgb(150,220,180) : MenuAccent(), TextAlign = ContentAlignment.MiddleLeft
            };
            p.Controls.Add(l); _body.Controls.Add(p);
        }

        private void AddInfo(string text)
        {
            var l = new Label
            {
                Text = text, AutoSize = false, Size = new Size(600, 58), Margin = new Padding(0,4,0,4),
                Font = new Font("Segoe UI", 8.7f), ForeColor = MenuAccent(), TextAlign = ContentAlignment.MiddleLeft
            };
            _body.Controls.Add(l);
        }

        private void AddCheck(string text, bool value, Action<bool> changed)
        {
            var p = NewPanel(42);
            var c = new CheckBox
            {
                Text = text, Checked = value, AutoSize = false, Location = new Point(10, 3), Size = new Size(575, 34),
                Font = new Font("Segoe UI", 9.3f), ForeColor = MenuText(), BackColor = MenuPanel(), FlatStyle = FlatStyle.Flat
            };
            c.CheckedChanged += delegate { if (!_building) changed(c.Checked); };
            p.Controls.Add(c); _body.Controls.Add(p);
        }

        private void AddCombo(string[] values, int selected, Action<int> changed)
        {
            var p = NewPanel(46);
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(10, 8), Size = new Size(580, 30),
                Font = new Font("Segoe UI", 9.3f), ForeColor = MenuText(), BackColor = MenuPanel(), FlatStyle = FlatStyle.Flat
            };
            c.Items.AddRange(values);
            c.SelectedIndex = Clamp(selected, 0, Math.Max(0, values.Length - 1));
            c.SelectedIndexChanged += delegate { if (!_building && c.SelectedIndex >= 0) changed(c.SelectedIndex); };
            p.Controls.Add(c); _body.Controls.Add(p);
        }

        private void AddNumber(string label, decimal value, decimal min, decimal max, decimal inc, Action<decimal> changed, int decimals = 0)
        {
            var p = NewPanel(46);
            var l = new Label { Text = label, AutoSize = false, Location = new Point(10, 5), Size = new Size(390, 34), Font = new Font("Segoe UI", 9.2f), ForeColor = MenuText(), TextAlign = ContentAlignment.MiddleLeft };
            var n = new NumericUpDown
            {
                Location = new Point(430, 8), Size = new Size(155, 28), Minimum = min, Maximum = max,
                Increment = inc, DecimalPlaces = decimals, Value = Math.Max(min, Math.Min(max, value)),
                Font = new Font("Consolas", 9.2f), ForeColor = MenuText(), BackColor = Color.FromArgb(34,37,41), BorderStyle = BorderStyle.FixedSingle
            };
            n.ValueChanged += delegate { if (!_building) changed(n.Value); };
            p.Controls.Add(l); p.Controls.Add(n); _body.Controls.Add(p);
        }

        private void AddText(string label, string value, Action<string> changed)
        {
            var p = NewPanel(46);
            var l = new Label { Text = label, AutoSize = false, Location = new Point(10,5), Size = new Size(330,34), Font = new Font("Segoe UI",9.2f), ForeColor=MenuText(), TextAlign=ContentAlignment.MiddleLeft };
            var tb = new TextBox { Text = value ?? "", Location = new Point(350,9), Size = new Size(235,28), Font = new Font("Consolas",9.2f), ForeColor=MenuText(), BackColor=Color.FromArgb(34,37,41), BorderStyle=BorderStyle.FixedSingle };
            Action commit = delegate { if (!_building) changed(tb.Text ?? ""); };
            tb.Leave += delegate { commit(); };
            tb.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { commit(); e.SuppressKeyPress = true; } };
            p.Controls.Add(l); p.Controls.Add(tb); _body.Controls.Add(p);
        }

        private void AddColor(string label, string value, Action<string> setter)
        {
            var p = NewPanel(46);
            var l = new Label { Text = label, AutoSize = false, Location = new Point(10,5), Size = new Size(330,34), Font = new Font("Segoe UI",9.2f), ForeColor=MenuText(), TextAlign=ContentAlignment.MiddleLeft };
            var sw = new Panel { Location = new Point(350,11), Size = new Size(34,22), BackColor = SafeColor(value, Color.Gray) };
            var tb = new TextBox { Text = value ?? "#FFFFFF", Location = new Point(400,9), Size = new Size(185,28), Font = new Font("Consolas",9.2f), ForeColor=MenuText(), BackColor=Color.FromArgb(34,37,41), BorderStyle=BorderStyle.FixedSingle };
            Action commit = delegate
            {
                if (_building) return;
                string v = NormalizeHex(tb.Text);
                if (v == null) { tb.Text = value; return; }
                setter(v); sw.BackColor = SafeColor(v, Color.Gray);
            };
            tb.Leave += delegate { commit(); };
            tb.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { commit(); e.SuppressKeyPress = true; } };
            p.Controls.Add(l); p.Controls.Add(sw); p.Controls.Add(tb); _body.Controls.Add(p);
        }

        private Panel NewPanel(int height)
        {
            return new Panel { Size = new Size(600, height), Margin = new Padding(0,2,0,2), BackColor = MenuPanel() };
        }

        private void SetSave(string key, object value, bool markCustom)
        {
            if (_building) return;
            _s.Set(key, value);
            if (markCustom && key.StartsWith("Ore", StringComparison.OrdinalIgnoreCase)) _s.Set("ActivePreset", "CUSTOM");
            Save(markCustom);
        }

        private void SetOre(string ore, string prefix, object value)
        {
            if (_building) return;
            _s.Set(prefix + ore, value);
            _s.Set("ActivePreset", "CUSTOM");
            Save(true);
        }

        private void SetThemeColor(string key, string value)
        {
            if (_building) return;
            _s.Set(key, value);
            _s.Set("ThemePreset", "CUSTOM");
            _s.Save();
            _host.SettingsChanged();
            RefreshFromSettings();
        }

        private void SetLcdColor(string key, string value)
        {
            if (_building) return;
            _s.Set(key, value);
            _s.Set("LcdTheme", "CUSTOM");
            _s.Save();
            _host.SettingsChanged();
            RefreshFromSettings();
        }

        private void Save(bool markCustom)
        {
            if (_building) return;
            _s.Save();
            _host.SettingsChanged();
        }

        private void ApplyThemeToForm()
        {
            BackColor = MenuBackground();
            _body.BackColor = MenuBackground();
        }

        private Color MenuBackground() { return SafeColor(_s.Get("MenuBackgroundColor", "#07090B"), Color.FromArgb(7,9,11)); }
        private Color MenuPanel() { return SafeColor(_s.Get("MenuPanelColor", "#111419"), Color.FromArgb(17,20,25)); }
        private Color MenuText() { return SafeColor(_s.Get("MenuTextColor", "#F1F3F5"), Color.FromArgb(241,243,245)); }
        private Color MenuAccent() { return SafeColor(_s.Get("MenuAccentColor", "#D52B2B"), Color.FromArgb(213,43,43)); }

        private static Color SafeColor(string hex, Color fallback)
        {
            try { return ColorTranslator.FromHtml(hex); } catch { return fallback; }
        }

        private static string NormalizeHex(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string s = value.Trim(); if (!s.StartsWith("#")) s = "#" + s;
            if (s.Length != 7) return null;
            for (int i=1;i<7;i++) if (!Uri.IsHexDigit(s[i])) return null;
            return s.ToUpperInvariant();
        }

        private static Keys MenuKeyToFormsKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return Keys.PageUp;
            try
            {
                if (string.Equals(key, "PageUp", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "PAGE UP", StringComparison.OrdinalIgnoreCase)) return Keys.PageUp;
                if (string.Equals(key, "PageDown", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "PAGE DOWN", StringComparison.OrdinalIgnoreCase)) return Keys.PageDown;
                return (Keys)Enum.Parse(typeof(Keys), key, true);
            }
            catch { return Keys.PageUp; }
        }

        private static int IndexOf(string[] values, string value)
        {
            for (int i=0;i<values.Length;i++) if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }

        private static int ThemeIndex(string value)
        {
            string v=(value??"").Trim().ToUpperInvariant();
            if(v=="ZEO WAR ROOM"||v=="NIGHT")v="WAR ROOM";
            if(v=="INDUSTRIAL AMBER")v="AMBER";
            if(v=="TACTICAL CYAN"||v=="ICE")v="HIGH CONTRAST";
            return IndexOf(Themes,v);
        }

        private static int Clamp(int v, int min, int max) { return Math.Max(min, Math.Min(max, v)); }
    }
}
