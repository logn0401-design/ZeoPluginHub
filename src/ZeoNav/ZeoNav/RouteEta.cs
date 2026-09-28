using System;
namespace ZeoNav
{
    // Arrival prediction only; never issues commands or changes a braking boundary.
    internal static class RouteEta
    {
        internal struct Result
        {
            internal double Seconds, Acceleration, Coast, TurnAndBrake, Peak;
            internal static Result Unknown { get { return new Result {Seconds=-1}; } }
        }
        internal static Result Plan(double distance,double speed,double cap,Func<double,double> acceleration,
            double brake,double turn,double terminalSpeed,double terminalAccel,double radius,double arrivalSpeed)
        {
            if(!SignalBudget.Finite(distance)||distance<0||brake<=.001||terminalAccel<=.001||cap<=0)return Result.Unknown;
            double tail=Math.Min(distance,Math.Max(30,terminalSpeed*terminalSpeed/terminalAccel));
            double main=distance-tail,end=Math.Min(terminalSpeed,Math.Sqrt(2*terminalAccel*Math.Max(0,tail-radius)));
            double terminal=Terminal(tail,end,terminalSpeed,terminalAccel,radius,arrivalSpeed);
            if(main<=.01)return new Result {Seconds=Terminal(distance,speed,terminalSpeed,terminalAccel,radius,arrivalSpeed)};
            const int count=256;
            double step=main/count,x=0,v=Math.Max(0,speed),ta=0,tc=0;
            for(int i=0;i<count;i++)
            {
                double a=acceleration(x+step*.5);
                if(!SignalBudget.Finite(a)||a<=.001)return Result.Unknown;
                double remaining=main-x;
                double stop=Math.Max(0,(v*v-end*end)/(2*brake))+v*turn;
                if(stop>=remaining)
                {
                    double tb=turn+Math.Max(0,(v-end)/brake);
                    return new Result {Seconds=ta+tc+tb+terminal,Acceleration=ta,Coast=tc,TurnAndBrake=tb,Peak=v};
                }
                double next=Math.Max(v,Math.Min(cap,Math.Sqrt(v*v+2*a*step)));
                if(Math.Max(0,(next*next-end*end)/(2*brake))+next*turn>remaining-step)
                {
                    double lo=0,hi=step;
                    for(int j=0;j<24;j++)
                    {
                        double dx=(lo+hi)*.5,n=Math.Max(v,Math.Min(cap,Math.Sqrt(v*v+2*a*dx)));
                        if(Math.Max(0,(n*n-end*end)/(2*brake))+n*turn>remaining-dx)hi=dx;else lo=dx;
                    }
                    double delta=(lo+hi)*.5;
                    next=Math.Max(v,Math.Min(cap,Math.Sqrt(v*v+2*a*delta)));
                    AddTime(v,next,delta,a,ref ta,ref tc);
                    double tb=turn+Math.Max(0,(next-end)/brake);
                    return new Result {Seconds=ta+tc+tb+terminal,Acceleration=ta,Coast=tc,TurnAndBrake=tb,Peak=next};
                }
                AddTime(v,next,step,a,ref ta,ref tc);v=next;x+=step;
            }
            return new Result {Seconds=ta+tc+terminal,Acceleration=ta,Coast=tc};
        }
        private static void AddTime(double v,double next,double distance,double a,ref double ta,ref double tc)
        {
            ta+=Math.Max(0,(next-v)/a);
            double covered=Math.Max(0,(next*next-v*v)/(2*a));
            tc+=Math.Max(0,distance-covered)/Math.Max(.001,next);
        }
        internal static double Terminal(double distance,double speed,double cap,double authority,double radius,double arrivalSpeed)
        {
            if(authority<=.001||cap<=0)return -1;
            double d=Math.Max(0,distance-radius),settle=Math.Max(0,(Math.Max(0,speed)-arrivalSpeed)/authority);
            if(d==0)return settle+.25;
            // Integrate the same distance-dependent desired-speed taper as live
            // terminal control, instead of treating the final approach as a cruise.
            double travel=0,step=d/64;
            for(int i=0;i<64;i++)
            {
                double remaining=radius+(i+.5)*step;
                double desired=Math.Min(cap,remaining*.22);
                if(remaining<30)desired=Math.Min(desired,3);
                travel+=step/Math.Max(.01,desired);
            }
            double response=Math.Min(cap,Math.Sqrt(authority*d))/authority;
            return Math.Max(settle,travel+response)+1.2+.25;
        }
        internal static Result RemainingBrake(double distance,double speed,double brake,double turn,
            double terminalSpeed,double terminalAccel,double radius,double arrivalSpeed)
        {
            if(brake<=.001||terminalAccel<=.001)return Result.Unknown;
            double end=Math.Min(terminalSpeed,Math.Max(0,speed));
            if(speed<=end)return new Result {Seconds=Terminal(distance,speed,terminalSpeed,terminalAccel,radius,arrivalSpeed)};
            double tail=Math.Min(distance,Math.Max(30,terminalSpeed*terminalSpeed/terminalAccel));
            double burn=Math.Max(0,(speed-end)/brake),travel=speed*turn+(speed+end)*.5*burn;
            double coast=Math.Max(0,distance-tail-travel)/Math.Max(.001,speed);
            double terminal=Terminal(Math.Max(0,distance-travel-speed*coast),end,terminalSpeed,terminalAccel,radius,arrivalSpeed);
            return new Result {Seconds=coast+turn+burn+terminal,Coast=coast,TurnAndBrake=turn+burn,Peak=speed};
        }
    }
}
