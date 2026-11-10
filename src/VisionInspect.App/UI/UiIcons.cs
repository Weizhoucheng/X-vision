using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace VisionInspect.App.UI
{
    public enum UiIcon
    {
        None = 0, Document, Camera, Photo, Play, Stop, Pause, Gear, Notebook,
        User, Sun, Moon, Save, Refresh, Trash, Signal, Chart, Plus, Minus, Folder, Sliders
    }

    /// <summary>统一 2px 圆头线性图标；颜色跟随控件前景色，日夜主题自动适配。</summary>
    public static class UiIcons
    {
        public static UiIcon ForText(string text)
        {
            string t = (text ?? string.Empty).Replace(" ", string.Empty);
            if (t.Contains("保存")) return UiIcon.Save;
            if (t.Contains("方案")) return UiIcon.Document;
            if (t.Contains("报表")) return UiIcon.Document;
            if (t.Contains("相机")) return UiIcon.Camera;
            if (t.Contains("文件夹")) return UiIcon.Folder;
            if (t.Contains("图像") || t.Contains("图片") || t.Contains("拍照")) return UiIcon.Photo;
            if (t.Contains("启动") || t.Contains("开始")) return UiIcon.Play;
            if (t.Contains("停止")) return UiIcon.Stop;
            if (t.Contains("暂停")) return UiIcon.Pause;
            if (t.Contains("设置")) return UiIcon.Sliders;
            if (t.Contains("参数") || t.Contains("配置")) return UiIcon.Gear;
            if (t.Contains("日志")) return UiIcon.Notebook;
            if (t.Contains("登录") || t.Contains("注销") || t.Contains("用户") || t.Contains("管理员") || t.Contains("操作员"))
                return UiIcon.User;
            if (t.Contains("统计") || t.Contains("计数") || t.Contains("结果")) return UiIcon.Chart;
            if (t.Contains("清空") || t.Contains("清除") || t.Contains("删除")) return UiIcon.Trash;
            if (t.Contains("刷新")) return UiIcon.Refresh;
            if (t.Contains("连接") || t.Contains("断开") || t.Contains("信号") || t.Contains("通讯") || t.Contains("通信")) return UiIcon.Signal;
            if (t == "＋" || t == "+") return UiIcon.Plus;
            if (t == "－" || t == "-") return UiIcon.Minus;
            return UiIcon.None;
        }

        public static Bitmap ToBitmap(UiIcon icon, int size, Color color)
        {
            var bitmap = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bitmap))
                Draw(g, icon, new Rectangle(0, 0, size, size), color, 2f);
            return bitmap;
        }

        public static void Draw(Graphics g, UiIcon icon, Rectangle bounds, Color color, float stroke = 2f)
        {
            if (icon == UiIcon.None || bounds.Width < 4 || bounds.Height < 4) return;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float s = Math.Min(bounds.Width, bounds.Height) / 24f;
            g.TranslateTransform(bounds.X, bounds.Y);
            g.ScaleTransform(s, s);
            using (var pen = new Pen(color, Math.Max(1.6f, stroke)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (icon)
                {
                    case UiIcon.Document:
                        g.DrawPath(pen, Path(5, 3, 14, 18, 3));
                        g.DrawLines(pen, new[] { new PointF(14, 3), new PointF(19, 8), new PointF(14, 8), new PointF(14, 3) });
                        g.DrawLine(pen, 9, 12, 15, 12); g.DrawLine(pen, 9, 16, 15, 16); break;
                    case UiIcon.Camera:
                        g.DrawPath(pen, Path(3, 7, 18, 13, 3));
                        g.DrawRectangle(pen, 8, 4, 8, 4);
                        g.DrawEllipse(pen, 9, 10, 6, 6); break;
                    case UiIcon.Photo:
                        g.DrawPath(pen, Path(3, 4, 18, 16, 2));
                        g.DrawEllipse(pen, 15, 7, 3, 3);
                        g.DrawLines(pen, new[] { new PointF(4, 17), new PointF(9, 12), new PointF(13, 16), new PointF(17, 12), new PointF(20, 15) }); break;
                    case UiIcon.Folder:
                        g.DrawPolygon(pen, new[] {new PointF(3,5),new PointF(10,5),new PointF(12,8),new PointF(21,8),new PointF(21,20),new PointF(3,20)});
                        g.DrawLine(pen,3,10,21,10);break;
                    case UiIcon.Sliders:
                        g.DrawLine(pen,3,6,7,6);g.DrawLine(pen,12,6,21,6);
                        g.DrawLine(pen,3,12,14,12);g.DrawLine(pen,19,12,21,12);
                        g.DrawLine(pen,3,18,5,18);g.DrawLine(pen,10,18,21,18);
                        g.DrawPath(pen,Path(7,3,5,6,1));g.DrawPath(pen,Path(14,9,5,6,1));g.DrawPath(pen,Path(5,15,5,6,1));break;
                    case UiIcon.Play:
                        g.DrawPolygon(pen, new[] { new PointF(8, 5), new PointF(19, 12), new PointF(8, 19) }); break;
                    case UiIcon.Stop:
                        g.DrawPath(pen, Path(5, 5, 14, 14, 2)); break;
                    case UiIcon.Pause:
                        g.DrawLine(pen, 9, 5, 9, 19); g.DrawLine(pen, 15, 5, 15, 19); break;
                    case UiIcon.Gear:
                        g.DrawEllipse(pen, 8, 8, 8, 8);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(pen, (float)(12 + Math.Cos(a) * 6), (float)(12 + Math.Sin(a) * 6),
                                (float)(12 + Math.Cos(a) * 8), (float)(12 + Math.Sin(a) * 8));
                        }
                        break;
                    case UiIcon.Notebook:
                        g.DrawPath(pen, Path(5, 3, 14, 18, 2));
                        g.DrawLine(pen, 9, 3, 9, 21);
                        g.DrawLine(pen, 12, 8, 16, 8); g.DrawLine(pen, 12, 12, 16, 12); break;
                    case UiIcon.User:
                        g.DrawEllipse(pen, 9, 4, 6, 6);
                        g.DrawArc(pen, 5, 12, 14, 10, 190, 160); break;
                    case UiIcon.Sun:
                        g.DrawEllipse(pen, 8, 8, 8, 8);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(pen, (float)(12 + Math.Cos(a) * 7), (float)(12 + Math.Sin(a) * 7),
                                (float)(12 + Math.Cos(a) * 9), (float)(12 + Math.Sin(a) * 9));
                        }
                        break;
                    case UiIcon.Moon:
                        g.DrawArc(pen, 5, 3, 15, 18, 55, 250);
                        g.DrawArc(pen, 10, 4, 12, 16, 120, 130); break;
                    case UiIcon.Save:
                        g.DrawPath(pen, Path(4, 4, 16, 16, 2));
                        g.DrawRectangle(pen, 8, 4, 8, 6);
                        g.DrawPath(pen, Path(7, 14, 10, 5, 1)); break;
                    case UiIcon.Refresh:
                        g.DrawArc(pen, 4, 4, 16, 16, 35, 285);
                        g.DrawLines(pen, new[] { new PointF(18, 3), new PointF(21, 7), new PointF(17, 8) }); break;
                    case UiIcon.Trash:
                        g.DrawLine(pen, 4, 7, 20, 7); g.DrawLine(pen, 9, 4, 15, 4);
                        g.DrawPath(pen, Path(6, 8, 12, 13, 2));
                        g.DrawLine(pen, 10, 11, 10, 17); g.DrawLine(pen, 14, 11, 14, 17); break;
                    case UiIcon.Signal:
                        g.DrawArc(pen, 4, 8, 16, 16, 200, 140);
                        g.DrawArc(pen, 8, 12, 8, 8, 200, 140);
                        g.DrawEllipse(pen, 11, 17, 2, 2); break;
                    case UiIcon.Chart:
                        g.DrawLine(pen, 4, 20, 20, 20);
                        g.DrawLine(pen, 6, 20, 6, 13); g.DrawLine(pen, 12, 20, 12, 9);
                        g.DrawLine(pen, 18, 20, 18, 5); break;
                    case UiIcon.Plus:
                        g.DrawLine(pen, 12, 5, 12, 19); g.DrawLine(pen, 5, 12, 19, 12); break;
                    case UiIcon.Minus:
                        g.DrawLine(pen, 5, 12, 19, 12); break;
                }
            }
            g.Restore(state);
        }

        private static GraphicsPath Path(float x, float y, float w, float h, float r)
        {
            var path = new GraphicsPath();
            float d = r * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
