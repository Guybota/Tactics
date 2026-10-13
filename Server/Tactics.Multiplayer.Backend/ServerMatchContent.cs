using System.Text.Json;
using Tactics.Core;

namespace Tactics.Multiplayer.Backend;

public sealed class ServerMapDefinition
{
    public string Id { get; }
    public string Version { get; }
    internal CustomMapData Map { get; }

    public ServerMapDefinition(string id, string version, CustomMapData map)
    {
        if (!BackendValidation.Identifier(id) || !BackendValidation.Identifier(version)) throw new ArgumentException("Invalid map metadata.");
        CustomMapValidator.ValidatePlayable(map);
        if (map.playerCount != 2) throw new ArgumentException("Private matches require a two-player map.");
        Id = id; Version = version;
        Map = JsonSerializer.Deserialize<CustomMapData>(JsonSerializer.Serialize(map, MatchDocumentCodec.JsonOptions), MatchDocumentCodec.JsonOptions);
    }
}

public sealed class ServerMatchContent
{
    public const string RulesVersion = "tactics-core-v1";
    internal MatchContentData Catalogue { get; }
    internal MatchEngine Engine { get; }
    private readonly Dictionary<string, ServerMapDefinition> maps = new(StringComparer.Ordinal);
    public string ContentVersion => Catalogue.Version;

    public ServerMatchContent(MatchContentData catalogue, IEnumerable<ServerMapDefinition> maps)
    {
        if (catalogue == null || maps == null) throw new ArgumentNullException();
        Catalogue = JsonSerializer.Deserialize<MatchContentData>(JsonSerializer.Serialize(catalogue, MatchDocumentCodec.JsonOptions), MatchDocumentCodec.JsonOptions);
        Engine = new MatchEngine(Catalogue.ToContent());
        foreach (var map in maps) this.maps.Add(map.Id, new ServerMapDefinition(map.Id, map.Version, map.Map));
        if (this.maps.Count == 0) throw new ArgumentException("A server-owned map is required.");
    }

    internal ServerMapDefinition Map(string id) => id != null && maps.TryGetValue(id, out var map) ? map : null;
    internal MatchRaceData Race(string id) => Catalogue.Races.Find(r => string.Equals(r.Id, id, StringComparison.Ordinal));
}

internal static class BackendValidation
{
    public static bool Identifier(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl);
    public static bool MatchId(string value) => value != null && Guid.TryParseExact(value, "N", out var id) && value == id.ToString("N");
}
