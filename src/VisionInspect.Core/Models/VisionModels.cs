using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionInspect.Core.Models
{
    /// <summary>一个可显示、可判定的输出值。保留原始值文本，便于日志和报表留痕。</summary>
    public sealed class VisionOutputValue
    {
        public string Name { get; set; }
        public string Source { get; set; }
        public string TypeName { get; set; }
        public VisionValueKind Kind { get; set; }
        public List<string> Values { get; set; }
        public string DisplayValue { get; set; }

        public VisionOutputValue()
        {
            Name = string.Empty;
            Source = string.Empty;
            TypeName = string.Empty;
            Values = new List<string>();
            DisplayValue = string.Empty;
        }

        public bool IsBooleanTrue
        {
            get
            {
                if (Kind != VisionValueKind.Boolean || Values == null || Values.Count == 0)
                    return false;
                bool parsed;
                return bool.TryParse(Values[0], out parsed) && parsed;
            }
        }

        public string JoinedValue
        {
            get
            {
                if (DisplayValue != null && DisplayValue.Length > 0) return DisplayValue;
                return Values == null ? string.Empty : string.Join(", ", Values);
            }
        }
    }

    public sealed class VisionModuleStatus
    {
        public string ProcedureName { get; set; }
        public string ModuleName { get; set; }
        public bool IsOk { get; set; }
        public double ModuleTimeMs { get; set; }
        public bool IsForbidden { get; set; }
    }

    /// <summary>由 VisionMaster 适配层返回的原始检测结果。</summary>
    public sealed class VisionInspectionResult
    {
        public VisionInspectionResult()
        {
            Outputs = new List<VisionOutputValue>();
            Modules = new List<VisionModuleStatus>();
            FormatOutputs = new List<string>();
            ProcedureName = string.Empty;
            JudgeText = "NG";
            JudgeSource = string.Empty;
        }

        public string SolutionPath { get; set; }
        public string ProcedureName { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsOk { get; set; }
        public bool ProcedureSucceeded { get; set; }
        public string JudgeText { get; set; }
        public string JudgeSource { get; set; }
        public double DurationMs { get; set; }
        public List<VisionOutputValue> Outputs { get; set; }
        public List<VisionModuleStatus> Modules { get; set; }
        public List<string> FormatOutputs { get; set; }
        public string ErrorMessage { get; set; }

        public string OutputSummary
        {
            get
            {
                if (Outputs == null || Outputs.Count == 0) return string.Empty;
                return string.Join("; ", Outputs.Select(x =>
                    string.Format("{0}={1}", x.Name, x.JoinedValue)));
            }
        }
    }

    /// <summary>方案内一个模块的静态信息，用于参数页和模块选择。</summary>
    public sealed class VisionModuleInfo
    {
        public VisionModuleInfo()
        {
            ProcedureName = string.Empty;
            Name = string.Empty;
            DisplayName = string.Empty;
            FullName = string.Empty;
            TypeName = string.Empty;
            Outputs = new List<VisionOutputInfo>();
        }

        public string ProcedureName { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string FullName { get; set; }
        public string TypeName { get; set; }
        public bool IsAvailable { get; set; }
        public bool IsImageSource { get; set; }
        public bool IsFormatModule { get; set; }
        public List<VisionOutputInfo> Outputs { get; set; }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
        }
    }

    public sealed class VisionOutputInfo
    {
        public string Name { get; set; }
        public string TypeName { get; set; }
    }

    public sealed class VisionProcedureInfo
    {
        public VisionProcedureInfo()
        {
            Name = string.Empty;
            Modules = new List<VisionModuleInfo>();
            Outputs = new List<VisionOutputInfo>();
        }

        public string Name { get; set; }
        public List<VisionModuleInfo> Modules { get; set; }
        public List<VisionOutputInfo> Outputs { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class VisionSolutionInfo
    {
        public VisionSolutionInfo()
        {
            SolutionPath = string.Empty;
            Procedures = new List<VisionProcedureInfo>();
        }

        public string SolutionPath { get; set; }
        public DateTime LoadedAt { get; set; }
        public string PrimaryProcedureName { get; set; }
        public string ImageSourceModuleFullName { get; set; }
        public List<VisionProcedureInfo> Procedures { get; set; }

        public VisionProcedureInfo FindProcedure(string name)
        {
            if (Procedures == null) return null;
            return Procedures.FirstOrDefault(x =>
                string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
