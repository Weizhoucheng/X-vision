using System;

namespace VisionInspect.Core.Models
{
    /// <summary>相机设备描述。Id 使用“传输层:序列号”作为稳定标识。</summary>
    public sealed class CameraDeviceInfo
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string Manufacturer { get; set; }
        public string Transport { get; set; }
        public string IpAddress { get; set; }
        public string UserDefinedName { get; set; }

        public CameraDeviceInfo()
        {
            Id = string.Empty;
            DisplayName = string.Empty;
            Model = string.Empty;
            SerialNumber = string.Empty;
            Manufacturer = string.Empty;
            Transport = string.Empty;
            IpAddress = string.Empty;
            UserDefinedName = string.Empty;
        }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(DisplayName)) return DisplayName;
            return string.Format("{0} / {1}", Model, SerialNumber);
        }
    }

    public sealed class LogEntryEventArgs : EventArgs
    {
        public LogEntryEventArgs(LogLevel level, string area, string message, DateTime timestamp)
        {
            Level = level;
            Area = area ?? string.Empty;
            Message = message ?? string.Empty;
            Timestamp = timestamp;
        }

        public LogLevel Level { get; private set; }
        public string Area { get; private set; }
        public string Message { get; private set; }
        public DateTime Timestamp { get; private set; }
    }
}
