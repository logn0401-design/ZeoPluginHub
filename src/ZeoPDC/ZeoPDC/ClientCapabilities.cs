using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;

namespace ZeoPDC
{
    [DataContract] internal sealed class ClientCapability
    {
        [DataMember] public string status="UNREAD",authority="UNKNOWN",assembly,gun,revision;
        [DataMember] public bool? distribution_definition,distribution_setting,closest_definition,closest_setting,own_overrides;
        [DataMember] public double? rof,range,rpm;
        [DataMember] public string valid_control_modes,control_mode,weapon_group,sequence,projectile_tags;
        [DataMember] public bool? painter_prohibited,target_slaving,unique_target,tracking_reticle;
        [DataMember] public string painter_route="NETWORK_POSITION_PATH_EXISTS_NOT_YET_EXERCISED";
        [DataMember] public string focus_route="SET_AI_FOCUS_API_SERVER_ONLY;PLAYER_FOCUS_IS_ENTITY_LEVEL";
        [DataMember] public string grouping_route="FIRE_SEQUENCING_NOT_PER_PROJECTILE_DISTRIBUTION";
        [DataMember] public string target_types_route="USE_TERMINAL_REQUESTS;API_SET_TARGET_TYPES_HAS_DIFFERENT_LOCAL_TOGGLE_PATH";
        [DataMember] public string preaim="NO_VERIFIED_REMOTE_AIM_PATH",target_request="LOCAL_API_NOT_REMOTE_TARGET_PROOF";
        [DataMember] public string stats="SERVER_DEFINITION_REQUIRED",burst="TRIGGER_SYNC_NOT_CUSTOM_TARGET_PROOF";
        internal object Identity;
        internal uint Revision;
        static object Optional(object o,string path){try{return OuterAimAdapter.Read(o,path);}catch{return null;}}
        internal static ClientCapability Read(object component,int part)
        {
            var r=new ClientCapability();
            try {
                if(component==null)return r;
                var a=component.GetType().Assembly; r.assembly=a.FullName+"/"+a.ManifestModule.ModuleVersionId;
                var session=a.GetType("CoreSystems.Session",true).GetField("I",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
                r.authority=(bool)OuterAimAdapter.Read(session,"IsServer")?"HOST":"REMOTE_CLIENT";
                var list=OuterAimAdapter.Read(component,"Platform.Weapons") as IList;
                if(list==null||list.Count!=1||part!=0){r.status="SINGLE_PART_REQUIRED";return r;}
                r.Identity=OuterAimAdapter.Read(component,"Data.Repo.Values");
                r.Revision=Convert.ToUInt32(OuterAimAdapter.Read(r.Identity,"Revision"));r.revision=r.Revision.ToString(CultureInfo.InvariantCulture);
                object w=list[part],sys=OuterAimAdapter.Read(w,"System"),settings=OuterAimAdapter.Read(r.Identity,"Set");
                r.distribution_definition=(bool)OuterAimAdapter.Read(sys,"AllowFireDistribution");
                r.closest_definition=(bool)OuterAimAdapter.Read(sys,"AllowSwitchTargetPriority");
                r.distribution_setting=(bool)OuterAimAdapter.Read(settings,"Overrides.EnableFireDistribution");
                r.closest_setting=(bool)OuterAimAdapter.Read(settings,"Overrides.TargetClosest");
                r.own_overrides=ReferenceEquals(OuterAimAdapter.Read(component,"MasterOverrides"),OuterAimAdapter.Read(settings,"Overrides"));
                r.rof=Convert.ToDouble(OuterAimAdapter.Read(settings,"RofModifier"));
                r.range=Convert.ToDouble(OuterAimAdapter.Read(settings,"Range"));
                r.rpm=Convert.ToDouble(OuterAimAdapter.Read(w,"RateOfFire"));
                r.valid_control_modes=Optional(sys,"WConst.ValidControlModes")?.ToString();
                r.control_mode=Optional(settings,"Overrides.Control")?.ToString();
                r.weapon_group=Optional(settings,"Overrides.WeaponGroupId")?.ToString();
                r.sequence=Optional(settings,"Overrides.SequenceId")?.ToString();
                r.projectile_tags=Optional(sys,"WConst.ValidUserProjectileTags.Count")?.ToString();
                r.painter_prohibited=Optional(session,"Settings.Enforcement.ProhibitHUDPainter") as bool?;
                r.target_slaving=Optional(sys,"TargetSlaving") as bool?;
                r.unique_target=Optional(sys,"UniqueTargetPerWeapon") as bool?;
                r.tracking_reticle=Optional(r.Identity,"State.TrackingReticle") as bool?;
                r.status="LOCAL_REPLICA_READ";
            }catch{r.status="UNSUPPORTED_CAPABILITY_SCHEMA";r.Identity=null;}
            return r;
        }
    }

    // A component revision is observed only after a full server component sync in
    // the audited WC implementation. It is stronger than an optimistic setter
    // readback, but is NOT a command-ID acknowledgement or physical firing proof.
    [DataContract] internal sealed class ClientSettingLease
    {
        [DataMember] public string key,status="UNOWNED",request_revision,observed_revision;
        [DataMember] public double original,expected;
        [DataMember] public bool owned,pending,restoring,blocked;
        [DataMember] public string sent_utc,matched_utc;
        internal object Identity;
        internal uint Revision;
        internal DateTime Sent,LastWrite;
        internal static bool Same(double a,double b){return BankPlanner.Finite(a)&&BankPlanner.Finite(b)&&Math.Abs(a-b)<=.001;}
        internal void Observe(ClientCapability c,double? value,DateTime now)
        {
            if(!owned||!pending)return;
            if(c.Identity==null||!value.HasValue){status="READ_UNAVAILABLE";if(now-Sent>TimeSpan.FromSeconds(5)){pending=false;blocked=true;status="NO_RESPONSE_TIMEOUT";}return;}
            if(!ReferenceEquals(Identity,c.Identity)||c.Revision<Revision){pending=false;blocked=true;status="GENERATION_CHANGED_UNVERIFIED";return;}
            observed_revision=c.revision;
            if(c.Revision>Revision) {
                pending=false;
                if(Same(value.Value,expected)) {
                    status=restoring?"RESTORE_SERVER_STATE_MATCHED":"SERVER_STATE_MATCHED";matched_utc=now.ToString("o");
                    if(restoring){owned=false;restoring=false;}
                }else {status="SERVER_VALUE_DIFFERENT";blocked=true;}
            }else if(now-Sent>TimeSpan.FromSeconds(5)){pending=false;blocked=true;status="NO_RESPONSE_TIMEOUT";}
        }
        internal bool Request(string name,double desired,ClientCapability c,double? current,DateTime now,Func<double,bool> write)
        {
            if(blocked||pending||c.Identity==null||!current.HasValue||!BankPlanner.Finite(current.Value)||!BankPlanner.Finite(desired))return false;
            if(owned&&(!ReferenceEquals(Identity,c.Identity)||!Same(current.Value,expected))){blocked=true;status="EXTERNAL_EDIT_YIELD";return false;}
            if(Same(current.Value,desired)){if(!owned)status="UNCHANGED_NOT_TESTED";return true;}
            if(owned&&now-LastWrite<TimeSpan.FromSeconds(1))return true;
            if(!owned){key=name;original=current.Value;Identity=c.Identity;owned=true;}
            expected=desired;Revision=c.Revision;request_revision=c.revision;Sent=LastWrite=now;sent_utc=now.ToString("o");pending=true;restoring=false;status="REQUESTED_LOCAL_READBACK_ONLY";
            try{if(!write(desired)){blocked=true;status="WRITE_FAILED_PENDING_RESTORE";return false;}}catch{blocked=true;status="WRITE_EXCEPTION_PENDING_RESTORE";return false;}
            return true;
        }
        internal void Restore(ClientCapability c,double? current,DateTime now,Func<double,bool> write)
        {
            if(!owned)return;
            if(c.Identity==null||!current.HasValue){status="RESTORE_READ_UNAVAILABLE";return;}
            if(!ReferenceEquals(Identity,c.Identity)){blocked=true;status="RESTORE_GENERATION_CHANGED_UNVERIFIED";return;}
            if(restoring){Observe(c,current,now);return;}
            // An externally changed value is never overwritten. A same-original
            // value with no newer server revision is not proof a queued write was cancelled.
            if(!Same(current.Value,expected)&&!Same(current.Value,original)){owned=false;pending=false;blocked=true;status="EXTERNAL_VALUE_PRESERVED";return;}
            if(!pending&&c.Revision>Revision&&Same(current.Value,original)){owned=false;status="RESTORE_SERVER_STATE_MATCHED";return;}
            expected=original;Revision=c.Revision;request_revision=c.revision;Sent=LastWrite=now;sent_utc=now.ToString("o");pending=true;restoring=true;status="RESTORE_REQUESTED_UNVERIFIED";
            try{if(!write(original)){blocked=true;status="RESTORE_WRITE_FAILED";}}catch{blocked=true;status="RESTORE_WRITE_EXCEPTION";}
        }
    }
}
