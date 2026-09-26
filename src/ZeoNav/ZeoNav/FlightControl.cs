using Sandbox.ModAPI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using VRage.Game.ModAPI;
using VRageMath;

namespace ZeoNav
{
    internal enum MoveDir { Forward = 0, Backward = 1, Right = 2, Left = 3, Up = 4, Down = 5 }

    internal sealed class ShipContext
    {
        public readonly Sandbox.ModAPI.IMyShipController Controller;
        public readonly IMyCubeGrid Grid;
        // Gyros contains ONLY standard/navigation gyros that Zeo Nav may command.
        // SDX RCS Control Computers are intentionally never promoted into this list:
        // they are isolated while Zeo Nav owns attitude control so they cannot fight
        // the real gyro bank.
        public readonly List<Sandbox.ModAPI.IMyGyro> Gyros = new List<Sandbox.ModAPI.IMyGyro>();
        private readonly List<Sandbox.ModAPI.IMyGyro> allGyros = new List<Sandbox.ModAPI.IMyGyro>();
        private readonly List<Sandbox.ModAPI.IMyGyro> rcsGyros = new List<Sandbox.ModAPI.IMyGyro>();
        private readonly Dictionary<MoveDir, List<Sandbox.ModAPI.IMyThrust>> thrusters = new Dictionary<MoveDir, List<Sandbox.ModAPI.IMyThrust>>();
        private readonly HashSet<long> mainDriveIds = new HashSet<long>();
        private readonly HashSet<long> constructGridIds = new HashSet<long>();
        private readonly double[] force = new double[6];
        private readonly NavOsJitAim aim;
        private readonly PrecisionAim precisionAim = new PrecisionAim();
        internal bool AllowRcsTurnAssist;
        private bool wasPrecision, bankTurn;
        internal double TurnAllowanceSeconds=180;
        private double observedTurnRate;
        private void ReleaseTurnBank(Sandbox.ModAPI.IMyGyro keep)
        {if(!bankTurn)return;foreach(var g in Gyros)if(g!=keep&&!g.Closed){g.Pitch=g.Yaw=g.Roll=0;g.GyroOverride=false;}foreach(var g in rcsGyros)if(!g.Closed){g.Pitch=g.Yaw=g.Roll=0;g.GyroOverride=false;SetGyroEnabledRemember(g,false);}
            bankTurn=false;}
        private void CommandTurnBank(Vector3D worldRate)
        {
            bankTurn=true;
            foreach(var g in Gyros.Concat(AllowRcsTurnAssist?rcsGyros.Where(r=>r.BlockDefinition.SubtypeName=="sdg_rcsGyroComputer"):Enumerable.Empty<Sandbox.ModAPI.IMyGyro>()))
            {
                if(g.Closed||!g.IsFunctional)continue;
                if(rcsGyros.Any(r=>object.ReferenceEquals(r,g)))SetGyroEnabledRemember(g,true);
                if(!g.Enabled)continue;
                controlledGyroRefs[g.EntityId]=g;RememberGyroCommandState(g);
                var command=PrecisionAim.GyroCommand(worldRate,g.WorldMatrix);
                g.Pitch=(float)command.X;g.Yaw=(float)command.Y;g.Roll=(float)command.Z;
                if(!g.GyroOverride)g.GyroOverride=true;
            }
        }

        public string AimMode { get; private set; } = "NAVOS-1";
        private Sandbox.ModAPI.IMyGyro activeGyro;
        private int activeGyroCursor;

        private readonly Dictionary<long, float> savedOverrides = new Dictionary<long, float>();
        private readonly Dictionary<long, Sandbox.ModAPI.IMyThrust> savedThrustRefs = new Dictionary<long, Sandbox.ModAPI.IMyThrust>();
        private bool thrustControlActive;
        private bool stagingThrust;
        private readonly ThrustCommandCache sentThrust = new ThrustCommandCache();
        internal Dictionary<MoveDir, List<Sandbox.ModAPI.IMyThrust>> ThrusterBanks { get { return thrusters; } }
        internal SignalBudget SignatureBudget;
        internal bool RcsOnly;
        internal static bool IsRcs(Sandbox.ModAPI.IMyThrust t)
        { return t != null && DriveClassifier.IsRcs(t.BlockDefinition.ToString(), t.DefinitionDisplayNameText, t.MaxThrust); }
        internal double RcsForce(MoveDir d)
        { return thrusters[d].Where(t => t != null && !t.Closed && t.IsWorking && IsRcs(t)).Sum(t => (double)t.MaxEffectiveThrust); }
        internal readonly double[] AppliedCommands = new double[6];
        public int TopologyRevision { get; private set; }
        private string signatureTopology = "";
        public void RefreshSignatureTopology()
        {
            string key = TopologyKey();
            if (key != signatureTopology) { signatureTopology = key; TopologyRevision++; }
        }
        public bool ThrustersQuiet
        {
            get
            {
                foreach (var bank in thrusters.Values) foreach (var t in bank)
                    if (t != null && !t.Closed && (t.CurrentThrust > 1 || t.ThrustOverridePercentage > 0)) return false;
                return true;
            }
        }

        private string TopologyKey()
        {
            var keys = new List<string>();
            foreach (var bank in thrusters) foreach (var t in bank.Value)
                if (t != null && !t.Closed) keys.Add(t.EntityId + ":" + (int)bank.Key + ":" + t.IsWorking);
            keys.Sort(StringComparer.Ordinal);
            return string.Join(";", keys);
        }

        private sealed class GyroCommandState
        {
            public bool Override;
            public float Pitch;
            public float Yaw;
            public float Roll;
            public float Power;
        }

        private readonly Dictionary<long, bool> savedGyroEnabled = new Dictionary<long, bool>();
        private readonly Dictionary<long, GyroCommandState> savedGyroCommands = new Dictionary<long, GyroCommandState>();
        private readonly Dictionary<long, Sandbox.ModAPI.IMyGyro> savedGyroRefs = new Dictionary<long, Sandbox.ModAPI.IMyGyro>();
        private readonly Dictionary<long, Sandbox.ModAPI.IMyGyro> controlledGyroRefs = new Dictionary<long, Sandbox.ModAPI.IMyGyro>();
        private bool gyroControlActive;
        private readonly Action<string> log;
        public bool? SavedDampeners;

        public ShipContext(Sandbox.ModAPI.IMyShipController controller, Action<string> logger)
        {
            Controller = controller;
            Grid = controller.CubeGrid;
            log = logger;
            aim = new NavOsJitAim(Grid == null ? VRage.Game.MyCubeSize.Large : Grid.GridSizeEnum);
            for (int i = 0; i < 6; i++) thrusters[(MoveDir)i] = new List<Sandbox.ModAPI.IMyThrust>();
        }

