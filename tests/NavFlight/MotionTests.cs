using System;
using System.Collections.Generic;
using System.Reflection;
using VRageMath;
using ZeoNav;

internal static partial class Tests
{
    private static void MotionTests()
    {
        foreach(double speed in new[]{0,.2,5,999,1001,2000,10000,50000})
        {
            var m=new WorldMotion();
            var v=Vector3D.Normalize(new Vector3D(1,2,-3))*speed;
            for(int i=0;i<=120;i++)m.Observe(v*(i/60d),Vector3D.Normalize(new Vector3D(1,2,-3))*Math.Min(speed,1000),i/60d,85);
            Check("World velocity remains accurate at "+speed+" m/s",m.Ready&&(m.Velocity-v).Length()<=2);
            if(speed>1100)Check("Clipped physics selects measured motion at "+speed,m.Source=="WORLD MOTION");
        }
        var motion=new WorldMotion();
        for(int i=0;i<=60;i++)motion.Observe(new Vector3D(i/60d*100,0,0),new Vector3D(100,0,0),i/60d,85);
        motion.Observe(new Vector3D(100,0,0),new Vector3D(101,0,0),1+1d/60,85);
        Check("Verified physics stays responsive between position windows",motion.Ready&&motion.Velocity.X==101);
        for(int i=0;i<1000;i++)motion.Observe(new Vector3D(100,0,0),new Vector3D(101,0,0),1+1d/60,85);
        Check("Repeated paused frame does not manufacture samples",motion.Ready&&motion.MeasuredVelocity.X<101);
        motion.Observe(new Vector3D(500000,0,0),new Vector3D(100,0,0),1.5,85);
        Check("Teleport invalidates velocity before a burn",!motion.Ready&&motion.Source.Contains("DISCONTINUITY"));
        motion.Observe(new Vector3D(500025,0,0),new Vector3D(100,0,0),1.75,85);
        Check("One post-teleport sample cannot re-arm",!motion.Ready);
        motion.Observe(new Vector3D(500050,0,0),new Vector3D(100,0,0),2,85);
        Check("Consistent motion recovers after quarantine",motion.Ready);
        motion.Observe(new Vector3D(500200,0,0),new Vector3D(100,0,0),3.5,85);
        Check("Missing simulation time invalidates old motion",!motion.Ready);
        motion.Observe(new Vector3D(double.NaN,0,0),Vector3D.Zero,4,85);
        Check("Non-finite position cannot become velocity",!motion.Ready);
        motion.Reset();
        for(int i=0;i<=60;i++)motion.Observe(Vector3D.Zero,new Vector3D(1000,0,0),i/60d,85);
        Check("Frozen world coordinates with moving physics cannot authorize thrust",!motion.Ready);
        bool accelerationOk=true;
        motion.Reset();
        for(int i=0;i<=900;i++)
        {
            double t=i/60d,v=84.3*t;
            motion.Observe(new Vector3D(.5*84.3*t*t,0,0),new Vector3D(Math.Min(1000,v),0,0),t,84.3);
            if(i>=30)accelerationOk&=motion.Ready&&Math.Abs(motion.Velocity.X-v)<45;
        }
        Check("Acceleration through 1000 m/s does not flap readiness",accelerationOk);
        // Vector comparison, not speed alone: equal magnitudes can hide wrong directions.
        motion.Reset();
        for(int i=0;i<=60;i++)motion.Observe(new Vector3D(0,i/60d*2000,0),new Vector3D(2000,0,0),i/60d,85);
        Check("Full world vector resolves equal-speed direction disagreement",motion.Ready&&Math.Abs(motion.Velocity.Y-2000)<.001&&motion.Velocity.X==0);
        Check("Braking budget grows with full speed",BrakingPlan.Distance(5000,85,85,0,20,1.12)>BrakingPlan.Distance(1000,85,85,0,20,1.12)*5);
        Check("Lower signature braking authority moves flip earlier",BrakingPlan.Distance(3000,85,10,0,20,1.12)>BrakingPlan.Distance(3000,85,85,0,20,1.12));
        Check("Longer turn allowance moves flip earlier",BrakingPlan.Distance(3000,85,85,0,30,1.12)>BrakingPlan.Distance(3000,85,85,0,10,1.12));
        Check("Toward-target gravity increases stop allowance",BrakingPlan.Distance(3000,85,75,10,20,1.12)>BrakingPlan.Distance(3000,85,85,0,20,1.12));
        Check("Invalid braking inputs fail closed",double.IsPositiveInfinity(BrakingPlan.Distance(100,85,0,0,20,1))&&double.IsPositiveInfinity(BrakingPlan.Distance(100,85,85,0,-1,1))&&double.IsPositiveInfinity(BrakingPlan.Distance(100,85,85,0,20,double.NaN)));
        foreach(double leg in new[]{169371d,103838d})foreach(double brake in new[]{84.3,30d})
        {
            double corrected=StoppingTrajectory(leg,brake,true),old=StoppingTrajectory(leg,brake,false);
            Check("Position-derived flip stops before target leg="+leg+" brake="+brake,corrected<leg&&corrected>leg*.4);
            Check("Clipped-speed flip reproduces overshoot leg="+leg+" brake="+brake,old>leg);
        }
        StopHandoffTests();
    }

