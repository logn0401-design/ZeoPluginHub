using System;
using System.Collections.Generic;
namespace ZeoOverlay
{
    internal static class QuickRefillBinding
    {
        // Windows virtual-key values used by MyKeys. Store the key, not the
        // dropdown index, so adding a new choice cannot change existing binds.
        internal static readonly int[] Keys;
        internal static readonly string[] Labels;
        internal static readonly string[] Modifiers={"NONE","CTRL","ALT","SHIFT","CTRL + SHIFT","CTRL + ALT","ALT + SHIFT","CTRL + ALT + SHIFT"};
        static QuickRefillBinding()
        {
            var keys=new List<int>{0};var labels=new List<string>{"UNBOUND"};
            for(int i=1;i<=12;i++){keys.Add(111+i);labels.Add("F"+i);}
            for(int i=65;i<=90;i++){keys.Add(i);labels.Add(((char)i).ToString());}
            for(int i=0;i<=9;i++){keys.Add(48+i);labels.Add(i.ToString());}
            for(int i=0;i<=9;i++){keys.Add(96+i);labels.Add("NUMPAD "+i);}
            keys.AddRange(new[]{36,35,45,46,33,34});labels.AddRange(new[]{"HOME","END","INSERT","DELETE","PAGE UP","PAGE DOWN"});
            Keys=keys.ToArray();Labels=labels.ToArray();
        }
        internal static int Index(int key){return Math.Max(0,Array.IndexOf(Keys,key));}
        internal static int NormalizeKey(int key){return Array.IndexOf(Keys,key)>=0 ? key : 0;}
        internal static int NormalizeModifier(int modifier){return modifier>=0&&modifier<Modifiers.Length ? modifier : 1;}
        internal static string Conflict(int key,int menuKey,bool distressEnabled,int distressKey,int? menuKeyCode=null)
        {
            if(key==0)return null;
            if(key==MenuBinding.Resolve(menuKey,menuKeyCode))return "Quick Refill key conflicts with MENU KEY. Choose another key.";
            if(distressEnabled&&distressKey>=0&&distressKey<8&&key==116+distressKey)return "Quick Refill key conflicts with DISTRESS KEY. Choose another key.";
            return null;
        }
        internal static bool MatchModifiers(int modifier,bool ctrl,bool alt,bool shift)
        {
            switch(modifier){
                case 0:return !ctrl&&!alt&&!shift;
                case 1:return ctrl&&!alt&&!shift;
                case 2:return !ctrl&&alt&&!shift;
                case 3:return !ctrl&&!alt&&shift;
                case 4:return ctrl&&!alt&&shift;
                case 5:return ctrl&&alt&&!shift;
                case 6:return !ctrl&&alt&&shift;
                case 7:return ctrl&&alt&&shift;
                default:return false;
            }
        }
    }
    internal static class MenuBinding
    {
        private static readonly int[] LegacyKeys={36,45,33,34,35};
        internal static bool Allowed(int key) => key==0 || key==8 || key==9 || key==13 || key==32 ||
            key>=33&&key<=40 || key==45 || key==46 || key>=48&&key<=57 || key>=65&&key<=90 ||
            key>=96&&key<=111 || key>=112&&key<=123 || key>=186&&key<=192 || key>=219&&key<=222;
        internal static int Resolve(int legacy,int? code)
        {
            if(code.HasValue && Allowed(code.Value))return code.Value;
            return LegacyKeys[legacy>=0&&legacy<LegacyKeys.Length?legacy:0];
        }
        internal static string Conflict(int key,bool distressEnabled,int distressKey,int refillKey,int targetKey)
        {
            if(key==0)return null;
            if(distressEnabled&&key==116+distressKey)return "Menu key conflicts with DISTRESS. Choose another key.";
            if(key==refillKey)return "Menu key conflicts with QUICK REFILL. Choose another key.";
            if(key==targetKey)return "Menu key conflicts with TARGET MARK. Choose another key.";
            return null;
        }
    }
    internal sealed class QuickRefillKeyLatch
    {
        private bool _down=true;
        private int _key=-1,_modifier=-1;
        internal bool Poll(int key,int modifier,bool down,bool modifiersMatch,bool gameplayAllowed,bool conflict)
        {
            // Require release after focus/menu/key changes; holding a binding
            // while typing or selecting it cannot start a refill on menu close.
            if(key!=_key||modifier!=_modifier){_key=key;_modifier=modifier;_down=down;return false;}
            if(!gameplayAllowed){_down=true;return false;}
            bool rising=down&&!_down;_down=down;
            return key!=0&&rising&&modifiersMatch&&gameplayAllowed&&!conflict;
        }
    }
}
