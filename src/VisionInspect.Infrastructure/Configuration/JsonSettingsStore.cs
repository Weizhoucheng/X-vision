using System;
using System.IO;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;
using VisionInspect.Infrastructure.Paths;

namespace VisionInspect.Infrastructure.Configuration
{
    /// <summary>线程安全的 JSON 配置仓库。</summary>
    public sealed class JsonSettingsStore : ISettingsStore
    {
        private readonly object _sync = new object();
        private readonly AppPaths _paths;
        private readonly IAppLogger _logger;

        public JsonSettingsStore(AppPaths paths, IAppLogger logger)
        {
            if (paths == null) throw new ArgumentNullException("paths");
            _paths = paths;
            _logger = logger;
        }

        public string SettingsFilePath
        {
            get { return _paths.SettingsFile; }
        }

        public AppSettings Load()
        {
            lock (_sync)
            {
                if (!File.Exists(SettingsFilePath))
                {
                    AppSettings defaults = new AppSettings();
                    defaults.Normalize();
                    Save(defaults);
                    return defaults;
                }

                try
                {
                    AppSettings settings = JsonHelper.LoadFile<AppSettings>(SettingsFilePath);
                    if (settings == null) settings = new AppSettings();
                    settings.Normalize();
                    return settings;
                }
                catch (Exception ex)
                {
                    string backup = SettingsFilePath + ".corrupt." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    try { File.Copy(SettingsFilePath, backup, true); } catch { }
                    if (_logger != null)
                        _logger.Error("Settings", "配置文件损坏，已使用默认配置。备份：" + backup, ex);

                    AppSettings defaults = new AppSettings();
                    defaults.Normalize();
                    Save(defaults);
                    return defaults;
                }
            }
        }

        public void Save(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            lock (_sync)
            {
                settings.Normalize();
                JsonHelper.SaveFileAtomic(SettingsFilePath, settings);
            }
        }
    }
}
