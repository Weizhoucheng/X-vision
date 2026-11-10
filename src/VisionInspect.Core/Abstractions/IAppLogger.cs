using System;
using VisionInspect.Core.Models;

namespace VisionInspect.Core.Abstractions
{
    /// <summary>系统日志/审计日志写入接口。实现必须线程安全。</summary>
    public interface IAppLogger
    {
        event EventHandler<LogEntryEventArgs> EntryWritten;

        void Debug(string area, string message);
        void Info(string area, string message);
        void Warn(string area, string message);
        void Error(string area, string message, Exception exception = null);
        void Audit(string userName, string action, string detail);
        void Flush();
    }
}
