using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage.Input;
using VRageMath;
using ZeoNav;
internal static partial class Tests
{
    private static void PickerTests()
    {
        var platform=typeof(VRage.MyVRage).GetField("<Platform>k__BackingField",BindingFlags.NonPublic|BindingFlags.Static);
        platform.SetValue(null,NativeDefaults(platform.FieldType));
        VRage.Utils.MyLog.Default=new VRage.Utils.MyLog(false);
        var render=typeof(VRageRender.MyRenderProxy).GetField("m_render",BindingFlags.NonPublic|BindingFlags.Static);
        render.SetValue(null,NativeDefaults(render.FieldType));
        var input=typeof(MyInput).GetField("<Static>k__BackingField",BindingFlags.NonPublic|BindingFlags.Static);
        bool click=false;MyKeys key=MyKeys.None;
        input.SetValue(null,FixtureProxy.Make(input.FieldType,call=>{
            if(call.MethodName=="IsNewKeyPressed")return (MyKeys)call.Args[0]==key&&key!=MyKeys.None;
            if(call.MethodName=="IsAnyNewMouseOrJoystickPressed"||call.MethodName=="IsNewLeftMousePressed"||call.MethodName=="IsNewPrimaryButtonPressed"||call.MethodName=="IsPrimaryButtonPressed")return click;
            return FixtureProxy.Default(call);
        }));
        string temp=Path.Combine(Path.GetTempPath(),"nav-picker-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var cfg=new NavConfig();JsonIo.Save(temp,cfg);
            GpsDto a=new GpsDto{Name="Home",X=1},b=new GpsDto{Name="Home",X=2},c=new GpsDto{Name="JustTooToxic Base",X=3};
            var gps=new List<GpsDto>{a,b,c};var commands=new List<NavCommand>();
            var host=new NavUiHost{Store=new NavUiStore(temp,()=>cfg,v=>cfg=v),Snapshot=()=>new NavSnapshot{Gps=gps},Command=commands.Add,Log=_=>{}};
            var screen=new NavNativeSettingsScreen(host);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            Func<string,object> field=n=>typeof(NavNativeSettingsScreen).GetField(n,flags).GetValue(screen);
            Action<string> call=n=>typeof(NavNativeSettingsScreen).GetMethod(n,flags,null,Type.EmptyTypes,null).Invoke(screen,null);
            var box=(MyGuiControlTextbox)field("_gpsSearch");var list=(MyGuiControlListbox)field("_gpsList");
            Check("GPS placeholder is just Select GPS",box.Text=="Select GPS");
            call("OpenGpsChoices");
            Check("Opening picker clears placeholder and shows results",box.Text==""&&list.Visible&&list.Items.Count==3);
            box.Text="H";
            Check("Typing keeps prefix visible and filters native results",box.Text=="H"&&list.Items.Count==2&&host.Selected==null);
            var itemRect=(RectangleF)typeof(MyGuiControlListbox).GetField("m_itemsRectangle",flags).GetValue(list);
            // Real native mouse dispatch, using the second row of duplicate names.
            MyGuiManager.MouseCursorPosition=list.GetPositionAbsoluteTopLeft()+itemRect.Position+new Vector2(.03f,list.ItemSize.Y*1.5f);
            click=true;screen.HandleInput(false);click=false;
            Check("Native click commits exact second GPS and closes dropdown",host.Selected==b&&!list.Visible&&box.Text=="Home"&&commands.Count==1&&commands[0].X==2);
            Check("Choosing a GPS never starts a route",commands.All(x=>x.Type=="SELECT_GPS"));
            call("OpenGpsChoices");box.Text="Just";
            Check("Editing clears old selection without hiding typed text",host.Selected==null&&box.Text=="Just"&&list.Items.Count==1);
            call("ChooseGps");
            Check("Enter selection uses highlighted GPS",host.Selected==c&&box.Text==c.Name&&!list.Visible);
            call("OpenGpsChoices");box.Text="Missing";call("ChooseGps");
            Check("Empty-result row cannot select or start flight",host.Selected==null&&list.Visible);
            key=MyKeys.Escape;screen.HandleInput(false);key=MyKeys.None;
            Check("Escape dismisses results and restores short prompt",!list.Visible&&box.Text=="Select GPS");
            var approach=(MyGuiControlLabel)field("_approach");
            var start=screen.Controls.OfType<MyGuiControlButton>().First(x=>x.Text=="START ROUTE");
            Check("Flight information has clear separation from actual button bounds",start.Position.Y-start.Size.Y*.5-approach.Position.Y>.018);
            Console.WriteLine("NATIVE GEOMETRY | list="+list.Size+" item="+list.ItemSize+" action="+start.Size);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    private static object NativeDefaults(Type type)
    {
        return FixtureProxy.Make(type,call=>{
            var result=((MethodInfo)call.MethodBase).ReturnType;
            if(result.IsInterface)return NativeDefaults(result);
            return FixtureProxy.Default(call);
        });
    }
}