        public string Name { get { try { return Grid.DisplayName ?? "Controlled Ship"; } catch { return "Controlled Ship"; } } }
        public Vector3D Position { get { try { return Controller.WorldAABB.Center; } catch { return Controller.GetPosition(); } } }
        internal readonly WorldMotion Motion=new WorldMotion();
        private int loggedMotionFault;
        internal void UpdateMotion(FlightVelocityApi velocityApi=null)
        {
            try
            {
                double mass=Mass,bound=Gravity.Length();for(int i=0;i<6;i++)bound+=force[i]/mass;
                Vector3D modVelocity;
                if(velocityApi!=null&&velocityApi.TryRead(Grid,out modVelocity))Motion.ObserveMod(Controller.CenterOfMass,Controller.GetShipVelocities().LinearVelocity,modVelocity,MyAPIGateway.Session.GameplayFrameCounter/60d,bound);
                else Motion.Observe(Controller.CenterOfMass,Controller.GetShipVelocities().LinearVelocity,
                    MyAPIGateway.Session.GameplayFrameCounter/60d,bound);
                if(Motion.FaultGeneration!=loggedMotionFault){loggedMotionFault=Motion.FaultGeneration;log("MOTION SAMPLE REJECTED // "+Motion.Source+" // "+Motion.LastFault);}
            }
            catch{Motion.Reset();}
        }
        public Vector3D Velocity { get { return Motion.Ready?Motion.Velocity:RawVelocity; } }
        private Vector3D RawVelocity {get{try{return Controller.GetShipVelocities().LinearVelocity;}catch{return Vector3D.Zero;}}}
        public Vector3D Gravity { get { try { return Controller.GetNaturalGravity(); } catch { return Vector3D.Zero; } } }
        public double Mass { get { try { return Math.Max(1, Controller.CalculateShipMass().PhysicalMass); } catch { return 1; } } }
        public int ThrusterCount { get { int n = 0; foreach (var x in thrusters.Values) n += x.Count; return n; } }
        public int ConstructGridCount { get; private set; }
        public int MainDriveCount { get; private set; }
        public int ForwardMainDriveCount { get; private set; }
        public int ForwardWorkingMainDriveCount { get; private set; }
        public double ForwardMainRatedForce { get; private set; }
        public double ForwardMainEffectiveForce { get; private set; }
        public int ForwardWorkingThrusterCount { get; private set; }
        public double ForwardCommandRatio { get; private set; }
        public double ForwardReadbackRatio { get; private set; }
        public int RcsGyroCount { get; private set; }
        public int SubgridGyroCount { get; private set; }
        public int TotalGyroCount { get { return allGyros.Count; } }
        public double NavGyroPowerPercent { get; private set; }
        public double LastAlignmentErrorDeg { get; private set; }
        public double LastAngularRateDeg { get; private set; }

        public string ForceSummary
        {
            get
            {
                return "grids=" + ConstructGridCount +
                    " thr=" + ThrusterCount +
                    " main=" + MainDriveCount +
                    " fMain=" + ForwardMainDriveCount +
                    " fMainReady=" + ForwardWorkingMainDriveCount +
                    " mainRated=" + Mn(ForwardMainRatedForce) +
                    " mainAvailable=" + Mn(ForwardMainEffectiveForce) +
                    " fWork=" + ForwardWorkingThrusterCount +
                    " align=" + LastAlignmentErrorDeg.ToString("0.000") + "deg" +
                    " omega=" + LastAngularRateDeg.ToString("0.000") + "deg/s" +
                    " fCmd=" + (ForwardCommandRatio * 100.0).ToString("0.0") + "%" +
                    " fRb=" + (ForwardReadbackRatio * 100.0).ToString("0.0") + "%" +
                    " gyro=" + TotalGyroCount +
                    " navGyro=" + Gyros.Count(g=>!g.Closed&&g.GyroOverride) + "/" + Gyros.Count +
                    " gyroMode=" + AimMode +
                    " otherOverrides=" + Gyros.Count(g => g != activeGyro && !g.Closed && g.GyroOverride) +
                    " subGyro=" + SubgridGyroCount +
                    " rcsGyro=" + RcsGyroCount +
                    " gyroPwr=" + NavGyroPowerPercent.ToString("0") + "%" +
                    " F=" + Mn(Force(MoveDir.Forward)) +
                    " B=" + Mn(Force(MoveDir.Backward)) +
                    " R=" + Mn(Force(MoveDir.Right)) +
                    " L=" + Mn(Force(MoveDir.Left)) +
                    " U=" + Mn(Force(MoveDir.Up)) +
                    " D=" + Mn(Force(MoveDir.Down));
            }
        }

