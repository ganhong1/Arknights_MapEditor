#nullable enable
using System;

namespace Arknights.MapEditor.Core
{
    public sealed class ScheduleAddress
    {
        public string? BranchKey { get; }
        public int Wave { get; }
        public int Part { get; }
        private ScheduleAddress(string? branchKey, int wave, int part)
        {
            if (wave < 0) throw new ArgumentOutOfRangeException(nameof(wave));
            if (part < 0) throw new ArgumentOutOfRangeException(nameof(part));
            BranchKey = branchKey; Wave = wave; Part = part;
        }
        public static ScheduleAddress Main(int wave, int fragment) => new ScheduleAddress(null, wave, fragment);
        public static ScheduleAddress Branch(string key, int phase) => new ScheduleAddress(
            string.IsNullOrEmpty(key) ? throw new ArgumentException("Branch key required", nameof(key)) : key, 0, phase);
    }
}
