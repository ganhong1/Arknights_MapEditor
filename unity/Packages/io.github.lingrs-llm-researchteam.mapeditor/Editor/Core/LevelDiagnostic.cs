#nullable enable
namespace Arknights.MapEditor.Core
{
    public sealed class LevelDiagnostic
    {
        public string Severity { get; }
        public string Path { get; }
        public string Message { get; }
        internal LevelDiagnostic(string severity, string path, string message)
        {
            Severity = severity; Path = path; Message = message;
        }
    }
}
