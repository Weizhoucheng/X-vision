using System;
using System.IO;
using Microsoft.Win32;
using VisionInspect.Core.Abstractions;

namespace VisionInspect.Infrastructure.SystemServices
{
    /// <summary>
    /// 通过 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 实现当前用户开机自启。
    /// 不使用 HKLM，避免每次设置都触发 UAC。
    /// </summary>
    public sealed class StartupService : IStartupService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private readonly string _valueName;
        private readonly string _executablePath;
        private readonly IAppLogger _logger;

        public StartupService(string valueName, string executablePath, IAppLogger logger)
        {
            if (string.IsNullOrWhiteSpace(valueName)) throw new ArgumentException("valueName");
            if (string.IsNullOrWhiteSpace(executablePath)) throw new ArgumentException("executablePath");
            _valueName = valueName;
            _executablePath = Path.GetFullPath(executablePath);
            _logger = logger;
        }

        public bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    {
                        string value = key == null ? null : key.GetValue(_valueName) as string;
                        return !string.IsNullOrWhiteSpace(value) &&
                               value.IndexOf(_executablePath, StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        public void SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) throw new InvalidOperationException("无法打开注册表启动项。");

                    if (enabled)
                        key.SetValue(_valueName, "\"" + _executablePath + "\" --autostart", RegistryValueKind.String);
                    else
                        key.DeleteValue(_valueName, false);
                }

                if (_logger != null)
                {
                    _logger.Info("Startup", enabled ? "已启用开机自启。" : "已禁用开机自启。");
                    _logger.Audit("system", "ChangeAutoStart", enabled ? "Enabled" : "Disabled");
                }
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Error("Startup", "修改开机自启失败。", ex);
                throw;
            }
        }
    }
}
