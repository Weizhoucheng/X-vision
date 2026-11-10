using System;
using System.IO;

namespace VisionInspect.Infrastructure.Paths
{
    /// <summary>
    /// 应用目录规划。
    /// 配置/日志默认放 %ProgramData%\VisionInspect；若当前用户无写权限，自动回退到 %LocalAppData%\VisionInspect。
    /// 大容量检测图片可通过 appsettings.json 的 Storage.DataRoot 指向独立数据盘。
    /// </summary>
    public sealed class AppPaths
    {
        private AppPaths(string root)
        {
            Root = Path.GetFullPath(root);
            ConfigDirectory = Path.Combine(Root, "config");
            LogDirectory = Path.Combine(Root, "logs");
            SystemLogDirectory = Path.Combine(LogDirectory, "system");
            AuditLogDirectory = Path.Combine(LogDirectory, "audit");
            DetectionLogDirectory = Path.Combine(LogDirectory, "detection");
            ReportDirectory = Path.Combine(Root, "reports");
            DefaultDataRoot = Path.Combine(Root, "data");
            ParameterDirectory = Path.Combine(Root, "parameters");
            SettingsFile = Path.Combine(ConfigDirectory, "appsettings.json");
            UsersFile = Path.Combine(ConfigDirectory, "users.json");
            EnsureDirectory(ConfigDirectory);
            EnsureDirectory(LogDirectory);
            EnsureDirectory(SystemLogDirectory);
            EnsureDirectory(AuditLogDirectory);
            EnsureDirectory(DetectionLogDirectory);
            EnsureDirectory(ReportDirectory);
            EnsureDirectory(ParameterDirectory);
        }

        public string Root { get; private set; }
        public string ConfigDirectory { get; private set; }
        public string LogDirectory { get; private set; }
        public string SystemLogDirectory { get; private set; }
        public string AuditLogDirectory { get; private set; }
        public string DetectionLogDirectory { get; private set; }
        public string ReportDirectory { get; private set; }
        public string DefaultDataRoot { get; private set; }
        public string ParameterDirectory { get; private set; }
        public string SettingsFile { get; private set; }
        public string UsersFile { get; private set; }

        public static AppPaths Create(string explicitRoot = null)
        {
            if (!string.IsNullOrWhiteSpace(explicitRoot))
            {
                AppPaths explicitPaths = new AppPaths(explicitRoot);
                explicitPaths.AssertWritable();
                return explicitPaths;
            }

            string programData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VisionInspect");
            try
            {
                AppPaths paths = new AppPaths(programData);
                paths.AssertWritable();
                return paths;
            }
            catch
            {
                string local = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisionInspect");
                AppPaths paths = new AppPaths(local);
                paths.AssertWritable();
                return paths;
            }
        }

        /// <summary>解析实际数据根目录；支持相对路径和系统环境变量。</summary>
        public string ResolveDataRoot(string configured)
        {
            if (string.IsNullOrWhiteSpace(configured)) return DefaultDataRoot;

            string expanded = Environment.ExpandEnvironmentVariables(configured.Trim());
            if (!Path.IsPathRooted(expanded))
                expanded = Path.Combine(Root, expanded);

            return Path.GetFullPath(expanded);
        }

        public string ResolveParameterDirectory(string configured)
        {
            if (string.IsNullOrWhiteSpace(configured)) return ParameterDirectory;

            string expanded = Environment.ExpandEnvironmentVariables(configured.Trim());
            if (!Path.IsPathRooted(expanded))
                expanded = Path.Combine(Root, expanded);
            return Path.GetFullPath(expanded);
        }

        private void AssertWritable()
        {
            string probe = Path.Combine(Root, ".write_probe_" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
        }

        public static void EnsureDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            Directory.CreateDirectory(path);
        }
    }
}
