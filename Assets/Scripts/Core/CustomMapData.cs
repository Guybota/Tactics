using System;
using System.Collections.Generic;

namespace Tactics.Core
{
    // Authoring data only. Match saves remain a separate format and directory.
    [Serializable]
    public sealed class CustomMapData
    {
        public const int CurrentVersion = 1;
        public int schemaVersion = CurrentVersion;
        public string id;
        public string name;
        public int width, height, playerCount = 2;
        public List<MapCellData> cells = new List<MapCellData>();
        public List<MapStartData> starts = new List<MapStartData>();
        public MapCellData Cell(int x, int y) => cells[y * width + x];
        public MapStartData StartFor(int slot) => starts.Find(s => s.slot == slot);
        public CustomMapData Clone()
        {
            var result = new CustomMapData { schemaVersion = schemaVersion, id = id, name = name,
                width = width, height = height, playerCount = playerCount };
            foreach (var cell in cells) result.cells.Add(new MapCellData { terrain = cell.terrain, castle = cell.castle, castleOwner = cell.castleOwner });
            foreach (var start in starts) result.starts.Add(new MapStartData { slot = start.slot, x = start.x, y = start.y });
            return result;
        }
    }
    [Serializable]
    public sealed class MapCellData { public TerrainKind terrain; public bool castle; public int castleOwner = -1; }
    [Serializable]
    public sealed class MapStartData { public int slot, x, y; }

    public static class CustomMapValidator
    {
        public static void ValidateStructure(CustomMapData map)
        {
            if (map == null) throw new ArgumentException("Map data is missing.");
            if (map.schemaVersion != CustomMapData.CurrentVersion) throw new ArgumentException("Unsupported map format.");
            if (!Guid.TryParseExact(map.id, "N", out _)) throw new ArgumentException("Invalid map identifier.");
            if (string.IsNullOrWhiteSpace(map.name) || map.name.Length > 64 || HasControlCharacters(map.name))
                throw new ArgumentException("Map name must contain 1–64 characters.");
            if (map.width < 10 || map.width > 50 || map.height < 10 || map.height > 50)
                throw new ArgumentException("Map dimensions must be between 10 and 50.");
            if (map.playerCount < 2 || map.playerCount > 4) throw new ArgumentException("Choose 2–4 participants.");
            if (map.cells == null || map.cells.Count != map.width * map.height) throw new ArgumentException("Incomplete terrain grid.");
            foreach (var cell in map.cells)
            {
                if (cell == null || !Enum.IsDefined(typeof(TerrainKind), cell.terrain)) throw new ArgumentException("Unknown terrain.");
                if (cell.castle && cell.terrain != TerrainKind.Grass) throw new ArgumentException("Castles must stand on grass.");
                if (cell.castleOwner < -1 || cell.castleOwner > 3 || (!cell.castle && cell.castleOwner != -1))
                    throw new ArgumentException("Invalid castle owner.");
            }
            if (map.starts == null || map.starts.Count > map.playerCount) throw new ArgumentException("Invalid start markers.");
            var slots = new HashSet<int>();
            foreach (var start in map.starts)
                if (start == null || start.slot < 0 || start.slot >= map.playerCount || !slots.Add(start.slot)
                    || start.x < 0 || start.y < 0 || start.x >= map.width || start.y >= map.height)
                    throw new ArgumentException("Invalid or repeated participant marker.");
        }
        private static bool HasControlCharacters(string text)
        {
            foreach (char c in text) if (char.IsControl(c)) return true;
            return false;
        }
        public static List<string> PlayIssues(CustomMapData map)
        {
            var issues = new List<string>();
            try { ValidateStructure(map); }
            catch (ArgumentException exception) { issues.Add(exception.Message); return issues; }
            var occupied = new HashSet<GridPosition>();
            foreach (var cell in map.cells)
                if (cell.castle && cell.castleOwner >= map.playerCount) { issues.Add("A castle belongs to an inactive participant."); break; }
            for (int slot = 0; slot < map.playerCount; slot++)
            {
                var start = map.StartFor(slot);
                if (start == null) { issues.Add("Place the P" + (slot + 1) + " start."); continue; }
                if (!occupied.Add(new GridPosition(start.x, start.y))) issues.Add("Participants cannot share a start.");
                if (map.Cell(start.x, start.y).terrain == TerrainKind.Mountain) issues.Add("P" + (slot + 1) + " starts on a mountain.");
            }
            if (issues.Count > 0) return issues;
            var first = map.StartFor(0);
            var visited = new bool[map.width, map.height];
            var queue = new Queue<GridPosition>();
            queue.Enqueue(new GridPosition(first.x, first.y)); visited[first.x, first.y] = true;
            var offsets = new[] { new GridPosition(1, 0), new GridPosition(-1, 0), new GridPosition(0, 1), new GridPosition(0, -1) };
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var offset in offsets)
                {
                    int x = p.X + offset.X, y = p.Y + offset.Y;
                    if (x < 0 || y < 0 || x >= map.width || y >= map.height || visited[x, y]
                        || map.Cell(x, y).terrain == TerrainKind.Mountain) continue;
                    visited[x, y] = true; queue.Enqueue(new GridPosition(x, y));
                }
            }
            for (int slot = 1; slot < map.playerCount; slot++)
            {
                var start = map.StartFor(slot);
                if (!visited[start.x, start.y]) issues.Add("P" + (slot + 1) + " has no path to P1.");
            }
            return issues;
        }
        public static void ValidatePlayable(CustomMapData map)
        {
            var issues = PlayIssues(map);
            if (issues.Count > 0) throw new ArgumentException(string.Join("\n", issues));
        }
    }
}
