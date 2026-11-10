using System;
using System.Collections.Generic;
using System.Text;

namespace VisionInspect.Infrastructure.Logging
{
    /// <summary>最小 RFC4180 CSV 编解码器。日志字段会强制压成单行，避免破坏按行读取。</summary>
    public static class CsvCodec
    {
        public static string Escape(string value)
        {
            if (value == null) return string.Empty;

            string oneLine = value.Replace("\r", " ").Replace("\n", " ");
            if (oneLine.IndexOf('"') >= 0)
                oneLine = oneLine.Replace("\"", "\"\"");
            if (oneLine.IndexOf(',') >= 0 || oneLine.IndexOf('"') >= 0)
                return "\"" + oneLine + "\"";
            return oneLine;
        }

        public static string BuildLine(params string[] fields)
        {
            if (fields == null) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(fields[i]));
            }
            return sb.ToString();
        }

        public static List<string> ParseLine(string line)
        {
            var fields = new List<string>();
            if (line == null) return fields;

            var current = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        fields.Add(current.ToString());
                        current.Length = 0;
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
            }
            fields.Add(current.ToString());
            return fields;
        }
    }
}
