using System.Reflection;
using System.Text.Json;
using Tactics.Core;
using Tactics.Multiplayer.Backend;

namespace Tactics.Multiplayer.CloudCode;

public static class EmbeddedMatchContent
{
    private static readonly Lazy<ServerMatchContent> content = new(Create);
    public static ServerMatchContent Value => content.Value;

    private static ServerMatchContent Create()
    {
        var catalogue = Read<MatchContentData>("Tactics.Multiplayer.Content.catalogue.json");
        var map = Read<CustomMapData>("Tactics.Multiplayer.Content.default-map.json");
        return new ServerMatchContent(catalogue, new[] { new ServerMapDefinition("default-1v1", "1", map) });
    }

    private static T Read<T>(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidDataException("Missing embedded server content.");
        return JsonSerializer.Deserialize<T>(stream, MatchDocumentCodec.JsonOptions)
            ?? throw new InvalidDataException("Invalid embedded server content.");
    }
}
