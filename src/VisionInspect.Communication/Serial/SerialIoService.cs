using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;

namespace VisionInspect.Communication.Serial
{
    /// <summary>
    /// 串口 IO 板卡通讯：
    /// - 检测完成后发送可配置的 OK/NG 电平指令（HEX 固定帧或 ASCII 模板）；
    /// - 可从串口接收触发字符，用于启动单次检测。
    ///
    /// 串口断开后由后台循环自动重开，不阻塞检测主流程。
    /// </summary>
    public sealed class SerialIoService : ITriggerSource, IDisposable
    {
        private readonly object _sync = new object();
        private readonly object _writeSync = new object();
        private readonly IAppLogger _logger;
        private readonly SerialPortSettings _settings;
        private readonly StringBuilder _receiveBuffer = new StringBuilder();
        private SerialPort _port;
        private CancellationTokenSource _loopCts;
        private Task _loopTask;
        private volatile bool _started;
        private DateTime _lastTriggerUtc = DateTime.MinValue;

        public SerialIoService(SerialPortSettings settings, IAppLogger logger)
        {
            _settings = settings ?? new SerialPortSettings();
            _logger = logger;
        }

        public event EventHandler<TriggerEventArgs> Triggered;
        public event EventHandler<CommunicationStatusEventArgs> StatusChanged;
        public event EventHandler<CommunicationDataEventArgs> DataTransferred;

