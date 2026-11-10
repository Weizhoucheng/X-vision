using VisionInspect.Core.Configuration;

namespace VisionInspect.Core.Abstractions
{
    public interface ISettingsStore
    {
        string SettingsFilePath { get; }
        AppSettings Load();
        void Save(AppSettings settings);
    }
}
