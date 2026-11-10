using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.ServiceProcess;

namespace VisionInspect.VisionMaster.Runtime
{
    public sealed class VmRuntimeReport
    {
        public string InstallRoot { get; set; }
        public string RuntimeDirectory { get; set; }
        public string SdkDirectory { get; set; }
        public int? ConfiguredAuthorizationMode { get; set; }
        public string AuthorizationModeName { get; set; }
        public string AuthorizationStatus { get; set; }
        public string ActivationTool { get; set; }
        public string AuthorizationService { get; set; }
        public List<string> MissingFiles { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public bool FilesReady { get { return MissingFiles.Count == 0 && !string.IsNullOrEmpty(InstallRoot); } }
        public DateTime CheckedAt { get; set; }
    }
    /// <summary>只读诊断，不初始化 SDK，不检查或修改许可内容，不把文件存在当作授权有效。</summary>
    public static class VmRuntimeDiagnostics
    {
        public static string ModeName(int? mode)
        {
            switch(mode) { case 0: return "本机 USB 加密狗"; case 1: return "Sentinel 软授权"; case 2: return "Virbox 软授权"; case 3: return "网络授权"; case 4: return "Virbox 多方式检测"; default: return "未确认"; }
        }
        public static VmRuntimeReport Inspect(string configuredRoot)
        {
            var report = new VmRuntimeReport { CheckedAt = DateTime.Now, AuthorizationStatus = "未验证；需通过实际方案加载及算法运行确认" };
            string root = VisionMasterEnvironment.ResolveInstallRoot(configuredRoot);
            if (string.IsNullOrWhiteSpace(root)) { report.MissingFiles.Add("未找到 VisionMaster 4.3.0 官方运行环境"); return report; }
            report.InstallRoot = Path.GetFullPath(root);
            report.RuntimeDirectory = Directory.Exists(Path.Combine(root,"Applications")) ? Path.Combine(root,"Applications") : Path.Combine(root,"Application");
            report.SdkDirectory = Path.Combine(root,"Development","V4.x","ComControls","Assembly");
            foreach(string name in new[] { "VM.Core.dll", "VM.PlatformSDKCS.dll", "ImageSourceModuleCs.dll", "FormatModuleCs.dll" }) if (!File.Exists(Path.Combine(report.SdkDirectory,name))) report.MissingFiles.Add(name);
            if (!Directory.Exists(report.RuntimeDirectory)) report.MissingFiles.Add("Application/Applications");
            string configuration = Path.Combine(report.RuntimeDirectory,"Server","ServerAuthConfig.ini");
            try {
                if (File.Exists(configuration)) { var match = Regex.Match(File.ReadAllText(configuration), @"^\s*AuthorizeType\s*=\s*(\d+)\b", RegexOptions.Multiline|RegexOptions.IgnoreCase); int mode; if (match.Success && int.TryParse(match.Groups[1].Value,out mode)) report.ConfiguredAuthorizationMode = mode; }
                else report.Warnings.Add("未找到官方授权方式配置文件");
            } catch (Exception ex) { report.Warnings.Add("无法读取授权方式：" + ex.Message); }
            report.AuthorizationModeName = ModeName(report.ConfiguredAuthorizationMode);
            try { using(var service=new ServiceController("Sense Shield Service")) report.AuthorizationService=service.Status.ToString(); }
            catch { report.AuthorizationService="未确认（未安装或无查询权限）"; }
            string tool = Path.Combine(report.RuntimeDirectory,"Tools","AuthActiveTool","SoftEncryptActive.exe");
            if (File.Exists(tool)) report.ActivationTool = tool;
            else report.Warnings.Add("未找到官方软授权激活工具");
            report.Warnings.Add("驱动或运行库安装完成不代表许可已激活。");
            return report;
        }
    }
}
