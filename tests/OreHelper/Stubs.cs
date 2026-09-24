using System;
using System.Linq;
using System.Collections.Generic;
using ZeoOreShared;
using VRageMath;
// Only game-owned inputs are substituted. Tests link production settings, matching,
// learning, chunk cursor, frame builder, layout and renderer source.
namespace ZeosOreHelper {
 internal sealed class Plugin {
  internal static string CatalogOverlayPath=null;
  internal static string DataDirectory=System.IO.Path.GetTempPath();
  internal const string Version="1.0.4";internal static Plugin Instance;
  internal OreSearchConfig Search;internal OreLearningStore Learning,SdxLearning;internal OreDepositSurveyor Deposits=new OreDepositSurveyor();
  internal static void Log(string text){Console.WriteLine(text);}internal void NativeAction(string cmd){}internal void NativeSettingsChanged(){}
 }
 internal struct GameWindowState {
  internal bool Valid,Focused;internal int Left,Top,Width,Height;
  internal static GameWindowState Capture(){return new GameWindowState{Valid=true,Focused=true,Left=0,Top=0,Width=1920,Height=1080};}
 }
 internal static class ScreenUtils {
  internal static Vector2D WorldToScreen(Vector3D p,out bool off){off=Math.Abs(p.X)>1||Math.Abs(p.Y)>1;return new Vector2D(p.X,p.Y);}
  internal static bool IsOutsideHud(Vector2D p){return Math.Abs(p.X)>.92||Math.Abs(p.Y)>.92;}
 }
 internal sealed class VoxelSurveyor {
  internal enum SurveyState{Ready,NoOre,Pending,NoStorage,ReadError}
  internal sealed class OreStat {internal string Ore;internal double EstimatedVolume,PercentOfSolid;}
  internal sealed class RoidRecord {
   internal SdxScanData SdxScan;internal long EntityId;internal Vector3D Position;internal bool Pinned,Skipped,Verified,HistoricalLead,MustHit;
   internal double Distance,MaxDimensionMeters,QualityIndex;internal string Grade="D";internal SurveyState State=SurveyState.Ready;
   internal List<OreStat> Ores=new List<OreStat>();
  }
  internal List<RoidRecord> Records=new List<RoidRecord>();internal int VisibleRoidCount=>Records.Count;
  internal int ReadyCount=>Records.Count;internal int PendingCount=>0;internal int ErrorCount=>0;internal int CachedCount=>0;
  internal RoidRecord GetRecord(long id){return Records.FirstOrDefault(r=>r.EntityId==id);}
  internal bool GradeMeetsMinimum(RoidRecord r){return true;}internal bool MatchesWantedOreMode(RoidRecord r){return true;}
  internal double GetRankValue(RoidRecord r){return r.QualityIndex;}
 }
}

namespace ZeosOreHelper {
 internal sealed class OreDepositSurveyor {
  internal System.Collections.Generic.List<OreOverlayDeposit> Items=new System.Collections.Generic.List<OreOverlayDeposit>();
  internal System.Collections.Generic.List<OreOverlayDeposit> Project(VoxelSurveyor v,HudSettings s,OreSearchConfig c){return s.MarkersEnabled&&c!=null&&c.Values.B("DepositMarkers",true)?Items:new System.Collections.Generic.List<OreOverlayDeposit>();}
 }
}
