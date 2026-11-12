#nullable enable
using System;

namespace Arknights.MapEditor.Core
{
    public sealed class LevelDocumentException : FormatException
    {
        public string Path { get; }

        public LevelDocumentException(string message, string path = "$", Exception? inner = null)
            : base(message + " (" + path + ")", inner)
        {
            Path = path;
        }
    }
}
