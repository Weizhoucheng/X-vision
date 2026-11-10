using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using VisionInspect.Core.Models;

namespace VisionInspect.VisionMaster.Parameters
{
    /// <summary>参数预设文件读写，使用 .NET Framework 内置 JSON 序列化，不引入额外依赖。</summary>
    public static class ParameterPresetStore
    {
        public static void Save(string path, ParameterSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path");
            if (snapshot == null || snapshot.Values == null) throw new ArgumentNullException("snapshot");
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
            string json = serializer.Serialize(snapshot);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temp, path, null, true);
                else
                    File.Move(temp, path);
            }
            finally
            {
                // 替换失败时保留原预设，不能退化成覆盖写而破坏已有参数。
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        public static ParameterSnapshot Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("参数预设文件不存在。", path);

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
            ParameterSnapshot snapshot = serializer.Deserialize<ParameterSnapshot>(File.ReadAllText(path, Encoding.UTF8));
            if (snapshot == null || snapshot.Values == null)
                throw new InvalidDataException("参数预设文件格式无效。");
            return snapshot;
        }

        public static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unnamed";
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }
    }
}
