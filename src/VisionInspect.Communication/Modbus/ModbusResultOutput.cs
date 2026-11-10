using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;

namespace VisionInspect.Communication.Modbus
{
    /// <summary>
    /// 把检测结果写入 PLC 保持寄存器。
    /// 默认寄存器布局：偏移 0 = 结果（OK/NG 配置值），偏移 1 = 序号低字，偏移 2 = 耗时 ms；
    /// 其他输出可按配置映射到后续偏移。
    /// </summary>
    public sealed class ModbusResultOutput : IDisposable
    {
        private readonly ModbusTcpClient _client = new ModbusTcpClient();
        private readonly IAppLogger _logger;
        private readonly ModbusTcpSettings _settings;
        private readonly object _statusSync = new object();
        private CancellationTokenSource _loopCts;
        private Task _loopTask;
        private volatile bool _started;
        private DateTime _lastErrorLogUtc = DateTime.MinValue;

        public ModbusResultOutput(ModbusTcpSettings settings, IAppLogger logger)
        {
            _settings = settings ?? new ModbusTcpSettings();
            _logger = logger;
        }

        public event EventHandler<CommunicationStatusEventArgs> StatusChanged;
        public bool IsConnected { get { return _client.IsConnected; } }
        public async Task<ushort[]> ReadRegistersAsync(byte unit, ushort address, ushort count, CancellationToken token)
        {
            try { return await _client.ReadHoldingRegistersAsync(unit, address, count, _settings.TimeoutMs, token).ConfigureAwait(false); }
            catch (Exception ex) { SetStatus(DeviceConnectionState.Fault, "寄存器读取失败：" + ex.Message); throw; }
        }
        public async Task WriteRegistersAsync(byte unit, ushort address, ushort[] values, CancellationToken token)
        {
            try { await _client.WriteMultipleRegistersAsync(unit, address, values, _settings.TimeoutMs, token).ConfigureAwait(false); }
            catch (Exception ex) { SetStatus(DeviceConnectionState.Fault, "寄存器写入失败：" + ex.Message); throw; }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_settings.Enabled)
            {
                SetStatus(DeviceConnectionState.Disabled, "Modbus TCP 未启用。");
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
            _client.Close();
            if (_loopTask != null)
            {
                try { await _loopTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
            if (_loopCts != null) { try { _loopCts.Dispose(); } catch { } }
            _loopCts = null;
            _loopTask = null;
            SetStatus(DeviceConnectionState.Disconnected, "Modbus TCP 已停止。");
        }

        public async Task PublishAsync(DetectionContext context, CancellationToken cancellationToken)
        {
            if (!_settings.Enabled || context == null) return;
            if (!_client.IsConnected)
            {
                SetStatus(DeviceConnectionState.Disconnected, "Modbus TCP 未连接，结果未发送。");
                return;
            }

            ushort[] registers = BuildRegisters(context);
            try
            {
                await _client.WriteMultipleRegistersAsync(_settings.UnitId, _settings.StartRegister,
                    registers, _settings.TimeoutMs, cancellationToken).ConfigureAwait(false);
                SetStatus(DeviceConnectionState.Connected, "检测结果已写入 PLC。");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (_logger != null)
                    _logger.Error("Modbus", "写入 PLC 寄存器失败。", ex);
                SetStatus(DeviceConnectionState.Fault, "Modbus 写入失败：" + ex.Message);
            }
        }

        public void Dispose()
        {
            try { StopAsync().GetAwaiter().GetResult(); } catch { }
            _client.Dispose();
        }

        private async Task ConnectionLoopAsync(CancellationToken cancellationToken)
        {
            bool attempted = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!_client.IsConnected)
                    {
                        if (attempted && !_settings.AutoReconnect) break;
                        attempted = true;
                        SetStatus(DeviceConnectionState.Connecting,
                            string.Format("正在连接 Modbus TCP {0}:{1}...", _settings.Host, _settings.Port));
                        await _client.ConnectAsync(_settings.Host, _settings.Port,
                            _settings.TimeoutMs, cancellationToken).ConfigureAwait(false);
                        SetStatus(DeviceConnectionState.Connected,
                            string.Format("Modbus TCP 已连接 {0}:{1}。", _settings.Host, _settings.Port));
                        if (_logger != null)
                            _logger.Info("Modbus", string.Format("连接成功：{0}:{1}，站号 {2}。",
                                _settings.Host, _settings.Port, _settings.UnitId));
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
                    _client.Close();
                    SetStatus(DeviceConnectionState.Fault, "Modbus 连接失败：" + ex.Message);
                    if (_logger != null &&
                        (DateTime.UtcNow - _lastErrorLogUtc).TotalSeconds > 30)
                    {
                        _logger.Warn("Modbus", "连接 PLC 失败：" + ex.Message);
                        _lastErrorLogUtc = DateTime.UtcNow;
                    }

                    if (!_settings.AutoReconnect)
                    {
                        // 不自动重连时保留 Fault，Stop/Start 后可重新尝试。
                        break;
                    }
                    else
                    {
                        await Task.Delay(Math.Max(1000, _settings.ReconnectIntervalMs), cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        private ushort[] BuildRegisters(DetectionContext context)
        {
            int count = Math.Max(2, (int)_settings.RegisterCount);
            var registers = new ushort[count];

            int resultIndex = Math.Min(count - 1, (int)_settings.ResultRegisterOffset);
            registers[resultIndex] = context.IsOk ? _settings.OkValue : _settings.NgValue;

            if (resultIndex + 1 < count)
                registers[resultIndex + 1] = unchecked((ushort)(context.Sequence & 0xFFFF));
            if (resultIndex + 2 < count)
            {
                int duration = (int)Math.Min(ushort.MaxValue, Math.Max(0,
                    context.Inspection == null ? 0 : context.Inspection.DurationMs));
                registers[resultIndex + 2] = (ushort)duration;
            }

            if (context.Inspection != null && context.Inspection.Outputs != null)
            {
                foreach (ModbusRegisterMapping mapping in _settings.Mappings.Where(x => x.Enabled))
                {
                    if (mapping.RegisterOffset >= count) continue;
                    VisionOutputValue output = context.Inspection.Outputs.FirstOrDefault(x =>
                        !string.IsNullOrWhiteSpace(x.Name) &&
                        string.Equals(x.Name, mapping.OutputName, StringComparison.OrdinalIgnoreCase));
                    if (output == null)
                    {
                        output = context.Inspection.Outputs.FirstOrDefault(x =>
                            !string.IsNullOrWhiteSpace(x.Name) &&
                            x.Name.IndexOf(mapping.OutputName ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                    if (output == null) continue;

                    double numeric;
                    if (!double.TryParse(output.JoinedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out numeric))
                        continue;

                    numeric *= mapping.Scale;
                    if (double.IsNaN(numeric) || double.IsInfinity(numeric)) continue;
                    numeric = Math.Max(ushort.MinValue, Math.Min(ushort.MaxValue, numeric));
                    registers[mapping.RegisterOffset] = (ushort)Math.Round(numeric, MidpointRounding.AwayFromZero);
                }
            }

            return registers;
        }

        private void SetStatus(DeviceConnectionState state, string message)
        {
            EventHandler<CommunicationStatusEventArgs> handler = StatusChanged;
            if (handler == null) return;
            try
            {
                handler(this, new CommunicationStatusEventArgs(CommunicationChannel.ModbusTcp, state, message));
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("Modbus", "通讯状态事件异常。", ex);
            }
        }
    }
}
