using System;
using System.Collections.Generic;
namespace ZeoOreShared {
 internal sealed class OreDepositCluster {
  internal string Ore;internal int MinX,MinY,MinZ,MaxX,MaxY,MaxZ,Cells;internal double Volume;
 }
 // Bounded, incremental six-neighbour grouping. A bounding region is NOT ore volume.
 internal sealed class OreDepositClusters {
  private struct Cell {internal string Ore;internal double Volume;}
  private readonly Dictionary<int,Cell> cells=new Dictionary<int,Cell>();
  private readonly Queue<int> frontier=new Queue<int>();private int[] keys;private int cursor;
  private OreDepositCluster current;
  internal readonly List<OreDepositCluster> Results=new List<OreDepositCluster>();
  internal int Count=>cells.Count;internal bool Complete{get;private set;}
  internal static int Key(int x,int y,int z){if(x<0||y<0||z<0||x>=512||y>=512||z>=512)throw new ArgumentOutOfRangeException();return x|(y<<9)|(z<<18);}
  internal void Add(int x,int y,int z,string ore,double volume){if(keys!=null)throw new InvalidOperationException();if(cells.Count>=200000)throw new InvalidOperationException("Deposit cell limit exceeded");if(string.IsNullOrEmpty(ore)||!OreLearningStore.Finite(volume)||volume<=0)throw new ArgumentException();cells.Add(Key(x,y,z),new Cell{Ore=ore,Volume=volume});}
  internal bool Step(int budget){
   if(keys==null){keys=new int[cells.Count];cells.Keys.CopyTo(keys,0);}
   while(budget-->0){
    if(frontier.Count==0){if(current!=null){Results.Add(current);current=null;}
     if(cursor>=keys.Length){Complete=true;return true;}
     int seed=keys[cursor++];Cell c;if(!cells.TryGetValue(seed,out c))continue;int x=seed&511,y=(seed>>9)&511,z=seed>>18;
     current=new OreDepositCluster{Ore=c.Ore,MinX=x,MaxX=x,MinY=y,MaxY=y,MinZ=z,MaxZ=z};frontier.Enqueue(seed);cells.Remove(seed);Consume(seed,c);
    }
    int key=frontier.Dequeue(),px=key&511,py=(key>>9)&511,pz=key>>18;
    if(px>0)Visit(key-1);if(px<511)Visit(key+1);if(py>0)Visit(key-512);if(py<511)Visit(key+512);if(pz>0)Visit(key-262144);if(pz<511)Visit(key+262144);
   }return false;
  }
  private void Visit(int key){Cell c;if(cells.TryGetValue(key,out c)&&c.Ore==current.Ore){cells.Remove(key);frontier.Enqueue(key);Consume(key,c);}}
  private void Consume(int key,Cell c){int x=key&511,y=(key>>9)&511,z=key>>18;current.MinX=Math.Min(current.MinX,x);current.MaxX=Math.Max(current.MaxX,x);current.MinY=Math.Min(current.MinY,y);current.MaxY=Math.Max(current.MaxY,y);current.MinZ=Math.Min(current.MinZ,z);current.MaxZ=Math.Max(current.MaxZ,z);current.Cells++;current.Volume+=c.Volume;}
 }
}
