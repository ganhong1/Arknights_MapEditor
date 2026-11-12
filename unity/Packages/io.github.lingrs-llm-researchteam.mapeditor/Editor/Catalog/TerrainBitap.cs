#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;

namespace Arknights.MapEditor.Catalog
{
    // Specialized port of Fuse.js 7.5.0 src/search/bitap/search.ts for ignoreLocation,
    // threshold 0.2, minMatchCharLength 1 and no match indices.
    // Copyright (c) 2026 Kiro Risk. Licensed under Apache-2.0. See SearchDependencies/LICENSE-Fuse.txt.
    internal sealed class TerrainBitap
    {
        private const double Threshold = 0.2;
        private readonly Dictionary<string, Dictionary<char, int>> alphabets =
            new Dictionary<string, Dictionary<char, int>>(StringComparer.Ordinal);

        internal double Score(string text, string pattern)
        {
            if (text.IndexOf(pattern, StringComparison.Ordinal) >= 0) return 0.001;
            if (1d / pattern.Length > Threshold) return 1;
            if (!alphabets.TryGetValue(pattern, out var alphabet))
            {
                if (alphabets.Count >= 128) alphabets.Clear();
                alphabet = new Dictionary<char, int>();
                for (var i = 0; i < pattern.Length; i++)
                {
                    alphabet.TryGetValue(pattern[i], out var mask);
                    alphabet[pattern[i]] = mask | (1 << (pattern.Length - i - 1));
                }
                alphabets.Add(pattern, alphabet);
            }

            var finish = text.Length + pattern.Length;
            var matchBit = 1 << (pattern.Length - 1);
            var current = ArrayPool<int>.Shared.Rent(finish + 2);
            var previous = ArrayPool<int>.Shared.Rent(finish + 2);
            try
            {
                for (var errors = 0; errors / (double)pattern.Length <= Threshold; errors++)
                {
                    current[finish + 1] = (1 << errors) - 1;
                    for (var position = finish; position >= 1; position--)
                    {
                        var charMask = position <= text.Length && alphabet.TryGetValue(text[position - 1], out var bits)
                            ? bits : 0;
                        var match = ((current[position + 1] << 1) | 1) & charMask;
                        if (errors != 0)
                            match |= ((previous[position + 1] | previous[position]) << 1) | 1 | previous[position + 1];
                        current[position] = match;
                        if ((match & matchBit) != 0) return Math.Max(0.001, errors / (double)pattern.Length);
                    }
                    (previous, current) = (current, previous);
                }
                return 1;
            }
            finally
            {
                ArrayPool<int>.Shared.Return(current);
                ArrayPool<int>.Shared.Return(previous);
            }
        }
    }
}
