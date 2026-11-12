#nullable enable
using System.IO;

namespace Arknights.MapEditor.Storage
{
    /// <summary>The target no longer matches the caller's last observed file.</summary>
    public sealed class DocumentFileConflictException : IOException
    {
        public DocumentFileConflictException(string message) : base(message) { }
    }
}
