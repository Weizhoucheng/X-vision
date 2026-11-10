using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VisionInspect.Core.Configuration
{
    /// <summary>
    /// 应用配置根对象。所有配置均保存在 config\appsettings.json。
    /// 设置在停止检测的边界提交；运行时服务同步应用配置，无需重启。
    /// </summary>
    public sealed class AppSettings
    {
        public int SchemaVersion { get; set; }
        public string LineName { get; set; }
        public string StationName { get; set; }
        public string VisionMasterInstallDir { get; set; }
        public string LastSolutionPath { get; set; }
        public string LastUserName { get; set; }
        /// <summary>界面主题：Light=白天浅色，Dark=深色夜间。</summary>
        public string UiTheme { get; set; }
        public List<string> SolutionCatalog { get; set; }
        public GeneralSettings General { get; set; }
        public VisionSettings Vision { get; set; }
        public DetectionSettings Detection { get; set; }
        public InputSettings Input { get; set; }
        public CameraSettings Camera { get; set; }
        public StorageSettings Storage { get; set; }
        public CommunicationSettings Communication { get; set; }
        public ResultJudgeOptions ResultJudge { get; set; }
        public ReportSettings Report { get; set; }

        public AppSettings()
        {
            SchemaVersion = 1;
            LineName = "产线 A";
            StationName = "视觉检测工位";
            VisionMasterInstallDir = string.Empty;
            LastSolutionPath = string.Empty;
            LastUserName = string.Empty;
            UiTheme = "Light";
            SolutionCatalog = new List<string>();
            General = new GeneralSettings();
            Vision = new VisionSettings();
            Detection = new DetectionSettings();
            Input = new InputSettings();
            Camera = new CameraSettings();
            Storage = new StorageSettings();
            Communication = new CommunicationSettings();
            ResultJudge = new ResultJudgeOptions();
            Report = new ReportSettings();
        }

        /// <summary>修复手工编辑 JSON 可能造成的空对象/越界值，保证服务构造不会抛空引用。</summary>
        public void Normalize()
        {
            if (SchemaVersion <= 0) SchemaVersion = 1;
            LineName = LineName ?? string.Empty;
            StationName = StationName ?? string.Empty;
            VisionMasterInstallDir = VisionMasterInstallDir ?? string.Empty;
            LastSolutionPath = LastSolutionPath ?? string.Empty;
            LastUserName = LastUserName ?? string.Empty;
            UiTheme = string.IsNullOrWhiteSpace(UiTheme) ? "Light" : UiTheme;
            if (!string.Equals(UiTheme, "Dark", StringComparison.OrdinalIgnoreCase))
                UiTheme = "Light";
            SolutionCatalog = SolutionCatalog ?? new List<string>();
            SolutionCatalog = SolutionCatalog
                .Where(x => !string.IsNullOrWhiteSpace(x) &&
                            string.Equals(Path.GetExtension(x), ".sol", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!string.Equals(Path.GetExtension(LastSolutionPath), ".sol", StringComparison.OrdinalIgnoreCase))
                LastSolutionPath = string.Empty;
            if (string.IsNullOrWhiteSpace(LastSolutionPath) && SolutionCatalog.Count > 0)
                LastSolutionPath = SolutionCatalog[0];

            General = General ?? new GeneralSettings();
            Vision = Vision ?? new VisionSettings();
            Detection = Detection ?? new DetectionSettings();
            Input = Input ?? new InputSettings();
            Camera = Camera ?? new CameraSettings();
            Storage = Storage ?? new StorageSettings();
            Communication = Communication ?? new CommunicationSettings();
            ResultJudge = ResultJudge ?? new ResultJudgeOptions();
            Report = Report ?? new ReportSettings();

            General.Normalize();
            Vision.Normalize();
            Detection.Normalize();
            Input.Normalize();
            Camera.Normalize();
            Storage.Normalize();
            Communication.Normalize();
            ResultJudge.Normalize();
            Report.Normalize();
        }
    }

    public sealed class GeneralSettings
    {
        public bool AutoStart { get; set; }
        public bool RememberLastSolution { get; set; }
        public bool ShowDebugLog { get; set; }
        public int PreviewThrottleMs { get; set; }
        public bool ConfirmBeforeExit { get; set; }

        public GeneralSettings()
        {
            RememberLastSolution = true;
            PreviewThrottleMs = 120;
            ConfirmBeforeExit = true;
        }

        public void Normalize()
        {
            if (PreviewThrottleMs < 30) PreviewThrottleMs = 30;
            if (PreviewThrottleMs > 2000) PreviewThrottleMs = 2000;
        }
    }

    public sealed class VisionSettings
    {
        public string PrimaryProcedureName { get; set; }
        public string ImageSourceModuleName { get; set; }
        public int RunTimeoutMs { get; set; }
        public int MaxOutputRows { get; set; }
        public bool AutoSaveParameterChanges { get; set; }
        public bool ParseFormatModuleOutputs { get; set; }
        public string ParameterPresetDirectory { get; set; }

        public VisionSettings()
        {
            PrimaryProcedureName = string.Empty;
            ImageSourceModuleName = string.Empty;
            RunTimeoutMs = 30000;
            MaxOutputRows = 300;
            AutoSaveParameterChanges = false;
            ParseFormatModuleOutputs = true;
            ParameterPresetDirectory = "params";
        }

        public void Normalize()
        {
            PrimaryProcedureName = PrimaryProcedureName ?? string.Empty;
            ImageSourceModuleName = ImageSourceModuleName ?? string.Empty;
            ParameterPresetDirectory = string.IsNullOrWhiteSpace(ParameterPresetDirectory) ? "params" : ParameterPresetDirectory;
            if (RunTimeoutMs < 1000) RunTimeoutMs = 1000;
            if (RunTimeoutMs > 600000) RunTimeoutMs = 600000;
            if (MaxOutputRows < 20) MaxOutputRows = 20;
            if (MaxOutputRows > 5000) MaxOutputRows = 5000;
        }
    }

    public sealed class DetectionSettings
    {
        public DetectionMode DefaultMode { get; set; }
        public int IntervalMs { get; set; }
        public int ErrorRecoveryDelayMs { get; set; }
        public int MaxConsecutiveErrors { get; set; }
        public int StopTimeoutMs { get; set; }
        public bool AutoClearStatsOnSolutionChange { get; set; }

        public DetectionSettings()
        {
            DefaultMode = DetectionMode.Single;
            IntervalMs = 200;
            ErrorRecoveryDelayMs = 1500;
            MaxConsecutiveErrors = 5;
            StopTimeoutMs = 8000;
            AutoClearStatsOnSolutionChange = true;
        }

        public void Normalize()
        {
            if (IntervalMs < 0) IntervalMs = 0;
            if (IntervalMs > 3600000) IntervalMs = 3600000;
            if (ErrorRecoveryDelayMs < 100) ErrorRecoveryDelayMs = 100;
            if (ErrorRecoveryDelayMs > 60000) ErrorRecoveryDelayMs = 60000;
            if (MaxConsecutiveErrors < 1) MaxConsecutiveErrors = 1;
            if (MaxConsecutiveErrors > 1000) MaxConsecutiveErrors = 1000;
            if (StopTimeoutMs < 1000) StopTimeoutMs = 1000;
            if (StopTimeoutMs > 60000) StopTimeoutMs = 60000;
        }
    }

    public sealed class InputSettings
    {
        public InputMode Mode { get; set; }
        public PlaybackMode LocalPlaybackMode { get; set; }
        public string LastLocalFolder { get; set; }
        public string LastCameraId { get; set; }

        public InputSettings()
        {
            Mode = InputMode.LocalImages;
            LocalPlaybackMode = PlaybackMode.Loop;
            LastLocalFolder = string.Empty;
            LastCameraId = string.Empty;
        }

        public void Normalize()
        {
            LastLocalFolder = LastLocalFolder ?? string.Empty;
            LastCameraId = LastCameraId ?? string.Empty;
        }
    }

    public sealed class CameraSettings
    {
        public CameraTriggerMode TriggerMode { get; set; }
        public CameraPixelFormat PixelFormat { get; set; }
        public int HardwareTriggerSource { get; set; }
        public float ExposureTimeUs { get; set; }
        public float GainDb { get; set; }
        public bool ApplyExposureAndGainOnConnect { get; set; }
        public bool EnableAutoReconnect { get; set; }
        public int ReconnectIntervalMs { get; set; }
        public int FrameTimeoutMs { get; set; }
        public int PreviewFrameSkip { get; set; }

        public CameraSettings()
        {
            TriggerMode = CameraTriggerMode.Software;
            PixelFormat = CameraPixelFormat.Mono8;
            HardwareTriggerSource = 0;      // Line0
            ExposureTimeUs = 5000;
            GainDb = 0;
            ApplyExposureAndGainOnConnect = true;
            EnableAutoReconnect = true;
            ReconnectIntervalMs = 3000;
            FrameTimeoutMs = 5000;
            PreviewFrameSkip = 2;
        }

        public void Normalize()
        {
            if (HardwareTriggerSource < 0) HardwareTriggerSource = 0;
            if (HardwareTriggerSource > 3) HardwareTriggerSource = 3;
            if (ExposureTimeUs < 0) ExposureTimeUs = 0;
            if (ReconnectIntervalMs < 500) ReconnectIntervalMs = 500;
            if (ReconnectIntervalMs > 60000) ReconnectIntervalMs = 60000;
            if (FrameTimeoutMs < 500) FrameTimeoutMs = 500;
            if (FrameTimeoutMs > 120000) FrameTimeoutMs = 120000;
            if (PreviewFrameSkip < 1) PreviewFrameSkip = 1;
            if (PreviewFrameSkip > 30) PreviewFrameSkip = 30;
        }
    }

    public sealed class StorageSettings
    {
        /// <summary>留空时使用默认数据目录（%ProgramData%\\VisionInspect 或用户回退目录）。</summary>
        public string DataRoot { get; set; }
        public bool EnableDetectionLog { get; set; }
        public bool EnableImageArchive { get; set; }
        public string ArchiveFormat { get; set; }
        public bool ArchiveNgOnly { get; set; }
        public int RetentionDays { get; set; }
        public int ArchiveQueueCapacity { get; set; }
        /// <summary>归档中正在写入和排队的原始图像字节预算（MiB），避免高分辨率图片耗尽内存。</summary>
        public int ArchiveMemoryLimitMb { get; set; }

        public StorageSettings()
        {
            DataRoot = string.Empty;
            EnableDetectionLog = true;
            EnableImageArchive = true;
            ArchiveFormat = "png";
            ArchiveNgOnly = false;
            RetentionDays = 90;
            ArchiveQueueCapacity = 500;
            ArchiveMemoryLimitMb = 256;
        }

        public void Normalize()
        {
            DataRoot = DataRoot ?? string.Empty;
            ArchiveFormat = (ArchiveFormat ?? "png").ToLowerInvariant();
            if (ArchiveFormat != "png" && ArchiveFormat != "bmp" && ArchiveFormat != "jpg")
                ArchiveFormat = "png";
            if (RetentionDays < 1) RetentionDays = 1;
            if (RetentionDays > 3650) RetentionDays = 3650;
            if (ArchiveQueueCapacity < 10) ArchiveQueueCapacity = 10;
            if (ArchiveQueueCapacity > 5000) ArchiveQueueCapacity = 5000;
            if (ArchiveMemoryLimitMb <= 0) ArchiveMemoryLimitMb = 256;
            if (ArchiveMemoryLimitMb < 16) ArchiveMemoryLimitMb = 16;
            if (ArchiveMemoryLimitMb > 4096) ArchiveMemoryLimitMb = 4096;
        }
    }

    public sealed class CommunicationSettings
    {
        public ModbusTcpSettings ModbusTcp { get; set; }
        public SerialPortSettings Serial { get; set; }
        public TcpTriggerSettings TcpTrigger { get; set; }

        public CommunicationSettings()
        {
            ModbusTcp = new ModbusTcpSettings();
            Serial = new SerialPortSettings();
            TcpTrigger = new TcpTriggerSettings();
        }

        public void Normalize()
        {
            ModbusTcp = ModbusTcp ?? new ModbusTcpSettings();
            Serial = Serial ?? new SerialPortSettings();
            TcpTrigger = TcpTrigger ?? new TcpTriggerSettings();
            ModbusTcp.Normalize();
            Serial.Normalize();
            TcpTrigger.Normalize();
        }
    }

    public sealed class ModbusTcpSettings
    {
        public bool Enabled { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public byte UnitId { get; set; }
        public ushort StartRegister { get; set; }
        public ushort RegisterCount { get; set; }
        public ushort ResultRegisterOffset { get; set; }
        public ushort OkValue { get; set; }
        public ushort NgValue { get; set; }
        public int TimeoutMs { get; set; }
        public int ReconnectIntervalMs { get; set; }
        public bool AutoReconnect { get; set; }
        public List<ModbusRegisterMapping> Mappings { get; set; }

        public ModbusTcpSettings()
        {
            Enabled = false;
            Host = "192.168.1.10";
            Port = 502;
            UnitId = 1;
            StartRegister = 1000;
            RegisterCount = 8;
            ResultRegisterOffset = 0;
            OkValue = 1;
            NgValue = 2;
            TimeoutMs = 1000;
            ReconnectIntervalMs = 3000;
            AutoReconnect = true;
            Mappings = new List<ModbusRegisterMapping>();
        }

        public void Normalize()
        {
            Host = Host ?? string.Empty;
            if (Port <= 0 || Port > 65535) Port = 502;
            if (RegisterCount < 2) RegisterCount = 2;
            if (RegisterCount > 100) RegisterCount = 100;
            if (ResultRegisterOffset >= RegisterCount) ResultRegisterOffset = 0;
            if (TimeoutMs < 100) TimeoutMs = 100;
            if (TimeoutMs > 10000) TimeoutMs = 10000;
            if (ReconnectIntervalMs < 500) ReconnectIntervalMs = 500;
            if (ReconnectIntervalMs > 60000) ReconnectIntervalMs = 60000;
            Mappings = Mappings ?? new List<ModbusRegisterMapping>();
            foreach (var mapping in Mappings) mapping.Normalize(RegisterCount);
        }
    }

    public sealed class ModbusRegisterMapping
    {
        public string OutputName { get; set; }
        public ushort RegisterOffset { get; set; }
        public double Scale { get; set; }
        public bool Enabled { get; set; }

        public ModbusRegisterMapping()
        {
            OutputName = string.Empty;
            Scale = 1.0;
            Enabled = true;
        }

        public void Normalize(ushort registerCount)
        {
            OutputName = OutputName ?? string.Empty;
            if (double.IsNaN(Scale) || double.IsInfinity(Scale) || Scale == 0) Scale = 1.0;
            if (RegisterOffset >= registerCount) Enabled = false;
        }
    }

    public sealed class SerialPortSettings
    {
        public bool Enabled { get; set; }
        public string PortName { get; set; }
        public int BaudRate { get; set; }
        public string Parity { get; set; }
        public int DataBits { get; set; }
        public string StopBits { get; set; }
        public string CommandEncoding { get; set; }
        public string OkCommand { get; set; }
        public string NgCommand { get; set; }
        public string TriggerCommand { get; set; }
        public bool EnableReceiveTrigger { get; set; }
        public bool AutoReconnect { get; set; }
        public int ReconnectIntervalMs { get; set; }

        public SerialPortSettings()
        {
            Enabled = false;
            PortName = "COM3";
            BaudRate = 115200;
            Parity = "None";
            DataBits = 8;
            StopBits = "One";
            CommandEncoding = "Hex";
            OkCommand = "AA 55 01 01 55 AA";
            NgCommand = "AA 55 01 02 55 AA";
            TriggerCommand = "TRIG";
            EnableReceiveTrigger = true;
            AutoReconnect = true;
            ReconnectIntervalMs = 3000;
        }

        public void Normalize()
        {
            PortName = PortName ?? string.Empty;
            Parity = Parity ?? "None";
            StopBits = StopBits ?? "One";
            CommandEncoding = string.Equals(CommandEncoding, "Ascii", StringComparison.OrdinalIgnoreCase) ? "Ascii" : "Hex";
            OkCommand = OkCommand ?? string.Empty;
            NgCommand = NgCommand ?? string.Empty;
            TriggerCommand = TriggerCommand ?? string.Empty;
            if (BaudRate < 1200) BaudRate = 9600;
            if (DataBits < 5 || DataBits > 8) DataBits = 8;
            if (ReconnectIntervalMs < 500) ReconnectIntervalMs = 500;
            if (ReconnectIntervalMs > 60000) ReconnectIntervalMs = 60000;
        }
    }

    public sealed class TcpTriggerSettings
    {
        public string Mode { get; set; }
        public string RemoteHost { get; set; }
        public int RemotePort { get; set; }
        public int TimeoutMs { get; set; }
        public bool AutoReconnect { get; set; }
        public int ReconnectIntervalMs { get; set; }
        public bool EnableReceiveTrigger { get; set; }
        public bool SendResults { get; set; }
        public string ResultTemplate { get; set; }
        public bool Enabled { get; set; }
        public string BindAddress { get; set; }
        public int ListenPort { get; set; }
        public string TriggerCommand { get; set; }
        public string AckCommand { get; set; }

        public TcpTriggerSettings()
        {
            Mode = "Server";
            RemoteHost = "127.0.0.1";
            RemotePort = 9000;
            TimeoutMs = 2000;
            AutoReconnect = true;
            ReconnectIntervalMs = 1000;
            EnableReceiveTrigger = true;
            ResultTemplate = "{RESULT},{SEQ},{DURATION}\\r\\n";
            Enabled = false;
            BindAddress = "0.0.0.0";
            ListenPort = 9000;
            TriggerCommand = "TRIG";
            AckCommand = "ACK";
        }

        public void Normalize()
        {
            Mode = string.Equals(Mode, "Client", StringComparison.OrdinalIgnoreCase) ? "Client" : "Server";
            RemoteHost = RemoteHost ?? string.Empty;
            if (RemotePort < 1 || RemotePort > 65535) RemotePort = 9000;
            TimeoutMs = Math.Max(100, Math.Min(10000, TimeoutMs));
            ReconnectIntervalMs = Math.Max(500, Math.Min(60000, ReconnectIntervalMs));
            ResultTemplate = ResultTemplate ?? "{RESULT},{SEQ},{DURATION}\\r\\n";
            BindAddress = string.IsNullOrWhiteSpace(BindAddress) ? "0.0.0.0" : BindAddress;
            if (ListenPort <= 0 || ListenPort > 65535) ListenPort = 9000;
            TriggerCommand = TriggerCommand ?? "TRIG";
            AckCommand = AckCommand ?? "ACK";
        }
    }

    public sealed class ResultJudgeOptions
    {
        /// <summary>优先使用的格式化/流程输出名，例如“检测结果”。留空则自动扫描。</summary>
        public string JudgeOutputName { get; set; }
        public string OkKeyword { get; set; }
        public string NgKeyword { get; set; }
        public bool UseFailSafeScan { get; set; }
        public bool UseProcedureStatusAsFallback { get; set; }
        public List<string> BooleanOutputNames { get; set; }

        public ResultJudgeOptions()
        {
            JudgeOutputName = "检测结果";
            OkKeyword = "OK";
            NgKeyword = "NG";
            UseFailSafeScan = true;
            UseProcedureStatusAsFallback = true;
            BooleanOutputNames = new List<string>();
        }

        public void Normalize()
        {
            JudgeOutputName = JudgeOutputName ?? string.Empty;
            OkKeyword = string.IsNullOrWhiteSpace(OkKeyword) ? "OK" : OkKeyword;
            NgKeyword = string.IsNullOrWhiteSpace(NgKeyword) ? "NG" : NgKeyword;
            BooleanOutputNames = BooleanOutputNames ?? new List<string>();
        }
    }

    public sealed class ReportSettings
    {
        public string ExportDirectory { get; set; }
        public string CompanyName { get; set; }

        public ReportSettings()
        {
            ExportDirectory = string.Empty;
            CompanyName = string.Empty;
        }

        public void Normalize()
        {
            ExportDirectory = ExportDirectory ?? string.Empty;
            CompanyName = CompanyName ?? string.Empty;
        }
    }
}
