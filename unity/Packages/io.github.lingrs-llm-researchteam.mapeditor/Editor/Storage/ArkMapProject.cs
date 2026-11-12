#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Arknights.MapEditor.Storage
{
    public sealed class ArkMapProject
    {
        public string LevelDataPath { get; }
        public IReadOnlyList<ArtMapping> ArtMappings { get; }
        public ArkMapProject(string levelDataPath, IEnumerable<ArtMapping> artMappings)
        {
            LevelDataPath = SafeProjectPath.Validate(levelDataPath);
            if (artMappings == null) throw new ArgumentNullException(nameof(artMappings));
            var list = new List<ArtMapping>(); var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in artMappings)
            {
                if (item == null || !keys.Add(item.Key)) throw new ArgumentException("Art mapping keys must be unique and entries non-null.", nameof(artMappings));
                list.Add(item);
                if (list.Count > 4096) throw new ArgumentException("At most 4096 art mappings are supported.", nameof(artMappings));
            }
            ArtMappings = new ReadOnlyCollection<ArtMapping>(list);
        }
    }
}
