using System;
using System.Globalization;
using ZeoOverlay;

namespace ZeoCore
{
    internal enum NativeOptionKind { Boolean, Number, Choice, Color, Action }

    internal sealed class NativeOption
    {
        internal string Page, Section, Label, Key;
        internal NativeOptionKind Kind;
        internal double Min, Max, Step;
        internal int Decimals;
        internal string[] Choices;
        internal Func<OverlaySettings, object> Read;
        internal Action<OverlaySettings, object> Write;

        internal string Format(OverlaySettings settings)
        {
            object value = Read(settings);
            if (Kind == NativeOptionKind.Number)
                return Convert.ToDouble(value).ToString("F" + Decimals, CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        internal object Parse(string text)
        {
            if (Kind == NativeOptionKind.Color)
            {
                string hex = (text ?? "").Trim().TrimStart('#');
                if (hex.Length != 6) throw new ArgumentException("Use a six-digit color such as #D9E6EA.");
                for (int i = 0; i < hex.Length; i++)
                    if (!Uri.IsHexDigit(hex[i])) throw new ArgumentException("Use a six-digit hexadecimal color.");
                return "#" + hex.ToUpperInvariant();
            }
            double value;
            if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                 !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) ||
                double.IsNaN(value) || double.IsInfinity(value) || value < Min || value > Max)
                throw new ArgumentException("Enter a value from " + Min.ToString(CultureInfo.InvariantCulture) + " to " + Max.ToString(CultureInfo.InvariantCulture) + ".");
            return Math.Round(value, Decimals, MidpointRounding.AwayFromZero);
        }
    }

    internal static partial class ZeoNativeCatalog
    {
        private static NativeOption Bool(string page, string section, string label, string key,
            Func<OverlaySettings, bool> read, Action<OverlaySettings, bool> write)
        {
            return new NativeOption { Page=page, Section=section, Label=label, Key=key, Kind=NativeOptionKind.Boolean,
                Read=s => read(s), Write=(s,v) => write(s, Convert.ToBoolean(v)) };
        }

        private static NativeOption Number(string page, string section, string label, string key,
            double min, double max, double step, int decimals, Func<OverlaySettings, double> read, Action<OverlaySettings, double> write)
        {
            return new NativeOption { Page=page, Section=section, Label=label, Key=key, Kind=NativeOptionKind.Number,
                Min=min, Max=max, Step=step, Decimals=decimals, Read=s => read(s), Write=(s,v) => write(s, Convert.ToDouble(v)) };
        }

        private static NativeOption Choice(string page, string section, string label, string key,
            string[] choices, Func<OverlaySettings, int> read, Action<OverlaySettings, int> write)
        {
            return new NativeOption { Page=page, Section=section, Label=label, Key=key, Kind=NativeOptionKind.Choice,
                Choices=choices, Read=s => read(s), Write=(s,v) => write(s, Convert.ToInt32(v)) };
        }

        private static NativeOption Color(string page, string section, string label, string key,
            Func<OverlaySettings, string> read, Action<OverlaySettings, string> write)
        {
            return new NativeOption { Page=page, Section=section, Label=label, Key=key, Kind=NativeOptionKind.Color,
                Read=s => read(s), Write=(s,v) => { write(s, (string)v); s.ThemePreset=4; } };
        }

        private static NativeOption ResetLayout(string page, string section, string label)
        {
            // Exactly the existing RESET POLISHED LAYOUT action: positions only.
            return new NativeOption { Page=page, Section=section, Label=label, Key="ResetLayout", Kind=NativeOptionKind.Action,
                Read=s => "", Write=(s,v) => {
                    s.FlightX=-0.92; s.FlightY=0.82; s.TrackPanelX=-0.72; s.TrackPanelY=-0.70;
                    s.LinkPanelX=0.62; s.LinkPanelY=0.82; s.AmmoX=0.62; s.AmmoY=-0.70;
                    s.RosterX=0.60; s.RosterY=0.30;
                    s.DistressPositionCustom=false; s.DistressX=0; s.DistressY=.96;
                } };
        }
    }

    internal sealed class ZeoNativeSettingsModel
    {
        private readonly string _path;
        private readonly Action _changed;
        internal OverlaySettings Current { get; private set; }

        internal ZeoNativeSettingsModel(string path, Action changed)
        {
            _path=path;
            _changed=changed;
            Reload();
        }

        internal void Reload() { Current=OverlaySettings.Load(_path); }

        internal void SaveRefillBinding(int key,int modifier)
        {
            Reload();
            if(QuickRefillBinding.NormalizeKey(key)!=key || QuickRefillBinding.NormalizeModifier(modifier)!=modifier)
                throw new ArgumentException("Unsupported binding.");
            string conflict=QuickRefillBinding.Conflict(key,Current.MenuKey,Current.DistressEnabled,Current.DistressKey);
            if(conflict!=null)throw new ArgumentException(conflict);
            Current.QuickRefillKey=key;Current.QuickRefillModifier=modifier;Current.Save();Reload();
            if(Current.QuickRefillKey!=key || Current.QuickRefillModifier!=modifier)throw new InvalidOperationException("Binding could not be saved.");
            _changed?.Invoke();
        }

        internal void Apply(NativeOption option, object value)
        {
            // Use the exact same model as SettingsForm. Reload before a mutation so
            // returning from the legacy window never writes a stale settings snapshot.
            // SaveUiExtension persists all six backings and the ammo filters together.
            Reload();
            if (option.Kind == NativeOptionKind.Number || option.Kind == NativeOptionKind.Color)
                value=option.Parse(Convert.ToString(value, CultureInfo.InvariantCulture));
            if (option.Kind == NativeOptionKind.Choice &&
                (Convert.ToInt32(value) < 0 || Convert.ToInt32(value) >= option.Choices.Length))
                throw new ArgumentException("Choose one of the listed values.");
            option.Write(Current, value);
            if(option.Key=="QuickRefillKey" || option.Key=="QuickRefillModifier" || option.Key=="MenuKey" || option.Key=="DistressKey" || option.Key=="DistressEnabled")
            {
                string conflict=QuickRefillBinding.Conflict(Current.QuickRefillKey,Current.MenuKey,Current.DistressEnabled,Current.DistressKey);
                if(conflict!=null){Reload();throw new ArgumentException(conflict);}
            }
            Current.Save();
            object expected=option.Read(Current);
            Reload();
            if (option.Kind != NativeOptionKind.Action && !Equals(expected, option.Read(Current)))
                throw new InvalidOperationException("The settings file could not be saved. Check folder access.");
            if (_changed != null) _changed();
        }
    }
}
