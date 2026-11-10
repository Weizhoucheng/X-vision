using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Cameras;
using VisionInspect.Communication;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Algorithms;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;
using VisionInspect.Core.Services;
using VisionInspect.Infrastructure.Images;
using VisionInspect.Infrastructure.Logging;
using VisionInspect.Infrastructure.Paths;
using VisionInspect.Infrastructure.Processing;
using VisionInspect.Infrastructure.Security;
using VisionInspect.Infrastructure.Storage;
using VisionInspect.Infrastructure.SystemServices;
using VisionInspect.VisionMaster;

namespace VisionInspect.App.Bootstrap
{
    /// <summary>
    /// 应用组合根。UI 不直接 new 服务，所有生命周期由这里统一管理。
    /// 退出顺序严格遵循：检测流程 → 方案/视觉引擎 → 相机 → 通讯 → 日志。
    /// </summary>
    public sealed partial class AppBootstrapper : IDisposable
    {
        private readonly AppPaths _paths;
        private readonly ISettingsStore _settingsStore;
        private readonly SemaphoreSlim _shutdownLock = new SemaphoreSlim(1, 1);
        private const int ShutdownStepTimeoutMs = 3000;
        private AppSettings _settings;
        private bool _shutdown;
        private bool _shutdownRequested;

        public AppBootstrapper(AppPaths paths, AppSettings settings, ISettingsStore settingsStore, IAppLogger logger,
            bool synchronizeAutoStart = true)
        {
            _paths = paths;
            _settings = settings;
            _settingsStore = settingsStore;
            Logger = logger;

            string dataRoot = paths.ResolveDataRoot(settings.Storage.DataRoot);
            AppPaths.EnsureDirectory(dataRoot);
            DataRoot = dataRoot;

            ParameterRoot = paths.ResolveParameterDirectory(settings.Vision.ParameterPresetDirectory);

            Logger.Info("App", "数据目录：" + dataRoot);
            Logger.Info("App", "配置目录：" + paths.ConfigDirectory);

            DetectionLog = new DetectionLogRepository(Path.Combine(dataRoot, "logs", "detection"), Logger);
            Archive = new ImageArchiveService(
                Path.Combine(dataRoot, "images"),
                settings.Storage.ArchiveFormat,
                settings.Storage.ArchiveNgOnly,
                settings.Storage.ArchiveQueueCapacity,
                Logger,
                settings.Storage.RetentionDays,
                settings.Storage.ArchiveMemoryLimitMb * 1024L * 1024);

            Auth = new AuthService(paths.UsersFile, Logger, new AdminPasswordStore());
            Statistics = new StatisticsService();
            LocalImages = new LocalImageInputProvider();

            var visionEngine = new VisionMasterEngine(ParameterRoot, settings.Vision, Logger);
            Vision = visionEngine;
            // 检测控制器只注入统一算法接口；VM UI 仍通过 Vision 接口使用原生控件，
            // 两者共享同一个 VisionMasterEngine，不重复加载 .sol。
            Algorithm = new VmAlgorithmEngine(visionEngine, settings.Vision, settings.ResultJudge, Logger);
            Camera = new HikCameraService(Logger);
            Communication = new CommunicationHub(settings, Logger);

            ResultPipeline = new ResultProcessingPipeline(
                DetectionLog, Archive, Communication, Logger, settings.Storage);

            Detection = new DetectionController(
                Algorithm, ResultPipeline, Statistics, Logger,
                settings.Detection, settings.Vision, settings.ResultJudge);

            ReportDirectory = ResolveReportDirectory(dataRoot, settings.Report.ExportDirectory);
            Report = new ExcelReportService(
                DetectionLog,
                ReportDirectory,
                settings,
                Logger);

            Startup = new StartupService("VisionInspect", System.Windows.Forms.Application.ExecutablePath, Logger);
            try
            {
                if (synchronizeAutoStart && Startup.IsEnabled != settings.General.AutoStart)
                    Startup.SetEnabled(settings.General.AutoStart);
            }
            catch (Exception ex) { Logger.Warn("Startup", "同步开机自启配置失败：" + ex.Message); }

            Detection.StateChanged += (s, e) => Logger.Debug("Detection", e.NewState + "：" + e.Message);
            Camera.StatusChanged += (s, e) => Logger.Debug("Camera", e.State + "：" + e.Message);
            Communication.StatusChanged += (s, e) => Logger.Debug("Communication", e.Channel + "：" + e.State);

            Logger.Info("App", string.Format("{0} / {1} 初始化完成。", settings.LineName, settings.StationName));
        }

        public IAppLogger Logger { get; private set; }
        public AppSettings Settings { get { return _settings; } }
        public AppPaths Paths { get { return _paths; } }
        public string DataRoot { get; private set; }
        public string ParameterRoot { get; private set; }
        public string ReportDirectory { get; private set; }

        public IAuthService Auth { get; private set; }
        public IVisionEngine Vision { get; private set; }
        /// <summary>统一算法引擎。检测链路只依赖该接口；VM 模式由适配器包装。</summary>
        public IAlgorithmEngine Algorithm { get; private set; }
        public ICameraService Camera { get; private set; }
        public CommunicationHub Communication { get; private set; }
        public ITriggerSource ExternalTriggers { get { return Communication; } }
        public IStatisticsService Statistics { get; private set; }
        public LocalImageInputProvider LocalImages { get; private set; }
        public IDetectionLogRepository DetectionLog { get; private set; }
        public IImageArchiveService Archive { get; private set; }
        public IReportService Report { get; private set; }
        public IStartupService Startup { get; private set; }
        public DetectionController Detection { get; private set; }
        public ResultProcessingPipeline ResultPipeline { get; private set; }

