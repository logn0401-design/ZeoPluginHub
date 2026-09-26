using System;
using System.Diagnostics;
using System.Globalization;
namespace Zeo.Performance
{
    // Aggregate wall time and process-wide GC counts, never contacts or locations.
    internal sealed class UpdateProfiler
    {
        private readonly string name;
        private readonly Action<string> log;
        private readonly double[] total=new double[2],max=new double[2];
        private readonly int[] count=new int[2];
        private long window=Stopwatch.GetTimestamp();
        private int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);
        internal UpdateProfiler(string name,Action<string> log){this.name=name;this.log=log;}
        internal void Record(long start,bool alt)
        {
            long now=Stopwatch.GetTimestamp();double ms=(now-start)*1000.0/Stopwatch.Frequency;
            int i=alt?1:0;total[i]+=ms;max[i]=Math.Max(max[i],ms);count[i]++;
            if((now-window)/(double)Stopwatch.Frequency<30)return;
            string text="PERF UPDATE "+name+" wall-ms";
            for(i=0;i<2;i++)text+=" "+(i==1?"alt":"normal")+"="+(count[i]==0?0:total[i]/count[i]).ToString("0.000",CultureInfo.InvariantCulture)+"avg/"+max[i].ToString("0.000",CultureInfo.InvariantCulture)+"max n="+count[i];
            int a=GC.CollectionCount(0),b=GC.CollectionCount(1),c=GC.CollectionCount(2);
            text+=" process-GC="+(a-gc0)+"/"+(b-gc1)+"/"+(c-gc2);
            gc0=a;gc1=b;gc2=c;window=now;
            Array.Clear(total,0,2);Array.Clear(max,0,2);Array.Clear(count,0,2);
            log(text);
        }
    }
}
