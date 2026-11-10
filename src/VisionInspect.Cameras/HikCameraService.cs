using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MvCamCtrl.NET;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;

namespace VisionInspect.Cameras
{
    /// <summary>
    /// 海康 MVS 工业相机服务，支持 GigE/USB3、Mono8/RGB8/BGR8、软触发/硬触发/连续采集，
    /// 带连接状态监控和后台自动重连。
    ///
    /// 线程模型：
    /// - MVS 图像回调线程只做“拷贝帧 + 唤醒等待者 + 预览事件”，绝不执行检测；
    /// - 连接/重连由信号量串行化，避免 UI 手动连接与监控线程竞争；
    /// - 预览事件通过 UI 层节流，防止高分辨率相机拖慢界面。
    /// </summary>
    public sealed class HikCameraService : ICameraService, IDisposable
    {
        private const int PixelTypeMono8 = 17301505;    // PixelType_Gvsp_Mono8
        private const int PixelTypeRgb8 = 35127316;     // PixelType_Gvsp_RGB8_Packed
        private const int PixelTypeBgr8 = 35127317;     // PixelType_Gvsp_BGR8_Packed

        private readonly object _sync = new object();
        private readonly SemaphoreSlim _connectGate = new SemaphoreSlim(1, 1);
        private readonly IAppLogger _logger;
        private readonly Dictionary<string, MyCamera.MV_CC_DEVICE_INFO> _knownDevices =
            new Dictionary<string, MyCamera.MV_CC_DEVICE_INFO>(StringComparer.OrdinalIgnoreCase);

        private MyCamera _camera;
        private MyCamera.MV_CC_DEVICE_INFO _deviceInfo;
        private CameraDeviceInfo _connectedDevice;
        private CameraSettings _settings;
        private MyCamera.cbOutputExdelegate _imageCallback;
        private CancellationTokenSource _monitorCts;
        private Task _monitorTask;
        private TaskCompletionSource<RawImage> _pendingCapture;
        private RawImage _latestFrame;
        private DateTime _latestFrameUtc = DateTime.MinValue;
        private DateTime _lastFrameUtc = DateTime.MinValue;
        private long _frameCounter;
        private bool _grabbing;
        private volatile bool _keepConnected;
        private bool _disposed;
        private DeviceConnectionState _state = DeviceConnectionState.Disconnected;

        public HikCameraService(IAppLogger logger)
        {
            _logger = logger;
        }

        public event EventHandler<CameraFrameEventArgs> FrameReceived;
        public event EventHandler<CameraStatusEventArgs> StatusChanged;

        public bool IsConnected
        {
            get
            {
                lock (_sync)
                    return _state == DeviceConnectionState.Connected && _camera != null;
            }
        }

        public CameraDeviceInfo ConnectedDevice
        {
            get { lock (_sync) return _connectedDevice; }
        }

        public DeviceConnectionState State
        {
            get { lock (_sync) return _state; }
        }

        public IReadOnlyList<CameraDeviceInfo> EnumerateDevices()
        {
            var result = new List<CameraDeviceInfo>();
            MyCamera.MV_CC_DEVICE_INFO_LIST list = new MyCamera.MV_CC_DEVICE_INFO_LIST();
            int ret = MyCamera.MV_CC_EnumDevices_NET(
                MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref list);

            if (ret != MyCamera.MV_OK)
                throw new CameraSdkException(ret, "枚举相机设备");

            lock (_sync) _knownDevices.Clear();

            if (list.pDeviceInfo == null || list.nDeviceNum == 0)
                return result;

            for (int i = 0; i < list.nDeviceNum; i++)
            {
                try
                {
                    IntPtr pointer = list.pDeviceInfo[i];
                    if (pointer == IntPtr.Zero) continue;

                    var raw = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(
                        pointer, typeof(MyCamera.MV_CC_DEVICE_INFO));

                    CameraDeviceInfo device = ConvertDevice(raw);
                    if (device == null) continue;

                    lock (_sync) _knownDevices[device.Id] = raw;
                    result.Add(device);
                }
                catch (Exception ex)
                {
                    if (_logger != null)
                        _logger.Warn("Camera", "解析第 " + i + " 个相机设备失败：" + ex.Message);
                }
            }

            return result
                .OrderBy(x => x.Transport)
                .ThenBy(x => x.DisplayName)
                .ToList();
        }

