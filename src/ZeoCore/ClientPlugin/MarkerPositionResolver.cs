using System;
using VRageMath;

namespace ZeoCore
{
    internal static class MarkerPositionResolver
    {
        // Resolve only an existing track's exact entity identity. Never enumerate
        // nearby entities or alter the sensor/fusion track to make a marker fit.
        internal static Vector3D Resolve(HudTrack track, HudMarkerAnchor anchor,
            double predictionAge, double staleSeconds, Func<long, Vector3D?> readLivePosition)
        {
            bool local = track.Source == HudTrackSource.WeaponCore || track.Source == HudTrackSource.Spectrum;
            bool liveEligible = anchor != HudMarkerAnchor.DetectionPosition && track.EntityId != 0 &&
                track.HasPosition && !track.IsDistress && !track.Stale && track.AgeSeconds <= staleSeconds &&
                (local || track.SameSector);
            if (liveEligible && readLivePosition != null)
            {
                Vector3D? live = readLivePosition(track.EntityId);
                if (live.HasValue && Finite(live.Value)) return live.Value;
            }

            // The fallback was already advanced to the last fusion tick. Only
            // advance by the additional render age; never predict a live anchor.
            if (predictionAge > 0 && !double.IsInfinity(predictionAge) && !double.IsNaN(predictionAge) && Finite(track.Velocity))
                return track.Position + track.Velocity * predictionAge;
            return track.Position;
        }

        private static bool Finite(Vector3D value)
        {
            return !(double.IsNaN(value.X) || double.IsInfinity(value.X) ||
                double.IsNaN(value.Y) || double.IsInfinity(value.Y) ||
                double.IsNaN(value.Z) || double.IsInfinity(value.Z));
        }
    }
}
