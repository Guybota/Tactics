using System;

namespace Tactics.Core
{
    // The journal must durably replace the record or throw; Save returning means
    // the exact pending command can be recovered after process termination.
    public interface IOnlineSessionJournal
    {
        void Save(OnlineMatchSessionData data);
    }

    [Serializable]
    public sealed class OnlineMatchSessionData
    {
        public const int CurrentVersion = 1;
        public int SchemaVersion;
        public string MatchId;
        public int Revision;
        public MatchSnapshotData Snapshot;
        public bool HasPendingCommand;
        public MatchCommandData PendingCommand;

        public void Validate()
        {
            if (SchemaVersion != CurrentVersion || !MatchCommandValidation.ValidIdentifier(MatchId)
                || Revision < 0 || Snapshot == null) throw new ArgumentException("Invalid reconnect record.");
            var state = Snapshot.ToState();
            if (!HasPendingCommand)
            {
                if (!MatchWireNull.IsEmptyCommand(PendingCommand)) throw new ArgumentException("Unexpected pending command payload.");
                PendingCommand = null;
                return;
            }
            if (PendingCommand == null) throw new ArgumentException("Missing pending command payload.");
            var command = PendingCommand.ToCommand();
            if (!string.Equals(command.MatchId, MatchId, StringComparison.Ordinal) || command.ExpectedRevision > Revision
                || command.Action.Actor.Player >= state.Players.Count)
                throw new ArgumentException("The pending command does not belong to this reconnect record.");
        }

        public OnlineMatchSessionData Clone()
        {
            Validate();
            return new OnlineMatchSessionData { SchemaVersion = CurrentVersion, MatchId = MatchId, Revision = Revision,
                Snapshot = MatchSnapshotData.FromState(Snapshot.ToState()),
                HasPendingCommand = HasPendingCommand,
                PendingCommand = PendingCommand == null ? null : MatchCommandData.FromCommand(PendingCommand.ToCommand()) };
        }
    }

    // Unity's JSON serializer expands a null inline class into an empty object.
    // Ignore only that exact empty shape when the surrounding contract says absent.
    // A schema-zero object containing meaningful fields is malformed, never absent.
    internal static class MatchWireNull
    {
        private static bool Empty(string text) => string.IsNullOrEmpty(text);
        public static bool IsEmptyCommand(MatchCommandData command)
        {
            if (command == null) return true;
            if (command.SchemaVersion != 0 || !Empty(command.MatchId) || !Empty(command.CommandId) || command.ExpectedRevision != 0) return false;
            var a = command.Action;
            return a == null || (a.SchemaVersion == 0 && a.Kind == MatchActionKind.Wait && a.ActorPlayer == 0 && a.ActorId == 0
                && a.DestinationX == 0 && a.DestinationY == 0 && a.TargetPlayer == 0 && a.TargetUnitId == 0
                && a.TargetX == 0 && a.TargetY == 0 && Empty(a.RecruitTypeId));
        }

        public static bool IsEmptySnapshot(MatchSnapshotData snapshot)
        {
            if (snapshot == null) return true;
            return snapshot.SchemaVersion == 0 && snapshot.Width == 0 && snapshot.Height == 0 && snapshot.Turn == 0
                && !snapshot.BuildMode && !snapshot.BattleRoyaleMode && !snapshot.IsOver
                && (snapshot.Winner == 0 || snapshot.Winner == -1)
                && (snapshot.StormWarmupRounds == 0 || snapshot.StormWarmupRounds == 1)
                && (snapshot.StormRoundsPerShrink == 0 || snapshot.StormRoundsPerShrink == 1)
                && (snapshot.Cells == null || snapshot.Cells.Count == 0) && (snapshot.Players == null || snapshot.Players.Count == 0);
        }
    }
}
