using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WuwaChecklist
{
    /// <summary>
    /// 极简 JSON 读写。只依赖 BCL，不引任何第三方包。
    /// 解析结果类型：Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null
    /// </summary>
    internal static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            int i = 0;
            object v = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i < text.Length) throw new FormatException("JSON 尾部有多余字符，位置 " + i);
            return v;
        }

        public static bool TryParse(string text, out object value, out string error)
        {
            try
            {
                value = Parse(text);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                value = null;
                error = ex.Message;
                return false;
            }
        }

        // ---------------- 读 ----------------

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                break;
            }
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 意外结束");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't':
                    Expect(s, ref i, "true");
                    return true;
                case 'f':
                    Expect(s, ref i, "false");
                    return false;
                case 'n':
                    Expect(s, ref i, "null");
                    return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref i);
                    throw new FormatException("JSON 非法字符 '" + c + "' 位置 " + i);
            }
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException("JSON 期望 " + word + "，位置 " + i);
            i += word.Length;
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("JSON 对象键必须是字符串，位置 " + i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("JSON 缺少 ':'，位置 " + i);
                i++;
                d[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 对象未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException("JSON 对象里出现意外字符 '" + s[i] + "' 位置 " + i);
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 数组未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException("JSON 数组里出现意外字符 '" + s[i] + "' 位置 " + i);
            }
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // "
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("JSON 字符串未闭合");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("JSON 转义不完整");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("JSON \\u 转义不完整");
                        sb.Append((char)ushort.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("JSON 未知转义 \\" + e);
                }
            }
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { i++; continue; }
                break;
            }
            string tok = s.Substring(start, i - start);
            double d;
            if (!double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("JSON 数字格式错误: " + tok);
            return d;
        }

        // ---------------- 写 ----------------

        public static string Stringify(object value, bool pretty = true)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, pretty, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object v, bool pretty, int depth)
        {
            if (v == null) { sb.Append("null"); return; }

            var s = v as string;
            if (s != null) { WriteString(sb, s); return; }

            if (v is bool) { sb.Append(((bool)v) ? "true" : "false"); return; }

            if (v is int || v is long || v is short || v is byte) { sb.Append(Convert.ToInt64(v, CultureInfo.InvariantCulture)); return; }
            if (v is double || v is float || v is decimal)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15)
                    sb.Append(((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture));
                else
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            var dict = v as IDictionary<string, object>;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    if (pretty) { sb.Append('\n'); Indent(sb, depth + 1); }
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    if (pretty) sb.Append(' ');
                    WriteValue(sb, kv.Value, pretty, depth + 1);
                }
                if (pretty) { sb.Append('\n'); Indent(sb, depth); }
                sb.Append('}');
                return;
            }

            var list = v as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    if (pretty) { sb.Append('\n'); Indent(sb, depth + 1); }
                    WriteValue(sb, item, pretty, depth + 1);
                }
                if (pretty && !first) { sb.Append('\n'); Indent(sb, depth); }
                sb.Append(']');
                return;
            }

            WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        private static void Indent(StringBuilder sb, int depth)
        {
            sb.Append(' ', depth * 2);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------------- 取值便利方法 ----------------

        public static Dictionary<string, object> Obj(object v)
        {
            return v as Dictionary<string, object> ?? new Dictionary<string, object>(StringComparer.Ordinal);
        }

        public static List<object> Arr(object v)
        {
            var l = v as List<object>;
            if (l != null) return l;
            var r = new List<object>();
            var e = v as IEnumerable;
            if (e != null) foreach (var x in e) r.Add(x);
            return r;
        }

        public static string Str(Dictionary<string, object> o, string key, string def = "")
        {
            if (o == null) return def;
            object v;
            if (!o.TryGetValue(key, out v) || v == null) return def;
            var s = v as string;
            if (s != null) return s;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static int Int(Dictionary<string, object> o, string key, int def = 0)
        {
            if (o == null) return def;
            object v;
            if (!o.TryGetValue(key, out v) || v == null) return def;
            try { return (int)Math.Round(Convert.ToDouble(v, CultureInfo.InvariantCulture)); }
            catch { return def; }
        }

        public static double Dbl(Dictionary<string, object> o, string key, double def = 0)
        {
            if (o == null) return def;
            object v;
            if (!o.TryGetValue(key, out v) || v == null) return def;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch { return def; }
        }

        public static bool Bool(Dictionary<string, object> o, string key, bool def = false)
        {
            if (o == null) return def;
            object v;
            if (!o.TryGetValue(key, out v) || v == null) return def;
            if (v is bool) return (bool)v;
            string s = v as string;
            if (s != null) return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture) != 0; }
            catch { return def; }
        }

        public static string[] StrArray(Dictionary<string, object> o, string key)
        {
            if (o == null) return new string[0];
            object v;
            if (!o.TryGetValue(key, out v) || v == null) return new string[0];
            var list = Arr(v);
            var res = new List<string>(list.Count);
            foreach (var item in list)
            {
                string s = item as string;
                if (s != null && s.Length > 0) res.Add(s);
            }
            return res.ToArray();
        }
    }
}
