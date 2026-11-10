using System;
using System.Threading.Tasks;
using VisionInspect.Core.Models;

namespace VisionInspect.Core.Abstractions
{
    public interface IDetectionController
    {
        event EventHandler<DetectionStateChangedEventArgs> StateChanged;
        event EventHandler<DetectionCompletedEventArgs> DetectionCompleted;
        event EventHandler<DetectionErrorEventArgs> ErrorOccurred;
        event EventHandler<DetectionNoticeEventArgs> Notice;

        DetectionRunState State { get; }
        DetectionMode Mode { get; }
        string LastMessage { get; }

        Task StartAsync(DetectionMode mode, IInputProvider inputProvider);
        Task PauseAsync();
        Task ResumeAsync();
        Task StopAsync();
        Task ShutdownAsync();
    }
}
