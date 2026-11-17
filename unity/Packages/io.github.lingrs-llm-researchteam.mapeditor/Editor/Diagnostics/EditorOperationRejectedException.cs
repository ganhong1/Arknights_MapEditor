#nullable enable
using System;

namespace Arknights.MapEditor.Editor
{
    /// <summary>A known editor precondition requires user action before the command can proceed.</summary>
    internal sealed class EditorOperationRejectedException : InvalidOperationException
    {
        internal EditorOperationRejectedException(string message) : base(message) { }
    }
}
