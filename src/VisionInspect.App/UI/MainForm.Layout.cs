using System;
using System.Drawing;
using System.Windows.Forms;
using VisionInspect.App.UI.Controls;

namespace VisionInspect.App.UI
{
    public sealed partial class MainForm
    {
        private Panel _pnlTopBar;
        private Panel _pnlBrand;
        private PictureBox _picBrandLogo;
        private Label _lblAppTitle;
        private Label _lblProductName;
        private ComboBox _cboSolution;
        private Button _btnLoadSolution;
        private Button _btnManageSolutions;
        private Button _btnImportFiles;
        private Button _btnImportFolder;
        private CameraSelector _cameraSelector;
        private RoleBadge _roleBadge;

        private Button _btnLoginLogout;
        private Button _btnSettings;
        private Button _btnExportReport;
        private SegmentedControl _segDetectionMode;
        private Button _btnStart;
        private Button _btnPause;
        private Button _btnResume;
        private Button _btnStop;
        private Button _btnResetStats;
        private Label _lblMessage;

        private Label _lblImageTitle;
        private ComboBox _cboRenderSource;
        private Button _btnZoomIn;
        private Button _btnZoomOut;
        private Button _btnActualSize;
        private CheckBox _chkCrosshair;
        private Panel _pnlRenderHost;
        private TabControl _tabSource;
        private Panel _pnlImageHeader;
        private TabPage _tabLocal;
        private TabPage _tabCamera;

        private Button _btnClearLocal;
        private ComboBox _cboPlaybackMode;
        private Label _lblLocalCount;
        private ListView _lvLocalImages;

        private ComboBox _cboCameras;
        private Button _btnRefreshCameras;
        private Button _btnConnectCamera;
        private ComboBox _cboTriggerMode;
        private NumericUpDown _numExposure;
        private NumericUpDown _numGain;
        private Button _btnApplyCamera;
        private Button _btnSoftTrigger;
        private Label _lblCameraStatus;
        private PictureBox _picCameraPreview;

        private TabControl _tabRight;
        private int _resultSplitterDistance;
        private TabPage _tabResult;
        private TabPage _tabParams;
        private TabPage _tabLog;
        private Label _lblJudgeResult;
        private Label _lblJudgeSource;
        private Label _lblStatTotal;
        private Label _lblStatOk;
        private Label _lblStatNg;
        private Label _lblStatYield;
        private DataGridView _dgvOutputs;
        private DataGridView _dgvHistory;

        private ComboBox _cboParamModule;
        private Button _btnRefreshModules;
        private Button _btnSaveSolution;
        private Panel _pnlParamHost;
        private Label _lblParamLock;
        private Label _lblParamHint;
        private Button _btnPresetSave;
        private Button _btnPresetLoad;
        private Button _btnParamReset;

        private TextBox _txtSystemLog;
        private Button _btnClearLog;

        private Panel _pnlStatusBar;
        private Label _lblStatusState;
        private Label _lblStatusSolution;
        private Label _lblStatusCamera;
        private Label _lblStatusModbus;
        private Label _lblStatusSerial;
        private Label _lblStatusTcp;
        private Label _lblClock;

        private Timer _clockTimer;
        private SplitContainer _splitMain;
        private CrosshairOverlayWindow _crosshairOverlay;

        private void InitializeLayout()
        {
            SuspendLayout();
            Theme.StyleForm(this);
            Text = "X Vision";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1440, 900);
            MinimumSize = new Size(1366, 768);
            WindowState = FormWindowState.Maximized;

            BuildTopBar();
            BuildStatusBar();
            BuildMainArea();

            // WinForms 从 z-order 末尾向前处理 Dock。Fill 区域必须位于最前，
            // 才能在 Top/Bottom 栏占位后填充剩余空间，否则会盖住顶栏/状态栏。
            Controls.SetChildIndex(_splitMain, 0);

            _clockTimer = new Timer { Interval = 500 };
            _clockTimer.Tick += (s, e) => _lblClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _clockTimer.Start();

            ResumeLayout(false);
            PerformLayout();
        }

