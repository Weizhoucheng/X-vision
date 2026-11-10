using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using VisionInspect.Communication.Modbus;
using VisionInspect.Communication.Serial;
using VisionInspect.Communication.Triggers;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;

namespace VisionInspect.Communication
{
    /// <summary>稳定的通信入口；配置切换、手动操作与结果发送共用生命周期锁。</summary>
    public sealed class CommunicationHub : ICommunicationHub, ITriggerSource, IDisposable
    {
        private ModbusResultOutput _modbus;
        private SerialIoService _serial;
        private TcpTriggerSource _tcp;
        private CommunicationSettings _settings;
        private readonly IAppLogger _logger;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly object _statusSync = new object();
        private readonly Dictionary<CommunicationChannel, CommunicationStatusEventArgs> _states = new Dictionary<CommunicationChannel, CommunicationStatusEventArgs>();
        private bool _started;
        private bool _disposed;
        public event EventHandler<CommunicationStatusEventArgs> StatusChanged;
        public event EventHandler<CommunicationDataEventArgs> DataTransferred;
        public event EventHandler<TriggerEventArgs> Triggered;

        public CommunicationHub(AppSettings settings, IAppLogger logger)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _logger = logger;
            _settings = Clone(settings.Communication);
            CreateModbus(); CreateSerial(); CreateTcp();
        }
        private static T Clone<T>(T value) { var json = new JavaScriptSerializer(); return json.Deserialize<T>(json.Serialize(value)); }
        private static bool Same<T>(T a, T b) { var json = new JavaScriptSerializer(); return json.Serialize(a) == json.Serialize(b); }
        private void CreateModbus() { _modbus = new ModbusResultOutput(_settings.ModbusTcp, _logger); _modbus.StatusChanged += OnStatusChanged; }
        private void CreateSerial() { _serial = new SerialIoService(_settings.Serial, _logger); _serial.StatusChanged += OnStatusChanged; _serial.Triggered += OnTriggered; _serial.DataTransferred += OnData; }
        private void CreateTcp() { _tcp = new TcpTriggerSource(_settings.TcpTrigger, _logger); _tcp.StatusChanged += OnStatusChanged; _tcp.Triggered += OnTriggered; _tcp.DataTransferred += OnData; }
        public CommunicationStatusEventArgs GetStatus(CommunicationChannel channel)
        {
            lock (_statusSync) { CommunicationStatusEventArgs value; return _states.TryGetValue(channel, out value) ? value : new CommunicationStatusEventArgs(channel, DeviceConnectionState.Disabled, "未启用"); }
        }
        public async Task StartAsync(CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try { ThrowIfDisposed(); if (_started) return; _started = true; foreach (CommunicationChannel channel in Channels) await StartCore(channel, token).ConfigureAwait(false); }
            finally { _gate.Release(); }
        }
        private static readonly CommunicationChannel[] Channels = { CommunicationChannel.ModbusTcp, CommunicationChannel.Serial, CommunicationChannel.TcpTrigger };
        public async Task ReconfigureAsync(CommunicationSettings settings, CancellationToken token)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var next = Clone(settings); next.Normalize();
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try {
                ThrowIfDisposed();
                if (!Same(_settings.ModbusTcp, next.ModbusTcp)) { await _modbus.StopAsync().ConfigureAwait(false); _modbus.Dispose(); _settings.ModbusTcp = next.ModbusTcp; CreateModbus(); await StartCore(CommunicationChannel.ModbusTcp, token).ConfigureAwait(false); }
                if (!Same(_settings.Serial, next.Serial)) { await _serial.StopAsync().ConfigureAwait(false); _serial.Dispose(); _settings.Serial = next.Serial; CreateSerial(); await StartCore(CommunicationChannel.Serial, token).ConfigureAwait(false); }
                if (!Same(_settings.TcpTrigger, next.TcpTrigger)) { await _tcp.StopAsync().ConfigureAwait(false); _tcp.Dispose(); _settings.TcpTrigger = next.TcpTrigger; CreateTcp(); await StartCore(CommunicationChannel.TcpTrigger, token).ConfigureAwait(false); }
                if (!_started) foreach (var channel in Channels) await StartCore(channel, token).ConfigureAwait(false);
                _started = true;
            } finally { _gate.Release(); }
        }
        public async Task ConnectAsync(CommunicationChannel channel, CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try { ThrowIfDisposed(); await StopCore(channel).ConfigureAwait(false); await StartCore(channel, token).ConfigureAwait(false); }
            finally { _gate.Release(); }
        }
        public async Task DisconnectAsync(CommunicationChannel channel)
        { await _gate.WaitAsync().ConfigureAwait(false); try { await StopCore(channel).ConfigureAwait(false); } finally { _gate.Release(); } }
        public async Task StopAsync()
        { await _gate.WaitAsync().ConfigureAwait(false); try { foreach (var channel in Channels) await StopCore(channel).ConfigureAwait(false); _started = false; } finally { _gate.Release(); } }
        private async Task StartCore(CommunicationChannel channel, CancellationToken token)
        {
            try {
                if (channel == CommunicationChannel.ModbusTcp) await _modbus.StartAsync(token).ConfigureAwait(false);
                else if (channel == CommunicationChannel.Serial) await _serial.StartAsync(token).ConfigureAwait(false);
                else await _tcp.StartAsync(token).ConfigureAwait(false);
            } catch (OperationCanceledException) { throw; }
            catch (Exception ex) { OnStatusChanged(this, new CommunicationStatusEventArgs(channel, DeviceConnectionState.Fault, ex.Message)); }
        }
        private Task StopCore(CommunicationChannel channel)
        { return channel == CommunicationChannel.ModbusTcp ? _modbus.StopAsync() : channel == CommunicationChannel.Serial ? _serial.StopAsync() : _tcp.StopAsync(); }
        public async Task SendAsync(CommunicationChannel channel, byte[] data, CancellationToken token)
        {
            if (data == null || data.Length == 0 || data.Length > 65536) throw new ArgumentException("发送长度须为 1~65536 字节。");
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try { ThrowIfDisposed(); if (channel == CommunicationChannel.Serial) await _serial.SendAsync(data, token).ConfigureAwait(false); else if (channel == CommunicationChannel.TcpTrigger) await _tcp.SendAsync(data, token).ConfigureAwait(false); else throw new InvalidOperationException("Modbus 请使用寄存器读写。"); }
            finally { _gate.Release(); }
        }
        public async Task<ushort[]> ReadRegistersAsync(byte unit, ushort address, ushort count, CancellationToken token)
        { await _gate.WaitAsync(token).ConfigureAwait(false); try { ThrowIfDisposed(); return await _modbus.ReadRegistersAsync(unit, address, count, token).ConfigureAwait(false); } finally { _gate.Release(); } }
        public async Task WriteRegistersAsync(byte unit, ushort address, ushort[] values, CancellationToken token)
        { await _gate.WaitAsync(token).ConfigureAwait(false); try { ThrowIfDisposed(); await _modbus.WriteRegistersAsync(unit, address, values, token).ConfigureAwait(false); } finally { _gate.Release(); } }
        public async Task PublishResultAsync(DetectionContext context, CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try {
                ThrowIfDisposed(); using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    timeout.CancelAfter(1500);
                    await Task.WhenAll(_modbus.PublishAsync(context, timeout.Token), _serial.PublishResultAsync(context, timeout.Token), _tcp.PublishResultAsync(context, timeout.Token)).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { Log("结果通信失败", ex); }
            finally { _gate.Release(); }
        }
        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(CommunicationHub)); }
        public void Dispose() { if (_disposed) return; StopAsync().GetAwaiter().GetResult(); _disposed = true; _modbus.Dispose(); _serial.Dispose(); _tcp.Dispose(); }
        private void OnStatusChanged(object sender, CommunicationStatusEventArgs e)
        {
            lock (_statusSync) _states[e.Channel] = e;
            try { StatusChanged?.Invoke(this, e); } catch (Exception ex) { Log("通信状态事件异常", ex); }
            if (e.State == DeviceConnectionState.Fault) _logger?.Warn("Communication", e.Channel + ": " + e.Message);
        }
        private void OnData(object sender, CommunicationDataEventArgs e) { try { DataTransferred?.Invoke(this, e); } catch (Exception ex) { Log("通信数据事件异常", ex); } }
        private void OnTriggered(object sender, TriggerEventArgs e) { try { Triggered?.Invoke(this, e); } catch (Exception ex) { Log("触发事件异常", ex); } }
        private void Log(string message, Exception error) { _logger?.Error("Communication", message, error); }
    }
}
