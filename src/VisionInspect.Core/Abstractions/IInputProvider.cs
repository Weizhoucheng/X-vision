using System.Threading;
using System.Threading.Tasks;

namespace VisionInspect.Core.Abstractions
{
    /// <summary>检测循环的图像输入源。实现需响应 CancellationToken，停止时尽快返回。</summary>
    public interface IInputProvider
    {
        string Name { get; }
        Task<RawImage> AcquireAsync(CancellationToken cancellationToken);
    }
}
