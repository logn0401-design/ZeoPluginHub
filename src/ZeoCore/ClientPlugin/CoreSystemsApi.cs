using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRageMath;

namespace ZeoCore
{
    internal sealed class CoreSystemsApi : IDisposable
    {
        private const long Channel = 67549756549;

        private bool _registered;
        private IReadOnlyDictionary<string, Delegate> _delegates;

        private Action<MyEntity, ICollection<MyTuple<MyEntity, float>>> _getSortedThreats;
        private Func<MyEntity, int, MyEntity> _getAiFocus;
        private Func<MyEntity, MyTuple<bool, int, int>> _getProjectilesLockedOn;
        private Action<MyEntity, ICollection<Vector3D>> _getProjectilesLockedOnPos;
        private Func<MyEntity, bool> _hasCoreWeapon;
        private Func<MyEntity, int, string> _getActiveAmmo;
        private Func<MyEntity, int, MyTuple<VRage.Game.MyDefinitionId, string, string, bool>> _getMagazineMap;

        public bool Ready { get; private set; }
        public int EndpointCount { get { return _delegates == null ? 0 : _delegates.Count; } }

        public void EnsureLoaded()
        {
            if (_registered || MyAPIGateway.Utilities == null)
                return;

            _registered = true;
            MyAPIGateway.Utilities.RegisterMessageHandler(Channel, HandleMessage);
            RequestApi();
        }

        public void RequestApi()
        {
            if (!_registered || MyAPIGateway.Utilities == null)
                return;
            try { MyAPIGateway.Utilities.SendModMessage(Channel, "ApiEndpointRequest"); }
            catch (Exception ex) { Plugin.Log("CoreSystems API request failed: " + ex.Message); }
        }

        private void HandleMessage(object obj)
        {
            if (obj is string)
                return;

            var next = obj as IReadOnlyDictionary<string, Delegate>;
            if (next == null)
                return;

            _delegates = next;
            _getSortedThreats = Bind<Action<MyEntity, ICollection<MyTuple<MyEntity, float>>>>("GetSortedThreatsBase");
            _getAiFocus = Bind<Func<MyEntity, int, MyEntity>>("GetAiFocusBase");
            _getProjectilesLockedOn = Bind<Func<MyEntity, MyTuple<bool, int, int>>>("GetProjectilesLockedOnBase");
            _getProjectilesLockedOnPos = Bind<Action<MyEntity, ICollection<Vector3D>>>("GetProjectilesLockedOnPos");
            _hasCoreWeapon = Bind<Func<MyEntity, bool>>("HasCoreWeaponBase");
            _getActiveAmmo = Bind<Func<MyEntity, int, string>>("GetActiveAmmoBase");
            _getMagazineMap = Bind<Func<MyEntity, int, MyTuple<VRage.Game.MyDefinitionId, string, string, bool>>>("GetMagazineMap");
            Ready = true;
            Plugin.Log("CoreSystems API READY endpoints=" + EndpointCount);
        }

        private T Bind<T>(string key) where T : class
        {
            try
            {
                Delegate d;
                if (_delegates != null && _delegates.TryGetValue(key, out d))
                    return d as T;
            }
            catch { }
            return null;
        }

        public void GetThreats(MyEntity grid, ICollection<MyTuple<MyEntity, float>> output)
        {
            if (_getSortedThreats != null && grid != null)
                _getSortedThreats(grid, output);
        }

        public MyEntity GetFocus(MyEntity grid)
        {
            if (_getAiFocus == null || grid == null) return null;
            try { return _getAiFocus(grid, 0); } catch { return null; }
        }

        public MyTuple<bool, int, int> GetLockedCount(MyEntity grid)
        {
            if (_getProjectilesLockedOn == null || grid == null) return new MyTuple<bool, int, int>();
            try { return _getProjectilesLockedOn(grid); } catch { return new MyTuple<bool, int, int>(); }
        }

        public bool HasCoreWeapon(MyEntity entity)
        {
            if (_hasCoreWeapon == null || entity == null) return false;
            try { return _hasCoreWeapon(entity); } catch { return false; }
        }

        public string GetActiveAmmo(MyEntity weapon, int weaponId)
        {
            if (_getActiveAmmo == null || weapon == null) return null;
            try { return _getActiveAmmo(weapon, weaponId); } catch { return null; }
        }

        public MyTuple<VRage.Game.MyDefinitionId, string, string, bool> GetMagazineMap(MyEntity weapon, int weaponId)
        {
            if (_getMagazineMap == null || weapon == null) return new MyTuple<VRage.Game.MyDefinitionId, string, string, bool>();
            try { return _getMagazineMap(weapon, weaponId); } catch { return new MyTuple<VRage.Game.MyDefinitionId, string, string, bool>(); }
        }

        public void GetLockedPositions(MyEntity grid, ICollection<Vector3D> output)
        {
            if (_getProjectilesLockedOnPos != null && grid != null)
            {
                try { _getProjectilesLockedOnPos(grid, output); } catch { }
            }
        }

        public bool ShotMonitorReady { get { return Ready && _delegates.ContainsKey("AddMonitorProjectile") && _delegates.ContainsKey("RemoveMonitorProjectile"); } }
        public bool WeaponParts(MyEntity weapon, IDictionary<string,int> output)
        {
            Delegate endpoint;
            try { if(!_delegates.TryGetValue("GetBlockWeaponMapBase",out endpoint)&&!_delegates.TryGetValue("GetBlockWeaponMap",out endpoint))return false; endpoint.DynamicInvoke(weapon,output);return output.Count>0; } catch {return false;}
        }
        public bool AddShotMonitor(MyEntity w,int part,Action<long,int,ulong,long,Vector3D,bool> cb)
        {try{var fn=Bind<Action<MyEntity,int,Action<long,int,ulong,long,Vector3D,bool>>>("AddMonitorProjectile");if(fn==null)return false;fn(w,part,cb);return true;}catch{return false;}}
        public void RemoveShotMonitor(MyEntity w,int part,Action<long,int,ulong,long,Vector3D,bool> cb)
        {try{Bind<Action<MyEntity,int,Action<long,int,ulong,long,Vector3D,bool>>>("RemoveMonitorProjectile")?.Invoke(w,part,cb);}catch{}}
        public bool ProjectileState(ulong id,out Vector3D velocity,out string ammo,out float health)
        {Vector3D pos;return ProjectileSample(id,out pos,out velocity,out ammo,out health);}
        public bool ProjectileSample(ulong id,out Vector3D pos,out Vector3D velocity,out string ammo,out float health)
        {
            pos=velocity=Vector3D.Zero;ammo=null;health=0;
            try{var fn=Bind<Func<ulong,MyTuple<Vector3D,Vector3D,float,float,long,string>>>("GetProjectileState");if(fn==null)return false;
                var x=fn(id);if(string.IsNullOrWhiteSpace(x.Item6)||!x.Item1.IsValid()||!x.Item2.IsValid())return false;
                pos=x.Item1;velocity=x.Item2;health=x.Item4;ammo=x.Item6;return true;}catch{return false;}
        }

        public void Dispose()
        {
            try
            {
                if (_registered && MyAPIGateway.Utilities != null)
                    MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, HandleMessage);
            }
            catch { }

            _registered = false;
            Ready = false;
            _delegates = null;
            _getSortedThreats = null;
            _getAiFocus = null;
            _getProjectilesLockedOn = null;
            _getProjectilesLockedOnPos = null;
            _hasCoreWeapon = null;
            _getActiveAmmo = null;
            _getMagazineMap = null;
        }
    }
}
