using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using VRageMath;
using ZeoNav;
internal static partial class Tests
{
    private static void DockGeometryTests()
    {
        var axes=new[]{Vector3D.Forward,Vector3D.Backward,Vector3D.Left,Vector3D.Right,Vector3D.Up,Vector3D.Down};
        var target=MatrixD.CreateWorld(new Vector3D(1200000,-800000,3000000),Vector3D.Normalize(new Vector3D(2,3,-4)),Vector3D.Up);
        var desiredConnector=MatrixD.CreateWorld(Vector3D.Zero,-target.Forward,target.Up);
        int orientations=0;
        foreach(var facing in axes)foreach(var up in axes.Where(u=>Math.Abs(Vector3D.Dot(u,facing))<.01))
        {
            var mount=MatrixD.CreateWorld(new Vector3D(13,-9,21),facing,up);
            var shipPose=MatrixD.Invert(mount)*desiredConnector;
            var face=Vector3D.Transform(mount.Translation,shipPose);
            var center=shipPose.Translation;
            var goal=DockingMath.CenterGoal(center,face,target.Translation,target.Forward,.1);
            shipPose.Translation=goal;
            var final=mount*shipPose;
            Check("Connector mount aligns independent of cockpit orientation "+(++orientations),
                Vector3D.Distance(final.Translation,target.Translation+target.Forward*.1)<1e-7&&Vector3D.Dot(final.Forward,target.Forward)<-.999999&&Vector3D.Dot(final.Up,target.Up)>.999999);
        }
        var rearGoal=DockingMath.CenterGoal(Vector3D.Zero,new Vector3D(0,0,8),new Vector3D(0,0,57),Vector3D.Forward,.1);
        Check("Rear connector 57m away requires reversing, not nose-first approach",rearGoal.Z>0&&rearGoal.X==0&&rearGoal.Y==0);
        var sideGoal=DockingMath.CenterGoal(Vector3D.Zero,new Vector3D(8,0,0),new Vector3D(57,0,0),Vector3D.Left,.1);
        Check("Side connector requires lateral RCS translation",sideGoal.X>0&&sideGoal.Z==0);
        var pose=MatrixD.CreateTranslation(4,5,6);var moved=pose;moved.Translation+=new Vector3D(.03,0,0);
        Check("Moving mechanical connector invalidates fixed hull plan",DockingMath.SamePose(pose,pose)&&!DockingMath.SamePose(pose,moved));
        moved=MatrixD.CreateRotationY(.01)*pose;
        Check("Rotating mechanical connector invalidates fixed hull plan",!DockingMath.SamePose(pose,moved));
        Check("Installed game supplies exact connector mating geometry",typeof(Sandbox.Game.Entities.Cube.MyShipConnector).GetProperty("ConnectionPosition",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!=null);

        var controller=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(m=>m.MethodName=="get_WorldMatrix"?MatrixD.Identity:FixtureProxy.Default(m));
        var ship=new ShipContext(controller,_=>{});var rcs=new List<DriveFixture>();
        for(int i=0;i<6;i++){var drive=new DriveFixture(700+i,"Reaction control",10,Vector3D.Backward);rcs.Add(drive);ship.ThrusterBanks[(MoveDir)i].Add(drive.Block);}
        var main=new DriveFixture(800,"Main drive",1000,Vector3D.Backward);ship.ThrusterBanks[MoveDir.Forward].Add(main.Block);
        ship.RefreshWorkingState();ship.BeginThrustControl();ship.RcsOnly=true;
        foreach(var direction in axes)
        {
            ship.BeginThrustFrame();ship.ApplyWorldAcceleration(direction,1);ship.CommitThrustFrame();
            Check("RCS translates on world axis "+direction,rcs.Count(r=>r.Override>0)==1&&main.Override==0);
        }
        ship.SignatureBudget=new SignalBudget{Ready=true,TargetKm=400};
        Check("Strong six-axis RCS permits 50m/s assistance",NavController.StopAssistSpeed(ship)==50);
        ship.SignatureBudget.Add("bank",true,0,1000000);ship.SignatureBudget.TargetKm=50;
        Check("Tight signature budget lowers early stop handoff",NavController.StopAssistSpeed(ship)<50);
        ship.SignatureBudget.Ready=false;
        Check("Stale signature cannot expand RCS handoff",NavController.StopAssistSpeed(ship)==5);
        var cfg=new NavConfig();var nav=new NavController(()=>ship,()=>cfg,()=>null,_=>{});
        var budget=new SignalBudget{Ready=true,TargetKm=750};budget.Add("main bank",true,0,1000000);
        ship.SignatureBudget=budget;NavSet(nav,"signalBudget",budget);
        ship.BeginThrustFrame();NavCall(nav,"FinishLowSpeedStop",ship,new Vector3D(0,0,30));ship.CommitThrustFrame();
        Check("30m/s stop uses RCS without firing main drive under constrained SIG",ship.RcsOnly&&rcs[0].Override>0&&main.Override==0);
        Check("Early RCS stop remains inside shared signature ceiling",budget.PredictedSquared(ship.AppliedCommands)<=Math.Pow(750*.97,2)+.001);
        budget.TargetKm=50;
        ship.BeginThrustFrame();NavCall(nav,"FinishLowSpeedStop",ship,new Vector3D(0,0,4));ship.CommitThrustFrame();
        Check("Insufficient RCS stop authority retains bounded main-drive fallback",!ship.RcsOnly&&main.Override>0);
        ship.EndThrustControl(false);
        FixedDockConstructTests();
    }
    private static void FixedDockConstructTests()
    {
        var subPose=MatrixD.CreateTranslation(10,0,0);bool locked=true;float pistonVelocity=0;
        var root=FixtureProxy.Make<VRage.Game.ModAPI.IMyCubeGrid>(m=>m.MethodName=="get_EntityId"?1L:m.MethodName=="get_WorldMatrix"?(object)MatrixD.Identity:FixtureProxy.Default(m));
        var sub=FixtureProxy.Make<VRage.Game.ModAPI.IMyCubeGrid>(m=>m.MethodName=="get_EntityId"?2L:m.MethodName=="get_WorldMatrix"?(object)subPose:FixtureProxy.Default(m));
        var pilot=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(m=>m.MethodName=="get_CubeGrid"?root:FixtureProxy.Default(m));
        var s=new ShipContext(pilot,_=>{});
        var dock=new DockingController(()=>s,()=>new NavConfig(),()=>null,_=>{},System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid()+".json"));
        Func<string,object> get=name=>typeof(DockingController).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dock);
        ((List<VRage.Game.ModAPI.IMyCubeGrid>)get("hullGrids")).AddRange(new[]{root,sub});
        var poses=(Dictionary<long,MatrixD>)get("hullPoses");poses[1]=MatrixD.Identity;poses[2]=subPose;
        ((List<Sandbox.ModAPI.IMyMotorStator>)get("fixedRotors")).Add(FixtureProxy.Make<Sandbox.ModAPI.IMyMotorStator>(m=>m.MethodName=="get_IsAttached"?true:m.MethodName=="get_RotorLock"?(object)locked:FixtureProxy.Default(m)));
        ((List<Sandbox.ModAPI.IMyPistonBase>)get("fixedPistons")).Add(FixtureProxy.Make<Sandbox.ModAPI.IMyPistonBase>(m=>m.MethodName=="get_IsAttached"?true:m.MethodName=="get_Velocity"?(object)pistonVelocity:FixtureProxy.Default(m)));
        var check=typeof(DockingController).GetMethod("FixedConstruct",BindingFlags.Instance|BindingFlags.NonPublic);
        Func<bool> valid=()=> (bool)check.Invoke(dock,new object[]{s});
        Check("Fixed multi-grid docking accepts locked rotor and stopped piston",valid());
        locked=false;Check("Unlocking rotor stops multi-grid docking",!valid());locked=true;
        pistonVelocity=.1f;Check("Commanding piston movement stops multi-grid docking",!valid());pistonVelocity=0;
        subPose.Translation+=new Vector3D(.03,0,0);Check("Unexpected subgrid drift stops multi-grid docking",!valid());
    }
}
