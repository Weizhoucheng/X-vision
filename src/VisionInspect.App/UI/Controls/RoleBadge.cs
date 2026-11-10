using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VisionInspect.App.UI.Controls
{
    /// <summary>顶栏角色徽标：小头像/盾牌图标 + 操作员/管理员文字。</summary>
    public sealed class RoleBadge : Control
    {
        private bool _isAdmin;

        public RoleBadge()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(104, 30);
            Font = Theme.SmallFont;
        }

        public bool IsAdmin
        {
            get { return _isAdmin; }
            set { _isAdmin = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color tint = _isAdmin ? Theme.AccentSoft : Theme.SurfaceAlt;
            Color line = _isAdmin ? Theme.Accent : Theme.Border;
            Color text = _isAdmin ? Theme.Accent : Theme.TextMuted;
            using (var path = Rounded(new Rectangle(0, 1, Width - 1, Height - 3), 8))
            using (var brush = new SolidBrush(tint))
            using (var pen = new Pen(line))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            int cx = 16, cy = Height / 2;
            using (var circle = new SolidBrush(_isAdmin ? Theme.Accent : Theme.TextMuted))
                e.Graphics.FillEllipse(circle, cx - 7, cy - 7, 14, 14);
            using (var pen = new Pen(Color.White, 1.4f))
            {
                if (_isAdmin)
                {
                    var shield = new[]
                    {
                        new PointF(cx, cy - 4.5f), new PointF(cx + 4.5f, cy - 2.5f),
                        new PointF(cx + 3.8f, cy + 2.5f), new PointF(cx, cy + 5f),
                        new PointF(cx - 3.8f, cy + 2.5f), new PointF(cx - 4.5f, cy - 2.5f)
                    };
                    e.Graphics.DrawPolygon(pen, shield);
                }
                else
                {
                    e.Graphics.DrawEllipse(pen, cx - 2.5f, cy - 4.2f, 5f, 5f);
                    e.Graphics.DrawArc(pen, cx - 5f, cy + 1f, 10f, 7f, 190, 160);
                }
            }
            using (var brush = new SolidBrush(text))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            })
            {
                e.Graphics.DrawString(string.IsNullOrWhiteSpace(Text) ? "未登录" : Text,
                    Font, brush, new RectangleF(28, 0, Width - 32, Height), format);
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
