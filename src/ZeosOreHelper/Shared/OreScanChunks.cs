using System;
namespace ZeoOreShared {
 internal struct OreScanChunk {internal int X,Y,Z,MaxX,MaxY,MaxZ;}
 internal sealed class OreScanChunks {
  private readonly int width,height,depth,edge;private int x,y,z;
  internal OreScanChunks(int width,int height,int depth,int edge){if(width<1||height<1||depth<1||edge<1)throw new ArgumentException("Positive scan dimensions required");this.width=width;this.height=height;this.depth=depth;this.edge=edge;}
  internal OreScanChunk Current {get{if(z>=depth)throw new InvalidOperationException("Scan complete");return new OreScanChunk{X=x,Y=y,Z=z,MaxX=Math.Min(width-1,x+edge-1),MaxY=Math.Min(height-1,y+edge-1),MaxZ=Math.Min(depth-1,z+edge-1)};}}
  internal bool Advance(){x+=edge;if(x>=width){x=0;y+=edge;if(y>=height){y=0;z+=edge;}}return z<depth;}
 }
 internal sealed class OrePingBudget {
  private readonly int max,offMax,detailMax;internal int Count,OffscreenCount,Details;
  internal OrePingBudget(int max,int offscreen,int detail){this.max=Math.Max(0,Math.Min(100,max));offMax=Math.Max(0,Math.Min(100,offscreen));detailMax=Math.Max(0,Math.Min(100,detail));}
  internal bool Take(bool offscreen){if(Count>=max||(offscreen&&OffscreenCount>=offMax))return false;Count++;if(offscreen)OffscreenCount++;return true;}
  internal bool Detail(){if(Details>=detailMax)return false;Details++;return true;}
 }
}
