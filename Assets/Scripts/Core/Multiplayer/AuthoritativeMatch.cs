using System;
using System.Collections.Generic;

namespace Tactics.Core
{
    public sealed class MatchCommand
    {
        public string MatchId { get; }
        public string CommandId { get; }
        public int ExpectedRevision { get; }
        public MatchAction Action { get; }

        public MatchCommand(string matchId, string commandId, int expectedRevision, MatchAction action)
        {
            MatchId = matchId; CommandId = commandId; ExpectedRevision = expectedRevision; Action = action;
        }

        internal bool SamePayload(MatchCommand other)
            => string.Equals(MatchId, other.MatchId, StringComparison.Ordinal)
                && ExpectedRevision == other.ExpectedRevision && Action.Kind == other.Action.Kind
                && Action.Actor.Equals(other.Action.Actor) && Action.TargetUnit.Equals(other.Action.TargetUnit)
                && Action.Destination.Equals(other.Action.Destination) && Action.Target.Equals(other.Action.Target)
                && string.Equals(Action.RecruitTypeId, other.Action.RecruitTypeId, StringComparison.Ordinal);
    }

    // Provider-independent, in-memory proof of authority. A production backend must persist
    // the state, revision and receipts atomically; this lock only protects this process.
    // Snapshots/results are server-internal and include RNG. Strip it in the transport DTO.
    public sealed class AuthoritativeMatch
    {
        private sealed class Receipt
        {
            public string PlayerId;
            public MatchCommand Command;
            public SessionActionResult Result;
        }

        private readonly object gate = new object();
        private readonly LocalMatchSession session;
        private readonly Dictionary<string, int> playerSlots = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, Receipt> receipts = new Dictionary<string, Receipt>(StringComparer.Ordinal);
        private readonly int maxCommandReceipts;
        public string MatchId { get; }

        public AuthoritativeMatch(string matchId, MatchEngine engine, MatchState initialState,
            IReadOnlyDictionary<string, int> playerSlots, int revision = 0, int maxCommandReceipts = 1024)
        {
            if (!MatchCommandValidation.ValidIdentifier(matchId)) throw new ArgumentException("Invalid match ID.", nameof(matchId));
            if (playerSlots == null) throw new ArgumentNullException(nameof(playerSlots));
            if (initialState == null) throw new ArgumentNullException(nameof(initialState));
            if (maxCommandReceipts < 1 || maxCommandReceipts > 10000) throw new ArgumentOutOfRangeException(nameof(maxCommandReceipts));
            var assignedSlots = new HashSet<int>();
            foreach (var binding in playerSlots)
            {
                if (!MatchCommandValidation.ValidIdentifier(binding.Key) || binding.Value < 0
                    || binding.Value >= initialState.Players.Count || !assignedSlots.Add(binding.Value))
                    throw new ArgumentException("Invalid or duplicate player binding.", nameof(playerSlots));
                this.playerSlots.Add(binding.Key, binding.Value);
            }
            if (assignedSlots.Count != initialState.Players.Count)
                throw new ArgumentException("Every player slot must have an authenticated identity.", nameof(playerSlots));
            MatchId = matchId; this.maxCommandReceipts = maxCommandReceipts;
            session = new LocalMatchSession(engine, initialState, revision);
        }

        // Identity must come from the trusted authentication boundary, never the request body.
        public SessionActionResult Snapshot(string authenticatedPlayerId)
        {
            lock (gate)
                return IsParticipant(authenticatedPlayerId) ? Reply(true, null) : Unauthorized();
        }

        public SessionActionResult Submit(string authenticatedPlayerId, MatchCommand command)
        {
            lock (gate)
            {
                if (!IsParticipant(authenticatedPlayerId)) return Unauthorized();
                if (command == null || !MatchCommandValidation.ValidIdentifier(command.MatchId)
                    || !MatchCommandValidation.ValidIdentifier(command.CommandId) || command.ExpectedRevision < 0)
                    return Reply(false, "Invalid command envelope.");
                if (!string.Equals(command.MatchId, MatchId, StringComparison.Ordinal)) return Reply(false, "Wrong match ID.");
                string actionError = MatchCommandValidation.ActionError(command.Action);
                if (actionError != null) return Reply(false, actionError);
                if (receipts.TryGetValue(command.CommandId, out var receipt))
                {
                    if (!string.Equals(receipt.PlayerId, authenticatedPlayerId, StringComparison.Ordinal)
                        || !receipt.Command.SamePayload(command)) return Reply(false, "Command ID was already used for another request.");
                    return receipt.Result;
                }
                if (command.Action.Actor.Player != playerSlots[authenticatedPlayerId])
                    return Reply(false, "Authenticated player does not own the acting slot.");
                // Never evict: a forgotten ID could apply twice. Rotate/archive matches using
                // a persistent authority when this explicit prototype limit is reached.
                if (receipts.Count >= maxCommandReceipts) return Reply(false, "Command receipt capacity reached.");
                var result = session.Submit(command.Action, command.ExpectedRevision);
                receipts.Add(command.CommandId, new Receipt { PlayerId = authenticatedPlayerId, Command = command, Result = result });
                return result;
            }
        }

        private bool IsParticipant(string playerId)
            => MatchCommandValidation.ValidIdentifier(playerId) && playerSlots.ContainsKey(playerId);
        private SessionActionResult Reply(bool success, string error)
            => new SessionActionResult(success, error, session.Revision, session.Snapshot());
        private static SessionActionResult Unauthorized()
            => new SessionActionResult(false, "Player is not a participant.", -1, null);
    }
}
