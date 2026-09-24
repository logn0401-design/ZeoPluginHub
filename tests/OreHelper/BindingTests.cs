using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using VRage.Input;
using ZeosOreHelper;
using ZeosOreOverlay;
using ZeoOreShared;
internal static partial class Tests {
    static void KeyBinding() {
        var path=Path.Combine(dir,"native-binding.ini");var seed=new OreOverlaySettings(path);
        seed.Set("MenuKey","F8");seed.Set("PanelHeightScale",1.3);seed.Set("OreEnabled:Iron",false);seed.Save();
        var model=new OreUiModel(null,path);var option=OreUiCatalog.Options.Single(o=>o.Key=="MenuKey");
        var draft=new OreMenuBinding(model.Current.Get("MenuKey"));draft.Begin();
        Check(draft.Listening&&draft.Editing,"capture suppresses menu shortcut immediately");
        Check(!draft.Capture(MyKeys.Escape)&&!draft.Capture(MyKeys.LeftControl)&&!draft.Capture(MyKeys.LeftShift),"ESC and modifier-only presses cannot bind");
        Check(draft.Capture(MyKeys.F8)&&draft.Editing&&!draft.Listening,"current menu key can be captured without leaving binding mode");
        Check(new OreOverlaySettings(path).Get("MenuKey")=="F8","capture alone never saves");
        draft.Begin();draft.Capture(MyKeys.F12);Check(draft.Draft=="F12","keys outside old limited dropdown accepted");
        var reopened=new OreMenuBinding(new OreOverlaySettings(path).Get("MenuKey"));
        Check(reopened.Draft=="F8"&&!reopened.Editing&&!reopened.Listening,"cancel/navigation discards the binding draft");
        OreIni.Merge(path,new Dictionary<string,string>{{"ConcurrentSetting","retain"},{"PanelWidthScale","1.25"}});
        model.Apply(option,draft.Draft);draft.Saved();
        Check(!draft.Editing&&!draft.Listening&&ReadHud(path).MenuKey=="F12","APPLY persists and releases shortcut suppression");
        Check(OreMenuBinding.ToKey(ReadHud(path).MenuKey)==MyKeys.F12,"game input resolves new key without PageUp fallback");
        var values=OreIni.Read(path);Check(values["ConcurrentSetting"]=="retain"&&values["PanelWidthScale"]=="1.25"&&values["PanelHeightScale"]=="1.3"&&values["OreEnabled:Iron"]=="False","binding save preserves concurrent settings, resize and ore choice");
        draft.Clear();Check(draft.Editing&&draft.Draft=="None"&&ReadHud(path).MenuKey=="F12","CLEAR is a draft until APPLY");
        model.Apply(option,draft.Draft);draft.Saved();var hud=ReadHud(path);SaveHud(hud,path);
        Check(hud.MenuKey=="None"&&ReadHud(path).MenuKey=="None"&&OreMenuBinding.ToKey(hud.MenuKey)==MyKeys.None,"disabled key survives plugin load/save and maps to no key");
        foreach(var invalid in new[]{"Escape","LeftShift","RightAlt","Control","999","112","F8, F9","bad",""})Reject(()=>model.Apply(option,invalid),"invalid key rejected: "+invalid);
        Check(ReadHud(path).MenuKey=="None","invalid saves preserve previous key");
        model.Apply(option,"f9");Check(ReadHud(path).MenuKey=="F9","key names canonicalized");
        foreach(var key in new[]{"PageUp","PageDown","Insert","Delete","End","F7","F8","F9","F10"})Check(HudSettings.NormalizeMenuKey(key)==key,"old binding preserved: "+key);
        Check(HudSettings.NormalizeMenuKey("broken")=="PageUp","corrupt persisted value has safe default");
        Check(option.Tab=="ADVANCED"&&option.Group=="HOME / KEYBINDS"&&option.Choices.Contains("None")&&option.Choices.Contains("F12"),"native catalog exposes key capture destination and full choices");
    }
}
