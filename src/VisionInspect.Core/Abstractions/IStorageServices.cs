using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core.Models;

namespace VisionInspect.Core.Abstractions
{
    /// <summary>检测日志仓储，按天落盘 CSV，并可按时间段读取用于报表。</summary>
    public interface IDetectionLogRepository
    {
        void Append(DetectionRecord record);
        IReadOnlyList<DetectionRecord> Query(DateTime fromInclusive, DateTime toInclusive);
    }

    /// <summary>图片归档服务，使用有界队列，避免磁盘抖动阻塞检测。</summary>
    public interface IImageArchiveService
    {
        string BuildImagePath(DetectionRecord record);
        bool Enqueue(DetectionRecord record, RawImage image);
        Task FlushAsync(CancellationToken cancellationToken);
    }

    /// <summary>Excel 报表导出服务。</summary>
    public interface IReportService
    {
        Task<string> ExportAsync(DateTime fromInclusive, DateTime toInclusive, string outputFile,
            CancellationToken cancellationToken);
    }

    /// <summary>Windows 开机自启配置。</summary>
    public interface IStartupService
    {
        bool IsEnabled { get; }
        void SetEnabled(bool enabled);
    }
}
