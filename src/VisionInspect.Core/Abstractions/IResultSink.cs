using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core.Models;

namespace VisionInspect.Core.Abstractions
{
    /// <summary>检测结果后处理管道：日志、图片归档、通讯输出。实现必须自行吞掉非致命异常。</summary>
    public interface IResultSink
    {
        Task HandleAsync(DetectionContext context, CancellationToken cancellationToken);
    }
}
