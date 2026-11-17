#nullable enable
using System;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        // Use the recovery identity, never a file path or the user's map title.
        private void DiagnoseDocumentOperation(string name, Action action) =>
            DiagnoseDocumentOperation(name, () => { action(); return true; });

        private T DiagnoseDocumentOperation<T>(string name, Func<T> action, Func<T, bool>? cancelled = null)
        {
            using var operation = PluginLog.Begin(name, documentId: recoveryId, revision: session?.Revision);
            try
            {
                var result = action();
                if (cancelled?.Invoke(result) == true) operation.Cancel();
                else operation.Success(session?.Revision);
                return result;
            }
            catch (Exception error)
            {
                operation.Fail(error);
                throw;
            }
        }
    }
}
