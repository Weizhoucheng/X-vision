using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;
using VisionInspect.Infrastructure.Configuration;

namespace VisionInspect.App.UI
{
    /// <summary>
    /// 系统设置。采用“编辑副本，确定后提交”的方式，取消不会污染正在运行的服务。
    /// 保存时同步应用运行时配置；取消不会提交未应用的编辑内容。
    /// </summary>
    public sealed partial class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly IAuthService _auth;

        private TextBox _txtLine;
        private TextBox _txtStation;
        private CheckBox _chkAutoStart;
        private CheckBox _chkRememberSolution;
        private CheckBox _chkConfirmExit;
        private NumericUpDown _numPreviewThrottle;

        private NumericUpDown _numInterval;
        private NumericUpDown _numMaxErrors;
        private NumericUpDown _numRecovery;
        private CheckBox _chkAutoClearStats;
        private CheckBox _chkAutoSaveParams;

        private ComboBox _cboTriggerMode;
        private ComboBox _cboPixelFormat;
        private NumericUpDown _numExposure;
        private NumericUpDown _numGain;
        private CheckBox _chkCameraReconnect;
        private NumericUpDown _numCameraReconnect;
        private NumericUpDown _numFrameTimeout;
        private NumericUpDown _numHardwareTriggerSource;
        private NumericUpDown _numPreviewSkip;

        private TextBox _txtDataRoot;
        private CheckBox _chkDetectionLog;
        private CheckBox _chkImageArchive;
        private CheckBox _chkArchiveNgOnly;
        private ComboBox _cboArchiveFormat;
        private NumericUpDown _numRetention;
        private NumericUpDown _numArchiveQueue;
        private NumericUpDown _numArchiveMemoryMb;
        private TextBox _txtReportDirectory;
        private TextBox _txtCompanyName;

        private CheckBox _chkModbusEnabled;
        private TextBox _txtModbusHost;
        private NumericUpDown _numModbusPort;
        private NumericUpDown _numModbusUnit;
        private NumericUpDown _numModbusStart;
        private NumericUpDown _numModbusCount;
        private NumericUpDown _numModbusResultOffset;
        private NumericUpDown _numModbusOk;
        private NumericUpDown _numModbusNg;

        private CheckBox _chkSerialEnabled;
        private TextBox _txtSerialPort;
        private NumericUpDown _numBaudRate;
        private ComboBox _cboSerialParity;
        private NumericUpDown _numDataBits;
        private ComboBox _cboStopBits;
        private ComboBox _cboSerialEncoding;
        private TextBox _txtOkCommand;
        private TextBox _txtNgCommand;
        private TextBox _txtSerialTrigger;
        private CheckBox _chkSerialTrigger;

        private CheckBox _chkTcpTriggerEnabled;
        private TextBox _txtTcpBind;
        private NumericUpDown _numTcpPort;
        private TextBox _txtTcpTrigger;
        private TextBox _txtTcpAck;

        private TextBox _txtNewPassword;
        private TextBox _txtConfirmPassword;

        public SettingsForm(AppSettings source, IAuthService auth, Bootstrap.AppBootstrapper runtime = null, bool openCommunication = false)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            _auth = auth;
            _runtime = runtime;

            string json = JsonHelper.Serialize(source);
            _settings = JsonHelper.Deserialize<AppSettings>(json) ?? new AppSettings();
            _settings.Normalize();

            Theme.StyleForm(this);
            Text = "系统设置";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(820, 650);

            var tabs = new Controls.StudioTabControl { Dock = DockStyle.Fill, Padding = new Point(16, 8) };
            Theme.StyleTab(tabs);

