namespace Tactics.Core
{
    public enum MatchActionKind { Wait, Attack, Capture, BuildCastle, Recruit, EndTurn }

    // Destination is where the acting unit stops. Target is the victim/build tile.
    public sealed class MatchAction
    {
        public readonly MatchActionKind Kind;
        public readonly UnitKey Actor, TargetUnit;
        public readonly GridPosition Destination, Target;
        public readonly string RecruitTypeId;
        public MatchAction(MatchActionKind kind, UnitKey actor, GridPosition destination,
            UnitKey targetUnit = default, GridPosition target = default, string recruitTypeId = null)
        {
            Kind = kind; Actor = actor; Destination = destination; TargetUnit = targetUnit;
            Target = target; RecruitTypeId = recruitTypeId;
        }
        public static MatchAction EndTurn(int player) => new MatchAction(MatchActionKind.EndTurn, new UnitKey(player, 0), default);
        public static MatchAction Recruit(int player, GridPosition castle, string unitTypeId)
            => new MatchAction(MatchActionKind.Recruit, new UnitKey(player, 0), castle, recruitTypeId: unitTypeId);
    }

    public readonly struct ActionResult
    {
        public readonly bool Success;
        public readonly string Error;
        public ActionResult(bool success, string error = null) { Success = success; Error = error; }
    }
}