    // Synthetic 1-D server flight: 250 ms motion windows, a saturated API, a two-second
    // burn/cutoff delay and a 20-second turn. Uses the live estimator and braking planner.
    private static double StoppingTrajectory(double distance,double brake,bool corrected)
    {
        const double a=84.3,dt=1d/60;
        double position=0,velocity=0,flipTime=-1;
        var motion=new WorldMotion();
        for(int i=0;i<60000;i++)
        {
            double time=i*dt;
            motion.Observe(new Vector3D(position,0,0),new Vector3D(Math.Min(1000,velocity),0,0),time,a);
            double sample=corrected?motion.Velocity.Length():Math.Min(1000,velocity);
            if(flipTime<0&&motion.Ready&&BrakingPlan.Distance(sample,a,brake,0,20,1.12)>=distance-position)flipTime=time;
            double accel=flipTime<0||time<flipTime+2?a:time<flipTime+22?0:-brake;
            velocity=Math.Max(0,velocity+accel*dt);position+=velocity*dt;
            if(flipTime>=0&&time>flipTime+22&&velocity==0)return position;
        }
        throw new Exception("Trajectory did not finish");
    }
    private static void NavSet(NavController nav,string field,object value)
    {typeof(NavController).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(nav,value);}
    private static object NavCall(NavController nav,string method,params object[] args)
    {return typeof(NavController).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(nav,args);}
    private static NavPhase Phase(NavController nav)
    {return (NavPhase)typeof(NavController).GetField("phase",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(nav);}
    private static void StopHandoffTests()
    {
        bool dampeners=false;
        var controller=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(call=>{
            switch(call.MethodName)
            {
                case "get_WorldMatrix":return MatrixD.Identity;
                case "get_DampenersOverride":return dampeners;
                case "set_DampenersOverride":dampeners=(bool)call.Args[0];return null;
            }
            return FixtureProxy.Default(call);
        });
        var ship=new ShipContext(controller,_=>{});
        var drive=new DriveFixture(501,"Main drive",100,Vector3D.Backward);
        ship.ThrusterBanks[MoveDir.Forward].Add(drive.Block);
        ship.RefreshWorkingState();ship.BeginThrustControl();
        var cfg=new NavConfig();
        var nav=new NavController(()=>ship,()=>cfg,()=>null,_=>{});
        var budget=new SignalBudget{Ready=true,TargetKm=400};
        budget.Add("drive",true,0,100000);
        ship.SignatureBudget=budget;NavSet(nav,"signalBudget",budget);
        NavSet(nav,"active",true);NavSet(nav,"phase",NavPhase.TERMINAL_SETTLE);
        Check("Low-speed 300 m arrival belongs to terminal guidance",NavController.UseTerminalAtLowSpeed(.2,300,19,5));
        Check("High-speed flight keeps full flip planning",!NavController.UseTerminalAtLowSpeed(500,300,19,5));
        Check("Long recovery stays outside terminal envelope",!NavController.UseTerminalAtLowSpeed(.2,3000,19,5));
        Check("Custom terminal envelope takes effect without bypassing low-speed gate",NavController.UseTerminalAtLowSpeed(.2,1500,19,5,1800)&&!NavController.UseTerminalAtLowSpeed(80,1500,19,5,1800));
        Check("Disabled early dampeners stay off",!NavController.EarlyDampenerEnvelope(40,500,10,5,0));
        Check("Configured early dampener speed limits handoff",!NavController.EarlyDampenerEnvelope(70,500,10,5,60)&&NavController.EarlyDampenerEnvelope(50,500,10,5,60));
        NavCall(nav,"UpdateTerminal",ship,cfg,new Vector3D(300,0,0),300d,new Vector3D(0,0,.2),.2d);
        Check("300 m terminal correction does not return to flip loop",Phase(nav)==NavPhase.TERMINAL_SETTLE);
        NavSet(nav,"phase",NavPhase.TERMINAL_SETTLE);
        NavCall(nav,"UpdateTerminal",ship,cfg,new Vector3D(3,0,0),3d,new Vector3D(0,0,1000),1000d);
        Check("High-speed terminal entry returns to main braking",Phase(nav)==NavPhase.INITIAL_BRAKE);
        NavSet(nav,"phase",NavPhase.TERMINAL_SETTLE);
        NavCall(nav,"UpdateTerminal",ship,cfg,new Vector3D(3000,0,0),3000d,Vector3D.Zero,0d);
        Check("Long recovery cannot remain capped at terminal crawl",Phase(nav)==NavPhase.INITIAL_BRAKE);
        NavSet(nav,"phase",NavPhase.INITIAL_BRAKE);
        NavCall(nav,"FinishLowSpeedStop",ship,new Vector3D(0,0,4));
        Check("Low-speed dampeners enabled when full output fits SIG",dampeners&&drive.Override==0);
        budget.TargetKm=100;
        NavCall(nav,"FinishLowSpeedStop",ship,new Vector3D(0,0,4));
        Check("Lower ceiling disables unrestricted dampeners",!dampeners&&drive.Override>0);
        Check("Manual final braking stays within SIG",budget.PredictedSquared(ship.AppliedCommands)<=Math.Pow(100*.97,2)+.001);
        budget.Ready=false;NavCall(nav,"FinishLowSpeedStop",ship,new Vector3D(0,0,4));
        Check("Stale SIG permits neither dampeners nor final thrust",!dampeners&&drive.Override==0);
        foreach(bool original in new[]{false,true})
        {
            budget.Ready=true;budget.TargetKm=400;dampeners=original;ship.SaveDampenersOnce();ship.SetDampeners(false);
            ship.BeginThrustControl();ship.SignatureBudget=budget;
            NavSet(nav,"active",true);NavSet(nav,"phase",NavPhase.TERMINAL_SETTLE);NavSet(nav,"arrivalTicks",0);
            for(int i=0;i<15;i++)NavCall(nav,"UpdateTerminal",ship,cfg,Vector3D.Zero,0d,Vector3D.Zero,0d);
            Check("Arrival restores original dampeners="+original,Phase(nav)==NavPhase.ARRIVED&&dampeners==original&&drive.Override==0&&!nav.IsControlling);
        }
        budget.Ready=true;budget.TargetKm=400;dampeners=true;ship.SaveDampenersOnce();ship.SetDampeners(false);ship.BeginThrustControl();
        NavSet(nav,"active",true);NavSet(nav,"phase",NavPhase.INITIAL_BRAKE);NavCall(nav,"FinishLowSpeedStop",ship,new Vector3D(0,0,4));nav.Abort("fixture");
        Check("Abort releases assist and restores original dampeners",dampeners&&drive.Override==0&&!nav.IsControlling);
    }
}