        internal bool HasDockConnection()
        {
            foreach(var grid in GetMechanicalConstructGrids())
            {
                var blocks=new List<IMySlimBlock>();grid.GetBlocks(blocks);
                if(blocks.Any(b=>(b.FatBlock as Sandbox.ModAPI.IMyShipConnector)?.Status==Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected))return true;
            }
            return false;
        }
        public void Scan()
        {
            var previousGyro = activeGyro;
            foreach (var x in thrusters.Values) x.Clear();
            Gyros.Clear();
            allGyros.Clear();
            rcsGyros.Clear();
            mainDriveIds.Clear();
            MainDriveCount = 0;
            ForwardMainDriveCount = 0;
            ForwardWorkingThrusterCount = 0;
            ForwardCommandRatio = AppliedCommands[(int)MoveDir.Forward];
            ForwardReadbackRatio = 0;
            constructGridIds.Clear();
            RcsGyroCount = 0;
            SubgridGyroCount = 0;
            NavGyroPowerPercent = 0;

            // SDX treats the ship as a mechanically linked construct. Epstein drives can
            // live on mechanically linked subgrids, so a single-grid scan under-counts
            // the main drive and can falsely report NO FORWARD THRUST.
            List<IMyCubeGrid> grids = GetMechanicalConstructGrids();
            ConstructGridCount = grids.Count;
            for (int gi = 0; gi < grids.Count; gi++)
            {
                IMyCubeGrid constructGrid = grids[gi];
                if (constructGrid != null && !constructGrid.Closed)
                    constructGridIds.Add(constructGrid.EntityId);
            }

            var seen = new HashSet<long>();
            var blocks = new List<IMySlimBlock>();
            for (int gi = 0; gi < grids.Count; gi++)
            {
                IMyCubeGrid member = grids[gi];
                if (member == null || member.Closed) continue;
                blocks.Clear();
                try
                {
                    member.GetBlocks(blocks, b => b != null && b.FatBlock != null &&
                        (b.FatBlock is Sandbox.ModAPI.IMyThrust || b.FatBlock is Sandbox.ModAPI.IMyGyro));
                }
                catch { continue; }

                for (int bi = 0; bi < blocks.Count; bi++)
                {
                    IMySlimBlock slim = blocks[bi];
                    var fat = slim == null ? null : slim.FatBlock;
                    if (fat == null || !seen.Add(fat.EntityId)) continue;

                    var t = fat as Sandbox.ModAPI.IMyThrust;
                    if (t != null)
                    {
                        if (thrustControlActive) RememberThrustOverride(t);
                        // Proven NavOS convention: a thruster accelerates opposite its
                        // WorldMatrix.Forward / exhaust-facing direction.
                        MoveDir blockDir = ClosestDir(t.WorldMatrix.Forward, Controller.WorldMatrix);
                        MoveDir accelDir = MoveDir.Forward;
                        if (blockDir == MoveDir.Backward) accelDir = MoveDir.Forward;
                        else if (blockDir == MoveDir.Forward) accelDir = MoveDir.Backward;
                        else if (blockDir == MoveDir.Left) accelDir = MoveDir.Right;
                        else if (blockDir == MoveDir.Right) accelDir = MoveDir.Left;
                        else if (blockDir == MoveDir.Down) accelDir = MoveDir.Up;
                        else if (blockDir == MoveDir.Up) accelDir = MoveDir.Down;
                        thrusters[accelDir].Add(t);

                        bool mainDrive = LooksLikeMainDrive(t);
                        if (mainDrive)
                        {
                            mainDriveIds.Add(t.EntityId);
                            MainDriveCount++;
                            if (accelDir == MoveDir.Forward) ForwardMainDriveCount++;
                        }
                        continue;
                    }

                    var g = fat as Sandbox.ModAPI.IMyGyro;
                    if (g != null && g.IsFunctional)
                    {
                        allGyros.Add(g);
                        if (LooksLikeRcsGyro(g))
                        {
                            // Isolate RCS Control Computers anywhere on the construct.
                            rcsGyros.Add(g);
                            RcsGyroCount++;
                        }
                        else if (g.CubeGrid == Controller.CubeGrid)
                        {
                            // Attitude control is deliberately limited to gyros rigidly
                            // mounted on the controller grid. Mechanical-subgrid gyros can
                            // torque rotors/hinges or inject a second dynamic body into the
                            // loop, which is unacceptable for precision alignment.
                            Gyros.Add(g);
                        }
                        else
                        {
                            SubgridGyroCount++;
                        }
                    }
                }
            }

            RefreshWorkingState();
            RefreshSignatureTopology();
            if (previousGyro != null && Gyros.Contains(previousGyro) && previousGyro.IsFunctional)
                activeGyro = previousGyro;
            else { activeGyro = null; activeGyroCursor = 0; ResetAim(); }
            if (thrustControlActive)
            {
                var attached = new HashSet<long>();
                foreach (var bank in thrusters.Values) foreach (var t in bank) attached.Add(t.EntityId);
                foreach (var t in savedThrustRefs.Values)
                    if (t != null && !t.Closed && !attached.Contains(t.EntityId))
                        try { t.ThrustOverridePercentage = 0; } catch { }
            }
            if (gyroControlActive) ApplyGyroControlState();
            try { log("SHIP SCAN // " + ForceSummary); } catch { }
        }

        internal List<IMyCubeGrid> GetMechanicalConstructGrids()
        {
            var result = new List<IMyCubeGrid>();
            AddGridUnique(result, Grid);

            // Use reflection so Zeo Nav survives minor Keen ModAPI signature changes
            // between SE builds. We support both GetGridGroup(...) and older GetGroup(...)
            // shapes, then call GetGrids(...) on the returned group data when present.
            try
            {
                object gridGroups = MyAPIGateway.GridGroups;
                if (gridGroups != null)
                {
                    MethodInfo[] methods = gridGroups.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
                    for (int mi = 0; mi < methods.Length; mi++)
                    {
                        MethodInfo m = methods[mi];
                        if (!m.Name.Equals("GetGridGroup", StringComparison.Ordinal) && !m.Name.Equals("GetGroup", StringComparison.Ordinal)) continue;
                        ParameterInfo[] ps = m.GetParameters();
                        object[] args = new object[ps.Length];
                        bool usable = true;
                        bool hasGrid = false;
                        for (int pi = 0; pi < ps.Length; pi++)
                        {
                            Type pt = ps[pi].ParameterType;
                            if (typeof(IMyCubeGrid).IsAssignableFrom(pt)) { args[pi] = Grid; hasGrid = true; continue; }
                            if (pt.IsEnum)
                            {
                                try { args[pi] = Enum.Parse(pt, "Mechanical", true); continue; } catch { usable = false; break; }
                            }
                            if (pt.IsAssignableFrom(typeof(List<IMyCubeGrid>))) { args[pi] = result; continue; }
                            usable = false; break;
                        }
                        if (!usable || !hasGrid) continue;

                        object group = null;
                        try { group = m.Invoke(gridGroups, args); } catch { continue; }
                        CollectGrids(group, result);
                        if (result.Count > 1) break;
                    }
                }
            }
            catch { }

            // Also merge the terminal system's same-construct view. Do this even when
            // GridGroups already found more than one grid: it catches API/version edge
            // cases where the returned mechanical group is incomplete.
            try
            {
                var terminal = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(Grid);
                if (terminal != null)
                {
                    var blocks = new List<Sandbox.ModAPI.Ingame.IMyTerminalBlock>();
                    terminal.GetBlocks(blocks);
                    for (int i = 0; i < blocks.Count; i++)
                    {
                        var b = blocks[i];
                        if (b == null || !b.IsSameConstructAs(Controller)) continue;
                        AddGridUnique(result, b.CubeGrid as IMyCubeGrid);
                    }
                }
            }
            catch { }

            return NormalizeConstructGrids(result);
        }

        internal static List<IMyCubeGrid> NormalizeConstructGrids(IEnumerable<IMyCubeGrid> grids)
        {
            // GetGroup(grid, kind, list) may append the root already seeded above.
            var seen=new HashSet<long>();var result=new List<IMyCubeGrid>();
            foreach(var grid in grids)if(grid!=null&&!grid.Closed&&seen.Add(grid.EntityId))result.Add(grid);
            return result;
        }

