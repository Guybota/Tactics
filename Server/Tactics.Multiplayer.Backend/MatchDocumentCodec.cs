using System.Text.Json;

namespace Tactics.Multiplayer.Backend;

public static class MatchDocumentCodec
{
    public const int MaxJsonBytes = 4 * 1024 * 1024;
    public static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true, MaxDepth = 64 };

    public static string Encode(MatchDocument document)
    {
        string json = JsonSerializer.Serialize(document, JsonOptions);
        RequireShape(json);
        return json;
    }

    public static MatchDocument Decode(string json)
    {
        try
        {
            RequireShape(json);
            return JsonSerializer.Deserialize<MatchDocument>(json, JsonOptions) ?? throw new InvalidDataException("Missing match document.");
        }
        catch (Exception error) when (error is JsonException || error is InvalidOperationException || error is ArgumentException)
        { throw new InvalidDataException("Malformed stored match.", error); }
    }

    private static void RequireShape(string json)
    {
        if (json == null || System.Text.Encoding.UTF8.GetByteCount(json) > MaxJsonBytes) throw new InvalidDataException("Stored match exceeds supported size.");
        using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var root = parsed.RootElement;
        Required(root, "SchemaVersion", "MatchId", "RulesVersion", "ContentVersion", "MapId", "MapVersion", "Revision", "BuildMode", "BattleRoyaleMode", "Players", "State", "Receipts");
        foreach (var seat in root.GetProperty("Players").EnumerateArray()) Required(seat, "PlayerId", "RaceId");
        var state = root.GetProperty("State");
        if (state.ValueKind != JsonValueKind.Null)
        {
            Required(state, "SchemaVersion", "RandomState", "Snapshot");
            if (!state.GetProperty("RandomState").TryGetUInt32(out _)) throw new InvalidDataException("Invalid saved RNG.");
            var snapshot = state.GetProperty("Snapshot");
            Required(snapshot, "SchemaVersion", "Width", "Height", "Turn", "BuildMode", "BattleRoyaleMode", "IsOver", "Winner", "StormWarmupRounds", "StormRoundsPerShrink", "Cells", "Players");
            foreach (var cell in snapshot.GetProperty("Cells").EnumerateArray()) Required(cell, "Terrain", "MovementCost", "SupportsCastle", "HasCastle", "HasStorm", "CastleOwner");
            foreach (var player in snapshot.GetProperty("Players").EnumerateArray())
            {
                Required(player, "RaceId", "Money", "NextUnitId", "HasLost", "Units");
                foreach (var unit in player.GetProperty("Units").EnumerateArray()) Required(unit, "Id", "TypeId", "X", "Y", "Health", "MaxHealth", "Attack", "Movement", "Range", "IsLeader", "HasActed", "IsDead");
            }
        }
        foreach (var receipt in root.GetProperty("Receipts").EnumerateArray())
        {
            Required(receipt, "PlayerId", "Command", "Success", "Error", "Revision");
            var command = receipt.GetProperty("Command");
            Required(command, "SchemaVersion", "MatchId", "CommandId", "ExpectedRevision", "Action");
            Required(command.GetProperty("Action"), "SchemaVersion", "Kind", "ActorPlayer", "ActorId", "DestinationX", "DestinationY", "TargetPlayer", "TargetUnitId", "TargetX", "TargetY", "RecruitTypeId");
        }
        RejectDuplicates(root);
    }

    private static void Required(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Missing required object.");
        foreach (string name in names) if (!value.TryGetProperty(name, out _)) throw new InvalidDataException("Missing stored field: " + name);
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate stored field.");
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
}
