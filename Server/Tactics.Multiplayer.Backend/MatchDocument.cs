using Tactics.Core;

namespace Tactics.Multiplayer.Backend;

public sealed class MatchDocument
{
    public const int CurrentVersion = 1;
    public int SchemaVersion;
    public string MatchId, RulesVersion, ContentVersion, MapId, MapVersion;
    public int Revision;
    public bool BuildMode, BattleRoyaleMode;
    public List<MatchSeat> Players = new();
    public MatchStateData State;
    public List<MatchReceipt> Receipts = new();
}

public sealed class MatchSeat
{
    public string PlayerId, RaceId;
}

// Compact transaction receipt: never store a historical board per command.
public sealed class MatchReceipt
{
    public string PlayerId;
    public MatchCommandData Command;
    public bool Success;
    public string Error;
    public int Revision;
}