        public bool IsOpen
        {
            get
            {
                try
                {
                    SerialPort port;
                    lock (_sync) port = _port;
                    return port != null && port.IsOpen;
                }
                catch
                {
                    return false;
                }
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_settings.Enabled)
            {
                SetStatus(DeviceConnectionState.Disabled, "串口 IO 未启用。");
                return Task.CompletedTask;
            }
            if (_started) return Task.CompletedTask;

            _started = true;
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken token = _loopCts.Token;
            _loopTask = Task.Run(() => ConnectionLoopAsync(token));
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            _started = false;
            if (_loopCts != null)
            {
                try { _loopCts.Cancel(); } catch { }
            }
            ClosePort();
            if (_loopTask != null)
            {
                try { await _loopTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
            // Open() 与 Stop() 交错时，循环返回后再确保关闭刚打开的句柄。
            ClosePort();
            if (_loopCts != null) { try { _loopCts.Dispose(); } catch { } }
            _loopCts = null;
            _loopTask = null;
            SetStatus(DeviceConnectionState.Disconnected, "串口 IO 已停止。");
        }

        public async Task PublishResultAsync(DetectionContext context, CancellationToken cancellationToken)
        {
            if (!_settings.Enabled || context == null) return;

            SerialPort port;
            lock (_sync) port = _port;
            if (port == null || !port.IsOpen) return;

            byte[] frame;
            try
            {
                string template = context.IsOk ? _settings.OkCommand : _settings.NgCommand;
                frame = CommandCodec.Build(template, _settings.CommandEncoding == "Hex", context);
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("Serial", "构建串口 IO 指令失败。", ex);
                return;
            }

            if (frame == null || frame.Length == 0) return;

            await Task.Run(() =>
            {
                lock (_writeSync)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        port.Write(frame, 0, frame.Length);
                        RaiseData(false, frame);
                        if (_logger != null)
                            _logger.Debug("Serial", string.Format("发送 IO 指令 {0} 字节，结果 {1}。",
                                frame.Length, context.IsOk ? "OK" : "NG"));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (_logger != null) _logger.Error("Serial", "串口写 IO 指令失败，将自动重连。", ex);
                        ClosePort();
                        SetStatus(DeviceConnectionState.Fault, "串口写入失败：" + ex.Message);
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            try { StopAsync().GetAwaiter().GetResult(); } catch { }
        }

        public Task SendAsync(byte[] frame, CancellationToken token)
        {
            return Task.Run(() => {
                lock (_writeSync) {
                    token.ThrowIfCancellationRequested();
                    SerialPort port; lock (_sync) port = _port;
                    if (port == null || !port.IsOpen) throw new InvalidOperationException("串口尚未连接。");
                    try { port.Write(frame, 0, frame.Length); RaiseData(false, frame); }
                    catch { ClosePort(); SetStatus(DeviceConnectionState.Fault, "串口发送失败。"); throw; }
                }
            }, token);
        }
        private void RaiseData(bool receive, byte[] data)
        { try { DataTransferred?.Invoke(this, new CommunicationDataEventArgs(CommunicationChannel.Serial, receive, data)); } catch (Exception ex) { _logger?.Error("Serial", "数据事件异常", ex); } }

        private async Task ConnectionLoopAsync(CancellationToken cancellationToken)
        {
            bool attempted = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!IsOpen)
                    {
                        if (attempted && !_settings.AutoReconnect) break;
                        attempted = true;
                        SetStatus(DeviceConnectionState.Connecting, "正在打开串口 " + _settings.PortName + "...");
                        OpenPort();
                        SetStatus(DeviceConnectionState.Connected, "串口 IO 已连接：" + _settings.PortName);
                    }

                    await Task.Delay(Math.Max(1000, _settings.ReconnectIntervalMs), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ClosePort();
                    SetStatus(DeviceConnectionState.Fault, "串口连接失败：" + ex.Message);
                    if (_logger != null) _logger.Warn("Serial", "串口打开失败：" + ex.Message);
                    if (!_settings.AutoReconnect) break;
                    await Task.Delay(Math.Max(1000, _settings.ReconnectIntervalMs), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        private void OpenPort()
        {
            var port = new SerialPort
            {
                PortName = _settings.PortName,
                BaudRate = _settings.BaudRate,
                DataBits = _settings.DataBits,
                ReadTimeout = 500,
                WriteTimeout = 500,
                Encoding = Encoding.ASCII,
                Parity = ParseParity(_settings.Parity),
                StopBits = ParseStopBits(_settings.StopBits)
            };
            try
            {
                port.DataReceived += OnDataReceived;
                port.Open();
            }
            catch
            {
                port.DataReceived -= OnDataReceived;
                port.Dispose();
                throw;
            }

            lock (_sync)
            {
                _port = port;
                _receiveBuffer.Length = 0;
            }

            if (_logger != null)
                _logger.Info("Serial", string.Format("串口已打开：{0}，{1},{2},{3},{4}。",
                    _settings.PortName, _settings.BaudRate, _settings.Parity, _settings.DataBits, _settings.StopBits));
        }

        private void ClosePort()
        {
            SerialPort port;
            lock (_sync)
            {
                port = _port;
                _port = null;
                _receiveBuffer.Length = 0;
            }

            if (port == null) return;
            try
            {
                port.DataReceived -= OnDataReceived;
                if (port.IsOpen) port.Close();
                port.Dispose();
            }
            catch { }
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                SerialPort port = sender as SerialPort;
                if (port == null || !port.IsOpen) return;

                byte[] bytes = new byte[Math.Min(65536, port.BytesToRead)];
                int count = port.Read(bytes, 0, bytes.Length);
                if (count == 0) return;
                if (count != bytes.Length) Array.Resize(ref bytes, count);
                RaiseData(true, bytes);
                string data = Encoding.ASCII.GetString(bytes);
                if (string.IsNullOrEmpty(data)) return;

                string completeText = null;
                lock (_sync)
                {
                    _receiveBuffer.Append(data);
                    if (_receiveBuffer.Length > 1024)
                        _receiveBuffer.Remove(0, _receiveBuffer.Length - 1024);

                    completeText = _receiveBuffer.ToString();
                }

                if (_settings.EnableReceiveTrigger &&
                    !string.IsNullOrWhiteSpace(_settings.TriggerCommand) &&
                    completeText.IndexOf(_settings.TriggerCommand, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lock (_sync) _receiveBuffer.Length = 0;
                    DateTime now = DateTime.UtcNow;
                    if ((now - _lastTriggerUtc).TotalMilliseconds > 500)
                    {
                        _lastTriggerUtc = now;
                        RaiseTrigger(_settings.TriggerCommand);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("Serial", "串口接收处理失败。", ex);
            }
        }

        private void RaiseTrigger(string payload)
        {
            EventHandler<TriggerEventArgs> handler = Triggered;
            if (handler == null) return;
            try { handler(this, new TriggerEventArgs(TriggerSource.Serial, payload)); }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("Serial", "串口触发事件处理异常。", ex);
            }
        }

        private void SetStatus(DeviceConnectionState state, string message)
        {
            EventHandler<CommunicationStatusEventArgs> handler = StatusChanged;
            if (handler == null) return;
            try
            {
                handler(this, new CommunicationStatusEventArgs(CommunicationChannel.Serial, state, message));
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("Serial", "串口状态事件异常。", ex);
            }
        }

        private static Parity ParseParity(string value)
        {
            Parity parity;
            return Enum.TryParse(value, true, out parity) ? parity : Parity.None;
        }

        private static StopBits ParseStopBits(string value)
        {
            StopBits bits;
            return Enum.TryParse(value, true, out bits) ? bits : StopBits.One;
        }
    }
}
