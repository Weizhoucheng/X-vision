using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FormatModuleCs;
using ImageSourceModuleCs;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;
using VisionInspect.Core.Services;
using VisionInspect.VisionMaster.Interop;
using VisionInspect.VisionMaster.Parameters;
using VM.Core;
using VM.PlatformSDKCS;

namespace VisionInspect.VisionMaster
{
    /// <summary>
    /// VisionMaster 4.3.0 适配器。
    ///
    /// 关键实现约束：
    /// - 只通过 VisionMasterDispatcher 的单线程调用 VmSolution/VmProcedure；
    /// - 图像源使用 ImageSourceModuleTool.ModuParams.ImageSourceType = SDK，
    ///   每次执行前 SetImageData，执行后立即释放 ImageBaseData；
    /// - 流程执行使用 Run + 对应流程完成回调，等待期间保持 STA 消息泵可用；
    /// - 输出统一解析成 VisionOutputValue；格式化模块输出额外从 FormatModuleTool.ModuResult.format_out 读取；
    /// - 方案切换先 CloseSolution 再 Load，退出时最后 Dispose VmSolution 单例。
    /// </summary>
    public sealed class VisionMasterEngine : IVisionEngine
    {
        private sealed class ModuleEntry
        {
            public VisionModuleInfo Info;
            public object Module;
            public VmModule VmModule;
        }

        private readonly object _dataSync = new object();
        private readonly VisionMasterDispatcher _dispatcher;
        private readonly IAppLogger _logger;
        private readonly VisionSettings _visionSettings;
        private string _parameterRoot;
        private bool _sdkReady;
        public void SetParameterDirectory(string directory)
        { Directory.CreateDirectory(directory); _parameterRoot = directory; }

        private readonly Dictionary<string, VmProcedure> _procedureObjects =
            new Dictionary<string, VmProcedure>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, EventHandler> _procedureEndHandlers =
            new Dictionary<string, EventHandler>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ModuleEntry> _modules =
            new Dictionary<string, ModuleEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FormatModuleTool>> _formatModules =
            new Dictionary<string, List<FormatModuleTool>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ImageSourceModuleTool> _imageSources =
            new Dictionary<string, ImageSourceModuleTool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ParameterSnapshot> _defaults =
            new Dictionary<string, ParameterSnapshot>(StringComparer.OrdinalIgnoreCase);

        private VisionSolutionInfo _solution;
        private string _loadedSolutionPath;
        private readonly SemaphoreSlim _runGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _shutdownGate = new SemaphoreSlim(1, 1);
        private readonly object _runSync = new object();
        private TaskCompletionSource<bool> _activeRunCompletion;
        private TaskCompletionSource<bool> _activeRunDrained;
        private string _activeProcedureName;

        private volatile bool _loaded;
        private volatile bool _shutdown;

        /// <summary>一次异步流程执行的上下文。由 VM 回调完成，随后回到调度线程解析结果。</summary>
        private sealed class RunContext
        {
            public VmProcedure Procedure;
            public string ProcedureName;
            public ImageBaseData Image;
            public readonly Stopwatch Stopwatch = Stopwatch.StartNew();
            public readonly DateTime Timestamp = DateTime.Now;
            public readonly TaskCompletionSource<bool> Completion =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _cleaned;

            public void CleanupImage()
            {
                if (Interlocked.Exchange(ref _cleaned, 1) != 0) return;
                try { if (Image != null) Image.Dispose(); } catch { }
            }
        }

        public VisionMasterEngine(string parameterRoot, VisionSettings visionSettings, IAppLogger logger)
        {
            if (string.IsNullOrWhiteSpace(parameterRoot))
                throw new ArgumentException("parameterRoot");

            _parameterRoot = parameterRoot;
            _visionSettings = visionSettings ?? new VisionSettings();
            _logger = logger;
            Directory.CreateDirectory(_parameterRoot);
            _dispatcher = new VisionMasterDispatcher();
        }

        public bool IsLoaded
        {
            get { return _loaded; }
        }

        public string LoadedSolutionPath
        {
            get { lock (_dataSync) return _loadedSolutionPath; }
        }

        public VisionSolutionInfo CurrentSolution
        {
            get { lock (_dataSync) return _solution; }
        }

        public IReadOnlyList<VisionModuleInfo> Modules
        {
            get
            {
                lock (_dataSync)
                    return _modules.Values.Select(x => x.Info).ToList();
            }
        }

