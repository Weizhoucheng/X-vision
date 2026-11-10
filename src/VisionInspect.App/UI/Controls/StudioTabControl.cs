using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace VisionInspect.App.UI.Controls
{
    public sealed class StudioTabControl:TabControl
    {
        private bool _fitting;
        public StudioTabControl(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);SizeMode=TabSizeMode.Fixed;ItemSize=new Size(120,36);}
        protected override void OnResize(EventArgs e){base.OnResize(e);FitHeaders();}
        protected override void OnControlAdded(ControlEventArgs e){base.OnControlAdded(e);FitHeaders();}
        protected override void OnHandleCreated(System.EventArgs e){base.OnHandleCreated(e);FitHeaders();}
        protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);FitHeaders();}
        private void FitHeaders()
        {
            if(_fitting||TabCount==0||ClientSize.Width<=30)return;
            var size=new Size(Math.Max(84,Math.Min(132,(ClientSize.Width-8)/TabCount)),Math.Max(34,Font.Height+16));
            if(ItemSize==size && (!IsHandleCreated||GetTabRect(TabCount-1).Right<ClientSize.Width))return;
            _fitting=true;try{
                if(ItemSize!=size)ItemSize=size;
                if(IsHandleCreated)for(int i=0;i<3;i++){
                    int right=GetTabRect(TabCount-1).Right;
                    if(right<=ClientSize.Width-4)break;
                    int width=Math.Max(50,(int)(ItemSize.Width*(ClientSize.Width-8.0)/right)-1);
                    ItemSize=new Size(width,ItemSize.Height);
                }
            }finally{_fitting=false;}
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Surface);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            for(int i=0;i<TabCount;i++){
                Rectangle raw=GetTabRect(i);var rect=new Rectangle(raw.X+2,raw.Y+3,Math.Max(1,raw.Width-5),Math.Max(1,raw.Height-6));
                bool selected=i==SelectedIndex;
                using(var path=Path(rect,7))using(var fill=new SolidBrush(selected?Theme.AccentSoft:Theme.Surface))e.Graphics.FillPath(fill,path);
                UiIcon icon=UiIcons.ForText(TabPages[i].Text);int iconWidth=icon==UiIcon.None?0:20;
                if(icon!=UiIcon.None)UiIcons.Draw(e.Graphics,icon,new Rectangle(rect.X+9,rect.Y+(rect.Height-16)/2,16,16),selected?Theme.Accent:Theme.TextMuted);
                TextRenderer.DrawText(e.Graphics,TabPages[i].Text,Font,new Rectangle(rect.X+10+iconWidth,rect.Y,Math.Max(1,rect.Width-16-iconWidth),rect.Height),selected?Theme.Accent:Theme.TextMuted,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                if(selected)using(var pen=new Pen(Theme.Accent,2))e.Graphics.DrawLine(pen,rect.Left+9,raw.Bottom-1,rect.Right-9,raw.Bottom-1);
            }
        }
        private static GraphicsPath Path(Rectangle r,int radius){int d=Math.Max(2,Math.Min(radius*2,Math.Min(r.Width,r.Height)));var p=new GraphicsPath();p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    }
}
