using System;

namespace VisionInspect.Core
{
    /// <summary>检测运行状态。状态迁移由 DetectionController 统一维护。</summary>
    public enum DetectionRunState
    {
        Idle = 0,
        Running = 1,
        Paused = 2,
        Stopping = 3,
        Fault = 4
    }

    /// <summary>检测模式。</summary>
    public enum DetectionMode
    {
        Single = 0,
        Continuous = 1
    }

    /// <summary>图像输入方式。</summary>
    public enum InputMode
    {
        LocalImages = 0,
        Camera = 1
    }

    /// <summary>本地图片播放方式。</summary>
    public enum PlaybackMode
    {
        /// <summary>只检测当前选中图片。</summary>
        Manual = 0,
        /// <summary>按列表顺序检测，到末尾后自动停止。</summary>
        Sequential = 1,
        /// <summary>按列表顺序循环检测。</summary>
        Loop = 2
    }

    /// <summary>用户角色。权限判断只认这两个角色。</summary>
    public enum UserRole
    {
        Operator = 0,
        Administrator = 1
    }

    /// <summary>相机触发方式。</summary>
    public enum CameraTriggerMode
    {
        /// <summary>软触发：上位机调用 TriggerSoftware。</summary>
        Software = 0,
        /// <summary>硬触发：由相机 IO（默认 Line0）触发。</summary>
        Hardware = 1,
        /// <summary>连续采集（自由运行），适合调试预览。</summary>
        Continuous = 2
    }

    /// <summary>相机像素格式。只承诺 Mono8 / RGB24 两条链路。</summary>
    public enum CameraPixelFormat
    {
        Mono8 = 0,
        Rgb24 = 1
    }

    /// <summary>原始图像像素格式。</summary>
    public enum ImagePixelFormat
    {
        Mono8 = 0,
        Rgb24 = 1,
        Bgr24 = 2
    }

    /// <summary>结构化输出值的逻辑类型。</summary>
    public enum VisionValueKind
    {
        Numeric = 0,
        Text = 1,
        Boolean = 2,
        Image = 3,
        Other = 4
    }

    /// <summary>设备/通讯状态。</summary>
    public enum DeviceConnectionState
    {
        Disabled = 0,
        Disconnected = 1,
        Connecting = 2,
        Connected = 3,
        Reconnecting = 4,
        Fault = 5,
        Listening = 6
    }

    /// <summary>检测判定结果。</summary>
    public enum InspectionVerdict
    {
        OK = 0,
        NG = 1,
        Error = 2
    }

    /// <summary>日志等级。</summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
        Audit = 4
    }

    /// <summary>通讯通道类型。</summary>
    public enum CommunicationChannel
    {
        ModbusTcp = 0,
        Serial = 1,
        TcpTrigger = 2
    }

    /// <summary>触发信号来源。</summary>
    public enum TriggerSource
    {
        Manual = 0,
        Serial = 1,
        Tcp = 2,
        CameraHardware = 3
    }
}
