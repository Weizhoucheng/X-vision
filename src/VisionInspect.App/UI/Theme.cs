using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VisionInspect.Core;

namespace VisionInspect.App.UI
{
    /// <summary>
    /// 简约工业主题：白色工作区 + 深蓝品牌区 + 高对比度文字。
    /// 所有窗体、表格、按钮、输入框统一从这里取色，避免局部出现低对比度文字。
    /// </summary>
    public static partial class Theme
    {
        // 白天浅色 / 深色夜间两套调色板；启动时由 AppSettings.UiTheme 选择。
        public static bool IsDark { get; private set; }
        public static Color Background { get; private set; }
        public static Color Surface { get; private set; }
        public static Color SurfaceAlt { get; private set; }
        public static Color Field { get; private set; }
        public static Color Border { get; private set; }
        public static Color Text { get; private set; }
        public static Color TextMuted { get; private set; }
        public static Color Accent { get; private set; }
        public static Color AccentDark { get; private set; }
        public static Color AccentSoft { get; private set; }
        public static Color Ok { get; private set; }
        public static Color Ng { get; private set; }
        public static Color Warning { get; private set; }
        public static Color Idle { get; private set; }
        public static Color BrandSurface { get; private set; }
        public static Color BrandSurfaceAlt { get; private set; }
        public static Color BrandText { get; private set; }
        public static Color BrandMuted { get; private set; }

        static Theme()
        {
            SetDark(false);
        }

        public static void SetDark(bool dark)
        {
            IsDark = dark;
            if (dark)
            {
                Background = Color.FromArgb(0x12, 0x18, 0x26);      // #121826
                Surface = Color.FromArgb(0x1E, 0x29, 0x3B);         // #1E293B
                SurfaceAlt = Color.FromArgb(0x17, 0x20, 0x33);      // #172033
                Field = Color.FromArgb(0x0F, 0x17, 0x2A);           // #0F172A
                Border = Color.FromArgb(0x33, 0x41, 0x55);          // #334155
                Text = Color.FromArgb(0xF1, 0xF5, 0xF9);            // #F1F5F9
                TextMuted = Color.FromArgb(0x94, 0xA3, 0xB8);       // #94A3B8
                Accent = Color.FromArgb(0x3B, 0x82, 0xF6);          // #3B82F6
                AccentDark = Color.FromArgb(0x25, 0x63, 0xEB);      // #2563EB
                AccentSoft = Color.FromArgb(0x1E, 0x3A, 0x5F);
                Ok = Color.FromArgb(0x34, 0xD3, 0x99);              // #34D399
                Ng = Color.FromArgb(0xF8, 0x71, 0x71);              // #F87171
                Warning = Color.FromArgb(0xFB, 0xBF, 0x24);
                Idle = Color.FromArgb(0x94, 0xA3, 0xB8);
                BrandSurface = Color.FromArgb(0x0B, 0x12, 0x20);
                BrandSurfaceAlt = Color.FromArgb(0x11, 0x1B, 0x2E);
                BrandText = Color.FromArgb(0xF8, 0xFA, 0xFC);
                BrandMuted = Color.FromArgb(0x94, 0xA3, 0xB8);
            }
            else
            {
                Background = Color.FromArgb(0xF7, 0xF8, 0xFA);      // #F7F8FA
                Surface = Color.FromArgb(0xFF, 0xFF, 0xFF);         // #FFFFFF
                SurfaceAlt = Color.FromArgb(0xF1, 0xF5, 0xF9);      // #F1F5F9
                Field = Color.FromArgb(0xFF, 0xFF, 0xFF);
                Border = Color.FromArgb(0xE5, 0xE7, 0xEB);          // #E5E7EB
                Text = Color.FromArgb(0x1F, 0x29, 0x37);            // #1F2937
                TextMuted = Color.FromArgb(0x6B, 0x72, 0x80);       // #6B7280
                Accent = Color.FromArgb(0x25, 0x63, 0xEB);          // #2563EB
                AccentDark = Color.FromArgb(0x1D, 0x4E, 0xD8);      // #1D4ED8
                AccentSoft = Color.FromArgb(0xEF, 0xF6, 0xFF);      // #EFF6FF
                Ok = Color.FromArgb(0x10, 0x94, 0x60);              // #109460
                Ng = Color.FromArgb(0xDC, 0x4F, 0x4F);              // #DC4F4F
                Warning = Color.FromArgb(0xB4, 0x53, 0x09);
                Idle = Color.FromArgb(0x94, 0xA3, 0xB8);
                BrandSurface = Color.FromArgb(0x0F, 0x17, 0x2A);    // #0F172A
                BrandSurfaceAlt = Color.FromArgb(0x1E, 0x29, 0x3B); // #1E293B
                BrandText = Color.FromArgb(0xF8, 0xFA, 0xFC);
                BrandMuted = Color.FromArgb(0xCB, 0xD5, 0xE1);
            }
        }

