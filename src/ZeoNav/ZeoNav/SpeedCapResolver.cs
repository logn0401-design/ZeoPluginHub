using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Reflection;
using VRage.Game.ModAPI;

namespace ZeoNav
{
    // ShipCore is a live-server dependency, not a compile-time dependency of Zeo Nav.
    // The client API has changed shape across ShipCore releases, so this adapter binds
    // TryGetMaxSpeed by capability instead of assuming one exact namespace/signature.
    internal static class SpeedCapResolver
    {
        // Ceiling supplied for this server by the pilot. AUTO still prefers ShipCore.
        public const double ServerCeilingMps = 50000;
        private static object shipCoreApi;
        private static MethodInfo shipCoreRegister;
        private static MethodInfo shipCoreUnregister;
        private static MethodInfo shipCoreMaxSpeed;
        private static string shipCoreBinding = "NONE";
        private static DateTime lastDiscovery = DateTime.MinValue;
        private static long cachedGridId;
        private static double cachedSpeed;
        private static string cachedSource = "UNKNOWN";
        private static DateTime cachedAt = DateTime.MinValue;

        public static double Resolve(IMyCubeGrid grid, double overrideMps, double observedSpeed, out string source)
        {
            if (SignalBudget.Finite(overrideMps) && overrideMps > 1)
            {
                source = "OVERRIDE";
                return Math.Min(ServerCeilingMps, overrideMps);
            }

            long id = grid == null ? 0 : grid.EntityId;
            if (id == cachedGridId && cachedSpeed > 1 && (DateTime.UtcNow - cachedAt).TotalSeconds < .75)
            {
                source = cachedSource;
                return cachedSpeed;
            }

            double speed;
            if (TryShipCore(grid, id, out speed))
            {
                Cache(id, Math.Min(ServerCeilingMps, speed), "SHIPCORE API // " + shipCoreBinding);
                source = cachedSource;
                return cachedSpeed;
            }

            // The reported server ceiling is stable, even if the ship is overspeed.
            // Do not raise the cap to chase observed velocity while the API is unavailable.
            speed = ServerCeilingMps;
            Cache(id, speed, "SERVER LIMIT / 50000 M/S");
            source = cachedSource;
            return cachedSpeed;
        }

        public static void Dispose()
        {
            try
            {
                if (shipCoreUnregister != null)
                    shipCoreUnregister.Invoke(shipCoreUnregister.IsStatic ? null : shipCoreApi, null);
            }
            catch { }
            shipCoreApi = null;
            shipCoreRegister = null;
            shipCoreUnregister = null;
            shipCoreMaxSpeed = null;
            shipCoreBinding = "NONE";
            cachedGridId = 0; cachedSpeed = 0; cachedAt = DateTime.MinValue; lastDiscovery = DateTime.MinValue;
        }

        private static void Cache(long gridId, double speed, string source)
        {
            cachedGridId = gridId;
            cachedSpeed = speed;
            cachedSource = source;
            cachedAt = DateTime.UtcNow;
        }

        private static bool TryShipCore(IMyCubeGrid grid, long gridId, out double speed)
        {
            speed = 0;
            if (grid == null || gridId == 0) return false;

            if (shipCoreMaxSpeed == null)
            {
                if ((DateTime.UtcNow - lastDiscovery).TotalSeconds < 2.0) return false;
                DiscoverShipCore();
            }
            if (shipCoreMaxSpeed == null) return false;

            try
            {
                ParameterInfo[] ps = shipCoreMaxSpeed.GetParameters();
                object target = shipCoreMaxSpeed.IsStatic ? null : shipCoreApi;
                if (!shipCoreMaxSpeed.IsStatic && target == null) return false;

                if (ps.Length == 1)
                {
                    object first;
                    if (!BuildGridArgument(ps[0].ParameterType, grid, gridId, out first)) return false;
                    object result = shipCoreMaxSpeed.Invoke(target, new object[] { first });
                    return TryExtractSpeed(result, out speed);
                }

                if (ps.Length == 2 && ps[1].ParameterType.IsByRef)
                {
                    object first;
                    if (!BuildGridArgument(ps[0].ParameterType, grid, gridId, out first)) return false;
                    Type outType = ps[1].ParameterType.GetElementType();
                    if (!IsNumeric(outType)) return false;
                    object[] args = new object[] { first, Activator.CreateInstance(outType) };
                    object result = shipCoreMaxSpeed.Invoke(target, args);
                    if (result is bool && !(bool)result) return false;
                    return TryExtractSpeed(args[1], out speed);
                }
            }
            catch
            {
                // ShipCore can be loaded before its synchronized grid snapshot exists.
            }
            return false;
        }