            tabs.TabPages.Add(BuildGeneralPage());
            tabs.TabPages.Add(BuildDetectionPage());
            tabs.TabPages.Add(BuildCameraPage());
            tabs.TabPages.Add(BuildStoragePage());
            tabs.TabPages.Add(BuildCommunicationPage());
            tabs.TabPages.Add(BuildSecurityPage());
            if (openCommunication) tabs.SelectedIndex = 4;

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Theme.Surface };
            var lblNote = Theme.MakeLabel("保存后立即应用；正在运行的检测会先停止。", true);
            lblNote.Location = new Point(18, 21);
            var btnOk = Theme.MakeButton("保存设置", null, Theme.ButtonKind.Primary, 110);
            _btnSaveSettings = btnOk;
            btnOk.Location = new Point(584, 12);
            btnOk.Click += async (s, e) => await ApplyEditorAsync(true);
            var btnCancel = Theme.MakeButton("取消", null, Theme.ButtonKind.Ghost, 84);
            btnCancel.Location = new Point(704, 12);
            btnCancel.DialogResult = DialogResult.Cancel;
            bottom.Controls.Add(lblNote);
            bottom.Controls.Add(btnOk);
            bottom.Controls.Add(btnCancel);

            Controls.Add(tabs);
            Controls.Add(bottom);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            FormClosing += (s, e) => { if (_saving) e.Cancel = true; };
        }

        public AppSettings Settings { get; private set; }

        private TabPage BuildGeneralPage()
        {
            var page = CreatePage("常规");
            var table = CreateTable();

            _txtLine = MakeText(_settings.LineName);
            _txtStation = MakeText(_settings.StationName);
            _chkAutoStart = MakeCheck("开机自动启动（当前用户）", _settings.General.AutoStart);
            _chkRememberSolution = MakeCheck("记住上次方案并自动加载", _settings.General.RememberLastSolution);
            _chkConfirmExit = MakeCheck("退出前二次确认", _settings.General.ConfirmBeforeExit);
            _numPreviewThrottle = MakeNumber(30, 2000, _settings.General.PreviewThrottleMs);

            AddRow(table, "产线名称", _txtLine);
            AddRow(table, "工位名称", _txtStation);
            AddRow(table, "", _chkAutoStart);
            AddRow(table, "", _chkRememberSolution);
            AddRow(table, "", _chkConfirmExit);
            AddRow(table, "相机预览节流(ms)", _numPreviewThrottle);
            AddNote(table, "预览节流用于降低高分辨率相机对 UI 的压力；不影响检测取图。");
            page.Controls.Add(table);
            return page;
        }

        private TabPage BuildDetectionPage()
        {
            var page = CreatePage("检测");
            var table = CreateTable();
            _numInterval = MakeNumber(0, 3600000, _settings.Detection.IntervalMs);
            _numMaxErrors = MakeNumber(1, 1000, _settings.Detection.MaxConsecutiveErrors);
            _numRecovery = MakeNumber(100, 60000, _settings.Detection.ErrorRecoveryDelayMs);
            _chkAutoClearStats = MakeCheck("切换方案时清零统计", _settings.Detection.AutoClearStatsOnSolutionChange);
            _chkAutoSaveParams = MakeCheck("参数修改后自动保存 .sol（管理员）", _settings.Vision.AutoSaveParameterChanges);

            AddRow(table, "连续检测间隔(ms)", _numInterval);
            AddRow(table, "最大连续异常次数", _numMaxErrors);
            AddRow(table, "异常恢复延时(ms)", _numRecovery);
            AddRow(table, "", _chkAutoClearStats);
            AddRow(table, "", _chkAutoSaveParams);
            AddNote(table, "达到最大连续异常次数后检测进入“异常”状态并停止循环；异常期间会写系统日志。");
            page.Controls.Add(table);
            return page;
        }

        private TabPage BuildCameraPage()
        {
            var page = CreatePage("相机");
            var table = CreateTable();
            _cboTriggerMode = MakeCombo("软触发", "硬触发", "连续采集");
            _cboTriggerMode.SelectedIndex = (int)_settings.Camera.TriggerMode;
            _cboPixelFormat = MakeCombo("Mono8", "RGB24");
            _cboPixelFormat.SelectedIndex = _settings.Camera.PixelFormat == CameraPixelFormat.Mono8 ? 0 : 1;
            _numHardwareTriggerSource = MakeNumber(0, 3, _settings.Camera.HardwareTriggerSource);
            _numExposure = MakeNumber(0, 10000000, (decimal)_settings.Camera.ExposureTimeUs);
            _numGain = MakeNumber(0, 1000, (decimal)_settings.Camera.GainDb, 1, 1);
            _chkCameraReconnect = MakeCheck("相机掉线自动重连", _settings.Camera.EnableAutoReconnect);
            _numCameraReconnect = MakeNumber(500, 60000, _settings.Camera.ReconnectIntervalMs);
            _numFrameTimeout = MakeNumber(500, 120000, _settings.Camera.FrameTimeoutMs);
            _numPreviewSkip = MakeNumber(1, 30, _settings.Camera.PreviewFrameSkip);

            AddRow(table, "触发方式", _cboTriggerMode);
            AddRow(table, "硬触发线号(0-3)", _numHardwareTriggerSource);
            AddRow(table, "像素格式", _cboPixelFormat);
            AddRow(table, "曝光时间(us)", _numExposure);
            AddRow(table, "增益(dB)", _numGain);
            AddRow(table, "", _chkCameraReconnect);
            AddRow(table, "重连间隔(ms)", _numCameraReconnect);
            AddRow(table, "取图超时(ms)", _numFrameTimeout);
            AddRow(table, "预览抽帧", _numPreviewSkip);
            AddNote(table, "相机参数由 MVS SDK 下发；部分型号不支持 RGB24 时程序会回退 BGR8 并在日志中提示。");
            page.Controls.Add(table);
            return page;
        }

        private TabPage BuildStoragePage()
        {
            var page = CreatePage("存储");
            var table = CreateTable();
            _txtDataRoot = MakeText(_settings.Storage.DataRoot);
            _txtDataRoot.Width = 430;
            _chkDetectionLog = MakeCheck("写入每日检测 CSV", _settings.Storage.EnableDetectionLog);
            _chkImageArchive = MakeCheck("归档检测图片", _settings.Storage.EnableImageArchive);
            _chkArchiveNgOnly = MakeCheck("仅归档 NG 图片", _settings.Storage.ArchiveNgOnly);
            _cboArchiveFormat = MakeCombo("PNG", "BMP", "JPEG");
            _cboArchiveFormat.SelectedIndex = _settings.Storage.ArchiveFormat == "bmp" ? 1 :
                _settings.Storage.ArchiveFormat == "jpg" ? 2 : 0;
            _numRetention = MakeNumber(1, 3650, _settings.Storage.RetentionDays);
            _numArchiveQueue = MakeNumber(10, 5000, _settings.Storage.ArchiveQueueCapacity);
            _numArchiveMemoryMb = MakeNumber(16, 4096, _settings.Storage.ArchiveMemoryLimitMb);
            _txtReportDirectory = MakeText(_settings.Report.ExportDirectory);
            _txtReportDirectory.Width = 430;
            _txtCompanyName = MakeText(_settings.Report.CompanyName);
            _txtCompanyName.Width = 430;

            AddRow(table, "数据根目录", _txtDataRoot);
            AddRow(table, "报表目录(留空=数据根/reports)", _txtReportDirectory);
            AddRow(table, "报表公司名称", _txtCompanyName);
            AddRow(table, "", _chkDetectionLog);
            AddRow(table, "", _chkImageArchive);
            AddRow(table, "", _chkArchiveNgOnly);
            AddRow(table, "归档格式", _cboArchiveFormat);
            AddRow(table, "保留天数", _numRetention);
            AddRow(table, "归档队列容量", _numArchiveQueue);
            AddRow(table, "归档内存上限(MiB)", _numArchiveMemoryMb);
            AddNote(table, "保存后新日志、图片和报表立即使用新目录，原目录中的历史文件保留。建议放在独立数据盘。");
            page.Controls.Add(table);
            return page;
        }

        private TabPage BuildCommunicationPage()
        {
            var page = CreatePage("通讯");
            var tabs = new Controls.StudioTabControl { Dock = DockStyle.Fill };
            Theme.StyleTab(tabs);

            tabs.TabPages.Add(BuildModbusPage());
            tabs.TabPages.Add(BuildSerialPage());
            tabs.TabPages.Add(BuildTcpTriggerPage());
            _communicationTabs = tabs;
            page.Controls.Add(tabs);
            if (_runtime != null) BuildOnlinePanel(page);
            return page;
        }

        private TabPage BuildModbusPage()
        {
            var page = CreatePage("Modbus");
            var table = CreateTable();
            _chkModbusEnabled = MakeCheck("启用 Modbus TCP 通信", _settings.Communication.ModbusTcp.Enabled);
            _txtModbusHost = MakeText(_settings.Communication.ModbusTcp.Host);
            _numModbusPort = MakeNumber(1, 65535, _settings.Communication.ModbusTcp.Port);
            _numModbusUnit = MakeNumber(1, 247, _settings.Communication.ModbusTcp.UnitId);
            _numModbusStart = MakeNumber(0, 65535, _settings.Communication.ModbusTcp.StartRegister);
            _numModbusCount = MakeNumber(2, 100, _settings.Communication.ModbusTcp.RegisterCount);
            _numModbusResultOffset = MakeNumber(0, 99, _settings.Communication.ModbusTcp.ResultRegisterOffset);
            _numModbusOk = MakeNumber(0, 65535, _settings.Communication.ModbusTcp.OkValue);
            _numModbusNg = MakeNumber(0, 65535, _settings.Communication.ModbusTcp.NgValue);
            _chkModbusReconnect = MakeCheck("断线后自动重连", _settings.Communication.ModbusTcp.AutoReconnect);
            _numModbusTimeout = MakeNumber(100,10000,_settings.Communication.ModbusTcp.TimeoutMs);
            _numModbusReconnect = MakeNumber(500,60000,_settings.Communication.ModbusTcp.ReconnectIntervalMs);

            AddRow(table, "", _chkModbusEnabled);
            AddRow(table, "PLC IP", _txtModbusHost);
            AddRow(table, "端口", _numModbusPort);
            AddRow(table, "站号", _numModbusUnit);
            AddRow(table, "起始寄存器", _numModbusStart);
            AddRow(table, "寄存器数量", _numModbusCount);
            AddRow(table, "结果寄存器偏移", _numModbusResultOffset);
            AddRow(table, "OK 值", _numModbusOk);
            AddRow(table, "NG 值", _numModbusNg);
            AddRow(table, "事务/连接超时(ms)", _numModbusTimeout);
            AddRow(table, "", _chkModbusReconnect);
            AddRow(table, "重连间隔(ms)", _numModbusReconnect);
            AddNote(table, "默认布局：偏移0=结果，偏移1=序号低字，偏移2=耗时ms；其余映射可在 appsettings.json 的 Mappings 中扩展。");
            page.Controls.Add(table);
            return page;
        }

        private TabPage BuildSerialPage()
        {
            var page = CreatePage("串口");
            var table = CreateTable();
            _chkSerialEnabled = MakeCheck("启用串口 IO", _settings.Communication.Serial.Enabled);
            _txtSerialPort = MakeText(_settings.Communication.Serial.PortName);
            _numBaudRate = MakeNumber(1200, 4000000, _settings.Communication.Serial.BaudRate);
            _cboSerialParity = MakeCombo("None", "Odd", "Even", "Mark", "Space");
            _cboSerialParity.SelectedItem = _settings.Communication.Serial.Parity;
            _numDataBits = MakeNumber(5, 8, _settings.Communication.Serial.DataBits);
            _cboStopBits = MakeCombo("One", "Two", "OnePointFive");
            _cboStopBits.SelectedItem = _settings.Communication.Serial.StopBits;
            _cboSerialEncoding = MakeCombo("Hex", "Ascii");
            _cboSerialEncoding.SelectedItem = _settings.Communication.Serial.CommandEncoding;
            _txtOkCommand = MakeText(_settings.Communication.Serial.OkCommand);
            _txtNgCommand = MakeText(_settings.Communication.Serial.NgCommand);
            _txtSerialTrigger = MakeText(_settings.Communication.Serial.TriggerCommand);
            _chkSerialTrigger = MakeCheck("接收串口触发字符", _settings.Communication.Serial.EnableReceiveTrigger);
            _chkSerialReconnect = MakeCheck("断线后自动重连", _settings.Communication.Serial.AutoReconnect);
            _numSerialReconnect = MakeNumber(500,60000,_settings.Communication.Serial.ReconnectIntervalMs);

            AddRow(table, "", _chkSerialEnabled);
            AddRow(table, "串口", _txtSerialPort);
            AddRow(table, "波特率", _numBaudRate);
            AddRow(table, "校验位", _cboSerialParity);
            AddRow(table, "数据位", _numDataBits);
            AddRow(table, "停止位", _cboStopBits);
            AddRow(table, "指令编码", _cboSerialEncoding);
            AddRow(table, "OK 指令", _txtOkCommand);
            AddRow(table, "NG 指令", _txtNgCommand);
            AddRow(table, "", _chkSerialTrigger);
            AddRow(table, "触发字符", _txtSerialTrigger);
            AddRow(table, "", _chkSerialReconnect);
            AddRow(table, "重连间隔(ms)", _numSerialReconnect);
            AddNote(table, "HEX 示例：AA 55 01 01 55 AA。ASCII 支持 {RESULT}/{SEQ}/{DURATION} 与 \\r\\n 转义。");
            page.Controls.Add(table);
            return page;
        }

        private TabPage BuildTcpTriggerPage()
        {
            var page = CreatePage("TCP");
            var table = CreateTable();
            _chkTcpTriggerEnabled = MakeCheck("启用 TCP 通信", _settings.Communication.TcpTrigger.Enabled);
            _cboTcpMode = MakeCombo("主动连接下位机", "本机监听");
            _cboTcpMode.SelectedIndex = _settings.Communication.TcpTrigger.Mode == "Client" ? 0 : 1;
            _txtTcpRemote = MakeText(_settings.Communication.TcpTrigger.RemoteHost);
            _numTcpRemote = MakeNumber(1,65535,_settings.Communication.TcpTrigger.RemotePort);
            _chkTcpReceive = MakeCheck("接收命令后触发检测", _settings.Communication.TcpTrigger.EnableReceiveTrigger);
            _chkTcpSendResults = MakeCheck("检测完成后发送结果", _settings.Communication.TcpTrigger.SendResults);
            _txtTcpResults = MakeText(_settings.Communication.TcpTrigger.ResultTemplate);
            _chkTcpReconnect = MakeCheck("断线后自动重连", _settings.Communication.TcpTrigger.AutoReconnect);
            _numTcpTimeout = MakeNumber(100,10000,_settings.Communication.TcpTrigger.TimeoutMs);
            _numTcpReconnect = MakeNumber(500,60000,_settings.Communication.TcpTrigger.ReconnectIntervalMs);
            _txtTcpBind = MakeText(_settings.Communication.TcpTrigger.BindAddress);
            _numTcpPort = MakeNumber(1, 65535, _settings.Communication.TcpTrigger.ListenPort);
            _txtTcpTrigger = MakeText(_settings.Communication.TcpTrigger.TriggerCommand);
            _txtTcpAck = MakeText(_settings.Communication.TcpTrigger.AckCommand);

            AddRow(table, "", _chkTcpTriggerEnabled);
            AddRow(table, "连接方式", _cboTcpMode);
            AddRow(table, "下位机 IP / 主机名", _txtTcpRemote);
            AddRow(table, "下位机端口", _numTcpRemote);
            AddRow(table, "监听地址", _txtTcpBind);
            AddRow(table, "监听端口", _numTcpPort);
            AddRow(table, "", _chkTcpReconnect);
            AddRow(table, "连接/发送超时(ms)", _numTcpTimeout);
            AddRow(table, "重连间隔(ms)", _numTcpReconnect);
            AddRow(table, "", _chkTcpReceive);
            AddRow(table, "触发字符串", _txtTcpTrigger);
            AddRow(table, "应答字符串", _txtTcpAck);
            AddRow(table, "", _chkTcpSendResults);
            AddRow(table, "结果模板", _txtTcpResults);
            AddNote(table, "连接与触发检测相互独立。客户端主动连接下位机；服务端监听等待对端连接。文本命令以换行结束；结果模板支持 {RESULT}/{SEQ}/{DURATION} 和 \\r\\n。");
            Action modeChanged = () => { bool client = _cboTcpMode.SelectedIndex == 0; _txtTcpRemote.Enabled = _numTcpRemote.Enabled = client; _txtTcpBind.Enabled = _numTcpPort.Enabled = !client; };
            _cboTcpMode.SelectedIndexChanged += (s,e) => modeChanged(); modeChanged();
            page.Controls.Add(table);
            return page;
        }

        private TabPage BuildSecurityPage()
        {
            var page = CreatePage("安全");
            var table = CreateTable();
            var lblUser = Theme.MakeLabel("当前用户：" + (_auth.CurrentUser == null ? "未登录" : _auth.CurrentUser.DisplayText), true);
            _txtNewPassword = MakePassword();
            _txtConfirmPassword = MakePassword();
            var btnChange = Theme.MakeButton("修改管理员密码", null, Theme.ButtonKind.Primary, 150);
            btnChange.Click += (s, e) => ChangePassword();

            AddRow(table, "", lblUser);
            AddRow(table, "新密码", _txtNewPassword);
            AddRow(table, "确认新密码", _txtConfirmPassword);
            AddRow(table, "", btnChange);
            AddNote(table, "管理员密码保存在 App.config 的 appSettings/AdminPassword，默认 admin；修改无需旧密码，也不限制长度和字符组合。目标机需有配置文件写权限。");
            page.Controls.Add(table);
            return page;
        }

        private void ChangePassword()
        {
            if (_auth == null || !_auth.IsAuthenticated)
            {
                MessageBox.Show(this, "请先登录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_txtNewPassword.Text != _txtConfirmPassword.Text)
            {
                MessageBox.Show(this, "两次输入的新密码不一致。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string message;
            if (_auth.ChangePassword(string.Empty, _txtNewPassword.Text, out message))
            {
                MessageBox.Show(this, message, "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _txtNewPassword.Clear();
                _txtConfirmPassword.Clear();
            }
            else
            {
                MessageBox.Show(this, message, "修改失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private bool ReadSettings()
        {
            System.Net.IPAddress bind;
            if (_chkTcpTriggerEnabled.Checked && (_cboTcpMode.SelectedIndex == 0 ? string.IsNullOrWhiteSpace(_txtTcpRemote.Text) : !System.Net.IPAddress.TryParse(_txtTcpBind.Text.Trim(), out bind)))
            { MessageBox.Show(this, "请填写下位机地址，或有效的本机监听 IP。", "配置检查"); return false; }
            if ((long)_numModbusStart.Value + (long)_numModbusCount.Value > 65536)
            { MessageBox.Show(this, "Modbus 寄存器范围不能超过 65535。", "配置检查"); return false; }
            if (string.IsNullOrWhiteSpace(_txtLine.Text) || string.IsNullOrWhiteSpace(_txtStation.Text))
            {
                MessageBox.Show(this, "产线和工位名称不能为空。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (_chkModbusEnabled.Checked && string.IsNullOrWhiteSpace(_txtModbusHost.Text))
            {
                MessageBox.Show(this, "启用 Modbus 时必须填写 PLC IP。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (_chkSerialEnabled.Checked && string.IsNullOrWhiteSpace(_txtSerialPort.Text))
            {
                MessageBox.Show(this, "启用串口 IO 时必须填写串口名称。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (_numModbusResultOffset.Value >= _numModbusCount.Value)
            {
                MessageBox.Show(this, "结果寄存器偏移必须小于寄存器数量。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            _settings.LineName = _txtLine.Text.Trim();
            _settings.StationName = _txtStation.Text.Trim();
            _settings.General.AutoStart = _chkAutoStart.Checked;
            _settings.General.RememberLastSolution = _chkRememberSolution.Checked;
            _settings.General.ConfirmBeforeExit = _chkConfirmExit.Checked;
            _settings.General.PreviewThrottleMs = (int)_numPreviewThrottle.Value;

            _settings.Detection.IntervalMs = (int)_numInterval.Value;
            _settings.Detection.MaxConsecutiveErrors = (int)_numMaxErrors.Value;
            _settings.Detection.ErrorRecoveryDelayMs = (int)_numRecovery.Value;
            _settings.Detection.AutoClearStatsOnSolutionChange = _chkAutoClearStats.Checked;
            _settings.Vision.AutoSaveParameterChanges = _chkAutoSaveParams.Checked;

            _settings.Camera.TriggerMode = _cboTriggerMode.SelectedIndex == 1 ? CameraTriggerMode.Hardware :
                _cboTriggerMode.SelectedIndex == 2 ? CameraTriggerMode.Continuous : CameraTriggerMode.Software;
            _settings.Camera.PixelFormat = _cboPixelFormat.SelectedIndex == 1 ? CameraPixelFormat.Rgb24 : CameraPixelFormat.Mono8;
            _settings.Camera.ExposureTimeUs = (float)_numExposure.Value;
            _settings.Camera.GainDb = (float)_numGain.Value;
            _settings.Camera.HardwareTriggerSource = (int)_numHardwareTriggerSource.Value;
            _settings.Camera.EnableAutoReconnect = _chkCameraReconnect.Checked;
            _settings.Camera.ReconnectIntervalMs = (int)_numCameraReconnect.Value;
            _settings.Camera.FrameTimeoutMs = (int)_numFrameTimeout.Value;
            _settings.Camera.PreviewFrameSkip = (int)_numPreviewSkip.Value;

            _settings.Storage.DataRoot = _txtDataRoot.Text.Trim();
            _settings.Storage.EnableDetectionLog = _chkDetectionLog.Checked;
            _settings.Storage.EnableImageArchive = _chkImageArchive.Checked;
            _settings.Storage.ArchiveNgOnly = _chkArchiveNgOnly.Checked;
            _settings.Storage.ArchiveFormat = _cboArchiveFormat.SelectedIndex == 1 ? "bmp" :
                _cboArchiveFormat.SelectedIndex == 2 ? "jpg" : "png";
            _settings.Storage.RetentionDays = (int)_numRetention.Value;
            _settings.Storage.ArchiveQueueCapacity = (int)_numArchiveQueue.Value;
            _settings.Storage.ArchiveMemoryLimitMb = (int)_numArchiveMemoryMb.Value;
            _settings.Report.ExportDirectory = _txtReportDirectory.Text.Trim();
            _settings.Report.CompanyName = _txtCompanyName.Text.Trim();

            ModbusTcpSettings modbus = _settings.Communication.ModbusTcp;
            modbus.Enabled = _chkModbusEnabled.Checked;
            modbus.Host = _txtModbusHost.Text.Trim();
            modbus.Port = (int)_numModbusPort.Value;
            modbus.UnitId = (byte)_numModbusUnit.Value;
            modbus.StartRegister = (ushort)_numModbusStart.Value;
            modbus.RegisterCount = (ushort)_numModbusCount.Value;
            modbus.ResultRegisterOffset = (ushort)_numModbusResultOffset.Value;
            modbus.OkValue = (ushort)_numModbusOk.Value;
            modbus.NgValue = (ushort)_numModbusNg.Value;
            modbus.TimeoutMs = (int)_numModbusTimeout.Value; modbus.AutoReconnect = _chkModbusReconnect.Checked; modbus.ReconnectIntervalMs = (int)_numModbusReconnect.Value;

            SerialPortSettings serial = _settings.Communication.Serial;
            serial.Enabled = _chkSerialEnabled.Checked;
            serial.PortName = _txtSerialPort.Text.Trim();
            serial.BaudRate = (int)_numBaudRate.Value;
            serial.Parity = Convert.ToString(_cboSerialParity.SelectedItem);
            serial.DataBits = (int)_numDataBits.Value;
            serial.StopBits = Convert.ToString(_cboStopBits.SelectedItem);
            serial.CommandEncoding = Convert.ToString(_cboSerialEncoding.SelectedItem);
            serial.OkCommand = _txtOkCommand.Text.Trim();
            serial.NgCommand = _txtNgCommand.Text.Trim();
            serial.EnableReceiveTrigger = _chkSerialTrigger.Checked;
            serial.TriggerCommand = _txtSerialTrigger.Text.Trim();
            serial.AutoReconnect = _chkSerialReconnect.Checked; serial.ReconnectIntervalMs = (int)_numSerialReconnect.Value;

            TcpTriggerSettings tcp = _settings.Communication.TcpTrigger;
            tcp.Enabled = _chkTcpTriggerEnabled.Checked;
            tcp.BindAddress = _txtTcpBind.Text.Trim();
            tcp.ListenPort = (int)_numTcpPort.Value;
            tcp.TriggerCommand = _txtTcpTrigger.Text.Trim();
            tcp.AckCommand = _txtTcpAck.Text.Trim();
            tcp.Mode = _cboTcpMode.SelectedIndex == 0 ? "Client" : "Server";
            tcp.RemoteHost = _txtTcpRemote.Text.Trim(); tcp.RemotePort = (int)_numTcpRemote.Value;
            tcp.EnableReceiveTrigger = _chkTcpReceive.Checked; tcp.SendResults = _chkTcpSendResults.Checked;
            tcp.ResultTemplate = _txtTcpResults.Text; tcp.AutoReconnect = _chkTcpReconnect.Checked; tcp.TimeoutMs = (int)_numTcpTimeout.Value;
            tcp.ReconnectIntervalMs = (int)_numTcpReconnect.Value;

            _settings.Normalize();
            Settings = _settings;
            return true;
        }

        private static TabPage CreatePage(string text)
        {
            return new TabPage(text) { BackColor = Theme.Surface, AutoScroll = true };
        }

        private static TableLayoutPanel CreateTable()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Padding = new Padding(18, 16, 18, 16),
                BackColor = Theme.Surface
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return table;
        }

        private static void AddRow(TableLayoutPanel table, string caption, Control control)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (!string.IsNullOrEmpty(caption))
            {
                var label = Theme.MakeLabel(caption, true);
                label.Margin = new Padding(0, 7, 8, 8);
                table.Controls.Add(label, 0, row);
            }
            else
            {
                table.Controls.Add(new Label { Text = string.Empty, AutoSize = true }, 0, row);
            }

            control.Margin = new Padding(0, 3, 0, 6);
            table.Controls.Add(control, 1, row);
        }

        private static void AddNote(TableLayoutPanel table, string text)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(580, 0),
                ForeColor = Theme.TextMuted,
                Font = Theme.SmallFont,
                Margin = new Padding(0, 10, 0, 0)
            };
            table.Controls.Add(label, 0, row);
            table.SetColumnSpan(label, 2);
        }

        private static TextBox MakeText(string value)
        {
            var textBox = new TextBox { Text = value ?? string.Empty, Width = 360 };
            Theme.StyleTextBox(textBox);
            return textBox;
        }

        private static TextBox MakePassword()
        {
            var textBox = new TextBox { Width = 360, UseSystemPasswordChar = true };
            Theme.StyleTextBox(textBox);
            return textBox;
        }

        private static CheckBox MakeCheck(string text, bool value)
        {
            return new CheckBox
            {
                Text = text,
                Checked = value,
                AutoSize = true,
                ForeColor = Theme.Text,
                Font = Theme.BaseFont,
                Margin = new Padding(0, 4, 0, 8)
            };
        }

        private static NumericUpDown MakeNumber(decimal min, decimal max, decimal value,
            int decimals = 0, decimal increment = 1)
        {
            var numeric = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                DecimalPlaces = decimals,
                Increment = increment,
                Width = 150
            };
            numeric.Value = Math.Max(min, Math.Min(max, value));
            Theme.StyleNumeric(numeric);
            return numeric;
        }

        private static ComboBox MakeCombo(params string[] items)
        {
            var combo = new VisionInspect.App.UI.Controls.StudioComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            combo.Items.AddRange(items);
            Theme.StyleCombo(combo);
            return combo;
        }
    }
}
