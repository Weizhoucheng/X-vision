using System;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;

namespace VisionInspect.Cameras
{
    /// <summary>把 ICameraService 适配为检测循环的图像输入源。</summary>
    public sealed class CameraInputProvider : IInputProvider
    {
        private readonly ICameraService _camera;
        private readonly TimeSpan _timeout;

        public CameraInputProvider(ICameraService camera, int timeoutMs)
        {
            if (camera == null) throw new ArgumentNullException("camera");
            _camera = camera;
            _timeout = TimeSpan.FromMilliseconds(Math.Max(500, timeoutMs));
        }

        public string Name
        {
            get
            {
                return _camera.ConnectedDevice == null
                    ? "工业相机"
                    : "相机:" + _camera.ConnectedDevice.DisplayName;
            }
        }

        public Task<RawImage> AcquireAsync(CancellationToken cancellationToken)
        {
            if (!_camera.IsConnected)
                throw new VisionInspectException("工业相机未连接。");
            return _camera.CaptureAsync(_timeout, cancellationToken);
        }
    }
}
