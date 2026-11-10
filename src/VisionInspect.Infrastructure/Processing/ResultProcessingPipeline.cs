using System;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;

namespace VisionInspect.Infrastructure.Processing
{
    /// <summary>
    /// 检测结果后处理顺序：
    /// 1) 计划图片归档路径并写入记录；
    /// 2) 图片进入有界归档队列（确认接收后才把路径写进日志）；
    /// 3) 追加每日检测日志；
    /// 4) 向 PLC/IO 板卡发送结果（失败不影响检测结论）。
    ///
    /// 后处理阶段使用 CancellationToken.None：已经完成的检测必须留下可追溯记录，
    /// 不能因为操作员点“停止”而把结果从日志/PLC 中吞掉。
    /// </summary>
    public sealed class ResultProcessingPipeline : IResultSink
    {
        private IDetectionLogRepository _detectionLog;
        private IImageArchiveService _archive;
        private readonly ICommunicationHub _communication;
        private readonly IAppLogger _logger;
        private readonly StorageSettings _storage;
        /// <summary>调用方必须先停止并等待检测及其后处理结束。</summary>
        public void ReconfigureStorage(IDetectionLogRepository repository, IImageArchiveService archive)
        { _detectionLog = repository; _archive = archive; }

        public ResultProcessingPipeline(
            IDetectionLogRepository detectionLog,
            IImageArchiveService archive,
            ICommunicationHub communication,
            IAppLogger logger,
            StorageSettings storage)
        {
            _detectionLog = detectionLog;
            _archive = archive;
            _communication = communication;
            _logger = logger;
            _storage = storage ?? new StorageSettings();
        }

        public async Task HandleAsync(DetectionContext context, CancellationToken cancellationToken)
        {
            if (context == null || context.Record == null) return;

            DetectionRecord record = context.Record;
            record.Error = context.Inspection == null ? string.Empty : context.Inspection.ErrorMessage ?? string.Empty;

            bool archiveRequested = _storage.EnableImageArchive && _archive != null && context.Image != null &&
                !(_storage.ArchiveNgOnly && record.IsOk);
            if (archiveRequested)
            {
                try
                {
                    record.ImagePath = _archive.BuildImagePath(record);
                    if (!_archive.Enqueue(record, context.Image))
                    {
                        record.ImagePath = string.Empty;
                        record.Error = AppendError(record.Error, "图片未进入归档队列");
                    }
                }
                catch (Exception ex)
                {
                    if (_logger != null) _logger.Error("ResultPipeline", "计算图片归档路径失败。", ex);
                    record.ImagePath = string.Empty;
                    record.Error = AppendError(record.Error, "图片归档排队失败：" + ex.Message);
                }
            }

            if (_storage.EnableDetectionLog && _detectionLog != null)
            {
                try
                {
                    await Task.Run(() => _detectionLog.Append(record), CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (_logger != null) _logger.Error("ResultPipeline", "写入检测日志失败。", ex);
                }
            }

            if (_communication != null)
            {
                try
                {
                    await _communication.PublishResultAsync(context, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (_logger != null) _logger.Error("ResultPipeline", "通讯结果输出失败。", ex);
                }
            }
        }

        private static string AppendError(string original, string message)
        {
            return string.IsNullOrEmpty(original) ? message : original + "; " + message;
        }
    }
}
