using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;

namespace VisionInspect.Core.Services
{
    /// <summary>
    /// OK/NG 判定规则。策略：
    /// 1) 优先读取配置的格式化输出（默认“检测结果”），匹配 OK/NG 关键字；
    /// 2) 可选全局文本扫描，任意 NG 先判 NG（失效安全）；
    /// 3) 最后回退流程执行状态。
    /// 该规则只处理格式化字符串/布尔输出，不臆测具体测量值的上下限，避免误判。
    /// </summary>
    public static class ResultJudge
    {
        public sealed class Outcome
        {
            public bool IsOk { get; set; }
            public string Text { get; set; }
            public string Source { get; set; }
        }

        public static Outcome Evaluate(
            IList<VisionOutputValue> outputs,
            bool procedureSucceeded,
            ResultJudgeOptions options)
        {
            return Evaluate(outputs, procedureSucceeded, null, options);
        }

        /// <summary>
        /// modulesSucceeded 是流程内所有未禁用模块的 IsRunOK 汇总。
        /// 某些 VM 方案没有配置流程输出，Procedure.IsRunOK 会是 false，
        /// 但模块实际全部执行成功；此时使用模块状态作为回退，避免“找到了却判 NG”。
        /// </summary>
        public static Outcome Evaluate(
            IList<VisionOutputValue> outputs,
            bool procedureSucceeded,
            bool? modulesSucceeded,
            ResultJudgeOptions options)
        {
            options = options ?? new ResultJudgeOptions();
            outputs = outputs ?? new List<VisionOutputValue>();

            if (!string.IsNullOrWhiteSpace(options.JudgeOutputName))
            {
                VisionOutputValue target = outputs.FirstOrDefault(x =>
                    string.Equals(x.Name, options.JudgeOutputName, StringComparison.OrdinalIgnoreCase));

                if (target == null)
                {
                    target = outputs.FirstOrDefault(x =>
                        !string.IsNullOrEmpty(x.Name) &&
                        x.Name.IndexOf(options.JudgeOutputName, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (target != null)
                {
                    bool? judged = JudgeText(target.JoinedValue, options);
                    if (judged.HasValue)
                    {
                        return new Outcome
                        {
                            IsOk = judged.Value,
                            Text = judged.Value ? "OK" : "NG",
                            Source = target.Name
                        };
                    }
                }
            }

            if (options.UseFailSafeScan)
            {
                bool sawOk = false;
                foreach (VisionOutputValue output in outputs)
                {
                    if (output.Kind == VisionValueKind.Image) continue;
                    string value = output.JoinedValue;
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    if (ContainsKeyword(value, options.NgKeyword))
                    {
                        return new Outcome
                        {
                            IsOk = false,
                            Text = "NG",
                            Source = output.Name
                        };
                    }

                    if (!sawOk && ContainsKeyword(value, options.OkKeyword))
                        sawOk = true;
                }

                if (sawOk)
                {
                    return new Outcome
                    {
                        IsOk = true,
                        Text = "OK",
                        Source = "输出扫描"
                    };
                }
            }

            if (options.UseProcedureStatusAsFallback)
            {
                bool effectiveSuccess = procedureSucceeded || modulesSucceeded == true;
                return new Outcome
                {
                    IsOk = effectiveSuccess,
                    Text = effectiveSuccess ? "OK" : "NG",
                    Source = procedureSucceeded ? "流程状态" :
                             modulesSucceeded == true ? "模块状态" : "流程状态"
                };
            }

            return new Outcome
            {
                IsOk = false,
                Text = "NG",
                Source = "无判定依据（默认NG）"
            };
        }

        private static bool? JudgeText(string text, ResultJudgeOptions options)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (ContainsKeyword(text, options.NgKeyword)) return false;
            if (ContainsKeyword(text, options.OkKeyword)) return true;
            return null;
        }

        /// <summary>
        /// 避免把 "STRING" 中的 "NG"、"TOKEN" 中的 "OK" 当结果。
        /// 允许中文紧邻关键字，例如“检测结果OK”。
        /// </summary>
        private static bool ContainsKeyword(string value, string keyword)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(keyword))
                return false;

            string pattern = "(?i)(?<![A-Z0-9])" + Regex.Escape(keyword.Trim()) + "(?![A-Z0-9])";
            return Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant);
        }
    }
}
