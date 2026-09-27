using System;
using Sandbox.ModAPI;
using VRageMath;

namespace ZeoNav
{
    // Measurements belong to one ship, axis and standard gyro bank. Never infer
    // braking capability from mass or rated torque alone.
    internal sealed class TurnResponse
    {
        internal double Acceleration, Deceleration;
        internal int Stops;
        internal bool Ready { get { return Stops>=2 && Acceleration>.0005 && Deceleration>.0005; } }
        internal void Accel(double speed,double seconds)
        {
            if(!Valid(speed,seconds))return;
            double value=Math.Min(25,speed/seconds*.5);
            Acceleration=Acceleration<=0?value:Math.Min(Acceleration,value);
        }
        internal void Stop(double speed,double seconds)
        {
            if(!Valid(speed,seconds))return;
            double value=Math.Min(25,speed/seconds*.5);
            Deceleration=Deceleration<=0?value:Math.Min(Deceleration,value);Stops++;
        }
        private static bool Valid(double speed,double seconds)
        {return SignalBudget.Finite(speed)&&SignalBudget.Finite(seconds)&&speed>=.005&&seconds>=1d/120&&seconds<=30;}
        internal double Rate(double radians)
        {
            double brake=Ready?Deceleration:.08;
            const double response=.30;
            return Math.Min(Ready?2.5:.35,Math.Max(0,-brake*response+Math.Sqrt(brake*brake*response*response+2*brake*Math.Max(0,radians-.015))));
        }
        internal double Allowance
        {
            get {
                if(!Ready)return 180;
                double peak=Math.Min(2.5,Math.Sqrt(2*Math.PI/(1/Acceleration+1/Deceleration)));
                double ramps=peak*peak*.5*(1/Acceleration+1/Deceleration);
                return 2+1.5*(peak/Acceleration+peak/Deceleration+Math.Max(0,Math.PI-ramps)/peak);
            }
        }
    }

