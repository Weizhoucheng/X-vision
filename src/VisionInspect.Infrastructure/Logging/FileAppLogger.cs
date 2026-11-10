using System;
using System.IO;
using System.Text;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Models;

namespace VisionInspect.Infrastructure.Logging
{
    /// <summary>
    /// 轻量级线程安全文件日志。按天自动切分，系统日志与审计日志分目录保存。
    /// 不依赖 log4net，减少现场部署的 DLL 依赖。
    /// </summary>
    public sealed class FileAppLogger : IAppLogger, IDisposable
    {
        private readonly object _sync = new object();
        private readonly string _systemDirectory;
        private readonly string _auditDirectory;
        private int _retentionDays;
        private volatile bool _debugEnabled;
        public void Configure(int retentionDays, bool debugEnabled)
        { lock (_sync) { _retentionDays = Math.Max(1, retentionDays); _debugEnabled = debugEnabled; } }

        private StreamWriter _systemWriter;
        private StreamWriter _auditWriter;
        private DateTime _systemDate;
        private DateTime _auditDate;
        private bool _disposed;

        public FileAppLogger(string systemDirectory, string auditDirectory, int retentionDays, bool debugEnabled)
        {
            _systemDirectory = systemDirectory;
            _auditDirectory = auditDirectory;
            _retentionDays = Math.Max(1, retentionDays);
            _debugEnabled = debugEnabled;
            Directory.CreateDirectory(_systemDirectory);
            Directory.CreateDirectory(_auditDirectory);
            CleanupOldFiles(_systemDirectory);
            CleanupOldFiles(_auditDirectory);
        }

        public event EventHandler<LogEntryEventArgs> EntryWritten;

        public void Debug(string area, string message)
        {
            if (_debugEnabled) Write(LogLevel.Debug, area, message, null, false);
        }

        public void Info(string area, string message)
        {
            Write(LogLevel.Info, area, message, null, false);
        }

        public void Warn(string area, string message)
        {
            Write(LogLevel.Warn, area, message, null, false);
        }

        public void Error(string area, string message, Exception exception = null)
        {
            Write(LogLevel.Error, area, message, exception, false);
        }

        public void Audit(string userName, string action, string detail)
        {
            string message = string.Format("user={0} | action={1} | detail={2}", userName ?? "-", action, detail ?? string.Empty);
            Write(LogLevel.Audit, "Audit", message, null, true);
        }

        public void Flush()
        {
            lock (_sync)
            {
                if (_systemWriter != null) _systemWriter.Flush();
                if (_auditWriter != null) _auditWriter.Flush();
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                if (_systemWriter != null)
                {
                    try { _systemWriter.Dispose(); } catch { }
                    _systemWriter = null;
                }
                if (_auditWriter != null)
                {
                    try { _auditWriter.Dispose(); } catch { }
                    _auditWriter = null;
                }
            }
        }

        private void Write(LogLevel level, string area, string message, Exception exception, bool audit)
        {
            string line = Format(level, area, message, exception);
            DateTime now = DateTime.Now;

            lock (_sync)
            {
                if (_disposed) return;
                try
                {
                    if (audit)
                    {
                        StreamWriter writer = GetAuditWriter(now);
                        writer.WriteLine(line);
                        writer.Flush();
                    }
                    else
                    {
                        StreamWriter writer = GetSystemWriter(now);
                        writer.WriteLine(line);
                        writer.Flush();
                    }
                }
                catch
                {
                    // 日志自身不能影响主业务；磁盘满/权限问题时直接放弃本条日志。
                }
            }

            EventHandler<LogEntryEventArgs> handler = EntryWritten;
            if (handler == null) return;
            try
            {
                handler(this, new LogEntryEventArgs(level, area, message, now));
            }
            catch
            {
                // UI 订阅者异常不能影响业务线程。
            }
        }

        private StreamWriter GetSystemWriter(DateTime now)
        {
            if (_systemWriter != null && _systemDate.Date == now.Date) return _systemWriter;

            if (_systemWriter != null)
            {
                try { _systemWriter.Dispose(); } catch { }
                _systemWriter = null;
            }

            _systemDate = now.Date;
            string path = Path.Combine(_systemDirectory, string.Format("system-{0:yyyy-MM-dd}.log", now));
            _systemWriter = NewWriter(path);
            CleanupOldFiles(_systemDirectory);
            return _systemWriter;
        }

        private StreamWriter GetAuditWriter(DateTime now)
        {
            if (_auditWriter != null && _auditDate.Date == now.Date) return _auditWriter;

            if (_auditWriter != null)
            {
                try { _auditWriter.Dispose(); } catch { }
                _auditWriter = null;
            }

            _auditDate = now.Date;
            string path = Path.Combine(_auditDirectory, string.Format("audit-{0:yyyy-MM-dd}.log", now));
            _auditWriter = NewWriter(path);
            CleanupOldFiles(_auditDirectory);
            return _auditWriter;
        }

        private static StreamWriter NewWriter(string path)
        {
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            return new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }

        private static string Format(LogLevel level, string area, string message, Exception exception)
        {
            var sb = new StringBuilder(256);
            sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
            sb.Append('[').Append(LevelText(level)).Append("] ");
            sb.Append('[').Append(string.IsNullOrWhiteSpace(area) ? "-" : area).Append("] ");
            sb.Append(message ?? string.Empty);

            if (exception != null)
            {
                sb.AppendLine();
                sb.Append(exception);
            }

            return sb.ToString();
        }

        private static string LevelText(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Info: return "INFO";
                case LogLevel.Warn: return "WARN";
                case LogLevel.Error: return "ERROR";
                case LogLevel.Audit: return "AUDIT";
                default: return "INFO";
            }
        }

        private void CleanupOldFiles(string directory)
        {
            try
            {
                DateTime cutoff = DateTime.Now.Date.AddDays(-_retentionDays);
                foreach (string file in Directory.EnumerateFiles(directory, "*.log"))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
            }
            catch
            {
                // 清理失败不影响日志功能。
            }
        }
    }
}
