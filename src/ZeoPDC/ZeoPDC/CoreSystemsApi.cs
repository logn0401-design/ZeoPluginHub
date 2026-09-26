using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRageMath;

namespace ZeoPDC
{
    internal sealed partial class CoreSystemsApi : IDisposable
    {
        private const long Channel = 67549756549;
        private readonly Action<string> log;
        private readonly string endpointDumpPath;
        private bool registered;
        private IReadOnlyDictionary<string, Delegate> delegates;

        private Action<MyEntity, ICollection<MyTuple<MyEntity, float>>> getSortedThreats;
        private Func<MyEntity, int, MyEntity> getAiFocus;
        private Func<MyEntity, MyTuple<bool, int, int>> getProjectilesLockedOn;
        private Action<MyEntity, ICollection<Vector3D>> getProjectilesLockedOnPos;
        private Func<MyEntity, bool> hasCoreWeapon;
        private Func<MyEntity, int, string> getActiveAmmo;
        private Func<MyEntity, int, MyTuple<VRage.Game.MyDefinitionId, string, string, bool>> getMagazineMap;

        // Direct-manager endpoints used by v0.3.4. These remove the PB from the
        // per-shot/per-gun control loop. The PB is now an optional stale-heartbeat watchdog.
        private Action<MyEntity, bool, bool, int> toggleWeaponFire;
        private Action<MyEntity, float> setBlockTrackingRange;
        private Action<MyEntity, int, Action<long, int, ulong, long, Vector3D, bool>> addMonitorProjectile;
        private Action<MyEntity, int, Action<long, int, ulong, long, Vector3D, bool>> removeMonitorProjectile;
        private Action<ICollection<MyTuple<ulong, Vector3D, int, long>>> getAllSmartProjectiles;
        private Func<ulong, MyTuple<Vector3D, Vector3D, float, float, long, string>> getProjectileState;
        private Func<MyEntity, int, MyTuple<bool, bool, bool, MyEntity>> getWeaponTarget;
        private Func<MyEntity, MyEntity, int, Vector3D?> getPredictedTargetPosition;
        private Func<MyEntity, int, bool> isWeaponShooting;
        private Func<MyEntity, int, bool, bool, bool> isWeaponReadyToFire;
        private Action<MyEntity, MyEntity, int> setWeaponTarget;

        private Func<MyEntity, object, int, double, bool> shootRequest;
        public bool TargetedRequestReady { get { return Ready && shootRequest != null; } }

        // Installed WC 3154371364 ApiBackend + WeaponTypes accept boxed ulong IDs.
        // Acceptance queues a one-cycle request; it does not confirm target or hit.
        public string RequestProjectileShot(MyEntity weapon, int part, ulong projectile)
        {
            if (shootRequest == null) return "UNSUPPORTED";
            if (weapon == null || projectile == 0) return "INVALID";
            try { return shootRequest(weapon, (object)projectile, part, 0.0) ? "ACCEPTED" : "DECLINED"; }
            catch { return "ERROR"; }
        }

        public bool TryGetAllSmartProjectiles(ICollection<MyTuple<ulong, Vector3D, int, long>> output)
        {
            if (getAllSmartProjectiles == null || output == null) return false;
            try { getAllSmartProjectiles(output); return true; }
            catch { output.Clear(); return false; }
        }

        public readonly OuterAimAdapter OuterAim = new OuterAimAdapter();
        public readonly NativeWeaponObserver NativeObserver = new NativeWeaponObserver();
        public readonly NativeProjectileObserver NativeProjectiles = new NativeProjectileObserver();
        internal struct ProjectileObservation
        {
            public Vector3D Position, Velocity;
            public float Health;
            public long Target;
            public string Ammo, Status, Source;
            public string NativeState, NativeStateStatus;
            public ThreatProfileObservation ThreatProfile;
        }
        public ProjectileObservation ObserveProjectile(ulong projectileId)
        {
            var r = new ProjectileObservation { Health = float.NaN, Status = "UNSUPPORTED", Ammo = "", Source = "WC_MONITORED_API" };
            if (getProjectileState == null || projectileId == 0) return r;
            try
            {
                var s = getProjectileState(projectileId);
                // WC returns a default tuple for a missing ID. It is not zero health.
                if (string.IsNullOrEmpty(s.Item6)) { r.Status = "NOT_FOUND"; return r; }
                if (!BankPlanner.Finite(s.Item1) || !BankPlanner.Finite(s.Item2) || !BankPlanner.Finite(s.Item4) || s.Item4 < 0)
                { r.Status = "INVALID_STATE"; return r; }
                r.Position = s.Item1; r.Velocity = s.Item2; r.Health = s.Item4;
                r.Target = s.Item5; r.Ammo = s.Item6; r.Status = "OK";
            }
            catch { r.Status = "ERROR"; }
            return r;
        }

