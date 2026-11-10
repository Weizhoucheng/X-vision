using System;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspect.Core.Utilities
{
    /// <summary>
    /// 可异步等待、支持取消的手动重置事件，用于检测循环的暂停/继续。
    /// 不使用 Thread.Sleep，避免占用线程池线程。
    /// </summary>
    public sealed class AsyncManualResetEvent
    {
        private TaskCompletionSource<bool> _tcs =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsSet
        {
            get { return _tcs.Task.IsCompleted; }
        }

        public void Set()
        {
            _tcs.TrySetResult(true);
        }

        public void Reset()
        {
            while (true)
            {
                TaskCompletionSource<bool> current = _tcs;
                if (!current.Task.IsCompleted) return;

                var next = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (Interlocked.CompareExchange(ref _tcs, next, current) == current)
                    return;
            }
        }

        public Task WaitAsync()
        {
            return _tcs.Task;
        }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                await _tcs.Task.ConfigureAwait(false);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            TaskCompletionSource<bool> source = _tcs;
            if (source.Task.IsCompleted)
                return;

            var cancellationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancellationTcs.TrySetCanceled()))
            {
                // 必须在 await 完成后才释放注册，否则暂停期间 Stop 无法唤醒等待。
                await WaitCoreAsync(source.Task, cancellationTcs.Task).ConfigureAwait(false);
            }
        }

        private static async Task WaitCoreAsync(Task gate, Task cancellation)
        {
            Task completed = await Task.WhenAny(gate, cancellation).ConfigureAwait(false);
            if (completed == cancellation)
                throw new OperationCanceledException("等待被取消。");
            await gate.ConfigureAwait(false);
        }
    }
}
