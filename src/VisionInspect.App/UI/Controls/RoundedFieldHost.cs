using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VisionInspect.App.UI;

namespace VisionInspect.App.UI.Controls
{
    /// <summary>8px 圆角输入框容器：原生 TextBox/ComboBox 放在圆角描边面板内。</summary>
    public sealed class RoundedFieldHost : Panel
    {
        public RoundedFieldHost(Control field)
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Field;
            Padding = new Padding(1);
            Field = field;
            field.Dock = DockStyle.Fill;
            field.Margin = Padding.Empty;
            var textBox = field as TextBox;
            if (textBox != null) textBox.BorderStyle = BorderStyle.None;
            Controls.Add(field);
            Theme.RoundControl(this, 8);
        }

        public Control Field { get; private set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Rounded(rect, 8))
            using (var brush = new SolidBrush(Theme.Field))
            using (var pen = new Pen(Theme.Border))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        private static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
