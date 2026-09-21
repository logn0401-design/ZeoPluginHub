using System;
using System.Collections.Generic;
using VRageMath;
using ZeoNav;
internal static partial class Tests
{
    private static Vector3D Turn(Vector3D vector,Vector3D axis,double degrees) {return Vector3D.TransformNormal(vector,MatrixD.CreateFromAxisAngle(axis,degrees*Math.PI/180));}
    private static void AttitudeTests()
    {
        Vector3D f=Vector3D.Forward,command;
        var hold=new PrecisionAim();
        Check("Large turn remains with NavOS",!hold.TryRate(f,Turn(f,Vector3D.Up,20),Vector3D.Zero,out command));
        Check("Precision acquires small angle",hold.TryRate(f,Turn(f,Vector3D.Up,2),Vector3D.Zero,out command));
        Check("Precision hysteresis holds at four degrees",hold.TryRate(f,Turn(f,Vector3D.Up,4),Vector3D.Zero,out command));
        Check("Large disturbance returns to NavOS",!hold.TryRate(f,Turn(f,Vector3D.Up,6),Vector3D.Zero,out command));
        Check("Frozen 180 remains with NavOS",!hold.TryRate(f,-f,Vector3D.Zero,out command));
        foreach(var axis in new[]{Vector3D.Right,Vector3D.Up,Vector3D.Forward}) {
            var omega=axis*(1.6*Math.PI/180);hold.TryRate(f,f,omega,out command);
            Check("Aligned angular motion is opposed on "+axis,Vector3D.Dot(command,omega)<0);
        }
        foreach(double sign in new[]{-1d,1d})foreach(var axis in new[]{Vector3D.Right,Vector3D.Up}) {
            var target=Turn(f,axis,sign*.3);hold.TryRate(f,target,Vector3D.Zero,out command);
            Check("Correction turns toward target",Vector3D.Dot(command,Vector3D.Cross(f,target))>0);
        }
        hold.TryRate(f,f,Vector3D.Zero,out command);Check("Settled hold commands zero",command==Vector3D.Zero);
        Check("Invalid velocity cannot enter precision",!hold.TryRate(f,f,new Vector3D(double.NaN,0,0),out command));
        Check("Crossing heading at 1.6 deg/s is not settled",!PrecisionAim.Settled(.07,.25,new Vector3D(0,1.6*Math.PI/180,0)));
        Check("Slow aligned ship can advance phase",PrecisionAim.Settled(.07,.25,new Vector3D(0,.1*Math.PI/180,0)));
        foreach(double angle in new[]{0d,.5,1.3,2.7}) {
            MatrixD gyro=MatrixD.CreateRotationX(angle)*MatrixD.CreateRotationY(angle*.7)*MatrixD.CreateRotationZ(angle*.3);
            Vector3D rate=new Vector3D(.01,-.02,.03);
            Vector3D local=PrecisionAim.GyroCommand(rate,gyro);
            Check("Arbitrary gyro mounting preserves world damping direction",Vector3D.Distance(-Vector3D.TransformNormal(local,gyro),rate)<1e-10);
        }
        // Synthetic first-order gyro response with delayed commands, not a game flight.
        foreach(double lag in new[]{.1,.3,.8})foreach(int delay in new[]{0,3,9,15})foreach(double initial in new[]{-.45,.45}) {
            double error=initial*Math.PI/180,omega=1.6*Math.PI/180,dt=1d/60;
            var queue=new Queue<double>(); for(int i=0;i<delay;i++)queue.Enqueue(0);
            var control=new PrecisionAim();double tailError=0,tailRate=0;
            for(int tick=0;tick<1800;tick++) {
                Vector3D target=Turn(f,Vector3D.Up,error*180/Math.PI),rate;
                bool precision=control.TryRate(f,target,new Vector3D(0,omega,0),out rate);
                double requested=precision ? rate.Y : 0;
                queue.Enqueue(requested); double applied=queue.Dequeue();
                omega+=(applied-omega)*Math.Min(1,dt/lag);error-=omega*dt;
                if(tick>=1500){tailError=Math.Max(tailError,Math.Abs(error));tailRate=Math.Max(tailRate,Math.Abs(omega));}
            }
            Check("Delayed-rate model settles lag="+lag+" delay="+delay+" initial="+initial,
                tailError<.05*Math.PI/180 && tailRate<.05*Math.PI/180);
        }
    }
}