        // 字体整体上调一档，优先使用系统自带微软雅黑 UI，保证中文清晰。
        public static readonly Font BaseFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font SmallFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font BoldFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font TitleFont = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font ResultFont = new Font("Microsoft YaHei UI", 30F, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font MonoFont = new Font("Consolas", 9.75F, FontStyle.Regular, GraphicsUnit.Point);

        public enum ButtonKind
        {
            Default,
            Primary,
            Danger,
            Success,
            Ghost
        }

        private sealed class ButtonBinding { public ButtonKind Kind; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, ButtonBinding> ButtonKinds =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Button, ButtonBinding>();

        private static Color[] Palette(bool dark)
        {
            if (dark)
                return new[]
                {
                    Color.FromArgb(0x12, 0x18, 0x26), Color.FromArgb(0x1E, 0x29, 0x3B), Color.FromArgb(0x17, 0x20, 0x33),
                    Color.FromArgb(0x0F, 0x17, 0x2A), Color.FromArgb(0x33, 0x41, 0x55), Color.FromArgb(0xF1, 0xF5, 0xF9),
                    Color.FromArgb(0x94, 0xA3, 0xB8), Color.FromArgb(0x3B, 0x82, 0xF6), Color.FromArgb(0x25, 0x63, 0xEB),
                    Color.FromArgb(0x1E, 0x3A, 0x5F), Color.FromArgb(0x34, 0xD3, 0x99), Color.FromArgb(0xF8, 0x71, 0x71),
                    Color.FromArgb(0xFB, 0xBF, 0x24), Color.FromArgb(0x94, 0xA3, 0xB8), Color.FromArgb(0x0B, 0x12, 0x20),
                    Color.FromArgb(0x11, 0x1B, 0x2E), Color.FromArgb(0xF8, 0xFA, 0xFC), Color.FromArgb(0x94, 0xA3, 0xB8)
                };
            return new[]
            {
                Color.FromArgb(0xF7, 0xF8, 0xFA), Color.FromArgb(0xFF, 0xFF, 0xFF), Color.FromArgb(0xF1, 0xF5, 0xF9),
                Color.FromArgb(0xFF, 0xFF, 0xFF), Color.FromArgb(0xE5, 0xE7, 0xEB), Color.FromArgb(0x1F, 0x29, 0x37),
                Color.FromArgb(0x6B, 0x72, 0x80), Color.FromArgb(0x25, 0x63, 0xEB), Color.FromArgb(0x1D, 0x4E, 0xD8),
                Color.FromArgb(0xEF, 0xF6, 0xFF), Color.FromArgb(0x10, 0x94, 0x60), Color.FromArgb(0xDC, 0x4F, 0x4F),
                Color.FromArgb(0xB4, 0x53, 0x09), Color.FromArgb(0x94, 0xA3, 0xB8), Color.FromArgb(0x0F, 0x17, 0x2A),
                Color.FromArgb(0x1E, 0x29, 0x3B), Color.FromArgb(0xF8, 0xFA, 0xFC), Color.FromArgb(0xCB, 0xD5, 0xE1)
            };
        }

        /// <summary>原地切换白天/夜间，不重启应用。</summary>
        public static void ApplyTheme(Control root)
        {
            if (root == null) return;
            Color[] oldColors = Palette(!IsDark);
            Color[] newColors = Palette(IsDark);
            root.SuspendLayout();
            try
            {
                ApplyControl(root, oldColors, newColors);
            }
            finally
            {
                root.ResumeLayout(true);
            }
            root.Invalidate(true);
        }

        private static Color MapColor(Color value, Color[] oldColors, Color[] newColors)
        {
            if (value.IsEmpty || value == Color.Transparent) return value;
            for (int i = 0; i < oldColors.Length; i++)
                if (value.ToArgb() == oldColors[i].ToArgb()) return newColors[i];
            return value;
        }

        private static void ApplyControl(Control control, Color[] oldColors, Color[] newColors)
        {
            if (control == null) return;
            if (control is Form transparentForm && !transparentForm.TransparencyKey.IsEmpty) return;
            string ns = control.GetType().Namespace ?? string.Empty;
            if (ns.StartsWith("VMControls", StringComparison.Ordinal)) return;

            if (control is Button button)
            {
                ButtonBinding binding;
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                ApplyButtonColors(button, ButtonKinds.TryGetValue(button, out binding) ? binding.Kind : ButtonKind.Default);
            }
            else if (control is TextBox textBox) StyleTextBox(textBox);
            else if (control is ComboBox combo) StyleCombo(combo);
            else if (control is NumericUpDown numeric) StyleNumeric(numeric);
            else if (control is DataGridView grid) StyleGrid(grid);
            else
            {
                Color back = MapColor(control.BackColor, oldColors, newColors);
                // 深色模式不允许残留白色/近白色面板，统一收口到 Surface。
                if (IsDark && !(control is PictureBox) && back.A > 0 && back.GetBrightness() > 0.94f)
                    back = Surface;
                control.BackColor = back;
                control.ForeColor = MapColor(control.ForeColor, oldColors, newColors);
            }

            if (control is ListView listView)
                ApplyNativeListViewTheme(listView);

            Form form = control as Form;
            if (form != null)
            {
                form.BackColor = Background;
                form.ForeColor = Text;
                ApplyWindowChrome(form);
            }

            foreach (Control child in control.Controls)
                ApplyControl(child, oldColors, newColors);
            control.Invalidate();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void ApplyWindowChrome(Form form)
        {
            if (form == null) return;
            try
            {
                if (!form.IsHandleCreated) return;
                int dark = IsDark ? 1 : 0;
                DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
                int border = ColorTranslator.ToWin32(IsDark
                    ? Color.FromArgb(48, 63, 80)
                    : Color.FromArgb(214, 222, 232));
                DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));
            }
            catch { }
        }

        public static void ApplyContextMenu(ContextMenuStrip menu)
        {
            if (menu == null) return;
            menu.BackColor = Surface;
            menu.ForeColor = Text;
            foreach (ToolStripItem item in menu.Items)
            {
                item.ForeColor = Text;
                item.BackColor = Surface;
                if (item is ToolStripMenuItem menuItem && menuItem.Tag is UiIcon)
                {
                    Image old = menuItem.Image;
                    menuItem.Image = UiIcons.ToBitmap((UiIcon)menuItem.Tag, 16, Text);
                    if (old != null) old.Dispose();
                }
            }
        }

        public static void StyleListView(ListView list)
        {
            list.BackColor = Surface;
            list.ForeColor = Text;
            list.OwnerDraw = true;
            list.DrawColumnHeader += (s, e) =>
            {
                using (var brush = new SolidBrush(SurfaceAlt))
                    e.Graphics.FillRectangle(brush, e.Bounds);
                using (var pen = new Pen(Border))
                    e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, BoldFont, e.Bounds, Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            list.DrawItem += (s, e) => { e.DrawDefault = false; };
            list.DrawSubItem += (s, e) =>
            {
                bool selected = (e.ItemState & ListViewItemStates.Selected) != 0;
                Color rowColor = selected ? AccentSoft : (e.ItemIndex % 2 == 0 ? Surface : SurfaceAlt);
                using (var brush = new SolidBrush(rowColor))
                    e.Graphics.FillRectangle(brush, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, SmallFont, e.Bounds,
                    selected ? Text : Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            list.HandleCreated += (s, e) => ApplyNativeListViewTheme(list);
            list.SizeChanged += (s,e) => {
                if(list.Columns.Count<1)return;
                int used=0;for(int i=0;i<list.Columns.Count-1;i++)used+=list.Columns[i].Width;
                int width=Math.Max(100,list.ClientSize.Width-used-4);
                if(list.Columns[list.Columns.Count-1].Width!=width)list.Columns[list.Columns.Count-1].Width=width;
            };
            ApplyNativeListViewTheme(list);
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        public static void ApplyNativeListViewTheme(ListView list)
        {
            if (list == null || !list.IsHandleCreated) return;
            try { SetWindowTheme(list.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null); }
            catch { }
        }

        public static void ApplyNativeTabTheme(TabControl tab)
        {
            if (tab == null || !tab.IsHandleCreated) return;
            try { SetWindowTheme(tab.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null); }
            catch { }
        }

        public static void StyleForm(Form form)
        {
            form.BackColor = Background;
            form.ForeColor = Text;
            form.Font = BaseFont;
            form.AutoScaleMode = AutoScaleMode.Dpi;
            try
            {
                Icon icon = BrandAssets.AppIcon;
                if (icon != null) form.Icon = icon;
                form.ShowIcon = true;
            }
            catch
            {
                // 图标加载失败不应阻断业务窗口。
            }

            // 字段在构造完成后才有值，显示前统一设置 Name，便于现场诊断与 UI 自动化。
            form.Load += (s, e) =>
            {
                ApplyWindowChrome(form);
                AssignControlNames(form);
            };
        }

        /// <summary>
        /// 字段 _btnStart 对应 Control.Name=btnStart。局部容器按父名称/类型/序号命名。
        /// 不递归改写 SDK 控件内部节点，避免影响第三方控件自身约定。
        /// </summary>
        public static void AssignControlNames(Form form)
        {
            if (string.IsNullOrEmpty(form.Name)) form.Name = form.GetType().Name;
            foreach (FieldInfo field in form.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (!typeof(Control).IsAssignableFrom(field.FieldType)) continue;
                Control control = field.GetValue(form) as Control;
                if (control != null && string.IsNullOrEmpty(control.Name))
                    control.Name = field.Name.TrimStart('_');
            }
            AssignChildNames(form);
        }

        private static void AssignChildNames(Control parent)
        {
            if (parent.GetType().Namespace != null &&
                parent.GetType().Namespace.StartsWith("VMControls", StringComparison.Ordinal)) return;
            for (int index = 0; index < parent.Controls.Count; index++)
            {
                Control child = parent.Controls[index];
                if (string.IsNullOrEmpty(child.Name))
                    child.Name = parent.Name + "_" + child.GetType().Name + index;
                AssignChildNames(child);
            }
        }

        public static void StylePanel(Control control, Color? back = null, Color? border = null)
        {
            control.BackColor = back ?? Surface;
            control.ForeColor = Text;
            control.Font = BaseFont;
            if (border.HasValue)
                control.Paint += (s, e) =>
                {
                    using (var pen = new Pen(border.Value))
                        e.Graphics.DrawRectangle(pen, 0, 0, control.Width - 1, control.Height - 1);
                };
        }

        public static void StyleLabel(Label label, bool muted = false, bool bold = false)
        {
            label.BackColor = Color.Transparent;
            label.ForeColor = muted ? TextMuted : Text;
            label.Font = bold ? BoldFont : BaseFont;
            label.AutoSize = true;
        }

        public static void StyleButton(Button button, ButtonKind kind = ButtonKind.Default)
        {
            ButtonBinding binding;
            if (!ButtonKinds.TryGetValue(button, out binding))
            {
                binding = new ButtonBinding();
                ButtonKinds.Add(button, binding);
                button.EnabledChanged += (s, e) => ApplyButtonColors(button, binding.Kind);
            }
            binding.Kind = kind;
            var rounded = button as Controls.RoundedButton;
            if (rounded != null && rounded.Icon == UiIcon.None)
                rounded.Icon = UiIcons.ForText(button.Text);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.Font = BoldFont;
            button.Cursor = Cursors.Hand;
            button.Height = 34;
            button.AutoSize = false;
            button.UseVisualStyleBackColor = false;
            ApplyButtonColors(button, kind);
        }

        private static void ApplyButtonColors(Button button, ButtonKind kind)
        {
            if (!button.Enabled)
            {
                button.BackColor = SurfaceAlt;
                button.ForeColor = TextMuted;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = SurfaceAlt;
                button.FlatAppearance.MouseDownBackColor = SurfaceAlt;
                return;
            }

            switch (kind)
            {
                case ButtonKind.Primary:
                    button.BackColor = Accent;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.BorderColor = Accent;
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(18, 112, 224);
                    button.FlatAppearance.MouseDownBackColor = AccentDark;
                    break;
                case ButtonKind.Success:
                    button.BackColor = Ok;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.BorderColor = Ok;
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(24, 145, 90);
                    button.FlatAppearance.MouseDownBackColor = Color.FromArgb(15, 100, 61);
                    break;
                case ButtonKind.Danger:
                    button.BackColor = Ng;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.BorderColor = Ng;
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(214, 55, 55);
                    button.FlatAppearance.MouseDownBackColor = Color.FromArgb(165, 30, 30);
                    break;
                case ButtonKind.Ghost:
                    button.BackColor = Surface;
                    button.ForeColor = Text;
                    button.FlatAppearance.BorderColor = Border;
                    button.FlatAppearance.MouseOverBackColor = AccentSoft;
                    button.FlatAppearance.MouseDownBackColor = Color.FromArgb(215, 232, 252);
                    break;
                default:
                    button.BackColor = Surface;
                    button.ForeColor = Text;
                    button.FlatAppearance.BorderColor = Border;
                    button.FlatAppearance.MouseOverBackColor = AccentSoft;
                    button.FlatAppearance.MouseDownBackColor = Color.FromArgb(215, 232, 252);
                    break;
            }
        }

        public static void StyleTextBox(TextBox textBox)
        {
            textBox.BackColor = Field;
            textBox.ForeColor = Text;
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.Font = BaseFont;
        }

        public static void StyleCombo(ComboBox combo)
        {
            combo.BackColor = Field;
            combo.ForeColor = Text;
            combo.FlatStyle = FlatStyle.Flat;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            if (!ComboPaintBindings.TryGetValue(combo, out object marker))
            {
                ComboPaintBindings.Add(combo, new object());
                combo.DrawItem += (sender,e) => {
                    Color back=(e.State & DrawItemState.Selected)!=0?AccentSoft:Field;
                    using(var brush=new SolidBrush(back))e.Graphics.FillRectangle(brush,e.Bounds);
                    string text=e.Index>=0&&e.Index<combo.Items.Count?combo.GetItemText(combo.Items[e.Index]):combo.Text;
                    TextRenderer.DrawText(e.Graphics,text,combo.Font,e.Bounds,Text,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                };
            }
            combo.Font = BaseFont;
            combo.IntegralHeight = false;
            combo.Height = 32;
        }

        public static void StyleNumeric(NumericUpDown numeric)
        {
            numeric.BackColor = Field;
            numeric.ForeColor = Text;
            numeric.BorderStyle = BorderStyle.FixedSingle;
            numeric.Font = BaseFont;
            numeric.Height = 32;
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox,object> ComboPaintBindings =
            new System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox,object>();

        public static Control WrapField(Control field)
        {
            if (field == null || field.Parent == null) return field;
            if (field.Parent is VisionInspect.App.UI.Controls.RoundedFieldHost) return field.Parent;
            Control parent = field.Parent;
            if (parent is TableLayoutPanel) return field;
            int index = parent.Controls.GetChildIndex(field);
            var host = new VisionInspect.App.UI.Controls.RoundedFieldHost(field)
            {
                Location = field.Location,
                Size = new Size(field.Width + 2, field.Height + 2),
                Anchor = field.Anchor,
                Margin = field.Margin
            };
            parent.Controls.Add(host);
            parent.Controls.SetChildIndex(host, index);
            field.Location = new Point(1, 1);
            field.Size = host.ClientSize;
            return host;
        }

        public static void RoundControl(Control control, int radius)
        {
            if (control == null) return;
            Action apply = () =>
            {
                if (control.IsDisposed || control.Width <= 1 || control.Height <= 1) return;
                using (GraphicsPath path = Rounded(new Rectangle(0, 0, control.Width, control.Height),
                           Math.Max(2, Math.Min(radius, Math.Min(control.Width, control.Height) / 2))))
                {
                    Region previous = control.Region;
                    control.Region = new Region(path);
                    if (previous != null) previous.Dispose();
                }
            };
            apply();
            control.Resize += (s, e) => apply();
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

        public static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = Surface;
            grid.BorderStyle = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
            grid.ColumnHeadersDefaultCellStyle.Font = BoldFont;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersHeight = 36;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
            grid.DefaultCellStyle.SelectionForeColor = Text;
            grid.DefaultCellStyle.Font = BaseFont;
            grid.AlternatingRowsDefaultCellStyle.BackColor = SurfaceAlt;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Text;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = AccentSoft;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Text;
            grid.RowHeadersVisible = false;
            grid.RowTemplate.Height = 32;
            grid.GridColor = Border;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }

        public static void StyleTab(TabControl tabs)
        {
            tabs.Font = BoldFont;
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(128, 34);
            tabs.Padding = new Point(12, 4);
            tabs.DrawItem += (s, e) =>
            {
                TabControl control = (TabControl)s;
                bool selected = e.Index == control.SelectedIndex;
                Rectangle rect = new Rectangle(e.Bounds.X + 3, e.Bounds.Y + 3,
                    Math.Max(1, e.Bounds.Width - 6), Math.Max(1, e.Bounds.Height - 7));
                using (GraphicsPath path = Rounded(rect, 8))
                using (var brush = new SolidBrush(selected ? AccentSoft : SurfaceAlt))
                using (var pen = new Pen(selected ? Accent : Border, selected ? 1.4f : 1f))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }
                string text = control.TabPages[e.Index].Text;
                UiIcon icon = UiIcons.ForText(text);
                int iconSize = 15;
                int gap = icon == UiIcon.None ? 0 : 6;
                Size textSize = TextRenderer.MeasureText(text, selected ? BoldFont : BaseFont);
                int content = (icon == UiIcon.None ? 0 : iconSize) + gap + textSize.Width;
                int x = rect.Left + Math.Max(8, (rect.Width - content) / 2);
                if (icon != UiIcon.None)
                {
                    UiIcons.Draw(e.Graphics, icon, new Rectangle(x, rect.Top + (rect.Height - iconSize) / 2, iconSize, iconSize),
                        selected ? Accent : TextMuted, 2f);
                    x += iconSize + gap;
                }
                TextRenderer.DrawText(e.Graphics, text, selected ? BoldFont : BaseFont,
                    new Rectangle(x, rect.Top, Math.Max(0, rect.Right - x - 4), rect.Height),
                    selected ? Accent : TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                    TextFormatFlags.EndEllipsis);
            };
        }

        public static Label MakeLabel(string text, bool muted = false, bool bold = false)
        {
            var label = new Label { Text = text };
            StyleLabel(label, muted, bold);
            return label;
        }

        public static Button MakeButton(string text, EventHandler onClick, ButtonKind kind = ButtonKind.Default, int width = 92)
        {
            UiIcon icon = UiIcons.ForText(text);
            int textWidth = TextRenderer.MeasureText(text ?? string.Empty, BoldFont,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
            int need = textWidth + (icon == UiIcon.None ? 0 : 24) + 22;
            var button = new Controls.RoundedButton
            {
                Text = text,
                Width = Math.Max(width, need),
                Icon = icon
            };
            StyleButton(button, kind);
            if (onClick != null) button.Click += onClick;
            return button;
        }

        public static Color StateColor(DetectionRunState state)
        {
            switch (state)
            {
                case DetectionRunState.Running: return Ok;
                case DetectionRunState.Paused: return Warning;
                case DetectionRunState.Stopping: return Warning;
                case DetectionRunState.Fault: return Ng;
                default: return Idle;
            }
        }

        public static string StateText(DetectionRunState state)
        {
            switch (state)
            {
                case DetectionRunState.Running: return "检测中";
                case DetectionRunState.Paused: return "暂停";
                case DetectionRunState.Stopping: return "停止中";
                case DetectionRunState.Fault: return "异常";
                default: return "空闲";
            }
        }

        /// <summary>空闲且方案已加载时显示“就绪”，形成空闲/就绪/检测中/暂停/异常五态显示。</summary>
        public static string StateText(DetectionRunState state, bool schemeReady)
        {
            if (state == DetectionRunState.Idle && schemeReady) return "就绪";
            return StateText(state);
        }
    }
}
