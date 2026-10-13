using System.Collections.Concurrent;
using System.Text.Json;

namespace Tactics.Multiplayer.Backend;

// Local verification provider. Atomic rename plus an exclusive lock file protects
// writers across service/store instances and processes. It is not cloud hosting.
public sealed class FileMatchStore : IMatchStore
{
    private sealed class Entry
    {
        public string Json { get; set; }
        public string WriteLock { get; set; }
    }

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string directory;

    public FileMatchStore(string directory)
    {
        this.directory = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
        Directory.CreateDirectory(this.directory);
    }

    public Task<StoredMatch> ReadAsync(string matchId) => Locked(matchId, async path =>
    {
        var value = await Load(path);
        return value == null ? null : new StoredMatch(value.Json, value.WriteLock);
    });

    public Task<bool> CreateAsync(string matchId, string json) => Locked(matchId, async path =>
    {
        if (File.Exists(path)) return false;
        await Save(path, json); return true;
    });

    public Task<bool> CompareExchangeAsync(string matchId, string expectedWriteLock, string json) => Locked(matchId, async path =>
    {
        var value = await Load(path);
        if (value == null || value.WriteLock != expectedWriteLock || string.IsNullOrEmpty(expectedWriteLock)) return false;
        await Save(path, json); return true;
    });

    private async Task<T> Locked<T>(string matchId, Func<string, Task<T>> operation)
    {
        if (!BackendValidation.MatchId(matchId)) throw new ArgumentException("Invalid storage match ID.");
        string path = Path.Combine(directory, matchId + ".json");
        var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            FileStream lease = null;
            for (int attempt = 0; lease == null; attempt++)
            {
                try { lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) when (attempt < 99) { await Task.Delay(10); }
            }
            using (lease) return await operation(path);
        }
        finally { gate.Release(); }
    }

    private static async Task<Entry> Load(string path)
    {
        if (!File.Exists(path)) return null;
        var value = JsonSerializer.Deserialize<Entry>(await File.ReadAllTextAsync(path));
        if (value == null || value.Json == null || string.IsNullOrEmpty(value.WriteLock)) throw new InvalidDataException("Invalid local store envelope.");
        return value;
    }

    private static async Task Save(string path, string json)
    {
        if (json == null) throw new ArgumentNullException(nameof(json));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var value = new Entry { Json = json, WriteLock = Guid.NewGuid().ToString("N") };
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
