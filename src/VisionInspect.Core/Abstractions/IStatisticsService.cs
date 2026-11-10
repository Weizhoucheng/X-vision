using VisionInspect.Core.Models;

namespace VisionInspect.Core.Abstractions
{
    public interface IStatisticsService
    {
        DetectionStatisticsSnapshot Snapshot { get; }
        void Record(bool isOk);
        void Reset();
    }
}
