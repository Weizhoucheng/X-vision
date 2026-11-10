using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionInspect.App.Bootstrap;
using VisionInspect.Communication.Serial;
using VisionInspect.Core;
using VisionInspect.Core.Models;

namespace VisionInspect.App.UI
{
    public sealed partial class SettingsForm
    {
        private readonly AppBootstrapper _runtime;
        private Button _btnSaveSettings;
        private TabControl _communicationTabs;
        private ComboBox _cboTcpMode;
        private TextBox _txtTcpRemote, _txtTcpResults;
        private NumericUpDown _numTcpRemote, _numTcpTimeout;
        private CheckBox _chkTcpReceive, _chkTcpSendResults, _chkTcpReconnect;
        private CheckBox _chkModbusReconnect, _chkSerialReconnect;
        private NumericUpDown _numModbusTimeout, _numModbusReconnect, _numSerialReconnect, _numTcpReconnect;
        private Panel _onlineBar;
        private Label _onlineStatus;
        private TextBox _onlinePayload, _onlineLog;
        private ComboBox _onlineEncoding;
        private CheckBox _appendNewLine;
        private System.Windows.Forms.Timer _onlineTimer;
        private readonly ConcurrentQueue<string> _traffic = new ConcurrentQueue<string>();
        private bool _saving, _onlineBusy;
        public bool Applied { get; private set; }
        private CommunicationChannel SelectedChannel { get { return _communicationTabs.SelectedIndex == 0 ? CommunicationChannel.ModbusTcp : _communicationTabs.SelectedIndex == 1 ? CommunicationChannel.Serial : CommunicationChannel.TcpTrigger; } }

