using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace ZeoCore
{
    internal sealed class ZeoHelpScreen : MyGuiScreenBase
    {
        private const int ResultsPerPage=6, LinesPerPage=14;
        private readonly ZeoHelpCatalog _catalog=new ZeoHelpCatalog();
        private readonly string _context;
        private readonly Action<ZeoHelpTopic> _navigate;
        private readonly MyGuiControlTextbox _search;
        private readonly List<MyGuiControlBase> _dynamic=new List<MyGuiControlBase>();
        private ZeoHelpTopic _selected;
        private int _resultPage,_textPage;
        private bool _dirty,_tabOnly=true;
        internal ZeoHelpScreen(string context,Action<ZeoHelpTopic> navigate)
            :base(new Vector2(.5f),new Vector4(.105f,.145f,.165f,1f),new Vector2(.94f,.91f),true)
        {
            _context=context;_navigate=navigate;DrawMouseCursor=true;CloseButtonEnabled=true;EnabledBackgroundFade=true;
            CanHideOthers=false;CanBeHidden=false;
            AddCaption("ZEOCORE // HELP",Vector4.One,new Vector2(0,-.417f),.85f);
            FixedLabel(-.415f,-.354f,"WHERE DO I...?  Search a feature or setting",.60f);
            _search=new MyGuiControlTextbox(new Vector2(0,-.298f),"",100,null,.62f){Size=new Vector2(.83f,.045f),OriginAlign=MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER};
            _search.SetToolTip("Search labels and guide text. Examples: ammo, text size, shared distance, attack, SOS.");
            _search.TextChanged+=delegate{_tabOnly=false;_resultPage=0;_textPage=0;_selected=null;_dirty=true;};Controls.Add(_search);
            Button(-.282f,-.235f,.26f,"THIS TAB: "+context,()=>{_search.Text="";_tabOnly=true;_resultPage=0;_textPage=0;_selected=_catalog.Topics.First(t=>t.Id=="tab-"+_context);_dirty=true;},false);
            Button(0,-.235f,.26f,"QUICK START",()=>{_search.Text="";_tabOnly=false;_resultPage=0;_textPage=0;_selected=_catalog.Topics[0];_dirty=true;},false);
            Button(.282f,-.235f,.26f,"ALL FEATURES",()=>{_search.Text="";_tabOnly=false;_resultPage=0;_textPage=0;_selected=null;_dirty=true;},false);
            Button(0,.395f,.28f,"BACK TO MENU",()=>CloseScreen(),false);
            _selected=_catalog.Topics.First(t=>t.Id=="tab-"+_context);Rebuild();
        }
        public override string GetFriendlyName(){return "ZeoCoreHelp";}
        public override bool Update(bool hasFocus){bool result=base.Update(hasFocus);if(_dirty&&hasFocus){_dirty=false;Rebuild();}return result;}
        private void Rebuild()
        {
            foreach(var control in _dynamic)Controls.Remove(control);_dynamic.Clear();
            var matches=_catalog.Search(_search.Text);if(_tabOnly)matches=matches.Where(t=>t.Page==_context).ToList();
            int pages=Math.Max(1,(matches.Count+ResultsPerPage-1)/ResultsPerPage);_resultPage=Math.Max(0,Math.Min(pages-1,_resultPage));
            if(_selected==null||!matches.Contains(_selected))_selected=matches.FirstOrDefault();
            Label(-.415f,-.178f,matches.Count+" RESULTS  /  "+(_resultPage+1)+" OF "+pages,.50f);
            for(int i=0;i<ResultsPerPage&&_resultPage*ResultsPerPage+i<matches.Count;i++){
                var topic=matches[_resultPage*ResultsPerPage+i];
                var button=Button(-.267f,-.116f+i*.066f,.296f,Short(topic.Title,30),()=>{_selected=topic;_textPage=0;_dirty=true;});
                button.Selected=topic==_selected;button.SetToolTip(topic.Page+" // "+topic.Title);
            }
            Button(-.344f,.292f,.14f,"PREVIOUS",()=>{_resultPage--;_dirty=true;}).Enabled=_resultPage>0;
            Button(-.19f,.292f,.14f,"NEXT",()=>{_resultPage++;_dirty=true;}).Enabled=_resultPage+1<pages;
            if(_selected==null){Label(-.08f,-.115f,"No matches. Try a shorter feature name.",.52f);return;}
            var title=Label(-.08f,-.178f,Short(_selected.Title,40),.58f);title.SetToolTip(_selected.Title);
            var lines=ZeoHelpCatalog.Wrap(_selected.Body,57);int textPages=Math.Max(1,(lines.Count+LinesPerPage-1)/LinesPerPage);_textPage=Math.Max(0,Math.Min(textPages-1,_textPage));
            for(int i=0;i<LinesPerPage&&_textPage*LinesPerPage+i<lines.Count;i++)Label(-.08f,-.126f+i*.027f,lines[_textPage*LinesPerPage+i],.50f);
            Label(.06f,.268f,"GUIDE "+(_textPage+1)+" / "+textPages,.45f);
            Button(-.012f,.315f,.135f,"BACK",()=>{_textPage--;_dirty=true;}).Enabled=_textPage>0;
            Button(.134f,.315f,.135f,"MORE",()=>{_textPage++;_dirty=true;}).Enabled=_textPage+1<textPages;
            Button(.324f,.315f,.22f,"OPEN SETTINGS",()=>{var topic=_selected;if(CloseScreen())_navigate(topic);});
        }
        private static string Short(string text,int max){return text.Length<=max?text:text.Substring(0,max-3)+"...";}
        private void FixedLabel(float x,float y,string text,float scale){var label=Label(x,y,text,scale);_dynamic.Remove(label);}
        private MyGuiControlLabel Label(float x,float y,string text,float scale){var label=new MyGuiControlLabel(new Vector2(x,y),null,text,new Vector4(.82f,.91f,.94f,1),scale,null,MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);Controls.Add(label);_dynamic.Add(label);return label;}
        private MyGuiControlButton Button(float x,float y,float width,string text,Action action,bool dynamic=true){
            var button=new MyGuiControlButton(new Vector2(x,y),MyGuiControlButtonStyleEnum.Rectangular,new Vector2(width,.045f),null,MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,null,new StringBuilder(text),.49f,MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,MyGuiControlHighlightType.WHEN_CURSOR_OVER,delegate(MyGuiControlButton _){action();});
            Controls.Add(button);if(dynamic)_dynamic.Add(button);return button;
        }
    }
}