        private int generation;

        public bool Ready { get; private set; }
        public int EndpointCount { get { return delegates == null ? 0 : delegates.Count; } }
        public int Generation { get { return generation; } }
        public bool DirectControlReady { get { return Ready && toggleWeaponFire != null && setBlockTrackingRange != null; } }
        public bool ShotMonitorReady { get { return Ready && addMonitorProjectile != null && removeMonitorProjectile != null; } }
        public bool StableProjectileReady { get { return Ready && getAllSmartProjectiles != null && getProjectileState != null; } }

        public CoreSystemsApi(Action<string> logger, string dumpPath)
        {
            log = logger ?? delegate { };
            endpointDumpPath = dumpPath;
        }

        public void EnsureLoaded()
        {
            if (registered || MyAPIGateway.Utilities == null) return;
            registered = true;
            MyAPIGateway.Utilities.RegisterMessageHandler(Channel, HandleMessage);
            RequestApi();
        }

        public void RequestApi()
        {
            if (!registered || MyAPIGateway.Utilities == null) return;
            try { MyAPIGateway.Utilities.SendModMessage(Channel, "ApiEndpointRequest"); }
            catch (Exception ex) { log("CoreSystems request failed: " + ex.Message); }
        }

        private void HandleMessage(object obj)
        {
            if (obj is string) return;
            var next = obj as IReadOnlyDictionary<string, Delegate>;
            if (next == null)
            {
                var mutable = obj as IDictionary<string, Delegate>;
                if (mutable != null) next = new Dictionary<string, Delegate>(mutable);
            }
            if (next == null) return;

            delegates = next;
            getSortedThreats = Bind<Action<MyEntity, ICollection<MyTuple<MyEntity, float>>>>("GetSortedThreatsBase");
            getAiFocus = Bind<Func<MyEntity, int, MyEntity>>("GetAiFocusBase");
            getProjectilesLockedOn = Bind<Func<MyEntity, MyTuple<bool, int, int>>>("GetProjectilesLockedOnBase");
            getProjectilesLockedOnPos = Bind<Action<MyEntity, ICollection<Vector3D>>>("GetProjectilesLockedOnPos");
            hasCoreWeapon = Bind<Func<MyEntity, bool>>("HasCoreWeaponBase");
            getActiveAmmo = Bind<Func<MyEntity, int, string>>("GetActiveAmmoBase");
            getMagazineMap = Bind<Func<MyEntity, int, MyTuple<VRage.Game.MyDefinitionId, string, string, bool>>>("GetMagazineMap");

            toggleWeaponFire = Bind<Action<MyEntity, bool, bool, int>>("ToggleWeaponFireBase");
            setBlockTrackingRange = Bind<Action<MyEntity, float>>("SetBlockTrackingRangeBase");
            addMonitorProjectile = Bind<Action<MyEntity, int, Action<long, int, ulong, long, Vector3D, bool>>>("AddMonitorProjectile");
            removeMonitorProjectile = Bind<Action<MyEntity, int, Action<long, int, ulong, long, Vector3D, bool>>>("RemoveMonitorProjectile");
            getAllSmartProjectiles = Bind<Action<ICollection<MyTuple<ulong, Vector3D, int, long>>>>("GetAllSmartProjectiles");
            getProjectileState = Bind<Func<ulong, MyTuple<Vector3D, Vector3D, float, float, long, string>>>("GetProjectileState");
            getWeaponTarget = Bind<Func<MyEntity, int, MyTuple<bool, bool, bool, MyEntity>>>("GetWeaponTargetBase");
            getPredictedTargetPosition = Bind<Func<MyEntity, MyEntity, int, Vector3D?>>("GetPredictedTargetPositionBase");
            isWeaponShooting = Bind<Func<MyEntity, int, bool>>("IsWeaponShootingBase");
            isWeaponReadyToFire = Bind<Func<MyEntity, int, bool, bool, bool>>("IsWeaponReadyToFireBase");
            setWeaponTarget = Bind<Action<MyEntity, MyEntity, int>>("SetWeaponTargetBase");

            shootRequest = Bind<Func<MyEntity, object, int, double, bool>>("ShootRequest");
            NativeObserver.Bind(getWeaponTarget?.Method.DeclaringType?.Assembly);
            OuterAim.Bind(getWeaponTarget?.Method.DeclaringType?.Assembly);
            NativeProjectiles.Bind(getWeaponTarget?.Method.DeclaringType?.Assembly);
            Ready = true;
            generation++;
            log("CoreSystems READY endpoints=" + EndpointCount + " generation=" + generation +
                " direct=" + DirectControlReady + " shotMonitor=" + ShotMonitorReady + " stableProjectiles=" + StableProjectileReady);
            DumpEndpoints();
        }

