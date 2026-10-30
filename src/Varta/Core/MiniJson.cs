using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Varta.Core
{
    /// <summary>Мінімальний JSON-парсер/серіалізатор без залежностей (однаково працює на net48 і net8).</summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException("json");
            if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);
            var p = new Parser(json);
            object v = p.ReadValue();
            p.SkipWs();
            if (!p.AtEnd) throw new FormatException("JSON: зайві дані після значення, позиція " + p.Pos);
            return v;
        }

        public static string Serialize(object value, bool indent)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, indent, 0);
            return sb.ToString();
        }

        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }
        public static List<object> Arr(object o) { return o as List<object>; }

        public static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v)) return null;
            return v;
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            if (v == null) return "";
            var s = v as string;
            if (s != null) return s;
            if (v is bool) return ((bool)v) ? "true" : "false";
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        }

        static void NewLine(StringBuilder sb, bool indent, int level)
        {
            if (!indent) return;
            sb.Append('\n');
            sb.Append(' ', level * 2);
        }

        static void WriteValue(StringBuilder sb, object v, bool indent, int level)
        {
            if (v == null) { sb.Append("null"); return; }
            var s = v as string;
            if (s != null) { WriteString(sb, s); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double || v is float || v is decimal)
            {
                sb.Append(Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (v is int || v is long || v is short || v is byte || v is uint || v is ulong || v is ushort || v is sbyte)
            {
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
                return;
            }
            if (v is Enum) { WriteString(sb, v.ToString()); return; }

            var dict = v as IDictionary;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, indent, level + 1);
                    WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture));
                    sb.Append(indent ? ": " : ":");
                    WriteValue(sb, e.Value, indent, level + 1);
                }
                NewLine(sb, indent, level);
                sb.Append('}');
                return;
            }

            var list = v as IEnumerable;
            if (list != null)
            {
                var items = new List<object>();
                foreach (object o in list) items.Add(o);
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append('[');
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    NewLine(sb, indent, level + 1);
                    WriteValue(sb, items[i], indent, level + 1);
                }
                NewLine(sb, indent, level);
                sb.Append(']');
                return;
            }

            WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
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
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Parser
        {
            readonly string _s;
            int _i;

            public Parser(string s) { _s = s; }
            public int Pos { get { return _i; } }
            public bool AtEnd { get { return _i >= _s.Length; } }

            public void SkipWs()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            FormatException Err(string msg) { return new FormatException("JSON: " + msg + ", позиція " + _i); }

            public object ReadValue()
            {
                SkipWs();
                if (AtEnd) throw Err("неочікуваний кінець даних");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            void Expect(string lit)
            {
                if (string.CompareOrdinal(_s, _i, lit, 0, lit.Length) != 0) throw Err("очікувалось '" + lit + "'");
                _i += lit.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>();
                _i++; // {
                SkipWs();
                if (!AtEnd && _s[_i] == '}') { _i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (AtEnd || _s[_i] != '"') throw Err("очікувався ключ у лапках");
                    string key = ReadString();
                    SkipWs();
                    if (AtEnd || _s[_i] != ':') throw Err("очікувалось ':'");
                    _i++;
                    d[key] = ReadValue();
                    SkipWs();
                    if (AtEnd) throw Err("неочікуваний кінець об'єкта");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return d; }
                    throw Err("очікувалось ',' або '}'");
                }
            }

            List<object> ReadArray()
            {
                var l = new List<object>();
                _i++; // [
                SkipWs();
                if (!AtEnd && _s[_i] == ']') { _i++; return l; }
                while (true)
                {
                    l.Add(ReadValue());
                    SkipWs();
                    if (AtEnd) throw Err("неочікуваний кінець масиву");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return l; }
                    throw Err("очікувалось ',' або ']'");
                }
            }

            string ReadString()
            {
                var sb = new StringBuilder();
                _i++; // "
                while (true)
                {
                    if (AtEnd) throw Err("незакритий рядок");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Err("незавершена escape-послідовність");
                    char e = _s[_i++];
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
                            if (_i + 4 > _s.Length) throw Err("некоректний \\u");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw Err("невідома escape-послідовність \\" + e);
                    }
                }
            }

            object ReadNumber()
            {
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                if (start == _i) throw Err("неочікуваний символ '" + _s[_i] + "'");
                string t = _s.Substring(start, _i - start);
                if (t.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0)
                    return double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture);
                long l;
                if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
                return double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
