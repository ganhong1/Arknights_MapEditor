#nullable enable
using System;

namespace Arknights.MapEditor.Storage
{
    public sealed class ArtMapping
    {
        public string Key { get; }
        public string ResourcePath { get; }
        public string? UnityGuid { get; }
        public ArtMapping(string key, string resourcePath, string? unityGuid = null)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Art mapping key is required.", nameof(key));
            if (unityGuid != null && (unityGuid.Length != 32 || !Guid.TryParseExact(unityGuid, "N", out _)))
                throw new ArgumentException("Unity GUID must contain 32 hexadecimal characters.", nameof(unityGuid));
            Key = key; ResourcePath = SafeProjectPath.Validate(resourcePath); UnityGuid = unityGuid;
        }
    }
}
