using System;

namespace VisionInspect.Core
{
    /// <summary>
    /// 业务层统一异常。UI 捕获该异常并同时写状态栏和弹窗，避免直接暴露 SDK 细节。
    /// </summary>
    public class VisionInspectException : Exception
    {
        public VisionInspectException(string message) : base(message) { }

        public VisionInspectException(string message, Exception innerException)
            : base(message, innerException) { }

        public VisionInspectException(int errorCode, string message, Exception innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }

        public int ErrorCode { get; protected set; }
    }

    /// <summary>VisionMaster SDK 调用异常，保留 16 进制错误码。</summary>
    public sealed class VisionSdkException : VisionInspectException
    {
        public VisionSdkException(int errorCode, string operation, Exception innerException = null)
            : base(errorCode,
                   string.Format("{0}失败。VM错误码：0x{1:X8}{2}",
                       operation,
                       errorCode,
                       VmAuthorizationErrors.IsAuthorizationError(errorCode) ? "；" + VmAuthorizationErrors.GetGuidance(errorCode) : innerException == null ? string.Empty : "；" + innerException.Message),
                   innerException)
        {
            Operation = operation;
        }

        public string Operation { get; private set; }
    }

    /// <summary>相机 SDK 调用异常。</summary>
    public sealed class CameraSdkException : VisionInspectException
    {
        public CameraSdkException(int errorCode, string operation, Exception innerException = null)
            : base(errorCode,
                   string.Format("{0}失败。MVS错误码：0x{1:X8}{2}",
                       operation,
                       errorCode,
                       innerException == null ? string.Empty : "；" + innerException.Message),
                   innerException)
        {
            Operation = operation;
        }

        public string Operation { get; private set; }
    }

    /// <summary>输入图片列表已经结束，用于顺序播放模式正常收尾。</summary>
    public sealed class EndOfSequenceException : Exception
    {
        public EndOfSequenceException() : base("本地图片列表已检测到末尾。") { }
    }

    /// <summary>检测被暂停/停止取消时使用的内部异常。</summary>
    public sealed class DetectionStoppedException : Exception
    {
        public DetectionStoppedException(string message = "检测已停止。") : base(message) { }
    }
}
