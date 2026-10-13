using System.Security.Cryptography;
using Tactics.Core;

namespace Tactics.Multiplayer.Backend;

public sealed class DurableMatchService
{
    private readonly IMatchStore store;
    private readonly ServerMatchContent content;
    private readonly int maxCasAttempts, maxReceipts;

    public DurableMatchService(IMatchStore store, ServerMatchContent content, int maxCasAttempts = 8, int maxReceipts = 4096)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.content = content ?? throw new ArgumentNullException(nameof(content));
        if (maxCasAttempts < 1 || maxCasAttempts > 32 || maxReceipts < 1 || maxReceipts > 4096) throw new ArgumentOutOfRangeException();
        this.maxCasAttempts = maxCasAttempts; this.maxReceipts = maxReceipts;
    }

    public async Task<MatchLobbyReply> CreateAsync(string authenticatedPlayerId, MatchCreateRequest request)
    {
        if (!BackendValidation.Identifier(authenticatedPlayerId) || request == null || request.SchemaVersion != MatchCreateRequest.CurrentVersion
            || content.Map(request.MapId) == null || content.Race(request.RaceId) == null) return LobbyError("Invalid match setup.");
        string mapId = request.MapId, raceId = request.RaceId;
        bool buildMode = request.BuildMode, battleRoyaleMode = request.BattleRoyaleMode;
        // Only this server generates creation keys. UUID collision resistance is also
        // required by providers without a strict atomic create-if-absent operation.
        for (int attempt = 0; attempt < maxCasAttempts; attempt++)
        {
            var map = content.Map(mapId);
            var document = new MatchDocument { SchemaVersion = MatchDocument.CurrentVersion, MatchId = Guid.NewGuid().ToString("N"),
                RulesVersion = ServerMatchContent.RulesVersion, ContentVersion = content.ContentVersion, MapId = map.Id, MapVersion = map.Version,
                BuildMode = buildMode, BattleRoyaleMode = battleRoyaleMode,
                Players = new List<MatchSeat> { new() { PlayerId = authenticatedPlayerId, RaceId = raceId } } };
            if (await store.CreateAsync(document.MatchId, MatchDocumentCodec.Encode(document))) return Lobby(document, authenticatedPlayerId);
        }
        return LobbyError("Could not allocate a match. Try again.");
    }

    public async Task<MatchLobbyReply> JoinAsync(string authenticatedPlayerId, MatchJoinRequest request)
    {
        if (!BackendValidation.Identifier(authenticatedPlayerId) || request == null || request.SchemaVersion != MatchJoinRequest.CurrentVersion
            || !BackendValidation.MatchId(request.MatchId) || content.Race(request.RaceId) == null) return LobbyError("Invalid invitation or race.");
        string matchId = request.MatchId, raceId = request.RaceId;
        for (int attempt = 0; attempt < maxCasAttempts; attempt++)
        {
            var stored = await store.ReadAsync(matchId);
            if (!TryDecode(stored, matchId, out var document)) return LobbyError("Match is unavailable.");
            var existing = document.Players.Find(p => p.PlayerId == authenticatedPlayerId);
            if (existing != null) return existing.RaceId == raceId ? Lobby(document, authenticatedPlayerId) : LobbyError("The joined race cannot be changed.");
            if (document.Players.Count != 1) return LobbyError("Match is unavailable.");
            document.Players.Add(new MatchSeat { PlayerId = authenticatedPlayerId, RaceId = raceId });
            var setup = document.Players.Select(p => new MatchPlayerSetup { RaceId = p.RaceId, LeaderTypeId = content.Race(p.RaceId).LeaderTypeId }).ToArray();
            uint seed = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(sizeof(uint)));
            var initial = MatchInitializer.Create(content.Map(document.MapId).Map, content.Catalogue, setup, seed,
                new MatchOptions { BuildMode = document.BuildMode, BattleRoyaleMode = document.BattleRoyaleMode });
            document.State = MatchStateData.FromState(initial); document.Revision = 1;
            if (await store.CompareExchangeAsync(document.MatchId, stored.WriteLock, MatchDocumentCodec.Encode(document)))
                return Lobby(document, authenticatedPlayerId);
        }
        return LobbyError("Match changed concurrently. Retry the invitation.");
    }

    public async Task<MatchLobbyReply> GetLobbyAsync(string authenticatedPlayerId, string matchId)
    {
        if (!BackendValidation.Identifier(authenticatedPlayerId) || !BackendValidation.MatchId(matchId)) return LobbyError("Match is unavailable.");
        var stored = await store.ReadAsync(matchId);
        return TryDecode(stored, matchId, out var document) && Slot(document, authenticatedPlayerId) >= 0
            ? Lobby(document, authenticatedPlayerId) : LobbyError("Match is unavailable.");
    }

    public async Task<MatchTransportReply> ReadAsync(string authenticatedPlayerId, string matchId)
    {
        if (!BackendValidation.Identifier(authenticatedPlayerId) || !BackendValidation.MatchId(matchId)) return Error(matchId, "Match is unavailable.");
        var stored = await store.ReadAsync(matchId);
        if (!TryDecode(stored, matchId, out var document) || Slot(document, authenticatedPlayerId) < 0) return Error(matchId, "Match is unavailable.");
        return document.State == null ? Error(matchId, "The match is waiting for another player.") : Reply(document, true, null);
    }

    public async Task<MatchTransportReply> SubmitAsync(string authenticatedPlayerId, MatchCommandData data)
    {
        MatchCommand command;
        try { command = data?.ToCommand(); }
        catch (ArgumentException) { return Error(data?.MatchId, "Invalid command."); }
        if (command == null || !BackendValidation.Identifier(authenticatedPlayerId) || !BackendValidation.MatchId(command.MatchId))
            return Error(data?.MatchId, "Invalid command.");
        // Copy wire fields: callers cannot change a DTO while an awaited CAS is pending.
        var captured = MatchCommandData.FromCommand(command);
        for (int attempt = 0; attempt < maxCasAttempts; attempt++)
        {
            var stored = await store.ReadAsync(command.MatchId);
            if (!TryDecode(stored, command.MatchId, out var document)) return Error(command.MatchId, "Match is unavailable.");
            int slot = Slot(document, authenticatedPlayerId);
            if (slot < 0) return Error(command.MatchId, "Match is unavailable.");
            if (document.State == null) return Error(command.MatchId, "The match is waiting for another player.");
            var prior = document.Receipts.Find(r => r.Command.CommandId == command.CommandId);
            if (prior != null)
                return prior.PlayerId == authenticatedPlayerId && SameCommand(prior.Command, captured)
                    ? Reply(document, prior.Success, prior.Error) : Reply(document, false, "Command ID was already used for another request.");
            if (command.Action.Actor.Player != slot) return Reply(document, false, "Authenticated player does not own the acting slot.");
            if (document.Receipts.Count >= maxReceipts) return Reply(document, false, "Command receipt capacity reached.");
            var session = new LocalMatchSession(content.Engine, document.State.ToState(), document.Revision);
            var result = session.Submit(command.Action, command.ExpectedRevision);
            document.Revision = result.Revision;
            if (result.Success) document.State = MatchStateData.FromState(result.State);
            document.Receipts.Add(new MatchReceipt { PlayerId = authenticatedPlayerId, Command = captured,
                Success = result.Success, Error = result.Error, Revision = result.Revision });
            string json;
            try { json = MatchDocumentCodec.Encode(document); }
            catch (InvalidDataException) { return Error(command.MatchId, "Match storage capacity reached."); }
            if (await store.CompareExchangeAsync(command.MatchId, stored.WriteLock, json)) return Reply(document, result.Success, result.Error);
        }
        // Every failed CAS was a definite non-write. An ambiguous store exception is
        // deliberately propagated so transports preserve the pending command for retry.
        return Error(command.MatchId, "Match changed concurrently. Retry the command.");
    }

    private bool TryDecode(StoredMatch stored, string expectedId, out MatchDocument document)
    {
        document = null;
        if (stored == null || string.IsNullOrEmpty(stored.WriteLock)) return false;
        try
        {
            var value = MatchDocumentCodec.Decode(stored.Json);
            Validate(value, expectedId); document = value; return true;
        }
        catch (Exception error) when (error is InvalidDataException || error is ArgumentException || error is InvalidOperationException || error is OverflowException)
        { return false; }
    }

    private void Validate(MatchDocument document, string expectedId)
    {
        var map = content.Map(document.MapId);
        if (document.SchemaVersion != MatchDocument.CurrentVersion || document.MatchId != expectedId || !BackendValidation.MatchId(document.MatchId)
            || document.RulesVersion != ServerMatchContent.RulesVersion || document.ContentVersion != content.ContentVersion
            || map == null || map.Version != document.MapVersion || document.Revision < 0 || document.Players == null
            || document.Players.Count < 1 || document.Players.Count > 2 || document.Receipts == null || document.Receipts.Count > maxReceipts)
            throw new InvalidDataException("Invalid match metadata.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in document.Players)
            if (player == null || !BackendValidation.Identifier(player.PlayerId) || !ids.Add(player.PlayerId) || content.Race(player.RaceId) == null)
                throw new InvalidDataException("Invalid player binding.");
        if (document.State == null)
        {
            if (document.Players.Count != 1 || document.Revision != 0 || document.Receipts.Count != 0) throw new InvalidDataException("Invalid waiting match.");
            return;
        }
        var state = document.State.ToState();
        if (document.Players.Count != 2 || document.Revision < 1 || state.Players.Count != 2 || state.Width != map.Map.width || state.Height != map.Map.height
            || state.BuildMode != document.BuildMode || state.BattleRoyaleMode != document.BattleRoyaleMode
            || state.StormWarmupRounds != 1 || state.StormRoundsPerShrink != 1) throw new InvalidDataException("Invalid active match.");
        var commandIds = new HashSet<string>(StringComparer.Ordinal);
        var acceptedRevisions = new HashSet<int>();
        foreach (var receipt in document.Receipts)
        {
            if (receipt == null || Slot(document, receipt.PlayerId) < 0 || receipt.Command == null) throw new InvalidDataException("Invalid receipt identity.");
            var command = receipt.Command.ToCommand();
            if (command.MatchId != document.MatchId || !commandIds.Add(command.CommandId) || command.Action.Actor.Player != Slot(document, receipt.PlayerId)
                || receipt.Revision < 1 || receipt.Revision > document.Revision || (receipt.Success && (command.ExpectedRevision != receipt.Revision - 1
                    || !acceptedRevisions.Add(receipt.Revision) || receipt.Error != null)) || (!receipt.Success && string.IsNullOrWhiteSpace(receipt.Error)))
                throw new InvalidDataException("Invalid command receipt.");
        }
        if (document.Revision != 1 + acceptedRevisions.Count) throw new InvalidDataException("Invalid action revision history.");
        for (int y = 0; y < state.Height; y++) for (int x = 0; x < state.Width; x++)
        {
            var source = map.Map.Cell(x, y);
            var cell = state.Cells[x, y];
            int movementCost = source.terrain == TerrainKind.Grass ? 1 : source.terrain == TerrainKind.Forest ? 2 : 100000;
            if (cell.Terrain != source.terrain || cell.MovementCost != movementCost || cell.SupportsCastle != (source.terrain == TerrainKind.Grass))
                throw new InvalidDataException("Board differs from pinned map.");
        }
        for (int slot = 0; slot < state.Players.Count; slot++)
        {
            var player = state.Players[slot];
            if (player.RaceId != document.Players[slot].RaceId) throw new InvalidDataException("Player race changed.");
            foreach (var unit in player.Units)
            {
                if (!content.Engine.Content.TryUnit(unit.TypeId, out var type)) throw new InvalidDataException("Unknown unit type.");
                if ((unit.IsLeader && unit.TypeId != content.Race(player.RaceId).LeaderTypeId)
                    || (!unit.IsLeader && !content.Engine.Content.CanRecruit(player.RaceId, unit.TypeId))
                    || unit.MaxHealth != (unit.IsLeader ? LeaderStats.Health(type.Health) : type.Health)
                    || unit.Attack != (unit.IsLeader ? LeaderStats.Attack(type.Attack) : type.Attack)
                    || unit.Movement != type.Movement || unit.Range != type.Range) throw new InvalidDataException("Unit differs from pinned content.");
            }
        }
    }

    private static int Slot(MatchDocument document, string identity) => document.Players.FindIndex(p => p.PlayerId == identity);
    private static bool SameCommand(MatchCommandData left, MatchCommandData right)
        => System.Text.Json.JsonSerializer.Serialize(left, MatchDocumentCodec.JsonOptions) == System.Text.Json.JsonSerializer.Serialize(right, MatchDocumentCodec.JsonOptions);
    private static MatchTransportReply Reply(MatchDocument document, bool success, string error)
        => new() { MatchId = document.MatchId, Success = success, Error = error, Revision = document.Revision,
            Snapshot = document.State == null ? null : MatchSnapshotData.FromState(document.State.Snapshot.ToState()) };
    private static MatchTransportReply Error(string matchId, string error) => new() { MatchId = matchId, Error = error, Revision = -1 };
    private static MatchLobbyReply LobbyError(string error)
        => new() { SchemaVersion = MatchLobbyReply.CurrentVersion, Error = error, PlayerSlot = -1, Revision = -1 };
    private static MatchLobbyReply Lobby(MatchDocument document, string identity)
        => new() { SchemaVersion = MatchLobbyReply.CurrentVersion, Success = true, MatchId = document.MatchId,
            MapId = document.MapId, ContentVersion = document.ContentVersion, PlayerSlot = Slot(document, identity), Revision = document.Revision,
            Status = document.State == null ? MatchLobbyStatus.Waiting : document.State.Snapshot.IsOver ? MatchLobbyStatus.Completed : MatchLobbyStatus.Active,
            Snapshot = document.State == null ? null : MatchSnapshotData.FromState(document.State.Snapshot.ToState()) };
}
