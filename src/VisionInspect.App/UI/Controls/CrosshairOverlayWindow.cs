using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VisionInspect.App.UI;

namespace VisionInspect.App.UI.Controls
{
    /// <summary>
    /// 位于 VM 渲染控件之上的独立分层窗口，用于绘制十字线、坐标和 OK/NG。
    ///
    /// 为什么不用子控件：
    /// VmRenderControl 使用原生渲染，透明 WinForms 子控件无法真正透视，会遮挡图像。
    /// 这里使用顶层无边框窗口 + TransparencyKey，通过 WS_EX_TRANSPARENT/HTTRANSPARENT
    /// 把鼠标事件透传给下层渲染控件，因此缩放/平移仍由 VM 控件处理。
    /// </summary>
    public sealed class CrosshairOverlayWindow : Form
    {
        private Point? _pointer;
        private bool _showCrosshair = true;
        private string _resultText = string.Empty;
        private bool _resultOk;
        private bool _showResult;
        private int _topCoverHeight;
        private bool _paintTopCover = true;

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int HTTRANSPARENT = -1;
        private const int MA_NOACTIVATE = 3;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        public CrosshairOverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Fuchsia;
            TransparencyKey = Color.Fuchsia;
            DoubleBuffered = true;
            TabStop = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
        }

        public bool ShowCrosshair
        {
            get { return _showCrosshair; }
            set { _showCrosshair = value; Invalidate(); }
        }

        /// <summary>
        /// 用白色覆盖 VM 自带的深色工具条，保持界面全白。
        /// 该区域由叠加窗口绘制，不依赖 VM 内部 WPF 控件是否暴露。
        /// </summary>
        public int TopCoverHeight
        {
            get { return _topCoverHeight; }
            set
            {
                _topCoverHeight = Math.Max(0, value);
                Invalidate();
            }
        }

        /// <summary>
        /// VM 原生黑底工具条无法可靠改成白色时必须保留控件。
        /// 为 false 时仅把该高度作为布局 inset，不绘制白色覆盖。
        /// </summary>
        public bool PaintTopCover
        {
            get { return _paintTopCover; }
            set { _paintTopCover = value; Invalidate(); }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public void UpdatePointer(Point? screenPoint)
        {
            _pointer = screenPoint.HasValue ? PointToClient(screenPoint.Value) : (Point?)null;
            Invalidate();
        }

        public void SetResult(string text, bool ok)
        {
            _resultText = text ?? string.Empty;
            _resultOk = ok;
            _showResult = !string.IsNullOrEmpty(_resultText);
            Invalidate();
        }

        public void ClearResult()
        {
            _showResult = false;
            _resultText = string.Empty;
            Invalidate();
        }

        public void SetScreenBounds(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            Bounds = bounds;
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 必须填充透明色键，否则未绘制区域会变成黑色。
            e.Graphics.Clear(TransparencyKey);
        }

        protected override void OnBackColorChanged(EventArgs e)
        {
            // 透明底色不参与日夜主题；防止其他通用样式入口再次把图像盖住。
            if (BackColor.ToArgb() != Color.Fuchsia.ToArgb())
            {
                BackColor = Color.Fuchsia;
                return;
            }
            base.OnBackColorChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width <= 4 || Height <= 4) return;

            if (_paintTopCover && _topCoverHeight > 0)
            {
                using (var brush = new SolidBrush(Color.White))
                    e.Graphics.FillRectangle(brush, 0, 0, Width, Math.Min(_topCoverHeight, Height));
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, Math.Min(_topCoverHeight, Height) - 1,
                        Width, Math.Min(_topCoverHeight, Height) - 1);
            }

            if (_showCrosshair)
                DrawCrosshair(e.Graphics);
            if (_showResult)
                DrawResultBadge(e.Graphics);
        }

        private void DrawCrosshair(Graphics graphics)
        {
            Point point = _pointer ?? new Point(Width / 2, Math.Max(_topCoverHeight + 20, Height / 2));
            if (point.X < 0 || point.Y < 0 || point.X >= Width || point.Y >= Height) return;
            if (point.Y <= _topCoverHeight) return;

            using (var pen = new Pen(Color.FromArgb(205, 110, 110), 1.2f))
            {
                pen.DashStyle = DashStyle.Solid;
                graphics.DrawLine(pen, 0, point.Y, point.X - 12, point.Y);
                graphics.DrawLine(pen, point.X + 12, point.Y, Width, point.Y);
                graphics.DrawLine(pen, point.X, _topCoverHeight, point.X, point.Y - 12);
                graphics.DrawLine(pen, point.X, point.Y + 12, point.X, Height);
            }

            using (var brush = new SolidBrush(Color.Black))
            using (var textBrush = new SolidBrush(Color.White))
            {
                string coordinate = string.Format("X:{0}  Y:{1}", point.X, point.Y);
                SizeF size = graphics.MeasureString(coordinate, Theme.SmallFont);
                var rect = new RectangleF(8, Height - size.Height - 10, size.Width + 12, size.Height + 4);
                graphics.FillRectangle(brush, rect);
                graphics.DrawString(coordinate, Theme.SmallFont, textBrush, rect.X + 6, rect.Y + 2);
            }
        }

        private void DrawResultBadge(Graphics graphics)
        {
            Color color = _resultOk ? Theme.Ok : Theme.Ng;
            using (var font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold))
            {
                SizeF size = graphics.MeasureString(_resultText, font);
                float top = _topCoverHeight + 12;
                if (top + size.Height + 10 > Height) top = _topCoverHeight + 4;
                var rect = new RectangleF(Width - size.Width - 36, top, size.Width + 24, size.Height + 10);
                using (var brush = new SolidBrush(color))
                    graphics.FillRectangle(brush, rect);
                using (var pen = new Pen(Color.White, 2))
                    graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                using (var textBrush = new SolidBrush(Color.White))
                    graphics.DrawString(_resultText, font, textBrush, rect.X + 12, rect.Y + 4);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pointer = null;
                _resultText = string.Empty;
            }
            base.Dispose(disposing);
        }
    }
}
