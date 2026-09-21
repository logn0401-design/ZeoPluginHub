using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using VRage.Game.ModAPI;
using VRageMath;
using ZeoNav;

internal static partial class Tests
{
    private static void ServerTests()
    {
        // Names collected from the installed SDX drive definitions; no name tags required.
        foreach(string subtype in new[]{
            "sdx_driveCivilian3x3_small",
            "sdx_driveCivilian3x3",
            "sdx_driveCivilian5x5",
            "sdx_driveCivilian7x7",
            "sdx_driveIndustrial7x7",
            "sdx_driveIndustrial9x9",
            "sdx_driveMcrnMilitary3x3",
            "sdx_driveMcrnMilitary5x5",
            "sdx_driveMcrnMilitary7x7",
            "sdx_driveOpaMilitary3x3",
            "sdx_driveOpaMilitary5x5",
            "sdx_driveOpaMilitary7x7",
            "sdx_driveTorch3x3_small",
            "sdx_driveUnnMilitary3x3",
            "sdx_driveUnnMilitary5x5",
            "sdx_driveUnnMilitary7x7",
            "sdx_driveCivilian3x3_smallMes",
            "sdx_driveCivilian3x3Mes",
            "sdx_driveCivilian5x5Mes",
            "sdx_driveCivilian7x7Mes",
            "sdx_driveIndustrial7x7Mes",
            "sdx_driveIndustrial9x9Mes",
            "sdx_driveMcrnMilitary3x3Mes",
            "sdx_driveMcrnMilitary5x5Mes",
            "sdx_driveMcrnMilitary7x7Mes",
            "sdx_driveOpaMilitary3x3Mes",
            "sdx_driveOpaMilitary5x5Mes",
            "sdx_driveOpaMilitary7x7Mes",
            "sdx_driveUnnMilitary3x3Mes",
            "sdx_driveUnnMilitary5x5Mes",
            "sdx_driveUnnMilitary7x7Mes"
        }) Check("Definition auto-detection: "+subtype, DriveClassifier.IsMain("MyObjectBuilder_Thrust/"+subtype,"Renamed block",0));
        Check("Other mod Epstein subtype is recognized",DriveClassifier.IsMain("MyObjectBuilder_Thrust/Other_EpsteinDrive","",0));
        Check("Epstein display metadata is recognized",DriveClassifier.IsMain("MyObjectBuilder_Thrust/OtherDrive","Epstein drive",0));
        Check("Reaction control remains separate from main drives",!DriveClassifier.IsMain("MyObjectBuilder_Thrust/sdx_rcsHeavy","Reaction control",3000000));
        Check("Future SDX drive variants need no code update",DriveClassifier.IsMain("MyObjectBuilder_Thrust/sdx_driveNewFamily11x11","",0));
        string source;
        SpeedCapResolver.Dispose();
        Check("AUTO uses reported 50000 m/s server ceiling without ShipCore", SpeedCapResolver.Resolve(null,0,0,out source)==50000 && source.StartsWith("SERVER LIMIT"));
        Check("Overspeed does not raise the AUTO server ceiling", SpeedCapResolver.Resolve(null,0,60000,out source)==50000);
        Check("Explicit lower speed limit survives", SpeedCapResolver.Resolve(null,12000,0,out source)==12000);
        Check("Explicit speed cannot exceed server maximum", SpeedCapResolver.Resolve(null,90000,0,out source)==50000);
        var gate=new AlignmentThrustGate();
        Check("Unaligned ship cannot start a burn",!gate.Allow(.3));
        Check("Burn acquires within quarter degree",gate.Allow(.24));
        bool jitter=true; foreach(double angle in new[]{.26,.29,.25,.4,.27}) jitter &= gate.Allow(angle);
        Check("Small angular jitter does not pulse thrust",jitter);
        Check("Half-degree departure cuts thrust until realigned",!gate.Allow(.51) && !gate.Allow(.3) && gate.Allow(.24));
        Check("Invalid attitude fails closed",!gate.Allow(double.NaN));
        gate.Allow(.2); gate.Reset(); Check("New phase reacquires alignment",!gate.Allow(.3));
        var cache=new ThrustCommandCache();
        Check("First command is published",cache.ShouldSend(1,1,0,0));
        Check("Delayed readback cannot flood repeated full-thrust commands",!cache.ShouldSend(1,1,0,.1));
        Check("Persistent missing acknowledgement gets a bounded retry",cache.ShouldSend(1,1,0,.6));
        Check("Cutoff is sent even when old readback is already zero",cache.ShouldSend(1,0,0,.61));
        Check("Acknowledged steady command needs no retransmission",!cache.ShouldSend(1,0,0,9));

        var blocks=new List<IMySlimBlock>();
        var grid=FixtureProxy.Make<IMyCubeGrid>(call=> {
            if(call.MethodName=="get_EntityId") return 42L;
            if(call.MethodName=="get_GridSizeEnum") return VRage.Game.MyCubeSize.Large;
            if(call.MethodName=="GetBlocks") {
                var output=(List<IMySlimBlock>)call.Args[0];
                var filter=call.ArgCount>1 ? call.Args[1] as Func<IMySlimBlock,bool> : null;
                foreach(var block in blocks) if(filter==null || filter(block)) output.Add(block);
            }
            return FixtureProxy.Default(call);
        });
        var controller=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(call=> {
            if(call.MethodName=="get_CubeGrid") return grid;
            if(call.MethodName=="get_WorldMatrix") return MatrixD.Identity;
            return FixtureProxy.Default(call);
        });
        var canterbury=new DriveFixture(1,"Industrial Canterbury",350000000,Vector3D.Backward);
        var unknown=new DriveFixture(2,"Future drive type",200000000,Vector3D.Backward);
        var rcs=new DriveFixture(3,"RCS",1500000,Vector3D.Backward);
        var reverse=new DriveFixture(4,"Reverse drive",10000000,Vector3D.Forward);
        foreach(var drive in new[]{canterbury,unknown,rcs,reverse}) {
            var captured=drive; blocks.Add(FixtureProxy.Make<IMySlimBlock>(call=>call.MethodName=="get_FatBlock" ? captured.Block : FixtureProxy.Default(call)));
        }
        canterbury.CustomName="RCS is in my pilot label";
        var logs=new List<string>(); var ship=new ShipContext(controller,logs.Add); ship.Scan();
        Check("Renaming a main drive cannot reclassify it as RCS",ship.ForwardMainDriveCount==2 && ship.ForwardWorkingMainDriveCount==2);
        Check("Canterbury and unfamiliar main drives share forward bank",ship.ThrusterBanks[MoveDir.Forward].Any(t=>t.EntityId==1) && ship.ThrusterBanks[MoveDir.Forward].Any(t=>t.EntityId==2));
        Check("Thrust uses acceleration direction rather than exhaust",ship.ThrusterBanks[MoveDir.Backward].Any(t=>t.EntityId==4));
        Check("All working forward types contribute actual effective thrust",ship.Force(MoveDir.Forward)==551500000 && ship.ForwardWorkingThrusterCount==3);
        ship.LogDriveAudit();
        Check("Drive audit reports unavailable versus rated output",logs.Any(s=>s.StartsWith("DRIVE BANK") && s.Contains("effective=")));
        ship.BeginThrustControl(); ship.BeginThrustFrame(); ship.ClearThrust(); ship.SetMove(MoveDir.Forward,1);
        Check("Planning does not publish intermediate zeros",canterbury.Writes.Count==0);
        ship.CommitThrustFrame();
        Check("Canterbury receives only final full-thrust command",canterbury.Writes.Count==1 && canterbury.Writes[0]==1);
        ship.BeginThrustFrame(); ship.ClearThrust(); ship.SetMove(MoveDir.Forward,1); ship.CommitThrustFrame();
        Check("Steady burn does not transmit zero/full each tick",canterbury.Writes.Count==1);
        ship.BeginThrustFrame(); ship.ClearThrust(); ship.CommitThrustFrame();
        Check("Final cutoff is immediate",canterbury.Writes.Count==2 && canterbury.Writes[1]==0);
        ship.BeginThrustFrame(); ship.SetMove(MoveDir.Forward,1); ship.CancelThrustFrame();
        Check("Cancelled frame cannot publish planned thrust",canterbury.Writes.Count==2);
        ship.BeginThrustFrame(); ship.SetMove(MoveDir.Forward,1); ship.EndThrustControl(false); ship.CommitThrustFrame();
        Check("Abort cancels pending thrust and releases override",canterbury.Override==0 && canterbury.Writes[canterbury.Writes.Count-1]==0);
        canterbury.Working=false; unknown.Working=false; ship.RefreshWorkingState();
        Check("Unavailable drives are distinguished from missing drives",ship.ForwardMainDriveCount==2 && ship.ForwardWorkingMainDriveCount==0 && ship.ForwardMainRatedForce==550000000 && ship.ForwardMainEffectiveForce==0 && ship.ForwardWorkingThrusterCount==1 && ship.Force(MoveDir.Forward)==1500000);
        canterbury.Working=true; unknown.Working=true; canterbury.Override=0; unknown.Override=0; rcs.Override=1; ship.RefreshWorkingState();
        Check("RCS acknowledgement cannot mask missing main-drive response",ship.ForwardReadbackRatio<.01);
        ship.BeginThrustControl(); ship.SignatureBudget=new SignalBudget {Ready=false};
        ship.BeginThrustFrame(); ship.ClearThrust(); ship.SetMove(MoveDir.Forward,1); ship.CommitThrustFrame();
        Check("Staged command still obeys stale signature cutoff",ship.ForwardCommandRatio==0 && canterbury.Override==0);
        ship.EndThrustControl(false);
    }
}

