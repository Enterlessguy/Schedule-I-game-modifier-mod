using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ScheduleIControlCenter
{
    internal static class JsonUtil
    {
        public static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 512
            };
        }

        public static Dictionary<string, object> ReadObject(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            object value = CreateSerializer().DeserializeObject(text);
            Dictionary<string, object> result = value as Dictionary<string, object>;
            if (result == null)
                throw new InvalidDataException("Expected a JSON object in " + path);
            return result;
        }

        public static void ValidateFile(string path)
        {
            ReadObject(path);
        }

        public static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static IEnumerable<object> AsItems(object value)
        {
            if (value == null)
                yield break;

            object[] array = value as object[];
            if (array != null)
            {
                foreach (object item in array)
                    yield return item;
                yield break;
            }

            ArrayList list = value as ArrayList;
            if (list != null)
            {
                foreach (object item in list)
                    yield return item;
                yield break;
            }

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable != null && !(value is string))
            {
                foreach (object item in enumerable)
                    yield return item;
            }
        }

        public static string GetString(Dictionary<string, object> obj, string key, string fallback)
        {
            object value;
            if (obj != null && obj.TryGetValue(key, out value) && value != null)
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            return fallback;
        }

        public static bool GetBool(Dictionary<string, object> obj, string key, bool fallback)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value) || value == null)
                return fallback;
            if (value is bool)
                return (bool)value;
            bool parsed;
            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) ? parsed : fallback;
        }

        public static int GetInt(Dictionary<string, object> obj, string key, int fallback)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value) || value == null)
                return fallback;
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        public static long GetLong(Dictionary<string, object> obj, string key, long fallback)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value) || value == null)
                return fallback;
            try { return Convert.ToInt64(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        public static decimal GetDecimal(Dictionary<string, object> obj, string key, decimal fallback)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value) || value == null)
                return fallback;
            try { return Convert.ToDecimal(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        public static void WriteObjectAtomic(string path, Dictionary<string, object> obj)
        {
            JavaScriptSerializer serializer = CreateSerializer();
            string json = PrettyPrint(serializer.Serialize(obj));

            // Parse the exact output before it can replace a save file.
            serializer.DeserializeObject(json);

            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("Target path has no directory: " + path);

            string temp = Path.Combine(directory, "." + Path.GetFileName(path) + ".controlcenter.tmp");
            string rollback = Path.Combine(directory, "." + Path.GetFileName(path) + ".controlcenter.rollback");
            File.WriteAllText(temp, json + Environment.NewLine, new UTF8Encoding(false));

            try
            {
                if (File.Exists(path))
                {
                    if (File.Exists(rollback))
                        File.Delete(rollback);
                    File.Replace(temp, path, rollback, true);
                    if (File.Exists(rollback))
                        File.Delete(rollback);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        public static string PrettyPrint(string json)
        {
            StringBuilder output = new StringBuilder(json.Length + 256);
            bool inString = false;
            bool escaped = false;
            int indent = 0;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    output.Append(c);
                    if (escaped)
                        escaped = false;
                    else if (c == '\\')
                        escaped = true;
                    else if (c == '"')
                        inString = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        output.Append(c);
                        break;
                    case '{':
                    case '[':
                        output.Append(c).AppendLine();
                        indent++;
                        AppendIndent(output, indent);
                        break;
                    case '}':
                    case ']':
                        output.AppendLine();
                        indent = Math.Max(0, indent - 1);
                        AppendIndent(output, indent);
                        output.Append(c);
                        break;
                    case ',':
                        output.Append(c).AppendLine();
                        AppendIndent(output, indent);
                        break;
                    case ':':
                        output.Append(": ");
                        break;
                    default:
                        if (!char.IsWhiteSpace(c))
                            output.Append(c);
                        break;
                }
            }
            return output.ToString();
        }

        private static void AppendIndent(StringBuilder output, int indent)
        {
            output.Append(' ', indent * 4);
        }
    }
}
