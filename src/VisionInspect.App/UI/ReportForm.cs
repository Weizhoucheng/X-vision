using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using VisionInspect.Core.Abstractions;

namespace VisionInspect.App.UI
{
    /// <summary>按日期范围导出 Excel 批次报表。</summary>
    public sealed class ReportForm : Form
    {
        private readonly IReportService _report;
        private readonly string _defaultDirectory;
        private readonly DateTimePicker _dtpStart;
        private readonly DateTimePicker _dtpEnd;
        private readonly Label _lblStatus;
        private readonly Button _btnExport;
        private CancellationTokenSource _exportCancellation;
        private bool _closeAfterExport;

        public ReportForm(IReportService report, string defaultDirectory)
        {
            _report = report ?? throw new ArgumentNullException(nameof(report));
            _defaultDirectory = defaultDirectory;
            Directory.CreateDirectory(_defaultDirectory);

            Theme.StyleForm(this);
            Text = "导出检测报表";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 280);
            BackColor = Theme.Background;

            var lblTitle = Theme.MakeLabel("批次检测报表", false, true);
            lblTitle.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
            lblTitle.Location = new Point(24, 20);

            var lblHint = Theme.MakeLabel("导出为 .xlsx，包含统计与明细两个工作表。", true);
            lblHint.Location = new Point(26, 54);

            var lblStart = Theme.MakeLabel("开始日期", true);
            lblStart.Location = new Point(28, 100);
            _dtpStart = new DateTimePicker
            {
                Location = new Point(120, 96),
                Width = 150,
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Today
            };

            var lblEnd = Theme.MakeLabel("结束日期", true);
            lblEnd.Location = new Point(292, 100);
            _dtpEnd = new DateTimePicker
            {
                Location = new Point(364, 96),
                Width = 128,
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Today
            };

            _lblStatus = new Label
            {
                Location = new Point(28, 146),
                Width = 460,
                Height = 42,
                ForeColor = Theme.TextMuted,
                Font = Theme.SmallFont,
                Text = "选择日期范围后导出。"
            };

            _btnExport = Theme.MakeButton("导出 Excel", null, Theme.ButtonKind.Primary, 120);
            _btnExport.Location = new Point(280, 206);
            _btnExport.Click += async (s, e) => await ExportAsync();

            var btnClose = Theme.MakeButton("关闭", (s, e) => Close(), Theme.ButtonKind.Ghost, 90);
            btnClose.Location = new Point(408, 206);
            btnClose.DialogResult = DialogResult.Cancel;

            Controls.Add(lblTitle);
            Controls.Add(lblHint);
            Controls.Add(lblStart);
            Controls.Add(_dtpStart);
            Controls.Add(lblEnd);
            Controls.Add(_dtpEnd);
            Controls.Add(_lblStatus);
            Controls.Add(_btnExport);
            Controls.Add(btnClose);
            CancelButton = btnClose;
            FormClosing += (s, e) =>
            {
                if (_exportCancellation == null) return;
                // 等待后台取消并释放文件后再关窗口，防止 continuation 访问已释放控件。
                e.Cancel = true;
                _closeAfterExport = true;
                _exportCancellation.Cancel();
                _lblStatus.Text = "正在取消导出...";
            };
        }

        private async System.Threading.Tasks.Task ExportAsync()
        {
            DateTime start = _dtpStart.Value.Date;
            DateTime end = _dtpEnd.Value.Date.AddDays(1).AddMilliseconds(-1);
            if (end < start)
            {
                MessageBox.Show(this, "结束日期不能早于开始日期。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string defaultFile = Path.Combine(_defaultDirectory,
                string.Format("检测报表_{0:yyyyMMdd}_{1:yyyyMMdd}.xlsx", start, end));
            using (var dialog = new SaveFileDialog
            {
                Filter = "Excel 工作簿|*.xlsx",
                InitialDirectory = _defaultDirectory,
                FileName = Path.GetFileName(defaultFile),
                OverwritePrompt = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _exportCancellation = new CancellationTokenSource();
                try
                {
                    _btnExport.Enabled = false;
                    _lblStatus.ForeColor = Theme.TextMuted;
                    _lblStatus.Text = "正在生成报表，请稍候...";
                    string path = await _report.ExportAsync(start, end, dialog.FileName,
                        _exportCancellation.Token);
                    if (_closeAfterExport) return;
                    _lblStatus.ForeColor = Theme.Ok;
                    _lblStatus.Text = "导出成功：" + path;

                    if (MessageBox.Show(this, "报表导出成功，是否打开所在文件夹？", "完成",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        Process.Start("explorer.exe", "/select,\"" + path + "\"");
                    }
                }
                catch (OperationCanceledException)
                {
                    _lblStatus.ForeColor = Theme.TextMuted;
                    _lblStatus.Text = "已取消导出。";
                }
                catch (Exception ex)
                {
                    _lblStatus.ForeColor = Theme.Ng;
                    _lblStatus.Text = "导出失败：" + ex.Message;
                    MessageBox.Show(this, "导出失败：" + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    _exportCancellation.Dispose();
                    _exportCancellation = null;
                    _btnExport.Enabled = true;
                    if (_closeAfterExport) Close();
                }
            }
        }
    }
}