internal sealed class DriveFixture
{
    public readonly Sandbox.ModAPI.IMyThrust Block;
    public readonly List<float> Writes=new List<float>();
    public bool Working=true;
    public float Override;
    public string CustomName;
    public DriveFixture(long id,string name,float force,Vector3D exhaust)
    {
        CustomName=name;
        Block=FixtureProxy.Make<Sandbox.ModAPI.IMyThrust>(call=> {
            switch(call.MethodName) {
                case "get_EntityId": return id;
                case "get_CustomName": return CustomName;
                case "get_DefinitionDisplayNameText": return name;
                case "get_WorldMatrix": return MatrixD.CreateWorld(Vector3D.Zero,exhaust,Vector3D.Up);
                case "get_IsWorking": return Working;
                case "get_Enabled": case "get_IsFunctional": return true;
                case "get_MaxEffectiveThrust": case "get_MaxThrust": return force;
                case "get_ThrustOverridePercentage": return Override;
                case "set_ThrustOverridePercentage": Override=(float)call.Args[0]; Writes.Add(Override); return null;
            }
            return FixtureProxy.Default(call);
        });
    }
}
internal sealed class FixtureProxy : RealProxy
{
    private readonly Func<IMethodCallMessage,object> invoke;
    private FixtureProxy(Type type,Func<IMethodCallMessage,object> invoke):base(type) {this.invoke=invoke;}
    public static T Make<T>(Func<IMethodCallMessage,object> invoke) {return (T)new FixtureProxy(typeof(T),invoke).GetTransparentProxy();}
    public static object Default(IMethodCallMessage call) {var t=((MethodInfo)call.MethodBase).ReturnType;return t==typeof(void)||!t.IsValueType ? null : Activator.CreateInstance(t);}
    public override IMessage Invoke(IMessage message) {var call=(IMethodCallMessage)message;try {return new ReturnMessage(invoke(call),null,0,call.LogicalCallContext,call);}catch(Exception ex){return new ReturnMessage(ex,call);}}
}
