using System;
using System.Threading.Tasks;

namespace Tactics.Core
{
    public interface IMatchSession
    {
        int Revision { get; }
        MatchState Snapshot();
        Task<SessionActionResult> SubmitAsync(MatchAction action, int expectedRevision);
    }

    // State is copied both when captured and when read by the caller.
    public sealed class SessionActionResult
    {
        private readonly MatchState state;
        public bool Success { get; }
        public string Error { get; }
        public int Revision { get; }
        public MatchState State => state?.Clone();

        public SessionActionResult(bool success, string error, int revision, MatchState state)
        {
            Success = success; Error = error; Revision = revision; this.state = state?.Clone();
        }
    }

    public sealed class LocalMatchSession : IMatchSession
    {
        private readonly object gate = new object();
        private readonly MatchEngine engine;
        private MatchState state;
        private int revision;
        public int Revision { get { lock (gate) return revision; } }

        public LocalMatchSession(MatchEngine engine, MatchState initialState, int revision = 0)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            Restore(initialState, revision);
        }

        public MatchState Snapshot() { lock (gate) return state.Clone(); }

        // Trusted local save/scene bridge. Never expose this operation to a remote client.
        public void Restore(MatchState restoredState, int restoredRevision)
        {
            if (restoredState == null) throw new ArgumentNullException(nameof(restoredState));
            if (restoredRevision < 0) throw new ArgumentOutOfRangeException(nameof(restoredRevision));
            if (restoredState.Players.Count < 2 || restoredState.Players.Count > 4 || restoredState.Turn < 0)
                throw new ArgumentException("Invalid match state.", nameof(restoredState));
            lock (gate) { state = restoredState.Clone(); revision = restoredRevision; }
        }

        public SessionActionResult Submit(MatchAction action, int expectedRevision)
        {
            lock (gate)
            {
                if (expectedRevision != revision) return Reply(false, "The match revision is stale.");
                if (revision == int.MaxValue) return Reply(false, "No match revisions remain.");
                string error = MatchCommandValidation.ActionError(action);
                if (error != null) return Reply(false, error);
                ActionResult result;
                MatchState next;
                try { result = engine.Execute(state, action, out next); }
                catch (OverflowException)
                {
                    // Execution works on a clone. Arithmetic limits are a definitive
                    // rejection, so a remote client can safely resolve its command ID.
                    return Reply(false, "The action exceeds supported match arithmetic limits.");
                }
                if (!result.Success) return Reply(false, result.Error);
                state = next; revision++;
                return Reply(true, null);
            }
        }

        public Task<SessionActionResult> SubmitAsync(MatchAction action, int expectedRevision)
            => Task.FromResult(Submit(action, expectedRevision));

        private SessionActionResult Reply(bool success, string error)
            => new SessionActionResult(success, error, revision, state);
    }

    internal static class MatchCommandValidation
    {
        public const int MaxIdentifierLength = 128;

        public static bool ValidIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxIdentifierLength) return false;
            foreach (char c in value) if (char.IsControl(c)) return false;
            return true;
        }

        public static string ActionError(MatchAction action)
        {
            if (action == null || action.Kind < MatchActionKind.Wait || action.Kind > MatchActionKind.EndTurn)
                return "Invalid action kind.";
            if (action.Actor.Player < 0 || action.Actor.Player > 3 || action.Actor.Id < 0
                || action.TargetUnit.Player < 0 || action.TargetUnit.Player > 3 || action.TargetUnit.Id < 0)
                return "Invalid unit identity.";
            if (!ValidPosition(action.Destination) || !ValidPosition(action.Target)) return "Invalid action coordinates.";
            if (action.RecruitTypeId != null && !ValidIdentifier(action.RecruitTypeId)) return "Invalid recruit type.";
            if (action.Kind == MatchActionKind.Recruit && action.RecruitTypeId == null) return "Missing recruit type.";
            return null;
        }

        private static bool ValidPosition(GridPosition position)
            => position.X >= 0 && position.Y >= 0 && position.X < 50 && position.Y < 50;
    }
}
