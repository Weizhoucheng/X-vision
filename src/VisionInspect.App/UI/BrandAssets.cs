using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
namespace VisionInspect.App.UI
{
    public static class BrandAssets
    {
        private static readonly Image Light=Render(256,false);
        private static readonly Image Dark=Render(256,true);
        private static readonly Icon Mark=CreateIcon();
        public static Image Logo {get{return Light;}}
        public static Image LogoForTheme(bool dark){return dark?Dark:Light;}
        public static Icon AppIcon {get{return Mark;}}
        public static void WarmUp(){GC.KeepAlive(Light);GC.KeepAlive(Dark);GC.KeepAlive(Mark);}
        public static Bitmap Render(int size,bool dark)
        {
            var bitmap=new Bitmap(size,size,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(bitmap)){
                g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size/128f,size/128f);
                Color ink=dark?Color.FromArgb(182,214,255):Color.FromArgb(34,67,116);
                using(var frame=new Pen(ink,5)){frame.StartCap=frame.EndCap=LineCap.Round;
                    g.DrawLines(frame,new[]{new PointF(26,43),new PointF(26,26),new PointF(43,26)});
                    g.DrawLines(frame,new[]{new PointF(85,26),new PointF(102,26),new PointF(102,43)});
                    g.DrawLines(frame,new[]{new PointF(26,85),new PointF(26,102),new PointF(43,102)});
                    g.DrawLines(frame,new[]{new PointF(85,102),new PointF(102,102),new PointF(102,85)});
                }
                using(var gradient=new LinearGradientBrush(new Point(36,36),new Point(92,92),Color.FromArgb(43,111,236),Color.FromArgb(30,205,194)))
                using(var path=new Pen(gradient,8)){path.StartCap=path.EndCap=LineCap.Round;
                    g.DrawLine(path,42,42,86,86);g.DrawLine(path,86,42,42,86);
                }
                using(var fill=new SolidBrush(dark?Color.FromArgb(20,29,43):Color.White))g.FillEllipse(fill,47,47,34,34);
                using(var lens=new Pen(Color.FromArgb(41,147,224),5))g.DrawEllipse(lens,48,48,32,32);
                using(var focus=new SolidBrush(Color.FromArgb(26,196,184)))g.FillEllipse(focus,58,58,12,12);
            }return bitmap;
        }
        [DllImport("user32.dll")]private static extern bool DestroyIcon(IntPtr icon);
        private static Icon CreateIcon()
        {
            using(var bitmap=Render(64,false)){IntPtr handle=bitmap.GetHicon();try{using(var borrowed=Icon.FromHandle(handle))return (Icon)borrowed.Clone();}finally{DestroyIcon(handle);}}
        }
    }
}
