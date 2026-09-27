using System;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Input;
using VRageMath;

namespace ZeoNav
{
    // A focus-owning, transparent input screen. The configured hold key owns selection
    // mode; the small Zeo reticle follows the OS pointer without rotating the camera.
    internal sealed class NavTargetPickerScreen : MyGuiScreenBase
    {
        private static NavTargetPickerScreen active;
        internal static bool InputActive {get{return active!=null&&active.State!=MyGuiScreenState.CLOSED;}}
        private readonly Func<NavConfig> config;
        private readonly Func<long> refresh;
        private readonly Action<long> confirm;
        private readonly Action cycle,abort;
        private readonly Action<Vector3,float> manualMove;
        private readonly TargetClickLatch click=new TargetClickLatch();
        private bool cancelRequested;
        internal bool HasFocus {get;private set;}
        internal bool CtrlAim {get;private set;}
        internal static bool HoldPressed(string keyText)
        {
            try
            {
                var i=MyInput.Static;var b=NavKeyBinding.Parse(keyText);
                if(i==null||b.Key==MyKeys.None||!i.IsKeyPress(b.Key))return false;
                string name=b.Key.ToString();
                bool ownCtrl=name.Contains("Control"),ownAlt=name.Contains("Alt")||name=="Menu",ownShift=name.Contains("Shift");
                return (ownCtrl||b.Ctrl)==i.IsAnyCtrlKeyPressed() &&
                    ((ownAlt||b.Alt)==i.IsAnyAltKeyPressed()||(!ownAlt&&!b.Alt)) &&
                    (ownShift||b.Shift)==i.IsAnyShiftKeyPressed();
            }
            catch{return false;}
        }
        internal NavTargetPickerScreen(Func<NavConfig> config,Func<long> refresh,Action<long> confirm,Action cycle,Action abort,Action<Vector3,float> manualMove)
            :base(new Vector2(.5f,.5f),Vector4.Zero,new Vector2(1,1),true)
        {
            this.config=config;this.refresh=refresh;this.confirm=confirm;this.cycle=cycle;this.abort=abort;this.manualMove=manualMove;
            active=this;CanHaveFocus=true;m_canShareInput=false;
            CtrlAim=HoldPressed(config().TargetAimHoldKey);DrawMouseCursor=false;CloseButtonEnabled=false;EnabledBackgroundFade=false;CanHideOthers=false;CanBeHidden=false;
        }
        public override string GetFriendlyName(){return "ZeoNavTargetPicker";}
        public override void InputLost(){click.Reset();cancelRequested=true;base.InputLost();}
        protected override void OnClosed(){if(ReferenceEquals(active,this))active=null;click.Reset();base.OnClosed();}
        public override bool Update(bool hasFocus)
        {
            HasFocus=hasFocus;
            if(!hasFocus)click.Reset();
            return base.Update(hasFocus);
        }
        private bool Pressed(string text)
        {
            try{var binding=NavKeyBinding.Parse(text);var input=MyAPIGateway.Input;
                return binding.Key!=MyKeys.None&&input.IsNewKeyPressed(binding.Key)&&binding.Matches(input.IsAnyCtrlKeyPressed(),input.IsAnyAltKeyPressed(),input.IsAnyShiftKeyPressed(),true);
            }catch{return false;}
        }
        public override void HandleInput(bool receivedFocusInThisUpdate)
        {
            var input=MyInput.Static;if(input==null||!HasFocus||State!=MyGuiScreenState.OPENED)return;
            var cfg=config();
            if(HoldPressed(cfg.TargetAimHoldKey)&&!CtrlAim){CtrlAim=true;click.Reset();}
            if(CtrlAim&&!HoldPressed(cfg.TargetAimHoldKey))cancelRequested=true;
            // Emergency abort must still work while a modifier is held for aiming.
            try{var emergency=NavKeyBinding.Parse(cfg.AbortKey);if(emergency.Key!=MyKeys.None&&input.IsNewKeyPressed(emergency.Key)&&emergency.EmergencyMatches(input.IsAnyCtrlKeyPressed(),input.IsAnyAltKeyPressed(),input.IsAnyShiftKeyPressed())){abort();cancelRequested=true;}}catch{}
            if(input.IsNewRightMousePressed()){abort();cancelRequested=true;}
            if(input.IsNewKeyPressed(MyKeys.Escape)||Pressed(cfg.TargetSelectKey)||Pressed(cfg.MenuKey))cancelRequested=true;
            if(cancelRequested)
            {
                // Keep mouse input captured through release, preventing a secondary shot.
                if(!input.IsLeftMousePressed()&&!input.IsRightMousePressed())CloseScreen();
                return;
            }
            if(Pressed(cfg.TargetCycleKey))cycle();
            if(CtrlAim)manualMove(input.GetPositionDelta(),input.GetRoll());
            long candidate=refresh();
            long selected=click.Observe(input.IsLeftMousePressed(),candidate,true);
            if(selected!=0)confirm(selected);
            // Never pass pointer input through to ship rotation, weapons, or GUI shortcuts.
        }
    }
    internal sealed class TargetCtrlLatch
    {
        private bool held;
        internal bool Observe(bool down,bool allowed){bool start=down&&!held&&allowed;held=down;return start;}
    }
    internal sealed class TargetClickLatch
    {
        private bool ready,wasDown;
        private long pressedId;
        internal void Reset(){ready=wasDown=false;pressedId=0;}
        internal long Observe(bool down,long candidate,bool focused)
        {
            if(!focused){Reset();return 0;}
            if(!ready){if(!down)ready=true;return 0;}
            long selected=0;
            if(down&&!wasDown)pressedId=candidate;
            if(!down&&wasDown){if(pressedId!=0&&candidate==pressedId)selected=pressedId;pressedId=0;}
            wasDown=down;return selected;
        }
    }
}
