using System;
using Sandbox.Graphics.GUI;
using VRage.Utils;
using VRageMath;

namespace ZeoUi
{
    // SE scales textbox layout constraints with TextScale, but CompositeTexture.Draw
    // clamps its background to the unscaled texture. Keep layout and visual bounds
    // equal, including after focus/highlight refreshes the engine's constraints.
    internal sealed class NativeRowTextbox : MyGuiControlTextbox
    {
        private bool ready, restoring;
        internal static float RowHeight
        {
            get
            {
                var texture=GetVisualStyle(MyGuiControlTextboxStyleEnum.Default).NormalTexture;
                return Vector2.Clamp(new Vector2(.2f,.041f),texture.MinSizeGui,texture.MaxSizeGui).Y;
            }
        }
        internal NativeRowTextbox(Vector2? position=null,string text=null,int maxLength=512,Vector4? color=null,float textScale=.64f)
            : base(position,text,maxLength,color,textScale)
        {
            ready=true;
            OriginAlign=MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER;
            RestoreBounds();
        }
        private void RestoreBounds()
        {
            if(!ready || restoring)return;
            restoring=true;
            try
            {
                float width=Size.X;
                MinSize=new Vector2(0,RowHeight);
                MaxSize=new Vector2(float.PositiveInfinity,RowHeight);
                Size=new Vector2(width,RowHeight);
            }
            finally { restoring=false; }
        }
        protected override void OnSizeChanged() { base.OnSizeChanged(); RestoreBounds(); }
        protected override void OnHasHighlightChanged() { base.OnHasHighlightChanged(); RestoreBounds(); }
        public override void OnFocusChanged(bool focus) { base.OnFocusChanged(focus); RestoreBounds(); }
    }
}
