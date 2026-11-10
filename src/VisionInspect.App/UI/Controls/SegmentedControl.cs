using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VisionInspect.App.UI;

namespace VisionInspect.App.UI.Controls
{
    /// <summary>单次/连续检测分段选择器，替代原生 RadioButton。</summary>
    public sealed class SegmentedControl : Control
    {
        private readonly string[] _items;
        private int _selectedIndex;
        private bool _hover;
        private int _hoverIndex = -1;

        public SegmentedControl(params string[] items)
        {
            _items = items ?? new string[0];
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Height = 34;
            Width = 190;
            Font = Theme.BoldFont;
            Cursor = Cursors.Hand;
            BackColor = Theme.Surface;
        }

        public event EventHandler SelectedIndexChanged;

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                int next = Math.Max(0, Math.Min(_items.Length - 1, value));
                if (next == _selectedIndex) return;
                _selectedIndex = next;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index = IndexAt(e.X);
            if (index != _hoverIndex) { _hoverIndex = index; _hover = index >= 0; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override bool IsInputKey(Keys keyData)
        {return keyData==Keys.Left||keyData==Keys.Right||keyData==Keys.Home||keyData==Keys.End||base.IsInputKey(keyData);}
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(e.KeyCode==Keys.Left)SelectedIndex--;
            if(e.KeyCode==Keys.Right)SelectedIndex++;
            if(e.KeyCode==Keys.Home)SelectedIndex=0;
            if(e.KeyCode==Keys.End)SelectedIndex=_items.Length-1;
            base.OnKeyDown(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false; _hoverIndex = -1; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                SelectedIndex = IndexAt(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            var track = new Rectangle(1, 1, Width - 2, Height - 2);
            using (GraphicsPath path = Rounded(track, 8))
            using (var brush = new SolidBrush(Theme.SurfaceAlt))
            using (var pen = new Pen(Theme.Border))
            {
                g.FillPath(brush, path); g.DrawPath(pen, path);
            }
            if (_items.Length == 0) return;
            int itemWidth = (Width - 4) / _items.Length;
            for (int i = 0; i < _items.Length; i++)
            {
                var itemRect = new Rectangle(2 + i * itemWidth, 3, itemWidth, Height - 6);
                bool selected = i == _selectedIndex;
                if (selected)
                {
                    using (GraphicsPath path = Rounded(itemRect, 6))
                    using (var brush = new SolidBrush(Theme.Accent))
                        g.FillPath(brush, path);
                }
                else if (_hover && i == _hoverIndex)
                {
                    using (GraphicsPath path = Rounded(itemRect, 6))
                    using (var brush = new SolidBrush(Theme.AccentSoft))
                        g.FillPath(brush, path);
                }
                TextRenderer.DrawText(g, _items[i], selected ? Theme.BoldFont : Theme.BaseFont,
                    itemRect, selected ? Color.White : Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        private int IndexAt(int x)
        {
            if (_items.Length == 0) return -1;
            int itemWidth = Math.Max(1, (Width - 4) / _items.Length);
            int index = (x - 2) / itemWidth;
            return index < 0 || index >= _items.Length ? -1 : index;
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
