using System;
using System.Collections.Generic;
using System.Globalization;
using VisionInspect.Core.Models;

namespace VisionInspect.Core.Algorithms
{
    /// <summary>
    /// 统一结果与现有检测/存储模型之间的转换。
    /// 保留 VM 原生结果对象，使原有 VM 模块状态、格式化输出和界面行为不丢失。
    /// </summary>
    public static class AlgorithmResultConverter
    {
        public static VisionInspectionResult ToVisionInspectionResult(DetectResult result,
            string solutionPath = null, string procedureName = null)
        {
            if (result == null) return null;

            var native = result.NativeResult as VisionInspectionResult;
            if (native != null)
            {
                // VM 适配器已经生成了完整结果，直接复用，确保 100% 保留原行为。
                native.IsOk = result.IsOk;
                native.DurationMs = result.DurationMs;
                native.Timestamp = result.Timestamp;
                native.JudgeText = result.JudgeText;
                native.JudgeSource = result.JudgeSource;
                native.ErrorMessage = result.ErrorMessage;
                return native;
            }

            var converted = new VisionInspectionResult
            {
                SolutionPath = solutionPath ?? result.SchemePath ?? string.Empty,
                ProcedureName = procedureName ?? result.ProcedureName ?? string.Empty,
                Timestamp = result.Timestamp,
                IsOk = result.IsOk,
                ProcedureSucceeded = result.ProcedureSucceeded,
                JudgeText = result.JudgeText,
                JudgeSource = result.JudgeSource,
                DurationMs = result.DurationMs,
                ErrorMessage = result.ErrorMessage
            };

            if (result.OutputValues != null)
            {
                foreach (DetectOutputValue output in result.OutputValues)
                {
                    VisionValueKind kind = ResolveKind(output.TypeName);
                    converted.Outputs.Add(new VisionOutputValue
                    {
                        Name = output.Name,
                        Source = output.Source,
                        TypeName = output.TypeName,
                        Kind = kind,
                        DisplayValue = output.Value,
                        Values = new List<string> { output.Value }
                    });
                }
            }

            if (result.FormatOutputs != null)
                converted.FormatOutputs.AddRange(result.FormatOutputs);

            return converted;
        }

        public static DetectResult FromVisionInspectionResult(VisionInspectionResult inspection,
            string engineName = "VisionMaster 4.3.0")
        {
            if (inspection == null) return null;

            var result = new DetectResult
            {
                IsOk = inspection.IsOk,
                ProcedureSucceeded = inspection.ProcedureSucceeded,
                JudgeText = inspection.JudgeText,
                JudgeSource = inspection.JudgeSource,
                DurationMs = inspection.DurationMs,
                Timestamp = inspection.Timestamp == default(DateTime) ? DateTime.Now : inspection.Timestamp,
                EngineName = engineName,
                SchemePath = inspection.SolutionPath,
                ProcedureName = inspection.ProcedureName,
                ErrorMessage = inspection.ErrorMessage,
                NativeResult = inspection
            };

            if (inspection.Outputs != null)
            {
                foreach (VisionOutputValue output in inspection.Outputs)
                {
                    result.AddOutput(output.Name, output.JoinedValue, output.TypeName, output.Source);
                }
            }
            if (inspection.FormatOutputs != null)
                result.FormatOutputs.AddRange(inspection.FormatOutputs);
            return result;
        }

        public static ParamDataType ResolveParamType(string typeName, bool isEnum, IList<string> options)
        {
            if (isEnum || (options != null && options.Count > 0)) return ParamDataType.Enum;
            if (string.IsNullOrWhiteSpace(typeName)) return ParamDataType.String;
            switch (typeName.Trim().ToLowerInvariant())
            {
                case "bool":
                case "boolean":
                    return ParamDataType.Boolean;
                case "byte":
                case "sbyte":
                case "int16":
                case "uint16":
                case "int32":
                case "uint32":
                case "int64":
                case "uint64":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                    return ParamDataType.Integer;
                case "single":
                case "double":
                case "float":
                case "decimal":
                    return ParamDataType.Number;
                default:
                    return ParamDataType.String;
            }
        }

        public static string Format(object value)
        {
            if (value == null) return string.Empty;
            if (value is bool) return (bool)value ? "True" : "False";
            IFormattable formattable = value as IFormattable;
            return formattable != null
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static VisionValueKind ResolveKind(string typeName)
        {
            string t = (typeName ?? string.Empty).Trim().ToLowerInvariant();
            if (t == "bool" || t == "boolean") return VisionValueKind.Boolean;
            if (t == "string" || t == "htuple") return VisionValueKind.Text;
            if (t == "image" || t == "hobject") return VisionValueKind.Image;
            if (t == "single" || t == "double" || t == "float" || t == "decimal" ||
                t == "int16" || t == "uint16" || t == "int32" || t == "uint32" ||
                t == "int64" || t == "uint64" || t == "byte" || t == "int")
                return VisionValueKind.Numeric;
            return VisionValueKind.Other;
        }
    }
}
