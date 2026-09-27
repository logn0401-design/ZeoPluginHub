using System;
using VRageMath;
using ZeoNav;
internal static partial class Tests
{
    private static void DockPaceTests()
    {
        var control=new SavedControlSwitch();bool enabled=true;int writes=0;
        control.Acquire(()=>enabled,v=>{enabled=v;writes++;});
        Check("Dock mouse guard disables manual gyro control",control.Active&&!enabled&&writes==1);
        control.Maintain();Check("Mouse guard does not flood unchanged synchronized setting",writes==1);
        enabled=true;control.Maintain();Check("Mouse guard retains ownership while docking",!enabled&&writes==2);
        control.Release();Check("Dock completion or abort restores pilot gyro control",enabled&&!control.Active);
        control.Release();Check("Repeated cleanup is harmless",enabled&&writes==3);
        enabled=false;control.Acquire(()=>enabled,v=>enabled=v);control.Release();
        Check("Previously disabled cockpit gyro setting stays disabled on release",!enabled);
        bool fail=true;enabled=true;
        try{control.Acquire(()=>enabled,v=>{if(fail)throw new InvalidOperationException("fixture");enabled=v;});}catch(InvalidOperationException){}
        fail=false;control.Release();Check("Partial mouse acquisition can restore original state",enabled&&!control.Active);
        bool ignoreRestore=true;control.Acquire(()=>enabled,v=>{if(!v||!ignoreRestore)enabled=v;});
        bool pending=false;try{control.Release();}catch(InvalidOperationException){pending=true;}
        Check("Failed restore retains ownership for retry",pending&&control.Active);
        ignoreRestore=false;control.Release();Check("Restore retry returns control",enabled&&!control.Active);
        var record=new DockMouseControl.Recovery{Context="server|world|pilot",Cockpit=123456,Original=true};
        var persisted=JsonIo.FromBytes<DockMouseControl.Recovery>(JsonIo.ToBytes(record));
        Check("Mouse recovery record preserves context and exact original state",persisted.Context==record.Context&&persisted.Cockpit==record.Cockpit&&persisted.Original);
        Check("Docking keyboard and roll takeover remain immediate",DockMouseControl.Takeover(Vector3.Forward,0)&&DockMouseControl.Takeover(Vector3.Zero,1)&&!DockMouseControl.Takeover(Vector3.Zero,0));
        var native=typeof(Sandbox.Game.Entities.MyShipController).GetProperty("ControlGyros");
        Check("Game exposes readable writable cockpit gyro control",native!=null&&native.CanRead&&native.CanWrite);
        var migrated=ConfigRules.Clamp(new NavConfig{ConfigVersion=11,DockTransitMps=0,DockApproachMps=.7,DockStandOffMeters=80,ApproachSigKm=180});
        Check("Transit migration preserves near speed, stand-off and SIG",migrated.ConfigVersion==18&&migrated.TargetAimHoldKey=="LeftControl"&&migrated.TargetHudY==-.76&&migrated.DockTransitMps==6&&migrated.DockApproachMps==.7&&migrated.DockStandOffMeters==80&&migrated.ApproachSigKm==180);
        Check("Configured transit speed survives reloading",ConfigRules.Clamp(new NavConfig{DockTransitMps=2}).DockTransitMps==2);
        Check("Existing custom transit limit is preserved",ConfigRules.Clamp(new NavConfig{ConfigVersion=13,DockTransitMps=3}).DockTransitMps==3);
        Check("Higher stored transit limit clamps to six",ConfigRules.Clamp(new NavConfig{DockTransitMps=9}).DockTransitMps==6);
        foreach(double a in new[]{.01,.05,.296,.74,1d})
        foreach(var axis in new[]{Vector3D.Forward,Vector3D.Backward,Vector3D.Left,Vector3D.Right,Vector3D.Up,Vector3D.Down})
        {
            double elapsed=0,maxCapture=0,maxNear=0,minGap=200;var pos=axis*200;var vel=Vector3D.Zero;
            bool bounded=true;
            for(int i=0;i<180000;i++)
            {
                double gap=Vector3D.Dot(pos,axis);
                var error=axis*.1-pos;
                double cap=DockingMath.SpeedLimit(DockStage.Capture,gap,0,a,6,1);
                var accel=DockingMath.ApproachAcceleration(error,vel,axis,a,cap,true);
                bounded&=accel.Length()<=a+1e-8&&cap<=6;
                vel+=accel/60;pos+=vel/60;elapsed+=1d/60;
                gap=Vector3D.Dot(pos,axis);minGap=Math.Min(gap,minGap);
                if(gap<=3)maxCapture=Math.Max(maxCapture,vel.Length());
                if(gap<=10)maxNear=Math.Max(maxNear,vel.Length());
                if(error.Length()<.025&&vel.Length()<.01)break;
            }
            if(axis==Vector3D.Forward)Console.WriteLine("PACE | a="+a+" time="+elapsed+" gap="+minGap+" error="+(pos-axis*.1).Length()+" bounded="+bounded+" near="+maxNear+" capture="+maxCapture);
            Check("Adaptive final leg settles without passing mating plane a="+a+" axis="+axis,bounded&&minGap>=.07&&(pos-axis*.1).Length()<.03);
            Check("Actual near/capture velocities taper before zone boundaries a="+a+" axis="+axis,maxCapture<=.27&&maxNear<=1.02);
            if(a>=.296)Check("200 m final leg materially beats 0.25 m/s crawl a="+a+" axis="+axis,elapsed<300);
        }
        Check("Lateral correction retains capture speed",DockingMath.SpeedLimit(DockStage.Capture,100,.3,.3,6,1)==.25);
        Check("Lower configured transit ceiling is respected",DockingMath.SpeedLimit(DockStage.Capture,100,0,.3,.2,1)==.2);
        Check("Prepare shows RCS hold",DockingMath.SpeedLimit(DockStage.Prepare,100,0,.3,6,1)==0);
        foreach(double dt in new[]{1d/60,1d/30,.1})
        {
            var position=new Vector3D(125,70,-30);var velocity=Vector3D.Zero;double elapsed=0,peak=0;
            for(int i=0;i<30000;i++)
            {
                var command=DockingMath.ApproachAcceleration(-position,velocity,Vector3D.Backward,.296,5,false);
                velocity+=command*dt;position+=velocity*dt;elapsed+=dt;peak=Math.Max(peak,velocity.Length());
                if(position.Length()<.5&&velocity.Length()<.15)break;
            }
            Console.WriteLine("TRANSIT | dt="+dt+" time="+elapsed+" position="+position.Length()+" speed="+velocity.Length()+" peak="+peak);
            Check("Faster diagonal transit reaches docking stage handoff under RCS braking dt="+dt,position.Length()<.5&&velocity.Length()<.15&&peak<=5.01&&elapsed<100);
        }
    }
}
