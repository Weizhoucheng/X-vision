using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;
namespace VisionInspect.App.Runtime
{
    /// <summary>兼容应用原有入口；GUI 与独立 SDK 使用同一运行时定位和加载实现。</summary>
    public static class VisionMasterEnvironment
    {
        public static string InstallRoot { get { return VisionMaster.Runtime.VisionMasterEnvironment.InstallRoot; } }
        public static string AssemblyDirectory { get { return VisionMaster.Runtime.VisionMasterEnvironment.AssemblyDirectory; } }
        public static void Prepare(AppSettings settings, IAppLogger logger) { VisionMaster.Runtime.VisionMasterEnvironment.Prepare(settings, logger); }
        public static string ResolveInstallRoot(string configured) { return VisionMaster.Runtime.VisionMasterEnvironment.ResolveInstallRoot(configured); }
    }
}
