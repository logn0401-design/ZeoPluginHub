using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;

namespace ZeoPDC
{
    internal sealed partial class PdcEngine
    {
        DecoyCycle decoys;
        long decoyShip;
        bool decoyFault;
        readonly CriticalHeatState heatWarning=new CriticalHeatState();
        void UpdateDecoyManager()
        {
            if(frame%30!=0 && HasShip) return;
            try {
                if(!HasShip || JoiningServer()) { DisposeDecoys(); return; }
                if(decoys!=null&&decoyShip!=grid.EntityId) { DisposeDecoys();decoys=null; }
                if(decoys==null) {
                    decoyShip=grid.EntityId;
                    string path=Path.Combine(dataDir,"DecoyOwnership",decoyShip.ToString()+".json");
                    List<DecoyLease> saved=new List<DecoyLease>();
                    if(File.Exists(path)) {
                        saved=JsonIo.Load<List<DecoyLease>>(path);
                        if(saved==null) throw new InvalidDataException("Decoy restoration journal is unreadable");
                    }
                    decoys=new DecoyCycle(saved,entries=>Plugin.AtomicDecoyJournal(path,entries));
                }
                var available=new List<DecoyPort>();
                var player=MyAPIGateway.Session?.Player;
                bool enabled=HasShip&&wc.Ready&&player!=null&&config().DecoyCyclingEnabled&&!config().LabEnabled;
                if(HasShip&&player!=null) {
                    var grids=new List<IMyCubeGrid>(); GetMechanicalConstructGrids(grid,controller,grids);
                    foreach(var member in grids) {
                        var blocks=new List<IMySlimBlock>(); member.GetBlocks(blocks,b=>b.FatBlock is IMyDecoy);
                        foreach(var slim in blocks) {
                            var d=slim.FatBlock as IMyDecoy;
                            if(d==null||d.Closed) continue;
                            var relation=d.GetUserRelationToOwner(player.IdentityId);
                            if(relation!=MyRelationsBetweenPlayerAndBlock.Owner&&relation!=MyRelationsBetweenPlayerAndBlock.FactionShare) continue;
                            available.Add(new DecoyPort { Id=d.EntityId,Active=d.IsWorking,Read=()=> {
                                if(d.Closed) throw new InvalidOperationException("Decoy unavailable");
                                return d.CustomData;
                            },Write=value=> {
                                var r=d.GetUserRelationToOwner(player.IdentityId);
                                if(d.Closed||(r!=MyRelationsBetweenPlayerAndBlock.Owner&&r!=MyRelationsBetweenPlayerAndBlock.FactionShare)) throw new InvalidOperationException("Decoy ownership changed");
                                d.CustomData=value;
                            }});
                        }
                    }
                }
                decoys.Step(available,enabled,frame,config().DecoyCycleSeconds); decoyFault=false;
            } catch(Exception ex) {
                if(!decoyFault) log("DECOY manager yielded: "+ex.Message);
                decoyFault=true;
            }
        }
        void DisposeDecoys() { if(decoys!=null) decoys.Step(new DecoyPort[0],false,frame,config().DecoyCycleSeconds); }
        void AddDefenseStatus(PdcSnapshot s)
        {
            s.PreaimStatus=PreaimSummary();
            s.RangeBankStatus=heatRangeState;
            s.DecoyStatus=decoyFault?"FAULT / YIELD":decoys?.Status??"WAITING";
            s.DecoyManaged=decoys?.Managed??0; s.DecoyEligible=decoys?.Eligible??0;
            heatWarning.Update(HasShip&&wc.Ready&&guns.Count>0,HottestHeat(),frame,config());
            s.CriticalHeat=heatWarning.Active;
            s.CriticalHeatAgeSeconds=heatWarning.Active?Math.Max(0,(frame-heatWarning.Entered)/60.0):0;
        }
    }
    public sealed partial class Plugin
    {
        internal static void AtomicDecoyJournal(string path,List<DecoyLease> leases) { AtomicLab(path,leases); }
    }
}
