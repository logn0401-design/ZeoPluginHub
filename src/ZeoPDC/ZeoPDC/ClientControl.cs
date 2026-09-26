using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Sandbox.ModAPI;

namespace ZeoPDC
{
    internal sealed class ClientGunState
    {
        public ClientCapability Capability=new ClientCapability();
        public ClientSettingLease Lease=new ClientSettingLease();
        public bool Return,Failed;
        public string mode="";
    }
    [DataContract] internal sealed class ClientDebugGun
    {
        [DataMember] public string gun,name,acquired,shooting_status,profile_status;
        [DataMember] public bool? shooting,ready;
        [DataMember] public double? heat,effective_rpm;
        [DataMember] public ClientCapability capability;
        [DataMember] public ClientSettingLease setting;
    }
    [DataContract] internal sealed class ClientDebug
    {
        [DataMember] public string utc,version,ship,state,mode,folder;
        [DataMember] public int endpoints;
        [DataMember] public bool stable_projectile_api,shot_monitor_api,targeted_request_api;
        [DataMember] public string evidence="SERVER_STATE_MATCHED is replica revision evidence, not command-ID acknowledgement, remote aim proof or physical hit proof";
        [DataMember] public ClientDebugGun[] guns;
    }
    internal sealed partial class PdcEngine
    {
        readonly List<Gun> clientOwned=new List<Gun>();
        readonly LabJournal clientJournal=new LabJournal();
        DateTime clientNextDebug;
        string clientState="OBSERVE",clientRecordedState="",clientRunKey="";
        internal static bool JoiningServer()
        {try{return MyAPIGateway.Session!=null&&MyAPIGateway.Multiplayer!=null&&!MyAPIGateway.Multiplayer.IsServer;}catch{return true;}}
        bool ClientAccess(Gun g)
        {try{return g.Block!=null&&!g.Block.Closed&&MyAPIGateway.Session?.Player!=null&&g.Block.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId);}catch{return false;}}
        double? ClientRead(Gun g,string key)
        {
            if(key=="Weapon ROF"||key=="Weapon Range"){float f;return TryReadTerminal(g.Block,key,out f)?(double?)f:null;}
            bool b;return TryReadTerminal(g.Block,key,out b)?(double?)(b?1:0):null;
        }
        bool ClientWrite(Gun g,string key,double value)
        {
            if(!ClientAccess(g))return false;
            return key=="Weapon ROF"||key=="Weapon Range"?SetTerminal(g.Block,key,(float)value):SetTerminal(g.Block,key,value>.5);
        }
        void ClientEvent(string kind,Gun g=null)
        {
            if(clientJournal.Folder==null)clientJournal.Open(Path.Combine(dataDir,"ClientDiagnostics"));
            clientJournal.Write(kind,frame,0,new ClientDebug {utc=DateTime.UtcNow.ToString("o"),version=Plugin.Version,ship=Id(grid==null?0:grid.EntityId),state=clientState,mode=config().ClientMode,
                guns=g==null?new ClientDebugGun[0]:new[]{ClientSample(g)}});
            clientJournal.Flush();
        }
        ClientDebugGun ClientSample(Gun g)
        {return new ClientDebugGun {gun=Id(g.EntityId),name=g.Name,acquired=g.NativeProjectile.ToString(),shooting_status=g.ShootingRead,shooting=g.ShootingRead=="OK"?(bool?)g.ShootingValue:null,
            ready=g.ReadyRead=="OK"?(bool?)g.ReadyValue:null,heat=g.HeatKnown?(double?)g.Heat:null,effective_rpm=g.Profile.Valid?(double?)g.Profile.EffectiveRpm:null,
            profile_status=g.Profile.Status,capability=g.Client.Capability,setting=g.Client.Lease};}
        void ObserveClientGun(Gun g,DateTime now)
        {
            try {g.Client.Capability=ClientCapability.Read(wc.NativeObserver.Component(g.Entity),g.Part);}
            catch {g.Client.Capability=new ClientCapability {status="COMPONENT_READ_FAILED"};}
            g.Client.Capability.gun=Id(g.EntityId);
            var l=g.Client.Lease;var before=l.status;
            l.Observe(g.Client.Capability,string.IsNullOrEmpty(l.key)?null:ClientRead(g,l.key),now);
            if(l.status!=before)ClientEvent("setting_observation",g);
        }
        void ServiceClientReturns()
        {
            var now=DateTime.UtcNow;
            foreach(var old in clientOwned.ToArray()) {
                var g=guns.FirstOrDefault(x=>ReferenceEquals(x.Client,old.Client))??old;
                if(!ReferenceEquals(g,old)){clientOwned.Remove(old);clientOwned.Add(g);}
                if(!guns.Contains(g))g.Client.Return=true;
                if(!g.Client.Return)continue;
                ObserveClientGun(g,now);var l=g.Client.Lease;var before=l.status;
                l.Restore(g.Client.Capability,ClientRead(g,l.key),now,v=>ClientWrite(g,l.key,v));
                if(l.status!=before)ClientEvent("restoration",g);
                if(!l.owned){g.Client.Return=false;clientOwned.Remove(g);}
            }
        }
        void ReleaseClientControl(string why)
        {
            clientState=why;
            foreach(var g in clientOwned)g.Client.Return=true;
            ServiceClientReturns();
        }
        void ApplyClientControl()
        {
            var c=config();var now=DateTime.UtcNow;
            foreach(var g in guns)ObserveClientGun(g,now);
            string runKey=(grid==null?"":Id(grid.EntityId))+"/"+wc.Generation+"/"+c.ClientMode;
            if(clientRunKey!=runKey){ReleaseClientControl("MODE_OR_GENERATION_CHANGE");clientRunKey=runKey;foreach(var g in guns)if(!g.Client.Lease.owned){g.Client.Lease=new ClientSettingLease();g.Client.Failed=false;}}
            ServiceClientReturns();
            if(c.ClientMode=="ADAPTIVE ROF"&&c.ControlEnabled&&c.ManagedDefenseEnabled&&!c.LabEnabled&&!fireLockout&&stableSensorHealthy) {
                clientState="CLIENT ADAPTIVE ROF / DISTRIBUTION UNMODIFIED";
                foreach(var g in guns){
                    var s=g.Client;
                    bool eligible=ClientAccess(g)&&g.Functional&&FreshProfile(g)&&g.Profile.Adjustable&&g.NativeControlKnown&&!g.NativeManual&&s.Capability.authority=="REMOTE_CLIENT"&&s.Capability.own_overrides==true;
                    if(!eligible||s.Failed||s.Return){if(s.Lease.owned)s.Return=true;continue;}
                    if(s.Lease.blocked){s.Failed=true;s.Return=true;continue;}
                    ResolveManagedCadence(g);if(!g.ManagedRofWrite)continue;
                    if(s.Lease.restoring)continue;
                    if(s.mode!="ADAPTIVE ROF"&&s.Lease.owned){s.Return=true;continue;}
                    s.mode="ADAPTIVE ROF";var before=s.Lease.sent_utc;
                    double desired=Math.Max(g.Profile.MinimumRof,Math.Min(1,g.Rof));
                    s.Lease.Request("Weapon ROF",desired,s.Capability,ClientRead(g,"Weapon ROF"),now,v=>ClientWrite(g,"Weapon ROF",v));
                    if(s.Lease.owned&&!clientOwned.Contains(g))clientOwned.Add(g);
                    if(before!=s.Lease.sent_utc)ClientEvent("rof_requested",g);
                    g.CommandResult=s.Lease.status;
                }
            }else {
                foreach(var g in clientOwned)g.Client.Return=true;
                if(!clientState.StartsWith("PROBE_"))clientState="CLIENT OBSERVE / NATIVE WC";
            }
            ServiceClientReturns();
            directControlState=clientState;directControlActive=false;
            foreach(var g in guns){g.Manager.ManagerFailure="REMOTE_CLIENT_UNSUPPORTED";g.OuterState="REMOTE_PREAIM_UNVERIFIED";}
            if(clientRecordedState!=clientState){ClientEvent("state");clientRecordedState=clientState;}
            if(now>=clientNextDebug){clientNextDebug=now.AddSeconds(1);WriteClientDebug(now);}
        }
        void WriteClientDebug(DateTime now)
        {
            try {
                var snapshot=new ClientDebug {utc=now.ToString("o"),version=Plugin.Version,ship=Id(grid==null?0:grid.EntityId),state=clientState,mode=config().ClientMode,folder=clientJournal.Folder,
                    endpoints=wc.EndpointCount,stable_projectile_api=wc.StableProjectileReady,shot_monitor_api=wc.ShotMonitorReady,targeted_request_api=wc.TargetedRequestReady,
                    guns=guns.Concat(clientOwned).GroupBy(g=>g.EntityId).Select(x=>ClientSample(x.First())).ToArray()};
                string path=Path.Combine(dataDir,"client-capabilities.json"),temp=path+".tmp";
                File.WriteAllBytes(temp,JsonIo.ToBytes(snapshot));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            }catch(Exception ex){log("Client capability debug: "+ex.Message);}
        }
    }
}
