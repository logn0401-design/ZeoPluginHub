using System;
using VRageMath;
using ZeoNav;
internal static partial class Tests
{
    private static WorldMotion CorrectedMotion()
    {
        var m=new WorldMotion();
        for(int i=0;i<=3;i++)m.Observe(new Vector3D(25*i,0,0),new Vector3D(100,0,0),i*.25,85);
        m.Observe(new Vector3D(130,0,0),new Vector3D(100,0,0),1,85);
        return m;
    }
    private static void RecoverMotion(WorldMotion m)
    {m.Observe(new Vector3D(155,0,0),new Vector3D(100,0,0),1.25,85);m.Observe(new Vector3D(180,0,0),new Vector3D(100,0,0),1.5,85);}
    private static void EngagementTests()
    {
        var gate=new PilotInputGate();bool quiet=true;
        for(int i=0;i<600;i++)quiet&=!gate.Observe(Vector3.Zero,new Vector2(.2f,.1f),0,i/60d);
        Check("Small sustained mouse jitter does not cancel Nav",quiet);
        gate.Reset();Check("Single moderate mouse bump is ignored",!gate.Observe(Vector3.Zero,new Vector2(.8f,0),0,0));
        Check("Moderate steering needs deliberate duration",!gate.Observe(Vector3.Zero,new Vector2(.8f,0),0,.1)&&gate.Observe(Vector3.Zero,new Vector2(.8f,0),0,.21));
        gate.Reset();gate.Observe(Vector3.Zero,new Vector2(.8f,0),0,0);gate.Observe(Vector3.Zero,Vector2.Zero,0,.1);
        Check("Separate bumps cannot accumulate into takeover",!gate.Observe(Vector3.Zero,new Vector2(.8f,0),0,.4));
        Check("Strong mouse movement cancels immediately",gate.Observe(Vector3.Zero,new Vector2(3,0),0,.41));
        Check("Movement key cancels immediately",gate.Observe(Vector3.Forward,Vector2.Zero,0,.42));
        Check("Roll key cancels immediately",gate.Observe(Vector3.Zero,Vector2.Zero,1,.43));
        foreach(double angle in new[]{0d,1,10,24.9,25.1,45,90,180})
        {
            var velocity=new Vector3D(Math.Sin(angle*Math.PI/180),0,-Math.Cos(angle*Math.PI/180))*2000;
            Check("Moving route window angle="+angle,MomentumCapture.InWindow(velocity,Vector3D.Forward)==(angle<=25));
        }
        Check("Distant target permits correction while preserving speed",MomentumCapture.HasRoom(900000,60000,1000,20,1,20));
        Check("Near destination retains braking instead of forward capture",!MomentumCapture.HasRoom(50000,60000,1000,20,1,20));
        Check("Weak sideways thrust prevents uncorrectable high-speed capture",!MomentumCapture.HasRoom(900000,60000,1000,400,.01,20));
        Check("Aligned moving ship does not require sideways thrust",MomentumCapture.HasRoom(900000,60000,1000,0,0,20));
        Check("Non-finite stopping data cannot preserve a burn",!MomentumCapture.HasRoom(900000,double.PositiveInfinity,1000,20,1,20));
        double aligned=MomentumCapture.AlignmentSeconds(.017,0,180);
        Check("Recorded 1025 m/s recovery retains aligned momentum",aligned==0&&MomentumCapture.HasRoom(543496,314615,1025,0,2.426,aligned));
        Check("Recovery still brakes when recorded stopping margin is absent",!MomentumCapture.HasRoom(314000,314615,1025,0,2.426,aligned));
        Check("Large heading correction retains a measured turn reserve",MomentumCapture.AlignmentSeconds(90,1,180)>90);
        Check("Missing turn telemetry cannot authorize continued acceleration",!MomentumCapture.HasRoom(543496,314615,1025,0,2.426,MomentumCapture.AlignmentSeconds(.017,double.NaN,180)));

        var m=CorrectedMotion();var recovery=new MotionRevalidation();
        Check("Moderate server correction invalidates velocity and records sample",!m.Ready&&m.RecoverableFault&&m.LastFault.Contains("residual="));
        Check("Recoverable correction enters bounded hold",recovery.Observe(m,0)==MotionDecision.Hold);
        m.Observe(new Vector3D(155,0,0),new Vector3D(100,0,0),1.25,85);
        Check("One fresh sample cannot resume flight",recovery.Observe(m,.25)==MotionDecision.Hold);
        m.Observe(new Vector3D(180,0,0),new Vector3D(100,0,0),1.5,85);
        Check("Two consistent samples release hold",recovery.Observe(m,.5)==MotionDecision.Recovered&&recovery.Observe(m,.6)==MotionDecision.Clear);
        m=CorrectedMotion();recovery.Reset();recovery.Observe(m,0);
        Check("Unverified motion lasting over two seconds aborts",recovery.Observe(m,2.01)==MotionDecision.Abort);
        m=CorrectedMotion();m.Observe(new Vector3D(100000,0,0),new Vector3D(100,0,0),1.25,85);recovery.Reset();
        Check("Large position jump aborts without grace",!m.RecoverableFault&&recovery.Observe(m,0)==MotionDecision.Abort);
        recovery.Reset();bool bounded=true;
        for(int i=0;i<3;i++){m=CorrectedMotion();bounded&=recovery.Observe(m,i*2)==MotionDecision.Hold;RecoverMotion(m);bounded&=recovery.Observe(m,i*2+.5)==MotionDecision.Recovered;}
        Check("Fourth correction within thirty seconds cannot cause endless resumes",bounded&&recovery.Observe(CorrectedMotion(),6)==MotionDecision.Abort);
        Check("Correction frequency allowance expires after thirty seconds",recovery.Observe(CorrectedMotion(),40)==MotionDecision.Hold);
        MotionHoldActuationTests();
    }
    private static void MotionHoldActuationTests()
    {
        Vector3 move=Vector3.Zero;bool dampeners=false;
        var grid=FixtureProxy.Make<VRage.Game.ModAPI.IMyCubeGrid>(c=>FixtureProxy.Default(c));
        var controller=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(c=>{
            switch(c.MethodName){case "get_CubeGrid":return grid;case "get_WorldMatrix":return MatrixD.Identity;case "get_MoveIndicator":return move;
            case "get_DampenersOverride":return dampeners;case "set_DampenersOverride":dampeners=(bool)c.Args[0];return null;}
            return FixtureProxy.Default(c);
        });
        var ship=new ShipContext(controller,_=>{});float pitch=1,yaw=1,roll=1;
        ship.Gyros.Add(FixtureProxy.Make<Sandbox.ModAPI.IMyGyro>(c=>{
            switch(c.MethodName){case "get_EntityId":return 910L;case "get_IsFunctional":case "get_Enabled":return true;case "get_WorldMatrix":return MatrixD.Identity;
            case "get_GyroPower":return 1f;case "set_Pitch":pitch=(float)c.Args[0];return null;case "set_Yaw":yaw=(float)c.Args[0];return null;case "set_Roll":roll=(float)c.Args[0];return null;}
            return FixtureProxy.Default(c);
        }));
        var main=new DriveFixture(911,"Main drive",1000,Vector3D.Backward);ship.ThrusterBanks[MoveDir.Forward].Add(main.Block);
        ship.RefreshWorkingState();ship.BeginThrustControl();ship.SetMove(MoveDir.Forward,1);
        for(int i=0;i<=3;i++)ship.Motion.Observe(new Vector3D(25*i,0,0),new Vector3D(100,0,0),i*.25,85);
        ship.Motion.Observe(new Vector3D(130,0,0),new Vector3D(100,0,0),1,85);
        var nav=new NavController(()=>ship,()=>new NavConfig(),()=>null,_=>{});NavSet(nav,"active",true);NavSet(nav,"phase",NavPhase.ACCELERATE);
        nav.Update(1);
        Check("Actual controller hold cuts thrust without aborting route",nav.IsControlling&&main.Override==0&&!dampeners);
        Check("Actual controller hold clears stale gyro rate",pitch==0&&yaw==0&&roll==0);
        RecoverMotion(ship.Motion);nav.Update(2);
        Check("Recovery retains route and awaits SIG rather than blindly burning",nav.IsControlling&&main.Override==0);
        move=Vector3.Forward;nav.Update(3);
        Check("Pilot takeover still cancels during recovery",!nav.IsControlling&&main.Override==0);
    }
}
