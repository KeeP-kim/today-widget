// 최소 JSON 파서 - 외부 의존성 없음, 할당 최소화
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DeskWidget
{
    /// <summary>
    /// JSON 노드. 존재하지 않는 경로를 타고 들어가도 예외 없이 Empty를 반환한다.
    /// (n["a"]["b"][3]["c"].S 처럼 안전하게 접근 가능)
    /// </summary>
    internal sealed class JNode
    {
        public static readonly JNode Empty = new JNode();

        private readonly object _v;

        private JNode() { _v = null; }
        public JNode(object v) { _v = v; }

        public bool Exists { get { return _v != null; } }

        public JNode this[string key]
        {
            get
            {
                var d = _v as Dictionary<string, JNode>;
                JNode r;
                if (d != null && d.TryGetValue(key, out r)) return r;
                return Empty;
            }
        }

        public JNode this[int index]
        {
            get
            {
                var l = _v as List<JNode>;
                if (l != null && index >= 0 && index < l.Count) return l[index];
                return Empty;
            }
        }

        public int Count
        {
            get
            {
                var l = _v as List<JNode>;
                return l == null ? 0 : l.Count;
            }
        }

        /// <summary>문자열 값. 숫자/불리언도 문자열로 변환해서 돌려준다.</summary>
        public string S
        {
            get
            {
                var s = _v as string;
                if (s != null) return s;
                if (_v is double) return ((double)_v).ToString(CultureInfo.InvariantCulture);
                if (_v is bool) return ((bool)_v) ? "true" : "false";
                return null;
            }
        }

        /// <summary>숫자 값. "1,386.00" 처럼 콤마가 섞인 문자열도 처리한다. 실패 시 NaN.</summary>
        public double D
        {
            get
            {
                if (_v is double) return (double)_v;
                var s = _v as string;
                if (s != null)
                {
                    s = s.Replace(",", "").Trim();
                    if (s.Length == 0) return double.NaN;
                    double d;
                    if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;
                }
                return double.NaN;
            }
        }

        public bool B { get { return (_v is bool) && (bool)_v; } }
    }

    internal static class Json
    {
        /// <summary>JSON 문자열을 파싱한다. 실패하면 JNode.Empty를 반환한다(예외를 던지지 않는다).</summary>
        // 재귀 깊이 상한. 이걸 두지 않으면 '[[[[[...' 처럼 깊게 중첩된 입력에서
        // StackOverflowException 이 나는데, 이 예외는 catch 로 잡을 수 없어 프로세스가 즉사한다.
        // 설정 파일이 손상되거나 서버가 이상한 응답을 줘도 위젯이 죽으면 안 된다.
        private const int MaxDepth = 64;

        public static JNode Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return JNode.Empty;
            try
            {
                int i = 0;
                var n = ParseValue(text, ref i, 0);
                return n;
            }
            catch
            {
                return JNode.Empty;
            }
        }

        private static JNode ParseValue(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("json too deep");

            SkipWs(s, ref i);
            if (i >= s.Length) return JNode.Empty;

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i, depth);
                case '[': return ParseArray(s, ref i, depth);
                case '"': return new JNode(ParseString(s, ref i));
                case 't':
                    Expect(s, ref i, "true");
                    return new JNode(true);
                case 'f':
                    Expect(s, ref i, "false");
                    return new JNode(false);
                case 'n':
                    Expect(s, ref i, "null");
                    return JNode.Empty;
                default:
                    return new JNode(ParseNumber(s, ref i));
            }
        }

        private static JNode ParseObject(string s, ref int i, int depth)
        {
            var d = new Dictionary<string, JNode>(StringComparer.Ordinal);
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return new JNode(d); }

            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') break;
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') break;
                i++;
                var val = ParseValue(s, ref i, depth + 1);
                d[key] = val;
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return new JNode(d);
        }

        private static JNode ParseArray(string s, ref int i, int depth)
        {
            var l = new List<JNode>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return new JNode(l); }

            while (i < s.Length)
            {
                var val = ParseValue(s, ref i, depth + 1);
                l.Add(val);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return new JNode(l);
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // opening quote
            int start = i;

            // 이스케이프가 없으면 Substring 한 번으로 끝낸다 (가장 흔한 경우)
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '"') { string fast = s.Substring(start, i - start); i++; return fast; }
                if (c == '\\') break;
                i++;
            }

            var sb = new StringBuilder(s, start, i - start, i - start + 16);
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '"') { i++; break; }
                if (c == '\\')
                {
                    i++;
                    if (i >= s.Length) break;
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
                            if (i + 4 <= s.Length)
                            {
                                int code;
                                if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
                                                 CultureInfo.InvariantCulture, out code))
                                    sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                else break;
            }
            double d;
            if (double.TryParse(s.Substring(start, i - start), NumberStyles.Any,
                                CultureInfo.InvariantCulture, out d)) return d;
            return double.NaN;
        }

        private static void Expect(string s, ref int i, string lit)
        {
            if (i + lit.Length <= s.Length && string.CompareOrdinal(s, i, lit, 0, lit.Length) == 0) i += lit.Length;
            else i++;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                else break;
            }
        }

        /// <summary>설정 저장용 최소 문자열 이스케이프.</summary>
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
