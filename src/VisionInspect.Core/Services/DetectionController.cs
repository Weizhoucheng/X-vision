using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Algorithms;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;
using VisionInspect.Core.Utilities;

namespace VisionInspect.Core.Services
{
    /// <summary>
    /// 检测状态机与异步循环。
    ///
    /// 设计要点：
    /// - 所有等待均为 async/await，不在 UI 线程阻塞；
    /// - 启动/暂停/继续/停止通过一个状态锁串行化，防止按钮连点造成并发检测；
    /// - 单次检测会自行结束；连续检测按配置间隔循环；
    /// - 单次循环异常不立即杀死程序，连续异常达到阈值才进入 Fault；
    /// - 结果后处理失败只记录日志，不改变已经产生的检测结论。
    /// </summary>
    public sealed class DetectionController : IDetectionController
    {
        private IVisionEngine _vision;
        private IAlgorithmEngine _algorithm;
        private readonly IResultSink _resultSink;
        private readonly IStatisticsService _statistics;
        private readonly IAppLogger _logger;
        private readonly DetectionSettings _detectionSettings;
        private readonly VisionSettings _visionSettings;
        private readonly ResultJudgeOptions _judgeOptions;

        private readonly object _sync = new object();
        private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);
        private readonly AsyncManualResetEvent _pauseGate = new AsyncManualResetEvent();

        private DetectionRunState _state = DetectionRunState.Idle;
        private DetectionMode _mode = DetectionMode.Single;
        private string _lastMessage = "空闲";
        private IInputProvider _inputProvider;
        private CancellationTokenSource _runCts;
        private Task _loopTask;
        private long _sequence;
        private bool _shutdownRequested;

        /// <summary>兼容既有 VM 界面/回归测试的构造入口。</summary>
        public DetectionController(
            IVisionEngine vision,
            IResultSink resultSink,
            IStatisticsService statistics,
            IAppLogger logger,
            DetectionSettings detectionSettings,
            VisionSettings visionSettings,
            ResultJudgeOptions judgeOptions)
        {
            if (vision == null) throw new ArgumentNullException("vision");
            if (resultSink == null) throw new ArgumentNullException("resultSink");
            if (statistics == null) throw new ArgumentNullException("statistics");
            if (logger == null) throw new ArgumentNullException("logger");

            _vision = vision;
            _resultSink = resultSink;
            _statistics = statistics;
            _logger = logger;
            _detectionSettings = detectionSettings ?? new DetectionSettings();
            _visionSettings = visionSettings ?? new VisionSettings();
            _judgeOptions = judgeOptions ?? new ResultJudgeOptions();
            _pauseGate.Set();
        }

        /// <summary>
        /// 面向接口的构造入口。VM/Halcon 都由组合根注入，检测控制器不依赖具体 SDK。
        /// </summary>
        public DetectionController(
            IAlgorithmEngine algorithm,
            IResultSink resultSink,
            IStatisticsService statistics,
            IAppLogger logger,
            DetectionSettings detectionSettings,
            VisionSettings visionSettings,
            ResultJudgeOptions judgeOptions)
        {
            if (algorithm == null) throw new ArgumentNullException("algorithm");
            if (resultSink == null) throw new ArgumentNullException("resultSink");
            if (statistics == null) throw new ArgumentNullException("statistics");
            if (logger == null) throw new ArgumentNullException("logger");

            _algorithm = algorithm;
            _resultSink = resultSink;
            _statistics = statistics;
            _logger = logger;
            _detectionSettings = detectionSettings ?? new DetectionSettings();
            _visionSettings = visionSettings ?? new VisionSettings();
            _judgeOptions = judgeOptions ?? new ResultJudgeOptions();
            _pauseGate.Set();
        }

        /// <summary>
        /// 切换到另一个算法引擎。只能在空闲/异常状态调用，调用方负责加载/释放旧引擎。
        /// </summary>
        public void SwitchAlgorithm(IAlgorithmEngine engine)
        {
            if (engine == null) throw new ArgumentNullException("engine");
            lock (_sync)
            {
                if (_state == DetectionRunState.Running || _state == DetectionRunState.Paused ||
                    _state == DetectionRunState.Stopping)
                    throw new VisionInspectException("检测运行中不能切换算法引擎。");
                _algorithm = engine;
                _vision = null;
            }
        }

