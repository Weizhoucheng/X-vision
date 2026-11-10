using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Models;

namespace VisionInspect.Infrastructure.Storage
{
    /// <summary>
    /// 检测图片归档。按日期/OK|NG 分目录。
    /// 采用有界队列 + 单写线程，保证磁盘慢或网络盘抖动时不阻塞检测循环；
    /// 队列满时丢弃图片并记录错误，宁可不留图也不能拖死产线。
    /// </summary>
    public sealed class ImageArchiveService : IImageArchiveService, IDisposable
    {
        private sealed class WorkItem
        {
            public string Path;
            public RawImage Image;
            public DetectionRecord Record;
        }

        private readonly object _pendingSync = new object();
        private readonly BlockingCollection<WorkItem> _queue;
        private readonly Task _worker;
        private readonly string _root;
        private readonly string _format;
        private readonly bool _ngOnly;
        private readonly IAppLogger _logger;
        private readonly int _retentionDays;
        private DateTime _lastCleanupDate;
        private int _pending;
        private long _pendingBytes;
        private readonly long _maxPendingBytes;
        private TaskCompletionSource<bool> _flushSignal;
        private volatile bool _disposed;

        public ImageArchiveService(string rootDirectory, string format, bool ngOnly, int queueCapacity, IAppLogger logger,
            int retentionDays, long maxPendingBytes = 256L * 1024 * 1024, bool cleanupOnCreate = true)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("rootDirectory");
            _root = rootDirectory;
            Directory.CreateDirectory(_root);
            _format = NormalizeFormat(format);
            _ngOnly = ngOnly;
            _logger = logger;
            _retentionDays = Math.Max(1, retentionDays);
            _maxPendingBytes = Math.Max(1, maxPendingBytes);
            _lastCleanupDate = DateTime.Now.Date;
            _queue = new BlockingCollection<WorkItem>(Math.Max(10, queueCapacity));
            _worker = Task.Factory.StartNew(WorkerLoop, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            if (cleanupOnCreate) CleanupOldArchives(_retentionDays);
        }
        public void ApplyRetention() { CleanupOldArchives(_retentionDays); }

        public string BuildImagePath(DetectionRecord record)
        {
            if (record == null) throw new ArgumentNullException("record");

            DateTime timestamp = record.Timestamp == default(DateTime) ? DateTime.Now : record.Timestamp;
            string resultFolder = record.IsOk ? "OK" : "NG";
            string directory = Path.Combine(_root,
                timestamp.ToString("yyyy"), timestamp.ToString("MM"), timestamp.ToString("dd"), resultFolder);
            string fileName = string.Format("{0}_{1:D6}_{2}.{3}",
                timestamp.ToString("yyyyMMdd_HHmmss_fff"),
                record.Sequence,
                resultFolder,
                _format);
            return Path.Combine(directory, fileName);
        }

        public bool Enqueue(DetectionRecord record, RawImage image)
        {
            if (record == null || image == null) return false;
            if (_disposed) return false;
            if (_ngOnly && record.IsOk) return false;

            string path = string.IsNullOrWhiteSpace(record.ImagePath)
                ? BuildImagePath(record)
                : record.ImagePath;
            record.ImagePath = path;

            var item = new WorkItem { Path = path, Image = image, Record = record };
            lock (_pendingSync)
            {
                if (_disposed) return false;
                if (image.Data.LongLength > _maxPendingBytes - _pendingBytes)
                {
                    if (_logger != null)
                        _logger.Error("ImageArchive", "归档图像内存预算已达上限，拒绝序号 " + record.Sequence);
                    return false;
                }
                if (!_queue.TryAdd(item))
                {
                    if (_logger != null)
                        _logger.Error("ImageArchive",
                            string.Format("归档队列已满（容量 {0}），已丢弃序号 {1} 的图片。",
                                _queue.BoundedCapacity, record.Sequence));
                    return false;
                }

                _pending++;
                _pendingBytes += image.Data.LongLength;
                return true;
            }
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                if (_worker.IsCompleted && _pending > 0)
                {
                    if (_logger != null)
                        _logger.Error("ImageArchive", "归档写线程已结束，仍有图片未落盘。");
                    return;
                }

                Task waitTask;
                lock (_pendingSync)
                {
                    if (_pending <= 0) return;
                    if (_flushSignal == null || _flushSignal.Task.IsCompleted)
                        _flushSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    waitTask = _flushSignal.Task;
                }

                Task completed = await Task.WhenAny(waitTask, Task.Delay(1000, cancellationToken)).ConfigureAwait(false);
                if (completed == waitTask) continue;
                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();
            }
        }

        public void Dispose()
        {
            lock (_pendingSync)
            {
                if (_disposed) return;
                _disposed = true;
                _queue.CompleteAdding();
            }

            // 慢盘可能超过等待上限；队列必须由最后退出的写线程释放，不能边枚举边 Dispose。
            try { _worker.Wait(TimeSpan.FromSeconds(15)); } catch { }
            if (_worker.IsCompleted) _queue.Dispose();
            else
                _worker.ContinueWith(_ => _queue.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void WorkerLoop()
        {
            foreach (WorkItem item in _queue.GetConsumingEnumerable())
            {
                try
                {
                    if (_lastCleanupDate != DateTime.Now.Date)
                    {
                        CleanupOldArchives(_retentionDays);
                        _lastCleanupDate = DateTime.Now.Date;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(item.Path));
                    using (Bitmap bitmap = item.Image.ToBitmap())
                    {
                        bitmap.Save(item.Path, GetImageFormat());
                    }
                }
                catch (Exception ex)
                {
                    if (_logger != null)
                        _logger.Error("ImageArchive", "图片归档失败：" + item.Path, ex);
                }
                finally
                {
                    lock (_pendingSync)
                    {
                        _pending--;
                        _pendingBytes -= item.Image.Data.LongLength;
                        if (_pending <= 0 && _flushSignal != null)
                            _flushSignal.TrySetResult(true);
                    }
                }
            }
        }

        private ImageFormat GetImageFormat()
        {
            switch (_format)
            {
                case "bmp": return ImageFormat.Bmp;
                case "jpg": return ImageFormat.Jpeg;
                default: return ImageFormat.Png;
            }
        }

        private static string NormalizeFormat(string format)
        {
            string value = (format ?? "png").Trim().ToLowerInvariant();
            if (value != "png" && value != "bmp" && value != "jpg") return "png";
            return value;
        }

        private void CleanupOldArchives(int retentionDays)
        {
            try
            {
                DateTime cutoff = DateTime.Now.Date.AddDays(-retentionDays);
                foreach (string yearDir in Directory.EnumerateDirectories(_root))
                {
                    int year;
                    if (!int.TryParse(Path.GetFileName(yearDir), out year)) continue;

                    foreach (string monthDir in Directory.EnumerateDirectories(yearDir))
                    {
                        int month;
                        if (!int.TryParse(Path.GetFileName(monthDir), out month)) continue;

                        foreach (string dayDir in Directory.EnumerateDirectories(monthDir))
                        {
                            int day;
                            if (!int.TryParse(Path.GetFileName(dayDir), out day)) continue;

                            DateTime date;
                            try { date = new DateTime(year, month, day); }
                            catch { continue; }

                            if (date < cutoff) Directory.Delete(dayDir, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Warn("ImageArchive", "清理过期图片失败：" + ex.Message);
            }
        }
    }
}
