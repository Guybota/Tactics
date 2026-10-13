using System;

namespace Tactics.Core
{
    // Wire envelope only. The transport supplies authenticated identity separately.
    [Serializable]
    public sealed class MatchCommandData
    {
        public const int CurrentVersion = 1;
        public int SchemaVersion;
        public string MatchId, CommandId;
        public int ExpectedRevision;
        public MatchActionData Action;

        public static MatchCommandData FromCommand(MatchCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var data = new MatchCommandData { SchemaVersion = CurrentVersion, MatchId = command.MatchId, CommandId = command.CommandId,
                ExpectedRevision = command.ExpectedRevision, Action = MatchActionData.FromAction(command.Action) };
            data.ToCommand();
            return data;
        }

        public MatchCommand ToCommand()
        {
            if (SchemaVersion != CurrentVersion || !MatchCommandValidation.ValidIdentifier(MatchId)
                || !MatchCommandValidation.ValidIdentifier(CommandId) || ExpectedRevision < 0 || Action == null)
                throw new ArgumentException("Invalid command wire envelope.");
            return new MatchCommand(MatchId, CommandId, ExpectedRevision, Action.ToAction());
        }
    }

    [Serializable]
    public sealed class MatchActionData
    {
        public const int CurrentVersion = 1;
        public int SchemaVersion;
        public MatchActionKind Kind;
        public int ActorPlayer, ActorId;
        public int DestinationX, DestinationY;
        public int TargetPlayer, TargetUnitId;
        public int TargetX, TargetY;
        public string RecruitTypeId;

        public static MatchActionData FromAction(MatchAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            var data = new MatchActionData { SchemaVersion = CurrentVersion, Kind = action.Kind, ActorPlayer = action.Actor.Player, ActorId = action.Actor.Id,
                DestinationX = action.Destination.X, DestinationY = action.Destination.Y,
                TargetPlayer = action.TargetUnit.Player, TargetUnitId = action.TargetUnit.Id,
                TargetX = action.Target.X, TargetY = action.Target.Y, RecruitTypeId = action.RecruitTypeId };
            data.ToAction();
            return data;
        }

        public MatchAction ToAction()
        {
            if (SchemaVersion != CurrentVersion) throw new ArgumentException("Unsupported action wire format.");
            // JsonUtility serializes a null string as empty. Normalize absent optional data
            // so a command's identity remains identical before and after transport.
            string recruitTypeId = Kind != MatchActionKind.Recruit && RecruitTypeId == string.Empty ? null : RecruitTypeId;
            var action = new MatchAction(Kind, new UnitKey(ActorPlayer, ActorId), new GridPosition(DestinationX, DestinationY),
                new UnitKey(TargetPlayer, TargetUnitId), new GridPosition(TargetX, TargetY), recruitTypeId);
            string error = MatchCommandValidation.ActionError(action);
            if (error != null) throw new ArgumentException(error);
            bool systemAction = Kind == MatchActionKind.EndTurn || Kind == MatchActionKind.Recruit;
            if ((systemAction && ActorId != 0) || (!systemAction && ActorId < 1) || ActorId == int.MaxValue
                || TargetUnitId == int.MaxValue || (Kind == MatchActionKind.Attack && TargetUnitId < 1))
                throw new ArgumentException("Invalid action unit ID.");
            return action;
        }
    }
}
