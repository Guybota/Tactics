using System;

namespace Tactics.Core
{
    public enum MatchLobbyStatus { Waiting, Active, Completed }

    [Serializable]
    public sealed class MatchCreateRequest
    {
        public const int CurrentVersion = 1;
        public int SchemaVersion;
        public string MapId, RaceId;
        public bool BuildMode, BattleRoyaleMode;
        public void Validate()
        {
            if (SchemaVersion != CurrentVersion || !MatchCommandValidation.ValidIdentifier(MapId)
                || !MatchCommandValidation.ValidIdentifier(RaceId)) throw new ArgumentException("Invalid match creation request.");
        }
    }

    [Serializable]
    public sealed class MatchJoinRequest
    {
        public const int CurrentVersion = 1;
        public int SchemaVersion;
        public string MatchId, RaceId;
        public void Validate()
        {
            if (SchemaVersion != CurrentVersion || !MatchCommandValidation.ValidIdentifier(MatchId)
                || !MatchCommandValidation.ValidIdentifier(RaceId)) throw new ArgumentException("Invalid match join request.");
        }
    }

    [Serializable]
    public sealed class MatchLobbyReply
    {
        public const int CurrentVersion = 1;
        public int SchemaVersion;
        public string MatchId, MapId, ContentVersion;
        public int PlayerSlot = -1, Revision = -1;
        public MatchLobbyStatus Status;
        public MatchSnapshotData Snapshot;
        public string Error;
        public bool Success;

        public void Validate()
        {
            if (SchemaVersion != CurrentVersion || !Enum.IsDefined(typeof(MatchLobbyStatus), Status))
                throw new ArgumentException("Invalid lobby reply format.");
            if (!Success)
            {
                if (!MatchWireNull.IsEmptySnapshot(Snapshot)) throw new ArgumentException("A failed lobby reply cannot contain match state.");
                Snapshot = null;
                return;
            }
            if (!MatchCommandValidation.ValidIdentifier(MatchId) || !MatchCommandValidation.ValidIdentifier(MapId)
                || !MatchCommandValidation.ValidIdentifier(ContentVersion) || PlayerSlot < 0 || PlayerSlot > 3 || Revision < 0)
                throw new ArgumentException("Invalid lobby reply.");
            if (Status == MatchLobbyStatus.Waiting)
            {
                if (!MatchWireNull.IsEmptySnapshot(Snapshot)) throw new ArgumentException("A waiting lobby has no active match state.");
                Snapshot = null;
                return;
            }
            if (Snapshot == null) throw new ArgumentException("An active lobby requires match state.");
            Snapshot.Validate();
            if (PlayerSlot >= Snapshot.Players.Count || Snapshot.IsOver != (Status == MatchLobbyStatus.Completed))
                throw new ArgumentException("Lobby status does not match its snapshot.");
        }
    }
}
