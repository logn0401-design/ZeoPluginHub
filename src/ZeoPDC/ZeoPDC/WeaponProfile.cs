using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ZeoPDC
{
    // Detached effective WC values. Never populated from a display-name lookup.
    internal sealed class WeaponProfile
    {
        public string Identity, Revision, Subtype, Ammo, Status = "UNREAD";
        public double NativeRpm, EffectiveRpm, MinimumRof, HeatPerEvent, CoolingPerSecond, MaximumHeat;
        public double MinimumRange, MaximumRange, HealthDamage, StartupSeconds, ShotLifeSeconds, ShotSpeed;
        public int ProjectilesPerEvent, BurstCount;
        public bool Adjustable, SimpleBallistic;
        public bool Valid { get { return Status == "OK" && !string.IsNullOrEmpty(Identity) &&
            Positive(NativeRpm) && Nonnegative(EffectiveRpm) && Nonnegative(MinimumRof) && MinimumRof <= 1 &&
            Nonnegative(HeatPerEvent) && Nonnegative(CoolingPerSecond) && Positive(MaximumHeat) &&
            Nonnegative(MinimumRange) && Positive(MaximumRange) && MaximumRange > MinimumRange &&
            ProjectilesPerEvent > 0 && Nonnegative(StartupSeconds) && Positive(ShotLifeSeconds) &&
            Nonnegative(ShotSpeed) && Nonnegative(HealthDamage); } }
        static bool Positive(double x) { return BankPlanner.Finite(x) && x > 0; }
        static bool Nonnegative(double x) { return BankPlanner.Finite(x) && x >= 0; }
        public double NominalHeatPerSecond { get { return NativeRpm / 60 * HeatPerEvent; } }
        public bool PreserveCadence { get { return NativeRpm <= 300 || NominalHeatPerSecond <= CoolingPerSecond || !SimpleBallistic; } }
        public void Seal()
        {
            // Effective RPM changes with native heat degradation, so it is a sample,
            // not a definition revision. All policy-driving definition values are hashed.
            string s = string.Join("|", new[] {Identity, Adjustable.ToString(), SimpleBallistic.ToString(),
                N(NativeRpm), N(MinimumRof), N(HeatPerEvent), N(CoolingPerSecond), N(MaximumHeat),
                N(MinimumRange), N(MaximumRange), N(HealthDamage), N(StartupSeconds), N(ShotLifeSeconds),
                N(ShotSpeed), ProjectilesPerEvent.ToString(CultureInfo.InvariantCulture), BurstCount.ToString(CultureInfo.InvariantCulture)});
            using(var sha=SHA256.Create()) Revision=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").ToLowerInvariant();
        }
        static string N(double x) { return x.ToString("R", CultureInfo.InvariantCulture); }
    }
    internal static class WeaponProfilePolicy
    {
        public static double? Cadence(WeaponProfile p, double adaptive, double heat, bool survival, double throttleStart, out string reason)
        {
            reason="PROFILE_UNKNOWN_NATIVE";
            if(p==null || !p.Valid) return null;
            if(!p.Adjustable) { reason="FIXED_NATIVE_CADENCE"; return null; }
            if(!BankPlanner.Finite(heat) || heat<0 || heat>100) { reason="HEAT_UNKNOWN_NATIVE"; return null; }
            if(!BankPlanner.Finite(adaptive) || adaptive<=0 || adaptive>1) { reason="INVALID_ROF_INPUT"; return null; }
            double result=p.PreserveCadence?1:adaptive;
            reason=p.NativeRpm<=300?"NATIVE_SLOW_CADENCE":p.NominalHeatPerSecond<=p.CoolingPerSecond?"NATIVE_COOLING_BALANCED":!p.SimpleBallistic?"NATIVE_SPECIAL_AMMO":"RANGE_ADAPTIVE";
            if(!survival)
            {
                double ceiling=heat>=96?.52:heat>=90?.55:heat>=80?.60:heat>=throttleStart?.65:1;
                if(ceiling<result) { result=ceiling; reason="OWN_HEAT_LIMIT"; }
            }
            // Survival removes heat conservation; keep the configured emergency
            // cadence for fast guns instead of silently rewriting user settings.
            double clamped=Math.Max(p.MinimumRof,Math.Min(1,result));
            if(clamped>result) reason+="_NATIVE_MINIMUM";
            return clamped;
        }
        public static double Range(WeaponProfile p,double requested)
        {
            if(p==null || !p.Valid || !BankPlanner.Finite(requested)) return double.NaN;
            // Never raise the user's full engagement ceiling. Bank tiers below
            // minimum are handled by the allocator using that full ceiling.
            return Math.Min(p.MaximumRange,Math.Max(100,requested));
        }
        public static bool Eligible(WeaponProfile p,double distance)
        { return p!=null && p.Valid && BankPlanner.Finite(distance) && distance>=p.MinimumRange && distance<=p.MaximumRange; }
        public static int? NominalHits(WeaponProfile p,double health)
        {
            if(p==null || !p.Valid || !p.SimpleBallistic || p.HealthDamage<=0 || !BankPlanner.Finite(health) || health<=0) return null;
            double n=Math.Ceiling(health/p.HealthDamage); return n<=int.MaxValue?(int?)n:null;
        }
    }
}
