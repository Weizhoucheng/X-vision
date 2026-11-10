using System;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Models;

namespace VisionInspect.Core.Services
{
    /// <summary>检测统计。使用独立锁，保证 UI 读取快照与后台写入不撕裂。</summary>
    public sealed class StatisticsService : IStatisticsService
    {
        private readonly object _sync = new object();
        private long _total;
        private long _ok;
        private long _ng;
        private DateTime _startedAt;

        public StatisticsService()
        {
            _startedAt = DateTime.Now;
        }

        public DetectionStatisticsSnapshot Snapshot
        {
            get
            {
                lock (_sync)
                {
                    return new DetectionStatisticsSnapshot
                    {
                        Total = _total,
                        Ok = _ok,
                        Ng = _ng,
                        StartedAt = _startedAt
                    };
                }
            }
        }

        public void Record(bool isOk)
        {
            lock (_sync)
            {
                _total++;
                if (isOk) _ok++;
                else _ng++;
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _total = 0;
                _ok = 0;
                _ng = 0;
                _startedAt = DateTime.Now;
            }
        }
    }
}