        private static bool BuildGridArgument(Type parameterType, IMyCubeGrid grid, long gridId, out object value)
        {
            value = null;
            if (parameterType == typeof(long) || parameterType == typeof(Int64)) { value = gridId; return true; }
            if (parameterType == typeof(ulong) || parameterType == typeof(UInt64)) { value = unchecked((ulong)gridId); return true; }
            if (parameterType == typeof(int) || parameterType == typeof(Int32)) { value = unchecked((int)gridId); return true; }
            if (parameterType.IsInstanceOfType(grid) || parameterType.IsAssignableFrom(grid.GetType())) { value = grid; return true; }
            if (typeof(IMyCubeGrid).IsAssignableFrom(parameterType)) { value = grid; return true; }
            return false;
        }

        private static bool TryExtractSpeed(object result, out double speed)
        {
            speed = 0;
            return TryExtractSpeed(result, 0, out speed);
        }

        private static bool TryExtractSpeed(object result, int depth, out double speed)
        {
            speed = 0;
            if (result == null || depth > 3) return false;

            Type rt = result.GetType();
            if (IsNumeric(rt))
            {
                try
                {
                    double d = Convert.ToDouble(result);
                    if (d > 1 && !double.IsNaN(d) && !double.IsInfinity(d)) { speed = d; return true; }
                }
                catch { }
                return false;
            }

            // Common result-wrapper success flags. Only an explicit false rejects it.
            string[] successNames = { "Success", "IsSuccess", "Succeeded", "HasValue", "Valid", "IsValid" };
            for (int i = 0; i < successNames.Length; i++)
            {
                object flag = ReadMember(result, successNames[i]);
                if (flag is bool && !(bool)flag) return false;
            }

            // ShipCore API wrappers have used Value; tolerate adjacent wrapper names so
            // a minor framework update does not silently turn every route into CAP WAIT.
            string[] valueNames = { "Value", "Data", "Result", "MaxSpeed", "Speed", "Item" };
            for (int i = 0; i < valueNames.Length; i++)
            {
                object nested = ReadMember(result, valueNames[i]);
                if (nested == null || Object.ReferenceEquals(nested, result)) continue;
                if (TryExtractSpeed(nested, depth + 1, out speed)) return true;
            }

            return false;
        }

