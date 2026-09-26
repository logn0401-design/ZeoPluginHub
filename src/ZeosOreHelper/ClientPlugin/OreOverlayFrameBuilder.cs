using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace ZeosOreHelper
{
    internal sealed class OreOverlayFrameBuilder
    {
        private readonly Dictionary<long,int> _numbers=new Dictionary<long,int>();
        private long _sequence;
        internal long SelectedEntityId { get; private set; }

        internal OreOverlayFrame Build(VoxelSurveyor surveyor,HudSettings s)
        {
            var w=GameWindowState.Capture();
            var search=Plugin.Instance?.Search;var learning=Plugin.Instance?.Learning;bool modern=search!=null&&!search.Legacy;var selectedOres=HudSettings.KnownOres.Where(s.IsOreEnabled).ToArray();
            var f=new OreOverlayFrame
            {
                Version=Plugin.Version,Sequence=++_sequence,UtcMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),HelperEnabled=s.Enabled,StreamerMode=s.StreamerMode,
                GameWindowValid=w.Valid,GameLeft=w.Left,GameTop=w.Top,GameWidth=w.Width,GameHeight=w.Height,GameFocused=w.Focused,
                VisibleCount=surveyor==null?0:surveyor.VisibleRoidCount,ReadyCount=surveyor==null?0:surveyor.ReadyCount,PendingCount=surveyor==null?0:surveyor.PendingCount,ErrorCount=surveyor==null?0:surveyor.ErrorCount,CachedCount=surveyor==null?0:surveyor.CachedCount
            };
            if(surveyor==null||!s.Enabled){SelectedEntityId=0;f.SearchMessage="Scanning is OFF";return f;}
            int below=0;

            var list=new List<OreOverlayRoid>();
            foreach(var r in surveyor.Records)
            {
                if(r==null||r.Skipped)continue;
                bool pinnedBypass=!modern&&r.Pinned&&s.PinnedAlwaysVisible;
                if(!pinnedBypass)
                {
                    if(r.Distance+0.001<s.MinimumDistanceMeters)continue;
                    if(!s.MaxLoadedRange&&r.Distance-0.001>s.SurveyRangeMeters)continue;
                    if(r.MaxDimensionMeters+0.001<s.MinimumDiameterMeters)continue;
                    if(s.MaximumDiameterMeters>0&&r.MaxDimensionMeters-0.001>s.MaximumDiameterMeters)continue;
                    if(r.State==VoxelSurveyor.SurveyState.NoOre&&!s.ShowNoOre)continue;
                    if(r.State==VoxelSurveyor.SurveyState.Ready&&!surveyor.GradeMeetsMinimum(r))continue;
                    bool finished=r.State==VoxelSurveyor.SurveyState.Ready||r.State==VoxelSurveyor.SurveyState.NoOre;
                    if(!modern&&finished&&s.WantedOnly&&!surveyor.MatchesWantedOreMode(r))continue;
                }

                bool off; Vector2D sp=ScreenUtils.WorldToScreen(r.Position,out off); off=off||ScreenUtils.IsOutsideHud(sp);
                bool projectionValid=!double.IsNaN(sp.X)&&!double.IsNaN(sp.Y)&&!double.IsInfinity(sp.X)&&!double.IsInfinity(sp.Y);
                if(!projectionValid){sp=Vector2D.Zero;off=true;}else {sp.X=Math.Max(-10000,Math.Min(10000,sp.X));sp.Y=Math.Max(-10000,Math.Min(10000,sp.Y));}
                var native=modern&&search.Values.B("PreferSdxScans",true)?r.SdxScan:null;
                var ores=EligibleOres(r,s,modern,native);
                ZeoOreShared.OreMatch match=null;
                if(modern){match=ZeoOreShared.OreSearch.Match(ores.Select(o=>new ZeoOreShared.OreMeasure{Ore=o.Ore,Volume=o.EstimatedVolume,Percent=o.PercentOfSolid}),selectedOres,s.RequireAllWantedOres,native!=null?Plugin.Instance.SdxLearning:learning,search);if(!match.HasWanted||(search.HideLow&&!match.Qualifies)){below++;continue;}if(match.Best!=null)ores=ores.OrderByDescending(o=>o.Ore==match.Best.Ore).ToList();}
                var top=ores.Count>0?ores[0]:null; var second=ores.Count>1?ores[1]:null;
                bool listEligible=(!modern&&r.Pinned)||r.State!=VoxelSurveyor.SurveyState.Ready||top==null||s.GetOreShowList(top.Ore);
                bool pingEligible=(!modern&&r.Pinned)||r.State!=VoxelSurveyor.SurveyState.Ready||top==null||s.GetOreShowPing(top.Ore);
                list.Add(new OreOverlayRoid
                {
                    EstimatedVolume=top==null?0:top.EstimatedVolume,ScanStatus=native!=null?"SDX2 EST":r.Verified?"VERIFIED SCAN":r.HistoricalLead&&r.State==VoxelSurveyor.SurveyState.Pending?"HISTORICAL":"ESTIMATED",MeetsThreshold=match==null||match.Qualifies,SearchRank=match==null?0:match.Rank,
                    EntityId=r.EntityId,ScreenX=sp.X,ScreenY=sp.Y,Offscreen=off,Pinned=r.Pinned,ListEligible=listEligible,PingEligible=pingEligible&&projectionValid,
                    State=StateName(r.State),Grade=HudSettings.NormalizeGrade(r.Grade,"X"),MustHit=r.MustHit,DistanceMeters=r.Distance,DiameterMeters=r.MaxDimensionMeters,Quality=r.QualityIndex,
                    TopOre=top==null?"":top.Ore,TopOrePercent=top==null?0:top.PercentOfSolid,SecondOre=second==null?"":second.Ore,SecondOrePercent=second==null?0:second.PercentOfSolid,
                    GradeColor=GradeColor(s,r),OreColor=top==null?s.HudAccentColor:s.GetOreColor(top.Ore)
                });
            }

            list.Sort(delegate(OreOverlayRoid a,OreOverlayRoid b)
            {
                if(a.Pinned!=b.Pinned)return b.Pinned.CompareTo(a.Pinned);
                int ar=StateRank(a.State),br=StateRank(b.State);if(ar!=br)return br.CompareTo(ar);
                if(modern){if(search.Values.Get("SearchSort")=="Nearest matching")return a.DistanceMeters.CompareTo(b.DistanceMeters);int rank=b.SearchRank.CompareTo(a.SearchRank);if(rank!=0)return rank;return a.EntityId.CompareTo(b.EntityId);}
                if(s.RankingMode=="nearest")return a.DistanceMeters.CompareTo(b.DistanceMeters);
                var ra=surveyor.GetRecord(a.EntityId);var rb=surveyor.GetRecord(b.EntityId);double av=ra==null?0:surveyor.GetRankValue(ra),bv=rb==null?0:surveyor.GetRankValue(rb);
                if(Math.Abs(av-bv)>0.0001)return bv.CompareTo(av);
                int ag=HudSettings.GradeRank(a.Grade),bg=HudSettings.GradeRank(b.Grade);if(ag!=bg)return bg.CompareTo(ag);
                return a.DistanceMeters.CompareTo(b.DistanceMeters);
            });

            AssignNumbers(list);
            var selected=FindSelected(list);SelectedEntityId=selected==null?0:selected.EntityId;f.SelectedEntityId=SelectedEntityId;if(selected!=null)selected.Selected=true;
            
            int detailMax=search==null?3:Math.Max(0,search.Values.I("PingDetailLimit",3));int offscreenMax=search==null?2:Math.Max(0,search.Values.I("MaxOffscreenPings",2));double maxDistance=search==null?1000000:Math.Max(0,search.Values.D("PingMaxDistanceKm",1000))*1000;
            var budget=new ZeoOreShared.OrePingBudget(s.MaxMarkers,offscreenMax,detailMax);
            f.QualifyingCount=list.Count(r=>r.MeetsThreshold);
            if(Plugin.Instance!=null)foreach(var d in Plugin.Instance.Deposits.Project(surveyor,s,search)){if(!budget.Take(false))break;d.DetailLabel=budget.Detail();f.Deposits.Add(d);}
            // Selection and pins take priority INSIDE the cap; neither bypasses it.
            foreach(var roid in list.OrderByDescending(r=>r.Selected).ThenByDescending(r=>r.Pinned)){
                bool eligible=s.MarkersEnabled&&roid.PingEligible&&roid.DistanceMeters<=maxDistance&&(!roid.Offscreen||s.PingOffscreenArrows)&&(!modern||selectedOres.Length>0);
                if(eligible&&budget.Take(roid.Offscreen)){roid.PingEligible=true;roid.DetailLabel=budget.Detail();}else roid.PingEligible=false;
            }
            if(list.Count>100){var keep=list.Where(r=>r.PingEligible||r.Selected).Concat(list).Distinct().Take(100).ToList();list=list.Where(keep.Contains).ToList();}
            int pingCount=budget.Count;f.ShownPings=pingCount;
            f.SearchMessage=selectedOres.Length==0?"Select at least one ore":list.Count==0?"No matches: check ore selection, range, grade and minimums. "+below+" below search filters.":pingCount+" shown / "+f.QualifyingCount+" qualify | "+(modern?search.Values.Get("SearchSort","Most estimated ore"):"Legacy quality")+(learning!=null&&selectedOres.Any(ore=>learning.Best(ore).Count<3)?" | Learning: verify top results":"");
            if(s.MinimumDistanceMeters>0)f.SearchMessage+=" | "+ZeoOreShared.OreSearchHints.MinimumRange(s.MinimumDistanceMeters);
            f.Roids=list;return f;
        }

        private static List<VoxelSurveyor.OreStat> EligibleOres(VoxelSurveyor.RoidRecord r,HudSettings s,bool modern=false,ZeoOreShared.SdxScanData native=null)
        {
            var o=new List<VoxelSurveyor.OreStat>();
            var source=native==null?r.Ores:native.Ore.Select(p=>new VoxelSurveyor.OreStat{Ore=p.Key,EstimatedVolume=p.Value,PercentOfSolid=p.Value/native.Total*100}).ToList();
            for(int i=0;i<source.Count;i++){var x=source[i];if(!s.IsOreEnabled(x.Ore)||(!modern&&s.GetOreWeight(x.Ore)<=0))continue;if(x.PercentOfSolid+0.000001<s.EffectiveOreMinimumPercent(x.Ore))continue;o.Add(x);}
            o.Sort((a,b)=>{int w=s.GetOreWeight(b.Ore).CompareTo(s.GetOreWeight(a.Ore));if(w!=0)return w;return b.PercentOfSolid.CompareTo(a.PercentOfSolid);});return o;
        }
        private void AssignNumbers(List<OreOverlayRoid> list)
        {
            var current=new HashSet<long>(list.Select(r=>r.EntityId));foreach(var id in _numbers.Keys.Where(k=>!current.Contains(k)).ToArray())_numbers.Remove(id);
            var live=new HashSet<long>();for(int i=0;i<list.Count;i++){live.Add(list[i].EntityId);int n;if(!_numbers.TryGetValue(list[i].EntityId,out n)){n=FindFree();if(n>0)_numbers[list[i].EntityId]=n;}list[i].Number=n;}
            var dead=_numbers.Keys.Where(k=>!live.Contains(k)).ToList();for(int i=0;i<dead.Count;i++)_numbers.Remove(dead[i]);
        }
        private int FindFree(){var u=new bool[101];foreach(var n in _numbers.Values)if(n>0&&n<=100)u[n]=true;for(int i=1;i<=100;i++)if(!u[i])return i;return 0;}
        private static OreOverlayRoid FindSelected(List<OreOverlayRoid> list){OreOverlayRoid best=null;double bd=0.014*0.014;for(int i=0;i<list.Count;i++){var v=list[i];if(v.Offscreen)continue;double d=v.ScreenX*v.ScreenX+v.ScreenY*v.ScreenY;if(d<bd){bd=d;best=v;}}return best;}
        private static string StateName(VoxelSurveyor.SurveyState s){if(s==VoxelSurveyor.SurveyState.Ready)return"READY";if(s==VoxelSurveyor.SurveyState.NoOre)return"NO ORE";if(s==VoxelSurveyor.SurveyState.Pending)return"SCANNING";if(s==VoxelSurveyor.SurveyState.NoStorage)return"NO STORAGE";return"READ ERR";}
        private static int StateRank(string s){if(s=="READY") return 4;if(s=="NO ORE") return 3;if(s=="SCANNING") return 2;return 1;}
        private static string GradeColor(HudSettings s,VoxelSurveyor.RoidRecord r){if(r.State==VoxelSurveyor.SurveyState.Pending)return s.ScanningColor;if(r.State==VoxelSurveyor.SurveyState.NoStorage||r.State==VoxelSurveyor.SurveyState.ReadError)return s.ErrorColor;string g=HudSettings.NormalizeGrade(r.Grade,"X");if(g=="S")return s.GradeSColor;if(g=="A")return s.GradeAColor;if(g=="B")return s.GradeBColor;if(g=="C")return s.GradeCColor;if(g=="D")return s.GradeDColor;return s.GradeXColor;}
    }
}
