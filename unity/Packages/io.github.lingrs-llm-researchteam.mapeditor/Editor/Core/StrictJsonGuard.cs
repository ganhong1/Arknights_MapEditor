#nullable enable
using System;
using System.Collections.Generic;

namespace Arknights.MapEditor.Core
{
    // Json.NET accepts JavaScript extensions. This bounded syntax gate rejects them;
    // Json.NET remains responsible for decoding strings, duplicate keys and the tree.
    internal sealed class StrictJsonGuard
    {
        private readonly string text;
        private int offset;
        private readonly List<string> fractions = new List<string>();

        private StrictJsonGuard(string text) { this.text = text; }

        internal static IReadOnlyList<string> Validate(string text)
        {
            var guard = new StrictJsonGuard(text);
            guard.Value(0);
            guard.Space();
            if (guard.offset != text.Length) guard.Fail();
            return guard.fractions;
        }

        private void Value(int depth)
        {
            Space();
            if (offset >= text.Length) Fail();
            var c = text[offset];
            if (c == '{' || c == '[')
            {
                if (depth >= LevelDocument.MaximumDepth) Fail("JSON exceeds maximum depth");
                Container(c == '{', depth + 1);
            }
            else if (c == '"') String();
            else if (c == 't') Literal("true");
            else if (c == 'f') Literal("false");
            else if (c == 'n') Literal("null");
            else Number();
        }

        private void Container(bool isObject, int depth)
        {
            offset++;
            Space();
            var end = isObject ? '}' : ']';
            if (Take(end)) return;
            while (true)
            {
                if (isObject)
                {
                    String();
                    Space();
                    if (!Take(':')) Fail();
                }
                Value(depth);
                Space();
                if (Take(end)) return;
                if (!Take(',')) Fail();
                Space();
                // A trailing comma cannot begin a key or value.
                if (offset == text.Length || text[offset] == end) Fail();
            }
        }

        private void String()
        {
            if (!Take('"')) Fail();
            while (offset < text.Length)
            {
                var c = text[offset++];
                if (c == '"') return;
                if (c < 0x20) Fail();
                if (c != '\\') continue;
                if (offset == text.Length) Fail();
                c = text[offset++];
                if (c == 'u')
                {
                    var unit = UnicodeUnit();
                    if (char.IsHighSurrogate(unit))
                    {
                        // Json.NET replaces lone escaped surrogates before the tree exists.
                        // Reject that lossy recovery here, including in property names.
                        if (!Take('\\') || !Take('u') || !char.IsLowSurrogate(UnicodeUnit()))
                            Fail("Unpaired Unicode surrogate escape");
                    }
                    else if (char.IsLowSurrogate(unit)) Fail("Unpaired Unicode surrogate escape");
                }
                else if ("\"\\/bfnrt".IndexOf(c) < 0) Fail();
            }
            Fail();
        }

        private char UnicodeUnit()
        {
            var value = 0;
            for (var i = 0; i < 4; i++)
            {
                if (offset == text.Length || !Hex(text[offset])) Fail();
                var c = text[offset++];
                value = value * 16 + (c <= '9' ? c - '0' : char.ToLowerInvariant(c) - 'a' + 10);
            }
            return (char)value;
        }

        private void Number()
        {
            var start = offset;
            Take('-');
            if (!Take('0'))
            {
                if (offset == text.Length || text[offset] < '1' || text[offset] > '9') Fail();
                Digits();
            }
            var fraction = false;
            if (Take('.')) { fraction = true; RequiredDigits(); }
            if (Take('e') || Take('E'))
            {
                fraction = true;
                if (!Take('+')) Take('-');
                RequiredDigits();
            }
            if (fraction) fractions.Add(text.Substring(start, offset - start));
        }

        private void RequiredDigits()
        {
            var start = offset;
            Digits();
            if (start == offset) Fail();
        }

        private void Digits()
        {
            while (offset < text.Length && text[offset] >= '0' && text[offset] <= '9') offset++;
        }

        private void Literal(string value)
        {
            for (var i = 0; i < value.Length; i++)
                if (offset == text.Length || text[offset++] != value[i]) Fail();
        }

        private bool Take(char value)
        {
            if (offset >= text.Length || text[offset] != value) return false;
            offset++;
            return true;
        }

        private void Space()
        {
            while (offset < text.Length && " \t\r\n".IndexOf(text[offset]) >= 0) offset++;
        }

        private static bool Hex(char c) => c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F';

        private void Fail(string message = "Invalid strict JSON syntax") =>
            throw new LevelDocumentException(message, "$ (character " + offset + ")");
    }
}
