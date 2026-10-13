using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tactics.Core
{
    // The transport owns authentication. Player identities are never supplied by actions.
    public interface IMatchTransport
    {
        Task<MatchTransportReply> ReadAsync(string matchId);
        Task<MatchTransportReply> SubmitAsync(MatchCommand command);
    }

    [Serializable]
    public sealed class MatchTransportReply
    {
        public string MatchId;
        public bool Success;
        public string Error;
        public int Revision;
        public MatchSnapshotData Snapshot;
    }

    // Only confirmed public snapshots enter the client. No Unity objects or server RNG.
    public sealed class OnlineMatchSession : IMatchSession
    {
        private readonly object gate = new object();
        private readonly IMatchTransport transport;
        private IOnlineSessionJournal journal;
        private readonly string matchId;
        private MatchState state;
        private int revision;
        private MatchCommand pendingCommand;
        private bool requestRunning;

        public int Revision { get { lock (gate) return revision; } }
        public bool HasPendingCommand { get { lock (gate) return pendingCommand != null; } }
        public bool IsRequestRunning { get { lock (gate) return requestRunning; } }
        public MatchState Snapshot() { lock (gate) return state.Clone(); }

        public OnlineMatchSession(IMatchTransport transport, string matchId, MatchSnapshotData initialState, int revision = 0,
            IOnlineSessionJournal journal = null)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            if (!MatchCommandValidation.ValidIdentifier(matchId)) throw new ArgumentException("A valid match ID is required.", nameof(matchId));
            if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
            this.matchId = matchId;
            state = (initialState ?? throw new ArgumentNullException(nameof(initialState))).ToState();
            this.revision = revision;
            this.journal = journal;
            journal?.Save(CaptureReconnectData());
        }

        public static OnlineMatchSession Restore(IMatchTransport transport, OnlineMatchSessionData data,
            IOnlineSessionJournal journal = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var copy = data.Clone();
            // Do not save a pending-free record during construction: it would erase
            // the durable uncertain command if termination occurs before restore ends.
            var session = new OnlineMatchSession(transport, copy.MatchId, copy.Snapshot, copy.Revision);
            session.pendingCommand = copy.PendingCommand?.ToCommand();
            session.journal = journal;
            journal?.Save(session.CaptureReconnectData());
            return session;
        }

        public OnlineMatchSessionData CaptureReconnectData()
        {
            lock (gate) return ReconnectData(state, revision, pendingCommand);
        }

        private OnlineMatchSessionData ReconnectData(MatchState current, int currentRevision, MatchCommand pending)
            => new OnlineMatchSessionData { SchemaVersion = OnlineMatchSessionData.CurrentVersion, MatchId = matchId,
                Revision = currentRevision, Snapshot = MatchSnapshotData.FromState(current),
                HasPendingCommand = pending != null,
                PendingCommand = pending == null ? null : MatchCommandData.FromCommand(pending) };

        public Task<SessionActionResult> SubmitAsync(MatchAction action, int expectedRevision)
        {
            MatchCommand command;
            lock (gate)
            {
                if (requestRunning || pendingCommand != null)
                    return Task.FromResult(Reply(false, "Resolve the pending action before submitting another."));
                if (action == null) return Task.FromResult(Reply(false, "An action is required."));
                if (expectedRevision != revision) return Task.FromResult(Reply(false, "The match revision is stale."));
                command = new MatchCommand(matchId, Guid.NewGuid().ToString("N"), expectedRevision, action);
                try { MatchCommandData.FromCommand(command); }
                catch (ArgumentException) { return Task.FromResult(Reply(false, "Invalid action payload.")); }
                pendingCommand = command;
                requestRunning = true;
            }
            return SendAsync(command);
        }

        // A timeout might occur after the server commits. Reuse the original command ID.
        public Task<SessionActionResult> RetryPendingAsync()
        {
            MatchCommand command;
            lock (gate)
            {
                if (requestRunning) return Task.FromResult(Reply(false, "A request is already in progress."));
                if (pendingCommand == null) return Task.FromResult(Reply(false, "There is no pending action."));
                command = pendingCommand;
                requestRunning = true;
            }
            return SendAsync(command);
        }

        public async Task<SessionActionResult> RefreshAsync()
        {
            lock (gate)
            {
                if (requestRunning) return Reply(false, "A request is already in progress.");
                requestRunning = true;
            }
            try
            {
                var response = await transport.ReadAsync(matchId);
                lock (gate)
                {
                    ValidateReply(response);
                    if (response.Success && response.Snapshot == null)
                        throw new InvalidOperationException("The server did not return a confirmed snapshot.");
                    CommitReply(response, false);
                    return Reply(response.Success, response.Error);
                }
            }
            catch (Exception)
            {
                lock (gate) return Reply(false, "Could not refresh the match. Try again.");
            }
            finally { lock (gate) requestRunning = false; }
        }

        private async Task<SessionActionResult> SendAsync(MatchCommand command)
        {
            bool networkAttempted = false;
            try
            {
                lock (gate) journal?.Save(CaptureReconnectData());
                networkAttempted = true;
                var response = await transport.SubmitAsync(command);
                lock (gate)
                {
                    ValidateReply(response);
                    if (response.Success && (response.Snapshot == null || response.Revision <= command.ExpectedRevision))
                        throw new InvalidOperationException("The server did not confirm the action revision.");
                    CommitReply(response, true);
                    return Reply(response.Success, response.Error);
                }
            }
            catch (Exception)
            {
                // Keep the envelope until a definite answer is received; never undo a
                // potentially committed action or issue a new ID for the same attempt.
                lock (gate) return Reply(false, networkAttempted
                    ? "The action result is unknown. Retry the pending action."
                    : "Could not save the pending action. Retry after storage is available.");
            }
            finally { lock (gate) requestRunning = false; }
        }

        private void ValidateReply(MatchTransportReply response)
        {
            if (response == null || !string.Equals(response.MatchId, matchId, StringComparison.Ordinal))
                throw new InvalidOperationException("The response belongs to a different match.");
            if (response.Snapshot != null && response.Revision < 0)
                throw new InvalidOperationException("Invalid match revision.");
        }

        private void CommitReply(MatchTransportReply response, bool clearPending)
        {
            var confirmed = response.Snapshot?.ToState();
            if (confirmed != null) ValidateMatchIdentity(confirmed);
            // Late receipts and stale poll responses cannot roll back a newer snapshot.
            bool newerSnapshot = confirmed != null && response.Revision >= revision;
            var nextState = newerSnapshot ? confirmed : state;
            int nextRevision = newerSnapshot ? response.Revision : revision;
            var nextPending = clearPending ? null : pendingCommand;
            // Save the complete replacement first. Failure preserves the old confirmed
            // state and exact pending ID, allowing a retry even after server commit.
            journal?.Save(ReconnectData(nextState, nextRevision, nextPending));
            state = nextState;
            revision = nextRevision;
            pendingCommand = nextPending;
        }

        private void ValidateMatchIdentity(MatchState confirmed)
        {
            if (confirmed.Width != state.Width || confirmed.Height != state.Height || confirmed.Players.Count != state.Players.Count
                || confirmed.BuildMode != state.BuildMode || confirmed.BattleRoyaleMode != state.BattleRoyaleMode
                || confirmed.StormWarmupRounds != state.StormWarmupRounds || confirmed.StormRoundsPerShrink != state.StormRoundsPerShrink)
                throw new InvalidOperationException("The reply changed the match's map, participants or rules.");
            for (int x = 0; x < state.Width; x++) for (int y = 0; y < state.Height; y++)
            {
                var before = state.Cells[x, y]; var after = confirmed.Cells[x, y];
                if (before.Terrain != after.Terrain || before.MovementCost != after.MovementCost || before.SupportsCastle != after.SupportsCastle)
                    throw new InvalidOperationException("The reply changed immutable terrain.");
            }
            for (int slot = 0; slot < state.Players.Count; slot++)
            {
                if (!string.Equals(state.Players[slot].RaceId, confirmed.Players[slot].RaceId, StringComparison.Ordinal))
                    throw new InvalidOperationException("The reply changed a participant's race.");
                var units = new Dictionary<int, UnitState>();
                foreach (var unit in confirmed.Players[slot].Units) units.Add(unit.Id, unit);
                foreach (var unit in state.Players[slot].Units)
                {
                    if (units.TryGetValue(unit.Id, out var after)
                        && (!string.Equals(unit.TypeId, after.TypeId, StringComparison.Ordinal) || unit.IsLeader != after.IsLeader))
                        throw new InvalidOperationException("The reply changed an existing unit's identity.");
                }
            }
        }

        private SessionActionResult Reply(bool success, string error)
            => new SessionActionResult(success, error, revision, state);
    }
}
