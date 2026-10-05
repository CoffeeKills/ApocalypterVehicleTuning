// Minimal JSON reader with source positions (line/column on every value and key).
// Unity 2020.3's Mono has no System.Text.Json and JsonUtility cannot report positions or
// reject unknown keys, so the mod carries its own. Strict RFC 8259: no comments, no trailing
// commas, no NaN/Infinity literals, duplicate keys rejected (last-wins would be a silent default).
// Load-time only: allocates freely, never called per tick.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ApocalypterDrivetrain.Model
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    public sealed class JsonValue
    {
        public JsonKind Kind;
        public int Line, Column;
        public bool Bool;
        public double Number;
        public string String;
        public List<JsonValue> Items;                 // Array
        public List<KeyValuePair<string, JsonValue>> Members;   // Object, in source order
        public Dictionary<string, int> KeyLines;      // Object: key -> line of the key token

        public string KindName
        {
            get
            {
                switch (Kind)
                {
                    case JsonKind.Null: return "null";
                    case JsonKind.Bool: return "boolean";
                    case JsonKind.Number: return "number";
                    case JsonKind.String: return "string";
                    case JsonKind.Array: return "array";
                    default: return "object";
                }
            }
        }

        public JsonValue Get(string key)
        {
            if (Members == null) return null;
            for (int i = 0; i < Members.Count; i++)
            {
                if (Members[i].Key == key) return Members[i].Value;
            }
            return null;
        }
    }

    public sealed class JsonParseException : Exception
    {
        public readonly int Line, Column;
        public readonly string Reason;
        public readonly bool DuplicateKey;
        public JsonParseException(int line, int column, string reason, bool duplicateKey)
            : base("line " + line + ", col " + column + ": " + reason)
        {
            Line = line; Column = column; Reason = reason; DuplicateKey = duplicateKey;
        }
    }

    public static class Json
    {
        public const int MaxDepth = 64;

        public static JsonValue Parse(string text)
        {
            if (text == null) throw new JsonParseException(1, 1, "no text", false);
            var p = new Parser(text);
            p.SkipWs();
            JsonValue v = p.ParseValue(0);
            p.SkipWs();
            if (!p.AtEnd) p.Fail("unexpected trailing content '" + p.Peek() + "' after the top-level value");
            return v;
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i, _line = 1, _lineStart;

            public Parser(string s) { _s = s; }
            public bool AtEnd { get { return _i >= _s.Length; } }
            public char Peek() { return _s[_i]; }
            private int Col { get { return _i - _lineStart + 1; } }

            public void Fail(string reason) { throw new JsonParseException(_line, Col, reason, false); }

            public void SkipWs()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == '\n') { _i++; _line++; _lineStart = _i; }
                    else if (c == ' ' || c == '\t' || c == '\r') _i++;
                    else if (c == '﻿' && _i == 0) _i++;   // BOM
                    else break;
                }
            }

            public JsonValue ParseValue(int depth)
            {
                if (depth > MaxDepth) Fail("nesting deeper than " + MaxDepth);
                if (AtEnd) Fail("unexpected end of text, expected a value");
                var v = new JsonValue { Line = _line, Column = Col };
                char c = _s[_i];
                if (c == '{') { ParseObject(v, depth); return v; }
                if (c == '[') { ParseArray(v, depth); return v; }
                if (c == '"') { v.Kind = JsonKind.String; v.String = ParseString(); return v; }
                if (c == '-' || (c >= '0' && c <= '9')) { v.Kind = JsonKind.Number; v.Number = ParseNumber(); return v; }
                if (Match("true")) { v.Kind = JsonKind.Bool; v.Bool = true; return v; }
                if (Match("false")) { v.Kind = JsonKind.Bool; v.Bool = false; return v; }
                if (Match("null")) { v.Kind = JsonKind.Null; return v; }
                if (c == '/') Fail("comments are not allowed in JSON");
                Fail("unexpected character '" + c + "', expected a value");
                return null;
            }

            private bool Match(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) return false;
                int end = _i + word.Length;
                if (end < _s.Length && char.IsLetterOrDigit(_s[end])) return false;
                _i = end;
                return true;
            }

            private void ParseObject(JsonValue v, int depth)
            {
                v.Kind = JsonKind.Object;
                v.Members = new List<KeyValuePair<string, JsonValue>>();
                v.KeyLines = new Dictionary<string, int>();
                _i++;
                SkipWs();
                if (!AtEnd && _s[_i] == '}') { _i++; return; }
                while (true)
                {
                    SkipWs();
                    if (AtEnd) Fail("unexpected end of text inside an object (missing '}')");
                    if (_s[_i] != '"') Fail(_s[_i] == '}' ? "trailing comma before '}'" : "expected a quoted key, got '" + _s[_i] + "'");
                    int keyLine = _line, keyCol = Col;
                    string key = ParseString();
                    if (v.KeyLines.ContainsKey(key))
                        throw new JsonParseException(keyLine, keyCol, "duplicate key \"" + key + "\" (first on line " + v.KeyLines[key] + ")", true);
                    v.KeyLines[key] = keyLine;
                    SkipWs();
                    if (AtEnd || _s[_i] != ':') Fail("expected ':' after key \"" + key + "\"");
                    _i++;
                    SkipWs();
                    JsonValue member = ParseValue(depth + 1);
                    v.Members.Add(new KeyValuePair<string, JsonValue>(key, member));
                    SkipWs();
                    if (AtEnd) Fail("unexpected end of text inside an object (missing '}')");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return; }
                    Fail("expected ',' or '}' after the value of \"" + key + "\"");
                }
            }

            private void ParseArray(JsonValue v, int depth)
            {
                v.Kind = JsonKind.Array;
                v.Items = new List<JsonValue>();
                _i++;
                SkipWs();
                if (!AtEnd && _s[_i] == ']') { _i++; return; }
                while (true)
                {
                    SkipWs();
                    if (!AtEnd && _s[_i] == ']') Fail("trailing comma before ']'");
                    v.Items.Add(ParseValue(depth + 1));
                    SkipWs();
                    if (AtEnd) Fail("unexpected end of text inside an array (missing ']')");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return; }
                    Fail("expected ',' or ']' in array");
                }
            }

            private string ParseString()
            {
                _i++;   // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) Fail("unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\n') { _i--; Fail("unterminated string (newline inside quotes)"); }
                    if (c < 0x20) { _i--; Fail("control character inside a string"); }
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) Fail("unterminated escape");
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
                            if (_i + 4 > _s.Length) Fail("truncated \\u escape");
                            int code;
                            if (!int.TryParse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code))
                                Fail("bad \\u escape");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default: _i--; Fail("unknown escape '\\" + e + "'"); break;
                    }
                }
            }

            private double ParseNumber()
            {
                int start = _i;
                if (_s[_i] == '-') _i++;
                if (AtEnd || !char.IsDigit(_s[_i])) Fail("malformed number");
                if (_s[_i] == '0' && _i + 1 < _s.Length && char.IsDigit(_s[_i + 1])) Fail("leading zeros are not allowed");
                while (!AtEnd && char.IsDigit(_s[_i])) _i++;
                if (!AtEnd && _s[_i] == '.')
                {
                    _i++;
                    if (AtEnd || !char.IsDigit(_s[_i])) Fail("malformed number (digit expected after '.')");
                    while (!AtEnd && char.IsDigit(_s[_i])) _i++;
                }
                if (!AtEnd && (_s[_i] == 'e' || _s[_i] == 'E'))
                {
                    _i++;
                    if (!AtEnd && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                    if (AtEnd || !char.IsDigit(_s[_i])) Fail("malformed exponent");
                    while (!AtEnd && char.IsDigit(_s[_i])) _i++;
                }
                double d;
                if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    Fail("malformed number");
                return d;
            }
        }
    }
}
