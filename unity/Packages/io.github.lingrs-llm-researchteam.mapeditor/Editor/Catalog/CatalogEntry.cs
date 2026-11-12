#nullable enable
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Catalog
{
    public sealed class CatalogEntry
    {
        private readonly JObject? template;
        public string Id { get; }
        public string Name { get; }
        public string DisplayName => Name.Length == 0 ? Id : Name;
        public string Description { get; }
        public string Category { get; }
        public bool HasTemplate => template != null;
        public bool VisualDefinitionAvailable { get; }
        public string? Family { get; }
        public bool VisualOnly { get; }
        internal CatalogEntry(string id, string name, string description, string category, JObject? template,
            bool visual, string? family, bool visualOnly)
        {
            Id = id;
            Name = name.Trim();
            Description = description;
            Category = category;
            this.template = (JObject?)template?.DeepClone();
            VisualDefinitionAvailable = visual;
            Family = family;
            VisualOnly = visualOnly;
        }
        public JObject? GetTemplate() => (JObject?)template?.DeepClone();
    }
}
