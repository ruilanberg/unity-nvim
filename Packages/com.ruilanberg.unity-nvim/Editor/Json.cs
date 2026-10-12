using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UnityNvim.Editor
{
    /// <summary>
    /// Minimal JSON for the bridge protocol (JsonUtility can't do dictionaries or dynamic shapes).
    /// Parse returns Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, double, bool or null.
    /// </summary>
    public static class Json
    {
        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        public static object Parse(string json)
        {
            var p = new Parser(json);
            var v = p.Value();
            p.SkipWhite();
            if (!p.End)
                throw new FormatException("trailing characters in JSON");
            return v;
        }

        static void Write(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case IDictionary dict:
                    sb.Append('{');
                    var first = true;
                    foreach (DictionaryEntry e in dict)
                    {
                        if (!first)
                            sb.Append(',');
                        first = false;
                        WriteString(sb, e.Key.ToString());
                        sb.Append(':');
                        Write(sb, e.Value);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable list:
                    sb.Append('[');
                    var firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem)
                            sb.Append(',');
                        firstItem = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    break;
                case Enum e:
                    WriteString(sb, e.ToString());
                    break;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case IFormattable num:
                    sb.Append(num.ToString(null, CultureInfo.InvariantCulture));
                    break;
                default:
                    WriteString(sb, v.ToString());
                    break;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Parser
        {
            readonly string s;
            int i;

            public Parser(string json) => s = json ?? "";
            public bool End => i >= s.Length;

            public void SkipWhite()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i]))
                    i++;
            }

            public object Value()
            {
                SkipWhite();
                if (End)
                    throw new FormatException("unexpected end of JSON");
                var c = s[i];
                switch (c)
                {
                    case '{': return Object();
                    case '[': return Array();
                    case '"': return String();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return Number();
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                    throw new FormatException("invalid JSON literal at " + i);
                i += word.Length;
            }

            Dictionary<string, object> Object()
            {
                var d = new Dictionary<string, object>();
                i++;
                SkipWhite();
                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return d;
                }
                while (true)
                {
                    SkipWhite();
                    var key = String();
                    SkipWhite();
                    if (End || s[i] != ':')
                        throw new FormatException("expected ':' at " + i);
                    i++;
                    d[key] = Value();
                    SkipWhite();
                    if (End)
                        throw new FormatException("unterminated object");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("expected ',' or '}' at " + i);
                }
            }

            List<object> Array()
            {
                var l = new List<object>();
                i++;
                SkipWhite();
                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return l;
                }
                while (true)
                {
                    l.Add(Value());
                    SkipWhite();
                    if (End)
                        throw new FormatException("unterminated array");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("expected ',' or ']' at " + i);
                }
            }

            string String()
            {
                if (End || s[i] != '"')
                    throw new FormatException("expected string at " + i);
                i++;
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    var c = s[i++];
                    if (c == '"')
                        return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (End)
                        break;
                    var e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 > s.Length)
                                throw new FormatException("bad \\u escape");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                            i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
                throw new FormatException("unterminated string");
            }

            double Number()
            {
                var start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
                    i++;
                if (start == i)
                    throw new FormatException("unexpected character '" + s[i] + "' at " + i);
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }

        // helpers for reading parsed messages
        public static string Str(this Dictionary<string, object> d, string key, string fallback = null) =>
            d != null && d.TryGetValue(key, out var v) && v is string s ? s : fallback;

        public static int Int(this Dictionary<string, object> d, string key, int fallback = 0) =>
            d != null && d.TryGetValue(key, out var v) && v is double n ? (int)n : fallback;

        public static bool Bool(this Dictionary<string, object> d, string key, bool fallback = false) =>
            d != null && d.TryGetValue(key, out var v) && v is bool b ? b : fallback;
    }
}