        public event EventHandler<DetectionStateChangedEventArgs> StateChanged;
        public event EventHandler<DetectionCompletedEventArgs> DetectionCompleted;
        public event EventHandler<DetectionErrorEventArgs> ErrorOccurred;
        public event EventHandler<DetectionNoticeEventArgs> Notice;

        public DetectionRunState State
        {
            get { lock (_sync) return _state; }
        }

        public DetectionMode Mode
        {
            get { lock (_sync) return _mode; }
        }

        public string LastMessage
        {
            get { lock (_sync) return _lastMessage; }
        }

        public async Task StartAsync(DetectionMode mode, IInputProvider inputProvider)
        {
            if (inputProvider == null) throw new ArgumentNullException("inputProvider");
            if (!IsEngineLoaded())
                throw new VisionInspectException("尚未加载视觉方案，不能启动检测。");

            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (_sync)
                {
                    if (_shutdownRequested)
                        throw new VisionInspectException("检测控制器正在退出，不能重新启动。");
                    if (_loopTask != null && !_loopTask.IsCompleted)
                        throw new VisionInspectException("检测循环仍在运行，请先停止。");
                    if (_state == DetectionRunState.Running || _state == DetectionRunState.Paused ||
                        _state == DetectionRunState.Stopping)
                        throw new VisionInspectException("当前状态不允许启动检测。");
                }

                var cts = new CancellationTokenSource();
                lock (_sync)
                {
                    _inputProvider = inputProvider;
                    _mode = mode;
                    _runCts = cts;
                    _pauseGate.Set();
                }

                SetState(DetectionRunState.Running,
                    mode == DetectionMode.Single ? "开始单次检测" : "开始连续检测");

                // 使用 Task.Run 把整个视觉执行链从 UI 线程移走。
                _loopTask = Task.Run(() => RunLoopAsync(mode, inputProvider, cts.Token));
                _logger.Info("Detection", string.Format("启动检测：模式={0}，输入={1}", mode, inputProvider.Name));
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task PauseAsync()
        {
            if (TrySetState(DetectionRunState.Running, DetectionRunState.Paused,
                "暂停请求已接收，等待当前检测完成后挂起", () => _pauseGate.Reset()))
            {
                _logger.Info("Detection", "检测暂停请求。");
                await Task.CompletedTask.ConfigureAwait(false);
            }
        }

        public async Task ResumeAsync()
        {
            if (!TrySetState(DetectionRunState.Paused, DetectionRunState.Running,
                "继续检测", () => _pauseGate.Set())) return;
            _logger.Info("Detection", "检测继续。");
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public async Task StopAsync()
        {
            Task loop;
            CancellationTokenSource cts;
            int timeoutMs;

            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (_sync)
                {
                    if (_state == DetectionRunState.Idle && (_loopTask == null || _loopTask.IsCompleted))
                    {
                        _pauseGate.Set();
                        return;
                    }

                    loop = _loopTask;
                    cts = _runCts;
                    timeoutMs = _detectionSettings.StopTimeoutMs;
                }

                SetState(DetectionRunState.Stopping, "正在停止检测，等待当前视觉任务退出");
                try
                {
                    if (cts != null) cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // 已经释放，说明循环已结束，忽略。
                }

                _pauseGate.Set();
                _logger.Info("Detection", "检测停止请求。");

                if (loop != null)
                {
                    Task completed = await Task.WhenAny(loop, Task.Delay(timeoutMs)).ConfigureAwait(false);
                    if (completed != loop)
                    {
                        _logger.Error("Detection",
                            string.Format("停止检测超时（{0} ms）。VisionMaster 同步执行可能仍未返回，禁止再次启动。", timeoutMs));
                        SetState(DetectionRunState.Fault,
                            "停止超时：视觉线程仍在执行，请等待其结束或重启软件");
                        // 调用方必须保留窗体和 SDK，不能越过仍在运行的检测继续释放资源。
                        throw new TimeoutException(string.Format("停止检测超时（{0} ms），任务尚未退出。", timeoutMs));
                    }
                    await loop.ConfigureAwait(false);
                }

                SetState(DetectionRunState.Idle, "已停止");
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task ShutdownAsync()
        {
            lock (_sync) _shutdownRequested = true;
            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error("Detection", "退出时停止检测异常。", ex);
                throw;
            }
        }

        private async Task RunLoopAsync(DetectionMode mode, IInputProvider inputProvider, CancellationToken cancellationToken)
        {
            bool fatal = false;
            int consecutiveErrors = 0;
            long sequence = 0;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await _pauseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        RawImage image = await inputProvider.AcquireAsync(cancellationToken).ConfigureAwait(false);
                        if (image == null)
                            throw new InvalidOperationException("输入源未返回图像。");

                        string procedureName;
                        VisionInspectionResult inspection;
                        if (_algorithm != null)
                        {
                            // 统一算法入口：图像转换为 UniversalImageData，结果转换为既有
                            // VisionInspectionResult 后继续走原来的日志/归档/通讯管道。
                            using (UniversalImageData universal = UniversalImageData.FromRawImage(image))
                            {
                                await _algorithm.SetInputImageAsync(universal, cancellationToken)
                                    .ConfigureAwait(false);
                                DetectResult detect = await _algorithm.RunAsync(cancellationToken)
                                    .ConfigureAwait(false);
                                if (detect == null) throw new InvalidOperationException("算法引擎未返回检测结果。");
                                procedureName = string.IsNullOrWhiteSpace(detect.ProcedureName)
                                    ? _visionSettings.PrimaryProcedureName : detect.ProcedureName;
                                inspection = AlgorithmResultConverter.ToVisionInspectionResult(
                                    detect, _algorithm.LoadedSchemePath, procedureName);
                            }
                        }
                        else
                        {
                            procedureName = ResolveProcedureName();
                            inspection = await _vision.RunOnceAsync(
                                image, procedureName, _judgeOptions, cancellationToken).ConfigureAwait(false);
                        }

                        // SDK 已经产生结果后，必须完成日志/归档/PLC；停止只影响下一轮取图。
                        if (inspection == null) throw new InvalidOperationException("视觉引擎未返回检测结果。");
                        sequence = Interlocked.Increment(ref _sequence);
                        var record = new DetectionRecord
                        {
                            Sequence = sequence,
                            Timestamp = inspection.Timestamp == default(DateTime) ? DateTime.Now : inspection.Timestamp,
                            IsOk = inspection.IsOk,
                            Solution = Path.GetFileName(LoadedSchemePath() ?? string.Empty),
                            Procedure = procedureName,
                            DurationMs = inspection.DurationMs,
                            InputSource = inputProvider.Name,
                            JudgeText = inspection.JudgeText,
                            OutputSummary = inspection.OutputSummary
                        };

                        _statistics.Record(inspection.IsOk);

                        var context = new DetectionContext
                        {
                            Sequence = sequence,
                            Timestamp = record.Timestamp,
                            SolutionPath = LoadedSchemePath(),
                            InputSource = inputProvider.Name,
                            Image = image,
                            Inspection = inspection,
                            Record = record
                        };

                        // 先通知 UI，再落盘/通讯，保证结果展示不被慢速 PLC/磁盘拖住。
                        RaiseDetectionCompleted(context);

                        try
                        {
                            // 检测结果一旦产生就必须留痕/输出，不能因为操作员随后点停止而丢失。
                            await _resultSink.HandleAsync(context, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception sinkEx)
                        {
                            _logger.Error("ResultPipeline",
                                string.Format("第 {0} 次检测结果后处理失败，检测结论保留。", sequence), sinkEx);
                        }

                        consecutiveErrors = 0;

                        if (mode == DetectionMode.Single)
                            break;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (EndOfSequenceException)
                    {
                        RaiseNotice("本地图片列表已到末尾，检测正常结束。");
                        break;
                    }
                    catch (Exception ex)
                    {
                        consecutiveErrors++;
                        _logger.Error("Detection",
                            string.Format("检测循环异常（连续第 {0} 次）。", consecutiveErrors), ex);
                        RaiseError(ex.Message, ex, consecutiveErrors);

                        if (mode == DetectionMode.Single || consecutiveErrors >= _detectionSettings.MaxConsecutiveErrors)
                        {
                            fatal = true;
                            SetState(DetectionRunState.Fault,
                                string.Format("连续异常 {0} 次，检测已停止：{1}", consecutiveErrors, ex.Message));
                            break;
                        }

                        TrySetState(DetectionRunState.Running, DetectionRunState.Running,
                            string.Format("检测异常，{0} ms 后自动恢复：{1}",
                                _detectionSettings.ErrorRecoveryDelayMs, ex.Message), null);

                        await Task.Delay(_detectionSettings.ErrorRecoveryDelayMs, cancellationToken)
                            .ConfigureAwait(false);
                        continue;
                    }

                    if (_detectionSettings.IntervalMs > 0)
                    {
                        await Task.Delay(_detectionSettings.IntervalMs, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常停止路径。
            }
            catch (Exception ex)
            {
                fatal = true;
                _logger.Error("Detection", "检测循环发生未处理异常。", ex);
                RaiseError(ex.Message, ex, consecutiveErrors);
                SetState(DetectionRunState.Fault, "检测循环异常：" + ex.Message);
            }
            finally
            {
                if (!fatal)
                {
                    SetState(DetectionRunState.Idle,
                        _state == DetectionRunState.Fault ? "视觉线程已结束" : "检测结束");
                }

                lock (_sync)
                {
                    _pauseGate.Set();
                    CancellationTokenSource finishedCts = _runCts;
                    _runCts = null;
                    if (finishedCts != null)
                    {
                        try { finishedCts.Dispose(); } catch { }
                    }
                }
            }
        }

        private bool IsEngineLoaded()
        {
            return _algorithm != null ? _algorithm.IsLoaded : _vision != null && _vision.IsLoaded;
        }

        private string LoadedSchemePath()
        {
            return _algorithm != null ? _algorithm.LoadedSchemePath :
                (_vision == null ? string.Empty : _vision.LoadedSolutionPath);
        }

        private string ResolveProcedureName()
        {
            if (!string.IsNullOrWhiteSpace(_visionSettings.PrimaryProcedureName))
                return _visionSettings.PrimaryProcedureName;

            VisionSolutionInfo solution = _vision.CurrentSolution;
            if (solution == null)
                throw new VisionInspectException("方案元数据为空，无法确定检测流程。");

            if (!string.IsNullOrWhiteSpace(solution.PrimaryProcedureName))
                return solution.PrimaryProcedureName;

            if (solution.Procedures != null && solution.Procedures.Count > 0)
                return solution.Procedures[0].Name;

            throw new VisionInspectException("方案内没有可用流程。");
        }

        private void RaiseDetectionCompleted(DetectionContext context)
        {
            EventHandler<DetectionCompletedEventArgs> handler = DetectionCompleted;
            if (handler == null) return;
            try
            {
                handler(this, new DetectionCompletedEventArgs(context));
            }
            catch (Exception ex)
            {
                _logger.Error("UI", "DetectionCompleted 事件处理异常。", ex);
            }
        }

        private void RaiseError(string message, Exception ex, int consecutive)
        {
            EventHandler<DetectionErrorEventArgs> handler = ErrorOccurred;
            if (handler == null) return;
            try
            {
                handler(this, new DetectionErrorEventArgs(message, ex, consecutive));
            }
            catch (Exception eventEx)
            {
                _logger.Error("UI", "ErrorOccurred 事件处理异常。", eventEx);
            }
        }

        private void RaiseNotice(string message)
        {
            EventHandler<DetectionNoticeEventArgs> handler = Notice;
            if (handler != null)
            {
                try
                {
                    handler(this, new DetectionNoticeEventArgs(message));
                }
                catch (Exception ex)
                {
                    _logger.Error("UI", "Notice 事件处理异常。", ex);
                }
            }
        }

        private void SetState(DetectionRunState newState, string message)
        {
            DetectionRunState oldState;
            lock (_sync)
            {
                oldState = _state;
                _state = newState;
                _lastMessage = message ?? string.Empty;
            }

            RaiseStateChanged(oldState, newState, message);
        }

        /// <summary>条件状态迁移：暂停、继续和错误恢复不能覆盖已经完成或停止的状态。</summary>
        private bool TrySetState(DetectionRunState expected, DetectionRunState newState,
            string message, Action transition)
        {
            lock (_sync)
            {
                if (_state != expected) return false;
                if (transition != null) transition();
                _state = newState;
                _lastMessage = message ?? string.Empty;
            }
            RaiseStateChanged(expected, newState, message);
            return true;
        }

        private void RaiseStateChanged(DetectionRunState oldState, DetectionRunState newState, string message)
        {
            if (oldState != newState || !string.IsNullOrEmpty(message))
                _logger.Debug("Detection", string.Format("状态：{0} -> {1}；{2}", oldState, newState, message));

            EventHandler<DetectionStateChangedEventArgs> handler = StateChanged;
            if (handler == null) return;

            try
            {
                handler(this, new DetectionStateChangedEventArgs(oldState, newState, message));
            }
            catch (Exception ex)
            {
                _logger.Error("UI", "StateChanged 事件处理异常。", ex);
            }
        }
    }
}
