using System;
using System.Text;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using ZeoOverlay;

namespace ZeoCore
{
    // A draft only. Closing, Escape or losing the page never saves a binding.
    internal sealed class ZeoMenuBindingScreen : MyGuiScreenBase
    {
        internal static bool IsOpen { get; private set; }
        static readonly MyKeys[] Keys=(MyKeys[])Enum.GetValues(typeof(MyKeys));
        readonly ZeoNativeSettingsModel model;
        readonly Action saved;
        readonly MyGuiControlButton capture;
        readonly MyGuiControlLabel status;
        int draft,armDelay;
        bool listening;
        internal ZeoMenuBindingScreen(ZeoNativeSettingsModel model,Action saved)
            :base(new Vector2(.5f,.5f),new Vector4(.105f,.145f,.165f,.98f),new Vector2(.60f,.36f),true)
        {
            this.model=model;this.saved=saved;
            model.Reload();draft=MenuBinding.Resolve(model.Current.MenuKey,model.Current.MenuKeyCode);
            IsOpen=true;DrawMouseCursor=true;CloseButtonEnabled=true;EnabledBackgroundFade=true;CanHideOthers=false;CanBeHidden=false;
            AddCaption("ZEOCORE // MENU KEY",new Vector4(.82f,.91f,.94f,1),new Vector2(0,-.135f),.75f);
            capture=Button(0,-.04f,.44f,"",()=>{listening=true;armDelay=2;FocusedControl=null;Refresh();status.Text="Press a key. APPLY saves; ESC cancels.";});
            status=new MyGuiControlLabel(new Vector2(-.26f,.015f),null,"Click the key button to choose a new shortcut.",null,.49f,null,MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
            Controls.Add(status);
            Button(-.185f,.085f,.16f,"CLEAR",()=>{draft=0;listening=false;Refresh();status.Text="APPLY disables the key. Use Pulsar Configure to reopen.";});
            Button(0,.085f,.16f,"APPLY",Apply);
            Button(.185f,.085f,.16f,"CANCEL",()=>CloseScreen());
            Refresh();
        }
        MyGuiControlButton Button(float x,float y,float width,string text,Action action)
        {
            var button=new MyGuiControlButton(new Vector2(x,y),MyGuiControlButtonStyleEnum.Rectangular,new Vector2(width,.043f),null,MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,null,new StringBuilder(text),.57f,MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,MyGuiControlHighlightType.WHEN_CURSOR_OVER,_=>action());
            Controls.Add(button);return button;
        }
        void Refresh(){capture.Text=listening?"PRESS A KEY...":draft==0?"NONE / ASSIGN":((MyKeys)draft).ToString().ToUpperInvariant();}
        void Apply()
        {
            if(listening){status.Text="Press a key first, or CLEAR to disable.";return;}
            try{model.SaveMenuBinding(draft);saved?.Invoke();CloseScreen();}
            catch(Exception ex){status.Text=ex.Message;status.SetToolTip(ex.Message);}
        }
        public override string GetFriendlyName()=>"ZeoCoreMenuKey";
        public override bool Update(bool hasFocus)
        {
            bool result=base.Update(hasFocus);
            if(!hasFocus||!listening||MyAPIGateway.Input==null)return result;
            if(armDelay>0){armDelay--;return result;}
            foreach(var key in Keys){
                if(key==MyKeys.None||!MenuBinding.Allowed((int)key)||!MyAPIGateway.Input.IsNewKeyPressed(key))continue;
                draft=(int)key;listening=false;Refresh();status.Text="Selected "+key+". Click APPLY to save.";break;
            }
            return result;
        }
        protected override void OnClosed(){IsOpen=false;base.OnClosed();}
    }
}