        public async Task<VisionSolutionInfo> LoadSolutionAsync(string solutionPath, string password,
            CancellationToken cancellationToken)
        {
            EnsureNotShutdown();
            if (string.IsNullOrWhiteSpace(solutionPath))
                throw new VisionInspectException("方案路径为空。");
            if (!File.Exists(solutionPath))
                throw new FileNotFoundException("视觉方案文件不存在。", solutionPath);

            VisionSolutionInfo result = await _dispatcher.InvokeAsync(
                () => LoadCore(solutionPath, password), cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        public Task UnloadSolutionAsync(CancellationToken cancellationToken)
        {
            EnsureNotShutdown();
            return _dispatcher.InvokeAsync(() =>
            {
                UnloadCore();
                return true;
            }, cancellationToken);
        }

        public Task SaveSolutionAsync(CancellationToken cancellationToken)
        {
            EnsureNotShutdown();
            return _dispatcher.InvokeAsync(() =>
            {
                EnsureLoadedCore();
                SaveCore();
                return true;
            }, cancellationToken);
        }

        public async Task<VisionInspectionResult> RunOnceAsync(RawImage image, string procedureName,
            ResultJudgeOptions judgeOptions, CancellationToken cancellationToken)
        {
            EnsureNotShutdown();
            if (!_loaded) throw new VisionInspectException("视觉方案未加载。");

            await _runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            RunContext context = null;
            try
            {
                // 1) 在 VM STA 线程注入图像并异步启动流程；Run() 不阻塞等待结果。
                context = await _dispatcher.InvokeAsync(
                    () => PrepareRun(image, procedureName), cancellationToken).ConfigureAwait(false);

                // 2) 在普通线程等待 OnWorkEndStatusCallBack，VM 消息泵仍可处理内部消息。
                int timeoutMs = Math.Max(1000, _visionSettings.RunTimeoutMs);
                using (var timeoutCancellation = new CancellationTokenSource())
                {
                    Task timeoutTask = Task.Delay(timeoutMs, timeoutCancellation.Token);
                    Task completed = await Task.WhenAny(context.Completion.Task, timeoutTask).ConfigureAwait(false);
                    // 正常完成后立即撤销定时器，避免高速产线积压整段超时窗口的 Delay 对象。
                    timeoutCancellation.Cancel();
                    if (completed != context.Completion.Task)
                    {
                        // 流程未回调时不能安全重启；保留 active 标记，回调到达后自动清理。
                        throw new VisionInspectException(string.Format(
                            "流程执行超时（{0} ms），旧流程可能仍在运行，已禁止新的执行。", timeoutMs));
                    }
                }

                // 3) 回调已到，回到同一 STA 线程读取结果/释放图像。
                return await _dispatcher.InvokeAsync(
                    () => CompleteRun(context, judgeOptions ?? new ResultJudgeOptions()),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                if (context != null && !context.Completion.Task.IsCompleted)
                {
                    // PrepareRun 成功但等待/调度异常：流程可能仍在跑，不能在回调前释放图像。
                    // ImageBaseData 仍属于 VM，回调后也在 SDK STA 线程释放。
                    _ = context.Completion.Task.ContinueWith(t => ReleaseAbandonedRunAsync(context),
                        TaskScheduler.Default).Unwrap();
                }
                else if (context != null)
                {
                    await _dispatcher.InvokeAsync(() => ReleaseRunContext(context),
                        CancellationToken.None).ConfigureAwait(false);
                }
                throw;
            }
            finally
            {
                _runGate.Release();
            }
        }

        public async Task<object> GetRenderSourceAsync(string procedureName, CancellationToken cancellationToken)
        {
            EnsureNotShutdown();
            return await _dispatcher.InvokeAsync<object>(() =>
            {
                EnsureLoadedCore();
                VmProcedure procedure;
                if (!_procedureObjects.TryGetValue(procedureName ?? string.Empty, out procedure))
                    throw new VisionInspectException("流程不存在：" + procedureName);
                return procedure;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<object> GetModuleSourceAsync(string moduleFullName, CancellationToken cancellationToken)
        {
            EnsureNotShutdown();
            return await _dispatcher.InvokeAsync<object>(() =>
            {
                EnsureLoadedCore();
                ModuleEntry entry;
                if (!_modules.TryGetValue(moduleFullName ?? string.Empty, out entry) || entry.Module == null)
                    throw new VisionInspectException("模块不存在或加载失败：" + moduleFullName);
                return entry.Module;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<VisionParameterInfo>> GetParametersAsync(string moduleFullName,
            CancellationToken cancellationToken)
        {
            EnsureNotShutdown();
            return await _dispatcher.InvokeAsync<IReadOnlyList<VisionParameterInfo>>(
                () => ParameterAdapter.Describe(GetModuleEntry(moduleFullName).Module),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<ParameterApplyResult> SetParameterAsync(string moduleFullName, string parameterName,
            string value, bool persistSolution, CancellationToken cancellationToken)
        {
            EnsureNotShutdown();
            return await _dispatcher.InvokeAsync(() =>
            {
                ModuleEntry entry = GetModuleEntry(moduleFullName);
                ParameterApplyResult result = ParameterAdapter.SetOne(entry.Module, parameterName, value);
                if (persistSolution && result.Applied > 0)
                    TryPersist(result);
                return result;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ParameterSnapshot> CaptureParametersAsync(string moduleFullName,
            CancellationToken cancellationToken)
        {
            EnsureNotShutdown();
            return await _dispatcher.InvokeAsync(
                () => ParameterAdapter.Capture(GetModuleEntry(moduleFullName).Module, moduleFullName),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<ParameterApplyResult> ApplyParametersAsync(string moduleFullName,
            ParameterSnapshot snapshot, bool persistSolution, CancellationToken cancellationToken)
        {
            EnsureNotShutdown();
            return await _dispatcher.InvokeAsync(() =>
            {
                ValidateSnapshotModule(moduleFullName, snapshot);
                ModuleEntry entry = GetModuleEntry(moduleFullName);
                ParameterApplyResult result = ParameterAdapter.Apply(entry.Module, snapshot);
                if (persistSolution && result.Applied > 0)
                    TryPersist(result);
                return result;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task SaveParameterPresetAsync(string moduleFullName, string presetFilePath,
            CancellationToken cancellationToken)
        {
            EnsureNotShutdown();
            await _dispatcher.InvokeAsync(() =>
            {
                ModuleEntry entry = GetModuleEntry(moduleFullName);
                ParameterSnapshot snapshot = ParameterAdapter.Capture(entry.Module, moduleFullName);
                ParameterPresetStore.Save(presetFilePath, snapshot);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ParameterApplyResult> LoadParameterPresetAsync(string moduleFullName,
            string presetFilePath, bool persistSolution, CancellationToken cancellationToken)
        {
            EnsureNotShutdown();
            return await _dispatcher.InvokeAsync(() =>
            {
                ParameterSnapshot snapshot = ParameterPresetStore.Load(presetFilePath);
                ValidateSnapshotModule(moduleFullName, snapshot);
                ModuleEntry entry = GetModuleEntry(moduleFullName);
                ParameterApplyResult result = ParameterAdapter.Apply(entry.Module, snapshot);
                if (persistSolution && result.Applied > 0)
                    TryPersist(result);
                return result;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ParameterApplyResult> ResetParametersToDefaultAsync(string moduleFullName,
            bool persistSolution, CancellationToken cancellationToken)
        {
            EnsureNotShutdown();
            return await _dispatcher.InvokeAsync(() =>
            {
                ModuleEntry entry = GetModuleEntry(moduleFullName);
                if (entry.VmModule == null)
                    throw new VisionInspectException("该对象不支持官方算子默认参数重置：" + moduleFullName);

                // 使用官方完整重置，包含简单属性适配器无法表达的 ROI、模板等算法参数。
                // Applied=1 表示完成一次模块重置，不代表已统计全部参数的数量。
                entry.VmModule.ResetParam();
                var result = new ParameterApplyResult { Applied = 1 };
                if (persistSolution)
                    TryPersist(result);
                return result;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task ShutdownAsync()
        {
            await _shutdownGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_shutdown) return;
                _shutdown = true;
                Task drained;
                lock (_runSync) drained = _activeRunDrained == null ? null : _activeRunDrained.Task;
                if (drained != null)
                {
                    if (_logger != null) _logger.Warn("VisionMaster", "退出时检测流程仍在运行，等待其完成回调...");
                    Task finished = await Task.WhenAny(drained,
                        Task.Delay(Math.Max(5000, _visionSettings.RunTimeoutMs + 5000))).ConfigureAwait(false);
                    if (finished != drained)
                        throw new VisionInspectException("流程仍在运行，无法安全关闭视觉方案。");
                }

                await _dispatcher.InvokeAsync(() =>
                {
                    // 回调到达且图像已释放后，才允许关闭方案和单例。
                    UnloadCore();
                    if (_sdkReady) { VmSolution.Instance.Dispose(); _sdkReady = false; }
                }, CancellationToken.None).ConfigureAwait(false);

                _dispatcher.Dispose();
                if (_logger != null) _logger.Info("VisionMaster", "视觉引擎已关闭。");
            }
            catch (Exception ex)
            {
                // 不释放仍在执行的原生对象；让窗体保持打开，并允许操作员稍后重试退出。
                _shutdown = false;
                if (_logger != null) _logger.Error("VisionMaster", "关闭视觉引擎失败，已保留 SDK 资源。", ex);
                throw;
            }
            finally { _shutdownGate.Release(); }
        }

        private VisionSolutionInfo LoadCore(string solutionPath, string password)
        {
            EnsureNotShutdown();
            if (_loaded)
                UnloadCore();

            try
            {
                VmSolution.Load(solutionPath, password, false);
                _sdkReady = true;

                var solution = new VisionSolutionInfo
                {
                    SolutionPath = solutionPath,
                    LoadedAt = DateTime.Now
                };

                ProcessInfoList processList = VmSolution.Instance.GetAllProcedureList();
                for (uint i = 0; i < processList.nNum; i++)
                {
                    string procedureName = processList.astProcessInfo[i].strProcessName;
                    VmProcedure procedure;
                    try
                    {
                        procedure = VmSolution.Instance[procedureName] as VmProcedure;
                    }
                    catch (Exception ex)
                    {
                        LogWarn("读取流程失败：" + procedureName, ex);
                        continue;
                    }

                    if (procedure == null) continue;
                    _procedureObjects[procedureName] = procedure;
                    // 不依赖 SDK 的 sender 值。每个流程绑定自己的闭包，防止其他流程回调
                    // （例如方案内部关联流程）误唤醒当前检测，造成读取旧结果或提前释放图像。
                    string callbackProcedureName = procedureName;
                    EventHandler handler = (sender, args) => OnProcedureWorkEnd(callbackProcedureName);
                    _procedureEndHandlers[procedureName] = handler;
                    procedure.OnWorkEndStatusCallBack += handler;

                    var procedureInfo = new VisionProcedureInfo { Name = procedureName };
                    procedureInfo.Outputs = ReadProcedureOutputInfo(procedure);
                    solution.Procedures.Add(procedureInfo);

                    ModuleInfoList moduleList = procedure.GetProcedureModuleList();
                    for (uint m = 0; m < moduleList.nNum; m++)
                    {
                        ModuInfo moduleInfo = moduleList.astModuleInfo[m];
                        string moduleName = moduleInfo.strModuleName;
                        string fullName = procedureName + "." + moduleName;
                        object moduleObject = null;

                        try
                        {
                            moduleObject = VmSolution.Instance[fullName];
                        }
                        catch (Exception ex)
                        {
                            LogWarn("读取模块失败：" + fullName, ex);
                        }

                        if (moduleObject == null)
                        {
                            try
                            {
                                moduleObject = VmSolution.Instance[procedureName + "." + moduleInfo.strDisplayName];
                            }
                            catch
                            {
                                // 保持 null，下面按不可用模块记录。
                            }
                        }

                        var visualModule = new VisionModuleInfo
                        {
                            ProcedureName = procedureName,
                            Name = moduleName,
                            DisplayName = string.IsNullOrWhiteSpace(moduleInfo.strDisplayName)
                                ? moduleName
                                : moduleInfo.strDisplayName,
                            FullName = fullName,
                            TypeName = moduleObject == null ? "Unavailable" : moduleObject.GetType().FullName,
                            IsAvailable = moduleObject != null
                        };

                        ModuleEntry entry = new ModuleEntry
                        {
                            Info = visualModule,
                            Module = moduleObject,
                            VmModule = moduleObject as VmModule
                        };

                        if (moduleObject is ImageSourceModuleTool)
                        {
                            visualModule.IsImageSource = true;
                            var imageTool = (ImageSourceModuleTool)moduleObject;
                            string configuredName = _visionSettings.ImageSourceModuleName ?? string.Empty;
                            bool configured = !string.IsNullOrWhiteSpace(configuredName) &&
                                              (string.Equals(moduleName, configuredName, StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(fullName, configuredName, StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(visualModule.DisplayName, configuredName, StringComparison.OrdinalIgnoreCase));

                            if (!_imageSources.ContainsKey(procedureName) || configured)
                                _imageSources[procedureName] = imageTool;
                        }

                        if (moduleObject is FormatModuleTool)
                        {
                            visualModule.IsFormatModule = true;
                            List<FormatModuleTool> list;
                            if (!_formatModules.TryGetValue(procedureName, out list))
                            {
                                list = new List<FormatModuleTool>();
                                _formatModules[procedureName] = list;
                            }
                            list.Add((FormatModuleTool)moduleObject);
                        }

                        if (entry.VmModule != null)
                            visualModule.Outputs = ReadModuleOutputInfo(entry.VmModule);

                        _modules[fullName] = entry;
                        procedureInfo.Modules.Add(visualModule);
                    }
                }

                if (solution.Procedures.Count == 0)
                    throw new VisionInspectException("方案内没有可执行流程，请检查 .sol 文件。");

                VisionProcedureInfo primary = ResolvePrimaryProcedure(solution);
                solution.PrimaryProcedureName = primary.Name;

                ImageSourceModuleTool imageSource;
                if (_imageSources.TryGetValue(primary.Name, out imageSource))
                {
                    try
                    {
                        imageSource.ModuParams.ImageSourceType = ImageSourceParam.ImageSourceTypeEnum.SDK;
                    }
                    catch (Exception ex)
                    {
                        LogWarn("设置图像源为 SDK 模式失败（流程：" + primary.Name + "）。", ex);
                    }

                    VisionModuleInfo sourceInfo = primary.Modules.FirstOrDefault(x =>
                        x.IsImageSource &&
                        string.Equals(x.Name, imageSource.Name, StringComparison.OrdinalIgnoreCase));
                    if (sourceInfo == null)
                        sourceInfo = primary.Modules.FirstOrDefault(x => x.IsImageSource);
                    solution.ImageSourceModuleFullName = sourceInfo == null ? string.Empty : sourceInfo.FullName;
                }
                else
                {
                    if (_logger != null)
                        _logger.Warn("VisionMaster", "主流程未找到图像源模块；将按方案内相机/本地源方式执行。");
                }

                lock (_dataSync)
                {
                    _solution = solution;
                    _loadedSolutionPath = solutionPath;
                }
                _loaded = true;

                CaptureDefaultParameters(solution);
                if (_logger != null)
                {
                    _logger.Info("VisionMaster", string.Format(
                        "方案加载成功：{0}，流程 {1} 个，模块 {2} 个，主流程 {3}。",
                        solutionPath, solution.Procedures.Count, _modules.Count, primary.Name));
                }

                return solution;
            }
            catch (Exception ex)
            {
                try { VmSolution.Instance.CloseSolution(); } catch { }
                ClearCoreState();
                _loaded = false;
                if (ex is VmException)
                {
                    VmException vmEx = (VmException)ex;
                    throw new VisionSdkException(vmEx.errorCode, "加载方案 " + Path.GetFileName(solutionPath), ex);
                }
                throw;
            }
        }

        private void UnloadCore()
        {
            lock (_runSync)
            {
                if (_activeRunCompletion != null)
                    throw new VisionInspectException("VM 流程仍在运行，禁止释放/切换方案。");
            }

            if (_solution == null && !_loaded)
            {
                ClearCoreState();
                return;
            }

            try
            {
                // 先解除回调，避免 CloseSolution 期间回调访问已释放对象。
                UnsubscribeProcedureEvents();
                SafeCloseSolution();
                if (_logger != null) _logger.Info("VisionMaster", "视觉方案已释放。");
            }
            catch
            {
                // 关闭失败时保留状态和事件，不能假装旧方案已释放再加载下一份方案。
                foreach (KeyValuePair<string, VmProcedure> pair in _procedureObjects)
                {
                    EventHandler handler;
                    if (_procedureEndHandlers.TryGetValue(pair.Key, out handler))
                        pair.Value.OnWorkEndStatusCallBack += handler;
                }
                throw;
            }
            ClearCoreState();
            _loaded = false;
        }

        private void ClearCoreState()
        {
            UnsubscribeProcedureEvents();
            lock (_dataSync)
            {
                _procedureObjects.Clear();
                _procedureEndHandlers.Clear();
                _modules.Clear();
                _formatModules.Clear();
                _imageSources.Clear();
                _defaults.Clear();
                _solution = null;
                _loadedSolutionPath = null;
            }
        }

        private void SafeCloseSolution()
        {
            VmSolution.Instance.CloseSolution();
        }

        /// <summary>在 VM 调度线程上注入图像并异步启动流程，不在这里等待结果。</summary>
        private RunContext PrepareRun(RawImage image, string procedureName)
        {
            EnsureNotShutdown();
            EnsureLoadedCore();

            VmProcedure procedure;
            if (!_procedureObjects.TryGetValue(procedureName ?? string.Empty, out procedure))
                throw new VisionInspectException("流程不存在：" + procedureName);

            lock (_runSync)
            {
                if (_activeRunCompletion != null)
                    throw new VisionInspectException("上一轮 VM 流程尚未结束，禁止新的执行。");
            }

            ImageSourceModuleTool imageSource;
            _imageSources.TryGetValue(procedureName, out imageSource);
            if (image != null && imageSource == null)
                throw new VisionInspectException("当前流程没有可接收 SDK 图像的图像源模块：" + procedureName);

            var context = new RunContext
            {
                Procedure = procedure,
                ProcedureName = procedureName
            };

            lock (_runSync)
            {
                if (_activeRunCompletion != null)
                    throw new VisionInspectException("上一轮 VM 流程尚未结束，禁止新的执行。");
                _activeRunCompletion = context.Completion;
                _activeRunDrained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _activeProcedureName = procedureName;
            }

            try
            {
                if (image != null)
                {
                    if (imageSource != null)
                    {
                        try
                        {
                            imageSource.ModuParams.ImageSourceType = ImageSourceParam.ImageSourceTypeEnum.SDK;
                            context.Image = VmImageConverter.ToImageBaseData(image);
                            imageSource.SetImageData(context.Image, true);
                        }
                        catch (Exception ex)
                        {
                            throw new VisionInspectException("向 VM 图像源写入图像失败。", ex);
                        }
                    }
                    else if (_logger != null)
                    {
                        _logger.Warn("VisionMaster", "流程没有图像源模块，图像未传入 VM。");
                    }
                }

                // 使用 Run + OnWorkEndStatusCallBack 的异步模式：
                // SyncRun 会阻塞调度线程，某些 VM 模块/脚本还需要消息泵才能完成，容易死锁。
                procedure.Run();
                if (_logger != null)
                    _logger.Debug("VisionMaster", "流程已提交异步执行：" + procedureName);
                return context;
            }
            catch (Exception ex)
            {
                ReleaseRunContext(context);
                if (ex is VmException)
                {
                    VmException vmEx = (VmException)ex;
                    throw new VisionSdkException(vmEx.errorCode, "启动流程 " + procedureName, ex);
                }
                throw;
            }
        }

        /// <summary>流程结束回调。只负责唤醒等待者，不在 SDK 回调线程解析结果。</summary>
        private void OnProcedureWorkEnd(string procedureName)
        {
            TaskCompletionSource<bool> completion;
            lock (_runSync)
                completion = string.Equals(_activeProcedureName, procedureName, StringComparison.OrdinalIgnoreCase)
                    ? _activeRunCompletion : null;
            if (completion != null)
                completion.TrySetResult(true);
        }

        /// <summary>回到 VM 调度线程读取流程结果，并释放本次图像。</summary>
        private VisionInspectionResult CompleteRun(RunContext context, ResultJudgeOptions judgeOptions)
        {
            try
            {
                context.Stopwatch.Stop();
                VmProcedure procedure = context.Procedure;

                bool procedureSucceeded = false;
                try { procedureSucceeded = procedure.IsRunOK == true; }
                catch { }

                var outputs = ParseOutputs(procedure, judgeOptions);
                var statuses = ReadModuleStatuses(context.ProcedureName);
                List<VisionModuleStatus> activeModules = statuses.Where(x => !x.IsForbidden).ToList();
                bool? modulesSucceeded = activeModules.Count == 0
                    ? (bool?)null
                    : activeModules.All(x => x.IsOk);
                ResultJudge.Outcome judge = ResultJudge.Evaluate(
                    outputs, procedureSucceeded, modulesSucceeded, judgeOptions);

                if (_logger != null)
                {
                    _logger.Debug("VisionMaster", string.Format(
                        "流程 {0} 完成：{1}，流程状态={2}，模块汇总={3}，耗时 {4:F1} ms，输出 {5} 个；模块状态：{6}",
                        context.ProcedureName, judge.Text, procedureSucceeded,
                        modulesSucceeded.HasValue ? (modulesSucceeded.Value ? "OK" : "NG") : "无",
                        context.Stopwatch.Elapsed.TotalMilliseconds, outputs.Count,
                        string.Join(" | ", statuses.Select(x => string.Format(
                            "{0}={1}{2}", x.ModuleName, x.IsOk ? "OK" : "NG",
                            x.IsForbidden ? "(禁用)" : string.Empty)))));
                }

                return new VisionInspectionResult
                {
                    SolutionPath = LoadedSolutionPath,
                    ProcedureName = context.ProcedureName,
                    Timestamp = context.Timestamp,
                    IsOk = judge.IsOk,
                    ProcedureSucceeded = procedureSucceeded,
                    JudgeText = judge.Text,
                    JudgeSource = judge.Source,
                    DurationMs = context.Stopwatch.Elapsed.TotalMilliseconds,
                    Outputs = outputs,
                    Modules = statuses
                };
            }
            finally
            {
                ReleaseRunContext(context);
            }
        }

        private void ReleaseRunContext(RunContext context)
        {
            if (context == null) return;
            TaskCompletionSource<bool> drained = null;
            lock (_runSync)
            {
                if (ReferenceEquals(_activeRunCompletion, context.Completion))
                {
                    _activeRunCompletion = null;
                    _activeProcedureName = null;
                    drained = _activeRunDrained;
                    _activeRunDrained = null;
                }
            }
            context.CleanupImage();
            if (drained != null) drained.TrySetResult(true);
        }

        private async Task ReleaseAbandonedRunAsync(RunContext context)
        {
            try
            {
                await _dispatcher.InvokeAsync(() => ReleaseRunContext(context),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("VisionMaster", "回收超时流程图像失败。", ex);
            }
        }

        private void UnsubscribeProcedureEvents()
        {
            List<VmProcedure> procedures;
            lock (_dataSync) procedures = _procedureObjects.Values.ToList();
            foreach (VmProcedure procedure in procedures)
            {
                try
                {
                    EventHandler handler;
                    if (_procedureEndHandlers.TryGetValue(procedure.Name, out handler))
                        procedure.OnWorkEndStatusCallBack -= handler;
                }
                catch { }
            }
        }

        private List<VisionOutputValue> ParseOutputs(VmProcedure procedure, ResultJudgeOptions judgeOptions)
        {
            var outputs = new List<VisionOutputValue>();
            int maxRows = Math.Max(20, _visionSettings.MaxOutputRows);
            List<VmDynamicIODefine.IoNameInfo> infos = null;

            try
            {
                infos = procedure.ModuResult == null ? null : procedure.ModuResult.GetAllOutputNameInfo();
            }
            catch (Exception ex)
            {
                LogWarn("读取流程输出名称失败。", ex);
            }

            if (infos != null)
            {
                foreach (VmDynamicIODefine.IoNameInfo info in infos)
                {
                    if (outputs.Count >= maxRows) break;

                    try
                    {
                        VisionOutputValue value = ReadOutputValue(procedure, info, judgeOptions);
                        if (value != null) outputs.Add(value);
                    }
                    catch (Exception ex)
                    {
                        LogWarn("解析输出失败：" + info.Name, ex);
                    }
                }
            }

            if (_visionSettings.ParseFormatModuleOutputs)
            {
                try
                {
                    AppendFormatModuleOutputs(procedure.Name, outputs, maxRows);
                }
                catch (Exception ex)
                {
                    LogWarn("读取格式化模块输出失败。", ex);
                }
            }

            // 方案不一定把模块结果配置为“流程输出”。这里补充读取模块输出叶子值，
            // 例如圆查找的 CenterX/CenterY/Radius/FitError，避免结果表只有一张图。
            try
            {
                AppendModuleOutputs(procedure.Name, outputs, maxRows);
            }
            catch (Exception ex)
            {
                LogWarn("读取模块输出值失败。", ex);
            }

            // CircleFind 等算子的结果对象通常只有专用 ModuResult，
            // 通过反射读取其公开标量字段（CenterX/CenterY/Radius/FitError 等）。
            try
            {
                AppendModuleResultValues(procedure.Name, outputs, maxRows);
            }
            catch (Exception ex)
            {
                LogWarn("读取模块结果对象失败。", ex);
            }

            if (outputs.Count >= maxRows)
            {
                outputs.Add(new VisionOutputValue
                {
                    Name = "[输出已截断]",
                    Source = "系统",
                    TypeName = "Text",
                    Kind = VisionValueKind.Text,
                    DisplayValue = string.Format("超过最大输出行数 {0}，请在设置中调整。", maxRows),
                    Values = new List<string> { string.Format("超过 {0} 行", maxRows) }
                });
            }

            return outputs;
        }

        private VisionOutputValue ReadOutputValue(VmProcedure procedure,
            VmDynamicIODefine.IoNameInfo info, ResultJudgeOptions judgeOptions)
        {
            var value = new VisionOutputValue
            {
                Name = info.Name,
                Source = "流程输出",
                TypeName = info.TypeName.ToString()
            };

            switch (info.TypeName)
            {
                case IMVS_MODULE_BASE_DATA_TYPE.IMVS_GRAP_TYPE_INT:
                {
                    IntDataArray result = procedure.ModuResult.GetOutputInt(info.Name);
                    if (result.pIntVal == null) return null;
                    int count = Math.Min(Math.Max(0, result.nValueNum), result.pIntVal.Length);

                    bool boolean = IsBooleanOutput(info.Name, judgeOptions.BooleanOutputNames) &&
                                   result.pIntVal.Take(count).All(x => x == 0 || x == 1);
                    value.Kind = boolean ? VisionValueKind.Boolean : VisionValueKind.Numeric;
                    foreach (int item in result.pIntVal.Take(count))
                        value.Values.Add(boolean ? (item == 1 ? "True" : "False") : item.ToString(CultureInfo.InvariantCulture));
                    break;
                }

                case IMVS_MODULE_BASE_DATA_TYPE.IMVS_GRAP_TYPE_FLOAT:
                {
                    FloatDataArray result = procedure.ModuResult.GetOutputFloat(info.Name);
                    if (result.pFloatVal == null) return null;

                    value.Kind = VisionValueKind.Numeric;
                    foreach (float item in result.pFloatVal.Take(Math.Max(0, result.nValueNum)))
                        value.Values.Add(item.ToString("G9", CultureInfo.InvariantCulture));
                    break;
                }

                case IMVS_MODULE_BASE_DATA_TYPE.IMVS_GRAP_TYPE_STRING:
                {
                    StringDataArray result = procedure.ModuResult.GetOutputString(info.Name);
                    if (result.astStringVal == null) return null;

                    foreach (StringData item in result.astStringVal.Take(Math.Max(0, result.nValueNum)))
                        value.Values.Add(item.strValue ?? string.Empty);

                    value.Kind = value.Values.Count > 0 &&
                                 value.Values.All(x => string.Equals(x, "True", StringComparison.OrdinalIgnoreCase) ||
                                                       string.Equals(x, "False", StringComparison.OrdinalIgnoreCase))
                        ? VisionValueKind.Boolean
                        : VisionValueKind.Text;
                    break;
                }

                case IMVS_MODULE_BASE_DATA_TYPE.IMVS_BASE_TYPE_IMAGE_DATA:
                    value.Kind = VisionValueKind.Image;
                    value.Values.Add("[图像输出]");
                    break;

                default:
                    value.Kind = VisionValueKind.Other;
                    value.Values.Add("[复杂类型：" + info.TypeName + "]");
                    break;
            }

            value.DisplayValue = string.Join(", ", value.Values);
            return value;
        }

        /// <summary>
        /// 补充读取模块自身的输出叶子值。VM 方案经常只在模块结果中展示参数，
        /// 没有把这些值配置到流程 Outputs；如果只读流程输出，界面就会出现“找到了但结果表空白”。
        /// </summary>
        private void AppendModuleOutputs(string procedureName, List<VisionOutputValue> outputs, int maxRows)
        {
            List<ModuleEntry> entries;
            lock (_dataSync)
            {
                entries = _modules.Values
                    .Where(x => string.Equals(x.Info.ProcedureName, procedureName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var names = new HashSet<string>(
                outputs.Select(x => x.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase);

            foreach (ModuleEntry entry in entries)
            {
                if (entry.VmModule == null || entry.VmModule.Outputs == null) continue;
                if (outputs.Count >= maxRows) return;

                List<VmIO> leaves;
                try { leaves = entry.VmModule.Outputs.GetAllLeafIO(); }
                catch { continue; }
                if (leaves == null) continue;

                foreach (VmIO leaf in leaves)
                {
                    if (outputs.Count >= maxRows) return;
                    if (leaf == null || !leaf.IsOutput || leaf.IsPrivate) continue;
                    if (!leaf.VisibleInResultTree && !leaf.IsResultShow) continue;

                    string type = leaf.TypeName ?? string.Empty;
                    if (type.IndexOf("Image", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        type.IndexOf("图像", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    string leafName = string.IsNullOrWhiteSpace(leaf.CustomName) ? leaf.Name : leaf.CustomName;
                    if (string.IsNullOrWhiteSpace(leafName)) continue;
                    // 有些模块会生成 "0"/"1" 这类内部索引叶子，展示价值低且容易误导。
                    if (leafName.All(char.IsDigit)) continue;

                    string moduleDisplayName = string.IsNullOrWhiteSpace(entry.Info.DisplayName)
                        ? entry.Info.Name : entry.Info.DisplayName;
                    string name = moduleDisplayName + "." + leafName;
                    if (!names.Add(name)) continue;

                    string text;
                    try { text = FormatIoValue(leaf); }
                    catch { continue; }
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    outputs.Add(new VisionOutputValue
                    {
                        Name = name,
                        Source = "模块:" + moduleDisplayName,
                        TypeName = string.IsNullOrWhiteSpace(type) ? "Value" : type,
                        Kind = IsBooleanType(type) ? VisionValueKind.Boolean :
                               IsNumericType(type) ? VisionValueKind.Numeric : VisionValueKind.Text,
                        DisplayValue = text,
                        Values = new List<string> { text }
                    });
                }
            }
        }

        private static string FormatIoValue(VmIO leaf)
        {
            var values = new List<string>();
            try
            {
                object raw = leaf.Value;
                if (raw is string)
                {
                    values.Add((string)raw);
                }
                else if (raw is IEnumerable)
                {
                    foreach (object item in (IEnumerable)raw)
                    {
                        if (item == null) continue;
                        values.Add(FormatScalar(item));
                        if (values.Count >= 16) break;
                    }
                }
                else if (raw != null)
                {
                    values.Add(FormatScalar(raw));
                }
            }
            catch
            {
                return string.Empty;
            }

            return string.Join(", ", values);
        }

        private static string FormatScalar(object value)
        {
            if (value is float) return ((float)value).ToString("G9", CultureInfo.InvariantCulture);
            if (value is double) return ((double)value).ToString("G15", CultureInfo.InvariantCulture);
            if (value is IFormattable) return ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool IsDisplayableModuleProperty(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName)) return false;
            string[] blocked =
            {
                "Image", "Params", "Root", "Parent", "BaseDataType", "Pixelformat",
                "DataLen", "Width", "Height", "ROI", "ROIAnnulus", "OutputMask",
                "MatchOutline", "OutputCircleAnnulus", "OutputAnnulus"
            };
            foreach (string word in blocked)
            {
                if (propertyName.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }
            return true;
        }

        private static bool IsNumericType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return false;
            return type.IndexOf("Int", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   type.IndexOf("Float", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   type.IndexOf("Double", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   type.IndexOf("Number", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBooleanType(string type)
        {
            return !string.IsNullOrWhiteSpace(type) &&
                   type.IndexOf("Bool", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void AppendModuleResultValues(string procedureName, List<VisionOutputValue> outputs, int maxRows)
        {
            List<ModuleEntry> entries;
            lock (_dataSync)
            {
                entries = _modules.Values
                    .Where(x => string.Equals(x.Info.ProcedureName, procedureName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => (x.Info.TypeName ?? string.Empty)
                        .IndexOf("CircleFind", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            var names = new HashSet<string>(
                outputs.Select(x => x.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase);

            foreach (ModuleEntry entry in entries)
            {
                if (outputs.Count >= maxRows) return;
                if (entry.VmModule == null) continue;

                PropertyInfo resultProperty = entry.VmModule.GetType().GetProperty(
                    "ModuResult", BindingFlags.Public | BindingFlags.Instance);
                if (resultProperty == null || !resultProperty.CanRead ||
                    resultProperty.GetIndexParameters().Length != 0) continue;

                object result;
                try { result = resultProperty.GetValue(entry.VmModule, null); }
                catch { continue; }
                if (result == null) continue;

                string moduleDisplayName = string.IsNullOrWhiteSpace(entry.Info.DisplayName)
                    ? entry.Info.Name : entry.Info.DisplayName;
                if ((entry.Info.TypeName ?? string.Empty).IndexOf(
                        "CircleFind", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AppendCircleFindResult(moduleDisplayName, result, outputs, names, maxRows);
                    continue;
                }

                try
                {
                    AppendReflectedValues(moduleDisplayName, result, string.Empty, 0,
                        outputs, names, maxRows);
                }
                catch { }
            }
        }

        private void AppendCircleFindResult(string moduleName, object result,
            List<VisionOutputValue> outputs, HashSet<string> names, int maxRows)
        {
            object circle = GetPropertyValue(result, "OutputCircle");
            object center = GetPropertyValue(circle, "CenterPoint");
            AddReflectedOutput(moduleName, string.Empty, "圆心X", GetPropertyValue(center, "X"),
                "Float", outputs, names, maxRows);
            AddReflectedOutput(moduleName, string.Empty, "圆心Y", GetPropertyValue(center, "Y"),
                "Float", outputs, names, maxRows);
            AddReflectedOutput(moduleName, string.Empty, "半径", GetPropertyValue(circle, "Radius"),
                "Float", outputs, names, maxRows);
            AddReflectedOutput(moduleName, string.Empty, "拟合误差", GetPropertyValue(result, "FitError"),
                "Float", outputs, names, maxRows);
            AddReflectedOutput(moduleName, string.Empty, "边缘点数", GetPropertyValue(result, "EdgePointNum"),
                "Int", outputs, names, maxRows);
        }

        private static string FriendlyOutputName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            string leaf = path;
            int dot = path.LastIndexOf('.');
            if (dot >= 0) leaf = path.Substring(dot + 1);
            switch (leaf)
            {
                case "SNCode": return "图像编号";
                case "FrameNum": return "帧号";
                case "LostFrameCount": return "丢帧数";
                case "LostPacketCount": return "丢包数";
                case "GetCameraFailCount": return "取图失败次数";
                case "ErrorCode": return "错误码";
                case "MatchNum": return "匹配数量";
                case "MatchOutlineNum": return "轮廓数量";
                case "ModuStatus": return "模块状态";
                case "FixtureInitAngle": return "初始角度";
                case "FixtureRunAngle": return "运行角度";
                case "FixtureInitScaleX": return "初始缩放X";
                case "FixtureInitScaleY": return "初始缩放Y";
                case "FixtureRunScaleX": return "运行缩放X";
                case "FixtureRunScaleY": return "运行缩放Y";
                case "CenterX": return "圆心X";
                case "CenterY": return "圆心Y";
                case "Radius": return "半径";
                case "FitError": return "拟合误差";
                case "EdgePointNum": return "边缘点数";
                default:
                    if (path.EndsWith("FixtureInitPoint.X", StringComparison.Ordinal)) return "初始点X";
                    if (path.EndsWith("FixtureInitPoint.Y", StringComparison.Ordinal)) return "初始点Y";
                    if (path.EndsWith("FixtureRunPoint.X", StringComparison.Ordinal)) return "运行点X";
                    if (path.EndsWith("FixtureRunPoint.Y", StringComparison.Ordinal)) return "运行点Y";
                    return leaf;
            }
        }

        private static object GetPropertyValue(object instance, string propertyName)
        {
            if (instance == null) return null;
            try
            {
                PropertyInfo property = instance.GetType().GetProperty(propertyName,
                    BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanRead ||
                    property.GetIndexParameters().Length != 0) return null;
                return property.GetValue(instance, null);
            }
            catch { return null; }
        }

        private static void AddReflectedOutput(string moduleName, string namePrefix, string name, object value,
            string typeName, List<VisionOutputValue> outputs, HashSet<string> names, int maxRows)
        {
            if (outputs.Count >= maxRows || value == null) return;
            string fullName = string.IsNullOrWhiteSpace(namePrefix) ? name : namePrefix + "." + name;
            if (!names.Add(fullName)) return;
            string text = value is bool ? ((bool)value ? "True" : "False") : FormatScalar(value);
            if (string.IsNullOrWhiteSpace(text)) return;
            outputs.Add(new VisionOutputValue
            {
                Name = fullName,
                Source = "模块:" + moduleName,
                TypeName = typeName,
                Kind = string.Equals(typeName, "Boolean", StringComparison.OrdinalIgnoreCase)
                    ? VisionValueKind.Boolean : VisionValueKind.Numeric,
                DisplayValue = text,
                Values = new List<string> { text }
            });
        }

        private void AppendReflectedValues(string moduleName, object instance, string prefix,
            int depth, List<VisionOutputValue> outputs, HashSet<string> names, int maxRows)
        {
            if (instance == null || depth > 2 || outputs.Count >= maxRows) return;
            Type type = instance.GetType();
            if (type.IsPrimitive || instance is string || instance is decimal || type.IsEnum) return;
            if (typeof(IEnumerable).IsAssignableFrom(type)) return;

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (outputs.Count >= maxRows) return;
                if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;

                string propertyName = property.Name;
                if (!IsDisplayableModuleProperty(propertyName)) continue;

                object value;
                try { value = property.GetValue(instance, null); }
                catch { continue; }
                if (value == null) continue;

                string path = string.IsNullOrWhiteSpace(prefix) ? propertyName : prefix + "." + propertyName;
                Type valueType = value.GetType();

                if (valueType.IsPrimitive || value is string || value is decimal || valueType.IsEnum)
                {
                    string name = moduleName + " " + FriendlyOutputName(path);
                    if (!names.Add(name)) continue;
                    string text = value is bool ? ((bool)value ? "True" : "False") : FormatScalar(value);
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    outputs.Add(new VisionOutputValue
                    {
                        Name = name,
                        Source = "模块:" + moduleName,
                        TypeName = valueType.Name,
                        Kind = value is bool ? VisionValueKind.Boolean :
                               valueType.IsEnum || IsNumericType(valueType.Name) ||
                               valueType == typeof(byte) || valueType == typeof(short) ||
                               valueType == typeof(int) || valueType == typeof(long) ||
                               valueType == typeof(float) || valueType == typeof(double)
                            ? VisionValueKind.Numeric : VisionValueKind.Text,
                        DisplayValue = text,
                        Values = new List<string> { text }
                    });
                }
                else if (depth < 2 && !typeof(IEnumerable).IsAssignableFrom(valueType))
                {
                    AppendReflectedValues(moduleName, value, path, depth + 1,
                        outputs, names, maxRows);
                }
            }
        }

        private void AppendFormatModuleOutputs(string procedureName, List<VisionOutputValue> outputs, int maxRows)
        {
            List<FormatModuleTool> modules;
            if (!_formatModules.TryGetValue(procedureName, out modules) || modules == null) return;

            foreach (FormatModuleTool formatter in modules)
            {
                if (formatter == null || formatter.ModuResult == null) continue;

                List<string> formatted = formatter.ModuResult.format_out;
                if (formatted == null || formatted.Count == 0) continue;

                List<VmIO> leaves = null;
                try
                {
                    leaves = formatter.Outputs == null ? null : formatter.Outputs.GetAllLeafIO();
                }
                catch
                {
                    // 名称只是展示用途，失败时使用索引。
                }

                for (int i = 0; i < formatted.Count && outputs.Count < maxRows; i++)
                {
                    string name;
                    if (leaves != null && i < leaves.Count)
                        name = string.IsNullOrWhiteSpace(leaves[i].CustomName) ? leaves[i].Name : leaves[i].CustomName;
                    else
                        name = string.Format("{0}.格式化输出[{1}]", formatter.Name, i);

                    string text = formatted[i] ?? string.Empty;
                    double numeric;
                    bool isBoolean = string.Equals(text, "True", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(text, "False", StringComparison.OrdinalIgnoreCase);
                    bool isNumeric = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out numeric) &&
                                     !double.IsNaN(numeric) && !double.IsInfinity(numeric);
                    outputs.Add(new VisionOutputValue
                    {
                        Name = name,
                        Source = "格式化:" + formatter.Name,
                        TypeName = "Format",
                        Kind = isBoolean ? VisionValueKind.Boolean :
                            isNumeric ? VisionValueKind.Numeric : VisionValueKind.Text,
                        Values = new List<string> { text },
                        DisplayValue = text
                    });
                }
            }
        }

        private List<VisionModuleStatus> ReadModuleStatuses(string procedureName)
        {
            var statuses = new List<VisionModuleStatus>();
            List<ModuleEntry> entries;
            lock (_dataSync)
            {
                entries = _modules.Values
                    .Where(x => string.Equals(x.Info.ProcedureName, procedureName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            foreach (ModuleEntry entry in entries)
            {
                if (entry.VmModule == null) continue;
                try
                {
                    statuses.Add(new VisionModuleStatus
                    {
                        ProcedureName = procedureName,
                        ModuleName = entry.Info.Name,
                        IsOk = entry.VmModule.IsRunOK == true,
                        IsForbidden = entry.VmModule.IsForbidden,
                        ModuleTimeMs = entry.VmModule.ModuleTime
                    });
                }
                catch
                {
                    // 不阻断结果展示。
                }
            }

            return statuses;
        }

        private List<VisionOutputInfo> ReadProcedureOutputInfo(VmProcedure procedure)
        {
            var result = new List<VisionOutputInfo>();
            try
            {
                if (procedure.ModuResult == null) return result;
                List<VmDynamicIODefine.IoNameInfo> infos = procedure.ModuResult.GetAllOutputNameInfo();
                if (infos == null) return result;

                foreach (VmDynamicIODefine.IoNameInfo info in infos)
                {
                    result.Add(new VisionOutputInfo { Name = info.Name, TypeName = info.TypeName.ToString() });
                }
            }
            catch (Exception ex)
            {
                LogWarn("读取流程输出定义失败。", ex);
            }
            return result;
        }

        private List<VisionOutputInfo> ReadModuleOutputInfo(VmModule module)
        {
            var result = new List<VisionOutputInfo>();
            try
            {
                List<VmIO> leaves = module.Outputs == null ? null : module.Outputs.GetAllLeafIO();
                if (leaves == null) return result;

                foreach (VmIO io in leaves)
                {
                    result.Add(new VisionOutputInfo
                    {
                        Name = string.IsNullOrWhiteSpace(io.CustomName) ? io.Name : io.CustomName,
                        TypeName = io.TypeName ?? string.Empty
                    });
                }
            }
            catch (Exception ex)
            {
                LogWarn("读取模块输出定义失败。", ex);
            }
            return result;
        }

        private VisionProcedureInfo ResolvePrimaryProcedure(VisionSolutionInfo solution)
        {
            string configured = _visionSettings.PrimaryProcedureName;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                VisionProcedureInfo match = solution.Procedures.FirstOrDefault(x =>
                    string.Equals(x.Name, configured, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            return solution.Procedures[0];
        }

        private void EnsureLoadedCore()
        {
            if (!_loaded || _solution == null)
                throw new VisionInspectException("视觉方案未加载。");
        }

        private ModuleEntry GetModuleEntry(string moduleFullName)
        {
            EnsureNotShutdown();
            EnsureNoActiveRunCore();
            ModuleEntry entry;
            if (string.IsNullOrWhiteSpace(moduleFullName) ||
                !_modules.TryGetValue(moduleFullName, out entry) ||
                entry.Module == null)
            {
                throw new VisionInspectException("模块不存在或不可用：" + moduleFullName);
            }
            return entry;
        }

        private void SaveCore()
        {
            EnsureNotShutdown();
            EnsureNoActiveRunCore();
            try
            {
                VmSolution.Save();
                if (_logger != null) _logger.Info("VisionMaster", "方案参数已保存。");
            }
            catch (VmException ex)
            {
                throw new VisionSdkException(ex.errorCode, "保存方案", ex);
            }
        }

        private void TryPersist(ParameterApplyResult result)
        {
            try
            {
                SaveCore();
            }
            catch (Exception ex)
            {
                result.Failures.Add("写入方案文件失败：" + ex.Message);
            }
        }

        private static void ValidateSnapshotModule(string moduleFullName, ParameterSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Values == null)
                throw new VisionInspectException("参数预设为空或格式无效。");
            if (!string.IsNullOrWhiteSpace(snapshot.ModuleFullName) &&
                !string.Equals(moduleFullName, snapshot.ModuleFullName, StringComparison.OrdinalIgnoreCase))
                throw new VisionInspectException("参数预设属于其他模块：" + snapshot.ModuleFullName);
        }

        private void CaptureDefaultParameters(VisionSolutionInfo solution)
        {
            // 同名 .sol 可以来自不同产品目录，默认参数必须按规范绝对路径隔离。
            string solutionKey;
            using (var hash = SHA256.Create())
            {
                byte[] pathHash = hash.ComputeHash(Encoding.UTF8.GetBytes(
                    Path.GetFullPath(solution.SolutionPath).ToUpperInvariant()));
                solutionKey = ParameterPresetStore.SafeFileName(
                    Path.GetFileNameWithoutExtension(solution.SolutionPath)) + "_" +
                    BitConverter.ToString(pathHash, 0, 12).Replace("-", string.Empty);
            }
            string defaultDirectory = Path.Combine(_parameterRoot, "defaults", solutionKey);
            Directory.CreateDirectory(defaultDirectory);

            List<ModuleEntry> entries;
            lock (_dataSync) entries = _modules.Values.ToList();

            foreach (ModuleEntry entry in entries)
            {
                if (entry.Module == null) continue;
                try
                {
                    string path = Path.Combine(defaultDirectory,
                        ParameterPresetStore.SafeFileName(entry.Info.FullName) + ".json");

                    ParameterSnapshot snapshot;
                    if (File.Exists(path))
                    {
                        snapshot = ParameterPresetStore.Load(path);
                    }
                    else
                    {
                        snapshot = ParameterAdapter.Capture(entry.Module, entry.Info.FullName);
                        ParameterPresetStore.Save(path, snapshot);
                    }

                    lock (_dataSync) _defaults[entry.Info.FullName] = snapshot;
                }
                catch (Exception ex)
                {
                    LogWarn("记录模块默认参数失败：" + entry.Info.FullName, ex);
                }
            }
        }

        private static bool IsBooleanOutput(string name, List<string> configuredNames)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            if (configuredNames != null && configuredNames.Any(x =>
                !string.IsNullOrWhiteSpace(x) &&
                (string.Equals(name, x, StringComparison.OrdinalIgnoreCase) ||
                 name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)))
                return true;

            return name.StartsWith("Is", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Enable", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("Flag", StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureNotShutdown()
        {
            if (_shutdown) throw new ObjectDisposedException("VisionMasterEngine");
        }

        private void EnsureNoActiveRunCore()
        {
            lock (_runSync)
                if (_activeRunCompletion != null)
                    throw new VisionInspectException("VM 流程仍在运行，请停止检测并等待当前流程完成后再修改或保存参数。");
        }

        private void LogWarn(string message, Exception exception)
        {
            if (_logger != null) _logger.Warn("VisionMaster", message + " " + exception.Message);
        }
    }
}