        private T Bind<T>(string key) where T : class
        {
            try
            {
                Delegate d;
                if (delegates != null && delegates.TryGetValue(key, out d)) return d as T;
            }
            catch { }
            return null;
        }

        public void GetThreats(MyEntity grid, ICollection<MyTuple<MyEntity, float>> output)
        {
            if (getSortedThreats == null || grid == null || output == null) return;
            try { getSortedThreats(grid, output); } catch { }
        }

        public bool TryGetThreats(MyEntity grid, ICollection<MyTuple<MyEntity, float>> output)
        {
            if(getSortedThreats==null || grid==null || output==null)return false;
            try {getSortedThreats(grid,output);return true;}
            catch {output.Clear();return false;}
        }

        public MyEntity GetFocus(MyEntity grid)
        {
            if (getAiFocus == null || grid == null) return null;
            try { return getAiFocus(grid, 0); } catch { return null; }
        }

        public MyTuple<bool, int, int> GetLockedCount(MyEntity grid)
        {
            if (getProjectilesLockedOn == null || grid == null) return new MyTuple<bool, int, int>();
            try { return getProjectilesLockedOn(grid); } catch { return new MyTuple<bool, int, int>(); }
        }

        public void GetLockedPositions(MyEntity grid, ICollection<Vector3D> output)
        {
            if (getProjectilesLockedOnPos == null || grid == null || output == null) return;
            try { getProjectilesLockedOnPos(grid, output); } catch { }
        }

        public void GetAllSmartProjectiles(ICollection<MyTuple<ulong, Vector3D, int, long>> output)
        {
            if (getAllSmartProjectiles == null || output == null) return;
            try { getAllSmartProjectiles(output); } catch { }
        }

        public bool TryGetProjectileState(ulong projectileId, out Vector3D pos, out Vector3D vel, out long targetId, out string ammo)
        {
            var s = ObserveProjectile(projectileId);
            pos = s.Position; vel = s.Velocity; targetId = s.Target; ammo = s.Ammo;
            return s.Status == "OK";
        }

        public bool HasCoreWeapon(MyEntity entity)
        {
            if (hasCoreWeapon == null || entity == null) return false;
            try { return hasCoreWeapon(entity); } catch { return false; }
        }

        public string GetActiveAmmo(MyEntity weapon, int weaponId)
        {
            if (getActiveAmmo == null || weapon == null) return null;
            try { return getActiveAmmo(weapon, weaponId); } catch { return null; }
        }

        public MyTuple<VRage.Game.MyDefinitionId, string, string, bool> GetMagazineMap(MyEntity weapon, int weaponId)
        {
            if (getMagazineMap == null || weapon == null) return new MyTuple<VRage.Game.MyDefinitionId, string, string, bool>();
            try { return getMagazineMap(weapon, weaponId); } catch { return new MyTuple<VRage.Game.MyDefinitionId, string, string, bool>(); }
        }

        public bool ToggleWeaponFire(MyEntity weapon, int part, bool on)
        {
            if (toggleWeaponFire == null || weapon == null) return false;
            try { toggleWeaponFire(weapon, on, false, part); return true; } catch { return false; }
        }

        public bool SetTrackingRange(MyEntity weapon, float range)
        {
            if (setBlockTrackingRange == null || weapon == null) return false;
            try { setBlockTrackingRange(weapon, range); return true; } catch { return false; }
        }

        public bool AddProjectileMonitor(MyEntity weapon, int part, Action<long, int, ulong, long, Vector3D, bool> callback)
        {
            if (addMonitorProjectile == null || weapon == null || callback == null) return false;
            try { addMonitorProjectile(weapon, part, callback); return true; } catch { return false; }
        }

        public bool RemoveProjectileMonitor(MyEntity weapon, int part, Action<long, int, ulong, long, Vector3D, bool> callback)
        {
            if (removeMonitorProjectile == null || weapon == null || callback == null) return false;
            try { removeMonitorProjectile(weapon, part, callback); return true; } catch { return false; }
        }

