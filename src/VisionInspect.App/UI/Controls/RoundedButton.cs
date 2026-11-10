using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VisionInspect.App.UI;

namespace VisionInspect.App.UI.Controls
{
    /// <summary>统一圆角/胶囊按钮，线性图标 + 文本，随主题自适应。</summary>
    public sealed class RoundedButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 1;
            UseVisualStyleBackColor = false;
            Font = Theme.BoldFont;
            Cursor = Cursors.Hand;
            Height = 34;
            AutoSize = false;
        }

        public UiIcon Icon { get; set; }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);

            int inset = 1;
            var rect = new Rectangle(inset, inset, Math.Max(1, Width - inset * 2), Math.Max(1, Height - inset * 2));
            int radius = 8;
            using (GraphicsPath path = Rounded(rect, radius))
            {
                Color fill = Enabled ? BackColor : Theme.SurfaceAlt;
                if (Enabled && _pressed) fill = FlatAppearance.MouseDownBackColor;
                else if (Enabled && _hover) fill = FlatAppearance.MouseOverBackColor;
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                using (var pen = new Pen(Enabled ? FlatAppearance.BorderColor : Theme.Border, 1f))
                    g.DrawPath(pen, path);
            }

            Color fore = Enabled ? ForeColor : Theme.TextMuted;
            int iconSize = 17;
            int gap = Icon == UiIcon.None || string.IsNullOrEmpty(Text) ? 0 : 7;
            Size textSize = TextRenderer.MeasureText(Text ?? string.Empty, Font,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            int contentWidth = (Icon == UiIcon.None ? 0 : iconSize) + gap + textSize.Width;
            int startX = Math.Max(6, (Width - contentWidth) / 2);
            if (Icon != UiIcon.None)
            {
                UiIcons.Draw(g, Icon, new Rectangle(startX, (Height - iconSize) / 2, iconSize, iconSize), fore, 2f);
                startX += iconSize + gap;
            }
            var textRect = new Rectangle(startX, 0, Math.Max(0, Width - startX - 6), Height);
            TextRenderer.DrawText(g, Text ?? string.Empty, Font, textRect, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle,-5,-5),fore,BackColor);
        }

        private static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            if (d <= 0) d = 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
