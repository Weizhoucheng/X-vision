using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Models;

namespace VisionInspect.Infrastructure.Logging
{
    /// <summary>
    /// 每日检测日志（CSV，UTF-8 BOM，Excel 可直接打开）。
    /// 文件名：detection-YYYY-MM-DD.csv。写入串行化，读取用于报表。
    /// </summary>
    public sealed class DetectionLogRepository : IDetectionLogRepository
    {
        private const string Header =
            "Sequence,Timestamp,Result,Solution,Procedure,DurationMs,InputSource,ImagePath,JudgeText,OutputSummary,Error";

        private readonly object _sync = new object();
        private readonly string _directory;
        private readonly IAppLogger _logger;

        public DetectionLogRepository(string directory, IAppLogger logger)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("directory");
            _directory = directory;
            _logger = logger;
            Directory.CreateDirectory(_directory);
        }

        public void Append(DetectionRecord record)
        {
            if (record == null) throw new ArgumentNullException("record");
            string path = GetFilePath(record.Timestamp);
            string line = CsvCodec.BuildLine(
                record.Sequence.ToString(CultureInfo.InvariantCulture),
                record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                record.ResultText,
                record.Solution,
                record.Procedure,
                record.DurationMs.ToString("F3", CultureInfo.InvariantCulture),
                record.InputSource,
                record.ImagePath,
                record.JudgeText,
                record.OutputSummary,
                record.Error);

            lock (_sync)
            {
                bool newFile = !File.Exists(path) || new FileInfo(path).Length == 0;
                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream,
                    new UTF8Encoding(newFile), 4096))
                {
                    if (newFile) writer.WriteLine(Header);
                    writer.WriteLine(line);
                }
            }
        }

        public IReadOnlyList<DetectionRecord> Query(DateTime fromInclusive, DateTime toInclusive)
        {
            var records = new List<DetectionRecord>();
            if (toInclusive < fromInclusive)
                return records;

            // 枚举实际存在的日志，避免静默丢弃 366 天以前的数据，以及 DateTime.MaxValue 的日期递增溢出。
            foreach (string path in Directory.EnumerateFiles(_directory, "detection-*.csv"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                DateTime date;
                if (!DateTime.TryParseExact(name.Substring("detection-".Length), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) continue;
                if (date < fromInclusive.Date || date > toInclusive.Date) continue;

                try
                {
                    // 报表读取允许检测线程继续追加同一天文件，避免默认 FileShare.Read 阻断产线日志。
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            if (line.StartsWith("Sequence,", StringComparison.Ordinal)) continue;

                            List<string> fields = CsvCodec.ParseLine(line);
                            if (fields.Count < 11) continue;

                            DateTime timestamp;
                            if (!DateTime.TryParseExact(fields[1], "yyyy-MM-dd HH:mm:ss.fff",
                                    CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp))
                                continue;

                            if (timestamp < fromInclusive || timestamp > toInclusive) continue;

                            double duration;
                            double.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
                            long sequence;
                            long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence);

                            records.Add(new DetectionRecord
                            {
                                Sequence = sequence,
                                Timestamp = timestamp,
                                IsOk = string.Equals(fields[2], "OK", StringComparison.OrdinalIgnoreCase),
                                Solution = fields[3],
                                Procedure = fields[4],
                                DurationMs = duration,
                                InputSource = fields[6],
                                ImagePath = fields[7],
                                JudgeText = fields[8],
                                OutputSummary = fields[9],
                                Error = fields[10]
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (_logger != null)
                        _logger.Error("DetectionLog", "读取检测日志失败：" + path, ex);
                }
            }

            records.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return records;
        }

        private string GetFilePath(DateTime timestamp)
        {
            return Path.Combine(_directory,
                string.Format("detection-{0:yyyy-MM-dd}.csv", timestamp));
        }
    }
}
