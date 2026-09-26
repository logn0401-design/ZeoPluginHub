using System;
using System.Linq;
using System.Collections.Generic;
namespace ZeoOreShared {
 internal sealed class OreMeasure {internal string Ore{get;set;}internal double Volume{get;set;}internal double Percent{get;set;}}
 internal sealed class OreMatch {internal bool Qualifies,HasWanted,Learning;internal OreMeasure Best;internal double Threshold,Benchmark,Rank;}
 internal static class OreSearch {
  internal static OreMatch Match(IEnumerable<OreMeasure> measures,string[] selected,bool requireAll,OreLearningStore store,OreSearchConfig config){
   bool richness=config.Values.Get("SearchSort")=="Richest ore";var available=measures.Where(m=>selected.Contains(m.Ore,StringComparer.OrdinalIgnoreCase)&&OreLearningStore.Finite(m.Volume)&&OreLearningStore.Finite(m.Percent)&&m.Volume>0).ToArray();var result=new OreMatch{HasWanted=available.Length>0};if(selected.Length==0||available.Length==0)return result;
   int good=0;double bestScore=-1;bool bestPass=false;foreach(var measure in available){var benchmark=store==null?new OreBenchmark():store.Best(measure.Ore);double best=richness?benchmark.Percent:benchmark.Volume;double amount=richness?measure.Percent:measure.Volume;bool learning=benchmark.Count<3||best<=0;double threshold=learning?0:best*config.Threshold(measure.Ore);bool passes=amount>=threshold; if(passes)good++;
    // Pick a qualifying ore before a larger nonqualifying ore, then sort by the selected physical metric.
    double score=amount;if(result.Best==null||(passes&&!bestPass)||(passes==bestPass&&score>bestScore)){bestScore=score;bestPass=passes;result.Best=measure;result.Threshold=threshold;result.Benchmark=best;result.Rank=amount;result.Learning=learning;}
   }result.Qualifies=requireAll?good==selected.Length:good>0;return result;
  }
 }
}
