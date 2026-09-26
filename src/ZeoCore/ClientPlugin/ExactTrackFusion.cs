using System;
using System.Collections.Generic;
using System.Globalization;
namespace ZeoCore
{
    // Same-sector contacts only. Never equate reporters, names or nearby positions.
    internal sealed class ExactTrackFusion
    {
        private readonly Dictionary<string,HudTrack> _index=new Dictionary<string,HudTrack>(StringComparer.Ordinal);
        private readonly Dictionary<long,long> _canonical=new Dictionary<long,long>();
        private readonly HashSet<HudTrack> _live=new HashSet<HudTrack>();
        private readonly List<HudTrack> _matches=new List<HudTrack>(4);
        private readonly List<HudTrack> _result=new List<HudTrack>(192);
        internal static bool Conflicts(long? first,long? second){return first.HasValue&&second.HasValue&&first.Value!=second.Value;}
        private static string Entity(long id){return "E:"+id.ToString(CultureInfo.InvariantCulture);}
        internal static void Capture(HudTrack track)
        {
            if(track.IdentityAliases==null)track.IdentityAliases=new List<string>(4);
            if(track.EntityId!=0)Add(track.IdentityAliases,Entity(track.EntityId));
            long emitter;
            if(long.TryParse(track.RawEmitterId,NumberStyles.Integer,CultureInfo.InvariantCulture,out emitter)&&emitter!=0)
                Add(track.IdentityAliases,"S:"+emitter.ToString(CultureInfo.InvariantCulture));
            else if((track.Source==HudTrackSource.Spectrum||track.Source==HudTrackSource.FleetSignal)&&track.EntityId!=0)
                Add(track.IdentityAliases,"S:"+track.EntityId.ToString(CultureInfo.InvariantCulture));
        }
        private static void Add(List<string> aliases,string key){if(!aliases.Contains(key))aliases.Add(key);}
        internal static void Remember(HudTrack keeper,HudTrack other)
        {
            Capture(keeper);Capture(other);
            for(int i=0;i<other.IdentityAliases.Count;i++)Add(keeper.IdentityAliases,other.IdentityAliases[i]);
        }
        private void Normalize(HudTrack track,Func<long,long> resolve)
        {
            Capture(track);int count=track.IdentityAliases.Count;
            for(int i=0;i<count;i++){
                long id;if(!long.TryParse(track.IdentityAliases[i].Substring(2),out id)||id==0)continue;
                long canonical;
                if(!_canonical.TryGetValue(id,out canonical)){canonical=resolve==null?id:resolve(id);_canonical[id]=canonical;}
                // Both raw emitter and grid aliases can resolve to an exact loaded construct.
                if(canonical!=0)Add(track.IdentityAliases,Entity(canonical));
            }
        }
        private static int Rank(HudTrack t,double staleSeconds)
        {
            if(t.Stale)return 0;
            if(FriendlyTrackPolicy.FreshNetworkFriendly(t,staleSeconds))return 6;
            if(t.Source==HudTrackSource.Spectrum)return 5;
            if(t.Source==HudTrackSource.WeaponCore)return 4;
            if(t.Source==HudTrackSource.FleetFriendly)return 3;
            return 2;
        }
        internal int Apply(List<HudTrack> tracks,Func<long,long> resolve,Action<HudTrack,HudTrack> merge,double staleSeconds=10)
        {
            _index.Clear();_canonical.Clear();_live.Clear();_result.Clear();int before=tracks.Count;
            for(int i=0;i<tracks.Count;i++){
                var incoming=tracks[i];if(incoming==null)continue;
                // A distress report is its own event, not an interchangeable sensor observation.
                bool local=incoming.Source==HudTrackSource.Spectrum||incoming.Source==HudTrackSource.WeaponCore;
                if(incoming.IsDistress||(!local&&!incoming.SameSector)){_live.Add(incoming);continue;}
                Normalize(incoming,resolve);_matches.Clear();HudTrack winner=incoming;
                for(int a=0;a<incoming.IdentityAliases.Count;a++){
                    HudTrack found;if(!_index.TryGetValue(incoming.IdentityAliases[a],out found)||_matches.Contains(found))continue;
                    _matches.Add(found);
                    if(Rank(found,staleSeconds)>Rank(winner,staleSeconds)||(Rank(found,staleSeconds)==Rank(winner,staleSeconds)&&found.AgeSeconds<=winner.AgeSeconds))winner=found;
                }
                if(!ReferenceEquals(winner,incoming)){merge(winner,incoming);Remember(winner,incoming);}
                for(int a=0;a<_matches.Count;a++){
                    var old=_matches[a];if(ReferenceEquals(old,winner))continue;
                    merge(winner,old);Remember(winner,old);_live.Remove(old);
                }
                _live.Add(winner);
                for(int a=0;a<winner.IdentityAliases.Count;a++)_index[winner.IdentityAliases[a]]=winner;
            }
            // Retain original order; never renumber all visible tracks because one joined/left.
            for(int i=0;i<tracks.Count;i++)if(tracks[i]!=null&&_live.Remove(tracks[i]))_result.Add(tracks[i]);
            tracks.Clear();tracks.AddRange(_result);return before-tracks.Count;
        }
    }
}