        public bool IsAdministrator
        {
            get { return Auth != null && Auth.IsAdministrator; }
        }

        /// <summary>退出开始后禁止新的设备和方案操作；超时保留资源，允许再次退出重试。</summary>
        public bool IsShutdownPending { get { return _shutdownRequested; } }

        public async Task<VisionSolutionInfo> LoadSolutionAsync(string solutionPath, string password)
        {
            if (string.IsNullOrWhiteSpace(solutionPath))
                throw new VisionInspectException("方案路径为空。");

            if (Detection.State == DetectionRunState.Running ||
                Detection.State == DetectionRunState.Paused)
            {
                await Detection.StopAsync().ConfigureAwait(true);
            }

            VisionSolutionInfo solution = await Vision.LoadSolutionAsync(
                solutionPath, password, CancellationToken.None).ConfigureAwait(true);
            _settings.LastSolutionPath = solutionPath;
            _settings.Vision.PrimaryProcedureName = solution.PrimaryProcedureName;
            if (!_settings.SolutionCatalog.Any(x =>
                    string.Equals(x, solutionPath, StringComparison.OrdinalIgnoreCase)))
                _settings.SolutionCatalog.Add(solutionPath);
            _settingsStore.Save(_settings);

            if (_settings.Detection.AutoClearStatsOnSolutionChange)
                Statistics.Reset();

            Logger.Audit(Auth.CurrentUser == null ? "-" : Auth.CurrentUser.UserName,
                "LoadSolution", solutionPath);
            return solution;
        }

        public void SaveSettings()
        {
            _settingsStore.Save(_settings);
        }

        public Task ApplySettingsAsync(AppSettings newSettings)
        {
            return ApplyRuntimeSettingsAsync(newSettings);
        }
        private async Task ShutdownCoreAsync()
        {
            await _shutdownLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_shutdown) return;
                _shutdownRequested = true;

                Logger.Info("App", "开始退出资源释放（单步超时 " + ShutdownStepTimeoutMs + " ms）。");

                // 固定顺序：检测 → 相机采集/句柄 → 算法引擎 → 通讯/IO → 归档/配置。
                // 每一步单独 try/catch + 超时，任何一步异常都不能阻止后续释放。
                await RunShutdownStepAsync("停止检测循环", () => Detection.ShutdownAsync())
                    .ConfigureAwait(false);
                await RunShutdownStepAsync("停止相机并释放句柄", () => Camera.ShutdownAsync())
                    .ConfigureAwait(false);
                await RunShutdownStepAsync("释放算法引擎/VM", () => Vision.ShutdownAsync())
                    .ConfigureAwait(false);
                await RunShutdownStepAsync("关闭通讯/IO", () => Communication.StopAsync())
                    .ConfigureAwait(false);
                await RunShutdownStepAsync("归档队列落盘", () => Archive.FlushAsync(CancellationToken.None))
                    .ConfigureAwait(false);

                var disposableArchive = Archive as IDisposable;
                if (disposableArchive != null)
                {
                    try { disposableArchive.Dispose(); }
                    catch (Exception ex) { Logger.Error("Shutdown", "释放归档服务失败。", ex); }
                }

                try { _settingsStore.Save(_settings); } catch (Exception ex) { Logger.Warn("Shutdown", "保存退出配置失败：" + ex.Message); }
                Logger.Info("App", "资源释放流程结束，退出。");
                Logger.Flush();
                _shutdown = true;
            }
            finally
            {
                _shutdownLock.Release();
            }
        }

        private async Task RunShutdownStepAsync(string stepName, Func<Task> action)
        {
            Task step = null;
            try
            {
                step = action();
                Task completed = await Task.WhenAny(step, Task.Delay(ShutdownStepTimeoutMs))
                    .ConfigureAwait(false);
                if (completed != step)
                {
                    Logger.Error("Shutdown", string.Format("{0}超时（{1} ms），强制跳过。",
                        stepName, ShutdownStepTimeoutMs));
                    // 观察后台任务异常，避免未观察异常影响进程退出。
                    _ = step.ContinueWith(t => { var ignored = t.Exception; },
                        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);
                    return;
                }
                await step.ConfigureAwait(false);
                Logger.Info("Shutdown", stepName + "完成。");
            }
            catch (Exception ex)
            {
                Logger.Error("Shutdown", stepName + "失败，继续后续释放。", ex);
            }
        }

        private static string ResolveReportDirectory(string dataRoot, string configured)
        {
            if (string.IsNullOrWhiteSpace(configured))
                return Path.Combine(dataRoot, "reports");

            string expanded = Environment.ExpandEnvironmentVariables(configured.Trim());
            if (!Path.IsPathRooted(expanded))
                expanded = Path.Combine(dataRoot, expanded);
            expanded = Path.GetFullPath(expanded);
            Directory.CreateDirectory(expanded);
            return expanded;
        }

        public void Dispose()
        {
            try { ShutdownAsync().GetAwaiter().GetResult(); } catch { }
            try { (Logger as IDisposable)?.Dispose(); } catch { }
        }
    }
}
