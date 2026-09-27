using System;
using System.Collections.Generic;
using VRageMath;
using ZeoNav;
internal static partial class Tests
{
    private static void TargetTests()
    {
        foreach(double speed in new[]{300d,1297,3345,10000,50000})
        {
            var motion=new WorldMotion();var velocity=new Vector3D(speed,0,0);
            bool accurate=true;
            for(int i=0;i<600;i++)
            {
                double time=i/60d;
                // Bounded client pose corrections; the mod's server-synced velocity stays stable.
                double correction=(i/15)%2==0?Math.Min(100,speed*.05):0;
                motion.ObserveMod(new Vector3D(speed*time+correction,0,0),new Vector3D(Math.Min(speed,1000),0,0),velocity,time,85);
                if(i>90)accurate&=motion.Ready&&motion.Velocity==velocity;
            }
            Check("Mod velocity tolerates bounded pose jitter at "+speed,accurate);
            motion.ObserveMod(new Vector3D(10000000,0,0),Vector3D.Zero,velocity,10,85);
            Check("Mod feed cannot authorize an enormous teleport at "+speed,!motion.Ready&&!motion.RecoverableFault);
        }
        var frozen=new WorldMotion();for(int i=0;i<180;i++)frozen.ObserveMod(Vector3D.Zero,Vector3D.Zero,new Vector3D(3345,0,0),i/60d,85);
        Check("Frozen positions cannot validate moving mod telemetry",!frozen.Ready);
        frozen.ObserveMod(Vector3D.Zero,Vector3D.Zero,new Vector3D(double.NaN,0,0),4,85);
        Check("Nonfinite mod telemetry fails closed",!frozen.Ready&&!frozen.RecoverableFault);
        var track=new TargetTrack();
        for(int i=0;i<3;i++)track.Accept(new TargetDetection{EmitterId=91,Position=new Vector3D(5000+i*1000,0,0),Velocity=new Vector3D(1000,0,0),DetectedAt=i*60},i);
        Check("Three distinct consistent contact samples permit lock",track.Fresh(120,2));
        track.Accept(track.Sample,3);
        Check("Repeated snapshot does not refresh contact age",!track.Fresh(300,5));
        track.Accept(new TargetDetection{EmitterId=91,Position=new Vector3D(8000,0,0),Velocity=new Vector3D(50000,0,0),DetectedAt=180},3);
        Check("Contact velocity/position disagreement quarantines target",!track.Fresh(180,3));
        Check("Matching equal speeds in opposite directions needs correction",(new Vector3D(5000,0,0)-new Vector3D(-5000,0,0)).Length()==10000);
        foreach(double a in new[]{.1,1,10,50})foreach(double d in new[]{1d,100,1000,100000})foreach(double turn in new[]{20d,136,180})
        {
            double speed=RendezvousMath.ClosingLimit(d,a,turn);
            Check("Rendezvous reserves turn and stop distance a="+a+" d="+d+" turn="+turn,speed*(turn+2)+speed*speed/a<=d+.0001);
        }
        var goal=RendezvousMath.GoalVelocity(new Vector3D(5000,0,0),new Vector3D(0,5000,0),5000,5,180,50000);
        Check("At stand-off desired velocity equals target vector",goal==new Vector3D(0,5000,0));
        goal=RendezvousMath.GoalVelocity(new Vector3D(100000,0,0),new Vector3D(0,49999,0),5000,50,180,50000);
        Check("Rendezvous combined velocity never exceeds cap",goal.Length()<=50000.00001);
        var targetVelocity=new Vector3D(300,200,0);
        Check("Match-speed goal is target velocity regardless of position",TargetFlight.DesiredVelocity(false,new Vector3D(50000,0,0),targetVelocity,5000,10,20,50000)==targetVelocity);
        var interceptGoal=TargetFlight.DesiredVelocity(true,new Vector3D(50000,0,0),targetVelocity,5000,10,20,50000);
        Check("Intercept adds bounded closing speed toward locked signal",interceptGoal.X>targetVelocity.X&&interceptGoal.Length()<=50000);
        Check("Intercept changes to target velocity at stand-off",TargetFlight.DesiredVelocity(true,new Vector3D(5000,0,0),targetVelocity,5000,10,20,50000)==targetVelocity);
        Check("Intercept refuses a target already inside selected stand-off",!TargetFlight.InterceptHasRoom(3652,26000,500));
        Check("Intercept accepts a target outside stand-off and separation floor",TargetFlight.InterceptHasRoom(60000,26000,500));
        double oldStop=BrakingPlan.Distance(3345,0,9.7,0,20,1.12),newStop=BrakingPlan.Distance(3345,0,9.7,0,180,1.12);
        Check("Recorded slow flip reserves over 500 km more travel",newStop-oldStop>500000);
        TargetSelectionTests();
        PointerSelectionTests();
        SpectrumSignalLockTests();
        BankTurnTests();
    }
    private static void PointerSelectionTests()
    {
        var ctrl=new TargetCtrlLatch();
        Check("Standalone Left Ctrl is capturable for configurable aim hold",NavKeyBinding.CaptureHoldKey(VRage.Input.MyKeys.LeftControl)&&!NavKeyBinding.CaptureKey(VRage.Input.MyKeys.LeftControl));
        Check("Left Ctrl begins targeting on a fresh eligible press",ctrl.Observe(true,true));
        Check("Holding Left Ctrl cannot reopen a closed or confirmed picker",!ctrl.Observe(true,true));
        ctrl.Observe(false,true);Check("Left Ctrl held in chat cannot activate targeting",!ctrl.Observe(true,false)&&!ctrl.Observe(true,true));
        ctrl.Observe(false,true);Check("Releasing then pressing Left Ctrl rearms targeting",ctrl.Observe(true,true));
        var click=new TargetClickLatch();
        Check("Opening while mouse held cannot lock a target",click.Observe(true,11,true)==0&&click.Observe(false,11,true)==0);
        Check("Target click waits for release",click.Observe(true,11,true)==0&&click.Observe(false,11,true)==11);
        Check("Dragging from one contact to another does not silently change lock",click.Observe(true,11,true)==0&&click.Observe(false,12,true)==0);
        click.Observe(true,11,true);click.Observe(true,11,false);
        Check("Lost focus cannot commit an old click",click.Observe(false,11,true)==0);
        Check("A click on empty space cannot lock",click.Observe(true,0,true)==0&&click.Observe(false,0,true)==0);
        foreach(var size in new[]{new Vector2I(1280,720),new Vector2I(1920,1080),new Vector2I(3440,1440)})
        {
            var point=TargetPointer.FromPixels(size.X*.75,size.Y*.25,size.X,size.Y);
            Check("Pointer matches projected screen coordinates at "+size,Math.Abs(point.X-.5)<1e-9&&Math.Abs(point.Y-.5)<1e-9);
            var next=TargetPointer.FromPixels(size.X*.75+30,size.Y*.25,size.X,size.Y);
            Check("Pick distance uses actual pixels at "+size,Math.Abs(TargetPointer.DistanceSquared(point,next,size.X,size.Y)-900)<1e-6);
        }
        Check("Outside-window and invalid pointer cannot select",!TargetPointer.Valid(new Vector2D(1.1,0),1920,1080)&&!TargetPointer.Valid(new Vector2D(double.NaN,0),1920,1080));
        var grid=FixtureProxy.Make<VRage.Game.ModAPI.IMyCubeGrid>(c=>FixtureProxy.Default(c));
        var pilot=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(c=>c.MethodName=="get_CubeGrid"?grid:FixtureProxy.Default(c));
        var own=new ShipContext(pilot,_=>{});var tracker=new TargetTracker();tracker.Select();
        tracker.LockedId=73;tracker.Confirmed=true;tracker.CandidateId=74;
        tracker.ClearLock();
        Check("Right-click clear removes confirmed lock and candidate",!tracker.Confirmed&&tracker.LockedId==0&&tracker.CandidateId==0&&!tracker.Selecting);
        tracker.Select();
        var camera=MatrixD.CreateTranslation(1000,0,0);
        Func<Vector3D,Vector3D> project=p=>{var local=Vector3D.Transform(p,MatrixD.Invert(camera));return new Vector3D(local.X/-local.Z,local.Y/-local.Z,0);};
        for(int i=0;i<3;i++)tracker.Update(new List<TargetDetection>{
            new TargetDetection{EmitterId=11,Position=new Vector3D(2000,0,-5000),DetectedAt=i*60},
            new TargetDetection{EmitterId=12,Position=new Vector3D(2450,0,-5000),DetectedAt=i*60},
            new TargetDetection{EmitterId=13,Position=new Vector3D(0,0,5000),DetectedAt=i*60},
            new TargetDetection{EmitterId=14,Position=new Vector3D(2050,0,-5000),DetectedAt=i*60}},own,i*60,i,camera,false);
        tracker.Aim(120,2,camera,project,new Vector2D(.2,0),1920,1080);
        Check("Third-person camera offset selects contact beneath mouse",tracker.CandidateId==11);
        tracker.Cycle();tracker.Aim(120,2,camera,project,new Vector2D(.2,0),1920,1080);
        Check("Cycle chooses nearby overlapping contact",tracker.CandidateId==14);
        tracker.Aim(120,2,camera,project,new Vector2D(.29,0),1920,1080);
        Check("Moving mouse to another contact resets cycle selection",tracker.CandidateId==12);
        tracker.Aim(120,2,camera,project,new Vector2D(0,0),1920,1080);
        Check("Behind-camera contact is never offered",tracker.CandidateId==0);
        tracker.Aim(1100,19,camera,project,new Vector2D(.2,0),1920,1080);
        Check("Expired Spectrum signal cannot be confirmed by mouse",tracker.CandidateId==0);
    }
    private static void SpectrumSignalLockTests()
    {
        var grid=FixtureProxy.Make<VRage.Game.ModAPI.IMyCubeGrid>(c=>FixtureProxy.Default(c));
        var pilot=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(c=>c.MethodName=="get_CubeGrid"?grid:FixtureProxy.Default(c));
        var own=new ShipContext(pilot,_=>{});
        var tracker=new TargetTracker();tracker.Select();
        // No streamed grid or entity is supplied: this is solely a Spectrum API emission.
        var distant=new TargetDetection{EmitterId=872341,Position=new Vector3D(0,0,-500000),Velocity=Vector3D.Zero,DetectedAt=0};
        tracker.Update(new List<TargetDetection>{distant},own,600,10,MatrixD.Identity,false);
        tracker.Aim(600,10,MatrixD.Identity,p=>new Vector3D(p.X/-p.Z,p.Y/-p.Z,0),Vector2D.Zero,1920,1080);
        Check("Distant signal beyond streamed-entity range can be hovered from Spectrum alone",tracker.CandidateId==distant.EmitterId);
        tracker.Select();
        Check("Single visible Spectrum signal can be locked",tracker.Confirmed&&tracker.LockedId==distant.EmitterId);
        Check("One old signal sample cannot authorize autonomous flight",!tracker.Locked.Fresh(600,10));
        tracker.Update(new List<TargetDetection>{distant},own,720,12,MatrixD.Identity,false);
        Check("Spectrum lock persists while signal remains visible",tracker.Confirmed);
        tracker.Update(new List<TargetDetection>{distant},own,901,15.1,MatrixD.Identity,false);
        Check("Spectrum lock expires with the mod's 15-second visibility",!tracker.Confirmed);
        var motion=new TargetTrack();
        motion.Accept(new TargetDetection{EmitterId=10,Position=Vector3D.Zero,Velocity=new Vector3D(100,0,0),DetectedAt=0},0);
        motion.Accept(new TargetDetection{EmitterId=10,Position=new Vector3D(100,0,0),Velocity=new Vector3D(200,0,0),DetectedAt=60},1);
        Check("Signal projection follows Spectrum velocity and acceleration",Math.Abs(motion.Position(120).X-350)<1e-6);
    }
    private static void TargetSelectionTests()
    {
        var grid=FixtureProxy.Make<VRage.Game.ModAPI.IMyCubeGrid>(c=>FixtureProxy.Default(c));
        var controller=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(c=>c.MethodName=="get_CubeGrid"?grid:FixtureProxy.Default(c));
        var own=new ShipContext(controller,_=>{});var tracker=new TargetTracker();tracker.Select();
        for(int i=0;i<3;i++)tracker.Update(new List<TargetDetection>{new TargetDetection{EmitterId=11,Position=new Vector3D(0,0,-5000),DetectedAt=i*60}},own,i*60,i,MatrixD.Identity);
        Check("Look-to-select chooses a fresh contact in camera cone",tracker.CandidateId==11&&tracker.Selecting&&!tracker.Confirmed);
        tracker.Select();Check("Second press confirms fixed emitter identity",tracker.Confirmed&&tracker.LockedId==11&&!tracker.Selecting);
        tracker.Update(new List<TargetDetection>{new TargetDetection{EmitterId=12,Position=new Vector3D(0,0,-5000),DetectedAt=180}},own,180,3,MatrixD.Identity);
        Check("One missing Spectrum poll retains the original lock",tracker.Confirmed&&tracker.LockedId==11);
        Check("Old contact cannot authorize thrust after its fresh window",!tracker.Locked.Fresh(300,5));
        tracker.Update(new List<TargetDetection>(),own,1081,18.1,MatrixD.Identity);
        Check("Unreacquired Spectrum lock expires after 15 seconds",!tracker.Confirmed&&tracker.LockedId==11);
        tracker.Select();tracker.Cancel();Check("Cancel closes selection without switching lock",!tracker.Selecting&&tracker.LockedId==11);
    }
    private static void BankTurnTests()
    {
        Check("RCS strength requires a clear torque advantage",!ShipContext.PreferRcsTorque(300,350)&&ShipContext.PreferRcsTorque(300,400));
        Check("Unknown or invalid gyro torque stays with standard gyros",!ShipContext.PreferRcsTorque(0,400)&&!ShipContext.PreferRcsTorque(300,double.PositiveInfinity));
        var grid=FixtureProxy.Make<VRage.Game.ModAPI.IMyCubeGrid>(c=>FixtureProxy.Default(c));
        var controller=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(c=>c.MethodName=="get_CubeGrid"?grid:c.MethodName=="get_WorldMatrix"?(object)MatrixD.Identity:FixtureProxy.Default(c));
        var ship=new ShipContext(controller,_=>{});
        var overrides=new bool[3];var commands=new Vector3D[3];
        for(int n=0;n<3;n++)
        {
            int id=n;MatrixD pose=MatrixD.CreateRotationX(n*.4);
            ship.Gyros.Add(FixtureProxy.Make<Sandbox.ModAPI.IMyGyro>(c=>{
                switch(c.MethodName){case "get_EntityId":return 200L+id;case "get_IsFunctional":case "get_Enabled":return true;
                case "get_WorldMatrix":return pose;case "get_GyroPower":return .7f;case "get_GyroOverride":return overrides[id];
                case "set_GyroOverride":overrides[id]=(bool)c.Args[0];return null;
                case "set_Pitch":commands[id].X=(float)c.Args[0];return null;case "set_Yaw":commands[id].Y=(float)c.Args[0];return null;case "set_Roll":commands[id].Z=(float)c.Args[0];return null;
                }return FixtureProxy.Default(c);
            }));
        }
        ship.Orient(Vector3D.Backward,.5);
        Check("Large turn commands all enabled standard gyros",overrides[0]&&overrides[1]&&overrides[2]);
        for(int i=0;i<3;i++)Check("Turn bank mounting "+i+" preserves world rate",Vector3D.Distance(-Vector3D.TransformNormal(commands[i],MatrixD.CreateRotationX(i*.4)),Vector3D.Up*.35)<1e-6);
        ship.Orient(Turn(Vector3D.Forward,Vector3D.Up,2.699),.25);
        Check("Actual 2.7-degree heading keeps all standard gyros commanded",
            overrides[0]&&overrides[1]&&overrides[2]&&ship.AimMode=="TURN-BANK-FINE"&&ship.TurnRequestDegPerSec>.1);
        ship.ApplyDockRotation(Vector3D.Zero);
        Check("Motion hold clears other bank overrides",(overrides[0]?1:0)+(overrides[1]?1:0)+(overrides[2]?1:0)==1&&commands[0]==Vector3D.Zero&&commands[1]==Vector3D.Zero&&commands[2]==Vector3D.Zero);
        bool rcsEnabled=false,rcsOverride=false;int rcsEnabledWrites=0,rcsOverrideWrites=0;Vector3D rcsCommand=Vector3D.Zero;
        var rcs=FixtureProxy.Make<Sandbox.ModAPI.IMyGyro>(c=>{
            switch(c.MethodName){case "get_EntityId":return 999L;case "get_IsFunctional":return true;case "get_Enabled":return rcsEnabled;
            case "set_Enabled":rcsEnabled=(bool)c.Args[0];rcsEnabledWrites++;return null;case "get_WorldMatrix":return MatrixD.Identity;
            case "get_BlockDefinition":var def=FixtureProxy.Default(c);def.GetType().GetField("SubtypeName").SetValue(def,"sdg_rcsGyroComputer");return def;
            case "get_GyroPower":return 1f;case "get_GyroOverride":return rcsOverride;case "set_GyroOverride":rcsOverride=(bool)c.Args[0];rcsOverrideWrites++;return null;
            case "set_Pitch":rcsCommand.X=(float)c.Args[0];return null;case "set_Yaw":rcsCommand.Y=(float)c.Args[0];return null;case "set_Roll":rcsCommand.Z=(float)c.Args[0];return null;
            }return FixtureProxy.Default(c);
        });
        var rcsList=(List<Sandbox.ModAPI.IMyGyro>)typeof(ShipContext).GetField("rcsGyros",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(ship);rcsList.Add(rcs);
        ship.Orient(Vector3D.Backward,.5);
        Check("Large turn isolates SDX RCS gyro computer",!rcsEnabled&&!rcsOverride&&rcsCommand==Vector3D.Zero);
        int enabledWrites=rcsEnabledWrites,overrideWrites=rcsOverrideWrites;
        ship.Orient(Vector3D.Backward,.5);
        Check("Steady turn does not toggle isolated RCS computer",rcsEnabledWrites==enabledWrites&&rcsOverrideWrites==overrideWrites);
        ship.ApplyDockRotation(Vector3D.Zero);
        Check("Fine control keeps RCS computer isolated",!rcsEnabled&&!rcsOverride&&rcsCommand==Vector3D.Zero);
        ship.ReleaseAll(false);
        Check("RCS release enables computer even when originally disabled",rcsEnabled&&!rcsOverride);
        rcsEnabled=false;ship.ReleaseAll(false);
        Check("Repeated idle release does not change pilot RCS choice",!rcsEnabled);
        ship.Orient(Vector3D.Backward,.5);
        Check("Standard-only turn keeps RCS disabled",!rcsEnabled&&!rcsOverride);
        rcsList.Clear();ship.ReleaseAll(false);
        Check("Release enables touched RCS even after it leaves current scan",rcsEnabled&&!rcsOverride);
        Check("Release restores every gyro override",!overrides[0]&&!overrides[1]&&!overrides[2]);
    }
}
