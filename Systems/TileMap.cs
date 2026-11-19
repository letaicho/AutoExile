using ExileCore;
using ExileCore.Shared.Helpers;
using ExileCore.Shared.Interfaces;
using GameOffsets;
using GameOffsets.Native;
using System.Collections.Concurrent;
using System.Numerics;

namespace AutoExile.Systems
{
    /// <summary>
    /// Reads tile metadata from terrain data to locate named landmarks (boss rooms,
    /// league mechanics, exits, etc.) even beyond render range.
    /// Tile positions are stored in grid coordinates (same space as GridPosNum).
    /// </summary>
    public class TileMap
    {
        private ConcurrentDictionary<string, List<Vector2>> _tiles = new();
        private bool _loaded;
        private string _loadedArea = "";
        public int TileCount => _tiles.Count;
        public bool IsLoaded => _loaded;
        public string LoadedArea => _loadedArea;

        /// <summary>
        /// Read tile data from terrain. Call once per zone (on area change).
        /// </summary>
        public bool Load(GameController gc)
        {
            try
            {
                var terrain = gc.IngameState.Data.Terrain;

                if (terrain.NumCols == 0 || terrain.NumRows == 0)
                    return false;

                // Use HUD's own tile reader rather than raw TileStructure/TgtTileStruct reads:
                // those offset structs are no longer unmanaged in current GameOffsets, and their
                // layout tracks HUD updates. Tile.Coordinate is already in grid units
                // (tileIndex % cols * 23, tileIndex / cols * 23); Name is the TGT detail name.
                var tileList = gc.IngameState.Data.Tiles;
                if (tileList == null || tileList.Count == 0)
                    return false;

                var tiles = new ConcurrentDictionary<string, List<Vector2>>();
                foreach (var tile in tileList)
                {
                    var gridPos = new Vector2(tile.Coordinate.X, tile.Coordinate.Y);

                    if (!string.IsNullOrEmpty(tile.Path))
                        tiles.GetOrAdd(tile.Path, _ => new List<Vector2>()).Add(gridPos);

                    if (!string.IsNullOrEmpty(tile.Name))
                        tiles.GetOrAdd(tile.Name, _ => new List<Vector2>()).Add(gridPos);
                }

                _tiles = tiles;
                _loaded = true;
                _loadedArea = gc.Area?.CurrentArea?.Name ?? "unknown";
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Clear tile data (call on area change before reloading).
        /// </summary>
        public void Clear()
        {
            _tiles.Clear();
            _loaded = false;
            _loadedArea = "";
        }

        /// <summary>
        /// Find tile position by name. Tries exact match first, then substring.
        /// Returns position in GRID coordinates (multiply by GridToWorld for world coords).
        /// </summary>
        public Vector2? FindTilePosition(string searchString, Vector2 playerGridPos)
        {
            if (string.IsNullOrEmpty(searchString) || !_loaded)
                return null;

            // Exact match first
            if (_tiles.TryGetValue(searchString, out var exactResults) && exactResults.Count > 0)
            {
                return exactResults
                    .OrderBy(p => Vector2.Distance(playerGridPos, p))
                    .First();
            }

            // Substring search (case-insensitive)
            var searchLower = searchString.ToLowerInvariant();
            Vector2? bestMatch = null;
            float bestDist = float.MaxValue;

            foreach (var kvp in _tiles)
            {
                if (!kvp.Key.ToLowerInvariant().Contains(searchLower))
                    continue;

                foreach (var pos in kvp.Value)
                {
                    var dist = Vector2.Distance(playerGridPos, pos);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestMatch = pos;
                    }
                }
            }

            return bestMatch;
        }

        /// <summary>
        /// Find all tile entries matching a search string. Returns key → positions.
        /// Useful for debug listing.
        /// </summary>
        public List<(string Key, List<Vector2> Positions)> SearchTiles(string searchString)
        {
            if (string.IsNullOrEmpty(searchString) || !_loaded)
                return new();

            var searchLower = searchString.ToLowerInvariant();
            return _tiles
                .Where(kvp => kvp.Key.ToLowerInvariant().Contains(searchLower))
                .Select(kvp => (kvp.Key, kvp.Value))
                .OrderBy(x => x.Key)
                .ToList();
        }

        /// <summary>
        /// Get positions for an exact key (no substring search).
        /// </summary>
        public List<Vector2>? GetPositions(string key)
        {
            return _tiles.TryGetValue(key, out var positions) ? positions : null;
        }

        /// <summary>
        /// Get all tile keys (for debug browsing).
        /// </summary>
        public IReadOnlyCollection<string> GetAllKeys()
        {
            return _tiles.Keys.ToList().AsReadOnly();
        }

        /// <summary>
        /// Convert grid position to world position for use with our pathfinder.
        /// </summary>
        public static Vector2 GridToWorld(Vector2 gridPos)
        {
            return gridPos * Pathfinding.GridToWorld;
        }
    }
}