        private static bool IsNumeric(Type t)
        {
            if (t == null) return false;
            Type u = Nullable.GetUnderlyingType(t);
            if (u != null) t = u;
            return t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort) ||
                   t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong) ||
                   t == typeof(float) || t == typeof(double) || t == typeof(decimal);
        }

        private static void DiscoverShipCore()
        {
            lastDiscovery = DateTime.UtcNow;
            shipCoreApi = null;
            shipCoreRegister = null;
            shipCoreUnregister = null;
            shipCoreMaxSpeed = null;
            shipCoreBinding = "NONE";

            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                MethodInfo bestMethod = null;
                Type bestType = null;
                int bestScore = Int32.MinValue;

                for (int ai = 0; ai < assemblies.Length; ai++)
                {
                    Assembly a = assemblies[ai];
                    string assemblyName = "";
                    try { assemblyName = a.GetName().Name ?? ""; } catch { }

                    Type[] types;
                    try { types = a.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                    catch { continue; }
                    if (types == null) continue;

                    for (int ti = 0; ti < types.Length; ti++)
                    {
                        Type t = types[ti];
                        if (t == null) continue;
                        string full = t.FullName ?? t.Name ?? "";
                        bool shipCoreNamed = assemblyName.IndexOf("shipcore", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                             full.IndexOf("shipcore", StringComparison.OrdinalIgnoreCase) >= 0;

                        MethodInfo[] methods;
                        try { methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static); }
                        catch { continue; }
                        for (int mi = 0; mi < methods.Length; mi++)
                        {
                            MethodInfo m = methods[mi];
                            if (!m.Name.Equals("TryGetMaxSpeed", StringComparison.Ordinal)) continue;
                            if (!MethodShapeSupported(m)) continue;

                            int score = 0;
                            if (shipCoreNamed) score += 100;
                            if (full.IndexOf("Client", StringComparison.OrdinalIgnoreCase) >= 0) score += 15;
                            if (full.IndexOf("Api", StringComparison.OrdinalIgnoreCase) >= 0) score += 15;
                            if (full.IndexOf("Framework", StringComparison.OrdinalIgnoreCase) >= 0) score += 5;
                            ParameterInfo[] ps = m.GetParameters();
                            if (ps.Length == 1) score += 8;
                            if (ps.Length > 0 && ps[0].ParameterType == typeof(long)) score += 6;
                            if (m.IsPublic) score += 3;
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestMethod = m;
                                bestType = t;
                            }
                        }
                    }
                }

                if (bestMethod == null || bestType == null) return;

                object instance = null;
                if (!bestMethod.IsStatic)
                {
                    instance = TryGetApiInstance(bestType);
                    if (instance == null) return;
                }

                MethodInfo register = FindZeroArgMethod(bestType, "Register", bestMethod.IsStatic);
                MethodInfo unregister = FindZeroArgMethod(bestType, "Unregister", bestMethod.IsStatic);
                if (register != null)
                {
                    try { register.Invoke(register.IsStatic ? null : instance, null); } catch { }
                }

                shipCoreApi = instance;
                shipCoreRegister = register;
                shipCoreUnregister = unregister;
                shipCoreMaxSpeed = bestMethod;
                shipCoreBinding = (bestType.FullName ?? bestType.Name) + "." + DescribeMethod(bestMethod);
            }
            catch { }
        }

        private static bool MethodShapeSupported(MethodInfo m)
        {
            ParameterInfo[] ps = m.GetParameters();
            if (ps.Length != 1 && ps.Length != 2) return false;
            Type first = ps[0].ParameterType;
            bool gridArg = first == typeof(long) || first == typeof(ulong) || first == typeof(int) ||
                           typeof(IMyCubeGrid).IsAssignableFrom(first) ||
                           first.Name.IndexOf("CubeGrid", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!gridArg) return false;
            if (ps.Length == 2)
            {
                if (!ps[1].ParameterType.IsByRef) return false;
                if (!IsNumeric(ps[1].ParameterType.GetElementType())) return false;
            }
            return true;
        }

        private static object TryGetApiInstance(Type t)
        {
            try { return Activator.CreateInstance(t); } catch { }
            try { return Activator.CreateInstance(t, true); } catch { }

            string[] propertyNames = { "Instance", "Api", "API", "ClientApi", "ClientAPI" };
            for (int i = 0; i < propertyNames.Length; i++)
            {
                try
                {
                    PropertyInfo p = t.GetProperty(propertyNames[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (p != null)
                    {
                        object v = p.GetValue(null, null);
                        if (v != null && t.IsInstanceOfType(v)) return v;
                    }
                }
                catch { }
            }

            string[] fieldNames = { "Instance", "Api", "API", "ClientApi", "ClientAPI" };
            for (int i = 0; i < fieldNames.Length; i++)
            {
                try
                {
                    FieldInfo f = t.GetField(fieldNames[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (f != null)
                    {
                        object v = f.GetValue(null);
                        if (v != null && t.IsInstanceOfType(v)) return v;
                    }
                }
                catch { }
            }
            return null;
        }

        private static MethodInfo FindZeroArgMethod(Type t, string name, bool preferStatic)
        {
            try
            {
                MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                MethodInfo fallback = null;
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo m = methods[i];
                    if (!m.Name.Equals(name, StringComparison.Ordinal) || m.GetParameters().Length != 0) continue;
                    if (m.IsStatic == preferStatic) return m;
                    if (fallback == null) fallback = m;
                }
                return fallback;
            }
            catch { return null; }
        }

        private static string DescribeMethod(MethodInfo m)
        {
            if (m == null) return "NONE";
            try
            {
                ParameterInfo[] ps = m.GetParameters();
                string text = m.Name + "(";
                for (int i = 0; i < ps.Length; i++)
                {
                    if (i > 0) text += ",";
                    text += ps[i].ParameterType.Name;
                }
                return text + ")";
            }
            catch { return m.Name; }
        }

        private static object ReadMember(object obj, string name)
        {
            if (obj == null) return null;
            Type t = obj.GetType();
            try
            {
                PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null) return p.GetValue(obj, null);
            }
            catch { }
            try
            {
                FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f.GetValue(obj);
            }
            catch { }
            return null;
        }
    }
}

