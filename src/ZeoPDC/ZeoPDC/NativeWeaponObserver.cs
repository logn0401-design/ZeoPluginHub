using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using VRage.Game.Entity;

namespace ZeoPDC
{
    // Read-only adapter for the locally audited WC implementation. Never changes
    // targeting, invokes acquisition, or writes WC fields. A schema mismatch
    // disables observation and leaves the existing defense path independent.
    internal sealed class NativeWeaponObserver
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        FieldInfo platform, state, weapons, target, targetState, targetObject, info, id;
        Type componentType, projectileType;
        MethodInfo getComponent;
        public string Status { get; private set; } = "UNBOUND";
        public string AssemblyIdentity { get; private set; } = "";

        static FieldInfo Field(Type type, string name)
        {
            var f = type.GetField(name, Flags);
            if (f == null) throw new MissingFieldException(type.FullName, name);
            return f;
        }

        public void Bind(Assembly assembly)
        {
            getComponent = null;
            Status = "UNSUPPORTED_SCHEMA";
            AssemblyIdentity = assembly == null ? "" : assembly.FullName;
            try
            {
                if (assembly == null) return;
                componentType = assembly.GetType("CoreSystems.Support.CoreComponent", true);
                var weaponType = assembly.GetType("CoreSystems.Platform.Weapon", true);
                projectileType = assembly.GetType("CoreSystems.Projectiles.Projectile", true);
                platform = Field(componentType, "Platform");
                state = Field(platform.FieldType, "State");
                weapons = Field(platform.FieldType, "Weapons");
                target = Field(weaponType, "Target");
                targetState = Field(target.FieldType, "TargetState");
                targetObject = Field(target.FieldType, "TargetObject");
                info = Field(projectileType, "Info");
                id = Field(info.FieldType, "Id");
                if (id.FieldType != typeof(ulong) || !state.FieldType.IsEnum || !targetState.FieldType.IsEnum ||
                    !Enum.GetNames(state.FieldType).Contains("Ready") || !Enum.GetNames(targetState.FieldType).Contains("IsProjectile") ||
                    !typeof(IList).IsAssignableFrom(weapons.FieldType)) return;
                Status = "SCHEMA_READY";
            }
            catch { Status = "UNSUPPORTED_SCHEMA"; }
        }

        public string Read(MyEntity entity, int part, out ulong projectile)
        {
            projectile = 0;
            if (Status != "SCHEMA_READY") return Status;
            if (entity == null || part < 0) return "NO_WEAPON";
            try
            {
                var container = entity.Components;
                if (container == null) return "NO_COMPONENT";
                if (getComponent == null)
                {
                    var method = container.GetType().GetMethods(Flags).FirstOrDefault(m => m.Name == "Get" &&
                        m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 0);
                    if (method == null) { Status = "UNSUPPORTED_CONTAINER"; return Status; }
                    getComponent = method.MakeGenericMethod(componentType);
                }
                return ReadComponent(getComponent.Invoke(container, null), part, out projectile);
            }
            catch { return "READ_ERROR"; }
        }

        internal object Component(MyEntity entity)
        {
            if(Status!="SCHEMA_READY" || entity==null || getComponent==null) return null;
            return getComponent.Invoke(entity.Components,null);
        }
        public WeaponProfile ReadProfile(MyEntity entity, int part)
        {
            if (Status != "SCHEMA_READY" || entity == null || getComponent == null)
                return new WeaponProfile { Status = "PROFILE_SOURCE_UNAVAILABLE" };
            try { return EffectiveProfileReader.Read(getComponent.Invoke(entity.Components, null), part); }
            catch { return new WeaponProfile { Status = "PROFILE_READ_ERROR" }; }
        }

        // Optional read-only safety observations; independent of identity adapter schema.
        public string ReadControl(MyEntity entity, int part, out bool aligned, out bool manual)
        {
            aligned = false; manual = true;
            if (Status != "SCHEMA_READY" || entity == null || getComponent == null) return "UNAVAILABLE";
            try
            {
                var component = getComponent.Invoke(entity.Components, null);
                return ReadControlComponent(component, part, out aligned, out manual);
            }
            catch { aligned = false; manual = true; return "UNSUPPORTED_CONTROL_SCHEMA"; }
        }
        internal string ReadControlComponent(object component, int part, out bool aligned, out bool manual)
        {
            aligned = false; manual = true;
            if (Status != "SCHEMA_READY" || component == null) return "UNAVAILABLE";
            try
            {
                var p = platform.GetValue(component);
                if (p == null || state.GetValue(p).ToString() != "Ready") return "NOT_READY";
                var list = weapons.GetValue(p) as IList;
                if (list == null || part < 0 || part >= list.Count) return "NO_PART";
                var t = target.GetValue(list[part]);
                manual = (bool)Field(component.GetType(), "UserControlled").GetValue(component);
                aligned = t != null && (bool)Field(t.GetType(), "IsAligned").GetValue(t);
                return "OK";
            }
            catch { aligned = false; manual = true; return "UNSUPPORTED_CONTROL_SCHEMA"; }
        }

        internal string ReadComponent(object component, int part, out ulong projectile)
        {
            projectile = 0;
            if (Status != "SCHEMA_READY") return Status;
            try
            {
                if (component == null) return "NO_COMPONENT";
                var p = platform.GetValue(component);
                if (p == null || state.GetValue(p).ToString() != "Ready") return "NOT_READY";
                var list = weapons.GetValue(p) as IList;
                if (list == null || part < 0 || part >= list.Count) return "NO_PART";
                var t = target.GetValue(list[part]);
                if (t == null) return "NO_TARGET";
                string kind = targetState.GetValue(t).ToString();
                if (kind != "IsProjectile") return kind == "None" ? "NO_TARGET" : kind;
                var obj = targetObject.GetValue(t);
                if (obj == null || !projectileType.IsInstanceOfType(obj)) return "PROJECTILE_OBJECT_UNAVAILABLE";
                var details = info.GetValue(obj);
                if (details == null) return "PROJECTILE_INFO_UNAVAILABLE";
                projectile = (ulong)id.GetValue(details);
                return projectile == 0 ? "PROJECTILE_ID_UNAVAILABLE" : "PROJECTILE";
            }
            catch { projectile = 0; return "READ_ERROR"; }
        }
    }
}