        private static void CollectGrids(object group, List<IMyCubeGrid> output)
        {
            if (group == null) return;
            var enumerable = group as IEnumerable;
            if (enumerable != null && !(group is string))
            {
                try { foreach (object x in enumerable) AddGridUnique(output, x as IMyCubeGrid); } catch { }
            }
            try
            {
                MethodInfo[] methods = group.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo m = methods[i];
                    if (!m.Name.Equals("GetGrids", StringComparison.Ordinal)) continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 0)
                    {
                        object r = m.Invoke(group, null);
                        var e = r as IEnumerable;
                        if (e != null) foreach (object x in e) AddGridUnique(output, x as IMyCubeGrid);
                    }
                    else if (ps.Length == 1 && ps[0].ParameterType.IsAssignableFrom(typeof(List<IMyCubeGrid>)))
                    {
                        var temp = new List<IMyCubeGrid>();
                        m.Invoke(group, new object[] { temp });
                        for (int g = 0; g < temp.Count; g++) AddGridUnique(output, temp[g]);
                    }
                }
            }
            catch { }
        }

        private static void AddGridUnique(List<IMyCubeGrid> list, IMyCubeGrid grid)
        {
            if (grid == null) return;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].EntityId == grid.EntityId) return;
            list.Add(grid);
        }

        public bool ContainsConstructGridId(long entityId)
        {
            return entityId != 0 && constructGridIds.Contains(entityId);
        }

        public int WorkingThrusterCount(MoveDir d)
        {
            int count = 0;
            List<Sandbox.ModAPI.IMyThrust> list = thrusters[d];
            for (int i = 0; i < list.Count; i++)
            {
                Sandbox.ModAPI.IMyThrust thruster = list[i];
                if (thruster != null && !thruster.Closed && thruster.IsWorking) count++;
            }
            return count;
        }

        private double ReadbackOverride(MoveDir d)
        {
            double sum = 0, weight = 0;
            foreach (var thrust in thrusters[d])
            {
                if (thrust == null || thrust.Closed || !thrust.IsWorking || (RcsOnly && !IsRcs(thrust))) continue;
                try
                {
                    double forceWeight = Math.Max(0, thrust.MaxEffectiveThrust);
                    sum += thrust.ThrustOverridePercentage * forceWeight;
                    weight += forceWeight;
                }
                catch { }
            }
            return weight > 0 ? sum / weight : 0;
        }

        public void LogDriveAudit()
        {
            log("DRIVE AUDIT // all IMyThrust subtypes participate; main drive families are auto-classified from definitions. Disabled or unpowered drives are not force-enabled.");
            foreach (var bank in thrusters)
                foreach (var group in bank.Value.Where(t => t != null && !t.Closed).GroupBy(t => t.BlockDefinition.ToString()))
                {
                    int total=0, working=0, enabled=0, functional=0;
                    double rated=0, effective=0, actual=0;
                    foreach (var t in group)
                    {
                        total++; if(t.Enabled) enabled++; if(t.IsFunctional) functional++;
                        rated += t.MaxThrust;
                        if(t.IsWorking) { working++; effective += t.MaxEffectiveThrust; }
                        actual += t.CurrentThrust;
                    }
                    log("DRIVE BANK // " + bank.Key + " // " + group.Key + " // main=" + group.Any(t => mainDriveIds.Contains(t.EntityId)) + " count=" + total +
                        " enabled=" + enabled + " functional=" + functional + " working=" + working +
                        " rated=" + Mn(rated) + " effective=" + Mn(effective) + " actual=" + Mn(actual));
                }
        }

        public void RefreshWorkingState()
        {
            for (int d = 0; d < 6; d++)
            {
                double total = 0;
                var list = thrusters[(MoveDir)d];
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var t = list[i];
                    if (t == null || t.Closed) { list.RemoveAt(i); continue; }
                    if (t.IsWorking) total += t.MaxEffectiveThrust;
                }
                force[d] = total;
            }
            ForwardWorkingThrusterCount = WorkingThrusterCount(MoveDir.Forward);
            ForwardReadbackRatio = ReadbackOverride(MoveDir.Forward);
            ForwardWorkingMainDriveCount = 0; ForwardMainRatedForce = ForwardMainEffectiveForce = 0;
            foreach (var thrust in thrusters[MoveDir.Forward])
            {
                if (thrust == null || thrust.Closed || !mainDriveIds.Contains(thrust.EntityId)) continue;
                ForwardMainRatedForce += thrust.MaxThrust;
                if (thrust.IsWorking && thrust.MaxEffectiveThrust > 1)
                { ForwardWorkingMainDriveCount++; ForwardMainEffectiveForce += thrust.MaxEffectiveThrust; }
            }
            for (int i = allGyros.Count - 1; i >= 0; i--) if (allGyros[i] == null || allGyros[i].Closed || !allGyros[i].IsFunctional) allGyros.RemoveAt(i);
            for (int i = rcsGyros.Count - 1; i >= 0; i--) if (rcsGyros[i] == null || rcsGyros[i].Closed || !rcsGyros[i].IsFunctional) rcsGyros.RemoveAt(i);
            for (int i = Gyros.Count - 1; i >= 0; i--) if (Gyros[i] == null || Gyros[i].Closed || !Gyros[i].IsFunctional) Gyros.RemoveAt(i);
        }

        public double Force(MoveDir d) { return RcsOnly ? RcsForce(d) : force[(int)d]; }
        public double Accel(MoveDir d, double ratio) { return Force(d) * Math.Max(0, Math.Min(1, ratio)) / Mass; }

        public void SaveDampenersOnce()
        {
            if (!SavedDampeners.HasValue) SavedDampeners = Controller.DampenersOverride;
        }

        public void SetDampeners(bool value) { try { Controller.DampenersOverride = value; } catch { } }

        public void RestoreDampeners()
        {
            try { if (SavedDampeners.HasValue) Controller.DampenersOverride = SavedDampeners.Value; } catch { }
            SavedDampeners = null;
        }

        private void RememberThrustOverride(Sandbox.ModAPI.IMyThrust t)
        {
            if (t == null || t.Closed || savedOverrides.ContainsKey(t.EntityId)) return;
            try { savedOverrides[t.EntityId] = t.ThrustOverridePercentage; } catch { savedOverrides[t.EntityId] = 0; }
            savedThrustRefs[t.EntityId] = t;
        }

        public void BeginThrustControl()
        {
            if (thrustControlActive) return;
            savedOverrides.Clear();
            savedThrustRefs.Clear();
            sentThrust.Reset();
            foreach (var kv in thrusters)
            {
                List<Sandbox.ModAPI.IMyThrust> list = kv.Value;
                for (int i = 0; i < list.Count; i++) RememberThrustOverride(list[i]);
            }
            thrustControlActive = true;
        }

        public void EndThrustControl(bool restorePrior)
        {
            if (!thrustControlActive) return;
            stagingThrust = false;
            sentThrust.Reset();
            SignatureBudget = null; RcsOnly = false;
            Array.Clear(AppliedCommands, 0, AppliedCommands.Length);

            // Clear every currently discovered Zeo-controlled thruster first.
            foreach (var kv in thrusters)
            {
                List<Sandbox.ModAPI.IMyThrust> list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    if (t == null || t.Closed) continue;
                    try { t.ThrustOverridePercentage = 0; } catch { }
                }
            }

            // Restore the exact pre-nav values using saved block references, even if a
            // mechanical topology rescan moved the block out of the current lists.
            if (savedThrustRefs.Count > 0)
            {
                foreach (var kv in savedThrustRefs)
                {
                    Sandbox.ModAPI.IMyThrust t = kv.Value;
                    if (t == null || t.Closed) continue;
                    float value;
                    if (!restorePrior || !savedOverrides.TryGetValue(kv.Key, out value)) value = 0;
                    try { t.ThrustOverridePercentage = value; } catch { }
                }
            }

            savedOverrides.Clear();
            savedThrustRefs.Clear();
            thrustControlActive = false;
            ForwardCommandRatio = 0;
            ForwardReadbackRatio = ReadbackOverride(MoveDir.Forward);
        }

        // Compute all six axes locally, then publish only the final command per bank.
        // ClearThrust during planning must never send an intermediate zero burn.
        public void BeginThrustFrame() { stagingThrust = thrustControlActive; }
        public void CommitThrustFrame()
        {
            if (!stagingThrust) return;
            stagingThrust = false;
            if (!thrustControlActive) return;
            // Release old allocations before requesting new ones on another axis.
            for (int pass = 0; pass < 2; pass++)
                for (int d = 0; d < 6; d++) WriteMove((MoveDir)d, (float)AppliedCommands[d], pass);
            ForwardWorkingThrusterCount = WorkingThrusterCount(MoveDir.Forward);
            ForwardReadbackRatio = ReadbackOverride(MoveDir.Forward);
        }
        public void CancelThrustFrame()
        {
            stagingThrust = false;
            ClearThrust();
        }
        private void WriteMove(MoveDir d, float p, int pass = -1)
        {
            double now = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
            foreach (var thrust in thrusters[d])
            {
                if (thrust == null || thrust.Closed) continue;
                try
                {
                    float output = RcsOnly && !IsRcs(thrust) ? 0 : p;
                    float readback = thrust.ThrustOverridePercentage;
                    bool reduction = output <= sentThrust.PreviousRatio(thrust.EntityId, readback);
                    if (pass >= 0 && (pass == 0) != reduction) continue;
                    if (sentThrust.ShouldSend(thrust.EntityId, output, readback, now)) thrust.ThrustOverridePercentage = output;
                }
                catch { sentThrust.Forget(thrust.EntityId); }
            }
        }
        public void SetMove(MoveDir d, double ratio)
        {
            if (!thrustControlActive) return;
            float p = SignalBudget.Finite(ratio) ? (float)Math.Max(0, Math.Min(1, ratio)) : 0;
            if (SignatureBudget != null && p > 0) p = (float)SignatureBudget.Limit((int)d, p, AppliedCommands);
            AppliedCommands[(int)d] = p;
            if (!stagingThrust) WriteMove(d, p);
            if (d == MoveDir.Forward)
            {
                ForwardCommandRatio = p;
                if (!stagingThrust)
                {
                    ForwardWorkingThrusterCount = WorkingThrusterCount(MoveDir.Forward);
                    ForwardReadbackRatio = ReadbackOverride(MoveDir.Forward);
                }
            }
        }

        public void ClearThrust()
        {
            for (int d = 0; d < 6; d++) SetMove((MoveDir)d, 0);
        }

        public void ResetAim() { aim.Reset(); precisionAim.Reset(); wasPrecision=false; AimMode="NAVOS-1"; LastAlignmentErrorDeg = 0; LastAngularRateDeg = 0; }

        private void RememberGyroEnabled(Sandbox.ModAPI.IMyGyro g)
        {
            if (g == null || g.Closed || savedGyroEnabled.ContainsKey(g.EntityId)) return;
            try { savedGyroEnabled[g.EntityId] = g.Enabled; } catch { savedGyroEnabled[g.EntityId] = true; }
            savedGyroRefs[g.EntityId] = g;
        }

        private void RememberGyroCommandState(Sandbox.ModAPI.IMyGyro g)
        {
            if (g == null || g.Closed || savedGyroCommands.ContainsKey(g.EntityId)) return;
            var state = new GyroCommandState();
            try
            {
                state.Override = g.GyroOverride;
                state.Pitch = g.Pitch;
                state.Yaw = g.Yaw;
                state.Roll = g.Roll;
                state.Power = g.GyroPower;
            }
            catch { }
            savedGyroCommands[g.EntityId] = state;
            savedGyroRefs[g.EntityId] = g;
        }

        private void SetGyroEnabledRemember(Sandbox.ModAPI.IMyGyro g, bool enabled)
        {
            if (g == null || g.Closed) return;
            RememberGyroEnabled(g);
            try { if (g.Enabled != enabled) g.Enabled = enabled; } catch { }
        }

        public void BeginGyroControl()
        {
            if (!gyroControlActive)
            {
                savedGyroEnabled.Clear();
                savedGyroCommands.Clear();
                savedGyroRefs.Clear();
                controlledGyroRefs.Clear();
                aim.Reset();
                gyroControlActive = true;
            }
            ApplyGyroControlState();
        }

        private Sandbox.ModAPI.IMyGyro EnsureActiveGyro()
        {
            if (activeGyro != null && !activeGyro.Closed && activeGyro.IsFunctional && Gyros.Any(r=>object.ReferenceEquals(r,activeGyro)))
            {
                controlledGyroRefs[activeGyro.EntityId] = activeGyro;
                RememberGyroCommandState(activeGyro);
                SetGyroEnabledRemember(activeGyro, true);
                try { NavGyroPowerPercent = activeGyro.GyroPower * 100; } catch { NavGyroPowerPercent = 0; }
                return activeGyro;
            }

            // Working NavOS does not multiply attitude authority across the whole gyro bank.
            // It owns ONE functional gyro at a time and falls through to the next only if
            // that gyro is unavailable. Mirror that behavior exactly here.
            activeGyro = null;
            ResetAim();
            if (Gyros.Count == 0) return null;
            int count = Gyros.Count;
            for (int n = 0; n < count; n++)
            {
                int idx = (activeGyroCursor + n) % count;
                Sandbox.ModAPI.IMyGyro g = Gyros[idx];
                if (g == null || g.Closed || !g.IsFunctional) continue;
                activeGyro = g;
                activeGyroCursor = (idx + 1) % Math.Max(1, count);
                controlledGyroRefs[g.EntityId] = g;
                RememberGyroCommandState(g);
                SetGyroEnabledRemember(g, true);
                try { NavGyroPowerPercent = g.GyroPower * 100; } catch { NavGyroPowerPercent = 0; }
                try { log("NAVOS GYRO SELECT -> " + g.CustomName + " id=" + g.EntityId + " power=" + NavGyroPowerPercent.ToString("0") + "%"); } catch { }
                return g;
            }
            return null;
        }

        private void ApplyGyroControlState()
        {
            if (!gyroControlActive) return;

            // Exact working NavOS authority model: ONE standard gyro is commanded. Do not
            // alter GyroPower and do not place every standard gyro into override mode.
            EnsureActiveGyro();

            // SDX RCS Control Computers are a second attitude-control system and can
            // fight the proven NavOS gyro solution. They are never used as a fallback.
            // Neutralize/disable them while Zeo Nav owns attitude, then restore exactly.
            for (int i = 0; i < rcsGyros.Count; i++)
            {
                Sandbox.ModAPI.IMyGyro g = rcsGyros[i];
                if (g == null || g.Closed || !g.IsFunctional) continue;
                if(AllowRcsTurnAssist&&bankTurn&&g.BlockDefinition.SubtypeName=="sdg_rcsGyroComputer")continue;
                controlledGyroRefs[g.EntityId] = g;
                RememberGyroCommandState(g);
                SetGyroEnabledRemember(g, false);
                try
                {
                    if (g.Pitch != 0) g.Pitch = 0;
                    if (g.Yaw != 0) g.Yaw = 0;
                    if (g.Roll != 0) g.Roll = 0;
                    if (g.GyroOverride) g.GyroOverride = false;
                }
                catch { }
            }
        }

        public void ReleaseGyros()
        {
            bankTurn=false;AllowRcsTurnAssist=false;
            aim.Reset();

            // Restore command state, then return touched RCS computers to enabled pilot control.
            foreach (var kv in controlledGyroRefs)
            {
                Sandbox.ModAPI.IMyGyro g = kv.Value;
                if (g == null || g.Closed) continue;
                GyroCommandState state;
                if (!savedGyroCommands.TryGetValue(kv.Key, out state) || state == null)
                {
                    try { g.Pitch = 0; g.Yaw = 0; g.Roll = 0; g.GyroOverride = false; } catch { }
                    continue;
                }
                try
                {
                    g.Pitch = state.Pitch;
                    g.Yaw = state.Yaw;
                    g.Roll = state.Roll;
                    g.GyroPower = state.Power;
                    g.GyroOverride = state.Override;
                }
                catch { }
            }

            foreach (var kv in savedGyroRefs)
            {
                Sandbox.ModAPI.IMyGyro g = kv.Value;
                if (g == null || g.Closed) continue;
                bool value;
                if (!savedGyroEnabled.TryGetValue(kv.Key, out value)) continue;
                try { g.Enabled = LooksLikeRcsGyro(g) || value; } catch { }
            }

            savedGyroEnabled.Clear();
            savedGyroCommands.Clear();
            savedGyroRefs.Clear();
            controlledGyroRefs.Clear();
            activeGyro = null;
            activeGyroCursor = 0;
            NavGyroPowerPercent = 0;
            gyroControlActive = false;
        }

        public void ReleaseAll(bool restorePriorThrust = true)
        {
            EndThrustControl(restorePriorThrust);
            ReleaseGyros();
            RestoreDampeners();
        }

        public void ApplyWorldAcceleration(Vector3D accelWorld, double maxRatio)
        {
            ClearThrust();
            Vector3D thrustAccelWorld = accelWorld - Gravity;
            MatrixD tr = MatrixD.Transpose(Controller.WorldMatrix);
            Vector3D local = Vector3D.TransformNormal(thrustAccelWorld, tr);
            double mass = Mass;
            ApplyAxis(local.X, mass, MoveDir.Right, MoveDir.Left, maxRatio);
            ApplyAxis(local.Y, mass, MoveDir.Up, MoveDir.Down, maxRatio);
            if (local.Z >= 0) ApplyForce(MoveDir.Backward, local.Z * mass, maxRatio); else ApplyForce(MoveDir.Forward, -local.Z * mass, maxRatio);
        }

        public void ApplySideDamping(Vector3D velocityWorld, Vector3D routeDir, double responseSeconds, double maxRatio)
        {
            if (routeDir.LengthSquared() < 1e-8) return;
            routeDir.Normalize();
            double along = Vector3D.Dot(velocityWorld, routeDir);
            Vector3D lateral = velocityWorld - routeDir * along;
            Vector3D desired = -lateral / Math.Max(.15, responseSeconds);
            MatrixD tr = MatrixD.Transpose(Controller.WorldMatrix);
            Vector3D local = Vector3D.TransformNormal(desired, tr);
            double mass = Mass;
            ApplyAxisNoClear(local.X, mass, MoveDir.Right, MoveDir.Left, maxRatio);
            ApplyAxisNoClear(local.Y, mass, MoveDir.Up, MoveDir.Down, maxRatio);
        }

        private void ApplyAxis(double localAccel, double mass, MoveDir positive, MoveDir negative, double maxRatio)
        {
            if (localAccel >= 0) ApplyForce(positive, localAccel * mass, maxRatio); else ApplyForce(negative, -localAccel * mass, maxRatio);
        }
        private void ApplyAxisNoClear(double localAccel, double mass, MoveDir positive, MoveDir negative, double maxRatio)
        {
            // Clear the opposite axis explicitly. Without this, a sign change in lateral
            // correction can leave BOTH RCS directions overridden from adjacent ticks.
            if (localAccel >= 0) { SetMove(negative, 0); ApplyForce(positive, localAccel * mass, maxRatio); }
            else { SetMove(positive, 0); ApplyForce(negative, -localAccel * mass, maxRatio); }
        }
        private void ApplyForce(MoveDir d, double requestedForce, double maxRatio)
        {
            double available = Force(d); if (available <= 1) { SetMove(d, 0); return; }
            SetMove(d, Math.Min(maxRatio, requestedForce / available));
        }

        public double ForwardAngleDegrees(Vector3D desiredForward)
        {
            if (desiredForward.LengthSquared() < 1e-8) return 0;
            desiredForward.Normalize();
            double dot = Math.Max(-1, Math.Min(1, Vector3D.Dot(Controller.WorldMatrix.Forward, desiredForward)));
            return Math.Acos(dot) * 180.0 / Math.PI;
        }

        public void ApplyDockRotation(Vector3D worldRate)
        {
            if(!gyroControlActive)BeginGyroControl();
            var g=EnsureActiveGyro();if(g==null)throw new InvalidOperationException("No navigation gyro available.");
            ReleaseTurnBank(g);
            var command=PrecisionAim.GyroCommand(worldRate,g.WorldMatrix);
            g.Pitch=(float)command.X;g.Yaw=(float)command.Y;g.Roll=(float)command.Z;g.GyroOverride=true;AimMode="DOCK-POSE";
        }

        public bool Orient(Vector3D desiredForward, double toleranceDeg)
        {
            if (Gyros.Count == 0 || desiredForward.LengthSquared() < 1e-8) return false;
            if (!gyroControlActive) BeginGyroControl();
            else ApplyGyroControlState(); // re-isolate SDX RCS attitude controllers every tick

            Sandbox.ModAPI.IMyGyro g = EnsureActiveGyro();
            if (g == null) return false;

            desiredForward.Normalize();
            double angle = ForwardAngleDegrees(desiredForward);
            LastAlignmentErrorDeg = angle;
            Vector3D angularVelocity=Vector3D.Zero; bool velocityKnown=true;
            try { angularVelocity=Controller.GetShipVelocities().AngularVelocity; LastAngularRateDeg=angularVelocity.Length()*180/Math.PI; }
            catch { LastAngularRateDeg=0; velocityKnown=false; }
            Vector3D worldRate=Vector3D.Zero;
            if(velocityKnown&&angle>5)
            {
                Vector3D forward=Controller.WorldMatrix.Forward;
                var axis=Vector3D.Cross(forward,desiredForward);
                if(axis.LengthSquared()<1e-8)axis=Controller.WorldMatrix.Up;else axis.Normalize();
                double radians=angle*Math.PI/180;
                double rate=Math.Min(.35,Math.Sqrt(2*.08*radians));
                worldRate=DockingMath.Limit(axis*Math.Min(rate,radians*1.5)-angularVelocity*.6,.35);
                CommandTurnBank(worldRate);AimMode=AllowRcsTurnAssist?"TURN-BANK + RCS":"TURN-BANK";wasPrecision=false;
                // Never shorten the braking reservation from an assisted turn:
                // Spectrum may remove RCS authority before the later flip.
                if(!AllowRcsTurnAssist&&angle>30&&LastAngularRateDeg>.2)
                {
                    observedTurnRate=observedTurnRate==0?LastAngularRateDeg:observedTurnRate*.98+LastAngularRateDeg*.02;
                    TurnAllowanceSeconds=Math.Max(20,180/observedTurnRate*1.5+10);
                }
                return false;
            }
            ReleaseTurnBank(g);
            bool precise=velocityKnown && precisionAim.TryRate(Controller.WorldMatrix.Forward,desiredForward,angularVelocity,out worldRate);
            if(precise)
            {
                if(!wasPrecision) aim.Reset();
                Vector3D command=PrecisionAim.GyroCommand(worldRate,g.WorldMatrix);
                g.Pitch=(float)command.X; g.Yaw=(float)command.Y; g.Roll=(float)command.Z;
                g.GyroOverride=true; AimMode="PRECISION-RATE";
            }
            else
            {
                if(wasPrecision) { aim.Reset(); precisionAim.Reset(); }
                aim.Apply(desiredForward,g,Controller.WorldMatrix); AimMode="NAVOS-1";
            }
            wasPrecision=precise;

            // Acquisition must be both aligned and slow enough to hold, not merely pass
            // through the heading while still rotating. Burn hold gates remain unchanged.
            return velocityKnown && PrecisionAim.Settled(angle, toleranceDeg, angularVelocity);
        }

        private static bool LooksLikeMainDrive(Sandbox.ModAPI.IMyThrust thrust)
        {
            if (thrust == null) return false;
            string definition = "", display = ""; double rated = 0;
            try { definition = thrust.BlockDefinition.ToString(); } catch { }
            try { display = thrust.DefinitionDisplayNameText; } catch { }
            try { rated = thrust.MaxThrust; } catch { }
            return DriveClassifier.IsMain(definition, display, rated);
        }

        private static bool LooksLikeRcsGyro(Sandbox.ModAPI.IMyGyro gyro)
        {
            string text = "";
            try { text += gyro.CustomName + " "; } catch { }
            try { text += gyro.BlockDefinition.ToString() + " "; } catch { }
            try { text += gyro.DefinitionDisplayNameText + " "; } catch { }
            return text.IndexOf("rcs", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("reaction control", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (text.IndexOf("control computer", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    text.IndexOf("gyro", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string Mn(double n) { return (n / 1000000.0).ToString("0.0") + "MN"; }

        private static MoveDir ClosestDir(Vector3D v, MatrixD m)
        {
            double best = double.MinValue; MoveDir dir = MoveDir.Forward;
            Vector3D[] axes = { m.Forward, m.Backward, m.Right, m.Left, m.Up, m.Down };
            for (int i = 0; i < 6; i++) { double d = Vector3D.Dot(v, axes[i]); if (d > best) { best = d; dir = (MoveDir)i; } }
            return dir;
        }
    }

    // v0.1.11: direct transplant of the JitAim controller from the supplied,
    // server-working NavOS SDX2 EOAISLOP v7.5.6.7 / StarCpt NavOS 2.16 base.
    //
    // Important fidelity rules:
    //   * ONE functional gyro is driven at a time (ShipContext selects it).
    //   * no plugin PID / angular-velocity feedback is layered on top.
    //   * no GyroPower rewrite.
    //   * use the exact NavOS ship->gyro Pitch/Yaw/Roll sign transform.
    //   * axis-specific coast/braking is inferred from error movement per Update1 tick.
    internal sealed class NavOsJitAim
    {
        private readonly double gyroMaxRpm;
        private double lastAngleRoll, lastAnglePitch, lastAngleYaw;
        private double lastMovedPerTickRoll, lastMovedPerTickPitch, lastMovedPerTickYaw;
        private double modRoll, modPitch, modYaw;
        private bool active;
        private int ticksOnTarget;

        private readonly double errorThreshold = DegToRad(0.025);
        private readonly double minVelThreshold = DegToRad(0.005);
        private readonly double ampThreshold = DegToRad(10.0);
        private const int MinTicksOnTarget = 5;

        public NavOsJitAim(VRage.Game.MyCubeSize gridSize)
        {
            double angleMultiplier = gridSize == VRage.Game.MyCubeSize.Small ? 2.0 : 1.0;
            gyroMaxRpm = 3.1415 * angleMultiplier;
            Reset();
        }

        public bool Settled { get { return ticksOnTarget > MinTicksOnTarget; } }

        public void Reset()
        {
            active = false;
            lastAngleRoll = lastAnglePitch = lastAngleYaw = 0;
            lastMovedPerTickRoll = lastMovedPerTickPitch = lastMovedPerTickYaw = 0;
            modRoll = modPitch = modYaw = 0;
            ticksOnTarget = 0;
        }

        private void Flush()
        {
            if (active) return;
            active = true;
            lastAngleRoll = lastAnglePitch = lastAngleYaw = 0;
            lastMovedPerTickRoll = lastMovedPerTickPitch = lastMovedPerTickYaw = 0;
            modRoll = modPitch = modYaw = 0;
            ticksOnTarget = 0;
        }

        private void CalculateAxisSpecificData(double now, ref double prior, ref double lastMovedPerTick,
            ref double mod, out bool onTarget, out bool braking)
        {
            onTarget = false;
            double radMovedPerTick = Math.Abs(prior - now);
            double ticksToTarget = radMovedPerTick > 0 ? Math.Abs(now) / radMovedPerTick : double.PositiveInfinity;
            double initVel = radMovedPerTick;
            double rateOfDecel = Math.Abs(lastMovedPerTick - radMovedPerTick);
            if (rateOfDecel > mod) mod = rateOfDecel;
            double ticksToStop = rateOfDecel > 0 ? initVel / rateOfDecel : double.PositiveInfinity;
            bool closing = Math.Abs(now) < Math.Abs(prior);

            if (!closing)
            {
                lastMovedPerTick = 0.0001;
                mod = double.Epsilon;
            }
            else lastMovedPerTick = radMovedPerTick;

            braking = closing && ticksToStop > ticksToTarget;
            if (Math.Abs(now) < errorThreshold)
            {
                braking = true;
                if (radMovedPerTick < minVelThreshold) onTarget = true;
            }
            prior = now;
        }

        public void Apply(Vector3D desiredForward, Sandbox.ModAPI.IMyGyro gyro, MatrixD refMatrix)
        {
            if (gyro == null || gyro.Closed || !gyro.IsFunctional || desiredForward.LengthSquared() < 1e-10) return;
            Flush();

            double pitch, yaw, roll;
            GetRotationAnglesSimultaneous(desiredForward, refMatrix, out yaw, out pitch, out roll);

            bool yawOnTarget, pitchOnTarget, rollOnTarget;
            bool yawBraking, pitchBraking, rollBraking;
            CalculateAxisSpecificData(roll, ref lastAngleRoll, ref lastMovedPerTickRoll, ref modRoll, out rollOnTarget, out rollBraking);
            CalculateAxisSpecificData(pitch, ref lastAnglePitch, ref lastMovedPerTickPitch, ref modPitch, out pitchOnTarget, out pitchBraking);
            CalculateAxisSpecificData(yaw, ref lastAngleYaw, ref lastMovedPerTickYaw, ref modYaw, out yawOnTarget, out yawBraking);

            Vector3D impulse = new Vector3D(pitchBraking ? 0 : pitch, yawBraking ? 0 : yaw, rollBraking ? 0 : roll);
            if (impulse.LengthSquared() > 0)
            {
                double absMax = AbsMax(impulse);
                if (absMax > 0)
                {
                    double magnitude = absMax > ampThreshold ? gyroMaxRpm : (absMax / ampThreshold) * gyroMaxRpm;
                    impulse = impulse / absMax * magnitude;
                }
            }

            ApplyGyroOverride(impulse.X, impulse.Y, impulse.Z, gyro, refMatrix);

            if (yawOnTarget && pitchOnTarget && rollOnTarget) ticksOnTarget++;
            else ticksOnTarget = 0;

            if (ticksOnTarget > MinTicksOnTarget)
            {
                try { gyro.Pitch = 0; gyro.Yaw = 0; gyro.Roll = 0; } catch { }
            }
        }

        private static void GetRotationAnglesSimultaneous(Vector3D desiredForwardVector, MatrixD worldMatrix,
            out double yaw, out double pitch, out double roll)
        {
            desiredForwardVector = SafeNormalize(desiredForwardVector);
            MatrixD transposed = MatrixD.Transpose(worldMatrix);
            desiredForwardVector = Vector3D.TransformNormal(desiredForwardVector, transposed);
            Vector3D axis = new Vector3D(desiredForwardVector.Y, -desiredForwardVector.X, 0);
            double angle = Math.Acos(Clamp(-desiredForwardVector.Z, -1.0, 1.0));
            if (axis.LengthSquared() < 1e-12)
            {
                angle = desiredForwardVector.Z < 0 ? 0 : Math.PI;
                yaw = angle;
                pitch = 0;
                roll = 0;
                return;
            }
            axis = SafeNormalize(axis);
            yaw = -axis.Y * angle;
            pitch = axis.X * angle;
            roll = -axis.Z * angle;
        }

        private static void ApplyGyroOverride(double pitchSpeed, double yawSpeed, double rollSpeed,
            Sandbox.ModAPI.IMyGyro gyro, MatrixD refMatrix)
        {
            try
            {
                Vector3D rotationVec = new Vector3D(-pitchSpeed, yawSpeed, rollSpeed);
                Vector3D relativeRotationVec = Vector3D.TransformNormal(rotationVec, refMatrix);
                Vector3D transformedRotationVec = Vector3D.TransformNormal(relativeRotationVec, MatrixD.Transpose(gyro.WorldMatrix));
                gyro.Pitch = (float)transformedRotationVec.X;
                gyro.Yaw = (float)transformedRotationVec.Y;
                gyro.Roll = (float)transformedRotationVec.Z;
                gyro.GyroOverride = true;
            }
            catch { }
        }

        private static Vector3D SafeNormalize(Vector3D v)
        {
            double len = v.Length();
            return len < 1e-8 ? Vector3D.Zero : v / len;
        }

        private static double AbsMax(Vector3D v)
        {
            return Math.Max(Math.Abs(v.X), Math.Max(Math.Abs(v.Y), Math.Abs(v.Z)));
        }

        private static double DegToRad(double deg) { return deg * Math.PI / 180.0; }
        private static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
    }

}
