using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspect.Communication.Modbus
{
    /// <summary>
    /// 最小 Modbus TCP 主站（功能码 0x10 写多个保持寄存器）。
    /// 不依赖 NModbus 等第三方库，减少现场 DLL 依赖。
    /// 一个实例只允许一个并发事务，由 SemaphoreSlim 串行化。
    /// </summary>
    public sealed class ModbusTcpClient : IDisposable
    {
        private readonly SemaphoreSlim _transactionLock = new SemaphoreSlim(1, 1);
        private TcpClient _client;
        private NetworkStream _stream;
        private ushort _transactionId;
        private bool _disposed;

        public bool IsConnected
        {
            get
            {
                try { return _client != null && _client.Connected && _stream != null && !(_client.Client.Poll(0, SelectMode.SelectRead) && _client.Client.Available == 0); }
                catch { return false; }
            }
        }

        public async Task ConnectAsync(string host, int port, int timeoutMs, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("host");
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException("port");
            if (_disposed) throw new ObjectDisposedException(GetType().Name);

            // 连接和事务共用同一把锁，重连不能替换正在使用的流。
            await _transactionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            var client = new TcpClient();
            client.NoDelay = true;
            try
            {
                Close();
                Task connectTask = client.ConnectAsync(host, port);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                using (timeout.Token.Register(() => { try { client.Close(); } catch { } }))
                {
                    timeout.CancelAfter(Math.Max(200, timeoutMs));
                    Task completed = await Task.WhenAny(connectTask, Task.Delay(Timeout.Infinite, timeout.Token))
                        .ConfigureAwait(false);
                    if (completed != connectTask)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new TimeoutException(string.Format("连接 Modbus TCP {0}:{1} 超时。", host, port));
                    }

                    await connectTask.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                _client = client;
                _stream = client.GetStream();
            }
            catch
            {
                try { client.Close(); } catch { }
                throw;
            }
            finally { _transactionLock.Release(); }
        }

        public async Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort startAddress, ushort count, int timeoutMs, CancellationToken cancellationToken)
        {
            if (count < 1 || count > 125 || (uint)startAddress + count > 65536) throw new ArgumentOutOfRangeException(nameof(count), "读取范围必须为 1~125 个，地址不得超过 65535。");
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
            await _transactionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                if (!IsConnected) throw new IOException("Modbus TCP 未连接。");
                ushort transaction = ++_transactionId;
                byte[] request = { (byte)(transaction >> 8), (byte)transaction, 0, 0, 0, 6, unitId, 3, (byte)(startAddress >> 8), (byte)startAddress, (byte)(count >> 8), (byte)count };
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                using (timeout.Token.Register(Close)) {
                    timeout.CancelAfter(Math.Max(100, timeoutMs));
                    try {
                        await _stream.WriteAsync(request, 0, request.Length, timeout.Token).ConfigureAwait(false);
                        byte[] header = new byte[7]; await ReadExactlyAsync(_stream, header, 0, 7, timeout.Token).ConfigureAwait(false);
                        int length = (header[4] << 8) | header[5];
                        if (header[0] != request[0] || header[1] != request[1] || header[2] != 0 || header[3] != 0 || header[6] != unitId || length < 3 || length > 254) throw new IOException("Modbus 读取响应头无效。");
                        byte[] pdu = new byte[length - 1]; await ReadExactlyAsync(_stream, pdu, 0, pdu.Length, timeout.Token).ConfigureAwait(false);
                        if (pdu[0] == 0x83) throw new IOException("Modbus 读取异常响应：0x" + pdu[1].ToString("X2"));
                        if (pdu[0] != 3 || pdu[1] != count * 2 || pdu.Length != 2 + count * 2) throw new IOException("Modbus 读取功能码或字节数不匹配。");
                        var result = new ushort[count]; for (int i = 0; i < count; i++) result[i] = (ushort)((pdu[2 + i * 2] << 8) | pdu[3 + i * 2]);
                        return result;
                    } catch (Exception) when (timeout.IsCancellationRequested) { Close(); cancellationToken.ThrowIfCancellationRequested(); throw new TimeoutException("Modbus 读取超时。"); }
                    catch { Close(); throw; }
                }
            } finally { _transactionLock.Release(); }
        }

        public async Task WriteMultipleRegistersAsync(byte unitId, ushort startAddress,
            ushort[] values, int timeoutMs, CancellationToken cancellationToken)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException("values");
            if (values.Length > 123)
                throw new ArgumentOutOfRangeException("values", "Modbus 单次最多写 123 个寄存器。");
            if ((uint)startAddress + values.Length > 65536)
                throw new ArgumentOutOfRangeException("startAddress", "寄存器范围超过 65535。");
            if (_disposed) throw new ObjectDisposedException(GetType().Name);

            await _transactionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!IsConnected) throw new IOException("Modbus TCP 未连接。");

                ushort transaction = ++_transactionId;
                byte[] frame = BuildWriteRequest(transaction, unitId, startAddress, values);

                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(Math.Max(200, timeoutMs));
                    // .NET Framework NetworkStream 的挂起读取不保证响应 token；关闭本次流解除阻塞。
                    NetworkStream stream = _stream;
                    using (timeout.Token.Register(() => { try { stream.Dispose(); } catch { } }))
                    try
                    {
                        await stream.WriteAsync(frame, 0, frame.Length, timeout.Token).ConfigureAwait(false);
                        await stream.FlushAsync(timeout.Token).ConfigureAwait(false);

                        byte[] header = new byte[7];
                        await ReadExactlyAsync(stream, header, 0, header.Length, timeout.Token)
                            .ConfigureAwait(false);
                        int mbapLength = (header[4] << 8) | header[5];
                        if (mbapLength < 2 || mbapLength > 254)
                            throw new IOException("Modbus 响应长度字段非法：" + mbapLength);

                        byte[] pdu = new byte[mbapLength - 1];
                        await ReadExactlyAsync(stream, pdu, 0, pdu.Length, timeout.Token)
                            .ConfigureAwait(false);

                        byte[] response = new byte[header.Length + pdu.Length];
                        Buffer.BlockCopy(header, 0, response, 0, header.Length);
                        Buffer.BlockCopy(pdu, 0, response, header.Length, pdu.Length);
                        ValidateResponse(response, transaction, unitId, startAddress, (ushort)values.Length);
                    }
                    catch (Exception) when (timeout.IsCancellationRequested)
                    {
                        // 无论用户取消还是超时，部分收发后的 TCP 流都不能再用于下一事务。
                        Close();
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new TimeoutException("Modbus TCP 事务超时。");
                    }
                    catch
                    {
                        Close();
                        throw;
                    }
                }
            }
            finally
            {
                _transactionLock.Release();
            }
        }

        public void Close()
        {
            NetworkStream stream = _stream;
            TcpClient client = _client;
            _stream = null;
            _client = null;

            try { if (stream != null) stream.Dispose(); } catch { }
            try { if (client != null) client.Close(); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
            // 不在事务 finally/Release 尚可能运行时销毁信号量。
        }

        private static byte[] BuildWriteRequest(ushort transaction, byte unitId,
            ushort startAddress, ushort[] values)
        {
            int pduLength = 6 + values.Length * 2;
            byte[] frame = new byte[7 + pduLength];
            frame[0] = (byte)(transaction >> 8);
            frame[1] = (byte)(transaction & 0xFF);
            frame[2] = 0;
            frame[3] = 0;
            frame[4] = (byte)((pduLength + 1) >> 8); // unit + PDU
            frame[5] = (byte)((pduLength + 1) & 0xFF);
            frame[6] = unitId;
            frame[7] = 0x10;
            frame[8] = (byte)(startAddress >> 8);
            frame[9] = (byte)(startAddress & 0xFF);
            frame[10] = (byte)(values.Length >> 8);
            frame[11] = (byte)(values.Length & 0xFF);
            frame[12] = (byte)(values.Length * 2);

            int offset = 13;
            foreach (ushort value in values)
            {
                frame[offset++] = (byte)(value >> 8);
                frame[offset++] = (byte)(value & 0xFF);
            }
            return frame;
        }

        private static void ValidateResponse(byte[] response, ushort transaction, byte unitId,
            ushort startAddress, ushort quantity)
        {
            if (response.Length < 9)
                throw new IOException("Modbus 响应长度不足。");
            if (response[2] != 0 || response[3] != 0)
                throw new IOException("Modbus 响应协议标识必须为 0。");

            if (response[0] != (byte)(transaction >> 8) ||
                response[1] != (byte)(transaction & 0xFF))
                throw new IOException("Modbus 响应事务号不匹配。");

            if (response[6] != unitId)
                throw new IOException("Modbus 响应站号不匹配。");

            if (response[7] == 0x90)
            {
                byte exceptionCode = response[8];
                throw new IOException(string.Format("Modbus 异常响应，异常码 0x{0:X2}。", exceptionCode));
            }

            if (response[7] != 0x10 || response.Length != 12)
                throw new IOException("Modbus 响应功能码或长度不匹配。");

            ushort responseAddress = (ushort)((response[8] << 8) | response[9]);
            ushort responseQuantity = (ushort)((response[10] << 8) | response[11]);
            if (responseAddress != startAddress || responseQuantity != quantity)
                throw new IOException("Modbus 响应地址/数量不匹配。");
        }

        private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            int read = 0;
            while (read < count)
            {
                int n = await stream.ReadAsync(buffer, offset + read, count - read, cancellationToken)
                    .ConfigureAwait(false);
                if (n <= 0) throw new IOException("Modbus TCP 连接被远端关闭。");
                read += n;
            }
        }
    }
}
