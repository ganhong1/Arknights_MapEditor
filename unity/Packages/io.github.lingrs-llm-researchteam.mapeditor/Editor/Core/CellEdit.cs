#nullable enable
using System;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Core
{
    public sealed class CellEdit
    {
        public int Row { get; }
        public int Col { get; }
        private readonly JObject tile;
        public JObject Tile => (JObject)tile.DeepClone();

        public CellEdit(int row, int col, JObject tile)
        {
            Row = row;
            Col = col;
            this.tile = (JObject)(tile ?? throw new ArgumentNullException(nameof(tile))).DeepClone();
        }
    }
}
