using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace VisionInspect.App.UI
{
    /// <summary>管理 .sol 方案目录。只维护路径列表，实际加载/释放由 VisionMasterEngine 完成。</summary>
    public sealed class SolutionManagerForm : Form
    {
        private readonly ListBox _lstSolutions;
        private readonly Label _lblHint;

        public SolutionManagerForm(IEnumerable<string> solutions)
        {
            Solutions = new List<string>();
            if (solutions != null)
            {
                Solutions.AddRange(solutions.Where(x => !string.IsNullOrWhiteSpace(x) &&
                    string.Equals(Path.GetExtension(x), ".sol", StringComparison.OrdinalIgnoreCase)));
            }
            Solutions = Solutions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            Theme.StyleForm(this);
            Text = "视觉方案管理";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(720, 440);

            var lblTitle = Theme.MakeLabel("方案目录", false, true);
            lblTitle.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            lblTitle.Location = new Point(22, 18);

            _lblHint = Theme.MakeLabel("支持添加多个 .sol，主界面下拉切换；切换时自动释放旧方案。", true);
            _lblHint.Location = new Point(24, 48);

            _lstSolutions = new ListBox
            {
                Location = new Point(24, 78),
                Size = new Size(548, 318),
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.BaseFont,
                HorizontalScrollbar = true
            };

            var btnAdd = Theme.MakeButton("添加 .sol", null, Theme.ButtonKind.Primary, 132);
            btnAdd.Location = new Point(588, 78);
            btnAdd.Click += (s, e) => AddSolutions();

            var btnRemove = Theme.MakeButton("移除选中", null, Theme.ButtonKind.Ghost, 132);
            btnRemove.Location = new Point(588, 120);
            btnRemove.Click += (s, e) => RemoveSelected();

            var btnUp = Theme.MakeButton("上移", null, Theme.ButtonKind.Ghost, 62);
            btnUp.Location = new Point(588, 178);
            btnUp.Click += (s, e) => MoveSelected(-1);

            var btnDown = Theme.MakeButton("下移", null, Theme.ButtonKind.Ghost, 62);
            btnDown.Location = new Point(658, 178);
            btnDown.Click += (s, e) => MoveSelected(1);

            var btnOk = Theme.MakeButton("确定", null, Theme.ButtonKind.Success, 100);
            btnOk.Location = new Point(500, 412);
            btnOk.Click += (s, e) =>
            {
                if (Solutions.Count == 0)
                {
                    MessageBox.Show(this, "至少保留一个方案，或取消本窗口。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };

            var btnCancel = Theme.MakeButton("取消", null, Theme.ButtonKind.Ghost, 90);
            btnCancel.Location = new Point(610, 412);
            btnCancel.DialogResult = DialogResult.Cancel;

            Controls.Add(lblTitle);
            Controls.Add(_lblHint);
            Controls.Add(_lstSolutions);
            Controls.Add(btnAdd);
            Controls.Add(btnRemove);
            Controls.Add(btnUp);
            Controls.Add(btnDown);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            RefreshList();
        }

        public List<string> Solutions { get; private set; }

        private void RefreshList()
        {
            _lstSolutions.BeginUpdate();
            try
            {
                _lstSolutions.Items.Clear();
                foreach (string path in Solutions)
                    _lstSolutions.Items.Add(path);
            }
            finally
            {
                _lstSolutions.EndUpdate();
            }
            _lblHint.Text = string.Format("共 {0} 个方案。切换方案会自动释放旧方案。", Solutions.Count);
        }

        private void AddSolutions()
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "VisionMaster 方案|*.sol",
                Multiselect = true,
                Title = "添加视觉方案"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (string file in dialog.FileNames)
                {
                    if (!Solutions.Any(x => string.Equals(x, file, StringComparison.OrdinalIgnoreCase)))
                        Solutions.Add(file);
                }
                RefreshList();
            }
        }

        private void RemoveSelected()
        {
            if (_lstSolutions.SelectedIndex < 0) return;
            Solutions.RemoveAt(_lstSolutions.SelectedIndex);
            RefreshList();
        }

        private void MoveSelected(int direction)
        {
            int index = _lstSolutions.SelectedIndex;
            int target = index + direction;
            if (index < 0 || target < 0 || target >= Solutions.Count) return;
            string value = Solutions[index];
            Solutions.RemoveAt(index);
            Solutions.Insert(target, value);
            RefreshList();
            _lstSolutions.SelectedIndex = target;
        }
    }
}
