using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;

namespace VisionInspect.VisionMaster.Runtime
{
    /// <summary>
    /// VisionMaster 运行环境准备。必须在任何 VM 类型被 JIT/加载之前调用。
    ///
    /// 做三件事：
    /// 1) 找到 VisionMaster 4.3.0 安装目录；
    /// 2) 将工作目录/PATH 指向 VM Application 与 SDK Assembly 目录；
    /// 3) 注册 AssemblyResolve，按需加载 VM 托管 DLL（项目引用设置为 Copy Local=false）。
    ///
    /// 注意：修改 CurrentDirectory 是 VM 官方示例的兼容做法；本应用所有业务路径均为绝对路径。
    /// </summary>
    public static class VisionMasterEnvironment
    {
        private static readonly object Sync = new object();
        private static bool _initialized;
        private static string _installRoot;
        private static string _assemblyDirectory;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        public static string InstallRoot
        {
            get { return _installRoot; }
        }

        public static string AssemblyDirectory
        {
            get { return _assemblyDirectory; }
        }

        public static void Prepare(AppSettings settings, IAppLogger logger)
        {
            lock (Sync)
            {
                if (_initialized) return;

                string root = ResolveInstallRoot(settings == null ? null : settings.VisionMasterInstallDir);
                if (string.IsNullOrWhiteSpace(root))
                {
                    throw new VisionInspectException(
                        "未找到 VisionMaster 4.3.0 安装目录。请在 config\\appsettings.json 中设置 VisionMasterInstallDir，" +
                        "或设置环境变量 VISIONMASTER_ROOT。");
                }

                // VM 4.3.0 安装包实际可能是 Applications（复数），旧文档/部分版本是 Application。
                string applicationDir = Path.Combine(root, "Applications");
                if (!Directory.Exists(applicationDir))
                    applicationDir = Path.Combine(root, "Application");
                string assemblyDir = Path.Combine(root, "Development", "V4.x", "ComControls", "Assembly");
                if (!File.Exists(Path.Combine(assemblyDir, "VM.Core.dll")))
                {
                    throw new VisionInspectException(
                        "VisionMaster 目录不完整，缺少 Development\\V4.x\\ComControls\\Assembly\\VM.Core.dll：" + root);
                }

                if (!Directory.Exists(applicationDir))
                {
                    throw new VisionInspectException("VisionMaster 目录不完整，缺少 Application/Applications 目录：" + root);
                }

                _installRoot = root;
                _assemblyDirectory = assemblyDir;

                // 先注册解析器，再触碰任何 VM 类型。
                AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

                // 追加 PATH，兼容 VM 原生 DLL 的依赖搜索。
                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                string extraPath = string.Join(";", new[]
                {
                    applicationDir,
                    assemblyDir,
                    Path.Combine(root, "Module(sp)", "x64")
                }.Where(Directory.Exists));
                Environment.SetEnvironmentVariable("PATH", extraPath + ";" + currentPath, EnvironmentVariableTarget.Process);

                try
                {
                    Environment.CurrentDirectory = applicationDir;
                }
                catch (Exception ex)
                {
                    if (logger != null) logger.Warn("Environment", "切换工作目录失败：" + ex.Message);
                }

                try
                {
                    SetDllDirectory(applicationDir);
                }
                catch
                {
                    // 非 Windows 或受限环境忽略；目标平台明确为 Windows x64。
                }

                _initialized = true;
                if (logger != null)
                {
                    logger.Info("Environment", "VisionMaster 运行环境：" + root);
                    logger.Info("Environment", "SDK Assembly：" + assemblyDir);
                }
            }
        }

        public static string ResolveInstallRoot(string configured)
        {
            var candidates = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(configured))
                candidates.Add(Environment.ExpandEnvironmentVariables(configured.Trim()));

            string env = Environment.GetEnvironmentVariable("VISIONMASTER_ROOT");
            if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env);

            string[] drives = { "C", "D", "E", "F" };
            foreach (string drive in drives)
            {
                candidates.Add(string.Format(@"{0}:\Program Files\VisionMaster4.3.0", drive));
                candidates.Add(string.Format(@"{0}:\Program Files (x86)\VisionMaster4.3.0", drive));
                candidates.Add(string.Format(@"{0}:\VisionMaster4.3.0", drive));
                candidates.Add(string.Format(@"{0}:\Program Files\VisionMaster_EDU4.3.0", drive));
            }

            foreach (string path in candidates)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    string full = Path.GetFullPath(path);
                    if (File.Exists(Path.Combine(full, "Development", "V4.x", "ComControls", "Assembly", "VM.Core.dll")) &&
                        (Directory.Exists(Path.Combine(full, "Applications")) ||
                         Directory.Exists(Path.Combine(full, "Application"))))
                        return full;
                }
                catch
                {
                    // 非法路径继续尝试下一个候选。
                }
            }

            return null;
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            try
            {
                string simpleName = new AssemblyName(args.Name).Name;
                if (string.IsNullOrWhiteSpace(simpleName)) return null;

                string[] directories =
                {
                    _assemblyDirectory,
                    Path.Combine(_installRoot ?? string.Empty, "Applications"),
                    Path.Combine(_installRoot ?? string.Empty, "Application"),
                    Path.Combine(_installRoot ?? string.Empty, "Module(sp)", "x64"),
                    AppDomain.CurrentDomain.BaseDirectory
                };

                foreach (string directory in directories)
                {
                    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) continue;
                    string dll = Path.Combine(directory, simpleName + ".dll");
                    if (File.Exists(dll))
                        return Assembly.LoadFrom(dll);
                }
            }
            catch
            {
                // 返回 null 让运行时继续默认探测。
            }

            return null;
        }
    }
}
