// Minimal, fast JSON reader/writer (no external dependencies).
// Objects -> Dictionary<string, object>, arrays -> List<object>, numbers -> double.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Tactile
{
    public static class Json
    {
        public static object Parse(string s)
        {
            var p = new Parser(s);
            object v = p.Value();
            return v;
        }

        sealed class Parser
        {
            readonly string s;
            int i;

            public Parser(string s) { this.s = s; }

            void Ws()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                    else break;
                }
            }

            char Peek()
            {
                if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
                return s[i];
            }

            public object Value()
            {
                Ws();
                char c = Peek();
                if (c == '{') return Obj();
                if (c == '[') return Arr();
                if (c == '"') return Str();
                if (c == 't') { Expect("true"); return true; }
                if (c == 'f') { Expect("false"); return false; }
                if (c == 'n') { Expect("null"); return null; }
                return Num();
            }

            void Expect(string w)
            {
                if (i + w.Length > s.Length || string.CompareOrdinal(s, i, w, 0, w.Length) != 0)
                    throw new FormatException("Bad JSON literal at " + i);
                i += w.Length;
            }

            Dictionary<string, object> Obj()
            {
                var d = new Dictionary<string, object>();
                i++;
                Ws();
                if (Peek() == '}') { i++; return d; }
                while (true)
                {
                    Ws();
                    string k = Str();
                    Ws();
                    if (Peek() != ':') throw new FormatException("Expected ':' at " + i);
                    i++;
                    d[k] = Value();
                    Ws();
                    char c = Peek();
                    i++;
                    if (c == ',') continue;
                    if (c == '}') return d;
                    throw new FormatException("Expected ',' or '}' at " + i);
                }
            }

            List<object> Arr()
            {
                var l = new List<object>();
                i++;
                Ws();
                if (Peek() == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value());
                    Ws();
                    char c = Peek();
                    i++;
                    if (c == ',') continue;
                    if (c == ']') return l;
                    throw new FormatException("Expected ',' or ']' at " + i);
                }
            }

            string Str()
            {
                if (Peek() != '"') throw new FormatException("Expected string at " + i);
                i++;
                StringBuilder sb = null;
                int start = i;
                while (true)
                {
                    char c = Peek();
                    if (c == '"')
                    {
                        string r;
                        if (sb == null) r = s.Substring(start, i - start);
                        else { sb.Append(s, start, i - start); r = sb.ToString(); }
                        i++;
                        return r;
                    }
                    if (c == '\\')
                    {
                        if (sb == null) sb = new StringBuilder();
                        sb.Append(s, start, i - start);
                        i++;
                        char e = Peek();
                        i++;
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'u':
                                sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                                i += 4;
                                break;
                            default: sb.Append(e); break;
                        }
                        start = i;
                        continue;
                    }
                    i++;
                }
            }

            object Num()
            {
                int st = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (i == st) throw new FormatException("Unexpected character at " + i);
                return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }

        public static string Write(object v)
        {
            var sb = new StringBuilder(4096);
            W(sb, v);
            return sb.ToString();
        }

        static void W(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            var str = v as string;
            if (str != null) { Q(sb, str); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double || v is int || v is long || v is float || v is decimal)
            {
                sb.Append(Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var dict = v as IDictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Q(sb, kv.Key);
                    sb.Append(':');
                    W(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            var en = v as IEnumerable;
            if (en != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in en)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    W(sb, item);
                }
                sb.Append(']');
                return;
            }
            Q(sb, v.ToString());
        }

        static void Q(StringBuilder sb, string s)
        {
            sb.Append('"');
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                string rep = null;
                if (c == '"') rep = "\\\"";
                else if (c == '\\') rep = "\\\\";
                else if (c == '\n') rep = "\\n";
                else if (c == '\r') rep = "\\r";
                else if (c == '\t') rep = "\\t";
                else if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029) rep = "\\u" + ((int)c).ToString("x4");
                if (rep != null)
                {
                    sb.Append(s, start, i - start);
                    sb.Append(rep);
                    start = i + 1;
                }
            }
            sb.Append(s, start, s.Length - start);
            sb.Append('"');
        }

        // ---- Accessors ----
        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }
        public static List<object> Arr(object o) { return o as List<object>; }

        public static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v;
            return null;
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            return Get(d, key) as string;
        }

        public static bool Bool(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            if (v is bool) return (bool)v;
            if (v is double) return (double)v != 0;
            var s = v as string;
            if (s != null) return s == "true" || s == "1";
            return false;
        }

        public static double? Num(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            if (v is double) return (double)v;
            return null;
        }
    }
}
