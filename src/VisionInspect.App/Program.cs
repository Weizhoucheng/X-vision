using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using VisionInspect.App.Bootstrap;
using VisionInspect.App.Runtime;
using VisionInspect.App.UI;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;
using VisionInspect.Infrastructure.Configuration;
using VisionInspect.Infrastructure.Logging;
using VisionInspect.Infrastructure.Paths;

namespace VisionInspect.App
{
    internal static class Program
    {
        private static Mutex _singleInstance;
        private static FileAppLogger _logger;
        private static AppBootstrapper _bootstrapper;

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            if(args!=null&&args.Any(a=>string.Equals(a,"--runtime-check",StringComparison.OrdinalIgnoreCase)))
            {
                var store=new JsonSettingsStore(AppPaths.Create(ParseDataRoot(args)),null);
                var configuration=store.Load();
                Theme.SetDark(string.Equals(configuration.UiTheme,"Dark",StringComparison.OrdinalIgnoreCase));
                Application.Run(new RuntimeEnvironmentForm(configuration.VisionMasterInstallDir));
                return;
            }

            bool createdNew;
            _singleInstance = new Mutex(true, @"Global\VisionInspect_4_3_x64", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("VisionInspect·视觉检测系统 已在运行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                Application.ThreadException += OnThreadException;

                if (!Environment.Is64BitProcess)
                {
                    MessageBox.Show("本程序必须在 x64 模式下运行。请检查项目平台目标是否为 x64。",
                        "平台错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string dataRoot = ParseDataRoot(args);
                AppPaths paths = AppPaths.Create(dataRoot);
                ISettingsStore settingsStore = new JsonSettingsStore(paths, null);
                AppSettings settings = settingsStore.Load();
                Theme.SetDark(string.Equals(settings.UiTheme, "Dark", StringComparison.OrdinalIgnoreCase));
                BrandAssets.WarmUp();

                _logger = new FileAppLogger(
                    paths.SystemLogDirectory,
                    paths.AuditLogDirectory,
                    settings.Storage.RetentionDays,
                    settings.General.ShowDebugLog);

                _logger.Info("App", string.Format("程序启动，版本 {0}，数据目录 {1}。",
                    Assembly.GetExecutingAssembly().GetName().Version, paths.Root));
                PrepareMvsNativePath();

                VisionMasterEnvironment.Prepare(settings, _logger);
                if (string.IsNullOrWhiteSpace(settings.VisionMasterInstallDir))
                {
                    settings.VisionMasterInstallDir = VisionMasterEnvironment.InstallRoot;
                    settingsStore.Save(settings);
                }

                _bootstrapper = new AppBootstrapper(paths, settings, settingsStore, _logger);

                using (var login = new LoginForm(_bootstrapper.Auth, _bootstrapper.Logger,
                           settings.LastUserName))
                {
                    if (login.ShowDialog() != DialogResult.OK)
                    {
                        ShutdownSynchronously();
                        return;
                    }
                }

                settings.LastUserName = _bootstrapper.Auth.CurrentUser == null
                    ? string.Empty
                    : _bootstrapper.Auth.CurrentUser.UserName;
                settingsStore.Save(settings);

                Application.Run(new MainForm(_bootstrapper));
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("Startup", "程序启动失败。", ex);
                MessageBox.Show(
                    "程序启动失败：\r\n" + ex.Message +
                    "\r\n\r\n请检查 VisionMaster 4.3.0(MVS) 安装、x64 平台目标及配置文件。",
                    "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ShutdownSynchronously();
                try
                {
                    if (_singleInstance != null) _singleInstance.ReleaseMutex();
                }
                catch { }
                try { _singleInstance.Dispose(); } catch { }
            }
        }

        private static string ParseDataRoot(string[] args)
        {
            if (args == null) return null;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--data=", StringComparison.OrdinalIgnoreCase))
                    return arg.Substring("--data=".Length).Trim('"');
            }
            return null;
        }

        private static void PrepareMvsNativePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(baseDir, "MvCameraControl.dll"))) return;

            string[] candidates =
            {
                Environment.GetEnvironmentVariable("MVS_RUNTIME_DIR") ?? string.Empty,
                @"C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64",
                @"C:\Program Files\Common Files\MVS\Runtime\Win64_x64",
                @"C:\Program Files (x86)\MVDC\MVS\Runtime\Win64_x64",
                @"C:\Program Files\MVS\Runtime\Win64_x64",
                @"C:\Program Files (x86)\MVS\Runtime\Win64_x64"
            };

            string extra = string.Join(";", candidates.Where(x => !string.IsNullOrWhiteSpace(x) && Directory.Exists(x)));
            if (string.IsNullOrWhiteSpace(extra)) return;

            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            Environment.SetEnvironmentVariable("PATH", extra + ";" + path, EnvironmentVariableTarget.Process);
        }

        private static void ShutdownSynchronously()
        {
            try
            {
                if (_bootstrapper != null)
                    _bootstrapper.ShutdownAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("Shutdown", "退出清理异常。", ex);
            }
            finally
            {
                if (_logger != null)
                {
                    _logger.Flush();
                    _logger.Dispose();
                    _logger = null;
                }
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            try
            {
                if (_logger != null) _logger.Error("UI", "未处理的 UI 线程异常。", e.Exception);
                MessageBox.Show("界面发生异常：" + e.Exception.Message +
                                "\r\n详细信息已写入系统日志。", "异常",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                Exception ex = e.ExceptionObject as Exception;
                if (_logger != null) _logger.Error("App", "未处理的应用程序异常。", ex);
                if (_logger != null) _logger.Flush();
            }
            catch { }
        }
    }
}
