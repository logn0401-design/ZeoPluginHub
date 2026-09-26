using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using VRage;
using VRageMath;

namespace ZeoPDC
{
    // GetProjectileState only covers weapons with registered projectile monitors.
    // Read the active objects for this poll's public smart-projectile IDs instead.
    // Never registers monitors on other weapons or writes WeaponCore state.
    internal sealed class NativeProjectileObserver
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        FieldInfo session, projectiles, active, info, id, position, velocity, health, target, targetId, ammo, ammoRound;
        readonly Dictionary<ulong, CoreSystemsApi.ProjectileObservation> snapshot = new Dictionary<ulong, CoreSystemsApi.ProjectileObservation>();
        FieldInfo projectileState;
        bool bound;
        public string Status { get; private set; } = "UNBOUND";
        static FieldInfo Field(Type type, string name, Type expected = null)
        {
            var f = type.GetField(name, Fields);
            if (f == null || (expected != null && f.FieldType != expected)) throw new MissingFieldException(type.FullName, name);
            return f;
        }
        public void Bind(Assembly assembly)
        {
            bound = false; projectileState = null; snapshot.Clear(); Status = "UNSUPPORTED_SCHEMA";
            try
            {
                if (assembly == null) return;
                var st = assembly.GetType("CoreSystems.Session", true);
                session = Field(st, "I", st);
                if (!session.IsStatic) return;
                projectiles = Field(st, "Projectiles");
                active = Field(projectiles.FieldType, "ActiveProjetiles"); // spelling in WC
                if (!typeof(IList).IsAssignableFrom(active.FieldType)) return;
                var pt = assembly.GetType("CoreSystems.Projectiles.Projectile", true);
                // State diagnostics are optional: schema changes must not disable health reads.
                projectileState = pt.GetField("State", Fields);
                if (projectileState != null && !projectileState.FieldType.IsEnum) projectileState = null;
                info = Field(pt, "Info");
                id = Field(info.FieldType, "Id", typeof(ulong));
                health = Field(info.FieldType, "BaseHealthPool", typeof(float));
                target = Field(info.FieldType, "Target"); targetId = Field(target.FieldType, "TargetId", typeof(long));
                ammo = Field(info.FieldType, "AmmoDef"); ammoRound = Field(ammo.FieldType, "AmmoRound", typeof(string));
                position = Field(pt, "Position", typeof(Vector3D)); velocity = Field(pt, "Velocity", typeof(Vector3D));
                bound = true; Status = "SCHEMA_READY";
            }
            catch { Status = "UNSUPPORTED_SCHEMA"; }
        }
        public void Capture(ICollection<MyTuple<ulong, Vector3D, int, long>> observed)
        {
            // Always clear before attempting a read: a failed poll must not reuse
            // yesterday's or the previous frame's apparent health/position.
            snapshot.Clear();
            if (!bound) return;
            if (observed == null || observed.Count == 0) { Status = "EMPTY"; return; }
            try
            {
                object s = session.GetValue(null);
                if (s == null) { Status = "NO_SESSION"; return; }
                object system = projectiles.GetValue(s);
                if (system == null) { Status = "NO_PROJECTILES"; return; }
                var list = active.GetValue(system) as IList;
                if (list == null || list.Count > 100000) { Status = "COLLECTION_UNAVAILABLE"; return; }
                var wanted = new HashSet<ulong>(); foreach (var row in observed) wanted.Add(row.Item1);
                int count = list.Count;
                for (int i = 0; i < count; i++)
                {
                    object p = list[i]; if (p == null) continue;
                    object details = info.GetValue(p); if (details == null) continue;
                    ulong key = (ulong)id.GetValue(details); if (!wanted.Contains(key)) continue;
                    var result = new CoreSystemsApi.ProjectileObservation { Source = "NATIVE_ACTIVE_PROJECTILES", Status = "INVALID_STATE", Health = float.NaN, Ammo = "" };
                    ReadState(p, ref result);
                    result.ThreatProfile=ThreatProfileObservation.Read(details);
                    var pos = (Vector3D)position.GetValue(p); var vel = (Vector3D)velocity.GetValue(p); float hp = (float)health.GetValue(details);
                    object ammunition = ammo.GetValue(details), targeting = target.GetValue(details);
                    string name = ammunition == null ? null : (string)ammoRound.GetValue(ammunition);
                    if ((ulong)id.GetValue(details) != key) throw new InvalidOperationException("Projectile identity changed during read");
                    if (result.NativeStateStatus == "OK")
                    {
                        var check = new CoreSystemsApi.ProjectileObservation();
                        ReadState(p, ref check);
                        if (check.NativeStateStatus != "OK" || check.NativeState != result.NativeState)
                        { result.NativeState = null; result.NativeStateStatus = "CHANGED_DURING_READ"; }
                    }
                    if (BankPlanner.Finite(pos) && BankPlanner.Finite(vel) && BankPlanner.Finite(hp) && hp >= 0 && !string.IsNullOrEmpty(name))
                    {
                        result.Position = pos; result.Velocity = vel; result.Health = hp; result.Ammo = name;
                        result.Target = targeting == null ? 0 : (long)targetId.GetValue(targeting); result.Status = "OK";
                    }
                    if (snapshot.ContainsKey(key)) throw new InvalidOperationException("Duplicate active projectile identity");
                    snapshot.Add(key, result);
                }
                if (list.Count != count) throw new InvalidOperationException("Active projectile collection changed during read");
                Status = "OK";
            }
            catch { snapshot.Clear(); Status = "CAPTURE_ERROR"; }
        }
        void ReadState(object projectile, ref CoreSystemsApi.ProjectileObservation result)
        {
            result.NativeState = null; result.NativeStateStatus = "UNSUPPORTED_SCHEMA";
            if (projectileState == null) return;
            try
            {
                var value = projectileState.GetValue(projectile);
                result.NativeState = Enum.GetName(projectileState.FieldType, value);
                result.NativeStateStatus = result.NativeState == null ? "UNKNOWN_VALUE" : "OK";
            }
            catch { result.NativeState = null; result.NativeStateStatus = "READ_ERROR"; }
        }
        public CoreSystemsApi.ProjectileObservation Read(ulong key)
        {
            CoreSystemsApi.ProjectileObservation result;
            if (snapshot.TryGetValue(key, out result)) return result;
            return new CoreSystemsApi.ProjectileObservation { Status = Status == "OK" || Status == "EMPTY" ? "NOT_FOUND" : Status,
                Source = "NATIVE_ACTIVE_PROJECTILES", Health = float.NaN, Ammo = "" };
        }
    }
}
