using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ReplayTimerMod
{
    internal static class MiniJson
    {
        // ── Public API ────────────────────────────────────────────────────────

        public static string Serialize(SceneIndex idx)
        {
            var sb = new StringBuilder();
            sb.Append("{\"entries\":[");
            for (int i = 0; i < idx.entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                SerializeEntry(sb, idx.entries[i]);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static SceneIndex Deserialize(string json)
        {
            var p = new Parser(json);
            p.SkipWs();
            p.Expect('{');

            var idx = new SceneIndex();

            while (true)
            {
                p.SkipWs();
                if (p.Peek() == '}') { p.Advance(); break; }
                if (p.Peek() == ',') { p.Advance(); continue; }

                string key = p.ReadString();
                p.SkipWs(); p.Expect(':'); p.SkipWs();

                if (key == "entries")
                {
                    p.Expect('[');
                    while (true)
                    {
                        p.SkipWs();
                        if (p.Peek() == ']') { p.Advance(); break; }
                        if (p.Peek() == ',') { p.Advance(); continue; }
                        idx.entries.Add(DeserializeEntry(p));
                    }
                }
                else
                {
                    p.SkipValue();
                }
            }

            return idx;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static void SerializeEntry(StringBuilder sb, EntryIndex e)
        {
            sb.Append("{\"snapshotId\":");
            AppendString(sb, e.snapshotId);
            sb.Append(",\"capturedAtUtcTicks\":");
            sb.Append(e.capturedAtUtcTicks.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"sceneName\":");
            AppendString(sb, e.sceneName);
            sb.Append(",\"entryFromScene\":");
            AppendString(sb, e.entryFromScene);
            sb.Append(",\"exitToScene\":");
            AppendString(sb, e.exitToScene);
            sb.Append(",\"totalTime\":");
            sb.Append(e.totalTime.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"hasVisualOverride\":");
            sb.Append(e.hasVisualOverride ? "true" : "false");
            sb.Append(",\"colorR\":");
            sb.Append(e.colorR.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"colorG\":");
            sb.Append(e.colorG.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"colorB\":");
            sb.Append(e.colorB.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"alpha\":");
            sb.Append(e.alpha.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"data\":");
            AppendString(sb, e.data);
            sb.Append(",\"serverRunId\":");
            AppendString(sb, e.serverRunId ?? "");
            sb.Append(",\"shareCode\":");
            AppendString(sb, e.shareCode ?? "");
            sb.Append('}');
        }

        private static void AppendString(StringBuilder sb, string s) =>
            JsonText.AppendQuoted(sb, s);

        private static EntryIndex DeserializeEntry(Parser p)
        {
            p.Expect('{');
            var e = new EntryIndex();

            while (true)
            {
                p.SkipWs();
                if (p.Peek() == '}') { p.Advance(); break; }
                if (p.Peek() == ',') { p.Advance(); continue; }

                string key = p.ReadString();
                p.SkipWs(); p.Expect(':'); p.SkipWs();

                switch (key)
                {
                    case "snapshotId":        e.snapshotId        = p.ReadString(); break;
                    case "capturedAtUtcTicks":e.capturedAtUtcTicks = p.ReadLong();  break;
                    case "sceneName":      e.sceneName      = p.ReadString(); break;
                    case "entryFromScene": e.entryFromScene = p.ReadString(); break;
                    case "exitToScene":    e.exitToScene    = p.ReadString(); break;
                    case "totalTime":      e.totalTime      = p.ReadFloat();  break;
                    case "hasVisualOverride": e.hasVisualOverride = p.ReadBool(); break;
                    case "colorR":         e.colorR         = p.ReadFloat();  break;
                    case "colorG":         e.colorG         = p.ReadFloat();  break;
                    case "colorB":         e.colorB         = p.ReadFloat();  break;
                    case "alpha":          e.alpha          = p.ReadFloat();  break;
                    case "data":           e.data           = p.ReadString(); break;
                    case "serverRunId":    e.serverRunId    = p.ReadString(); break;
                    case "shareCode":      e.shareCode      = p.ReadString(); break;
                    default:               p.SkipValue();                     break;
                }
            }

            return e;
        }

        // ── Recursive-descent parser ──────────────────────────────────────────

        private sealed class Parser
        {
            private readonly string _s;
            private int _pos;

            public Parser(string s) { _s = s; }

            public char Peek() => _pos < _s.Length ? _s[_pos] : '\0';
            public void Advance() => _pos++;

            public void SkipWs()
            {
                while (_pos < _s.Length && _s[_pos] <= ' ') _pos++;
            }

            public void Expect(char c)
            {
                SkipWs();
                if (_pos >= _s.Length || _s[_pos] != c)
                    throw new Exception(
                        $"MiniJson: expected '{c}' at pos {_pos}, got '{Peek()}'");
                _pos++;
            }

            public string ReadString()
            {
                SkipWs();
                Expect('"');
                var sb = new StringBuilder();
                while (_pos < _s.Length)
                {
                    char c = _s[_pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }

                    char esc = _s[_pos++];
                    switch (esc)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/');  break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'u':
                            sb.Append((char)Convert.ToInt32(
                                _s.Substring(_pos, 4), 16));
                            _pos += 4;
                            break;
                        default: sb.Append(esc); break;
                    }
                }
                throw new Exception("MiniJson: unterminated string");
            }

            public float ReadFloat()
            {
                int start = _pos;
                while (_pos < _s.Length)
                {
                    char c = _s[_pos];
                    if (c == ',' || c == '}' || c == ']' || c <= ' ') break;
                    _pos++;
                }
                return float.Parse(
                    _s.Substring(start, _pos - start),
                    CultureInfo.InvariantCulture);
            }

            public long ReadLong()
            {
                int start = _pos;
                while (_pos < _s.Length)
                {
                    char c = _s[_pos];
                    if (c == ',' || c == '}' || c == ']' || c <= ' ') break;
                    _pos++;
                }
                return long.Parse(
                    _s.Substring(start, _pos - start),
                    CultureInfo.InvariantCulture);
            }

            public bool ReadBool()
            {
                SkipWs();
                bool value = Peek() == 't';
                SkipValue(); // consume the true/false literal
                return value;
            }

            /// Skips any JSON value without interpreting it (forward-compat).
            public void SkipValue()
            {
                SkipWs();
                char c = Peek();
                if (c == '"') { ReadString(); return; }
                if (c == '{' || c == '[')
                {
                    char close = c == '{' ? '}' : ']';
                    Advance();
                    int depth = 1;
                    while (_pos < _s.Length && depth > 0)
                    {
                        char ch = _s[_pos++];
                        if (ch == '"')
                        {
                            while (_pos < _s.Length)
                            {
                                char sc = _s[_pos++];
                                if (sc == '\\') _pos++;
                                else if (sc == '"') break;
                            }
                        }
                        else if (ch == c)    depth++;
                        else if (ch == close) depth--;
                    }
                    return;
                }
                // number or literal (true / false / null)
                while (_pos < _s.Length)
                {
                    char ch = _s[_pos];
                    if (ch == ',' || ch == '}' || ch == ']' || ch <= ' ') break;
                    _pos++;
                }
            }
        }
    }
}