using System;
using System.Threading;

namespace ZeoOverlay
{
    // Coalesce packet notifications into one UI callback. RenderTick reads the
    // latest packet, not the packet that originally requested the callback.
    internal sealed class MarkerRenderWakeup
    {
        private int _pending;

        internal void Request(Action<Action> enqueue, Action render)
        {
            if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0) return;
            try
            {
                enqueue(delegate
                {
                    try { render(); }
                    finally { Interlocked.Exchange(ref _pending, 0); }
                });
            }
            catch
            {
                Interlocked.Exchange(ref _pending, 0);
                // The form may be closing or not have a handle yet. The normal
                // render timer remains the recovery path for missed wakeups.
            }
        }
    }
}
