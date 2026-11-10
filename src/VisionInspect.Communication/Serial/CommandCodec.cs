using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VisionInspect.Core.Models;

namespace VisionInspect.Communication.Serial
{
    /// <summary>串口 IO 指令编解码。支持 ASCII 模板和 HEX 固定帧。</summary>
    public static class CommandCodec
    {
        public static byte[] Build(string template, bool isHex, DetectionContext context)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;

            if (isHex)
            {
                return ParseHex(template);
            }

            string text = ExpandEscapes(template)
                .Replace("{RESULT}", context != null && context.IsOk ? "OK" : "NG")
                .Replace("{SEQ}", context == null ? "0" : context.Sequence.ToString(CultureInfo.InvariantCulture))
                .Replace("{DURATION}", context == null || context.Inspection == null
                    ? "0"
                    : context.Inspection.DurationMs.ToString("F1", CultureInfo.InvariantCulture));

            return Encoding.ASCII.GetBytes(text);
        }

        public static byte[] ParseHex(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("HEX 指令为空。");

            var clean = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c) || c == ',' || c == '-' || c == ':') continue;
                if (c == '0' && i + 1 < text.Length && (text[i + 1] == 'x' || text[i + 1] == 'X'))
                {
                    i++;
                    continue;
                }
                if (!Uri.IsHexDigit(c))
                    throw new FormatException("HEX 指令包含非法字符：" + c);
                clean.Append(c);
            }

            if (clean.Length == 0 || (clean.Length % 2) != 0)
                throw new FormatException("HEX 指令长度必须为非零偶数。");

            var bytes = new byte[clean.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(clean.ToString(i * 2, 2), 16);
            return bytes;
        }

        public static string ExpandEscapes(string text)
        {
            if (text == null) return string.Empty;
            return text
                .Replace("\\r", "\r")
                .Replace("\\n", "\n")
                .Replace("\\t", "\t")
                .Replace("\\0", "\0");
        }
    }
}
