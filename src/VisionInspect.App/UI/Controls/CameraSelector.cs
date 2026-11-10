using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VisionInspect.App.UI.Controls
{
    /// <summary>顶栏相机设备下拉入口，点击由 MainForm 弹出 GigE/USB 设备菜单。</summary>
    public sealed class CameraSelector : Control
    {
        private bool _hover;
        private bool _connected;

        public CameraSelector()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(124, 30);
            Font = Theme.SmallFont;
            Cursor = Cursors.Hand;
            Text = "工业相机";
        }

        public bool Connected
        {
            get { return _connected; }
            set { _connected = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(new Rectangle(0, 1, Width - 1, Height - 3), 8))
            using (var brush = new SolidBrush(_hover ? Theme.SurfaceAlt : Theme.Surface))
            using (var pen = new Pen(_hover ? Theme.Accent : Theme.Border))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            int x = 10, y = Height / 2;
            using (var pen = new Pen(_connected ? Theme.Ok : Theme.TextMuted, 1.4f))
            {
                e.Graphics.DrawRectangle(pen, x, y - 5, 13, 10);
                e.Graphics.DrawEllipse(pen, x + 4, y - 2, 5, 5);
                e.Graphics.DrawLine(pen, x + 4, y - 5, x + 7, y - 8);
                e.Graphics.DrawLine(pen, x + 7, y - 8, x + 10, y - 5);
            }
            using (var brush = new SolidBrush(Theme.Text))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            })
            {
                e.Graphics.DrawString(string.IsNullOrWhiteSpace(Text) ? "工业相机" : Text,
                    Font, brush, new RectangleF(31, 0, Width - 50, Height), format);
            }
            using (var pen = new Pen(Theme.TextMuted, 1.5f))
            {
                int ax = Width - 16, ay = Height / 2 - 2;
                e.Graphics.DrawLine(pen, ax - 4, ay, ax, ay + 4);
                e.Graphics.DrawLine(pen, ax, ay + 4, ax + 4, ay);
            }
        }

        private static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
