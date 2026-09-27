using System;
namespace ZeoUi
{
    internal static class SettingHelp
    {
        internal static string Explain(string key,string label)
        {
            key=key??"";label=label??key;
            if(key=="MaxSharedTracks")return "Limits the number of received shared contacts. Nearest eligible contacts are shown first; local sensors remain independent.";
            if(key=="MaxSharedTrackDistanceKm")return "Maximum distance for received shared contacts, in kilometres. Zero removes this distance limit.";
            if(key=="IdleOptimization")return "After ten clear seconds, slow housekeeping. Projectile checks remain active; signals, firing or sensor failure wake full processing.";
            if(key=="InterceptStandOffKm")return "Stop closing at this distance from the locked target, then match its velocity. A target already inside this distance does not start an intercept.";
            if(key=="MatchRcsOnly")return "Restrict target flight to RCS thrust. Turn off to permit main engines when the alignment and braking checks allow them.";
            if(key=="MatchKeep")return "Continue matching the target's velocity after acquisition. Turn off to release control after a stable match.";
            if(key=="MaxDriveSigKm")return "Caps the ship's permitted own Spectrum emission range. A lower limit can reduce acceleration and increase journey time.";
            if(key=="BufferKm")return "Extra distance reserved before the destination for braking and final approach.";
            if(key=="RefuelAfterDock")return "After a successful connection, request a gas-tank refill from the connected supply. The station must have compatible gas available.";
            if(key=="TargetHudTextScale"||key=="HudTextScale")return "Changes text size inside this HUD frame. Text is fitted to the available space; widen or raise the frame for larger readouts.";
            if(key.EndsWith("Width",StringComparison.Ordinal)||key.EndsWith("Height",StringComparison.Ordinal))return "Changes this HUD frame's dimensions. The same adjustment is available by dragging its edges in Move / Resize HUD.";
            if(key.EndsWith("X",StringComparison.Ordinal))return "Moves this HUD panel left or right. Move / Resize HUD also lets you drag it visually.";
            if(key.EndsWith("Y",StringComparison.Ordinal))return "Moves this HUD panel up or down. Move / Resize HUD also lets you drag it visually.";
            if(key.IndexOf("Color",StringComparison.OrdinalIgnoreCase)>=0)return "Changes the colour of "+label.ToLowerInvariant()+". Use PICK or enter a six-digit #RRGGBB colour, then APPLY.";
            if(key.EndsWith("Key",StringComparison.Ordinal)||key.IndexOf("Binding",StringComparison.OrdinalIgnoreCase)>=0)return "Click the binding, press the desired key, then APPLY. CLEAR prepares an unbound shortcut; APPLY confirms it. Escape cancels capture.";
            if(key.StartsWith("HudShow",StringComparison.Ordinal)||key.StartsWith("Show",StringComparison.Ordinal))return "Shows or hides "+label.ToLowerInvariant()+" on the display.";
            if(key.IndexOf("Scale",StringComparison.OrdinalIgnoreCase)>=0)return "Adjusts the displayed size of "+label.ToLowerInvariant()+". A value of 1 is the standard size.";
            if(key.IndexOf("Opacity",StringComparison.OrdinalIgnoreCase)>=0)return "Adjusts how solid the panel background looks. Lower values let more of the game show through.";
            if(key.IndexOf("Range",StringComparison.OrdinalIgnoreCase)>=0||key.IndexOf("Distance",StringComparison.OrdinalIgnoreCase)>=0)return "Sets the distance used by "+label.ToLowerInvariant()+". Use the units and allowed range shown here.";
            if(key.IndexOf("Seconds",StringComparison.OrdinalIgnoreCase)>=0)return "Sets the time in seconds for "+label.ToLowerInvariant()+".";
            if(key.StartsWith("@",StringComparison.Ordinal))return "Runs "+label.ToLowerInvariant()+". Read the action details before applying changes.";
            return "Adjusts "+label.ToLowerInvariant()+". Hover the related controls for the allowed values and save action.";
        }
    }
}
