using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Input;
using VRageMath;

namespace ZeoCore
{
    internal sealed partial class ZeoHudController
    {
        private ZeoConfig _targetConfig;
        private TargetMarkClient _targetClient;
        private AccountLinkClient _targetAccount;
        private readonly ZeoOverlay.QuickRefillKeyLatch _targetKey=new ZeoOverlay.QuickRefillKeyLatch();
        private string _targetContext="",_targetSession=Guid.NewGuid().ToString("N");
        private long _targetSequence,_targetExpires,_targetNext;
        private HashSet<string> _chosenIds;
        private bool _targetClear;
        private string _targetStatus="Enable target marks in FLEET, then set a key.";
        internal void TargetAction()
        {
            if(_chosenIds!=null){_chosenIds=null;_targetClear=true;_targetNext=0;Plugin.Notify("Attack target cleared",4000);return;}
            var link=_targetAccount?.Snapshot();
            Plugin.Notify(link!=null&&!link.Authorized ? (string.IsNullOrWhiteSpace(link.PairingCode)?link.State+" // "+link.Detail:"Members > ZeoCore Devices: "+link.PairingCode) : _targetStatus,12000);
        }
        private void UpdateTargets(int frame,SectorSnapshot sector,ServerTrustSnapshot trust)
        {
            var player=MyAPIGateway.Session?.Player;
            string context=sector.Known&&player!=null ? sector.Id+"|"+player.IdentityId : "";
            if(!_settings.TargetMarksEnabled||!_settings.ReceiveFleetLink)context="";
            if(context!=_targetContext){_targetContext=context;_targetSession=Guid.NewGuid().ToString("N");_chosenIds=null;_targetClear=false;_targetExpires=0;_targetNext=0;_targetClient?.Context(context);}
            if(context.Length==0){DisposeTargets();_targetKey.Poll(_settings.TargetMarkKey,_settings.TargetMarkModifier,false,false,false,false);return;}
            if(_targetClient==null&&DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()<_targetNext)return;
            try{
                if(_targetClient==null){_targetAccount=new AccountLinkClient(_targetConfig);_targetClient=new TargetMarkClient(_targetConfig);_targetClient.Context(context);}
                _targetAccount.Update(frame,trust,player.SteamUserId,player.IdentityId,player.DisplayName);
                var link=_targetAccount.Snapshot();
                if(!link.Authorized){_targetClient.Context("");_targetStatus="Target link: "+link.State;PollTargetKey(false);return;}
                if(link.EffectiveScope!="faction"&&link.EffectiveScope!="alliance"){
                    _targetClient.Context("");_targetStatus="Ask your faction leader for Faction or Alliance information access.";PollTargetKey(false);return;
                }
                if(link.VerifiedSteamId!=player.SteamUserId.ToString()||link.VerifiedIdentityId!=player.IdentityId.ToString()){
                    _targetClient.Context("");_targetStatus="Waiting for current player identity verification";return;
                }
                _targetClient.Context(context);
                // Linked account must correspond to the current SE identity; server membership owns recipient scope.
                if(!_settings.TransmitTelemetry){
                    if(_chosenIds!=null){_chosenIds=null;_targetClear=true;_targetNext=0;}
                    _targetStatus="Target sending is OFF in PRIVACY; receiving remains enabled.";
                }
                PollTargetKey(_settings.TransmitTelemetry);
                long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();if(now<_targetNext)return;
                _targetNext=now+2000;
                var local=_engine.GetHudSnapshot();if(!local.HasShip&&!local.HasObserver)return;
                var origin=local.HasShip?local.OwnPosition:local.ObserverPosition;
                var body=new Dictionary<string,object>{{"world","default"},{"server",sector.ServerId.ToString(CultureInfo.InvariantCulture)},
                    {"sector",sector.Id},{"position",_settings.TransmitTelemetry?new[]{origin.X,origin.Y,origin.Z}:new double[3]},
                    {"max_distance_m",_settings.TransmitTelemetry?_settings.MaxSharedTrackDistanceKm*1000:0},
                    {"alliance_enabled",_settings.TargetMarksAlliance}};
                HudTrack marked=null;
                if(_chosenIds!=null){
                    foreach(var t in _tracks)if(t.IdentityAliases!=null&&t.IdentityAliases.Any(_chosenIds.Contains)){marked=t;break;}
                    if(now>=_targetExpires||marked==null||marked.Stale||marked.Friendly||TrackAge(marked)>10){_chosenIds=null;_targetClear=true;marked=null;}
                }
                if(marked!=null||_targetClear){
                    var command=new Dictionary<string,object>{{"action",marked==null?"clear":"mark"},{"session_id",_targetSession},
                        {"sequence",++_targetSequence},{"simulation_tick",Math.Max(0,frame)},
                        {"configuration_revision",Plugin.Version+":a"+(_settings.TargetMarksAlliance?1:0)+":n"+_settings.MaxSharedTracks+
                            ":r"+_settings.MaxSharedTrackDistanceKm.ToString("0.###",CultureInfo.InvariantCulture)+":s"+_settings.StaleSeconds+
                            ":p"+(_settings.TargetMarkPulse?1:0)+":h"+(int)_settings.MarkerAnchor},
                        {"observed_at_ms",now-(marked==null?0:(long)(TrackAge(marked)*1000))}};
                    if(marked!=null){command["target_ids"]=_chosenIds.Take(8).ToArray();command["position"]=new[]{marked.Position.X,marked.Position.Y,marked.Position.Z};command["expires_at_ms"]=_targetExpires;}
                    body["command"]=command;
                }
                if(_targetClient.Send(body))_targetClear=false;
                if(_settings.TransmitTelemetry)_targetStatus=_targetClient.Status;
            }catch{_targetNext=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+10000;_targetStatus="Target link could not initialize. Check the linked device configuration.";}
        }
        private double TrackAge(HudTrack t){return t.AgeSeconds+Math.Max(0,(DateTime.UtcNow-_lastTrackBuildUtc).TotalSeconds);}
        private void PollTargetKey(bool authorized)
        {
            var input=MyAPIGateway.Input;var gui=MyAPIGateway.Gui;
            int key=_settings.TargetMarkKey,mod=_settings.TargetMarkModifier;
            bool down=input!=null&&key!=0&&input.IsKeyPress((MyKeys)key);
            bool allowed=gui!=null&&!gui.ChatEntryVisible&&!gui.IsCursorVisible&&Sandbox.Graphics.GUI.MyScreenManager.GetScreenWithFocus() is Sandbox.Game.Gui.MyGuiScreenGamePlay&&GameWindowState.Capture().Focused;
            bool match=input!=null&&ZeoOverlay.QuickRefillBinding.MatchModifiers(mod,input.IsAnyCtrlKeyPressed(),input.IsAnyAltKeyPressed(),input.IsAnyShiftKeyPressed());
            bool conflict=ZeoOverlay.QuickRefillBinding.Conflict(key,(int)_settings.MenuKey,_settings.DistressEnabled,(int)_settings.DistressKey,_settings.MenuKeyCode)!=null||
                (key==_settings.QuickRefillKey&&mod==_settings.QuickRefillModifier);
            if(!_targetKey.Poll(key,mod,down,match,allowed,conflict))return;
            if(!authorized){TargetAction();return;}
            HudTrack pick=null;double best=.035*.035;var camera=MyAPIGateway.Session?.Camera;
            if(camera!=null)foreach(var t in _tracks){
                if(!t.HasPosition||t.Friendly||t.IsDistress||t.Stale||TrackAge(t)>10||(!t.SameSector&&t.Source!=HudTrackSource.Spectrum&&t.Source!=HudTrackSource.WeaponCore))continue;
                if(_settings.MaxSharedTrackDistanceKm>0&&t.Distance>_settings.MaxSharedTrackDistanceKm*1000)continue;
                Vector2D p;bool off;Project(camera,MarkerPositionResolver.Resolve(t,_settings.MarkerAnchor,0,_settings.StaleSeconds,LiveMarkerPosition,_nativeSignalPosition),out p,out off);
                if(!off&&p.LengthSquared()<best){best=p.LengthSquared();pick=t;}
            }
            if(_chosenIds!=null&&(pick==null||pick.IdentityAliases!=null&&pick.IdentityAliases.Any(_chosenIds.Contains))){TargetAction();return;}
            if(pick==null){Plugin.Notify("Aim at a fresh signal to mark a target.",3000);return;}
            ExactTrackFusion.Capture(pick);_chosenIds=new HashSet<string>(pick.IdentityAliases.Take(8),StringComparer.Ordinal);
            _targetExpires=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+60000;_targetClear=false;_targetNext=0;
            Plugin.Notify("Attack target queued for sharing (60 seconds). Press again to clear.",4500);
        }
        private void AddTargetTracks(LocalHudSnapshot local,Vector3D origin)
        {
            if(_targetClient==null||!_settings.TargetMarksEnabled||!_settings.ReceiveFleetLink||_settings.MaxSharedTracks==0||!local.SectorKnown)return;
            foreach(var mark in _targetClient.Snapshot()){
                double distance=Vector3D.Distance(origin,mark.Position);
                if(_settings.MaxSharedTrackDistanceKm>0&&distance>_settings.MaxSharedTrackDistanceKm*1000)continue;
                long entity=0;string raw=null;
                foreach(var id in mark.Ids){long parsed;if(id.Length<3||!long.TryParse(id.Substring(2),out parsed)||parsed<=0)continue;if(id.StartsWith("E:"))entity=parsed;else if(id.StartsWith("S:"))raw=parsed.ToString(CultureInfo.InvariantCulture);}
                if(entity==0&&raw!=null)long.TryParse(raw,out entity);
                if(entity==0||IsOwnShipTrack(entity))continue;
                var t=new HudTrack{EntityId=entity,Key="attack:"+entity,Source=HudTrackSource.FleetSignal,Relation="unknown",Name="ATTACK TARGET",
                    RawEmitterId=raw,IdentityAliases=new List<string>(mark.Ids),AttackTarget=true,Position=mark.Position,HasPosition=true,
                    AgeSeconds=mark.Age,Distance=distance,SectorId=local.SectorId,SectorName=local.SectorName,SectorKnown=true,SameSector=true};
                if(Allowed(t))_tracks.Add(t);
            }
        }
        private void DisposeTargets(){_targetClient?.Dispose();_targetAccount?.Dispose();_targetClient=null;_targetAccount=null;}
    }
}