        public async Task ConnectAsync(CameraDeviceInfo device, CameraSettings settings,
            CancellationToken cancellationToken)
        {
            if (device == null) throw new ArgumentNullException("device");
            if (settings == null) throw new ArgumentNullException("settings");
            if (_disposed) throw new ObjectDisposedException(GetType().Name);

            MyCamera.MV_CC_DEVICE_INFO rawInfo = default(MyCamera.MV_CC_DEVICE_INFO);
            bool known;
            lock (_sync) known = _knownDevices.TryGetValue(device.Id, out rawInfo);

            if (!known)
            {
                EnumerateDevices();
                lock (_sync)
                {
                    if (!_knownDevices.TryGetValue(device.Id, out rawInfo))
                        throw new VisionInspectException("相机不在线或已被其他程序占用：" + device.DisplayName);
                }
            }

            await StopMonitorAsync().ConfigureAwait(false);
            await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _keepConnected = true;
                await Task.Run(() => DisconnectCore(false)).ConfigureAwait(false);
                _settings = CopySettings(settings);
                _deviceInfo = rawInfo;
                _connectedDevice = device;

                SetState(DeviceConnectionState.Connecting, "正在连接相机：" + device.DisplayName, device);
                await Task.Run(() => OpenCoreSafe(device), cancellationToken).ConfigureAwait(false);
                StartMonitor();
            }
            catch
            {
                SetState(DeviceConnectionState.Fault, "相机连接失败。", device);
                throw;
            }
            finally
            {
                _connectGate.Release();
            }
        }

