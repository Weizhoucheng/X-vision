using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace VisionInspect.App.UI.Controls
{
    public sealed class StudioComboBox:ComboBox
    {
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            bool printing=message.Msg==0x0317||message.Msg==0x0318;
            if((message.Msg==0x000F||message.Msg==0x0085||printing)&&IsHandleCreated&&Width>20&&Height>4)
                using(var g=printing&&message.WParam!=IntPtr.Zero?Graphics.FromHdc(message.WParam):Graphics.FromHwnd(Handle)){
                    int arrow=Math.Min(25,Width/3);
                    using(var fill=new SolidBrush(Enabled?Theme.Field:Theme.SurfaceAlt))g.FillRectangle(fill,Width-arrow-1,1,arrow,Height-2);
                    using(var border=new Pen(Theme.Border))g.DrawRectangle(border,0,0,Width-1,Height-1);
                    g.SmoothingMode=SmoothingMode.AntiAlias;
                    using(var pen=new Pen(Theme.TextMuted,1.5f)){float x=Width-arrow/2f,y=Height/2f;g.DrawLines(pen,new[]{new PointF(x-4,y-2),new PointF(x,y+2),new PointF(x+4,y-2)});}
                }
        }
    }
}