        // Keep raw flags: a projectile target may not have a MyEntity. Do not infer
        // flag meanings until validated against the installed WeaponCore version.
        public string ReadTargetTelemetry(MyEntity weapon, int part, out bool flag1, out bool flag2, out bool flag3, out MyEntity target)
        {
            flag1 = flag2 = flag3 = false; target = null;
            if (getWeaponTarget == null) return "UNSUPPORTED";
            if (weapon == null) return "NO_WEAPON";
            try
            {
                var r = getWeaponTarget(weapon, part);
                flag1 = r.Item1; flag2 = r.Item2; flag3 = r.Item3; target = r.Item4;
                return "OK";
            }
            catch { return "ERROR"; }
        }

        public string ReadFireTelemetry(MyEntity weapon, int part, bool readiness, out bool value)
        {
            value = false;
            if (readiness ? isWeaponReadyToFire == null : isWeaponShooting == null) return "UNSUPPORTED";
            if (weapon == null) return "NO_WEAPON";
            try
            {
                value = readiness ? isWeaponReadyToFire(weapon, part, false, false) : isWeaponShooting(weapon, part);
                return "OK";
            }
            catch { return "ERROR"; }
        }

        public bool TryGetWeaponTarget(MyEntity weapon, int part, out MyEntity target)
        {
            target = null;
            if (getWeaponTarget == null || weapon == null) return false;
            try
            {
                var r = getWeaponTarget(weapon, part);
                target = r.Item4;
                return target != null;
            }
            catch { return false; }
        }

        public bool TryGetPredictedTargetPosition(MyEntity weapon, MyEntity target, int part, out Vector3D predicted)
        {
            predicted = Vector3D.Zero;
            if (getPredictedTargetPosition == null || weapon == null || target == null) return false;
            try
            {
                Vector3D? p = getPredictedTargetPosition(weapon, target, part);
                if (!p.HasValue) return false;
                predicted = p.Value;
                return true;
            }
            catch { return false; }
        }

        public bool IsWeaponShooting(MyEntity weapon, int part)
        {
            if (isWeaponShooting == null || weapon == null) return false;
            try { return isWeaponShooting(weapon, part); } catch { return false; }
        }

        public bool IsWeaponReady(MyEntity weapon, int part)
        {
            if (isWeaponReadyToFire == null || weapon == null) return false;
            try { return isWeaponReadyToFire(weapon, part, false, true); } catch { return false; }
        }

        public bool SetWeaponTarget(MyEntity weapon, MyEntity target, int part)
        {
            if (setWeaponTarget == null || weapon == null) return false;
            try { setWeaponTarget(weapon, target, part); return true; } catch { return false; }
        }

        public bool TryGetWeaponMap(object weapon, IDictionary<string, int> output)
        {
            if (weapon == null || output == null) return false;
            object r;
            if (!TryInvoke(new[] { "GetBlockWeaponMapBase", "GetBlockWeaponMap" }, out r, weapon, output)) return false;
            return r is bool ? (bool)r : output.Count > 0;
        }

        public float GetWeaponHeat(object weapon, int part)
        {
            return GetFloat(new[] { "GetWeaponHeatLevelBase", "GetWeaponHeatLevel" }, 0f, weapon, part);
        }

        public bool TryReadHeatPercent(object weapon, int part, out double percent)
        {
            percent=double.NaN; object heat, max;
            if (!TryInvoke(new[] { "GetWeaponHeatLevelBase", "GetWeaponHeatLevel" },out heat,weapon,part) || heat==null ||
                !TryInvoke(new[] { "GetMaxWeaponHeatLevelBase", "GetMaxWeaponHeatLevel" },out max,weapon,part) || max==null) return false;
            try
            {
                double h=Convert.ToDouble(heat,CultureInfo.InvariantCulture), m=Convert.ToDouble(max,CultureInfo.InvariantCulture);
                if (!BankPlanner.Finite(h) || !BankPlanner.Finite(m) || h<0 || m<=0) return false;
                percent=100*h/m; return BankPlanner.Finite(percent);
            }
            catch { return false; }
        }

        public int GetMaxWeaponHeat(object weapon, int part)
        {
            return GetInt(new[] { "GetMaxWeaponHeatLevelBase", "GetMaxWeaponHeatLevel" }, 0, weapon, part);
        }

