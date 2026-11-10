using System;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core.Models;

namespace VisionInspect.Core.Abstractions
{
    /// <summary>通讯 IO 聚合接口。单个通道失败不得影响检测主流程。</summary>
    public interface ICommunicationHub
    {
        event EventHandler<CommunicationStatusEventArgs> StatusChanged;
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync();
        Task PublishResultAsync(DetectionContext context, CancellationToken cancellationToken);
    }

    /// <summary>外部触发源（串口/网口），触发事件可能来自后台线程。</summary>
    public interface ITriggerSource
    {
        event EventHandler<TriggerEventArgs> Triggered;
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync();
    }
}
