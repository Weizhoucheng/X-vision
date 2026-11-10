using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using VisionInspect.Core;
using VisionInspect.Core.Models;

namespace VisionInspect.VisionMaster.Parameters
{
    /// <summary>
    /// 基于 ModuParams 公共属性的轻量参数适配器。
    ///
    /// 说明：VM 官方参数配置控件（VmParamsConfigWithRenderControl）能覆盖所有复杂参数；
    /// 本适配器只处理基础类型（int/float/bool/string/enum），用于预设、默认值恢复和自动化。
    /// 复杂 ROI/模板/集合参数不会被错误地序列化。
    /// </summary>
    public static class ParameterAdapter
    {
        public static IReadOnlyList<VisionParameterInfo> Describe(object module)
        {
            object parameters = GetParametersObject(module);
            var result = new List<VisionParameterInfo>();

            foreach (PropertyInfo property in GetSimpleProperties(parameters.GetType()))
            {
                try
                {
                    object value = property.GetValue(parameters, null);
                    var info = new VisionParameterInfo
                    {
                        Name = property.Name,
                        DisplayName = GetDisplayName(property),
                        Category = GetCategory(property),
                        TypeName = FriendlyTypeName(property.PropertyType),
                        ValueText = FormatValue(value),
                        IsWritable = property.GetSetMethod(false) != null,
                        IsEnum = (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType).IsEnum
                    };

                    if (info.IsEnum)
                        info.EnumValues.AddRange(Enum.GetNames(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType));

                    result.Add(info);
                }
                catch
                {
                    // 个别参数读取依赖模块状态，忽略而不是让整个参数页失败。
                }
            }

            return result
                .OrderBy(x => x.Category, StringComparer.CurrentCulture)
                .ThenBy(x => x.DisplayName, StringComparer.CurrentCulture)
                .ToList();
        }

        public static ParameterSnapshot Capture(object module, string moduleFullName)
        {
            object parameters = GetParametersObject(module);
            var snapshot = new ParameterSnapshot { ModuleFullName = moduleFullName };

            foreach (PropertyInfo property in GetSimpleProperties(parameters.GetType()))
            {
                // 只读的状态/标识属性可以展示，但不能放进可恢复的预设。
                if (property.GetSetMethod(false) == null) continue;
                try
                {
                    object value = property.GetValue(parameters, null);
                    snapshot.Values[property.Name] = FormatValue(value);
                }
                catch
                {
                    // 忽略无法读取的属性。
                }
            }

            return snapshot;
        }

        public static ParameterApplyResult Apply(object module, ParameterSnapshot snapshot)
        {
            var result = new ParameterApplyResult();
            if (snapshot == null || snapshot.Values == null) return result;

            object parameters = GetParametersObject(module);
            Dictionary<string, PropertyInfo> properties = GetSimpleProperties(parameters.GetType())
                .ToDictionary(x => x.Name, x => x, StringComparer.Ordinal);

            foreach (KeyValuePair<string, string> pair in snapshot.Values)
            {
                PropertyInfo property;
                if (!properties.TryGetValue(pair.Key, out property) || property.GetSetMethod(false) == null)
                {
                    result.Failures.Add(pair.Key + "：参数不存在或只读");
                    continue;
                }

                try
                {
                    object converted = ConvertValue(pair.Value, property.PropertyType);
                    property.SetValue(parameters, converted, null);
                    result.Applied++;
                }
                catch (Exception ex)
                {
                    result.Failures.Add(string.Format("{0}：{1}", pair.Key, ex.Message));
                }
            }

            return result;
        }

        public static ParameterApplyResult SetOne(object module, string parameterName, string value)
        {
            var snapshot = new ParameterSnapshot();
            snapshot.Values[parameterName] = value;
            return Apply(module, snapshot);
        }

        public static object GetParametersObject(object module)
        {
            if (module == null) throw new VisionInspectException("视觉模块对象为空。");

            PropertyInfo property = module.GetType().GetProperty("ModuParams",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property == null)
                throw new VisionInspectException("模块不包含 ModuParams 属性：" + module.GetType().FullName);

            object parameters = property.GetValue(module, null);
            if (parameters == null)
                throw new VisionInspectException("模块参数对象为空：" + module.GetType().FullName);

            return parameters;
        }

        private static IEnumerable<PropertyInfo> GetSimpleProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Where(p => p.CanRead)
                .Where(p => IsSimpleType(p.PropertyType))
                .Where(p =>
                {
                    var browsable = p.GetCustomAttribute<BrowsableAttribute>(true);
                    return browsable == null || browsable.Browsable;
                });
        }

        private static bool IsSimpleType(Type type)
        {
            Type target = Nullable.GetUnderlyingType(type) ?? type;
            if (target.IsEnum) return true;
            return target == typeof(string) ||
                   target == typeof(bool) ||
                   target == typeof(byte) ||
                   target == typeof(sbyte) ||
                   target == typeof(short) ||
                   target == typeof(ushort) ||
                   target == typeof(int) ||
                   target == typeof(uint) ||
                   target == typeof(long) ||
                   target == typeof(ulong) ||
                   target == typeof(float) ||
                   target == typeof(double) ||
                   target == typeof(decimal);
        }

        private static string FormatValue(object value)
        {
            if (value == null) return string.Empty;
            if (value is float) return ((float)value).ToString("G9", CultureInfo.InvariantCulture);
            if (value is double) return ((double)value).ToString("G17", CultureInfo.InvariantCulture);
            if (value is decimal) return ((decimal)value).ToString(CultureInfo.InvariantCulture);
            if (value is bool) return (bool)value ? "True" : "False";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static object ConvertValue(string text, Type targetType)
        {
            Type nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null && string.IsNullOrEmpty(text)) return null;
            Type target = nullableType ?? targetType;
            if (target == typeof(string)) return text ?? string.Empty;
            if (target == typeof(bool))
            {
                bool boolValue;
                if (bool.TryParse(text, out boolValue)) return boolValue;
                if (text == "1") return true;
                if (text == "0") return false;
                throw new FormatException("不是有效布尔值。");
            }
            if (target.IsEnum)
                return Enum.Parse(target, text, true);

            object converted = Convert.ChangeType(text, target, CultureInfo.InvariantCulture);
            if (converted is float && (float.IsNaN((float)converted) || float.IsInfinity((float)converted)) ||
                converted is double && (double.IsNaN((double)converted) || double.IsInfinity((double)converted)))
                throw new FormatException("参数必须是有限数值。");
            return converted;
        }

        private static string FriendlyTypeName(Type type)
        {
            Type target = Nullable.GetUnderlyingType(type) ?? type;
            if (target.IsEnum) return "枚举";
            if (target == typeof(bool)) return "布尔";
            if (target == typeof(string)) return "字符串";
            if (target == typeof(float) || target == typeof(double) || target == typeof(decimal)) return "浮点数";
            return "整数";
        }

        private static string GetDisplayName(PropertyInfo property)
        {
            var attribute = property.GetCustomAttribute<DisplayNameAttribute>(true);
            return attribute == null || string.IsNullOrWhiteSpace(attribute.DisplayName)
                ? property.Name
                : attribute.DisplayName;
        }

        private static string GetCategory(PropertyInfo property)
        {
            var attribute = property.GetCustomAttribute<CategoryAttribute>(true);
            return attribute == null || string.IsNullOrWhiteSpace(attribute.Category)
                ? "参数"
                : attribute.Category;
        }
    }
}
