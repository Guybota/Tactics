namespace Tactics.Core
{
    // Strategy weights, never bonuses to game statistics. Values are deliberately tunable.
    [System.Serializable]
    public sealed class BotProfile
    {
        public float Progress = 35;
        public float Capture = 450;
        public float Build = 260;
        public float Threat = .35f;
        public float LeaderCaution = 3;
        public float Storm = 220;
        public float Recruit = 170;
        public float ArmyPerEnemy = 1.5f;
        public static BotProfile Normal => new BotProfile();
        internal BotProfile Copy() => (BotProfile)MemberwiseClone();
    }

    public sealed class BotDecision
    {
        public MatchAction Action { get; }
        public float Score { get; }
        public string Reason { get; }
        internal BotDecision(MatchAction action, float score, string reason)
        { Action = action; Score = score; Reason = reason; }
    }
}
