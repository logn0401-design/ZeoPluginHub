using System;
using System.Linq;
using VRage.Input;

namespace ZeosOreHelper {
    internal sealed class OreMenuBinding {
        internal static bool Capturable(MyKeys key) {
            int code=(int)key;
            return Enum.IsDefined(typeof(MyKeys),key) && code>=8 && code!=16 && code!=17 && code!=18 && code!=27 && (code<160 || code>165);
        }
        internal static readonly string[] Choices=new[]{"None"}.Concat(Enum.GetValues(typeof(MyKeys)).Cast<MyKeys>().Where(Capturable).Select(k=>k.ToString()).Distinct()).ToArray();
        internal static string Validate(string value) {
            var text=(value??"").Trim();
            var name=Choices.FirstOrDefault(k=>k.Equals(text,StringComparison.OrdinalIgnoreCase));
            if(name==null)throw new ArgumentException("Choose a keyboard key. ESC and modifier-only keys are reserved.");
            return name;
        }
        internal static string Normalize(string value) {try{return Validate(value);}catch(ArgumentException){return "PageUp";}}
        internal static MyKeys ToKey(string value) {return (MyKeys)Enum.Parse(typeof(MyKeys),Normalize(value),true);}
        internal string Draft{get;private set;}
        internal bool Editing{get;private set;}
        internal bool Listening{get;private set;}
        internal OreMenuBinding(string saved){Draft=Normalize(saved);}
        internal void Begin(){Editing=true;Listening=true;}
        internal bool Capture(MyKeys key){if(!Listening||!Capturable(key))return false;Draft=key.ToString();Listening=false;return true;}
        internal void Clear(){Draft="None";Listening=false;Editing=true;}
        internal void Saved(){Editing=false;Listening=false;}
    }
}
