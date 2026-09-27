using System;
using ZeoNav;
internal static partial class Tests
{
    private static void AdaptiveTests()
    {
        TurnReliabilityTests();
        CalibrationFixture(10);
        CalibrationFixture(.2);
        CalibrationFixture(.04);
        var unknown=new TurnResponse();
        Check("Unknown ship reserves conservative turn",!unknown.Ready&&unknown.Allowance==180&&unknown.Rate(Math.PI)<=.35);
        unknown.Accel(double.NaN,.1);unknown.Stop(.35,0);unknown.Stop(.35,double.PositiveInfinity);
        Check("Invalid observations cannot authorize faster turn",!unknown.Ready);
        var strong=new TurnResponse();strong.Accel(.35,.02);strong.Stop(.35,.025);
        Check("One braking observation cannot authorize faster turn",!strong.Ready);
        strong.Stop(.35,.025);
        var heavy=new TurnResponse();heavy.Accel(.3,.6);heavy.Stop(.3,.8);heavy.Stop(.3,1);
        Check("Ready ship profile requires acceleration and two stops",strong.Ready&&heavy.Ready);
        Check("Strong and heavy ships use different rate limits",strong.Rate(Math.PI)>heavy.Rate(Math.PI)&&strong.Allowance<heavy.Allowance);
        Check("Learning preserves conservative slower response",Math.Abs(heavy.Deceleration-.15)<1e-8);
        Check("Known turn uses bounded command ceiling",strong.Rate(Math.PI)<=2.5);
        foreach(var response in new[]{strong,heavy})foreach(double delay in new[]{0d,.05,.10})
        {
            double angle=Math.PI,velocity=0,time=0,dt=1d/120;int wait=(int)(delay/dt);
            var queue=new System.Collections.Generic.Queue<double>();for(int i=0;i<wait;i++)queue.Enqueue(0);
            double maximum=0;
            for(int i=0;i<120*180&&(angle>.02||velocity>.08);i++)
            {
                double request=response.Rate(angle);queue.Enqueue(request);request=queue.Dequeue();
                double a=velocity<request?response.Acceleration*2:response.Deceleration*2;
                velocity+=Math.Max(-a*dt,Math.Min(a*dt,request-velocity));angle-=velocity*dt;time+=dt;maximum=Math.Max(maximum,velocity);
            }
            Check("Turn simulation settles without crossing heading delay="+delay+" brake="+response.Deceleration,angle>=0&&angle<=.02&&velocity<=.08&&time<180);
            Check("Turn prediction covers modeled duration delay="+delay+" brake="+response.Deceleration,time<=response.Allowance+2);
        }
        Check("Far retrograde coast does not dribble brake thrust",ArrivalBraking.Deceleration(300,80000,40,50,10)==0);
        Check("Brake demand saturates within available authority",ArrivalBraking.Deceleration(1000,100,40,50,10)==50);
        foreach(double distance in new[]{5000d,50000,100000})foreach(double a in new[]{1d,10,50})
        {
            // Integrate acceleration -> conservative turn -> feedback braking.
            double x=distance,v=0,dt=.02;bool braking=false;double elapsed=0;
            for(int i=0;i<200000&&!(braking&&x<=1000&&v<=20);i++)
            {
                if(!braking&&BrakingPlan.Distance(v,a,a*.8,0,6,1.12)>=x)
                {x-=v*6;elapsed+=6;braking=true;}
                double acc=braking?-ArrivalBraking.Deceleration(v,x,a*.8,a,10):a;
                v=Math.Max(0,v+acc*dt);x-=v*dt;elapsed+=dt;
            }
            Check("Arrival envelope reaches safe terminal handoff a="+a+" range="+distance,x>0&&x<=1000&&v<=20&&elapsed<4000);
        }
        var plain=NavKeyBinding.Parse("NumPad8");var modified=NavKeyBinding.Parse("Ctrl+NumPad8");
        Check("Emergency abort accepts incidental held modifiers",plain.EmergencyMatches(true,true,true));
        Check("Emergency abort preserves required binding modifier",!modified.EmergencyMatches(false,true,true)&&modified.EmergencyMatches(true,true,true));
        Check("Lock toolbar goes opposite default top banner",NavHudLayoutScreen.ToolbarOffset(true,.96)>.5);
        Check("Bottom banner retains top toolbar",NavHudLayoutScreen.ToolbarOffset(true,-.5)==0);
    }
    private static void TurnReliabilityTests()
    {
        Check("Heavy ship keeps full bank at recorded 2.7-degree stall",
            TurnAuthorityPolicy.FullBank(2.699,.25,0,false));
        Check("Settled ship can release fine-turn bank",
            !TurnAuthorityPolicy.FullBank(.07,.25,.1*Math.PI/180,true));
        Check("Moving angular ship keeps braking authority near heading",
            TurnAuthorityPolicy.FullBank(.07,.25,1.2*Math.PI/180,true));
        Check("Unknown full-bank precision begins conservatively",
            Math.Abs(TurnAuthorityPolicy.FineRateLimit(2.7*Math.PI/180,null)-1.5*Math.PI/180)<1e-9);
        var slowFine=new TurnResponse();slowFine.Accel(.005,3);slowFine.Stop(.005,3);slowFine.Stop(.005,3);
        Check("Measured weak braking limits fine-bank request",
            TurnAuthorityPolicy.FineRateLimit(2.7*Math.PI/180,slowFine)<1.5*Math.PI/180);
        var watch=new TurnProgressWatchdog();var start=DateTime.UtcNow;
        watch.Reset(start,180);
        Check("Manual flip tolerates initial delayed physics",
            watch.Observe(start.AddSeconds(9),180,0,20,33)==TurnProgress.Continue);
        Check("Manual flip retries once after no progress",
            watch.Observe(start.AddSeconds(11),180,0,20,33)==TurnProgress.Recover);
        Check("Manual flip stops after bounded failed retry",
            watch.Observe(start.AddSeconds(24),180,0,20,33)==TurnProgress.Fail);
        watch.Reset(start,180);
        bool progressing=true;
        for(int i=1;i<=6;i++)progressing &= watch.Observe(start.AddSeconds(i*6),180-i,1,12,33)==TurnProgress.Continue;
        Check("Slow 36-second progressing flip is not aborted",progressing);
        var learner=new PassiveTurnLearner();var response=new TurnResponse();
        double angle=0,priorOmega=0;var forward=VRageMath.Vector3D.Forward;
        double[] rates={0,.05,.10,.04,0,.05,.10,.04};
        for(int i=0;i<rates.Length;i++)
        {
            if(i>0)angle+=(priorOmega+rates[i])*.25;
            double request=(i==3||i==4||i==7)?0:.35;
            learner.Observe(i*.5,Turn(forward,VRageMath.Vector3D.Up,angle*180/Math.PI),
                VRageMath.Vector3D.Up,VRageMath.Vector3D.Up*rates[i],
                VRageMath.Vector3D.Up*request,response);
            priorOmega=rates[i];
        }
        Check("Passive coasting turns learn two distinct braking legs",response.Ready);
        var stale=new PassiveTurnLearner();var untrusted=new TurnResponse();
        stale.Observe(0,forward,VRageMath.Vector3D.Up,VRageMath.Vector3D.Zero,
            VRageMath.Vector3D.Up*.35,untrusted);
        stale.Observe(.5,forward,VRageMath.Vector3D.Up,VRageMath.Vector3D.Up*.2,
            VRageMath.Vector3D.Up*.35,untrusted);
        Check("Frozen pose cannot authorize passive turn learning",!untrusted.Ready&&untrusted.Acceleration==0);
        var weak=new TurnResponse();weak.Accel(.005,3);weak.Stop(.005,3);weak.Stop(.005,3);
        Check("Validated weak gyro response remains usable with long reserve",weak.Ready&&weak.Allowance>180);
    }
    private static void CalibrationFixture(double authority)
    {
        var savedSession=Sandbox.ModAPI.MyAPIGateway.Session;
        int frame=0;double angle=0,velocity=0,mass=100000;float yaw=0,power=1;bool enabled=true,over=false;
        Func<VRageMath.MatrixD> pose=()=>VRageMath.MatrixD.CreateRotationY(angle);
        try {
            Sandbox.ModAPI.MyAPIGateway.Session=FixtureProxy.Make<VRage.Game.ModAPI.IMySession>(c=>c.MethodName=="get_GameplayFrameCounter"?(object)frame:FixtureProxy.Default(c));
            var grid=FixtureProxy.Make<VRage.Game.ModAPI.IMyCubeGrid>(c=>{
                switch(c.MethodName){case "get_WorldMatrix":return pose();case "get_GridSize":return 2.5f;case "get_Max":return new VRageMath.Vector3I(10,10,20);}
                return FixtureProxy.Default(c);
            });
            var controller=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(c=>{
                switch(c.MethodName){case "get_CubeGrid":return grid;case "get_WorldMatrix":return pose();
                case "GetShipVelocities":var v=FixtureProxy.Default(c);v.GetType().GetField("AngularVelocity").SetValue(v,VRageMath.Vector3D.Up*velocity);return v;
                case "CalculateShipMass":var m=FixtureProxy.Default(c);var f=m.GetType().GetField("PhysicalMass");f.SetValue(m,Convert.ChangeType(mass,f.FieldType));return m;
                }return FixtureProxy.Default(c);
            });
            var gyro=FixtureProxy.Make<Sandbox.ModAPI.IMyGyro>(c=>{
                switch(c.MethodName){case "get_EntityId":return 83L;case "get_IsFunctional":return true;case "get_Enabled":return enabled;
                case "set_Enabled":enabled=(bool)c.Args[0];return null;case "get_WorldMatrix":return pose();case "get_GyroPower":return power;
                case "set_GyroPower":power=(float)c.Args[0];return null;case "get_GyroOverride":return over;case "set_GyroOverride":over=(bool)c.Args[0];return null;
                case "get_Yaw":return yaw;case "set_Yaw":yaw=(float)c.Args[0];return null;
                }return FixtureProxy.Default(c);
            });
            var ship=new ShipContext(controller,_=>{});ship.Gyros.Add(gyro);ship.FlipAxisMode="YAW";
            bool ready=false;double peakAngle=0;
            for(frame=0;frame<2400;frame++){
                ready=ship.PrepareRouteTurn();if(ready)break;
                double request=over&&enabled?-yaw:0;
                velocity+=Math.Max(-authority/60,Math.Min(authority/60,request-velocity));angle+=velocity/60;
                peakAngle=Math.Max(peakAngle,Math.Abs(angle));
            }
            Check("Actual turn-check sequence learns bank authority="+authority,ready&&ship.AdaptiveTurnReady&&frame<2400);
            Check("Turn-check ends settled within bounded heading excursion authority="+authority,Math.Abs(velocity)<=.015&&peakAngle<Math.PI/2);
            ship.RecordFlipTime(40);Check("Actual slower full flip expands reserve",ship.AdaptiveTurnAllowance>=50);
            mass*=1.2;frame+=31;
            Check("Mass changes invalidate previous turn reserve",!ship.AdaptiveTurnReady&&ship.AdaptiveTurnAllowance>=180);
            ship.ReleaseAll(false);Check("Abort releases calibration gyro override",!over&&yaw==0);
        }finally{Sandbox.ModAPI.MyAPIGateway.Session=savedSession;}
    }
}
