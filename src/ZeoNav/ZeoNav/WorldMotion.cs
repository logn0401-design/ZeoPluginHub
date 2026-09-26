using System;
using VRageMath;
namespace ZeoNav
{
    // Independent world-position cross-check. Simulation time avoids render FPS/pause bias.
    internal sealed class WorldMotion
    {
        internal Vector3D Velocity, ApiVelocity, MeasuredVelocity;
        internal bool Ready {get;private set;}
        internal string Source {get;private set;}="WAIT WORLD VELOCITY";
        internal bool RecoverableFault {get;private set;}
        internal int FaultGeneration {get;private set;}
        internal string LastFault {get;private set;}="";
        private bool modSource;
        private Vector3D modAnchor, modIntegral, lastMod;
        private double modTime, modWindow;
        private int modGood;
        internal void ObserveMod(Vector3D position,Vector3D physics,Vector3D velocity,double time,double accelerationBound)
        {
            ApiVelocity=physics;
            if(!Finite(position)||!Finite(velocity)||velocity.Length()>100000||!SignalBudget.Finite(time))
            {Reset();Source="INVALID MOD VELOCITY";FaultGeneration++;return;}
            if(!modSource){Reset();modSource=true;modAnchor=position;modIntegral=Vector3D.Zero;lastMod=velocity;modTime=modWindow=time;Source="WAIT MOD VELOCITY";RecoverableFault=true;return;}
            double dt=time-modTime;
            if(dt==0)return;
            if(dt<0||dt>1){Reset();Source="MOD VELOCITY TIME GAP";FaultGeneration++;return;}
            modIntegral+=(lastMod+velocity)*(.5*dt);lastMod=velocity;modTime=time;
            Velocity=velocity;
            double age=time-modWindow;
            if(age<.499)return;
            Vector3D displacement=position-modAnchor;
            MeasuredVelocity=displacement/age;
            double residual=(displacement-modIntegral).Length();
            double tolerance=Math.Max(150,velocity.Length()*.15)+Math.Max(0,accelerationBound)*age*age;
            bool frozen=displacement.Length()<.01&&velocity.Length()>5;
            bool bad=residual>tolerance||frozen;
            modAnchor=position;modWindow=time;modIntegral=Vector3D.Zero;
            if(bad){Ready=false;modGood=0;RecoverableFault=residual<Math.Max(1000,velocity.Length()*.3);Source="MOD / POSITION DISAGREEMENT";LastFault="modSpeed="+velocity.Length().ToString("0.0")+" residual="+residual.ToString("0.0")+"m frozen="+frozen;FaultGeneration++;return;}
            modGood++;Ready=modGood>=2;RecoverableFault=true;Source=Ready?"FLIP AND BURN / WORLD CHECK":"WAIT MOD VELOCITY";
        }
        private Vector3D anchor,previous;
        private double anchorTime,lastTime,previousTime;
        private int good;
        private bool initialized,havePrevious;
        internal void Observe(Vector3D position,Vector3D api,double time,double accelerationBound)
        {
            if(modSource)Reset();
            ApiVelocity=api;
            if(!Finite(position)||!Finite(api)||!SignalBudget.Finite(time))
            {Reset();Source="INVALID VELOCITY";LastFault="non-finite position, velocity or time";FaultGeneration++;return;}
            if(!initialized){Seed(position,api,time);return;}
            double step=time-lastTime;
            if(step<0||step>1.0){Seed(position,api,time);Source="VELOCITY TIME GAP";RecoverableFault=false;LastFault="simulation step="+step.ToString("0.000")+"s";FaultGeneration++;return;}
            lastTime=time;
            if(Ready&&Source=="PHYSICS / WORLD VERIFIED")Velocity=api;
            double dt=time-anchorTime;
            if(dt<.249)return;
            Vector3D measured=(position-anchor)/dt;
            anchor=position;anchorTime=time;
            double bound=Math.Max(1,SignalBudget.Finite(accelerationBound)?accelerationBound:1);
            // Teleports, streamed grid replacement and large corrections cannot become a burn command.
            double delta=havePrevious?(measured-previous).Length():0;
            double limit=Math.Max(75,bound*(time-previousTime)*3+10);
            if(!Finite(measured)||measured.Length()>100000||(havePrevious&&delta>limit))
            {
                RecoverableFault=Finite(measured)&&measured.Length()<=100000&&havePrevious&&delta*dt<=1000;
                LastFault="dt="+dt.ToString("0.000")+"s api="+api+" measured="+measured+" previous="+previous+
                    " delta="+delta.ToString("0.0")+"m/s limit="+limit.ToString("0.0")+"m/s residual="+(delta*dt).ToString("0.0")+"m recoverable="+RecoverableFault;
                FaultGeneration++;Seed(position,api,time);Source="WORLD MOTION DISCONTINUITY";return;
            }
            previous=measured;previousTime=time;havePrevious=true;MeasuredVelocity=measured;
            if(measured.Length()<.01&&api.Length()>5)
            {good=0;Ready=false;Source="WAIT MOVING WORLD POSITION";return;}
            good++;Ready=good>=2;
            double tolerance=Math.Max(2,Math.Max(api.Length()*.001,bound*.16));
            bool disagreement=(measured-api).Length()>tolerance;
            Velocity=disagreement?measured:api;
            Source=!Ready?"WAIT WORLD VELOCITY":disagreement?"WORLD MOTION":"PHYSICS / WORLD VERIFIED";
        }
        private void Seed(Vector3D p,Vector3D api,double time)
        {initialized=true;anchor=p;anchorTime=lastTime=time;good=0;havePrevious=false;Ready=false;Velocity=api;MeasuredVelocity=Vector3D.Zero;Source="WAIT WORLD VELOCITY";}
        internal void Reset(){modSource=false;modGood=0;initialized=false;havePrevious=false;Ready=false;good=0;RecoverableFault=false;Velocity=ApiVelocity=MeasuredVelocity=Vector3D.Zero;Source="WAIT WORLD VELOCITY";}
        internal static bool Finite(Vector3D v){return SignalBudget.Finite(v.X)&&SignalBudget.Finite(v.Y)&&SignalBudget.Finite(v.Z);}
    }

    internal static class BrakingPlan
    {
        internal static double Distance(double speed,double acceleration,double brake,double gravity,double flipSeconds,double safety)
        {
            if(!SignalBudget.Finite(speed)||!SignalBudget.Finite(acceleration)||!SignalBudget.Finite(brake)||!SignalBudget.Finite(gravity)||!SignalBudget.Finite(flipSeconds)||!SignalBudget.Finite(safety)||brake<=0||flipSeconds<0)return double.PositiveInfinity;
            speed=Math.Max(0,speed);acceleration=Math.Max(0,acceleration);gravity=Math.Max(0,gravity);
            const double reaction=2; // telemetry window, transport and burn/cutoff response
            double flipSpeed=speed+acceleration*reaction;
            double burnSpeed=flipSpeed+gravity*flipSeconds;
            return (speed*reaction+.5*acceleration*reaction*reaction+
                    flipSpeed*flipSeconds+.5*gravity*flipSeconds*flipSeconds+
                    burnSpeed*burnSpeed/(2*brake))*Math.Max(1,safety);
        }
    }
}
