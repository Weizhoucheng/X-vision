using System;
using System.Collections.Generic;

namespace VisionInspect.Core.Models
{
    /// <summary>
    /// 一个可编辑的简单参数描述。复杂结构参数由 VM 原生参数配置控件负责，
    /// 这里主要用于参数预设、默认值以及无界面自动化场景。
    /// </summary>
    public sealed class VisionParameterInfo
    {
        public VisionParameterInfo()
        {
            Name = string.Empty;
            DisplayName = string.Empty;
            Category = string.Empty;
            TypeName = string.Empty;
            ValueText = string.Empty;
            EnumValues = new List<string>();
        }

        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Category { get; set; }
        public string TypeName { get; set; }
        public string ValueText { get; set; }
        public bool IsWritable { get; set; }
        public bool IsEnum { get; set; }
        public List<string> EnumValues { get; set; }
    }

    /// <summary>模块简单参数的原子快照。</summary>
    public sealed class ParameterSnapshot
    {
        public ParameterSnapshot()
        {
            ModuleFullName = string.Empty;
            Values = new Dictionary<string, string>(StringComparer.Ordinal);
            CapturedAtUtc = DateTime.UtcNow;
        }

        public string ModuleFullName { get; set; }
        public DateTime CapturedAtUtc { get; set; }
        public Dictionary<string, string> Values { get; set; }
    }

    public sealed class ParameterApplyResult
    {
        public ParameterApplyResult()
        {
            Failures = new List<string>();
        }

        public bool Success
        {
            get { return Failures == null || Failures.Count == 0; }
        }

        public int Applied { get; set; }
        public List<string> Failures { get; set; }
    }
}
