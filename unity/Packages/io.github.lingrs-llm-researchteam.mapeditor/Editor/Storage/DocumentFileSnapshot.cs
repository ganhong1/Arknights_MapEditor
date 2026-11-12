#nullable enable
namespace Arknights.MapEditor.Storage
{
    /// <summary>Decoded UTF-8 text and SHA-256 of the exact bytes read or written.</summary>
    public sealed class DocumentFileSnapshot
    {
        public string Text { get; }
        public string Sha256 { get; }

        internal DocumentFileSnapshot(string text, string sha256)
        {
            Text = text;
            Sha256 = sha256;
        }
    }
}
