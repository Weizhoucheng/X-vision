using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionInspect.Core.Models
{
    /// <summary>一次检测的完整上下文，交给结果落盘/通讯管道。</summary>
    public sealed class DetectionContext
    {
        public long Sequence { get; set; }
        public DateTime Timestamp { get; set; }
        public string SolutionPath { get; set; }
        public string InputSource { get; set; }
        public RawImage Image { get; set; }
        public VisionInspectionResult Inspection { get; set; }
        public DetectionRecord Record { get; set; }
        public bool IsOk { get { return Inspection != null && Inspection.IsOk; } }
    }

    /// <summary>写入每日 CSV 的记录，也是报表明细的数据源。</summary>
    public sealed class DetectionRecord
    {
        public long Sequence { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsOk { get; set; }
        public string Solution { get; set; }
        public string Procedure { get; set; }
        public double DurationMs { get; set; }
        public string InputSource { get; set; }
        public string ImagePath { get; set; }
        public string OutputSummary { get; set; }
        public string JudgeText { get; set; }
        public string Error { get; set; }

        public DetectionRecord()
        {
            Solution = string.Empty;
            Procedure = string.Empty;
            InputSource = string.Empty;
            ImagePath = string.Empty;
            OutputSummary = string.Empty;
            JudgeText = string.Empty;
            Error = string.Empty;
        }

        public string ResultText
        {
            get { return IsOk ? "OK" : "NG"; }
        }
    }

    public sealed class DetectionStatisticsSnapshot
    {
        public long Total { get; set; }
        public long Ok { get; set; }
        public long Ng { get; set; }
        public double YieldRate
        {
            get { return Total <= 0 ? 0.0 : (double)Ok * 100.0 / Total; }
        }

        public DateTime StartedAt { get; set; }

        public override string ToString()
        {
            return string.Format("Total={0}, OK={1}, NG={2}, Yield={3:F2}%", Total, Ok, Ng, YieldRate);
        }
    }

    public sealed class DetectionStateChangedEventArgs : EventArgs
    {
        public DetectionStateChangedEventArgs(DetectionRunState oldState, DetectionRunState newState, string message)
        {
            OldState = oldState;
            NewState = newState;
            Message = message ?? string.Empty;
            Timestamp = DateTime.Now;
        }

        public DetectionRunState OldState { get; private set; }
        public DetectionRunState NewState { get; private set; }
        public string Message { get; private set; }
        public DateTime Timestamp { get; private set; }
    }

    public sealed class DetectionCompletedEventArgs : EventArgs
    {
        public DetectionCompletedEventArgs(DetectionContext context)
        {
            Context = context;
        }

        public DetectionContext Context { get; private set; }
    }

    public sealed class DetectionErrorEventArgs : EventArgs
    {
        public DetectionErrorEventArgs(string message, Exception exception, int consecutiveErrors)
        {
            Message = message ?? string.Empty;
            Exception = exception;
            ConsecutiveErrors = consecutiveErrors;
            Timestamp = DateTime.Now;
        }

        public string Message { get; private set; }
        public Exception Exception { get; private set; }
        public int ConsecutiveErrors { get; private set; }
        public DateTime Timestamp { get; private set; }
    }

    public sealed class DetectionNoticeEventArgs : EventArgs
    {
        public DetectionNoticeEventArgs(string message)
        {
            Message = message ?? string.Empty;
            Timestamp = DateTime.Now;
        }

        public string Message { get; private set; }
        public DateTime Timestamp { get; private set; }
    }

    public sealed class CameraFrameEventArgs : EventArgs
    {
        public CameraFrameEventArgs(RawImage frame)
        {
            Frame = frame;
        }

        public RawImage Frame { get; private set; }
    }

    public sealed class CameraStatusEventArgs : EventArgs
    {
        public CameraStatusEventArgs(DeviceConnectionState state, string message, CameraDeviceInfo device)
        {
            State = state;
            Message = message ?? string.Empty;
            Device = device;
            Timestamp = DateTime.Now;
        }

        public DeviceConnectionState State { get; private set; }
        public string Message { get; private set; }
        public CameraDeviceInfo Device { get; private set; }
        public DateTime Timestamp { get; private set; }
    }

    public sealed class CommunicationStatusEventArgs : EventArgs
    {
        public CommunicationStatusEventArgs(CommunicationChannel channel, DeviceConnectionState state, string message)
        {
            Channel = channel;
            State = state;
            Message = message ?? string.Empty;
            Timestamp = DateTime.Now;
        }

        public CommunicationChannel Channel { get; private set; }
        public DeviceConnectionState State { get; private set; }
        public string Message { get; private set; }
        public DateTime Timestamp { get; private set; }
    }

    public sealed class TriggerEventArgs : EventArgs
    {
        public TriggerEventArgs(TriggerSource source, string payload)
        {
            Source = source;
            Payload = payload ?? string.Empty;
            Timestamp = DateTime.Now;
        }

        public TriggerSource Source { get; private set; }
        public string Payload { get; private set; }
        public DateTime Timestamp { get; private set; }
    }

    public sealed class OutputPreviewRow
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Type { get; set; }
        public string Source { get; set; }
    }

    public static class DetectionMapping
    {
        public static List<OutputPreviewRow> ToPreviewRows(this VisionInspectionResult result)
        {
            if (result == null || result.Outputs == null) return new List<OutputPreviewRow>();

            return result.Outputs.Select(o => new OutputPreviewRow
            {
                Name = o.Name,
                Value = o.JoinedValue,
                Type = KindText(o.Kind),
                Source = o.Source
            }).ToList();
        }

        public static string KindText(VisionValueKind kind)
        {
            switch (kind)
            {
                case VisionValueKind.Numeric: return "数值";
                case VisionValueKind.Boolean: return "布尔";
                case VisionValueKind.Text: return "字符串";
                case VisionValueKind.Image: return "图像";
                default: return "其他";
            }
        }
    }
}
