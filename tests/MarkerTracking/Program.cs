using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using VRageMath;
using ZeoCore;
using ZeoOverlay;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        checks++;
    }
    private static bool Near(Vector3D a, Vector3D b) { return Vector3D.DistanceSquared(a,b) < 1e-12; }
    private static HudTrack Track()
    {
        return new HudTrack { EntityId=42, TrackId=17, Key="S:42", Source=HudTrackSource.Spectrum,
            Position=new Vector3D(100,20,30), Velocity=new Vector3D(60,0,0), HasPosition=true };
    }
    private static void Anchors()
    {
        var track=Track();
        Vector3D original=track.Position;
        int lookups=0;
        Vector3D actual=original;
        Func<long,Vector3D?> read=id => { Check(id==42,"Lookup must use exact tracked entity ID"); lookups++; return actual; };
        // Hold sensor/fusion sample constant while the ship moves for 15 frames.
        // Include zero prediction (the user's current setting).
        for(int i=0;i<15;i++)
        {
            actual=new Vector3D(100+i*2,20+i,30);
            var marker=MarkerPositionResolver.Resolve(track,HudMarkerAnchor.Auto,i%2==0 ? 0 : .1,10,read);
            Check(Near(marker,actual),"Marker must use each new live transform without velocity overshoot");
        }
        Check(lookups==15,"Every marker projection reads current position");
        Check(Near(track.Position,original) && track.EntityId==42 && track.TrackId==17 && track.Key=="S:42",
            "Rendering must not mutate sensor position or stable identity");
        lookups=0;
        var predicted=original+track.Velocity*.2;
        Check(Near(MarkerPositionResolver.Resolve(track,HudMarkerAnchor.DetectionPosition,.2,10,read),predicted),"Detection-position preference retained");
        Check(lookups==0,"Detection-position preference must not resolve live entities");
        Check(Near(MarkerPositionResolver.Resolve(track,HudMarkerAnchor.Auto,.2,10,id=>null),predicted),"Unloaded entity uses existing prediction");
        Check(Near(MarkerPositionResolver.Resolve(track,HudMarkerAnchor.Auto,0,10,id=>null),original),"Prediction OFF stays OFF for sensor-only data");
        Check(Near(MarkerPositionResolver.Resolve(track,HudMarkerAnchor.Auto,.2,10,id=>new Vector3D(double.NaN,0,0)),predicted),"Invalid live transform falls back safely");
        foreach(var kind in new[]{"stale","old","distress","cross-sector","no-position","zero-id"})
        {
            var t=Track();
            if(kind=="stale")t.Stale=true;
            if(kind=="old")t.AgeSeconds=11;
            if(kind=="distress")t.IsDistress=true;
            if(kind=="cross-sector") { t.Source=HudTrackSource.FleetContact; t.SameSector=false; }
            if(kind=="no-position")t.HasPosition=false;
            if(kind=="zero-id")t.EntityId=0;
            MarkerPositionResolver.Resolve(t,HudMarkerAnchor.Auto,.2,10,id=> { throw new Exception("Ineligible lookup: "+kind); });
            Check(true,"Ineligible live anchor rejected: "+kind);
        }
        track.Source=HudTrackSource.FleetFriendly; track.SameSector=true;
        Check(Near(MarkerPositionResolver.Resolve(track,HudMarkerAnchor.GridCenter,.2,10,read),actual),"Replicated same-sector friendly uses current position");
        track.Source=HudTrackSource.WeaponCore; track.SameSector=false;
        Check(Near(MarkerPositionResolver.Resolve(track,HudMarkerAnchor.Auto,.2,10,read),actual),"Local WeaponCore track uses current position");
    }
    private static void Wakeups()
    {
        var gate=new MarkerRenderWakeup();
        var queue=new Queue<Action>();
        int latest=0, rendered=-1;
        for(int i=1;i<=100;i++) { latest=i; gate.Request(a=>queue.Enqueue(a),()=>rendered=latest); }
        Check(queue.Count==1,"Packet burst queues only one render");
        queue.Dequeue()();
        Check(rendered==100,"Render reads newest packet instead of queued old packet");
        gate.Request(a=>queue.Enqueue(a),()=>rendered=101);
        queue.Dequeue()();
        Check(rendered==101,"A later packet can schedule another render");
        gate.Request(a=>{throw new InvalidOperationException("closing form");},()=>{});
        gate.Request(a=>queue.Enqueue(a),()=>{});
        Check(queue.Count==1,"Scheduling failure releases pending gate"); queue.Dequeue()();
        gate.Request(a=>queue.Enqueue(a),()=>{throw new InvalidOperationException("render failure");});
        try { queue.Dequeue()(); } catch(InvalidOperationException) { }
        gate.Request(a=>queue.Enqueue(a),()=>{});
        Check(queue.Count==1,"Render failure releases pending gate"); queue.Dequeue()();
        var concurrent=new ConcurrentQueue<Action>();
        Parallel.For(0,200,i=>gate.Request(a=>concurrent.Enqueue(a),()=>{}));
        Check(concurrent.Count==1,"Concurrent requests remain bounded to one callback");
    }
    private static int Main()
    {
        try { Anchors(); Wakeups(); Console.WriteLine("PASS: "+checks+" live-anchor and packet-wakeup assertions."); return 0; }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
