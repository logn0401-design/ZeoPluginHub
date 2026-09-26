using ProtoBuf;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.Plugins;
using VRageMath;

namespace ZeoNav
{
    public sealed class Plugin : IPlugin
    {
        public const string Version = "1.1.15-CTRL-RETICLE-INTERCEPT-FLIP";
        private string catalogOverlayPath;

        // Pulsar supplies this hash-verified package before Init. Settings remain in dataDir.
        public void LoadAssets(IReadOnlyDictionary<string, string> assets)
        {
            string directory;
            if (assets == null || !assets.TryGetValue("ZeoNavOverlayPackage", out directory)
                || string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("Pulsar did not supply the Zeo Nav overlay package.");
            string executable = Path.GetFullPath(Path.Combine(directory, "ZeoNavOverlay.exe"));
            if (!File.Exists(executable) || !File.Exists(executable + ".config"))
                throw new FileNotFoundException("The Zeo Nav overlay package is incomplete.", executable);
            catalogOverlayPath = executable;
        }

        private NavUiHost uiHost;
        private bool worldWasReady;
        private readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulsar", "ZeoNav");
        private readonly string configPath;
        private readonly string logPath;
        private UdpClient tx;
        private UdpClient rx;
        private IPEndPoint overlayEndpoint;
        private int commandPort;
        private readonly Zeo.Shared.OverlayProcessOwner overlayProcess = new Zeo.Shared.OverlayProcessOwner();
        private NavConfig config;
        private ShipContext ship;
        private NavController nav;
        private DockingController docking;
        private NavRefuel refuel;
        private SpectrumAdapter spectrum;
        private readonly TargetTracker targets=new TargetTracker();
        private TargetFlight targetFlight;
        private NavTargetPickerScreen targetPicker;
        private readonly TargetCtrlLatch targetCtrl=new TargetCtrlLatch();
        private Vector2D targetPointer;
        private bool pointerVisible;
        private readonly System.Diagnostics.Stopwatch targetClock=System.Diagnostics.Stopwatch.StartNew();
        private readonly FlightVelocityApi flightVelocity=new FlightVelocityApi();
        private List<GpsDto> gps = new List<GpsDto>();
        private int frame;
        private bool menuVisible;
        private long gameHwnd;
        private int gamePid;
        private bool disposed;
        private DateTime lastOverlayLaunch = DateTime.MinValue;
        private bool hasSelectedGps;
        private string selectedGpsName = "";
        private Vector3D selectedGps;

        public Plugin()
        {
            configPath = Path.Combine(dataDir, "config.json");
            logPath = Path.Combine(dataDir, "zeonav.log");
        }

        public void Init(object gameInstance)
        {
            Directory.CreateDirectory(dataDir);
            config = JsonIo.Load<NavConfig>(configPath) ?? new NavConfig();
            config = ConfigRules.Clamp(config);
            JsonIo.Save(configPath, config);
            gamePid = Process.GetCurrentProcess().Id;
            gameHwnd = Process.GetCurrentProcess().MainWindowHandle.ToInt64();
            tx = new UdpClient(AddressFamily.InterNetwork);
            rx = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            commandPort = ((IPEndPoint)rx.Client.LocalEndPoint).Port;
            rx.Client.Blocking = false;
            Log("IPC command receiver bound dynamically on 127.0.0.1:" + commandPort);
            spectrum = new SpectrumAdapter(Log);
            spectrum.Init();
            targetFlight=new TargetFlight(()=>ship,()=>config,()=>spectrum,targets,Log);
            nav = new NavController(() => ship, () => config, () => spectrum, Log);
            refuel=new NavRefuel(()=>ship,Log,Path.Combine(dataDir,"refuel-recovery.jsonl"));
            docking=new DockingController(()=>ship,()=>config,()=>spectrum,Log,Path.Combine(dataDir,"dock-connectors.json"));
            docking.OnDocked=()=> {if(config.RefuelAfterDock)refuel.Toggle();};
            uiHost=new NavUiHost {
                Store=new NavUiStore(configPath,()=>config,updated=>config=updated),
                Snapshot=UiSnapshot,Command=ApplyCommand,EnsureOverlay=LaunchOverlay,Log=Log,Docking=docking,Refuel=refuel,
                Legacy=()=> { menuVisible=true; LaunchOverlay(); }
            };
            LaunchOverlay();
            Log("============================================================");
            Log("Zeo Nav " + Version + " initialized // menu=" + config.MenuKey);
            Log("Startup control state forced DISARMED. No route auto-resume.");
        }

        public void Dispose()
        {
            disposed = true;CloseTargetPicker();targetFlight?.Abort("Plugin closed.");targets.Reset();
            try {docking?.Abort("PLUGIN DISPOSE");refuel?.Stop("Plugin closed.");}catch(Exception ex){Log("Dock/refuel dispose: "+ex.Message);}
            NavNativeUi.Close();
            if(uiHost!=null) { uiHost.Layout=null; uiHost.Bounds=null; }
            try { nav?.Abort("PLUGIN DISPOSE"); } catch { }
            try { spectrum?.Dispose(); } catch { }
            flightVelocity.Reset();
            try { SpeedCapResolver.Dispose(); } catch { }
            try { rx?.Close(); } catch { }
            try { tx?.Close(); } catch { }
            overlayProcess.Dispose();
            Log("Zeo Nav disposed; overrides released.");
        }

        public void Update()
        {
            if (disposed) return;
            frame++;
            try
            {
                if (frame % 30 == 0) RefreshWindowIdentity();
                if (!SessionReady())
                {
                    docking?.Abort("Session or player unavailable.");refuel?.Stop("Session or player unavailable.");
                    if (nav != null && nav.IsControlling) nav.Abort("SESSION/PLAYER NOT READY");
                    CloseTargetPicker();targetFlight?.Abort("Session closed.");targets.Reset();
                    ship = null;
                    if(worldWasReady)
                    {
                        NavNativeUi.Close();
                        if(uiHost!=null) { uiHost.Layout=null; uiHost.Bounds=null; }
                    }
                    worldWasReady=false;
                    spectrum.ResetSession();flightVelocity.Reset();
                    if (frame % 30 == 0) SendSnapshot();
                    return;
                }

                RefreshShip();
                docking.RecoverMouse(ship);
                flightVelocity.Update(frame);ship?.UpdateMotion(flightVelocity);
                worldWasReady=true;
                if (frame % 120 == 0) RefreshGps();
                spectrum.Update(frame, ship);
                if(frame%6==0&&(targets.Selecting||targets.LockedId!=0))
                    targets.Update(spectrum.ReadContacts(),ship,MyAPIGateway.Session.GameplayFrameCounter,targetClock.Elapsed.TotalSeconds,MyAPIGateway.Session.Camera.WorldMatrix,false);
                if(targetPicker!=null)RefreshTargetAim();
                PollCommands();
                HandleHotkeys();
                if(targetFlight.Active)targetFlight.Update(MyAPIGateway.Session.GameplayFrameCounter,targetClock.Elapsed.TotalSeconds);else if(docking.Active)docking.Update();else nav.Update(frame);
                refuel.Update();

                if (targetPicker!=null||(targets.Confirmed?frame%2==0:frame%6==0)) SendSnapshot();
                if (frame % 600 == 0) LaunchOverlay();
            }
            catch (Exception ex)
            {
                Log("UPDATE ERROR: " + ex);targetFlight?.Abort("Update error.");
                try {docking?.Abort("Update error.");refuel?.Stop("Update error.");}catch { }
                try { nav?.Abort("UPDATE EXCEPTION " + ex.GetType().Name); } catch { }
            }
        }

        public void OpenConfigDialog()
        {
            try { if (SessionReady()) RefreshGps(); } catch { }
            ToggleMenu();
        }

        private void ToggleMenu()
        {
            CloseTargetPicker();
            if(menuVisible) { menuVisible=false; return; }
            if(uiHost==null || !NavNativeUi.Toggle(uiHost)) { menuVisible=true; LaunchOverlay(); }
        }

        private NavSnapshot UiSnapshot()
        {
            var s=nav.BuildSnapshot();
            var r=WinRect.GetClientScreenRect(new IntPtr(gameHwnd));
            s.Version=Version; s.GameHwnd=gameHwnd; s.GamePid=gamePid;
            s.ClientX=r.X; s.ClientY=r.Y; s.ClientW=r.W; s.ClientH=r.H;
            s.MenuVisible=menuVisible; s.Gps=gps; s.Config=config;
            s.Layout=uiHost==null ? null : uiHost.Layout;
            if(docking!=null&&docking.Active){s.State="DOCKING";s.Phase="DOCK "+docking.Stage;s.Destination=docking.Destination;s.DistanceMeters=docking.Distance;s.EtaSeconds=-1;s.WarningText=docking.Status;s.HudVisible=true;s.SignalGovernorState="DOCK / RCS ONLY";s.SpeedCapSource="DOCK RCS";s.SpeedCapMps=docking.SpeedLimitMps;s.CommandSpeedMps=docking.SpeedLimitMps;s.MaxDriveSigKm=ApproachProfile.Arrival(config);}
            else if(refuel!=null&&refuel.Active){s.State="REFUEL";s.Phase="REFUEL";s.WarningText=refuel.Status;s.HudVisible=true;}
            FillTargetSnapshot(s);
            return s;
        }
        private void FillTargetSnapshot(NavSnapshot s)
        {
            int tick=MyAPIGateway.Session==null?0:MyAPIGateway.Session.GameplayFrameCounter;
            s.TargetSelecting=pointerVisible&&targetPicker!=null&&targetPicker.HasFocus;s.TargetLocked=targets.Confirmed;s.TargetPointerX=targetPointer.X;s.TargetPointerY=targetPointer.Y;s.TargetPointerFree=true;s.TargetCursorState=targets.Selecting?(targets.CandidateId!=0?2:1):(targets.Confirmed?3:1);s.TargetStatus=targetFlight.Active?targetFlight.Status:targets.Status+" / "+targetFlight.Status;
            TargetTrack t=null;
            if(targets.Selecting)targets.Tracks.TryGetValue(targets.CandidateId,out t);else t=targets.Locked;
            s.TargetLabel=t==null?"NO FRESH CONTACT":t.Sample.Label;
            if(t!=null&&ship!=null&&MyAPIGateway.Session?.Camera!=null)
            {
                var pos=t.Position(tick);s.TargetDistance=Vector3D.Distance(pos,ship.Position);s.TargetRelativeSpeed=(t.Sample.Velocity-ship.Velocity).Length();
                var camera=MyAPIGateway.Session.Camera;var projected=camera.WorldToScreen(ref pos);
                s.TargetMarkerVisible=((targets.Selecting&&s.TargetSelecting)||targets.Confirmed)&&Vector3D.Dot(pos-camera.WorldMatrix.Translation,camera.WorldMatrix.Forward)>0&&Math.Abs(projected.X)<1&&Math.Abs(projected.Y)<1;
                s.TargetMarkerX=projected.X;s.TargetMarkerY=projected.Y;
            }
            if(targetFlight.Active){s.MaxDriveSigKm=targetFlight.Ceiling;s.SignalGovernorState="TARGET / OWN SIG BUDGET";s.FlipInSeconds=-1;s.StopDistanceMeters=0;s.Progress01=0;s.State="TARGET FLIGHT";s.Phase=targetFlight.Mode;s.Destination=s.TargetLabel;s.DistanceMeters=targetFlight.Distance;s.LateralMps=targetFlight.RelativeSpeed;s.WarningText=targetFlight.Status;s.HudVisible=true;s.EtaSeconds=-1;}
        }
        private void SelectTarget()
        {
            if(targetPicker!=null){CloseTargetPicker();return;}
            if(ship==null||MyAPIGateway.Session?.Camera==null){targets.Status="Control a ship before selecting a target.";return;}
            NavNativeUi.Close();menuVisible=false;targets.Select();
            var picker=new NavTargetPickerScreen(()=>config,RefreshTargetAim,ConfirmPointerTarget,()=>targets.Cycle(),AbortTargetPicking,ForwardTargetManualMove);
            picker.Closed+=delegate{if(ReferenceEquals(targetPicker,picker)){targetPicker=null;targets.Cancel();pointerVisible=false;}};
            targetPicker=picker;
            try{Sandbox.Graphics.GUI.MyGuiSandbox.AddScreen(picker);}
            catch{CloseTargetPicker();throw;}
        }
        private long RefreshTargetAim()
        {
            pointerVisible=false;
            if(targetPicker==null||!targetPicker.HasFocus||MyAPIGateway.Session?.Camera==null||!WinRect.IsForeground(new IntPtr(gameHwnd)))return 0;
            var bounds=WinRect.GetClientScreenRect(new IntPtr(gameHwnd));
            if(!WinRect.TryCursor(new IntPtr(gameHwnd),bounds,out targetPointer)){targets.CandidateId=0;return 0;}
            pointerVisible=true;var camera=MyAPIGateway.Session.Camera;
            if(!targets.Selecting)return 0;
            targets.Aim(MyAPIGateway.Session.GameplayFrameCounter,targetClock.Elapsed.TotalSeconds,camera.WorldMatrix,p=>camera.WorldToScreen(ref p),targetPointer,bounds.W,bounds.H);
            return targets.CandidateId;
        }
        private void ConfirmPointerTarget(long id)
        {
            if(!targets.Selecting||id==0||RefreshTargetAim()!=id)return;
            targets.Select();if(targets.Confirmed&&!targets.Selecting&&targetPicker!=null&&!targetPicker.CtrlAim)CloseTargetPicker();
        }
        private void CloseTargetPicker()
        {
            var picker=targetPicker;targetPicker=null;targets.Cancel();pointerVisible=false;
            try{picker?.CloseScreen(true);}catch{}
        }
        private void ClearTargetLock()
        {
            if(targetFlight.Active)targetFlight.Abort("Target lock cleared by pilot.");
            targets.ClearLock();pointerVisible=false;
            Log("TARGET LOCK CLEARED // pilot request");
        }
        private void AbortTargetPicking()
        {
            AbortAll("TARGET PICKER ABORT",false);
            targets.ClearLock();
            pointerVisible=false;
            Log("TARGET PICKER ABORT // flight, dock, route and lock released");
        }
        private void ForwardTargetManualMove(Vector3 move,float roll)
        {
            var current=MyAPIGateway.Session?.Player?.Controller?.ControlledEntity;
            if(ship==null||current==null||!ReferenceEquals(current.Entity,ship.Controller))return;
            if(move!=Vector3.Zero||Math.Abs(roll)>.001f)
            {
                if(docking.Active)docking.Abort("Manual movement/roll input: docking released.");
                if(targetFlight.Active)targetFlight.Abort("Manual movement/roll input: target flight released.");
                if(nav.IsControlling)nav.Abort("MANUAL PILOT INPUT");
            }
            if(!docking.Active&&!targetFlight.Active&&!nav.IsControlling)
                Sandbox.Game.World.MySession.Static?.ControlledEntity?.MoveAndRotate(move,Vector2.Zero,roll);
        }
        private void BeginTargetFlight(bool intercept)
        {
            if(targetFlight.Active){targetFlight.Abort("Target flight cancelled.");return;}
            if(!targets.Confirmed){targetFlight.Status="Select and confirm a fresh Spectrum contact first.";return;}
            nav.Abort("Target flight selected.");docking.Abort("Target flight selected.");refuel.Stop("Target flight selected.");
            targetFlight.Start(intercept,MyAPIGateway.Session.GameplayFrameCounter,targetClock.Elapsed.TotalSeconds);
        }
        private bool SessionReady()
        {
            return MyAPIGateway.Session != null && MyAPIGateway.Session.Player != null && MyAPIGateway.Input != null;
        }

        private void RefreshWindowIdentity()
        {
            try
            {
                var p = Process.GetCurrentProcess();
                gamePid = p.Id;
                if (p.MainWindowHandle != IntPtr.Zero) gameHwnd = p.MainWindowHandle.ToInt64();
            }
            catch { }
        }

        private void RefreshShip()
        {
            Sandbox.ModAPI.IMyShipController controller = null;
            try
            {
                var ce = MyAPIGateway.Session.Player.Controller?.ControlledEntity;
                if (ce != null) controller = ce.Entity as Sandbox.ModAPI.IMyShipController;
            }
            catch { }

            if (controller == null || controller.CubeGrid == null)
            {
                if (ship != null)
                {
                    CloseTargetPicker();targetFlight.Abort("Player left ship control.");targets.Reset();nav.Abort("PLAYER LEFT SHIP CONTROL");
                    docking.Abort("Player left ship control.");refuel.Stop("Player left ship control.");
                    ship = null;
                }
                return;
            }

            if (ship == null || ship.Controller.EntityId != controller.EntityId || ship.Grid.EntityId != controller.CubeGrid.EntityId)
            {
                if (ship != null) nav.Abort("CONTROLLED GRID CHANGED");
                CloseTargetPicker();targetFlight.Abort("Controlled ship changed.");targets.Reset();
                docking.Abort("Controlled ship changed.");refuel.Stop("Controlled ship changed.");
                ship = new ShipContext(controller, Log);
                ship.Scan();
                Log("Controlled grid -> " + ship.Name + " id=" + ship.Grid.EntityId + " gyros=" + ship.Gyros.Count + " thrusters=" + ship.ThrusterCount);
            }
            else if (frame % 300 == 0)
            {
                // Re-scan the whole mechanical construct so late-loaded/new/damaged
                // SDX main drives on subgrids are reflected without a cockpit change.
                ship.Scan();
            }
            else if (frame % 60 == 0)
            {
                ship.RefreshWorkingState();
            }
        }

        private void RefreshGps()
        {
            var next = new List<GpsDto>();
            try
            {
                var list = new List<IMyGps>();
                MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId, list);
                Vector3D own = ship != null ? ship.Position : MyAPIGateway.Session.Player.GetPosition();
                foreach (var g in list)
                {
                    if (g == null) continue;
                    next.Add(new GpsDto
                    {
                        Name = string.IsNullOrWhiteSpace(g.Name) ? "Unnamed GPS" : g.Name,
                        X = g.Coords.X,
                        Y = g.Coords.Y,
                        Z = g.Coords.Z,
                        Distance = Vector3D.Distance(own, g.Coords)
                    });
                }
                next.Sort((a, b) => a.Name.CompareTo(b.Name));
                gps = next;
            }
            catch (Exception ex)
            {
                if (frame % 600 == 0) Log("GPS read failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private void HandleHotkeys()
        {
            bool ctrlOpen=targetCtrl.Observe(NavTargetPickerScreen.CtrlHeld,config.TargetCtrlAim&&!NavNativeUi.CapturingKey&&!NavNativeUi.IsOpen&&!menuVisible&&ship!=null&&!MyAPIGateway.Gui.ChatEntryVisible&&!MyAPIGateway.Gui.IsCursorVisible&&WinRect.IsForeground(new IntPtr(gameHwnd)));
            if(targetPicker!=null)return; // The transparent picker owns input through mouse release.
            if(NavNativeUi.CapturingKey) return;
            if (KeyNew(config.MenuKey))
            {
                RefreshGps();
                ToggleMenu();
            }
            if (KeyNew(config.AbortKey)) {AbortAll("ABORT KEY");return;}
            if(targets.Selecting&&KeyNew("Escape")){targets.Cancel();return;}
            // Text entry and dropdown navigation must not launch a route or flip.
            // The configured emergency abort remains available while the menu is open.
            if(NavNativeUi.IsOpen || menuVisible || MyAPIGateway.Gui.ChatEntryVisible || MyAPIGateway.Gui.IsCursorVisible) return;
            if(ctrlOpen){SelectTarget();return;}
            if(NavHotkeys.Unique(config,"TargetSelectKey")&&KeyNew(config.TargetSelectKey,true)){SelectTarget();return;}
            if(NavHotkeys.Unique(config,"TargetCycleKey")&&KeyNew(config.TargetCycleKey,true)){targets.Cycle();return;}
            if(NavHotkeys.Unique(config,"InterceptKey")&&KeyNew(config.InterceptKey)){BeginTargetFlight(true);return;}
            if(NavHotkeys.Unique(config,"MatchVelocityKey")&&KeyNew(config.MatchVelocityKey)){BeginTargetFlight(false);return;}
            if(NavHotkeys.Unique(config,"QuickDockKey")&&KeyNew(config.QuickDockKey)){BeginDock(true);return;}
            if(NavHotkeys.Unique(config,"RefuelKey")&&KeyNew(config.RefuelKey)){if(!targetFlight.Active&&!docking.Active&&!nav.IsControlling)refuel.Toggle();return;}
            if (KeyNew(config.ManualFlipKey)) {targetFlight.Abort("Manual flip selected.");docking.Abort("Manual flip selected.");refuel.Stop("Flight selected.");nav.StartManualFlip();}
            if (KeyNew(config.SignalUpKey)) { config.MaxDriveSigKm = Math.Min(750, config.MaxDriveSigKm + 5); SaveConfig(); }
            if (KeyNew(config.SignalDownKey)) { config.MaxDriveSigKm = Math.Max(5, config.MaxDriveSigKm - 5); SaveConfig(); }
            if (KeyNew(config.StartKey))
            {
                targetFlight.Abort("Route selected.");docking.Abort("Route selected.");refuel.Stop("Flight selected.");
                if (hasSelectedGps) nav.StartRoute(selectedGpsName, selectedGps, config.BufferKm * 1000.0);
                else if (nav.HasLastDestination) nav.RestartLastRoute();
            }
        }

        private bool KeyNew(string key,bool allowLookAlt=false)
        {
            try { var binding=NavKeyBinding.Parse(key);var input=MyAPIGateway.Input;
                return binding.Key!=MyKeys.None && input.IsNewKeyPressed(binding.Key) &&
                    binding.Matches(input.IsAnyCtrlKeyPressed(),input.IsAnyAltKeyPressed(),input.IsAnyShiftKeyPressed(),allowLookAlt);
            } catch { return false; }
        }

        private void PollCommands()
        {
            if (rx == null) return;
            int guard = 0;
            while (rx.Available > 0 && guard++ < 20)
            {
                IPEndPoint ep = new IPEndPoint(IPAddress.Loopback, 0);
                byte[] bytes = rx.Receive(ref ep);
                var cmd = JsonIo.FromBytes<NavCommand>(bytes);
                if (cmd == null) continue;

                if (string.Equals((cmd.Type ?? "").Trim(), "HELLO", StringComparison.OrdinalIgnoreCase))
                {
                    int port = (int)Math.Round(cmd.Value);
                    if (port > 1024 && port < 65535)
                    {
                        bool changed = overlayEndpoint == null || overlayEndpoint.Port != port;
                        overlayEndpoint = new IPEndPoint(IPAddress.Loopback, port);
                        if (changed) Log("IPC overlay handshake -> snapshots 127.0.0.1:" + port + " // commands 127.0.0.1:" + commandPort);
                        SendSnapshot();
                    }
                    continue;
                }

                ApplyCommand(cmd);
            }
        }

        private void ApplyCommand(NavCommand cmd)
        {
            string t = (cmd.Type ?? "").Trim().ToUpperInvariant();
            if(t=="LAYOUT_BOUNDS") { if(uiHost!=null) uiHost.Receive(cmd.LayoutBounds); return; }
            if (t == "START")
            {
                targetFlight.Abort("Route selected.");docking.Abort("Route selected.");refuel.Stop("Flight selected.");
                config.BufferKm = Math.Max(0, Math.Min(10, cmd.Value));
                SaveConfig();
                nav.StartRoute(cmd.Name ?? "GPS", new Vector3D(cmd.X, cmd.Y, cmd.Z), config.BufferKm * 1000.0);
            }
            else if(t=="TARGET_SELECT")SelectTarget();
            else if(t=="TARGET_CLEAR")ClearTargetLock();
            else if(t=="INTERCEPT")BeginTargetFlight(true);
            else if(t=="MATCH_VELOCITY")BeginTargetFlight(false);
            else if (t == "ABORT") AbortAll("UI ABORT");
            else if (t == "FLIP") {targetFlight.Abort("Manual flip selected.");docking.Abort("Manual flip selected.");refuel.Stop("Flight selected.");nav.StartManualFlip();}
            else if (t == "DOCK_SCAN") docking.Scan();
            else if (t == "DOCK_START") BeginDock(false);
            else if (t == "DOCK_QUICK") BeginDock(true);
            else if (t == "REFUEL") {if(!targetFlight.Active&&!docking.Active&&!nav.IsControlling)refuel.Toggle();}
            else if (t == "SELECT_GPS")
            {
                selectedGpsName = string.IsNullOrWhiteSpace(cmd.Name) ? "GPS" : cmd.Name;
                selectedGps = new Vector3D(cmd.X, cmd.Y, cmd.Z);
                hasSelectedGps = true;
                if(uiHost!=null) uiHost.Selected=new GpsDto { Name=selectedGpsName,X=cmd.X,Y=cmd.Y,Z=cmd.Z };
                nav.SetPreviewDestination(selectedGpsName, selectedGps);
            }
            else if (t == "MENU_CLOSE") menuVisible = false;
            else if (t == "MENU_TOGGLE") ToggleMenu();
            else if (t == "SET") ApplySetting(cmd.Key, cmd.Text, cmd.Value);
        }

        private void AbortAll(string reason,bool closePicker=true)
        {if(closePicker)CloseTargetPicker();targetFlight?.Abort(reason);targets.Cancel();docking?.Abort(reason);refuel?.Stop(reason);nav?.Abort(reason);}
        private void BeginDock(bool quick)
        {
            if(docking.Active){docking.Abort("Docking cancelled.");return;}
            try{targetFlight.Abort("Docking requested.");nav.Abort("Docking requested.");refuel.Stop("Docking requested.");docking.Start(quick);}
            catch(Exception ex){docking.Abort("Docking could not start: "+ex.Message);Log("DOCK START ERROR // "+ex);}
            Log("DOCK REQUEST // active="+docking.Active+" // "+docking.Status);
            try {MyAPIGateway.Utilities.ShowNotification(docking.Status,5000,"White");}catch { }
        }
        private void ApplySetting(string key, string text, double value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            var p = typeof(NavConfig).GetField(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p == null) return;
            try
            {
                object next=p.FieldType==typeof(string) ? (object)(text??"") : p.FieldType==typeof(bool) ? (object)(value>=.5) : p.FieldType==typeof(int) ? (object)(int)Math.Round(value) : value;
                uiHost.Store.Apply(new Dictionary<string,object> { {p.Name,next} });
            }
            catch (Exception ex) { Log("SET " + key + " failed: " + ex.Message); }
        }

        private void SaveConfig()
        {
            try { JsonIo.Save(configPath, config); } catch (Exception ex) { Log("Config save failed: " + ex.Message); }
        }

        private void SendSnapshot()
        {
            if (overlayEndpoint == null) return;
            try
            {
                NavSnapshot s = UiSnapshot();
                byte[] bytes = JsonIo.ToBytes(s);
                tx.Send(bytes, bytes.Length, overlayEndpoint);
            }
            catch (Exception ex)
            {
                if (frame % 600 == 0) Log("Snapshot send failed: " + ex.Message);
            }
        }

        private void LaunchOverlay()
        {
            if ((DateTime.UtcNow - lastOverlayLaunch).TotalSeconds < 5) return;
            lastOverlayLaunch = DateTime.UtcNow;
            try
            {
                if (overlayProcess.Running) return;
                overlayEndpoint = null;

                string exe = catalogOverlayPath ?? Path.Combine(dataDir, "ZeoNavOverlay.exe");
                if (!File.Exists(exe)) return;

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--command-port " + commandPort,
                    WorkingDirectory = dataDir,
                    UseShellExecute = true
                };

                overlayProcess.Start(psi);
                Log("Capture-safe ZeoNavOverlay launch requested // commandPort=" + commandPort + " ownerPid=" + gamePid);
            }
            catch (Exception ex) { Log("Overlay launch failed: " + ex.Message); }
        }

        private void Log(string line)
        {
            try { File.AppendAllText(logPath, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC' ") + line + Environment.NewLine); } catch { }
        }
    }

    internal static class ConfigRules
    {
        public static NavConfig Clamp(NavConfig c)
        {
            if (c == null) c = new NavConfig();

            // v0.1.6 adds the configurable trip panel. Older preserved configs deserialize
            // missing bool/string members as false/null rather than using field initializers.
            // Upgrade them once so the default really is "match the active Zeo HUD".
            if (c.ConfigVersion < 2)
            {
                c.TripPanelVisibility = "AUTO";
                c.TripUseHudTheme = true;
                if (string.IsNullOrWhiteSpace(c.TripHudText)) c.TripHudText = "#E8ECF1";
                if (string.IsNullOrWhiteSpace(c.TripSecondaryText)) c.TripSecondaryText = "#AEB8C4";
                if (string.IsNullOrWhiteSpace(c.TripPanelBacking)) c.TripPanelBacking = "#101419";
                if (string.IsNullOrWhiteSpace(c.TripPanelBorder)) c.TripPanelBorder = "#617181";
                if (string.IsNullOrWhiteSpace(c.TripAccent)) c.TripAccent = "#FFB84A";
                c.ConfigVersion = 2;
            }

            // v0.1.9 makes capture exclusion an explicit user-facing Streamer Mode and
            // upgrades preserved configs to the privacy-safe default. It also reasserts
            // ZeoCore-follow mode for the trip panel so older experimental trip colors
            // cannot silently survive as the default visual language.
            if (c.ConfigVersion < 3)
            {
                c.StreamerMode = true;
                c.TripUseHudTheme = true;
                c.ConfigVersion = 3;
            }

            // v0.1.11 replaced the old magenta TransparencyKey trip HUD with a
            // true per-pixel-alpha layered window and moves all trip controls onto a
            // dedicated page. No user placement/color settings are discarded.
            if (c.ConfigVersion < 4)
            {
                c.ConfigVersion = 4;
            }

            // v0.1.14 removes the misleading aggression percentage entirely. Preserve the
            // user's previous displayed intent by migrating the v0.1.13 quadratic slider
            // to its formerly advertised KM value. Future control is direct MAX SIG KM.
            if (c.ConfigVersion < 5)
            {
                int oldSlider = Math.Max(5, Math.Min(100, c.DriveSlider));
                if (oldSlider >= 100) c.MaxDriveSigKm = 490.0;
                else
                {
                    double x = oldSlider / 100.0;
                    c.MaxDriveSigKm = Math.Max(5.0, Math.Min(490.0, 500.0 * x * x));
                }
                c.ConfigVersion = 5;
            }

            // v0.1.15 invalidates only the old learned SIG calibration store, not user UI
            // settings. The new v2 calibration file is populated only from validated live-KM
            // feedback, so bad v0.1.14 reflection samples cannot seed future routes.
            if (c.ConfigVersion < 6)
            {
                c.ConfigVersion = 6;
            }

            c.BufferKm = Math.Max(0, Math.Min(10, c.BufferKm));
            if (c.ConfigVersion < 7)
            {
                // The measured Artemis flip took ~16 seconds. Replace the old ten-second
                // default with twenty; preserve explicitly different allowances.
                if (Math.Abs(c.FlipTimeSeconds - 10) < .001) c.FlipTimeSeconds = 20;
                c.ConfigVersion = 7;
            }
            if(c.ConfigVersion<8){c.QuickDockKey="None";c.RefuelKey="None";c.DockScanMeters=500;c.DockStandOffMeters=50;c.DockApproachMps=1;c.RefuelAfterDock=false;c.ConfigVersion=8;}
            if(c.ConfigVersion<9){c.ApproachSigEnabled=false;c.ApproachSigKm=75;c.ApproachDistanceKm=100;c.ConfigVersion=9;}
            if(c.ConfigVersion<10){c.DepartureSigEnabled=false;c.DepartureSigKm=75;c.DepartureDistanceKm=100;c.ConfigVersion=10;}
            c.DepartureSigKm=SignalBudget.Finite(c.DepartureSigKm)?ClampD(c.DepartureSigKm,5,750):75;
            c.DepartureDistanceKm=SignalBudget.Finite(c.DepartureDistanceKm)?ClampD(c.DepartureDistanceKm,1,1000):100;
            c.ApproachSigKm=SignalBudget.Finite(c.ApproachSigKm)?ClampD(c.ApproachSigKm,5,750):75;
            c.ApproachDistanceKm=SignalBudget.Finite(c.ApproachDistanceKm)?ClampD(c.ApproachDistanceKm,1,1000):100;
            c.QuickDockKey=string.IsNullOrWhiteSpace(c.QuickDockKey)?"None":c.QuickDockKey;
            c.RefuelKey=string.IsNullOrWhiteSpace(c.RefuelKey)?"None":c.RefuelKey;
            c.DockScanMeters=SignalBudget.Finite(c.DockScanMeters)?ClampD(c.DockScanMeters,50,1000):500;
            c.DockStandOffMeters=SignalBudget.Finite(c.DockStandOffMeters)?ClampD(c.DockStandOffMeters,10,500):50;
            c.DockApproachMps=SignalBudget.Finite(c.DockApproachMps)?ClampD(c.DockApproachMps,.2,2):1;
            c.DriveSlider = Math.Max(5, Math.Min(100, c.DriveSlider)); // legacy field retained for old config compatibility
            c.MaxDriveSigKm = ClampD(c.MaxDriveSigKm <= 0 ? 125.0 : c.MaxDriveSigKm, 5.0, 750.0);
            c.SpeedCapOverride = SignalBudget.Finite(c.SpeedCapOverride) ? ClampD(c.SpeedCapOverride, 0.0, SpeedCapResolver.ServerCeilingMps) : 0;
            c.SpectrumFeedback = true; // direct KM governor requires live Spectrum feedback
            c.InterceptStandOffKm=SignalBudget.Finite(c.InterceptStandOffKm)&&c.InterceptStandOffKm>=2?Math.Min(50,c.InterceptStandOffKm):5;
            foreach(var key in new[]{"TargetSelectKey","TargetCycleKey","InterceptKey","MatchVelocityKey"}){var field=typeof(NavConfig).GetField(key);if(string.IsNullOrWhiteSpace((string)field.GetValue(c)))field.SetValue(c,"None");}
            if(c.ConfigVersion<11){c.MatchKeep=true;c.MatchRcsOnly=false;c.ConfigVersion=11;}
            if(c.ConfigVersion<12){c.DockTransitMps=5;c.ConfigVersion=12;}
            if(c.ConfigVersion<13)c.ConfigVersion=13;
            if(c.ConfigVersion<14){if(Math.Abs(c.DockTransitMps-5)<.001)c.DockTransitMps=6;c.ConfigVersion=14;}
            if(c.ConfigVersion<15){c.TargetCtrlAim=true;c.ConfigVersion=15;}
            c.DockTransitMps=SignalBudget.Finite(c.DockTransitMps)?ClampD(c.DockTransitMps,.2,6):6;
            c.FlipTimeSeconds = SignalBudget.Finite(c.FlipTimeSeconds)?Math.Max(1, Math.Min(1800, c.FlipTimeSeconds)):180;
            c.BrakeSafety = Math.Max(1.0, Math.Min(2.0, c.BrakeSafety));
            c.ArrivalRadiusMeters = Math.Max(1, Math.Min(1000, c.ArrivalRadiusMeters));
            c.ArrivalSpeedMps = Math.Max(0.05, Math.Min(25, c.ArrivalSpeedMps));
            // CONTROL LAB v0.1.x routes are deliberately stop routes. Do not expose
            // non-functional fly-through toggles: the user's required behavior is
            // automatic flip + burn + complete stop.
            c.AutoFlip = true;
            c.FullStop = true;
            c.GlobalScale = ClampD(c.GlobalScale, .50, 2.75);
            c.PanelScale = ClampD(c.PanelScale, .45, 2.50);
            c.DestinationScale = ClampD(c.DestinationScale, .5, 3.0);
            c.DistanceScale = ClampD(c.DistanceScale, .5, 3.0);
            c.SpeedScale = ClampD(c.SpeedScale, .5, 3.0);
            c.SignalScale = ClampD(c.SignalScale, .5, 3.0);
            c.EtaScale = ClampD(c.EtaScale, .5, 3.0);
            c.PhaseScale = ClampD(c.PhaseScale, .5, 3.0);
            c.FlipScale = ClampD(c.FlipScale, .5, 3.0);
            c.StopScale = ClampD(c.StopScale, .5, 3.0);
            c.ProgressScale = ClampD(c.ProgressScale, .5, 3.0);
            c.WarningScale = ClampD(c.WarningScale, .5, 3.0);
            c.HudWidth=NavLayoutModel.Size(c.HudWidth); c.HudHeight=NavLayoutModel.Size(c.HudHeight);
            c.HudX = ClampD(c.HudX, -.98, .98);
            c.HudY = ClampD(c.HudY, -.98, .98);
            c.BackingOpacity = Math.Max(0, Math.Min(245, c.BackingOpacity));
            c.BorderWidth = ClampD(c.BorderWidth, .25, 4.0);
            c.InnerPadding = ClampD(c.InnerPadding, .40, 2.25);
            c.CornerCut = ClampD(c.CornerCut, .25, 2.5);
            c.HeaderHeight = ClampD(c.HeaderHeight, .5, 2.0);
            c.PatternIntensity = ClampD(c.PatternIntensity, 0.0, 2.0);
            string trip = (c.TripPanelVisibility ?? "AUTO").Trim().ToUpperInvariant();
            if (trip != "AUTO" && trip != "ALWAYS" && trip != "HIDDEN") trip = "AUTO";
            c.TripPanelVisibility = trip;
            return c;
        }
        private static double ClampD(double x, double a, double b) { return x < a ? a : x > b ? b : x; }
    }

    internal static class JsonIo
    {
        public static byte[] ToBytes<T>(T obj)
        {
            using (var ms = new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(ms, obj); return ms.ToArray(); }
        }
        public static T FromBytes<T>(byte[] bytes) where T : class
        {
            try { using (var ms = new MemoryStream(bytes)) return new DataContractJsonSerializer(typeof(T)).ReadObject(ms) as T; } catch { return null; }
        }
        public static T Load<T>(string path) where T : class
        {
            try { return File.Exists(path) ? FromBytes<T>(File.ReadAllBytes(path)) : null; } catch { return null; }
        }
        public static void Save<T>(string path, T obj) { File.WriteAllBytes(path, ToBytes(obj)); }
    }

    internal struct ScreenRect { public int X, Y, W, H; }
    internal static class WinRect
    {
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        internal static bool IsForeground(IntPtr hwnd){return hwnd!=IntPtr.Zero&&GetForegroundWindow()==hwnd;}
        internal static bool TryCursor(IntPtr hwnd,ScreenRect bounds,out Vector2D pointer)
        {
            pointer=Vector2D.Zero;POINT p;
            if(hwnd==IntPtr.Zero||GetForegroundWindow()!=hwnd||!GetCursorPos(out p)||bounds.W<200||bounds.H<200)return false;
            pointer=TargetPointer.FromPixels(p.X-bounds.X,p.Y-bounds.Y,bounds.W,bounds.H);
            return TargetPointer.Valid(pointer,bounds.W,bounds.H);
        }
        public static ScreenRect GetClientScreenRect(IntPtr hwnd)
        {
            var s = new ScreenRect { X = 0, Y = 0, W = 1920, H = 1080 };
            try
            {
                RECT r; if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out r)) return s;
                POINT p = new POINT { X = 0, Y = 0 }; ClientToScreen(hwnd, ref p);
                s.X = p.X; s.Y = p.Y; s.W = Math.Max(1, r.R - r.L); s.H = Math.Max(1, r.B - r.T);
            }
            catch { }
            return s;
        }
    }
}

