using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace VisionInspect.Infrastructure.Configuration
{
    /// <summary>
    /// .NET Framework 内置 JavaScriptSerializer 的薄封装，避免引入额外 NuGet 依赖。
    /// 所有文件写入采用“临时文件 + 替换”，防止断电时留下半个 JSON。
    /// </summary>
    public static class JsonHelper
    {
        public static string Serialize(object value, bool indented = true)
        {
            var serializer = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 128
            };
            string json = serializer.Serialize(value);
            return indented ? PrettyPrint(json) : json;
        }

        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return default(T);
            var serializer = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 128
            };
            return serializer.Deserialize<T>(json);
        }

        public static T LoadFile<T>(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            return Deserialize<T>(json);
        }

        public static void SaveFileAtomic(string path, object value)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, Serialize(value, true), new UTF8Encoding(false));

            if (File.Exists(path))
            {
                try
                {
                    // File.Replace 在同一卷上保证原子替换；FAT/网络盘可能不支持。
                    File.Replace(temp, path, null, true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(temp, path, true);
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    File.Copy(temp, path, true);
                    File.Delete(temp);
                }
            }
            else
            {
                File.Move(temp, path);
            }
        }

        /// <summary>仅供阅读方便，不追求极致的 JSON 格式化性能。</summary>
        private static string PrettyPrint(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            var sb = new StringBuilder(json.Length + 128);
            int depth = 0;
            bool inString = false;
            bool escape = false;

            foreach (char c in json)
            {
                if (inString)
                {
                    sb.Append(c);
                    if (escape) escape = false;
                    else if (c == '\\') escape = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        sb.Append(c);
                        break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        sb.AppendLine();
                        depth++;
                        sb.Append(' ', depth * 2);
                        break;
                    case '}':
                    case ']':
                        sb.AppendLine();
                        depth = Math.Max(0, depth - 1);
                        sb.Append(' ', depth * 2);
                        sb.Append(c);
                        break;
                    case ',':
                        sb.Append(c);
                        sb.AppendLine();
                        sb.Append(' ', depth * 2);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
