#nullable enable
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Catalog
{
    /// <summary>Observed document configuration, not a reusable placement template.</summary>
    public sealed class ImportedObjectEntry
    {
        private readonly JObject raw;
        public string Id { get; }
        public string Category { get; }
        public string JsonPath { get; }
        public bool CanPlaceFromCatalog => false;
        internal ImportedObjectEntry(string id,string category,string path,JObject raw)
        { Id=id;Category=category;JsonPath=path;this.raw=(JObject)raw.DeepClone(); }
        public JObject GetConfiguration() => (JObject)raw.DeepClone();
    }
}
