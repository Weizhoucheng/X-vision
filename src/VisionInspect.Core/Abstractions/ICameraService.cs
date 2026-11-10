using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;

namespace VisionInspect.Core.Abstractions
{
    /// <summary>海康 MVS 相机服务。实现负责掉线检测与后台自动重连。</summary>
    public interface ICameraService
    {
        event EventHandler<CameraFrameEventArgs> FrameReceived;
        event EventHandler<CameraStatusEventArgs> StatusChanged;

        bool IsConnected { get; }
        CameraDeviceInfo ConnectedDevice { get; }
        DeviceConnectionState State { get; }

        IReadOnlyList<CameraDeviceInfo> EnumerateDevices();
        Task ConnectAsync(CameraDeviceInfo device, CameraSettings settings, CancellationToken cancellationToken);
        Task DisconnectAsync();
        Task ApplySettingsAsync(CameraSettings settings, CancellationToken cancellationToken);
        /// <summary>实时写曝光时间并回读校验；连接断开时抛错。</summary>
        Task SetExposureAsync(float exposureTimeUs, CancellationToken cancellationToken);
        /// <summary>实时写增益并回读校验；连接断开时抛错。</summary>
        Task SetGainAsync(float gainDb, CancellationToken cancellationToken);
        Task SetTriggerModeAsync(CameraTriggerMode mode, CancellationToken cancellationToken);
        Task<RawImage> CaptureAsync(TimeSpan timeout, CancellationToken cancellationToken);
        Task ShutdownAsync();
    }
}
