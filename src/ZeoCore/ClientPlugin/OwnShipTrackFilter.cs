using System;
using System.Collections.Generic;
namespace ZeoCore
{
    // Exact identities only: nearby/docked ships must never be mistaken for self.
    internal sealed class OwnShipTrackFilter
    {
        private readonly HashSet<long> _grids=new HashSet<long>();
        private readonly Dictionary<long,bool> _resolved=new Dictionary<long,bool>();
        internal void Reset(long ownGrid){_grids.Clear();_resolved.Clear();if(ownGrid!=0)_grids.Add(ownGrid);}
        internal void AddGrid(long id){if(id!=0)_grids.Add(id);}
        internal bool Contains(long entityId,Func<long,long> resolveGrid)
        {
            if(entityId==0||_grids.Count==0)return false;
            if(_grids.Contains(entityId))return true;
            bool result;if(_resolved.TryGetValue(entityId,out result))return result;
            long grid=resolveGrid==null?entityId:resolveGrid(entityId);result=grid!=0&&_grids.Contains(grid);
            if(_resolved.Count<512)_resolved[entityId]=result;
            return result;
        }
    }
}