        public async Task DisconnectAsync()
        {
            _keepConnected = false;
            await StopMonitorAsync().ConfigureAwait(false);
            await _connectGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() => DisconnectCore(true)).ConfigureAwait(false);
            }
            finally
            {
                _connectGate.Release();
            }
        }

        public async Task ApplySettingsAsync(CameraSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            settings = CopySettings(settings);

            await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_camera == null) { _settings = CopySettings(settings); return; }
                await Task.Run(() => ApplySettingsCore(settings, true), cancellationToken)
                    .ConfigureAwait(false);
                _settings = CopySettings(settings);
            }
            catch
            {
                await Task.Run(() => DisconnectCore(false)).ConfigureAwait(false);
                SetState(DeviceConnectionState.Fault, "相机参数写入失败，连接已关闭。", _connectedDevice);
                throw;
            }
            finally
            {
                _connectGate.Release();
            }
        }

        public Task SetExposureAsync(float exposureTimeUs, CancellationToken cancellationToken)
        {
            CameraSettings copy = ReadCurrentSettings();
            copy.ExposureTimeUs = exposureTimeUs;
            return ApplySettingsAsync(copy, cancellationToken);
        }

        public Task SetGainAsync(float gainDb, CancellationToken cancellationToken)
        {
            CameraSettings copy = ReadCurrentSettings();
            copy.GainDb = gainDb;
            return ApplySettingsAsync(copy, cancellationToken);
        }

        private CameraSettings ReadCurrentSettings()
        {
            lock (_sync)
            {
                if (_settings == null) throw new VisionInspectException("请先连接相机。");
                return CopySettings(_settings);
            }
        }

        public async Task SetTriggerModeAsync(CameraTriggerMode mode, CancellationToken cancellationToken)
        {
            CameraSettings current;
            lock (_sync)
            {
                if (_settings == null) throw new VisionInspectException("请先连接相机。");
                current = CopySettings(_settings);
                current.TriggerMode = mode;
            }

            await ApplySettingsAsync(current, cancellationToken).ConfigureAwait(false);
        }

        public async Task<RawImage> CaptureAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("timeout");
            var completion = new TaskCompletionSource<RawImage>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                // 只在创建等待者和发送软触发期间占有设备锁；等待图像时允许断线/停止唤醒等待者。
                await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    CameraTriggerMode mode;
                    lock (_sync)
                    {
                        if (_state != DeviceConnectionState.Connected || _camera == null || !_grabbing)
                            throw new VisionInspectException("相机未连接或未开始取流。");
                        if (_pendingCapture != null)
                            throw new VisionInspectException("相机取图重入：上一帧尚未返回。");
                        _pendingCapture = completion;
                        mode = _settings == null ? CameraTriggerMode.Software : _settings.TriggerMode;
                    }
                    if (mode == CameraTriggerMode.Software)
                    {
                        int ret = await Task.Run(() => _camera.MV_CC_TriggerSoftwareExecute_NET(), cancellationToken)
                            .ConfigureAwait(false);
                        if (ret != MyCamera.MV_OK) throw new CameraSdkException(ret, "发送软触发");
                    }
                }
                finally { _connectGate.Release(); }

                using (var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    Task delay = Task.Delay(timeout, wait.Token);
                    Task completed = await Task.WhenAny(completion.Task, delay).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (completed != completion.Task)
                        throw new VisionInspectException("相机取图超时（" + (int)timeout.TotalMilliseconds + " ms）。");
                    wait.Cancel(); // 成功取图后释放定时等待，不积压长超时任务。
                    return await completion.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_pendingCapture, completion)) _pendingCapture = null;
                }
            }
        }

        public async Task ShutdownAsync()
        {
            if (_disposed) return;
            _keepConnected = false;
            await StopMonitorAsync().ConfigureAwait(false);
            // 必须真正获得锁后才可销毁句柄；不能忽略 WaitAsync(timeout) 返回的 bool。
            await _connectGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() => DisconnectCore(true)).ConfigureAwait(false);
                _disposed = true;
            }
            finally { _connectGate.Release(); }
        }

        public void Dispose()
        {
            try { ShutdownAsync().GetAwaiter().GetResult(); } catch { }
        }

        private void OpenCore(CameraDeviceInfo device)
        {
            var camera = new MyCamera();
            int ret = camera.MV_CC_CreateDevice_NET(ref _deviceInfo);
            if (ret != MyCamera.MV_OK)
                throw new CameraSdkException(ret, "创建相机句柄");

            _camera = camera;
            ret = camera.MV_CC_OpenDevice_NET();
            if (ret != MyCamera.MV_OK)
            {
                DisconnectCore(false);
                throw new CameraSdkException(ret, "打开相机");
            }

            _imageCallback = OnFrameReceived;
            ret = camera.MV_CC_RegisterImageCallBackEx_NET(_imageCallback, IntPtr.Zero);
            if (ret != MyCamera.MV_OK)
            {
                DisconnectCore(false);
                throw new CameraSdkException(ret, "注册图像回调");
            }

            ApplySettingsCore(_settings, false);

            ret = camera.MV_CC_StartGrabbing_NET();
            if (ret != MyCamera.MV_OK)
            {
                DisconnectCore(false);
                throw new CameraSdkException(ret, "启动取流");
            }

            lock (_sync)
            {
                _grabbing = true;
                _lastFrameUtc = DateTime.UtcNow;
                _latestFrame = null;
            }

            SetState(DeviceConnectionState.Connected, "相机已连接并开始取流。", device);
            if (_logger != null)
                _logger.Info("Camera", string.Format("相机连接成功：{0}，触发模式 {1}。",
                    device.DisplayName, _settings == null ? "Unknown" : _settings.TriggerMode.ToString()));
        }

        private void ApplySettingsCore(CameraSettings settings, bool restartGrabbing)
        {
            if (_camera == null || settings == null) return;

            if (restartGrabbing && _grabbing)
            {
                int stopRet = _camera.MV_CC_StopGrabbing_NET();
                if (stopRet != MyCamera.MV_OK) throw new CameraSdkException(stopRet, "停止取流以设置参数");
                _grabbing = false;
            }

            SetRequiredEnum("TriggerMode", (uint)(settings.TriggerMode == CameraTriggerMode.Continuous ? 0 : 1));
            if (settings.TriggerMode == CameraTriggerMode.Software)
            {
                SetRequiredEnum("TriggerSource", 7u); // Software
            }
            else if (settings.TriggerMode == CameraTriggerMode.Hardware)
            {
                SetRequiredEnum("TriggerSource", (uint)settings.HardwareTriggerSource);
            }

            uint pixelType = settings.PixelFormat == CameraPixelFormat.Mono8
                ? (uint)PixelTypeMono8
                : (uint)PixelTypeRgb8;
            int ret = _camera.MV_CC_SetPixelFormat_NET(pixelType);
            if (ret != MyCamera.MV_OK)
            {
                // 某些相机不支持 RGB8_Packed，尝试 BGR8；RawImage 会在转换层处理 BGR。
                int fallback = settings.PixelFormat == CameraPixelFormat.Mono8 ? ret :
                    _camera.MV_CC_SetPixelFormat_NET((uint)PixelTypeBgr8);
                if (fallback != MyCamera.MV_OK) throw new CameraSdkException(ret, "设置 Mono8/RGB8 像素格式");
                if (_logger != null)
                {
                    _logger.Warn("Camera", string.Format(
                        "设置像素格式失败（0x{0:X8}），BGR8 回退 {1}。", ret,
                        fallback == MyCamera.MV_OK ? "成功" : "也失败"));
                }
            }

            // 关键修复：曝光/增益属于在线参数，不能只在“连接时应用”开关为 true 时写。
            // 每次 ApplySettingsAsync 都直接写相机寄存器并回读校验，效果与 MVS 客户端一致。
            TrySetEnum("ExposureAuto", 0u);
            TrySetEnum("GainAuto", 0u);
            SetFloatVerified("ExposureTime", settings.ExposureTimeUs, "曝光时间");
            SetFloatVerified("Gain", settings.GainDb, "增益");

            TrySetEnum("AcquisitionMode", 2u); // Continuous
            TrySetImageNodeNumber(2u);
            TryGigEPacketSize();

            if (restartGrabbing)
            {
                int startRet = _camera.MV_CC_StartGrabbing_NET();
                if (startRet != MyCamera.MV_OK)
                    throw new CameraSdkException(startRet, "重新启动取流");
                _grabbing = true;
            }
        }

        private void OpenCoreSafe(CameraDeviceInfo device)
        {
            try
            {
                OpenCore(device);
            }
            catch
            {
                DisconnectCore(false);
                throw;
            }
        }

        private void DisconnectCore(bool publishState)
        {
            TaskCompletionSource<RawImage> pending = null;
            lock (_sync)
            {
                pending = _pendingCapture;
                _pendingCapture = null;
            }
            if (pending != null)
                pending.TrySetException(new VisionInspectException("相机连接已断开。"));

            MyCamera camera = _camera;
            _camera = null;
            if (camera != null)
            {
                try
                {
                    if (_grabbing) camera.MV_CC_StopGrabbing_NET();
                }
                catch { }
                try { camera.MV_CC_CloseDevice_NET(); } catch { }
                try { camera.MV_CC_DestroyDevice_NET(); } catch { }
            }

            lock (_sync)
            {
                _grabbing = false;
                _latestFrame = null;
                _latestFrameUtc = DateTime.MinValue;
            }

            if (publishState)
                SetState(DeviceConnectionState.Disconnected, "相机已断开。", _connectedDevice);
        }

        private void StartMonitor()
        {
            _monitorCts = new CancellationTokenSource();
            CancellationToken token = _monitorCts.Token;
            _monitorTask = Task.Run(() => MonitorLoopAsync(token));
        }

        private async Task StopMonitorAsync()
        {
            CancellationTokenSource cts = _monitorCts;
            Task task = _monitorTask;
            _monitorCts = null;
            _monitorTask = null;
            if (cts == null) return;
            cts.Cancel();
            try { if (task != null) await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            finally { cts.Dispose(); }
        }

        private async Task MonitorLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    int interval = _settings == null ? 3000 : _settings.ReconnectIntervalMs;
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                    if (!_keepConnected || cancellationToken.IsCancellationRequested) continue;

                    bool lost;
                    // 健康检查也访问 native 句柄，必须与连接/参数/销毁串行。
                    await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        try { lost = _camera == null || !_grabbing || !_camera.MV_CC_IsDeviceConnected_NET(); }
                        catch { lost = true; }
                    }
                    finally { _connectGate.Release(); }

                    if (!lost && _settings != null && _settings.TriggerMode == CameraTriggerMode.Continuous && _grabbing)
                    {
                        lock (_sync)
                        {
                            lost = _lastFrameUtc != DateTime.MinValue &&
                                   (DateTime.UtcNow - _lastFrameUtc).TotalMilliseconds >
                                   Math.Max(3000, _settings.FrameTimeoutMs * 2);
                        }
                    }

                    if (!lost) continue;

                    if (_settings != null && !_settings.EnableAutoReconnect)
                    {
                        SetState(DeviceConnectionState.Fault, "相机掉线，自动重连未启用。", _connectedDevice);
                        continue;
                    }

                    if (_logger != null)
                        _logger.Warn("Camera", "检测到相机掉线，开始自动重连。");

                    SetState(DeviceConnectionState.Reconnecting, "相机掉线，正在重连...", _connectedDevice);
                    await TryReconnectAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_logger != null) _logger.Error("Camera", "相机监控线程异常。", ex);
                    await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task TryReconnectAsync(CancellationToken cancellationToken)
        {
            if (!await _connectGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                return;

            try
            {
                if (!_keepConnected || cancellationToken.IsCancellationRequested) return;
                DisconnectCore(false);

                for (int attempt = 1; attempt <= 3 && _keepConnected; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        SetState(DeviceConnectionState.Reconnecting,
                            string.Format("第 {0}/3 次重连相机...", attempt), _connectedDevice);
                        // USB 重插后枚举结构可能改变，按稳定序列号重新查找，不复用旧设备结构。
                        EnumerateDevices();
                        lock (_sync)
                        {
                            if (_connectedDevice == null || !_knownDevices.TryGetValue(_connectedDevice.Id, out _deviceInfo))
                                throw new VisionInspectException("目标相机尚未重新上线。");
                        }
                        await Task.Run(() => OpenCoreSafe(_connectedDevice), cancellationToken)
                            .ConfigureAwait(false);
                        if (_logger != null) _logger.Info("Camera", "相机自动重连成功。");
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (_logger != null)
                            _logger.Warn("Camera", string.Format("第 {0} 次重连失败：{1}", attempt, ex.Message));
                        if (attempt < 3)
                            await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
                    }
                }

                SetState(DeviceConnectionState.Fault, "相机自动重连失败，将继续重试。", _connectedDevice);
            }
            finally
            {
                _connectGate.Release();
            }
        }

        private void OnFrameReceived(IntPtr pData, ref MyCamera.MV_FRAME_OUT_INFO_EX frameInfo, IntPtr pUser)
        {
            if (_disposed || pData == IntPtr.Zero) return;

            try
            {
                int pixelType = (int)frameInfo.enPixelType;
                lock (_sync) _lastFrameUtc = DateTime.UtcNow;
                ImagePixelFormat format;
                if (pixelType == PixelTypeMono8) format = ImagePixelFormat.Mono8;
                else if (pixelType == PixelTypeRgb8) format = ImagePixelFormat.Rgb24;
                else if (pixelType == PixelTypeBgr8) format = ImagePixelFormat.Bgr24;
                else
                {
                    if (_logger != null && (Interlocked.Increment(ref _frameCounter) % 100) == 0)
                        _logger.Warn("Camera", string.Format("暂不支持的像素格式 0x{0:X8}，请设置 Mono8 或 RGB8Packed。", pixelType));
                    return;
                }

                // 只有“检测正在等帧”或“预览抽帧命中”时才拷贝整帧，
                // 避免高分辨率相机在无人使用时每秒分配数百 MB。
                bool needForDetection;
                lock (_sync) needForDetection = _pendingCapture != null;

                CameraSettings settings = _settings;
                int skip = settings == null ? 2 : Math.Max(1, settings.PreviewFrameSkip);
                long frameNumber = Interlocked.Increment(ref _frameCounter);
                bool previewDue = (frameNumber % skip) == 0;
                if (!needForDetection && !previewDue) return;

                int width = frameInfo.nWidth;
                int height = frameInfo.nHeight;
                int length = (int)frameInfo.nFrameLen;
                int expected = checked(width * height * (format == ImagePixelFormat.Mono8 ? 1 : 3));
                if (length < expected)
                    throw new InvalidOperationException(string.Format(
                        "帧长度异常：{0} < {1}。", length, expected));

                byte[] buffer = new byte[expected];
                Marshal.Copy(pData, buffer, 0, expected);
                var frame = new RawImage(buffer, width, height, format,
                    ConnectedDevice == null ? "Camera" : ConnectedDevice.DisplayName);

                TaskCompletionSource<RawImage> pending;
                lock (_sync)
                {
                    _lastFrameUtc = DateTime.UtcNow;
                    _latestFrame = frame;
                    _latestFrameUtc = _lastFrameUtc;
                    pending = _pendingCapture;
                    if (pending != null) _pendingCapture = null;
                }

                if (pending != null)
                    pending.TrySetResult(frame);

                if (previewDue)
                {
                    EventHandler<CameraFrameEventArgs> handler = FrameReceived;
                    if (handler != null)
                        handler(this, new CameraFrameEventArgs(frame));
                }
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("Camera", "处理相机图像回调失败。", ex);
            }
        }

        private CameraDeviceInfo ConvertDevice(MyCamera.MV_CC_DEVICE_INFO raw)
        {
            var device = new CameraDeviceInfo();
            if (raw.nTLayerType == MyCamera.MV_GIGE_DEVICE)
            {
                var gige = (MyCamera.MV_GIGE_DEVICE_INFO_EX)MyCamera.ByteToStruct(
                    raw.SpecialInfo.stGigEInfo, typeof(MyCamera.MV_GIGE_DEVICE_INFO_EX));
                string user = DecodeFixedString(gige.chUserDefinedName);
                device.Id = "GIGE:" + (gige.chSerialNumber ?? string.Empty);
                device.Transport = "GigE";
                device.Model = gige.chModelName ?? string.Empty;
                device.SerialNumber = gige.chSerialNumber ?? string.Empty;
                device.Manufacturer = gige.chManufacturerName ?? string.Empty;
                device.UserDefinedName = user;
                device.IpAddress = IpToString(gige.nCurrentIp);
                device.DisplayName = string.IsNullOrWhiteSpace(user)
                    ? string.Format("{0} [{1}]", device.Model, string.IsNullOrEmpty(device.IpAddress) ? device.SerialNumber : device.IpAddress)
                    : user + " / " + device.Model;
                return device;
            }

            if (raw.nTLayerType == MyCamera.MV_USB_DEVICE)
            {
                var usb = (MyCamera.MV_USB3_DEVICE_INFO_EX)MyCamera.ByteToStruct(
                    raw.SpecialInfo.stUsb3VInfo, typeof(MyCamera.MV_USB3_DEVICE_INFO_EX));
                string user = DecodeFixedString(usb.chUserDefinedName);
                device.Id = "USB:" + (usb.chSerialNumber ?? string.Empty);
                device.Transport = "USB3";
                device.Model = usb.chModelName ?? string.Empty;
                device.SerialNumber = usb.chSerialNumber ?? string.Empty;
                device.Manufacturer = usb.chManufacturerName ?? string.Empty;
                device.UserDefinedName = user;
                device.DisplayName = string.IsNullOrWhiteSpace(user)
                    ? string.Format("{0} [USB {1}]", device.Model, device.SerialNumber)
                    : user + " / " + device.Model;
                return device;
            }

            return null;
        }

        private static string DecodeFixedString(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            int length = Array.IndexOf(bytes, (byte)0);
            if (length < 0) length = bytes.Length;
            return Encoding.UTF8.GetString(bytes, 0, length).Trim();
        }

        private static string IpToString(uint ip)
        {
            if (ip == 0) return string.Empty;
            byte[] bytes = BitConverter.GetBytes(ip);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return new IPAddress(bytes).ToString();
        }

        private void TrySetEnum(string key, uint value)
        {
            if (_camera == null) return;
            int ret = _camera.MV_CC_SetEnumValue_NET(key, value);
            if (ret != MyCamera.MV_OK && _logger != null)
                _logger.Warn("Camera", string.Format("设置枚举参数 {0}={1} 失败：0x{2:X8}", key, value, ret));
        }

        /// <summary>触发模式是检测契约；失败必须上报，不能继续以错误的触发方式取图。</summary>
        private void SetRequiredEnum(string key, uint value)
        {
            int ret = _camera.MV_CC_SetEnumValue_NET(key, value);
            if (ret != MyCamera.MV_OK) throw new CameraSdkException(ret, "设置 " + key);
        }

        private static CameraSettings CopySettings(CameraSettings value)
        {
            return new CameraSettings
            {
                TriggerMode = value.TriggerMode, PixelFormat = value.PixelFormat,
                HardwareTriggerSource = value.HardwareTriggerSource,
                ExposureTimeUs = value.ExposureTimeUs, GainDb = value.GainDb,
                ApplyExposureAndGainOnConnect = value.ApplyExposureAndGainOnConnect,
                EnableAutoReconnect = value.EnableAutoReconnect,
                ReconnectIntervalMs = value.ReconnectIntervalMs,
                FrameTimeoutMs = value.FrameTimeoutMs, PreviewFrameSkip = value.PreviewFrameSkip
            };
        }

        /// <summary>
        /// 直接写入浮点相机参数，并回读确认。写入失败必须抛错，不能静默保留旧参数。
        /// </summary>
        private void SetFloatVerified(string key, float value, string displayName)
        {
            if (_camera == null) return;
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                throw new VisionInspectException(displayName + "超出有效范围。");

            int ret = _camera.MV_CC_SetFloatValue_NET(key, value);
            if (ret != MyCamera.MV_OK)
                throw new CameraSdkException(ret, "设置" + displayName + "(" + key + ")");

            // 部分相机把参数映射到离散档位，回读用于日志和现场核对。
            var actual = new MyCamera.MVCC_FLOATVALUE();
            int readRet = _camera.MV_CC_GetFloatValue_NET(key, ref actual);
            if (_logger != null)
            {
                if (readRet == MyCamera.MV_OK)
                    _logger.Info("Camera", string.Format("相机参数已写入：{0}({1})={2}，回读={3}",
                        displayName, key, value, actual.fCurValue));
                else
                    _logger.Warn("Camera", string.Format("相机参数已写入但回读失败：{0}({1})，回读码 0x{2:X8}",
                        displayName, key, readRet));
            }

            if (readRet == MyCamera.MV_OK)
            {
                float tolerance = Math.Max(0.01f, Math.Abs(value) * 0.01f);
                if (Math.Abs(actual.fCurValue - value) > tolerance && _logger != null)
                    _logger.Warn("Camera", string.Format(
                        "{0}写入值与回读值差异较大：写入 {1}，回读 {2}；相机可能对参数做了量化。",
                        displayName, value, actual.fCurValue));
            }
        }

        private void TrySetFloat(string key, float value)
        {
            if (_camera == null || value < 0) return;
            int ret = _camera.MV_CC_SetFloatValue_NET(key, value);
            if (ret != MyCamera.MV_OK && _logger != null)
                _logger.Warn("Camera", string.Format("设置浮点参数 {0}={1} 失败：0x{2:X8}", key, value, ret));
        }

        private void TrySetImageNodeNumber(uint count)
        {
            if (_camera == null) return;
            int ret = _camera.MV_CC_SetImageNodeNum_NET(count);
            if (ret != MyCamera.MV_OK && _logger != null)
                _logger.Debug("Camera", string.Format("设置取流缓存节点失败：0x{0:X8}", ret));
        }

        private void TryGigEPacketSize()
        {
            if (_camera == null) return;
            try
            {
                int packetSize = _camera.MV_CC_GetOptimalPacketSize_NET();
                if (packetSize > 0)
                    _camera.MV_GIGE_SetGevSCPSPacketSize_NET((uint)packetSize);
            }
            catch
            {
                // USB 相机或旧固件不支持，无影响。
            }
        }

        private void SetState(DeviceConnectionState state, string message, CameraDeviceInfo device)
        {
            lock (_sync) _state = state;
            if (_logger != null && state != DeviceConnectionState.Reconnecting)
                _logger.Debug("Camera", message);
            EventHandler<CameraStatusEventArgs> handler = StatusChanged;
            if (handler != null)
            {
                try { handler(this, new CameraStatusEventArgs(state, message, device)); }
                catch (Exception ex)
                {
                    if (_logger != null) _logger.Error("Camera", "相机状态事件处理异常。", ex);
                }
            }
        }
    }
}
