using System;
using System.Reflection;
using VRageMath;
using ZeoNav;
using Connector=Sandbox.ModAPI.IMyShipConnector;
using PortStatus=Sandbox.ModAPI.Ingame.MyShipConnectorStatus;
internal static partial class Tests
{
    private static void DockReferenceTests()
    {
        var root=FixtureProxy.Make<VRage.Game.ModAPI.IMyCubeGrid>(c=>c.MethodName=="get_EntityId"?101L:FixtureProxy.Default(c));
        var sub=FixtureProxy.Make<VRage.Game.ModAPI.IMyCubeGrid>(c=>c.MethodName=="get_EntityId"?202L:FixtureProxy.Default(c));
        var grids=ShipContext.NormalizeConstructGrids(new[]{root,root,sub,sub,null});
        Check("Mechanical group append cannot duplicate root or subgrids",grids.Count==2&&grids[0].EntityId==101&&grids[1].EntityId==202);
        Check("Duplicate API entries do not masquerade as docking topology change",DockingMath.SameConstruct(new[]{101L},new[]{101L,101L}));
        Check("API ordering does not change docking topology",DockingMath.SameConstruct(new[]{101L,202L},new[]{202L,101L}));
        Check("Detached subgrid still stops docking",!DockingMath.SameConstruct(new[]{101L,202L},new[]{101L}));
        Check("Newly attached subgrid still stops docking",!DockingMath.SameConstruct(new[]{101L},new[]{101L,202L}));
        var prep=new DockPreparation();prep.Reset(10);
        Check("Dock preparation ignores the sample taken before control acquisition",!prep.Observe(true,10,true,0,1));
        Check("Dock preparation tolerates temporary dampener output",!prep.Observe(true,11,false,0,1.1)&&!prep.Failed);
        Check("Dock baseline requires distinct quiet samples",!prep.Observe(true,12,true,0,2)&&!prep.Observe(true,12,true,0,2.1));
        Check("Three fresh quiet samples complete preparation",!prep.Observe(true,13,true,0,3)&&prep.Observe(true,14,true,0,4));
        prep.Reset();prep.Observe(false,1,true,0,12);
        Check("Missing Spectrum times out with specific cause",prep.Failed&&prep.Status.Contains("Spectrum"));
        prep.Reset();prep.Observe(true,1,false,0,12);
        Check("Persistent thruster output times out with specific cause",prep.Failed&&prep.Status.Contains("thruster"));
        prep.Reset();prep.Observe(true,1,true,.31,1);
        Check("Quiet acquisition cannot continue during excessive drift",prep.Failed);
        prep.Reset();prep.Observe(true,1,true,0,1);prep.Observe(false,1,true,0,2);
        Check("Stale SIG resets quiet acquisition",!prep.Observe(true,2,true,0,3)&&!prep.Observe(true,3,true,0,4));

        var native=typeof(Sandbox.Game.Entities.Cube.MyShipConnector);
        Check("Installed connector exposes exact constraint point",native.GetMethod("ConstraintPositionWorld",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.ReturnType==typeof(Vector3D));
        Check("Installed connector exposes dummy orientation",native.GetField("m_connectorDummyLocal",BindingFlags.NonPublic|BindingFlags.Instance)?.FieldType==typeof(Matrix));
        int count=0;
        foreach(var axis in new[]{Vector3D.Forward,Vector3D.Backward,Vector3D.Left,Vector3D.Right,Vector3D.Up,Vector3D.Down})
        {
            var up=Math.Abs(Vector3D.Dot(axis,Vector3D.Up))>.9?Vector3D.Forward:Vector3D.Up;
            var modelCenter=new Vector3D(1234567,2000,-987654);
            var dummy=MatrixD.CreateWorld(modelCenter+axis*1.2,axis,up);
            // Modded dummy and actual constraint differ; generic block Forward is wrong.
            var constraint=modelCenter+axis*1.1;
            var frame=DockingMath.FaceFrame(constraint,dummy,modelCenter,MatrixD.Identity);
            Check("Model face independent of block orientation "+(++count),Vector3D.Dot(frame.Forward,axis)>.999999&&frame.Translation==constraint&&Math.Abs(Vector3D.Dot(frame.Up,axis))<1e-9);
            Check("Aligned connector enters final without unnecessary retreat "+count,DockingMath.FinalReady(constraint+axis*2,constraint,axis,.1,0,30));
            Check("Wrong side or lateral mismatch cannot skip staging "+count,!DockingMath.FinalReady(constraint-axis,constraint,axis,.1,0,30)&&!DockingMath.FinalReady(constraint+axis*2+up,constraint,axis,.1,0,30));
            var position=axis*10+up*.4;var velocity=-axis*4;bool bounded=true;double minimum=10;
            for(int i=0;i<12000;i++)
            {
                var acceleration=DockingMath.ApproachAcceleration(-position,velocity,axis,1,.25,true);
                bounded&=acceleration.Length()<=1.0000001;velocity+=acceleration/60;position+=velocity/60;
                minimum=Math.Min(minimum,Vector3D.Dot(position,axis));
            }
            Check("RCS approach brakes and settles on axis "+count,bounded&&minimum>-.15&&position.Length()<.03&&velocity.Length()<.01);
        }
        var stop=DockingMath.ApproachAcceleration(new Vector3D(0,0,-1),new Vector3D(0,0,-2),Vector3D.Backward,1,.25,true);
        Check("Excess closing speed requests braking immediately",stop.Z>.99);
        var lateral=DockingMath.ApproachAcceleration(new Vector3D(1,0,-3),Vector3D.Zero,Vector3D.Backward,1,.25,true);
        Check("Final approach holds axial closing while lateral error is corrected",lateral.X>0&&Math.Abs(lateral.Z)<1e-9);
        Check("Final approach requires low angular motion",!DockingMath.FinalReady(Vector3D.Backward,Vector3D.Zero,Vector3D.Backward,0,.1,30));

        Connector own=null,target=null,wrong=null;bool otherWrong=false,remoteReady=true;
        own=FixtureProxy.Make<Connector>(c=>c.MethodName=="get_EntityId"?11L:c.MethodName=="get_IsWorking"?true:c.MethodName=="get_Status"?(object)PortStatus.Connectable:c.MethodName=="get_OtherConnector"?(otherWrong?wrong:target):FixtureProxy.Default(c));
        target=FixtureProxy.Make<Connector>(c=>c.MethodName=="get_EntityId"?22L:c.MethodName=="get_IsWorking"?true:c.MethodName=="get_Status"?(object)(remoteReady?PortStatus.Connectable:PortStatus.Unconnected):c.MethodName=="get_OtherConnector"?own:FixtureProxy.Default(c));
        wrong=FixtureProxy.Make<Connector>(c=>c.MethodName=="get_EntityId"?33L:FixtureProxy.Default(c));
        Check("Only reciprocal selected connector capture permits lock",DockingCapture.PairReady(own,target));
        otherWrong=true;Check("Attraction to another port cannot be locked",!DockingCapture.PairReady(own,target));
        otherWrong=false;remoteReady=false;Check("One-sided capture waits for server state",!DockingCapture.PairReady(own,target));
    }
}
