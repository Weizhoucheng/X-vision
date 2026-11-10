using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Abstractions;
using VisionInspect.Infrastructure.Configuration;
using VisionInspect.Infrastructure.Logging;
using VisionInspect.Infrastructure.Storage;
using VisionInspect.VisionMaster;

namespace VisionInspect.App.Bootstrap
{
    public sealed partial class AppBootstrapper
    {
        private readonly SemaphoreSlim _settingsGate = new SemaphoreSlim(1, 1);
        public async Task ShutdownAsync()
        {
            _shutdownRequested = true;
            await _settingsGate.WaitAsync().ConfigureAwait(false);
            try { await ShutdownCoreAsync().ConfigureAwait(false); }
            finally { _settingsGate.Release(); }
        }
        public bool IsApplyingSettings { get; private set; }
        private static T Snapshot<T>(T value) { return JsonHelper.Deserialize<T>(JsonHelper.Serialize(value)); }
        private static bool Different<T>(T a, T b) { return JsonHelper.Serialize(a) != JsonHelper.Serialize(b); }
        private static void CopyValues<T>(T from, T to)
        { foreach (var property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)) if (property.CanRead && property.CanWrite) property.SetValue(to, property.GetValue(from, null), null); }
        private static void Writable(string directory)
        {
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, ".xvision-write-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        }
        private async Task ApplyRuntimeSettingsAsync(AppSettings value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (!IsAdministrator) throw new UnauthorizedAccessException("系统设置需要管理员权限。");
            var next = Snapshot(value); next.Normalize();
            await _settingsGate.WaitAsync().ConfigureAwait(true);
            ImageArchiveService stagedArchive = null;
            var before = Snapshot(_settings);
            bool cameraApplied = false, startupApplied = false, communicationApplied = false;
            try {
                if (_shutdownRequested) throw new InvalidOperationException("程序正在退出。");
                IsApplyingSettings = true;
                await Detection.StopAsync().ConfigureAwait(true);
                string dataRoot = _paths.ResolveDataRoot(next.Storage.DataRoot);
                string parameterRoot = _paths.ResolveParameterDirectory(next.Vision.ParameterPresetDirectory);
                string reports = await Task.Run(() => ResolveReportDirectory(dataRoot, next.Report.ExportDirectory)).ConfigureAwait(true);
                await Task.Run(() => { Writable(dataRoot); Writable(parameterRoot); Writable(reports); }).ConfigureAwait(true);
                bool storageChanged = Different(before.Storage, next.Storage);
                IDetectionLogRepository repository = DetectionLog;
                if (storageChanged) {
                    using (var timeout = new CancellationTokenSource(5000)) await Archive.FlushAsync(timeout.Token).ConfigureAwait(true);
                    stagedArchive = await Task.Run(() => new ImageArchiveService(Path.Combine(dataRoot, "images"), next.Storage.ArchiveFormat,
                        next.Storage.ArchiveNgOnly, next.Storage.ArchiveQueueCapacity, Logger, next.Storage.RetentionDays,
                        next.Storage.ArchiveMemoryLimitMb * 1024L * 1024, false)).ConfigureAwait(true);
                    repository = await Task.Run(() => new DetectionLogRepository(Path.Combine(dataRoot, "logs", "detection"), Logger)).ConfigureAwait(true);
                }
                if (Different(before.Camera, next.Camera)) {
                    await Camera.ApplySettingsAsync(next.Camera, CancellationToken.None).ConfigureAwait(true); cameraApplied = true;
                }
                if (before.General.AutoStart != next.General.AutoStart) { Startup.SetEnabled(next.General.AutoStart); startupApplied = true; }
                if (Different(before.Communication, next.Communication)) {
                    communicationApplied = true;
                    await Communication.ReconfigureAsync(next.Communication, CancellationToken.None).ConfigureAwait(true);
                }
                // 先完成可能失败的运行时操作与配置写入，再发布新的服务引用。
                next.UiTheme = _settings.UiTheme;
                var report = await Task.Run(() => new ExcelReportService(repository, reports, _settings, Logger)).ConfigureAwait(true);
                await Task.Run(() => ((VisionMasterEngine)Vision).SetParameterDirectory(parameterRoot)).ConfigureAwait(true);
                await Task.Run(() => _settingsStore.Save(next)).ConfigureAwait(true);

                CopyValues(next.General, _settings.General); CopyValues(next.Detection, _settings.Detection);
                CopyValues(next.Vision, _settings.Vision); CopyValues(next.Input, _settings.Input);
                CopyValues(next.Camera, _settings.Camera); CopyValues(next.Storage, _settings.Storage);
                CopyValues(next.ResultJudge, _settings.ResultJudge); CopyValues(next.Report, _settings.Report);
                _settings.LineName = next.LineName; _settings.StationName = next.StationName;
                _settings.Communication = next.Communication;
                DataRoot = dataRoot; ParameterRoot = parameterRoot; ReportDirectory = reports;
                var retiredArchive = Archive;
                if (stagedArchive != null) Archive = stagedArchive;
                DetectionLog = repository; Report = report;
                ResultPipeline.ReconfigureStorage(DetectionLog, Archive);
                stagedArchive = null;
                (Logger as FileAppLogger)?.Configure(next.Storage.RetentionDays, next.General.ShowDebugLog);
                LocalImages.PlaybackMode = next.Input.LocalPlaybackMode;
                if (!ReferenceEquals(retiredArchive, Archive)) await Task.Run(() => (retiredArchive as IDisposable)?.Dispose()).ConfigureAwait(true);
                if (storageChanged) await Task.Run(() => ((ImageArchiveService)Archive).ApplyRetention()).ConfigureAwait(true);
                Logger.Audit(Auth.CurrentUser?.UserName ?? "-", "ApplySettings", "配置已保存并立即应用；检测已停止，无需重启。");
            } catch {
                if (communicationApplied) try { await Communication.ReconfigureAsync(before.Communication, CancellationToken.None).ConfigureAwait(true); } catch (Exception ex) { Logger.Error("Settings", "恢复通信配置失败", ex); }
                if (cameraApplied) try { await Camera.ApplySettingsAsync(before.Camera, CancellationToken.None).ConfigureAwait(true); } catch (Exception ex) { Logger.Error("Settings", "恢复相机配置失败", ex); }
                if (startupApplied) try { Startup.SetEnabled(before.General.AutoStart); } catch (Exception ex) { Logger.Error("Settings", "恢复自启动配置失败", ex); }
                try { ((VisionMasterEngine)Vision).SetParameterDirectory(ParameterRoot); } catch { }
                if (stagedArchive != null) await Task.Run(() => stagedArchive.Dispose()).ConfigureAwait(true);
                throw;
            } finally { IsApplyingSettings = false; _settingsGate.Release(); }
        }
    }
}
