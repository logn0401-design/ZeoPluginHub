using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Input;
namespace ZeoNav
{
    internal sealed class NavKeyBinding
    {
        internal MyKeys Key;
        internal bool Ctrl,Alt,Shift;
        internal string Text;
        private static readonly Dictionary<string,NavKeyBinding> Parsed=new Dictionary<string,NavKeyBinding>(StringComparer.Ordinal);
        internal static NavKeyBinding Parse(string text)
        {
            text=text??"None";
            lock(Parsed)
            {
                NavKeyBinding binding;if(Parsed.TryGetValue(text,out binding))return binding;
                binding=ParseCore(text);if(Parsed.Count>=128)Parsed.Clear();Parsed[text]=binding;return binding;
            }
        }
        private static NavKeyBinding ParseCore(string text)
        {
            var result=new NavKeyBinding();
            var parts=(string.IsNullOrWhiteSpace(text)?"None":text).Split('+').Select(p=>p.Trim()).ToArray();
            for(int i=0;i<parts.Length-1;i++)
            {
                if(parts[i].Equals("Ctrl",StringComparison.OrdinalIgnoreCase)&&!result.Ctrl)result.Ctrl=true;
                else if(parts[i].Equals("Alt",StringComparison.OrdinalIgnoreCase)&&!result.Alt)result.Alt=true;
                else if(parts[i].Equals("Shift",StringComparison.OrdinalIgnoreCase)&&!result.Shift)result.Shift=true;
                else throw new ArgumentException("Use Ctrl, Alt or Shift with one key.");
            }
            string name=Enum.GetNames(typeof(MyKeys)).FirstOrDefault(n=>n.Equals(parts.Last(),StringComparison.OrdinalIgnoreCase));
            if(name==null)throw new ArgumentException("Choose a valid keyboard key.");
            result.Key=(MyKeys)Enum.Parse(typeof(MyKeys),name);
            if(result.Key==MyKeys.None&&parts.Length>1)throw new ArgumentException("An unbound action cannot have modifiers.");
            result.Text=(result.Ctrl?"Ctrl+":"")+(result.Alt?"Alt+":"")+(result.Shift?"Shift+":"")+name;
            return result;
        }
        internal bool Matches(bool ctrl,bool alt,bool shift,bool allowLookAlt=false)
        { return Ctrl==ctrl && Shift==shift && (Alt==alt || (allowLookAlt&&!Alt)); }
        internal static bool CaptureKey(MyKeys key)
        { string name=key.ToString();return key!=MyKeys.None&&key!=MyKeys.Escape&&!name.Contains("Control")&&!name.Contains("Shift")&&!name.Contains("Alt")&&name!="Menu"; }
        internal static string Capture(MyKeys key,bool ctrl,bool alt,bool shift)
        { return Parse((ctrl?"Ctrl+":"")+(alt?"Alt+":"")+(shift?"Shift+":"")+key).Text; }
    }

    // Drafts never write config; the row's explicit APPLY owns persistence.
    internal sealed class NavKeyDraft
    {
        internal string Saved,Value;
        internal bool Listening;
        internal NavKeyDraft(string value){Saved=Value=value;}
        internal void Begin(){Value=Saved;Listening=true;}
        internal void Accept(string value){Value=NavKeyBinding.Parse(value).Text;Listening=false;}
        internal void Cancel(){Value=Saved;Listening=false;}
        internal void Applied(){Saved=Value;Listening=false;}
    }
    internal static class NavHotkeys
    {
        internal static readonly string[] Keys={"MenuKey","StartKey","AbortKey","ManualFlipKey","SignalUpKey","SignalDownKey","QuickDockKey","RefuelKey","TargetSelectKey","TargetCycleKey","InterceptKey","MatchVelocityKey"};
        internal static bool Unique(NavConfig c,string field)
        {
            try {
                var key=NavKeyBinding.Parse((string)typeof(NavConfig).GetField(field).GetValue(c));
                return key.Key!=MyKeys.None && !Keys.Where(f=>f!=field).Any(f=>Conflicts(key,field,NavKeyBinding.Parse((string)typeof(NavConfig).GetField(f).GetValue(c)),f));
            } catch {return false;}
        }
        private static bool LookKey(string field){return field=="TargetSelectKey"||field=="TargetCycleKey";}
        private static bool Conflicts(NavKeyBinding a,string af,NavKeyBinding b,string bf)
        {return a.Key!=MyKeys.None && a.Key==b.Key && a.Ctrl==b.Ctrl && a.Shift==b.Shift && (a.Alt==b.Alt||LookKey(af)||LookKey(bf));}
        internal static void ValidateNewBindings(NavConfig c)
        {
            foreach(string field in Keys)
            {
                string key=(string)typeof(NavConfig).GetField(field).GetValue(c);
                NavKeyBinding.Parse(key);
                if(!string.IsNullOrWhiteSpace(key)&&!key.Equals("None",StringComparison.OrdinalIgnoreCase)&&!Unique(c,field))
                    throw new ArgumentException("Choose a separate key for "+field.Replace("Key","")+". That key is already assigned in Nav.");
            }
        }
    }
}
