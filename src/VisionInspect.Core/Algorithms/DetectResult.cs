using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionInspect.Core.Algorithms
{
    public enum DetectGeometryKind
    {
        Point = 0,
        Line = 1,
        Rectangle = 2,
        Circle = 3,
        Polygon = 4,
        Text = 5
    }

    /// <summary>统一几何结果。坐标为原图像素坐标，由显示层统一转换为 VM 绘制元素。</summary>
    public sealed class DetectGeometry
    {
        public DetectGeometry()
        {
            Label = string.Empty;
            Color = "#00C853";
            Points = new List<System.Drawing.PointF>();
        }

        public DetectGeometryKind Kind { get; set; }
        public string Label { get; set; }
        public float X1 { get; set; }
        public float Y1 { get; set; }
        public float X2 { get; set; }
        public float Y2 { get; set; }
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float Radius { get; set; }
        public string Color { get; set; }
        public List<System.Drawing.PointF> Points { get; set; }
    }

    /// <summary>统一输出项；保留类型和来源，供 UI、CSV、PLC 映射共同使用。</summary>
    public sealed class DetectOutputValue
    {
        public DetectOutputValue()
        {
            Name = string.Empty;
            Source = string.Empty;
            TypeName = string.Empty;
            Value = string.Empty;
        }

        public string Name { get; set; }
        public string Value { get; set; }
        public string TypeName { get; set; }
        public string Source { get; set; }
    }

    /// <summary>
    /// 全项目统一检测结果。NativeResult 仅供具体适配器保存原生结果（例如 VM 的
    /// VisionInspectionResult），上层不得依赖其具体类型。
    /// </summary>
    public sealed class DetectResult
    {
        public DetectResult()
        {
            OutputValues = new List<DetectOutputValue>();
            Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Geometries = new List<DetectGeometry>();
            FormatOutputs = new List<string>();
            Timestamp = DateTime.Now;
            JudgeText = "NG";
            JudgeSource = string.Empty;
            ErrorMessage = string.Empty;
        }

        public bool IsOk { get; set; }
        public bool ProcedureSucceeded { get; set; }
        public string JudgeText { get; set; }
        public string JudgeSource { get; set; }
        public double DurationMs { get; set; }
        public DateTime Timestamp { get; set; }
        public string EngineName { get; set; }
        public string SchemePath { get; set; }
        public string ProcedureName { get; set; }
        public string ErrorMessage { get; set; }

        /// <summary>键值对结果集，便于 PLC/报表按名称取值。</summary>
        public Dictionary<string, string> Values { get; private set; }
        public List<DetectOutputValue> OutputValues { get; private set; }
        public List<string> FormatOutputs { get; private set; }
        public List<DetectGeometry> Geometries { get; private set; }

        /// <summary>叠加绘制后的结果图；为空时显示原始输入图。</summary>
        public UniversalImageData ResultImage { get; set; }

        /// <summary>适配器原生结果，仅用于保留 VM 模块状态等扩展信息。</summary>
        public object NativeResult { get; set; }

        public string OutputSummary
        {
            get
            {
                if (OutputValues.Count == 0) return string.Empty;
                return string.Join("; ", OutputValues.Select(x =>
                    string.Format("{0}={1}", x.Name, x.Value)));
            }
        }

        public void AddOutput(string name, object value, string typeName = null, string source = null)
        {
            string text = value == null ? string.Empty : Convert.ToString(
                value, System.Globalization.CultureInfo.InvariantCulture);
            OutputValues.Add(new DetectOutputValue
            {
                Name = name ?? string.Empty,
                Value = text,
                TypeName = typeName ?? (value == null ? string.Empty : value.GetType().Name),
                Source = source ?? string.Empty
            });
            if (!string.IsNullOrEmpty(name))
                Values[name] = text;
        }
    }
}
