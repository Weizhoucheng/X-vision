using System;
using System.Collections.Generic;

namespace VisionInspect.Core.Algorithms
{
    public enum ParamDataType
    {
        String = 0,
        Integer = 1,
        Number = 2,
        Boolean = 3,
        Enum = 4
    }

    /// <summary>统一参数描述。参数面板只根据该对象动态生成控件。</summary>
    public sealed class ParamItem
    {
        public ParamItem()
        {
            Name = string.Empty;
            DisplayName = string.Empty;
            Group = string.Empty;
            Description = string.Empty;
            DataType = ParamDataType.String;
            Options = new List<string>();
        }

        /// <summary>唯一名称。VM 多模块场景使用 "模块全名|参数名"。</summary>
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Group { get; set; }
        public string Description { get; set; }
        public ParamDataType DataType { get; set; }
        public object Value { get; set; }
        public object DefaultValue { get; set; }
        public double? Minimum { get; set; }
        public double? Maximum { get; set; }
        public List<string> Options { get; set; }
        public bool IsReadOnly { get; set; }
        public string Unit { get; set; }

        public string ValueText
        {
            get { return Value == null ? string.Empty : Convert.ToString(Value,
                System.Globalization.CultureInfo.InvariantCulture); }
        }
    }
}