    internal sealed partial class ShipContext
    {
        private TurnResponse[] turnResponses={new TurnResponse(),new TurnResponse(),new TurnResponse()};
        private readonly PassiveTurnLearner passiveTurn = new PassiveTurnLearner();
        private long turnHardware;
        private double turnMass,turnChecked=-100,measuredAdaptiveFlip;
        private int preferredTurnAxis=1;
        private int calibrationStage=-1,calibrationAxis;
        private double calibrationStart,calibrationLeg,calibrationStopSpeed;
        private bool calibrationAccel;
        private Vector3D calibrationOrigin,calibrationWorldAxis;
        private static double TurnNow { get { return MyAPIGateway.Session.GameplayFrameCounter/60d; } }
        private int AxisIndex(Vector3D axis)
        {
            if(Math.Abs(Vector3D.Dot(axis,Controller.WorldMatrix.Right))>.85)return 0;
            if(Math.Abs(Vector3D.Dot(axis,Controller.WorldMatrix.Up))>.85)return 1;
            return 2;
        }
        private void CheckTurnHardware()
        {
            if(turnResponses==null)turnResponses=new[]{new TurnResponse(),new TurnResponse(),new TurnResponse()};
            if(MyAPIGateway.Session==null||Controller==null||Grid==null)return;
            double now=TurnNow;if(now-turnChecked<.5)return;turnChecked=now;
            preferredTurnAxis=AxisIndex(ChooseHalfTurnAxis(false));
            long key=TopologyRevision;
            unchecked {foreach(var g in Gyros)key=key*31+g.EntityId+(g.IsFunctional&&!g.Closed?1:0)+(long)(g.GyroPower*1000);key=key*31+Grid.Min.GetHashCode();key=key*31+Grid.Max.GetHashCode();}
            double mass=Mass;
            if(key!=turnHardware || turnMass<=0 || Math.Abs(mass-turnMass)>turnMass*.05)
            {
                turnResponses=new[]{new TurnResponse(),new TurnResponse(),new TurnResponse()};
                passiveTurn.Reset();
                calibrationStage=-1;TurnAllowanceSeconds=180;measuredAdaptiveFlip=0;turnHardware=key;turnMass=mass;
            }
        }
        internal bool AdaptiveTurnReady
        {get {CheckTurnHardware();return FlipTurnMode!="RCS"&&turnResponses[preferredTurnAxis].Ready;}}
        internal double AdaptiveTurnAllowance
        {get {return AdaptiveTurnReady?Math.Max(measuredAdaptiveFlip*1.25,turnResponses[preferredTurnAxis].Allowance):Math.Max(180,TurnAllowanceSeconds);}}
        private double AdaptiveRate(Vector3D axis,double radians)
        {
            CheckTurnHardware();
            // Assisted banks have different torque; they do not inherit standard-bank learning.
            return rcsTurnActive?Math.Min(.35,Math.Sqrt(2*.08*radians)):turnResponses[AxisIndex(axis)].Rate(radians);
        }
        private void ObservePassiveTurn(Vector3D axis,Vector3D forward,
            Vector3D angular,Vector3D requested)
        {
            if(rcsTurnActive||calibrationStage>=0||MyAPIGateway.Session==null)return;
            try
            {
                var look=Controller.RotationIndicator;
                if(look.LengthSquared()>.0004 || Math.Abs(Controller.RollIndicator)>.02)
                { passiveTurn.Reset(); return; }
            }
            catch { passiveTurn.Reset(); return; }
            CheckTurnHardware();
            int index=AxisIndex(axis);
            bool wasReady=turnResponses[index].Ready;
            if(passiveTurn.Observe(TurnNow,forward,axis,angular,requested,turnResponses[index]) &&
                !wasReady && turnResponses[index].Ready)
            {
                TurnAllowanceSeconds=Math.Max(TurnAllowanceSeconds,turnResponses[index].Allowance);
                log("TURN PASSIVE READY // axis="+index+" accel="+turnResponses[index].Acceleration.ToString("0.000")+
                    "rad/s2 brake="+turnResponses[index].Deceleration.ToString("0.000")+"rad/s2");
            }
        }
        internal void CancelTurnCheck(){calibrationStage=-1;}
        // A bounded gyro-only probe before accelerating from rest avoids learning
        // the first flip while already committed to a high-speed approach.
        internal bool PrepareRouteTurn()
        {
            CheckTurnHardware();if(FlipTurnMode=="RCS")return true;
            if(AdaptiveTurnReady){calibrationStage=-1;return true;}
            double now=TurnNow;
            if(calibrationStage<0)
            {
                calibrationWorldAxis=ChooseHalfTurnAxis();calibrationAxis=AxisIndex(calibrationWorldAxis);
                calibrationOrigin=Controller.WorldMatrix.Forward;calibrationStart=calibrationLeg=now;
                calibrationAccel=false;calibrationStage=0;log("TURN CHECK START // standard bank / thrust off");
            }
            if(now-calibrationStart>40)throw new InvalidOperationException("Turn check could not settle; check gyro authority before retrying.");
            if(!gyroControlActive)BeginGyroControl();
            rcsTurnActive=false;ApplyGyroControlState();
            var velocity=Controller.GetShipVelocities().AngularVelocity;
            double speed=Math.Abs(Vector3D.Dot(velocity,calibrationWorldAxis));
            var response=turnResponses[calibrationAxis];
            if(calibrationStage==0||calibrationStage==2)
            {
                if(!calibrationAccel&&speed>=.30){response.Accel(speed,now-calibrationLeg);calibrationAccel=true;}
                bool stop=calibrationStage==0?((now-calibrationLeg>=1&&speed>=.30)||ForwardAngleDegrees(calibrationOrigin)>=15||now-calibrationLeg>=8):ForwardAngleDegrees(calibrationOrigin)<=2||now-calibrationLeg>=8;
                if(stop){if(!calibrationAccel)response.Accel(speed,now-calibrationLeg);calibrationStopSpeed=speed;calibrationLeg=now;calibrationStage++;CommandTurnBank(Vector3D.Zero);}
                else CommandTurnBank(calibrationWorldAxis*(calibrationStage==0?.35:-.35));
            }
            else
            {
                CommandTurnBank(Vector3D.Zero);
                if(velocity.Length()<=.015 && now-calibrationLeg>=1d/60)
                {
                    response.Stop(calibrationStopSpeed,now-calibrationLeg);
                    if(calibrationStage==1){calibrationStage=2;calibrationLeg=now;calibrationAccel=false;}
                    else if(response.Ready)
                    {
                        calibrationStage=-1;TurnAllowanceSeconds=response.Allowance;
                        log("TURN CHECK READY // accel="+response.Acceleration.ToString("0.00")+"rad/s2 brake="+response.Deceleration.ToString("0.00")+"rad/s2 allowance="+response.Allowance.ToString("0.00")+"s");return true;
                    }
                    else throw new InvalidOperationException("Turn check response too weak or incomplete; check gyros before retrying.");
                }
            }
            AimMode="TURN CHECK";return false;
        }
    }

    internal static class ArrivalBraking
    {
        internal static double Deceleration(double closing,double distance,double planned,double available,double reserve)
        {
            if(!SignalBudget.Finite(closing)||!SignalBudget.Finite(distance)||!SignalBudget.Finite(planned)||!SignalBudget.Finite(available))return Math.Max(0,available);
            double envelope=Math.Sqrt(2*Math.Max(.01,planned)*Math.Max(0,distance-reserve));
            // Coast while genuinely outside the braking envelope; smoothly converge
            // onto its speed curve with headroom for disturbances and model error.
            return Math.Max(0,Math.Min(available,planned+(Math.Max(0,closing)-envelope)/2));
        }
    }
}
