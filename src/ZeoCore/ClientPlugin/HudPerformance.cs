using System;
using System.Diagnostics;
using System.Globalization;
namespace ZeoCore
{
    // Aggregate timings only: no player IDs, coordinates, names or payloads.
    internal sealed class HudPerformance
    {
        private readonly double[] total=new double[3], maximum=new double[3];
        private readonly int[] count=new int[3];
        private long last=Stopwatch.GetTimestamp();
        internal void Record(int area,long start) { RecordMilliseconds(area,(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency); }
        internal void RecordMilliseconds(int area,double milliseconds) {
            total[area]+=milliseconds; maximum[area]=Math.Max(maximum[area],milliseconds);count[area]++;
        }
        internal string Flush() {
            long now=Stopwatch.GetTimestamp();if((now-last)/(double)Stopwatch.Frequency<30)return null;
            last=now;string result="PERF HUD window 30s";string[] labels={"spectrum","fusion","projection"};
            for(int i=0;i<3;i++){
                result+=" "+labels[i]+"="+(count[i]==0?0:total[i]/count[i]).ToString("0.000",CultureInfo.InvariantCulture)+"ms avg/"+maximum[i].ToString("0.000",CultureInfo.InvariantCulture)+"ms max n="+count[i];
                total[i]=maximum[i]=0;count[i]=0;
            }
            return result;
        }
    }
}
