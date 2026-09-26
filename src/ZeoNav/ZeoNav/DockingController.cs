using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using Connector=Sandbox.ModAPI.IMyShipConnector;
using ConnectorStatus=Sandbox.ModAPI.Ingame.MyShipConnectorStatus;

namespace ZeoNav
{
    internal sealed class DockingController
    {
        [DataContract] internal sealed class Preferences { [DataMember] public Dictionary<string,long> Connectors=new Dictionary<string,long>(); }
        internal sealed class Port { internal Connector Block; internal string Name; internal double Distance; }
        internal readonly List<Port> OwnPorts=new List<Port>(),Targets=new List<Port>();
        internal long OwnId,TargetId;
        internal bool ManualTarget,ManualOwn;
        internal void SelectTarget(long id){if(!Active){TargetId=id;ManualTarget=id!=0;}}
        internal string Status="Press AUTO DOCK or your docking hotkey to scan and dock to the nearest usable station port.";
        internal bool Active {get;private set;}
        internal string Destination {get{return target==null?"Dock":target.CubeGrid.DisplayName+" / "+target.CustomName;}}
        internal double Distance {get{return own==null||target==null?0:Vector3D.Distance(own.GetPosition(),target.GetPosition());}}
        internal DockStage Stage {get;private set;}
        internal double SpeedLimitMps {get;private set;}
        private readonly DockMouseControl mouseControl;
        internal void RecoverMouse(ShipContext s){if(!Active)mouseControl.Recover(s?.Controller);}
        private readonly Func<ShipContext> ship;
        private readonly Func<NavConfig> config;
        private readonly Func<SpectrumAdapter> spectrum;
        private SignalBudget budget;
        private int topology;
        private readonly Action<string> log;
        private readonly string preferencesPath;
        private Preferences preferences;
        private ShipContext controlled;
        private Connector own,target;
        private MatrixD targetPose;
        private MatrixD targetBlockPose;
        private DockStage preparedStage;
        private readonly DockPreparation preparation=new DockPreparation();
        private DateTime lockReadySince;
        private MatrixD clearingAttitude;
        private readonly List<IMyCubeGrid> hullGrids=new List<IMyCubeGrid>();
        private readonly Dictionary<long,MatrixD> hullPoses=new Dictionary<long,MatrixD>();
        private readonly List<IMyMotorStator> fixedRotors=new List<IMyMotorStator>();
        private readonly List<IMyPistonBase> fixedPistons=new List<IMyPistonBase>();
        private Vector3D targetFace;
        private Vector3D hold;
        private Vector3D preparationPosition;
        private bool capturing;
        private DateTime captureStarted;
        private double radius,rotationRadius,standOff,authority;
        private DateTime started,nextCheck,nextConnect,nextTelemetry;
        private int stable;
        internal Action OnDocked;
        internal DockingController(Func<ShipContext> ship,Func<NavConfig> config,Func<SpectrumAdapter> spectrum,Action<string> log,string path)
        {this.ship=ship;this.config=config;this.spectrum=spectrum;this.log=log;preferencesPath=path;mouseControl=new DockMouseControl(System.IO.Path.ChangeExtension(path,"mouse-recovery.json"));preferences=JsonIo.Load<Preferences>(path)??new Preferences();if(preferences.Connectors==null)preferences.Connectors=new Dictionary<string,long>();}
        private string Key(ShipContext s) {return MyAPIGateway.Multiplayer.ServerId+"|"+MyAPIGateway.Session.Name+"|"+s.Grid.EntityId;}
        private bool Access(Connector c) {return c!=null&&!c.Closed&&c.IsWorking&&c.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId);}
        internal sealed class PortScanReport
        {
            internal int Seen,Unavailable,NoAccess,Occupied,Outside,Accepted;
            internal bool Consider(Connector c,bool accessible,double distance,double range)
            {
                if(c==null)return false;
                Seen++;
                if(c.Closed||!c.IsWorking){Unavailable++;return false;}
                if(!accessible){NoAccess++;return false;}
                if(c.Status==ConnectorStatus.Connected){Occupied++;return false;}
                if(distance>range){Outside++;return false;}
                Accepted++;return true;
            }
            public override string ToString(){return "seen="+Seen+" ready="+Accepted+" offline="+Unavailable+" noAccess="+NoAccess+" occupied="+Occupied+" outOfRange="+Outside;}
            internal string Rejection(){return Seen==0?"no connectors found":Unavailable+" offline, "+NoAccess+" without access, "+Occupied+" occupied, "+Outside+" outside range";}
        }
        private bool scanFailed;
        internal void SelectOwn(long id)
        {
            if(Active)return;
            if(id!=0&&!OwnPorts.Any(p=>p.Block.EntityId==id))return;
            OwnId=id;ManualOwn=id!=0;var s=ship();if(s!=null){try{if(id==0)preferences.Connectors.Remove(Key(s));else preferences.Connectors[Key(s)]=id;JsonIo.Save(preferencesPath,preferences);}catch(Exception ex){Status="Connector selected; could not save preference: "+ex.Message;}}
        }
        internal void Scan()
        {scanFailed=false;try{ScanCore();}catch(Exception ex){scanFailed=true;OwnPorts.Clear();Targets.Clear();Status="Port scan failed: "+ex.Message;log(Status);}}
        private void ScanCore()
        {
            if(Active){Status="Docking active; abort before changing ports.";return;}
            OwnPorts.Clear();Targets.Clear();var s=ship();
            if(s==null){Status="Control your ship from a cockpit first.";return;}
            s.Scan();var slim=new List<IMySlimBlock>();
            foreach(var member in s.GetMechanicalConstructGrids()){var parts=new List<IMySlimBlock>();member.GetBlocks(parts);slim.AddRange(parts);}
            var ownReport=new PortScanReport();var stationReport=new PortScanReport();int mobileGrids=0,stationGrids=0;
            foreach(var b in slim){var c=b.FatBlock as Connector;if(ownReport.Consider(c,Access(c),0,double.MaxValue))OwnPorts.Add(new Port{Block=c,Name=c.CustomName});}
            long saved;if(preferences.Connectors.TryGetValue(Key(s),out saved)){OwnId=saved;ManualOwn=true;}
            if(!OwnPorts.Any(p=>p.Block.EntityId==OwnId)){OwnId=0;ManualOwn=false;}
            var area=new BoundingSphereD(s.Position,config().DockScanMeters);
            foreach(var entity in MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref area))
            {
                var grid=entity as IMyCubeGrid;
                if(grid==null||grid.Closed||s.ContainsConstructGridId(grid.EntityId))continue;
                if(!grid.IsStatic){mobileGrids++;continue;}
                stationGrids++;
                slim.Clear();grid.GetBlocks(slim);
                foreach(var b in slim)
                {
                    var c=b.FatBlock as Connector;
                    if(c==null)continue;
                    double distance=Vector3D.Distance(c.GetPosition(),s.Position);
                    if(stationReport.Consider(c,Access(c),distance,config().DockScanMeters))Targets.Add(new Port{Block=c,Name=grid.DisplayName+" / "+c.CustomName,Distance=distance});
                }
            }
            Targets.Sort((a,b)=>a.Distance.CompareTo(b.Distance));
            if(!Targets.Any(p=>p.Block.EntityId==TargetId)){TargetId=0;ManualTarget=false;}
            Status=OwnPorts.Count==0?"No usable ship connector: "+ownReport.Rejection()+".":Targets.Count==0?
                "No station port within "+config().DockScanMeters.ToString("0")+" m: "+(stationGrids==0?"no static stations detected ("+mobileGrids+" mobile grids ignored)":stationReport.Rejection())+".":
                Targets.Count+" nearby ports. AUTO DOCK chooses the closest usable port.";
            log("DOCK SCAN // range="+config().DockScanMeters.ToString("0")+"m constructGrids="+s.ConstructGridCount+" stationGrids="+stationGrids+" mobileGrids="+mobileGrids+" own["+ownReport+"] station["+stationReport+"] // "+Status);
        }
        // Re-rank every press using connector positions; list order and prior automatic
        // targets cannot pin a ship to an older, farther destination.
        internal static Tuple<Port,Port> PickNearest(IEnumerable<Port> ownPorts,IEnumerable<Port> targets,long ownPreference,long explicitTarget,Func<Connector,Connector,bool> eligible)
        {
            var pairs=from a in ownPorts
                      where ownPreference==0||a.Block.EntityId==ownPreference
                      from b in targets
                      where explicitTarget==0||b.Block.EntityId==explicitTarget
                      orderby Vector3D.DistanceSquared(a.Block.GetPosition(),b.Block.GetPosition()),b.Block.EntityId,a.Block.EntityId
                      select Tuple.Create(a,b);
            foreach(var pair in pairs)
            {
                var a=pair.Item1.Block;var b=pair.Item2.Block;
                if(a==null||b==null||a.Closed||b.Closed||!a.IsWorking||!b.IsWorking||b.CubeGrid==null||!b.CubeGrid.IsStatic||a.CubeGrid==b.CubeGrid)continue;
                if(a.Status==ConnectorStatus.Connected||b.Status==ConnectorStatus.Connected)continue;
                if(a.Status==ConnectorStatus.Connectable&&a.OtherConnector?.EntityId!=b.EntityId)continue;
                if(b.Status==ConnectorStatus.Connectable&&b.OtherConnector?.EntityId!=a.EntityId)continue;
                if(eligible(a,b))return pair;
            }
            return null;
        }
        internal void Start(bool quick)
        {
            if(Active){Abort("Quick Dock cancelled.");return;}
            Scan();
            var s=ship();if(s==null)return;
            if(scanFailed||OwnPorts.Count==0||Targets.Count==0)return;
            int approachRejected=0;double bestMargin=double.NegativeInfinity;string bestPort="";
            var pair=PickNearest(OwnPorts,Targets,ManualOwn?OwnId:0,!quick&&ManualTarget?TargetId:0,
                (a,b)=>{
                    if(!Access(a)||!Access(b))return false;
                    // A nearby ship may need to back away before it can rotate. Requiring
                    // a whole-ship radius here prevented that clearance maneuver entirely.
                    double margin=Vector3D.Dot(DockingMath.PortPosition(a)-DockingMath.PortPosition(b),DockingMath.PortFrame(b).Forward);
                    if(margin>=0)return true;
                    approachRejected++;if(margin>bestMargin){bestMargin=margin;bestPort=b.CustomName;}
                    return false;
                });
            if(pair==null){Status=approachRejected>0?"Ports found, but insufficient room in front. Move at least "+Math.Ceiling(-bestMargin).ToString("0")+" m outward from "+bestPort+" and retry.":"Ports found, but the selected pair is occupied, attracting another connector, or unavailable. Rescan and retry.";log("DOCK PAIR REJECTED // approachRejected="+approachRejected+" ownPreference="+(ManualOwn?OwnId:0)+" targetPreference="+(!quick&&ManualTarget?TargetId:0)+" // "+Status);return;}
            own=pair.Item1.Block;target=pair.Item2.Block;OwnId=own.EntityId;TargetId=target.EntityId;
            if(quick)ManualTarget=false;
            if(!s.Motion.Ready){Status="Wait for verified world velocity before docking.";return;}
            if(!Access(own)||!Access(target)||own.Status==ConnectorStatus.Connected||target.Status==ConnectorStatus.Connected){Status="Port unavailable or ship already docked.";return;}
            if((own.Status==ConnectorStatus.Connectable&&own.OtherConnector?.EntityId!=target.EntityId)||
               (target.Status==ConnectorStatus.Connectable&&target.OtherConnector?.EntityId!=own.EntityId))
            {Status="A connector is already attracting a different ship/port.";return;}
            if(s.Grid.IsStatic||s.Gyros.Count==0||s.Velocity.Length()>2||s.Gravity.Length()>.05){Status="Docking requires a mobile ship, navigation gyros, space and speed below 2 m/s.";return;}
            hullGrids.Clear();hullGrids.AddRange(s.GetMechanicalConstructGrids());hullPoses.Clear();
            fixedRotors.Clear();fixedPistons.Clear();
            var inverse=MatrixD.Invert(s.Grid.WorldMatrix);
            foreach(var member in hullGrids)
            {
                hullPoses[member.EntityId]=member.WorldMatrix*inverse;
                var blocks=new List<IMySlimBlock>();member.GetBlocks(blocks);
                foreach(var block in blocks){var rotor=block.FatBlock as IMyMotorStator;if(rotor!=null)fixedRotors.Add(rotor);var piston=block.FatBlock as IMyPistonBase;if(piston!=null)fixedPistons.Add(piston);}
            }
            if(!FixedConstruct(s)){Status="Stop and lock moving rotors/pistons before docking.";return;}
            authority=Enumerable.Range(0,6).Min(i=>s.RcsForce((MoveDir)i)/s.Mass);
            if(authority<.05){Status="Need at least 0.05 m/s² available RCS thrust in all six directions.";return;}
            if(s.Velocity.Length()>.15){Status="Slow below 0.15 m/s before docking (current "+s.Velocity.Length().ToString("0.00")+" m/s).";return;}
            budget=null;preparation.Reset(spectrum()?.SampleGeneration??-1);lockReadySince=DateTime.MinValue;preparationPosition=s.Position;
            targetBlockPose=target.WorldMatrix;targetPose=DockingMath.PortFrame(target);targetFace=targetPose.Translation;radius=rotationRadius=0;
            var turnCenter=s.Controller.CenterOfMass;
            if(!DockingMath.Finite(turnCenter))turnCenter=s.Grid.WorldAABB.Center;
            foreach(var member in hullGrids)
            {
                var box=member.WorldAABB;
                radius=Math.Max(radius,Vector3D.Distance(box.Center,s.Grid.WorldAABB.Center)+box.HalfExtents.Length());
                rotationRadius=Math.Max(rotationRadius,Vector3D.Distance(box.Center,turnCenter)+box.HalfExtents.Length());
            }
            standOff=Math.Max(config().DockStandOffMeters,rotationRadius+10);
            controlled=s;hold=s.Grid.WorldAABB.Center;started=DateTime.UtcNow;nextCheck=nextConnect=nextTelemetry=DateTime.MinValue;stable=0;capturing=false;
            Stage=DockStage.Align;
            if(PairReady())Stage=DockStage.Capture;
            else if(ReadyForFinal(s))
            {
                Stage=DockStage.Capture;
                var finalGoal=DockingMath.CenterGoal(hold,DockingMath.PortPosition(own),targetFace,targetPose.Forward,.1);
                if(!ClearPath(hold,finalGoal,true)){controlled=null;Stage=DockStage.Idle;Status="Final docking corridor obstructed; reposition and retry.";return;}
            }
            else if(!ClearPath(hold,hold,false))
            {
                // Preserve the current connector attitude while translating away from
                // the face. Rotate only after the whole construct has clear swing room.
                Stage=DockStage.Clear;clearingAttitude=DockingMath.PortFrame(own);
                var initial=hold;
                double outward=Vector3D.Dot(turnCenter-targetFace,targetPose.Forward);
                double firstMove=Math.Max(10,rotationRadius+10-outward);
                bool found=false;
                // Probe a few progressively farther waypoints. Both the complete
                // translation and full rotation must be clear before taking one.
                foreach(double extra in new[]{0d,15d,30d,60d,120d})
                {
                    var trial=initial+targetPose.Forward*(firstMove+extra);
                    Stage=DockStage.Clear;
                    if(!ClearPath(initial,trial,false,false))continue;
                    Stage=DockStage.Align;
                    bool swingClear=ClearPath(trial,trial,false,false);
                    Stage=DockStage.Clear;
                    if(!swingClear)continue;
                    hold=trial;found=true;
                    log("DOCK CLEAR PLAN // retreat="+(firstMove+extra).ToString("0.0")+"m turnRadius="+rotationRadius.ToString("0.0")+"m standOff="+standOff.ToString("0.0")+"m");
                    break;
                }
                if(!found){controlled=null;Stage=DockStage.Idle;Status="No clear RCS retreat and rotation space from this port. Reposition into open space.";return;}
            }
            preparedStage=Stage;Stage=DockStage.Prepare;SpeedLimitMps=0;
            Active=true;s.SaveDampenersOnce();s.BeginThrustControl();s.RcsOnly=true;s.BeginGyroControl();s.SetDampeners(false);s.SignatureBudget=null;s.ClearThrust();
            try{mouseControl.Acquire(s.Controller);}catch(Exception ex){Abort("Cannot isolate mouse steering: "+ex.Message);return;}
            Status="PREPARING — releasing dampener corrections; waiting for fresh quiet own SIG.";
            log("DOCK START // own="+own.EntityId+" target="+target.EntityId+" grids="+hullGrids.Count+" next="+preparedStage+" ownFace="+DockingMath.PortPosition(own)+" ownForward="+DockingMath.PortFrame(own).Forward+" targetFace="+targetFace+" targetForward="+targetPose.Forward);
        }
        internal void Update()
        {
            if(!Active)return;
            try
            {
                var s=ship();var now=DateTime.UtcNow;
                if(s!=controlled||s==null||!s.Motion.Ready||!Access(own)||!Access(target)||!hullPoses.ContainsKey(own.CubeGrid.EntityId)||!target.CubeGrid.IsStatic){Abort("Docking context or port lost.");return;}
                if(own.Status==ConnectorStatus.Connected){bool correct=own.OtherConnector?.EntityId==target.EntityId;Abort(correct?"DOCKED — connector locked.":"Connected to a different port; controls released.");if(correct)OnDocked?.Invoke();return;}
                if(!FixedConstruct(s)){Abort("Mechanical assembly moved or unlocked; docking released.");return;}
                if(target.Status==ConnectorStatus.Connected||(target.Status==ConnectorStatus.Connectable&&target.OtherConnector?.EntityId!=own.EntityId)||!DockingMath.SamePose(target.WorldMatrix,targetBlockPose)){Abort("Target moved or became occupied.");return;}
                if((now-started).TotalMinutes>30||s.Velocity.Length()>7.5||s.Gravity.Length()>.05){Abort("Docking stopped: time, speed or gravity limit.");return;}
                var c=s.Controller;
                // Mouse belongs to camera/UI while docking; keyboard takeover is immediate.
                if(DockMouseControl.Takeover(c.MoveIndicator,c.RollIndicator))
                {log("DOCK INPUT // move="+c.MoveIndicator+" roll="+c.RollIndicator);Abort("Manual movement/roll input: docking released.");return;}
                mouseControl.Maintain();
                if(Stage==DockStage.Prepare)
                {
                    if(Vector3D.DistanceSquared(s.Position,preparationPosition)>.25){Abort("Dock preparation stopped: ship drifted over 0.5 m. Stop and retry.");return;}
                    s.SetDampeners(false);s.ClearThrust();s.ApplyDockRotation(Vector3D.Zero);
                    var feed=spectrum();bool fresh=feed!=null&&feed.DriveKmReady;
                    bool ready=preparation.Observe(fresh,fresh?feed.SampleGeneration:-1,s.ThrustersQuiet,s.Velocity.Length(),(now-started).TotalSeconds);
                    Status=preparation.Status;
                    if(preparation.Failed){Abort(Status);return;}
                    if(!ready)return;
                    s.RefreshWorkingState();s.RefreshSignatureTopology();budget=feed.BuildBudget(s,true);budget.TargetKm=ApproachProfile.Arrival(config());
                    budget.SphericalBaseSquared=feed.SphericalWeakKm*feed.SphericalWeakKm;budget.DirectionalBaseSquared=feed.DirectionalWeakKm*feed.DirectionalWeakKm;budget.Ready=true;
                    authority=Enumerable.Range(0,6).Min(i=>s.RcsForce((MoveDir)i)/s.Mass*budget.Limit(i,1,new double[6]))*.5;
                    if(feed.DriveKm>=budget.TargetKm*.99||authority<.01){Abort("Insufficient RCS authority within MAX SIG; raise the limit or reduce idle emissions.");return;}
                    s.SignatureBudget=budget;topology=s.TopologyRevision;Stage=preparedStage;
                    Status=Stage+" — RCS docking to "+Destination;log("DOCK PREPARED // stage="+Stage+" speed="+s.Velocity.Length()+" ownSig="+feed.DriveKm+" maxSig="+budget.TargetKm+" authority="+authority);
                }
                var velocities=c.GetShipVelocities();
                var desired=Stage==DockStage.Clear?clearingAttitude:MatrixD.CreateWorld(Vector3D.Zero,-targetPose.Forward,targetPose.Up);
                double angle;var rate=DockingMath.RotationRate(DockingMath.PortFrame(own),desired,velocities.AngularVelocity,out angle);
                if(!DockingMath.Finite(rate)){Abort("Invalid docking attitude.");return;}
                Vector3D goal;
                if(Stage==DockStage.Align||Stage==DockStage.Clear)goal=hold;
                else goal=DockingMath.CenterGoal(s.Grid.WorldAABB.Center,DockingMath.PortPosition(own),targetFace,targetPose.Forward,Stage==DockStage.Capture?.1:standOff);
                if(own.Status==ConnectorStatus.Connectable)
                {
                    if(own.OtherConnector?.EntityId!=target.EntityId){Abort("Different connector in capture range; approach stopped.");return;}
                    if(!capturing){capturing=true;captureStarted=now;lockReadySince=DateTime.MinValue;log("DOCK CAPTURE // selected pair; translation released");}
                }
                if(capturing)
                {
                    SpeedLimitMps=0;
                    // Let the magnetic constraint settle instead of counter-thrusting its pull.
                    s.ClearThrust();s.ApplyDockRotation(Vector3D.Zero);
                    if((now-captureStarted).TotalSeconds>10){Abort("Connector lock timed out; controls released.");return;}
                    if(own.Status!=ConnectorStatus.Connectable){Abort("Connector capture lost; approach stopped. Retry Auto Dock.");return;}
                    if(PairReady())
                    {
                        if(lockReadySince==DateTime.MinValue)lockReadySince=now;
                        if((now-lockReadySince).TotalSeconds>=1.35&&now>=nextConnect){own.Connect();nextConnect=now.AddSeconds(1);log("DOCK CONNECT REQUEST // own="+own.EntityId+" target="+target.EntityId);}
                    }
                    else lockReadySince=DateTime.MinValue;
                    Status="CAPTURE — thrust off; waiting for the selected connectors to lock.";
                    return;
                }
                var error=goal-s.Grid.WorldAABB.Center;
                if(now>=nextCheck)
                {
                    nextCheck=now.AddSeconds(.25);s.RefreshWorkingState();
                    var currentGrids=s.GetMechanicalConstructGrids().Select(g=>g.EntityId).ToArray();
                    if(!DockingMath.SameConstruct(hullPoses.Keys,currentGrids)){log("DOCK TOPOLOGY // expected="+string.Join(",",hullPoses.Keys.OrderBy(id=>id))+" actual="+string.Join(",",currentGrids.OrderBy(id=>id)));Abort("Ship mechanical topology changed; docking released.");return;}
                    s.RefreshSignatureTopology();
                    var feed=spectrum();budget.TargetKm=ApproachProfile.Arrival(config());
                    if(!feed.DriveKmReady||feed.DriveKm>budget.TargetKm*.99||s.TopologyRevision!=topology){Abort("Docking stopped: own signal, MAX SIG or drive availability changed.");return;}
                    authority=Enumerable.Range(0,6).Min(i=>s.RcsForce((MoveDir)i)/s.Mass*budget.Limit(i,1,new double[6]))*.5;
                    if(authority<.01||s.Gyros.Count==0){Abort("Docking maneuvering authority lost within MAX SIG.");return;}
                    if(!ClearPath(s.Grid.WorldAABB.Center,goal,Stage==DockStage.Capture)){Abort("Docking corridor obstructed; approach stopped.");return;}
                }
                // A departure from alignment stops translation; never slide sideways into a port.
                if(Stage!=DockStage.Align&&angle>5){Abort("Connector alignment lost; reposition and retry.");return;}
                var faceDelta=DockingMath.PortPosition(own)-targetFace;
                SpeedLimitMps=DockingMath.SpeedLimit(Stage,Vector3D.Dot(faceDelta,targetPose.Forward),DockingMath.Lateral(faceDelta,targetPose.Forward),authority*.5,config().DockTransitMps,config().DockApproachMps);
                double speed=SpeedLimitMps;
                var acceleration=DockingMath.ApproachAcceleration(error,s.Velocity,targetPose.Forward,authority*.5,speed,Stage==DockStage.Capture);
                s.BeginThrustFrame();s.ApplyWorldAcceleration(acceleration,1);s.ApplyDockRotation(rate);s.CommitThrustFrame();
                if(now>=nextTelemetry)
                {
                    nextTelemetry=now.AddSeconds(2);
                    log("DOCK CONTROL // stage="+Stage+" error="+error.Length().ToString("0.000")+"m lateral="+DockingMath.Lateral(DockingMath.PortPosition(own)-targetFace,targetPose.Forward).ToString("0.000")+"m angle="+angle.ToString("0.000")+"deg speed="+s.Velocity.Length().ToString("0.000")+"m/s limit="+SpeedLimitMps.ToString("0.000")+"m/s authority="+authority.ToString("0.000")+" own="+own.Status+" target="+target.Status);
                }
                bool settled=angle<.5&&velocities.AngularVelocity.Length()<.15*Math.PI/180&&s.Velocity.Length()<.15;
                stable=settled?stable+1:0;
                if(Stage==DockStage.Clear&&error.Length()<.5&&stable>=30)
                {Stage=DockStage.Align;hold=s.Grid.WorldAABB.Center;stable=0;if(!ClearPath(hold,hold,false)){Abort("Still no rotation clearance; reposition farther from structures.");return;}Status="ALIGN — matching the selected connector's facing and roll.";}
                else if(Stage==DockStage.Align&&stable>=30){Stage=DockStage.Approach;stable=0;Status="APPROACH — moving to the port's staging point.";}
                else if(Stage==DockStage.Approach&&error.Length()<.5&&stable>=30){Stage=DockStage.Capture;stable=0;Status="FINAL — RCS approach; slowing for connector capture.";}
                else if(Stage==DockStage.Capture)
                {
                    if(DockingMath.Lateral(DockingMath.PortPosition(own)-targetFace,targetPose.Forward)>.75){Abort("Lateral docking error; final approach stopped.");return;}
                    // Never drive block centers through each other if capture is unavailable.
                    double gap=Vector3D.Dot(DockingMath.PortPosition(own)-targetFace,targetPose.Forward);
                    if((gap<.05||error.Length()<.05)&&own.Status!=ConnectorStatus.Connectable){Abort("Connector did not become connectable; check clearance and connector compatibility.");return;}
                }
            }
            catch(Exception ex){Abort("Docking error: "+ex.Message);log("DOCK ERROR // "+ex);}
        }
        private bool PairReady(){return DockingCapture.PairReady(own,target);}
        private bool ReadyForFinal(ShipContext s)
        {
            double angle;DockingMath.RotationRate(DockingMath.PortFrame(own),MatrixD.CreateWorld(Vector3D.Zero,-targetPose.Forward,targetPose.Up),Vector3D.Zero,out angle);
            return DockingMath.FinalReady(DockingMath.PortPosition(own),targetFace,targetPose.Forward,angle,s.Controller.GetShipVelocities().AngularVelocity.Length(),standOff);
        }
        // Conservative occupied-block envelopes, not a pathfinder. Reject a blocked route.
        private bool FixedConstruct(ShipContext s)
        {
            var inverse=MatrixD.Invert(s.Grid.WorldMatrix);
            foreach(var member in hullGrids)
            {
                if(member.Closed||!DockingMath.SamePose(member.WorldMatrix*inverse,hullPoses[member.EntityId]))return false;
            }
            foreach(var rotor in fixedRotors)if(rotor.Closed||(rotor.IsAttached&&!rotor.RotorLock))return false;
            foreach(var piston in fixedPistons)if(piston.Closed||(piston.IsAttached&&Math.Abs(piston.Velocity)>.0001))return false;
            return true;
        }
        private bool ClearPath(Vector3D from,Vector3D to,bool final,bool report=true)
        {
            var s=controlled;if(s==null)return false;
            bool turning=Stage==DockStage.Align;
            Vector3D delta=to-from;
            var center=s.Controller.CenterOfMass;
            if(!DockingMath.Finite(center))center=s.Grid.WorldAABB.Center;
            center+=from-s.Grid.WorldAABB.Center;
            var area=new BoundingSphereD(turning?center:(from+to)*.5,(turning?rotationRadius:radius)+delta.Length()*.5+2);
            var hull=new List<IMySlimBlock>();
            if(hullGrids.Count==0)s.Grid.GetBlocks(hull);else foreach(var member in hullGrids){var parts=new List<IMySlimBlock>();member.GetBlocks(parts);hull.AddRange(parts);}
            var sweep=new List<Tuple<IMySlimBlock,BoundingBoxD,DockBox>>();
            foreach(var b in hull)
            {
                BoundingBoxD box;b.GetWorldBoundingBox(out box,true);
                var oriented=DockBox.Block(b,box);
                box=new BoundingBoxD(Vector3D.Min(box.Min,box.Min+delta)-.1,Vector3D.Max(box.Max,box.Max+delta)+.1);
                sweep.Add(Tuple.Create(b,box,oriented));
            }
            var corridor=BoundingBoxD.CreateInvalid();foreach(var part in sweep)corridor.Include(part.Item2);
            int comparisons=0;
            foreach(var entity in MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref area))
            {
                var grid=entity as IMyCubeGrid;
                if(grid!=null&&s.ContainsConstructGridId(grid.EntityId))continue;
                if(grid==null)
                {
                    // Non-physical marker/helper entities cannot collide with the ship.
                    if(entity.Closed||(entity.Physics==null&&!(entity is Sandbox.Game.Entities.MyVoxelBase)))continue;
                    var box=entity.WorldAABB;
                    if(turning&&box.Intersects(new BoundingSphereD(center,rotationRadius+1)))return Blocked("rotation entity="+entity.EntityId+" type="+entity.GetType().Name,report);
                    if(!turning&&corridor.Intersects(box))foreach(var part in sweep)
                    {
                        if(++comparisons>2000000)return Blocked("clearance work limit",report);
                        if(part.Item2.Intersects(box)&&DockBox.Sweep(part.Item3,DockBox.World(box),delta))return Blocked("path entity="+entity.EntityId+" type="+entity.GetType().Name,report);
                    }
                    continue;
                }
                foreach(var obstacle in grid.GetBlocksInsideSphere(ref area))
                {
                    BoundingBoxD box;obstacle.GetWorldBoundingBox(out box,true);
                    if(turning)
                    {
                        var rotationSpace=new BoundingSphereD(center,rotationRadius+1);
                        if(box.Intersects(rotationSpace))return Blocked("rotation grid="+grid.EntityId+" name="+grid.DisplayName+" block="+obstacle.Position,report);
                        continue;
                    }
                    if(!corridor.Intersects(box))continue;
                    var obstacleBox=DockBox.Block(obstacle,box);
                    foreach(var part in sweep)
                    {
                        if(++comparisons>2000000)return Blocked("clearance work limit",report); // Bound per-update work for huge constructs.
                        // Only the chosen connector pair may enter each other's capture volume.
                        if(final&&part.Item1.FatBlock?.EntityId==own.EntityId&&obstacle.FatBlock?.EntityId==target.EntityId)continue;
                        if(part.Item2.Intersects(box)&&DockBox.Sweep(part.Item3,obstacleBox,delta))return Blocked("path grid="+grid.EntityId+" name="+grid.DisplayName+" block="+obstacle.Position+" ownBlock="+part.Item1.Position+" travel="+delta.Length().ToString("0.00")+"m final="+final,report);
                    }
                }
            }
            return true;
        }
        private bool Blocked(string detail,bool report=true){if(report)log("DOCK BLOCKER // stage="+Stage+" "+detail);return false;}
        internal void Abort(string reason)
        {
            bool owned=Active;Active=false;Stage=DockStage.Idle;SpeedLimitMps=0;
            if(controlled!=null&&owned){try{controlled.ReleaseAll(false);controlled.SetDampeners(true);}catch(Exception ex){log("DOCK RELEASE // "+ex.Message);}}
            try{mouseControl.Release();}catch(Exception ex){log("DOCK MOUSE RESTORE FAILED // "+ex.Message);}
            controlled=null;Status=reason;if(owned)log("DOCK STOP // "+reason);
            if(owned)try{MyAPIGateway.Utilities.ShowNotification(reason,6000,"White");}catch { }
        }
    }
}
