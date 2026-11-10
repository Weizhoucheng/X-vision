using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Communication.Serial;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;

namespace VisionInspect.Communication.Triggers
{
    /// <summary>TCP 双向通道。兼容原文本触发服务，支持客户端主动连接及服务端监听。</summary>
    public sealed class TcpTriggerSource : ITriggerSource, IDisposable
    {
        private sealed class Peer { public TcpClient Client; public readonly SemaphoreSlim Writes = new SemaphoreSlim(1, 1); }
        private readonly TcpTriggerSettings _settings;
        private readonly IAppLogger _logger;
        private readonly ConcurrentDictionary<TcpClient, Peer> _clients = new ConcurrentDictionary<TcpClient, Peer>();
        private readonly ConcurrentDictionary<long, Task> _tasks = new ConcurrentDictionary<long, Task>();
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Task _loop;
        private long _id;
        private bool _started;
        public event EventHandler<TriggerEventArgs> Triggered;
        public event EventHandler<CommunicationStatusEventArgs> StatusChanged;
        public event EventHandler<CommunicationDataEventArgs> DataTransferred;
        public TcpTriggerSource(TcpTriggerSettings settings, IAppLogger logger) { _settings = settings ?? new TcpTriggerSettings(); _logger = logger; }
        public Task StartAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!_settings.Enabled) { Status(DeviceConnectionState.Disabled, "TCP 未启用。"); return Task.CompletedTask; }
            if (_started) return Task.CompletedTask;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            try {
                _started = true;
                if (_settings.Mode == "Client") _loop = Task.Run(() => ConnectLoop(_cts.Token));
                else {
                    _listener = new TcpListener(IPAddress.Parse(_settings.BindAddress), _settings.ListenPort);
                    _listener.Start();
                    Status(DeviceConnectionState.Listening, "TCP 正在监听 " + _settings.BindAddress + ":" + _settings.ListenPort);
                    _loop = AcceptLoop(_listener, _cts.Token);
                }
            } catch { _started = false; _listener?.Stop(); _cts.Dispose(); _cts = null; throw; }
            return Task.CompletedTask;
        }
        public async Task StopAsync()
        {
            _started = false;
            _cts?.Cancel(); _listener?.Stop();
            foreach (var peer in _clients.Keys) peer.Close();
            if (_loop != null) try { await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
            foreach (var peer in _clients.Keys) peer.Close();
            await Task.WhenAll(_tasks.Values.ToArray()).ConfigureAwait(false);
            _cts?.Dispose(); _cts = null; _loop = null; _listener = null;
            Status(DeviceConnectionState.Disconnected, "TCP 已断开。");
        }
        private async Task ConnectLoop(CancellationToken token)
        {
            do {
                var client = new TcpClient { NoDelay = true };
                try {
                    Status(DeviceConnectionState.Connecting, "正在连接 TCP " + _settings.RemoteHost + ":" + _settings.RemotePort);
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                    using (timeout.Token.Register(() => client.Close())) {
                        timeout.CancelAfter(_settings.TimeoutMs);
                        var connecting = client.ConnectAsync(_settings.RemoteHost, _settings.RemotePort);
                        if (await Task.WhenAny(connecting, Task.Delay(Timeout.Infinite, timeout.Token)).ConfigureAwait(false) != connecting) {
                            _ = connecting.ContinueWith(t => { var observed = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                            token.ThrowIfCancellationRequested(); throw new TimeoutException("TCP 连接超时。");
                        }
                        await connecting.ConfigureAwait(false);
                        timeout.Token.ThrowIfCancellationRequested();
                    }
                    var peer = new Peer { Client = client }; _clients.TryAdd(client, peer);
                    Status(DeviceConnectionState.Connected, "TCP 已连接 " + _settings.RemoteHost + ":" + _settings.RemotePort);
                    await ReadPeer(peer, token).ConfigureAwait(false);
                    if (!token.IsCancellationRequested) Status(DeviceConnectionState.Disconnected, "TCP 对端已断开。");
                } catch (Exception ex) {
                    if (!token.IsCancellationRequested) Status(DeviceConnectionState.Fault, "TCP 连接失败：" + ex.Message);
                } finally { client.Close(); Peer removed; _clients.TryRemove(client, out removed); }
                if (!_settings.AutoReconnect || token.IsCancellationRequested) break;
                await Task.Delay(_settings.ReconnectIntervalMs, token).ConfigureAwait(false);
            } while (!token.IsCancellationRequested);
        }
        private async Task AcceptLoop(TcpListener listener, CancellationToken token)
        {
            using (token.Register(() => listener.Stop()))
            while (!token.IsCancellationRequested) {
                try {
                    var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    if (token.IsCancellationRequested || _clients.Count >= 32) { client.Close(); continue; }
                    client.NoDelay = true;
                    var peer = new Peer { Client = client }; _clients.TryAdd(client, peer);
                    Status(DeviceConnectionState.Connected, "TCP 客户端已连接，在线 " + _clients.Count);
                    long id = Interlocked.Increment(ref _id);
                    Task task = ReadPeer(peer, token); _tasks.TryAdd(id, task);
                    _ = task.ContinueWith(t => { Task removed; _tasks.TryRemove(id, out removed); }, TaskScheduler.Default);
                } catch (Exception ex) {
                    if (token.IsCancellationRequested) break;
                    Status(DeviceConnectionState.Fault, "TCP 监听失败：" + ex.Message);
                    await Task.Delay(500, token).ConfigureAwait(false);
                }
            }
        }
        private async Task ReadPeer(Peer peer, CancellationToken token)
        {
            try {
                using (peer.Client)
                using (token.Register(() => peer.Client.Close())) {
                    var stream = peer.Client.GetStream(); byte[] buffer = new byte[1024], line = new byte[4096]; int length = 0;
                    while (!token.IsCancellationRequested) {
                        int count = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                        if (count == 0) break;
                        Data(true, buffer.Take(count).ToArray());
                        // 普通收发无需触发字符；仅开启外部触发时解析换行命令。
                        if (!_settings.EnableReceiveTrigger) continue;
                        for (int i = 0; i < count; i++) {
                            if (buffer[i] == 10) {
                                string text = Encoding.UTF8.GetString(line, 0, length).TrimEnd('\r'); length = 0;
                                if (!string.IsNullOrWhiteSpace(_settings.TriggerCommand) && text.IndexOf(_settings.TriggerCommand, StringComparison.OrdinalIgnoreCase) >= 0) {
                                    token.ThrowIfCancellationRequested();
                                    try { Triggered?.Invoke(this, new TriggerEventArgs(TriggerSource.Tcp, text)); } catch (Exception ex) { _logger?.Error("TCP", "触发事件异常", ex); }
                                    if (!string.IsNullOrEmpty(_settings.AckCommand)) await WritePeer(peer, Encoding.UTF8.GetBytes(_settings.AckCommand + "\r\n"), token).ConfigureAwait(false);
                                }
                            } else { if (length == line.Length) throw new IOException("TCP 触发行超过 4096 字节。"); line[length++] = buffer[i]; }
                        }
                    }
                }
            } catch (Exception ex) { if (!token.IsCancellationRequested) _logger?.Warn("TCP", "TCP 接收结束：" + ex.Message); }
            finally {
                Peer removed; _clients.TryRemove(peer.Client, out removed);
                if (_settings.Mode != "Client" && !token.IsCancellationRequested && _clients.IsEmpty) Status(DeviceConnectionState.Listening, "TCP 正在监听，暂无客户端。");
            }
        }
        public async Task SendAsync(byte[] data, CancellationToken token)
        {
            var peers = _clients.Values.ToArray(); if (peers.Length == 0) throw new IOException("TCP 尚未连接对端。");
            await Task.WhenAll(peers.Select(peer => WritePeer(peer, data, token))).ConfigureAwait(false);
        }
        private async Task WritePeer(Peer peer, byte[] data, CancellationToken token)
        {
            await peer.Writes.WaitAsync(token).ConfigureAwait(false);
            try {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                using (timeout.Token.Register(() => peer.Client.Close())) {
                    timeout.CancelAfter(_settings.TimeoutMs);
                    await peer.Client.GetStream().WriteAsync(data, 0, data.Length, timeout.Token).ConfigureAwait(false);
                    timeout.Token.ThrowIfCancellationRequested(); Data(false, data);
                }
            } finally { peer.Writes.Release(); }
        }
        public Task PublishResultAsync(DetectionContext context, CancellationToken token)
        {
            if (!_settings.Enabled || !_settings.SendResults || context == null || _clients.IsEmpty) return Task.CompletedTask;
            byte[] data = CommandCodec.Build(_settings.ResultTemplate, false, context);
            return data == null ? Task.CompletedTask : SendAsync(data, token);
        }
        private void Data(bool receive, byte[] bytes) { try { DataTransferred?.Invoke(this, new CommunicationDataEventArgs(CommunicationChannel.TcpTrigger, receive, bytes)); } catch (Exception ex) { _logger?.Error("TCP", "数据事件异常", ex); } }
        private void Status(DeviceConnectionState state, string message) { try { StatusChanged?.Invoke(this, new CommunicationStatusEventArgs(CommunicationChannel.TcpTrigger, state, message)); } catch (Exception ex) { _logger?.Error("TCP", "状态事件异常", ex); } }
        public void Dispose() { StopAsync().GetAwaiter().GetResult(); }
    }
}
