using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
namespace ZeoPDC
{
    [DataContract] internal sealed class LabEndpoint
    {
        [DataMember] public string schema="zeo.pdc.lab.v1",session,version,utc;
        [DataMember] public int port,pid;
    }
    [DataContract] internal sealed class LabAck
    {
        [DataMember] public string request_id,status,detail,utc,ship_id;
        [DataMember] public int revision;
    }
    [DataContract] internal sealed class LabStatus
    {
        [DataMember] public string utc,session,ship_id,state,pending_request,folder,fault,damage_status,deadline_utc;
        [DataMember] public int revision,baseline_complete,baseline_required=5;
        [DataMember] public bool clear_boundary,recording;
        [DataMember] public string damage_records,damage_dropped,damage_key_drops,journal_errors;
        [DataMember] public PdcConfig effective;
        [DataMember] public LabGunSample[] guns;
        [DataMember] public LabAck last_ack;
    }
    public sealed partial class Plugin
    {
        string labSession=Guid.NewGuid().ToString("N"),labState="DISARMED",labShip="",labStopReason="";
        DateTime labPing,labDeadline,labPendingDeadline;
        PdcCommand labPending;
        PdcConfig labPendingConfig;
        LabAck labLastAck;
        readonly Dictionary<string,LabAck> labAcks=new Dictionary<string,LabAck>();
        readonly Queue<string> labAckOrder=new Queue<string>();
        static void AtomicLab<T>(string path,T value,bool immutable=false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); byte[] bytes=JsonIo.ToBytes(value);
            if(immutable) { using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read)) { f.Write(bytes,0,bytes.Length); f.Flush(true); } return; }
            string temp=path+".tmp"; File.WriteAllBytes(temp,bytes);
            if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
        }
        void InitializeLab()
        {
            AtomicLab(Path.Combine(dataDir,"lab-endpoint.json"),new LabEndpoint { session=labSession,version=Version,port=commandPort,pid=gamePid,utc=DateTime.UtcNow.ToString("o") });
        }
        LabAck Ack(PdcCommand cmd,string status,string detail)
        {
            var a=new LabAck { request_id=cmd?.RequestId,status=status,detail=detail,revision=engine.LabRevision,utc=DateTime.UtcNow.ToString("o"),ship_id=engine.LabShipId };
            labLastAck=a;
            if(!string.IsNullOrEmpty(cmd?.RequestId)) {
                if(!labAcks.ContainsKey(cmd.RequestId)) { labAckOrder.Enqueue(cmd.RequestId); if(labAckOrder.Count>256) labAcks.Remove(labAckOrder.Dequeue()); }
                labAcks[cmd.RequestId]=a;
            }
            engine.LabLog.Write("command",frame,engine.LabRevision,a); engine.LabLog.Flush(); return a;
        }
        void HandleLabCommand(PdcCommand cmd,IPEndPoint sender)
        {
            LabAck result;
            try {
                if(!IPAddress.IsLoopback(sender.Address) || cmd.Session!=labSession || string.IsNullOrWhiteSpace(cmd.RequestId) || cmd.RequestId.Length>80)
                    result=new LabAck { status="REJECTED",detail="Session or request ID mismatch" };
                else if(labAcks.TryGetValue(cmd.RequestId,out result)) { }
                else if(cmd.Type=="LAB_STATUS" || cmd.Type=="LAB_PING") { labPing=DateTime.UtcNow; result=Ack(cmd,"OK",labState); }
                else if(cmd.Type=="LAB_CANCEL") { if(labPending!=null) Ack(labPending,"CANCELLED","Cancelled by user"); labPending=null; labPendingConfig=null; result=Ack(cmd,"CANCELLED","Pending transaction cancelled"); }
                else if(cmd.ShipId!=engine.LabShipId || string.IsNullOrEmpty(cmd.ShipId)) result=Ack(cmd,"REJECTED","Controlled ship mismatch");
                else if(cmd.Revision!=engine.LabRevision) result=Ack(cmd,"REJECTED","Configuration revision changed; read status");
                else if(cmd.Type=="LAB_ROLLBACK") { labPending=null; labPendingConfig=null; ReturnLabBaseline("USER_ROLLBACK"); result=Ack(cmd,"ROLLED_BACK","Baseline requested; verify per-gun restoration status"); }
                else if(labPending!=null) result=Ack(cmd,"REJECTED","One transaction is already pending");
                else if((cmd.Type=="LAB_START" || cmd.Type=="LAB_START_BEST") && (!config.LabEnabled || labState=="SAFE_BASELINE")) {
                    labPending=cmd; labPendingConfig=cmd.Type=="LAB_START_BEST"?LabSettings.ProvenBest(config):LabSettings.Baseline(config); labPendingDeadline=DateTime.UtcNow.AddMinutes(3);
                    result=Ack(cmd,"QUEUED","Waiting for fresh clear boundary to start five baseline groups");
                } else if(cmd.Type=="LAB_PATCH" && config.LabEnabled && labState!="SAFE_BASELINE") {
                    labPendingConfig=LabSettings.Apply(config,cmd.Patch,engine.LabSamples().Select(x=>long.Parse(x.gun)));
                    labPending=cmd; labPendingDeadline=DateTime.UtcNow.AddMinutes(engine.LabBaselineComplete<5?10:3);
                    engine.LabLog.Write("requested_patch",frame,engine.LabRevision,cmd);
                    result=Ack(cmd,"QUEUED","Applies after five comparable baseline groups and a clear boundary");
                } else result=Ack(cmd,"REJECTED","Command unavailable in "+labState);
            } catch(Exception ex) { result=Ack(cmd,"REJECTED",ex.Message); }
            try { var bytes=JsonIo.ToBytes(result); rx.Send(bytes,bytes.Length,sender); } catch { }
        }
        void ServiceLab()
        {
            var now=DateTime.UtcNow;
            if(config.LabEnabled && labState!="SAFE_BASELINE" &&
                (now>=labDeadline || now-labPing>TimeSpan.FromMinutes(2) || engine.LabShipId!=labShip || !string.IsNullOrEmpty(engine.LabFault)))
                ReturnLabBaseline(now>=labDeadline?"SESSION_DEADLINE":now-labPing>TimeSpan.FromMinutes(2)?"CONTROLLER_HEARTBEAT_EXPIRED":"CONTEXT_OR_RESTORE_FAILURE");
            if(labPending!=null && now>=labPendingDeadline) { Ack(labPending,"EXPIRED","No eligible boundary before transaction deadline"); labPending=null; labPendingConfig=null; }
            if(labPending!=null && labPending.ShipId!=engine.LabShipId) { Ack(labPending,"REJECTED","Ship changed while queued"); labPending=null; labPendingConfig=null; }
            if(labPending!=null && engine.LabCanApply && ((labPending.Type=="LAB_START" || labPending.Type=="LAB_START_BEST") || engine.LabBaselineComplete>=5)) {
                var cmd=labPending;
                // Rebase the requested fields on current settings so an intervening
                // HUD edit or battery rescan is not overwritten by a queued snapshot.
                try {
                    var next=(cmd.Type=="LAB_START" || cmd.Type=="LAB_START_BEST")?(cmd.Type=="LAB_START_BEST"?LabSettings.ProvenBest(config):LabSettings.Baseline(config)):LabSettings.Apply(config,cmd.Patch,engine.LabSamples().Select(x=>long.Parse(x.gun)));
                    if(!engine.ReleaseManagerHardware("LAB_BOUNDARY") || !engine.ReleaseLabHardware("CONFIG_BOUNDARY")) throw new InvalidOperationException("Owned restoration not verified");
                    if((cmd.Type=="LAB_START" || cmd.Type=="LAB_START_BEST")) {
                        if(engine.TestArmed) engine.AbortTest(); // recording only; defense and attacker continue
                        config=next; engine.CaptureArmRequest(config); engine.ArmTest();
                        if(!engine.TestArmed) throw new InvalidOperationException("Recorder could not arm");
                        engine.LabBegin(); labPing=now; labDeadline=now.AddHours(2); labShip=engine.LabShipId;
                        labState="BASELINE"; labStopReason="";
                    }
                    int rev=(cmd.Type=="LAB_START" || cmd.Type=="LAB_START_BEST")?1:engine.LabRevision+1;
                    AtomicLab(Path.Combine(engine.LabLog.Folder,"config-"+rev.ToString("D6")+".json"),next,true);
                    config=next; AtomicLab(configPath,config); engine.LabChanged(rev);
                    if(cmd.Type!="LAB_START" && cmd.Type!="LAB_START_BEST") labState="EXPERIMENT";
                    Ack(cmd,"APPLIED","Configuration committed; hardware application/readback follows in gun samples");
                } catch(Exception ex) { Ack(cmd,"FAILED",ex.Message); if(config.LabEnabled) ReturnLabBaseline("TRANSACTION_FAILED"); }
                labPending=null; labPendingConfig=null;
            }
            if(labState=="BASELINE" && engine.LabBaselineComplete>=5) labState="READY_FOR_EXPERIMENT";
            if(frame%30==0) {
                var s=new LabStatus { utc=now.ToString("o"),session=labSession,ship_id=engine.LabShipId,state=labState,pending_request=labPending?.RequestId,
                    folder=engine.LabLog.Folder,fault=string.IsNullOrEmpty(labStopReason)?engine.LabFault:labStopReason+" / "+engine.LabFault,damage_status=engine.LabDamageStatus,revision=engine.LabRevision,
                    baseline_complete=engine.LabBaselineComplete,clear_boundary=engine.LabCanApply,recording=engine.TestArmed,
                    damage_records=engine.LabDamageRecords.ToString(),damage_dropped=engine.LabDroppedDamage.ToString(),damage_key_drops=engine.LabDamageKeyDrops.ToString(),
                    journal_errors=engine.LabLog.WriteErrors.ToString(),effective=config,guns=engine.LabSamples(),last_ack=labLastAck,
                    deadline_utc=config.LabEnabled?labDeadline.ToString("o"):null };
                AtomicLab(Path.Combine(dataDir,"lab-status.json"),s);
            }
        }
        void ReturnLabBaseline(string reason)
        {
            if(!config.LabEnabled) return;
            bool restored=engine.ReleaseLabHardware(reason);
            var baseline=LabSettings.Baseline(config); int revision=engine.LabRevision+1;
            if(engine.LabLog.Folder!=null) AtomicLab(Path.Combine(engine.LabLog.Folder,"config-"+revision.ToString("D6")+".json"),baseline,true);
            config=baseline; engine.LabChanged(revision); AtomicLab(configPath,config);
            labState="SAFE_BASELINE"; labPending=null; labPendingConfig=null;
            labStopReason=reason; engine.StopLabRecording();
            Log("LAB returned to native/preaim/ROF baseline: "+reason+" restored="+restored);
        }
    }
}

