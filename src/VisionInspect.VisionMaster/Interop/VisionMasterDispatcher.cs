using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VisionInspect.VisionMaster.Interop
{
    /// <summary>
    /// VisionMaster SDK 专用串行调度器。
    ///
    /// 使用一个带 Windows 消息泵的 STA 工作线程执行所有 VM 调用：
    /// - 满足 VM 底层组件的线程/COM 模型；
    /// - SyncRun 等阻塞调用离开 UI 线程；
    /// - 方案加载、执行、卸载、参数读取全部串行，避免原生层并发崩溃。
    ///
    /// 取消只对“尚未执行”的任务生效；已经进入 SyncRun 的任务无法安全中止。
    /// </summary>
    public sealed class VisionMasterDispatcher : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private readonly Thread _thread;
        private Control _control;
        private Exception _startupError;
        private int _disposed;

        public VisionMasterDispatcher()
        {
            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "VisionMaster.SDK.STA"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            if (!_ready.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("VisionMaster SDK 工作线程启动超时。");
            if (_startupError != null)
                throw new InvalidOperationException("VisionMaster SDK 工作线程启动失败。", _startupError);
        }

        public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken)
        {
            if (action == null) throw new ArgumentNullException("action");
            if (Volatile.Read(ref _disposed) != 0)
                return Task.FromException<T>(new ObjectDisposedException("VisionMasterDispatcher"));

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<T>(cancellationToken);

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _control.BeginInvoke(new Action(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetCanceled();
                        return;
                    }

                    try { completion.TrySetResult(action()); }
                    catch (Exception ex) { completion.TrySetException(ex); }
                }));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }

            return completion.Task;
        }

        public Task InvokeAsync(Action action, CancellationToken cancellationToken)
        {
            if (action == null) throw new ArgumentNullException("action");
            return InvokeAsync<object>(() =>
            {
                action();
                return null;
            }, cancellationToken);
        }

        private void ThreadMain()
        {
            try
            {
                WindowsFormsSynchronizationContext.AutoInstall = true;
                _control = new Control();
                IntPtr handle = _control.Handle; // 强制创建消息队列句柄。
                if (handle == IntPtr.Zero)
                    throw new InvalidOperationException("无法创建 VM 调度消息窗口。");

                _ready.Set();
                Application.Run();
            }
            catch (Exception ex)
            {
                _startupError = ex;
                _ready.Set();
            }
            finally
            {
                try
                {
                    if (_control != null && !_control.IsDisposed)
                        _control.Dispose();
                }
                catch { }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try
            {
                if (Thread.CurrentThread != _thread && _control != null)
                {
                    _control.BeginInvoke(new Action(() => Application.ExitThread()));
                    _thread.Join(TimeSpan.FromSeconds(8));
                }
            }
            catch
            {
                // 消息循环可能已经退出；后台线程由进程回收。
            }
            finally
            {
                try { _ready.Dispose(); } catch { }
            }
        }
    }
}
