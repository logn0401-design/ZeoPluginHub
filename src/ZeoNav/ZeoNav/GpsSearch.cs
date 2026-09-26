using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeoNav
{
    internal static class GpsSearch
    {
        internal static List<GpsDto> Filter(IEnumerable<GpsDto> source, string query)
        {
            string prefix = (query ?? "").Trim();
            return (source ?? Enumerable.Empty<GpsDto>()).Where(g => g != null &&
                (g.Name ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        // Coordinates distinguish duplicate names; changing distance does not change identity.
        internal static bool Same(GpsDto a, GpsDto b)
        {
            return a != null && b != null && a.Name == b.Name && a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        }
        internal static int SelectedIndex(IList<GpsDto> choices, GpsDto selected)
        {
            for (int i = 0; i < choices.Count; i++) if (Same(choices[i], selected)) return i;
            return -1;
        }
        internal static bool SameList(IList<GpsDto> a, IList<GpsDto> b)
        {
            if(a==null || b==null) return (a==null || a.Count==0) && (b==null || b.Count==0);
            if(a.Count!=b.Count) return false;
            for(int i=0;i<a.Count;i++) if(!Same(a[i],b[i])) return false;
            return true;
        }
    }
}