        private async Task<bool> ApplyEditorAsync(bool close)
        {
            if (_saving || !ReadSettings()) return false;
            _saving = true; _btnSaveSettings.Enabled = false;
            try {
                if (_runtime != null) { await _runtime.ApplySettingsAsync(Settings); Applied = true; }
                if (close) { _saving = false; DialogResult = DialogResult.OK; Close(); }
                return true;
            } catch (Exception ex) { MessageBox.Show(this, "设置未能应用：" + ex.Message, "应用失败", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
            finally { _saving = false; if (!_btnSaveSettings.IsDisposed) _btnSaveSettings.Enabled = true; }
        }
        private void BuildOnlinePanel(TabPage page)
        {
            _onlineBar = new Panel { Dock = DockStyle.Bottom, Height = 214, Padding = new Padding(8), BackColor = Theme.SurfaceAlt };
            var commands = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, WrapContents = false };
            var connect = Theme.MakeButton("应用并连接", null, Theme.ButtonKind.Primary, 130);
            var disconnect = Theme.MakeButton("断开当前通道", null, Theme.ButtonKind.Ghost, 140);
            var clear = Theme.MakeButton("清空收发记录", null, Theme.ButtonKind.Ghost, 140);
            connect.Click += async (s,e) => await OnlineAction(async () => {
                if (SelectedChannel == CommunicationChannel.ModbusTcp) _chkModbusEnabled.Checked = true;
                else if (SelectedChannel == CommunicationChannel.Serial) _chkSerialEnabled.Checked = true;
                else _chkTcpTriggerEnabled.Checked = true;
                if (!await ApplyEditorAsync(false)) return;
                var state = _runtime.Communication.GetStatus(SelectedChannel).State;
                if (state != DeviceConnectionState.Connected && state != DeviceConnectionState.Connecting && state != DeviceConnectionState.Listening)
                    await _runtime.Communication.ConnectAsync(SelectedChannel, CancellationToken.None);
                Queue("配置已立即应用，连接状态见下方；不需要发送触发命令才能连接。");
            });
            disconnect.Click += async (s,e) => await OnlineAction(() => _runtime.Communication.DisconnectAsync(SelectedChannel));
            clear.Click += (s,e) => { while (_traffic.TryDequeue(out string ignored)) { } _onlineLog.Clear(); };
            commands.Controls.AddRange(new Control[] { connect, disconnect, clear });
            var sendRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 37, WrapContents = false };
            _onlinePayload = MakeText(string.Empty); _onlinePayload.Width = 268;
            _onlineEncoding = MakeCombo("ASCII", "HEX"); _onlineEncoding.Width = 96; _onlineEncoding.SelectedIndex = 0;
            _appendNewLine = MakeCheck("换行", true); _appendNewLine.Margin = new Padding(4,6,4,0);
            var send = Theme.MakeButton("发送", null, Theme.ButtonKind.Ghost, 70);
            var read = Theme.MakeButton("读寄存器", null, Theme.ButtonKind.Ghost, 98);
            var write = Theme.MakeButton("写寄存器", null, Theme.ButtonKind.Ghost, 98);
            send.Click += async (s,e) => await OnlineAction(async () => {
                if (SelectedChannel == CommunicationChannel.ModbusTcp) throw new InvalidOperationException("Modbus 请使用读/写寄存器按钮。");
                byte[] data = _onlineEncoding.SelectedIndex == 1 ? CommandCodec.ParseHex(_onlinePayload.Text) : Encoding.ASCII.GetBytes(CommandCodec.ExpandEscapes(_onlinePayload.Text) + (_appendNewLine.Checked ? "\r\n" : string.Empty));
                await _runtime.Communication.SendAsync(SelectedChannel, data, CancellationToken.None);
            });
            read.Click += async (s,e) => await OnlineAction(async () => {
                RequireModbus(); var values = await _runtime.Communication.ReadRegistersAsync((byte)_numModbusUnit.Value, (ushort)_numModbusStart.Value, (ushort)_numModbusCount.Value, CancellationToken.None);
                Queue("Modbus RX [" + _numModbusStart.Value + "] = " + string.Join(",", values));
            });
            write.Click += async (s,e) => await OnlineAction(async () => {
                RequireModbus(); var values = _onlinePayload.Text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(ushort.Parse).ToArray();
                await _runtime.Communication.WriteRegistersAsync((byte)_numModbusUnit.Value, (ushort)_numModbusStart.Value, values, CancellationToken.None);
                Queue("Modbus TX [" + _numModbusStart.Value + "] = " + string.Join(",", values));
            });
            sendRow.Controls.AddRange(new Control[] { _onlinePayload, _onlineEncoding, _appendNewLine, send, read, write });
            var hint = new Label { Dock = DockStyle.Top, Height = 23, Text = "Modbus 使用上方站号/起始地址/数量（0 基址）；写入值以逗号分隔。TCP/串口可直接收发。", Font = Theme.SmallFont, ForeColor = Theme.TextMuted };
            Action updateActions = () => {
                bool modbus = SelectedChannel == CommunicationChannel.ModbusTcp;
                send.Visible = _onlineEncoding.Visible = _appendNewLine.Visible = !modbus;
                read.Visible = write.Visible = modbus;
                hint.Text = modbus ? "使用上方站号/起始地址/数量（0 基址）；写入值使用十进制，多个值以逗号分隔。" : "连接后可直接发送和接收数据；ASCII 支持 \\r\\n 转义，HEX 使用空格分隔的十六进制字节。";
            };
            _communicationTabs.SelectedIndexChanged += (s,e) => updateActions(); updateActions();
            _onlineStatus = new Label { Dock = DockStyle.Bottom, Height = 28, Font = Theme.SmallFont, ForeColor = Theme.Text, AutoEllipsis = true };
            _onlineLog = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = Theme.MonoFont, BackColor = Theme.Field, ForeColor = Theme.Text };
            _onlineBar.Controls.Add(_onlineLog); _onlineBar.Controls.Add(hint); _onlineBar.Controls.Add(sendRow); _onlineBar.Controls.Add(commands); _onlineBar.Controls.Add(_onlineStatus);
            page.Controls.Add(_onlineBar);
            _runtime.Communication.StatusChanged += OnlineStatus;
            _runtime.Communication.DataTransferred += OnlineData;
            _onlineTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _onlineTimer.Tick += (s,e) => {
                var text = new StringBuilder(); string item;
                for (int i = 0; i < 64 && _traffic.TryDequeue(out item); i++) text.AppendLine(item);
                if (text.Length > 0) { if (_onlineLog.TextLength > 48000) _onlineLog.Clear(); _onlineLog.AppendText(text.ToString()); }
                _onlineStatus.Text = string.Join("  |  ", new[] { CommunicationChannel.ModbusTcp, CommunicationChannel.Serial, CommunicationChannel.TcpTrigger }.Select(c => (c == CommunicationChannel.TcpTrigger ? "TCP" : c == CommunicationChannel.Serial ? "串口" : "Modbus") + "：" + ConnectionStateText(_runtime.Communication.GetStatus(c).State)));
            };
            _onlineTimer.Start();
            FormClosing += (s,e) => { if (_onlineBusy) e.Cancel = true; };
            Disposed += (s,e) => { _onlineTimer.Stop(); _onlineTimer.Dispose(); _runtime.Communication.StatusChanged -= OnlineStatus; _runtime.Communication.DataTransferred -= OnlineData; };
        }
        private void RequireModbus() { if (SelectedChannel != CommunicationChannel.ModbusTcp) throw new InvalidOperationException("请先选择 Modbus 页。"); }
        private static string ConnectionStateText(DeviceConnectionState state)
        { switch(state) { case DeviceConnectionState.Connected: return "已连接"; case DeviceConnectionState.Connecting: return "连接中"; case DeviceConnectionState.Listening: return "监听中"; case DeviceConnectionState.Fault: return "故障"; case DeviceConnectionState.Disabled: return "未启用"; default: return "未连接"; } }
        private async Task OnlineAction(Func<Task> operation)
        {
            if (_onlineBusy || _saving) return;
            _onlineBusy = true; _onlineBar.Enabled = false; _btnSaveSettings.Enabled = false;
            try { await operation(); } catch (Exception ex) { Queue("失败：" + ex.Message); }
            finally { _onlineBusy = false; if (!_onlineBar.IsDisposed) _onlineBar.Enabled = true; if (!_btnSaveSettings.IsDisposed) _btnSaveSettings.Enabled = true; }
        }
        private void Queue(string value) { if (_traffic.Count < 256) _traffic.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + " " + value); }
        private static string ChannelName(CommunicationChannel channel) { return channel == CommunicationChannel.TcpTrigger ? "TCP" : channel == CommunicationChannel.Serial ? "串口" : "Modbus"; }
        private void OnlineStatus(object sender, CommunicationStatusEventArgs e) { Queue(ChannelName(e.Channel) + " " + e.Message); }
        private void OnlineData(object sender, CommunicationDataEventArgs e)
        { if (_traffic.Count >= 256) return; Queue(ChannelName(e.Channel) + (e.IsReceive ? " RX " : " TX ") + Encoding.UTF8.GetString(e.Data, 0, Math.Min(e.Data.Length, 256)).Replace("\r", "\\r").Replace("\n", "\\n") + "  HEX: " + BitConverter.ToString(e.Data, 0, Math.Min(e.Data.Length, 64)).Replace('-', ' ')); }
    }
}