        public int GetAmmoCount(object weapon, int part)
        {
            return GetInt(new[] { "GetAmmoCountBase", "GetAmmoCount" }, -1, weapon, part);
        }

        public float GetMaxWeaponRange(object weapon, int part)
        {
            return GetFloat(new[] { "GetMaxWeaponRangeBase", "GetMaxWeaponRange" }, 0f, weapon, part);
        }

        public bool TryGetWeaponScope(object weapon, int part, out Vector3D origin, out Vector3D direction)
        {
            origin = Vector3D.Zero;
            direction = Vector3D.Zero;
            object r;
            if (!TryInvoke(new[] { "GetWeaponScopeBase", "GetWeaponScope" }, out r, weapon, part) || r == null) return false;
            try
            {
                Type t = r.GetType();
                object a = ReadMember(t, r, "Item1");
                object b = ReadMember(t, r, "Item2");
                if (!(a is Vector3D) || !(b is Vector3D)) return false;
                origin = (Vector3D)a;
                direction = (Vector3D)b;
                if (direction.LengthSquared() < 1e-8) return false;
                direction.Normalize();
                return true;
            }
            catch { return false; }
        }

        public string EndpointType(string key)
        {
            try
            {
                Delegate d;
                if (delegates != null && delegates.TryGetValue(key, out d) && d != null) return d.GetType().ToString();
            }
            catch { }
            return "";
        }

        private float GetFloat(string[] keys, float fallback, params object[] args)
        {
            object r;
            if (!TryInvoke(keys, out r, args) || r == null) return fallback;
            try { return Convert.ToSingle(r, CultureInfo.InvariantCulture); } catch { return fallback; }
        }

        private int GetInt(string[] keys, int fallback, params object[] args)
        {
            object r;
            if (!TryInvoke(keys, out r, args) || r == null) return fallback;
            try { return Convert.ToInt32(r, CultureInfo.InvariantCulture); } catch { return fallback; }
        }

        private bool TryInvoke(string[] keys, out object result, params object[] args)
        {
            result = null;
            if (delegates == null || keys == null) return false;
            for (int i = 0; i < keys.Length; i++)
            {
                Delegate d;
                if (!delegates.TryGetValue(keys[i], out d) || d == null) continue;
                try
                {
                    ParameterInfo[] ps = d.Method.GetParameters();
                    if (ps.Length != args.Length) continue;
                    bool compatible = true;
                    for (int p = 0; p < ps.Length; p++)
                    {
                        object a = args[p];
                        if (a == null) continue;
                        Type pt = ps[p].ParameterType;
                        if (!pt.IsInstanceOfType(a) && !pt.IsAssignableFrom(a.GetType()))
                        {
                            compatible = false;
                            break;
                        }
                    }
                    if (!compatible) continue;
                    result = d.DynamicInvoke(args);
                    return true;
                }
                catch { }
            }
            return false;
        }

        private static object ReadMember(Type t, object obj, string name)
        {
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null) return p.GetValue(obj, null);
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) return f.GetValue(obj);
            return null;
        }

        private void DumpEndpoints()
        {
            if (string.IsNullOrWhiteSpace(endpointDumpPath) || delegates == null) return;
            try
            {
                var keys = new List<string>(delegates.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);
                using (var sw = new StreamWriter(endpointDumpPath, false))
                {
                    sw.WriteLine("ZEO PDC // LIVE CORESYSTEMS ENDPOINT CENSUS");
                    sw.WriteLine("UTC=" + DateTime.UtcNow.ToString("o"));
                    sw.WriteLine("COUNT=" + delegates.Count);
                    sw.WriteLine("DIRECT_CONTROL_READY=" + DirectControlReady);
                    sw.WriteLine("SHOT_MONITOR_READY=" + ShotMonitorReady);
                    sw.WriteLine("STABLE_PROJECTILE_READY=" + StableProjectileReady);
                    sw.WriteLine();
                    foreach (string key in keys)
                    {
                        Delegate d = delegates[key];
                        sw.WriteLine(key + " = " + (d == null ? "<null>" : d.GetType().ToString()));
                        if (d != null)
                        {
                            try { sw.WriteLine("  METHOD " + d.Method); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex) { log("Endpoint dump failed: " + ex.Message); }
        }

        public void Dispose()
        {
            try
            {
                if (registered && MyAPIGateway.Utilities != null)
                    MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, HandleMessage);
            }
            catch { }
            registered = false;
            Ready = false;
            delegates = null;
        }
    }
}