        private void BuildStatusBar()
        {
            _pnlStatusBar = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = Theme.SurfaceAlt };
            _pnlStatusBar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, 0, _pnlStatusBar.Width, 0);
            };

            var flp = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(10, 4, 0, 0)
            };
            _lblStatusState = MakeStatusLabel("空闲", Theme.Idle);
            _lblStatusSolution = MakeStatusLabel("方案：未加载", Theme.TextMuted);
            _lblStatusCamera = MakeStatusLabel("相机：未连接", Theme.TextMuted);
            _lblStatusModbus = MakeStatusLabel("Modbus：未启用", Theme.TextMuted);
            _lblStatusSerial = MakeStatusLabel("串口：未启用", Theme.TextMuted);
            _lblStatusTcp = MakeStatusLabel("触发TCP：未启用", Theme.TextMuted);
            flp.Controls.Add(_lblStatusState);
            flp.Controls.Add(MakeSeparator());
            flp.Controls.Add(_lblStatusSolution);
            flp.Controls.Add(MakeSeparator());
            flp.Controls.Add(_lblStatusCamera);
            flp.Controls.Add(MakeSeparator());
            flp.Controls.Add(_lblStatusModbus);
            flp.Controls.Add(MakeSeparator());
            flp.Controls.Add(_lblStatusSerial);
            flp.Controls.Add(MakeSeparator());
            flp.Controls.Add(_lblStatusTcp);

            _lblClock = new Label
            {
                Dock = DockStyle.Right,
                Width = 190,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Theme.TextMuted,
                Font = Theme.MonoFont,
                Padding = new Padding(0, 0, 12, 0)
            };

            _pnlStatusBar.Controls.Add(flp);
            _pnlStatusBar.Controls.Add(_lblClock);
            Controls.Add(_pnlStatusBar);
        }

        private void BuildMainArea()
        {
            _splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 5,
                BackColor = Theme.Border
            };

            // ---------------- 左侧图像区 ----------------
            _pnlImageHeader = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Surface };
            _pnlImageHeader.Paint += (s, e) =>
            {
                using (var brush = new SolidBrush(Theme.Surface))
                    e.Graphics.FillRectangle(brush, _pnlImageHeader.ClientRectangle);
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, _pnlImageHeader.Height - 1, _pnlImageHeader.Width, _pnlImageHeader.Height - 1);
            };
            _lblImageTitle = Theme.MakeLabel("图像显示", false, true);
            _lblImageTitle.Location = new Point(14, 13);
            _lblImageTitle.AutoSize = false;
            _lblImageTitle.Size = new Size(78, 24);
            _cboRenderSource = new VisionInspect.App.UI.Controls.StudioComboBox
            {
                Location = new Point(100, 8),
                Width = 340,
                DropDownWidth = 420,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            Theme.StyleCombo(_cboRenderSource);
            var flpImageTools = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(0, 5, 10, 3)
            };
            _btnZoomIn = Theme.MakeButton("＋", null, Theme.ButtonKind.Ghost, 38);
            _btnZoomOut = Theme.MakeButton("－", null, Theme.ButtonKind.Ghost, 38);
            ((RoundedButton)_btnZoomIn).Icon = UiIcon.None;
            ((RoundedButton)_btnZoomOut).Icon = UiIcon.None;
            _btnZoomIn.Width = _btnZoomOut.Width = 38;
            _btnActualSize = Theme.MakeButton("1:1", null, Theme.ButtonKind.Ghost, 48);
            _chkCrosshair = new CheckBox
            {
                Text = "十字线",
                AutoSize = true,
                ForeColor = Theme.Text,
                Checked = true,
                Margin = new Padding(8, 7, 0, 0)
            };
            _btnActualSize.Margin = new Padding(4, 0, 0, 0);
            flpImageTools.Controls.Add(_btnZoomIn);
            flpImageTools.Controls.Add(_btnZoomOut);
            flpImageTools.Controls.Add(_chkCrosshair);
            flpImageTools.Controls.Add(_btnActualSize);
            _pnlImageHeader.SizeChanged += (s, e) =>
                _cboRenderSource.Width = Math.Max(130, Math.Min(340, _pnlImageHeader.Width - flpImageTools.Width - 118));
            _pnlImageHeader.Controls.Add(_lblImageTitle);
            _pnlImageHeader.Controls.Add(_cboRenderSource);
            _pnlImageHeader.Controls.Add(flpImageTools);
            Theme.RoundControl(_pnlImageHeader, 8);

            _pnlRenderHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Border,
                BorderStyle = BorderStyle.None,
                Padding = new Padding(1)
            };
            Theme.RoundControl(_pnlRenderHost, 8);

            _tabSource = new StudioTabControl { Dock = DockStyle.Bottom, Height = 218 };
            Theme.StyleTab(_tabSource);
            _tabLocal = new TabPage("本地图像") { BackColor = Theme.Surface, UseVisualStyleBackColor = false };
            _tabCamera = new TabPage("工业相机") { BackColor = Theme.Surface, UseVisualStyleBackColor = false };
            _tabSource.TabPages.Add(_tabLocal);
            // 相机配置由顶部“工业相机”按钮打开独立对话框，不再放在底部来源页。
            // _tabCamera 仍复用原有控件和事件，只是不加入底部 TabControl。

            BuildLocalTab();
            BuildCameraTab();

            _splitMain.Panel1.BackColor = Theme.Surface;
            _splitMain.Panel1.Controls.Add(_pnlRenderHost);
            _splitMain.Panel1.Controls.Add(_pnlImageHeader);
            _splitMain.Panel1.Controls.Add(_tabSource);

            // ---------------- 右侧数据区 ----------------
            _tabRight = new StudioTabControl { Dock = DockStyle.Fill };
            Theme.StyleTab(_tabRight);
            _tabResult = new TabPage("结果数据") { BackColor = Theme.Surface, UseVisualStyleBackColor = false };
            _tabParams = new TabPage("参数配置") { BackColor = Theme.Surface, UseVisualStyleBackColor = false };
            _tabLog = new TabPage("运行日志") { BackColor = Theme.Surface, UseVisualStyleBackColor = false };
            _tabRight.TabPages.Add(_tabResult);
            _tabRight.TabPages.Add(_tabParams);
            _tabRight.TabPages.Add(_tabLog);

            BuildResultTab();
            BuildParameterTab();
            BuildLogTab();
            _splitMain.Panel2.Controls.Add(_tabRight);

            Controls.Add(_splitMain);
        }

        private void BuildLocalTab()
        {
            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 38,
                AutoSize = false,
                WrapContents = false,
                Padding = new Padding(6, 4, 4, 2)
            };
            _btnClearLocal = Theme.MakeButton("清空", null, Theme.ButtonKind.Ghost, 62);
            var lblPlayback = Theme.MakeLabel("播放", true);
            lblPlayback.Margin = new Padding(6, 7, 4, 0);
            _cboPlaybackMode = new VisionInspect.App.UI.Controls.StudioComboBox { Width = 112, DropDownStyle = ComboBoxStyle.DropDownList };
            Theme.StyleCombo(_cboPlaybackMode);
            _cboPlaybackMode.Items.AddRange(new object[] { "手动单张", "顺序播放", "循环播放" });
            _cboPlaybackMode.SelectedIndex = 2;
            _cboPlaybackMode.Margin = new Padding(0, 1, 8, 0);
            _lblLocalCount = Theme.MakeLabel("共 0 张", true);
            _lblLocalCount.Margin = new Padding(0, 7, 0, 0);
            top.Controls.Add(_btnClearLocal);
            top.Controls.Add(lblPlayback);
            top.Controls.Add(_cboPlaybackMode);
            top.Controls.Add(_lblLocalCount);

            _lvLocalImages = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                MultiSelect = false,
                HideSelection = false,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.None,
                Font = Theme.SmallFont,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _lvLocalImages.Columns.Add("#", 44);
            _lvLocalImages.Columns.Add("文件名", 260);
            _lvLocalImages.Columns.Add("完整路径", 620);
            Theme.StyleListView(_lvLocalImages);

            _tabLocal.Controls.Add(_lvLocalImages);
            _tabLocal.Controls.Add(top);
        }

        private void BuildCameraTab()
        {
            // 相机配置在独立对话框中使用，采用“顶部控件 + 大预览”的纵向布局，
            // 不再沿用底部来源页那种左右挤压的小布局。
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12, 10, 12, 10)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            var row1 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            _cboCameras = new VisionInspect.App.UI.Controls.StudioComboBox { Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
            Theme.StyleCombo(_cboCameras);
            _cboCameras.Margin = new Padding(0, 1, 6, 0);
            _btnRefreshCameras = Theme.MakeButton("刷新", null, Theme.ButtonKind.Ghost, 62);
            _btnConnectCamera = Theme.MakeButton("连接", null, Theme.ButtonKind.Primary, 76);
            _btnRefreshCameras.Margin = new Padding(0, 0, 6, 0);
            _btnConnectCamera.Margin = new Padding(0);
            row1.Controls.Add(_cboCameras);
            row1.Controls.Add(_btnRefreshCameras);
            row1.Controls.Add(_btnConnectCamera);

            var row2 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var lblTrigger = Theme.MakeLabel("触发", true);
            lblTrigger.Margin = new Padding(0, 7, 4, 0);
            _cboTriggerMode = new VisionInspect.App.UI.Controls.StudioComboBox { Width = 96, DropDownStyle = ComboBoxStyle.DropDownList };
            Theme.StyleCombo(_cboTriggerMode);
            _cboTriggerMode.Items.AddRange(new object[] { "软触发", "硬触发", "连续采集" });
            _cboTriggerMode.Margin = new Padding(0, 1, 6, 0);
            var lblExposure = Theme.MakeLabel("曝光(us)", true);
            lblExposure.Margin = new Padding(0, 7, 4, 0);
            _numExposure = new NumericUpDown { Width = 82, Minimum = 0, Maximum = 10000000, DecimalPlaces = 0, Increment = 100 };
            Theme.StyleNumeric(_numExposure);
            _numExposure.Margin = new Padding(0, 1, 6, 0);
            var lblGain = Theme.MakeLabel("增益(dB)", true);
            lblGain.Margin = new Padding(0, 7, 4, 0);
            _numGain = new NumericUpDown { Width = 64, Minimum = 0, Maximum = 1000, DecimalPlaces = 1, Increment = 0.5m };
            Theme.StyleNumeric(_numGain);
            _numGain.Margin = new Padding(0, 1, 6, 0);
            _btnApplyCamera = Theme.MakeButton("应用", null, Theme.ButtonKind.Ghost, 62);
            _btnApplyCamera.Margin = new Padding(0, 0, 6, 0);
            _btnSoftTrigger = Theme.MakeButton("软触发采图", null, Theme.ButtonKind.Ghost, 96);
            _btnSoftTrigger.Margin = new Padding(0);
            row2.Controls.Add(lblTrigger);
            row2.Controls.Add(_cboTriggerMode);
            row2.Controls.Add(lblExposure);
            row2.Controls.Add(_numExposure);
            row2.Controls.Add(lblGain);
            row2.Controls.Add(_numGain);
            row2.Controls.Add(_btnApplyCamera);
            row2.Controls.Add(_btnSoftTrigger);

            _lblCameraStatus = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Theme.TextMuted,
                Font = Theme.SmallFont,
                Text = "相机未连接",
                Padding = new Padding(2, 6, 0, 0)
            };

            _picCameraPreview = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 4, 0, 4)
            };
            table.Controls.Add(row1, 0, 0);
            table.Controls.Add(row2, 0, 1);
            table.Controls.Add(_picCameraPreview, 0, 2);
            table.Controls.Add(_lblCameraStatus, 0, 3);
            _tabCamera.Controls.Add(table);
        }

        private void BuildResultTab()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(6)
            };
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));

            var pnlJudge = new Panel { Dock = DockStyle.Fill, BackColor = Theme.SurfaceAlt };
            _lblJudgeResult = new Label
            {
                Dock = DockStyle.Fill,
                Text = "等待检测",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = Theme.ResultFont,
                ForeColor = Theme.TextMuted
            };
            _lblJudgeSource = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = Theme.SmallFont,
                ForeColor = Theme.TextMuted,
                Text = "—"
            };
            pnlJudge.Controls.Add(_lblJudgeResult);
            pnlJudge.Controls.Add(_lblJudgeSource);
            Theme.RoundControl(pnlJudge, 8);

            var pnlStats = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Theme.Surface,
                Padding = new Padding(0, 4, 0, 4)
            };
            for (int i = 0; i < 4; i++) pnlStats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            _lblStatTotal = MakeStatLabel("检测总数", "0", Theme.Text);
            _lblStatOk = MakeStatLabel("OK", "0", Theme.Ok);
            _lblStatNg = MakeStatLabel("NG", "0", Theme.Ng);
            _lblStatYield = MakeStatLabel("合格率", "0.00%", Theme.Accent);
            pnlStats.Controls.Add(_lblStatTotal, 0, 0);
            pnlStats.Controls.Add(_lblStatOk, 1, 0);
            pnlStats.Controls.Add(_lblStatNg, 2, 0);
            pnlStats.Controls.Add(_lblStatYield, 3, 0);
            Theme.RoundControl(pnlStats, 8);

            _dgvOutputs = new DataGridView { Dock = DockStyle.Fill };
            Theme.StyleGrid(_dgvOutputs);
            _dgvOutputs.Columns.Add(new DataGridViewTextBoxColumn { Name = "colOutputName", HeaderText = "变量名", FillWeight = 32 });
            _dgvOutputs.Columns.Add(new DataGridViewTextBoxColumn { Name = "colOutputValue", HeaderText = "值", FillWeight = 36 });
            _dgvOutputs.Columns.Add(new DataGridViewTextBoxColumn { Name = "colOutputType", HeaderText = "类型", FillWeight = 14 });
            _dgvOutputs.Columns.Add(new DataGridViewTextBoxColumn { Name = "colOutputSource", HeaderText = "来源", FillWeight = 18 });
            _dgvOutputs.ColumnHeadersDefaultCellStyle.Font = Theme.SmallFont;
            _dgvOutputs.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            _dgvOutputs.Columns["colOutputType"].MinimumWidth = 48;
            _dgvOutputs.Columns["colOutputSource"].MinimumWidth = 56;

            _dgvHistory = new DataGridView { Dock = DockStyle.Fill };
            Theme.StyleGrid(_dgvHistory);
            _dgvHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "colHisTime", HeaderText = "时间", FillWeight = 38 });
            _dgvHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "colHisResult", HeaderText = "结果", FillWeight = 16 });
            _dgvHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "colHisDuration", HeaderText = "耗时(ms)", FillWeight = 22 });
            _dgvHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "colHisSource", HeaderText = "输入源", FillWeight = 24 });
            _dgvHistory.ColumnHeadersHeight = 44;
            _dgvHistory.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _dgvHistory.ColumnHeadersDefaultCellStyle.Padding = new Padding(2, 0, 2, 0);
            _dgvHistory.ColumnHeadersDefaultCellStyle.Font = Theme.SmallFont;
            _dgvHistory.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            _dgvHistory.Columns["colHisResult"].MinimumWidth = 48;
            _dgvHistory.Columns["colHisDuration"].MinimumWidth = 76;
            _dgvHistory.ScrollBars = ScrollBars.Vertical;

            table.Controls.Add(pnlJudge, 0, 0);
            table.Controls.Add(pnlStats, 0, 1);
            table.Controls.Add(_dgvOutputs, 0, 2);
            table.Controls.Add(_dgvHistory, 0, 3);
            _tabResult.Controls.Add(table);
        }

        private Label MakeStatLabel(string caption, string value, Color color)
        {
            var label = new Label
            {
                Dock = DockStyle.Fill,
                Text = caption + "\r\n" + value,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = color,
                BackColor = Theme.SurfaceAlt,
                Font = Theme.BoldFont,
                Margin = new Padding(4, 4, 4, 4),
                Padding = new Padding(6, 4, 6, 4)
            };
            label.Tag = value;
            Theme.RoundControl(label, 8);
            return label;
        }

        private void BuildParameterTab()
        {
            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 88,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(6, 4, 4, 2)
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            top.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var lblModule = Theme.MakeLabel("算子模块", true);
            lblModule.Margin = new Padding(0, 7, 6, 0);
            _cboParamModule = new VisionInspect.App.UI.Controls.StudioComboBox { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
            Theme.StyleCombo(_cboParamModule);
            _cboParamModule.Margin = new Padding(0, 1, 6, 0);
            _btnRefreshModules = Theme.MakeButton("刷新", null, Theme.ButtonKind.Ghost, 62);
            _btnSaveSolution = Theme.MakeButton("保存方案", null, Theme.ButtonKind.Primary, 92);
            _btnRefreshModules.Margin = new Padding(0, 0, 6, 0);
            _btnSaveSolution.Margin = new Padding(0);
            _cboParamModule.Dock = DockStyle.Fill;
            top.Controls.Add(lblModule, 0, 0);
            top.Controls.Add(_btnRefreshModules, 1, 0);
            top.Controls.Add(_btnSaveSolution, 2, 0);
            top.Controls.Add(_cboParamModule, 0, 1);
            top.SetColumnSpan(_cboParamModule, 3);

            _pnlParamHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(4) };
            _lblParamLock = new Label
            {
                Dock = DockStyle.Fill,
                Text = "参数配置需要管理员权限。\r\n请使用管理员账号登录。",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Warning,
                BackColor = Theme.Surface,
                Font = Theme.BoldFont
            };

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                WrapContents = false,
                Padding = new Padding(6, 5, 4, 2)
            };
            _lblParamHint = Theme.MakeLabel("提示：原生参数控件修改即时生效；保存方案后写入 .sol 文件。", true);
            _lblParamHint.Margin = new Padding(0, 7, 10, 0);
            _btnPresetSave = Theme.MakeButton("保存预设", null, Theme.ButtonKind.Ghost, 92);
            _btnPresetLoad = Theme.MakeButton("加载预设", null, Theme.ButtonKind.Ghost, 92);
            _btnParamReset = Theme.MakeButton("恢复默认", null, Theme.ButtonKind.Ghost, 92);
            foreach (Button button in new[] { _btnPresetSave, _btnPresetLoad, _btnParamReset })
                button.Margin = new Padding(0, 0, 6, 0);
            bottom.Controls.Add(_lblParamHint);
            bottom.Controls.Add(_btnPresetSave);
            bottom.Controls.Add(_btnPresetLoad);
            bottom.Controls.Add(_btnParamReset);

            // 参数页底部“保存预设/加载预设/恢复默认”功能条按需求移除。
            bottom.Visible = false;
            _tabParams.Controls.Add(_pnlParamHost);
            _tabParams.Controls.Add(top);
            _pnlParamHost.Controls.Add(_lblParamLock);
        }

        private void BuildLogTab()
        {
            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 38,
                WrapContents = false,
                Padding = new Padding(6, 4, 4, 2)
            };
            _btnClearLog = Theme.MakeButton("清空显示", null, Theme.ButtonKind.Ghost, 88);
            var lblHint = Theme.MakeLabel("系统日志目录：logs\\system；检测日志目录：logs\\detection", true);
            lblHint.Margin = new Padding(8, 7, 0, 0);
            top.Controls.Add(_btnClearLog);
            top.Controls.Add(lblHint);

            _txtSystemLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Theme.SurfaceAlt,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.MonoFont,
                WordWrap = false
            };
            _tabLog.Controls.Add(_txtSystemLog);
            _tabLog.Controls.Add(top);
        }

        private static Label MakeStatusLabel(string text, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = color,
                Font = Theme.SmallFont,
                Margin = new Padding(0, 2, 0, 0),
                Padding = new Padding(2, 0, 8, 0)
            };
        }

        private static Label MakeSeparator()
        {
            return new Label
            {
                Text = "|",
                AutoSize = true,
                ForeColor = Theme.Border,
                Font = Theme.SmallFont,
                Margin = new Padding(0, 2, 4, 0)
            };
        }
    }
}
