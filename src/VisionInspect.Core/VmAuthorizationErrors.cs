namespace VisionInspect.Core
{
    /// <summary>区分官方授权失败和 DLL/驱动缺失，保持底层错误码供程序处理。</summary>
    public static class VmAuthorizationErrors
    {
        public static bool IsAuthorizationError(int code) { uint value = unchecked((uint)code); return value >= 0xE0000700 && value <= 0xE00007FF; }
        public static string GetGuidance(int code)
        {
            switch (unchecked((uint)code)) {
                case 0xE0000700: return "VM 当前未检测到可用硬件授权，或授权检测异常。若需不插 USB 加密狗运行，请取得覆盖当前版本和算法的官方软授权并完成激活。二次开发和安装驱动不会自动获得运行许可。";
                case 0xE0000701: return "VM 硬件授权检测异常，请检查设备连接和官方授权驱动。";
                case 0xE0000702: return "现有 VM 许可未通过算法库授权校验，请核对方案所用算法是否包含在许可中。";
                case 0xE0000703: return "VM 算法库授权已过期，请使用官方授权工具核对许可。";
                case 0xE0000704: return "VM 未检测到可用软授权，或软授权检测异常，请核对激活状态和所选授权方式。";
                case 0xE0000705: return "VM 软授权尚未激活，请在官方激活工具中使用本机对应的授权码或离线授权文件。";
                case 0xE0000706: case 0xE000070E: return "当前软授权未覆盖所需功能，请核对算法和 SDK 使用范围。";
                case 0xE0000707: return "VM 软授权功能已过期，请核对许可有效期。";
                case 0xE000070A: return "VM 官方软授权运行服务/驱动未安装，请使用匹配版本的官方安装程序。";
                case 0xE000071B: return "VM 网络授权校验失败，请检查官方授权服务器配置及网络。";
                case 0xE000071C: return "VM 网络授权并发数已达到上限，请释放占用或核对许可。";
                default: return IsAuthorizationError(code) ? "VM 官方授权校验未通过，请核对授权方式、功能范围、有效期与官方运行服务。" : string.Empty;
            }
        }
    }
}
