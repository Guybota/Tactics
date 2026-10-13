using System;
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
        private readonly string matchId;
        private MatchState state;
        private int revision;
        private MatchCommand pendingCommand;
        private bool requestRunning;

        public int Revision { get { lock (gate) return revision; } }
        public bool HasPendingCommand { get { lock (gate) return pendingCommand != null; } }
        public bool IsRequestRunning { get { lock (gate) return requestRunning; } }
        public MatchState Snapshot() { lock (gate) return state.Clone(); }

        public OnlineMatchSession(IMatchTransport transport, string matchId, MatchSnapshotData initialState, int revision = 0)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            if (string.IsNullOrWhiteSpace(matchId)) throw new ArgumentException("A match ID is required.", nameof(matchId));
            if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
            this.matchId = matchId;
            state = (initialState ?? throw new ArgumentNullException(nameof(initialState))).ToState();
            this.revision = revision;
        }

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
                    ApplySnapshot(response);
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
            try
            {
                var response = await transport.SubmitAsync(command);
                lock (gate)
                {
                    ValidateReply(response);
                    if (response.Success && (response.Snapshot == null || response.Revision <= command.ExpectedRevision))
                        throw new InvalidOperationException("The server did not confirm the action revision.");
                    ApplySnapshot(response);
                    pendingCommand = null;
                    return Reply(response.Success, response.Error);
                }
            }
            catch (Exception)
            {
                // Keep the envelope until a definite answer is received; never undo a
                // potentially committed action or issue a new ID for the same attempt.
                lock (gate) return Reply(false, "The action result is unknown. Retry the pending action.");
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

        private void ApplySnapshot(MatchTransportReply response)
        {
            if (response.Snapshot == null) return;
            var confirmed = response.Snapshot.ToState();
            // Late receipts and stale poll responses cannot roll back a newer snapshot.
            if (response.Revision < revision) return;
            state = confirmed;
            revision = response.Revision;
        }

        private SessionActionResult Reply(bool success, string error)
            => new SessionActionResult(success, error, revision, state);
    }
}
